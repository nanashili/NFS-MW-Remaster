#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Disposable authoring preview. It evaluates authored zone geometry and
    /// profile intent in an isolated preview scene; production runtime owners
    /// and saves are never used as preview state.
    /// </summary>
    public sealed class AudioZonePreviewSession : IDisposable
    {
        private sealed class SourceState
        {
            public AudioZone zone;
            public AudioZoneAmbienceLayer layer;
            public AudioSource source;
            public AudioLowPassFilter lowPass;
            public AudioHighPassFilter highPass;
            public bool wasActive;
        }

        private readonly List<AudioZone> zones = new List<AudioZone>();
        private readonly List<SourceState> sources = new List<SourceState>();
        private readonly List<AudioListener> mutedListeners = new List<AudioListener>();
        private readonly List<bool> listenerStates = new List<bool>();
        private readonly List<Vector3> path = new List<Vector3>();
        private readonly Dictionary<string, bool> previousMembership = new Dictionary<string, bool>(StringComparer.Ordinal);
        private Scene previewScene;
        private GameObject root;
        private GameObject listenerObject;
        private AudioListener listener;
        private AudioSource presetSource;
        private AudioLowPassFilter presetLowPass;
        private AudioHighPassFilter presetHighPass;
        private AudioZoneProfile profileA;
        private AudioZoneProfile profileB;
        private AudioClip sourcePreset;
        private string soloZoneId = string.Empty;
        private string status = "Preview stopped.";
        private double lastTick;
        private float pathDistance;
        private float elapsed;
        private float speed = 12;
        private bool playing;
        private bool useB;
        private bool dryOnly;
        private int missingClips;

        public bool IsValid => previewScene.IsValid() && previewScene.isLoaded && root != null;
        public bool Playing => playing;
        public bool UseB { get => useB; set { useB = value; status = useB ? "Profile B active." : "Profile A active."; } }
        public bool DryOnly { get => dryOnly; set { dryOnly = value; } }
        public float Speed { get => speed; set => speed = Mathf.Clamp(SensoryMath.Finite(value), 0, 120); }
        public float Elapsed => elapsed;
        public Vector3 ListenerPosition => listenerObject != null ? listenerObject.transform.position : Vector3.zero;
        public string Status => status;
        public int MissingClipCount => missingClips;
        public string SoloZoneId => soloZoneId;

        public void Start(IList<AudioZone> authoredZones, AudioZoneProfile a, AudioZoneProfile b,
            AudioClip preset, Vector3 startPosition, float initialSpeed)
        {
            Stop();
            profileA = a;
            profileB = b;
            sourcePreset = preset;
            speed = Mathf.Clamp(SensoryMath.Finite(initialSpeed), 0, 120);
            zones.Clear();
            if (authoredZones != null)
                foreach (var zone in authoredZones)
                    if (zone != null && zone.Profile != null && zone.gameObject.scene.IsValid() && zone.gameObject.scene.isLoaded) zones.Add(zone);
            previewScene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Audio Zone Editor Preview");
            root.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(root, previewScene);
            listenerObject = new GameObject("Preview Listener");
            listenerObject.hideFlags = HideFlags.HideAndDontSave;
            listenerObject.transform.position = startPosition;
            listenerObject.transform.SetParent(root.transform, true);
            SceneManager.MoveGameObjectToScene(listenerObject, previewScene);
            listener = listenerObject.AddComponent<AudioListener>();
            listener.enabled = true;
            MuteProductionListeners();
            BuildSources();
            playing = true;
            elapsed = 0;
            pathDistance = 0;
            lastTick = EditorApplication.timeSinceStartup;
            status = missingClips > 0 ? "Preview running; " + missingClips + " clip(s) are unavailable." : "Preview running in a disposable scene.";
        }

        public void SetProfiles(AudioZoneProfile a, AudioZoneProfile b)
        {
            profileA = a;
            profileB = b;
        }

        public void SetSourcePreset(AudioClip clip)
        {
            if (sourcePreset == clip) return;
            sourcePreset = clip;
            if (presetSource == null) return;
            presetSource.Stop();
            presetSource.clip = sourcePreset;
            if (sourcePreset != null && sourcePreset.loadState == AudioDataLoadState.Loaded) presetSource.Play();
        }

        public void SetSoloZone(string id) => soloZoneId = id ?? string.Empty;

        public void SetPath(IList<Vector3> points)
        {
            path.Clear();
            if (points == null) return;
            foreach (var point in points)
                if (SensoryMath.IsFinite(point.x) && SensoryMath.IsFinite(point.y) && SensoryMath.IsFinite(point.z)) path.Add(point);
            pathDistance = 0;
        }

        public void SetPlaying(bool value)
        {
            playing = value && IsValid;
            lastTick = EditorApplication.timeSinceStartup;
            status = playing ? "Preview running." : "Preview paused.";
        }

        public void Tick()
        {
            if (!IsValid) return;
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp(SensoryMath.Finite((float)(now - lastTick)), 0, 0.1f);
            lastTick = now;
            if (!playing) return;
            elapsed += dt;
            AdvanceListener(dt);
            UpdateSources();
        }

        public void Stop()
        {
            playing = false;
            for (int i = 0; i < sources.Count; i++)
                if (sources[i] != null && sources[i].source != null) sources[i].source.Stop();
            if (presetSource != null) presetSource.Stop();
            sources.Clear();
            previousMembership.Clear();
            if (listenerObject != null) UnityEngine.Object.DestroyImmediate(listenerObject);
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            listener = null;
            presetSource = null;
            presetLowPass = null;
            presetHighPass = null;
            if (previewScene.IsValid() && previewScene.isLoaded) EditorSceneManager.ClosePreviewScene(previewScene);
            previewScene = default;
            RestoreProductionListeners();
            status = "Preview stopped and listener state restored.";
            missingClips = 0;
        }

        public void Dispose() => Stop();

        private void BuildSources()
        {
            missingClips = 0;
            int budget = 32;
            foreach (var zone in zones)
            {
                if (budget <= 0) break;
                foreach (var layer in zone.Profile.Ambience)
                {
                    if (budget-- <= 0) break;
                    if (layer == null || layer.clip == null) { missingClips++; continue; }
                    var sourceObject = new GameObject("Preview " + zone.name + " " + layer.id);
                    sourceObject.hideFlags = HideFlags.HideAndDontSave;
                    sourceObject.transform.SetParent(root.transform, false);
                    SceneManager.MoveGameObjectToScene(sourceObject, previewScene);
                    var source = sourceObject.AddComponent<AudioSource>();
                    source.playOnAwake = false; source.loop = layer.loop; source.clip = layer.clip;
                    source.spatialBlend = 1; source.rolloffMode = AudioRolloffMode.Logarithmic;
                    source.minDistance = 4; source.maxDistance = Mathf.Max(1, layer.maxDistance);
                    source.priority = Mathf.Clamp(layer.priority, 0, 256); source.volume = 0; source.pitch = Mathf.Clamp(layer.pitch, 0.25f, 3);
                    var low = sourceObject.AddComponent<AudioLowPassFilter>(); low.enabled = false;
                    var high = sourceObject.AddComponent<AudioHighPassFilter>(); high.enabled = false;
                    var state = new SourceState { zone = zone, layer = layer, source = source, lowPass = low, highPass = high };
                    sources.Add(state);
                    if (layer.clip.loadState == AudioDataLoadState.Loaded) source.Play(); else missingClips++;
                }
            }
            if (sourcePreset != null)
            {
                var sourceObject = new GameObject("Preview Source Preset");
                sourceObject.hideFlags = HideFlags.HideAndDontSave;
                sourceObject.transform.SetParent(root.transform, false);
                SceneManager.MoveGameObjectToScene(sourceObject, previewScene);
                presetSource = sourceObject.AddComponent<AudioSource>();
                presetLowPass = sourceObject.AddComponent<AudioLowPassFilter>();
                presetHighPass = sourceObject.AddComponent<AudioHighPassFilter>();
                presetSource.clip = sourcePreset; presetSource.loop = true; presetSource.spatialBlend = 0; presetSource.volume = 0.65f;
                if (sourcePreset.loadState == AudioDataLoadState.Loaded) presetSource.Play(); else missingClips++;
            }
        }

        private void AdvanceListener(float dt)
        {
            if (path.Count < 2)
            {
                if (listenerObject != null) listenerObject.transform.position = listenerObject.transform.position;
                return;
            }
            float total = 0;
            for (int i = 1; i < path.Count; i++) total += Vector3.Distance(path[i - 1], path[i]);
            if (total <= 0.001f) return;
            pathDistance = Mathf.Repeat(pathDistance + speed * dt, total);
            float remaining = pathDistance;
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 from = path[i - 1], to = path[i]; float length = Vector3.Distance(from, to);
                if (remaining <= length)
                {
                    listenerObject.transform.position = Vector3.Lerp(from, to, remaining / Mathf.Max(0.001f, length));
                    return;
                }
                remaining -= length;
            }
            listenerObject.transform.position = path[path.Count - 1];
        }

        private void UpdateSources()
        {
            Vector3 listenerPosition = ListenerPosition;
            AudioZoneProfile dominant = null;
            float dominantWeight = 0;
            foreach (var zone in zones)
            {
                bool wasActive = previousMembership.TryGetValue(zone.StableId, out bool old) && old;
                bool active = zone.TryEvaluate(listenerPosition, wasActive, out float weight, out _, out _);
                previousMembership[zone.StableId] = active;
                if (active && weight > dominantWeight) { dominant = zone.Profile; dominantWeight = weight; }
            }
            for (int i = 0; i < sources.Count; i++)
            {
                var state = sources[i];
                if (state?.source == null || state.layer == null || state.zone == null) continue;
                bool wasActive = previousMembership.TryGetValue(state.zone.StableId, out bool old) && old;
                state.zone.TryEvaluate(listenerPosition, wasActive, out float weight, out _, out _);
                if (!string.IsNullOrEmpty(soloZoneId) && state.zone.StableId != soloZoneId) weight = 0;
                var profile = useB ? profileB ?? state.zone.Profile : profileA ?? state.zone.Profile;
                float gain = Mathf.Clamp01(state.layer.gain * weight);
                state.source.transform.position = state.zone.transform.TransformPoint(state.layer.localPosition);
                state.source.volume = gain;
                state.source.pitch = Mathf.Clamp(state.layer.pitch, 0.25f, 3);
                float low = profile != null ? profile.LowPass(state.layer.category) : 22000;
                float high = profile != null ? profile.HighPass(state.layer.category) : 20;
                state.lowPass.enabled = low < 21999; state.lowPass.cutoffFrequency = Mathf.Clamp(low, 20, 22000);
                state.highPass.enabled = high > 21; state.highPass.cutoffFrequency = Mathf.Clamp(high, 20, Mathf.Max(20, low));
            }
            if (presetSource != null)
            {
                presetSource.transform.position = listenerPosition;
                var profile = useB ? profileB : profileA;
                float low = profile != null ? profile.LowPass(SensoryCategory.Environment) : 22000;
                float high = profile != null ? profile.HighPass(SensoryCategory.Environment) : 20;
                presetLowPass.enabled = !dryOnly && low < 21999; presetLowPass.cutoffFrequency = Mathf.Clamp(low, 20, 22000);
                presetHighPass.enabled = !dryOnly && high > 21; presetHighPass.cutoffFrequency = Mathf.Clamp(high, 20, Mathf.Max(20, low));
                presetSource.volume = dryOnly ? 0.65f : 0.65f * (profile != null ? Mathf.Clamp01(profile.Gain(SensoryCategory.Environment)) : 1);
            }
            status = missingClips > 0 ? "Preview running; " + missingClips + " clip(s) are unavailable." :
                "Preview running · listener " + listenerPosition.ToString("F1") + " · dominant " + (dominant != null ? dominant.DisplayName : "neutral");
        }

        private void MuteProductionListeners()
        {
            mutedListeners.Clear(); listenerStates.Clear();
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include))
            {
                if (candidate == null || candidate == listener) continue;
                mutedListeners.Add(candidate); listenerStates.Add(candidate.enabled);
                candidate.enabled = false;
            }
        }

        private void RestoreProductionListeners()
        {
            for (int i = 0; i < mutedListeners.Count; i++)
                if (mutedListeners[i] != null) mutedListeners[i].enabled = listenerStates[i];
            mutedListeners.Clear(); listenerStates.Clear();
        }
    }
}
#endif
