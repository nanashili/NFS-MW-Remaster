using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Serializable] public sealed class ImpactMaterialPair
    {
        public SensorySurface a = SensorySurface.Metal, b = SensorySurface.Concrete;
        public AudioClip light, heavy, scrape;
        public ParticleSystem particles;
        [Range(0, 1)] public float heavyThreshold = 0.45f;
        public bool Matches(SensorySurface first, SensorySurface second) => a == first && b == second || a == second && b == first;
    }
    [CreateAssetMenu(menuName = "NFS MW Remaster/Sensory/Impact material pairs")]
    public sealed class ImpactMaterialLibrary : ScriptableObject
    {
        public ImpactMaterialPair[] pairs = Array.Empty<ImpactMaterialPair>();
        public ImpactMaterialPair Find(SensorySurface first, SensorySurface second)
        { foreach (var pair in pairs) if (pair != null && pair.Matches(first, second)) return pair; return null; }
        public bool Validate(out string failure)
        {
            if (pairs == null || pairs.Length > 64) { failure = "Material table exceeds its 64-pair authoring budget."; return false; }
            for (int i = 0; i < pairs.Length; i++)
            {
                if (pairs[i] == null) { failure = "Null material pair."; return false; }
                for (int j = 0; j < i; j++)
                    if (pairs[j].Matches(pairs[i].a, pairs[i].b)) { failure = "Duplicate symmetric material pair."; return false; }
            }
            failure = ""; return true;
        }
    }
}
