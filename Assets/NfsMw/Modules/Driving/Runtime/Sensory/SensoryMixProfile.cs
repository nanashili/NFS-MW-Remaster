using System;
using UnityEngine;
using UnityEngine.Audio;

namespace NfsMwRemaster.Driving
{
    public enum SensoryMixState { FreeRoam, Race, Pursuit, Cooldown, Paused, Crash }
    [Serializable] public sealed class SensoryMixRoute { public SensoryCategory category; public AudioMixerGroup group; }
    [Serializable] public sealed class SensorySnapshot { public SensoryMixState state; public AudioMixerSnapshot snapshot; }
    [CreateAssetMenu(menuName = "NFS MW Remaster/Sensory/Mix")]
    public sealed class SensoryMixProfile : ScriptableObject
    {
        public AudioMixer mixer;
        public SensoryMixRoute[] routes = Array.Empty<SensoryMixRoute>();
        public SensorySnapshot[] snapshots = Array.Empty<SensorySnapshot>();
        public AudioMixerGroup Route(SensoryCategory category)
        { foreach (var route in routes) if (route.category == category) return route.group; return null; }
        public void Transition(SensoryMixState state)
        { foreach (var entry in snapshots) if (entry.state == state && entry.snapshot != null) { entry.snapshot.TransitionTo(0.35f); return; } }
    }
    [Serializable] public sealed class SensoryPreferences
    {
        [Range(0, 1)] public float master = 0.8f, vehicle = 1, effects = 1, music = 0.7f, police = 1, cameraMotion = 0.65f, haptics = 0.6f;
        public bool flashes = true, subtitles = true;
        public void Sanitize()
        {
            master = SensoryMath.Unit(master); vehicle = SensoryMath.Unit(vehicle); effects = SensoryMath.Unit(effects);
            music = SensoryMath.Unit(music); police = SensoryMath.Unit(police); cameraMotion = SensoryMath.Unit(cameraMotion); haptics = SensoryMath.Unit(haptics);
        }
    }
}
