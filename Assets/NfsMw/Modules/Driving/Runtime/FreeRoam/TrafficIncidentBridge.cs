using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    // Authored roadblock deployment feeds the same lane graph. Each source owns its closure leases.
    public sealed class TrafficIncidentBridge : MonoBehaviour
    {
        [SerializeField] private RoadNetwork roads;
        [SerializeField] private PoliceRoadHazard hazard;
        [SerializeField, Min(1)] private float halfWidth = 9;
        private readonly List<IDisposable> leases = new List<IDisposable>();
        private bool deployed;
        private float nextCheck;
        public void Configure(RoadNetwork network, PoliceRoadHazard source) { roads = network; hazard = source; }
        private void Update()
        {
            if (roads == null || hazard == null || Time.time < nextCheck) return;
            nextCheck = Time.time + 0.25f;
            bool active = hazard.IsDeployed && hazard.Kind == PoliceRoadHazardKind.Roadblock;
            if (active == deployed) return; deployed = active;
            if (!active) { Release(); return; }
            for (int i = 0; i < roads.Lanes.Count; i++)
            {
                RoadLane lane = roads.Lanes[i]; if (lane.Intersection >= 0) continue;
                float along = lane.Project(transform.position, out float distance); lane.Sample(along, out Vector3 direction);
                if (distance <= halfWidth && Mathf.Abs(Vector3.Dot(direction, transform.forward)) > 0.7f)
                    leases.Add(roads.Lanes.AcquireClosure(i, along));
            }
        }
        private void Release() { for (int i = 0; i < leases.Count; i++) leases[i].Dispose(); leases.Clear(); }
        private void OnDisable() { Release(); deployed = false; }
    }
}
