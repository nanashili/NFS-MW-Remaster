using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public interface IMostWantedFrontendKeyValueStore
    {
        bool HasKey(string key);
        string GetString(string key);
        void SetString(string key, string value);
        void DeleteKey(string key);
        void Save();
    }

    public interface IMostWantedFrontendPreferenceStoreHealth
    {
        bool RequiresReload { get; }
    }

    /// <summary>
    /// Keeps the existing sensory key authoritative. PlayerPrefs has no multi-key transaction or
    /// durability acknowledgement; reported write exceptions trigger best-effort restoration.
    /// </summary>
    public sealed class MostWantedFrontendPlayerPrefsStore : IMostWantedFrontendPreferenceStore, IMostWantedFrontendPreferenceStoreHealth
    {
        public const string FrontendKey = "nfs.mw.frontend.preferences.v1";
        public const string SensoryKey = "sensory.preferences.v1";
        private const int MaximumDocumentLength = 65536;
        private readonly IMostWantedFrontendKeyValueStore values;
        private bool newerVersion;
        public bool RequiresReload { get; private set; }
        [Serializable] private sealed class VersionHeader { public int version; }
        [Serializable] private sealed class FrontendDocument
        {
            public int version, width, height, qualityLevel, vSyncCount, targetFrameRate;
            public FullScreenMode fullScreenMode;
            public bool pauseOnFocusLoss, hoodCamera;
            public float menuMusic;
            public bool eaTraxEnabled;
            public MostWantedFrontendMusicOrder eaTraxOrder;
            public string playerAlias;
            public MostWantedFrontendKeyboardBindings keyboard;
            public FrontendDocument(MostWantedFrontendSettings source)
            {
                version = source.version; width = source.width; height = source.height;
                qualityLevel = source.qualityLevel; vSyncCount = source.vSyncCount; targetFrameRate = source.targetFrameRate;
                fullScreenMode = source.fullScreenMode; pauseOnFocusLoss = source.pauseOnFocusLoss;
                hoodCamera = source.hoodCamera; playerAlias = source.playerAlias; keyboard = source.keyboard.Clone();
                menuMusic = source.menuMusic; eaTraxEnabled = source.eaTraxEnabled; eaTraxOrder = source.eaTraxOrder;
            }
        }

        public MostWantedFrontendPlayerPrefsStore(IMostWantedFrontendKeyValueStore storage = null)
        { values = storage ?? new UnityPlayerPrefs(); }

        public bool TryLoad(MostWantedFrontendSettings defaults, out MostWantedFrontendSettings settings, out string failure)
        {
            if (defaults == null) throw new ArgumentNullException(nameof(defaults));
            settings = defaults.Clone(); failure = string.Empty;
            var sound = MostWantedFrontendSettings.CloneAudio(defaults.audio);
            try
            {
                if (values.HasKey(SensoryKey)) JsonUtility.FromJsonOverwrite(CheckedDocument(values.GetString(SensoryKey)), sound);
                settings.audio = MostWantedFrontendSettings.CloneAudio(sound);
                if (values.HasKey(FrontendKey))
                {
                    string json = CheckedDocument(values.GetString(FrontendKey));
                    int version = JsonUtility.FromJson<VersionHeader>(json)?.version ?? 0;
                    newerVersion = version > MostWantedFrontendSettings.CurrentVersion;
                    if (version != MostWantedFrontendSettings.CurrentVersion)
                    {
                        failure = newerVersion ? "Preferences were saved by a newer build and will not be overwritten."
                            : "The saved frontend preferences version is unsupported. Defaults are in use; the saved document is unchanged.";
                        return false;
                    }
                    JsonUtility.FromJsonOverwrite(json, settings);
                }
                // Audio must not be obtained from a stale duplicate embedded in the frontend document.
                settings.audio = sound;
                if (!settings.TryValidate(out failure))
                { settings = defaults.Clone(); settings.audio = sound;
                    if (!settings.TryValidate(out _)) settings = defaults.Clone();
                    failure = "Invalid saved preferences: " + failure + " Saved data has not been changed."; return false; }
                return true;
            }
            catch (Exception exception)
            {
                settings = defaults.Clone(); settings.audio = sound;
                if (!settings.TryValidate(out _)) settings = defaults.Clone();
                failure = "Saved preferences could not be read. Saved data has not been changed: " + exception.Message;
                return false;
            }
        }

        public bool TrySave(MostWantedFrontendSettings settings, out string failure)
        {
            if (RequiresReload || newerVersion)
            { failure = newerVersion ? "Preferences from a newer build will not be overwritten." : "Restart before saving settings after the failed storage rollback."; return false; }
            if (settings == null) { failure = "Preferences are missing."; return false; }
            if (!settings.TryValidate(out failure)) return false;
            bool hadFrontend = false, hadSensory = false, captured = false;
            string oldFrontend = null, oldSensory = null;
            try
            {
                hadFrontend = values.HasKey(FrontendKey); hadSensory = values.HasKey(SensoryKey);
                if (hadFrontend) oldFrontend = values.GetString(FrontendKey);
                if (hadSensory) oldSensory = values.GetString(SensoryKey);
                if (hadFrontend && !string.IsNullOrWhiteSpace(oldFrontend) && oldFrontend.Length <= MaximumDocumentLength)
                {
                    try { newerVersion = (JsonUtility.FromJson<VersionHeader>(CheckedDocument(oldFrontend))?.version ?? 0) > MostWantedFrontendSettings.CurrentVersion; }
                    catch (ArgumentException) { /* An explicit Apply may replace a malformed document, but never a known newer version. */ }
                    if (newerVersion) { failure = "Preferences from a newer build will not be overwritten."; return false; }
                }
                captured = true;
                var document = new FrontendDocument(settings);
                string frontendJson = JsonUtility.ToJson(document);
                string sensoryJson = JsonUtility.ToJson(settings.audio);
                values.SetString(FrontendKey, frontendJson);
                values.SetString(SensoryKey, sensoryJson);
                values.Save();
                failure = string.Empty; return true;
            }
            catch (Exception exception)
            {
                failure = "Settings could not be saved: " + exception.Message;
                if (captured)
                {
                    try
                    {
                        RestoreKey(FrontendKey, hadFrontend, oldFrontend);
                        RestoreKey(SensoryKey, hadSensory, oldSensory);
                        values.Save();
                    }
                    catch (Exception rollback)
                    { RequiresReload = true; failure += " Storage rollback also failed: " + rollback.Message; }
                }
                return false;
            }
        }

        private void RestoreKey(string key, bool existed, string previous)
        { if (existed) values.SetString(key, previous); else values.DeleteKey(key); }
        private static string CheckedDocument(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumDocumentLength)
                throw new ArgumentException("The preferences document is empty or exceeds its size limit.");
            string trimmed = value.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
                throw new ArgumentException("Preferences must be a JSON object.");
            return trimmed;
        }

        private sealed class UnityPlayerPrefs : IMostWantedFrontendKeyValueStore
        {
            public bool HasKey(string key) => PlayerPrefs.HasKey(key);
            public string GetString(string key) => PlayerPrefs.GetString(key);
            public void SetString(string key, string value) => PlayerPrefs.SetString(key, value);
            public void DeleteKey(string key) => PlayerPrefs.DeleteKey(key);
            public void Save() => PlayerPrefs.Save();
        }
    }

    /// <summary>Delegates output to existing audio/camera/quality consumers. Never changes assets or vehicle tuning.</summary>
    public sealed class MostWantedFrontendUnityBackend : IMostWantedFrontendPreferenceBackend
    {
        private static Action<int> applyQuality;
        private readonly HashSet<SensoryAudioWorld> initializedAudio = new HashSet<SensoryAudioWorld>();
        private VehicleCameraRig initializedCamera;
        public double Realtime => Time.realtimeSinceStartupAsDouble;
        public bool IsFocused => Application.isFocused;
        public bool CanChangeDisplay => !Application.isEditor && !Application.isBatchMode
            && (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.OSXPlayer
                || Application.platform == RuntimePlatform.LinuxPlayer);
        public bool CanChangeQuality => applyQuality != null && QualitySettings.names.Length > 0;
        public bool CanChangeCameraView => FindPlayerCamera() != null;

        public static void ConfigureQualityAdapter(Action<int> adapter) { applyQuality = adapter; }

        public MostWantedFrontendSettings Capture(MostWantedFrontendSettings fallback)
        {
            var result = fallback?.Clone() ?? new MostWantedFrontendSettings();
            if (Screen.width >= 320 && Screen.height >= 200) { result.width = Screen.width; result.height = Screen.height; }
            result.fullScreenMode = Screen.fullScreenMode;
            result.qualityLevel = Math.Max(0, QualitySettings.GetQualityLevel());
            result.vSyncCount = Mathf.Clamp(QualitySettings.vSyncCount, 0, 4);
            result.targetFrameRate = Application.targetFrameRate <= 0 ? -1 : Mathf.Clamp(Application.targetFrameRate, 30, 360);
            var audio = UnityEngine.Object.FindObjectsByType<SensoryAudioWorld>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var authoritative = AdaptiveMusic.Instance != null ? AdaptiveMusic.Instance.AudioWorld : null;
            if (authoritative == null && audio.Length == 1) authoritative = audio[0];
            bool frontendAudio = IsFrontendAudioState();
            if (authoritative != null && authoritative.Preferences != null && !frontendAudio)
            {
                result.audio = MostWantedFrontendSettings.CloneAudio(authoritative.Preferences);
                result.eaTraxEnabled = !authoritative.IsMuted(SensoryCategory.Music);
            }
            var camera = FindPlayerCamera();
            if (camera != null) result.hoodCamera = camera.IsHoodView;
            return result;
        }

        public bool TryValidate(MostWantedFrontendSettings before, MostWantedFrontendSettings candidate, out string failure)
        {
            if (before == null || candidate == null) return Fail("The settings snapshot is unavailable.", out failure);
            if (!candidate.TryValidate(out failure)) return false;
            if (candidate.qualityLevel != before.qualityLevel)
            {
                if (!CanChangeQuality) return Fail("The HDRP quality adapter is unavailable.", out failure);
                if (candidate.qualityLevel >= QualitySettings.names.Length) return Fail("That quality preset is unavailable in this build.", out failure);
            }
            if (candidate.hoodCamera != before.hoodCamera && !CanChangeCameraView)
                return Fail("A uniquely bound player camera is required to change camera view.", out failure);
            if (!candidate.SameDisplay(before))
            {
                if (!CanChangeDisplay) return Fail("Display-mode changes require a desktop player build, not the Editor or a headless run.", out failure);
                if (candidate.fullScreenMode == FullScreenMode.ExclusiveFullScreen && Application.platform != RuntimePlatform.WindowsPlayer)
                    return Fail("Exclusive fullscreen is available only in the Windows player.", out failure);
                if (candidate.fullScreenMode == FullScreenMode.MaximizedWindow && Application.platform != RuntimePlatform.OSXPlayer)
                    return Fail("Maximized window mode is available only in the macOS player.", out failure);
                bool found = false;
                foreach (Resolution mode in Screen.resolutions)
                    if (mode.width == candidate.width && mode.height == candidate.height) { found = true; break; }
                // Keep the existing custom window size as a valid choice without inventing display modes.
                found |= candidate.width == before.width && candidate.height == before.height;
                if (!found) return Fail("That resolution is not reported by the current display.", out failure);
            }
            failure = string.Empty; return true;
        }

        public bool TryApply(MostWantedFrontendSettings before, MostWantedFrontendSettings candidate, out string failure)
        {
            if (before == null || candidate == null) return Fail("The settings snapshot is unavailable.", out failure);
            try
            {
                if (before.qualityLevel != candidate.qualityLevel)
                {
                    if (applyQuality == null) return Fail("The HDRP quality adapter is unavailable.", out failure);
                    applyQuality(candidate.qualityLevel);
                }
                if (before.vSyncCount != candidate.vSyncCount) QualitySettings.vSyncCount = candidate.vSyncCount;
                if (before.targetFrameRate != candidate.targetFrameRate) Application.targetFrameRate = candidate.targetFrameRate;
                // Compare requested snapshots, not Screen.width: resolution requests complete asynchronously.
                if (!candidate.SameDisplay(before)) Screen.SetResolution(candidate.width, candidate.height, candidate.fullScreenMode);
                foreach (var world in UnityEngine.Object.FindObjectsByType<SensoryAudioWorld>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (!SameAudio(world.Preferences, candidate.audio)) ApplyAudio(world, candidate.audio);
                    world.Mute(SensoryCategory.Music, !candidate.eaTraxEnabled);
                    initializedAudio.Add(world);
                }
                if (candidate.hoodCamera != before.hoodCamera)
                {
                    var camera = FindPlayerCamera();
                    // User changes are capability-checked before Apply. If a scene has since
                    // unloaded during cancellation, a destroyed camera has nothing left to restore.
                    if (camera != null)
                    {
                        if (camera.IsHoodView != candidate.hoodCamera) camera.ToggleCameraMode();
                        initializedCamera = camera;
                    }
                }
                else if (initializedCamera == null) initializedCamera = FindPlayerCamera();
                failure = string.Empty; return true;
            }
            catch (Exception exception) { failure = "Runtime settings apply failed: " + exception.Message; return false; }
        }

        public bool IsDisplayApplied(MostWantedFrontendSettings candidate) => candidate != null
            && Screen.width == candidate.width && Screen.height == candidate.height && Screen.fullScreenMode == candidate.fullScreenMode;

        public void RefreshBindings(MostWantedFrontendSettings committed)
        {
            if (committed == null) return;
            initializedAudio.RemoveWhere(world => world == null);
            foreach (var world in UnityEngine.Object.FindObjectsByType<SensoryAudioWorld>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (initializedAudio.Add(world)) ApplyAudio(world, committed.audio);
                world.Mute(SensoryCategory.Music, !committed.eaTraxEnabled);
            }
            var camera = FindPlayerCamera();
            if (camera != null && camera != initializedCamera)
            {
                if (camera.IsHoodView != committed.hoodCamera) camera.ToggleCameraMode();
                initializedCamera = camera;
            }
        }

        private static VehicleCameraRig FindPlayerCamera()
        {
            var session = GameFlowRuntime.Instance != null ? GameFlowRuntime.Instance.WorldSession : null;
            VehicleCameraRig found = null;
            foreach (var camera in UnityEngine.Object.FindObjectsByType<VehicleCameraRig>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!camera.isActiveAndEnabled || camera.Target == null) continue;
                if (session != null)
                {
                    if (camera.Target != session.transform && !camera.Target.IsChildOf(session.transform)) continue;
                }
                else if (camera.Target.GetComponent<PlayerVehicleInput>() == null) continue;
                if (found != null) return null; // Ambiguous ownership is not permission to alter arbitrary cameras.
                found = camera;
            }
            return found;
        }

        private static bool SameAudio(SensoryPreferences left, SensoryPreferences right) => left != null && right != null
            && left.master == right.master && left.vehicle == right.vehicle && left.effects == right.effects
            && left.music == right.music && left.police == right.police && left.flashes == right.flashes;

        private static void ApplyAudio(SensoryAudioWorld world, SensoryPreferences source)
        {
            if (world == null || world.Preferences == null || source == null) return;
            var target = world.Preferences;
            target.master = source.master; target.vehicle = source.vehicle; target.effects = source.effects;
            target.music = source.music; target.police = source.police; target.flashes = source.flashes;
            world.ApplyPreferences(false);
        }
        private static bool IsFrontendAudioState()
        {
            return GameFlowRuntime.Instance?.AllowsFrontendMusic == true;
        }
        private static bool Fail(string message, out string failure) { failure = message; return false; }
    }
}
