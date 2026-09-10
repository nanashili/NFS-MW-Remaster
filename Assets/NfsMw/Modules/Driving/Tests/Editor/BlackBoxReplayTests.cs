using System;
using System.Linq;
using NUnit.Framework;
using NfsMwRemaster.Driving.Editor.AudioAnalysis;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class BlackBoxReplayTests
    {
        const int SourceRate = 44100;
        const int SourceFrames = SourceRate * 3;

        static EngineAudioCompiledSnapshot Snapshot()
        {
            return new EngineAudioCompiledSnapshot(new[]
            {
                Region("off-load", 0, .49f, .17f), Region("on-load", .5f, 1, .31f)
            }, 19);
        }

        static EngineAudioRegion Region(string id, float minimumLoad, float maximumLoad, float phase)
        {
            var pcm = new float[SourceFrames * 2];
            for (int frame = 0; frame < SourceFrames; frame++)
            {
                double angle = 2 * Math.PI * (220 + frame * .03) * frame / SourceRate + phase;
                pcm[frame * 2] = (float)(.65 * Math.Sin(angle));
                pcm[frame * 2 + 1] = (float)(.45 * Math.Cos(angle * .97));
            }
            Assert.That(EngineAudioCompiler.TryCompile(pcm, 0, SourceFrames, SourceRate, 2,
                new[] { new EngineRpmAnchor(1000, 0), new EngineRpmAnchor(8000, SourceFrames - 1) }, out var region, id), Is.True);
            region.minimumLoad = minimumLoad; region.maximumLoad = maximumLoad; region.gain = .8f;
            return region;
        }

        static BlackBoxReplaySettings Settings(int outputRate, int controlHz, int blockFrames)
        {
            return new BlackBoxReplaySettings
            {
                scenario = BlackBoxScenario.Sweep, outputRate = outputRate, channels = 2,
                blockFrames = blockFrames, controlHz = controlHz, seconds = 2.25f,
                rpm = 1400, endRpm = 7600, load = .8f, gain = .9f
            };
        }

        [Test]
        public void ReplayIsSampleExactAcrossCallbackSizesAndControlRates()
        {
            foreach (int outputRate in new[] { 44100, 48000 })
            foreach (int controlHz in new[] { 50, 120 })
            {
                var baseline = BlackBoxReplay.Render(Snapshot(), Settings(outputRate, controlHz, 64), "replay-fixture");
                Assert.That(baseline.pcm.Any(v => Math.Abs(v) > 1e-5f), Is.True, "Replay output must be nonzero.");
                foreach (int block in new[] { 257, 1024 })
                {
                    var candidate = BlackBoxReplay.Render(Snapshot(), Settings(outputRate, controlHz, block), "replay-fixture");
                    Assert.That(candidate.pcm.Length, Is.EqualTo(baseline.pcm.Length));
                    CollectionAssert.AreEqual(baseline.pcm, candidate.pcm,
                        outputRate + " Hz / " + controlHz + " Hz differs at block " + block);
                    StringAssert.Contains("differences 0.", BlackBoxReplayComparison.CompareDecisions(baseline, candidate));
                }
                Assert.That(baseline.telemetry.Length, Is.GreaterThan(100));
                for (int i = 0; i < baseline.telemetry.Length; i++)
                {
                    Assert.That(baseline.telemetry[i].tick, Is.EqualTo(i));
                    Assert.That(baseline.telemetry[i].outputFrame,
                        Is.EqualTo((long)Math.Round(i * outputRate / (double)controlHz)));
                }
                Assert.That(baseline.telemetry.First().rpm, Is.Not.EqualTo(baseline.telemetry.Last().rpm));
            }
        }

        [Test]
        public void ReplaySeekEqualsUninterruptedPrefixAtExactOutputCursor()
        {
            var settings = Settings(48000, 120, 257);
            var full = BlackBoxReplay.Render(Snapshot(), settings, "replay-fixture");
            int cursor = 48000;
            var seek = BlackBoxReplay.Render(Snapshot(), settings, "replay-fixture", cursor);
            Assert.That(seek.pcm.Length, Is.EqualTo(cursor * settings.channels));
            CollectionAssert.AreEqual(full.pcm.Take(seek.pcm.Length).ToArray(), seek.pcm);
            Assert.That(seek.telemetry.Last().outputFrame, Is.LessThanOrEqualTo(cursor));
        }

        [Test]
        public void ReplayTracesShowLoadRegionSelectionAndLimiterStops()
        {
            var lift = Settings(48000, 50, 257); lift.scenario = BlackBoxScenario.ThrottleLift; lift.endRpm = 7000;
            var lifted = BlackBoxReplay.Render(Snapshot(), lift, "replay-fixture");
            Assert.That(lifted.traces.Any(t => t.reason == EngineAudioTraceReason.Rendered && t.regionIndex == 0), Is.True);
            Assert.That(lifted.traces.Any(t => t.reason == EngineAudioTraceReason.Rendered && t.regionIndex == 1), Is.True);
            Assert.That(lifted.traces.Any(t => t.reason == EngineAudioTraceReason.LoadExcluded && t.regionIndex == 0), Is.True);
            Assert.That(lifted.traces.Any(t => t.reason == EngineAudioTraceReason.LoadExcluded && t.regionIndex == 1), Is.True);
            foreach (var trace in lifted.traces.Where(t => t.reason == EngineAudioTraceReason.GrainStarted))
            {
                Assert.That(trace.regionIndex, Is.InRange(0, 1));
                Assert.That(trace.sourceOffset, Is.InRange(0, SourceFrames - 1));
                Assert.That(trace.grainLength, Is.GreaterThan(0));
                Assert.That(trace.grainStartOutputFrame, Is.InRange(0L, lifted.pcm.LongLength / lift.channels));
            }

            var limiter = Settings(48000, 120, 257); limiter.scenario = BlackBoxScenario.Limiter; limiter.endRpm = 7000;
            var limited = BlackBoxReplay.Render(Snapshot(), limiter, "replay-fixture");
            Assert.That(limited.traces.Any(t => t.reason == EngineAudioTraceReason.NotRunning && !t.engineRunning), Is.True);
            Assert.That(limited.traces.Any(t => t.reason == EngineAudioTraceReason.Rendered), Is.True);
        }
    }
}
