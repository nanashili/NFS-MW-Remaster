using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class MostWantedFrontendPreferencesTests
    {
        private MostWantedFrontendSettings initial;
        private FrontendMemoryStore store;
        private FrontendTestBackend backend;
        private MostWantedFrontendPreferences preferences;

        [SetUp]
        public void SetUp()
        {
            initial = new MostWantedFrontendSettings { qualityLevel = 2 };
            store = new FrontendMemoryStore(); backend = new FrontendTestBackend(initial);
            preferences = new MostWantedFrontendPreferences(initial, store, backend);
        }

        [TearDown] public void TearDown() => preferences.Dispose();

        [Test]
        public void CurrentAndDraftAreDetachedFromCommittedData()
        {
            var returned = preferences.Current;
            returned.audio.music = 0; returned.keyboard.primary[0] = Key.I;
            preferences.BeginEdit(); preferences.Draft.audio.music = .1f;
            Assert.That(preferences.Current.audio.music, Is.EqualTo(initial.audio.music));
            Assert.That(preferences.Current.keyboard.primary[0], Is.EqualTo(Key.A));
            Assert.That(preferences.IsDirty, Is.True);
        }

        [Test]
        public void SavedPauseOnFocusLossIsReadableBeforeTheFirstRuntimeTick()
        {
            preferences.Dispose();
            store.Saved = initial.Clone(); store.Saved.pauseOnFocusLoss = false;
            preferences = new MostWantedFrontendPreferences(initial, store, backend);
            Assert.That(preferences.PauseOnFocusLoss, Is.False);
            Assert.That(preferences.Current.pauseOnFocusLoss, Is.False);
            Assert.That(backend.ApplyCount, Is.Zero);
            Assert.That(store.SaveCount, Is.Zero);
        }

        [Test]
        public void CancelAnUnappliedDraftDoesNotTouchRuntimeOrStorage()
        {
            preferences.BeginEdit(); int applies = backend.ApplyCount;
            preferences.Draft.audio.master = .2f; preferences.Draft.playerAlias = "another_profile";
            preferences.Cancel();
            Assert.That(preferences.IsEditing, Is.False);
            Assert.That(backend.ApplyCount, Is.EqualTo(applies));
            Assert.That(store.SaveCount, Is.Zero);
            Assert.That(backend.Live.audio.master, Is.EqualTo(initial.audio.master));
        }

        [Test]
        public void BeginEditIsIdempotentAcrossNestedSettingsPages()
        {
            preferences.BeginEdit(); preferences.Draft.audio.music = .2f;
            var draft = preferences.Draft; preferences.BeginEdit();
            Assert.That(preferences.Draft, Is.SameAs(draft));
            Assert.That(preferences.Draft.audio.music, Is.EqualTo(.2f));
        }

        [Test]
        public void DefaultsAreStagedAndLimitedToTheirSection()
        {
            preferences.BeginEdit();
            preferences.Draft.audio.master = .1f; preferences.Draft.audio.flashes = false;
            preferences.Draft.pauseOnFocusLoss = false; preferences.Draft.playerAlias = "keep_alias";
            preferences.RestoreDefaults(MostWantedFrontendPreferenceSection.Audio);
            Assert.That(preferences.Draft.audio.master, Is.EqualTo(new SensoryPreferences().master));
            Assert.That(preferences.Draft.audio.flashes, Is.False);
            Assert.That(preferences.Draft.pauseOnFocusLoss, Is.False);
            Assert.That(preferences.Draft.playerAlias, Is.EqualTo("keep_alias"));
            Assert.That(store.SaveCount, Is.Zero);
        }

        [Test]
        public void ApplySavesDetachedSnapshotAndClosesDraft()
        {
            preferences.BeginEdit(); var draft = preferences.Draft;
            draft.audio.master = .2f; draft.pauseOnFocusLoss = false; draft.playerAlias = "saved_alias";
            Assert.That(preferences.TryApply(out string failure), Is.True, failure);
            draft.audio.master = 1;
            Assert.That(preferences.Current.audio.master, Is.EqualTo(.2f));
            Assert.That(preferences.PauseOnFocusLoss, Is.False);
            Assert.That(store.Saved.playerAlias, Is.EqualTo("saved_alias"));
            Assert.That(preferences.IsEditing, Is.False);
            Assert.That(store.SaveCount, Is.EqualTo(1));
        }

        [Test]
        public void UnexposedSensoryFieldsCannotBeChangedThroughTheFrontend()
        {
            preferences.BeginEdit();
            preferences.Draft.audio.haptics = 0; preferences.Draft.audio.cameraMotion = 0;
            preferences.Draft.audio.subtitles = !initial.audio.subtitles;
            preferences.Draft.audio.music = .4f;
            Assert.That(preferences.TryApply(out string failure), Is.True, failure);
            Assert.That(store.Saved.audio.haptics, Is.EqualTo(initial.audio.haptics));
            Assert.That(store.Saved.audio.cameraMotion, Is.EqualTo(initial.audio.cameraMotion));
            Assert.That(store.Saved.audio.subtitles, Is.EqualTo(initial.audio.subtitles));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-.1f)]
        [TestCase(1.1f)]
        public void InvalidAudioIsRejectedBeforeAnySideEffects(float invalid)
        {
            preferences.BeginEdit(); int applies = backend.ApplyCount;
            preferences.Draft.audio.music = invalid;
            Assert.That(preferences.TryApply(out string failure), Is.False);
            Assert.That(failure, Does.Contain("Audio"));
            Assert.That(backend.ApplyCount, Is.EqualTo(applies));
            Assert.That(store.SaveCount, Is.Zero);
        }

        [Test]
        public void FailedPersistenceRestoresRuntimeAndRetainsDraft()
        {
            preferences.BeginEdit(); preferences.Draft.audio.master = .1f;
            store.FailSave = true;
            Assert.That(preferences.TryApply(out string failure), Is.False);
            Assert.That(failure, Does.Contain("storage"));
            Assert.That(backend.Live.audio.master, Is.EqualTo(initial.audio.master));
            Assert.That(preferences.Current.audio.master, Is.EqualTo(initial.audio.master));
            Assert.That(preferences.Draft.audio.master, Is.EqualTo(.1f));
        }

        [Test]
        public void APartialBackendFailureIsRolledBackBeforeReturningFailure()
        {
            preferences.BeginEdit(); preferences.Draft.audio.music = .15f;
            backend.FailApplyCount = 1;
            Assert.That(preferences.TryApply(out _), Is.False);
            Assert.That(backend.Live.audio.music, Is.EqualTo(initial.audio.music));
            Assert.That(preferences.RecoveryRequired, Is.False);
            Assert.That(store.SaveCount, Is.Zero);
        }

        [Test]
        public void RollbackFailureDisablesFurtherCommits()
        {
            preferences.BeginEdit(); preferences.Draft.audio.music = .15f;
            backend.FailApplyCount = 2;
            Assert.That(preferences.TryApply(out string failure), Is.False);
            Assert.That(preferences.RecoveryRequired, Is.True);
            Assert.That(failure, Does.Contain("restoration failed"));
            int calls = backend.ApplyCount;
            Assert.That(preferences.TryApply(out _), Is.False);
            Assert.That(backend.ApplyCount, Is.EqualTo(calls));
            Assert.That(store.SaveCount, Is.Zero);
        }

        [Test]
        public void UnsupportedQualityChangeCannotMasqueradeAsApplied()
        {
            preferences.BeginEdit(); backend.CanChangeQuality = false;
            preferences.Draft.qualityLevel = 3;
            Assert.That(preferences.TryApply(out string failure), Is.False);
            Assert.That(failure, Does.Contain("quality"));
            Assert.That(store.SaveCount, Is.Zero);
            Assert.That(backend.Live.qualityLevel, Is.EqualTo(2));
        }

        [Test]
        public void VideoPreviewDoesNotCommitOtherSettingsBeforeConfirmation()
        {
            StageVideo(); preferences.Draft.audio.master = .2f; preferences.Draft.pauseOnFocusLoss = false;
            Assert.That(preferences.TryApply(out string failure), Is.True, failure);
            Assert.That(preferences.HasPendingVideoChange, Is.True);
            Assert.That(backend.Live.width, Is.EqualTo(1280));
            Assert.That(preferences.Current.width, Is.EqualTo(1920));
            Assert.That(preferences.PauseOnFocusLoss, Is.True);
            Assert.That(store.SaveCount, Is.Zero);
            Assert.That(preferences.ConfirmVideo(out failure), Is.True, failure);
            Assert.That(preferences.HasPendingVideoChange, Is.False);
            Assert.That(preferences.Current.width, Is.EqualTo(1280));
            Assert.That(preferences.PauseOnFocusLoss, Is.False);
            Assert.That(store.SaveCount, Is.EqualTo(1));
        }

        [Test]
        public void MutatingDraftCannotChangeAlreadyRequestedDisplayConfirmation()
        {
            StageVideo(); Assert.That(preferences.TryApply(out _), Is.True);
            preferences.Draft.width = 1024;
            Assert.That(preferences.TryApply(out _), Is.False);
            Assert.That(preferences.ConfirmVideo(out string failure), Is.True, failure);
            Assert.That(preferences.Current.width, Is.EqualTo(1280));
            Assert.That(store.Saved.width, Is.EqualTo(1280));
        }

        [Test]
        public void DisplayMustActuallyBeActiveBeforeItCanBeConfirmed()
        {
            StageVideo(); Assert.That(preferences.TryApply(out _), Is.True);
            backend.DisplayReady = false;
            Assert.That(preferences.ConfirmVideo(out string failure), Is.False);
            Assert.That(failure, Does.Contain("not active yet"));
            Assert.That(store.SaveCount, Is.Zero);
            Assert.That(preferences.HasPendingVideoChange, Is.True);
        }

        [Test]
        public void VideoTimeoutUsesUnscaledBackendTimeAndRestoresEverything()
        {
            StageVideo(); preferences.Draft.audio.master = .1f;
            Assert.That(preferences.TryApply(out _), Is.True);
            backend.Now += MostWantedFrontendPreferences.VideoConfirmationDuration + .1;
            preferences.Tick();
            Assert.That(preferences.HasPendingVideoChange, Is.False);
            Assert.That(preferences.IsEditing, Is.False);
            Assert.That(backend.Live.width, Is.EqualTo(1920));
            Assert.That(backend.Live.audio.master, Is.EqualTo(initial.audio.master));
            Assert.That(store.SaveCount, Is.Zero);
        }

        [Test]
        public void FocusLossCancelsDisplayPreviewEvenWithoutVisibleUi()
        {
            StageVideo(); Assert.That(preferences.TryApply(out _), Is.True);
            backend.IsFocused = false; preferences.Tick();
            Assert.That(backend.Live.width, Is.EqualTo(1920));
            Assert.That(store.SaveCount, Is.Zero);
            Assert.That(preferences.HasPendingVideoChange, Is.False);
        }

        [Test]
        public void DisposingTheServiceRestoresAnUnconfirmedDisplay()
        {
            StageVideo(); Assert.That(preferences.TryApply(out _), Is.True);
            preferences.Dispose(); preferences.Dispose();
            Assert.That(backend.Live.width, Is.EqualTo(1920));
            Assert.That(store.SaveCount, Is.Zero);
        }

        [Test]
        public void FailedConfirmSaveRestoresPreviewAndKeepsOldCommittedValues()
        {
            StageVideo(); Assert.That(preferences.TryApply(out _), Is.True); store.FailSave = true;
            Assert.That(preferences.ConfirmVideo(out _), Is.False);
            Assert.That(backend.Live.width, Is.EqualTo(1920));
            Assert.That(preferences.Current.width, Is.EqualTo(1920));
            Assert.That(preferences.HasPendingVideoChange, Is.False);
        }

        [Test]
        public void FailedCancelCannotReportThatVideoWasReverted()
        {
            StageVideo(); Assert.That(preferences.TryApply(out _), Is.True);
            backend.FailApplyCount = 1; backend.Now += 16;
            Assert.That(preferences.ConfirmVideo(out string failure), Is.False);
            Assert.That(preferences.RecoveryRequired, Is.True);
            Assert.That(failure, Does.Contain("restoration failed"));
        }

        [Test]
        public void DeferredDisplayRestorationCannotLeakPreviewValuesIntoANewDraft()
        {
            StageVideo(); Assert.That(preferences.TryApply(out _), Is.True);
            var preview = backend.Live.Clone(); backend.DisplayReady = false;
            preferences.Cancel(); Assert.That(preferences.IsRestoringDisplay, Is.True);
            backend.Live = preview; // The OS has not completed the requested rollback yet.
            preferences.BeginEdit();
            Assert.That(preferences.Draft.width, Is.EqualTo(1920));
            Assert.That(preferences.TryApply(out _), Is.False);
            backend.Live = initial.Clone(); backend.DisplayReady = true; backend.Now += 1;
            preferences.Tick(); Assert.That(preferences.IsRestoringDisplay, Is.False);
            Assert.That(store.SaveCount, Is.Zero);
        }

        [Test]
        public void UnverifiedDisplayRestorationEventuallyRequiresRecovery()
        {
            StageVideo(); Assert.That(preferences.TryApply(out _), Is.True);
            backend.DisplayReady = false; preferences.Cancel();
            backend.Now += MostWantedFrontendPreferences.VideoConfirmationDuration + 1;
            preferences.Tick();
            Assert.That(preferences.RecoveryRequired, Is.True);
            Assert.That(preferences.Status, Does.Contain("could not be verified"));
            Assert.That(store.SaveCount, Is.Zero);
        }

        [TestCase("../profile")]
        [TestCase("space name")]
        [TestCase("")]
        [TestCase(" alias")]
        public void AliasValidationMatchesTheExistingCareerEntryRules(string alias)
        {
            preferences.BeginEdit(); preferences.Draft.playerAlias = alias;
            Assert.That(preferences.TryApply(out _), Is.False);
            Assert.That(store.SaveCount, Is.Zero);
        }

        [Test]
        public void BindingChangesAreStagedAndConflictsDoNotModifyTheDraft()
        {
            preferences.BeginEdit();
            Assert.That(preferences.TrySetBinding(MostWantedFrontendAction.Throttle, 0, Key.I, out string failure), Is.True, failure);
            Assert.That(preferences.Current.keyboard.Get(MostWantedFrontendAction.Throttle), Is.EqualTo(Key.W));
            Assert.That(preferences.TrySetBinding(MostWantedFrontendAction.Brake, 0, Key.I, out failure), Is.False);
            Assert.That(preferences.Draft.keyboard.Get(MostWantedFrontendAction.Brake), Is.EqualTo(Key.S));
            preferences.Cancel(); Assert.That(preferences.Current.keyboard.Get(MostWantedFrontendAction.Throttle), Is.EqualTo(Key.W));
        }

        [TestCase(Key.Escape)] [TestCase(Key.M)] [TestCase(Key.E)] [TestCase(Key.R)] [TestCase(Key.X)]
        [TestCase(Key.Q)] [TestCase(Key.N)] [TestCase(Key.B)] [TestCase(Key.LeftMeta)]
        public void UnmigratedGameCommandsRemainReserved(Key key)
        {
            preferences.BeginEdit();
            Assert.That(preferences.TrySetBinding(MostWantedFrontendAction.Throttle, 0, key, out _), Is.False);
        }

        [Test]
        public void RebindingCannotRemoveTheLastControlForAnAction()
        {
            preferences.BeginEdit();
            Assert.That(preferences.TrySetBinding(MostWantedFrontendAction.Throttle, 1, Key.None, out _), Is.True);
            Assert.That(preferences.TrySetBinding(MostWantedFrontendAction.Throttle, 0, Key.None, out _), Is.False);
            Assert.That(preferences.Draft.keyboard.Get(MostWantedFrontendAction.Throttle), Is.EqualTo(Key.W));
        }

        private void StageVideo()
        { preferences.BeginEdit(); preferences.Draft.width = 1280; preferences.Draft.height = 720; }
    }

    internal sealed class FrontendMemoryStore : IMostWantedFrontendPreferenceStore
    {
        public MostWantedFrontendSettings Saved;
        public bool FailSave;
        public int SaveCount;
        public bool TryLoad(MostWantedFrontendSettings defaults, out MostWantedFrontendSettings settings, out string failure)
        { settings = (Saved ?? defaults).Clone(); failure = string.Empty; return true; }
        public bool TrySave(MostWantedFrontendSettings settings, out string failure)
        {
            SaveCount++;
            if (FailSave) { failure = "Injected storage failure."; return false; }
            Saved = settings.Clone(); failure = string.Empty; return true;
        }
    }

    internal sealed class FrontendTestBackend : IMostWantedFrontendPreferenceBackend
    {
        public MostWantedFrontendSettings Live;
        public int ApplyCount, FailApplyCount;
        public bool DisplayReady = true;
        public double Now;
        public FrontendTestBackend(MostWantedFrontendSettings initial) { Live = initial.Clone(); }
        public double Realtime => Now;
        public bool IsFocused { get; set; } = true;
        public bool CanChangeDisplay { get; set; } = true;
        public bool CanChangeQuality { get; set; } = true;
        public bool CanChangeCameraView { get; set; } = true;
        public MostWantedFrontendSettings Capture(MostWantedFrontendSettings fallback) => Live.Clone();
        public bool TryValidate(MostWantedFrontendSettings before, MostWantedFrontendSettings candidate, out string failure)
        {
            if (!CanChangeQuality && before.qualityLevel != candidate.qualityLevel) { failure = "No quality adapter."; return false; }
            if (!CanChangeDisplay && !candidate.SameDisplay(before)) { failure = "No display adapter."; return false; }
            if (!CanChangeCameraView && candidate.hoodCamera != before.hoodCamera) { failure = "No camera adapter."; return false; }
            failure = string.Empty; return true;
        }
        public bool TryApply(MostWantedFrontendSettings before, MostWantedFrontendSettings candidate, out string failure)
        {
            ApplyCount++; Live = candidate.Clone();
            if (FailApplyCount > 0) { FailApplyCount--; failure = "Injected partial runtime failure."; return false; }
            failure = string.Empty; return true;
        }
        public bool IsDisplayApplied(MostWantedFrontendSettings candidate) => DisplayReady && candidate.SameDisplay(Live);
        public void RefreshBindings(MostWantedFrontendSettings committed) { }
    }
}
