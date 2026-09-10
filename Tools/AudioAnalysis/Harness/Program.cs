using System;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NfsMwRemaster.Driving.AudioAnalysis;

internal static class Program
{
    private sealed class Options
    {
        public string Output = "Library/BlackBoxAudio/audio-analysis-report.json";
        public string Vgmstream;
        public string ReferenceDirectory;
        public bool StrictReference;
        public readonly List<string> Inputs = new();
    }

    private sealed class RunReport
    {
        public string GeneratedUtc { get; set; }
        public string Machine { get; set; }
        public string ParserSource { get; set; }
        public string ParserSourceSha256 { get; set; }
        public List<FixtureResult> Fixtures { get; set; } = new();
        public bool Passed { get; set; }
    }

    private sealed class FixtureResult
    {
        public string Input { get; set; }
        public string SourceSha256 { get; set; }
        public long SourceBytes { get; set; }
        public string Variant { get; set; }
        public string DetectedByteOrder { get; set; }
        public double ParseElapsedMilliseconds { get; set; }
        public bool IsPartial { get; set; }
        public bool FieldRangeInvariant { get; set; }
        public List<Capability> Capabilities { get; set; }
        public List<FieldEvidence> Fields { get; set; }
        public List<StructuralTable> Tables { get; set; }
        public List<LogicalReference> References { get; set; }
        public List<RecordingResult> Recordings { get; set; } = new();
        public List<AnalysisGap> Gaps { get; set; }
        public List<ByteRange> UnknownSpans { get; set; }
    }

    private sealed class RecordingResult
    {
        public string Id { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int ParserFrames { get; set; }
        public bool Decoded { get; set; }
        public string Codec { get; set; }
        public double DecodeElapsedMilliseconds { get; set; }
        public string ReferenceWav { get; set; }
        public string ReferenceSha256 { get; set; }
        public int ReferenceSampleRate { get; set; }
        public int ReferenceChannels { get; set; }
        public bool MetadataMatch { get; set; }
        public int ReferenceFrames { get; set; }
        public int ComparedFrames { get; set; }
        public int MissingReferenceFrames { get; set; }
        public int MissingParserFrames { get; set; }
        public float MaxAbsolutePcmError { get; set; }
        public int MismatchedSamples { get; set; }
        public bool ReferenceMatch { get; set; }
        public string ReferenceStatus { get; set; }
    }

    private sealed class WavData
    {
        public int SampleRate;
        public int Channels;
        public float[] Samples = Array.Empty<float>();
    }

    public static int Main(string[] args)
    {
        try
        {
            var options = ParseOptions(args);
            if (options.Inputs.Count == 0) throw new ArgumentException("Provide one or more binary paths.");
            var parserPath = Environment.GetEnvironmentVariable("BLACKBOX_AUDIO_PARSER_SOURCE") ?? Path.Combine(Directory.GetCurrentDirectory(), "Assets/NfsMw/Modules/Driving/Editor/AudioAnalysis/Binary/BinaryAnalysis.cs");
            var report = new RunReport { GeneratedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), Machine = Environment.OSVersion + "; " + RuntimeInformation(), ParserSource = parserPath, ParserSourceSha256 = File.Exists(parserPath) ? Sha256(File.ReadAllBytes(parserPath)) : "missing" };
            bool passed = true;
            foreach (var input in options.Inputs) passed &= Inspect(input, options, report);
            report.Passed = passed;
            var output = Path.GetFullPath(options.Output);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Converters = { new JsonStringEnumConverter() } });
            File.WriteAllText(output, json + Environment.NewLine);
            Console.WriteLine($"Audio analysis report: {output}");
            Console.WriteLine($"Fixtures: {report.Fixtures.Count}; passed={report.Passed}; parser={report.ParserSourceSha256}");
            return report.Passed ? 0 : 2;
        }
        catch (Exception ex) { Console.Error.WriteLine("audio-analysis: " + ex.Message); return 64; }
    }

    private static bool Inspect(string input, Options options, RunReport report)
    {
        string path = Path.GetFullPath(input);
        byte[] bytes = File.ReadAllBytes(path);
        var parser = new BinaryAnalysisParser();
        var timer = Stopwatch.StartNew();
        var evidence = parser.Parse(bytes, path);
        timer.Stop();
        bool fieldRangeInvariant = evidence.Fields.All(f => f.Range.Length >= 0 && f.RawHex != null && f.RawHex.Length == f.Range.Length * 2);
        var result = new FixtureResult { Input = path, SourceSha256 = evidence.Source.Sha256, SourceBytes = bytes.LongLength, Variant = evidence.Variant, DetectedByteOrder = evidence.DetectedByteOrder?.ToString(), ParseElapsedMilliseconds = timer.Elapsed.TotalMilliseconds, IsPartial = evidence.IsPartial, FieldRangeInvariant = fieldRangeInvariant, Capabilities = evidence.Capabilities, Fields = evidence.Fields, Tables = evidence.Tables, References = evidence.References, Gaps = evidence.Gaps, UnknownSpans = evidence.UnknownSpans };
        bool passed = fieldRangeInvariant;
        for (int i = 0; i < evidence.Recordings.Count; i++)
        {
            var recording = evidence.Recordings[i];
            var rr = new RecordingResult { Id = recording.Id, SampleRate = recording.SampleRate, Channels = recording.Channels, ParserFrames = recording.ValidFrames, Decoded = recording.Decoded, Codec = recording.Codec };
            rr.DecodeElapsedMilliseconds = recording.DecodeElapsedMilliseconds;
            string reference = ResolveReference(path, i, options);
            if (reference == null) { rr.ReferenceStatus = options.ReferenceDirectory == null ? "not-requested" : "missing"; if (options.StrictReference) passed = false; result.Recordings.Add(rr); continue; }
            if (options.Vgmstream != null && !RunVgmstream(options.Vgmstream, path, i + 1, reference)) { rr.ReferenceStatus = "vgmstream-failed"; passed = false; result.Recordings.Add(rr); continue; }
            try
            {
                var wav = ReadWav(reference); rr.ReferenceWav = reference; rr.ReferenceSha256 = Sha256(File.ReadAllBytes(reference)); rr.ReferenceSampleRate = wav.SampleRate; rr.ReferenceChannels = wav.Channels; rr.MetadataMatch = wav.SampleRate == recording.SampleRate && wav.Channels == recording.Channels; rr.ReferenceFrames = wav.Samples.Length; rr.ComparedFrames = Math.Min(recording.ValidFrames, wav.Samples.Length); rr.MissingReferenceFrames = Math.Max(0, recording.ValidFrames - wav.Samples.Length); rr.MissingParserFrames = Math.Max(0, wav.Samples.Length - recording.ValidFrames);
                float max = 0; int mismatch = 0; const float tolerance = 1f / 32768f;
                for (int n = 0; n < rr.ComparedFrames; n++) { float d = Math.Abs(recording.Pcm[n] - wav.Samples[n]); if (d > max) max = d; if (d > tolerance) mismatch++; }
                rr.MaxAbsolutePcmError = max; rr.MismatchedSamples = mismatch; rr.ReferenceMatch = rr.MetadataMatch && mismatch == 0 && rr.MissingReferenceFrames == 0; rr.ReferenceStatus = rr.ReferenceMatch ? "match" : "mismatch"; if (!rr.ReferenceMatch) passed = false;
            }
            catch (Exception ex) { rr.ReferenceStatus = "invalid-wav: " + ex.Message; passed = false; }
            result.Recordings.Add(rr);
        }
        report.Fixtures.Add(result); Console.WriteLine($"{Path.GetFileName(path)}: {evidence.Variant}, parse={timer.Elapsed.TotalMilliseconds:F3}ms, recordings={evidence.Recordings.Count}");
        return passed;
    }

    private static string ResolveReference(string input, int stream, Options options)
    {
        if (options.ReferenceDirectory == null) return null;
        string stem = Path.GetFileNameWithoutExtension(input);
        string[] candidates = { Path.Combine(options.ReferenceDirectory, stem + "." + (stream + 1).ToString(CultureInfo.InvariantCulture) + ".wav"), Path.Combine(options.ReferenceDirectory, stem + "-" + (stream + 1).ToString(CultureInfo.InvariantCulture) + ".wav"), Path.Combine(options.ReferenceDirectory, stem + ".wav") };
        var existing = candidates.FirstOrDefault(File.Exists);
        if (existing != null || options.Vgmstream == null) return existing;
        Directory.CreateDirectory(options.ReferenceDirectory);
        return candidates[0];
    }

    private static bool RunVgmstream(string executable, string input, int stream, string output)
    {
        var psi = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add("-s"); psi.ArgumentList.Add(stream.ToString(CultureInfo.InvariantCulture)); psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(output); psi.ArgumentList.Add(input);
        using var process = Process.Start(psi)!; process.WaitForExit(); return process.ExitCode == 0 && File.Exists(output);
    }

    private static WavData ReadWav(string path)
    {
        byte[] b = File.ReadAllBytes(path); if (b.Length < 44 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WAVE") throw new InvalidDataException("not a RIFF/WAVE file");
        ushort format = 0, channels = 0, bits = 0; int rate = 0; int dataOffset = -1, dataLength = 0; int p = 12;
        while (p + 8 <= b.Length) { string id = Encoding.ASCII.GetString(b, p, 4); int n = BitConverter.ToInt32(b, p + 4); p += 8; if (n < 0 || n > b.Length - p) throw new InvalidDataException("WAV chunk exceeds file"); if (id == "fmt " && n >= 16) { format = BitConverter.ToUInt16(b, p); channels = BitConverter.ToUInt16(b, p + 2); rate = BitConverter.ToInt32(b, p + 4); bits = BitConverter.ToUInt16(b, p + 14); } else if (id == "data") { dataOffset = p; dataLength = n; break; } p += n + (n & 1); }
        if (dataOffset < 0 || channels == 0 || (format != 1 && format != 3) || (bits != 16 && bits != 32)) throw new InvalidDataException("unsupported WAV format");
        int stride = channels * bits / 8, frames = dataLength / stride; var samples = new float[frames]; for (int i = 0; i < frames; i++) { int q = dataOffset + i * stride; samples[i] = format == 1 && bits == 16 ? BitConverter.ToInt16(b, q) / 32768f : format == 3 && bits == 32 ? BitConverter.ToSingle(b, q) : BitConverter.ToInt32(b, q) / 2147483648f; }
        return new WavData { SampleRate = rate, Channels = channels, Samples = samples };
    }

    private static Options ParseOptions(string[] args)
    {
        var o = new Options(); for (int i = 0; i < args.Length; i++) { string a = args[i]; string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("Missing value for " + a); if (a == "--out") o.Output = Value(); else if (a == "--vgmstream") o.Vgmstream = Value(); else if (a == "--reference-dir") o.ReferenceDirectory = Path.GetFullPath(Value()); else if (a == "--strict-reference") o.StrictReference = true; else if (a.StartsWith("-")) throw new ArgumentException("Unknown option " + a); else o.Inputs.Add(a); } return o;
    }

    private static string Sha256(byte[] bytes) { using var sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant(); }
    private static string RuntimeInformation() => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + "; " + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
}
