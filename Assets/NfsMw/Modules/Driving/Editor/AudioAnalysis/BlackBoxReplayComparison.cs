using System;
using System.Collections.Generic;
using System.Threading;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    /// <summary>Compare captured decisions, independent of how the caller partitioned DSP blocks.</summary>
    public static class BlackBoxReplayComparison
    {
        private readonly struct Decision : IEquatable<Decision>
        {
            public readonly long frame;
            public readonly EngineAudioTrace trace;
            public readonly EngineAudioRegionInfo region;
            public Decision(long frame, EngineAudioTrace trace, EngineAudioRegionInfo region)
            { this.frame = frame; this.trace = trace; this.region = region; }
            public bool Equals(Decision other) => frame == other.frame && trace.reason == other.trace.reason &&
                region.Id == other.region.Id && region.SourceHash == other.region.SourceHash && region.RecordingId == other.region.RecordingId &&
                trace.sourceFrame == other.trace.sourceFrame && trace.grainLength == other.trace.grainLength && trace.sourceRate == other.trace.sourceRate &&
                trace.effectiveGain == other.trace.effectiveGain && trace.requestedRpm == other.trace.requestedRpm && trace.requestedLoad == other.trace.requestedLoad && trace.gear == other.trace.gear;
            public override string ToString() => "out " + frame + " · " + trace.reason + " · " + region.Id + " / " + region.RecordingId + " · source frame " + trace.sourceFrame;
        }

        public static string CompareDecisions(BlackBoxReplayResult a, BlackBoxReplayResult b, int latencyFrames = 0, CancellationToken cancel = default)
        {
            if (a?.snapshot == null || b?.snapshot == null || a.traces == null || b.traces == null)
                return "Renderer-decision comparison unavailable: both sides need captured traces and matching immutable snapshot metadata. A raw WAV provides neither.";
            if (a.settings.outputRate != b.settings.outputRate || a.settings.channels != b.settings.channels)
                throw new ArgumentException("Decision comparison requires matching output rate and channels.");
            if (a.dropped != 0 || b.dropped != 0)
                return "Renderer-decision comparison incomplete: A dropped " + a.dropped + " / B dropped " + b.dropped + ". Missing records cannot prove matching selections.";
            long skipA = Math.Max(0L, latencyFrames), skipB = Math.Max(0L, -(long)latencyFrames);
            long frames = Math.Min(a.summary.FrameCount - skipA, b.summary.FrameCount - skipB);
            var first = Capture(a, skipA, frames, cancel); var second = Capture(b, skipB, frames, cancel);
            int count = Math.Min(first.Count, second.Count), different = Math.Abs(first.Count - second.Count); string mismatch = "";
            for (int i = 0; i < count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                if (first[i].Equals(second[i])) continue;
                different++; if (mismatch.Length == 0) mismatch = " First difference: A " + first[i] + " / B " + second[i] + ".";
            }
            return "Renderer decisions: A " + first.Count + " / B " + second.Count + " grain starts and tick-level exclusions; differences " + different + "." + mismatch +
                " DSP-block render summaries are excluded; original traces remain available. Agreement between native reconstructions is not independent EA control evidence.";
        }

        private static List<Decision> Capture(BlackBoxReplayResult result, long skip, long frames, CancellationToken cancel)
        {
            var values = new List<Decision>(); var exclusions = new HashSet<(long, int, int, EngineAudioTraceReason)>();
            foreach (var trace in result.traces)
            {
                cancel.ThrowIfCancellationRequested(); if (trace.reason == EngineAudioTraceReason.Rendered) continue;
                bool grain = trace.reason == EngineAudioTraceReason.GrainStarted;
                long frame = (grain ? trace.grainStartOutputFrame : trace.outputFrameStart) - skip;
                if (frame < 0 || frame >= frames) continue;
                if (!grain && !exclusions.Add((trace.resetEpoch, trace.telemetryTick, trace.regionIndex, trace.reason))) continue;
                if (trace.snapshotToken != result.snapshot.Token) throw new InvalidOperationException("Trace snapshot identity no longer matches its captured metadata.");
                var region = trace.regionIndex >= 0 && trace.regionIndex < result.snapshot.RegionCount ? result.snapshot.GetRegionInfo(trace.regionIndex) : default;
                values.Add(new Decision(frame, trace, region));
            }
            // Callback partitions may interleave different region records differently. Keep actual event time and stable identity.
            values.Sort((x, y) => { int order = x.frame.CompareTo(y.frame); if (order != 0) return order; order = string.CompareOrdinal(x.region.Id, y.region.Id); return order != 0 ? order : x.trace.reason.CompareTo(y.trace.reason); });
            return values;
        }
    }
}
