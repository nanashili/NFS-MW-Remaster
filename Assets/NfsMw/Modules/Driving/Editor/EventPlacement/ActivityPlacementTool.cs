using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving.Editor
{
    [EditorTool("Place / edit world activity")]
    public sealed class ActivityPlacementTool : EditorTool
    {
        public override void OnWillBeDeactivated()=>EventPlacementView.StopActiveBrush();
        public override GUIContent toolbarIcon => new GUIContent("Activity", "Place an activity on a collision surface or edit its world anchor");
        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { ToolManager.RestorePreviousTool(); e.Use(); return; }
            if (EventPlacementWindow.Brush != null)
            {
                if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (Physics.Raycast(ray, out var hit, 100000, ~0, QueryTriggerInteraction.Ignore))
                {
                    Handles.color = Color.yellow; Handles.DrawWireDisc(hit.point, hit.normal, 4);
                    if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
                    {
                        try { EventPlacementCommands.Create(EventPlacementWindow.Brush, hit.point); }
                        catch (System.Exception exception) { Debug.LogWarning(exception.Message); }
                        e.Use();
                    }
                }
                window.Repaint(); return;
            }
            var source = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<EventPlacementSource>() : null;
            if (source == null || !EventPlacementCompiler.Resolve(source.anchor, out var pose, out _)) return;
            if (source.anchor.kind == ActivityAnchorKind.World)
            {
                EditorGUI.BeginChangeCheck(); Vector3 position = Handles.PositionHandle(pose.position, pose.rotation);
                if (EditorGUI.EndChangeCheck()) EventPlacementCommands.MoveWorld(source, position);
            }
            else Handles.Label(pose.position, "Bound anchor: edit station / source in Event Placement Studio");
        }
    }

    [CustomEditor(typeof(EventPlacementSource)), CanEditMultipleObjects]
    public sealed class EventPlacementSourceInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("Open Event Placement Studio")) EventPlacementWindow.Open();
            serializedObject.Update(); DrawPropertiesExcluding(serializedObject, "m_Script", "id", "schema", "published");
            if (serializedObject.ApplyModifiedProperties()) foreach (EventPlacementSource source in targets) EventPlacementCommands.Changed(source);
            EditorGUILayout.SelectableLabel(((EventPlacementSource)target).id, GUILayout.Height(18));
        }
        private void OnSceneGUI()
        {
            var source = (EventPlacementSource)target;
            if (!EventPlacementCompiler.Resolve(source.anchor, out var pose, out _)) return;
            Vector3 entrance = pose.position + pose.rotation * source.interactionOffset;
            using (new Handles.DrawingScope(Color.green, Matrix4x4.TRS(entrance, pose.rotation, Vector3.one))) Handles.DrawWireCube(Vector3.zero, source.triggerSize);
            Handles.color = Color.cyan;
            if (EventPlacementCompiler.Resolve(source.access, out var access, out _)) { Handles.DrawDottedLine(access.position, entrance, 5); Handles.Label(access.position, "GPS / road access"); }
            Handles.Label(entrance, source.name + "\nEntrance");
            Vector3 staging = pose.position + pose.rotation * source.stagingOffset;
            if (source.definition != null && source.definition.adapter == "race" && source.definition.race != null && source.definition.race.published != null)
            {
                var route = source.definition.race.published;
                for (int i = 0; i < route.GridCount; i++)
                    using (new Handles.DrawingScope(Color.magenta, Matrix4x4.TRS(route.GridPosition(i) + route.GridRotation(i) * Vector3.up * (route.EntrantDimensions.y / 2), route.GridRotation(i), Vector3.one)))
                        Handles.DrawWireCube(Vector3.zero, route.EntrantDimensions);
                if (route.GridCount > 0) staging = route.GridPosition(0);
            }
            using (new Handles.DrawingScope(Color.magenta, Matrix4x4.TRS(staging + pose.rotation * Vector3.up * (source.vehicleSize.y / 2), pose.rotation, Vector3.one))) Handles.DrawWireCube(Vector3.zero, source.vehicleSize);
            Handles.color = Color.yellow; Vector3 icon = pose.position + pose.rotation * source.iconOffset; Handles.DrawDottedLine(entrance, icon, 3); Handles.Label(icon, "Map icon");
            Handles.ArrowHandleCap(0, entrance, pose.rotation, 4, EventType.Repaint);
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUI.BeginChangeCheck(); Vector3 nextIcon = Handles.PositionHandle(icon, pose.rotation);
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(source, "Move map icon"); source.iconOffset = Quaternion.Inverse(pose.rotation) * (nextIcon - pose.position); EventPlacementCommands.Changed(source); }
            }
        }
    }

    [Overlay(typeof(SceneView), "World activities")]
    public sealed class ActivityPlacementOverlay : Overlay
    {
        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            root.Add(new Button(EventPlacementWindow.Open) { text = "Event Placement Studio" });
            root.Add(new Button(() => ToolManager.SetActiveTool<ActivityPlacementTool>()) { text = "Edit activity handles" });
            root.Add(new Button(() => SceneView.lastActiveSceneView?.FrameSelected()) { text = "Frame selected activity" });
            return root;
        }
    }
}
