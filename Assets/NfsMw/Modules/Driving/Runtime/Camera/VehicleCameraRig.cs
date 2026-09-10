using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Acquires dependencies once and owns the sole final camera transform/FOV write.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class VehicleCameraRig : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Transform cockpitAnchor;
        [SerializeField] private Rigidbody targetBody;
        [SerializeField] private VehicleCameraProfile profile;
        [SerializeField] private bool motionBlurEnabled = true;
        private Camera cameraComponent;
        private VehicleController source;
        private VehicleWheel[] wheels;
        private readonly VehicleCameraTelemetrySampler sampler = new VehicleCameraTelemetrySampler();
        private readonly VehicleCameraPipeline pipeline = new VehicleCameraPipeline();
        private readonly CameraCollisionModule collision = new CameraCollisionModule();
        private bool hoodCamera, ownsProfile;
        public VehicleCameraEffects DebugEffects { get; set; } = VehicleCameraEffects.All;
        public VehicleCameraDiagnostics Diagnostics { get; private set; }
        public event System.Action<VehicleCameraDiagnostics> PoseResolved;
        public bool IsHoodView => hoodCamera;
        public Transform Target => target;
        public Transform CockpitAnchor => cockpitAnchor;
        public VehicleCameraProfile Profile => profile;
        public bool MotionBlurEnabled { get => motionBlurEnabled; set => motionBlurEnabled = value; }

        public void SetProfile(VehicleCameraProfile value)
        {
            ReleaseFallback(); profile = value; AcquireProfile(); ResetPose();
        }
        public void SetTarget(Transform newTarget, Rigidbody body)
        {
            Unbind(); target = newTarget; targetBody = body; Bind(); ResetPose();
        }
        public void SetCockpitAnchor(Transform anchor) { cockpitAnchor = anchor; ResetPose(); }
        public void ToggleCameraMode() { hoodCamera = !hoodCamera; ResetPose(); }
        private void Awake() { cameraComponent = GetComponent<Camera>(); AcquireProfile(); }
        private void AcquireProfile()
        {
            if (profile) return;
            profile = Resources.Load<VehicleCameraProfile>(VehicleCameraProfile.DefaultResource);
            if (!profile) { profile = ScriptableObject.CreateInstance<VehicleCameraProfile>(); profile.hideFlags = HideFlags.HideAndDontSave; ownsProfile = true; }
        }
        private void Bind()
        {
            if (!target) return;
            if (!targetBody) targetBody = target.GetComponent<Rigidbody>();
            source = target.GetComponent<VehicleController>(); wheels = source ? source.Wheels : null;
            collision.Bind(target);
            if (source && isActiveAndEnabled) { source.PhysicsSampled += Sample; source.PoseReset += ResetPose; }
        }
        private void Unbind() { if (source) { source.PhysicsSampled -= Sample; source.PoseReset -= ResetPose; } source = null; }
        public void ResetPose() { sampler.Reset(); pipeline.Reset(); collision.Reset(); }
        private void OnEnable() { Unbind(); Bind(); ResetPose(); }
        private void OnDisable() { Unbind(); collision.Dispose(); }
        private void OnDestroy() { Unbind(); collision.Dispose(); ReleaseFallback(); }
        private void ReleaseFallback()
        {
            if (!ownsProfile || !profile) return;
            if (Application.isPlaying) Destroy(profile); else DestroyImmediate(profile);
            ownsProfile = false;
        }
        private void Sample(float dt) => sampler.Sample(targetBody, wheels, source && source.Telemetry.NitrousActive, dt);
        private void FixedUpdate() { if (!source) Sample(Time.fixedDeltaTime); }
        private void LateUpdate()
        {
            if (!target || !profile || Time.deltaTime <= 0) return;
            VehicleCameraPose pose = pipeline.Resolve(profile, sampler.Frame(target, cockpitAnchor), hoodCamera, DebugEffects, Time.deltaTime, cameraComponent.aspect);
            if ((DebugEffects & profile.effects & VehicleCameraEffects.Collision) != 0 && (!hoodCamera || profile.collision.cockpit))
                pose.position = collision.Resolve(pose.collisionOrigin, pose.position, profile.collision, cameraComponent.nearClipPlane, pose.fieldOfView, cameraComponent.aspect, Time.deltaTime);
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            cameraComponent.fieldOfView = pose.fieldOfView;
            var debug = pose.debug; debug.distance = Vector3.Distance(pose.position, pose.collisionOrigin); Diagnostics = debug;
            PoseResolved?.Invoke(Diagnostics);
        }
    }
}
