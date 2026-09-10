using System;
using System.Linq;
using System.Threading;
using NfsMwRemaster.Driving.AudioAnalysis;
using NfsMwRemaster.Driving.AudioAnalysis.Analysis;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    public sealed class BlackBoxTablePlot
    {
        public string name;
        public double low, high;
        public float[] minimum, maximum;
        public static BlackBoxTablePlot Build(StructuralTable table, CancellationToken cancellation)
        {
            int bins = Math.Min(1024, table.Entries.Count);
            var plot = new BlackBoxTablePlot { name = table.Name, low = uint.MaxValue, high = 0, minimum = new float[bins], maximum = new float[bins] };
            foreach (var entry in table.Entries) { cancellation.ThrowIfCancellationRequested(); plot.low = Math.Min(plot.low, entry.RawValue); plot.high = Math.Max(plot.high, entry.RawValue); }
            double extent = Math.Max(1, plot.high - plot.low);
            for (int b = 0; b < bins; b++)
            {
                double low = uint.MaxValue, high = 0;
                int start = (int)((long)b * table.Entries.Count / bins), end = (int)((long)(b + 1) * table.Entries.Count / bins);
                for (int i = start; i < end; i++) { cancellation.ThrowIfCancellationRequested(); low = Math.Min(low, table.Entries[i].RawValue); high = Math.Max(high, table.Entries[i].RawValue); }
                plot.minimum[b] = (float)((low - plot.low) / extent); plot.maximum[b] = (float)((high - plot.low) / extent);
            }
            return plot;
        }
    }

    public sealed partial class BlackBoxInspectorWindow
    {
        [SerializeField] private int plotTable, plotEntry;
        private GinMappingInvestigation hypothesis;
        [SerializeField] private double hypothesisRpm = 3500;
        private string hypothesisResult = "";
        private void DrawTablePlots(BlackBoxAnalysisResult analysis)
        {
            if (analysis.tables == null || analysis.tables.Length == 0) return;
            plotTable = EditorGUILayout.Popup("Source table · raw order", Math.Min(plotTable, analysis.tables.Length - 1), analysis.tables.Select(p => p.name).ToArray());
            var plot = analysis.tables[plotTable]; var table = Document.Report.Tables[plotTable];
            if (table.Entries.Count == 0) return;
            Rect area = GUILayoutUtility.GetRect(100, 125, GUILayout.ExpandWidth(true)); EditorGUI.DrawRect(area, new Color(0.13f, 0.13f, 0.13f));
            if (Event.current.type == EventType.Repaint)
            {
                Handles.BeginGUI(); Handles.color = new Color(0.8f, 0.8f, 0.7f); Vector3 prior = default;
                for (int i = 0; i < plot.minimum.Length; i++)
                {
                    float x = area.x + i * area.width / Math.Max(1, plot.minimum.Length - 1);
                    var bottom = new Vector3(x, area.yMax - plot.minimum[i] * area.height); var top = new Vector3(x, area.yMax - plot.maximum[i] * area.height);
                    Handles.DrawLine(bottom, top); if (i > 0) Handles.DrawLine(prior, bottom); prior = top;
                }
                Handles.EndGUI();
            }
            if (Event.current.type == EventType.MouseDown && area.Contains(Event.current.mousePosition))
            { plotEntry = Mathf.Clamp((int)((Event.current.mousePosition.x - area.x) / area.width * table.Entries.Count), 0, table.Entries.Count - 1); Event.current.Use(); }
            plotEntry = EditorGUILayout.IntSlider("Selected original entry", Math.Min(plotEntry, table.Entries.Count - 1), 0, table.Entries.Count - 1);
            var entry = table.Entries[plotEntry];
            Text("Raw table coordinates", "Entry index → / uint32 raw value ↑ " + plot.low + "…" + plot.high + ". Units unverified; source order and extra entries retained.\n" + table.Name + "[" + entry.Index + "] = " + entry.RawValue + " / 0x" + entry.RawValue.ToString("X8") + " @ " + entry.Range);
            if (GUILayout.Button("Locate selected table entry in structure / bytes")) { structureIndex = plotTable + 1; rowsDirty = true; SelectRange(table.Name + "[" + entry.Index + "]", entry.Range, entry.Semantic); ShowView(1); }
            using (new EditorGUI.DisabledScope(presenter.Busy))
                if (GUILayout.Button("Investigate uniform endpoint hypothesis · explicitly unverified")) Queue(() =>
                { var report = Document.Report; presenter.Start(c => { c.ThrowIfCancellationRequested(); return GinMappingInvestigator.BuildUniformEndpointHypothesis(report); }, value => hypothesis = value); });
            if (hypothesis != null && hypothesis.SourceHash == analysis.sourceHash)
            {
                Text("Hypothesis experiment", hypothesis.Observation);
                if (hypothesis.Candidate != null)
                {
                    hypothesisRpm = EditorGUILayout.DoubleField("Hypothetical requested RPM", hypothesisRpm);
                    using (new EditorGUI.DisabledScope(presenter.Busy)) if (GUILayout.Button("Evaluate candidate inverse · retain alternatives")) Queue(() =>
                    {
                        var candidate = hypothesis.Candidate; double requested = hypothesisRpm;
                        presenter.Start(c => { c.ThrowIfCancellationRequested(); var alternatives = candidate.RpmToFrames(requested); return alternatives.Count + " alternatives for " + requested + " RPM\n" + string.Join("\n", alternatives.Take(32).Select(a => "Source frames " + a.OutputStart.ToString("G12") + "…" + a.OutputEnd.ToString("G12") + " from table entries " + a.FirstTableIndex + " / " + a.SecondTableIndex)); }, value => hypothesisResult = value);
                    });
                    Text("Hypothetical inverse · not recovered metadata", hypothesisResult);
                }
            }
            DrawAuthoredRpmPlot();
        }
        private void DrawAuthoredRpmPlot()
        {
            var matching = session.regions.Where(r => r.sourceHash == Document.Report.Source.Sha256 && r.recordingId == Recording.Id).ToArray();
            if (matching.Length == 0) return;
            var curves = matching.Select(r => r.rpmAnchors != null && r.rpmAnchors.Length > 0 ? r.rpmAnchors :
                new[] { new EngineRpmAnchor(r.startRpm, r.startFrame), new EngineRpmAnchor(r.endRpm, r.endFrame - 1) }).ToArray();
            float maximum = curves.SelectMany(anchors => anchors).Max(a => a.rpm); if (maximum <= 0 || float.IsNaN(maximum) || float.IsInfinity(maximum)) return;
            Rect area = GUILayoutUtility.GetRect(100, 105, GUILayout.ExpandWidth(true)); EditorGUI.DrawRect(area, new Color(0.13f, 0.13f, 0.13f));
            if (Event.current.type == EventType.Repaint)
            {
                Handles.BeginGUI(); Handles.color = Color.white;
                foreach (var anchors in curves)
                    for (int i = 1; i < anchors.Length; i++)
                        Handles.DrawLine(new Vector3(area.x + area.width * anchors[i - 1].sourceFrame / Recording.ValidFrames, area.yMax - area.height * anchors[i - 1].rpm / maximum),
                            new Vector3(area.x + area.width * anchors[i].sourceFrame / Recording.ValidFrames, area.yMax - area.height * anchors[i].rpm / maximum));
                Handles.EndGUI();
            }
            EditorGUILayout.LabelField("Authored overlay plot · source frame → / requested RPM ↑ 0…" + maximum + " · independent of raw table coordinates and vehicle redline", EditorStyles.wordWrappedMiniLabel);
        }
    }
}
