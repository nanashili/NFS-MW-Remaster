using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving.Editor
{
    [CustomEditor(typeof(VehiclePhysicsLabDefinition))]
    public sealed class VehiclePhysicsLabDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            VehiclePhysicsLabDefinition experiment = (VehiclePhysicsLabDefinition)target;
            EditorGUILayout.Space(6f);
            if (experiment.IsValid(out string failure))
                EditorGUILayout.HelpBox("Valid for isolated manual stepping. Temporary overrides are runtime-only until explicitly applied.", MessageType.Info);
            else
                EditorGUILayout.HelpBox(failure, MessageType.Error);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Open Vehicle Physics Lab")) VehiclePhysicsLabWindow.Open().SelectDefinition(experiment);
                if (GUILayout.Button("Select vehicle setup") && experiment.vehicle != null) Selection.activeObject = experiment.vehicle;
                if (GUILayout.Button("Select track asset") && experiment.track != null) Selection.activeObject = experiment.track;
            }
        }
    }

    [CustomEditor(typeof(VehiclePhysicsLabSweepDefinition))]
    public sealed class VehiclePhysicsLabSweepDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            VehiclePhysicsLabSweepDefinition sweep = (VehiclePhysicsLabSweepDefinition)target;
            EditorGUILayout.Space(6f);
            if (sweep.IsValid(out string failure))
                EditorGUILayout.HelpBox("Sweep is valid: " + sweep.TrialCount + " isolated trial(s).", MessageType.Info);
            else
                EditorGUILayout.HelpBox(failure, MessageType.Error);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Open Vehicle Physics Lab"))
                {
                    VehiclePhysicsLabWindow window = VehiclePhysicsLabWindow.Open();
                    window.SelectSweep(sweep);
                }
            }
        }
    }

    [CustomEditor(typeof(VehiclePhysicsLabSuite))]
    public sealed class VehiclePhysicsLabSuiteEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            VehiclePhysicsLabSuite suite = (VehiclePhysicsLabSuite)target;
            EditorGUILayout.Space(6f);
            if (suite.IsValid(out string failure))
                EditorGUILayout.HelpBox("Suite is valid: " + suite.experiments.Length * suite.repetitions + " isolated run(s).", MessageType.Info);
            else
                EditorGUILayout.HelpBox(failure, MessageType.Error);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Open Vehicle Physics Lab")) VehiclePhysicsLabWindow.Open().SelectSuite(suite);
            }
        }
    }

    [CustomEditor(typeof(VehiclePhysicsLabTrack))]
    public sealed class VehiclePhysicsLabTrackEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            VehiclePhysicsLabTrack track = (VehiclePhysicsLabTrack)target;
            EditorGUILayout.Space(6f);
            if (track.IsValid(out string failure)) EditorGUILayout.HelpBox("Track fixture is valid.", MessageType.Info);
            else EditorGUILayout.HelpBox(failure, MessageType.Error);
            if (GUILayout.Button("Open Vehicle Physics Lab"))
            {
                VehiclePhysicsLabWindow window = VehiclePhysicsLabWindow.Open();
                VehiclePhysicsLabDefinition experiment = VehiclePhysicsLabEditorAssetSearch.FindReferencingExperiment(track);
                if (experiment != null) { window.SelectDefinition(experiment); return; }
                Selection.activeObject = track;
            }
        }
    }

    [CustomEditor(typeof(VehiclePhysicsLabTrackAnchor))]
    public sealed class VehiclePhysicsLabTrackAnchorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            VehiclePhysicsLabTrackAnchor anchor = (VehiclePhysicsLabTrackAnchor)target;
            if (anchor.track == null) return;
            if (GUILayout.Button("Open track asset")) Selection.activeObject = anchor.track;
            if (GUILayout.Button("Open Vehicle Physics Lab"))
            {
                VehiclePhysicsLabWindow window = VehiclePhysicsLabWindow.Open();
                VehiclePhysicsLabDefinition experiment = VehiclePhysicsLabEditorAssetSearch.FindReferencingExperiment(anchor.track);
                if (experiment != null) window.SelectDefinition(experiment);
            }
            EditorGUILayout.HelpBox("Use the Vehicle Physics Lab Track Tool in the Scene view to move the anchor and edit the track width with Undo.", MessageType.Info);
        }

        private void OnSceneGUI()
        {
            VehiclePhysicsLabTrackAnchor anchor = (VehiclePhysicsLabTrackAnchor)target;
            VehiclePhysicsLabSceneDrawing.DrawTrack(anchor.track, anchor.transform, anchor.drawLabels);
        }
    }

    /// <summary>
    /// Scene-view authoring tool for fixture placement. It edits only the
    /// selected anchor transform and track asset, with explicit Undo records.
    /// </summary>
    [EditorTool("Vehicle Physics Lab Track Tool", typeof(VehiclePhysicsLabTrackAnchor))]
    public sealed class VehiclePhysicsLabTrackTool : EditorTool
    {
        public override GUIContent toolbarIcon => new GUIContent("LAB", "Move the Physics Lab anchor and edit its fixture width.");

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            VehiclePhysicsLabTrackAnchor anchor = Selection.activeGameObject == null
                ? null
                : Selection.activeGameObject.GetComponent<VehiclePhysicsLabTrackAnchor>();
            if (anchor == null || anchor.track == null || !anchor.track.IsValid(out _)) return;

            Event current = Event.current;
            if (current.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(anchor.transform.position, anchor.transform.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(anchor.transform, "Move Physics Lab track anchor");
                anchor.transform.position = moved;
                EditorSceneManager.MarkSceneDirty(anchor.gameObject.scene);
            }

            VehiclePhysicsLabTrackSample sample = anchor.track.Sample(0f);
            Vector3 worldPosition = anchor.transform.TransformPoint(sample.position + sample.left * sample.width * 0.5f);
            Vector3 worldDirection = anchor.transform.TransformDirection(sample.left).normalized;
            float size = HandleUtility.GetHandleSize(worldPosition) * 0.12f;
            EditorGUI.BeginChangeCheck();
            Vector3 changedWidth = Handles.Slider(worldPosition, worldDirection, size, Handles.SphereHandleCap, 0.1f);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 local = anchor.transform.InverseTransformPoint(changedWidth) - sample.position;
                float width = Mathf.Max(2f, Vector3.Dot(local, sample.left) * 2f);
                Undo.RecordObject(anchor.track, "Resize Physics Lab track");
                anchor.track.width = width;
                EditorUtility.SetDirty(anchor.track);
            }
            Handles.Label(anchor.transform.TransformPoint(sample.position + Vector3.up), "Physics Lab · width " + anchor.track.width.ToString("F1") + " m");
            SceneView.RepaintAll();
        }
    }

    [Overlay(typeof(SceneView), "Vehicle Physics Lab")]
    public sealed class VehiclePhysicsLabSceneOverlay : Overlay
    {
        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            root.style.minWidth = 220f;
            root.Add(new Button(() => VehiclePhysicsLabWindow.Open()) { text = "Open Vehicle Physics Lab" });
            root.Add(new Button(() => ToolManager.SetActiveTool<VehiclePhysicsLabTrackTool>()) { text = "Activate track tool" });
            Toggle track = new Toggle("Draw track") { value = VehiclePhysicsLabWindow.SceneDrawTrack };
            root.Add(track);
            track.RegisterValueChangedCallback(change =>
            {
                VehiclePhysicsLabWindow.SceneDrawTrack = change.newValue;
                SceneView.RepaintAll();
            });
            Toggle trajectory = new Toggle("Draw trajectory") { value = VehiclePhysicsLabWindow.SceneDrawTrajectory };
            root.Add(trajectory);
            trajectory.RegisterValueChangedCallback(change =>
            {
                VehiclePhysicsLabWindow.SceneDrawTrajectory = change.newValue;
                SceneView.RepaintAll();
            });
            Toggle forces = new Toggle("Draw force vectors") { value = VehiclePhysicsLabWindow.SceneDrawForces };
            root.Add(forces);
            forces.RegisterValueChangedCallback(change =>
            {
                VehiclePhysicsLabWindow.SceneDrawForces = change.newValue;
                SceneView.RepaintAll();
            });
            Toggle contacts = new Toggle("Draw wheel contacts") { value = VehiclePhysicsLabWindow.SceneDrawContacts };
            root.Add(contacts);
            contacts.RegisterValueChangedCallback(change =>
            {
                VehiclePhysicsLabWindow.SceneDrawContacts = change.newValue;
                SceneView.RepaintAll();
            });
            root.Add(new Label("Scene overlay is visualization only. Physics remains owned by VehicleController and the isolated runner."));
            return root;
        }

    }

    internal static class VehiclePhysicsLabEditorAssetSearch
    {
        public static VehiclePhysicsLabDefinition FindReferencingExperiment(VehiclePhysicsLabTrack track)
        {
            if (track == null) return null;
            string[] guids = AssetDatabase.FindAssets("t:VehiclePhysicsLabDefinition");
            for (int i = 0; i < guids.Length; i++)
            {
                VehiclePhysicsLabDefinition experiment = AssetDatabase.LoadAssetAtPath<VehiclePhysicsLabDefinition>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (experiment != null && experiment.track == track) return experiment;
            }
            return null;
        }
    }
}
