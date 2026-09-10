using System;
using System.IO;
using System.Text;
using NfsMwRemaster.Driving.AudioAnalysis;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    /// <summary>Reduced-evidence, bounded RIFF/WAVE adapter. Never invents discarded GIN/bank metadata.</summary>
    public static class BlackBoxWaveReader
    {
        public static bool IsWave(byte[] b) => b != null && b.Length >= 12 && Tag(b, 0) == "RIFF" && Tag(b, 8) == "WAVE";
        public static AnalysisReport Parse(byte[] b, string path)
        {
            var r = new AnalysisReport { Source = new SourceEvidence(path, BlackBoxSourceAccess.Hash(b), b.Length), Variant = "RIFF/WAVE · reduced evidence", DetectedByteOrder = ByteOrder.Little, ParserVersion = "black-box-wave/1" };
            if (!IsWave(b)) throw new InvalidDataException("Expected RIFF/WAVE.");
            int format = 0, channels = 0, rate = 0, bits = 0, align = 0, payload = -1, length = 0, chunks = 0;
            long end = Math.Min(b.LongLength, U32(b, 4) + 8L);
            try
            {
                if (U32(b, 4) + 8L > b.Length) throw new InvalidDataException("RIFF extent exceeds available bytes.");
                for (long p = 12; p < end;)
                {
                    if (++chunks > 4096 || p + 8 > end) throw new InvalidDataException("Truncated or excessive RIFF chunks.");
                    int offset = checked((int)p); string tag = Tag(b, offset); uint size = U32(b, offset + 4); long next = p + 8L + size;
                    if (next > end) throw new InvalidDataException("Chunk extends beyond RIFF bounds: " + tag);
                    r.Fields.Add(new FieldEvidence("chunk " + chunks + " / " + tag, new ByteRange(p, 8), BitConverter.ToString(b, offset, 8), size + " payload bytes", EvidenceProvenance.Decoded, "RIFF chunk extent; little-endian uint32, odd payloads have one alignment byte."));
                    if (tag == "fmt ")
                    {
                        if (size < 16 || format != 0) throw new InvalidDataException("Missing or ambiguous WAVE format record.");
                        format = U16(b, offset + 8); channels = U16(b, offset + 10); rate = checked((int)U32(b, offset + 12)); align = U16(b, offset + 20); bits = U16(b, offset + 22);
                        if (channels < 1 || channels > 8 || rate < 1 || rate > 192000 || align != channels * (bits / 8) || U32(b, offset + 16) != (long)rate * align)
                            throw new InvalidDataException("Inconsistent WAVE rate/channel/block alignment.");
                        r.Fields.Add(new FieldEvidence("PCM format", new ByteRange(p + 8, size), BitConverter.ToString(b, offset + 8, 16), "format " + format + " / " + channels + " channels / " + rate + " Hz / " + bits + " bits", EvidenceProvenance.Decoded, "WAVE header units; no recovered RPM metadata."));
                    }
                    else if (tag == "data") { if (payload >= 0) throw new InvalidDataException("Multiple data chunks require explicit interpretation."); payload = offset + 8; length = checked((int)size); }
                    else if (tag != "fact") r.Gaps.Add(new AnalysisGap { Code = "wave-extra-chunk", Range = new ByteRange(p + 8, size), Message = "Uninterpreted WAVE chunk " + tag, NextExperiment = "Inspect this chunk before assigning loop or annotation semantics." });
                    p = next + (size & 1);
                }
                if (payload < 0 || format == 0 || align <= 0 || length % align != 0 || length / align > BinaryAnalysisParser.MaxDecodedFrames || (long)length / align * channels > 32 * 1024 * 1024)
                    throw new InvalidDataException("Invalid or excessive WAVE sample data.");
                if (!(format == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32) || format == 3 && bits == 32)) throw new InvalidDataException("Supported WAV subset is PCM8/16/24/32 or IEEE float32; compressed/extensible formats need a reviewed adapter.");
                var pcm = new float[length / (bits / 8)];
                for (int i = 0, p = payload; i < pcm.Length; i++, p += bits / 8)
                {
                    float value;
                    if (format == 3) value = BitConverter.ToSingle(b, p);
                    else if (bits == 8) value = (b[p] - 128) / 128f;
                    else if (bits == 16) value = (short)U16(b, p) / 32768f;
                    else if (bits == 24) value = ((b[p] | b[p + 1] << 8 | b[p + 2] << 16) << 8 >> 8) / 8388608f;
                    else value = unchecked((int)U32(b, p)) / 2147483648f;
                    if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Nonfinite float PCM; raw source bytes retained, audition blocked.");
                    pcm[i] = value;
                }
                r.Recordings.Add(new Recording { Id = "wave:data", Payload = new ByteRange(payload, length), SampleRate = rate, Channels = channels, ValidFrames = pcm.Length / channels, Pcm = pcm, Decoded = true, Codec = format == 3 ? "IEEE float32" : "PCM" + bits, Notes = "Reduced evidence: source WAV contains no recoverable GIN/ABK tables by this adapter." });
            }
            catch (Exception error) when (error is InvalidDataException || error is OverflowException)
            { r.IsPartial = true; r.Gaps.Add(new AnalysisGap { Code = "wave-unsupported-or-truncated", Message = error.Message, NextExperiment = "Provide valid original PCM or a documented adapter for this WAVE variant." }); }
            r.Capabilities.Add(new Capability("detection", CapabilityStatus.Supported, "RIFF/WAVE signatures"));
            r.Capabilities.Add(new Capability("structural-parsing", r.IsPartial ? CapabilityStatus.Partial : CapabilityStatus.Supported, "Bounded RIFF chunk traversal"));
            r.Capabilities.Add(new Capability("PCM-decoding", r.Recordings.Count > 0 ? CapabilityStatus.Supported : CapabilityStatus.Blocked, "Explicit PCM subset only"));
            r.Capabilities.Add(new Capability("recovered-original-metadata", CapabilityStatus.Blocked, "WAV alone cannot restore discarded original RPM tables or controllers."));
            r.Capabilities.Add(new Capability("native-conversion", CapabilityStatus.Partial, "Explicit authored approximation only"));
            r.Capabilities.Add(new Capability("reference-interactive", CapabilityStatus.Blocked, "No matched game capture context."));
            r.Gaps.Add(new AnalysisGap { Code = "wave-lost-metadata", Message = "Original bank identities, table semantics and interactive controls are unavailable from this WAV alone.", NextExperiment = "Attach the original GIN/ABK and exact configuration/build context, or keep the mapping authored." });
            return r;
        }
        private static string Tag(byte[] b, int p) => Encoding.ASCII.GetString(b, p, 4);
        private static int U16(byte[] b, int p) => b[p] | b[p + 1] << 8;
        private static uint U32(byte[] b, int p) => (uint)(b[p] | b[p + 1] << 8 | b[p + 2] << 16 | b[p + 3] << 24);
    }
}
