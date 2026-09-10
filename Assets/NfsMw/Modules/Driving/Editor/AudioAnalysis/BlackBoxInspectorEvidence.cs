using System;
using System.IO;
using System.Linq;
using NfsMwRemaster.Driving.AudioAnalysis;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    public sealed partial class BlackBoxInspectorWindow
    {
        [SerializeField] private bool sourceEvidenceOpen, bankReferencesOpen;
        private BlackBoxTableView recordingTable;
        private void DrawSources()
        {
            var doc = Document;
            if (doc == null) { EditorGUILayout.HelpBox("Add a source file or folder on the left to begin.", MessageType.Info); return; }
            GUILayout.Label(Path.GetFileName(doc.Attachment.relativePath), EditorStyles.largeLabel);
            int decoded = doc.Report.Recordings.Count(r => r.Decoded && r.Pcm != null);
            EditorGUILayout.LabelField(decoded + " decoded recordings · " + session.regions.Count(r => r.sourceHash == doc.Report.Source.Sha256) + " mappings in this session", EditorStyles.miniLabel);
            if (doc.IsStale)
            {
                EditorGUILayout.HelpBox("This source changed. Review the new revision before exporting mappings.", MessageType.Warning);
                if (GUILayout.Button("Accept this source revision; require region review")) Run(() => presenter.ReviewRevision(doc));
            }
            DrawRecordingList();
            var rec = Recording;
            if (rec != null && rec.Decoded && rec.Pcm != null)
            {
                GUILayout.Space(8);
                GUILayout.Label("Selected: " + rec.Id, EditorStyles.boldLabel);
                EditorGUILayout.LabelField((rec.ValidFrames / (double)rec.SampleRate).ToString("F2") + " seconds · " + rec.SampleRate + " Hz · " + rec.Channels + " channels", EditorStyles.miniLabel);
                var analysis = presenter.Analysis;
                if (analysis != null && analysis.sourceHash == doc.Report.Source.Sha256 && analysis.recordingId == rec.Id) DrawWaveform(analysis, rec.ValidFrames);
                DrawAuditionProgress(rec.Pcm, rec.SampleRate, 0, rec.ValidFrames);
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawAuditionButton(rec.Pcm, rec.SampleRate, rec.Channels, 0, rec.ValidFrames, rec.Id, "recording", GUILayout.Height(28));
                    if (GUILayout.Button("Map this recording →", GUILayout.Height(28))) ShowView(2);
                }
                EditorGUILayout.LabelField("Decoded audio can be mapped and exported. RPM and load behavior are your authored choices.", EditorStyles.wordWrappedMiniLabel);
            }
            else if (rec != null) EditorGUILayout.HelpBox(rec.Notes + " Select a decoded recording to map, or inspect this recording's source evidence below.", MessageType.Info);
            else EditorGUILayout.HelpBox("This file contains no decoded recordings. Select an audio source on the left; companion records remain available under Bank references.", MessageType.Info);
            GUILayout.Space(12);
            sourceEvidenceOpen = EditorGUILayout.Foldout(sourceEvidenceOpen, "Source evidence and decode details", true);
            if (sourceEvidenceOpen) DrawSourceEvidence();
        }

        private void DrawRecordingList()
        {
            if (recordingTable == null)
            {
                var columns = new UnityEditor.IMGUI.Controls.MultiColumnHeaderState(new[]
                {
                    new UnityEditor.IMGUI.Controls.MultiColumnHeaderState.Column { headerContent = new GUIContent("Recording"), width = 185, minWidth = 110, autoResize = true, canSort = true },
                    new UnityEditor.IMGUI.Controls.MultiColumnHeaderState.Column { headerContent = new GUIContent("Length"), width = 90, minWidth = 65, canSort = true },
                    new UnityEditor.IMGUI.Controls.MultiColumnHeaderState.Column { headerContent = new GUIContent("State"), width = 140, minWidth = 90, canSort = true }
                });
                recordingTable = new BlackBoxTableView(new UnityEditor.IMGUI.Controls.TreeViewState<int>(), new UnityEditor.IMGUI.Controls.MultiColumnHeader(columns), row =>
                { recordingIndex = row.index; startFrame = 0; endFrame = Recording?.ValidFrames ?? 0; audition.Stop(); });
            }
            var doc = Document;
            recordingTable.SetRows(doc.Report.Recordings.Select((r, i) => new BlackBoxTableRow
            {
                id = i + 1, index = i, values = new[] { r.Id, r.SampleRate > 0 ? (r.ValidFrames / (double)r.SampleRate).ToString("F2") + " s" : "—",
                    !r.Decoded ? "Decode unavailable" : session.regions.Any(m => m.sourceHash == doc.Report.Source.Sha256 && m.recordingId == r.Id) ? "Mapped" : "Ready to map" }
            }));
            recordingTable.SetSelection(new[] { recordingIndex + 1 });
            recordingTable.OnGUI(GUILayoutUtility.GetRect(200, Mathf.Clamp(doc.Report.Recordings.Count * 20 + 30, 90, 230), GUILayout.ExpandWidth(true)));
        }

        private void DrawSourceEvidence()
        {
            var doc = Document;
            if (GUILayout.Button("Inspect bank references")) ShowView(3);
            Text("Source SHA-256", doc.Report.Source.Sha256);
            Text("Location", doc.Attachment.root + "/" + doc.Attachment.relativePath);
            Text("Parser", doc.Report.Variant + " · " + doc.Report.ParserVersion);
            EditorGUI.BeginChangeCheck(); string context = EditorGUILayout.TextField("Declared game / platform", doc.Attachment.declaredContext);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(session, "Declare source context"); doc.Attachment.declaredContext = context; presenter.Touch(); }
            foreach (var capability in doc.Report.Capabilities)
                if (capability.Name != "native-conversion") Text(capability.Name + " · " + capability.Status, capability.Evidence);
            foreach (var gap in doc.Report.Gaps.Take(24)) Text(gap.Code, gap.Message + " Next: " + gap.NextExperiment);
            using (new EditorGUI.DisabledScope(presenter.Busy)) if (GUILayout.Button("Export source evidence")) Run(ExportEvidence);
        }

        private void ExportEvidence()
        {
            session.ValidateSchema();
            var documents = presenter.Documents; string folder = Path.GetFullPath("Library/BlackBoxAudio/Reports/" + session.id + "/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            presenter.Start(c =>
            {
                Directory.CreateDirectory(folder);
                foreach (var document in documents) { c.ThrowIfCancellationRequested(); File.WriteAllText(Path.Combine(folder, document.Report.Source.Sha256 + ".json"), JsonUtility.ToJson(BlackBoxEvidenceExport.From(document), true)); }
                File.WriteAllLines(Path.Combine(folder, "source-inventory.txt"), documents.Select(d => d.Report.Source.Sha256 + "\t" + d.Report.Variant + "\t" + d.Attachment.relativePath + "\tDeclared context: " + d.Attachment.declaredContext));
                return folder;
            }, result => { presenter.SetStatus("Evidence exported to " + result); EditorUtility.RevealInFinder(result); });
        }

        private void DrawStructure()
        {
            var doc = Document; if (doc == null) { EditorGUILayout.HelpBox("Add sources before inspecting their structure.", MessageType.Info); return; }
            EditorGUI.BeginChangeCheck(); structureIndex = EditorGUILayout.Popup("Structure branch", structureIndex, structureLabels);
            if (EditorGUI.EndChangeCheck()) { rowsDirty = true; page = 0; }
            DrawStructureTableView();
            if (rows.Length == 100000) EditorGUILayout.HelpBox("Showing the first 100,000 matches. Refine the search or navigate by byte address; original table entries remain intact.", MessageType.Warning);
            EditorGUILayout.Space();
            Text(selectedName.Length == 0 ? "Select a field or byte" : selectedName, selectedNotes);
            if (selectedLength > 0) Text("Evidence chain", doc.Report.Source.Sha256 + " → " + doc.Report.ParserVersion + " → 0x" + selectedOffset.ToString("X") + " +" + selectedLength + " bytes");
            using (new EditorGUILayout.HorizontalScope())
            {
                byteAddress = EditorGUILayout.TextField("Hex byte address", byteAddress);
                if (GUILayout.Button("Go", GUILayout.Width(55))) Run(() => { long target = Convert.ToInt64(byteAddress.Replace("0x", ""), 16); SelectByte(Math.Max(0, Math.Min(doc.Bytes.LongLength - 1, target))); });
                if (GUILayout.Button("−256", GUILayout.Width(60))) byteOffset = Math.Max(0, byteOffset - 256);
                if (GUILayout.Button("+256", GUILayout.Width(60))) byteOffset = Math.Min(Math.Max(0, (doc.Bytes.Length - 1) / 256 * 256), byteOffset + 256);
            }
            if (Event.current.type == EventType.KeyDown && GUIUtility.keyboardControl == 0)
            {
                long delta = Event.current.keyCode == KeyCode.LeftArrow ? -1 : Event.current.keyCode == KeyCode.RightArrow ? 1 : Event.current.keyCode == KeyCode.PageDown ? 256 : Event.current.keyCode == KeyCode.PageUp ? -256 : 0;
                if (delta != 0) { SelectByte(Math.Max(0, Math.Min(doc.Bytes.LongLength - 1, selectedOffset + delta))); Event.current.Use(); }
            }
            for (long offset = byteOffset; offset < Math.Min(byteOffset + 256, doc.Bytes.LongLength); offset += 16)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(offset.ToString("X8"), EditorStyles.miniLabel, GUILayout.Width(80));
                    for (int b = 0; b < 16 && offset + b < doc.Bytes.LongLength; b++)
                    {
                        long index = offset + b; bool selected = index >= selectedOffset && index - selectedOffset < selectedLength;
                        if (GUILayout.Toggle(selected, doc.Bytes[index].ToString("X2"), "Button", GUILayout.Width(30))) if (!selected) SelectByte(index);
                    }
                    GUILayout.FlexibleSpace();
                }
            }
            EditorGUILayout.LabelField("Offsets are bytes. Frame indices and interleaved PCM scalar indices are separate coordinates.", EditorStyles.wordWrappedMiniLabel);
        }

        private void SelectByte(long offset)
        {
            var report = Document.Report;
            foreach (var field in report.Fields)
                if (offset >= field.Range.Offset && offset < field.Range.End) { SelectRange(field.Name, field.Range, BlackBoxByteRow.Describe(field)); return; }
            for (int t = 0; t < report.Tables.Count; t++)
            {
                var table = report.Tables[t]; if (offset < table.Range.Offset || offset >= table.Range.End) continue;
                int low = 0, high = table.Entries.Count - 1;
                while (low <= high)
                {
                    int mid = low + (high - low) / 2; var entry = table.Entries[mid];
                    if (offset < entry.Range.Offset) high = mid - 1;
                    else if (offset >= entry.Range.End) low = mid + 1;
                    else { SelectRange(table.Name + "[" + entry.Index + "]", entry.Range, entry.Provenance + " · " + entry.Semantic); structureIndex = t + 1; rowsDirty = true; return; }
                }
            }
            string note = string.Join("\n", report.References.Where(r => offset >= r.Source.Offset && offset < r.Source.End).Select(r => r.Kind + " → " + r.Target + " · " + r.Notes));
            if (note.Length == 0) note = "No interpreted field contains this byte. Inspect the payload or unresolved-span ledger; byte presence alone does not establish semantics.";
            SelectRange("Byte 0x" + offset.ToString("X"), new ByteRange(offset, 1), note);
        }

        private void DrawBanks()
        {
            var doc = Document; if (doc == null) { EditorGUILayout.HelpBox("Attach a bank and any available companion configuration.", MessageType.Info); return; }
            GUILayout.Label("Bank references", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox("Decoded bank recordings are available in Recordings. You can give each one an RPM/load range. Original event roles and controller rules remain unverified.", MessageType.Info);
            if (GUILayout.Button("Choose a recording to map →", GUILayout.Height(28))) ShowView(0);
            bankReferencesOpen = EditorGUILayout.Foldout(bankReferencesOpen, doc.Report.References.Count + " source references · technical details", true);
            if (!bankReferencesOpen) return;
            int count = doc.Report.References.Count; Page(ref bankPage, count, 40);
            foreach (var edge in doc.Report.References.Skip(bankPage * 40).Take(40))
            {
                EditorGUILayout.LabelField(edge.Kind + " → " + edge.Target, EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(edge.Source.ToString(), GUILayout.Width(185))) { SelectRange(edge.Kind, edge.Source, edge.Notes); ShowView(1); }
                    EditorGUILayout.LabelField((edge.RangeVerified ? "Range checked" : "Unresolved target") + " · " + edge.Provenance + " · " + edge.Notes, EditorStyles.wordWrappedMiniLabel);
                }
                if (edge.Kind == "configuration-names-source")
                {
                    var candidates = presenter.Documents.Where(d => Path.GetFileName(d.Attachment.relativePath).Equals(Path.GetFileName(edge.Target), StringComparison.OrdinalIgnoreCase)).ToArray();
                    EditorGUILayout.LabelField(candidates.Length == 0 ? "Missing named companion" : candidates.Length == 1 ? "One filename candidate; game/build binding unverified" : "AMBIGUOUS: " + candidates.Length + " paths/revisions match this filename", EditorStyles.wordWrappedLabel);
                    foreach (var candidate in candidates.Take(8)) if (GUILayout.Button(candidate.Attachment.relativePath + " · " + candidate.Report.Source.Sha256.Substring(0, 12))) { SelectSource(Array.IndexOf(presenter.Documents, candidate)); ShowView(0); }
                }
            }
            if (count == 0) EditorGUILayout.HelpBox("No dependency edges are verified for this source. Lack of a parsed reference does not establish that a payload is unused by the game.", MessageType.Info);
            EditorGUILayout.Space(); PickRecording();
            if (Recording != null) Text("Selected physical payload", Recording.Id + " · " + Recording.Payload + " · " + Recording.Codec + "\n" + Recording.Notes);
            foreach (var gap in doc.Report.Gaps.Take(12)) Text(gap.Code, gap.Message + " Next: " + gap.NextExperiment);
            EditorGUILayout.HelpBox("Original control evaluation: BLOCKED for unresolved operations. The authored native test scenarios in Playback trace use documented native rules and do not execute bank bytes or configuration scripts.", MessageType.Warning);
        }
    }
}
