#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public enum AdaptiveMusicDiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    [Serializable]
    public sealed class AdaptiveMusicDiagnostic
    {
        public AdaptiveMusicDiagnosticSeverity severity;
        public string code = string.Empty;
        public string message = string.Empty;
        public UnityEngine.Object target;
        public string property = string.Empty;

        public AdaptiveMusicDiagnostic(AdaptiveMusicDiagnosticSeverity severity, string code, string message,
            UnityEngine.Object target = null, string property = "")
        {
            this.severity = severity;
            this.code = code ?? string.Empty;
            this.message = message ?? string.Empty;
            this.target = target;
            this.property = property ?? string.Empty;
        }
    }

    /// <summary>
    /// Editor-facing catalog and authoring operations. It never becomes a
    /// runtime dependency and it never changes gameplay state while validating.
    /// </summary>
    public static class AdaptiveMusicEditorModel
    {
        public const string ProfileFolder = "Assets/NfsMw/Modules/Driving/Data/AdaptiveMusic";
        public const string FixtureProfilePath = ProfileFolder + "/AdaptiveMusicFixture.asset";
        public const string FixtureScenePath = "Assets/NfsMw/Scenes/Tests/AdaptiveMusicTest.unity";
        private const string FixtureAudioFolder = "Assets/NfsMw/Modules/Driving/Audio/AdaptiveMusicFixture";

        private static readonly AdaptiveMusicContext[] CoverageContexts =
        {
            AdaptiveMusicContext.Frontend,
            AdaptiveMusicContext.FreeRoam,
            AdaptiveMusicContext.Race,
            AdaptiveMusicContext.Pursuit,
            AdaptiveMusicContext.Cooldown,
            AdaptiveMusicContext.Escape,
            AdaptiveMusicContext.Results
        };

        public static List<SensoryMusicProfile> FindProfiles()
        {
            var result = new List<SensoryMusicProfile>();
            foreach (string guid in AssetDatabase.FindAssets("t:SensoryMusicProfile"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<SensoryMusicProfile>(path);
                if (profile != null) result.Add(profile);
            }
            result.Sort((a, b) => string.Compare(a.name + "|" + a.profileId, b.name + "|" + b.profileId, StringComparison.Ordinal));
            return result;
        }

        public static List<AdaptiveMusicDiagnostic> ValidateAll()
        {
            var diagnostics = new List<AdaptiveMusicDiagnostic>();
            var profiles = FindProfiles();
            if (profiles.Count == 0)
            {
                Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Info, "PROFILE_NONE",
                    "No SensoryMusicProfile assets were found. Create an arrangement or build the synthetic fixture.");
                return diagnostics;
            }

            var profileIds = new Dictionary<string, SensoryMusicProfile>(StringComparer.Ordinal);
            foreach (var profile in profiles)
            {
                if (profile == null) continue;
                if (string.IsNullOrWhiteSpace(profile.profileId))
                    Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Error, "PROFILE_ID", "Profile ID is empty.", profile, "profileId");
                else if (profileIds.TryGetValue(profile.profileId, out var other))
                    Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Error, "PROFILE_ID_DUPLICATE",
                        "Profile ID is shared with " + other.name + ". Runtime ownership and save migrations need unique IDs.", profile, "profileId");
                else profileIds.Add(profile.profileId, profile);

                if (!profile.Validate(out string failure))
                    Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Error, "PROFILE_INVALID", failure, profile);
                if (profile.schemaVersion < SensoryMusicProfile.CurrentSchemaVersion)
                    Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Warning, "PROFILE_MIGRATION",
                        "Profile uses an older schema. Migrate it before publishing.", profile, "schemaVersion");
                if (!profile.HasArrangement)
                {
                    Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Info, "LEGACY_COMPATIBILITY",
                        "Legacy single-arrangement data is accepted by the runtime compatibility path. Migrate it to author states for transitions, stingers and validation.", profile);
                    ValidateLegacyAudio(diagnostics, profile);
                }
                else
                {
                    ValidateArrangementAudio(diagnostics, profile);
                    ValidateCoverage(diagnostics, profile);
                }
            }
            return diagnostics;
        }

        public static List<AdaptiveMusicDiagnostic> ValidateProfile(SensoryMusicProfile profile)
        {
            var all = ValidateAll();
            all.RemoveAll(d => d.target != null && d.target != profile);
            return all;
        }

        public static SensoryMusicProfile CreateProfile(string folder = ProfileFolder, string fileName = "AdaptiveMusicProfile.asset")
        {
            EnsureFolder(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + fileName);
            var profile = ScriptableObject.CreateInstance<SensoryMusicProfile>();
            profile.profileId = "music.profile." + Guid.NewGuid().ToString("N").Substring(0, 8);
            profile.schemaVersion = SensoryMusicProfile.CurrentSchemaVersion;
            profile.sourceRevision = "authoring";
            profile.sections = Array.Empty<MusicSection>();
            profile.cues = Array.Empty<MusicCue>();
            profile.transitions = Array.Empty<MusicTransitionRule>();
            profile.stingers = Array.Empty<MusicStinger>();
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
            return profile;
        }

        public static void MigrateLegacy(SensoryMusicProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (profile.HasArrangement) return;
            Undo.RecordObject(profile, "Migrate adaptive music profile");
            if (string.IsNullOrWhiteSpace(profile.profileId))
            {
                var assetPath = AssetDatabase.GetAssetPath(profile);
                var assetGuid = string.IsNullOrWhiteSpace(assetPath) ? string.Empty : AssetDatabase.AssetPathToGUID(assetPath);
                profile.profileId = "music.profile." + (string.IsNullOrWhiteSpace(assetGuid) ? Guid.NewGuid().ToString("N") : assetGuid);
            }
            profile.schemaVersion = SensoryMusicProfile.CurrentSchemaVersion;
            profile.sourceRevision = "migrated-legacy-v1";

            var legacy = profile.stems ?? Array.Empty<SensoryMusicStem>();
            var sections = new List<MusicSection>();
            AddMigratedSection(sections, "frontend", "Frontend", MusicSectionKind.Intro, AdaptiveMusicContext.Frontend, profile, legacy);
            AddMigratedSection(sections, "free-roam", "Free roam", MusicSectionKind.LowIntensity, AdaptiveMusicContext.FreeRoam, profile, legacy);
            AddMigratedSection(sections, "race", "Race", MusicSectionKind.HighIntensity, AdaptiveMusicContext.Race, profile, legacy);
            AddMigratedSection(sections, "pursuit", "Pursuit", MusicSectionKind.HighIntensity, AdaptiveMusicContext.Pursuit, profile, legacy);
            AddMigratedSection(sections, "cooldown", "Cooldown", MusicSectionKind.Cooldown, AdaptiveMusicContext.Cooldown, profile, legacy);
            AddMigratedSection(sections, "escape", "Escape", MusicSectionKind.Escape, AdaptiveMusicContext.Escape, profile, legacy);
            AddMigratedSection(sections, "results", "Results", MusicSectionKind.Outcome, AdaptiveMusicContext.Results | AdaptiveMusicContext.Failure, profile, legacy);
            profile.sections = sections.ToArray();

            profile.cues = new[]
            {
                Cue("cue.frontend", "Frontend", AdaptiveMusicContext.Frontend, "frontend", 500, 0.12f),
                Cue("cue.free-roam", "Free roam", AdaptiveMusicContext.FreeRoam, "free-roam", 400, 0.08f),
                Cue("cue.race", "Race", AdaptiveMusicContext.Race, "race", 600, 0.55f),
                Cue("cue.pursuit", "Pursuit", AdaptiveMusicContext.Pursuit, "pursuit", 800, 0.78f),
                Cue("cue.cooldown", "Cooldown", AdaptiveMusicContext.Cooldown, "cooldown", 700, 0.38f),
                Cue("cue.escape", "Escape", AdaptiveMusicContext.Escape, "escape", 1000, 0.45f),
                Cue("cue.results", "Results", AdaptiveMusicContext.Results, "results", 1000, 0.18f),
                Cue("cue.pause", "Pause", AdaptiveMusicContext.Pause, "frontend", 950, 0.12f),
                Cue("cue.failure", "Failure", AdaptiveMusicContext.Failure, "results", 1000, 0.24f)
            };

            var transitions = new List<MusicTransitionRule>();
            foreach (var section in profile.sections)
            {
                transitions.Add(new MusicTransitionRule
                {
                    stableId = "transition.to." + section.stableId,
                    displayName = "Any → " + section.displayName,
                    fromSectionId = "*",
                    toSectionId = section.stableId,
                    eligibleContexts = section.eligibleContexts,
                    priority = 10,
                    quantization = MusicQuantization.Bar,
                    minimumDwellSeconds = 0.4f,
                    allowTempoChange = false,
                    allowHarmonicMismatch = false
                });
            }
            profile.transitions = transitions.ToArray();
            profile.stingers = new[]
            {
                MigratedStinger("outcome.escaped", "Escaped", profile.escapedStinger, AdaptiveMusicContext.Escape),
                MigratedStinger("outcome.arrested", "Arrested", profile.arrestedStinger, AdaptiveMusicContext.Failure),
                MigratedStinger("outcome.paid-fine", "Fine paid", profile.finePaidStinger, AdaptiveMusicContext.Results | AdaptiveMusicContext.Failure)
            };
            profile.sourceRevision = ComputeSourceRevision(profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        public static AdaptiveMusicGraphLayout GetOrCreateLayout(SensoryMusicProfile profile)
        {
            if (profile == null) return null;
            string profilePath = AssetDatabase.GetAssetPath(profile);
            string folder = string.IsNullOrEmpty(profilePath) ? ProfileFolder : Path.GetDirectoryName(profilePath).Replace('\\', '/');
            EnsureFolder(folder);
            string path = folder + "/" + profile.name + ".layout.asset";
            var layout = AssetDatabase.LoadAssetAtPath<AdaptiveMusicGraphLayout>(path);
            if (layout == null)
            {
                layout = ScriptableObject.CreateInstance<AdaptiveMusicGraphLayout>();
                layout.profileId = profile.profileId;
                AssetDatabase.CreateAsset(layout, AssetDatabase.GenerateUniqueAssetPath(path));
                AssetDatabase.SaveAssets();
            }
            if (layout.profileId != profile.profileId)
            {
                layout.profileId = profile.profileId;
                EditorUtility.SetDirty(layout);
            }
            if (layout.RemoveUnknownNodes(profile)) EditorUtility.SetDirty(layout);
            return layout;
        }

        public static string ComputeSourceRevision(SensoryMusicProfile profile)
        {
            if (profile == null) return string.Empty;
            var text = new StringBuilder(2048);
            text.Append(profile.profileId).Append('|').Append(profile.schemaVersion).Append('|')
                .Append(profile.bpm.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(profile.beatsPerBar).Append('|').Append(profile.bars).Append('|');
            foreach (var section in profile.sections ?? Array.Empty<MusicSection>())
            {
                if (section == null) continue;
                text.Append(section.stableId).Append('|').Append(section.displayName).Append('|').Append(section.kind).Append('|').Append(section.eligibleContexts).Append('|')
                    .Append(section.bpm.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(section.beatsPerBar).Append('|').Append(section.bars).Append('|')
                    .Append(section.loopStartBeat.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(section.loopEndBeat.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(section.harmonicFamily).Append('|').Append(section.entryMarker).Append('|').Append(section.exitMarker).Append('|')
                    .Append(section.gridEnabled).Append('|').Append(section.allowFreeTimeTransition).Append('|').Append(section.allowHarmonicMismatch)
                    .Append('|').Append(section.fallbackSectionId).Append('|').Append(section.fullMix).Append(';');
                foreach (var stem in section.stems ?? Array.Empty<MusicStem>())
                {
                    if (stem == null) continue;
                    text.Append(stem.stableId).Append('|').Append(stem.displayName).Append('|').Append(stem.role).Append('|')
                        .Append(stem.gain.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                        .Append(stem.entryOffsetBeats.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                        .Append(stem.alwaysOn).Append('|').Append(stem.critical).Append('|').Append(stem.allowPhaseCompensation).Append('|').Append(stem.loadPolicy).Append('|')
                        .Append(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(stem.clip))).Append('|');
                    AppendCurve(text, stem.intensityGain);
                    text.Append(';');
                }
                foreach (var marker in section.markers ?? Array.Empty<MusicMarker>())
                {
                    if (marker == null) continue;
                    text.Append(marker.stableId).Append('|').Append(marker.beat.ToString("R", CultureInfo.InvariantCulture))
                        .Append('|').Append(marker.transitionPoint).Append(';');
                }
            }
            foreach (var cue in profile.cues ?? Array.Empty<MusicCue>())
                if (cue != null) text.Append("cue|").Append(cue.stableId).Append('|').Append(cue.displayName).Append('|').Append(cue.context).Append('|')
                    .Append(cue.defaultSectionId).Append('|').Append(cue.priority).Append('|').Append(cue.defaultIntensity.ToString("R", CultureInfo.InvariantCulture)).Append(';');
            foreach (var transition in profile.transitions ?? Array.Empty<MusicTransitionRule>())
                if (transition != null) text.Append(transition.stableId).Append('|').Append(transition.fromSectionId).Append('|').Append(transition.toSectionId)
                    .Append('|').Append(transition.eligibleContexts).Append('|').Append(transition.priority).Append('|').Append(transition.quantization)
                    .Append('|').Append(transition.minimumDwellSeconds.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(transition.cooldownSeconds.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append(transition.requiredMarker)
                    .Append('|').Append(transition.bridgeSectionId).Append('|').Append(transition.allowTempoChange).Append('|')
                    .Append(transition.allowHarmonicMismatch).Append(';');
            foreach (var stinger in profile.stingers ?? Array.Empty<MusicStinger>())
                if (stinger != null) text.Append(stinger.stableId).Append('|').Append(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(stinger.clip)))
                    .Append('|').Append(stinger.eligibleContexts).Append('|').Append(stinger.priority).Append('|').Append(stinger.quantization)
                    .Append('|').Append(stinger.cooldownSeconds.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(stinger.repetitionSuppressionSeconds.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(stinger.maxQueueAgeSeconds.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append(stinger.gain.ToString("R", CultureInfo.InvariantCulture))
                    .Append('|').Append(stinger.interruptible).Append('|').Append(stinger.requiredSectionId).Append(';');
            if (profile.intensity != null)
                text.Append("intensity|").Append(profile.intensity.pursuitThreshold.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(profile.intensity.highIntensityThreshold.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(profile.intensity.hysteresis.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(profile.intensity.minimumDwellSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(';');
            if (profile.playback != null)
                text.Append("playback|").Append(profile.playback.pauseMode).Append('|').Append(profile.playback.scheduleLookaheadSeconds.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(profile.playback.crossfadeSeconds.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(profile.playback.minimumDwellSeconds.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(profile.playback.maxConcurrentStems).Append('|').Append(profile.playback.persistAcrossScenes).Append('|')
                    .Append(profile.playback.keepTransportOnFrontendNavigation).Append('|').Append(profile.playback.restartAtSectionBoundaryAfterDeviceChange).Append(';');
            foreach (var legacyStem in profile.stems ?? Array.Empty<SensoryMusicStem>())
                if (legacyStem != null) text.Append("legacy|").Append(legacyStem.stableId).Append('|').Append(legacyStem.displayName).Append('|')
                    .Append(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(legacyStem.clip))).Append('|').Append(legacyStem.intensityGain != null).Append(';');
            text.Append("legacy-stingers|").Append(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile.escapedStinger))).Append('|')
                .Append(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile.arrestedStinger))).Append('|')
                .Append(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile.finePaidStinger))).Append(';');
            return Hash128.Compute(text.ToString()).ToString();
        }

        private static void AppendCurve(StringBuilder text, AnimationCurve curve)
        {
            if (curve == null) { text.Append("curve:null"); return; }
            text.Append("curve:").Append(curve.length).Append(':');
            foreach (var key in curve.keys)
                text.Append(key.time.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(key.value.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(key.inTangent.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(key.outTangent.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(key.inWeight.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(key.outWeight.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(key.weightedMode).Append(';');
        }

        /// <summary>
        /// Creates deterministic original test audio and a complete arrangement.
        /// It is deliberately separate from commercial/reference recordings.
        /// </summary>
        public static SensoryMusicProfile BuildSyntheticFixture()
        {
            EnsureFolder("Assets/NfsMw/Modules/Driving/Data"); EnsureFolder(ProfileFolder); EnsureFolder(FixtureAudioFolder); EnsureFolder("Assets/NfsMw/Scenes");
            var profile = AssetDatabase.LoadAssetAtPath<SensoryMusicProfile>(FixtureProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<SensoryMusicProfile>();
                AssetDatabase.CreateAsset(profile, FixtureProfilePath);
            }
            const float fixtureBpm = 120f;
            const int fixtureMeter = 4;
            const int fixtureBars = 8;
            var clips = new AudioClip[3];
            for (int i = 0; i < clips.Length; i++)
                clips[i] = GetOrCreateDiagnosticClip("fixture-music-" + i + "-120bpm-4x8.wav", () => DiagnosticAudioSynthesis.Music(i, fixtureBpm, fixtureMeter, fixtureBars));
            var escaped = GetOrCreateDiagnosticClip("fixture-stinger-escaped.wav", () => DiagnosticAudioSynthesis.Stinger(0));
            var arrested = GetOrCreateDiagnosticClip("fixture-stinger-arrested.wav", () => DiagnosticAudioSynthesis.Stinger(1));
            var paid = GetOrCreateDiagnosticClip("fixture-stinger-paid.wav", () => DiagnosticAudioSynthesis.Stinger(2));

            profile.profileId = "music.editor.fixture";
            profile.schemaVersion = SensoryMusicProfile.CurrentSchemaVersion;
            profile.bpm = fixtureBpm; profile.beatsPerBar = fixtureMeter; profile.bars = fixtureBars;
            profile.stems = Array.Empty<SensoryMusicStem>();
            profile.escapedStinger = escaped; profile.arrestedStinger = arrested; profile.finePaidStinger = paid;
            profile.intensity = new MusicIntensityProfile
            {
                pursuitThreshold = 0.2f,
                highIntensityThreshold = 0.65f,
                hysteresis = 0.06f,
                minimumDwellSeconds = 0.8f,
                intensity = AnimationCurve.EaseInOut(0, 0, 1, 1)
            };
            profile.playback = new MusicPlaybackPolicy
            {
                persistAcrossScenes = true,
                keepTransportOnFrontendNavigation = true,
                pauseMode = MusicPauseMode.PauseTransport,
                scheduleLookaheadSeconds = 0.2f,
                crossfadeSeconds = 0.45f,
                minimumDwellSeconds = 0.4f,
                maxConcurrentStems = 4,
                restartAtSectionBoundaryAfterDeviceChange = true
            };
            profile.sections = new[]
            {
                FixtureSection("frontend", "Frontend", MusicSectionKind.Intro, AdaptiveMusicContext.Frontend, clips, 1),
                FixtureSection("free-roam", "Free roam", MusicSectionKind.LowIntensity, AdaptiveMusicContext.FreeRoam, clips, 2),
                FixtureSection("race", "Race", MusicSectionKind.HighIntensity, AdaptiveMusicContext.Race, clips, 3),
                FixtureSection("pursuit", "Pursuit", MusicSectionKind.HighIntensity, AdaptiveMusicContext.Pursuit, clips, 3),
                FixtureSection("cooldown", "Cooldown", MusicSectionKind.Cooldown, AdaptiveMusicContext.Cooldown, clips, 2),
                FixtureSection("escape", "Escape", MusicSectionKind.Escape, AdaptiveMusicContext.Escape, clips, 1),
                FixtureSection("results", "Results", MusicSectionKind.Outcome, AdaptiveMusicContext.Results | AdaptiveMusicContext.Failure, clips, 1)
            };
            profile.cues = new[]
            {
                Cue("fixture.frontend", "Frontend", AdaptiveMusicContext.Frontend, "frontend", 500, 0.12f),
                Cue("fixture.free-roam", "Free roam", AdaptiveMusicContext.FreeRoam, "free-roam", 400, 0.08f),
                Cue("fixture.race", "Race", AdaptiveMusicContext.Race, "race", 600, 0.55f),
                Cue("fixture.pursuit", "Pursuit", AdaptiveMusicContext.Pursuit, "pursuit", 800, 0.8f),
                Cue("fixture.cooldown", "Cooldown", AdaptiveMusicContext.Cooldown, "cooldown", 700, 0.38f),
                Cue("fixture.escape", "Escape", AdaptiveMusicContext.Escape, "escape", 1000, 0.45f),
                Cue("fixture.results", "Results", AdaptiveMusicContext.Results, "results", 1000, 0.18f),
                Cue("fixture.failure", "Failure", AdaptiveMusicContext.Failure, "results", 1000, 0.24f)
            };
            profile.transitions = new[]
            {
                FixtureTransition("transition.frontend", "*", "frontend", AdaptiveMusicContext.Frontend, 10),
                FixtureTransition("transition.free-roam", "*", "free-roam", AdaptiveMusicContext.FreeRoam, 10),
                FixtureTransition("transition.race", "*", "race", AdaptiveMusicContext.Race, 10),
                FixtureTransition("transition.pursuit", "*", "pursuit", AdaptiveMusicContext.Pursuit, 20, MusicQuantization.Marker, "bar-5"),
                FixtureTransition("transition.cooldown", "*", "cooldown", AdaptiveMusicContext.Cooldown, 10),
                FixtureTransition("transition.escape", "*", "escape", AdaptiveMusicContext.Escape, 30),
                FixtureTransition("transition.results", "*", "results", AdaptiveMusicContext.Results | AdaptiveMusicContext.Failure, 30)
            };
            profile.stingers = new[]
            {
                FixtureStinger("outcome.escaped", "Escaped", escaped, AdaptiveMusicContext.Escape),
                FixtureStinger("outcome.arrested", "Arrested", arrested, AdaptiveMusicContext.Failure),
                FixtureStinger("outcome.paid-fine", "Fine paid", paid, AdaptiveMusicContext.Results | AdaptiveMusicContext.Failure)
            };
            profile.sourceRevision = ComputeSourceRevision(profile);
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            return profile;
        }

        private static void ValidateLegacyAudio(List<AdaptiveMusicDiagnostic> diagnostics, SensoryMusicProfile profile)
        {
            var stems = profile.stems ?? Array.Empty<SensoryMusicStem>();
            if (stems.Length == 0)
                Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Error, "LEGACY_NO_STEMS", "Legacy profile has no stems and cannot produce music.", profile, "stems");
            for (int i = 0; i < stems.Length; i++)
            {
                var stem = stems[i];
                if (stem == null) { Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Error, "LEGACY_NULL_STEM", "Legacy stem is null.", profile, "stems"); continue; }
                if (stem.clip == null) Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Warning, "STEM_CLIP", "Legacy stem " + i + " has no AudioClip and will be skipped.", profile, "stems");
                else ValidateClipImporter(diagnostics, stem.clip, "Legacy stem " + i, profile);
            }
            if (profile.escapedStinger == null && profile.arrestedStinger == null && profile.finePaidStinger == null)
                Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Info, "NO_STINGERS", "No legacy outcome stingers are assigned.", profile);
        }

        private static void ValidateArrangementAudio(List<AdaptiveMusicDiagnostic> diagnostics, SensoryMusicProfile profile)
        {
            if (profile.sections == null || profile.sections.Length == 0)
            {
                Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Error, "NO_SECTIONS", "Arrangement has no sections.", profile, "sections");
                return;
            }
            foreach (var section in profile.sections)
            {
                if (section == null) continue;
                int ready = 0;
                foreach (var stem in section.stems ?? Array.Empty<MusicStem>())
                {
                    if (stem == null) continue;
                    if (stem.clip == null)
                    {
                        Add(diagnostics, stem.critical ? AdaptiveMusicDiagnosticSeverity.Error : AdaptiveMusicDiagnosticSeverity.Warning,
                            "STEM_CLIP", "Stem " + stem.stableId + " in " + section.stableId + " has no AudioClip.", profile, "sections");
                        continue;
                    }
                    ready++;
                    ValidateClipImporter(diagnostics, stem.clip, "Stem " + stem.stableId + " in " + section.stableId, profile);
                    double duration = section.LoopDurationSeconds();
                    if (Math.Abs(stem.clip.length - duration) > SensoryMusicProfile.DurationToleranceSeconds && !stem.allowPhaseCompensation)
                        Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Error, "STEM_DURATION",
                            "Stem " + stem.stableId + " is " + stem.clip.length.ToString("0.###", CultureInfo.InvariantCulture) + "s but section loop is " + duration.ToString("0.###", CultureInfo.InvariantCulture) + "s.", profile, "sections");
                }
                if (ready == 0)
                {
                    if (!string.IsNullOrEmpty(section.fallbackSectionId))
                        Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Warning, "SECTION_FALLBACK",
                            "Section " + section.stableId + " has no ready stems; runtime will use fallback " + section.fallbackSectionId + ".", profile, "sections");
                    else
                        Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Error, "SECTION_UNREADY",
                            "Section " + section.stableId + " has no ready stem and no fallback.", profile, "sections");
                }
            }
            foreach (var stinger in profile.stingers ?? Array.Empty<MusicStinger>())
            {
                if (stinger == null) continue;
                if (stinger.clip == null) Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Warning, "STINGER_CLIP",
                    "Stinger " + stinger.stableId + " has no AudioClip and will remain unavailable.", profile, "stingers");
                else ValidateClipImporter(diagnostics, stinger.clip, "Stinger " + stinger.stableId, profile);
            }
            if (profile.playback != null && profile.playback.maxConcurrentStems < 2)
                Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Warning, "STEM_BUDGET_LOW", "Stem budget is below two voices; layered transitions will sound incomplete.", profile, "playback");
        }

        private static void ValidateCoverage(List<AdaptiveMusicDiagnostic> diagnostics, SensoryMusicProfile profile)
        {
            foreach (var context in CoverageContexts)
            {
                bool covered = false;
                foreach (var cue in profile.cues ?? Array.Empty<MusicCue>())
                {
                    var section = cue != null && cue.context == context ? profile.FindSection(cue.defaultSectionId) : null;
                    if (section != null && section.Allows(context)) { covered = true; break; }
                }
                if (!covered)
                    foreach (var section in profile.sections ?? Array.Empty<MusicSection>())
                        if (section != null && section.Allows(context)) { covered = true; break; }
                if (!covered)
                    Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Error, "CONTEXT_COVERAGE",
                        "No default section covers " + context + ".", profile, "cues");
            }
            if (profile.transitions == null || profile.transitions.Length == 0)
                Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Warning, "NO_TRANSITIONS", "Arrangement has no transition rules; section requests can only use request quantization.", profile, "transitions");
            if (profile.stingers == null || profile.stingers.Length == 0)
                Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Info, "NO_STINGERS", "Arrangement has no stingers.", profile, "stingers");
        }

        private static void ValidateClipImporter(List<AdaptiveMusicDiagnostic> diagnostics, AudioClip clip, string label, UnityEngine.Object target)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(path))
            {
                Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Warning, "CLIP_EXTERNAL", label + " is not a project asset; runtime loading policy cannot be inspected.", target);
                return;
            }
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return;
            var settings = importer.defaultSampleSettings;
            if (!settings.preloadAudioData)
                Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Warning, "CLIP_NOT_PRELOADED", label + " is not preloaded. Schedule a load before its transition boundary or use an explicit fallback.", target);
            if (importer.loadInBackground)
                Add(diagnostics, AdaptiveMusicDiagnosticSeverity.Info, "CLIP_BACKGROUND_LOAD", label + " loads in the background; first-use scheduling needs a readiness lead.", target);
        }

        private static AudioClip GetOrCreateDiagnosticClip(string fileName, Func<float[]> synthesize)
        {
            string path = FixtureAudioFolder + "/" + fileName;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null) return clip;
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                DiagnosticAudioSynthesis.WriteWave(file, synthesize());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer != null)
            {
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings;
                importer.loadInBackground = false;
                importer.userData = "Original deterministic adaptive music editor fixture; not an NFS recording.";
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        private static MusicSection FixtureSection(string id, string name, MusicSectionKind kind, AdaptiveMusicContext context, AudioClip[] clips, int stemCount)
        {
            var stems = new MusicStem[stemCount];
            for (int i = 0; i < stemCount; i++)
                stems[i] = new MusicStem
                {
                    stableId = id + ".stem." + i,
                    displayName = id + " " + (i == 0 ? "base" : i == 1 ? "rhythm" : "tension"),
                    role = i == 0 ? MusicStemRole.Base : i == 1 ? MusicStemRole.Rhythm : MusicStemRole.Tension,
                    clip = clips[Mathf.Min(i, clips.Length - 1)],
                    gain = i == 0 ? 0.64f : i == 1 ? 0.48f : 0.34f,
                    intensityGain = i == 0 ? AnimationCurve.Linear(0, 0.75f, 1, 0.75f) : i == 1 ? AnimationCurve.Linear(0, 0, 1, 0.8f) : AnimationCurve.EaseInOut(0.35f, 0, 1, 0.9f),
                    alwaysOn = i == 0,
                    critical = i == 0,
                    loadPolicy = MusicClipLoadPolicy.Preload
                };
            return new MusicSection
            {
                stableId = id,
                displayName = name,
                kind = kind,
                eligibleContexts = context,
                bpm = 120,
                beatsPerBar = 4,
                bars = 8,
                harmonicFamily = "fixture-a-minor",
                gridEnabled = true,
                markers = new[]
                {
                    new MusicMarker { stableId = "bar-1", displayName = "Bar 1", beat = 0, transitionPoint = true },
                    new MusicMarker { stableId = "bar-5", displayName = "Bar 5", beat = 16, transitionPoint = true }
                },
                stems = stems
            };
        }

        private static MusicTransitionRule FixtureTransition(string id, string from, string to, AdaptiveMusicContext context, int priority,
            MusicQuantization quantization = MusicQuantization.Bar, string requiredMarker = "")
        {
            return new MusicTransitionRule
            {
                stableId = id,
                displayName = "Any → " + to,
                fromSectionId = from,
                toSectionId = to,
                eligibleContexts = context,
                priority = priority,
                quantization = quantization,
                requiredMarker = requiredMarker,
                minimumDwellSeconds = 0.4f,
                cooldownSeconds = 0,
                allowTempoChange = false,
                allowHarmonicMismatch = false
            };
        }

        private static MusicStinger FixtureStinger(string id, string name, AudioClip clip, AdaptiveMusicContext context)
        {
            return new MusicStinger
            {
                stableId = id,
                displayName = name,
                clip = clip,
                eligibleContexts = context,
                priority = 220,
                quantization = MusicQuantization.Bar,
                cooldownSeconds = 1,
                repetitionSuppressionSeconds = 8,
                maxQueueAgeSeconds = 5,
                gain = 0.8f,
                interruptible = true
            };
        }

        private static void AddMigratedSection(List<MusicSection> sections, string id, string name, MusicSectionKind kind,
            AdaptiveMusicContext context, SensoryMusicProfile profile, SensoryMusicStem[] legacy)
        {
            var converted = new MusicStem[Mathf.Max(1, legacy.Length)];
            for (int i = 0; i < converted.Length; i++)
            {
                var old = legacy.Length > 0 ? legacy[Mathf.Min(i, legacy.Length - 1)] : null;
                converted[i] = new MusicStem
                {
                    stableId = id + ".stem." + i,
                    displayName = old != null && !string.IsNullOrWhiteSpace(old.displayName) ? old.displayName : id + " stem " + i,
                    role = i == 0 ? MusicStemRole.Base : i == 1 ? MusicStemRole.Rhythm : MusicStemRole.Tension,
                    clip = old != null ? old.clip : null,
                    gain = i == 0 ? 0.7f : 0.45f,
                    intensityGain = old != null && old.intensityGain != null ? new AnimationCurve(old.intensityGain.keys) : AnimationCurve.Linear(0, i == 0 ? 0.7f : 0, 1, i == 0 ? 0.7f : 0.8f),
                    alwaysOn = i == 0,
                    critical = i == 0,
                    loadPolicy = MusicClipLoadPolicy.Preload
                };
            }
            sections.Add(new MusicSection
            {
                stableId = id,
                displayName = name,
                kind = kind,
                eligibleContexts = context,
                bpm = profile.bpm,
                beatsPerBar = profile.beatsPerBar,
                bars = profile.bars,
                harmonicFamily = "legacy",
                gridEnabled = true,
                stems = converted
            });
        }

        private static MusicCue Cue(string id, string name, AdaptiveMusicContext context, string section, int priority, float intensity)
            => new MusicCue { stableId = id, displayName = name, context = context, defaultSectionId = section, priority = priority, defaultIntensity = intensity };

        private static MusicStinger MigratedStinger(string id, string name, AudioClip clip, AdaptiveMusicContext context)
            => FixtureStinger(id, name, clip, context);

        private static void Add(List<AdaptiveMusicDiagnostic> diagnostics, AdaptiveMusicDiagnosticSeverity severity, string code,
            string message, UnityEngine.Object target = null, string property = "")
            => diagnostics.Add(new AdaptiveMusicDiagnostic(severity, code, message, target, property));

        private static void EnsureFolder(string path)
        {
            string normalized = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(normalized)) return;
            string[] parts = normalized.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        public static void EnsureAssetFolder(string path) => EnsureFolder(path);
    }
}
#endif
