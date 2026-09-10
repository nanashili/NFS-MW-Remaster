#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Disposable editor-only audition. It intentionally does not use the
    /// runtime SensoryAudioWorld, mixer preferences or gameplay singleton.
    /// </summary>
    public sealed class AdaptiveMusicPreviewSession : IDisposable
    {
        private readonly List<AudioSource> sources = new List<AudioSource>();
        private readonly List<MusicStem> sourceStems = new List<MusicStem>();
        private readonly List<double> sourceScheduledAt = new List<double>();
        private readonly List<bool> sourceLoops = new List<bool>();
        private GameObject root;
        private MusicSection section;
        private float intensity;
        private double scheduledAt;
        private string error = string.Empty;
        private bool subscribed;

        public bool IsPlaying => root != null && sources.Count > 0;
        public MusicSection Section => section;
        public double ScheduledAt => scheduledAt;
        public string Error => error;
        public double PositionSeconds => !IsPlaying ? 0 : Math.Max(0, AudioSettings.dspTime - scheduledAt);
        public double Beat => section == null ? 0 : PositionSeconds / section.BeatDurationSeconds();
        public int Bar => section == null ? 0 : (int)Math.Floor(Beat / Math.Max(1, section.beatsPerBar));

        public bool StartSection(SensoryMusicProfile profile, MusicSection value, float requestedIntensity)
        {
            Stop();
            error = string.Empty;
            section = value;
            intensity = Mathf.Clamp01(requestedIntensity);
            if (section == null) { error = "No section selected."; return false; }
            var stems = section.stems ?? Array.Empty<MusicStem>();
            root = new GameObject("Adaptive Music Editor Preview");
            root.hideFlags = HideFlags.HideAndDontSave;
            scheduledAt = AudioSettings.dspTime + Mathf.Max(0.12f, profile != null && profile.playback != null ? profile.playback.scheduleLookaheadSeconds : 0.2f);
            for (int i = 0; i < stems.Length; i++)
            {
                var stem = stems[i];
                if (stem == null || stem.clip == null) continue;
                if (stem.clip.loadState == AudioDataLoadState.Unloaded) stem.clip.LoadAudioData();
                if (stem.clip.loadState != AudioDataLoadState.Loaded)
                {
                    if (stem.critical) error = "Critical stem " + stem.stableId + " is not loaded.";
                    continue;
                }
                var source = root.AddComponent<AudioSource>();
                source.playOnAwake = false; source.loop = true; source.spatialBlend = 0;
                source.clip = stem.clip; source.priority = Mathf.Clamp(stem.critical ? 32 : 96, 0, 256);
                source.volume = Gain(stem);
                if (stem.entryOffsetBeats > 0 && stem.clip.samples > 0)
                {
                    float seconds = stem.entryOffsetBeats * (float)(60d / Mathf.Max(0.001f, section.bpm));
                    source.timeSamples = Mathf.Clamp((int)(Mathf.Repeat(seconds, stem.clip.length) / stem.clip.length * stem.clip.samples), 0, stem.clip.samples - 1);
                }
                source.PlayScheduled(scheduledAt);
                AddSource(source, stem, scheduledAt, true);
            }
            if (sources.Count == 0)
            {
                Stop();
                if (string.IsNullOrEmpty(error)) error = "No loaded stems are available for this section.";
                return false;
            }
            EditorApplication.update += Update;
            subscribed = true;
            return true;
        }

        public bool StartStinger(MusicStinger stinger)
        {
            error = string.Empty;
            if (stinger == null || stinger.clip == null) { error = "No stinger clip selected."; return false; }
            if (stinger.clip.loadState == AudioDataLoadState.Unloaded) stinger.clip.LoadAudioData();
            if (stinger.clip.loadState != AudioDataLoadState.Loaded) { error = "Stinger clip is not loaded."; return false; }
            if (root == null)
            {
                root = new GameObject("Adaptive Music Editor Preview");
                root.hideFlags = HideFlags.HideAndDontSave;
            }
            var source = root.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = false; source.spatialBlend = 0;
            source.clip = stinger.clip; source.volume = Mathf.Clamp01(stinger.gain);
            double stingerAt = AudioSettings.dspTime + 0.08;
            source.PlayScheduled(stingerAt);
            AddSource(source, null, stingerAt, false);
            if (!subscribed) { EditorApplication.update += Update; subscribed = true; }
            return true;
        }

        public void SetIntensity(float value)
        {
            intensity = Mathf.Clamp01(value);
            for (int i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                var stem = sourceStems[i];
                if (source == null || stem == null || section == null) continue;
                source.volume = Gain(stem);
            }
        }

        public void Stop()
        {
            if (subscribed) { EditorApplication.update -= Update; subscribed = false; }
            if (root != null)
            {
                for (int i = 0; i < sources.Count; i++) if (sources[i] != null) sources[i].Stop();
                if (Application.isPlaying) UnityEngine.Object.Destroy(root); else UnityEngine.Object.DestroyImmediate(root);
            }
            sources.Clear(); sourceStems.Clear(); sourceScheduledAt.Clear(); sourceLoops.Clear(); root = null; section = null; scheduledAt = 0;
        }

        public void Dispose() => Stop();

        private void Update()
        {
            if (root == null) { Stop(); return; }
            double now = AudioSettings.dspTime;
            for (int i = sources.Count - 1; i >= 0; i--)
            {
                var source = sources[i];
                if (source == null)
                {
                    RemoveSource(i);
                    continue;
                }
                if (!sourceLoops[i] && source.clip != null && now >= sourceScheduledAt[i] + source.clip.length + 0.1)
                {
                    source.Stop();
                    UnityEngine.Object.DestroyImmediate(source);
                    RemoveSource(i);
                }
            }
            if (sources.Count == 0) Stop();
        }

        private void AddSource(AudioSource source, MusicStem stem, double at, bool loop)
        {
            sources.Add(source); sourceStems.Add(stem); sourceScheduledAt.Add(at); sourceLoops.Add(loop);
        }

        private void RemoveSource(int index)
        {
            sources.RemoveAt(index); sourceStems.RemoveAt(index); sourceScheduledAt.RemoveAt(index); sourceLoops.RemoveAt(index);
        }

        private float Gain(MusicStem stem)
        {
            float curve = stem.intensityGain == null ? 1 : Mathf.Clamp01(stem.intensityGain.Evaluate(intensity));
            return Mathf.Clamp01(stem.gain * curve);
        }
    }
}
#endif
