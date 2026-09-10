using System;
using UnityEngine;
namespace NfsMwRemaster.Lighting
{
    [Serializable] public sealed class BakedReflection
    {
        public string probeId;
        public Cubemap texture;
    }
    public sealed class LightingBakeSet : ScriptableObject
    {
        public int schemaVersion=1;
        public string id=Guid.NewGuid().ToString("N"), sourceFingerprint, profileRevision, createdUtc, scenePath;
        public BakedReflection[] reflections=Array.Empty<BakedReflection>();
    }
}
