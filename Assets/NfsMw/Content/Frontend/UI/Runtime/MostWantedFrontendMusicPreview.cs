using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// A bounded, cancellable preview through the existing voice pool and music mixer route.
    /// It does not change the adaptive arrangement, its active requests or any shared asset.
    /// </summary>
    public sealed class MostWantedFrontendMusicPreview : IDisposable
    {
        public const int MaximumStems = 16;
        public const double MaximumPreviewSeconds = 30;
        public const double LoadTimeoutSeconds = 10;
        private readonly List<FeedbackVoiceLease> voices = new List<FeedbackVoiceLease>(MaximumStems);
        private SensoryAudioWorld world;
        private MusicStem[] pending;
        private double loadDeadline, stopAt;
        private bool disposed;
        public string Status { get; private set; } = string.Empty;
        public string Title { get; private set; } = string.Empty;
        public bool IsLoading => pending != null;
        public bool IsPlaying => world != null && voices.Count > 0;
        public int VoiceCount => voices.Count;

        public bool TryPlay(SensoryAudioWorld audioWorld, string title, MusicStem[] stems, out string failure)
        {
            if (disposed) return Reject("The music preview is closed.", out failure);
            if (audioWorld == null || !audioWorld.isActiveAndEnabled) return Reject("No active audio world is connected.", out failure);
            if (!AudioListener.pause) return Reject("Pause gameplay before previewing music.", out failure);
            if (stems == null || stems.Length == 0 || stems.Length > MaximumStems)
                return Reject("The section has no audio stems or exceeds the preview voice limit.", out failure);
            var requested = new MusicStem[stems.Length];
            for (int i = 0; i < stems.Length; i++)
            {
                var stem = stems[i];
                if (stem?.clip == null || !float.IsFinite(stem.gain) || stem.gain < 0 || stem.gain > 1)
                    return Reject("This section has a missing clip or invalid gain.", out failure);
                // The preview deliberately starts clips at their beginning; it never rewrites authored offsets.
                requested[i] = new MusicStem { clip = stem.clip, gain = stem.gain };
            }
            Stop();
            world = audioWorld; Title = title ?? string.Empty; pending = requested;
            loadDeadline = Time.unscaledTimeAsDouble + LoadTimeoutSeconds;
            foreach (var stem in pending)
                if (stem.clip.loadState == AudioDataLoadState.Unloaded && !stem.clip.LoadAudioData())
                { Stop(); return Reject("The audio clip could not be loaded.", out failure); }
            Status = "Loading preview…";
            Tick();
            if (!IsLoading && !IsPlaying) { failure = Status; return false; }
            failure = string.Empty; return true;
        }

        public void Tick()
        {
            if (disposed || (!IsLoading && voices.Count == 0)) return;
            if (world == null || !world.isActiveAndEnabled || !AudioListener.pause)
            { Stop(); Status = "Preview stopped."; return; }
            if (pending != null)
            {
                bool ready = true;
                foreach (var stem in pending)
                {
                    if (stem.clip == null || stem.clip.loadState == AudioDataLoadState.Failed)
                    { Stop(); Status = "The preview clip is unavailable."; return; }
                    ready &= stem.clip.loadState == AudioDataLoadState.Loaded;
                }
                if (!ready)
                {
                    if (Time.unscaledTimeAsDouble >= loadDeadline) { Stop(); Status = "The preview clip did not load in time."; }
                    return;
                }
                // Never evict gameplay or adaptive-music voices to make room for a preview.
                if (pending.Length > world.VoiceLimit - world.ActiveVoices)
                { Stop(); Status = "The audio voice pool has no room for this preview."; return; }
                double duration = 0;
                foreach (var stem in pending)
                {
                    var voice = world.Play(stem.clip, SensoryCategory.Music, null, Vector3.zero, stem.gain,
                        1, 256, ignoreListenerPause: true);
                    if (!world.Owns(voice)) { Stop(); Status = "The audio world rejected the preview."; return; }
                    voices.Add(voice); duration = Math.Max(duration, stem.clip.length);
                }
                pending = null; stopAt = Time.unscaledTimeAsDouble + Math.Min(MaximumPreviewSeconds, duration);
                Status = "Previewing: " + Title;
            }
            for (int i = voices.Count - 1; i >= 0; i--)
                if (!world.Owns(voices[i])) voices.RemoveAt(i);
            if (voices.Count == 0 || Time.unscaledTimeAsDouble >= stopAt)
            { Stop(); Status = "Preview finished."; }
        }

        public void Stop()
        {
            if (world != null) foreach (var voice in voices) world.Release(voice);
            voices.Clear(); pending = null; world = null; Title = string.Empty;
            Status = "Preview stopped.";
        }

        public void Dispose()
        { if (disposed) return; Stop(); disposed = true; }

        private bool Reject(string message, out string failure)
        { Status = failure = message; return false; }
    }
}
