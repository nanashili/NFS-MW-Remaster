#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>Offline, original test signals. No recordings, game decodes or runtime synthesis.</summary>
    public static class DiagnosticAudioSynthesis
    {
        public const int SampleRate = 24000;
        private const double Tau = Math.PI * 2;
        private static double Tone(double frequency, double time) => Math.Sin(Tau * frequency * time);
        private static double Decay(double time, double rate) => Math.Exp(-time * rate);

        public static float[] Engine(EngineLayerKind kind, float rpm, bool loaded)
        {
            if (rpm < 100 || rpm > 20000 || float.IsNaN(rpm)) throw new ArgumentOutOfRangeException(nameof(rpm));
            // A diagnostic four-cylinder firing fundamental; integer cycles over two seconds.
            double hz = Math.Round(rpm / 30.0 * 2) / 2;
            return Render(2, kind == EngineLayerKind.Exhaust ? .22 : .12, true, (t, noise) =>
            {
                double body = .62 * Tone(hz, t) + .28 * Tone(hz * 2, t) + .16 * Tone(hz * 3, t);
                double rasp = .13 * Tone(hz * 5, t) + .08 * Tone(hz * 7, t);
                if (kind == EngineLayerKind.Intake) return .35 * body + (loaded ? .35 : .12) * noise + .25 * Tone(hz * 4, t);
                if (kind == EngineLayerKind.Mechanical) return .15 * body + .22 * Tone(hz * 6, t) + .14 * noise;
                if (kind == EngineLayerKind.Transmission) return Tone(hz * 4, t) * .5 + Tone(hz * 8, t) * .1;
                if (kind == EngineLayerKind.Induction) return noise * .5 + Tone(hz * 8, t) * .2;
                return body + (loaded ? 1 : .25) * rasp + noise * (loaded ? .22 : .09);
            }, (uint)(rpm * 7 + (int)kind * 97 + (loaded ? 31 : 71)));
        }

        public static float[] Surface(SensorySurface surface, string role)
        {
            bool wet = surface == SensorySurface.AsphaltWet || surface == SensorySurface.Water;
            bool loose = surface == SensorySurface.Gravel || surface == SensorySurface.Dirt || surface == SensorySurface.Grass;
            bool metal = surface == SensorySurface.Metal || surface == SensorySurface.Glass;
            bool loop = role == "rolling" || role == "slip" || role == "scrape";
            bool heavy = role == "heavy" || role == "destruction";
            double resonance = surface == SensorySurface.Glass ? 2100 : metal ? 780 : surface == SensorySurface.Wood ? 190 : 95;
            return Render(loop ? 2 : heavy ? .9 : .45, loop ? .16 : heavy ? .34 : .24, loop, (t, noise) =>
            {
                double chatter = .65 + .35 * Tone(loose ? 19 : 37, t);
                if (role == "rolling") return noise * chatter + (wet ? 0 : .13 * Tone(loose ? 47 : 67, t));
                if (role == "slip") return wet || loose ? noise * chatter
                    : .55 * Tone(950, t + .00012 * Tone(7, t)) + .25 * Tone(1470, t) + noise * .3;
                if (role == "scrape") return noise * chatter + (metal ? .25 * Tone(resonance, t) : .1 * Tone(140, t));
                double hit = noise * Decay(t, heavy ? 7 : 18) + .6 * Tone(resonance, t) * Decay(t, metal ? 5 : 20);
                if (heavy) hit += .3 * Tone(58, t) * Decay(t, 9);
                if (role == "destruction") hit += noise * Math.Pow(.5 + .5 * Tone(23, t), 4) * Decay(t, 4);
                return hit;
            }, (uint)(571 + (int)surface * 137), wet ? .7 : loose ? .25 : .4);
        }

        public static float[] Vehicle(string role)
        {
            bool loop = role == "nitrous" || role == "wind" || role == "traffic";
            double duration = loop ? 2 : role == "startup" ? 1.4 : .38;
            return Render(duration, loop ? .16 : .25, loop, (t, noise) =>
            {
                switch (role)
                {
                    case "wind": return noise * (.8 + .2 * Tone(1, t));
                    case "traffic": return noise * .7 + Tone(80, t) * .15 + Tone(121, t) * .1;
                    case "nitrous": return noise + Tone(1900, t) * .08;
                    case "startup": return (.5 * Tone(20, t + t * t * .3) + noise * .2) * (.5 + .5 * Tone(14, t))
                        + Tone(30, t) * Math.Min(1, t);
                    case "shift-up": return (Tone(170, t - .4 * t * t) * .65 + noise * .2) * Decay(t, 14);
                    case "shift-down": return (Tone(95, t + t * t) * .65 + noise * .2) * Decay(t, 9);
                    case "limiter": return (Tone(180, t) + noise * .2) * Math.Pow(.5 + .5 * Tone(24, t), 3) * Decay(t, 3);
                    case "overrun": return (noise + Tone(70, t) * .3) * Math.Pow(.5 + .5 * Tone(17, t), 8) * Decay(t, 10);
                    default: throw new ArgumentException("Unknown diagnostic vehicle sound: " + role);
                }
            }, 811, role == "nitrous" ? .8 : .18);
        }

        public static float[] Siren(int variant)
        {
            if (variant < 0 || variant > 2) throw new ArgumentOutOfRangeException(nameof(variant));
            double rate = variant == 0 ? .5 : variant == 1 ? 4 : 1.5;
            return Render(4, .21, true, (t, _) =>
            {
                // Integral of a periodic 550..1050 Hz sweep: continuous phase and seamless four-second loops.
                double phase = Tau * 800 * t + 250 / rate * (1 - Math.Cos(Tau * rate * t));
                return Math.Sin(phase) + .16 * Math.Sin(3 * phase);
            });
        }

        public static float[] Radio(int cue)
        {
            if (cue < 0 || cue > 12) throw new ArgumentOutOfRangeException(nameof(cue));
            return Render(.85, .19, false, (t, noise) =>
            {
                double pulse = t % .22;
                double gate = pulse < .14 ? Math.Sin(Math.PI * pulse / .14) : 0;
                return Tone(680 + cue * 37, t) * gate * .5 + noise * .16;
            }, (uint)(975 + cue));
        }

        public static float[] Music(int stem, double bpm, int beatsPerBar, int bars)
        {
            if (stem < 0 || stem > 3 || double.IsNaN(bpm) || bpm < 30 || bpm > 240 || beatsPerBar < 1 || bars < 1)
                throw new ArgumentOutOfRangeException(nameof(bpm));
            double beat = 60 / bpm, duration = beat * beatsPerBar * bars;
            int[] notes = { 0, 0, 7, 3, 0, 10, 7, 3 };
            return Render(duration, stem == 0 ? .16 : .12, true, (t, noise) =>
            {
                double within = t % beat;
                double eighth = t % (beat / 2);
                int step = (int)(t / (beat / 2));
                if (stem == 0)
                {
                    double hz = 55 * Math.Pow(2, notes[step % notes.Length] / 12.0);
                    return (Tone(hz, eighth) + Tone(hz * 2, eighth) * .22) * Math.Sin(Math.PI * eighth / (beat / 2));
                }
                if (stem == 1)
                {
                    double kick = Math.Sin(Tau * (52 * within + 2.4 * (1 - Decay(within, 35)))) * Decay(within, 18);
                    double snare = ((int)(t / beat) % 2 == 1 ? noise * Decay(within, 28) : 0);
                    return kick * .6 + snare * .65 + noise * Decay(eighth, 80) * .1;
                }
                double note = 220 * Math.Pow(2, notes[(step * 3) % notes.Length] / 12.0);
                return (Tone(note, eighth) + Tone(note * 2, eighth) * .18) * Math.Sin(Math.PI * eighth / (beat / 2)) * Decay(eighth, 8);
            }, (uint)(673 + stem), .8);
        }

        public static float[] Stinger(int outcome)
        {
            if (outcome < 0 || outcome > 2) throw new ArgumentOutOfRangeException(nameof(outcome));
            return Render(1.4, .23, false, (t, _) =>
            {
                int note = (int)Math.Min(3, t / .22);
                double frequency = 220 * Math.Pow(2, (outcome == 1 ? 9 - note * 3 : note * (outcome == 2 ? 4 : 5)) / 12.0);
                double local = t - note * .22;
                return (Tone(frequency, local) + Tone(frequency * 2, local) * .2) * Math.Min(1, local / .008) * Decay(local, 6);
            });
        }

        private static float[] Render(double seconds, double peak, bool loop, Func<double, double, double> signal,
            uint seed = 173, double filter = .4)
        {
            if (double.IsNaN(seconds) || seconds <= 0 || seconds > 60) throw new ArgumentOutOfRangeException(nameof(seconds));
            int count = (int)Math.Round(seconds * SampleRate), overlap = loop ? Math.Min(1200, count / 4) : 0;
            var buffer = new float[count + overlap];
            double low = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
                low += filter * (seed / (double)uint.MaxValue * 2 - 1 - low);
                double t = i / (double)SampleRate;
                double value = signal(t, low);
                if (!loop) value *= Math.Min(1, t / .006) * Math.Min(1, (count - 1 - i) / (SampleRate * .025));
                if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidDataException("Non-finite diagnostic signal.");
                buffer[i] = (float)value;
            }
            // Wrap the tail into the start without inserting silence or changing the exact music duration.
            for (int i = 0; i < overlap; i++)
            {
                double weight = .5 - .5 * Math.Cos(Math.PI * i / overlap);
                buffer[i] = (float)(buffer[count + i] * (1 - weight) + buffer[i] * weight);
            }
            double maximum = 0;
            for (int i = 0; i < count; i++) maximum = Math.Max(maximum, Math.Abs(buffer[i]));
            if (maximum < .000001) throw new InvalidDataException("Silent diagnostic signal.");
            var result = new float[count];
            for (int i = 0; i < count; i++) result[i] = (float)(buffer[i] * peak / maximum);
            return result;
        }

        public static void WriteWave(Stream destination, float[] samples)
        {
            if (samples == null || samples.Length == 0 || samples.Length > SampleRate * 60) throw new ArgumentException("Invalid PCM length.");
            foreach (float sample in samples)
                if (float.IsNaN(sample) || float.IsInfinity(sample) || Math.Abs(sample) > 1) throw new InvalidDataException("Invalid PCM sample.");
            using (var writer = new BinaryWriter(destination, Encoding.ASCII, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Length * 2);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(SampleRate); writer.Write(SampleRate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2);
                foreach (float sample in samples) writer.Write((short)Math.Round(sample * short.MaxValue));
            }
        }
    }
}
#endif
