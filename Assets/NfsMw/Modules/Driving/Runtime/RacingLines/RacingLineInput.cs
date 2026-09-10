using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [DefaultExecutionOrder(-110)]
    [RequireComponent(typeof(VehicleController))]
    public sealed class RacingLineInput : MonoBehaviour, IVehicleInputSource
    {
        public RacingLineSource source;
        [Range(0, 1)] public float pacing = 1;
        private VehicleController vehicle;
        private RacingLineTracker tracker;
        private float nextCheck;
        private string vehicleTuning;
        private RacingLineRuntimeCache.Lease lease;
        private RacingLineArtifact artifact;
        public float Station => tracker?.Station ?? 0;
        public bool Finished => tracker != null && tracker.Finished;
        public string Failure { get; private set; }
        public VehicleInputState Current { get; private set; }
        public bool ConsumeResetRequest() => false;
        public bool ConsumeCameraToggleRequest() => false;
        private void OnEnable()
        {
            Current = new VehicleInputState { Handbrake = true };
            if (!Application.isPlaying) return;
            vehicle = GetComponent<VehicleController>(); vehicle.SetInputSource(this);
            // On scene load our input execution order precedes VehicleController.Awake. Bind after its configuration in Start.
            if (vehicle.Body != null) Rebind();
        }
        private void Start() { if (tracker == null) Rebind(); }
        public bool Rebind()
        {
            Current = new VehicleInputState { Brake = 1 };
            tracker = null; lease?.Dispose(); lease = null; vehicle = GetComponent<VehicleController>(); vehicle.SetInputSource(this);
            try
            {
                lease = RacingLineRuntimeCache.Acquire(source); string fingerprint = lease.Fingerprint();
                if (Mathf.Abs(Time.fixedDeltaTime - source.vehicle.fixedStep) > 0.000001f)
                    throw new ArgumentException("LINE_FIXED_STEP: runtime physics step differs from the verified setup. Match the project setting deliberately or recalibrate.");
                if (source.published == null) throw new ArgumentException("LINE_MISSING: no published trajectory.");
                if (!source.published.TryOpen(fingerprint, out var reader, out string failure)) throw new ArgumentException(failure);
                using var tuning = new RacingTuningLease(source.vehicle.CreateEffectiveTuning());
                if (JsonUtility.ToJson(vehicle.Tuning) != JsonUtility.ToJson(tuning.Value)) throw new ArgumentException("LINE_TUNE: actual runtime vehicle tuning does not match the verified setup.");
                ValidateShape();
                var body = GetComponent<Rigidbody>();
                if (!source.published.Entry.Contains(reader[0], transform, body.linearVelocity)
                    || body.angularVelocity.magnitude > 0.05f || Mathf.Abs(Vector3.Dot(body.linearVelocity, reader[0].normal)) > 0.1f
                    || Mathf.Abs(Vector3.Dot(transform.position - reader[0].position, reader[0].normal) - RacingVehicleRig.SpawnHeight(source.vehicle)) > 0.6f)
                    throw new ArgumentException("LINE_ENTRY: align a reset car with the published start pose/speed envelope. Mid-route joins require a separately verified transition.");
                tracker = new RacingLineTracker(reader, source.vehicle.controller, source.vehicle.wheelbase);
                artifact = source.published; vehicleTuning = JsonUtility.ToJson(vehicle.Tuning); Failure = null; nextCheck = Time.unscaledTime + 0.5f; return true;
            }
            catch (ArgumentException exception) { lease?.Dispose(); lease = null; Failure = exception.Message; return false; }
        }
        private void FixedUpdate()
        {
            if (vehicle == null) vehicle = GetComponent<VehicleController>();
            if (tracker != null && Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + 0.5f;
                try
                {
                    if (source == null || source != lease.Source || source.published != artifact || lease.Fingerprint() != artifact.Fingerprint
                        || JsonUtility.ToJson(vehicle.Tuning) != vehicleTuning || Mathf.Abs(Time.fixedDeltaTime - source.vehicle.fixedStep) > 0.000001f)
                        throw new ArgumentException("LINE_STALE: dependency, fixed step or runtime tune changed.");
                    ValidateShape();
                }
                catch (ArgumentException exception) { tracker = null; lease?.Dispose(); lease = null; Failure = exception.Message; }
            }
            if (tracker == null)
            { float speed = vehicle.Body == null ? 0 : vehicle.Body.linearVelocity.magnitude; Current = new VehicleInputState { Brake = speed > 1.2f ? 1 : 0, Handbrake = speed <= 1.2f }; return; }
            Current = tracker.Step(transform, vehicle.Body.linearVelocity, vehicle.Body.angularVelocity, vehicle.Tuning, Time.fixedDeltaTime, pacing);
        }
        private void ValidateShape()
        {
            var boxes = GetComponentsInChildren<BoxCollider>(); var wheels = GetComponentsInChildren<VehicleWheel>();
            if (!vehicle.enabled || !vehicle.HasLocalPhysicsBindings || transform.lossyScale != Vector3.one || boxes.Length != 1 || boxes[0].transform != transform
                || !boxes[0].enabled || boxes[0].isTrigger || boxes[0].size != source.vehicle.dimensions || wheels.Length != 4
                || boxes[0].sharedMaterial != null || gameObject.layer != 2
                || GetComponentsInChildren<Collider>().Length != 1 || GetComponentsInChildren<Rigidbody>().Length != 1)
                throw new ArgumentException("LINE_RIG: actual chassis footprint differs from the verified physics setup.");
            var body = GetComponent<Rigidbody>();
            if (body.isKinematic || !body.useGravity || !body.automaticInertiaTensor || body.constraints != RigidbodyConstraints.None)
                throw new ArgumentException("LINE_RIG: the trajectory requires a dynamic, gravity-driven, unconstrained chassis.");
            var prefab = source.vehicle.physicsPrefab; var expectedBody = prefab == null ? null : prefab.GetComponent<Rigidbody>();
            if (vehicle.UsesBuiltInAero != (prefab == null || prefab.UsesBuiltInAero)
                || body.solverIterations != (expectedBody == null ? Physics.defaultSolverIterations : expectedBody.solverIterations)
                || body.solverVelocityIterations != (expectedBody == null ? Physics.defaultSolverVelocityIterations : expectedBody.solverVelocityIterations)
                || Mathf.Abs(body.maxDepenetrationVelocity - (expectedBody == null ? Physics.defaultMaxDepenetrationVelocity : expectedBody.maxDepenetrationVelocity)) > 0.00001f
                || Mathf.Abs(body.sleepThreshold - (expectedBody == null ? Physics.sleepThreshold : expectedBody.sleepThreshold)) > 0.00001f
                || Mathf.Abs(boxes[0].contactOffset - (prefab == null ? Physics.defaultContactOffset : prefab.GetComponent<BoxCollider>().contactOffset)) > 0.00001f)
                throw new ArgumentException("LINE_RIG_SETTINGS: aerodynamic or native solver/contact settings differ from the verified setup.");
            Vector3 centre = source.vehicle.physicsPrefab == null ? new Vector3(0, 0.3f, 0) : source.vehicle.physicsPrefab.GetComponent<BoxCollider>().center;
            if (Vector3.Distance(boxes[0].center, centre) > 0.001f)
                throw new ArgumentException("LINE_RIG: chassis centre differs from the verified footprint.");
            if (vehicle.ModuleHost != null)
                foreach (var module in vehicle.ModuleHost.Modules)
                    if (module is IVehiclePrePhysicsModule || module is IVehiclePostPhysicsModule)
                        throw new ArgumentException("LINE_RIG_MODULE: unverified physics module; calibrate through an explicitly supported physics adapter.");
            var expected = source.vehicle.physicsPrefab == null ? null : source.vehicle.physicsPrefab.GetComponentsInChildren<VehicleWheel>(true);
            for (int i = 0; i < wheels.Length; i++)
            {
                bool front = i < 2;
                var position = expected == null ? new Vector3((i % 2 == 0 ? -1 : 1) * source.vehicle.trackWidth * 0.5f, -0.18f,
                    (front ? 1 : -1) * source.vehicle.wheelbase * 0.5f)
                    : source.vehicle.physicsPrefab.transform.InverseTransformPoint(expected[i].transform.position);
                if (wheels[i].GroundMask != Physics.DefaultRaycastLayers
                    || Vector3.Distance(transform.InverseTransformPoint(wheels[i].transform.position), position) > 0.001f
                    || Vector3.Angle(transform.InverseTransformDirection(wheels[i].transform.up), expected == null ? Vector3.up
                        : source.vehicle.physicsPrefab.transform.InverseTransformDirection(expected[i].transform.up)) > 0.1f
                    || wheels[i].Axle != (expected == null ? front ? VehicleAxle.Front : VehicleAxle.Rear : expected[i].Axle)
                    || wheels[i].IsSteeringWheel != (expected == null ? front : expected[i].IsSteeringWheel)
                    || wheels[i].IsDrivenWheel != (expected == null ? !front : expected[i].IsDrivenWheel)
                    || wheels[i].IsHandbrakeWheel != (expected == null ? !front : expected[i].IsHandbrakeWheel))
                    throw new ArgumentException("LINE_RIG: actual wheel geometry or drive/steering flags differ from the verified setup.");
            }
        }
        private void OnDisable() { tracker = null; lease?.Dispose(); lease = null; Current = new VehicleInputState { Handbrake = true }; }
    }
}
