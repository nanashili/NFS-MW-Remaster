using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Runtime owner for acoustic-zone membership, mix arbitration and authored
    /// ambience admission. It deliberately does not own music strategy, police
    /// sensing, user preferences or road topology.
    /// </summary>
    [DefaultExecutionOrder(-180), DisallowMultipleComponent]
    public sealed class AudioZoneWorld : MonoBehaviour
    {
        private const string FallbackId = "<audio-zone-fallback>";

        private struct Candidate
        {
            public AudioZone zone;
            public AudioZoneProfile profile;
            public float weight;
            public bool insideCore;
            public bool hysteresisHeld;
            public int priority;

            public string Id => zone != null ? zone.StableId : FallbackId;
        }

        [SerializeField] private SensoryAudioWorld audioWorld;
        [SerializeField] private AudioZoneListenerPolicy listenerPolicy = AudioZoneListenerPolicy.RenderedAudioListener;
        [SerializeField] private Transform explicitListener;
        [SerializeField] private Transform listenerVehicle;
        [SerializeField] private AudioZoneProfile fallbackProfile;
        [SerializeField, Range(0.01f, 0.5f)] private float sampleInterval = 0.05f;
        [SerializeField, Range(1, 64)] private int maxActiveZones = 24;
        [SerializeField, Range(0, 64)] private int maxAmbienceLayers = 16;
        [SerializeField] private bool manageNativeReverb = true;
        [SerializeField] private AudioReverbZone nativeReverbZone;
        [SerializeField] private bool useExplicitZoneList;
        [SerializeField] private AudioZone[] authoredZones = Array.Empty<AudioZone>();

        private readonly List<AudioZone> zones = new List<AudioZone>();
        private readonly List<AudioZonePortal> portals = new List<AudioZonePortal>();
        private readonly HashSet<AudioZone> seenZones = new HashSet<AudioZone>();
        private readonly List<Candidate> candidates = new List<Candidate>(32);
        private readonly List<AudioZoneRuntimeInfluence> activeInfluences = new List<AudioZoneRuntimeInfluence>(32);
        private readonly Dictionary<string, bool> previousMembership = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, FeedbackVoiceLease> ambienceLeases = new Dictionary<string, FeedbackVoiceLease>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> ambienceNextAllowed = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly HashSet<string> desiredAmbience = new HashSet<string>(StringComparer.Ordinal);
        private AudioZoneRuntimeInfluence[] influenceSnapshot = Array.Empty<AudioZoneRuntimeInfluence>();
        private AudioZoneMixState effectiveMix = new AudioZoneMixState();
        private AudioZoneRuntimeSnapshot snapshot = new AudioZoneRuntimeSnapshot();
        private AudioReverbPreset initialReverbPreset;
        private bool initialReverbEnabled;
        private bool capturedReverb;
        private bool needsRefresh = true;
        private long sample;
        private double nextSample;
        private string previousPrimary = string.Empty;

        public SensoryAudioWorld AudioWorld => audioWorld;
        public AudioZoneListenerPolicy ListenerPolicy => listenerPolicy;
        public AudioZoneProfile FallbackProfile => fallbackProfile;
        public float SampleInterval => sampleInterval;
        public int MaxActiveZones => maxActiveZones;
        public int MaxAmbienceLayers => maxAmbienceLayers;
        public Transform ResolvedListener => ResolveListener();
        public Vector3 ListenerPosition
        {
            get
            {
                var value = ResolveListener();
                return value != null ? value.position : transform.position;
            }
        }
        public string PrimaryZoneId => effectiveMix.primaryZoneId;
        public int ActiveZoneCount => candidates.Count;
        public double LastSampleTime => effectiveMix.evaluatedAt;
        public IReadOnlyList<AudioZoneRuntimeInfluence> ActiveInfluences => activeInfluences;
        public AudioZoneMixState EffectiveMix => effectiveMix.Clone();
        /// <summary>Diagnostic snapshot. Consumers should treat the returned data as read-only.</summary>
        public AudioZoneRuntimeSnapshot RuntimeSnapshot => snapshot;

        private void Reset()
        {
            audioWorld = GetComponent<SensoryAudioWorld>();
            if (audioWorld == null) audioWorld = FindAnyObjectByType<SensoryAudioWorld>();
        }

        private void OnValidate()
        {
            sampleInterval = Mathf.Clamp(SensoryMath.Finite(sampleInterval), 0.01f, 0.5f);
            maxActiveZones = Mathf.Clamp(maxActiveZones, 1, 64);
            maxAmbienceLayers = Mathf.Clamp(maxAmbienceLayers, 0, 64);
            if (authoredZones == null) authoredZones = Array.Empty<AudioZone>();
        }

        private void Awake()
        {
            if (audioWorld == null) audioWorld = GetComponent<SensoryAudioWorld>();
            if (nativeReverbZone != null)
            {
                initialReverbPreset = nativeReverbZone.reverbPreset;
                initialReverbEnabled = nativeReverbZone.enabled;
                capturedReverb = true;
            }
            effectiveMix.ResetNeutral();
            snapshot.mix = effectiveMix;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            needsRefresh = true;
        }

        private void Start()
        {
            if (audioWorld == null) audioWorld = FindAnyObjectByType<SensoryAudioWorld>();
            RefreshNow();
            EvaluateNow();
        }

        private void Update()
        {
            double now = AudioSettings.dspTime;
            if (now < nextSample) return;
            nextSample = now + sampleInterval;
            EvaluateNow();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            ReleaseAmbience();
            if (audioWorld != null) audioWorld.ApplyEnvironmentMix(null);
            RestoreNativeReverb();
        }

        private void OnDestroy() => RestoreNativeReverb();

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => needsRefresh = true;
        private void OnSceneUnloaded(Scene scene) => needsRefresh = true;

        public void Configure(SensoryAudioWorld world, AudioZoneListenerPolicy policy,
            Transform explicitAnchor = null, Transform vehicle = null, AudioZoneProfile fallback = null)
        {
            audioWorld = world;
            listenerPolicy = policy;
            explicitListener = explicitAnchor;
            listenerVehicle = vehicle;
            fallbackProfile = fallback;
            needsRefresh = true;
        }

        public void SetZones(AudioZone[] values)
        {
            authoredZones = values ?? Array.Empty<AudioZone>();
            useExplicitZoneList = true;
            needsRefresh = true;
        }

        public void UseDiscoveredZones()
        {
            useExplicitZoneList = false;
            needsRefresh = true;
        }

        public void SetNativeReverbZone(AudioReverbZone zone, bool manage = true)
        {
            if (nativeReverbZone != null && capturedReverb) RestoreNativeReverb();
            nativeReverbZone = zone;
            manageNativeReverb = manage;
            capturedReverb = false;
            if (nativeReverbZone != null)
            {
                initialReverbPreset = nativeReverbZone.reverbPreset;
                initialReverbEnabled = nativeReverbZone.enabled;
                capturedReverb = true;
            }
        }

        public void RefreshNow()
        {
            zones.Clear();
            portals.Clear();
            seenZones.Clear();
            if (useExplicitZoneList)
            {
                foreach (var zone in authoredZones)
                    if (zone != null && zone.gameObject.scene.IsValid() && zone.gameObject.scene.isLoaded && seenZones.Add(zone)) zones.Add(zone);
            }
            else
            {
                foreach (var zone in FindObjectsByType<AudioZone>(FindObjectsInactive.Include))
                    if (zone != null && zone.gameObject.scene.IsValid() && zone.gameObject.scene.isLoaded && seenZones.Add(zone)) zones.Add(zone);
            }
            foreach (var portal in FindObjectsByType<AudioZonePortal>(FindObjectsInactive.Include))
                if (portal != null && portal.gameObject.scene.IsValid() && portal.gameObject.scene.isLoaded) portals.Add(portal);
            SortZones();
            portals.Sort((a, b) => string.Compare(a.StableId, b.StableId, StringComparison.Ordinal));
            needsRefresh = false;
        }

        public void EvaluateNow()
        {
            if (needsRefresh) RefreshNow();
            Vector3 position = ListenerPosition;
            double now = AudioSettings.dspTime;
            candidates.Clear();
            for (int i = 0; i < zones.Count; i++)
            {
                var zone = zones[i];
                if (zone == null || !zone.isActiveAndEnabled || zone.Profile == null || string.IsNullOrEmpty(zone.StableId)) continue;
                bool wasActive = previousMembership.TryGetValue(zone.StableId, out bool old) && old;
                if (zone.TryEvaluate(position, wasActive, out float weight, out bool insideCore, out bool hysteresisHeld))
                {
                    candidates.Add(new Candidate { zone = zone, profile = zone.Profile, weight = weight,
                        insideCore = insideCore, hysteresisHeld = hysteresisHeld, priority = zone.Profile.Priority });
                    previousMembership[zone.StableId] = true;
                }
                else previousMembership[zone.StableId] = false;
            }
            SortCandidates();
            // Keep the hot path bounded even when an additive/overlapping
            // authoring layout contains more candidates than the runtime tier
            // allows. Sorting is deterministic, so truncation is reproducible.
            if (candidates.Count > maxActiveZones)
                candidates.RemoveRange(maxActiveZones, candidates.Count - maxActiveZones);
            bool usingFallback = candidates.Count == 0;
            if (usingFallback && fallbackProfile != null)
                candidates.Add(new Candidate { profile = fallbackProfile, weight = 1, priority = fallbackProfile.Priority });
            ComposeMix(position, now, usingFallback);
            UpdateAmbience(now);
            UpdateNativeReverb();
            UpdateSnapshot(position, now);
        }

        public AudioZoneMembership[] ResolveMembership(Vector3 position, int limit = 8)
        {
            if (needsRefresh) RefreshNow();
            limit = Mathf.Clamp(limit, 1, 64);
            var result = new List<AudioZoneMembership>(Mathf.Min(limit, zones.Count));
            for (int i = 0; i < zones.Count; i++)
            {
                var zone = zones[i];
                if (zone == null || zone.Profile == null || !zone.EnabledForRuntime) continue;
                bool wasActive = previousMembership.TryGetValue(zone.StableId, out bool old) && old;
                if (!zone.TryEvaluate(position, wasActive, out float weight, out bool insideCore, out bool held)) continue;
                result.Add(new AudioZoneMembership { zoneId = zone.StableId, profile = zone.Profile,
                    weight = weight, insideCore = insideCore, hysteresisHeld = held });
            }
            result.Sort((a, b) => Compare(a.profile, a.weight, a.zoneId, b.profile, b.weight, b.zoneId));
            if (result.Count > limit) result.RemoveRange(limit, result.Count - limit);
            return result.ToArray();
        }

        public bool TryGetPortalTransmission(AudioZone from, AudioZone to, out float transmission, out float lowPassHz)
        {
            transmission = from == to && from != null ? 1 : 0;
            lowPassHz = transmission > 0 ? 22000 : 20;
            if (from == null || to == null || from == to) return from == to && from != null;
            if (needsRefresh) RefreshNow();
            for (int i = 0; i < portals.Count; i++)
            {
                var portal = portals[i];
                if (portal == null || !portal.Connects(from, to)) continue;
                transmission = portal.Transmission;
                lowPassHz = portal.LowPassHz;
                return true;
            }
            return false;
        }

        private void ComposeMix(Vector3 position, double now, bool usingFallback)
        {
            effectiveMix.ResetNeutral();
            effectiveMix.listenerPosition = position;
            effectiveMix.listenerPolicy = listenerPolicy.ToString();
            effectiveMix.evaluatedAt = now;
            effectiveMix.usingFallback = usingFallback;
            if (candidates.Count == 0) return;

            var dominant = candidates[0];
            effectiveMix.primaryZoneId = dominant.Id;
            effectiveMix.dominantPriority = dominant.priority;
            float total = 0;
            for (int i = 0; i < candidates.Count; i++) total += Mathf.Max(0, candidates[i].weight);
            total = Mathf.Max(0.0001f, total);

            for (int categoryIndex = 0; categoryIndex < AudioZoneMixState.CategoryCount; categoryIndex++)
            {
                var category = (SensoryCategory)categoryIndex;
                bool hasWinner = TryPickOverride(category, out Candidate winner);
                if (hasWinner)
                {
                    effectiveMix.categoryGains[categoryIndex] = winner.profile.Gain(category);
                    effectiveMix.categoryLowPassHz[categoryIndex] = winner.profile.LowPass(category);
                    effectiveMix.categoryHighPassHz[categoryIndex] = winner.profile.HighPass(category);
                    continue;
                }
                float gain = 0, low = 0, high = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    var candidate = candidates[i];
                    float weight = Mathf.Max(0, candidate.weight) / total;
                    gain += weight * candidate.profile.Gain(category);
                    low += weight * LogBlend(candidate.profile.LowPass(category));
                    high += weight * LogBlend(candidate.profile.HighPass(category));
                }
                effectiveMix.categoryGains[categoryIndex] = Mathf.Clamp(gain, 0, 8);
                effectiveMix.categoryLowPassHz[categoryIndex] = Mathf.Clamp(Mathf.Exp(low), 20, 22000);
                effectiveMix.categoryHighPassHz[categoryIndex] = Mathf.Clamp(Mathf.Exp(high), 20, 22000);
                if (effectiveMix.categoryHighPassHz[categoryIndex] > effectiveMix.categoryLowPassHz[categoryIndex])
                    effectiveMix.categoryHighPassHz[categoryIndex] = effectiveMix.categoryLowPassHz[categoryIndex];
            }

            bool reverbWinner = TryPickReverbOverride(out Candidate reverb);
            if (reverbWinner)
            {
                effectiveMix.reverbWet = reverb.profile.ReverbWet;
                effectiveMix.reverbDecay = reverb.profile.ReverbDecay;
                effectiveMix.reverbPreset = reverb.profile.ReverbPreset;
            }
            else
            {
                float wet = 0, decayLog = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    float weight = Mathf.Max(0, candidates[i].weight) / total;
                    wet += weight * candidates[i].profile.ReverbWet;
                    decayLog += weight * LogBlend(Mathf.Max(0.1f, candidates[i].profile.ReverbDecay));
                }
                effectiveMix.reverbWet = Mathf.Clamp01(wet);
                effectiveMix.reverbDecay = Mathf.Clamp(Mathf.Exp(decayLog), 0.1f, 20);
                effectiveMix.reverbPreset = dominant.profile.ReverbPreset;
            }
        }

        private bool TryPickOverride(SensoryCategory category, out Candidate winner)
        {
            bool found = false;
            winner = default;
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate.profile == null || !candidate.profile.Affects(category)) continue;
                var mode = candidate.profile.BlendMode;
                if (mode != AudioZoneBlendMode.PriorityOverride && mode != AudioZoneBlendMode.ExclusiveCategory) continue;
                if (!found || Compare(candidate, winner) < 0) { winner = candidate; found = true; }
                if (mode == AudioZoneBlendMode.PriorityOverride) break;
            }
            return found;
        }

        private bool TryPickReverbOverride(out Candidate winner)
        {
            winner = default;
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate.profile == null) continue;
                if (candidate.profile.BlendMode == AudioZoneBlendMode.PriorityOverride
                    || candidate.profile.BlendMode == AudioZoneBlendMode.ExclusiveCategory)
                { winner = candidate; return true; }
            }
            return false;
        }

        private void UpdateAmbience(double now)
        {
            desiredAmbience.Clear();
            int admitted = 0;
            for (int i = 0; i < candidates.Count && admitted < maxAmbienceLayers; i++)
            {
                var candidate = candidates[i];
                if (candidate.profile == null || candidate.weight <= 0) continue;
                var layers = candidate.profile.Ambience;
                for (int layerIndex = 0; layerIndex < layers.Length && admitted < maxAmbienceLayers; layerIndex++)
                {
                    var layer = layers[layerIndex];
                    if (layer == null || layer.clip == null || string.IsNullOrWhiteSpace(layer.id)) continue;
                    string key = candidate.Id + "|" + layer.id;
                    desiredAmbience.Add(key);
                    if (layer.clip.loadState != AudioDataLoadState.Loaded)
                    {
                        // An unloaded clip must not consume the live ambience
                        // budget. This lets a streamed-in layer win admission
                        // without being starved by unavailable content above it.
                        if (ambienceLeases.TryGetValue(key, out var unavailableLease))
                        {
                            if (audioWorld != null) audioWorld.Release(unavailableLease);
                            ambienceLeases.Remove(key);
                        }
                        continue;
                    }
                    admitted++;
                    if (ambienceNextAllowed.TryGetValue(key, out double allowed) && now < allowed && !ambienceLeases.ContainsKey(key)) continue;
                    var anchor = candidate.zone != null ? candidate.zone.transform : null;
                    float gain = Mathf.Clamp01(layer.gain * candidate.weight);
                    if (!ambienceLeases.TryGetValue(key, out var lease) || !audioWorld || !audioWorld.Owns(lease, layer.clip))
                    {
                        if (audioWorld != null) audioWorld.Release(lease);
                        if (audioWorld == null) continue;
                        double scheduled = now + Mathf.Max(0, layer.startDelay);
                        lease = audioWorld.Play(layer.clip, layer.category, anchor, layer.localPosition, gain,
                            Mathf.Clamp(layer.pitch, 0.25f, 3), layer.priority, layer.loop, scheduled);
                        if (!lease.IsValid) continue;
                        ambienceLeases[key] = lease;
                        if (!layer.loop) ambienceNextAllowed[key] = scheduled + layer.clip.length / Mathf.Max(0.25f, layer.pitch) + layer.cooldown;
                    }
                    audioWorld.UpdateVoice(lease, gain, layer.pitch, layer.localPosition);
                    audioWorld.SetVoiceDistance(lease, 4, Mathf.Max(1, layer.maxDistance));
                }
            }
            var stale = ListPool<string>.Get();
            foreach (var item in ambienceLeases)
                if (!desiredAmbience.Contains(item.Key) || audioWorld == null || !audioWorld.Owns(item.Value)) stale.Add(item.Key);
            for (int i = 0; i < stale.Count; i++)
            {
                if (ambienceLeases.TryGetValue(stale[i], out var lease) && audioWorld != null) audioWorld.Release(lease);
                ambienceLeases.Remove(stale[i]);
            }
            ListPool<string>.Release(stale);
        }

        private void ReleaseAmbience()
        {
            if (audioWorld != null)
                foreach (var item in ambienceLeases) audioWorld.Release(item.Value);
            ambienceLeases.Clear();
            ambienceNextAllowed.Clear();
            desiredAmbience.Clear();
        }

        private void UpdateNativeReverb()
        {
            if (!manageNativeReverb || nativeReverbZone == null) return;
            var listener = ResolveListener();
            if (listener != null) nativeReverbZone.transform.position = listener.position;
            bool active = !AudioListener.pause && effectiveMix.reverbPreset != AudioReverbPreset.Off && effectiveMix.reverbWet > 0.001f;
            nativeReverbZone.enabled = active;
            if (active)
            {
                nativeReverbZone.reverbPreset = effectiveMix.reverbPreset;
                nativeReverbZone.minDistance = 0.1f;
                nativeReverbZone.maxDistance = Mathf.Max(2, effectiveMix.reverbDecay * 18);
            }
        }

        private void RestoreNativeReverb()
        {
            if (!capturedReverb || nativeReverbZone == null) return;
            nativeReverbZone.reverbPreset = initialReverbPreset;
            nativeReverbZone.enabled = initialReverbEnabled;
            capturedReverb = false;
        }

        private void UpdateSnapshot(Vector3 position, double now)
        {
            while (activeInfluences.Count < candidates.Count) activeInfluences.Add(new AudioZoneRuntimeInfluence());
            if (activeInfluences.Count > candidates.Count) activeInfluences.RemoveRange(candidates.Count, activeInfluences.Count - candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var influence = activeInfluences[i];
                influence.zoneId = candidate.Id;
                influence.profileId = candidate.profile != null ? candidate.profile.StableId : string.Empty;
                influence.category = candidate.profile != null ? candidate.profile.Category : AudioZoneCategory.Custom;
                influence.priority = candidate.priority;
                influence.weight = candidate.weight;
                influence.insideCore = candidate.insideCore;
                influence.hysteresisHeld = candidate.hysteresisHeld;
            }
            if (influenceSnapshot.Length != activeInfluences.Count) influenceSnapshot = new AudioZoneRuntimeInfluence[activeInfluences.Count];
            for (int i = 0; i < activeInfluences.Count; i++) influenceSnapshot[i] = activeInfluences[i];
            snapshot.schema = 1;
            snapshot.sample = ++sample;
            snapshot.time = now;
            snapshot.listenerPosition = position;
            snapshot.listenerPolicy = listenerPolicy;
            snapshot.primaryZoneId = effectiveMix.primaryZoneId;
            snapshot.transitionReason = previousPrimary == snapshot.primaryZoneId ? "steady" : (string.IsNullOrEmpty(previousPrimary) ? "initial" : "primary-zone-changed");
            snapshot.lastObstructionQueryAge = audioWorld == null || audioWorld.LastObstructionQueryTime <= 0
                ? float.PositiveInfinity : Mathf.Max(0, (float)(now - audioWorld.LastObstructionQueryTime));
            snapshot.mix = effectiveMix;
            snapshot.influences = influenceSnapshot;
            previousPrimary = snapshot.primaryZoneId;
        }

        private Transform ResolveListener()
        {
            switch (listenerPolicy)
            {
                case AudioZoneListenerPolicy.ExplicitTransform: return explicitListener;
                case AudioZoneListenerPolicy.ListenerVehicle: return listenerVehicle;
                case AudioZoneListenerPolicy.VehicleThenListener:
                    return listenerVehicle != null ? listenerVehicle : RenderedListener();
                default: return RenderedListener();
            }
        }

        private Transform RenderedListener()
        {
            if (audioWorld != null && audioWorld.Listener != null) return audioWorld.Listener;
            var listener = FindAnyObjectByType<AudioListener>();
            return listener != null ? listener.transform : null;
        }

        private void SortZones() => zones.Sort((a, b) => string.Compare(a.StableId, b.StableId, StringComparison.Ordinal));

        private void SortCandidates() => candidates.Sort((a, b) => Compare(a, b));

        private static int Compare(Candidate a, Candidate b)
            => Compare(a.profile, a.weight, a.Id, b.profile, b.weight, b.Id);

        private static int Compare(AudioZoneProfile aProfile, float aWeight, string aId,
            AudioZoneProfile bProfile, float bWeight, string bId)
        {
            int result = (bProfile != null ? bProfile.Priority : 0).CompareTo(aProfile != null ? aProfile.Priority : 0);
            if (result != 0) return result;
            result = bWeight.CompareTo(aWeight);
            return result != 0 ? result : string.Compare(aId, bId, StringComparison.Ordinal);
        }

        private static float LogBlend(float value) => Mathf.Log(Mathf.Clamp(SensoryMath.Finite(value), 20, 22000));

        /// <summary>Small allocation-free-enough scratch pool for the bounded stale-key sweep.</summary>
        private static class ListPool<T>
        {
            private static readonly Stack<List<T>> Pool = new Stack<List<T>>();
            public static List<T> Get() => Pool.Count > 0 ? Pool.Pop() : new List<T>(16);
            public static void Release(List<T> list) { list.Clear(); Pool.Push(list); }
        }
    }
}
