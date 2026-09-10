using Unity.Profiling;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Prewarmed, world-space particle admission. No prefab creation in contact/impact callbacks.</summary>
    [DefaultExecutionOrder(-200)]
    public sealed class SensoryEffectsWorld : MonoBehaviour
    {
        [SerializeField] private ParticleSystem[] prefabs = System.Array.Empty<ParticleSystem>();
        [SerializeField, Range(1, 24)] private int systemLimit = 16;
        [SerializeField, Range(128, 4096)] private int particleLimit = 2048;
        [SerializeField] private Transform listener;
        private ParticleSystem[] systems;
        private int live;
        private int qualityParticleLimit = int.MaxValue;
        private float qualityDistance = 100;
        public int LiveParticles => live;
        public int ParticleLimit => particleLimit;
        public int DroppedRequests { get; private set; }
        private static readonly ProfilerMarker Marker = new ProfilerMarker("Sensory.Particles");
        public void Configure(ParticleSystem[] effects, Transform view) { prefabs = effects; listener = view; }
        public void ApplyQuality(int budget, float distance)
        {
            qualityParticleLimit=Mathf.Clamp(budget,128,4096);
            qualityDistance=Mathf.Max(1,distance);
            if(systems==null)return;
            int perSystem=Mathf.Max(1,qualityParticleLimit/Mathf.Max(1,systems.Length));
            foreach(var system in systems)
            {
                if(!system)continue;
                var main=system.main;
                main.maxParticles=perSystem;
            }
        }
        private void Awake()
        {
            systemLimit = Mathf.Clamp(systemLimit, 1, 24); particleLimit = Mathf.Clamp(particleLimit, 128, 4096);
            if (prefabs == null) prefabs = System.Array.Empty<ParticleSystem>();
            int count = Mathf.Min(systemLimit, prefabs.Length);
            systems = new ParticleSystem[count];
            for (int i = 0; i < count; i++)
            {
                if (prefabs[i] == null) continue;
                if (prefabs[i].GetComponentsInChildren<ParticleSystem>(true).Length != 1)
                { Debug.LogError("Sensory particle entries must contain one system; nested systems bypass the shared cap.", this); continue; }
                var ps = Instantiate(prefabs[i], transform); ps.name = "Pooled " + prefabs[i].name;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.stopAction = ParticleSystemStopAction.None; main.loop = true; main.prewarm = false;
                main.maxParticles = Mathf.Max(1, Mathf.Min(particleLimit,qualityParticleLimit) / Mathf.Max(1, count));
                var emission = ps.emission; emission.enabled = false;
                var collision = ps.collision; collision.enabled = false;
                var subEmitters = ps.subEmitters; subEmitters.enabled = false;
                systems[i] = ps; ps.Play();
            }
        }
        public bool Emit(ParticleSystem prefab, Vector3 position, Vector3 normal, Vector3 velocity, int count, float intensity)
        {
            if (!isActiveAndEnabled || systems == null || prefab == null || Time.timeScale <= 0 || count <= 0) return false;
            if (listener != null && (position - listener.position).sqrMagnitude > qualityDistance * qualityDistance) return false;
            using (Marker.Auto())
            {
                for (int i = 0; i < systems.Length; i++)
                {
                    if (prefabs[i] != prefab || systems[i] == null) continue;
                    int accepted = Mathf.Min(count, systems[i].main.maxParticles - systems[i].particleCount);
                    if (accepted <= 0) { DroppedRequests++; return false; }
                    var p = new ParticleSystem.EmitParams { position = position, velocity = velocity * 0.12f + normal * (0.3f + SensoryMath.Unit(intensity)),
                        applyShapeToPosition = true };
                    systems[i].Emit(p, Mathf.Min(accepted, 24)); return true;
                }
                DroppedRequests++; return false;
            }
        }
        private void LateUpdate()
        { live = 0; if (systems != null) foreach (var system in systems) if (system != null) live += system.particleCount; }
        private void OnDisable()
        { if (systems != null) foreach (var system in systems) if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); live = 0; }
        private void OnEnable()
        { if (systems != null) foreach (var system in systems) if (system != null) system.Play(); }
    }
}
