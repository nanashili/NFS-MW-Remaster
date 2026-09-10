using System.IO;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace NfsMwRemaster.Driving
{
    /// <summary>HDRP wave queries constrained to the original game's ocean polygons.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(WaterSurface))]
    public sealed class RockportOcean : MonoBehaviour
    {
        [SerializeField] private TextAsset footprint;
        private WaterSurface surface;
        private Vector3[] triangles;
        private Bounds bounds;
        public WaterSurface Surface => surface ? surface : surface = GetComponent<WaterSurface>();
        public int TriangleCount { get { EnsureFootprint(); return triangles.Length / 3; } }

        public void Configure(TextAsset source) { footprint = source; triangles = null; EnsureFootprint(); }
        private void Awake() => EnsureFootprint();

        private void EnsureFootprint()
        {
            if (triangles != null) return;
            if (!footprint) { triangles = System.Array.Empty<Vector3>(); return; }
            using (var reader = new BinaryReader(new MemoryStream(footprint.bytes)))
            {
                int count = reader.ReadInt32();
                if (count <= 0 || count % 3 != 0 || reader.BaseStream.Length != 4L + count * 12L)
                    throw new InvalidDataException("Invalid original ocean footprint.");
                triangles = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    triangles[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    if (i == 0) bounds = new Bounds(triangles[i], Vector3.zero); else bounds.Encapsulate(triangles[i]);
                }
            }
        }

        public bool Contains(Vector3 worldPoint)
        {
            EnsureFootprint();
            Vector3 point = transform.InverseTransformPoint(worldPoint);
            if (point.x < bounds.min.x || point.x > bounds.max.x || point.z < bounds.min.z || point.z > bounds.max.z) return false;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                double a = Side(triangles[i], triangles[i + 1], point);
                double b = Side(triangles[i + 1], triangles[i + 2], point);
                double c = Side(triangles[i + 2], triangles[i], point);
                if ((a >= -1e-6 && b >= -1e-6 && c >= -1e-6) || (a <= 1e-6 && b <= 1e-6 && c <= 1e-6)) return true;
            }
            return false;
        }

        private static double Side(Vector3 a, Vector3 b, Vector3 p) =>
            ((double)b.x - a.x) * ((double)p.z - a.z) - ((double)b.z - a.z) * ((double)p.x - a.x);

        public bool TrySample(Vector3 point, out float height, out Vector3 normal, out Vector3 current)
        {
            height = 0; normal = Vector3.up; current = Vector3.zero;
            if (!isActiveAndEnabled || !Contains(point) || !Surface.isActiveAndEnabled) return false;
            var search = new WaterSearchParameters
            {
                targetPositionWS = point, startPositionWS = point, error = .01f,
                maxIterations = 12, includeDeformation = true, outputNormal = true
            };
            if (!Surface.ProjectPointOnWaterSurface(search, out var result)) return false;
            height = result.projectedPositionWS.y; normal = result.normalWS;
            current = (Vector3)result.currentDirectionWS * (Surface.largeCurrentSpeedValue / 3.6f);
            return float.IsFinite(height) && float.IsFinite(normal.x) && float.IsFinite(normal.y) && float.IsFinite(normal.z);
        }

        private void OnTriggerEnter(Collider other) => Enroll(other);
        private void OnTriggerStay(Collider other) => Enroll(other);
        private void Enroll(Collider other)
        {
            var body = other.attachedRigidbody;
            if (!body || body.isKinematic || other.isTrigger) return;
            if (!body.TryGetComponent<OceanBuoyantBody>(out var buoyancy)) buoyancy = body.gameObject.AddComponent<OceanBuoyantBody>();
            buoyancy.SetOcean(this);
        }
    }
}
