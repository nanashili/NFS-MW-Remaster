using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public sealed class CareerSaveInspector : EditorWindow
    {
        private string directory, selected, destination = "career_copy", activeSlot = "", report = "";
        private IReadOnlyList<SaveSlotInfo> slots;
        private Vector2 scroll;
        [MenuItem("Tools/Driving/Save Inspector")]
        public static void Open() { GetWindow<CareerSaveInspector>("Save Inspector"); }
        private void OnEnable() { directory = System.IO.Path.Combine(Application.persistentDataPath, "CareerProfiles"); }
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Header inspection is read-only. Verify checks the full profile without restoring gameplay. Archive is recoverable; no save bytes are deleted.", MessageType.Info);
            directory = EditorGUILayout.TextField("Save directory", directory);
            activeSlot = EditorGUILayout.TextField("Protected active slot", activeSlot);
            if (GUILayout.Button("Refresh slots")) Run(() => { slots = new CareerSaveRepository(directory).Enumerate(); });
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (slots != null) foreach (SaveSlotInfo slot in slots)
            {
                if (GUILayout.Button(slot.Slot + " — " + (slot.Header == null ? "Unreadable header" : slot.Header.displayName + " / generation " + slot.Header.generation))) selected = slot.Slot;
                EditorGUILayout.LabelField(slot.Status, EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField("Selected", selected ?? "None");
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(selected)))
            {
                if (GUILayout.Button("Verify profile and recovery")) Run(() =>
                {
                    var result = new CareerSaveRepository(directory).Load(selected);
                    report = "Container " + result.Header.format + ", schema " + result.Header.schema
                        + ", generation " + result.Header.generation + ", bytes " + result.Header.payloadBytes
                        + "\nOperation " + result.Header.operation + "\nParent " + result.Header.parent
                        + "\n" + (string.IsNullOrEmpty(result.Recovery) ? "Valid" : result.Recovery);
                });
                if (GUILayout.Button("Inspect all recovery / staged artifacts")) Run(() =>
                {
                    var lines = new System.Text.StringBuilder();
                    foreach (var artifact in new CareerSaveRepository(directory).InspectRecovery(selected))
                        lines.AppendLine(System.IO.Path.GetFileName(artifact.Path) + " — " + artifact.Bytes + " bytes — " + artifact.Status);
                    report = lines.ToString();
                });
                destination = EditorGUILayout.TextField("Copy to slot", destination);
                if (GUILayout.Button("Copy into a NEW slot")) Run(() => { new CareerSaveRepository(directory).Copy(selected, destination); report = "Copied to " + destination; });
                using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
                    if (GUILayout.Button("Archive selected slot…") && EditorUtility.DisplayDialog("Archive career?",
                        "Remove " + selected + " from the slot list? Its files remain recoverable in .archives.", "Archive", "Cancel"))
                        Run(() => { var repo = new CareerSaveRepository(directory); var read = repo.Load(selected);
                            report = "Archived to " + repo.Archive(selected, read.HeadStamp, activeSlot); slots = repo.Enumerate(); selected = null; });
            }
            if (GUILayout.Button("Restore archived directory…"))
            {
                string path = EditorUtility.OpenFolderPanel("Select archived slot", System.IO.Path.Combine(directory, ".archives"), "");
                if (!string.IsNullOrEmpty(path)) Run(() => { new CareerSaveRepository(directory).RestoreArchive(path); report = "Archive restored."; });
            }
            EditorGUILayout.TextArea(report, GUILayout.MinHeight(90));
        }
        private void Run(Action action) { try { action(); } catch (Exception exception) { report = exception.Message; } }
    }

    [CustomEditor(typeof(CareerProfileSystem))]
    public sealed class CareerProfileSaveInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var profile = (CareerProfileSystem)target;
            EditorGUILayout.LabelField("Save state", profile.SaveStatus);
            EditorGUILayout.LabelField("Queued priority", profile.QueuedReason.ToString());
            EditorGUILayout.LabelField("Dirty / saved revision", profile.DirtyRevision + " / " + profile.CommittedRevision);
            EditorGUILayout.LabelField("Last snapshot (ms)", profile.LastSnapshotMilliseconds.ToString("F2"));
            if (profile.LastSnapshotMilliseconds > 8)
                EditorGUILayout.HelpBox("Snapshot capture exceeded the 8 ms authoring warning budget. Profile the main-thread DTO capture before increasing save size.", MessageType.Warning);
            if (!string.IsNullOrEmpty(profile.LastSaveFailure)) EditorGUILayout.HelpBox(profile.LastSaveFailure, MessageType.Warning);
            using (new EditorGUI.DisabledScope(!Application.isPlaying || profile.SaveInProgress))
                if (GUILayout.Button("Mark dirty for next safe-point autosave")) profile.MarkPersistentStateChanged();
        }
    }
}
