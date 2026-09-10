using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Serializable]
    public sealed class TrafficDriverDistribution
    {
        [Min(0)] public float weight = 1;
        [Range(0, 0.2f)] public float correlatedVariation = 0.08f;
        public TrafficDriverProfile median = new TrafficDriverProfile();
        public TrafficDriverDistribution() { }
        public TrafficDriverDistribution(float share, float temperament)
        { weight = share; median.ApplyTemperament(temperament); }
    }

    [CreateAssetMenu(menuName = "NFS MW Remaster/Traffic/Driver Population")]
    public sealed class TrafficDriverPopulationProfile : ScriptableObject
    {
        public TrafficDriverDistribution[] distributions = {
            new TrafficDriverDistribution(20, 0.1f), new TrafficDriverDistribution(60, 0.5f),
            new TrafficDriverDistribution(17, 0.88f), new TrafficDriverDistribution(3, 0.985f) };

        // Copy into per-trip state: neither sampling nor vehicle caps mutate the shared asset.
        public void SampleInto(int seed, TrafficDriverProfile destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            float total = 0;
            if (distributions != null) foreach (var row in distributions)
                if (row?.median != null && TrafficDriver.Finite(row.weight) && row.weight > 0) total += row.weight;
            if (!TrafficDriver.Finite(total) || total <= 0) { destination.ApplySeed(seed); return; }
            var random = new TrafficRandom(seed); float draw = random.Unit() * total;
            foreach (var row in distributions)
            {
                if (row?.median == null || !TrafficDriver.Finite(row.weight) || row.weight <= 0) continue;
                draw -= row.weight; if (draw > 0) continue;
                destination.CopyFrom(row.median);
                float variation = TrafficDriver.Finite(row.correlatedVariation) ? Mathf.Clamp(row.correlatedVariation, 0, 0.2f) : 0;
                destination.Vary((random.Unit() * 2 - 1) * variation);
                destination.lateralPreference = (random.Unit() * 2 - 1) * 0.2f;
                return;
            }
            destination.ApplySeed(seed);
        }
    }
}
