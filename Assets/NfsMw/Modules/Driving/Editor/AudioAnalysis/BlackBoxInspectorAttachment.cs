using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    public sealed partial class BlackBoxInspectorWindow
    {
        [SerializeField] private VehicleAudio attachmentTarget;
        private VehicleAudio[] attachmentCandidates = Array.Empty<VehicleAudio>();

        private void SelectAttachmentTarget(VehicleAudio target)
        {
            attachmentTarget = target;
            BlackBoxVehicleAttachment.RememberTarget(session, target);
            presenter.Touch();
        }

        private void UseAttachmentSelection(UnityEngine.Object selection)
        {
            attachmentCandidates = BlackBoxVehicleAttachment.FindSceneVehicles(selection);
            SelectAttachmentTarget(attachmentCandidates.Length == 1 ? attachmentCandidates[0] : null);
            if (attachmentCandidates.Length == 0)
                presenter.SetStatus("Select a VehicleAudio in the Hierarchy, or its model/folder in the Project. Its scene must be open.");
        }

        private void DrawVehicleAttachment()
        {
            GUILayout.Space(8);
            GUILayout.Label("Target vehicle", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var selected = EditorGUILayout.ObjectField(attachmentTarget != null ? attachmentTarget.gameObject : null, typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck()) UseAttachmentSelection(selected);
                if (GUILayout.Button("Use selection", GUILayout.Width(104))) Run(() => UseAttachmentSelection(Selection.activeObject));
            }
            if (attachmentCandidates.Length > 1)
            {
                string[] labels = new[] { "Choose a scene vehicle…" }.Concat(attachmentCandidates.Select(p => p == null ? "Unavailable" : p.gameObject.scene.name + " / " + p.name)).ToArray();
                int index = Array.IndexOf(attachmentCandidates, attachmentTarget) + 1;
                EditorGUI.BeginChangeCheck(); int next = EditorGUILayout.Popup("Matching vehicles", index, labels);
                if (EditorGUI.EndChangeCheck()) SelectAttachmentTarget(next > 0 ? attachmentCandidates[next - 1] : null);
            }
            if (attachmentTarget == null)
            {
                EditorGUILayout.HelpBox(string.IsNullOrEmpty(session.targetScenePath)
                    ? "Drag in your scene vehicle. You can also select its model folder in the Project and click Use selection."
                    : "Open " + session.targetScenePath + " and select the target vehicle again.", MessageType.Info);
                return;
            }
            EditorGUILayout.LabelField(attachmentTarget.gameObject.scene.name + " / " + attachmentTarget.name, EditorStyles.wordWrappedMiniLabel);
        }

        private void PrepareAttachment()
        {
            EnsureOutputFolder();
            presenter.Plan = BlackBoxVehicleAttachment.Prepare(session, presenter.Documents, attachmentTarget);
            presenter.SetStatus("Attachment prepared for " + attachmentTarget.name + ". Review the profile and apply it below.");
        }

        private void DrawAttachmentPlan()
        {
            var plan = presenter.Plan;
            if (plan == null || plan.SceneTarget == null) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Ready to attach · " + plan.SceneTarget.name, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(session.regions.Count + " reviewed mappings · profile, audio and mapping metadata", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(plan.ProfilePath, EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(presenter.Busy || EditorApplication.isPlayingOrWillChangePlaymode))
                    if (GUILayout.Button("Apply engine audio", GUILayout.Height(30))) Queue(() =>
                    {
                        var prepared = presenter.Plan;
                        BlackBoxNativeMapping.ApplyToScene(prepared);
                        presenter.Plan = null;
                        presenter.SetStatus("Attached to " + prepared.SceneTarget.name + ". Save the scene to keep the assignment.");
                        Selection.activeGameObject = prepared.SceneTarget.gameObject;
                    });
            }
        }

        private void DrawAttachedVehicle(VehicleSensoryProfile saved)
        {
            if (attachmentTarget == null || saved == null || attachmentTarget.Profile != saved) return;
            EditorGUILayout.LabelField("✓ Attached to " + attachmentTarget.name, EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select vehicle")) { Selection.activeGameObject = attachmentTarget.gameObject; EditorGUIUtility.PingObject(attachmentTarget.gameObject); }
                using (new EditorGUI.DisabledScope(!attachmentTarget.gameObject.scene.isDirty || EditorApplication.isPlayingOrWillChangePlaymode))
                    if (GUILayout.Button("Save vehicle scene")) Run(() => EditorSceneManager.SaveScene(attachmentTarget.gameObject.scene));
            }
        }
    }
}
