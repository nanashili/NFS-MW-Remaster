using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor.WorldValidation
{
    public abstract class WorldValidationRuleBase : IWorldValidationRule
    {
        protected WorldValidationRuleBase(string id, string name, string owner, WorldValidationCategory category,
            WorldValidationCost cost, IEnumerable<WorldValidationScopeKind> scopes, IEnumerable<string> inputs,
            IEnumerable<string> dependencies = null, bool supportsFixes = false, string runtimeRequirements = "")
        {
            Descriptor = new WorldValidationRuleDescriptor
            {
                id = id,
                version = 1,
                displayName = name,
                ownerModule = owner,
                category = category,
                expectedCost = cost,
                supportedScopes = (scopes ?? Array.Empty<WorldValidationScopeKind>()).ToArray(),
                requiredInputs = (inputs ?? Array.Empty<string>()).ToArray(),
                dependencies = (dependencies ?? Array.Empty<string>()).ToArray(),
                supportsFixes = supportsFixes,
                requiresMainThread = true,
                runtimeRequirements = runtimeRequirements ?? string.Empty
            };
        }

        public WorldValidationRuleDescriptor Descriptor { get; }
        public abstract void Evaluate(WorldValidationContext context, WorldValidationResultSink results);

        protected static void AssetIssue(WorldValidationResultSink sink, WorldValidationRuleDescriptor descriptor,
            UnityEngine.Object asset, WorldValidationStatus status, WorldValidationSeverity severity,
            string code, string title, string message)
        {
            var result = WorldValidationResult.Create(descriptor, status, severity, code, title, message);
            sink.AddTarget(result, asset);
        }

        protected static void SceneIssue(WorldValidationResultSink sink, WorldValidationRuleDescriptor descriptor,
            Scene scene, WorldValidationStatus status, WorldValidationSeverity severity,
            string code, string title, string message, Vector3? position = null)
        {
            var result = WorldValidationResult.Create(descriptor, status, severity, code, title, message);
            result.SetTarget(string.Empty, scene.path, string.Empty);
            if (position.HasValue)
            {
                result.worldPosition = position.Value;
                result.hasWorldPosition = true;
            }
            sink.Add(result);
        }

        protected static void PassAsset(WorldValidationResultSink sink, WorldValidationRuleDescriptor descriptor,
            UnityEngine.Object asset, string message = null)
        {
            var result = WorldValidationResult.Passed(descriptor, message ?? "No issues found in the inspected asset.");
            sink.AddTarget(result, asset);
        }

        protected static void PassScene(WorldValidationResultSink sink, WorldValidationRuleDescriptor descriptor,
            Scene scene, string message = null)
        {
            var result = WorldValidationResult.Passed(descriptor, message ?? "No issues found in the inspected scene.");
            result.SetTarget(string.Empty, scene.path, string.Empty);
            sink.Add(result);
        }

        protected static void NotEvaluated(WorldValidationResultSink sink, WorldValidationRuleDescriptor descriptor,
            string code, string message, UnityEngine.Object asset = null)
        {
            var result = WorldValidationResult.NotEvaluated(descriptor, code, message);
            if (asset != null) sink.AddTarget(result, asset);
            else sink.Add(result);
        }

        protected static void Unsupported(WorldValidationResultSink sink, WorldValidationRuleDescriptor descriptor,
            string code, string message)
        {
            sink.Add(WorldValidationResult.Unsupported(descriptor, code, message));
        }

        protected static string ExceptionMessage(Exception exception)
        {
            Exception root = exception;
            while (root.InnerException != null) root = root.InnerException;
            return root.GetType().Name + ": " + root.Message;
        }

        protected static bool Finite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        protected static bool Finite(float value)
        {
            return float.IsFinite(value);
        }
    }

    public sealed class WorldValidationIdentityRule : WorldValidationRuleBase
    {
        public WorldValidationIdentityRule()
            : base("world.identity.references", "Identity and reference integrity", "Core/Identity",
                WorldValidationCategory.Identity, WorldValidationCost.Cheap,
                Enum.GetValues(typeof(WorldValidationScopeKind)).Cast<WorldValidationScopeKind>(),
                new[] { "ScriptableObject", "stable IDs", "published revisions" }) { }

        public override void Evaluate(WorldValidationContext context, WorldValidationResultSink results)
        {
            int inspected = 0;
            CheckRoadNetworks(context, results, ref inspected);
            CheckRoutes(context, results, ref inspected);
            CheckActivities(context, results, ref inspected);
            CheckTrafficProfiles(context, results, ref inspected);
            CheckMissionAssets(context, results, ref inspected);
            CheckCareerAssets(context, results, ref inspected);
            if (inspected == 0) NotEvaluated(results, Descriptor, "IDENTITY_NO_INPUT", "No identity-bearing assets were included in this scope.");
        }

        private void CheckRoadNetworks(WorldValidationContext context, WorldValidationResultSink results, ref int inspected)
        {
            var ids = new Dictionary<string, RoadNetworkAsset>(StringComparer.Ordinal);
            foreach (RoadNetworkAsset asset in context.FindAssets<RoadNetworkAsset>().Distinct())
            {
                inspected++;
                string path = AssetDatabase.GetAssetPath(asset);
                if (!asset.NetworkId.IsValid) AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                    "ROAD_NETWORK_ID", "Invalid network identity", "The authoritative road publication has no valid stable RoadId.");
                else if (ids.TryGetValue(asset.NetworkId.ToString(), out RoadNetworkAsset other)) AssetIssue(results, Descriptor, asset,
                    WorldValidationStatus.Failed, WorldValidationSeverity.Blocker, "ROAD_NETWORK_ID_DUPLICATE", "Duplicate network identity",
                    "The stable network ID is also used by " + AssetDatabase.GetAssetPath(other) + ".");
                else ids.Add(asset.NetworkId.ToString(), asset);

                bool valid = true;
                if (asset.SchemaVersion != RoadNetworkAsset.CurrentSchema)
                {
                    AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "ROAD_NETWORK_SCHEMA", "Unsupported road publication", "Expected road schema " + RoadNetworkAsset.CurrentSchema + ", found " + asset.SchemaVersion + ".");
                    valid = false;
                }
                if (string.IsNullOrWhiteSpace(asset.Fingerprint))
                {
                    AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "ROAD_NETWORK_REVISION", "Missing road revision", "A road publication without a source fingerprint cannot be trusted by routes, maps or events.");
                    valid = false;
                }
                try
                {
                    var lanes = asset.Lanes;
                    var laneIds = new HashSet<string>(StringComparer.Ordinal);
                    var compatibilityIds = new HashSet<int>();
                    var known = new HashSet<string>(StringComparer.Ordinal);
                    foreach (RoadBakedLane lane in lanes)
                    {
                        if (lane == null || !lane.Id.IsValid)
                        {
                            AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                                "ROAD_LANE_ID", "Invalid lane identity", "Every published lane needs a valid stable RoadId.");
                            valid = false;
                            continue;
                        }
                        if (!laneIds.Add(lane.Id.ToString()))
                        {
                            AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                                "ROAD_LANE_ID_DUPLICATE", "Duplicate lane identity", "The publication contains the same stable lane ID more than once.");
                            valid = false;
                        }
                        known.Add(lane.Id.ToString());
                        if (!compatibilityIds.Add(lane.CompatibilityId))
                        {
                            AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                                "ROAD_LANE_COMPAT_DUPLICATE", "Duplicate runtime lane index", "Generated compatibility IDs must be unique within a publication.");
                            valid = false;
                        }
                        if (lane.Samples == null || lane.Samples.Count < 2 || !Finite(lane.Length) || lane.Length <= 0)
                        {
                            AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                                "ROAD_LANE_GEOMETRY", "Invalid lane geometry", "A published lane needs at least two finite, advancing samples.");
                            valid = false;
                            continue;
                        }
                        for (int i = 0; i < lane.Samples.Count; i++)
                        {
                            RoadLaneSample sample = lane.Samples[i];
                            if (!Finite(sample.position) || !Finite(sample.forward) || !Finite(sample.up)
                                || !Finite(sample.width) || sample.width <= 0 || !Finite(sample.distance))
                            {
                                AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                                    "ROAD_LANE_SAMPLE", "Invalid lane sample", "Lane samples must have finite position, orientation, distance and positive width.");
                                valid = false;
                                break;
                            }
                        }
                    }
                    foreach (RoadBakedLane lane in lanes)
                    {
                        if (lane == null) continue;
                        foreach (RoadId successor in lane.Successors ?? Array.Empty<RoadId>())
                            if (!successor.IsValid || !known.Contains(successor.ToString()))
                            {
                                AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                                    "ROAD_SUCCESSOR_DANGLING", "Dangling lane successor", "A lane points to a successor that is not present in the same publication.");
                                valid = false;
                            }
                    }
                    if (asset.Chunks == null) valid = false;
                    foreach (RoadBakedChunk chunk in asset.Chunks ?? Array.Empty<RoadBakedChunk>())
                        if (chunk == null || chunk.Mesh == null || chunk.Material == null)
                        {
                            AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                                "ROAD_CHUNK_OUTPUT", "Incomplete generated road chunk", "Published road geometry must contain a mesh and material for every chunk.");
                            valid = false;
                        }
                    try { RoadNetwork.ValidatePublication(asset); }
                    catch (Exception exception)
                    {
                        AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                            "ROAD_PUBLICATION_RUNTIME", "Runtime cannot consume road publication", ExceptionMessage(exception));
                        valid = false;
                    }
                }
                catch (Exception exception)
                {
                    AssetIssue(results, Descriptor, asset, WorldValidationStatus.ErrorRunning, WorldValidationSeverity.Error,
                        "IDENTITY_ASSET_READ", "Could not inspect road publication", ExceptionMessage(exception));
                    valid = false;
                }
                if (valid) PassAsset(results, Descriptor, asset, "Road publication identity, revision and runtime shape are internally consistent.");
            }
        }

        private void CheckRoutes(WorldValidationContext context, WorldValidationResultSink results, ref int inspected)
        {
            var ids = new Dictionary<string, RaceRouteDefinition>(StringComparer.Ordinal);
            foreach (RaceRouteDefinition asset in context.FindAssets<RaceRouteDefinition>().Distinct())
            {
                inspected++;
                bool valid = true;
                if (!Guid.TryParseExact(asset.id ?? string.Empty, "N", out _))
                {
                    AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "ROUTE_ID", "Invalid route identity", "Race routes must use a stable N-format GUID.");
                    valid = false;
                }
                else if (ids.TryGetValue(asset.id, out RaceRouteDefinition other))
                {
                    AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "ROUTE_ID_DUPLICATE", "Duplicate route identity", "The route ID is also used by " + AssetDatabase.GetAssetPath(other) + ".");
                    valid = false;
                }
                else ids.Add(asset.id, asset);
                if (asset.schema != RaceRouteDefinition.CurrentSchema)
                {
                    AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "ROUTE_SCHEMA", "Unsupported route schema", "Expected route schema " + RaceRouteDefinition.CurrentSchema + ".");
                    valid = false;
                }
                if (asset.network == null || asset.network.SchemaVersion != RoadNetworkAsset.CurrentSchema)
                {
                    AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "ROUTE_NETWORK", "Route has no compatible road publication", "Routes consume the authoritative published road network and cannot fall back to a legacy graph.");
                    valid = false;
                }
                if (valid) PassAsset(results, Descriptor, asset, "Route identity and authoritative network reference are valid; continuity is checked by the route rule.");
            }
        }

        private void CheckActivities(WorldValidationContext context, WorldValidationResultSink results, ref int inspected)
        {
            var ids = new Dictionary<string, WorldActivityDefinition>(StringComparer.Ordinal);
            foreach (WorldActivityDefinition asset in context.FindAssets<WorldActivityDefinition>().Distinct())
            {
                inspected++;
                bool valid = true;
                if (!Guid.TryParseExact(asset.id ?? string.Empty, "N", out _))
                {
                    AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "ACTIVITY_ID", "Invalid activity identity", "World activity definitions must use a stable N-format GUID.");
                    valid = false;
                }
                else if (ids.TryGetValue(asset.id, out WorldActivityDefinition other))
                {
                    AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "ACTIVITY_ID_DUPLICATE", "Duplicate activity identity", "The activity ID is also used by " + AssetDatabase.GetAssetPath(other) + ".");
                    valid = false;
                }
                else ids.Add(asset.id, asset);
                if (asset.schema != 1 || string.IsNullOrWhiteSpace(asset.adapter))
                {
                    AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "ACTIVITY_SCHEMA", "Incomplete activity definition", "Activity schema and runtime adapter are required.");
                    valid = false;
                }
                try
                {
                    if (asset.availability == null)
                    {
                        AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Error,
                            "ACTIVITY_AVAILABILITY", "Invalid availability condition", "Availability JSON did not compile into a career requirement.");
                        valid = false;
                    }
                }
                catch (Exception exception)
                {
                    AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Error,
                        "ACTIVITY_AVAILABILITY", "Invalid availability condition", ExceptionMessage(exception));
                    valid = false;
                }
                if (asset.adapter == "race" && asset.race == null || asset.adapter == "mission" && asset.mission == null)
                {
                    AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Error,
                        "ACTIVITY_REFERENCE", "Missing activity owner", "The selected adapter requires a bound race or mission definition.");
                    valid = false;
                }
                if (valid) PassAsset(results, Descriptor, asset, "Activity identity and typed owner references are valid.");
            }
        }

        private void CheckTrafficProfiles(WorldValidationContext context, WorldValidationResultSink results, ref int inspected)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (TrafficWorldProfile asset in context.FindAssets<TrafficWorldProfile>().Distinct())
            {
                inspected++;
                bool valid = !string.IsNullOrWhiteSpace(asset.profileId) && ids.Add(asset.profileId);
                if (!valid) AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Error,
                    "TRAFFIC_PROFILE_ID", "Invalid traffic profile identity", "Traffic world profiles need a non-empty unique profile ID within the inspected scope.");
                if (!Finite(asset.defaultPortalDemandPerHour) || asset.defaultPortalDemandPerHour < 0 || !Finite(asset.demandScale) || asset.demandScale < 0
                    || !Finite(asset.populationInterval) || asset.populationInterval <= 0 || !Finite(asset.physicalRadius) || asset.physicalRadius < 100
                    || !Finite(asset.minimumPortalHeadway) || asset.minimumPortalHeadway <= 0)
                {
                    AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Error,
                        "TRAFFIC_PROFILE_BOUNDS", "Invalid traffic profile bounds", "Demand, radius, interval and headway values must be finite and within the runtime contract.");
                    valid = false;
                }
                if (asset.demand == null || asset.vehicleWeights == null || asset.vehicleWeights.Length != 7)
                {
                    AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Error,
                        "TRAFFIC_PROFILE_COLLECTION", "Incomplete traffic profile", "Traffic profiles require demand data and seven vehicle-category weights.");
                    valid = false;
                }
                if (valid) PassAsset(results, Descriptor, asset, "Traffic population identity and static bounds are valid.");
            }
            foreach (TrafficVehicleProfile asset in context.FindAssets<TrafficVehicleProfile>().Distinct())
            {
                inspected++;
                bool valid = Finite(asset.colliderSize) && asset.colliderSize.x > 0 && asset.colliderSize.y > 0 && asset.colliderSize.z > 0
                    && Finite(asset.colliderCenter) && Finite(asset.comfortAccelerationLimit) && asset.comfortAccelerationLimit > 0
                    && Finite(asset.brakingLimit) && asset.brakingLimit >= 4 && Finite(asset.wheelbase) && asset.wheelbase > 0
                    && Finite(asset.maximumLateralAcceleration) && asset.maximumLateralAcceleration > 0;
                if (!valid) AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Error,
                    "TRAFFIC_VEHICLE_BOUNDS", "Invalid traffic vehicle envelope", "Traffic vehicle dimensions and motion limits must be finite and positive.");
                else PassAsset(results, Descriptor, asset, "Traffic vehicle envelope is usable by the population runtime.");
            }
        }

        private void CheckMissionAssets(WorldValidationContext context, WorldValidationResultSink results, ref int inspected)
        {
            foreach (MissionDefinitionAsset asset in context.FindAssets<MissionDefinitionAsset>().Distinct())
            {
                inspected++;
                try
                {
                    MissionGraph graph = asset.Compile();
                    if (graph == null || string.IsNullOrEmpty(graph.Id)) throw new InvalidOperationException("Compiled mission has no stable ID.");
                    PassAsset(results, Descriptor, asset, "Mission compiles through the authoritative runtime graph.");
                }
                catch (Exception exception)
                {
                    AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "MISSION_COMPILE", "Mission cannot compile", ExceptionMessage(exception));
                }
            }
        }

        private void CheckCareerAssets(WorldValidationContext context, WorldValidationResultSink results, ref int inspected)
        {
            foreach (CareerDefinitionAsset asset in context.FindAssets<CareerDefinitionAsset>().Distinct())
            {
                inspected++;
                try
                {
                    CareerGraph graph = asset.Compile();
                    if (graph == null || string.IsNullOrEmpty(graph.Id)) throw new InvalidOperationException("Compiled career has no stable ID.");
                    PassAsset(results, Descriptor, asset, "Career compiles through the authoritative progression graph.");
                }
                catch (Exception exception)
                {
                    AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "CAREER_COMPILE", "Career cannot compile", ExceptionMessage(exception));
                }
            }
        }
    }

    public sealed class WorldValidationRoadRule : WorldValidationRuleBase
    {
        public WorldValidationRoadRule()
            : base("roads.authority", "Road authority and generated geometry", "Driving/Roads",
                WorldValidationCategory.Roads, WorldValidationCost.Standard,
                new[] { WorldValidationScopeKind.SelectedObjects, WorldValidationScopeKind.OpenScenes, WorldValidationScopeKind.ExplicitScenes,
                    WorldValidationScopeKind.BuildContent, WorldValidationScopeKind.Project },
                new[] { "RoadAuthoring", "RoadNetworkAuthoring", "RoadNetworkAsset", "RoadBuildGuard" }) { }

        public override void Evaluate(WorldValidationContext context, WorldValidationResultSink results)
        {
            int inspectedScenes = 0;
            context.InspectScenes(scene =>
            {
                context.ThrowIfCancellationRequested();
                inspectedScenes++;
                var roots = scene.GetRootGameObjects();
                var roads = roots.SelectMany(root => root.GetComponentsInChildren<RoadAuthoring>(true)).Where(value => value != null).ToArray();
                var networks = roots.SelectMany(root => root.GetComponentsInChildren<RoadNetworkAuthoring>(true)).Where(value => value != null).ToArray();
                if (roads.Length == 0 && networks.Length == 0)
                {
                    NotEvaluated(results, Descriptor, "ROAD_SCENE_NO_INPUT", "The scene contains no authoritative road authoring objects.");
                    return;
                }

                bool valid = true;
                var roadIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (RoadAuthoring road in roads)
                {
                    if (!road.Id.IsValid || !roadIds.Add(road.Id.ToString()))
                    {
                        SceneIssue(results, Descriptor, scene, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                            "ROAD_AUTHORING_ID", "Invalid or duplicate road identity", "Every authored road in a scene needs a unique stable RoadId.", road.transform.position);
                        valid = false;
                    }
                    if (road.SchemaVersion != RoadAuthoring.CurrentSchema || road.Profile == null || road.Reference == null)
                    {
                        SceneIssue(results, Descriptor, scene, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                            "ROAD_AUTHORING_SOURCE", "Incomplete road source", "Road authoring needs the current schema, a profile and its owned spline reference.", road.transform.position);
                        valid = false;
                    }
                }
                var networkIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (RoadNetworkAuthoring network in networks)
                {
                    if (!network.Id.IsValid || !networkIds.Add(network.Id.ToString()))
                    {
                        SceneIssue(results, Descriptor, scene, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                            "ROAD_NETWORK_AUTHORING_ID", "Invalid or duplicate network identity", "Road network authoring owners need unique stable IDs.", network.transform.position);
                        valid = false;
                    }
                    if (network.Roads == null || network.Roads.Any(road => road == null || !roads.Contains(road)))
                    {
                        SceneIssue(results, Descriptor, scene, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                            "ROAD_OWNERSHIP", "Road ownership is incomplete", "Every RoadAuthoring must be owned by the network that publishes it.", network.transform.position);
                        valid = false;
                    }
                }
                try
                {
                    RoadBuildGuard.ValidateScene(scene);
                }
                catch (BuildFailedException exception)
                {
                    SceneIssue(results, Descriptor, scene, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        ParseCode(exception.Message, "ROAD_BUILD_GUARD"), "Road publication is not build-safe", exception.Message);
                    valid = false;
                }
                catch (Exception exception)
                {
                    SceneIssue(results, Descriptor, scene, WorldValidationStatus.ErrorRunning, WorldValidationSeverity.Error,
                        "ROAD_VALIDATOR_EXCEPTION", "Road validator failed", ExceptionMessage(exception));
                    valid = false;
                }
                if (valid) PassScene(results, Descriptor, scene, "Road ownership, publication and generated geometry match the road authority contract.");
            });
            if (inspectedScenes == 0) NotEvaluated(results, Descriptor, "ROAD_NO_SCENES", "This rule needs a scene scope; no scenes were available.");
        }

        private static string ParseCode(string message, string fallback)
        {
            if (string.IsNullOrEmpty(message)) return fallback;
            int separator = message.IndexOf(':');
            string candidate = separator > 0 ? message.Substring(0, separator) : string.Empty;
            return candidate.All(character => char.IsUpper(character) || char.IsDigit(character) || character == '_') ? candidate : fallback;
        }
    }

    public sealed class WorldValidationRaceRouteRule : WorldValidationRuleBase
    {
        public WorldValidationRaceRouteRule()
            : base("races.routes", "Race route continuity and publication", "Driving/RaceRoutes",
                WorldValidationCategory.RaceVehicle, WorldValidationCost.Standard,
                Enum.GetValues(typeof(WorldValidationScopeKind)).Cast<WorldValidationScopeKind>(),
                new[] { "RaceRouteDefinition", "RaceRouteCompiler", "RoadNetworkAsset" },
                new[] { "world.identity.references" }) { }

        public override void Evaluate(WorldValidationContext context, WorldValidationResultSink results)
        {
            int count = 0;
            foreach (RaceRouteDefinition route in context.FindAssets<RaceRouteDefinition>().Distinct())
            {
                count++;
                bool valid = true;
                RaceRoutePlan plan;
                try
                {
                    plan = RaceRouteCompiler.Build(route, progress =>
                    {
                        context.ThrowIfCancellationRequested();
                        return false;
                    });
                }
                catch (Exception exception)
                {
                    AssetIssue(results, Descriptor, route, exception is OperationCanceledException ? WorldValidationStatus.Cancelled : WorldValidationStatus.ErrorRunning,
                        WorldValidationSeverity.Error, "ROUTE_VALIDATOR_EXCEPTION", "Route validator failed", ExceptionMessage(exception));
                    continue;
                }
                foreach (RaceRouteIssue issue in plan.issues)
                {
                    WorldValidationStatus status = issue.error ? WorldValidationStatus.Failed : WorldValidationStatus.Warning;
                    WorldValidationSeverity severity = issue.error ? WorldValidationSeverity.Error : WorldValidationSeverity.Warning;
                    AssetIssue(results, Descriptor, route, status, severity, issue.rule ?? "ROUTE_ISSUE",
                        issue.error ? "Route continuity issue" : "Route review", issue.message ?? issue.ToString());
                    valid &= !issue.error;
                }
                if (route.published == null)
                {
                    AssetIssue(results, Descriptor, route, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "ROUTE_UNPUBLISHED", "Route has no runtime publication", "The editor source may be valid, but runtime events consume only an explicit immutable route publication.");
                    valid = false;
                }
                else
                {
                    string sourceFingerprint = string.Empty;
                    try { sourceFingerprint = RaceRouteCompiler.Fingerprint(route); } catch (Exception exception) { sourceFingerprint = exception.Message; }
                    if (route.published.Schema != 1 || route.published.RouteId != route.id
                        || route.published.Fingerprint != sourceFingerprint || route.network == null
                        || route.published.Network != route.network || route.published.RoadFingerprint != route.network.Fingerprint)
                    {
                        AssetIssue(results, Descriptor, route, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                            "ROUTE_PUBLICATION_STALE", "Route publication is stale", "Published route identity, source revision or road publication no longer matches the authored route.");
                        valid = false;
                    }
                }
                if (valid) PassAsset(results, Descriptor, route, "Route continuity, entrant envelope and immutable publication agree.");
            }
            if (count == 0) NotEvaluated(results, Descriptor, "ROUTE_NO_INPUT", "No race route definitions were included in this scope.");
        }
    }

    public sealed class WorldValidationEventPlacementRule : WorldValidationRuleBase
    {
        public WorldValidationEventPlacementRule()
            : base("events.placements", "Event placement access and clearance", "Driving/EventPlacement",
                WorldValidationCategory.EventsMissionsCareer, WorldValidationCost.Standard,
                new[] { WorldValidationScopeKind.SelectedObjects, WorldValidationScopeKind.OpenScenes, WorldValidationScopeKind.ExplicitScenes,
                    WorldValidationScopeKind.BuildContent, WorldValidationScopeKind.Project },
                new[] { "EventPlacementSource", "EventPlacementCompiler", "WorldActivityDefinition" },
                new[] { "world.identity.references", "roads.authority" }) { }

        public override void Evaluate(WorldValidationContext context, WorldValidationResultSink results)
        {
            int scenes = 0;
            context.InspectScenes(scene =>
            {
                scenes++;
                EventPlacementSource[] sources = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<EventPlacementSource>(true)).Where(value => value != null).ToArray();
                if (sources.Length == 0)
                {
                    NotEvaluated(results, Descriptor, "EVENT_SCENE_NO_INPUT", "The scene contains no event placement sources.");
                    return;
                }
                foreach (EventPlacementSource source in sources)
                {
                    context.ThrowIfCancellationRequested();
                    try
                    {
                        ActivityPlan plan = EventPlacementCompiler.Build(source, context.IncludeExpensive);
                        bool valid = true;
                        foreach (string error in plan.errors ?? new List<string>())
                        {
                            AssetIssue(results, Descriptor, source, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                                "EVENT_PLACEMENT", "Event placement is invalid", error);
                            valid = false;
                        }
                        foreach (string warning in plan.warnings ?? new List<string>())
                        {
                            AssetIssue(results, Descriptor, source, WorldValidationStatus.Warning, WorldValidationSeverity.Warning,
                                warning != null && warning.IndexOf("not evaluated", StringComparison.OrdinalIgnoreCase) >= 0 ? "EVENT_GEOMETRY_NOT_EVALUATED" : "EVENT_REVIEW",
                                "Event placement review", warning);
                            valid = false;
                        }
                        if (valid) PassAsset(results, Descriptor, source, "Event definition, access anchor and authored interaction envelope are valid.");
                    }
                    catch (Exception exception)
                    {
                        AssetIssue(results, Descriptor, source, WorldValidationStatus.ErrorRunning, WorldValidationSeverity.Error,
                            "EVENT_VALIDATOR_EXCEPTION", "Event placement validator failed", ExceptionMessage(exception));
                    }
                }
            });
            if (scenes == 0) NotEvaluated(results, Descriptor, "EVENT_NO_SCENES", "This rule needs a scene scope; no scenes were available.");
        }
    }

    public sealed class WorldValidationMissionCareerRule : WorldValidationRuleBase
    {
        public WorldValidationMissionCareerRule()
            : base("missions.career.compile", "Mission and career compilation", "Driving/MissionsCareer",
                WorldValidationCategory.EventsMissionsCareer, WorldValidationCost.Standard,
                Enum.GetValues(typeof(WorldValidationScopeKind)).Cast<WorldValidationScopeKind>(),
                new[] { "MissionDefinitionAsset", "CareerDefinitionAsset", "authoritative runtime compilers" },
                new[] { "world.identity.references" }) { }

        public override void Evaluate(WorldValidationContext context, WorldValidationResultSink results)
        {
            int count = 0;
            foreach (MissionDefinitionAsset asset in context.FindAssets<MissionDefinitionAsset>().Distinct())
            {
                count++;
                try
                {
                    MissionGraph graph = asset.Compile();
                    if (graph == null) throw new InvalidOperationException("Mission compiler returned no graph.");
                    PassAsset(results, Descriptor, asset, "Mission graph compiles; detailed authoring diagnostics are supplied by the Mission editor adapter when installed.");
                }
                catch (Exception exception)
                {
                    AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "MISSION_COMPILE", "Mission compilation failed", ExceptionMessage(exception));
                }
            }
            foreach (CareerDefinitionAsset asset in context.FindAssets<CareerDefinitionAsset>().Distinct())
            {
                count++;
                try
                {
                    CareerGraph graph = asset.Compile();
                    if (graph == null) throw new InvalidOperationException("Career compiler returned no graph.");
                    PassAsset(results, Descriptor, asset, "Career graph compiles through the authoritative progression runtime.");
                }
                catch (Exception exception)
                {
                    AssetIssue(results, Descriptor, asset, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "CAREER_COMPILE", "Career compilation failed", ExceptionMessage(exception));
                }
            }
            if (count == 0) NotEvaluated(results, Descriptor, "MISSION_CAREER_NO_INPUT", "No mission or career definition assets were included in this scope.");
        }
    }

    public sealed class WorldValidationTrafficPoliceRule : WorldValidationRuleBase
    {
        public WorldValidationTrafficPoliceRule()
            : base("traffic.police.authoring", "Traffic signals, police sites and spawn envelopes", "Driving/TrafficPolice",
                WorldValidationCategory.TrafficPolice, WorldValidationCost.Standard,
                new[] { WorldValidationScopeKind.SelectedObjects, WorldValidationScopeKind.OpenScenes, WorldValidationScopeKind.ExplicitScenes,
                    WorldValidationScopeKind.BuildContent, WorldValidationScopeKind.Project },
                new[] { "TrafficWorldProfile", "TrafficSignalPlan", "RoadTrafficSignals", "PoliceRoadHazard" },
                new[] { "roads.authority" }, false,
                "Static authoring checks only. Dynamic fairness requires a scenario provider and geometry evidence.") { }

        public override void Evaluate(WorldValidationContext context, WorldValidationResultSink results)
        {
            int inputs = 0;
            foreach (TrafficWorldProfile profile in context.FindAssets<TrafficWorldProfile>().Distinct())
            {
                inputs++;
                bool valid = true;
                if (profile.demand == null || profile.demand.Any(flow => flow == null || !Finite(flow.vehiclesPerHour) || flow.vehiclesPerHour < 0))
                {
                    AssetIssue(results, Descriptor, profile, WorldValidationStatus.Failed, WorldValidationSeverity.Error,
                        "TRAFFIC_DEMAND", "Invalid traffic demand", "Demand flows must be present and use finite non-negative rates.");
                    valid = false;
                }
                if (profile.districts != null)
                {
                    var districtIds = new HashSet<int>();
                    foreach (TrafficDistrictDemand district in profile.districts)
                        if (district == null || !districtIds.Add(district.district) || !Finite(district.multiplier) || district.multiplier < 0)
                        {
                            AssetIssue(results, Descriptor, profile, WorldValidationStatus.Failed, WorldValidationSeverity.Error,
                                "TRAFFIC_DISTRICT", "Invalid traffic district demand", "District IDs must be unique and their multipliers finite and non-negative.");
                            valid = false;
                        }
                }
                if (valid) PassAsset(results, Descriptor, profile, "Traffic population metadata is structurally valid; runtime density still needs scenario evidence.");
            }
            foreach (TrafficVehicleProfile profile in context.FindAssets<TrafficVehicleProfile>().Distinct())
            {
                inputs++;
                bool valid = Finite(profile.colliderSize) && profile.colliderSize.x > 0 && profile.colliderSize.y > 0 && profile.colliderSize.z > 0
                    && Finite(profile.colliderCenter) && Finite(profile.brakingLimit) && profile.brakingLimit >= 4;
                if (!valid) AssetIssue(results, Descriptor, profile, WorldValidationStatus.Failed, WorldValidationSeverity.Error,
                    "TRAFFIC_SPAWN_ENVELOPE", "Invalid traffic spawn envelope", "Vehicle collider and braking dimensions must be finite and usable for safe spawning.");
                else PassAsset(results, Descriptor, profile, "Traffic spawn envelope is statically valid.");
            }

            var roadNetworks = new List<RoadNetwork>();
            context.InspectScenes(scene =>
            {
                roadNetworks.AddRange(scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RoadNetwork>(true)).Where(value => value != null));
                RoadTrafficSignals[] signals = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RoadTrafficSignals>(true)).Where(value => value != null).ToArray();
                foreach (RoadTrafficSignals signal in signals)
                {
                    inputs++;
                    if (signal.Plans == null || signal.Plans.Count == 0)
                    {
                        NotEvaluated(results, Descriptor, "SIGNAL_NO_PLAN", "Signal controller has no explicit plan; its fallback timing is not equivalent to authored movement permissions.", signal);
                        continue;
                    }
                    foreach (TrafficSignalPlan plan in signal.Plans)
                    {
                        if (plan == null)
                        {
                            AssetIssue(results, Descriptor, signal, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                                "SIGNAL_NULL_PLAN", "Missing signal plan", "A RoadTrafficSignals owner contains a null plan reference.");
                            continue;
                        }
                        bool matched = false;
                        string failure = string.Empty;
                        foreach (RoadNetwork network in roadNetworks.Distinct())
                        {
                            try
                            {
                                if (plan.Validate(network.Lanes, out string candidate)) { matched = true; break; }
                                failure = candidate;
                            }
                            catch (Exception exception) { failure = ExceptionMessage(exception); }
                        }
                        if (matched) PassAsset(results, Descriptor, plan, "Signal phases and movement permissions match an authoritative lane network.");
                        else if (roadNetworks.Count == 0) NotEvaluated(results, Descriptor, "SIGNAL_NO_NETWORK", "Signal plan cannot be checked until its authoritative road network is in scope.", plan);
                        else AssetIssue(results, Descriptor, plan, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                            "SIGNAL_INVALID", "Signal plan is invalid", string.IsNullOrEmpty(failure) ? "No scoped road network accepted this plan." : failure);
                    }
                }

                PoliceRoadHazard[] hazards = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PoliceRoadHazard>(true)).Where(value => value != null).ToArray();
                foreach (PoliceRoadHazard hazard in hazards)
                {
                    inputs++;
                    ValidateHazard(hazard, scene, results);
                }
            });
            if (inputs == 0) NotEvaluated(results, Descriptor, "TRAFFIC_POLICE_NO_INPUT", "No traffic profiles, signal owners or police hazard sites were included in this scope.");
        }

        private void ValidateHazard(PoliceRoadHazard hazard, Scene scene, WorldValidationResultSink results)
        {
            try
            {
                var serialized = new SerializedObject(hazard);
                SerializedProperty director = serialized.FindProperty("director");
                SerializedProperty content = serialized.FindProperty("physicalContent");
                SerializedProperty level = serialized.FindProperty("minimumLevel");
                SerializedProperty minimum = serialized.FindProperty("minimumDistance");
                SerializedProperty maximum = serialized.FindProperty("maximumDistance");
                SerializedProperty extents = serialized.FindProperty("clearanceHalfExtents");
                bool valid = true;
                if (director == null || director.objectReferenceValue == null)
                {
                    SceneIssue(results, Descriptor, scene, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "POLICE_DIRECTOR", "Police hazard has no pursuit owner", "The deployment site cannot participate in a pursuit without a VehiclePursuitDirector.", hazard.transform.position);
                    valid = false;
                }
                var contentObject = content?.objectReferenceValue as GameObject;
                if (contentObject == null || contentObject == hazard.gameObject || !contentObject.transform.IsChildOf(hazard.transform))
                {
                    SceneIssue(results, Descriptor, scene, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "POLICE_CONTENT", "Police hazard content is not owned by its site", "Physical hazard content must be a dedicated child of the authored deployment site.", hazard.transform.position);
                    valid = false;
                }
                if (level == null || level.intValue < 1 || level.intValue > 5 || minimum == null || maximum == null
                    || !Finite(minimum.floatValue) || !Finite(maximum.floatValue) || minimum.floatValue < 1 || maximum.floatValue <= minimum.floatValue)
                {
                    SceneIssue(results, Descriptor, scene, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "POLICE_DISTANCE", "Police hazard distance policy is invalid", "Minimum/maximum deployment distances must be finite, ordered and within the authored pursuit contract.", hazard.transform.position);
                    valid = false;
                }
                Vector3 size = extents?.vector3Value ?? Vector3.zero;
                if (!Finite(size) || size.x <= 0 || size.y <= 0 || size.z <= 0)
                {
                    SceneIssue(results, Descriptor, scene, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "POLICE_CLEARANCE", "Police hazard clearance is invalid", "Roadblock and spike-strip clearance extents must be finite and positive.", hazard.transform.position);
                    valid = false;
                }
                if (hazard.IsDeployed)
                {
                    SceneIssue(results, Descriptor, scene, WorldValidationStatus.Warning, WorldValidationSeverity.Warning,
                        "POLICE_RUNTIME_STATE", "Police hazard is active in edit mode", "A deployed runtime state is not authored evidence; reset it before judging placement.", hazard.transform.position);
                    valid = false;
                }
                if (valid) PassAsset(results, Descriptor, hazard, "Police site ownership, fairness envelope and deployment bounds are valid.");
            }
            catch (Exception exception)
            {
                AssetIssue(results, Descriptor, hazard, WorldValidationStatus.ErrorRunning, WorldValidationSeverity.Error,
                    "POLICE_VALIDATOR_EXCEPTION", "Police hazard validator failed", ExceptionMessage(exception));
            }
        }
    }

    public sealed class WorldValidationWorldArtRule : WorldValidationRuleBase
    {
        public WorldValidationWorldArtRule()
            : base("world.art.budgets", "World art budgets and generated-object integrity", "Core/WorldArt",
                WorldValidationCategory.WorldArt, WorldValidationCost.Standard,
                Enum.GetValues(typeof(WorldValidationScopeKind)).Cast<WorldValidationScopeKind>(),
                new[] { "Texture2D", "Mesh", "MeshRenderer", "LODGroup", "policy budgets" }) { }

        public override void Evaluate(WorldValidationContext context, WorldValidationResultSink results)
        {
            int assets = 0;
            int textureBytes = Mathf.Max(1, context.Policy != null ? context.Policy.maximumTextureMegabytes : 64) * 1024 * 1024;
            int meshVertices = Mathf.Max(1, context.Policy != null ? context.Policy.maximumMeshVertices : 200000);
            foreach (Texture2D texture in context.FindAssets<Texture2D>().Distinct())
            {
                assets++;
                long estimate = EstimateTextureBytes(texture);
                if (estimate > textureBytes)
                {
                    var result = WorldValidationResult.Failed(Descriptor, "TEXTURE_BUDGET", "Texture exceeds configured budget",
                        texture.name + " is estimated at " + FormatBytes(estimate) + "; configured limit is " + FormatBytes(textureBytes) + ".",
                        WorldValidationSeverity.Warning);
                    result.heuristic = true;
                    result.AddEvidence(WorldValidationEvidenceKind.Metric, "Estimated texture memory", FormatBytes(estimate));
                    results.AddTarget(result, texture);
                }
                else if (context.Request.includeInfo) PassAsset(results, Descriptor, texture, "Estimated texture memory is within the configured budget.");
            }
            foreach (Mesh mesh in context.FindAssets<Mesh>().Distinct())
            {
                assets++;
                if (mesh.vertexCount > meshVertices)
                {
                    var result = WorldValidationResult.Failed(Descriptor, "MESH_VERTEX_BUDGET", "Mesh exceeds configured vertex budget",
                        mesh.name + " has " + mesh.vertexCount + " vertices; configured limit is " + meshVertices + ".",
                        WorldValidationSeverity.Warning);
                    result.heuristic = true;
                    result.AddEvidence(WorldValidationEvidenceKind.Metric, "Vertex count", mesh.vertexCount.ToString());
                    results.AddTarget(result, mesh);
                }
                else if (context.Request.includeInfo) PassAsset(results, Descriptor, mesh, "Mesh vertex count is within the configured budget.");
            }

            int scenes = 0;
            context.InspectScenes(scene =>
            {
                scenes++;
                int objects = 0;
                bool valid = true;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
                    objects += root.GetComponentsInChildren<Transform>(true).Length;
                    foreach (MeshRenderer renderer in renderers)
                    {
                        MeshFilter filter = renderer.GetComponent<MeshFilter>();
                        if (filter == null || filter.sharedMesh == null)
                        {
                            SceneIssue(results, Descriptor, scene, WorldValidationStatus.Failed, WorldValidationSeverity.Error,
                                "ART_RENDERER_MESH", "Renderer has no mesh source", "A MeshRenderer without an owned MeshFilter/shared mesh cannot be rendered reliably.", renderer.transform.position);
                            valid = false;
                        }
                    }
                    foreach (LODGroup lod in root.GetComponentsInChildren<LODGroup>(true))
                    {
                        if (lod.GetLODs() == null || lod.GetLODs().Length == 0)
                        {
                            SceneIssue(results, Descriptor, scene, WorldValidationStatus.Warning, WorldValidationSeverity.Warning,
                                "ART_LOD_EMPTY", "LOD group has no levels", "LODGroup is present but has no authored renderer levels.", lod.transform.position);
                            valid = false;
                        }
                    }
                }
                int objectBudget = context.Policy != null ? context.Policy.maximumLoadedSceneObjects : 250000;
                if (objects > objectBudget)
                {
                    SceneIssue(results, Descriptor, scene, WorldValidationStatus.Warning, WorldValidationSeverity.Warning,
                        "ART_OBJECT_BUDGET", "Scene object count exceeds policy budget", "Inspected " + objects + " objects; configured budget is " + objectBudget + ". This is a static review, not a measured frame-time result.");
                    valid = false;
                }
                if (valid) PassScene(results, Descriptor, scene, "Loaded renderers, LOD metadata and static object budget are within the configured rules.");
            });
            if (assets == 0 && scenes == 0) NotEvaluated(results, Descriptor, "ART_NO_INPUT", "No art assets or scenes were included in this scope.");
        }

        private static long EstimateTextureBytes(Texture2D texture)
        {
            long mipFactor = texture.mipmapCount > 1 ? 4L : 3L;
            return Math.Max(1, texture.width) * (long)Math.Max(1, texture.height) * 4L * mipFactor / 3L;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L) return (bytes / (1024f * 1024f)).ToString("0.0") + " MB";
            return (bytes / 1024f).ToString("0.0") + " KB";
        }
    }

    public sealed class WorldValidationRuntimeScenarioRule : WorldValidationRuleBase
    {
        public WorldValidationRuntimeScenarioRule()
            : base("runtime.scenario.evidence", "Runtime scenario evidence", "Core/Scenario",
                WorldValidationCategory.RuntimeScenario, WorldValidationCost.Expensive,
                Enum.GetValues(typeof(WorldValidationScopeKind)).Cast<WorldValidationScopeKind>(),
                new[] { "scenario provider", "physics/render/audio environment" },
                new[] { "roads.authority", "traffic.police.authoring", "races.routes" }, false,
                "Requires an explicitly registered scenario provider and a suitable graphics/audio environment.") { }

        public override void Evaluate(WorldValidationContext context, WorldValidationResultSink results)
        {
            if (!context.IncludeExpensive)
            {
                NotEvaluated(results, Descriptor, "SCENARIO_NOT_REQUESTED",
                    "Expensive runtime evidence was not requested. Static validation does not prove traffic fairness, vehicle-class traversal, render cost or audio transitions.");
                return;
            }
            Unsupported(results, Descriptor, "SCENARIO_PROVIDER_MISSING",
                "No runtime scenario provider is registered in this project. The dashboard will not fabricate a green result from static metadata.");
        }
    }
}
