using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    // Bounded, GameObject-free OD demand. Arrival clocks do not pause when the physical pool is
    // full. Rejected capacity is counted explicitly; pending requests retain their original seed.
    // This is an origin reservoir, never permission to erase or re-roll an admitted visible trip.
    public sealed class TrafficDemandReservoir
    {
        private readonly float[] rates, remainingHazard;
        private readonly TrafficRandom[] random;
        private readonly int[] seeds, heads, counts;
        private readonly int perFlowCapacity;
        public int FlowCount => rates.Length;
        public long Generated { get; private set; }
        public long Admitted { get; private set; }
        public long SuppressedAtCapacity { get; private set; }
        public int Pending { get; private set; }
        public int ProcessingBacklogRows { get; private set; }

        public TrafficDemandReservoir(float[] vehiclesPerHour, int seed, int maximumPendingPerFlow = 32)
        {
            if (vehiclesPerHour == null || vehiclesPerHour.Length > 4096) throw new ArgumentException("Demand requires at most 4096 OD flows.");
            if (maximumPendingPerFlow < 1 || maximumPendingPerFlow > 256) throw new ArgumentOutOfRangeException(nameof(maximumPendingPerFlow));
            rates = (float[])vehiclesPerHour.Clone(); perFlowCapacity = maximumPendingPerFlow;
            random = new TrafficRandom[rates.Length]; remainingHazard = new float[rates.Length];
            heads = new int[rates.Length]; counts = new int[rates.Length]; seeds = new int[rates.Length * perFlowCapacity];
            for (int i = 0; i < rates.Length; i++)
            {
                if (!TrafficDriver.Finite(rates[i]) || rates[i] < 0 || rates[i] > 3600)
                    throw new ArgumentException("Demand rates must be finite, 0..3600 vehicles/hour per flow.");
                random[i] = new TrafficRandom(unchecked(seed + i * 7919)); remainingHazard[i] = Hazard(ref random[i]);
            }
        }
        public void Advance(float dt, float[] multipliers)
        {
            if (!TrafficDriver.Finite(dt) || dt < 0 || dt > 60) throw new ArgumentOutOfRangeException(nameof(dt));
            if (multipliers == null || multipliers.Length != FlowCount) throw new ArgumentException("One multiplier is required per OD flow.");
            ProcessingBacklogRows = 0;
            for (int i = 0; i < FlowCount; i++)
            {
                float multiplier = multipliers[i];
                if (!TrafficDriver.Finite(multiplier) || multiplier < 0 || multiplier > 100)
                    throw new ArgumentException("Demand multipliers must be finite, 0..100.");
                remainingHazard[i] -= rates[i] * multiplier * dt / 3600;
                int processed = 0;
                while (remainingHazard[i] <= 0 && processed++ < 256)
                {
                    int requestSeed = unchecked((int)random[i].Next()); Generated++;
                    if (counts[i] < perFlowCapacity)
                    {
                        seeds[i * perFlowCapacity + (heads[i] + counts[i]) % perFlowCapacity] = requestSeed;
                        counts[i]++; Pending++;
                    }
                    else SuppressedAtCapacity++;
                    remainingHazard[i] += Hazard(ref random[i]);
                }
                // Keep numerical arrival debt rather than blocking a frame or silently losing it.
                if (remainingHazard[i] <= 0) ProcessingBacklogRows++;
            }
        }
        public int PendingForFlow(int flow) => counts[flow];
        public bool TryPeek(int flow, out int seed)
        {
            seed = 0; if (counts[flow] == 0) return false;
            seed = seeds[flow * perFlowCapacity + heads[flow]]; return true;
        }
        public void CommitAdmission(int flow)
        {
            if (counts[flow] == 0) throw new InvalidOperationException("Cannot admit a request that is not pending.");
            heads[flow] = (heads[flow] + 1) % perFlowCapacity; counts[flow]--; Pending--; Admitted++;
        }
        private static float Hazard(ref TrafficRandom generator) => -Mathf.Log(Mathf.Max(0.000001f, 1 - generator.Unit()));
    }
}
