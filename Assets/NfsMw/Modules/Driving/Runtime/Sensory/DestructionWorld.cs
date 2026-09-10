using System;
using Unity.Profiling;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum DestructionTier { Cosmetic, WholeBody, Chunks, SetPiece }
    public readonly struct DestructionFact
    {
        public readonly string Id;
        public readonly int Generation;
        public readonly VehicleController Instigator;
        public readonly FeedbackImpact Impact;
        public readonly DestructionTier Tier;
        public DestructionFact(string id, int generation, VehicleController instigator, FeedbackImpact impact, DestructionTier tier)
        { Id = id; Generation = generation; Instigator = instigator; Impact = impact; Tier = tier; }
    }
    public sealed class DestructionLatch
    {
        public bool Broken { get; private set; }
        public int Generation { get; private set; }
        public bool TryBreak(float severity, float threshold)
        {
            if (Broken || !SensoryMath.IsFinite(severity) || !SensoryMath.IsFinite(threshold) || threshold <= 0 || severity < threshold) return false;
            Broken = true; return true;
        }
        public void Reset() { Broken = false; Generation++; }
    }
    /// <summary>Semantic event owner plus bounded cosmetic debris. Pool exhaustion never cancels a gameplay break.</summary>
    public sealed class DestructionWorld : MonoBehaviour
    {
        [SerializeField] private Rigidbody debrisPrefab;
        [SerializeField, Range(0, 32)] private int capacity = 16;
        [SerializeField, Min(0.2f)] private float lifetime = 6;
        [SerializeField] private Transform listener;
        [SerializeField] private SensoryAudioWorld audioWorld;
        [SerializeField] private SensoryEffectsWorld effects;
        [SerializeField] private SensorySurfaceProfile[] materials = Array.Empty<SensorySurfaceProfile>();
        [SerializeField] private Collider[] ignoredVehicleColliders = Array.Empty<Collider>();
        private Rigidbody[] debris;
        private Collider[][] debrisColliders;
        private float[] expiry;
        private int cursor;
        public event Action<DestructionFact> Broken;
        public int ActiveDebris { get; private set; }
        public int Capacity => capacity;
        public int BreaksPublished { get; private set; }
        public void SetIgnoredVehicles(Collider[] colliders) { ignoredVehicleColliders = colliders ?? Array.Empty<Collider>(); }
        private static readonly ProfilerMarker Marker = new ProfilerMarker("Sensory.Destruction");
        public void Configure(Rigidbody template, Transform view, SensoryAudioWorld audio, SensoryEffectsWorld particles, SensorySurfaceProfile[] surfaces)
        { debrisPrefab = template; listener = view; audioWorld = audio; effects = particles; materials = surfaces; }
        private void Awake()
        {
            capacity = Mathf.Clamp(capacity, 0, 32); lifetime = Mathf.Max(0.2f, SensoryMath.Finite(lifetime));
            debris = new Rigidbody[capacity]; expiry = new float[debris.Length];
            debrisColliders = new Collider[debris.Length][];
            if (debrisPrefab == null) return;
            for (int i = 0; i < debris.Length; i++)
            {
                var body = Instantiate(debrisPrefab, transform);
                body.gameObject.layer = 2; body.isKinematic = true; body.gameObject.SetActive(false); debris[i] = body;
                debrisColliders[i] = body.GetComponentsInChildren<Collider>(true);
            }
        }
        public void Publish(DestructionFact fact)
        {
            using (Marker.Auto())
            {
                BreaksPublished++;
                // Listener failures cannot undo the physical break or prevent other subscribers observing it.
                var handlers = Broken;
                if (handlers != null) foreach (Action<DestructionFact> listenerAction in handlers.GetInvocationList())
                    try { listenerAction(fact); } catch (Exception error) { Debug.LogException(error, this); }
                foreach (var surface in materials)
                {
                    if (surface == null || surface.surface != fact.Impact.Material) continue;
                    if (audioWorld != null) audioWorld.Play(surface.destruction, SensoryCategory.Impacts, null, fact.Impact.Point, fact.Impact.Severity, 1, 14);
                    if (effects != null) effects.Emit(surface.impactEffect, fact.Impact.Point, fact.Impact.Normal, Vector3.zero, 20, fact.Impact.Severity);
                    break;
                }
                if (fact.Tier == DestructionTier.Chunks || fact.Tier == DestructionTier.SetPiece) Burst(fact);
            }
        }
        private void Burst(DestructionFact fact)
        {
            if (debris == null || debris.Length == 0 || listener != null && (fact.Impact.Point - listener.position).sqrMagnitude > 100 * 100) return;
            for (int n = 0; n < 4; n++)
            {
                int slot = cursor++ % debris.Length;
                var body = debris[slot]; if (body == null) continue;
                body.gameObject.SetActive(false); body.isKinematic = true;
                body.position = fact.Impact.Point + Vector3.up * (0.2f + n * 0.15f);
                body.rotation = Quaternion.Euler(n * 57, slot * 37, 0); body.gameObject.SetActive(true); body.isKinematic = false;
                foreach (var collider in debrisColliders[slot]) foreach (var vehicle in ignoredVehicleColliders)
                    if (collider != null && vehicle != null) Physics.IgnoreCollision(collider, vehicle);
                body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
                float angle = (slot * 137.5f) * Mathf.Deg2Rad;
                var direction = new Vector3(Mathf.Cos(angle), 1, Mathf.Sin(angle));
                body.AddForce(direction.normalized * Mathf.Lerp(2, 8, fact.Impact.Severity), ForceMode.VelocityChange);
                body.angularVelocity = direction * 4; expiry[slot] = Time.time + lifetime;
            }
        }
        private void FixedUpdate()
        {
            ActiveDebris = 0;
            if (debris == null) return;
            for (int i = 0; i < debris.Length; i++)
            {
                var body = debris[i]; if (body == null || !body.gameObject.activeSelf) continue;
                if (Time.time >= expiry[i] || body.IsSleeping() || listener != null && (body.position - listener.position).sqrMagnitude > 130 * 130)
                { body.isKinematic = true; body.gameObject.SetActive(false); continue; }
                ActiveDebris++;
            }
        }
        private void OnDisable()
        { if (debris != null) foreach (var body in debris) if (body != null) { body.isKinematic = true; body.gameObject.SetActive(false); } ActiveDebris = 0; }
    }


}
