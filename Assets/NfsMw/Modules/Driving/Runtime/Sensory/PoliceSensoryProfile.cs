using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(menuName = "NFS MW Remaster/Sensory/Police")]
    public sealed class PoliceSensoryProfile : ScriptableObject
    {
        public AudioClip[] sirens = Array.Empty<AudioClip>();
        public PoliceRadioCue[] radio = Array.Empty<PoliceRadioCue>();
        public PoliceRadioCue Cue(RadioCueKind kind)
        { foreach (var cue in radio) if (cue != null && cue.kind == kind) return cue; return null; }
    }
}
