using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Versioned music authoring data. The legacy fields remain serialized so
    /// existing sensory scenes continue to load while arrangements are migrated.
    /// </summary>
    [CreateAssetMenu(menuName = "NFS MW Remaster/Sensory/Music")]
    public sealed class SensoryMusicProfile : ScriptableObject
    {
        public const int CurrentSchemaVersion = 2;
        public const double DurationToleranceSeconds = 0.025;

        [Header("Identity")]
        public string profileId = "music.profile";
        public int schemaVersion = CurrentSchemaVersion;
        public string sourceRevision = "authoring";

        [Header("Legacy single arrangement")]
        [Range(30, 240)] public float bpm = 120;
        [Range(1, 12)] public int beatsPerBar = 4;
        [Min(1)] public int bars = 8;
        public SensoryMusicStem[] stems = Array.Empty<SensoryMusicStem>();
        public AudioClip escapedStinger, arrestedStinger, finePaidStinger;

        [Header("Versioned arrangement")]
        public MusicCue[] cues = Array.Empty<MusicCue>();
        public MusicSection[] sections = Array.Empty<MusicSection>();
        public MusicTransitionRule[] transitions = Array.Empty<MusicTransitionRule>();
        public MusicStinger[] stingers = Array.Empty<MusicStinger>();
        public MusicIntensityProfile intensity = new MusicIntensityProfile();
        public MusicPlaybackPolicy playback = new MusicPlaybackPolicy();

        public bool HasArrangement => sections != null && sections.Length > 0;

        public MusicSection FindSection(string stableId)
        {
            if (sections == null || string.IsNullOrEmpty(stableId)) return null;
            for (int i = 0; i < sections.Length; i++)
                if (sections[i] != null && string.Equals(sections[i].stableId, stableId, StringComparison.Ordinal)) return sections[i];
            return null;
        }

        public MusicTransitionRule FindTransition(string stableId)
        {
            if (transitions == null || string.IsNullOrEmpty(stableId)) return null;
            for (int i = 0; i < transitions.Length; i++)
                if (transitions[i] != null && string.Equals(transitions[i].stableId, stableId, StringComparison.Ordinal)) return transitions[i];
            return null;
        }

        public MusicStinger FindStinger(string stableId)
        {
            if (stingers == null || string.IsNullOrEmpty(stableId)) return null;
            for (int i = 0; i < stingers.Length; i++)
                if (stingers[i] != null && string.Equals(stingers[i].stableId, stableId, StringComparison.Ordinal)) return stingers[i];
            return null;
        }

        public string DefaultSectionFor(AdaptiveMusicContext context)
        {
            MusicCue best = null;
            MusicSection bestSection = null;
            if (cues != null)
                for (int i = 0; i < cues.Length; i++)
                {
                    var cue = cues[i];
                    var section = cue != null && cue.context == context ? FindSection(cue.defaultSectionId) : null;
                    if (section == null || !section.Allows(context)) continue;
                    if (best == null || cue.priority > best.priority
                        || cue.priority == best.priority && string.CompareOrdinal(cue.stableId, best.stableId) < 0)
                    {
                        best = cue;
                        bestSection = section;
                    }
                }
            if (bestSection != null) return bestSection.stableId;
            if (sections != null)
                for (int i = 0; i < sections.Length; i++)
                    if (sections[i] != null && sections[i].Allows(context)) return sections[i].stableId;
            return string.Empty;
        }

        public bool Validate(out string failure)
        {
            var errors = new List<string>();
            ValidateIdentity(errors);
            if (!SensoryMath.IsFinite(bpm) || bpm < 30 || bpm > 240 || beatsPerBar < 1 || beatsPerBar > 12 || bars < 1)
                errors.Add("Legacy tempo, meter or length is invalid.");
            if (stems == null || stems.Length > 4) errors.Add("Legacy stem budget is 0–4.");
            ValidateLegacyStems(errors);
            if (HasArrangement) ValidateArrangement(errors);
            else if (sections == null) errors.Add("Arrangement collection is null; use an empty array for legacy mode.");
            if (errors.Count > 0)
            {
                failure = string.Join(" ", errors.ToArray());
                return false;
            }
            failure = string.Empty;
            return true;
        }

        private void ValidateIdentity(List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(profileId) || profileId.Length > 128) errors.Add("Profile ID must be 1–128 characters.");
            if (schemaVersion < 1 || schemaVersion > CurrentSchemaVersion) errors.Add("Unsupported music schema version.");
            if (string.IsNullOrWhiteSpace(sourceRevision) || sourceRevision.Length > 128) errors.Add("Source revision must be 1–128 characters.");
        }

        private void ValidateLegacyStems(List<string> errors)
        {
            double duration = 60.0 / Mathf.Max(1, bpm) * Mathf.Max(1, beatsPerBar) * Mathf.Max(1, bars);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (stems == null) return;
            foreach (var stem in stems)
            {
                if (stem == null) { errors.Add("Legacy arrangement contains a null stem."); continue; }
                // Files authored before schema v2 have no serialized IDs. Their
                // compatibility path assigns deterministic index-based IDs at
                // runtime; do not reject those existing assets during migration.
                if (!string.IsNullOrWhiteSpace(stem.stableId) && stem.stableId != "legacy-stem" && !ids.Add(stem.stableId))
                    errors.Add("Duplicate legacy stem ID: " + stem.stableId);
                if (stem.intensityGain == null) errors.Add("Legacy stem " + stem.stableId + " has no intensity curve.");
                if (stem.clip != null && Math.Abs(stem.clip.length - duration) > DurationToleranceSeconds)
                    errors.Add("Legacy stem " + stem.stableId + " does not match the declared bar duration.");
            }
        }

        private void ValidateArrangement(List<string> errors)
        {
            var sectionIds = new HashSet<string>(StringComparer.Ordinal);
            int maxStems = playback == null ? 8 : Mathf.Clamp(playback.maxConcurrentStems, 1, 16);
            for (int i = 0; i < sections.Length; i++)
            {
                var section = sections[i];
                if (section == null) { errors.Add("Arrangement contains a null section at index " + i + "."); continue; }
                if (string.IsNullOrWhiteSpace(section.stableId)) errors.Add("Section " + i + " has no stable ID.");
                else if (!sectionIds.Add(section.stableId)) errors.Add("Duplicate section ID: " + section.stableId);
                if (!SensoryMath.IsFinite(section.bpm) || section.bpm < 30 || section.bpm > 240 || section.beatsPerBar < 1 || section.beatsPerBar > 16 || section.bars < 1)
                    errors.Add("Section " + section.stableId + " has invalid tempo, meter or length.");
                if (!SensoryMath.IsFinite(section.loopStartBeat) || !SensoryMath.IsFinite(section.loopEndBeat)
                    || section.loopStartBeat < 0 || section.loopStartBeat >= section.TotalBeats()
                    || section.loopEndBeat < 0 || section.loopEndBeat > section.TotalBeats()
                    || section.loopEndBeat > 0 && section.loopEndBeat <= section.loopStartBeat)
                    errors.Add("Section " + section.stableId + " has invalid loop points.");
                if (section.stems == null || section.stems.Length > maxStems)
                    errors.Add("Section " + section.stableId + " exceeds the configured stem budget of " + maxStems + ".");
                if (section.fullMix && (section.stems == null || section.stems.Length != 1))
                    errors.Add("Full-mix section " + section.stableId + " must contain exactly one stem.");
                ValidateMarkers(section, errors);
                ValidateSectionStems(section, errors);
            }
            ValidateCues(sectionIds, errors);
            ValidateTransitions(sectionIds, errors);
            ValidateStingers(errors);
            if (intensity == null || intensity.intensity == null) errors.Add("Intensity profile needs a curve.");
            else
            {
                if (!SensoryMath.IsFinite(intensity.pursuitThreshold) || intensity.pursuitThreshold < 0 || intensity.pursuitThreshold > 1
                    || !SensoryMath.IsFinite(intensity.highIntensityThreshold) || intensity.highIntensityThreshold < 0 || intensity.highIntensityThreshold > 1
                    || intensity.highIntensityThreshold < intensity.pursuitThreshold)
                    errors.Add("Intensity thresholds must be finite, ordered and between 0 and 1.");
                if (!SensoryMath.IsFinite(intensity.hysteresis) || intensity.hysteresis < 0 || intensity.hysteresis > 0.5f)
                    errors.Add("Intensity hysteresis must be finite and between 0 and 0.5.");
                if (!SensoryMath.IsFinite(intensity.minimumDwellSeconds) || intensity.minimumDwellSeconds < 0)
                    errors.Add("Intensity minimum dwell must be finite and non-negative.");
            }
            if (playback == null) errors.Add("Playback policy is missing.");
            else
            {
                if (!SensoryMath.IsFinite(playback.scheduleLookaheadSeconds) || playback.scheduleLookaheadSeconds < 0.05f || playback.scheduleLookaheadSeconds > 4f)
                    errors.Add("Schedule lookahead must be finite and between 0.05 and 4 seconds.");
                if (!SensoryMath.IsFinite(playback.crossfadeSeconds) || playback.crossfadeSeconds < 0)
                    errors.Add("Crossfade duration must be finite and non-negative.");
                if (!SensoryMath.IsFinite(playback.minimumDwellSeconds) || playback.minimumDwellSeconds < 0)
                    errors.Add("Playback minimum dwell must be finite and non-negative.");
                if (playback.maxConcurrentStems < 1 || playback.maxConcurrentStems > 16)
                    errors.Add("Maximum concurrent stems must be between 1 and 16.");
            }
        }

        private static void ValidateMarkers(MusicSection section, List<string> errors)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (section.markers == null) return;
            double loopBeats = section.LoopBeats();
            for (int i = 0; i < section.markers.Length; i++)
            {
                var marker = section.markers[i];
                if (marker == null) { errors.Add("Section " + section.stableId + " contains a null marker."); continue; }
                if (string.IsNullOrWhiteSpace(marker.stableId) || !ids.Add(marker.stableId))
                    errors.Add("Marker IDs must be unique and non-empty in section " + section.stableId + ".");
                if (!SensoryMath.IsFinite(marker.beat) || marker.beat < 0 || marker.beat >= loopBeats)
                    errors.Add("Marker " + marker.stableId + " is outside the loop of section " + section.stableId + ".");
            }
        }

        private static void ValidateSectionStems(MusicSection section, List<string> errors)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            double duration = section.LoopDurationSeconds();
            if (section.stems == null) return;
            foreach (var stem in section.stems)
            {
                if (stem == null) { errors.Add("Section " + section.stableId + " contains a null stem."); continue; }
                if (string.IsNullOrWhiteSpace(stem.stableId)) errors.Add("Section " + section.stableId + " has a stem without a stable ID.");
                else if (!ids.Add(stem.stableId)) errors.Add("Duplicate stem ID " + stem.stableId + " in section " + section.stableId + ".");
                if (!SensoryMath.IsFinite(stem.gain) || stem.gain < 0 || stem.gain > 1) errors.Add("Stem " + stem.stableId + " has an invalid gain.");
                if (!SensoryMath.IsFinite(stem.entryOffsetBeats) || stem.entryOffsetBeats < 0 || stem.entryOffsetBeats >= section.TotalBeats()) errors.Add("Stem " + stem.stableId + " has an invalid entry offset.");
                if (stem.intensityGain == null) errors.Add("Stem " + stem.stableId + " has no intensity curve.");
                if (stem.clip != null && Math.Abs(stem.clip.length - duration) > DurationToleranceSeconds && !stem.allowPhaseCompensation)
                    errors.Add("Stem " + stem.stableId + " does not match section " + section.stableId + " loop duration.");
            }
        }

        private void ValidateCues(HashSet<string> sectionIds, List<string> errors)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (cues == null) return;
            foreach (var cue in cues)
            {
                if (cue == null) { errors.Add("Arrangement contains a null cue."); continue; }
                if (string.IsNullOrWhiteSpace(cue.stableId) || !ids.Add(cue.stableId)) errors.Add("Cue IDs must be unique and non-empty.");
                if (cue.context == AdaptiveMusicContext.None) errors.Add("Cue " + cue.stableId + " needs an eligible context.");
                if (!SensoryMath.IsFinite(cue.defaultIntensity) || cue.defaultIntensity < 0 || cue.defaultIntensity > 1)
                    errors.Add("Cue " + cue.stableId + " has an invalid default intensity.");
                if (!string.IsNullOrEmpty(cue.defaultSectionId) && !sectionIds.Contains(cue.defaultSectionId))
                    errors.Add("Cue " + cue.stableId + " points to missing section " + cue.defaultSectionId + ".");
                else if (!string.IsNullOrEmpty(cue.defaultSectionId))
                {
                    var section = FindSection(cue.defaultSectionId);
                    if (section != null && !section.Allows(cue.context))
                        errors.Add("Cue " + cue.stableId + " points to section " + cue.defaultSectionId + " which is not eligible for " + cue.context + ".");
                }
            }
        }

        private void ValidateTransitions(HashSet<string> sectionIds, List<string> errors)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (transitions == null) return;
            foreach (var rule in transitions)
            {
                if (rule == null) { errors.Add("Arrangement contains a null transition."); continue; }
                if (string.IsNullOrWhiteSpace(rule.stableId) || !ids.Add(rule.stableId)) errors.Add("Transition IDs must be unique and non-empty.");
                if (string.IsNullOrWhiteSpace(rule.toSectionId)) errors.Add("Transition " + rule.stableId + " needs a destination section.");
                if (!string.IsNullOrEmpty(rule.fromSectionId) && rule.fromSectionId != "*" && !sectionIds.Contains(rule.fromSectionId))
                    errors.Add("Transition " + rule.stableId + " points from missing section " + rule.fromSectionId + ".");
                if (!string.IsNullOrEmpty(rule.toSectionId) && !sectionIds.Contains(rule.toSectionId))
                    errors.Add("Transition " + rule.stableId + " points to missing section " + rule.toSectionId + ".");
                if (!string.IsNullOrEmpty(rule.bridgeSectionId) && !sectionIds.Contains(rule.bridgeSectionId))
                    errors.Add("Transition " + rule.stableId + " points to missing bridge " + rule.bridgeSectionId + ".");
                if (!SensoryMath.IsFinite(rule.minimumDwellSeconds) || !SensoryMath.IsFinite(rule.cooldownSeconds)
                    || rule.minimumDwellSeconds < 0 || rule.cooldownSeconds < 0) errors.Add("Transition " + rule.stableId + " has an invalid timer.");
                if (!string.IsNullOrEmpty(rule.requiredMarker))
                {
                    MusicSection from = string.IsNullOrEmpty(rule.fromSectionId) || rule.fromSectionId == "*" ? null : FindSection(rule.fromSectionId);
                    MusicSection to = FindSection(rule.toSectionId);
                    bool markerExists = from != null && (from.FindMarker(rule.requiredMarker) != null || from.exitMarker == rule.requiredMarker);
                    markerExists |= to != null && (to.FindMarker(rule.requiredMarker) != null || to.entryMarker == rule.requiredMarker);
                    if (!markerExists && rule.fromSectionId != "*" && !string.IsNullOrEmpty(rule.fromSectionId))
                        errors.Add("Transition " + rule.stableId + " requires missing marker " + rule.requiredMarker + ".");
                    else if (!markerExists && rule.quantization == MusicQuantization.Marker)
                        errors.Add("Marker-quantized transition " + rule.stableId + " has no named marker data.");
                }
            }
        }

        private void ValidateStingers(List<string> errors)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (stingers == null) return;
            foreach (var stinger in stingers)
            {
                if (stinger == null) { errors.Add("Arrangement contains a null stinger."); continue; }
                if (string.IsNullOrWhiteSpace(stinger.stableId) || !ids.Add(stinger.stableId)) errors.Add("Stinger IDs must be unique and non-empty.");
                if (!SensoryMath.IsFinite(stinger.cooldownSeconds) || !SensoryMath.IsFinite(stinger.repetitionSuppressionSeconds)
                    || !SensoryMath.IsFinite(stinger.maxQueueAgeSeconds) || stinger.cooldownSeconds < 0
                    || stinger.repetitionSuppressionSeconds < 0 || stinger.maxQueueAgeSeconds < 0)
                    errors.Add("Stinger " + stinger.stableId + " has an invalid timer.");
                if (!SensoryMath.IsFinite(stinger.gain) || stinger.gain < 0 || stinger.gain > 1) errors.Add("Stinger " + stinger.stableId + " has an invalid gain.");
                if (stinger.eligibleContexts == AdaptiveMusicContext.None) errors.Add("Stinger " + stinger.stableId + " needs an eligible context.");
            }
        }

        /// <summary>Returns a stable, read-only description used by editor validation and tests.</summary>
        public string SchemaDescription => "music-profile/" + schemaVersion + "/" + (profileId ?? string.Empty) + "/" + (sourceRevision ?? string.Empty);
    }

    /// <summary>Legacy stem shape retained for existing sensory content and migration.</summary>
    [Serializable]
    public sealed class SensoryMusicStem
    {
        public string stableId = "legacy-stem";
        public string displayName = "Legacy stem";
        public AudioClip clip;
        public AnimationCurve intensityGain = AnimationCurve.Linear(0, 1, 1, 1);
    }
}
