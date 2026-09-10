using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using NfsMwRemaster.Driving.AudioAnalysis;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    public sealed class BlackBoxDocument
    {
        public readonly BlackBoxSource Attachment;
        public readonly byte[] Bytes;
        public readonly AnalysisReport Report;
        public BlackBoxDocument(BlackBoxSource attachment, byte[] bytes, AnalysisReport report)
        { Attachment = attachment; Bytes = bytes; Report = report; }
        public bool IsStale => !string.IsNullOrEmpty(Attachment.hash) && Attachment.hash != Report.Source.Sha256;
    }

    public static class BlackBoxInspection
    {
        public static BlackBoxDocument[] Inspect(BlackBoxSource[] sources, CancellationToken cancel = default)
        {
            if (sources == null || sources.Length > BlackBoxSourceAccess.MaximumFiles) throw new InvalidDataException("Too many sources.");
            var documents = new List<BlackBoxDocument>();
            long memory = 0;
            foreach (var source in sources)
            {
                cancel.ThrowIfCancellationRequested();
                var bytes = BlackBoxSourceAccess.Read(source, cancel);
                var report = Path.GetExtension(source.relativePath).Equals(".nfsms", StringComparison.OrdinalIgnoreCase)
                    ? InspectConfiguration(bytes, source.relativePath)
                    : BlackBoxWaveReader.IsWave(bytes) ? BlackBoxWaveReader.Parse(bytes, source.relativePath)
                    : BinaryAnalysisParser.ParseBytes(bytes, source.relativePath);
                memory += bytes.Length;
                foreach (var recording in report.Recordings) memory += 4L * (recording.Pcm?.Length ?? 0);
                if (memory > 512L * 1024 * 1024) throw new InvalidDataException("Inspection exceeds the 512 MiB retained PCM/source budget. Attach a smaller set.");
                documents.Add(new BlackBoxDocument(source, bytes, report));
            }
            return documents.ToArray();
        }

        /// <summary>Read script records as text; never execute a mod script or synthesize inherited game state.</summary>
        public static AnalysisReport InspectConfiguration(byte[] bytes, string path)
        {
            if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Configuration exceeds 4 MiB.");
            var report = new AnalysisReport
            {
                Source = new SourceEvidence(path, BlackBoxSourceAccess.Hash(bytes), bytes.Length),
                Variant = "NFS modscript text / ordered records", ParserVersion = "black-box-modscript/1"
            };
            int offset = 0, line = 1;
            while (offset < bytes.Length)
            {
                int end = offset;
                while (end < bytes.Length && bytes[end] != 10) end++;
                int length = end - offset;
                string record = Encoding.UTF8.GetString(bytes, offset, length).TrimEnd('\r');
                var range = new ByteRange(offset, length);
                report.Fields.Add(new FieldEvidence("line " + line, range,
                    BitConverter.ToString(bytes, offset, Math.Min(length, 256)).Replace("-", " "), record,
                    EvidenceProvenance.Decoded, "UTF-8 text record; source order retained. No script execution. Raw bytes remain inspectable."));
                string[] words = record.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length >= 4 && words[0] == "update_field")
                {
                    string target = words[words.Length - 1];
                    string ext = Path.GetExtension(target);
                    if (ext.Equals(".gin", StringComparison.OrdinalIgnoreCase) || ext.Equals(".abk", StringComparison.OrdinalIgnoreCase))
                        report.References.Add(new LogicalReference { Kind = "configuration-names-source", Source = range, Target = target,
                            Provenance = EvidenceProvenance.Decoded, Notes = "Explicit filename token in " + words[3] + "; resolving a particular revision still requires review." });
                }
                if (words.Length > 0 && words[0] == "copy_fields")
                    report.Gaps.Add(new AnalysisGap { Code = "inherited-configuration", Range = range, Message = record,
                        NextExperiment = "Attach the exact base engineaudio record and modscript interpreter revision; field merge semantics and inherited values are unresolved." });
                offset = end + 1; line++;
                if (line > 16384) throw new InvalidDataException("Configuration exceeds the 16384-record limit.");
            }
            report.Capabilities.Add(new Capability("detection", CapabilityStatus.Supported, "Explicit .nfsms adapter; syntax is retained as text."));
            report.Capabilities.Add(new Capability("structural parsing", CapabilityStatus.Partial, "Ordered UTF-8 lines and explicit source-name tokens only."));
            report.Capabilities.Add(new Capability("dependency resolution", CapabilityStatus.Partial, "Named candidates; case and revision conflicts remain visible."));
            report.Capabilities.Add(new Capability("control evaluation", CapabilityStatus.Blocked, "Game interpreter, inherited fields and units are not available."));
            report.Gaps.Add(new AnalysisGap { Code = "configuration-semantics", Message = "Parameter names and numeric values are decoded, not verified engine control rules.",
                NextExperiment = "Supply base database records, format interpreter evidence and a timestamped game capture for this build." });
            return report;
        }
    }

    [Serializable] public sealed class BlackBoxEvidenceEntry
    {
        public string sourceHash, sourcePath, name, raw, decoded, type, units, transformation, provenance, verification, parserVersion, references;
        public long offset, length;
    }
    [Serializable] public sealed class BlackBoxEvidenceExport
    {
        public int schema = 1;
        public string sourceHash, sourcePath, variant, parserVersion;
        public BlackBoxEvidenceEntry[] fields;
        public BlackBoxRecordingEvidence[] recordings;
        public string[] capabilities, gaps, references, unknownSpans;
        public static BlackBoxEvidenceExport From(BlackBoxDocument document)
        {
            var report = document.Report;
            var fields = new List<BlackBoxEvidenceEntry>();
            foreach (var field in report.Fields)
                fields.Add(Entry(report, field.Name, field.Range, field.RawHex, field.Value, field.Provenance.ToString(), field.DecodedType, field.Units, field.Transformation, field.Verification, field.EvidenceReferences, field.Notes));
            foreach (var table in report.Tables)
                foreach (var entry in table.Entries)
                    fields.Add(Entry(report, table.Name + "[" + entry.Index + "]", entry.Range,
                        entry.RawValue.ToString("X8"), entry.RawValue.ToString(CultureInfo.InvariantCulture), entry.Provenance.ToString(), entry.DecodedType, entry.Units, entry.Transformation, entry.Verification, entry.EvidenceReferences, entry.Semantic));
            return new BlackBoxEvidenceExport
            {
                sourceHash = report.Source.Sha256, sourcePath = report.Source.SourcePath, variant = report.Variant, parserVersion = report.ParserVersion,
                recordings = report.Recordings.ConvertAll(r => new BlackBoxRecordingEvidence { id = r.Id, codec = r.Codec, notes = r.Notes,
                    offset = r.Payload.Offset, length = r.Payload.Length, sampleRate = r.SampleRate, channels = r.Channels, validFrames = r.ValidFrames, decoded = r.Decoded }).ToArray(),
                fields = fields.ToArray(), unknownSpans = report.UnknownSpans.ConvertAll(r => r.ToString() + " | Unparsed bytes; next: compare a matching parser revision or supply a second controlled fixture.").ToArray(), capabilities = report.Capabilities.ConvertAll(c => c.Name + ": " + c.Status + " — " + c.Evidence).ToArray(),
                gaps = report.Gaps.ConvertAll(g => g.Code + ": " + g.Message + " | Next: " + g.NextExperiment).ToArray(),
                references = report.References.ConvertAll(r => r.Kind + " @ " + r.Source + " -> " + r.Target + " | " + r.Provenance + " | " + r.Notes).ToArray()
            };
        }
        private static BlackBoxEvidenceEntry Entry(AnalysisReport report, string name, ByteRange range, string raw, string decoded, string provenance, string type, string units, string transformation, string verification, List<string> references, string notes)
        {
            return new BlackBoxEvidenceEntry { sourceHash = report.Source.Sha256, sourcePath = report.Source.SourcePath,
                name = name, offset = range.Offset, length = range.Length, raw = raw, decoded = decoded,
                type = type ?? "Raw bytes / text; semantic type unresolved", units = units ?? "Unknown", transformation = transformation ?? notes,
                provenance = provenance, verification = verification ?? "Decoded; semantic interpretation unverified", parserVersion = report.ParserVersion,
                references = string.Join(" | ", references ?? new List<string>()) + " | " + notes };
        }
    }

    [Serializable] public sealed class BlackBoxRecordingEvidence
    {
        public string id, codec, notes;
        public long offset, length;
        public int sampleRate, channels, validFrames;
        public bool decoded;
    }
}
