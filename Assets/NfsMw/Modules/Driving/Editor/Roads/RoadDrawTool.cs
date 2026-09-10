using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    [EditorTool("Draw road")]
    public sealed class RoadDrawTool : EditorTool
    {
        public static RoadProfile Profile;
        public static RoadNetworkAuthoring Network;
        public static bool SnapToSurface = true;
        public static float Grid = 1;
        private readonly List<Vector3> points = new List<Vector3>();
        private string message;
        public override GUIContent toolbarIcon => new GUIContent("Road", "Draw a road: click points, Enter to create, Escape to cancel.");
        public override void OnWillBeDeactivated() { points.Clear(); message = null; }
        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var evt = Event.current;
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(12, 12, 410, 90), GUI.skin.box);
            GUILayout.Label(Profile == null ? "Choose a profile in the Road Network Editor." : "Draw " + Profile.name);
            GUILayout.Label("Click: add point  ·  Enter/double-click: create  ·  Escape: cancel");
            if (!string.IsNullOrEmpty(message)) GUILayout.Label(message);
            GUILayout.EndArea(); Handles.EndGUI();
            if (Profile == null || evt.alt) return;
            if (evt.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            { points.Clear(); message = null; evt.Use(); window.Repaint(); return; }
            if (evt.type == EventType.KeyDown && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
            { Commit(); evt.Use(); return; }
            Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            Vector3 position;
            if (SnapToSurface && Physics.Raycast(ray, out var hit, 100000, ~0, QueryTriggerInteraction.Ignore)) position = hit.point;
            else
            {
                var plane = new Plane(Vector3.up, points.Count == 0 ? Vector3.zero : points[points.Count - 1]);
                if (!plane.Raycast(ray, out float distance)) return;
                position = ray.GetPoint(distance);
            }
            if (evt.control && Grid > 0) position = new Vector3(Mathf.Round(position.x / Grid) * Grid, position.y, Mathf.Round(position.z / Grid) * Grid);
            Handles.color = new Color(0.2f, 0.8f, 1);
            for (int i = 0; i < points.Count; i++)
            {
                Handles.SphereHandleCap(0, points[i], Quaternion.identity, HandleUtility.GetHandleSize(points[i]) * 0.08f, EventType.Repaint);
                if (i > 0) Handles.DrawLine(points[i - 1], points[i]);
            }
            if (points.Count > 0) Handles.DrawDottedLine(points[points.Count - 1], position, 4);
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                if (evt.clickCount > 1) Commit();
                else if (points.Count == 0 || Vector3.Distance(points[points.Count - 1], position) > 0.1f) points.Add(position);
                evt.Use();
            }
            if (evt.type == EventType.MouseMove) window.Repaint();
        }
        private void Commit()
        {
            if (points.Count < 2) { message = "Place at least two distinct points."; return; }
            try
            {
                var road = RoadAuthoringCommands.Create(Profile, points, Network);
                Selection.activeGameObject = road.gameObject; RoadPreview.Rebuild(road);
                points.Clear(); message = null;
            }
            catch (ArgumentException exception) { message = exception.Message; }
        }
    }
}
