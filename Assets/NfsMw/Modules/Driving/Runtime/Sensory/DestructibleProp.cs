using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [DisallowMultipleComponent]
    public sealed class DestructibleProp : MonoBehaviour
    {
        [SerializeField] private string stableId;
        [SerializeField] private DestructionWorld world;
        [SerializeField] private DestructionTier tier = DestructionTier.Chunks;
        [SerializeField, Range(0.01f, 1)] private float threshold = 0.25f;
        [SerializeField] private SensorySurface material = SensorySurface.Wood;
        [SerializeField] private GameObject intactVisual, brokenSetPiece;
        [SerializeField] private Collider[] blocking = System.Array.Empty<Collider>();
        [SerializeField] private Rigidbody wholeBody;
        private readonly DestructionLatch latch = new DestructionLatch();
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private bool initialKinematic;
        public bool IsBroken => latch.Broken;
        public string StableId => stableId;
        public void Configure(string id, DestructionWorld events, GameObject intact, Collider[] colliders, SensorySurface surface, DestructionTier type = DestructionTier.Chunks)
        { stableId = id; world = events; intactVisual = intact; blocking = colliders; material = surface; tier = type; }
        public void ConfigureWholeBody(string id, DestructionWorld events, GameObject intact, Collider[] colliders, Rigidbody body, SensorySurface surface)
        {
            Configure(id, events, intact, colliders, surface, DestructionTier.WholeBody);
            wholeBody = body;
        }
        private void Awake()
        {
            initialPosition = transform.position; initialRotation = transform.rotation;
            initialKinematic = wholeBody == null || wholeBody.isKinematic;
            if (brokenSetPiece != null) brokenSetPiece.SetActive(false);
            if (string.IsNullOrWhiteSpace(stableId) || intactVisual == gameObject || world == null
                || tier == DestructionTier.WholeBody && wholeBody == null || tier == DestructionTier.SetPiece && brokenSetPiece == null)
            { Debug.LogError("Destructible requires a unique ID, world, child visual and authored tier content.", this); enabled = false; }
        }
        private void OnCollisionEnter(Collision collision)
        {
            if (!isActiveAndEnabled || latch.Broken || collision.contactCount == 0) return;
            var instigator = collision.collider.GetComponentInParent<VehicleController>();
            if (instigator == null || instigator.Body == null) return;
            var contact = collision.GetContact(0);
            var impact = new FeedbackImpact { Point = contact.point, Normal = contact.normal, Material = material,
                NormalSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal)), Impulse = collision.impulse.magnitude };
            impact.Severity = ImpactClassifier.Severity(impact.NormalSpeed, impact.Impulse, instigator.Body.mass);
            ApplyImpact(impact, instigator);
        }
        public bool ApplyImpact(FeedbackImpact impact, VehicleController instigator)
        {
            if (!isActiveAndEnabled || world == null || !latch.TryBreak(impact.Severity, threshold)) return false;
            if (tier == DestructionTier.WholeBody)
            {
                wholeBody.isKinematic = false;
                wholeBody.AddForceAtPosition(-impact.Normal * Mathf.Min(impact.Impulse, wholeBody.mass * 12), impact.Point, ForceMode.Impulse);
            }
            else
            {
                if (intactVisual != null) intactVisual.SetActive(false);
                foreach (var collider in blocking) if (collider != null) collider.enabled = false;
                if (brokenSetPiece != null) brokenSetPiece.SetActive(true);
            }
            impact.Material = material;
            world.Publish(new DestructionFact(stableId, latch.Generation, instigator, impact, tier)); return true;
        }
        public void ResetProp()
        {
            latch.Reset(); transform.SetPositionAndRotation(initialPosition, initialRotation);
            if (wholeBody != null) { wholeBody.isKinematic = false; wholeBody.linearVelocity = Vector3.zero; wholeBody.angularVelocity = Vector3.zero; wholeBody.isKinematic = initialKinematic; }
            foreach (var collider in blocking) if (collider != null) collider.enabled = true;
            if (intactVisual != null) intactVisual.SetActive(true);
            if (brokenSetPiece != null) brokenSetPiece.SetActive(false);
        }
    }
}
