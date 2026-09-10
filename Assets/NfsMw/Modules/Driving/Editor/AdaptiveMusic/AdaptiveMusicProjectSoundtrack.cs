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
    /// <summary>A playable project music asset discovered under the soundtrack root.</summary>
    public sealed class AdaptiveMusicSoundtrackTrack
    {
        public string assetPath = string.Empty;
        public string displayName = string.Empty;
        public string folder = string.Empty;
        public string stableId = string.Empty;
        public AudioClip clip;
    }

    /// <summary>
    /// Results of scanning the project soundtrack without changing assets. Raw
    /// unsupported sources are retained in the report so the editor can explain
    /// why a source cannot yet be used by the runtime.
    /// </summary>
    public sealed class AdaptiveMusicSoundtrackScan
    {
        public readonly List<AdaptiveMusicSoundtrackTrack> playableTracks = new List<AdaptiveMusicSoundtrackTrack>();
        public readonly List<string> unsupportedSources = new List<string>();
        public readonly List<string> unimportedSupportedSources = new List<string>();

        public int SourceCount => playableTracks.Count + unsupportedSources.Count + unimportedSupportedSources.Count;
        public bool HasPlayableTracks => playableTracks.Count > 0;
        public bool HasUnsupportedSources => unsupportedSources.Count > 0;

        public string BuildFailureMessage()
        {
            if (SourceCount == 0)
                return "No audio sources were found under Assets/NfsMw/Content/Audio/Music.";
            if (unsupportedSources.Count > 0)
                return "The soundtrack contains " + unsupportedSources.Count + " unsupported source(s). Unity 6 cannot import these source formats as AudioClip assets; convert the listed files to OGG, MP3, WAV, AIFF or FLAC and place the converted files under Assets/NfsMw/Content/Audio/Music, then refresh.";
            if (unimportedSupportedSources.Count > 0)
                return "The soundtrack files use supported extensions but Unity has not imported them as AudioClip assets yet. Wait for the import to finish, reimport the files, then refresh.";
            return "No playable AudioClip assets were found under Assets/NfsMw/Content/Audio/Music.";
        }
    }

    /// <summary>
    /// Imports supplied soundtrack assets into a runtime profile. It does not
    /// overwrite source audio and deliberately treats a full song as one base
    /// layer; layered intensity remains available for authored stem sections.
    /// </summary>
    public static class AdaptiveMusicProjectSoundtrack
    {
        public const string MusicRoot = "Assets/NfsMw/Content/Audio/Music";
        public const string SoundtrackProfilePath = AdaptiveMusicEditorModel.ProfileFolder + "/AdaptiveMusicSoundtrack.asset";

        private static readonly string[] SupportedExtensions =
        {
            ".wav", ".mp3", ".ogg", ".aif", ".aiff", ".flac", ".mod", ".it", ".s3m", ".xm"
        };

        private static readonly string[] KnownUnsupportedExtensions =
        {
            ".m4a", ".aac", ".aifc", ".caf", ".mp4", ".m4b"
        };

        private static readonly AdaptiveMusicContext[] CueContexts =
        {
            AdaptiveMusicContext.Frontend,
            AdaptiveMusicContext.FreeRoam,
            AdaptiveMusicContext.Race,
            AdaptiveMusicContext.Pursuit,
            AdaptiveMusicContext.Cooldown,
            AdaptiveMusicContext.Escape,
            AdaptiveMusicContext.Results,
            AdaptiveMusicContext.Failure,
            AdaptiveMusicContext.Pause
        };

        [MenuItem("NFS MW Remaster/Sensory/Adaptive Music/Build From Project Soundtrack")]
        public static void BuildFromMenu()
        {
            var scan = Scan();
            if (!scan.HasPlayableTracks)
            {
                EditorUtility.DisplayDialog("Project soundtrack is not playable", scan.BuildFailureMessage(), "OK");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<SensoryMusicProfile>(SoundtrackProfilePath) != null
                && !EditorUtility.DisplayDialog("Update soundtrack profile?",
                    "This updates the generated arrangement at " + SoundtrackProfilePath + ". Source audio is not changed.",
                    "Update", "Cancel")) return;
            var profile = BuildProfile(scan, true);
            if (profile != null)
            {
                Selection.activeObject = profile;
                EditorGUIUtility.PingObject(profile);
                AdaptiveMusicEditorWindow.Open(profile);
                string suffix = scan.HasUnsupportedSources ? " Some source files still need conversion; see the editor warning." : string.Empty;
                Debug.Log("Built " + SoundtrackProfilePath + " from " + scan.playableTracks.Count + " project soundtrack clip(s)." + suffix);
            }
        }

        public static AdaptiveMusicSoundtrackScan Scan()
        {
            var result = new AdaptiveMusicSoundtrackScan();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!AssetDatabase.IsValidFolder(MusicRoot)) return result;

            string absoluteRoot = Path.Combine(Application.dataPath, "Audio", "Music");
            if (Directory.Exists(absoluteRoot))
            {
                foreach (string file in Directory.GetFiles(absoluteRoot, "*", SearchOption.AllDirectories))
                {
                    string assetPath = ToAssetPath(file);
                    if (string.IsNullOrEmpty(assetPath) || !seen.Add(assetPath)) continue;
                    string extension = Path.GetExtension(assetPath).ToLowerInvariant();
                    if (IsKnownUnsupported(extension))
                    {
                        result.unsupportedSources.Add(assetPath);
                        continue;
                    }
                    if (!IsSupported(extension)) continue;
                    AddSupported(result, assetPath);
                }
            }

            // AssetDatabase also catches imported clips whose source extension
            // is hidden behind a package/importer path or a platform-specific
            // case variant that Directory.GetFiles did not expose consistently.
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { MusicRoot }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (!seen.Add(assetPath)) continue;
                AddSupported(result, assetPath);
            }
            result.playableTracks.Sort((a, b) => string.Compare(a.folder + "|" + a.displayName, b.folder + "|" + b.displayName, StringComparison.OrdinalIgnoreCase));
            result.unsupportedSources.Sort(StringComparer.OrdinalIgnoreCase);
            result.unimportedSupportedSources.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>
        /// Creates or updates the generated profile. Callers must make the
        /// overwrite decision before invoking this method.
        /// </summary>
        public static SensoryMusicProfile BuildProfile(AdaptiveMusicSoundtrackScan scan, bool replaceExisting)
        {
            if (scan == null) throw new ArgumentNullException(nameof(scan));
            if (!scan.HasPlayableTracks) throw new InvalidOperationException(scan.BuildFailureMessage());
            AdaptiveMusicEditorModel.EnsureAssetFolder(AdaptiveMusicEditorModel.ProfileFolder);

            var profile = AssetDatabase.LoadAssetAtPath<SensoryMusicProfile>(SoundtrackProfilePath);
            if (profile != null && !replaceExisting)
                throw new InvalidOperationException("The generated project soundtrack profile already exists: " + SoundtrackProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<SensoryMusicProfile>();
                AssetDatabase.CreateAsset(profile, SoundtrackProfilePath);
            }
            else Undo.RecordObject(profile, "Rebuild project soundtrack profile");

            var sections = new List<MusicSection>();
            var sectionsByTrack = new Dictionary<string, MusicSection>(StringComparer.OrdinalIgnoreCase);
            foreach (var track in scan.playableTracks)
            {
                var section = CreateFullMixSection(track);
                section.album = profile.FindSection(section.stableId)?.album ?? string.Empty;
                sections.Add(section);
                sectionsByTrack.Add(track.assetPath, section);
            }

            var cues = new List<MusicCue>();
            foreach (var context in CueContexts)
            {
                var track = ChooseTrack(scan.playableTracks, context);
                if (track == null) continue;
                var section = sectionsByTrack[track.assetPath];
                section.eligibleContexts |= context;
                cues.Add(new MusicCue
                {
                    stableId = "cue.soundtrack." + ContextName(context),
                    displayName = ContextName(context) + " soundtrack",
                    context = context,
                    defaultSectionId = section.stableId,
                    priority = ContextPriority(context),
                    defaultIntensity = DefaultIntensity(context)
                });
            }

            var transitions = new List<MusicTransitionRule>();
            foreach (var section in sections)
                transitions.Add(new MusicTransitionRule
                {
                    stableId = "transition.soundtrack." + section.stableId,
                    displayName = "Song end → " + section.displayName,
                    fromSectionId = "*",
                    toSectionId = section.stableId,
                    eligibleContexts = section.eligibleContexts,
                    priority = 10,
                    quantization = MusicQuantization.SectionEnd,
                    minimumDwellSeconds = 0.25f,
                    cooldownSeconds = 0,
                    allowTempoChange = true,
                    allowHarmonicMismatch = true
                });

            profile.profileId = "music.project.soundtrack";
            profile.schemaVersion = SensoryMusicProfile.CurrentSchemaVersion;
            profile.sourceRevision = "authoring";
            profile.bpm = 120;
            profile.beatsPerBar = 4;
            profile.bars = 8;
            profile.stems = Array.Empty<SensoryMusicStem>();
            profile.escapedStinger = null;
            profile.arrestedStinger = null;
            profile.finePaidStinger = null;
            profile.sections = sections.ToArray();
            profile.cues = cues.ToArray();
            profile.transitions = transitions.ToArray();
            profile.stingers = Array.Empty<MusicStinger>();
            profile.intensity = new MusicIntensityProfile
            {
                pursuitThreshold = 0.2f,
                highIntensityThreshold = 0.65f,
                hysteresis = 0.06f,
                minimumDwellSeconds = 1f,
                intensity = AnimationCurve.EaseInOut(0, 0, 1, 1)
            };
            profile.playback = new MusicPlaybackPolicy
            {
                persistAcrossScenes = true,
                keepTransportOnFrontendNavigation = true,
                pauseMode = MusicPauseMode.PauseTransport,
                scheduleLookaheadSeconds = 0.35f,
                crossfadeSeconds = 1.25f,
                minimumDwellSeconds = 0.5f,
                maxConcurrentStems = 2,
                restartAtSectionBoundaryAfterDeviceChange = true
            };
            profile.sourceRevision = AdaptiveMusicEditorModel.ComputeSourceRevision(profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return AssetDatabase.LoadAssetAtPath<SensoryMusicProfile>(SoundtrackProfilePath);
        }

        private static MusicSection CreateFullMixSection(AdaptiveMusicSoundtrackTrack track)
        {
            const float bpm = 120f;
            const int beatsPerBar = 4;
            double beatDuration = 60d / bpm;
            double duration = Math.Max(0.5d, track.clip != null ? track.clip.length : 0d);
            double loopBeats = duration / beatDuration;
            int bars = Mathf.Max(1, Mathf.CeilToInt((float)(loopBeats / beatsPerBar)));
            float loopEndBeat = Mathf.Clamp((float)loopBeats, 0.001f, bars * beatsPerBar);
            return new MusicSection
            {
                stableId = track.stableId,
                displayName = track.displayName + " [" + track.folder + "]",
                kind = KindForFolder(track.folder),
                eligibleContexts = ContextsForFolder(track.folder),
                bpm = bpm,
                beatsPerBar = beatsPerBar,
                bars = bars,
                loopStartBeat = 0,
                loopEndBeat = loopEndBeat,
                harmonicFamily = "supplied-full-mix",
                gridEnabled = false,
                allowFreeTimeTransition = true,
                allowHarmonicMismatch = true,
                fullMix = true,
                markers = new[]
                {
                    new MusicMarker { stableId = "song-start", displayName = "Song start", beat = 0, transitionPoint = true }
                },
                stems = new[]
                {
                    new MusicStem
                    {
                        stableId = track.stableId + ".full-mix",
                        displayName = track.displayName + " full mix",
                        role = MusicStemRole.Base,
                        clip = track.clip,
                        gain = 0.85f,
                        intensityGain = AnimationCurve.Linear(0, 1, 1, 1),
                        entryOffsetBeats = 0,
                        alwaysOn = true,
                        critical = true,
                        allowPhaseCompensation = false,
                        loadPolicy = MusicClipLoadPolicy.Streaming
                    }
                }
            };
        }

        private static AdaptiveMusicSoundtrackTrack ChooseTrack(List<AdaptiveMusicSoundtrackTrack> tracks, AdaptiveMusicContext context)
        {
            string preferredFolder = context == AdaptiveMusicContext.Frontend || context == AdaptiveMusicContext.Results
                || context == AdaptiveMusicContext.Failure || context == AdaptiveMusicContext.Pause ? "Menu"
                : context == AdaptiveMusicContext.Pursuit || context == AdaptiveMusicContext.Escape || context == AdaptiveMusicContext.Cooldown ? "Pursuit"
                : "Roaming";
            for (int pass = 0; pass < 2; pass++)
                foreach (var track in tracks)
                    if (track != null && (pass == 0 ? string.Equals(track.folder, preferredFolder, StringComparison.OrdinalIgnoreCase) : true)) return track;
            return null;
        }

        private static MusicSectionKind KindForFolder(string folder)
        {
            if (string.Equals(folder, "Menu", StringComparison.OrdinalIgnoreCase)) return MusicSectionKind.Intro;
            if (string.Equals(folder, "Pursuit", StringComparison.OrdinalIgnoreCase)) return MusicSectionKind.HighIntensity;
            return MusicSectionKind.LowIntensity;
        }

        private static AdaptiveMusicContext ContextsForFolder(string folder)
        {
            if (string.Equals(folder, "Menu", StringComparison.OrdinalIgnoreCase)) return AdaptiveMusicContext.Frontend | AdaptiveMusicContext.Results | AdaptiveMusicContext.Failure | AdaptiveMusicContext.Pause;
            if (string.Equals(folder, "Pursuit", StringComparison.OrdinalIgnoreCase)) return AdaptiveMusicContext.Pursuit | AdaptiveMusicContext.Cooldown | AdaptiveMusicContext.Escape;
            if (string.Equals(folder, "Roaming", StringComparison.OrdinalIgnoreCase)) return AdaptiveMusicContext.FreeRoam | AdaptiveMusicContext.Race;
            return AdaptiveMusicContext.FreeRoam | AdaptiveMusicContext.Race;
        }

        private static string ContextName(AdaptiveMusicContext context)
        {
            switch (context)
            {
                case AdaptiveMusicContext.Frontend: return "frontend";
                case AdaptiveMusicContext.FreeRoam: return "free-roam";
                case AdaptiveMusicContext.Race: return "race";
                case AdaptiveMusicContext.Pursuit: return "pursuit";
                case AdaptiveMusicContext.Cooldown: return "cooldown";
                case AdaptiveMusicContext.Escape: return "escape";
                case AdaptiveMusicContext.Results: return "results";
                case AdaptiveMusicContext.Failure: return "failure";
                case AdaptiveMusicContext.Pause: return "pause";
                default: return "context";
            }
        }

        private static int ContextPriority(AdaptiveMusicContext context)
        {
            if (context == AdaptiveMusicContext.Pause) return 950;
            if (context == AdaptiveMusicContext.Results || context == AdaptiveMusicContext.Failure) return 1000;
            if (context == AdaptiveMusicContext.Escape) return 900;
            if (context == AdaptiveMusicContext.Pursuit) return 800;
            if (context == AdaptiveMusicContext.Cooldown) return 700;
            if (context == AdaptiveMusicContext.Race) return 600;
            if (context == AdaptiveMusicContext.Frontend) return 500;
            return 400;
        }

        private static float DefaultIntensity(AdaptiveMusicContext context)
        {
            if (context == AdaptiveMusicContext.Pursuit) return 0.8f;
            if (context == AdaptiveMusicContext.Race) return 0.55f;
            if (context == AdaptiveMusicContext.Cooldown) return 0.35f;
            if (context == AdaptiveMusicContext.Escape) return 0.65f;
            return context == AdaptiveMusicContext.Frontend || context == AdaptiveMusicContext.Pause ? 0.12f : 0.2f;
        }

        private static void AddSupported(AdaptiveMusicSoundtrackScan result, string assetPath)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            if (clip == null)
            {
                result.unimportedSupportedSources.Add(assetPath);
                return;
            }
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            string readable = Path.GetFileNameWithoutExtension(assetPath);
            string folder = SourceFolder(assetPath);
            result.playableTracks.Add(new AdaptiveMusicSoundtrackTrack
            {
                assetPath = assetPath,
                displayName = readable,
                folder = folder,
                stableId = StableId(readable, guid),
                clip = clip
            });
        }

        private static string SourceFolder(string assetPath)
        {
            string relative = assetPath.Substring(Math.Min(assetPath.Length, MusicRoot.Length)).Trim('/');
            int separator = relative.IndexOf('/');
            return separator < 0 ? "Root" : relative.Substring(0, separator);
        }

        private static string StableId(string displayName, string guid)
        {
            var text = new StringBuilder("soundtrack.");
            foreach (char value in (displayName ?? string.Empty).ToLowerInvariant())
                text.Append(char.IsLetterOrDigit(value) ? value : '-');
            string readable = text.ToString().Trim('-');
            if (readable.Length > 72) readable = readable.Substring(0, 72).Trim('-');
            string suffix = string.IsNullOrEmpty(guid) ? StableHash(displayName) : guid.Substring(0, Math.Min(8, guid.Length));
            return readable + "." + suffix;
        }

        private static string StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in value ?? string.Empty) hash = (hash ^ c) * 16777619;
                return hash.ToString("x8", CultureInfo.InvariantCulture);
            }
        }

        private static bool IsSupported(string extension)
        {
            for (int i = 0; i < SupportedExtensions.Length; i++)
                if (string.Equals(SupportedExtensions[i], extension, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool IsKnownUnsupported(string extension)
        {
            for (int i = 0; i < KnownUnsupportedExtensions.Length; i++)
                if (string.Equals(KnownUnsupportedExtensions[i], extension, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string ToAssetPath(string file)
        {
            string full = Path.GetFullPath(file).Replace('\\', '/');
            string data = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
            if (!full.StartsWith(data + "/", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            return "Assets/" + full.Substring(data.Length + 1);
        }
    }
}
#endif
