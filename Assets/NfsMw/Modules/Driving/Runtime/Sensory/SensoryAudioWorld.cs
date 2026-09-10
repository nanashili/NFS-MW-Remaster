using System;
using Unity.Profiling;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Scene-owned voice pool. All output, including music and radio, crosses this admission interface.</summary>
    [DefaultExecutionOrder(-200), DisallowMultipleComponent]
    public sealed class SensoryAudioWorld : MonoBehaviour
    {
        private sealed class Voice
        {
            public AudioSource source;
            public NativeEngineVoiceFilter nativeFilter;
            public AudioLowPassFilter lowPass;
            public AudioHighPassFilter highPass;
            public FeedbackVoiceLease lease;
            public Transform anchor;
            public Vector3 offset;
            public SensoryCategory category;
            public double expiry;
            public bool unscaledLifetime;
            public double unscaledStart;
            public float targetGain, gain, occlusion, fixedLowPass, fixedHighPass;
        }
        [SerializeField, Range(1, 32)] private int voiceLimit = 28;
        [SerializeField, Range(0, 4)] private int occlusionRays = 2;
        [SerializeField] private Transform listener, listenerVehicle;
        [SerializeField] private SensoryMixProfile mix;
        [SerializeField] private SensoryPreferences preferences = new SensoryPreferences();
        private Voice[] voices;
        private FeedbackVoiceBudget budget;
        private int occlusionCursor;
        private double nextOcclusion, radioUntil;
        private float musicDuck = 1;
        private SensoryMixState mixState;
        private AudioZoneMixState environmentMix = new AudioZoneMixState();
        private readonly RaycastHit[] hits = new RaycastHit[16];
        private readonly bool[] muted = new bool[9];
        private AudioClip nativeSilence;
        private static readonly ProfilerMarker Marker = new ProfilerMarker("Sensory.AudioVoices");
        public int ActiveVoices => budget?.Count ?? 0;
        public int VoiceLimit => voiceLimit;
        public int DroppedVoices { get; private set; }
        public SensoryPreferences Preferences => preferences;
        public SensoryMixState MixState => mixState;
        /// <summary>Last bounded obstruction query timestamp, useful to explain stale acoustic diagnostics.</summary>
        public double LastObstructionQueryTime { get; private set; }
        /// <summary>Environment-only mix owned by AudioZoneWorld. User/master/music preferences stay here.</summary>
        public AudioZoneMixState EnvironmentMix => environmentMix.Clone();
        public Transform Listener => listener;
        public void Configure(Transform view, Transform vehicle, SensoryMixProfile profile)
        { listener = view; listenerVehicle = vehicle; mix = profile; }
        private void Awake()
        {
            voiceLimit = Mathf.Clamp(voiceLimit, 1, 32); occlusionRays = Mathf.Clamp(occlusionRays, 0, 4);
            if (preferences == null) preferences = new SensoryPreferences();
            budget = new FeedbackVoiceBudget(Mathf.Clamp(voiceLimit, 1, 32));
            voices = new Voice[budget.Capacity];
            for (int i = 0; i < voices.Length; i++)
            {
                var root = new GameObject("Sensory Voice " + i); root.transform.SetParent(transform, false);
                var source = root.AddComponent<AudioSource>(); source.playOnAwake = false;
                var nativeFilter = root.AddComponent<NativeEngineVoiceFilter>();
                var low = root.AddComponent<AudioLowPassFilter>(); low.enabled = false;
                var high = root.AddComponent<AudioHighPassFilter>(); high.enabled = false;
                voices[i] = new Voice { source = source, nativeFilter = nativeFilter, lowPass = low, highPass = high };
            }
            nativeSilence = AudioClip.Create("Sensory Native Silence", 256, 1, AudioSettings.outputSampleRate, true);
            AudioSettings.OnAudioConfigurationChanged += AudioDeviceChanged;
        }
        private void Start()
        {
            if (PlayerPrefs.HasKey("sensory.preferences.v1"))
            {
                try { JsonUtility.FromJsonOverwrite(PlayerPrefs.GetString("sensory.preferences.v1"), preferences); }
                catch (ArgumentException) { preferences = new SensoryPreferences(); }
            }
            ApplyPreferences(false);
        }
        public void ApplyPreferences(bool save)
        {
            preferences.Sanitize();
            if (mix != null && mix.mixer != null)
            {
                mix.mixer.SetFloat("MasterVolume", Decibels(preferences.master));
                mix.mixer.SetFloat("VehicleVolume", Decibels(preferences.vehicle));
                mix.mixer.SetFloat("EffectsVolume", Decibels(preferences.effects));
                mix.mixer.SetFloat("MusicVolume", Decibels(preferences.music));
                mix.mixer.SetFloat("PoliceVolume", Decibels(preferences.police));
            }
            if (save) { PlayerPrefs.SetString("sensory.preferences.v1", JsonUtility.ToJson(preferences)); PlayerPrefs.Save(); }
        }
        public void Mute(SensoryCategory category, bool value) { muted[(int)category] = value; }
        public bool IsMuted(SensoryCategory category) => muted[(int)category];
        public void SetMix(SensoryMixState state)
        { if (mixState == state) return; mixState = state; if (mix != null) mix.Transition(state); }
        public void ApplyEnvironmentMix(AudioZoneMixState state)
        {
            if (state == null)
            {
                environmentMix.ResetNeutral();
                return;
            }
            state.EnsureArrays();
            environmentMix.schema = state.schema;
            environmentMix.primaryZoneId = state.primaryZoneId ?? string.Empty;
            environmentMix.listenerPolicy = state.listenerPolicy ?? string.Empty;
            environmentMix.listenerPosition = state.listenerPosition;
            environmentMix.reverbWet = Mathf.Clamp01(SensoryMath.Finite(state.reverbWet));
            environmentMix.reverbDecay = Mathf.Max(0.1f, SensoryMath.Finite(state.reverbDecay));
            environmentMix.reverbPreset = state.reverbPreset;
            environmentMix.dominantPriority = state.dominantPriority;
            environmentMix.usingFallback = state.usingFallback;
            environmentMix.evaluatedAt = state.evaluatedAt;
            environmentMix.EnsureArrays();
            for (int i = 0; i < AudioZoneMixState.CategoryCount; i++)
            {
                environmentMix.categoryGains[i] = Mathf.Clamp(SensoryMath.Finite(state.categoryGains[i]), 0, 8);
                environmentMix.categoryLowPassHz[i] = Mathf.Clamp(SensoryMath.Finite(state.categoryLowPassHz[i]), 20, 22000);
                environmentMix.categoryHighPassHz[i] = Mathf.Clamp(SensoryMath.Finite(state.categoryHighPassHz[i]), 20, 22000);
                if (environmentMix.categoryHighPassHz[i] > environmentMix.categoryLowPassHz[i])
                    environmentMix.categoryHighPassHz[i] = environmentMix.categoryLowPassHz[i];
            }
        }
        public bool Owns(FeedbackVoiceLease lease) => budget != null && budget.Owns(lease);
        public bool Owns(FeedbackVoiceLease lease, AudioClip clip) => Owns(lease) && voices[lease.Index].source.clip == clip;
        public FeedbackVoiceLease Play(AudioClip clip, SensoryCategory category, Transform anchor, Vector3 position,
            float gain, float pitch, int priority, bool loop = false, double scheduled = 0, float phase = 0,
            bool ignoreListenerPause = false, bool scheduleOnUnscaledClock = false)
        {
            if (!isActiveAndEnabled || budget == null || clip == null || clip.loadState != AudioDataLoadState.Loaded) return default;
            // Unity freezes DSP scheduling with AudioListener.pause. Frontend music
            // therefore supplies an unscaled deadline and starts with Play when due.
            if (ignoreListenerPause && category != SensoryCategory.Music && category != SensoryCategory.UI) return default;
            if (scheduleOnUnscaledClock && !ignoreListenerPause) return default;
            if (ignoreListenerPause && scheduled != 0 && !scheduleOnUnscaledClock) return default;
            Vector3 world = anchor != null ? anchor.TransformPoint(position) : position;
            bool spatial = category != SensoryCategory.Music && category != SensoryCategory.Radio && category != SensoryCategory.UI;
            if (spatial && listener != null && (world - listener.position).sqrMagnitude > 180 * 180) return default;
            var lease = budget.Acquire(priority);
            if (!lease.IsValid) { DroppedVoices++; return default; }
            var voice = voices[lease.Index]; ResetVoice(voice);
            voice.lease = lease; voice.anchor = anchor; voice.offset = position; voice.category = category;
            voice.targetGain = SensoryMath.Unit(gain); voice.gain = loop ? 0 : voice.targetGain;
            pitch = Mathf.Clamp(SensoryMath.Finite(pitch), 0.25f, 3);
            if (double.IsNaN(scheduled) || double.IsInfinity(scheduled)) scheduled = 0;
            voice.unscaledLifetime = ignoreListenerPause;
            voice.unscaledStart = scheduleOnUnscaledClock ? scheduled : 0;
            voice.expiry = Math.Max(ignoreListenerPause ? Time.unscaledTimeAsDouble : AudioSettings.dspTime, scheduled)
                + (loop ? 1.5 : clip.length / pitch + 0.1);
            var source = voice.source; source.transform.position = world; source.clip = clip; source.loop = loop;
            source.ignoreListenerPause = ignoreListenerPause;
            source.pitch = Mathf.Clamp(SensoryMath.Finite(pitch), 0.25f, 3);
            source.priority = Mathf.Clamp(priority, 0, 256); source.spatialBlend = spatial ? 1 : 0;
            source.minDistance = category == SensoryCategory.Sirens ? 8 : 4; source.maxDistance = 180;
            source.rolloffMode = AudioRolloffMode.Logarithmic; source.dopplerLevel = spatial ? 0.35f : 0;
            source.outputAudioMixerGroup = mix != null ? mix.Route(category) : null;
            voice.fixedLowPass = category == SensoryCategory.Radio ? 3400 : 22000;
            voice.fixedHighPass = category == SensoryCategory.Radio ? 350 : 20;
            if (category == SensoryCategory.Radio)
            {
                voice.highPass.enabled = true; voice.highPass.cutoffFrequency = 350;
                voice.lowPass.enabled = true; voice.lowPass.cutoffFrequency = 3400;
            }
            phase = SensoryMath.Finite(phase);
            if (loop && phase > 0 && clip.samples > 0) source.timeSamples = (int)(Mathf.Repeat(phase, 1) * (clip.samples - 1));
            ApplyVoiceFilters(voice);
            source.volume = muted[(int)category] ? 0 : voice.gain * FallbackGain(category) * EnvironmentGain(category);
            if (scheduleOnUnscaledClock)
            {
                if (scheduled <= Time.unscaledTimeAsDouble) { source.Play(); voice.unscaledStart = 0; }
            }
            else if (scheduled > AudioSettings.dspTime) source.PlayScheduled(scheduled); else source.Play();
            return lease;
        }
        /// <summary>Admits a native renderer through this world's existing pooled source and mixer route.</summary>
        public FeedbackVoiceLease PlayNative(IProceduralVehicleAudio renderer, SensoryCategory category, Transform anchor,
            Vector3 position, float gain, int priority)
        {
            if (!isActiveAndEnabled || budget == null || renderer == null || !renderer.IsReady) return default;
            var lease = budget.Acquire(priority); if (!lease.IsValid) { DroppedVoices++; return default; }
            var voice = voices[lease.Index]; ResetVoice(voice); voice.lease = lease; voice.anchor = anchor; voice.offset = position; voice.category = category;
            voice.nativeFilter.Configure(renderer, AudioSettings.outputSampleRate); voice.targetGain = voice.gain = SensoryMath.Unit(gain);
            var source = voice.source; source.clip = nativeSilence; source.loop = true; source.spatialBlend = category == SensoryCategory.Music ? 0 : 1;
            source.transform.position = anchor != null ? anchor.TransformPoint(position) : position; source.priority = Mathf.Clamp(priority, 0, 256);
            source.outputAudioMixerGroup = mix != null ? mix.Route(category) : null; source.volume = voice.gain * FallbackGain(category) * EnvironmentGain(category); source.Play();
            voice.expiry = double.MaxValue; return lease;
        }
        public void UpdateVoice(FeedbackVoiceLease lease, float gain, float pitch = 1, Vector3? localPosition = null)
        {
            if (!Owns(lease)) return;
            var voice = voices[lease.Index]; voice.targetGain = SensoryMath.Unit(gain);
            if (localPosition.HasValue) voice.offset = localPosition.Value;
            voice.source.pitch = Mathf.Clamp(SensoryMath.Finite(pitch), 0.25f, 3);
            if (voice.source.loop) voice.expiry = Math.Max(voice.expiry,
                (voice.unscaledLifetime ? Time.unscaledTimeAsDouble : AudioSettings.dspTime) + 1.5);
        }
        public void SetVoiceDistance(FeedbackVoiceLease lease, float minDistance, float maxDistance)
        {
            if (!Owns(lease)) return;
            var source = voices[lease.Index].source;
            source.minDistance = Mathf.Max(0.01f, SensoryMath.Finite(minDistance));
            source.maxDistance = Mathf.Max(source.minDistance, SensoryMath.Finite(maxDistance));
        }
        public void Release(FeedbackVoiceLease lease)
        {
            if (!Owns(lease)) return;
            ResetVoice(voices[lease.Index]); budget.Release(lease);
        }
        public void DuckForRadio(float duration) { radioUntil = Math.Max(radioUntil, AudioSettings.dspTime + duration); }
        private void Update()
        {
            using (Marker.Auto())
            {
                double now = AudioSettings.dspTime;
                musicDuck = SensoryMath.Envelope(musicDuck, now < radioUntil ? 0.35f : 1, Time.unscaledDeltaTime, 0.08f, 0.4f);
                foreach (var voice in voices)
                {
                    if (!Owns(voice.lease)) continue;
                    if (voice.unscaledStart > 0 && Time.unscaledTimeAsDouble >= voice.unscaledStart)
                    {
                        voice.source.Play(); voice.unscaledStart = 0;
                        // Loading a menu can stall the first frame beyond the scheduled
                        // deadline. Lifetime starts when the source actually starts.
                        voice.expiry = Time.unscaledTimeAsDouble + (voice.source.loop ? 1.5
                            : voice.source.clip.length / voice.source.pitch + .1);
                    }
                    if (voice.source.spatialBlend > 0.5f && listener != null && voice.anchor != null && (voice.anchor.position - listener.position).sqrMagnitude > 190 * 190)
                    { Release(voice.lease); continue; }
                    if ((voice.unscaledLifetime ? Time.unscaledTimeAsDouble : now) >= voice.expiry
                        || voice.anchor != null && !voice.anchor.gameObject.activeInHierarchy)
                    { Release(voice.lease); continue; }
                    if (voice.anchor != null) voice.source.transform.position = voice.anchor.TransformPoint(voice.offset);
                    voice.gain = SensoryMath.Envelope(voice.gain, voice.targetGain, Time.unscaledDeltaTime, 0.04f, 0.15f);
                    ApplyVoiceFilters(voice);
                    voice.source.volume = muted[(int)voice.category] ? 0 : voice.gain * FallbackGain(voice.category) * EnvironmentGain(voice.category)
                        * (voice.category == SensoryCategory.Music ? musicDuck : 1);
                }
                if (listener != null && now >= nextOcclusion && !AudioListener.pause)
                {
                    nextOcclusion = now + 0.05;
                    for (int i = 0; i < occlusionRays; i++) Occlude(voices[occlusionCursor++ % voices.Length]);
                }
            }
        }
        private void Occlude(Voice voice)
        {
            if (!Owns(voice.lease) || voice.source.spatialBlend < 0.5f) return;
            LastObstructionQueryTime = AudioSettings.dspTime;
            Vector3 from = voice.source.transform.position, delta = listener.position - from;
            int count = Physics.RaycastNonAlloc(from, delta.normalized, hits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
            bool blocked = count == hits.Length;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i].transform;
                if (voice.anchor != null && hit.IsChildOf(voice.anchor) || listenerVehicle != null && hit.IsChildOf(listenerVehicle)) continue;
                blocked = true; break;
            }
            voice.occlusion = SensoryMath.Envelope(voice.occlusion, blocked ? 1 : 0, 0.1f, 0.15f, 0.4f);
            ApplyVoiceFilters(voice);
        }
        private float EnvironmentGain(SensoryCategory category)
            => environmentMix == null ? 1 : Mathf.Clamp(SensoryMath.Finite(environmentMix.Gain(category)), 0, 8);
        private void ApplyVoiceFilters(Voice voice)
        {
            float occlusionCutoff = Mathf.Lerp(22000, 1600, SensoryMath.Unit(voice.occlusion));
            float lowPass = Mathf.Min(occlusionCutoff, voice.fixedLowPass, environmentMix == null ? 22000 : environmentMix.LowPass(voice.category));
            float highPass = Mathf.Max(voice.fixedHighPass, environmentMix == null ? 20 : environmentMix.HighPass(voice.category));
            voice.lowPass.enabled = lowPass < 21999;
            voice.lowPass.cutoffFrequency = Mathf.Clamp(lowPass, 20, 22000);
            voice.highPass.enabled = highPass > 21;
            voice.highPass.cutoffFrequency = Mathf.Clamp(highPass, 20, Mathf.Max(20, lowPass));
        }
        private float FallbackGain(SensoryCategory category)
        {
            if (mix != null && mix.mixer != null && mix.Route(category) != null) return 1;
            float categoryGain = category == SensoryCategory.Music ? preferences.music
                : category == SensoryCategory.Radio || category == SensoryCategory.Sirens ? preferences.police
                : category == SensoryCategory.Player || category == SensoryCategory.OtherVehicle ? preferences.vehicle : preferences.effects;
            return preferences.master * categoryGain;
        }
        private static float Decibels(float value) => value <= 0.0001f ? -80 : 20 * Mathf.Log10(value);
        private static void ResetVoice(Voice voice)
        {
            voice.source.Stop(); voice.source.clip = null; voice.source.loop = false;
            voice.source.ignoreListenerPause = false; voice.unscaledLifetime = false; voice.unscaledStart = 0;
            voice.source.outputAudioMixerGroup = null; voice.source.volume = 0; voice.source.pitch = 1;
            voice.lowPass.enabled = voice.highPass.enabled = false;
            voice.nativeFilter.Clear();
            voice.anchor = null; voice.offset = Vector3.zero; voice.targetGain = voice.gain = voice.occlusion = 0;
            voice.fixedLowPass = 22000; voice.fixedHighPass = 20;
        }
        private void AudioDeviceChanged(bool deviceWasChanged) { if (voices != null) foreach (var voice in voices) voice.nativeFilter.SetSampleRate(AudioSettings.outputSampleRate); StopAll(); }
        private void StopAll() { if (voices != null) foreach (var voice in voices) Release(voice.lease); }
        private void OnDisable() => StopAll();
        private void OnDestroy()
        { AudioSettings.OnAudioConfigurationChanged -= AudioDeviceChanged; StopAll(); if (nativeSilence != null) Destroy(nativeSilence); nativeSilence = null; }
    }
}
