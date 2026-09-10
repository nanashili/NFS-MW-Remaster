using System;
using System.Collections.Generic;
using NfsMwRemaster.Driving.Editor.Workspace;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    /// <summary>Public AudioSource preview using the workspace's existing disposable preview ownership.</summary>
    public sealed class BlackBoxAudition : IDisposable
    {
        private RacingPreviewLease lease;
        private Scene scene;
        private GameObject root;
        private AudioSource source;
        private AudioClip clip;
        private float[] input;
        private int firstFrame, endFrame, lastFrame;
        private bool paused;
        private readonly List<AudioListener> listeners = new List<AudioListener>();
        public bool Active => source != null && source.isPlaying;
        public bool Paused => source != null && paused;
        public int Frame => source == null || paused ? lastFrame : source.timeSamples;
        public int FrameCount => endFrame - firstFrame;
        public string Label { get; private set; } = "Stopped";
        public void SetGain(float value) { if (source != null) source.volume = Mathf.Clamp01(value); }
        public bool Matches(float[] pcm, int start, int end) => pcm != null && ReferenceEquals(input, pcm) && firstFrame == start && endFrame == end;

        public void Toggle(float[] pcm, int rate, int channels, int start, int end, float monitorGain, string label)
        {
            if (Matches(pcm, start, end) && Paused) Resume();
            else if (Matches(pcm, start, end) && Active) Pause();
            else Play(pcm, rate, channels, start, end, monitorGain, label);
        }
        public void Pause()
        {
            if (!Active) return;
            lastFrame = source.timeSamples; source.Pause(); paused = true;
        }
        public void Resume()
        {
            if (!Paused) return;
            source.UnPause(); paused = false; EditorApplication.QueuePlayerLoopUpdate();
        }
        // The window polls completion so the final progress and Play label repaint,
        // and a finished audition releases its listener lease without resetting progress.
        public bool Tick()
        {
            if (source == null || paused || Active) return false;
            lastFrame = FrameCount; Release(); return true;
        }

        public void Play(float[] pcm, int rate, int channels, int start, int end, float monitorGain, string label)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode to use the isolated audition. Live traces remain available during play.");
            if (pcm == null || channels < 1 || channels > 8 || rate < 1 || start < 0 || end <= start || (long)end * channels > pcm.Length)
                throw new ArgumentException("Audition requires available PCM and a valid half-open frame interval.");
            Stop();
            try
            {
                lease = RacingPreviewSessions.Acquire("audio-listener", "Black Box audition", Stop);
                scene = EditorSceneManager.NewPreviewScene();
                root = new GameObject("Black Box audition") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, scene);
                foreach (var listener in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include))
                    if (listener != null && listener.enabled) { listeners.Add(listener); listener.enabled = false; }
                root.AddComponent<AudioListener>();
                source = root.AddComponent<AudioSource>(); source.playOnAwake = false; source.loop = false; source.spatialBlend = 0;
                source.volume = Mathf.Clamp01(monitorGain);
                clip = AudioClip.Create(label, end - start, channels, rate, false);
                var interval = new float[checked((end - start) * channels)];
                Array.Copy(pcm, start * channels, interval, 0, interval.Length);
                if (!clip.SetData(interval, 0)) throw new InvalidOperationException("Unity could not load preview PCM.");
                input = pcm; firstFrame = start; endFrame = end; lastFrame = 0;
                source.clip = clip; source.Play(); Label = label;
                EditorApplication.QueuePlayerLoopUpdate();
                AudioSettings.OnAudioConfigurationChanged += DeviceChanged;
            }
            catch { Stop(); throw; }
        }
        private void DeviceChanged(bool _) => Stop();
        public void Stop()
        {
            Release(); input = null; firstFrame = endFrame = lastFrame = 0; Label = "Stopped";
        }
        private void Release()
        {
            AudioSettings.OnAudioConfigurationChanged -= DeviceChanged;
            if (source != null) { source.Stop(); source.clip = null; }
            source = null; paused = false;
            if (clip != null) UnityEngine.Object.DestroyImmediate(clip); clip = null;
            if (root != null) UnityEngine.Object.DestroyImmediate(root); root = null;
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene); scene = default;
            foreach (var listener in listeners) if (listener != null) listener.enabled = true;
            listeners.Clear(); lease?.Dispose(); lease = null;
        }
        public void Dispose() => Stop();
    }
}
