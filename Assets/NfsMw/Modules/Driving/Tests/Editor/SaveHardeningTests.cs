using System;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class SaveHardeningTests
    {
        private string directory;
        [SetUp] public void SetUp() { directory = Path.Combine(Path.GetTempPath(), "nfs-save-test-" + Guid.NewGuid().ToString("N")); }
        [TearDown] public void TearDown() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        private static string Profile(int cash = 100, string slot = "career")
        { var p = CareerProfileData.Create(slot, "Test Driver"); p.wallet.balance = cash; return JsonUtility.ToJson(p); }
        private string[] Files() => Directory.GetFiles(directory, "*.save", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        [Test]
        public void FloatingPointValidationDoesNotDependOnEditorCulture()
        {
            var profile = CareerProfileData.Create("career", "Test Driver");
            profile.weather = new CareerWeatherData();
            profile.bounty.pursuitActive = true;
            profile.bounty.pursuitDurationSeconds = 12.5f;
            profile.weather.transitionCooldowns = new[] { 1.25f };
            string json = JsonUtility.ToJson(profile);
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Assert.That(
                    CareerSaveCodec.Validate("career", json),
                    Is.EqualTo(CareerProfileData.CurrentVersion));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [TestCase(SaveWriteStage.Validated)] [TestCase(SaveWriteStage.Created)]
        [TestCase(SaveWriteStage.QuarterWritten)] [TestCase(SaveWriteStage.HalfWritten)]
        [TestCase(SaveWriteStage.Written)] [TestCase(SaveWriteStage.Flushed)] [TestCase(SaveWriteStage.Verified)]
        [TestCase(SaveWriteStage.Promoted)] [TestCase(SaveWriteStage.Retained)]
        public void FailureAtEveryStageRetainsCoherentCommittedState(SaveWriteStage stage)
        {
            var repository = new CareerSaveRepository(directory);
            SaveReadResult first = repository.Commit("career", Profile(100), null);
            var failing = new CareerSaveRepository(directory, 3, point => { if (point == stage) throw new IOException("Injected disk failure"); });
            if (stage < SaveWriteStage.Promoted) Assert.Throws<IOException>(() => failing.Commit("career", Profile(200), first.HeadStamp));
            else Assert.That(failing.Commit("career", Profile(200), first.HeadStamp).Payload, Is.EqualTo(Profile(200)));
            var restarted = new CareerSaveRepository(directory);
            Assert.That(restarted.Load("career").Payload, Is.EqualTo(Profile(stage < SaveWriteStage.Promoted ? 100 : 200)));
            Assert.That(File.Exists(Files()[0]), Is.True);
        }

        [Test]
        public void CorruptLatestRecoversAndKeepsEvidenceAcrossFurtherSaves()
        {
            var repository = new CareerSaveRepository(directory);
            SaveReadResult saved = repository.Commit("career", Profile(100), null);
            saved = repository.Commit("career", Profile(200), saved.HeadStamp);
            string bad = Files().Last(); byte[] bytes = File.ReadAllBytes(bad); bytes[bytes.Length - 1] ^= 1; File.WriteAllBytes(bad, bytes);
            SaveReadResult recovered = repository.Load("career");
            Assert.That(recovered.Payload, Is.EqualTo(Profile(100))); Assert.That(recovered.Recovery, Does.Contain("Recovered"));
            var next = repository.Commit("career", Profile(300), recovered.HeadStamp);
            Assert.That(next.Header.generation, Is.EqualTo(3)); Assert.That(File.ReadAllBytes(bad), Is.EqualTo(bytes));
        }

        [Test]
        public void StaleWriterCannotPublishAndCurrentOperationRetriesAreIdempotent()
        {
            var repository = new CareerSaveRepository(directory);
            SaveReadResult first = repository.Commit("career", Profile(), null);
            string operation = Guid.NewGuid().ToString("N");
            SaveReadResult second = repository.Commit("career", Profile(200), first.HeadStamp, false, operation);
            Assert.That(repository.Commit("career", Profile(200), first.HeadStamp, false, operation).Header.generation, Is.EqualTo(2));
            Assert.That(Assert.Throws<SaveException>(() => repository.Commit("career", Profile(300), first.HeadStamp)).Error, Is.EqualTo(SaveError.Conflict));
            Assert.Throws<SaveException>(() => repository.Commit("career", Profile(999), second.HeadStamp, false, operation));
            Assert.That(repository.Load("career").Payload, Is.EqualTo(Profile(200)));
        }

        [Test]
        public void TwoRepositoryInstancesUseSameExclusiveLock()
        {
            var repository = new CareerSaveRepository(directory);
            var first = repository.Commit("career", Profile(), null);
            bool rejected = false;
            var writer = new CareerSaveRepository(directory, 3, point =>
            {
                if (point != SaveWriteStage.Verified) return;
                try { repository.Commit("career", Profile(400), first.HeadStamp); }
                catch (IOException) { rejected = true; }
            });
            writer.Commit("career", Profile(200), first.HeadStamp);
            Assert.That(rejected, Is.True); Assert.That(repository.Load("career").Payload, Is.EqualTo(Profile(200)));
        }

        [TestCase("../outside")] [TestCase("a/b")] [TestCase("a\\b")] [TestCase("a b")] [TestCase(".")] [TestCase(" career")]
        public void InvalidNamesAreRejectedRatherThanAliased(string name)
        { Assert.Throws<ArgumentException>(() => new CareerSaveRepository(directory).Commit(name, Profile(1, name), null)); }

        [Test]
        public void SlotsAreIndependentAndHeadersCanBeEnumerated()
        {
            var repository = new CareerSaveRepository(directory);
            repository.Commit("A", Profile(10, "A"), null); repository.Commit("a", Profile(20, "a"), null);
            Assert.That(repository.Load("A").Payload, Is.EqualTo(Profile(10, "A")));
            Assert.That(repository.Load("a").Payload, Is.EqualTo(Profile(20, "a")));
            Assert.That(repository.Enumerate().Count, Is.EqualTo(2));
            Assert.Throws<SaveException>(() => repository.Commit("A", Profile(30, "A"), null, true));
        }

        [Test]
        public void BrowserFindsBackupOnlySlotsAndIgnoresUnrelatedInvalidNames()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "career.json.bak"), Profile());
            File.WriteAllText(Path.Combine(directory, "unrelated.settings.json"), "{}");
            var repository = new CareerSaveRepository(directory);
            Assert.That(repository.Enumerate().Select(s => s.Slot), Is.EquivalentTo(new[] { "career" }));
            Assert.That(repository.Load("career").Payload, Is.EqualTo(Profile()));
        }

        [Test]
        public void LegacyMigrationIsExplicitAndLeavesOriginalBytes()
        {
            Directory.CreateDirectory(directory);
            string original = Profile().Replace("\"saveVersion\":4", "\"saveVersion\":1");
            string path = Path.Combine(directory, "career.json"); File.WriteAllText(path, original);
            var root = new GameObject("Migration test");
            try
            {
                var storage = root.AddComponent<JsonCareerProfileStorage>(); storage.SetDirectory(directory);
                Assert.That(storage.TryLoad("career", out string migrated, out string failure), Is.True, failure);
                Assert.That(CareerSaveCodec.Validate("career", migrated), Is.EqualTo(CareerProfileData.CurrentVersion));
                Assert.That(File.ReadAllText(path), Is.EqualTo(original)); Assert.That(Files().Length, Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void FutureLegacyVersionBlocksFallbackAndOverwrite()
        {
            var repository = new CareerSaveRepository(directory);
            var current = repository.Commit("career", Profile(), null);
            File.WriteAllText(Path.Combine(directory, "career.json"), Profile().Replace("\"saveVersion\":4", "\"saveVersion\":999"));
            Assert.That(Assert.Throws<SaveException>(() => repository.Load("career")).Error, Is.EqualTo(SaveError.UnsupportedVersion));
            Assert.Throws<SaveException>(() => repository.Commit("career", Profile(1), current.HeadStamp));
        }

        [TestCase("{}")] [TestCase("{\"saveVersion\":2,\"saveVersion\":1}")]
        [TestCase("{\"saveVersion\":0}")] [TestCase("not json")]
        public void MalformedProfileDoesNotBecomeCanonical(string malformed)
        {
            var repository = new CareerSaveRepository(directory);
            Assert.Throws<SaveException>(() => repository.Commit("career", malformed, null)); Assert.That(repository.Exists("career"), Is.False);
        }

        [Test]
        public void MissingWalletAndNegativeBalanceAreRejectedWithoutDefaulting()
        {
            Assert.Throws<SaveException>(() => CareerSaveCodec.Validate("career", Profile().Replace("\"wallet\":{\"balance\":100},", "")));
            Assert.Throws<SaveException>(() => CareerSaveCodec.Validate("career", Profile(-1)));
        }

        [Test]
        public void TruncationAndBitFlipFuzzNeverAcceptsDamagedGeneration()
        {
            var repository = new CareerSaveRepository(directory);
            var first = repository.Commit("career", Profile(100), null);
            repository.Commit("career", Profile(200), first.HeadStamp);
            string latest = Files().Last(); byte[] bytes = File.ReadAllBytes(latest);
            for (int length = 0; length < bytes.Length; length += 37)
            {
                File.WriteAllBytes(latest, bytes.Take(length).ToArray());
                Assert.That(repository.Load("career").Payload, Is.EqualTo(Profile(100)), "Truncate " + length);
            }
            for (int index = 0; index < bytes.Length; index += 31)
            {
                byte[] damaged = (byte[])bytes.Clone(); damaged[index] ^= 4; File.WriteAllBytes(latest, damaged);
                Assert.That(repository.Load("career").Payload, Is.EqualTo(Profile(100)), "Flip " + index);
            }
        }

        [Test]
        public void LongRunRetentionAndUncommittedStagingAreBounded()
        {
            var repository = new CareerSaveRepository(directory);
            SaveReadResult previous = null;
            for (int i = 0; i < 1000; i++) previous = repository.Commit("career", Profile(i), previous?.HeadStamp);
            Assert.That(Files().Length, Is.EqualTo(3)); Assert.That(previous.Header.generation, Is.EqualTo(1000));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Files()[0]), "orphan.pending"), "incomplete");
            Assert.That(repository.Load("career").Payload, Is.EqualTo(Profile(999)));
        }

        [Test]
        public void CopyAndRecoverableArchiveNeverOverwriteAnotherSlot()
        {
            var repository = new CareerSaveRepository(directory);
            var saved = repository.Commit("career", Profile(), null);
            var copy = repository.Copy("career", "copy");
            Assert.That(CareerSaveCodec.Validate("copy", copy.Payload), Is.EqualTo(CareerProfileData.CurrentVersion));
            Assert.Throws<SaveException>(() => repository.Copy("career", "copy"));
            Assert.Throws<SaveException>(() => repository.Archive("career", saved.HeadStamp, "career"));
            string archived = repository.Archive("career", saved.HeadStamp, "copy");
            Assert.That(repository.Exists("career"), Is.False); Assert.That(Directory.Exists(archived), Is.True);
            repository.RestoreArchive(archived); Assert.That(repository.Load("career").Payload, Is.EqualTo(Profile()));
            Assert.That(repository.Load("copy").Payload, Is.EqualTo(copy.Payload));
        }

        [Test]
        public void AsyncStorageCommitsDetachedBytesAndRetainsLiveWriterBaseline()
        {
            var root = new GameObject("Async save test");
            try
            {
                var storage = root.AddComponent<JsonCareerProfileStorage>(); storage.SetDirectory(directory);
                Assert.That(storage.TrySave("career", Profile(100), out _), Is.True);
                string captured = Profile(200); var task = storage.SaveAsync("career", captured); captured = Profile(999);
                Assert.That(task.GetAwaiter().GetResult(), Is.Empty);
                Assert.That(storage.TryLoad("career", out string actual, out string failure), Is.True, failure);
                Assert.That(actual, Is.EqualTo(Profile(200)));
                Assert.That(storage.TrySave("career", Profile(300), out failure), Is.True, failure);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void InvalidLoadDoesNotChangeLiveStateAndFailedSaveDoesNotMutateCommittedSnapshot()
        {
            var root = new GameObject("Snapshot test");
            try
            {
                var backend = root.AddComponent<SaveTestStorage>();
                var participant = root.AddComponent<SaveTestParticipant>();
                var profile = root.AddComponent<CareerProfileSystem>();
                profile.ConfigureAutomaticPersistence(false, false, false); profile.SetProfileId("career");
                profile.SetStorage(backend); profile.SetParticipants(new MonoBehaviour[] { participant });
                participant.Cash = 100; Assert.That(profile.TrySave(out _), Is.True);
                participant.Cash = 200; backend.Reject = true; Assert.That(profile.TrySave(out _), Is.False);
                Assert.That(profile.CurrentProfile.wallet.balance, Is.EqualTo(100));
                backend.Payload = "{}"; Assert.That(profile.TryLoad(out _), Is.False); Assert.That(participant.Cash, Is.EqualTo(200));
                var snapshot = profile.CurrentProfile; snapshot.wallet.balance = 999;
                Assert.That(profile.CurrentProfile.wallet.balance, Is.EqualTo(100));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private sealed class SaveTestStorage : MonoBehaviour, ICareerProfileStorage
        {
            public string Payload; public bool Reject;
            public bool TrySave(string id, string payload, out string failure)
            { failure = Reject ? "Injected failure" : ""; if (!Reject) Payload = payload; return !Reject; }
            public bool TryLoad(string id, out string payload, out string failure) { payload = Payload; failure = ""; return true; }
        }
        private sealed class SaveTestParticipant : MonoBehaviour, ICareerProfileParticipant
        {
            public int Cash; public string ProfileSectionId => "test";
            public void Capture(CareerProfileData profile) { profile.wallet.balance = Cash; }
            public bool Restore(CareerProfileData profile, out string failure) { Cash = profile.wallet.balance; failure = ""; return true; }
        }

        [Test]
        public void DirtyRevisionSurvivesOlderAsyncCompletionAndRetryBackoff()
        {
            var policy = new CareerAutosavePolicy(); policy.Changed(0);
            Assert.That(policy.Due(1, true), Is.False); Assert.That(policy.Due(2, false), Is.False);
            Assert.That(policy.Due(2, true), Is.True); policy.Begin(2); policy.Changed(3); policy.Complete(true, 4);
            Assert.That(policy.Dirty, Is.True); Assert.That(policy.SavedRevision, Is.EqualTo(1));
            policy.Begin(7); policy.Complete(false, 8); Assert.That(policy.Due(12, true), Is.False);
            Assert.That(policy.Due(13, true), Is.True); policy.Begin(13); policy.Complete(true, 14); Assert.That(policy.Dirty, Is.False);
        }

        [Test]
        public void ExplicitRecoveryCreatesNewGenerationAndArtifactInspectionShowsStaging()
        {
            var repository = new CareerSaveRepository(directory);
            var first = repository.Commit("career", Profile(100), null);
            var second = repository.Commit("career", Profile(200), first.HeadStamp);
            var recovered = repository.RecoverGeneration("career", 1, second.HeadStamp);
            Assert.That(recovered.Header.generation, Is.EqualTo(3)); Assert.That(recovered.Payload, Is.EqualTo(Profile(100)));
            File.Copy(Files().Last(), Path.Combine(Path.GetDirectoryName(Files()[0]), "uncommitted.pending"));
            Assert.That(repository.InspectRecovery("career").Any(a => a.Status.Contains("NOT committed")), Is.True);
            Assert.That(repository.Load("career").Header.generation, Is.EqualTo(3));
        }

        [Test]
        public void OversizedCorruptGenerationDoesNotBlockOlderValidRecovery()
        {
            var repository = new CareerSaveRepository(directory);
            var first = repository.Commit("career", Profile(), null);
            repository.Commit("career", Profile(200), first.HeadStamp);
            using (var stream = new FileStream(Files().Last(), FileMode.Open, FileAccess.Write))
                stream.SetLength(CareerSaveCodec.MaximumBytes + 8192);
            Assert.That(repository.Load("career").Payload, Is.EqualTo(Profile()));
        }

        [Test]
        public void NewerContainerVersionBlocksFallbackAfterVerifiedHeader()
        {
            var repository = new CareerSaveRepository(directory);
            var first = repository.Commit("career", Profile(), null);
            repository.Commit("career", Profile(200), first.HeadStamp);
            RewriteHeader(Files().Last(), h => h["format"] = 2);
            Assert.That(Assert.Throws<SaveException>(() => repository.Load("career")).Error, Is.EqualTo(SaveError.UnsupportedVersion));
        }

        [Test]
        public void DivergentParentHistoryCannotBeSelectedByGenerationNumberAlone()
        {
            var repository = new CareerSaveRepository(directory);
            var first = repository.Commit("career", Profile(), null);
            repository.Commit("career", Profile(200), first.HeadStamp);
            RewriteHeader(Files().Last(), h => h["parent"] = Guid.NewGuid().ToString("N"));
            Assert.That(Assert.Throws<SaveException>(() => repository.Load("career")).Error, Is.EqualTo(SaveError.Conflict));
        }

        [Test]
        public void LargeUnitySnapshotRoundTripMeasuresActualCapturePath()
        {
            var profile = CareerProfileData.Create("career", "Large Unity fixture");
            for (int i = 0; i < 10000; i++)
            {
                var vehicle = new CareerVehicleData { vehicleId = "vehicle_" + i };
                for (int j = 0; j < 10; j++) vehicle.performanceUpgradeIds.Add("upgrade_" + j);
                profile.vehicles.Add(vehicle);
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var snapshot = JsonUtility.FromJson<CareerProfileData>(JsonUtility.ToJson(profile));
            string json = JsonUtility.ToJson(snapshot, true);
            double milliseconds = watch.Elapsed.TotalMilliseconds;
            Assert.That(snapshot.vehicles.Count, Is.EqualTo(10000)); Assert.That(CareerSaveCodec.Validate("career", json), Is.EqualTo(CareerProfileData.CurrentVersion));
            TestContext.WriteLine("Unity editor detached snapshot + formatted serialization: " + milliseconds.ToString("F2")
                + " ms; " + CareerSaveCodec.Utf8.GetByteCount(json) + " bytes; 10000 vehicles. Not a player-build frame budget.");
        }

        private static void RewriteHeader(string file, Action<Newtonsoft.Json.Linq.JObject> edit)
        {
            byte[] prefix, payload; Newtonsoft.Json.Linq.JObject header;
            using (var stream = File.OpenRead(file)) using (var reader = new BinaryReader(stream))
            {
                prefix = reader.ReadBytes(8); int size = reader.ReadInt32();
                header = CareerSaveCodec.Parse(CareerSaveCodec.Utf8.GetString(reader.ReadBytes(size)));
                reader.ReadBytes(32); payload = reader.ReadBytes((int)(stream.Length - stream.Position));
            }
            edit(header); byte[] bytes = CareerSaveCodec.Utf8.GetBytes(header.ToString(Newtonsoft.Json.Formatting.None));
            using (var stream = File.Create(file)) using (var writer = new BinaryWriter(stream))
            using (var hash = System.Security.Cryptography.SHA256.Create())
            { writer.Write(prefix); writer.Write(bytes.Length); writer.Write(bytes); writer.Write(hash.ComputeHash(bytes)); writer.Write(payload); }
        }
    }
}
