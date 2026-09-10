using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Serializable]
    public sealed class TrafficSignalPhase
    {
        [Min(0)] public float greenSeconds = 9;
        [Min(0)] public float yellowSeconds = 3;
        [Min(0)] public float allRedSeconds = 2;
        public int[] allowedMovementLaneIds = Array.Empty<int>();

        public IReadOnlyList<int> AllowedMovementLaneIds =>
            allowedMovementLaneIds ?? Array.Empty<int>();
    }

    [CreateAssetMenu(
        menuName = "NFS MW Remaster/Driving/Traffic Signal Plan",
        fileName = "TrafficSignalPlan")]
    public sealed class TrafficSignalPlan : ScriptableObject
    {
        // This is the stable RoadLane.Intersection/node ID, not a compact lane index.
        public int intersectionNodeId = -1;
        public float offsetSeconds;
        public TrafficSignalPhase[] phases = Array.Empty<TrafficSignalPhase>();

        public int IntersectionNodeId => intersectionNodeId;
        public float OffsetSeconds => offsetSeconds;
        public IReadOnlyList<TrafficSignalPhase> Phases =>
            phases ?? Array.Empty<TrafficSignalPhase>();

        public void Configure(int intersection, float offset, TrafficSignalPhase[] configuredPhases)
        {
            intersectionNodeId = intersection;
            offsetSeconds = offset;
            phases = configuredPhases ?? Array.Empty<TrafficSignalPhase>();
        }

        public bool Validate(RoadLaneNetwork network, out string error)
        {
            error = string.Empty;
            if (network == null)
            {
                error = "A road lane network is required.";
                return false;
            }
            if (intersectionNodeId < 0)
            {
                error = "A signal plan requires a non-negative intersection node ID.";
                return false;
            }
            if (!Finite(offsetSeconds))
            {
                error = "Signal plan offset must be finite.";
                return false;
            }

            bool hasMatchingIntersection = false;
            for (int i = 0; i < network.Count; i++)
            {
                if (network[i].Intersection != intersectionNodeId) continue;
                hasMatchingIntersection = true;
                break;
            }
            if (!hasMatchingIntersection)
            {
                error = "Signal plan intersection has no matching lane.";
                return false;
            }

            TrafficSignalPhase[] authoredPhases = phases;
            if (authoredPhases == null || authoredPhases.Length == 0)
            {
                error = "Signal plan requires at least one phase.";
                return false;
            }

            float cycle = 0;
            for (int phaseIndex = 0; phaseIndex < authoredPhases.Length; phaseIndex++)
            {
                TrafficSignalPhase phase = authoredPhases[phaseIndex];
                if (phase == null)
                {
                    error = "Signal plan contains a null phase.";
                    return false;
                }
                if (!ValidDuration(phase.greenSeconds)
                    || !ValidDuration(phase.yellowSeconds)
                    || !ValidDuration(phase.allRedSeconds))
                {
                    error = "Signal phase durations must be finite and non-negative.";
                    return false;
                }
                float phaseDuration = phase.greenSeconds + phase.yellowSeconds + phase.allRedSeconds;
                if (!Finite(phaseDuration) || phaseDuration <= 0)
                {
                    error = "Each signal phase must have a positive duration.";
                    return false;
                }
                cycle += phaseDuration;
                if (!Finite(cycle))
                {
                    error = "Signal plan cycle duration must be finite.";
                    return false;
                }

                int[] movements = phase.allowedMovementLaneIds;
                if (movements == null)
                {
                    error = "Signal phase movement IDs must not be null.";
                    return false;
                }
                if (movements.Length > 0
                    && (phase.greenSeconds <= 0
                        || phase.yellowSeconds <= 0
                        || phase.allRedSeconds <= 0))
                {
                    error = "Movement-bearing signal phases require positive green, yellow, and all-red durations.";
                    return false;
                }
                for (int i = 0; i < movements.Length; i++)
                {
                    int laneIndex = FindLaneIndex(network, movements[i]);
                    if (laneIndex < 0 || network[laneIndex].Intersection != intersectionNodeId)
                    {
                        error = "Signal phase contains an unknown or mismatched movement lane ID.";
                        return false;
                    }
                    for (int j = 0; j < i; j++)
                    {
                        if (movements[j] == movements[i])
                        {
                            error = "Signal phase contains a duplicate movement lane ID.";
                            return false;
                        }
                    }
                }

                // Only movements in this same phase may be simultaneously green.
                for (int i = 0; i < movements.Length; i++)
                {
                    int first = FindLaneIndex(network, movements[i]);
                    for (int j = i + 1; j < movements.Length; j++)
                    {
                        int second = FindLaneIndex(network, movements[j]);
                        if (!network.MovementsConflict(first, second)
                            && !network.MovementsConflict(second, first)) continue;
                        error = "Signal phase allows geometrically conflicting movements.";
                        return false;
                    }
                }
            }

            if (cycle <= 0)
            {
                error = "Signal plan cycle must be greater than zero.";
                return false;
            }
            return true;
        }

        public bool TryValidate(RoadLaneNetwork network, out string error) =>
            Validate(network, out error);

        internal static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        internal static int FindLaneIndex(RoadLaneNetwork network, int stableLaneId)
        {
            for (int i = 0; i < network.Count; i++)
                if (network[i].Id == stableLaneId) return i;
            return -1;
        }

        private static bool ValidDuration(float value) => Finite(value) && value >= 0;
    }
}
