using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using NfsMwRemaster.Driving.AudioAnalysis;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    public sealed partial class BlackBoxInspectorWindow : EditorWindow
    {
        private static readonly Unity.Profiling.ProfilerMarker GuiMarker = new Unity.Profiling.ProfilerMarker(Unity.Profiling.ProfilerCategory.Scripts, "BlackBoxInspector.OnGUI");
        private static readonly string[] Tabs = { "Sources & dependencies", "Structure & bytes", "RPM & regions", "Banks & control", "Playback trace", "Native mapping & comparison" };
        [SerializeField] private BlackBoxSession session;
        [SerializeField] private VehicleAudio live;
        [SerializeField] private int tab, sourceIndex, recordingIndex, structureIndex, page, bankPage, tracePage, selectedTrace;
        [SerializeField] private string search = "", byteAddress = "0", selectedName = "", selectedNotes = "";
        [SerializeField] private long byteOffset, selectedOffset, selectedLength;
        [SerializeField] private int startFrame, endFrame;
        [SerializeField] private float monitorGain = 0.2f;
        [SerializeField] private double rpmPerHz = 60;
        [SerializeField] private BlackBoxReplaySettings replaySettings = new BlackBoxReplaySettings();
        [SerializeField] private int cursorFrame;
        [SerializeField] private bool freezeLive;
        private string automaticAnalysisKey = "";
        private BlackBoxPresenter presenter;
        private BlackBoxAudition audition;
        private Vector2 scroll;
        private Texture2D spectrum;
        private BlackBoxAnalysisResult spectrumSource;
        private BlackBoxDocument[] displayedDocuments;
        private string[] sourceLabels = Array.Empty<string>(), recordingLabels = Array.Empty<string>(), structureLabels = Array.Empty<string>();
        private BlackBoxByteRow[] rows = Array.Empty<BlackBoxByteRow>();
        private bool rowsDirty;
        private double repaintAt;
        private BlackBoxReplayResult render, referenceRender;
        private readonly List<EngineAudioTrace> liveTraces = new List<EngineAudioTrace>();
        private long liveDropped;
        private int editorDropped;
        public BlackBoxSession Session => session;
        public bool IsBusy => presenter != null && presenter.Busy;
        public int ActiveView => tab;
        public void ShowView(int index)
        {
            if (index < 0 || index >= Tabs.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (tab == index && technicalView == (index == 1)) return;
            tab = index;
            technicalView = index == 1;
            scroll = Vector2.zero; audition?.Stop(); Repaint();
        }
        private BlackBoxDocument Document => sourceIndex >= 0 && sourceIndex < presenter.Documents.Length ? presenter.Documents[sourceIndex] : null;
        private Recording Recording => Document != null && recordingIndex >= 0 && recordingIndex < Document.Report.Recordings.Count ? Document.Report.Recordings[recordingIndex] : null;

        [MenuItem("Racing Tools/Audio/Black Box Inspector")]
        public static void ShowWindow() => Open();
        public static void OpenSession(BlackBoxSession selected)
        {
            if (selected == null) throw new ArgumentNullException(nameof(selected));
            var window = GetWindow<BlackBoxInspectorWindow>("Black Box Audio"); window.SelectSession(selected); window.Show(); window.Focus();
        }
        public static void Open(VehicleProfileDraft vehicle = null, VehicleSensoryProfile profile = null, VehicleAudio instance = null)
        {
            var window = GetWindow<BlackBoxInspectorWindow>("Black Box Audio");
            if (vehicle != null && vehicle.audioAnalysis != null) window.SelectSession(vehicle.audioAnalysis);
            else if (vehicle != null && window.session != null && window.session.vehicle != vehicle || profile != null && window.session != null && window.session.profile != profile)
                window.CreateSession();
            if (window.session == null) window.CreateSession();
            if (vehicle != null) { Undo.RecordObject(window.session, "Select audio analysis vehicle"); window.session.vehicle = vehicle; window.session.profile = vehicle.audio; window.presenter.Touch(); }
            else if (profile != null && window.session.profile != profile) { Undo.RecordObject(window.session, "Select audio analysis profile"); window.session.profile = profile; window.presenter.Touch(); }
            if (instance != null) { window.live = instance; window.SelectAttachmentTarget(instance); window.ShowView(4); }
            window.Show(); window.Focus();
        }
        private void OnEnable()
        {
            minSize = new Vector2(850, 580); audition = new BlackBoxAudition();
            presenter = new BlackBoxPresenter(session);
            attachmentTarget = BlackBoxVehicleAttachment.RestoreTarget(session);
            EditorApplication.update += Tick; Undo.undoRedoPerformed += UndoChanged;
            EditorApplication.playModeStateChanged += PlayChanged;
            if (session != null && session.sources.Count > 0) presenter.Reinspect();
        }
        private void OnDisable()
        {
            EditorApplication.update -= Tick; Undo.undoRedoPerformed -= UndoChanged; EditorApplication.playModeStateChanged -= PlayChanged;
            presenter?.Dispose(); audition?.Dispose(); ClearSpectrum();
        }
        private void PlayChanged(PlayModeStateChange _) { audition?.Stop(); liveTraces.Clear(); editorDropped = 0; liveDropped = 0; traceCacheDirty = true; }
        private void UndoChanged() { presenter.Touch(); rowsDirty = true; Repaint(); }
        private void CreateSession()
        {
            var created = CreateInstance<BlackBoxSession>(); created.name = "Black Box analysis"; SelectSession(created);
        }
        private void SelectSession(BlackBoxSession selected)
        {
            if (session == selected && presenter != null && presenter.Session == selected) return;
            audition?.Stop(); presenter?.Dispose(); session = selected; presenter = new BlackBoxPresenter(selected);
            render = referenceRender = null; sourceIndex = recordingIndex = structureIndex = page = 0; ClearSpectrum();
            displayedDocuments = null; rows = Array.Empty<BlackBoxByteRow>(); automaticAnalysisKey = ""; lastPackagePath = "";
            attachmentTarget = BlackBoxVehicleAttachment.RestoreTarget(selected); attachmentCandidates = Array.Empty<VehicleAudio>();
            if (selected != null && selected.sources.Count > 0) presenter.Reinspect();
        }
        private void Tick()
        {
            if (presenter == null) return;
            bool changed = presenter.Tick();
            if (audition.Tick()) { changed = true; Repaint(); }
            if (displayedDocuments != presenter.Documents)
            {
                displayedDocuments = presenter.Documents; sourceLabels = displayedDocuments.Select(d => d.Attachment.relativePath + (d.IsStale ? " [CHANGED]" : "")).ToArray();
                SelectSource(Math.Min(sourceIndex, sourceLabels.Length - 1)); changed = true;
            }
            if (rowsDirty && technicalView && !presenter.Busy && Document != null)
            {
                rowsDirty = false; var doc = Document; int scope = structureIndex; string filter = search;
                presenter.Start(c => BlackBoxByteRow.Build(doc, scope, filter, c), result => { rows = result; page = Mathf.Clamp(page, 0, Math.Max(0, (rows.Length - 1) / 48)); presenter.SetStatus("Source structure ready."); });
            }
            var selectedRecording = Recording;
            if (!presenter.Busy && !technicalView && (tab == 0 || tab == 2) && selectedRecording != null && selectedRecording.Decoded && selectedRecording.Pcm != null)
            {
                string key = Document.Report.Source.Sha256 + ":" + selectedRecording.Id;
                if (key != automaticAnalysisKey)
                {
                    automaticAnalysisKey = key;
                    Run(() => presenter.Analyze(Document, selectedRecording, rpmPerHz, startFrame, endFrame));
                }
            }
            if (presenter.Analysis != spectrumSource) { ClearSpectrum(); spectrumSource = presenter.Analysis; BuildSpectrum(); changed = true; }
            if (live != null && live.NativeRenderer != null)
            {
                while (live.NativeRenderer.Trace.TryRead(out var trace))
                {
                    if (freezeLive) { editorDropped++; continue; }
                    if (liveTraces.Count >= 8192) { liveTraces.RemoveRange(0, 2048); editorDropped += 2048; }
                    liveTraces.Add(trace); changed = true; traceCacheDirty = true;
                }
                liveDropped = live.NativeRenderer.Trace.Dropped;
            }
            if ((changed || traceCacheDirty || presenter.Busy || audition.Active || live != null && !freezeLive) && EditorApplication.timeSinceStartup >= repaintAt)
            { repaintAt = EditorApplication.timeSinceStartup + 0.1; RefreshTraceCache(); Repaint(); }
        }
        private void SelectSource(int index)
        {
            audition?.Stop();
            sourceIndex = index; recordingIndex = structureIndex = page = bankPage = 0; selectedName = selectedNotes = ""; selectedOffset = selectedLength = byteOffset = 0;
            var doc = Document;
            recordingLabels = doc == null ? Array.Empty<string>() : doc.Report.Recordings.Select(r => r.Id + (r.Decoded ? " [PCM]" : " [unavailable]")).ToArray();
            structureLabels = doc == null ? Array.Empty<string>() : new[] { "Header / fields" }.Concat(doc.Report.Tables.Select(t => t.Name + " (" + t.Entries.Count + " entries)"))
                .Concat(new[] { "Physical recordings", "Logical references", "Unresolved spans / rules" }).ToArray();
            startFrame = 0; endFrame = Recording?.ValidFrames ?? 0; rowsDirty = true;
        }
        private void OnGUI()
        { using (GuiMarker.Auto()) DrawWindow(); }
        private void DrawWindow()
        {
            DrawWorkspaceShell();
        }
        private void SaveSession()
        {
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(session)))
            {
                string path = EditorUtility.SaveFilePanelInProject("Save analysis authoring session", "Audio-analysis", "asset", "Store under an Editor folder to keep source references out of player content.");
                if (string.IsNullOrEmpty(path)) return;
                if (!path.Contains("/Editor/")) throw new InvalidOperationException("Analysis sessions contain Editor-only evidence. Choose an Editor folder.");
                AssetDatabase.CreateAsset(session, path);
            }
            if (session.vehicle != null) { Undo.RecordObject(session.vehicle, "Attach audio evidence session"); session.vehicle.audioAnalysis = session; EditorUtility.SetDirty(session.vehicle); AssetDatabase.SaveAssetIfDirty(session.vehicle); }
            AssetDatabase.SaveAssetIfDirty(session); presenter.SetStatus("Analysis session saved. Original bytes remain external immutable evidence.");
        }
        private void Run(Action action)
        {
            try { action(); }
            catch (ExitGUIException) { throw; }
            catch (Exception error) { presenter.SetError(error.GetBaseException().Message); }
        }
        private void Text(string label, string value)
        {
            float width = position.width - leftPaneWidth - (showDetails && position.width >= 1100 ? rightPaneWidth + 5 : 0) - 55;
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(value ?? "", EditorStyles.wordWrappedLabel, GUILayout.Height(Math.Min(400, Math.Max(20, EditorStyles.wordWrappedLabel.CalcHeight(new GUIContent(value ?? ""), Math.Max(250, width))))));
        }
        private static void Page(ref int value, int count, int size)
        {
            int last = Math.Max(0, (count - 1) / size);
            using (new EditorGUILayout.HorizontalScope())
            { using (new EditorGUI.DisabledScope(value <= 0)) if (GUILayout.Button("Previous", GUILayout.Width(80))) value--; EditorGUILayout.LabelField((value + 1) + " / " + (last + 1) + " · " + count + " rows"); using (new EditorGUI.DisabledScope(value >= last)) if (GUILayout.Button("Next", GUILayout.Width(80))) value++; }
        }
        private void PickRecording()
        {
            if (recordingLabels.Length == 0) return;
            int next = EditorGUILayout.Popup("Physical recording", recordingIndex, recordingLabels);
            if (next != recordingIndex) { recordingIndex = next; startFrame = 0; endFrame = Recording?.ValidFrames ?? 0; }
        }
        private void SelectRange(string name, ByteRange range, string notes)
        { selectedName = name; selectedOffset = range.Offset; selectedLength = range.Length; byteOffset = Math.Max(0, range.Offset / 256 * 256); byteAddress = range.Offset.ToString("X"); selectedNotes = notes; }
    }

    internal sealed class BlackBoxByteRow
    {
        public string name, raw, value, notes;
        public ByteRange range;
        public static BlackBoxByteRow[] Build(BlackBoxDocument document, int scope, string search, CancellationToken cancel)
        {
            var report = document.Report; var result = new List<BlackBoxByteRow>();
            void Add(string name, ByteRange range, string raw, string value, string notes)
            {
                cancel.ThrowIfCancellationRequested();
                if (result.Count >= 100000) return;
                if ((name + " " + raw + " " + value).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add(new BlackBoxByteRow { name = name, range = range, raw = raw, value = value, notes = notes });
            }
            if (scope == 0) foreach (var f in report.Fields) Add(f.Name, f.Range, f.RawHex, f.Value, Describe(f));
            else if (scope <= report.Tables.Count)
            {
                var table = report.Tables[scope - 1];
                foreach (var e in table.Entries) Add(table.Name + "[" + e.Index + "]", e.Range, e.RawValue.ToString("X8"), e.RawValue.ToString(CultureInfo.InvariantCulture), e.Provenance + " · " + e.Semantic);
            }
            else if (scope == report.Tables.Count + 1) foreach (var r in report.Recordings) Add(r.Id, r.Payload, "encoded payload", r.ValidFrames + " frames", r.Codec + " · " + r.Notes);
            else if (scope == report.Tables.Count + 2) foreach (var r in report.References) Add(r.Kind, r.Source, "0x" + r.RawTarget.ToString("X"), r.Target, r.Provenance + " · " + r.Notes);
            else
            {
                foreach (var g in report.Gaps) Add(g.Code, g.Range ?? new ByteRange(0, 0), "unresolved", g.Message, "Next experiment: " + g.NextExperiment);
                foreach (var span in report.UnknownSpans) Add("Unparsed span", span, "preserved bytes", span.Length + " bytes", "Next: compare a matching parser revision or a second controlled fixture. Parsed extent does not prove semantic coverage.");
            }
            return result.ToArray();
        }
        public static string Describe(FieldEvidence f) => f.Provenance + " · " + (f.Verification ?? "Semantic interpretation unverified") + "\nRaw " + f.RawHex + " → " + f.Value + " · " + (f.DecodedType ?? "raw bytes / text") + " · units " + (f.Units ?? "unknown") + "\n" + (f.Transformation ?? f.Notes) + " · " + string.Join(" | ", f.EvidenceReferences);
    }
}
