using System;
using System.Collections.Generic;
using NfsMwRemaster.Driving.Editor.Workspace;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Static geometry preview: only shared meshes/materials cross this seam, never source scripts.
    /// Skinned meshes use their authored bind pose. This is not suspension or physics simulation.
    /// Prepare explicitly; drawing does not scan the source hierarchy or AssetDatabase.
    /// </summary>
    public sealed class VehicleProfileMeshPreview : IDisposable
    {
        private struct DrawItem { public Mesh Mesh; public Material Material; public Matrix4x4 Matrix; public int Submesh; }
        private readonly List<DrawItem> draws = new List<DrawItem>();
        private PreviewRenderUtility preview;
        private RacingPreviewLease lease;
        private Bounds bounds;
        private Vector2 orbit = new Vector2(135, 15), pan;
        private float zoom = 1;
        private double lastRender;
        private Texture cached;
        private Vector2 lastSize;
        private bool dirty = true;
        public int DrawCount => draws.Count;
        public bool IsDisposed => preview == null;

        public VehicleProfileMeshPreview(GameObject source, int lod = 0)
        {
            if (source == null || !AssetDatabase.Contains(source)) throw new ArgumentException("Choose a persistent model or prefab asset.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Static preview is available in Edit Mode.");
            try
            {
                lease = RacingPreviewSessions.Acquire("vehicle-profile-static-geometry", "Vehicle Profiles", Dispose);
                var excluded = new HashSet<Renderer>();
                var included = new HashSet<Renderer>();
                foreach (var group in source.GetComponentsInChildren<LODGroup>(true))
                {
                    var levels = group.GetLODs();
                    int selected = Mathf.Clamp(lod, 0, Math.Max(0, levels.Length - 1));
                    for (int i = 0; i < levels.Length; i++) foreach (var renderer in levels[i].renderers)
                        if (renderer != null) (i == selected ? included : excluded).Add(renderer);
                }
                excluded.ExceptWith(included);
                bool first = true;
                foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.enabled || excluded.Contains(renderer)) continue;
                    // Root activation is irrelevant to an asset, but disabled child assemblies stay hidden.
                    bool active = true;
                    for (var t = renderer.transform; t != source.transform && t != null; t = t.parent)
                        active &= t.gameObject.activeSelf;
                    if (!active) continue;
                    Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) continue;
                    var matrix = source.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                    var local = mesh.bounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var point = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                            new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                        if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
                    }
                    var materials = renderer.sharedMaterials;
                    for (int sub = 0; sub < Math.Min(mesh.subMeshCount, materials.Length); sub++)
                        if (materials[sub] != null) draws.Add(new DrawItem { Mesh = mesh, Material = materials[sub], Matrix = matrix, Submesh = sub });
                }
                if (draws.Count == 0) throw new ArgumentException("No visible mesh/material bindings at this LOD.");
                preview = new PreviewRenderUtility();
                preview.camera.fieldOfView = 35;
                preview.camera.nearClipPlane = 0.01f;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.ambientColor = new Color(.35f, .35f, .35f);
            }
            catch { Dispose(); throw; }
        }

        public void Draw(Rect rect, Action repaint)
        {
            if (IsDisposed || rect.width < 1 || rect.height < 1) return;
            Event e = Event.current;
            if (rect.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDrag)
                {
                    if (e.button == 0) { orbit.x += e.delta.x; orbit.y = Mathf.Clamp(orbit.y + e.delta.y, -85, 85); }
                    else pan += e.delta / Mathf.Max(rect.height, 1);
                    dirty = true; e.Use(); repaint();
                }
                else if (e.type == EventType.ScrollWheel)
                { zoom = Mathf.Clamp(zoom * Mathf.Exp(e.delta.y * .04f), .25f, 8); dirty = true; e.Use(); repaint(); }
                else if (e.type == EventType.MouseUp && dirty) { cached = null; repaint(); }
            }
            if (e.type != EventType.Repaint) return;
            if (rect.size != lastSize) dirty = true;
            if (dirty && (cached == null || EditorApplication.timeSinceStartup - lastRender >= .05))
            {
                float radius = Mathf.Max(.1f, bounds.extents.magnitude);
                Quaternion rotation = Quaternion.Euler(orbit.y, orbit.x, 0);
                var target = bounds.center + rotation * new Vector3(-pan.x, pan.y, 0) * radius * 2;
                preview.camera.transform.SetPositionAndRotation(target - rotation * Vector3.forward * radius * 3.5f * zoom, rotation);
                preview.camera.farClipPlane = radius * 50 + 10;
                preview.BeginPreview(rect, GUIStyle.none);
                try
                {
                    foreach (var draw in draws)
                        if (draw.Mesh != null && draw.Material != null) preview.RenderMesh(draw.Mesh, draw.Matrix, draw.Material, draw.Submesh);
                    preview.Render(true);
                }
                finally { cached = preview.EndPreview(); }
                lastSize = rect.size; lastRender = EditorApplication.timeSinceStartup; dirty = false;
            }
            if (cached != null) GUI.DrawTexture(rect, cached, ScaleMode.StretchToFill, false);
        }

        public void Dispose()
        {
            preview?.Cleanup(); preview = null; cached = null;
            draws.Clear(); lease?.Dispose(); lease = null;
        }
    }
}
