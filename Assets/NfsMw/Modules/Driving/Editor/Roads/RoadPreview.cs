using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Splines;
using UnityEngine;
using UnityEngine.Splines;

namespace NfsMwRemaster.Driving.Editor
{
    [InitializeOnLoad]
    public static class RoadPreview
    {
        private sealed class Preview : IDisposable
        {
            public GameObject root;
            public RoadMeshBuild build;
            public Matrix4x4 sourceMatrix;
            public readonly List<GameObject> hiddenBaked = new List<GameObject>();
            public void Dispose()
            {
                foreach (var chunk in hiddenBaked) if (chunk != null) SceneVisibilityManager.instance.Show(chunk, false);
                if (root != null) UnityEngine.Object.DestroyImmediate(root); build?.Dispose();
            }
        }
        private static readonly Dictionary<RoadAuthoring, Preview> previews = new Dictionary<RoadAuthoring, Preview>();
        private static readonly HashSet<RoadAuthoring> pending = new HashSet<RoadAuthoring>();
        private static readonly Dictionary<RoadAuthoring, string> errors = new Dictionary<RoadAuthoring, string>();
        private static double nextUpdate;
        private static Material fallback;
        static RoadPreview()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.quitting += Clear;
            EditorApplication.playModeStateChanged += _ => Clear();
            Undo.undoRedoPerformed += InvalidateVisible;
            Undo.postprocessModifications += Modified;
            EditorSplineUtility.AfterSplineWasModified += SplineChanged;
        }
        public static string Error(RoadAuthoring road) => road != null && errors.TryGetValue(road, out var error) ? error : null;
        public static void Invalidate(RoadAuthoring road) { if (road != null && !EditorApplication.isPlayingOrWillChangePlaymode) pending.Add(road); }
        public static void Remove(RoadAuthoring road)
        {
            if (previews.TryGetValue(road, out var old)) { old.Dispose(); previews.Remove(road); }
            pending.Remove(road); errors.Remove(road);
        }
        public static void Rebuild(RoadAuthoring road)
        {
            if (road == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            Remove(road);
            var preview = new Preview { sourceMatrix = road.transform.localToWorldMatrix };
            try
            {
                preview.build = RoadMeshBuilder.Build(road);
                preview.root = new GameObject("Road preview") { hideFlags = HideFlags.HideAndDontSave };
                foreach (var chunk in preview.build.Chunks)
                {
                    var go = new GameObject(road.Profile.bands[chunk.BandIndex].label) { hideFlags = HideFlags.HideAndDontSave };
                    go.transform.SetParent(preview.root.transform, false); go.transform.position = chunk.Origin;
                    go.AddComponent<MeshFilter>().sharedMesh = chunk.Mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = road.Profile.bands[chunk.BandIndex].material ?? FallbackMaterial();
                }
                var network = road.GetComponentInParent<RoadNetworkAuthoring>();
                if (network != null)
                    foreach (var chunk in network.GetComponentsInChildren<RoadGeneratedChunk>())
                        if (chunk.Asset != null && chunk.Index >= 0 && chunk.Index < chunk.Asset.Chunks.Count
                            && chunk.Asset.Chunks[chunk.Index].RoadId == road.Id && !SceneVisibilityManager.instance.IsHidden(chunk.gameObject, false))
                        { preview.hiddenBaked.Add(chunk.gameObject); SceneVisibilityManager.instance.Hide(chunk.gameObject, false); }
                previews.Add(road, preview);
            }
            catch (ArgumentException exception) { preview.Dispose(); errors[road] = exception.Message; }
            catch { preview.Dispose(); throw; }
            SceneView.RepaintAll();
        }
        private static Material FallbackMaterial()
        {
            if (fallback != null) return fallback;
            var shader = Shader.Find("HDRP/Lit");
            if (shader == null) throw new ArgumentException("ROAD_MATERIAL: HDRP Lit is unavailable; assign a compatible material.");
            fallback = new Material(shader) { name = "Road preview", hideFlags = HideFlags.HideAndDontSave };
            fallback.SetColor("_BaseColor", new Color(0.16f, 0.18f, 0.20f));
            return fallback;
        }
        private static void Update()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup < nextUpdate) return;
            nextUpdate = EditorApplication.timeSinceStartup + 0.1;
            var visible = new List<RoadAuthoring>(previews.Keys);
            foreach (var road in visible)
            {
                if (road == null || !road.isActiveAndEnabled) Remove(road);
                else if (previews[road].sourceMatrix != road.transform.localToWorldMatrix) Invalidate(road);
            }
            pending.RemoveWhere(road => road == null);
            if (pending.Count == 0) return;
            var dirty = new List<RoadAuthoring>(pending); pending.Clear();
            foreach (var road in dirty) if (road != null) Rebuild(road);
        }
        private static void SplineChanged(Spline spline)
        {
            foreach (var road in UnityEngine.Object.FindObjectsByType<RoadAuthoring>(FindObjectsInactive.Exclude))
                if (road.Reference != null && road.Reference.Spline == spline) Invalidate(road);
        }
        private static void InvalidateVisible()
        {
            var roads = new List<RoadAuthoring>(previews.Keys);
            foreach (var road in roads) { Remove(road); if (road != null) Invalidate(road); }
        }
        private static UndoPropertyModification[] Modified(UndoPropertyModification[] changes)
        {
            foreach (var change in changes)
            {
                var target = change.currentValue.target;
                if (target is RoadAuthoring road) Invalidate(road);
                else if (target is RoadProfile profile)
                    foreach (var visible in previews.Keys) if (visible != null && visible.Profile == profile) Invalidate(visible);
            }
            return changes;
        }
        private static void Clear()
        {
            foreach (var preview in previews.Values) preview.Dispose();
            previews.Clear(); pending.Clear(); errors.Clear();
            if (fallback != null) UnityEngine.Object.DestroyImmediate(fallback);
            fallback = null;
        }
    }
}
