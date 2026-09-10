using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NfsMwRemaster.Driving.AudioAnalysis
{
    public enum EvidenceProvenance { Decoded, VerifiedInterpretation, Derived, Inferred, AuthoredOverride, UnknownConflicting }
    public enum CapabilityStatus { Unsupported, Partial, Supported, Blocked }
    public enum ByteOrder { Little, Big }

    public readonly struct ByteRange
    {
        public readonly long Offset; public readonly long Length;
        public ByteRange(long offset, long length) { Offset = offset; Length = length; }
        public long End { get { return Offset + Length; } }
        public override string ToString() { return "0x" + Offset.ToString("X", CultureInfo.InvariantCulture) + "..0x" + End.ToString("X", CultureInfo.InvariantCulture); }
    }

    public sealed class SourceEvidence
    {
        public string SourcePath; public string Sha256; public long ByteLength;
        public SourceEvidence(string path, string hash, long length) { SourcePath = path ?? string.Empty; Sha256 = hash; ByteLength = length; }
    }

    public sealed class FieldEvidence
    {
        public string Name; public ByteRange Range; public string RawHex; public string Value;
        public EvidenceProvenance Provenance; public string Notes;
        public string DecodedType; public string Units; public string Transformation; public string Verification;
        public List<string> EvidenceReferences = new List<string>();
        public FieldEvidence(string name, ByteRange range, string raw, string value, EvidenceProvenance provenance, string notes = null)
        { Name = name; Range = range; RawHex = raw; Value = value; Provenance = provenance; Notes = notes ?? string.Empty; }
    }

    public sealed class TableEntry
    {
        public int Index; public ByteRange Range; public uint RawValue; public float? FloatValue;
        public string Semantic = "Unknown"; public EvidenceProvenance Provenance = EvidenceProvenance.Decoded;
        public string DecodedType; public string Units; public string Transformation; public string Verification;
        public List<string> EvidenceReferences = new List<string>();
    }

    public sealed class StructuralTable
    {
        public string Name; public ByteRange Range; public uint DeclaredCount; public List<TableEntry> Entries = new List<TableEntry>();
    }

    public sealed class Recording
    {
        public string Id; public ByteRange Payload; public int SampleRate; public int Channels; public int ValidFrames;
        public float[] Pcm; public bool Decoded; public string Codec; public string Notes;
        public double DecodeElapsedMilliseconds;
        public int LoopStart, LoopEnd;
    }

    public sealed class LogicalReference
    {
        public string Kind; public ByteRange Source; public string Target; public uint RawTarget; public bool RangeVerified;
        public EvidenceProvenance Provenance; public string Notes;
    }

    public sealed class AnalysisGap
    {
        public string Code; public string Message; public ByteRange? Range; public string NextExperiment;
    }

    public sealed class Capability
    {
        public string Name; public CapabilityStatus Status; public string Evidence; public Capability(string n, CapabilityStatus s, string e) { Name = n; Status = s; Evidence = e; }
    }

    public sealed class AnalysisReport
    {
        public SourceEvidence Source; public string Variant = "Unknown"; public ByteOrder? DetectedByteOrder;
        public List<FieldEvidence> Fields = new List<FieldEvidence>(); public List<StructuralTable> Tables = new List<StructuralTable>();
        public List<Recording> Recordings = new List<Recording>(); public List<LogicalReference> References = new List<LogicalReference>();
        public List<AnalysisGap> Gaps = new List<AnalysisGap>(); public List<Capability> Capabilities = new List<Capability>();
        public List<ByteRange> UnknownSpans = new List<ByteRange>();
        public bool IsPartial; public string ParserVersion = "black-box-binary-m1-m3/2";
    }

    public sealed class BinaryAnalysisParser
    {
        public const int MaxTableEntries = 1 << 20;
        public const int MaxModules = 0x400;
        public const int MaxSampleTables = 0x400;
        public const int MaxDecodedFrames = 32 * 1024 * 1024;
        public const int MaxTraversalReferences = 1 << 20;

        public AnalysisReport Parse(byte[] data, string sourcePath)
        {
            if (data == null) data = Array.Empty<byte>();
            var report = new AnalysisReport { Source = new SourceEvidence(sourcePath, Hash(data), data.LongLength) };
            if (data.Length < 4) { Gap(report, "short-input", "At least four bytes are required for a format signature.", 0, data.Length, "Attach the original binary."); return report; }
            string magic = Encoding.ASCII.GetString(data, 0, 4);
            if (magic == "Gnsu" || magic == "Octn") ParseGin(data, report, magic);
            else if (magic == "ABKC") ParseAbkc(data, report);
            else if (magic == "BNKl") { report.Variant = "BNKl / direct EA SCHl bank"; report.DetectedByteOrder = ByteOrder.Little; report.Capabilities.Add(new Capability("bnk-structure", CapabilityStatus.Partial, "Direct BNKl signature and little-endian bank layout are detected.")); ParseEmbeddedBnk(data, report, 0); }
            else if (magic == "BNKb") { report.Variant = "BNKb / direct EA SCHl bank"; report.DetectedByteOrder = ByteOrder.Big; report.Capabilities.Add(new Capability("bnk-structure", CapabilityStatus.Unsupported, "BNKb signature is detected, but only the observed little-endian BNKl v5 subset is implemented.")); Gap(report, "bnkb-unsupported", "BNKb is detected as a direct bank, but its big-endian entry layout is outside the implemented subset.", 0, data.Length, "Provide a BNKb fixture and matching vgmstream structure for a bounded decoder."); AddCaps(report, CapabilityStatus.Unsupported); }
            else if (magic == "S10A") { report.Variant = "S10A / direct EAAC bank"; report.DetectedByteOrder = ByteOrder.Big; report.Capabilities.Add(new Capability("s10a-structure", CapabilityStatus.Partial, "Direct S10A signature and big-endian bank layout are detected.")); ParseEmbeddedS10A(data, report, 0); }
            else { report.Variant = "Unknown"; Gap(report, "unknown-signature", "No supported M1/M3 signature was detected: " + magic, 0, 4, "Provide a Gnsu/Octn GIN or ABKC bank."); AddCaps(report, CapabilityStatus.Unsupported); }
            FinalizeUnknownSpans(report);
            return report;
        }

        public static AnalysisReport ParseBytes(byte[] data, string sourcePath) { return new BinaryAnalysisParser().Parse(data, sourcePath); }

        private static void ParseGin(byte[] b, AnalysisReport r, string magic)
        {
            r.Variant = magic + " GIN / EA-XAS v0"; r.DetectedByteOrder = ByteOrder.Little;
            if (b.Length < 0x20) { Gap(r, "gin-header-truncated", "GIN requires a 0x20-byte header before table data.", 0, b.Length, "Attach the complete original GIN."); AddCaps(r, CapabilityStatus.Partial); return; }
            var rd = new Reader(b, ByteOrder.Little);
            AddField(r, rd, "signature", 0, 4, magic);
            AddField(r, rd, "header[0x04]", 4, 4, Hex(rd.Bytes(4, 4)), Hex(rd.U32(4)), "raw uint32; semantics unverified");
            AddField(r, rd, "candidate_endpoint_0", 8, 4, Hex(rd.Bytes(8, 4)), FloatText(rd.F32(8)), "candidate RPM endpoint; semantics unverified");
            AddField(r, rd, "candidate_endpoint_1", 12, 4, Hex(rd.Bytes(12, 4)), FloatText(rd.F32(12)), "candidate RPM endpoint; semantics unverified");
            uint countA = rd.U32(16), countB = rd.U32(20), frames = rd.U32(24), rate = rd.U32(28);
            AddField(r, rd, "table_a_count", 16, 4, countA.ToString(CultureInfo.InvariantCulture)); AddField(r, rd, "table_b_count", 20, 4, countB.ToString(CultureInfo.InvariantCulture));
            AddField(r, rd, "decoded_sample_frames", 24, 4, frames.ToString(CultureInfo.InvariantCulture)); AddField(r, rd, "sample_rate", 28, 4, rate.ToString(CultureInfo.InvariantCulture));
            long start;
            try { start = checked(0x20L + checked((long)countA + 1) * 4L + checked((long)countB + 1) * 4L); }
            catch { Gap(r, "offset-overflow", "GIN table offset arithmetic overflowed.", 16, 8, "Inspect count fields against a trusted revision."); return; }
            if (countA > MaxTableEntries || countB > MaxTableEntries) { Gap(r, "count-limit", "GIN table count exceeds parser limit.", 16, 8, "Use a bounded fixture or raise the reviewed parser limit."); return; }
            AddTable(b, r, rd, "table_a", 0x20, countA); AddTable(b, r, rd, "table_b", 0x20 + ((long)countA + 1) * 4, countB);
            if (frames > MaxDecodedFrames) { Gap(r, "frame-limit", "Declared sample frames exceed decode limit.", 24, 4, "Review the source before increasing the limit."); return; }
            if (rate == 0 || rate > 1000000) Gap(r, "invalid-rate", "Sample rate is zero or implausibly large; structure retained.", 28, 4, "Verify platform/version context.");
            long payload = start; long encoded = ((frames + 31L) / 32L) * 0x13L;
            if (payload < 0 || payload > b.Length) { Gap(r, "payload-out-of-range", "GIN encoded payload starts outside the source bounds.", payload, 0, "Attach a complete original file."); AddCaps(r, CapabilityStatus.Partial); return; }
            long availableBytes = b.Length - payload; long availableFrames = availableBytes / 0x13L; int validFrames = (int)Math.Min((long)frames, availableFrames * 32L); long physicalPayloadBytes = Math.Min(encoded, availableFrames * 0x13L);
            if (encoded > availableBytes) Gap(r, "payload-truncated", "Declared GIN frames extend beyond physical encoded bytes; only complete EA-XAS blocks are decoded.", payload + availableBytes, encoded - availableBytes, "Attach the complete original file or compare the missing final block with a reference decoder.");
            var rec = new Recording { Id = "gin:payload", Payload = new ByteRange(payload, physicalPayloadBytes), SampleRate = (int)rate, Channels = 1, ValidFrames = validFrames, Codec = "EA-XAS v0", Notes = "One physical mono recording; table meanings are unverified." };
            var decodeTimer = Stopwatch.StartNew(); rec.Pcm = DecodeXasV0(b, (int)payload, validFrames, r); decodeTimer.Stop(); rec.DecodeElapsedMilliseconds = decodeTimer.Elapsed.TotalMilliseconds; rec.Decoded = rec.Pcm != null; r.Recordings.Add(rec);
            AddCaps(r, rec.Decoded ? CapabilityStatus.Supported : CapabilityStatus.Partial);
            r.Capabilities.Add(new Capability("rpm-mapping", CapabilityStatus.Blocked, "GIN table semantic domain and direction are not established by the vgmstream structural parser."));
            r.Capabilities.Add(new Capability("control-evaluation", CapabilityStatus.Unsupported, "No GIN control bytecode semantics implemented."));
        }

        private static void AddTable(byte[] b, AnalysisReport r, Reader rd, string name, long offset, uint count)
        {
            var table = new StructuralTable { Name = name, Range = new ByteRange(offset, Math.Min((long)b.Length - Math.Min(offset, b.Length), (count + 1L) * 4L)), DeclaredCount = count };
            for (long i = 0; i <= count; i++) { long p = offset + i * 4; if (!rd.Has(p, 4)) { Gap(r, "table-truncated", name + " includes an entry outside the source.", p, 4, "Supply the complete source."); break; } uint raw = rd.U32(p); table.Entries.Add(new TableEntry { Index = (int)i, Range = new ByteRange(p, 4), RawValue = raw, FloatValue = BitConverter.ToSingle(BitConverter.GetBytes(raw), 0), DecodedType = "uint32 + optional IEEE754 view", Verification = "raw bytes decoded; semantic meaning unresolved" }); }
            r.Tables.Add(table);
        }

        private static float[] DecodeXasV0(byte[] b, int payload, int frames, AnalysisReport r)
        {
            if (frames == 0) return Array.Empty<float>(); var pcm = new float[frames]; int written = 0;
            for (int frame = 0; frame < frames && written < frames; frame += 32)
            {
                int p = payload + (frame / 32) * 0x13; if (p < 0 || p + 0x13 > b.Length) { Gap(r, "decode-truncated", "EA-XAS frame is truncated.", p, 0x13, "Attach all encoded frames."); return null; }
                uint h = (uint)(b[p] | b[p + 1] << 8 | b[p + 2] << 16 | b[p + 3] << 24); int shift = (int)((h >> 16) & 0x0f); int hist2 = (short)(h & 0xfff0), hist1 = (short)((h >> 16) & 0xfff0); int coef = (int)(h & 0x0f);
                if (coef >= 4) { Gap(r, "decode-coefficient-unsupported", "EA-XAS predictor coefficient index is outside the four verified XA pairs.", p, 4, "Compare this block with a matching native/reference decoder."); return null; }
                float c1 = new[] { 0f, .9375f, 1.796875f, 1.53125f }[coef], c2 = new[] { 0f, 0f, -.8125f, -.859375f }[coef];
                int[] head = { hist2, hist1 }; for (int i = 0; i < 2 && written < frames; i++) pcm[written++] = head[i] / 32768f;
                for (int i = 0; i < 30 && written < frames; i++) { int n = ((i & 1) == 0) ? ((b[p + 4 + i / 2] >> 4) & 15) : (b[p + 4 + i / 2] & 15); int sample = (short)(n << 12) >> shift; sample = Clamp((int)(sample + hist1 * c1 + hist2 * c2)); pcm[written++] = sample / 32768f; hist2 = hist1; hist1 = sample; }
            }
            return pcm;
        }

        private static void ParseAbkc(byte[] b, AnalysisReport r)
        {
            r.Variant = "ABKC / EA SCHl module bank (structural subset)"; AddField(r, new Reader(b, ByteOrder.Little), "signature", 0, 4, "ABKC");
            if (b.Length < 0x24) { Gap(r, "abkc-short", "ABKC header is shorter than module/table pointers.", 0, b.Length, "Attach complete ABK."); AddCaps(r, CapabilityStatus.Partial); return; }
            var le = new Reader(b, ByteOrder.Little); var be = new Reader(b, ByteOrder.Big); uint leTable = le.U32(0x1c), beTable = be.U32(0x1c); bool leOk = InRange(leTable, b.Length, 4), beOk = InRange(beTable, b.Length, 4); bool leStructural = leOk && (le.U16(0x0a) == 0 || InRange(leTable, b.Length, 0x30)); bool beStructural = beOk && (be.U16(0x0a) == 0 || InRange(beTable, b.Length, 0x30)); Reader rd = leStructural || !beStructural ? le : be; r.DetectedByteOrder = rd.Order;
            AddField(r, rd, "module_count", 0x0a, 2, rd.U16(0x0a).ToString(CultureInfo.InvariantCulture)); AddField(r, rd, "modules_table", 0x1c, 4, Hex(rd.U32(0x1c))); AddField(r, rd, "bank_offset", 0x20, 4, Hex(rd.U32(0x20)));
            uint modules = rd.U16(0x0a), modulesTable = rd.U32(0x1c), bank = rd.U32(0x20); if (modules > MaxModules || !InRange(modulesTable, b.Length, 4)) { Gap(r, "module-table-invalid", "ABKC module count or table pointer is outside limits.", 0x0a, 0x16, "Verify endianness and ABKC revision."); AddCaps(r, CapabilityStatus.Partial); return; }
            int refs = 0; var seen = new HashSet<uint>(); long table = modulesTable;
            for (uint i = 0; i < modules; i++) { if (!InRange(table, b.Length, 0x30)) { Gap(r, "module-truncated", "Module entry is outside source bounds.", table, 0x30, "Attach complete ABK."); break; } int players = b[(int)table + 0x24]; uint moduleData = rd.U32(table + 0x2c); if (players == 0xff || players > 0x80 || !InRange(moduleData, b.Length, 4)) { Gap(r, "module-invalid", "Module player count or data pointer is invalid.", table, 0x30, "Inspect the ABKC variant."); break; }
                for (int j = 0; j < players && refs++ < MaxTraversalReferences; j++) { long po = table + 0x3c + j * 4; if (!InRange(po, b.Length, 4)) { Gap(r, "player-pointer-truncated", "Player pointer falls outside module entry.", po, 4, "Attach complete ABK."); break; } uint player = rd.U32(po); long samplesPtr = (long)moduleData + player + 4; if (!InRange(samplesPtr, b.Length, 4)) { Gap(r, "sample-table-invalid", "Player sample-table pointer is out of range.", samplesPtr, 4, "Verify table offsets for this variant."); continue; } uint samples = rd.U32(samplesPtr); bool firstTable = seen.Add(samples); r.References.Add(new LogicalReference { Kind = "player-references-sample-table", Source = new ByteRange(po, 4), Target = "sample-table@0x" + samples.ToString("X"), RawTarget = samples, RangeVerified = InRange(samples, b.Length, 4), Provenance = EvidenceProvenance.Decoded, Notes = firstTable ? "First table occurrence." : "Shared table reference; retained." }); if (!InRange(samples, b.Length, 4) || !firstTable) continue; uint count = rd.U32(samples); if (count > MaxTableEntries || !InRange(samples, b.Length, 4 + (long)count * 12)) { Gap(r, "sample-table-invalid", "Sample table count exceeds bounds.", samples, 4, "Inspect table layout for this revision."); continue; } var st = new StructuralTable { Name = "sample-table@0x" + samples.ToString("X"), Range = new ByteRange(samples, 4 + (long)count * 12), DeclaredCount = count }; for (uint k = 0; k < count; k++) { long ep = samples + 4 + k * 12; uint target = rd.U32(ep + 4); st.Entries.Add(new TableEntry { Index = (int)k, Range = new ByteRange(ep, 12), RawValue = target, Semantic = "sound entry target (variant semantics unresolved)" }); r.References.Add(new LogicalReference { Kind = "sample-entry", Source = new ByteRange(ep, 12), Target = "raw-target@0x" + target.ToString("X"), RawTarget = target, RangeVerified = true, Provenance = EvidenceProvenance.Decoded, Notes = "Entry type/priority/stream routing preserved as raw structure." }); } r.Tables.Add(st); }
                int controllers = b[(int)table + 0x27]; table += 0x3c + (long)(players + controllers) * 4;
            }
            if (refs >= MaxTraversalReferences) Gap(r, "reference-limit", "ABKC traversal limit reached; report is partial.", null, 0, "Review with a narrower fixture.");
            if (InRange(bank, b.Length, 4)) { string sig = Encoding.ASCII.GetString(b, (int)bank, 4); r.References.Add(new LogicalReference { Kind = "bank-container", Source = new ByteRange(0x20, 4), Target = sig, RawTarget = bank, RangeVerified = true, Provenance = EvidenceProvenance.Decoded, Notes = sig == "S10A" ? "EAAC S10A container detected; observed RAM EA-XAS v1 entries may be decoded." : "BNK signature detected; sample semantics remain variant-specific." }); if (sig == "S10A") { r.Capabilities.Add(new Capability("s10a-structure", CapabilityStatus.Partial, "S10A signature and bank offset are verified.")); ParseEmbeddedS10A(b, r, bank); } else if (sig == "BNKl" || sig == "BNKb") r.Capabilities.Add(new Capability("bnk-structure", CapabilityStatus.Partial, "BNKl/BNKb signature is verified; payload traversal remains variant-specific.")); }
            if (InRange(bank, b.Length, 8) && Encoding.ASCII.GetString(b, (int)bank, 4) == "BNKl") ParseEmbeddedBnk(b, r, bank);
            AddCaps(r, CapabilityStatus.Partial); r.Capabilities.Add(new Capability("rpm-mapping", CapabilityStatus.Blocked, "ABKC module/sample references contain no verified RPM meaning in this parser.")); r.Capabilities.Add(new Capability("control-evaluation", CapabilityStatus.Unsupported, "Unknown controllers and external SCHl/AST semantics are not treated as no-ops.")); Gap(r, "codec-unimplemented", "Unsupported bank codecs and external control semantics remain outside this bounded decoder.", null, 0, "Attach a matching reference decoder for the unsupported bank variant.");
        }

        private sealed class BnkSoundHeader { public int Index; public long Header; public long Data; public int Samples; public int Rate; public int Version; public int Codec; public int Channels; public int LoopStart, LoopEnd; }

        private static void ParseEmbeddedS10A(byte[] b, AnalysisReport r, uint bank)
        {
            var rd = new Reader(b, ByteOrder.Big); if (!InRange(bank, b.Length, 0x0c)) { Gap(r, "s10a-header-truncated", "S10A header is shorter than the entry count and offset table pointer.", bank, Math.Max(0, b.Length - bank), "Attach the complete S10A payload."); return; } uint count = rd.U32(bank + 8); if (count == 0 || count > 0x400 || !InRange(bank + 0x0c, b.Length, count * 4L)) { Gap(r, "s10a-table-invalid", "S10A entry table is outside bounded source limits.", bank + 8, 8, "Inspect the S10A revision with a matching header parser."); return; }
            for (uint i = 0; i < count; i++) { uint relative = rd.U32(bank + 0x0c + i * 4L); long header = (long)bank + relative; if (!InRange(header, b.Length, 8)) { Gap(r, "s10a-header-invalid", "S10A entry header points outside the source.", header, 8, "Attach the complete S10A payload."); continue; } uint h1 = rd.U32(header), h2 = rd.U32(header + 4); int version = (int)(h1 >> 28), codec = (int)((h1 >> 24) & 0x0f), channels = (int)((h1 >> 18) & 0x3f) + 1, rate = (int)(h1 & 0x3ffff), type = (int)(h2 >> 30), samples = (int)(h2 & 0x1fffffff); bool loopFlag = (h2 & 0x20000000) != 0; if (loopFlag && (!InRange(header, b.Length, 12) || rd.U32(header + 8) >= samples)) { Gap(r, "s10a-loop-invalid", "S10A sustain start is missing or outside its recording.", header, Math.Min(12, b.Length - header), "Attach a valid complete S10A recording."); continue; } var e = new FieldEvidence("s10a[" + i.ToString(CultureInfo.InvariantCulture) + "].header", new ByteRange(header, loopFlag ? 12 : 8), Hex(b, (int)header, loopFlag ? 12 : 8), "version=" + version.ToString(CultureInfo.InvariantCulture) + ", codec=" + codec.ToString(CultureInfo.InvariantCulture) + ", channels=" + channels.ToString(CultureInfo.InvariantCulture) + ", rate=" + rate.ToString(CultureInfo.InvariantCulture) + ", samples=" + samples.ToString(CultureInfo.InvariantCulture), EvidenceProvenance.Decoded, "EAAC SNR header; semantics pinned to vgmstream ea_eaac_abk.c"); e.DecodedType = "EAAC SNR header"; e.Units = "rate Hz; samples frames"; e.Transformation = "big-endian packed fields"; e.Verification = "raw header decoded; decode supported only for observed RAM mono EA-XAS v1"; e.EvidenceReferences.Add("vgmstream:ea_eaac_abk.c"); r.Fields.Add(e); if (version != 0 || codec != 4 || channels != 1 || type != 0 || rate <= 0 || samples <= 0 || samples > MaxDecodedFrames) { Gap(r, "s10a-entry-unsupported", "S10A entry is outside the observed RAM mono EA-XAS v1 subset.", header, 8, "Provide a matching SNR/SNS decoder for this entry."); continue; } int previousRecordings = r.Recordings.Count; DecodeS10AEaXasV1(b, r, (int)i, header + (loopFlag ? 12 : 8), samples, rate); if (loopFlag && r.Recordings.Count > previousRecordings) { var decoded = r.Recordings[r.Recordings.Count - 1]; decoded.LoopStart = checked((int)rd.U32(header + 8)); decoded.LoopEnd = samples; } }
            r.Capabilities.Add(new Capability("s10a-ea-xas-v1", r.Recordings.Any(x => x.Id.StartsWith("s10a:")) ? CapabilityStatus.Supported : CapabilityStatus.Blocked, "Observed S10A RAM mono EA-XAS v1 entries use bounded 0x4c-byte/128-sample blocks."));
        }

        private static void DecodeS10AEaXasV1(byte[] b, AnalysisReport r, int index, long data, int samples, int rate)
        {
            var decodeTimer = Stopwatch.StartNew(); var pcm = new float[samples]; int written = 0; long block = data; long payloadEnd = data; float[][] coefs = { new[] { 0f, 0f }, new[] { .9375f, 0f }, new[] { 1.796875f, -.8125f }, new[] { 1.53125f, -.859375f } };
            while (written < samples && InRange(block, b.Length, 8)) { uint blockWord = new Reader(b, ByteOrder.Big).U32(block); int blockSize = (int)(blockWord & 0x00ffffff); uint blockSamples = new Reader(b, ByteOrder.Big).U32(block + 4); if (blockSize < 8 || !InRange(block, b.Length, blockSize) || blockSamples == 0) break; int frames = Math.Min((blockSize - 8) / 0x4c, (samples - written + 127) / 128); for (int frameIndex = 0; frameIndex < frames && written < samples; frameIndex++) { long p = block + 8 + frameIndex * 0x4cL; byte[] frame = new byte[0x4c]; Buffer.BlockCopy(b, (int)p, frame, 0, frame.Length); for (int group = 0; group < 4 && written < samples; group++) { uint h = (uint)(frame[group * 4] | frame[group * 4 + 1] << 8 | frame[group * 4 + 2] << 16 | frame[group * 4 + 3] << 24); int coef = (int)(h & 15); if (coef >= 4) coef = 0; int shift = (int)((h >> 16) & 15), hist2 = (short)(h & 0xfff0), hist1 = (short)((h >> 16) & 0xfff0); if (written < samples) pcm[written++] = hist2 / 32768f; if (written < samples) pcm[written++] = hist1 / 32768f; for (int row = 0; row < 15 && written < samples; row++) for (int n = 0; n < 2 && written < samples; n++) { int nib = n == 0 ? (frame[16 + row * 4 + group] >> 4) & 15 : frame[16 + row * 4 + group] & 15; int sample = (short)(nib << 12) >> shift; sample = Clamp((int)(sample + hist1 * coefs[coef][0] + hist2 * coefs[coef][1])); pcm[written++] = sample / 32768f; hist2 = hist1; hist1 = sample; } } } block += blockSize; payloadEnd = block; }
            if (written < samples) Gap(r, "s10a-payload-truncated", "S10A EA-XAS v1 blocks ended before the declared sample count.", payloadEnd, Math.Max(0, b.Length - payloadEnd), "Attach the complete S10A RAM payload.");
            decodeTimer.Stop(); r.Recordings.Add(new Recording { Id = "s10a:" + index.ToString(CultureInfo.InvariantCulture), Payload = new ByteRange(data, Math.Max(0, payloadEnd - data)), SampleRate = rate, Channels = 1, ValidFrames = written, Pcm = written == pcm.Length ? pcm : pcm.Take(written).ToArray(), Decoded = written == samples, Codec = "EA-XAS v1", Notes = "Observed S10A RAM mono entry; bounded EA SNS blocks and 0x4c-byte EA-XAS v1 frames decoded with pinned vgmstream equations.", DecodeElapsedMilliseconds = decodeTimer.Elapsed.TotalMilliseconds });
        }

        // Observed BNKl v5 PC sound entries use PT00 headers, version 2, and EA-XA v2.
        // This is intentionally a narrow decoder: unknown patch tags or codecs stop that entry.
        private static void ParseEmbeddedBnk(byte[] b, AnalysisReport r, uint bank)
        {
            if (!InRange(bank, b.Length, 8)) { Gap(r, "bnk-header-truncated", "BNK header is shorter than the version and entry count fields.", bank, Math.Max(0, b.Length - bank), "Attach the complete BNK payload."); return; } var rd = new Reader(b, ByteOrder.Little); long baseOffset = bank; int version = b[baseOffset + 4]; int count = rd.U16(baseOffset + 6); if (version != 5 || count > 0x400) { r.Capabilities.Add(new Capability("bnk-pcm-decoding", CapabilityStatus.Blocked, "Only observed BNKl version 5 is enabled; this bank has version " + version.ToString(CultureInfo.InvariantCulture) + ".")); return; }
            long table = baseOffset + 0x14; if (!InRange(table, b.Length, count * 4L)) { Gap(r, "bnk-table-truncated", "BNKl sound offset table exceeds the source.", table, count * 4L, "Attach the complete BNK payload."); return; }
            var sounds = new List<BnkSoundHeader>();
            for (int i = 1; i < count; i++) { uint relative = rd.U32(table + i * 4L); if (relative == 0) continue; long header = table + i * 4L + relative; if (!InRange(header, b.Length, 4)) { Gap(r, "bnk-header-invalid", "BNKl sound header points outside the source.", header, 4, "Verify BNKl offset origin."); continue; } var parsed = ParseBnkVariableHeader(b, r, i, header, baseOffset); if (parsed != null) sounds.Add(parsed); }
            for (int i = 0; i < sounds.Count; i++) { var s = sounds[i]; long next = b.Length; for (int j = 0; j < sounds.Count; j++) if (sounds[j].Data > s.Data && sounds[j].Data < next) next = sounds[j].Data; DecodeBnkEaXaV2(b, r, s, next); }
            r.Capabilities.Add(new Capability("bnk-pcm-decoding", sounds.Count > 0 && r.Recordings.Any(x => x.Id.StartsWith("bnk:")) ? CapabilityStatus.Supported : CapabilityStatus.Partial, "Observed BNKl v5 PT00 entries with EA-XA v2 PCM/ADPCM block decoding."));
        }

        private static BnkSoundHeader ParseBnkVariableHeader(byte[] b, AnalysisReport r, int index, long header, long bank)
        {
            if (!InRange(header, b.Length, 4) || b[header] != (byte)'P' || b[header + 1] != (byte)'T' || b[header + 2] != 0 || b[header + 3] != 0) { Gap(r, "bnk-header-variant", "BNKl entry does not use the observed PT00 variable header.", header, 4, "Inspect this entry with a matching EA header revision."); return null; }
            int version = 2, codec = 0x0a, channels = 1, samples = 0, rate = 0, loopStart = 0, loopEnd = 0; long data = 0; long p = header + 4; bool ended = false;
            while (!ended && p < b.Length && p - header < 0x1000) { int tag = b[p++]; if (tag == 0xff) { ended = true; break; } if (tag == 0xfc || tag == 0xfd) continue; if (p >= b.Length) break; int n = b[p++]; if (n == 0xff) { if (!InRange(p, b.Length, 4)) break; uint skip = new Reader(b, ByteOrder.Big).U32(p); p += 4 + skip; continue; } if (n > 4 || !InRange(p, b.Length, n)) { Gap(r, "bnk-patch-invalid", "BNKl variable-header patch exceeds bounds.", p - 2, n, "Inspect the entry with a matching header parser."); return null; } uint value = 0; for (int j = 0; j < n; j++) value = (value << 8) | b[p++]; string field = null;
                switch (tag) { case 0x80: version = (int)value; field = "version"; break; case 0x82: channels = (int)value; field = "channels"; break; case 0x84: rate = (int)value; field = "sample_rate"; break; case 0x85: if (value > MaxDecodedFrames) { Gap(r, "bnk-frame-limit", "BNKl declared sample frames exceed the bounded decoder limit.", p - n - 2, n + 2, "Review the source before increasing the parser limit."); return null; } samples = (int)value; field = "sample_frames"; break; case 0x86: case 0x87: if (value >= MaxDecodedFrames) { Gap(r, "bnk-loop-invalid", "BNKl sustain point exceeds the bounded recording limit.", p - n - 2, n + 2, "Attach a valid complete BNKl recording."); return null; } if (tag == 0x86) { loopStart = (int)value; field = "loop_start"; } else { loopEnd = (int)value + 1; field = "loop_end_inclusive"; } break; case 0x88: data = bank + value; field = "channel_0_offset"; break; case 0xa0: codec = (int)value; field = "codec2"; break; }
                if (field != null) { var evidence = new FieldEvidence("bnk[" + index.ToString(CultureInfo.InvariantCulture) + "]." + field, new ByteRange(p - n - 2, n + 2), Hex(b, (int)(p - n - 2), n + 2), value.ToString(CultureInfo.InvariantCulture), EvidenceProvenance.Decoded, "EA variable-header patch; tag semantics pinned to vgmstream ea_schl.c."); evidence.DecodedType = field == "sample_rate" || field == "sample_frames" || field == "channels" ? "uint" : "uint32"; evidence.Units = field == "sample_rate" ? "Hz" : field == "sample_frames" ? "frames" : null; evidence.Transformation = field == "channel_0_offset" ? "bank base + relative offset" : "big-endian patch integer"; evidence.Verification = field == "sample_rate" || field == "sample_frames" || field == "channels" ? "verified against vgmstream metadata/reference decode" : "raw decoding; interpretation pinned only for observed BNKl v5"; evidence.EvidenceReferences.Add("vgmstream:ea_schl.c"); r.Fields.Add(evidence); }
            }
            if (rate == 0) rate = 22050; // EA platform PC default in the pinned parser.
            if (!ended || channels != 1 || samples <= 0 || data <= 0 || !InRange(data, b.Length, 1)) { Gap(r, "bnk-header-incomplete", "BNKl entry lacks a bounded mono EA-XA recording header.", header, Math.Max(0, p - header), "Compare this entry against vgmstream's EA variable-header parser."); return null; }
            return new BnkSoundHeader { Index = index, Header = header, Data = data, Samples = samples, Rate = rate, Version = version, Codec = codec, Channels = channels, LoopStart = loopStart, LoopEnd = loopEnd };
        }

        private static void DecodeBnkEaXaV2(byte[] b, AnalysisReport r, BnkSoundHeader s, long limit)
        {
            if (s.Codec != 0x0a) { Gap(r, "bnk-codec-unsupported", "BNKl entry codec2 is not EA-XA v2: " + s.Codec.ToString(CultureInfo.InvariantCulture), s.Header, 1, "Provide a decoder/reference for this codec."); return; }
            var decodeTimer = Stopwatch.StartNew();
            var pcm = new float[s.Samples]; long p = s.Data; int done = 0, h1 = 0, h2 = 0; int[] k0 = { 0, 240, 460, 392 }, k1 = { 0, 0, -208, -220 };
            while (done < s.Samples && p < limit)
            {
                // The final frame can store only the declared remaining samples.
                // Match ea_xa_decoder.c's bounded reads instead of requiring 28 samples of padding.
                int frames = Math.Min(28, s.Samples - done); byte info = b[p];
                if (info == 0xee)
                {
                    int bytes = 5 + frames * 2; if (p + bytes > limit) break;
                    h1 = (short)(b[p + 1] << 8 | b[p + 2]); h2 = (short)(b[p + 3] << 8 | b[p + 4]);
                    for (int i = 0; i < frames; i++) { int v = (short)(b[p + 5 + i * 2] << 8 | b[p + 6 + i * 2]); pcm[done++] = v / 32768f; }
                    p += bytes;
                }
                else
                {
                    int coef = info >> 4, shift = (info & 15) + 8, bytes = 1 + (frames + 1) / 2;
                    if (coef >= 4 || p + bytes > limit) break;
                    for (int i = 0; i < frames; i++) { int nib = ((i & 1) == 0) ? (b[p + 1 + i / 2] >> 4) & 15 : b[p + 1 + i / 2] & 15; int value = (nib << 28) >> shift; value = Clamp((value + k0[coef] * h1 + k1[coef] * h2) >> 8); pcm[done++] = value / 32768f; h2 = h1; h1 = value; }
                    p += bytes;
                }
            }
            if (done < s.Samples) Gap(r, "bnk-payload-truncated", "EA-XA recording ended before its declared sample count.", p, Math.Max(0, limit - p), "Attach the complete BNK payload or compare with a native reference decoder.");
            decodeTimer.Stop(); var rec = new Recording { Id = "bnk:" + s.Index.ToString(CultureInfo.InvariantCulture), Payload = new ByteRange(s.Data, Math.Max(0, p - s.Data)), SampleRate = s.Rate, Channels = 1, ValidFrames = done, Pcm = done == pcm.Length ? pcm : pcm.Take(done).ToArray(), Decoded = done > 0, Codec = "EA-XA v2", Notes = "Observed BNKl v5 mono PT00 entry; PCM (0xEE) and EA-XA ADPCM blocks decoded with pinned vgmstream equations.", DecodeElapsedMilliseconds = decodeTimer.Elapsed.TotalMilliseconds, LoopStart = s.LoopStart, LoopEnd = s.LoopEnd }; r.Recordings.Add(rec);
        }

        private static void AddCaps(AnalysisReport r, CapabilityStatus decode) { if (!r.Capabilities.Any(c => c.Name == "detection")) r.Capabilities.Add(new Capability("detection", CapabilityStatus.Supported, "Signature read from source bytes.")); if (!r.Capabilities.Any(c => c.Name == "structural-parsing")) r.Capabilities.Add(new Capability("structural-parsing", r.Gaps.Count == 0 ? CapabilityStatus.Supported : CapabilityStatus.Partial, "Bounded offsets/counts and raw fields retained.")); if (!r.Capabilities.Any(c => c.Name == "pcm-decoding")) r.Capabilities.Add(new Capability("pcm-decoding", decode, "Verified codec subset only; no automatic normalization/downmixing.")); if (!r.Capabilities.Any(c => c.Name == "dependency-resolution")) r.Capabilities.Add(new Capability("dependency-resolution", CapabilityStatus.Blocked, "Separate ABK/G​​IN/SCHl/AST dependencies are not resolved by this parser.")); if (!r.Capabilities.Any(c => c.Name == "native-conversion")) r.Capabilities.Add(new Capability("native-conversion", CapabilityStatus.Unsupported, "No native profile conversion is implemented.")); if (!r.Capabilities.Any(c => c.Name == "reference-PCM")) r.Capabilities.Add(new Capability("reference-PCM", CapabilityStatus.Partial, "Reference decoder comparison is recorded externally for approved fixtures.")); if (!r.Capabilities.Any(c => c.Name == "reference-interactive")) r.Capabilities.Add(new Capability("reference-interactive", CapabilityStatus.Unsupported, "No interactive/native playback session is exposed.")); }
        private static void FinalizeUnknownSpans(AnalysisReport r) { var known = new List<ByteRange>(); foreach (var f in r.Fields) known.Add(f.Range); foreach (var t in r.Tables) known.Add(t.Range); foreach (var x in r.Recordings) known.Add(x.Payload); foreach (var x in r.References) known.Add(x.Source); known = known.Where(x => x.Length > 0).OrderBy(x => x.Offset).ToList(); long cursor = 0; foreach (var x in known) { if (x.Offset > cursor) r.UnknownSpans.Add(new ByteRange(cursor, x.Offset - cursor)); cursor = Math.Max(cursor, Math.Min(r.Source.ByteLength, x.End)); } if (cursor < r.Source.ByteLength) r.UnknownSpans.Add(new ByteRange(cursor, r.Source.ByteLength - cursor)); }
        private static void AddField(AnalysisReport r, Reader rd, string name, long p, int n, string value, string notes = null) { if (!rd.Has(p, n)) { Gap(r, "field-truncated", name + " is outside source bounds.", p, n, "Attach complete source."); return; } var e = new FieldEvidence(name, new ByteRange(p, n), Hex(rd.Bytes(p, n)), value, EvidenceProvenance.Decoded, notes); AnnotateField(e, name, rd); r.Fields.Add(e); }
        private static void AddField(AnalysisReport r, Reader rd, string name, long p, int n, string raw, string value, string notes) { if (rd.Has(p, n)) { var e = new FieldEvidence(name, new ByteRange(p, n), raw, value, EvidenceProvenance.Decoded, notes); AnnotateField(e, name, rd); r.Fields.Add(e); } }
        private static void AnnotateField(FieldEvidence e, string name, Reader rd) { e.DecodedType = name == "signature" ? "ASCII signature" : name.Contains("endpoint") ? "uint32 + IEEE754 candidate" : name.Contains("count") ? "uint32 count" : name == "sample_rate" ? "uint32" : "raw integer"; e.Units = name.Contains("rate") ? "Hz" : name.Contains("frames") ? "frames" : name.Contains("count") ? "entries" : null; e.Transformation = rd.Order == ByteOrder.Little ? "little-endian field read" : "big-endian field read"; e.Verification = name.Contains("endpoint") ? "raw field; endpoint semantics unresolved" : "raw bytes decoded from source"; }
        private static void Gap(AnalysisReport r, string code, string message, long? offset, long length, string next) { r.IsPartial = true; r.Gaps.Add(new AnalysisGap { Code = code, Message = message, Range = offset.HasValue ? new ByteRange(offset.Value, Math.Max(0, length)) : (ByteRange?)null, NextExperiment = next }); }
        private static bool InRange(long p, long length, long bytes) { return p >= 0 && p <= length && bytes >= 0 && bytes <= length - p; }
        private static int Clamp(int n) { return n < -32768 ? -32768 : n > 32767 ? 32767 : n; }
        private static string Hash(byte[] b) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(b)).Replace("-", string.Empty).ToLowerInvariant(); }
        private static string Hex(uint n) { return "0x" + n.ToString("X8", CultureInfo.InvariantCulture); }
        private static string Hex(byte[] b) { var sb = new StringBuilder(b.Length * 2); for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("X2", CultureInfo.InvariantCulture)); return sb.ToString(); }
        private static string Hex(byte[] b, int offset, int length) { var sb = new StringBuilder(length * 2); for (int i = 0; i < length && offset + i < b.Length; i++) sb.Append(b[offset + i].ToString("X2", CultureInfo.InvariantCulture)); return sb.ToString(); }
        private static string FloatText(float f) { return float.IsNaN(f) || float.IsInfinity(f) ? "non-finite" : f.ToString("R", CultureInfo.InvariantCulture); }

        private sealed class Reader
        {
            private readonly byte[] b; public readonly ByteOrder Order; public Reader(byte[] bytes, ByteOrder order) { b = bytes; Order = order; }
            public bool Has(long p, long n) { return p >= 0 && n >= 0 && p <= b.Length && n <= b.Length - p; }
            public byte[] Bytes(long p, int n) { var x = new byte[n]; if (Has(p, n)) Buffer.BlockCopy(b, (int)p, x, 0, n); return x; }
            public ushort U16(long p) { return Order == ByteOrder.Little ? (ushort)(b[p] | b[p + 1] << 8) : (ushort)(b[p] << 8 | b[p + 1]); }
            public uint U32(long p) { return Order == ByteOrder.Little ? (uint)(b[p] | b[p + 1] << 8 | b[p + 2] << 16 | b[p + 3] << 24) : (uint)(b[p] << 24 | b[p + 1] << 16 | b[p + 2] << 8 | b[p + 3]); }
            public float F32(long p) { return BitConverter.ToSingle(BitConverter.GetBytes(U32(p)), 0); }
        }
    }
}
