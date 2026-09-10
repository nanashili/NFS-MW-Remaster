using System;
using NUnit.Framework;
using Unity.Profiling;

namespace NfsMwRemaster.Driving.Tests
{
    /// <summary>Offline checks for the callback-owned playback layer.</summary>
    public sealed class VehicleAudioDspTests
    {
        private static AemsPcmRenderer Tone(int sourceFrames, bool loop, out AemsEvaluator.Player[] players)
        {
            var pcm = new float[sourceFrames];
            for (int i = 0; i < pcm.Length; i++) pcm[i] = .5f * (float)Math.Sin(2 * Math.PI * 440 * i / 48000);
            players = new[] { new AemsEvaluator.Player { Active = true, Control = 1, Generation = 1, Sample = 1 } };
            var renderer = new AemsPcmRenderer(new[] { new AemsPcmRenderer.Recording { SampleId = 1, SampleRate = 48000,
                Channels = 1, Pcm = pcm, LoopStart = loop ? 123 : 0, LoopEnd = loop ? sourceFrames - 7 : 0 } }, 1);
            renderer.Publish(players);
            return renderer;
        }

        [TestCase(44100, .5f, .5f)]
        [TestCase(48000, 2f, .5f)]
        [TestCase(44100, .5f, 2f)]
        [TestCase(48000, 2f, 2f)]
        [TestCase(48000, 1f, 1f)]
        public void AuthoredTempoControlsDurationAndPitchControlsFrequency(int rate, float pitch, float tempo)
        {
            var renderer = Tone(48000, false, out var players);
            renderer.SetPlayback(pitch, tempo);
            var block = new float[128]; var output = new float[rate * 3];
            int rendered = 0;
            while (!players[0].Completed && rendered + block.Length <= output.Length)
            {
                renderer.RenderInto(block, 1, rate); renderer.Publish(players);
                Array.Copy(block, 0, output, rendered, block.Length); rendered += block.Length;
            }
            Assert.True(players[0].Completed, "finite recording must finish");
            Assert.That(rendered / (double)rate, Is.EqualTo(1 / (double)tempo).Within(.045), "duration follows tempo, independently of pitch");
            int start = rate / 5, end = Math.Min(rendered - rate / 20, rate * 2 / 5), crossings = 0;
            for (int i = start + 1; i < end; i++) if ((output[i - 1] <= 0) != (output[i] <= 0)) crossings++;
            double frequency = crossings * rate / (double)(2 * (end - start));
            Assert.That(frequency, Is.EqualTo(440 * pitch).Within(35), "settled frequency follows pitch across tempo changes");
        }

        [TestCase(44100)]
        [TestCase(48000)]
        public void LoopOutputIsFiniteContinuousAndIndependentOfCallbackPartition(int rate)
        {
            var whole = Tone(733, true, out _); var split = Tone(733, true, out _);
            whole.SetPlayback(.5f, 2); split.SetPlayback(.5f, 2);
            var expected = new float[rate]; whole.RenderInto(expected, 1, rate);
            var actual = new float[rate]; var block = new float[997];
            for (int offset = 0; offset < actual.Length;)
            {
                int count = Math.Min(offset % 811 + 1, actual.Length - offset);
                split.RenderInto(block, 1, rate, count); Array.Copy(block, 0, actual, offset, count); offset += count;
            }
            double energy = 0;
            for (int i = 0; i < actual.Length; i++)
            {
                Assert.False(float.IsNaN(actual[i]) || float.IsInfinity(actual[i]));
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(1e-6), "callback partition changed sample " + i);
                energy += actual[i] * actual[i];
            }
            Assert.That(energy / actual.Length, Is.GreaterThan(.001), "intro/sustain loop must remain audible");
        }

        [Test]
        public void AemsWarmCallbackAllocatesNothing()
        {
            var renderer = Tone(733, true, out _); var output = new float[512];
            renderer.SetPlayback(.5f, 2); renderer.RenderInto(output, 1, 48000);
            AssertAllocationProbe();
            using var allocations = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 64, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            for (int i = 0; i < 30; i++) renderer.RenderInto(output, 1, 48000);
            allocations.Stop(); Assert.True(allocations.Valid); Assert.AreEqual(0, allocations.Count);
        }

        [TestCase(44100, .5f)]
        [TestCase(48000, 2f)]
        public void GinTempoClockIsIndependentOfPitchAndCallbackPartition(int rate, float tempo)
        {
            var pcm = new float[48000];
            for (int i = 0; i < pcm.Length; i++) pcm[i] = .25f * (float)Math.Sin(i * .051);
            Assert.True(EngineAudioCompiler.TryCompile(pcm, 0, pcm.Length, 48000, 1,
                new[] { new EngineRpmAnchor(1000, 0), new EngineRpmAnchor(7000, pcm.Length - 1) }, out var region));
            var snapshot = new EngineAudioCompiledSnapshot(new[] { region }, 1);
            var whole = new EngineAudioRenderer(1024); var split = new EngineAudioRenderer(1024); var pitched = new EngineAudioRenderer(1024);
            foreach (var renderer in new[] { whole, split, pitched }) { renderer.SetTelemetry(snapshot); renderer.SetInputs(3000, .5f, true); renderer.SetPlayback(.5f, tempo); }
            pitched.SetPlayback(2, tempo);
            var expected = new float[rate]; var actual = new float[rate]; var differentPitch = new float[rate]; var block = new float[257];
            whole.RenderInto(expected, 1, rate); pitched.RenderInto(differentPitch, 1, rate);
            for (int offset = 0; offset < rate;)
            {
                int count = Math.Min(block.Length, rate - offset); split.RenderInto(block, 1, rate, count);
                Array.Copy(block, 0, actual, offset, count); offset += count;
            }
            double difference = 0;
            for (int i = 0; i < rate; i++)
            {
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(1e-6));
                Assert.False(float.IsNaN(actual[i]) || float.IsInfinity(actual[i]));
                difference += Math.Abs(expected[i] - differentPitch[i]);
            }
            var starts = new System.Collections.Generic.List<long>(); var pitchedStarts = new System.Collections.Generic.List<long>();
            for (int i = 0; i < whole.Trace.Count; i++) if (whole.Trace.Read(i).reason == EngineAudioTraceReason.GrainStarted) starts.Add(whole.Trace.Read(i).grainStartOutputFrame);
            for (int i = 0; i < pitched.Trace.Count; i++) if (pitched.Trace.Read(i).reason == EngineAudioTraceReason.GrainStarted) pitchedStarts.Add(pitched.Trace.Read(i).grainStartOutputFrame);
            CollectionAssert.AreEqual(starts, pitchedStarts, "pitch must not move GIN envelope starts");
            Assert.That(starts.Count, Is.EqualTo(50 * tempo).Within(2), "GIN tempo controls grain cadence");
            Assert.That(difference, Is.GreaterThan(1), "pitch control must change rendered PCM");
        }

        [TestCase(44100)]
        [TestCase(48000)]
        public void AemsPitchChangesFrequencyWithoutChangingTempo(int sampleRate)
        {
            const int sourceRate = 48000, sourceFrames = 96000;
            var pcm = new float[sourceFrames];
            for (int i = 0; i < pcm.Length; i++) pcm[i] = (float)Math.Sin(2 * Math.PI * 440 * i / sourceRate);
            var recording = new AemsPcmRenderer.Recording { SampleId = 1, SampleRate = sourceRate, Channels = 1, Pcm = pcm };
            var players = new[] { new AemsEvaluator.Player { Active = true, Control = 1, Generation = 1, Sample = 1, Gain = 1, Pitch = 1 } };
            var renderer = new AemsPcmRenderer(new[] { recording }, 1);
            renderer.SetPlayback(2, .5f); renderer.Publish(players);
            var output = new float[sampleRate * 2];
            int offset = 0;
            while (offset < output.Length)
            {
                int frames = Math.Min(offset % 997 + 257, output.Length - offset);
                var block = new float[frames];
                renderer.RenderInto(block, 1, sampleRate, frames);
                Array.Copy(block, 0, output, offset, frames);
                offset += frames;
            }
            // The first 250 ms contains the control smoothing ramp. Measure the settled region.
            int crossings = 0, start = sampleRate / 2, end = sampleRate;
            for (int i = start + 1; i < end; i++) if ((output[i - 1] <= 0) != (output[i] <= 0)) crossings++;
            double measured = crossings * sampleRate / (double)(2 * (end - start));
            Assert.That(measured, Is.InRange(760, 1000), "pitch=2 should produce approximately 880 Hz");
        }

        [Test]
        public void AemsOneShotCompletionIsIndependentOfPitchAndPartialBlocksClearOnlyRequestedFrames()
        {
            var recording = new AemsPcmRenderer.Recording { SampleId = 1, SampleRate = 1000, Channels = 1, Pcm = new float[100] };
            var players = new[] { new AemsEvaluator.Player { Active = true, Control = 1, Generation = 1, Sample = 1, Gain = 1, Pitch = 1 } };
            var renderer = new AemsPcmRenderer(new[] { recording }, 1);
            renderer.SetPlayback(4, 1); renderer.Publish(players);
            var output = new float[200]; for (int i = 100; i < output.Length; i++) output[i] = 7;
            renderer.RenderInto(output, 1, 1000, 100); renderer.Publish(players);
            Assert.True(players[0].Completed);
            Assert.That(output[100], Is.EqualTo(7), "tail outside requested region must be untouched");
        }

        [Test]
        public void EngineRendererWarmRenderAllocationsStayAtZero()
        {
            var pcm = new float[2048]; for (int i = 0; i < pcm.Length; i++) pcm[i] = (float)Math.Sin(i * .1);
            var region = new EngineAudioRegion { sourceStartFrame = 0, sourceEndFrame = pcm.Length, sampleRate = 48000, channels = 1,
                compiledPcm = pcm, rpmAnchors = new[] { new EngineRpmAnchor(1000, 0), new EngineRpmAnchor(2000, pcm.Length - 1) } };
            Assert.True(EngineAudioCompiler.TryBuildSnapshot(new[] { region }, 1, out var snapshot));
            var renderer = new EngineAudioRenderer(0); renderer.SetTelemetry(snapshot); renderer.SetFrame(1500, .5f, true, 1, 0, 1, 2);
            var output = new float[512]; renderer.RenderInto(output, 1, 48000, output.Length);
            AssertAllocationProbe();
            using var allocations = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 64, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            renderer.RenderInto(output, 1, 48000, output.Length);
            allocations.Stop(); Assert.True(allocations.Valid); Assert.AreEqual(0, allocations.Count);
        }

        private static void AssertAllocationProbe()
        {
            using var positive = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 32, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            GC.KeepAlive(new byte[4096]); positive.Stop();
            Assert.True(positive.Valid); Assert.Greater(positive.Count, 0, "allocation counter must detect the retained positive control");
        }
    }
}
