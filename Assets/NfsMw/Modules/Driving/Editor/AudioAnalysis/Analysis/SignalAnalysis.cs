using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace NfsMwRemaster.Driving.AudioAnalysis.Analysis
{
    public sealed class SpectrogramParameters
    {
        public int WindowSize = 1024;
        public int HopSize = 256;
        public int MaxColumns = 4096;
        public string EngineOrder = "unspecified";
        public override string ToString() { return WindowSize + ":" + HopSize + ":" + MaxColumns + ":" + (EngineOrder ?? ""); }
    }

    public static class SignalAnalysis
    {
        public const string AlgorithmVersion = "audio-analysis-analysis-1";

        public static WaveformSummary Summarize(float[] interleaved, int sampleRate, int channels, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (interleaved == null) throw new ArgumentNullException("interleaved");
            if (sampleRate <= 0 || channels <= 0 || interleaved.Length % channels != 0) throw new ArgumentException("Invalid PCM shape.");
            double sum = 0; float peak = 0;
            for (int i = 0; i < interleaved.Length; i++)
            {
                if ((i & 8191) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (float.IsNaN(interleaved[i]) || float.IsInfinity(interleaved[i])) throw new ArgumentException("Non-finite PCM is not analyzable.", "interleaved");
                float value = Math.Abs(interleaved[i]); if (value > peak) peak = value; sum += interleaved[i] * (double)interleaved[i];
            }
            return new WaveformSummary(sampleRate, channels, interleaved.Length / channels, peak, Math.Sqrt(sum / Math.Max(1, interleaved.Length)), AlgorithmVersion);
        }

        public static SpectrogramResult Spectrogram(float[] interleaved, int sampleRate, int channels, SpectrogramParameters parameters, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (parameters == null || parameters.WindowSize < 8 || parameters.WindowSize > 4096 || (parameters.WindowSize & (parameters.WindowSize - 1)) != 0 || parameters.HopSize <= 0 || parameters.MaxColumns <= 0 || parameters.MaxColumns > 4096) throw new ArgumentException("Invalid or unbounded spectrogram parameters.");
            if (interleaved == null || channels <= 0 || interleaved.Length % channels != 0) throw new ArgumentException("Invalid PCM shape.");
            int frames = interleaved.Length / channels;
            for (int i = 0; i < interleaved.Length; i++) if (float.IsNaN(interleaved[i]) || float.IsInfinity(interleaved[i])) throw new ArgumentException("Non-finite PCM is not analyzable.", "interleaved");
            int columns = frames < parameters.WindowSize ? 0 : 1 + (frames - parameters.WindowSize) / parameters.HopSize;
            columns = Math.Min(columns, Math.Max(1, parameters.MaxColumns));
            int bins = parameters.WindowSize / 2 + 1;
            var magnitudes = new float[columns * bins];
            var window = new double[parameters.WindowSize];
            for (int n = 0; n < window.Length; n++) window[n] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (window.Length - 1));
            for (int column = 0; column < columns; column++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int offset = column * parameters.HopSize;
                var realPart = new double[parameters.WindowSize]; var imaginaryPart = new double[parameters.WindowSize];
                for (int n = 0; n < parameters.WindowSize; n++) realPart[n] = interleaved[(offset + n) * channels] * window[n];
                Fft(realPart, imaginaryPart, false);
                for (int k = 0; k < bins; k++) magnitudes[column * bins + k] = (float)Math.Sqrt(realPart[k] * realPart[k] + imaginaryPart[k] * imaginaryPart[k]);
            }
            string hash = Hash(parameters.ToString() + ":" + sampleRate + ":" + channels + ":" + frames + ":" + HashFloats(interleaved));
            return new SpectrogramResult(sampleRate, parameters.WindowSize, parameters.HopSize, bins, columns, magnitudes, hash, AlgorithmVersion);
        }

        /// <summary>Finds spectral peaks and retains each requested engine-order interpretation.</summary>
        public static IReadOnlyList<FrequencyCandidate> FrequencyCandidates(float[] mono, int sampleRate, EngineOrderModel model, double rpmPerHz, int maxCandidates = 8, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (mono == null || mono.Length < 16 || sampleRate <= 0 || rpmPerHz <= 0 || double.IsNaN(rpmPerHz) || double.IsInfinity(rpmPerHz) || maxCandidates <= 0) throw new ArgumentException("Invalid signal or frequency scale.");
            for (int i = 0; i < mono.Length; i++) if (float.IsNaN(mono[i]) || float.IsInfinity(mono[i])) throw new ArgumentException("Non-finite PCM is not analyzable.", "mono");
            int n = 1; while (n * 2 <= mono.Length && n < 4096) n *= 2;
            int bins = n / 2; var magnitudes = new double[bins]; var real = new double[n]; var imaginary = new double[n];
            for (int i = 0; i < n; i++) real[i] = mono[i];
            cancellationToken.ThrowIfCancellationRequested(); Fft(real, imaginary, false);
            for (int k = 1; k < bins; k++) magnitudes[k] = Math.Sqrt(real[k] * real[k] + imaginary[k] * imaginary[k]) / n;
            var peaks = new List<FrequencyCandidate>();
            for (int k = 1; k < bins - 1; k++)
            {
                if (magnitudes[k] < magnitudes[k - 1] || magnitudes[k] < magnitudes[k + 1]) continue;
                double hz = k * (double)sampleRate / n;
                for (int harmonic = 1; harmonic <= 4; harmonic++)
                {
                    string assumption = (model == EngineOrderModel.Unknown ? "engine order unspecified" : "explicit engine-order model") + "; harmonic " + harmonic;
                    peaks.Add(new FrequencyCandidate(hz, hz * rpmPerHz / harmonic, magnitudes[k], harmonic, model, assumption));
                }
            }
            peaks.Sort((a, b) => b.Strength.CompareTo(a.Strength));
            if (peaks.Count > maxCandidates) peaks.RemoveRange(maxCandidates, peaks.Count - maxCandidates);
            return peaks;
        }

        public static SignalComparison Compare(float[] a, float[] b, int channels, long declaredLatencyFrames = 0)
        {
            if (a == null || b == null || channels <= 0 || a.Length % channels != 0 || b.Length % channels != 0) throw new ArgumentException("Invalid PCM shape.");
            for (int i = 0; i < a.Length; i++) if (float.IsNaN(a[i]) || float.IsInfinity(a[i])) throw new ArgumentException("Non-finite PCM is not comparable.", "a");
            for (int i = 0; i < b.Length; i++) if (float.IsNaN(b[i]) || float.IsInfinity(b[i])) throw new ArgumentException("Non-finite PCM is not comparable.", "b");
            long latencyScalars = declaredLatencyFrames == long.MinValue ? long.MaxValue : Math.Abs(declaredLatencyFrames) * (long)channels;
            int boundedLatency = latencyScalars > int.MaxValue ? int.MaxValue : (int)latencyScalars;
            int startA = declaredLatencyFrames > 0 ? boundedLatency : 0;
            int startB = declaredLatencyFrames < 0 ? boundedLatency : 0;
            int count = Math.Min(a.Length - startA, b.Length - startB); if (count < 0) count = 0;
            double sum = 0, sumA = 0, sumB = 0; float peak = 0;
            for (int i = 0; i < count; i++) { double x = a[startA + i], y = b[startB + i], d = Math.Abs(x - y); if (d > peak) peak = (float)d; sum += d * d; sumA += x * x; sumB += y * y; }
            return new SignalComparison(declaredLatencyFrames, count / channels, peak, Math.Sqrt(sum / Math.Max(1, count)), Math.Sqrt(sumA / Math.Max(1, count)), Math.Sqrt(sumB / Math.Max(1, count)), a.Length == b.Length);
        }

        static string Hash(string value)
        {
            using (var sha = SHA256.Create()) { byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")); var sb = new StringBuilder(bytes.Length * 2); foreach (byte b in bytes) sb.Append(b.ToString("x2")); return sb.ToString(); }
        }
        static string HashFloats(float[] values)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = new byte[values.Length * 4]; Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
                byte[] digest = sha.ComputeHash(bytes); var sb = new StringBuilder(digest.Length * 2); foreach (byte b in digest) sb.Append(b.ToString("x2")); return sb.ToString();
            }
        }
        static void Fft(double[] real, double[] imaginary, bool inverse)
        {
            int n = real.Length;
            for (int i = 1, j = 0; i < n; i++) { int bit = n >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit; if (i < j) { double t = real[i]; real[i] = real[j]; real[j] = t; t = imaginary[i]; imaginary[i] = imaginary[j]; imaginary[j] = t; } }
            for (int length = 2; length <= n; length <<= 1)
            {
                double angle = 2 * Math.PI / length * (inverse ? 1 : -1), wr = Math.Cos(angle), wi = Math.Sin(angle);
                for (int start = 0; start < n; start += length) { double ur = 1, ui = 0; int half = length >> 1; for (int i = 0; i < half; i++) { int even = start + i, odd = even + half; double tr = ur * real[odd] - ui * imaginary[odd], ti = ur * imaginary[odd] + ui * real[odd]; real[odd] = real[even] - tr; imaginary[odd] = imaginary[even] - ti; real[even] += tr; imaginary[even] += ti; double next = ur * wr - ui * wi; ui = ur * wi + ui * wr; ur = next; } }
            }
            if (inverse) for (int i = 0; i < n; i++) { real[i] /= n; imaginary[i] /= n; }
        }
    }
}
