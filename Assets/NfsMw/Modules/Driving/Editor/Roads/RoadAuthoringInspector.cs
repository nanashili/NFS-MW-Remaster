using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace NfsMwRemaster.Driving.Editor
{
    [CustomEditor(typeof(RoadAuthoring)), CanEditMultipleObjects]
    public sealed class RoadAuthoringInspector : UnityEditor.Editor
    {
        private int curve;
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck(); DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck()) foreach (RoadAuthoring road in targets) RoadPreview.Invalidate(road);
            if (GUILayout.Button("Apply / reconcile profile"))
                foreach (RoadAuthoring road in targets) if (road.Profile != null) RoadAuthoringCommands.ApplyProfile(road, road.Profile);
            if (GUILayout.Button("Rebuild preview")) foreach (RoadAuthoring road in targets) RoadPreview.Rebuild(road);
            if (targets.Length == 1)
            {
                var road = (RoadAuthoring)target;
                curve = EditorGUILayout.IntField("Curve to split", curve);
                if (GUILayout.Button("Insert midpoint (preserve shape)"))
                {
                    try { RoadAuthoringCommands.InsertKnot(road, curve, 0.5f); }
                    catch (ArgumentException exception) { Debug.LogWarning(exception.Message, road); }
                }
                if (RoadPreview.Error(road) is string error) EditorGUILayout.HelpBox(error, MessageType.Error);
                EditorGUILayout.HelpBox("Preview meshes are temporary. Save the scene and bake before using this road in a player build.", MessageType.Info);
            }
        }
        private void OnSceneGUI()
        {
            var road = (RoadAuthoring)target;
            if (road.Reference == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var spline = road.Reference.Spline; var transform = road.Reference.transform;
            for (int i = 0; i < spline.Count; i++)
            {
                var knot = spline[i]; Vector3 world = transform.TransformPoint(knot.Position);
                EditorGUI.BeginChangeCheck();
                Vector3 changed = Handles.PositionHandle(world, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(road.Reference, "Move road knot");
                    knot.Position = transform.InverseTransformPoint(changed); spline[i] = knot;
                    RoadAuthoringCommands.Changed(road.Reference);
                }
                Handles.Label(world, i.ToString());
            }
        }
    }
}
