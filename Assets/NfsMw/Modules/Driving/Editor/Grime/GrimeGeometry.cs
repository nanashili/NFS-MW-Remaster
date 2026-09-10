using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace NfsMwRemaster.Driving.Editor
{
    public static class GrimeGeometry
    {
        public const string Revision = "grime-mesh-1";
        public static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
        public static string Reference(UnityEngine.Object value) => value == null ? "null" : GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();
        public static string MeshRevision(GrimeReceiver receiver)
        {
            if (receiver == null || receiver.Mesh == null) return "missing";
            var mesh = receiver.Mesh; var path = AssetDatabase.GetAssetPath(mesh);
            // Asset dependency hash covers imported topology. Procedural meshes need their actual geometry hashed.
            string data = string.IsNullOrEmpty(path) ? string.Join(";", mesh.vertices.Select(v => v.ToString("R"))) + string.Join(",", mesh.triangles) : AssetDatabase.GetAssetDependencyHash(path).ToString();
            return Hash128.Compute(Reference(mesh) + mesh.vertexCount + data).ToString();
        }
        public static string BrushRevision(GrimeBrush brush)
        {
            if (brush == null) return "missing";
            string textureHash(Texture2D texture) => texture == null ? "none" : Reference(texture) + AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(texture));
            // Replace texture references before JSON hashing so transient object IDs never define content revisions.
            var copy = ScriptableObject.CreateInstance<GrimeBrush>();
            try { EditorUtility.CopySerialized(brush, copy); copy.colorOpacity = null; copy.normalMap = null; return Hash128.Compute(JsonUtility.ToJson(copy) + textureHash(brush.colorOpacity) + textureHash(brush.normalMap)).ToString(); }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }
        public static bool ReceiverAllowed(GrimeCanvas canvas, GrimeReceiver receiver, out string failure)
        {
            var pipeline=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if(!(pipeline is UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset)) { failure="This backend requires HDRP."; return false; }
            failure = "Receiver needs an enabled explicit GrimeReceiver, MeshCollider and Renderer.";
            if (receiver == null || string.IsNullOrEmpty(receiver.id) || !receiver.allowDressing || !receiver.isActiveAndEnabled || receiver.surface == null || !receiver.surface.enabled || receiver.surface.isTrigger || receiver.surface.convex || receiver.surfaceRenderer == null || !receiver.surfaceRenderer.enabled || receiver.Mesh == null) return false;
            if(!receiver.Mesh.isReadable) { failure="Receiver mesh needs CPU Read/Write access for stable triangle anchors."; return false; }
            if (!(receiver.surfaceRenderer is MeshRenderer) || receiver.surfaceRenderer.GetComponent<MeshFilter>()?.sharedMesh != receiver.Mesh) { failure = "Receiver renderer and collider must use the same static mesh."; return false; }
            if (receiver.surface.transform != receiver.surfaceRenderer.transform) { failure = "Collider and renderer must share the receiver transform."; return false; }
            if (receiver.surface.GetComponentInParent<Rigidbody>() != null || receiver.surface.GetComponentInParent<VehicleController>() != null) { failure = "Moving bodies and vehicles cannot receive static dressing."; return false; }
            if ((canvas.physicsLayers.value & (1 << receiver.surface.gameObject.layer)) == 0 || (canvas.renderingLayers & receiver.surfaceRenderer.renderingLayerMask) == 0) { failure = "Receiver excluded by physics/rendering-layer filter."; return false; }
            if (!string.IsNullOrEmpty(canvas.requiredCategory) && receiver.surfaceCategory != canvas.requiredCategory) { failure = "Receiver surface category is excluded."; return false; }
            var materials = receiver.surfaceRenderer.sharedMaterials;
            if (materials.Length == 0 || materials.Any(m => m == null || m.renderQueue >= 3000 || (m.HasProperty("_SurfaceType") && m.GetFloat("_SurfaceType") > .5f))) { failure = "Transparent or missing receiver material is unsupported."; return false; }
            if ((receiver.Mesh.subMeshCount > 1 || materials.Length > 1) && !receiver.approvedAllSubmeshes) { failure = "Mixed-material receivers require explicit approval of every submesh."; return false; }
            if (canvas.requiredMaterial != null && materials.Any(m => m != canvas.requiredMaterial)) { failure = "Receiver does not match the required material on every submesh."; return false; }
            failure = ""; return true;
        }
        public static GrimeAnchor Capture(GrimeReceiver receiver, RaycastHit hit, Vector3 tangent)
            => new GrimeAnchor { kind = GrimeAnchorKind.Mesh, receiver = receiver, receiverId = receiver.id, meshRevision = MeshRevision(receiver), triangle = hit.triangleIndex, barycentric = hit.barycentricCoordinate, position = hit.point, normal = hit.normal, tangent = Vector3.ProjectOnPlane(tangent, hit.normal).normalized };
        public static bool Resolve(GrimeAnchor anchor, out Vector3 position, out Vector3 normal, out Vector3 tangent, out string failure, string knownMeshRevision = null, Vector3[] knownVertices = null, int[] knownTriangles = null)
        {
            if(anchor==null) { position=normal=tangent=Vector3.zero; failure="Missing anchor."; return false; }
            position = anchor.position; normal = anchor.normal; tangent = anchor.tangent; failure = "";
            if (anchor.receiver == null || anchor.receiver.id != anchor.receiverId) { failure = "Receiver identity is unresolved; remap explicitly."; return false; }
            if (anchor.kind == GrimeAnchorKind.Road)
            {
                if (anchor.network == null || anchor.network.SchemaVersion != RoadNetworkAsset.CurrentSchema || anchor.roadRevision != anchor.network.Fingerprint) { failure = "Road revision changed; preview remapping before accepting."; return false; }
                var lane = anchor.network.Lanes.FirstOrDefault(l => l.Id.ToString() == anchor.laneId);
                if (lane == null || !float.IsFinite(anchor.lateral) || !float.IsFinite(anchor.station) || anchor.station < 0 || anchor.station > lane.Length) { failure = "Lane/station removed or out of range; no nearest-lane remap."; return false; }
                var sample = lane.Sample(anchor.station); position = sample.position + sample.left * anchor.lateral; normal = sample.up; tangent = sample.forward;
            }
            else if (anchor.kind == GrimeAnchorKind.Mesh)
            {
                if (anchor.meshRevision != (knownMeshRevision ?? MeshRevision(anchor.receiver))) { failure = "Mesh topology is stale; triangle anchors cannot be reused."; return false; }
                var mesh = anchor.receiver.Mesh; var triangles = knownTriangles ?? mesh.triangles;
                int i = anchor.triangle * 3;
                if (i < 0 || i + 2 >= triangles.Length || !Finite(anchor.barycentric) || Mathf.Abs(anchor.barycentric.x + anchor.barycentric.y + anchor.barycentric.z - 1) > .001f || anchor.barycentric.x < -.00001f || anchor.barycentric.y < -.00001f || anchor.barycentric.z < -.00001f) { failure = "Invalid triangle/barycentric anchor."; return false; }
                var vertices = knownVertices ?? mesh.vertices; Vector3 a = vertices[triangles[i]], b = vertices[triangles[i+1]], c = vertices[triangles[i+2]];
                var transform = anchor.receiver.surface.transform;
                position = transform.TransformPoint(a * anchor.barycentric.x + b * anchor.barycentric.y + c * anchor.barycentric.z);
                normal = transform.localToWorldMatrix.inverse.transpose.MultiplyVector(Vector3.Cross(b-a, c-a)).normalized;
                tangent = Vector3.ProjectOnPlane(anchor.tangent, normal).normalized;
            }
            else if (anchor.kind != GrimeAnchorKind.World) { failure = "Unknown anchor schema."; return false; }
            if (!Finite(position) || !Finite(normal) || normal.sqrMagnitude < .5f || !Finite(tangent)) { failure = "Non-finite or degenerate anchor."; return false; }
            if (tangent.sqrMagnitude < .01f) tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < .9f ? Vector3.up : Vector3.right).normalized;
            return true;
        }
        public static List<Vector3> Resample(IReadOnlyList<Vector3> points, float spacing)
        {
            if (!float.IsFinite(spacing) || spacing < .01f) throw new ArgumentOutOfRangeException(nameof(spacing));
            var result = new List<Vector3>(); if (points.Count == 0) return result;
            if (points.Any(p => !Finite(p))) throw new ArgumentException("Non-finite stroke path.");
            result.Add(points[0]); float remaining = spacing;
            for (int i = 1; i < points.Count; i++)
            {
                var cursor = points[i-1]; var direction = points[i] - cursor; float length = direction.magnitude;
                if (length < .00001f) continue; direction /= length;
                while (length >= remaining)
                {
                    cursor += direction * remaining; result.Add(cursor); length -= remaining; remaining = spacing;
                    if (result.Count > 50000) throw new InvalidOperationException("Stroke exceeds 50,000 spatial samples.");
                }
                remaining -= length;
            }
            return result;
        }
        public static float Noise(int seed, string id, int index, int channel)
        {
            unchecked
            {
                uint hash = 2166136261; foreach (char c in id) hash = (hash ^ c) * 16777619;
                hash ^= (uint)seed + (uint)index * 374761393 + (uint)channel * 668265263;
                hash = (hash ^ (hash >> 13)) * 1274126177; return (hash ^ (hash >> 16)) / (float)uint.MaxValue;
            }
        }
    }
}
