using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NfsMwRemaster.Driving.AudioAnalysis.Analysis;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    public enum BlackBoxScenario { Fixed, Sweep, ThrottleLift, Shifts, Limiter }

    [Serializable]
    public sealed class BlackBoxReplaySettings
    {
        public BlackBoxScenario scenario;
        public int outputRate = 48000, channels = 1, blockFrames = 256, controlHz = 50;
        public float seconds = 4, rpm = 3500, endRpm = 7500, load = 0.8f, gain = 0.5f;
        public int gear = 3;
        public string semantics = "Authored test telemetry. No inferred EA controls; no RNG, smoothing or hidden game state.";
        public BlackBoxReplaySettings Copy() => (BlackBoxReplaySettings)MemberwiseClone();
        public void Validate()
        {
            if (outputRate < 8000 || outputRate > 192000 || channels < 1 || channels > 2 || blockFrames < 16 || blockFrames > 4096 || controlHz < 1 || controlHz > 200 ||
                !Finite(seconds) || seconds <= 0 || seconds > 20 || !Finite(rpm) || !Finite(endRpm) || rpm <= 0 || endRpm <= 0 ||
                !Finite(load) || load < 0 || load > 1 || !Finite(gain) || gain < 0 || gain > 1)
                throw new ArgumentException("Replay requires 8–192 kHz, 1–2 channels, 16–4096 frames/block, 1–200 Hz control, 0–20 s and finite positive RPM.");
        }
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
    [Serializable]
    public struct BlackBoxTelemetry
    { public int tick, gear; public long outputFrame; public double seconds; public float rpm, load; public bool running; }

    public sealed class BlackBoxReplayResult
    {
        public EngineAudioCompiledSnapshot snapshot;
        public float[] pcm;
        public BlackBoxReplaySettings settings;
        public BlackBoxTelemetry[] telemetry;
        public EngineAudioTrace[] traces;
        public WaveformSummary summary;
        public long dropped;
        public double renderMilliseconds;
        public string fingerprint;
    }

    /// <summary>Offline driver for the SAME production renderer. Seek reconstructs every control tick from zero.</summary>
    public static class BlackBoxReplay
    {
        public static BlackBoxTelemetry At(BlackBoxReplaySettings s, int tick)
        {
            double seconds = tick / (double)s.controlHz, t = Math.Min(1, seconds / s.seconds);
            // Round the rational tick/rate transform once; time-as-double is descriptive only.
            var value = new BlackBoxTelemetry { tick = tick, seconds = seconds, outputFrame = (long)Math.Round(tick * (double)s.outputRate / s.controlHz),
                rpm = s.rpm, load = s.load, gear = s.gear, running = true };
            switch (s.scenario)
            {
                case BlackBoxScenario.Sweep: value.rpm = (float)(s.rpm + (s.endRpm - s.rpm) * t); break;
                case BlackBoxScenario.ThrottleLift: value.load = t >= 0.5 ? 0 : s.load; value.rpm = (float)(s.endRpm + (s.rpm - s.endRpm) * t); break;
                case BlackBoxScenario.Shifts:
                    double phase = t * 3; value.gear = s.gear + Math.Min(2, (int)phase);
                    value.rpm = (float)(s.rpm + (s.endRpm - s.rpm) * (phase - Math.Floor(phase))); value.load = phase % 1 < 0.08 ? 0 : s.load; break;
                case BlackBoxScenario.Limiter:
                    value.rpm = (float)(s.rpm + (s.endRpm - s.rpm) * Math.Min(1, t * 2));
                    if (t >= 0.5) value.running = tick % Math.Max(2, s.controlHz / 10) != 0;
                    break;
            }
            return value;
        }

        public static BlackBoxReplayResult Render(EngineAudioCompiledSnapshot snapshot, BlackBoxReplaySettings settings,
            string fingerprint, int throughFrame = 0, CancellationToken cancel = default)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot)); settings.Validate();
            int frames = checked((int)Math.Round(settings.seconds * settings.outputRate));
            if (throughFrame > 0) frames = Math.Min(frames, throughFrame);
            var output = new float[checked(frames * settings.channels)]; var block = new float[settings.blockFrames * settings.channels];
            var renderer = new EngineAudioRenderer(1024); renderer.SetTelemetry(snapshot);
            var telemetry = new List<BlackBoxTelemetry>(); var traces = new List<EngineAudioTrace>();
            int position = 0, tick = 0; var watch = Stopwatch.StartNew();
            while (position < frames)
            {
                cancel.ThrowIfCancellationRequested(); var input = At(settings, tick); telemetry.Add(input);
                renderer.SetFrame(input.rpm, input.load, input.running, tick, input.seconds, 0, input.gear);
                int next = Math.Min(frames, checked((int)At(settings, ++tick).outputFrame));
                while (position < next)
                {
                    cancel.ThrowIfCancellationRequested(); int count = Math.Min(settings.blockFrames, next - position);
                    renderer.Render(input.rpm, input.load, settings.outputRate, count, settings.gain, 1, block, 4, settings.channels);
                    Array.Copy(block, 0, output, position * settings.channels, count * settings.channels); position += count;
                    while (renderer.Trace.TryRead(out var trace)) { if (traces.Count < 200000) traces.Add(trace); else throw new InvalidOperationException("Replay trace budget exceeded."); }
                }
            }
            watch.Stop();
            return new BlackBoxReplayResult { snapshot = snapshot, pcm = output, settings = settings, telemetry = telemetry.ToArray(), traces = traces.ToArray(),
                dropped = renderer.Trace.Dropped, renderMilliseconds = watch.Elapsed.TotalMilliseconds, fingerprint = fingerprint,
                summary = SignalAnalysis.Summarize(output, settings.outputRate, settings.channels, cancel) };
        }
    }
}
