using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(menuName = "NFS MW Remaster/HDRP Quality Catalog", fileName = "NFS HDRP Quality Profiles")]
    public sealed class HdrpQualityCatalog : ScriptableObject
    {
        public int schemaVersion = 1;
        public HdrpQualityPreset[] tiers = Array.Empty<HdrpQualityPreset>();

        public HdrpQualityPreset Get(int index)
        {
            if (tiers != null && tiers.Length > 0)
            {
                int safe = Mathf.Clamp(index, 0, tiers.Length - 1);
                if (tiers[safe] != null) return tiers[safe];
            }
            return HdrpQualityPreset.Defaults((HdrpQualityTier)Mathf.Clamp(index, 0, 4));
        }
    }
}
