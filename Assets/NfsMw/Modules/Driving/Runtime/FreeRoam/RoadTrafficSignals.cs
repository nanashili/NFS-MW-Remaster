using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum RoadSignalAspect { Green, Yellow, Red }
    public sealed class RoadTrafficSignals : MonoBehaviour
    {
        [SerializeField, Min(1)] private float greenSeconds = 9;
        [SerializeField, Min(0.1f)] private float yellowSeconds = 3;
        [SerializeField, Min(0.1f)] private float allRedSeconds = 2;
        [SerializeField] private TrafficSignalPlan[] plans = Array.Empty<TrafficSignalPlan>();
        private readonly Dictionary<int, CachedSignalPlan> movementPlans = new Dictionary<int, CachedSignalPlan>();
        private readonly HashSet<int> invalidIntersections = new HashSet<int>();

        public IReadOnlyList<TrafficSignalPlan> Plans => plans ?? Array.Empty<TrafficSignalPlan>();

        public void Configure(float green, float yellow, float clearance)
        { greenSeconds = Mathf.Max(1, green); yellowSeconds = Mathf.Max(0.1f, yellow); allRedSeconds = Mathf.Max(0.1f, clearance); }

        public void ConfigurePlans(RoadLaneNetwork network, TrafficSignalPlan[] authoredPlans)
        {
            movementPlans.Clear();
            invalidIntersections.Clear();
            if (authoredPlans != null)
                plans = authoredPlans;
            TrafficSignalPlan[] configured = authoredPlans ?? plans ?? Array.Empty<TrafficSignalPlan>();
            for (int i = 0; i < configured.Length; i++)
            {
                TrafficSignalPlan plan = configured[i];
                if (plan == null || plan.IntersectionNodeId < 0) continue;
                int intersection = plan.IntersectionNodeId;
                if (invalidIntersections.Contains(intersection)) continue;
                if (movementPlans.ContainsKey(intersection))
                {
                    movementPlans.Remove(intersection);
                    invalidIntersections.Add(intersection);
                    continue;
                }
                if (network == null || !plan.Validate(network, out _))
                {
                    invalidIntersections.Add(intersection);
                    continue;
                }
                movementPlans.Add(intersection, new CachedSignalPlan(plan));
            }
        }

        public bool TryMovementAspect(int intersection, int movementLaneId, float time, out RoadSignalAspect aspect)
        {
            aspect = RoadSignalAspect.Red;
            if (movementPlans.TryGetValue(intersection, out CachedSignalPlan plan))
            {
                if (!TrafficSignalPlan.Finite(time)) return true;
                if (!plan.TryAspect(movementLaneId, time, out aspect))
                {
                    aspect = RoadSignalAspect.Red;
                }
                return true;
            }
            if (invalidIntersections.Contains(intersection))
            {
                aspect = RoadSignalAspect.Red;
                return true;
            }
            return false;
        }

        public bool IsGreen(Vector3 intersection, Vector3 direction, float time) => Aspect(intersection, direction, time) == RoadSignalAspect.Green;
        public RoadSignalAspect Aspect(Vector3 intersection, Vector3 direction, float time)
        {
            float half = greenSeconds + yellowSeconds + allRedSeconds;
            float phase = Mathf.Repeat(time + Mathf.Abs(intersection.x + intersection.z) * 0.013f
                - (Mathf.Abs(direction.z) > Mathf.Abs(direction.x) ? 0 : half), 2 * half);
            return phase < greenSeconds ? RoadSignalAspect.Green : phase < greenSeconds + yellowSeconds ? RoadSignalAspect.Yellow : RoadSignalAspect.Red;
        }
        public bool RequiresStop(Vector3 position, Vector3 direction, IRoadNetwork network)
        { return StopLineDistance(position, direction, network) < 14; }

        public float StopLineDistance(Vector3 position, Vector3 direction, IRoadNetwork network)
        {
            float nearest = float.PositiveInfinity;
            foreach (RoadNode node in network.Nodes)
            {
                if (node.exits.Length < 3) continue;
                Vector3 delta = node.position - position; delta.y = 0;
                float ahead = Vector3.Dot(delta, direction);
                float lateral = Mathf.Abs(Vector3.Dot(delta, Vector3.Cross(Vector3.up, direction)));
                // Once committed past the stop line, clear the junction even if lights change.
                if (ahead > 8 && ahead < 90 && lateral < 8 && !IsGreen(node.position, direction, Time.time))
                    nearest = Mathf.Min(nearest, Mathf.Max(0, ahead - 10.1f));
            }
            return nearest;
        }

        private sealed class CachedSignalPlan
        {
            private readonly CachedPhase[] phases;
            private readonly float cycleSeconds;
            private readonly float offsetSeconds;
            private readonly Dictionary<int, bool[]> allowedByMovement = new Dictionary<int, bool[]>();

            internal CachedSignalPlan(TrafficSignalPlan plan)
            {
                TrafficSignalPhase[] authored = plan.phases ?? Array.Empty<TrafficSignalPhase>();
                phases = new CachedPhase[authored.Length];
                float cursor = 0;
                for (int i = 0; i < authored.Length; i++)
                {
                    TrafficSignalPhase phase = authored[i];
                    float greenEnd = cursor + phase.greenSeconds;
                    float yellowEnd = greenEnd + phase.yellowSeconds;
                    float end = yellowEnd + phase.allRedSeconds;
                    phases[i] = new CachedPhase { Start = cursor, GreenEnd = greenEnd, YellowEnd = yellowEnd, End = end };
                    for (int j = 0; j < phase.allowedMovementLaneIds.Length; j++)
                    {
                        int movement = phase.allowedMovementLaneIds[j];
                        if (!allowedByMovement.TryGetValue(movement, out bool[] allowed))
                        {
                            allowed = new bool[authored.Length];
                            allowedByMovement.Add(movement, allowed);
                        }
                        allowed[i] = true;
                    }
                    cursor = end;
                }
                cycleSeconds = cursor;
                offsetSeconds = Mathf.Repeat(plan.offsetSeconds, cycleSeconds);
            }

            internal bool TryAspect(int movementLaneId, float time, out RoadSignalAspect aspect)
            {
                aspect = RoadSignalAspect.Red;
                float phaseTime = time - offsetSeconds;
                if (!TrafficSignalPlan.Finite(phaseTime)) return true;
                phaseTime = Mathf.Repeat(phaseTime, cycleSeconds);
                int low = 0, high = phases.Length - 1;
                while (low < high)
                {
                    int middle = (low + high) / 2;
                    if (phaseTime < phases[middle].End) high = middle;
                    else low = middle + 1;
                }
                if (!allowedByMovement.TryGetValue(movementLaneId, out bool[] allowed)
                    || !allowed[low]) return true;
                CachedPhase phase = phases[low];
                aspect = phaseTime < phase.GreenEnd ? RoadSignalAspect.Green
                    : phaseTime < phase.YellowEnd ? RoadSignalAspect.Yellow
                    : RoadSignalAspect.Red;
                return true;
            }
        }

        private struct CachedPhase
        {
            public float Start;
            public float GreenEnd;
            public float YellowEnd;
            public float End;
        }
    }
}
