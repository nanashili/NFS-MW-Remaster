using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NfsMwRemaster.Driving
{
    public interface IMostWantedFrontendPreferenceStore
    {
        bool TryLoad(MostWantedFrontendSettings defaults, out MostWantedFrontendSettings settings, out string failure);
        bool TrySave(MostWantedFrontendSettings settings, out string failure);
    }

    /// <summary>Main-thread runtime boundary. Apply must not persist; rollback uses the same boundary.</summary>
    public interface IMostWantedFrontendPreferenceBackend
    {
        double Realtime { get; }
        bool IsFocused { get; }
        bool CanChangeDisplay { get; }
        bool CanChangeQuality { get; }
        bool CanChangeCameraView { get; }
        MostWantedFrontendSettings Capture(MostWantedFrontendSettings fallback);
        bool TryValidate(MostWantedFrontendSettings before, MostWantedFrontendSettings candidate, out string failure);
        bool TryApply(MostWantedFrontendSettings before, MostWantedFrontendSettings candidate, out string failure);
        bool IsDisplayApplied(MostWantedFrontendSettings candidate);
        void RefreshBindings(MostWantedFrontendSettings committed);
    }

    /// <summary>
    /// One local preferences transaction over existing consumers. The screen owns rendering, while
    /// GameFlowRuntime ticks this service even with the screen hidden. All operations are main-thread only.
    /// </summary>
    public sealed class MostWantedFrontendPreferences : IDisposable
    {
        public const double VideoConfirmationDuration = 15;
        public const double RebindingDuration = 15;
        private static MostWantedFrontendPreferences runtime;
        private readonly IMostWantedFrontendPreferenceStore store;
        private readonly IMostWantedFrontendPreferenceBackend backend;
        private readonly MostWantedFrontendSettings defaults;
        private MostWantedFrontendSettings current, draft, pending, rollback, restoringDisplay;
        private double videoDeadline, rebindDeadline, nextBindingRefresh, restorationDeadline;
        private bool started, disposed, captureReleased;
        private int rebindFrame, lastCaptureFrame = -1, lastRebindEndFrame = -1;

        public static MostWantedFrontendPreferences Runtime => Initialize(GameFlowRuntime.Instance?.Settings);
        public static MostWantedFrontendPreferences Initialize(GameFlowSettings configuration)
        {
            if (runtime != null && !runtime.disposed) return runtime;
            var live = new MostWantedFrontendUnityBackend();
            var initial = live.Capture(new MostWantedFrontendSettings
            {
                playerAlias = configuration != null ? configuration.DefaultAlias : "free_roam_profile",
                pauseOnFocusLoss = configuration == null || configuration.PauseOnFocusLoss
            });
            runtime = new MostWantedFrontendPreferences(initial, new MostWantedFrontendPlayerPrefsStore(), live);
            return runtime;
        }

        public static void ConfigureQualityAdapter(Action<int> applyQuality)
            => MostWantedFrontendUnityBackend.ConfigureQualityAdapter(applyQuality);

        public MostWantedFrontendPreferences(MostWantedFrontendSettings initial,
            IMostWantedFrontendPreferenceStore preferenceStore, IMostWantedFrontendPreferenceBackend runtimeBackend)
        {
            if (initial == null) throw new ArgumentNullException(nameof(initial));
            store = preferenceStore ?? throw new ArgumentNullException(nameof(preferenceStore));
            backend = runtimeBackend ?? throw new ArgumentNullException(nameof(runtimeBackend));
            defaults = initial.Clone();
            if (!defaults.TryValidate(out string invalid)) throw new ArgumentException(invalid, nameof(initial));
            current = defaults.Clone();
            try
            {
                bool read = store.TryLoad(defaults.Clone(), out var loaded, out string failure);
                if (loaded != null && loaded.TryValidate(out string validation)) current = loaded.Clone();
                else if (read) Status = "The saved preferences are invalid; defaults are in use.";
                if (!read) Status = failure;
            }
            catch (Exception exception) { Status = "Preferences could not be read: " + exception.Message; }
        }

        public MostWantedFrontendSettings Current => current.Clone();
        public MostWantedFrontendSettings Draft => draft;
        public bool IsEditing => draft != null;
        public bool IsDirty => draft != null && !draft.EquivalentTo(current);
        public bool IsRebinding { get; private set; }
        public bool RebindingConsumesInput => IsRebinding || lastRebindEndFrame == Time.frameCount;
        public MostWantedFrontendAction RebindingAction { get; private set; }
        public int RebindingSlot { get; private set; }
        public bool HasPendingVideoChange => pending != null;
        public bool IsRestoringDisplay => restoringDisplay != null;
        public bool RecoveryRequired { get; private set; }
        public bool CanChangeDisplay => backend.CanChangeDisplay;
        public bool CanChangeQuality => backend.CanChangeQuality;
        public bool CanChangeCameraView => backend.CanChangeCameraView;
        public float VideoConfirmationSecondsRemaining => pending == null ? 0 : (float)Math.Max(0, videoDeadline - backend.Realtime);
        public string Status { get; private set; } = string.Empty;
        public bool PauseOnFocusLoss => current.pauseOnFocusLoss;

        public void BeginEdit()
        {
            ThrowIfDisposed();
            if (IsEditing || RecoveryRequired) return;
            EnsureStarted();
            if (RecoveryRequired) return;
            if (IsRestoringDisplay) { draft = current.Clone(); return; }
            // Snapshot live presentation values, including a camera view changed by the driving command.
            try
            {
                var actual = backend.Capture(current);
                if (actual == null || !actual.TryValidate(out _)) { Status = "The current settings could not be captured safely."; return; }
                current = actual.Clone(); draft = current.Clone();
            }
            catch (Exception exception) { Status = "The current settings could not be captured: " + exception.Message; }
        }

        public void RestoreDefaults(MostWantedFrontendPreferenceSection section)
        {
            ThrowIfDisposed();
            if (!IsEditing || HasPendingVideoChange || RecoveryRequired || IsRebinding) return;
            if (!Enum.IsDefined(typeof(MostWantedFrontendPreferenceSection), section)) throw new ArgumentOutOfRangeException(nameof(section));
            bool all = section == MostWantedFrontendPreferenceSection.All;
            if (all || section == MostWantedFrontendPreferenceSection.Audio)
            {
                var sound = new SensoryPreferences();
                draft.audio.master = sound.master; draft.audio.vehicle = sound.vehicle; draft.audio.effects = sound.effects;
                draft.audio.music = sound.music; draft.audio.police = sound.police;
                draft.menuMusic = defaults.menuMusic;
                draft.eaTraxEnabled = defaults.eaTraxEnabled;
                draft.eaTraxOrder = defaults.eaTraxOrder;
            }
            if (all || section == MostWantedFrontendPreferenceSection.Video)
            {
                if (CanChangeDisplay) { draft.width = defaults.width; draft.height = defaults.height; draft.fullScreenMode = defaults.fullScreenMode; }
                if (CanChangeQuality) draft.qualityLevel = defaults.qualityLevel;
                draft.vSyncCount = defaults.vSyncCount; draft.targetFrameRate = defaults.targetFrameRate;
            }
            if (all || section == MostWantedFrontendPreferenceSection.Gameplay)
            {
                draft.pauseOnFocusLoss = defaults.pauseOnFocusLoss;
                draft.audio.flashes = new SensoryPreferences().flashes;
                if (CanChangeCameraView) draft.hoodCamera = defaults.hoodCamera;
            }
            if (all || section == MostWantedFrontendPreferenceSection.Player)
            {
                draft.playerAlias = defaults.playerAlias;
                if (CanChangeCameraView) draft.hoodCamera = defaults.hoodCamera;
            }
            if (all || section == MostWantedFrontendPreferenceSection.Controls) draft.keyboard = new MostWantedFrontendKeyboardBindings();
            Status = "Defaults are staged. Apply to save, or go back to discard.";
        }

        /// <summary>Success with HasPendingVideoChange means preview only; nothing has been saved yet.</summary>
        public bool TryApply(out string failure)
        {
            if (!CanEdit(out failure)) return false;
            var candidate = draft.Clone();
            PreserveUnexposedAudio(candidate);
            if (!candidate.TryValidate(out failure)) return Reject(failure, out failure);
            MostWantedFrontendSettings actual;
            try { actual = backend.Capture(current); }
            catch (Exception exception) { return Reject("Could not snapshot current settings: " + exception.Message, out failure); }
            if (actual == null) return Reject("The runtime did not provide a rollback snapshot.", out failure);
            if (!ValidateRuntime(actual, candidate, out failure)) return false;
            if (!ApplyRuntime(actual, candidate, out failure))
            {
                RestoreRuntime(candidate, actual, ref failure);
                return Reject(failure, out failure);
            }
            if (!candidate.SameDisplay(actual))
            {
                rollback = actual.Clone(); pending = candidate.Clone();
                videoDeadline = backend.Realtime + VideoConfirmationDuration;
                Status = "Keep these display settings? Unconfirmed changes will be reverted.";
                failure = string.Empty; return true;
            }
            return Commit(candidate, actual, out failure);
        }

        public bool ConfirmVideo(out string failure)
        {
            if (disposed || RecoveryRequired || pending == null) return Reject("There is no display change to confirm.", out failure);
            if (!backend.IsFocused || backend.Realtime >= videoDeadline)
            {
                Cancel();
                return Reject(RecoveryRequired ? Status : IsRestoringDisplay
                    ? "The unconfirmed display change was cancelled. Restoration is pending."
                    : "The display change was not confirmed and has been reverted.", out failure);
            }
            try
            {
                if (!backend.IsDisplayApplied(pending)) return Reject("The requested display mode is not active yet.", out failure);
            }
            catch (Exception exception) { return Reject("The display mode could not be verified: " + exception.Message, out failure); }
            var candidate = pending; var previous = rollback;
            pending = rollback = null;
            return Commit(candidate, previous, out failure);
        }

        public void Cancel()
        {
            if (disposed) return;
            CancelRebinding();
            if (pending != null)
            {
                string failure = string.Empty;
                var preview = pending; var previous = rollback;
                pending = rollback = null;
                RestoreRuntime(preview, previous, ref failure);
                Status = RecoveryRequired ? failure : IsRestoringDisplay
                    ? "Restoring the previous display settings. No settings were saved."
                    : "Display changes reverted. No settings were saved.";
            }
            else if (IsEditing && !RecoveryRequired) Status = "Unsaved settings discarded.";
            draft = null;
        }

        public bool TrySetBinding(MostWantedFrontendAction action, int slot, Key key, out string failure)
        {
            if (!CanEdit(out failure)) return false;
            if (!draft.keyboard.TrySet(action, slot, key, out failure)) return Reject(failure, out failure);
            Status = "Binding staged. Apply to save."; return true;
        }

        public string GetBindingDisplay(MostWantedFrontendAction action, int slot = 0)
            => (draft ?? current).keyboard.DisplayName(action, slot);
        public bool IsPressed(Keyboard keyboard, MostWantedFrontendAction action)
            => !disposed && !MapInputFocus.Captured && current.keyboard.IsPressed(action, keyboard);
        public bool WasPressedThisFrame(Keyboard keyboard, MostWantedFrontendAction action)
            => !disposed && !MapInputFocus.Captured && current.keyboard.WasPressedThisFrame(action, keyboard);

        public bool BeginRebind(MostWantedFrontendAction action, int slot, out string failure)
        {
            if (!CanEdit(out failure)) return false;
            if ((int)action < 0 || (int)action >= MostWantedFrontendKeyboardBindings.ActionCount || slot < 0 || slot > 1)
                return Reject("Unknown control or binding slot.", out failure);
            if (Keyboard.current == null) return Reject("Connect a keyboard before changing keyboard bindings.", out failure);
            RebindingAction = action; RebindingSlot = slot; IsRebinding = true;
            lastRebindEndFrame = -1;
            rebindFrame = Time.frameCount; rebindDeadline = backend.Realtime + RebindingDuration;
            captureReleased = !Keyboard.current.anyKey.isPressed;
            MapInputFocus.Acquire(this);
            Status = "Press a key for " + MostWantedFrontendKeyboardBindings.Label(action) + ". Escape cancels.";
            failure = string.Empty; return true;
        }

        public bool CancelRebinding()
        {
            if (!IsRebinding) return lastRebindEndFrame == Time.frameCount;
            IsRebinding = false; lastRebindEndFrame = Time.frameCount; MapInputFocus.Release(this);
            Status = "Binding capture cancelled. No binding was changed."; return true;
        }

        public void Tick()
        {
            if (disposed || RecoveryRequired) return;
            EnsureStarted();
            if (RecoveryRequired) return;
            if (restoringDisplay != null)
            {
                try
                {
                    if (backend.IsDisplayApplied(restoringDisplay))
                    { restoringDisplay = null; Status = "Display changes reverted. No settings were saved."; }
                    else if (backend.Realtime >= restorationDeadline)
                    { RecoveryRequired = true; Status = "The previous display mode could not be verified after restoration. Restart before applying more settings."; }
                }
                catch (Exception exception) { RecoveryRequired = true; Status = "Display restoration could not be verified: " + exception.Message; }
                return;
            }
            if (pending != null && (!backend.IsFocused || backend.Realtime >= videoDeadline))
            {
                Cancel(); return;
            }
            TickRebinding();
            if (!IsEditing && backend.Realtime >= nextBindingRefresh)
            {
                nextBindingRefresh = backend.Realtime + 1;
                try { backend.RefreshBindings(current.Clone()); }
                catch (Exception exception) { Status = "A runtime settings binding is unavailable: " + exception.Message; }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            Cancel(); MapInputFocus.Release(this); disposed = true;
        }

        private void EnsureStarted()
        {
            if (started) return;
            started = true;
            MostWantedFrontendSettings actual;
            try { actual = backend.Capture(current); }
            catch (Exception exception) { Status = "Saved settings could not be applied: " + exception.Message; return; }
            if (actual == null) { Status = "Saved settings could not be applied: no runtime snapshot."; return; }
            var candidate = current.Clone();
            // Monitor/platform changes cannot force an unavailable saved display mode. Other preferences survive.
            if (!ValidateRuntime(actual, candidate, out string failure))
            {
                candidate.width = actual.width; candidate.height = actual.height; candidate.fullScreenMode = actual.fullScreenMode;
                candidate.qualityLevel = actual.qualityLevel; candidate.hoodCamera = actual.hoodCamera;
                Status = "Saved presentation settings are unavailable on this configuration: " + failure;
            }
            if (!ApplyRuntime(actual, candidate, out failure))
            {
                RestoreRuntime(candidate, actual, ref failure);
                Status = failure; return;
            }
            current = candidate;
        }

        private bool Commit(MostWantedFrontendSettings candidate, MostWantedFrontendSettings previous, out string failure)
        {
            bool saved;
            try { saved = store.TrySave(candidate.Clone(), out failure); }
            catch (Exception exception) { saved = false; failure = "Could not save settings: " + exception.Message; }
            if (!saved)
            {
                RestoreRuntime(candidate, previous, ref failure);
                if (store is IMostWantedFrontendPreferenceStoreHealth health && health.RequiresReload) RecoveryRequired = true;
                return Reject(failure, out failure);
            }
            current = candidate.Clone(); draft = null;
            Status = "Settings saved."; failure = string.Empty; return true;
        }

        private void PreserveUnexposedAudio(MostWantedFrontendSettings candidate)
        {
            if (candidate.audio == null || current.audio == null) return;
            candidate.audio.cameraMotion = current.audio.cameraMotion;
            candidate.audio.haptics = current.audio.haptics;
            candidate.audio.subtitles = current.audio.subtitles;
        }

        private bool ValidateRuntime(MostWantedFrontendSettings before, MostWantedFrontendSettings candidate, out string failure)
        {
            try
            {
                if (backend.TryValidate(before, candidate, out failure)) return true;
                return Reject(failure, out failure);
            }
            catch (Exception exception) { return Reject("The runtime rejected the settings: " + exception.Message, out failure); }
        }

        private bool ApplyRuntime(MostWantedFrontendSettings before, MostWantedFrontendSettings candidate, out string failure)
        {
            try { return backend.TryApply(before, candidate, out failure); }
            catch (Exception exception) { failure = "Could not apply settings: " + exception.Message; return false; }
        }

        private void RestoreRuntime(MostWantedFrontendSettings preview, MostWantedFrontendSettings previous, ref string failure)
        {
            string rollbackFailure = "The rollback snapshot is missing.";
            if (previous != null && ApplyRuntime(preview, previous, out rollbackFailure))
            {
                if (preview != null && !preview.SameDisplay(previous))
                {
                    try
                    {
                        if (!backend.IsDisplayApplied(previous))
                        { restoringDisplay = previous.Clone(); restorationDeadline = backend.Realtime + VideoConfirmationDuration; }
                    }
                    catch (Exception exception)
                    { restoringDisplay = previous.Clone(); restorationDeadline = backend.Realtime + VideoConfirmationDuration; Status = exception.Message; }
                }
                return;
            }
            RecoveryRequired = true;
            failure = (failure ?? string.Empty) + " Runtime restoration failed; restart before applying more settings. " + rollbackFailure;
        }

        private void TickRebinding()
        {
            if (!IsRebinding) return;
            var keyboard = Keyboard.current;
            if (!backend.IsFocused || keyboard == null || backend.Realtime >= rebindDeadline)
            { CancelRebinding(); return; }
            if (Time.frameCount <= rebindFrame || lastCaptureFrame == Time.frameCount) return;
            lastCaptureFrame = Time.frameCount;
            if (keyboard.escapeKey.wasPressedThisFrame) { CancelRebinding(); return; }
            if (!captureReleased) { captureReleased = !keyboard.anyKey.isPressed; return; }
            foreach (var key in keyboard.allKeys)
            {
                if (!key.wasPressedThisFrame) continue;
                if (!draft.keyboard.TrySet(RebindingAction, RebindingSlot, key.keyCode, out string failure))
                { Status = failure + " Press another key, or Escape to cancel."; return; }
                IsRebinding = false; lastRebindEndFrame = Time.frameCount; MapInputFocus.Release(this);
                Status = "Binding staged. Apply to save."; return;
            }
        }

        private bool CanEdit(out string failure)
        {
            if (disposed) return Reject("The preferences session is closed.", out failure);
            if (RecoveryRequired) return Reject("Restart before applying more settings; the previous rollback failed.", out failure);
            if (!IsEditing) return Reject("Open a settings page before applying changes.", out failure);
            if (IsRestoringDisplay) return Reject("The previous display mode is still being restored.", out failure);
            if (pending != null) return Reject("Confirm or cancel the display preview first.", out failure);
            if (IsRebinding) return Reject("Finish or cancel binding capture first.", out failure);
            failure = string.Empty; return true;
        }

        private bool Reject(string reason, out string failure)
        { Status = failure = string.IsNullOrEmpty(reason) ? "The settings operation failed." : reason; return false; }
        private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(MostWantedFrontendPreferences)); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            runtime?.Dispose(); runtime = null;
            MostWantedFrontendUnityBackend.ConfigureQualityAdapter(null);
        }
    }

    public static class MostWantedFrontendBindings
    {
        public static bool IsPressed(Keyboard keyboard, MostWantedFrontendAction action)
            => MostWantedFrontendPreferences.Runtime.IsPressed(keyboard, action);
        public static bool WasPressedThisFrame(Keyboard keyboard, MostWantedFrontendAction action)
            => MostWantedFrontendPreferences.Runtime.WasPressedThisFrame(keyboard, action);
    }
}
