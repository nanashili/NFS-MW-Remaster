using System;
using System.Threading;

namespace NfsMwRemaster.Driving
{
    public interface IProceduralVehicleAudio
    {
        bool IsReady { get; }
        void RenderInto(float[] output, int channels, int sampleRate);
        void RenderInto(float[] output, int channels, int sampleRate, int frameCount);
    }
    /// <summary>Sample-accurate PCM cursors and sustain bounds; only this renderer owns audio-thread cursors.</summary>
    public sealed class AemsPcmRenderer : IProceduralVehicleAudio
    {
        /// <summary>Playback multipliers are finite and clamped to this range.</summary>
        public const float SupportedPlaybackMinimum = 0.25f, SupportedPlaybackMaximum = 4f;
        public bool IsReady => true;
        public sealed class Recording
        {
            public int SampleId, SampleRate, Channels, LoopStart, LoopEnd;
            public float[] Pcm;
            public int Frames => Pcm.Length / Channels;
        }
        private struct VoiceCommand { public int Generation, Sample, Control; public float Gain, Pitch, Tempo; public bool Active; }
        private readonly Recording[] recordings;
        private readonly Recording[] activeRecordings;
        private readonly double[] cursor, phaseA, phaseB;
        private readonly int[] generation, completed;
        private readonly float[] gain, elapsed, remaining;
        private readonly int[] ageA, ageB, untilGrain;
        private VoiceCommand[] controls;
        private float playbackPitch = 1, playbackTempo = 1;
        private volatile float requestedPitch = 1, requestedTempo = 1;
        public AemsPcmRenderer(Recording[] recordings, int players)
        {
            if (recordings == null || recordings.Length > 4096 || players < 1 || players > 32) throw new ArgumentException("Invalid AEMS renderer budget.");
            foreach (var rec in recordings)
                if (rec == null || rec.Pcm == null || rec.SampleRate < 1 || rec.Channels < 1 || rec.Channels > 8 || rec.Pcm.Length % rec.Channels != 0
                    || rec.Frames < 1 || rec.LoopStart < 0 || rec.LoopEnd < 0 || rec.LoopEnd > rec.Frames || rec.LoopEnd > 0 && rec.LoopEnd <= rec.LoopStart)
                    throw new ArgumentException("Invalid decoded AEMS recording.");
            VehicleAudioWindow.Warmup();
            this.recordings = recordings; cursor = new double[players]; generation = new int[players]; completed = new int[players];
            gain = new float[players]; elapsed = new float[players]; remaining = new float[players]; controls = new VoiceCommand[players];
            phaseA = new double[players]; phaseB = new double[players]; ageA = new int[players]; ageB = new int[players];
            untilGrain = new int[players]; activeRecordings = new Recording[players];
        }
        /// <summary>Sets the authored-rate layer. Both values are clamped to 0.25..4 and non-finite values become 1.</summary>
        public void SetPlayback(float pitch, float tempo)
        { requestedPitch = ClampPlayback(pitch); requestedTempo = ClampPlayback(tempo); }
        private static float ClampPlayback(float value)
        { return float.IsNaN(value) || float.IsInfinity(value) ? 1f : Math.Max(SupportedPlaybackMinimum, Math.Min(SupportedPlaybackMaximum, value)); }
        public void Publish(AemsEvaluator.Player[] players)
        {
            if (players.Length != cursor.Length) throw new ArgumentException("AEMS player count changed.");
            var next = new VoiceCommand[players.Length];
            for (int i = 0; i < next.Length; i++)
            {
                var p = players[i];
                if (p.Generation != 0 && Volatile.Read(ref completed[i]) == p.Generation) p.Completed = true;
                bool current = Volatile.Read(ref generation[i]) == p.Generation;
                p.ElapsedMilliseconds = current ? Volatile.Read(ref elapsed[i]) : 0;
                p.RemainingMilliseconds = current ? Volatile.Read(ref remaining[i]) : 0;
                next[i] = new VoiceCommand { Generation = p.Generation, Sample = p.Sample, Control = p.Control, Gain = p.Gain, Pitch = p.Pitch, Tempo = p.TimeScale, Active = p.Active };
            }
            Volatile.Write(ref controls, next);
        }
        public void RenderInto(float[] output, int channels, int sampleRate)
        { RenderInto(output, channels, sampleRate, output != null && channels > 0 ? output.Length / channels : 0); }
        public void RenderInto(float[] output, int channels, int sampleRate, int frameCount)
        {
            if (output == null || channels < 1 || channels > 8 || sampleRate < 1 || frameCount < 0 || frameCount > output.Length / channels) return;
            Array.Clear(output, 0, frameCount * channels);
            if (frameCount == 0) return;
            var current = Volatile.Read(ref controls);
            float targetPitch = requestedPitch, targetTempo = requestedTempo;
            float smoothing = 1f - (float)Math.Exp(-1.0 / (sampleRate * 0.0125));
            int hop = Math.Max(1, sampleRate / 50), grainLength = hop * 2;
            for (int i = 0; i < current.Length; i++)
            {
                var control = current[i];
                if (control.Generation != generation[i])
                {
                    cursor[i] = phaseA[i] = 0; phaseB[i] = double.NaN; ageA[i] = hop; ageB[i] = grainLength;
                    untilGrain[i] = hop; gain[i] = elapsed[i] = remaining[i] = 0;
                    Volatile.Write(ref generation[i], control.Generation);
                }
                activeRecordings[i] = null;
                if (!control.Active || control.Control != 1 || completed[i] == control.Generation && control.Generation != 0) continue;
                foreach (var candidate in recordings) if (candidate.SampleId == control.Sample) { activeRecordings[i] = candidate; break; }
                if (activeRecordings[i] == null) Volatile.Write(ref completed[i], control.Generation);
            }
            // Controls advance once per output sample, regardless of player count or callback partitioning.
            for (int frame = 0; frame < frameCount; frame++)
            {
                playbackPitch += (targetPitch - playbackPitch) * smoothing;
                playbackTempo += (targetTempo - playbackTempo) * smoothing;
                for (int i = 0; i < current.Length; i++)
                {
                    var rec = activeRecordings[i]; if (rec == null) continue;
                    var control = current[i];
                    if (rec.LoopEnd == 0 && cursor[i] >= rec.Frames)
                    { Volatile.Write(ref completed[i], control.Generation); activeRecordings[i] = null; continue; }
                    if (untilGrain[i] <= 0)
                    {
                        if (ageA[i] >= grainLength) { phaseA[i] = cursor[i]; ageA[i] = 0; }
                        else { phaseB[i] = cursor[i]; ageB[i] = 0; }
                        untilGrain[i] = hop;
                    }
                    float a = ageA[i] < grainLength ? Window(ageA[i], grainLength) : 0;
                    float b = ageB[i] < grainLength ? Window(ageB[i], grainLength) : 0;
                    float normalization = a + b;
                    float targetGain = FiniteClamp(control.Gain, 0, 0, 1);
                    gain[i] += (targetGain - gain[i]) * smoothing;
                    for (int channel = 0; channel < channels; channel++)
                    {
                        int sourceChannel = rec.Channels == 1 ? 0 : Math.Min(channel, rec.Channels - 1);
                        float value = a > 0 ? Read(rec, phaseA[i], sourceChannel) * a : 0;
                        if (b > 0) value += Read(rec, phaseB[i], sourceChannel) * b;
                        if (normalization > .00001f) output[frame * channels + channel] += value / normalization * gain[i];
                    }
                    double rate = FiniteClamp(control.Pitch, 1, 0, 16) * rec.SampleRate / (double)sampleRate;
                    double pitchStep = rate * playbackPitch;
                    phaseA[i] += pitchStep; phaseB[i] += pitchStep;
                    cursor[i] += rate * playbackTempo * FiniteClamp(control.Tempo, 1, .5f, 2);
                    ageA[i]++; ageB[i]++; untilGrain[i]--;
                    if (rec.LoopEnd == 0 && cursor[i] >= rec.Frames)
                    { Volatile.Write(ref completed[i], control.Generation); activeRecordings[i] = null; }
                }
            }
            for (int i = 0; i < current.Length; i++)
            {
                Recording rec = null; foreach (var candidate in recordings) if (candidate.SampleId == current[i].Sample) { rec = candidate; break; }
                if (rec == null) continue;
                Volatile.Write(ref elapsed[i], (float)(cursor[i] * 1000 / rec.SampleRate));
                Volatile.Write(ref remaining[i], rec.LoopEnd > 0 ? 0 : (float)Math.Max(0, (rec.Frames - cursor[i]) * 1000 / rec.SampleRate));
            }
        }
        private static float Window(int age, int length) => VehicleAudioWindow.Hann(age / (double)length);
        private static float FiniteClamp(float value, float fallback, float low, float high)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(low, Math.Min(high, value));
        private static float Read(Recording rec, double position, int channel)
        {
            int end = rec.LoopEnd > 0 ? rec.LoopEnd : rec.Frames;
            if (rec.LoopEnd > 0 && position >= end) position = rec.LoopStart + (position - end) % (end - rec.LoopStart);
            if (position < 0 || position >= end) return 0;
            int a = (int)position, b = a + 1;
            if (b >= end) b = rec.LoopEnd > 0 ? rec.LoopStart : a;
            float first = rec.Pcm[a * rec.Channels + channel];
            return first + (rec.Pcm[b * rec.Channels + channel] - first) * (float)(position - a);
        }
    }

    /// <summary>Shared immutable window table is built on the main thread, before any renderer callback.</summary>
    internal static class VehicleAudioWindow
    {
        private const int Resolution = 2048;
        private static readonly float[] values = new float[Resolution + 1];
        static VehicleAudioWindow()
        { for (int i = 0; i <= Resolution; i++) values[i] = (float)(.5 - .5 * Math.Cos(2 * Math.PI * i / Resolution)); }
        internal static void Warmup() { }
        internal static float Hann(double phase)
        {
            double position = Math.Max(0, Math.Min(1, phase)) * Resolution;
            int a = (int)position, b = Math.Min(Resolution, a + 1);
            return values[a] + (values[b] - values[a]) * (float)(position - a);
        }
    }
}
