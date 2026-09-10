using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class MostWantedFrontendPreferenceStorageTests
    {
        private FrontendKeyValueMemory values;
        private MostWantedFrontendPlayerPrefsStore store;
        private MostWantedFrontendSettings defaults;
        [SetUp]
        public void SetUp()
        { values = new FrontendKeyValueMemory(); store = new MostWantedFrontendPlayerPrefsStore(values); defaults = new MostWantedFrontendSettings(); }

        [Test]
        public void RoundTripUsesTheOriginalSensoryKeyAndKeepsUnexposedValues()
        {
            defaults.audio.master = .21f; defaults.audio.cameraMotion = .37f; defaults.audio.subtitles = false;
            defaults.playerAlias = "saved_01"; defaults.pauseOnFocusLoss = false;
            defaults.menuMusic = .38f;
            Assert.That(store.TrySave(defaults, out string failure), Is.True, failure);
            Assert.That(values.GetString(MostWantedFrontendPlayerPrefsStore.FrontendKey), Does.Not.Contain("\"master\""));
            Assert.That(values.GetString(MostWantedFrontendPlayerPrefsStore.SensoryKey), Does.Contain("\"master\""));
            Assert.That(store.TryLoad(new MostWantedFrontendSettings(), out var loaded, out failure), Is.True, failure);
            Assert.That(loaded.audio.master, Is.EqualTo(.21f));
            Assert.That(loaded.audio.cameraMotion, Is.EqualTo(.37f));
            Assert.That(loaded.audio.subtitles, Is.False);
            Assert.That(loaded.pauseOnFocusLoss, Is.False);
            Assert.That(loaded.playerAlias, Is.EqualTo("saved_01"));
            Assert.That(loaded.menuMusic, Is.EqualTo(.38f));
        }

        [Test]
        public void ExistingSensoryPreferencesLoadWithoutAnyFrontendDocument()
        {
            values.SetString(MostWantedFrontendPlayerPrefsStore.SensoryKey, "{\"master\":0.25,\"music\":0.15,\"haptics\":0.42}");
            Assert.That(store.TryLoad(defaults, out var loaded, out string failure), Is.True, failure);
            Assert.That(loaded.audio.master, Is.EqualTo(.25f));
            Assert.That(loaded.audio.music, Is.EqualTo(.15f));
            Assert.That(loaded.audio.haptics, Is.EqualTo(.42f));
            Assert.That(values.SaveCalls, Is.Zero);
        }

        [Test]
        public void AStaleEmbeddedAudioObjectCannotOverrideTheCanonicalSensoryKey()
        {
            values.SetString(MostWantedFrontendPlayerPrefsStore.FrontendKey, "{\"version\":1,\"audio\":{\"master\":1,\"music\":1}}");
            values.SetString(MostWantedFrontendPlayerPrefsStore.SensoryKey, "{\"master\":0.25,\"music\":0.15}");
            Assert.That(store.TryLoad(defaults, out var loaded, out string failure), Is.True, failure);
            Assert.That(loaded.audio.master, Is.EqualTo(.25f));
            Assert.That(loaded.audio.music, Is.EqualTo(.15f));
        }

        [Test]
        public void ForwardVersionIsNotOverwrittenAndDoesNotDiscardKnownAudio()
        {
            const string document = "{\"version\":999,\"futureSetting\":true}";
            values.SetString(MostWantedFrontendPlayerPrefsStore.FrontendKey, document);
            values.SetString(MostWantedFrontendPlayerPrefsStore.SensoryKey, "{\"master\":0.25}");
            Assert.That(store.TryLoad(defaults, out var loaded, out _), Is.False);
            Assert.That(loaded.audio.master, Is.EqualTo(.25f));
            Assert.That(store.TrySave(defaults, out _), Is.False);
            Assert.That(values.GetString(MostWantedFrontendPlayerPrefsStore.FrontendKey), Is.EqualTo(document));
            Assert.That(values.SaveCalls, Is.Zero);
        }

        [Test]
        public void ANewerFrontendDocumentIsProtectedEvenWhenSensoryLoadingFailsFirst()
        {
            const string document = "{\"version\":999}";
            values.SetString(MostWantedFrontendPlayerPrefsStore.FrontendKey, document);
            values.SetString(MostWantedFrontendPlayerPrefsStore.SensoryKey, "corrupt audio");
            Assert.That(store.TryLoad(defaults, out _, out _), Is.False);
            Assert.That(store.TrySave(defaults, out _), Is.False);
            Assert.That(values.GetString(MostWantedFrontendPlayerPrefsStore.FrontendKey), Is.EqualTo(document));
            Assert.That(values.SaveCalls, Is.Zero);
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("[1,2,3]")]
        [TestCase("{\"version\":1,\"keyboard\":{\"primary\":[],\"secondary\":[]}}")]
        public void MalformedPreferencesReturnDefaultsWithoutDeletingStoredData(string document)
        {
            values.SetString(MostWantedFrontendPlayerPrefsStore.FrontendKey, document);
            Assert.That(store.TryLoad(defaults, out var loaded, out _), Is.False);
            Assert.That(loaded.TryValidate(out _), Is.True);
            Assert.That(values.GetString(MostWantedFrontendPlayerPrefsStore.FrontendKey), Is.EqualTo(document));
            Assert.That(values.SaveCalls, Is.Zero);
        }

        [Test]
        public void FailedSaveRestoresBothPriorDocumentsAndLeavesOtherPreferencesUntouched()
        {
            Assert.That(store.TrySave(defaults, out _), Is.True);
            string oldFrontend = values.GetString(MostWantedFrontendPlayerPrefsStore.FrontendKey);
            string oldAudio = values.GetString(MostWantedFrontendPlayerPrefsStore.SensoryKey);
            values.SetString("other.system", "preserve"); values.FailSaves = 1;
            defaults.audio.music = .12f; defaults.playerAlias = "changed";
            Assert.That(store.TrySave(defaults, out _), Is.False);
            Assert.That(values.GetString(MostWantedFrontendPlayerPrefsStore.FrontendKey), Is.EqualTo(oldFrontend));
            Assert.That(values.GetString(MostWantedFrontendPlayerPrefsStore.SensoryKey), Is.EqualTo(oldAudio));
            Assert.That(values.GetString("other.system"), Is.EqualTo("preserve"));
            Assert.That(store.RequiresReload, Is.False);
        }

        [Test]
        public void AFailedFirstSaveDoesNotLeaveKeysThatDidNotPreviouslyExist()
        {
            values.FailSaves = 1;
            Assert.That(store.TrySave(defaults, out _), Is.False);
            Assert.That(values.HasKey(MostWantedFrontendPlayerPrefsStore.FrontendKey), Is.False);
            Assert.That(values.HasKey(MostWantedFrontendPlayerPrefsStore.SensoryKey), Is.False);
        }

        [Test]
        public void AnUncertainStorageRollbackFailsClosed()
        {
            values.FailSaves = 2;
            Assert.That(store.TrySave(defaults, out string failure), Is.False);
            Assert.That(store.RequiresReload, Is.True);
            Assert.That(failure, Does.Contain("rollback also failed"));
            int calls = values.SaveCalls;
            Assert.That(store.TrySave(defaults, out _), Is.False);
            Assert.That(values.SaveCalls, Is.EqualTo(calls));
        }
    }

    internal sealed class FrontendKeyValueMemory : IMostWantedFrontendKeyValueStore
    {
        private readonly Dictionary<string, string> data = new Dictionary<string, string>();
        public int FailSaves, SaveCalls;
        public bool HasKey(string key) => data.ContainsKey(key);
        public string GetString(string key) => data.TryGetValue(key, out var value) ? value : string.Empty;
        public void SetString(string key, string value) { data[key] = value; }
        public void DeleteKey(string key) { data.Remove(key); }
        public void Save()
        {
            SaveCalls++;
            if (FailSaves > 0) { FailSaves--; throw new InvalidOperationException("Injected preferences save failure."); }
        }
    }
}
