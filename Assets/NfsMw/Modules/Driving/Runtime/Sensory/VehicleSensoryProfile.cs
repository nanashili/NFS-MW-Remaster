using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Serializable] public sealed class EngineRpmRegion
    {
        [Min(1)] public float rpm = 1000;
        public AudioClip onLoad, offLoad;
    }
    public enum EngineLayerKind { Exhaust, Intake, Mechanical, Transmission, Induction }
    [Serializable] public sealed class EngineSoundLayer
    {
        public EngineLayerKind kind;
        [Range(0, 1)] public float gain = 0.7f;
        [Range(0.25f, 1)] public float minimumPitch = 0.65f;
        [Range(1, 3)] public float maximumPitch = 1.6f;
        public Vector3 localPosition;
        public EngineRpmRegion[] regions = Array.Empty<EngineRpmRegion>();
        // Optional versioned analysis data. Empty preserves the established AudioClip loop path.
        public EngineAudioRegion[] nativeRegions = Array.Empty<EngineAudioRegion>();
        public AnimationCurve hoodGain = AnimationCurve.Linear(0, 1, 1, 0.75f);
    }

    [CreateAssetMenu(menuName = "NFS MW Remaster/Sensory/Vehicle")]
    public sealed class VehicleSensoryProfile : ScriptableObject
    {
        public string vehicleIdentity = "Unrecorded reference vehicle";
        public int engineAudioSchema = 1;
        public string engineAudioId = string.Empty;
        public int engineAudioRevision;
        public EngineSoundLayer[] engineLayers = Array.Empty<EngineSoundLayer>();
        public MostWantedVehicleAudio mostWantedAudio;
        public bool HasCompleteAudio => mostWantedAudio != null && mostWantedAudio.schema != 0;
        public AudioClip startup, shiftUp, shiftDown, limiter, overrun, nitrous, wind;
        public AudioClip trafficRoadNoise;
        public SensorySurfaceProfile[] surfaces = Array.Empty<SensorySurfaceProfile>();
        public ParticleSystem nitrousEffect;
        public Vector3[] exhaustPorts = { new Vector3(0, 0.35f, -1.8f) };
        public ImpactMaterialLibrary impactMaterials;
        public AnimationCurve windGain = AnimationCurve.EaseInOut(0, 0, 90, 0.65f);
        public AnimationCurve roadGain = AnimationCurve.EaseInOut(0, 0, 60, 0.55f);
        public AnimationCurve highSpeedEngineGain = AnimationCurve.Linear(0, 1, 1, 0.7f);
        public SensorySurfaceProfile Surface(SensorySurface id)
        {
            foreach (var entry in surfaces) if (entry != null && entry.surface == id) return entry;
            return null;
        }
        public bool Validate(out string failure)
        {
            if (HasCompleteAudio && !mostWantedAudio.Validate(out failure)) return false;
            if (engineLayers == null || engineLayers.Length > 5 || surfaces == null || exhaustPorts == null || exhaustPorts.Length > 4
                || windGain == null || roadGain == null || highSpeedEngineGain == null)
            { failure = "Invalid curves, surfaces, exhaust ports or engine layer budget."; return false; }
            foreach (var layer in engineLayers)
            {
                if (layer == null || layer.hoodGain == null || (layer.regions == null && (layer.nativeRegions == null || layer.nativeRegions.Length == 0)))
                { failure = "Each engine layer requires legacy RPM regions or versioned native regions."; return false; }
                if (layer.regions == null || layer.regions.Length == 0) continue;
                if (layer.regions.Length < 2 || layer.regions.Length > 8)
                { failure = "Each legacy engine layer requires two to eight ordered RPM regions."; return false; }
                float last = 0;
                foreach (var region in layer.regions)
                {
                    if (region == null || !SensoryMath.IsFinite(region.rpm) || region.rpm <= last)
                    { failure = "RPM regions must be finite, positive and strictly increasing."; return false; }
                    last = region.rpm;
                }
                if (layer.nativeRegions != null)
                    foreach (var native in layer.nativeRegions)
                        if (native != null && !native.IsValid) { failure = "Native engine regions must contain valid half-open PCM intervals and anchors."; return false; }
            }
            failure = ""; return true;
        }
    }

    public static class EngineBlend
    {
        public static float Weight(EngineRpmRegion[] regions, int index, float rpm, float load, bool onLoad)
        {
            if (regions == null || regions.Length == 0 || index < 0 || index >= regions.Length || !SensoryMath.IsFinite(rpm)) return 0;
            int lower = 0;
            while (lower < regions.Length - 1 && rpm > regions[lower + 1].rpm) lower++;
            int upper = Mathf.Min(lower + 1, regions.Length - 1);
            float blend = lower == upper ? 0 : Mathf.InverseLerp(regions[lower].rpm, regions[upper].rpm, rpm);
            float region = index == lower ? 1 - blend : index == upper ? blend : 0;
            return Mathf.Sqrt(Mathf.Max(0, region) * (onLoad ? SensoryMath.Unit(load) : 1 - SensoryMath.Unit(load)));
        }
    }
}
