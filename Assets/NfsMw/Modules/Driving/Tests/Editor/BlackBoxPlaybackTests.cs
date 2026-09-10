using System;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class BlackBoxPlaybackTests
    {
        private static EngineAudioCompiledSnapshot Snapshot(params EngineAudioRegion[] regions)
            => new EngineAudioCompiledSnapshot(regions, 7);

        private static EngineAudioRegion Region(int start, int end, params EngineRpmAnchor[] anchors)
        {
            var pcm = new float[end - start];
            for (int i = 0; i < pcm.Length; i++) pcm[i] = i + 1;
            Assert.True(EngineAudioCompiler.TryCompile(pcm, start, end, 10, 1, anchors, out var region));
            return region;
        }

        [Test]
        public void RenderIsIndependentOfRequestedBufferSize()
        {
            var snapshot = Snapshot(Region(0, 8, new EngineRpmAnchor(1000, 0), new EngineRpmAnchor(2000, 7)));
            var a = new float[4]; var b = new float[8];
            EngineAudioKernel.Render(snapshot, 1500, 1, 10, 4, 1, 1, a, null);
            EngineAudioKernel.Render(snapshot, 1500, 1, 10, 8, 1, 1, b, null);
            for (int i = 0; i < a.Length; i++) Assert.AreEqual(a[i], b[i], 1e-6f);
        }

        [Test]
        public void StatefulRendererProducesSameWholeSequenceAcrossCallbackSizes()
        {
            var snapshot = Snapshot(Region(0, 8, new EngineRpmAnchor(1000, 0), new EngineRpmAnchor(2000, 7)));
            var small = new EngineAudioRenderer(); small.SetTelemetry(snapshot); var a = new float[8]; var chunk = new float[4];
            small.Render(1500, 1, 10, 4, 1, 1, chunk); Array.Copy(chunk, 0, a, 0, 4);
            small.Render(1500, 1, 10, 4, 1, 1, chunk, 4, 1); Array.Copy(chunk, 0, a, 4, 4);
            var large = new EngineAudioRenderer(); large.SetTelemetry(snapshot); var b = new float[8];
            large.Render(1500, 1, 10, 8, 1, 1, b);
            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void RendererResetReplaysDeterministicallyAndClearsTrace()
        {
            var renderer = new EngineAudioRenderer(8); renderer.SetTelemetry(Snapshot(Region(0, 8, new EngineRpmAnchor(1000, 0), new EngineRpmAnchor(2000, 7))));
            var first = new float[4]; renderer.Render(1500, 1, 10, 4, 1, 1, first);
            while (renderer.Trace.TryRead(out _)) { }
            renderer.Reset(); var second = new float[4]; renderer.Render(1500, 1, 10, 4, 1, 1, second);
            CollectionAssert.AreEqual(first, second); Assert.Greater(renderer.Trace.Count, 0); Assert.AreEqual(1, renderer.Cadence);
        }

        [Test]
        public void MissingPcmFailsClosedAndReportsReason()
        {
            var bad = new EngineAudioRegion { sourceStartFrame = 0, sourceEndFrame = 4, sampleRate = 10, channels = 1,
                rpmAnchors = new[] { new EngineRpmAnchor(1000, 0), new EngineRpmAnchor(2000, 3) }, compiledPcm = new float[0] };
            var trace = new EngineAudioTraceRing(4); var output = new float[4];
            Assert.AreEqual(0, EngineAudioKernel.Render(Snapshot(bad), 1500, 1, 10, 4, 1, 1, output, trace));
            Assert.AreEqual(EngineAudioTraceReason.InvalidRegion, trace.Read(0).reason);
        }

        [Test]
        public void DescendingAndOverlappingMappingsAreHandledExplicitly()
        {
            var descending = Region(0, 8, new EngineRpmAnchor(7000, 0), new EngineRpmAnchor(1000, 7));
            var trace = new EngineAudioTraceRing(8); Assert.AreEqual(1, EngineAudioKernel.Render(Snapshot(descending), 4000, 1, 10, 2, 1, 1, new float[2], trace));
            var ambiguous = Region(0, 8, new EngineRpmAnchor(1000, 0), new EngineRpmAnchor(3000, 4), new EngineRpmAnchor(2000, 7));
            trace.Clear(); Assert.AreEqual(0, EngineAudioKernel.Render(Snapshot(ambiguous), 2500, 1, 10, 2, 1, 1, new float[2], trace));
            Assert.AreEqual(EngineAudioTraceReason.AmbiguousMapping, trace.Read(0).reason);
        }
    }
}
