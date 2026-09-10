#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    internal sealed class SensoryAuthoringContent
    {
        public VehicleSensoryProfile vehicle;
        public PoliceSensoryProfile police;
        public SensoryMusicProfile music;
        public FeedbackReplayAsset replay;
        public ParticleSystem[] effects;
        public Material skids;
        public Rigidbody debris;
    }
    internal static class SensoryContentBuilder
    {
        private static T Asset<T>(string path, Action<T> initialize) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) throw new InvalidOperationException("Asset type mismatch: " + path);
            var asset = ScriptableObject.CreateInstance<T>(); initialize(asset); AssetDatabase.CreateAsset(asset, path); return asset;
        }
        public static SensoryAuthoringContent Ensure(string folder)
        {
            var particleMaterial = AssetDatabase.LoadAssetAtPath<Material>(folder + "/DiagnosticParticles.mat");
            if (particleMaterial == null)
            {
                particleMaterial = new Material(Shader.Find("NFS/Sensory Particles"));
                particleMaterial.SetFloat("_EnableBlendModePreserveSpecularLighting", 0);
                UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(particleMaterial);
                particleMaterial.renderQueue = 3000; AssetDatabase.CreateAsset(particleMaterial, folder + "/DiagnosticParticles.mat");
            }
            var smoke = Particle(folder, "DiagnosticSmoke", new Color(0.55f, 0.55f, 0.55f, 0.12f), 0.4f, 1.6f, particleMaterial);
            var dust = Particle(folder, "DiagnosticDust", new Color(0.5f, 0.35f, 0.2f, 0.18f), 0.3f, 1.2f, particleMaterial);
            var spray = Particle(folder, "DiagnosticSpray", new Color(0.5f, 0.7f, 0.85f, 0.25f), 0.08f, 0.5f, particleMaterial);
            var spark = Particle(folder, "DiagnosticSpark", new Color(1, 0.5f, 0.12f, 0.8f), 0.03f, 0.2f, particleMaterial);
            var flame = Particle(folder, "DiagnosticNitrous", new Color(0.2f, 0.4f, 1, 0.3f), 0.12f, 0.2f, particleMaterial);
            var surfaces = new SensorySurfaceProfile[11]; int index = 0;
            foreach (SensorySurface kind in Enum.GetValues(typeof(SensorySurface)))
            {
                if (kind == SensorySurface.Air || kind == SensorySurface.Unknown) continue;
                surfaces[index++] = Asset<SensorySurfaceProfile>(folder + "/" + kind + ".asset", s =>
                {
                    s.surface = kind;
                    bool wet = kind == SensorySurface.AsphaltWet || kind == SensorySurface.Water;
                    bool loose = kind == SensorySurface.Gravel || kind == SensorySurface.Dirt || kind == SensorySurface.Grass;
                    s.cameraRoughness = loose ? .6f : kind == SensorySurface.Concrete ? .18f : .08f;
                    s.grip = wet ? 0.8f : loose ? 0.7f : 1; s.rollingResistance = loose ? 1.8f : 1;
                    s.leavesSkid = kind == SensorySurface.AsphaltDry || kind == SensorySurface.Concrete;
                    s.contactEffect = wet ? spray : loose ? dust : s.leavesSkid ? smoke : null;
                    s.impactEffect = kind == SensorySurface.Metal ? spark : kind == SensorySurface.Wood || kind == SensorySurface.Concrete ? dust : null;
                });
            }
            var impactLibrary = Asset<ImpactMaterialLibrary>(folder + "/ImpactPairs.asset", library =>
            {
                library.pairs = new ImpactMaterialPair[surfaces.Length];
                for (int i = 0; i < surfaces.Length; i++) library.pairs[i] = new ImpactMaterialPair
                    { a = SensorySurface.Metal, b = surfaces[i].surface, particles = surfaces[i].impactEffect };
            });
            var vehicle = Asset<VehicleSensoryProfile>(folder + "/ReferenceVehicle.asset", v =>
            {
                v.surfaces = surfaces; v.nitrousEffect = flame;
                v.engineLayers = new EngineSoundLayer[3];
                for (int i = 0; i < 3; i++)
                {
                    var layer = new EngineSoundLayer { kind = (EngineLayerKind)i, gain = i == 0 ? 0.65f : 0.3f,
                        localPosition = new Vector3(0, 0.4f, i == 0 ? -1.8f : 1.2f), regions = new EngineRpmRegion[5] };
                    for (int r = 0; r < 5; r++) layer.regions[r] = new EngineRpmRegion { rpm = new[] { 900, 1800, 3200, 5000, 7200 }[r] };
                    v.engineLayers[i] = layer;
                }
            });
            if (vehicle.impactMaterials == null) { vehicle.impactMaterials = impactLibrary; EditorUtility.SetDirty(vehicle); }
            var police = Asset<PoliceSensoryProfile>(folder + "/PoliceFeedback.asset", p =>
            {
                p.radio = new PoliceRadioCue[14];
                string[] lines = { "Vehicle observed.", "Pull over and switch off the engine.", "Pursuit active.", "Visual contact lost. Searching.",
                    "Search area cooling down.", "Additional units responding.", "Roadblock deployed.", "Spike strip deployed.",
                    "Unit disabled.", "Heavy collision reported.", "Pursuit ended. Suspect escaped.", "Suspect arrested.", "Fine payment accepted.", "" };
                for (int i = 0; i < 13; i++) p.radio[i] = new PoliceRadioCue { kind = (RadioCueKind)i, subtitle = lines[i], priority = i >= 10 ? 10 : i == 7 ? 20 : 60 };
                Array.Resize(ref p.radio, 13);
            });
            var music = Asset<SensoryMusicProfile>(folder + "/AdaptiveMusic.asset", m =>
            {
                m.stems = new[] {
                    new SensoryMusicStem { intensityGain = AnimationCurve.Linear(0, 0.65f, 1, 0.65f) },
                    new SensoryMusicStem { intensityGain = AnimationCurve.Linear(0, 0, 1, 0.7f) },
                    new SensoryMusicStem { intensityGain = AnimationCurve.EaseInOut(0.5f, 0, 1, 0.8f) }
                };
            });
            var replay = Asset<FeedbackReplayAsset>(folder + "/DiagnosticSequence.asset", MakeReplay);
            var skid = AssetDatabase.LoadAssetAtPath<Material>(folder + "/Skids.mat");
            if (skid == null) { skid = new Material(Shader.Find("NFS/Sensory Skid")); AssetDatabase.CreateAsset(skid, folder + "/Skids.mat"); }
            var debris = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/DiagnosticDebris.prefab");
            if (debris == null)
            {
                var root = GameObject.CreatePrimitive(PrimitiveType.Cube); root.name = "Diagnostic debris"; root.transform.localScale = Vector3.one * 0.16f;
                root.layer = 2; var body = root.AddComponent<Rigidbody>(); body.mass = 0.15f; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                // Cosmetic debris cannot block or strike vehicles; ground-only layer filtering is authored on the prefab.
                foreach (var collider in root.GetComponents<Collider>()) { collider.excludeLayers = 1 << 2; }
                debris = PrefabUtility.SaveAsPrefabAsset(root, folder + "/DiagnosticDebris.prefab"); UnityEngine.Object.DestroyImmediate(root);
            }
            return new SensoryAuthoringContent { vehicle = vehicle, police = police, music = music, replay = replay,
                effects = new[] { smoke, dust, spray, spark, flame }, skids = skid, debris = debris.GetComponent<Rigidbody>() };
        }
        private static ParticleSystem Particle(string folder, string name, Color color, float size, float life, Material material)
        {
            string path = folder + "/" + name + ".prefab";
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (saved != null) return saved.GetComponent<ParticleSystem>();
            var root = new GameObject(name); var system = root.AddComponent<ParticleSystem>(); system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main; main.playOnAwake = false; main.startLifetime = life; main.startSize = size; main.startSpeed = 0;
            main.startColor = color; main.maxParticles = 256; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = system.emission; emission.enabled = false; var shape = system.shape; shape.enabled = false;
            var colorOver = system.colorOverLifetime; colorOver.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) }); colorOver.color = gradient;
            system.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            saved = PrefabUtility.SaveAsPrefabAsset(root, path); UnityEngine.Object.DestroyImmediate(root); return saved.GetComponent<ParticleSystem>();
        }
        private static void MakeReplay(FeedbackReplayAsset asset)
        {
            asset.scenario = "Synthetic diagnostic sequence: load sweep, wheelspin, wet contact, drift, nitrous, airborne, landing. Not measured driving.";
            asset.frames = new VehicleFeedbackFrame[1500];
            var normalizer = new FeedbackNormalizer();
            for (int i = 0; i < asset.frames.Length; i++)
            {
                float time = i * 0.02f, cycle = Mathf.Repeat(time, 10), speed = cycle * 5;
                var f = new VehicleFeedbackFrame { Sequence = i + 1, Epoch = 1 + i / 500, Time = time, WheelCount = 4, BodyMaterial = SensorySurface.Metal,
                    Capabilities = FeedbackCapabilities.Motion | FeedbackCapabilities.Engine | FeedbackCapabilities.Wheels | FeedbackCapabilities.Nitrous,
                    Position = new Vector3(5, 0.7f, -60 + cycle * 3), Rotation = Quaternion.identity, EngineRunning = true,
                    EngineRpm = 900 + cycle * 630, NormalizedRpm = cycle / 10, EngineLoad = cycle < 7 ? 0.8f : -0.15f,
                    Speed = speed, Velocity = Vector3.forward * speed, LocalVelocity = new Vector3(0, cycle > 8 && cycle < 9 ? -5 : 0, speed),
                    SlipAngle = cycle > 5 && cycle < 7 ? 0.5f : 0, Gear = 1 + (int)(cycle / 2), NitrousFlow = cycle > 4 && cycle < 5 ? 1 : 0 };
                for (int w = 0; w < 4; w++) f.SetWheel(w, new WheelFeedback { Front = w < 2, Driven = w >= 2,
                    Grounded = !(cycle > 8 && cycle < 9), Load = 3500, RoadSpeed = speed, LinearSpeed = speed + (cycle < 3 ? 10 : 0),
                    LongitudinalSlip = cycle < 3 ? 0.4f : 0, LateralSlip = f.SlipAngle, Surface = time < 10 ? SensorySurface.AsphaltDry : time < 20 ? SensorySurface.Gravel : SensorySurface.AsphaltWet,
                    Point = f.Position + new Vector3(w % 2 == 0 ? -0.88f : 0.88f, -0.68f, w < 2 ? 1.38f : -1.38f), Normal = Vector3.up });
                asset.frames[i] = normalizer.Step(f, 0.02f);
            }
        }
    }
}
#endif
