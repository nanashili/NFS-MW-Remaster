using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Eight submerged-volume samples apply buoyancy and drag through PhysX.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
    public sealed class OceanBuoyantBody : MonoBehaviour
    {
        private const float SeaWaterDensity = 1025;
        [SerializeField] private RockportOcean ocean;
        [SerializeField, Min(.001f)] private float displacedVolume;
        [SerializeField, Range(0, 2)] private float dragCoefficient = 1.05f;
        [SerializeField] private Bounds localBounds;
        private Rigidbody body;
        private readonly Vector3[] samples = new Vector3[8];
        public float SubmergedFraction { get; private set; }
        public Vector3 LastBuoyancyForce { get; private set; }
        public int SuccessfulWaveQueries { get; private set; }
        public float DisplacedVolume => displacedVolume;

        public void SetOcean(RockportOcean value) => ocean = value;
        public void Configure(RockportOcean water, Bounds shape, float cubicMetres)
        {
            ocean = water; localBounds = shape; displacedVolume = Mathf.Max(.001f, cubicMetres); BuildSamples();
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (localBounds.size.sqrMagnitude < .0001f) MeasureShape();
            BuildSamples();
        }

        private void MeasureShape()
        {
            bool found = false;
            foreach (var collider in GetComponentsInChildren<Collider>())
            {
                if (collider.isTrigger || collider.attachedRigidbody != body || collider is WheelCollider) continue;
                Bounds shape = collider.bounds;
                bool localShape = false;
                if (collider is BoxCollider box) { shape = new Bounds(box.center, box.size); localShape = true; }
                else if (collider is MeshCollider mesh && mesh.sharedMesh) { shape = mesh.sharedMesh.bounds; localShape = true; }
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = shape.center + Vector3.Scale(shape.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = transform.InverseTransformPoint(localShape ? collider.transform.TransformPoint(corner) : corner);
                    if (!found) { localBounds = new Bounds(local, Vector3.zero); found = true; } else localBounds.Encapsulate(local);
                }
            }
            if (!found) { enabled = false; return; }
            // A conservative effective hull volume; author Configure for a known shape.
            if (displacedVolume <= 0) displacedVolume = localBounds.size.x * localBounds.size.y * localBounds.size.z * Mathf.Abs(transform.lossyScale.x * transform.lossyScale.y * transform.lossyScale.z) * .65f;
        }

        private void BuildSamples()
        {
            for (int i = 0; i < 8; i++) samples[i] = localBounds.center + Vector3.Scale(localBounds.size, new Vector3((i & 1) == 0 ? -.25f : .25f, (i & 2) == 0 ? -.25f : .25f, (i & 4) == 0 ? -.25f : .25f));
        }

        public static float Submersion(float sampleHeight, float waterHeight, float cellHeight) =>
            Mathf.Clamp01((waterHeight - sampleHeight) / Mathf.Max(.001f, cellHeight) + .5f);

        public static Vector3 Buoyancy(float volume, float submerged, Vector3 gravity) =>
            -gravity * (SeaWaterDensity * Mathf.Max(0, volume) * Mathf.Clamp01(submerged));

        private void FixedUpdate()
        {
            SubmergedFraction = 0; LastBuoyancyForce = Vector3.zero; SuccessfulWaveQueries = 0;
            if (!body || body.isKinematic || !ocean || !ocean.isActiveAndEnabled) return;
            Vector3 size = localBounds.size;
            float cellHeight = .5f * (Mathf.Abs(transform.TransformVector(Vector3.right * size.x).y) + Mathf.Abs(transform.TransformVector(Vector3.up * size.y).y) + Mathf.Abs(transform.TransformVector(Vector3.forward * size.z).y));
            float sampleVolume = displacedVolume / 8;
            float sampleArea = Mathf.Pow(displacedVolume, 2f / 3f) / 8;
            for (int i = 0; i < samples.Length; i++)
            {
                Vector3 point = transform.TransformPoint(samples[i]);
                if (!ocean.TrySample(point, out float height, out _, out Vector3 current)) continue;
                SuccessfulWaveQueries++;
                float wet = Submersion(point.y, height, cellHeight); SubmergedFraction += wet / 8;
                if (wet <= 0) continue;
                Vector3 lift = Buoyancy(sampleVolume, wet, Physics.gravity); LastBuoyancyForce += lift;
                Vector3 relativeVelocity = body.GetPointVelocity(point) - current;
                Vector3 drag = -.5f * SeaWaterDensity * dragCoefficient * sampleArea * wet * relativeVelocity.magnitude * relativeVelocity;
                // Bound each sample's drag impulse to avoid reversing a fast impact in one step.
                drag = Vector3.ClampMagnitude(drag, body.mass * relativeVelocity.magnitude / (8 * Time.fixedDeltaTime));
                body.AddForceAtPosition(lift + drag, point, ForceMode.Force);
            }
        }
    }
}
