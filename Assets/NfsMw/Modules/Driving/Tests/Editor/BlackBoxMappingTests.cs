using System;
using System.Threading;
using NUnit.Framework;
using NfsMwRemaster.Driving.AudioAnalysis;
using NfsMwRemaster.Driving.AudioAnalysis.Analysis;
using AnalysisSourceEvidence = NfsMwRemaster.Driving.AudioAnalysis.Analysis.SourceEvidence;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class BlackBoxMappingTests
    {
        static RpmAnchor A(int index, double coordinate, double rpm, long start, long end)
        { return new RpmAnchor(index, coordinate, rpm, new FrameRange(start, end), MappingEvidenceStatus.Candidate, new AnalysisSourceEvidence("synthetic")); }

        [Test]
        public void PreservesDescendingNonlinearSourceOrder()
        {
            var map = new RpmMapping(new[] { A(4, 0, 7200, 0, 100), A(3, 1, 5000, 100, 260), A(2, 2, 1800, 260, 500) });
            Assert.That(map.IsMonotonic, Is.True); Assert.That(map.IsAscending, Is.False);
            Assert.That(map.FrameToRpm(180), Is.EqualTo(3400).Within(0.01));
            Assert.That(map.Anchors[0].TableIndex, Is.EqualTo(4));
        }

        [Test]
        public void PreservesDescendingPlaybackFrameCoordinates()
        {
            var map = new RpmMapping(new[]
                { new RpmAnchor(0, 0, 413280, 8480, new FrameRange(405024, 413280), MappingEvidenceStatus.Candidate, new AnalysisSourceEvidence("synthetic")),
                  new RpmAnchor(1, 1, 405024, 5000, new FrameRange(396768, 405024), MappingEvidenceStatus.Candidate, new AnalysisSourceEvidence("synthetic")) });
            Assert.That(map.FrameToRpm(409152), Is.EqualTo(6740).Within(0.01));
            Assert.That(map.RpmToFrames(6740)[0].OutputStart, Is.EqualTo(409152).Within(0.01));
        }

        [Test]
        public void DuplicateRpmProducesAmbiguousAlternatives()
        {
            var map = new RpmMapping(new[] { A(0, 0, 1000, 0, 10), A(1, 1, 2000, 10, 20), A(2, 2, 1000, 20, 30) });
            var alternatives = map.RpmToFrames(1000);
            Assert.That(map.IsMonotonic, Is.False); Assert.That(alternatives.Count, Is.EqualTo(2));
            var frameAmbiguous = new RpmMapping(new[] { A(0, 0, 1000, 0, 10), A(1, 1, 2000, 0, 20), A(2, 2, 1000, 20, 30) });
            Assert.Throws<InvalidOperationException>(() => frameAmbiguous.FrameToRpm(0));
        }

        [Test]
        public void SameFrameWithDifferentRpmRetainsBothInverseAlternatives()
        {
            var map = new RpmMapping(new[] { A(0, 0, 1000, 0, 10), A(1, 1, 2000, 0, 20) });
            CollectionAssert.AreEquivalent(new[] { 1000d, 2000d }, map.FrameToRpmAlternatives(0));
        }

        [Test]
        public void ExactOnlyDoesNotInterpolateUnknownRpm()
        {
            var map = new RpmMapping(new[] { A(0, 0, 1000, 0, 10), A(1, 1, 2000, 10, 20) }, MappingLookupPolicy.ExactOnly);
            Assert.That(map.RpmToFrames(1500).Count, Is.EqualTo(0));
            Assert.That(map.RpmToFrames(2000).Count, Is.EqualTo(1));
            Assert.That(map.TryFrameToRpm(double.NaN, out _), Is.False);
            Assert.That(map.TryTableCoordinateToRpm(0.5, out _), Is.False);
            Assert.That(map.TryTableCoordinateToRpm(1, out var exactRpm), Is.True);
            Assert.That(exactRpm, Is.EqualTo(2000));
        }

        [Test]
        public void BoundPoliciesSelectDiscreteAnchorsAndPreserveDuplicates()
        {
            var anchors = new[] { A(0, 0, 1000, 0, 10), A(1, 1, 3000, 10, 20), A(2, 2, 3000, 20, 30), A(3, 3, 5000, 30, 40) };
            Assert.That(new RpmMapping(anchors, MappingLookupPolicy.LowerBound).RpmToFrames(4000).Count, Is.EqualTo(2));
            Assert.That(new RpmMapping(anchors, MappingLookupPolicy.UpperBound).RpmToFrames(2000).Count, Is.EqualTo(2));
            Assert.That(new RpmMapping(anchors, MappingLookupPolicy.LowerBound).RpmToFrames(500).Count, Is.EqualTo(0));
        }

        [Test]
        public void FlatSegmentRetainsOnePositionPerMatchingSegment()
        {
            var map = new RpmMapping(new[] { A(0, 0, 2000, 0, 10), A(1, 1, 2000, 10, 20), A(2, 2, 3000, 20, 30) });
            Assert.That(map.RpmToFrames(2000).Count, Is.EqualTo(2));
        }

        [Test]
        public void HalfOpenFrameTransformUsesOutwardBoundaryRounding()
        {
            var transform = new FrameTransform(44100, 48000);
            FrameRange output = transform.SourceToOutput(new FrameRange(1, 2));
            Assert.That(output.Start, Is.EqualTo(1)); Assert.That(output.EndExclusive, Is.EqualTo(3));
        }

        [Test]
        public void WaveformAndAnalysisAreDeterministicAndCancellable()
        {
            var pcm = new float[256]; for (int i = 0; i < pcm.Length; i++) pcm[i] = (float)Math.Sin(i * 0.2);
            WaveformSummary first = SignalAnalysis.Summarize(pcm, 48000, 1);
            WaveformSummary second = SignalAnalysis.Summarize(pcm, 48000, 1);
            Assert.That(first.Rms, Is.EqualTo(second.Rms)); Assert.That(first.AlgorithmVersion, Is.Not.Empty);
            var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => SignalAnalysis.Spectrogram(pcm, 48000, 1, new SpectrogramParameters { WindowSize = 16, HopSize = 4 }, cancelled.Token));
        }

        [Test]
        public void ComparisonReportsDeclaredLatencyWithoutHidingLengths()
        {
            var a = new float[] { 0, 1, 2, 3 }; var b = new float[] { 9, 0, 1, 2, 3 };
            SignalComparison result = SignalAnalysis.Compare(a, b, 1, -1);
            Assert.That(result.DeclaredLatencyFrames, Is.EqualTo(-1)); Assert.That(result.ComparedFrames, Is.EqualTo(4)); Assert.That(result.LengthsMatch, Is.False);
        }

        [Test]
        public void GinInvestigatorKeepsObservedCandidateBlockedFromVerifiedStatus()
        {
            var report = new AnalysisReport { Source = new NfsMwRemaster.Driving.AudioAnalysis.SourceEvidence("fixture.gin", "hash", 128), ParserVersion = "test" };
            report.Fields.Add(new FieldEvidence("candidate_endpoint_0", new ByteRange(8, 4), "", "2456", EvidenceProvenance.Decoded));
            report.Fields.Add(new FieldEvidence("candidate_endpoint_1", new ByteRange(12, 4), "", "7957", EvidenceProvenance.Decoded));
            report.Fields.Add(new FieldEvidence("decoded_sample_frames", new ByteRange(24, 4), "", "100", EvidenceProvenance.Decoded));
            report.Tables.Add(new StructuralTable { Name = "table_a", Entries = new System.Collections.Generic.List<TableEntry> { new TableEntry { Index = 0, RawValue = 10 }, new TableEntry { Index = 1, RawValue = 90 } } });
            report.Tables.Add(new StructuralTable { Name = "table_b", Entries = new System.Collections.Generic.List<TableEntry> { new TableEntry { Index = 0, RawValue = 0 } } });
            GinMappingInvestigation result = GinMappingInvestigator.Inspect(report);
            Assert.That(result.Status, Is.EqualTo("candidate")); Assert.That(result.Candidate, Is.Null);
            var hypothesis = GinMappingInvestigator.BuildUniformEndpointHypothesis(report);
            Assert.That(hypothesis.Status, Is.EqualTo("hypothesis")); Assert.That(hypothesis.Candidate.Anchors[0].Status, Is.EqualTo(MappingEvidenceStatus.Candidate));
        }
    }
}
