using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public sealed class ActivityPlan
    {
        public readonly List<string> errors = new List<string>();
        public readonly List<string> warnings = new List<string>();
        public ActivityRecord record;
        public bool Valid => errors.Count == 0 && record != null;
    }

    public static class EventPlacementCompiler
    {
        public static bool Resolve(ActivityAnchor anchor, out Pose pose, out string failure)
        {
            pose = default; failure = "Missing anchor.";
            if (anchor == null) return false;
            switch (anchor.kind)
            {
                case ActivityAnchorKind.World:
                    pose = new Pose(anchor.worldPosition, Quaternion.Euler(anchor.worldEuler)); break;
                case ActivityAnchorKind.Lane:
                    if (anchor.network == null || anchor.network.SchemaVersion != RoadNetworkAsset.CurrentSchema)
                    { failure = "Missing or unsupported road publication."; return false; }
                    if (anchor.sourceRevision != anchor.network.Fingerprint)
                    { failure = "Road revision changed. Review and explicitly accept the new revision."; return false; }
                    var lane = anchor.network.Lanes.FirstOrDefault(l => l.Id.ToString() == anchor.laneId);
                    if (lane == null) { failure = "Lane binding is unresolved; no nearest-lane remap was made."; return false; }
                    if (!float.IsFinite(anchor.station) || anchor.station < 0 || anchor.station > lane.Length)
                    { failure = "Station lies outside the bound lane."; return false; }
                    var sample = lane.Sample(anchor.station);
                    pose = new Pose(sample.position, Quaternion.LookRotation(sample.forward, sample.up)); break;
                case ActivityAnchorKind.CityEntrance:
                    if (anchor.city == null || anchor.city.SchemaVersion != 1 || anchor.sourceRevision != anchor.city.Fingerprint)
                    { failure = "City publication is missing or its revision changed."; return false; }
                    if (!anchor.city.TryResolve(anchor.entranceId, out var entrance) || !entrance.accessValidated)
                    { failure = "Entrance was removed or has no validated road access."; return false; }
                    pose = new Pose(anchor.city.Origin + entrance.localPosition, Quaternion.Euler(anchor.worldEuler)); break;
                case ActivityAnchorKind.Socket:
                    if (anchor.socket == null || anchor.socket.id != anchor.socketId || anchor.socket.revision != anchor.sourceRevision)
                    { failure = "Socket identity or revision is unresolved."; return false; }
                    pose = new Pose(anchor.socket.transform.position, anchor.socket.transform.rotation); break;
                default: failure = "Unknown anchor schema."; return false;
            }
            if (!Finite(pose.position) || !Finite(pose.rotation.eulerAngles)) { failure = "Anchor is non-finite."; return false; }
            failure = string.Empty; return true;
        }

        public static ActivityPlan Build(EventPlacementSource source, bool checkGeometry = true)
        {
            var plan = new ActivityPlan();
            if (source == null) { plan.errors.Add("Select a placement."); return plan; }
            if (source.schema != 1 || string.IsNullOrWhiteSpace(source.id)) plan.errors.Add("Invalid placement schema or identity.");
            var definition = source.definition;
            if (definition == null) { plan.errors.Add("Choose a definition from the palette."); return plan; }
            if (definition.schema != 1 || string.IsNullOrWhiteSpace(definition.id)) plan.errors.Add("Invalid definition schema or identity.");
            if (!Resolve(source.anchor, out var pose, out string failure)) plan.errors.Add("Anchor: " + failure);
            if (source.access.kind != ActivityAnchorKind.Lane || !Resolve(source.access, out _, out _))
                plan.errors.Add("Access must resolve to an explicit current lane station.");
            Resolve(source.access, out var access, out _);
            try { CareerRequirement.Compile(definition.availability); } catch (Exception e) { plan.errors.Add("Availability: " + e.Message); }
            if (!Positive(source.triggerSize) || !Positive(source.vehicleSize) || !Finite(source.interactionOffset) || !Finite(source.stagingOffset) || !Finite(source.iconOffset) || !Finite(source.cinematicOffset))
                plan.errors.Add("Offsets and positive dimensions must be finite.");
            if (!float.IsFinite(source.maximumSpeed) || source.maximumSpeed < 0 || source.maximumSpeed >= 3 || !float.IsFinite(source.dwellSeconds) || source.dwellSeconds < 0 || !float.IsFinite(source.approachAngle) || source.approachAngle < 0 || source.approachAngle > 180 || !float.IsFinite(source.maximumGrade) || source.maximumGrade < 0 || source.maximumGrade > 45 || !float.IsFinite(source.maximumAccessLength) || source.maximumAccessLength < 0)
                plan.errors.Add("Interaction or access policy is out of range.");
            if ((definition.adapter == "service" && source.triggerSize.magnitude / 2 > 12) || (definition.adapter == "race" && source.triggerSize.magnitude / 2 > 15))
                plan.errors.Add("Trigger extends outside the existing runtime owner's interaction radius.");
            foreach (string guid in AssetDatabase.FindAssets("t:WorldActivityDefinition"))
            {
                var otherDefinition = AssetDatabase.LoadAssetAtPath<WorldActivityDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (otherDefinition != definition && otherDefinition != null && otherDefinition.id == definition.id) plan.errors.Add("Definition ID is duplicated in " + AssetDatabase.GetAssetPath(otherDefinition));
            }
            var r = new ActivityRecord
            {
                id = source.id, definitionId = definition.id, label = definition.displayName, adapter = definition.adapter,
                completionScope = definition.completionScope, uniqueDefinition = definition.uniqueDefinition,
                hideWhenLocked = definition.hideWhenLocked, color = definition.markerColor,
                interaction = pose.position + pose.rotation * source.interactionOffset,
                staging = pose.position + pose.rotation * source.stagingOffset,
                icon = pose.position + pose.rotation * source.iconOffset,
                cinematic = pose.position + pose.rotation * source.cinematicOffset,
                accessLane = source.access.network != null ? source.access.network.Lanes.FirstOrDefault(l => l.Id.ToString() == source.access.laneId)?.Id ?? default : default,
                accessNetworkId = source.access.network != null ? source.access.network.NetworkId.ToString() : "",
                accessRoadRevision = source.access.sourceRevision, accessDistance = source.access.station,
                access = access.position, rotation = pose.rotation, triggerSize = source.triggerSize, vehicleSize = source.vehicleSize,
                approachAngle = source.approachAngle, maximumSpeed = source.maximumSpeed, dwellSeconds = source.dwellSeconds,
                district = source.district, level = source.level, cell = source.streamingCell,
                availability = definition.availability, mission = definition.mission, serviceKind = definition.serviceKind, storefront = definition.storefront,
                exits = (source.serviceExitOffsets ?? Array.Empty<Vector3>()).Select(p => pose.position + pose.rotation * p).ToArray()
            };
            plan.record = r;
            switch (definition.adapter)
            {
                case "race":
                    if (definition.race == null || definition.race.published == null) plan.errors.Add("Race requires a published route.");
                    else
                    {
                        var race = definition.race;
                        r.route = race.published; r.raceKind = race.policy; r.laps = race.laps; r.timeLimit = race.timeLimit; r.targetSpeedKph = race.targetSpeedKph;
                        if (r.route.Fingerprint != RaceRouteCompiler.Fingerprint(race)) plan.errors.Add("Race source differs from its publication; publish the route first.");
                        if (r.route.Network != source.access.network) plan.errors.Add("Race and entrance must use the same road publication.");
                        if (r.route.GridCount == 0) plan.errors.Add("Race has no published staging grid.");
                        else r.staging = r.route.GridPosition(0);
                        if (r.route.EntrantDimensions.x > source.vehicleSize.x || r.route.EntrantDimensions.y > source.vehicleSize.y || r.route.EntrantDimensions.z > source.vehicleSize.z) plan.errors.Add("Access envelope is smaller than the race entrant.");
                    }
                    break;
                case "service":
                    if (r.exits.Length == 0 || r.exits.Any(p => !Finite(p))) plan.errors.Add("Services need finite ordered exit candidates.");
                    break;
                case "mission":
                    if (definition.completionScope != ActivityCompletionScope.Definition) plan.errors.Add("MissionHost owns definition-scoped completion. Select Definition completion scope.");
                    if (definition.mission == null) plan.errors.Add("Bind a Mission Framework definition.");
                    else try { r.missionFingerprint = definition.mission.Compile().ContentHash; } catch (Exception e) { plan.errors.Add("Mission: " + e.Message); }
                    plan.warnings.Add("Runtime requires a MissionHost containing this definition and its career facts provider.");
                    break;
                default: plan.errors.Add("Missing runtime adapter: " + definition.adapter + ". Definition remains editable; publication is disabled."); break;
            }
            if (definition.uniqueDefinition && definition.completionScope != ActivityCompletionScope.Definition)
                plan.errors.Add("Unique definitions require definition-scoped completion identity.");
            foreach (var other in UnityEngine.Object.FindObjectsByType<EventPlacementSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (other == source) continue;
                if (other.id == source.id) plan.errors.Add("Duplicate placement ID: " + other.name + ". Use Duplicate with fresh ID.");
                if (definition.uniqueDefinition && other.definition != null && other.definition.id == definition.id) plan.errors.Add("Unique definition is already placed: " + other.name);
                if (other.level != source.level || other.district != source.district || !Resolve(other.anchor, out var otherPose, out _)) continue;
                var otherCenter = otherPose.position + otherPose.rotation * other.interactionOffset;
                if (Overlaps(r.interaction, r.rotation, r.triggerSize, otherCenter, otherPose.rotation, other.triggerSize))
                    plan.errors.Add("Interaction envelope conflicts with " + other.name + " on level " + source.level + ". Move or resize the placements.");
            }
            if (plan.errors.Count == 0)
            {
                var lane = source.access.network.Lanes.First(l => l.Id.ToString() == source.access.laneId);
                if (lane.Sample(source.access.station).width < source.vehicleSize.x) plan.errors.Add("Bound access lane is narrower than the vehicle.");
                if (source.anchor.kind == ActivityAnchorKind.CityEntrance && source.anchor.city.TryResolve(source.anchor.entranceId, out var entrance) && entrance.entranceLane.ToString() != source.access.laneId)
                    plan.errors.Add("City entrance belongs to a different lane. Bind that entrance explicitly.");
                if (source.anchor.kind == ActivityAnchorKind.Lane && source.anchor.network != source.access.network)
                    plan.errors.Add("Anchor and access use different road publications.");
                CheckCorridor(source, r.access, r.interaction, plan, checkGeometry);
                CheckCorridor(source, r.interaction, r.staging, plan, checkGeometry);
                if (definition.adapter == "service") foreach (var exit in r.exits) CheckCorridor(source, r.access, exit, plan, checkGeometry);
                if (checkGeometry)
                {
                    var colliders = new Collider[32];
                    foreach (var point in new[] { r.interaction, r.staging }.Concat(definition.adapter == "service" ? r.exits : Array.Empty<Vector3>()))
                        if (source.gameObject.scene.GetPhysicsScene().OverlapBox(point + r.rotation * Vector3.up * (r.vehicleSize.y / 2 + .2f), r.vehicleSize / 2, colliders, r.rotation, source.obstructionMask, QueryTriggerInteraction.Ignore) > 0)
                        { plan.errors.Add("Authored vehicle heading has obstructed clearance at entrance/staging/exit."); break; }
                }
                if (!checkGeometry) plan.warnings.Add("Geometry not evaluated. This diagnostic plan cannot be published.");
            }
            r.fingerprint = Fingerprint(source, r);
            return plan;
        }

        private static void CheckCorridor(EventPlacementSource source, Vector3 start, Vector3 end, ActivityPlan plan, bool geometry)
        {
            Vector3 delta = end - start;
            if (delta.magnitude > source.maximumAccessLength) { plan.errors.Add("Access corridor exceeds the approved length."); return; }
            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            if (Mathf.Atan2(Mathf.Abs(delta.y), horizontal) * Mathf.Rad2Deg > source.maximumGrade)
            { plan.errors.Add("Access crosses an incompatible height or grade (check bridge/tunnel level)."); return; }
            if (!geometry) return;
            // Conservative swept vehicle envelope plus support checks in this placement's physics scene.
            var scene = source.gameObject.scene.GetPhysicsScene();
            Quaternion rotation = horizontal > .01f ? Quaternion.LookRotation(new Vector3(delta.x, 0, delta.z)) : Quaternion.Euler(0, source.anchor.worldEuler.y, 0);
            int count = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 1));
            var overlaps = new Collider[64];
            for (int i = 0; i <= count; i++)
            {
                Vector3 ground = Vector3.Lerp(start, end, (float)i / count);
                if (!scene.Raycast(ground + Vector3.up * .5f, Vector3.down, out var support, 1, source.obstructionMask, QueryTriggerInteraction.Ignore))
                { plan.errors.Add("Access has no loaded supporting surface. Load collision geometry before publishing."); return; }
                if (Vector3.Angle(support.normal, Vector3.up) > source.maximumGrade)
                { plan.errors.Add("Access surface exceeds the slope limit."); return; }
                int hits = scene.OverlapBox(ground + Vector3.up * (source.vehicleSize.y / 2 + .2f), source.vehicleSize / 2 + new Vector3(0, 0, .5f), overlaps, rotation, source.obstructionMask, QueryTriggerInteraction.Ignore);
                if (hits > 0) { plan.errors.Add("Vehicle clearance is obstructed near " + ground.ToString("F1") + "."); return; }
            }
        }
        private static string Fingerprint(EventPlacementSource source, ActivityRecord record)
        {
            var copy = record.Copy(); copy.fingerprint = null;
            string refs = AssetDatabase.GetAssetPath(source.definition);
            if (source.definition != null) refs += AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(source.definition));
            return Hash128.Compute(StableJson(JsonUtility.ToJson(copy), copy.route, copy.mission, copy.storefront) + refs + StableJson(JsonUtility.ToJson(source.anchor), source.anchor.network, source.anchor.city, source.anchor.socket) + StableJson(JsonUtility.ToJson(source.access), source.access.network, source.access.city, source.access.socket) + source.maximumAccessLength.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + source.maximumGrade.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + source.obstructionMask.value).ToString();
        }
        [Serializable] private sealed class ReferenceBox { public UnityEngine.Object value; }
        private static string StableJson(string json, params UnityEngine.Object[] references)
        {
            var ids = new Dictionary<string, string> { { "0", "null" } };
            foreach (var reference in references)
            {
                if (reference == null) continue;
                var match = System.Text.RegularExpressions.Regex.Match(JsonUtility.ToJson(new ReferenceBox { value = reference }), @"""instanceID"":(-?\d+)");
                if (match.Success) ids[match.Groups[1].Value] = GlobalObjectId.GetGlobalObjectIdSlow(reference).ToString();
            }
            return System.Text.RegularExpressions.Regex.Replace(json, @"""instanceID"":(-?\d+)", match =>
                "\"stableReference\":\"" + ids[match.Groups[1].Value] + "\"");
        }
        public static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
        private static bool Positive(Vector3 p) => Finite(p) && p.x > 0 && p.y > 0 && p.z > 0;
        public static bool Overlaps(Vector3 a, Quaternion ar, Vector3 sizeA, Vector3 b, Quaternion br, Vector3 sizeB)
        {
            // Full oriented-box separating axis test (including cross axes).
            Vector3[] axesA = { ar * Vector3.right, ar * Vector3.up, ar * Vector3.forward };
            Vector3[] axesB = { br * Vector3.right, br * Vector3.up, br * Vector3.forward };
            var axes = new List<Vector3>(axesA); axes.AddRange(axesB);
            foreach (var x in axesA) foreach (var y in axesB) axes.Add(Vector3.Cross(x, y));
            foreach (var axis in axes)
            {
                if (axis.sqrMagnitude < .000001f) continue;
                float radius = 0;
                for (int i = 0; i < 3; i++) radius += Mathf.Abs(Vector3.Dot(axis, axesA[i])) * sizeA[i] / 2 + Mathf.Abs(Vector3.Dot(axis, axesB[i])) * sizeB[i] / 2;
                if (Mathf.Abs(Vector3.Dot(b - a, axis)) >= radius) return false;
            }
            return true;
        }
    }
}
