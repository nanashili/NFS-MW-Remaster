using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using UnityEditor.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    public static class RoadAuthoringCommands
    {
        public static int ConnectMatchingEnds(RoadNetworkAuthoring source)
        {
            var candidates = RoadNetworkBake.MatchingConnections(source);
            var connections = new List<RoadLaneConnection>(source.connections);
            var existing = new HashSet<string>();
            foreach (var link in connections) existing.Add(link.from + ":" + link.to);
            foreach (var link in candidates) if (existing.Add(link.from + ":" + link.to)) connections.Add(link);
            int added = connections.Count - source.connections.Length;
            if (added == 0) return 0;
            Undo.IncrementCurrentGroup(); Undo.RecordObject(source, "Connect road lane ends");
            source.connections = connections.ToArray(); Changed(source); return added;
        }

        public static void AddRoads(RoadNetworkAuthoring network, RoadAuthoring[] roads)
        {
            if (network == null || roads == null) throw new ArgumentException("Choose a network and roads.");
            var combined = new List<RoadAuthoring>(network.Roads);
            foreach (var road in roads)
            {
                if (road == null || road.gameObject.scene != network.gameObject.scene || road.GetComponentInParent<RoadNetworkAuthoring>() != null)
                    throw new ArgumentException("Add unassigned roads from the network's scene.");
                if (!combined.Contains(road)) combined.Add(road);
            }
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.RecordObject(network, "Add roads to network");
            foreach (var road in roads) Undo.SetTransformParent(road.transform, network.transform, "Add road to network");
            network.Initialize(combined.ToArray()); Changed(network); Undo.CollapseUndoOperations(group);
        }

        public static RoadNetworkAuthoring CreateNetwork(RoadAuthoring[] roads)
        {
            if (roads == null || roads.Length == 0 || roads[0] == null) throw new ArgumentException("Select roads for the network.");
            var scene = roads[0].gameObject.scene;
            var unique = new HashSet<RoadAuthoring>();
            foreach (var road in roads)
                if (road == null || !unique.Add(road) || road.gameObject.scene != scene || road.GetComponentInParent<RoadNetworkAuthoring>() != null)
                    throw new ArgumentException("Use unassigned roads from one scene.");
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            var root = new GameObject("Road Network");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var network = root.AddComponent<RoadNetworkAuthoring>(); network.Initialize(roads);
            Undo.RegisterCreatedObjectUndo(root, "Create road network");
            foreach (var road in roads) Undo.SetTransformParent(road.transform, root.transform, "Add road to network");
            Undo.CollapseUndoOperations(group); Changed(network); return network;
        }

        public static void InsertKnot(RoadAuthoring road, int curveIndex, float t)
        {
            RoadGeometry.Validate(road);
            var spline = road.Reference.Spline;
            if (curveIndex < 0 || curveIndex >= spline.Count - 1 || !RoadGeometry.Finite(t) || t <= 0 || t >= 1)
                throw new ArgumentOutOfRangeException(nameof(curveIndex));
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(road.Reference, "Insert road knot");
            var curve = spline.GetCurve(curveIndex);
            float3 a = math.lerp(curve.P0, curve.P1, t), b = math.lerp(curve.P1, curve.P2, t), c = math.lerp(curve.P2, curve.P3, t);
            float3 d = math.lerp(a, b, t), e = math.lerp(b, c, t), p = math.lerp(d, e, t);
            spline.SetTangentMode(curveIndex, TangentMode.Broken);
            spline.SetTangentMode(curveIndex + 1, TangentMode.Broken);
            var first = spline[curveIndex]; var last = spline[curveIndex + 1];
            first.TangentOut = math.rotate(math.inverse(first.Rotation), a - curve.P0);
            last.TangentIn = math.rotate(math.inverse(last.Rotation), c - curve.P3);
            spline[curveIndex] = first; spline[curveIndex + 1] = last;
            spline.Insert(curveIndex + 1, new BezierKnot(p, d - p, e - p), TangentMode.Broken);
            Changed(road.Reference);
        }

        public static void ApplyProfile(RoadAuthoring road, RoadProfile profile)
        {
            Undo.RecordObject(road, "Apply road profile");
            road.Initialize(profile, road.GetComponent<SplineContainer>());
            Changed(road);
        }

        internal static void Changed(Component component)
        {
            EditorUtility.SetDirty(component);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            if (component.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
            RoadPreview.Invalidate(component.GetComponent<RoadAuthoring>());
        }

        public static RoadAuthoring Create(RoadProfile profile, IReadOnlyList<Vector3> points, RoadNetworkAuthoring network = null)
        {
            if (profile == null || points == null || points.Count < 2) throw new ArgumentException("Choose a road profile and at least two points.");
            var root = new GameObject("Road — " + profile.name);
            try
            {
                root.transform.position = points[0];
                var container = root.AddComponent<SplineContainer>();
                var spline = new Spline();
                foreach (var point in points) spline.Add(new BezierKnot(point - points[0]), TangentMode.AutoSmooth);
                container.Spline = spline;
                var road = root.AddComponent<RoadAuthoring>();
                road.Initialize(profile, container);
                RoadGeometry.Evaluate(road);
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                Undo.RegisterCreatedObjectUndo(root, "Create road");
                if (network != null)
                {
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, network.gameObject.scene);
                    AddRoads(network, new[] { road });
                }
                Undo.CollapseUndoOperations(group);
                return road;
            }
            catch { UnityEngine.Object.DestroyImmediate(root); throw; }
        }
    }
}
