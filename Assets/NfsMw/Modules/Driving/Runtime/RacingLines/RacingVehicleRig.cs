using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving
{
    // A narrow fixture adapter, not another vehicle model. Only the shared controller applies forces.
    public sealed class RacingVehicleRig : IDisposable
    {
        public GameObject Root { get; private set; }
        public VehicleController Vehicle { get; private set; }
        public RacingSimulationInput Input { get; private set; }
        public BoxCollider Chassis { get; private set; }
        public VehicleAssists Assists { get; private set; }
        public Rigidbody Body => Vehicle.Body;
        private RacingTuningLease tuning;

        public RacingVehicleRig(Scene scene, RacingVehicleSetup setup, Vector3 position, Quaternion rotation, bool manual = true)
        {
            RacingLineSnapshot.ValidateSetup(setup);
            if (!scene.IsValid() || manual && scene.GetPhysicsScene() == Physics.defaultPhysicsScene)
                throw new ArgumentException("LINE_RIG_SCENE: use an isolated physics scene for manual stepping.");
            try
            {
                tuning = new RacingTuningLease(setup.CreateEffectiveTuning());
                if (setup.physicsPrefab != null)
                {
                    ValidatePrefab(setup.physicsPrefab, setup.dimensions);
                    // An inactive parent prevents Awake/OnEnable during cloning, even for an active prefab.
                    var staging = new GameObject("Line rig staging"); staging.SetActive(false);
                    SceneManager.MoveGameObjectToScene(staging, scene);
                    try
                    {
                        Root = UnityEngine.Object.Instantiate(setup.physicsPrefab.gameObject, staging.transform);
                        Root.SetActive(false); Root.transform.SetParent(null);
                    }
                    finally { Destroy(staging); }
                    Vehicle = Root.GetComponent<VehicleController>();
                }
                else
                {
                    Root = new GameObject("Shared physics calibration rig"); Root.SetActive(false);
                    SceneManager.MoveGameObjectToScene(Root, scene);
                    Vehicle = Root.AddComponent<VehicleController>();
                    var box = Root.AddComponent<BoxCollider>(); box.size = setup.dimensions;
                    box.center = new Vector3(0, 0.3f, 0);
                    var wheels = new VehicleWheel[4];
                    for (int i = 0; i < 4; i++)
                    {
                        bool front = i < 2;
                        var mount = new GameObject(front ? "Front wheel" : "Rear wheel"); mount.transform.SetParent(Root.transform, false);
                        mount.transform.localPosition = new Vector3((i % 2 == 0 ? -1 : 1) * setup.trackWidth * 0.5f, -0.18f,
                            (front ? 1 : -1) * setup.wheelbase * 0.5f);
                        wheels[i] = mount.AddComponent<VehicleWheel>();
                        wheels[i].Setup(front ? VehicleAxle.Front : VehicleAxle.Rear, front, !front, !front, null);
                    }
                    Vehicle.Wheels = wheels;
                }
                foreach (var child in Root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 2;
                foreach (var wheel in Root.GetComponentsInChildren<VehicleWheel>(true)) wheel.SetGroundMask(Physics.DefaultRaycastLayers);
                Root.transform.SetPositionAndRotation(position, rotation);
                Input = Root.AddComponent<RacingSimulationInput>();
                Vehicle.SetManualSimulation(manual);
                Vehicle.ConfigureForRuntime(tuning.Value, Input, Root.GetComponentsInChildren<VehicleWheel>(true));
                Chassis = Root.GetComponentInChildren<BoxCollider>(); Assists = Root.GetComponent<VehicleAssists>();
                Body.interpolation = RigidbodyInterpolation.None;
                Root.SetActive(true);
            }
            catch { Dispose(); throw; }
        }

        public static void ValidatePrefab(VehicleController prefab, Vector3 dimensions)
        {
            if (!prefab.HasLocalPhysicsBindings)
                throw new ArgumentException("LINE_PREFAB_BINDINGS: powertrain, assists, nitrous and module-host references must belong to the fixture chassis.");
            var host = prefab.GetComponent<VehicleModuleHost>();
            var body = prefab.GetComponent<Rigidbody>();
            if (!prefab.enabled || body == null || body.isKinematic || !body.useGravity || !body.automaticInertiaTensor || body.constraints != RigidbodyConstraints.None)
                throw new ArgumentException("LINE_PREFAB_BODY: require an enabled controller and dynamic, gravity-driven, unconstrained chassis with automatic inertia.");
            if (host == null || !JsonUtility.ToJson(host).Contains("\"moduleComponents\":[]"))
                throw new ArgumentException("LINE_PREFAB_MODULES: a physics fixture cannot reference gameplay modules, including external objects.");
            if (prefab.transform.parent != null || prefab.transform.localScale != Vector3.one
                || prefab.GetComponentsInChildren<Rigidbody>(true).Length != 1 || prefab.GetComponentsInChildren<VehicleWheel>(true).Length != 4)
                throw new ArgumentException("LINE_PREFAB: require one root rigidbody, four wheels and unit scale.");
            foreach (var component in prefab.GetComponentsInChildren<Component>(true))
            {
                if (component == null) throw new ArgumentException("LINE_PREFAB: missing script.");
                var t = component.GetType();
                if (t != typeof(Transform) && t != typeof(Rigidbody) && t != typeof(BoxCollider) && t != typeof(VehicleController)
                    && t != typeof(VehiclePowertrain) && t != typeof(VehicleAssists) && t != typeof(VehicleModuleHost)
                    && t != typeof(VehicleWheel) && t != typeof(VehicleNitrous) && t != typeof(MeshFilter) && t != typeof(MeshRenderer))
                    throw new ArgumentException("LINE_PREFAB_SCRIPT: unsupported component " + t.Name + ". Supply a physics-only prefab, not a player/career root.");
            }
            var boxes = prefab.GetComponentsInChildren<BoxCollider>(true);
            if (boxes.Length != 1 || boxes[0].transform != prefab.transform || boxes[0].isTrigger || !boxes[0].enabled
                || boxes[0].sharedMaterial != null || boxes[0].size != dimensions || Mathf.Abs(boxes[0].center.x) > 0.001f || Mathf.Abs(boxes[0].center.z) > 0.001f)
                throw new ArgumentException("LINE_PREFAB_FOOTPRINT: require one enabled root chassis box matching setup dimensions, centred laterally/longitudinally.");
        }
        public static float SpawnHeight(RacingVehicleSetup setup)
        {
            using var effective = new RacingTuningLease(setup.CreateEffectiveTuning());
            float mountHeight = setup.physicsPrefab == null ? -0.18f
                : setup.physicsPrefab.transform.InverseTransformPoint(setup.physicsPrefab.GetComponentsInChildren<VehicleWheel>(true)[0].transform.position).y;
            return effective.Value.tires.wheelRadius + effective.Value.tires.suspensionRestLength - mountHeight;
        }
        public void Dispose()
        {
            if (Root != null) { Root.SetActive(false); Destroy(Root); Root = null; }
            tuning?.Dispose(); tuning = null;
        }
        private static void Destroy(UnityEngine.Object value)
        { if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    }
}
