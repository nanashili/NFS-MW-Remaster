using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public sealed partial class FreeRoamSession
    {
        private ICareerFacts activityFacts;
        private int activityRegistryVersion = -1;
        private readonly Dictionary<string, float> activityDwell = new Dictionary<string, float>();
        public void SetActivityCareerFacts(ICareerFacts facts) => activityFacts = facts;
        public bool ActivityEligible(ActivityMapMarker marker) => marker.Eligible(activityFacts);
        public bool ActivityEligible(WorldActivityInstance instance, out string reason) => instance.Eligible(activityFacts, out reason);
        private bool IsPlacedLocation(IWorldLocation location) => location is WorldLocation component && ActivityRegistry.Owns(component.GetComponent<WorldActivityInstance>());
        private bool InPlacement(WorldActivityInstance instance)
        {
            var r = instance.Data;
            return CanDrive && State == FreeRoamState.Driving && !Pursued && vehicle != null && vehicle.Body != null
                && r.Contains(transform.position) && vehicle.Body.linearVelocity.magnitude <= r.maximumSpeed
                && Vector3.Angle(transform.forward, r.rotation * Vector3.forward) <= r.approachAngle;
        }
        private void TickPlacementDwell() => AdvanceActivityInteractions(Time.deltaTime);
        public void AdvanceActivityInteractions(float deltaTime)
        {
            if (!float.IsFinite(deltaTime) || deltaTime < 0 || deltaTime > 1) throw new System.ArgumentOutOfRangeException(nameof(deltaTime));
            if (activityRegistryVersion != ActivityRegistry.Version) { activityDwell.Clear(); activityRegistryVersion = ActivityRegistry.Version; }
            // No per-frame scans of the asset database or scene hierarchy.
            foreach (var instance in ActivityRegistry.Loaded)
            {
                string id = instance.Data.id;
                activityDwell.TryGetValue(id, out float elapsed);
                activityDwell[id] = InPlacement(instance) && CanDrive ? elapsed + deltaTime : 0;
            }
        }
        private bool CheckPlacedInteraction(WorldActivityInstance instance, out string failure)
        {
            failure = string.Empty;
            if (instance == null) return true; // Existing unbound locations keep their owner policy.
            if (!ActivityRegistry.Owns(instance)) return Reject("Placement is not registered.", out failure);
            if (!instance.Eligible(activityFacts, out failure)) return false;
            if (!InPlacement(instance)) return Reject("Stop inside the entrance, facing the approach direction.", out failure);
            activityDwell.TryGetValue(instance.Data.id, out float dwell);
            if (dwell < instance.Data.dwellSeconds)
                return Reject("Hold still inside the entrance.", out failure);
            return true;
        }
        private bool TryInteractWithPlacement()
        {
            WorldActivityInstance nearest = null; float distance = float.PositiveInfinity;
            foreach (var instance in ActivityRegistry.Loaded)
            {
                if (!instance.Data.Contains(transform.position)) continue;
                float candidate = (instance.Data.interaction - transform.position).sqrMagnitude;
                if (candidate < distance || (candidate == distance && string.CompareOrdinal(instance.Data.id, nearest?.Data.id) < 0))
                { nearest = instance; distance = candidate; }
            }
            if (nearest == null) return false;
            TryActivatePlacement(nearest, out string failure); if (!string.IsNullOrEmpty(failure)) Status = failure;
            return true;
        }
        public bool TryActivatePlacement(WorldActivityInstance instance, out string failure)
        {
            if (instance == null) return Reject("Missing activity.", out failure);
            if (!CheckPlacedInteraction(instance, out failure)) return false;
            switch (instance.Data.adapter)
            {
                case "race": return TryStartEvent(instance.GetComponent<FreeRoamEventDefinition>(), out failure);
                case "service": return TryEnter(instance.GetComponent<WorldLocation>(), out failure);
                case "mission":
                    var host = GetComponent<MissionHost>();
                    if (host == null || instance.Data.mission == null) return Reject("Mission host or definition is unavailable.", out failure);
                    try
                    {
                        var mission = instance.Data.mission.Compile();
                        if (mission.ContentHash != instance.Data.missionFingerprint) return Reject("Mission changed since placement publication. Republish before starting.", out failure);
                        host.StartMission(mission.Id); return true;
                    }
                    catch (System.Exception exception) { return Reject(exception.Message, out failure); }
                default: return Reject("No runtime adapter registered for " + instance.Data.adapter, out failure);
            }
        }
        private bool TryExitPlacedService()
        {
            if (State != FreeRoamState.Location || !(ActiveLocation is WorldLocation location)) return true;
            var instance = location.GetComponent<WorldActivityInstance>();
            if (instance == null || instance.Data == null) return true;
            var r = instance.Data;
            foreach (var ground in r.exits)
            {
                bool clear = true;
                var colliders = new Collider[128];
                int count = gameObject.scene.GetPhysicsScene().OverlapBox(ground + r.rotation * Vector3.up * (r.vehicleSize.y / 2 + .15f), r.vehicleSize / 2, colliders, r.rotation, ~0, QueryTriggerInteraction.Ignore);
                if (count == colliders.Length) clear = false;
                for (int i = 0; i < count; i++)
                    if (vehicle == null || !colliders[i].transform.IsChildOf(vehicle.transform)) { clear = false; break; }
                if (!clear) continue;
                MovePlayer(ground + r.rotation * Vector3.up * .8f, r.rotation); return true;
            }
            Status = "All authored service exits are occupied. Wait before leaving."; return false;
        }
    }
}
