#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    [CustomEditor(typeof(SensoryMusicProfile))]
    public sealed class AdaptiveMusicProfileInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var profile = (SensoryMusicProfile)target;
            EditorGUILayout.HelpBox(profile.HasArrangement ? "Versioned adaptive arrangement" : "Legacy compatibility arrangement", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Adaptive Music Editor")) AdaptiveMusicEditorWindow.Open(profile);
                if (GUILayout.Button("Validate"))
                {
                    var diagnostics = AdaptiveMusicEditorModel.ValidateProfile(profile);
                    int errors = 0; foreach (var item in diagnostics) if (item.severity == AdaptiveMusicDiagnosticSeverity.Error) errors++;
                    Debug.Log("Adaptive music validation: " + diagnostics.Count + " diagnostic(s), " + errors + " error(s).", profile);
                }
            }
            if (!profile.HasArrangement && GUILayout.Button("Migrate legacy arrangement"))
            {
                AdaptiveMusicEditorModel.MigrateLegacy(profile);
                EditorUtility.SetDirty(profile);
            }
            DrawDefaultInspector();
        }
    }

    [CustomEditor(typeof(AdaptiveMusic))]
    public sealed class AdaptiveMusicDirectorInspector : UnityEditor.Editor
    {
        private readonly System.Collections.Generic.List<AdaptiveMusicTraceEntry> trace = new System.Collections.Generic.List<AdaptiveMusicTraceEntry>();

        public override void OnInspectorGUI()
        {
            var director = (AdaptiveMusic)target;
            EditorGUILayout.HelpBox("Single adaptive transport owner. SensoryAudioWorld remains the pooled voice and mixer boundary.", MessageType.Info);
            if (GUILayout.Button("Open Adaptive Music Editor")) AdaptiveMusicEditorWindow.Open(director.Profile);
            DrawDefaultInspector();
            EditorGUILayout.Space(6); EditorGUILayout.LabelField("RUNTIME TRANSPORT", EditorStyles.boldLabel);
            var transport = director.Transport;
            EditorGUILayout.LabelField("Running", transport.running ? transport.paused ? "Paused" : "Yes" : "No");
            EditorGUILayout.LabelField("Context", transport.context.ToString());
            EditorGUILayout.LabelField("Section", transport.activeSectionId);
            EditorGUILayout.LabelField("Pending", transport.pendingSectionId);
            EditorGUILayout.LabelField("DSP", transport.dspTime.ToString("0.000") + " · beat " + transport.beat.ToString("0.00") + " · bar " + transport.bar);
            if (!string.IsNullOrEmpty(transport.lastError)) EditorGUILayout.HelpBox(transport.lastError, MessageType.Warning);
            if (GUILayout.Button("Copy trace to Console"))
            {
                trace.Clear(); director.CopyTrace(trace);
                foreach (var item in trace) Debug.Log(item.dspTime.ToString("0.000") + " " + item.eventName + " " + item.detail, director);
            }
        }
    }

    public static class AdaptiveMusicSceneGizmos
    {
        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        private static void Draw(AdaptiveMusic director, GizmoType gizmoType)
        {
            if (director == null) return;
            Color previous = Handles.color;
            Handles.color = director.HasStarted ? new Color(0.2f, 0.85f, 1f, 0.8f) : new Color(1f, 0.55f, 0.15f, 0.8f);
            Handles.DrawWireDisc(director.transform.position, Vector3.up, 1.25f);
            Handles.Label(director.transform.position + Vector3.up * 1.4f,
                "Adaptive Music\n" + (string.IsNullOrEmpty(director.ActiveSectionId) ? "not started" : director.ActiveSectionId));
            Handles.color = previous;
        }
    }
}
#endif
