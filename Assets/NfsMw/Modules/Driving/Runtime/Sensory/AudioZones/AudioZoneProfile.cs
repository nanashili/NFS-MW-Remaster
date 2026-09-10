using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(menuName = "NFS MW Remaster/Sensory/Audio Zone Profile")]
    public sealed class AudioZoneProfile : ScriptableObject
    {
        public const int CurrentSchema = 1;

        [SerializeField] private int schema = CurrentSchema;
        [SerializeField] private string stableId = string.Empty;
        [SerializeField] private string displayName = "Open street";
        [SerializeField] private AudioZoneCategory category = AudioZoneCategory.OpenStreet;
        [SerializeField] private AudioZoneBlendMode blendMode = AudioZoneBlendMode.WeightedBlend;
        [SerializeField, Range(0, 1000)] private int priority;
        [SerializeField, Min(0)] private float blendDistance = 6;
        [SerializeField, Min(0)] private float hysteresis = 0.75f;
        [SerializeField] private AnimationCurve blendCurve = null;
        [SerializeField, Range(-80, 12)] private float gainDb;
        [SerializeField, Range(20, 22000)] private float lowPassHz = 22000;
        [SerializeField, Range(20, 22000)] private float highPassHz = 20;
        [SerializeField, Range(0, 1)] private float reverbWet;
        [SerializeField, Min(0.1f)] private float reverbDecay = 1;
        [SerializeField] private AudioReverbPreset reverbPreset = AudioReverbPreset.Off;
        [SerializeField] private bool affectPlayer = true;
        [SerializeField] private bool affectOtherVehicles = true;
        [SerializeField] private bool affectTires = true;
        [SerializeField] private bool affectImpacts = true;
        [SerializeField] private bool affectEnvironment = true;
        [SerializeField] private bool affectSirens;
        [SerializeField] private bool affectRadio;
        [SerializeField] private bool affectMusic;
        [SerializeField] private bool affectUi;
        [SerializeField] private AudioZoneAmbienceLayer[] ambience = Array.Empty<AudioZoneAmbienceLayer>();
        [SerializeField] private AudioZoneSourceOverride[] sourceOverrides = Array.Empty<AudioZoneSourceOverride>();
        [SerializeField] private string fallbackProfileId = string.Empty;
        [SerializeField, TextArea(2, 5)] private string backendNotes = string.Empty;

        public int Schema => schema;
        public string StableId => stableId;
        public string DisplayName => displayName;
        public AudioZoneCategory Category => category;
        public AudioZoneBlendMode BlendMode => blendMode;
        public int Priority => priority;
        public float BlendDistance => blendDistance;
        public float Hysteresis => hysteresis;
        public AnimationCurve BlendCurve => blendCurve;
        public float GainDb => gainDb;
        public float LowPassHz => lowPassHz;
        public float HighPassHz => highPassHz;
        public float ReverbWet => reverbWet;
        public float ReverbDecay => reverbDecay;
        public AudioReverbPreset ReverbPreset => reverbPreset;
        public string FallbackProfileId => fallbackProfileId;
        public string BackendNotes => backendNotes;
        public AudioZoneAmbienceLayer[] Ambience => ambience ?? Array.Empty<AudioZoneAmbienceLayer>();
        public AudioZoneSourceOverride[] SourceOverrides => sourceOverrides ?? Array.Empty<AudioZoneSourceOverride>();

        public void SetStableId(string value) => stableId = value ?? string.Empty;
        public void SetDisplayName(string value) => displayName = value ?? string.Empty;
        public void SetCategory(AudioZoneCategory value) => category = value;
        public void SetBlendMode(AudioZoneBlendMode value) => blendMode = value;
        public void SetPriority(int value) => priority = Mathf.Clamp(value, 0, 1000);
        public void SetBlend(float distance, float hysteresisValue, AnimationCurve curve)
        {
            blendDistance = Mathf.Max(0, SensoryMath.Finite(distance));
            hysteresis = Mathf.Max(0, SensoryMath.Finite(hysteresisValue));
            blendCurve = curve ?? AnimationCurve.EaseInOut(0, 0, 1, 1);
        }
        public void SetAcoustics(float gain, float lowPass, float highPass, float wet, float decay, AudioReverbPreset preset)
        {
            gainDb = Mathf.Clamp(SensoryMath.Finite(gain), -80, 12);
            lowPassHz = Mathf.Clamp(SensoryMath.Finite(lowPass), 20, 22000);
            highPassHz = Mathf.Clamp(SensoryMath.Finite(highPass), 20, lowPassHz);
            reverbWet = Mathf.Clamp01(SensoryMath.Finite(wet));
            reverbDecay = Mathf.Clamp(SensoryMath.Finite(decay), 0.1f, 20);
            reverbPreset = preset;
        }
        public void SetAmbience(AudioZoneAmbienceLayer[] layers) => ambience = layers ?? Array.Empty<AudioZoneAmbienceLayer>();
        public void SetSourceOverrides(AudioZoneSourceOverride[] overrides) => sourceOverrides = overrides ?? Array.Empty<AudioZoneSourceOverride>();
        public void SetDefaults(AudioZoneCategory value, string id)
        {
            schema = CurrentSchema;
            stableId = id ?? string.Empty;
            category = value;
            displayName = value.ToString();
            blendMode = value == AudioZoneCategory.OpenStreet ? AudioZoneBlendMode.WeightedBlend : AudioZoneBlendMode.PriorityOverride;
            priority = value == AudioZoneCategory.OpenStreet ? 0 : 10;
            blendDistance = value == AudioZoneCategory.Tunnel || value == AudioZoneCategory.Underpass ? 9 : 5;
            hysteresis = 0.75f;
            blendCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
            gainDb = 0;
            lowPassHz = value == AudioZoneCategory.OpenStreet ? 22000 : value == AudioZoneCategory.Garage ? 6500 : 11000;
            highPassHz = 20;
            reverbWet = value == AudioZoneCategory.OpenStreet ? 0 : 0.35f;
            reverbDecay = value == AudioZoneCategory.Tunnel ? 2.4f : value == AudioZoneCategory.Garage ? 1.6f : 1.2f;
            reverbPreset = value == AudioZoneCategory.Tunnel ? AudioReverbPreset.Cave : value == AudioZoneCategory.Garage ? AudioReverbPreset.ParkingLot : AudioReverbPreset.Off;
            ambience = Array.Empty<AudioZoneAmbienceLayer>();
            sourceOverrides = Array.Empty<AudioZoneSourceOverride>();
        }

        public float Curve(float normalizedDistance)
        {
            if (blendCurve == null || blendCurve.length == 0) return Mathf.Clamp01(normalizedDistance);
            return Mathf.Clamp01(blendCurve.Evaluate(Mathf.Clamp01(normalizedDistance)));
        }

        public float Gain(SensoryCategory source)
        {
            if (!Affects(source)) return 1;
            float db = gainDb;
            foreach (var entry in SourceOverrides)
                if (entry != null && entry.enabled && entry.category == source) db += entry.gainDb;
            return DbToLinear(db);
        }

        public float LowPass(SensoryCategory source)
        {
            if (!Affects(source)) return 22000;
            float value = lowPassHz;
            foreach (var entry in SourceOverrides)
                if (entry != null && entry.enabled && entry.category == source) value = Mathf.Min(value, Mathf.Clamp(entry.lowPassHz, 20, 22000));
            return Mathf.Clamp(value, 20, 22000);
        }

        public float HighPass(SensoryCategory source)
        {
            if (!Affects(source)) return 20;
            float value = highPassHz;
            foreach (var entry in SourceOverrides)
                if (entry != null && entry.enabled && entry.category == source) value = Mathf.Max(value, Mathf.Clamp(entry.highPassHz, 20, 22000));
            return Mathf.Clamp(value, 20, 22000);
        }

        public bool Affects(SensoryCategory source)
        {
            switch (source)
            {
                case SensoryCategory.Player: return affectPlayer;
                case SensoryCategory.OtherVehicle: return affectOtherVehicles;
                case SensoryCategory.Tires: return affectTires;
                case SensoryCategory.Impacts: return affectImpacts;
                case SensoryCategory.Environment: return affectEnvironment;
                case SensoryCategory.Sirens: return affectSirens;
                case SensoryCategory.Radio: return affectRadio;
                case SensoryCategory.Music: return affectMusic;
                case SensoryCategory.UI: return affectUi;
                default: return false;
            }
        }

        public bool Validate(out string failure)
        {
            var failures = new List<string>();
            if (schema != CurrentSchema) failures.Add("schema " + schema + " is unsupported");
            if (!AudioZoneStableId.IsValid(stableId)) failures.Add("stable ID is empty or contains unsupported characters");
            if (string.IsNullOrWhiteSpace(displayName)) failures.Add("display name is empty");
            if (blendDistance < 0 || float.IsNaN(blendDistance) || float.IsInfinity(blendDistance)) failures.Add("blend distance is invalid");
            if (hysteresis < 0 || float.IsNaN(hysteresis) || float.IsInfinity(hysteresis)) failures.Add("hysteresis is invalid");
            if (blendCurve == null || blendCurve.length == 0) failures.Add("blend curve is missing");
            if (lowPassHz < 20 || highPassHz < 20 || lowPassHz > 22000 || highPassHz > 22000) failures.Add("filter frequency is outside 20-22000 Hz");
            if (highPassHz > lowPassHz) failures.Add("high-pass frequency exceeds low-pass frequency");
            if (!SensoryMath.IsFinite(gainDb) || !SensoryMath.IsFinite(lowPassHz) || !SensoryMath.IsFinite(highPassHz)
                || reverbWet < 0 || reverbWet > 1 || !SensoryMath.IsFinite(reverbWet)
                || reverbDecay <= 0 || !SensoryMath.IsFinite(reverbDecay)) failures.Add("gain, filter or reverb values are invalid");
            var layerIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var layer in Ambience)
            {
                if (layer == null) { failures.Add("ambience contains a null layer"); continue; }
                if (string.IsNullOrWhiteSpace(layer.id) || !layerIds.Add(layer.id)) failures.Add("ambience layer IDs must be unique and non-empty");
                if (!SensoryMath.IsFinite(layer.gain) || !SensoryMath.IsFinite(layer.pitch) || !SensoryMath.IsFinite(layer.maxDistance)
                    || layer.gain < 0 || layer.gain > 1 || layer.pitch < 0.25f || layer.pitch > 3 || layer.maxDistance <= 0
                    || !SensoryMath.IsFinite(layer.startDelay) || !SensoryMath.IsFinite(layer.cooldown)
                    || layer.startDelay < 0 || layer.cooldown < 0)
                    failures.Add("ambience layer has invalid gain, pitch, distance or timing: " + layer.id);
            }
            var overrideKeys = new HashSet<SensoryCategory>();
            foreach (var entry in SourceOverrides)
            {
                if (entry == null) { failures.Add("source overrides contain a null entry"); continue; }
                if (!overrideKeys.Add(entry.category)) failures.Add("source override category is duplicated: " + entry.category);
                if (!SensoryMath.IsFinite(entry.gainDb) || !SensoryMath.IsFinite(entry.lowPassHz) || !SensoryMath.IsFinite(entry.highPassHz)
                    || !SensoryMath.IsFinite(entry.transmission) || entry.transmission < 0 || entry.transmission > 1
                    || entry.lowPassHz < 20 || entry.lowPassHz > 22000 || entry.highPassHz < 20 || entry.highPassHz > 22000 || entry.highPassHz > entry.lowPassHz)
                    failures.Add("source override filter is invalid: " + entry.category);
            }
            failure = string.Join("; ", failures);
            return failures.Count == 0;
        }

        private static float DbToLinear(float db) => Mathf.Pow(10, Mathf.Clamp(db, -80, 12) / 20f);
    }
}
