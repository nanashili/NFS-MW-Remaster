using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NUnit.Framework;
using NfsMwRemaster.Driving.AudioAnalysis;
using NfsMwRemaster.Driving.Editor.AudioAnalysis;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class BlackBoxIntegrationTests
    {
        string temporaryRoot;

        [SetUp] public void SetUp() { temporaryRoot = Path.Combine(Path.GetTempPath(), "nfs-audio-analysis-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporaryRoot); }
        [TearDown] public void TearDown() { if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true); }

        [Test]
        public void SourceAccessRestrictsApprovedRootAndDiscoversAllowedFiles()
        {
            File.WriteAllBytes(Path.Combine(temporaryRoot, "engine.gin"), new byte[] { 1 });
            File.WriteAllText(Path.Combine(temporaryRoot, "notes.txt"), "ignore");
            var found = BlackBoxSourceAccess.Discover(temporaryRoot);
            Assert.That(found.Length, Is.EqualTo(1)); Assert.That(found[0].relativePath, Is.EqualTo("engine.gin"));
            Assert.Throws<InvalidDataException>(() => BlackBoxSourceAccess.Resolve(temporaryRoot, "../outside.gin"));
            Assert.Throws<InvalidDataException>(() => BlackBoxSourceAccess.Resolve(temporaryRoot, Path.Combine(temporaryRoot, "engine.gin")));
        }

        [Test]
        public void SourceReadHonoursCancellationBeforeRetainingBytes()
        {
            string path = Path.Combine(temporaryRoot, "large.gin"); File.WriteAllBytes(path, new byte[256 * 1024]);
            var source = new BlackBoxSource { root = temporaryRoot, relativePath = "large.gin" };
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => BlackBoxSourceAccess.Read(source, cancellation.Token));
            }
        }

        [Test]
        public void NfsmsEvidenceKeepsDecodedCandidateAndBlockedSemanticsSeparate()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("update_field car engine M3GTR_cutl.gin\ncopy_fields car base\n");
            var report = BlackBoxInspection.InspectConfiguration(bytes, "fixture.nfsms");
            Assert.That(report.References.Any(r => r.Kind == "configuration-names-source"), Is.True);
            Assert.That(report.Gaps.Any(g => g.Code == "inherited-configuration"), Is.True);
            Assert.That(report.Capabilities.Any(c => c.Name == "control evaluation" && c.Status == CapabilityStatus.Blocked), Is.True);
        }

        [Test]
        public void WavExportPreservesFloatAmplitudeAndHalfOpenFrames()
        {
            float[] pcm = { -1.25f, 0.5f, 2f, -0.25f };
            using (var stream = new MemoryStream())
            {
                BlackBoxWaveExport.Write(stream, pcm, 44100, 1, 1, 3);
                byte[] bytes = stream.ToArray();
                Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("RIFF"));
                Assert.That(BitConverter.ToInt32(bytes, 52), Is.EqualTo(8));
                Assert.That(BitConverter.ToSingle(bytes, 56), Is.EqualTo(0.5f));
                Assert.That(BitConverter.ToSingle(bytes, 60), Is.EqualTo(2f));
            }
            using (var stream = new MemoryStream()) Assert.Throws<InvalidDataException>(() => BlackBoxWaveExport.Write(stream, pcm, 44100, 1, 0, 5));
            pcm[0] = float.NaN;
            using (var stream = new MemoryStream()) Assert.Throws<InvalidDataException>(() => BlackBoxWaveExport.Write(stream, pcm, 44100, 1, 0, 1));
        }

        [Test]
        public void ApproximateOrGuessedMappingCannotEnterVerifiedPath()
        {
            Assert.Throws<InvalidOperationException>(() => BlackBoxNativeMapping.Prepare(null, null, false));
            var report = new AnalysisReport { Source = new SourceEvidence("candidate.gin", "hash", 32) };
            report.Fields.Add(new FieldEvidence("candidate_endpoint_0", new ByteRange(8, 4), "", "1000", EvidenceProvenance.Decoded));
            report.Fields.Add(new FieldEvidence("candidate_endpoint_1", new ByteRange(12, 4), "", "7000", EvidenceProvenance.Decoded));
            report.Fields.Add(new FieldEvidence("decoded_sample_frames", new ByteRange(24, 4), "", "20", EvidenceProvenance.Decoded));
            report.Tables.Add(new StructuralTable { Name = "table_a", Entries = { new TableEntry { Index = 0, RawValue = 0 }, new TableEntry { Index = 1, RawValue = 20 } } });
            var investigation = NfsMwRemaster.Driving.AudioAnalysis.Analysis.GinMappingInvestigator.Inspect(report);
            Assert.That(investigation.Status, Is.EqualTo("candidate"));
            Assert.That(investigation.Candidate, Is.Null);
            var hypothesis = NfsMwRemaster.Driving.AudioAnalysis.Analysis.GinMappingInvestigator.BuildUniformEndpointHypothesis(report);
            Assert.That(hypothesis.Candidate.Anchors.All(a => a.Status != NfsMwRemaster.Driving.AudioAnalysis.Analysis.MappingEvidenceStatus.Verified), Is.True);
        }

        [Test]
        public void ComparisonPreservesLatencySignChannelShapeAndNonfiniteRejection()
        {
            float[] a = { 0, 1, 0, 2, 0, 3 }; float[] b = { 9, 9, 0, 1, 0, 2, 0, 3 };
            var compared = NfsMwRemaster.Driving.AudioAnalysis.Analysis.SignalAnalysis.Compare(a, b, 2, -1);
            Assert.That(compared.DeclaredLatencyFrames, Is.EqualTo(-1)); Assert.That(compared.ComparedFrames, Is.EqualTo(3)); Assert.That(compared.LengthsMatch, Is.False);
            Assert.Throws<ArgumentException>(() => NfsMwRemaster.Driving.AudioAnalysis.Analysis.SignalAnalysis.Compare(new[] { float.NaN }, new[] { 0f }, 1));
        }

        [Test]
        public void PresenterCancellationKeepsPriorStateAndDoesNotAcceptLateJob()
        {
            using (var presenter = new BlackBoxPresenter(null))
            {
                presenter.Start(c => { while (!c.IsCancellationRequested) Thread.Sleep(1); c.ThrowIfCancellationRequested(); return 7; }, value => presenter.SetStatus("unexpected"));
                presenter.Cancel(); WaitForTick(presenter);
                Assert.That(presenter.Status, Does.Contain("Cancelled")); Assert.That(presenter.Status, Is.Not.EqualTo("unexpected"));
            }
        }

        [Test]
        public void PresenterRevisionChangeRejectsStaleResult()
        {
            var session = ScriptableObject.CreateInstance<BlackBoxSession>();
            try
            {
                using (var presenter = new BlackBoxPresenter(session))
                {
                    presenter.Start(c => { Thread.Sleep(20); return 3; }, value => presenter.SetStatus("accepted"));
                    session.revision++;
                    WaitForTick(presenter);
                    Assert.That(presenter.Status, Does.Contain("Obsolete source/session revision rejected"));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(session); }
        }

        static void WaitForTick(BlackBoxPresenter presenter)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(2);
            while (!presenter.Tick() && DateTime.UtcNow < deadline) Thread.Sleep(5);
            Assert.That(presenter.Busy, Is.False, "Background presenter job did not finish within the test budget.");
        }
    }
}
