using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum AudioZoneCategory
    {
        OpenStreet,
        EnclosedAlley,
        Tunnel,
        Underpass,
        ParkingStructure,
        IndustrialInterior,
        Garage,
        Frontend,
        Custom
    }

    public enum AudioZoneShape
    {
        Box,
        Sphere,
        Capsule,
        Convex
    }

    public enum AudioZoneListenerPolicy
    {
        RenderedAudioListener,
        ListenerVehicle,
        VehicleThenListener,
        ExplicitTransform
    }

    public enum AudioZoneBlendMode
    {
        WeightedBlend,
        PriorityOverride,
        ExclusiveCategory,
        AdditiveAmbience
    }

    public enum AudioZonePortalState
    {
        Open,
        Closed,
        WorldControlled
    }

    [Serializable]
    public sealed class AudioZoneAmbienceLayer
    {
        public string id = "ambience";
        public AudioClip clip;
        public SensoryCategory category = SensoryCategory.Environment;
        [Range(0, 1)] public float gain = 0.3f;
        [Range(0.25f, 3f)] public float pitch = 1f;
        [Range(0, 256)] public int priority = 210;
        public bool loop = true;
        public Vector3 localPosition;
        [Min(1)] public float maxDistance = 80;
        [Min(0)] public float startDelay;
        [Min(0)] public float cooldown;
    }

    [Serializable]
    public sealed class AudioZoneSourceOverride
    {
        public SensoryCategory category = SensoryCategory.Environment;
        public bool enabled = true;
        [Range(-80, 12)] public float gainDb;
        [Range(20, 22000)] public float lowPassHz = 22000;
        [Range(20, 22000)] public float highPassHz = 20;
        [Range(0, 1)] public float transmission = 1;
    }

    [Serializable]
    public sealed class AudioZoneMixState
    {
        public const int CategoryCount = 9;
        public int schema = 1;
        public string primaryZoneId = string.Empty;
        public string listenerPolicy = string.Empty;
        public Vector3 listenerPosition;
        public float[] categoryGains = new float[CategoryCount];
        public float[] categoryLowPassHz = new float[CategoryCount];
        public float[] categoryHighPassHz = new float[CategoryCount];
        public float reverbWet;
        public float reverbDecay = 1;
        public AudioReverbPreset reverbPreset = AudioReverbPreset.Off;
        public int dominantPriority;
        public bool usingFallback;
        public double evaluatedAt;

        public AudioZoneMixState()
        {
            ResetNeutral();
        }

        public void ResetNeutral()
        {
            schema = 1;
            primaryZoneId = string.Empty;
            listenerPolicy = string.Empty;
            listenerPosition = Vector3.zero;
            reverbWet = 0;
            reverbDecay = 1;
            reverbPreset = AudioReverbPreset.Off;
            dominantPriority = 0;
            usingFallback = true;
            evaluatedAt = 0;
            EnsureArrays();
            for (int i = 0; i < CategoryCount; i++)
            {
                categoryGains[i] = 1;
                categoryLowPassHz[i] = 22000;
                categoryHighPassHz[i] = 20;
            }
        }

        public void EnsureArrays()
        {
            if (categoryGains == null || categoryGains.Length != CategoryCount) categoryGains = NewArray(categoryGains, 1);
            if (categoryLowPassHz == null || categoryLowPassHz.Length != CategoryCount) categoryLowPassHz = NewArray(categoryLowPassHz, 22000);
            if (categoryHighPassHz == null || categoryHighPassHz.Length != CategoryCount) categoryHighPassHz = NewArray(categoryHighPassHz, 20);
        }

        public float Gain(SensoryCategory category)
        {
            EnsureArrays();
            return categoryGains[Mathf.Clamp((int)category, 0, CategoryCount - 1)];
        }

        public float LowPass(SensoryCategory category)
        {
            EnsureArrays();
            return categoryLowPassHz[Mathf.Clamp((int)category, 0, CategoryCount - 1)];
        }

        public float HighPass(SensoryCategory category)
        {
            EnsureArrays();
            return categoryHighPassHz[Mathf.Clamp((int)category, 0, CategoryCount - 1)];
        }

        public AudioZoneMixState Clone()
        {
            var clone = new AudioZoneMixState
            {
                schema = schema,
                primaryZoneId = primaryZoneId,
                listenerPolicy = listenerPolicy,
                listenerPosition = listenerPosition,
                reverbWet = reverbWet,
                reverbDecay = reverbDecay,
                reverbPreset = reverbPreset,
                dominantPriority = dominantPriority,
                usingFallback = usingFallback,
                evaluatedAt = evaluatedAt
            };
            EnsureArrays();
            Array.Copy(categoryGains, clone.categoryGains, CategoryCount);
            Array.Copy(categoryLowPassHz, clone.categoryLowPassHz, CategoryCount);
            Array.Copy(categoryHighPassHz, clone.categoryHighPassHz, CategoryCount);
            return clone;
        }

        private static float[] NewArray(float[] old, float fallback)
        {
            var values = new float[CategoryCount];
            for (int i = 0; i < values.Length; i++) values[i] = old != null && i < old.Length ? old[i] : fallback;
            return values;
        }
    }

    [Serializable]
    public sealed class AudioZoneRuntimeInfluence
    {
        public string zoneId = string.Empty;
        public string profileId = string.Empty;
        public AudioZoneCategory category;
        public int priority;
        public float weight;
        public bool insideCore;
        public bool hysteresisHeld;
    }

    [Serializable]
    public sealed class AudioZoneRuntimeSnapshot
    {
        public int schema = 1;
        public long sample;
        public double time;
        public Vector3 listenerPosition;
        public AudioZoneListenerPolicy listenerPolicy;
        public string primaryZoneId = string.Empty;
        public string transitionReason = string.Empty;
        public float lastObstructionQueryAge;
        public AudioZoneMixState mix = new AudioZoneMixState();
        public AudioZoneRuntimeInfluence[] influences = Array.Empty<AudioZoneRuntimeInfluence>();
    }

    [Serializable]
    public struct AudioZoneMembership
    {
        public string zoneId;
        public AudioZoneProfile profile;
        public float weight;
        public bool insideCore;
        public bool hysteresisHeld;

        public bool IsValid => profile != null && !string.IsNullOrEmpty(zoneId) && weight > 0;
    }

    internal static class AudioZoneStableId
    {
        public static bool IsValid(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 150 || value != value.Trim()) return false;
            foreach (char c in value)
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' || c == ':')) return false;
            return true;
        }

        public static string Create(string prefix, string hint)
        {
            string safe = string.IsNullOrWhiteSpace(hint) ? "item" : hint.Trim();
            var chars = safe.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!(char.IsLetterOrDigit(chars[i]) || chars[i] == '.' || chars[i] == '_' || chars[i] == '-' || chars[i] == ':')) chars[i] = '_';
            return (prefix ?? "audio") + "." + new string(chars) + "." + Guid.NewGuid().ToString("N").Substring(0, 10);
        }
    }
}
