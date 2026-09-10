using System;
using System.IO;
using System.Linq;
using NfsMwRemaster.Driving.AudioAnalysis.Analysis;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    public sealed partial class BlackBoxInspectorWindow
    {
        [SerializeField] private float draftRpm0 = 1000, draftRpm1 = 6000;
        [SerializeField] private int draftMethod, selectedMapping;
        [SerializeField] private bool analysisOpen, mappingEvidenceOpen;
        private BlackBoxTableView mappingTable;

        private void DrawRpm()
        {
            GUILayout.Label("Build your engine mapping", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("Choose a recording, set its RPM range, then review the draft.", EditorStyles.wordWrappedMiniLabel);
            PickRecording(); var recording = Recording; var document = Document;
            if (recording == null || !recording.Decoded || recording.Pcm == null)
            {
                EditorGUILayout.HelpBox("Select decoded audio in Recordings to add a mapping.", MessageType.Info);
                if (GUILayout.Button("Choose recording →")) ShowView(0);
                if (session.regions.Count > 0) DrawRegions();
                return;
            }
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("New mapping · " + recording.Id, EditorStyles.boldLabel);
                bool hasTable = document.Report.Tables.Any(t => t.Name == "table_a" && t.Entries.Count >= 2);
                draftMethod = hasTable ? EditorGUILayout.Popup("Starting point", draftMethod, new[] { "My RPM endpoints", "GIN table hypothesis" }) : 0;
                if (draftMethod == 0)
                {
                    draftRpm0 = EditorGUILayout.FloatField("Start RPM", draftRpm0);
                    draftRpm1 = EditorGUILayout.FloatField("End RPM", draftRpm1);
                    startFrame = EditorGUILayout.IntField("First frame", startFrame);
                    endFrame = EditorGUILayout.IntField("End frame (exclusive)", endFrame);
                }
                else EditorGUILayout.HelpBox("Use source table frame positions with candidate RPM endpoints. This creates a draft to review; it does not establish the original game's playback rules.", MessageType.None);
                DrawAuditionProgress(recording.Pcm, recording.SampleRate, startFrame, endFrame);
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawAuditionButton(recording.Pcm, recording.SampleRate, recording.Channels, startFrame, endFrame, recording.Id, "selection", GUILayout.Height(26));
                    using (new EditorGUI.DisabledScope(session.regions.Count >= 16 || document.IsStale))
                        if (GUILayout.Button("Create mapping draft", GUILayout.Height(26))) Run(AddRegion);
                }
            }
            GUILayout.Space(8); DrawRegions();
            GUILayout.Space(12);
            analysisOpen = EditorGUILayout.Foldout(analysisOpen, "Waveform, spectrum and RPM evidence", true);
            if (analysisOpen) DrawAnalysisTools();
        }

        private void DrawAnalysisTools()
        {
            var recording = Recording; var document = Document;
            rpmPerHz = EditorGUILayout.DoubleField("Model · RPM / Hz", rpmPerHz);
            EditorGUILayout.LabelField("60 RPM/Hz is a 1× crankshaft hypothesis. Harmonics remain alternatives.", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUI.DisabledScope(presenter.Busy))
                if (GUILayout.Button("Analyze selected frames")) Run(() => presenter.Analyze(document, recording, rpmPerHz, startFrame, endFrame));
            var analysis = presenter.Analysis;
            if (analysis == null || analysis.sourceHash != document.Report.Source.Sha256 || analysis.recordingId != recording.Id) return;
            DrawWaveform(analysis, recording.ValidFrames);
            EditorGUILayout.LabelField("Click to set first frame; Shift-click to set end frame.", EditorStyles.miniLabel);
            DrawTablePlots(analysis);
            if (spectrum != null) GUI.DrawTexture(GUILayoutUtility.GetRect(100, 170, GUILayout.ExpandWidth(true)), spectrum, ScaleMode.StretchToFill);
            if (analysis.mapping != null) Text("Table interpretation", analysis.mapping.Observation);
            if (analysis.candidates != null) foreach (var candidate in analysis.candidates)
                EditorGUILayout.LabelField(candidate.FrequencyHz.ToString("F2") + " Hz → " + candidate.Rpm.ToString("F1") + " RPM", candidate.Assumption);
            using (new EditorGUI.DisabledScope(presenter.Busy))
                if (GUILayout.Button("Export selected frames + source manifest")) Run(() => ExportWave(startFrame, endFrame));
        }
        private void DrawWaveform(BlackBoxAnalysisResult analysis, int frames)
        {
            Rect area = GUILayoutUtility.GetRect(100, 150, GUILayout.ExpandWidth(true)); EditorGUI.DrawRect(area, new Color(0.13f, 0.13f, 0.13f));
            if (Event.current.type == EventType.Repaint)
            {
                Handles.BeginGUI(); Handles.color = new Color(0.65f, 0.8f, 0.9f);
                float scale = Math.Max(1, analysis.summary.Peak), center = area.center.y;
                for (int i = 0; i < analysis.minimum.Length; i++)
                { float x = area.x + i * area.width / Math.Max(1, analysis.minimum.Length - 1); Handles.DrawLine(new Vector3(x, center - analysis.minimum[i] / scale * area.height * 0.45f), new Vector3(x, center - analysis.maximum[i] / scale * area.height * 0.45f)); }
                Handles.EndGUI();
            }
            float start = Mathf.Clamp01(startFrame / (float)frames), end = Mathf.Clamp01(endFrame / (float)frames);
            EditorGUI.DrawRect(new Rect(area.x + start * area.width, area.y, Math.Max(1, (end - start) * area.width), area.height), new Color(0.3f, 0.6f, 0.8f, 0.18f));
            GUI.Label(new Rect(area.x + 5, area.y + 4, area.width - 10, 22), "Selection [" + startFrame + ", " + endFrame + ") frames · click start / Shift-click end", EditorStyles.whiteMiniLabel);
            if (Event.current.type == EventType.MouseDown && area.Contains(Event.current.mousePosition))
            { int frame = Mathf.Clamp(Mathf.RoundToInt((Event.current.mousePosition.x - area.x) / area.width * frames), 0, frames); if (Event.current.shift) endFrame = frame; else startFrame = frame; Event.current.Use(); Repaint(); }
        }
        private void BuildSpectrum()
        {
            var data = spectrumSource?.spectrogram; if (data == null || data.ColumnCount < 1) return;
            int height = Math.Min(256, data.BinCount); var pixels = new Color32[data.ColumnCount * height];
            float peak = 0; foreach (float value in data.Magnitudes) peak = Math.Max(peak, value);
            for (int x = 0; x < data.ColumnCount; x++) for (int y = 0; y < height; y++)
            {
                int bin = y * (data.BinCount - 1) / Math.Max(1, height - 1); float value = data.Magnitudes[x * data.BinCount + bin];
                float intensity = Mathf.Clamp01((20 * Mathf.Log10(Math.Max(1e-10f, value / Math.Max(1e-10f, peak))) + 80) / 80);
                pixels[y * data.ColumnCount + x] = Color.Lerp(new Color(0.04f, 0.06f, 0.09f), new Color(0.9f, 0.85f, 0.55f), intensity);
            }
            spectrum = new Texture2D(data.ColumnCount, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            spectrum.SetPixels32(pixels); spectrum.Apply(false, true);
        }
        private void ClearSpectrum() { if (spectrum != null) DestroyImmediate(spectrum); spectrum = null; spectrumSource = null; }
        private void AddRegion()
        {
            if (session.regions.Count >= 16) throw new InvalidOperationException("A profile supports up to sixteen mappings.");
            var region = draftMethod == 1 ? BlackBoxMappingDrafts.FromGinTable(Document, Recording) :
                BlackBoxMappingDrafts.FromEndpoints(Document, Recording, startFrame, endFrame, draftRpm0, draftRpm1);
            Undo.RecordObject(session, "Add engine mapping draft");
            session.regions.Add(region); selectedMapping = session.regions.Count - 1; presenter.Touch();
            presenter.SetStatus("Draft added. Check its RPM/load range, then mark it reviewed below.");
        }

        private void DrawRegions()
        {
            GUILayout.Label("Your mappings · " + session.regions.Count(r => r.reviewed) + "/" + session.regions.Count + " reviewed", EditorStyles.boldLabel);
            if (session.regions.Count == 0) return;
            if (mappingTable == null)
            {
                var header = new UnityEditor.IMGUI.Controls.MultiColumnHeaderState(new[]
                {
                    new UnityEditor.IMGUI.Controls.MultiColumnHeaderState.Column { headerContent = new GUIContent("Mapping"), width = 170, minWidth = 100, autoResize = true, canSort = true },
                    new UnityEditor.IMGUI.Controls.MultiColumnHeaderState.Column { headerContent = new GUIContent("RPM"), width = 130, minWidth = 100, canSort = true },
                    new UnityEditor.IMGUI.Controls.MultiColumnHeaderState.Column { headerContent = new GUIContent("State"), width = 110, minWidth = 85, canSort = true }
                });
                mappingTable = new BlackBoxTableView(new UnityEditor.IMGUI.Controls.TreeViewState<int>(), new UnityEditor.IMGUI.Controls.MultiColumnHeader(header), row => selectedMapping = row.index);
            }
            selectedMapping = Mathf.Clamp(selectedMapping, 0, session.regions.Count - 1);
            mappingTable.SetRows(session.regions.Select((r, i) => new BlackBoxTableRow { id = i + 1, index = i,
                values = new[] { r.label, r.startRpm.ToString("F0") + " → " + r.endRpm.ToString("F0"), r.reviewed ? "Reviewed" : "Needs review" } }));
            mappingTable.SetSelection(new[] { selectedMapping + 1 });
            mappingTable.OnGUI(GUILayoutUtility.GetRect(200, Mathf.Clamp(session.regions.Count * 20 + 30, 70, 190), GUILayout.ExpandWidth(true)));
            var selected = session.regions[selectedMapping];
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.BeginChangeCheck();
                string label = EditorGUILayout.TextField("Name", selected.label);
                bool hasAnchors = selected.rpmAnchors != null && selected.rpmAnchors.Length > 0;
                int start = selected.startFrame, end = selected.endFrame;
                float rpm0 = selected.startRpm, rpm1 = selected.endRpm;
                if (hasAnchors)
                {
                    EditorGUILayout.LabelField(selected.rpmAnchors.Length + " table anchors · " + rpm0.ToString("F0") + " → " + rpm1.ToString("F0") + " RPM", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField("The exported profile preserves these nonlinear source positions.", EditorStyles.wordWrappedMiniLabel);
                }
                else
                {
                    rpm0 = EditorGUILayout.FloatField("Start RPM", rpm0); rpm1 = EditorGUILayout.FloatField("End RPM", rpm1);
                    start = EditorGUILayout.IntField("First source frame", start); end = EditorGUILayout.IntField("End frame (exclusive)", end);
                }
                float load0 = EditorGUILayout.Slider("Minimum load", selected.minimumLoad, 0, 1);
                float load1 = EditorGUILayout.Slider("Maximum load", selected.maximumLoad, 0, 1);
                float gain = EditorGUILayout.Slider("Mapping volume", selected.gain, 0, 1);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(session, "Edit engine mapping");
                    selected.label = label; selected.startRpm = rpm0; selected.endRpm = rpm1; selected.startFrame = start; selected.endFrame = end;
                    selected.minimumLoad = load0; selected.maximumLoad = load1; selected.gain = gain; selected.reviewed = false; presenter.Touch();
                }
                EditorGUILayout.LabelField(selected.assumptions, EditorStyles.wordWrappedMiniLabel);
                EditorGUI.BeginChangeCheck(); bool reviewed = EditorGUILayout.ToggleLeft("I reviewed this authored RPM/load mapping", selected.reviewed);
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(session, "Review engine mapping"); selected.reviewed = reviewed; presenter.Touch(); }
                mappingEvidenceOpen = EditorGUILayout.Foldout(mappingEvidenceOpen, "Edit assumptions and source details", true);
                if (mappingEvidenceOpen)
                {
                    EditorGUI.BeginChangeCheck(); string assumptions = EditorGUILayout.TextField("Assumptions", selected.assumptions);
                    string evidence = EditorGUILayout.TextField("Evidence", selected.evidence);
                    if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(session, "Edit mapping evidence"); selected.assumptions = assumptions; selected.evidence = evidence; selected.reviewed = false; presenter.Touch(); }
                    if (hasAnchors && GUILayout.Button("Replace table with editable endpoints"))
                    { Undo.RecordObject(session, "Replace table hypothesis"); selected.rpmAnchors = Array.Empty<EngineRpmAnchor>(); selected.lookupPolicy = "Linear within authored endpoints"; selected.assumptions = "User replaced table hypothesis with linear RPM endpoints; original controller behavior unverified."; selected.reviewed = false; presenter.Touch(); }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("▶ Hear source")) Run(() =>
                    { var pair = BlackBoxNativeMapping.Find(presenter.Documents, selected.sourceHash, selected.recordingId); var rec = pair.Item2; audition.Play(rec.Pcm, rec.SampleRate, rec.Channels, selected.startFrame, selected.endFrame, monitorGain, selected.label); });
                    if (GUILayout.Button("Remove mapping", GUILayout.Width(125)))
                    { Undo.RecordObject(session, "Remove engine mapping"); session.regions.RemoveAt(selectedMapping); presenter.Touch(); GUIUtility.ExitGUI(); }
                }
            }
            if (session.regions.All(r => r.reviewed))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Preview engine →", GUILayout.Height(28)))
                    { replaySettings.rpm = (selected.startRpm + selected.endRpm) / 2; replaySettings.endRpm = selected.endRpm; replaySettings.load = (selected.minimumLoad + selected.maximumLoad) / 2; ShowView(4); }
                    if (GUILayout.Button("Apply to vehicle →", GUILayout.Height(28))) ShowView(5);
                }
            }
        }
        private void ExportWave(int start, int end)
        {
            var doc = Document; var rec = Recording;
            string path = EditorUtility.SaveFilePanel("Export preserved PCM and provenance manifest", "", "recording", "wav"); if (path.Length == 0) return;
            if (File.Exists(path) || File.Exists(path + ".json")) throw new IOException("Choose a new filename; existing evidence exports are preserved.");
            presenter.Start(c => { BlackBoxWaveExport.ExportNew(path, doc, rec, start, end, c); return path; },
                output => presenter.SetStatus("Exported preserved PCM plus source interval manifest: " + output));
        }
    }
}
