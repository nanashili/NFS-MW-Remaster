using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace NfsMwRemaster.Driving
{
    /// <summary>Fixed-size ring mesh; no per-mark objects, decals, materials or render-feature dependency.</summary>
    public sealed class SkidMarkWorld : MonoBehaviour
    {
        [SerializeField, Range(32, 2048)] private int capacity = 512;
        [SerializeField, Min(1)] private float lifetime = 35;
        [SerializeField] private Material material;
        private Mesh mesh;
        private Vector3[] vertices;
        private Color32[] colors;
        private float[] born;
        private byte[] alpha;
        private int cursor;
        private bool dirty;
        private float nextFade;
        public int Capacity => capacity;
        public int SegmentsWritten { get; private set; }
        private static readonly ProfilerMarker Marker = new ProfilerMarker("Sensory.SkidMesh");
        public void Configure(Material value) { material = value; }
        private void Awake()
        {
            capacity = Mathf.Clamp(capacity, 32, 2048);
            vertices = new Vector3[capacity * 4]; colors = new Color32[vertices.Length]; born = new float[capacity]; alpha = new byte[capacity];
            var triangles = new int[capacity * 6]; var uv = new Vector2[vertices.Length];
            for (int i = 0; i < capacity; i++)
            {
                int v = i * 4, t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 2; triangles[t + 4] = v + 3; triangles[t + 5] = v + 1;
                uv[v] = Vector2.zero; uv[v + 1] = Vector2.right; uv[v + 2] = Vector2.up; uv[v + 3] = Vector2.one;
            }
            mesh = new Mesh { name = "Bounded tyre marks" }; mesh.MarkDynamic();
            mesh.vertices = vertices; mesh.colors32 = colors; mesh.uv = uv; mesh.triangles = triangles;
            var filter = gameObject.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        }
        public void Add(Vector3 from, Vector3 to, Vector3 normal, float width, Color tint)
        {
            Vector3 delta = to - from;
            if (mesh == null || delta.sqrMagnitude < 0.025f || delta.sqrMagnitude > 9) return;
            int segment = cursor++ % capacity, at = segment * 4;
            Vector3 side = Vector3.Cross(normal, delta.normalized).normalized * Mathf.Clamp(width, 0.05f, 0.8f) * 0.5f;
            Vector3 lift = normal * 0.015f;
            vertices[at] = transform.InverseTransformPoint(from - side + lift);
            vertices[at + 1] = transform.InverseTransformPoint(from + side + lift);
            vertices[at + 2] = transform.InverseTransformPoint(to - side + lift);
            vertices[at + 3] = transform.InverseTransformPoint(to + side + lift);
            Color32 color = tint;
            for (int i = 0; i < 4; i++) colors[at + i] = color;
            alpha[segment] = color.a; born[segment] = Time.time;
            SegmentsWritten++; dirty = true;
        }
        private void LateUpdate()
        {
            if (mesh == null) return;
            using (Marker.Auto())
            {
                if (Time.time >= nextFade)
                {
                    nextFade = Time.time + 0.1f;
                    for (int i = 0; i < capacity; i++)
                    {
                        byte a = (byte)(alpha[i] * Mathf.Clamp01((lifetime - (Time.time - born[i])) / 5f));
                        for (int j = 0; j < 4; j++) colors[i * 4 + j].a = a;
                    }
                    mesh.SetColors(colors);
                }
                if (!dirty) return;
                dirty = false; mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.RecalculateBounds();
            }
        }
        private void OnDestroy() { if (mesh != null) Destroy(mesh); }
    }
}
