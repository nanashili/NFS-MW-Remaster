using System;
using System.Collections.Generic;
using System.Globalization;
using NfsMwRemaster.Driving.AudioAnalysis;

namespace NfsMwRemaster.Driving.AudioAnalysis.Analysis
{
    /// <summary>Turns only observed GIN structure into an explicitly candidate mapping.</summary>
    public static class GinMappingInvestigator
    {
        public static GinMappingInvestigation Inspect(AnalysisReport report)
        {
            if (report == null) throw new ArgumentNullException("report");
            if (report.Source == null) return new GinMappingInvestigation("unknown", "", report.ParserVersion, null, null, 0, 0, 0, "Missing source evidence.", null);
            StructuralTable a = Find(report, "table_a"), b = Find(report, "table_b");
            int frames = FieldInt(report, "decoded_sample_frames"), countA = a == null ? 0 : a.Entries.Count, countB = b == null ? 0 : b.Entries.Count;
            double? e0 = FieldDouble(report, "candidate_endpoint_0"), e1 = FieldDouble(report, "candidate_endpoint_1");
            if (a == null || a.Entries.Count < 2 || !e0.HasValue || !e1.HasValue)
                return new GinMappingInvestigation("blocked", report.Source.Sha256, report.ParserVersion, e0, e1, countA, countB, frames, "Structural table or endpoint evidence is incomplete; no mapping candidate emitted.", null);

            string direction = Direction(a);
            string observation = "Observed table_a has " + countA + " entries and table_b has " + countB + "; table_a raw values are ordered " + direction + " and terminate near declared decoded frames. " +
                "GINTool v1.3.3 passes min/max RPM to its encoder and compiles cumulative grain sample counts, but this does not prove table_a's runtime RPM meaning for this fixture.";
            return new GinMappingInvestigation("candidate", report.Source.Sha256, report.ParserVersion, e0, e1, countA, countB, frames, observation + " No RPM interpolation is emitted without an explicit authoring hypothesis.", null);
        }

        /// <summary>Explicitly constructs the common endpoint interpolation hypothesis for authoring review.</summary>
        public static GinMappingInvestigation BuildUniformEndpointHypothesis(AnalysisReport report)
        {
            var inspected = Inspect(report);
            if (inspected.Endpoint0 == null || inspected.Endpoint1 == null || inspected.TableACount < 2) return inspected;
            StructuralTable a = Find(report, "table_a"); var anchors = new List<RpmAnchor>(a.Entries.Count); int frames = inspected.DeclaredFrames;
            for (int i = 0; i < a.Entries.Count; i++)
            {
                uint raw = a.Entries[i].RawValue; double frame = raw;
                double rpm = inspected.Endpoint0.Value + (inspected.Endpoint1.Value - inspected.Endpoint0.Value) * i / Math.Max(1, a.Entries.Count - 1);
                long start = raw; long next = i + 1 < a.Entries.Count ? (long)a.Entries[i + 1].RawValue : (frames > 0 ? frames : start);
                anchors.Add(new RpmAnchor(a.Entries[i].Index, i, frame, rpm, new FrameRange(Math.Min(start, next), Math.Max(start, next)), MappingEvidenceStatus.Candidate,
                    new NfsMwRemaster.Driving.AudioAnalysis.Analysis.SourceEvidence(report.Source.SourcePath, report.Source.Sha256, "table_a[" + i + "]", a.Entries[i].RawValue.ToString("X8", CultureInfo.InvariantCulture), report.ParserVersion,
                        "Explicit authoring hypothesis only: uniformly interpolated endpoints; historical RPM semantics remain unverified.")));
            }
            return new GinMappingInvestigation("hypothesis", inspected.SourceHash, inspected.ParserVersion, inspected.Endpoint0, inspected.Endpoint1, inspected.TableACount, inspected.TableBCount, inspected.DeclaredFrames,
                inspected.Observation + " Explicit hypothesis: uniformly interpolate endpoint values over table A entry order; this must not be treated as recovered metadata.", new RpmMapping(anchors));
        }

        static string Direction(StructuralTable a)
        {
            bool ascending = true, descending = true;
            for (int i = 1; i < a.Entries.Count; i++) { ascending &= a.Entries[i - 1].RawValue <= a.Entries[i].RawValue; descending &= a.Entries[i - 1].RawValue >= a.Entries[i].RawValue; }
            return ascending ? "ascending" : descending ? "descending" : "non-monotonic";
        }
        static StructuralTable Find(AnalysisReport report, string name) { for (int i = 0; i < report.Tables.Count; i++) if (report.Tables[i].Name == name) return report.Tables[i]; return null; }
        static int FieldInt(AnalysisReport report, string name) { string value = Field(report, name); int parsed; return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : 0; }
        static double? FieldDouble(AnalysisReport report, string name)
        {
            FieldEvidence field = null; for (int i = 0; i < report.Fields.Count; i++) if (report.Fields[i].Name == name) { field = report.Fields[i]; break; }
            if (field == null) return null; float value; return float.TryParse(field.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value) ? (double?)value : null;
        }
        static string Field(AnalysisReport report, string name) { for (int i = 0; i < report.Fields.Count; i++) if (report.Fields[i].Name == name) return report.Fields[i].Value; return ""; }
    }
}
