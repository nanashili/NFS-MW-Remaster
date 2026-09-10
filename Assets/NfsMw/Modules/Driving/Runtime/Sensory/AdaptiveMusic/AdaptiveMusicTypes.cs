using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Flags]
    public enum AdaptiveMusicContext
    {
        None = 0,
        Frontend = 1 << 0,
        FreeRoam = 1 << 1,
        Race = 1 << 2,
        Pursuit = 1 << 3,
        Cooldown = 1 << 4,
        Escape = 1 << 5,
        Failure = 1 << 6,
        Results = 1 << 7,
        Pause = 1 << 8,
        Cinematic = 1 << 9,
        All = ~0
    }

    public enum MusicSectionKind
    {
        Intro,
        LowIntensity,
        HighIntensity,
        Cooldown,
        Escape,
        Outcome,
        Custom
    }

    public enum MusicStemRole
    {
        Base,
        Rhythm,
        Percussion,
        Bass,
        Tension,
        Atmosphere,
        Custom
    }

    public enum MusicQuantization
    {
        Immediate,
        Beat,
        Bar,
        SectionEnd,
        Marker
    }

    public enum MusicClipLoadPolicy
    {
        Preload,
        CompressedInMemory,
        Streaming
    }

    public enum MusicPauseMode
    {
        PauseTransport,
        DuckOnly,
        ContinueTransport
    }

    public enum MusicRequestKind
    {
        Context,
        Section
    }

    /// <summary>One synchronized layer in a horizontal arrangement.</summary>
    [Serializable]
    public sealed class MusicStem
    {
        public string stableId = "stem";
        public string displayName = "Stem";
        public MusicStemRole role = MusicStemRole.Base;
        public AudioClip clip;
        [Range(0f, 1f)] public float gain = 1f;
        public AnimationCurve intensityGain = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        [Min(0f)] public float entryOffsetBeats;
        public bool alwaysOn = true;
        public bool critical;
        public bool allowPhaseCompensation;
        public MusicClipLoadPolicy loadPolicy = MusicClipLoadPolicy.Preload;

        public MusicStem Copy()
        {
            return new MusicStem
            {
                stableId = stableId,
                displayName = displayName,
                role = role,
                clip = clip,
                gain = gain,
                intensityGain = intensityGain == null ? null : new AnimationCurve(intensityGain.keys),
                entryOffsetBeats = entryOffsetBeats,
                alwaysOn = alwaysOn,
                critical = critical,
                allowPhaseCompensation = allowPhaseCompensation,
                loadPolicy = loadPolicy
            };
        }
    }

    /// <summary>Grid and loop metadata for one horizontal music section.</summary>
    [Serializable]
    public sealed class MusicSection
    {
        public string stableId = "section";
        public string displayName = "Section";
        public string album = string.Empty;
        public MusicSectionKind kind = MusicSectionKind.Custom;
        public AdaptiveMusicContext eligibleContexts = AdaptiveMusicContext.All;
        [Range(30f, 240f)] public float bpm = 120f;
        [Range(1, 16)] public int beatsPerBar = 4;
        [Min(1)] public int bars = 8;
        [Min(0f)] public float loopStartBeat;
        [Min(0f)] public float loopEndBeat;
        public string harmonicFamily = "default";
        public string entryMarker;
        public string exitMarker;
        public bool gridEnabled = true;
        public bool allowFreeTimeTransition;
        public bool allowHarmonicMismatch;
        public string fallbackSectionId;
        public bool fullMix;
        public MusicMarker[] markers = Array.Empty<MusicMarker>();
        public MusicStem[] stems = Array.Empty<MusicStem>();

        public double BeatDurationSeconds()
        {
            return 60d / Mathf.Max(0.001f, bpm);
        }

        public double GridDurationSeconds()
        {
            return BeatDurationSeconds() * Mathf.Max(1, beatsPerBar);
        }

        public double LoopBeats()
        {
            double totalBeats = Mathf.Max(1, bars) * Mathf.Max(1, beatsPerBar);
            double start = Mathf.Max(0f, loopStartBeat);
            double end = loopEndBeat > start ? loopEndBeat : totalBeats;
            return Math.Max(1d, end - start);
        }

        public double LoopDurationSeconds()
        {
            return Math.Max(0.001d, LoopBeats() * BeatDurationSeconds());
        }

        public float TotalBeats()
        {
            return Mathf.Max(1, bars) * Mathf.Max(1, beatsPerBar);
        }

        public bool Allows(AdaptiveMusicContext context)
        {
            return context != AdaptiveMusicContext.None && (eligibleContexts & context) != 0;
        }

        public MusicMarker FindMarker(string markerId)
        {
            if (markers == null || string.IsNullOrWhiteSpace(markerId)) return null;
            for (int i = 0; i < markers.Length; i++)
                if (markers[i] != null && string.Equals(markers[i].stableId, markerId, StringComparison.Ordinal)) return markers[i];
            return null;
        }

        public MusicMarker FirstTransitionMarker()
        {
            if (markers == null) return null;
            for (int i = 0; i < markers.Length; i++)
                if (markers[i] != null && markers[i].transitionPoint) return markers[i];
            return null;
        }
    }

    /// <summary>Named beat position inside a section loop used by marker-quantized transitions.</summary>
    [Serializable]
    public sealed class MusicMarker
    {
        public string stableId = "marker";
        public string displayName = "Marker";
        [Min(0f)] public float beat;
        public bool transitionPoint = true;
    }

    [Serializable]
    public sealed class MusicCue
    {
        public string stableId = "cue";
        public string displayName = "Cue";
        public AdaptiveMusicContext context = AdaptiveMusicContext.FreeRoam;
        public string defaultSectionId;
        [Min(0)] public int priority;
        [Range(0f, 1f)] public float defaultIntensity;
    }

    /// <summary>Declarative edge between two sections. Runtime arbitration remains in AdaptiveMusic.</summary>
    [Serializable]
    public sealed class MusicTransitionRule
    {
        public string stableId = "transition";
        public string displayName = "Transition";
        public string fromSectionId = "*";
        public string toSectionId;
        public AdaptiveMusicContext eligibleContexts = AdaptiveMusicContext.All;
        [Min(0)] public int priority;
        public MusicQuantization quantization = MusicQuantization.Bar;
        [Min(0f)] public float minimumDwellSeconds;
        [Min(0f)] public float cooldownSeconds;
        public string requiredMarker;
        public string bridgeSectionId;
        public bool allowTempoChange;
        public bool allowHarmonicMismatch;

        public bool Matches(string fromId, string toId, AdaptiveMusicContext context)
        {
            bool from = string.IsNullOrEmpty(fromSectionId) || fromSectionId == "*" || string.Equals(fromSectionId, fromId, StringComparison.Ordinal);
            bool to = string.IsNullOrEmpty(toSectionId) || string.Equals(toSectionId, toId, StringComparison.Ordinal);
            return from && to && (eligibleContexts == AdaptiveMusicContext.All || (eligibleContexts & context) != 0);
        }
    }

    [Serializable]
    public sealed class MusicStinger
    {
        public string stableId = "stinger";
        public string displayName = "Stinger";
        public AudioClip clip;
        public AdaptiveMusicContext eligibleContexts = AdaptiveMusicContext.All;
        [Min(0)] public int priority = 100;
        public MusicQuantization quantization = MusicQuantization.Beat;
        [Min(0f)] public float cooldownSeconds = 1.5f;
        [Min(0f)] public float repetitionSuppressionSeconds = 8f;
        [Min(0f)] public float maxQueueAgeSeconds = 3f;
        [Range(0f, 1f)] public float gain = 1f;
        public bool interruptible = true;
        public string requiredSectionId;

        public bool Allows(AdaptiveMusicContext context)
        {
            return context != AdaptiveMusicContext.None && (eligibleContexts & context) != 0;
        }
    }

    [Serializable]
    public sealed class MusicIntensityProfile
    {
        [Range(0f, 1f)] public float pursuitThreshold = 0.2f;
        [Range(0f, 1f)] public float highIntensityThreshold = 0.65f;
        [Range(0f, 0.5f)] public float hysteresis = 0.06f;
        [Min(0f)] public float minimumDwellSeconds = 0.8f;
        public AnimationCurve intensity = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public float Evaluate(float value)
        {
            return Mathf.Clamp01(intensity == null ? value : intensity.Evaluate(Mathf.Clamp01(value)));
        }
    }

    [Serializable]
    public sealed class MusicPlaybackPolicy
    {
        public bool persistAcrossScenes = true;
        public bool keepTransportOnFrontendNavigation = true;
        public MusicPauseMode pauseMode = MusicPauseMode.PauseTransport;
        [Min(0.05f)] public float scheduleLookaheadSeconds = 0.2f;
        [Min(0f)] public float crossfadeSeconds = 0.35f;
        [Min(0f)] public float minimumDwellSeconds = 0.4f;
        [Range(1, 16)] public int maxConcurrentStems = 8;
        public bool restartAtSectionBoundaryAfterDeviceChange = true;
    }

    [Serializable]
    public struct MusicGameplaySnapshot
    {
        public AdaptiveMusicContext context;
        [Range(0f, 1f)] public float intensity;
        public bool pursuitDetected;
        public int heatLevel;
        public int respondingUnits;
        public string sourceId;

        public static MusicGameplaySnapshot ForContext(AdaptiveMusicContext value, float level, string source = "")
        {
            return new MusicGameplaySnapshot { context = value, intensity = Mathf.Clamp01(level), sourceId = source ?? string.Empty };
        }
    }

    [Serializable]
    public struct AdaptiveMusicTransportSnapshot
    {
        public bool running;
        public bool paused;
        public bool usingLegacyArrangement;
        public double dspTime;
        public double transportOrigin;
        public double cuePositionSeconds;
        public double beat;
        public int beatInBar;
        public int bar;
        public float intensity;
        public float targetIntensity;
        public AdaptiveMusicContext context;
        public string profileId;
        public string activeSectionId;
        public string pendingSectionId;
        public string bridgeSectionId;
        public string pendingFinalSectionId;
        public string pendingRequestId;
        public string pendingSource;
        public string selectedTransitionId;
        public string lastError;
        public int activeStemCount;
        public int scheduledStemCount;
        public int droppedStemCount;
        public double scheduledAt;
    }

    [Serializable]
    public struct AdaptiveMusicStemSnapshot
    {
        public string sectionId;
        public string stemId;
        public string displayName;
        public MusicStemRole role;
        public bool scheduled;
        public bool playing;
        public bool critical;
        public float targetGain;
        public float currentGain;
        public float phase;
        public double scheduledAt;
        public string assetState;
    }

    [Serializable]
    public struct AdaptiveMusicTraceEntry
    {
        public double dspTime;
        public double requestTime;
        public double scheduledAt;
        public string eventName;
        public string requestId;
        public string source;
        public string fromSectionId;
        public string toSectionId;
        public AdaptiveMusicContext context;
        public MusicQuantization quantization;
        public string detail;
    }
}
