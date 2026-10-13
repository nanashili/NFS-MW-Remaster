using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving
{
    public enum VehicleAxle { Front, Rear }

    /// <summary>Unity contact and presentation adapter for SuspensionRacer::Tire and DoWheelForces.</summary>
    [DisallowMultipleComponent]
    public sealed class VehicleWheel : MonoBehaviour
    {
        [SerializeField] private VehicleAxle axle;
        [SerializeField] private bool steeringWheel;
        [SerializeField] private bool drivenWheel = true;
        [SerializeField] private bool handbrakeWheel;
        [SerializeField] private float radius = .34f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private Transform visual;
        [SerializeField, Min(0f)] private float visualReferenceRadius;
        [SerializeField] private Vector3 visualRotationOffset = new Vector3(0, 0, 90);
        [SerializeField] private bool drawDebugGizmos = true;
        private Rigidbody body;
        private MostWantedDrivingSettings settings;
        private bool fitmentBaselineCaptured;
        private Vector3 baselineLocalPosition, baselineVisualScale;
        private float baselineRadius, steerAngle, visualSpinAngle;
        private Collider contactCollider;
        private VehicleSurface contactSurface;
        private WeatherSurfaceRegion weatherRegion;
        public MostWantedTire Tire { get; private set; }
        public Vector3 LocalArm { get; private set; }
        public Vector3 WorldForce { get; private set; }
        public Vector3 ForcePosition { get; private set; }
        public float CompressionMeters { get; private set; }
        public float AirTime { get; private set; }
        public float BottomOutCorrection { get; private set; }
        public float Radius => radius;
        public int GroundMask => groundMask.value;
        public VehicleAxle Axle => axle;
        public bool IsFrontWheel => axle == VehicleAxle.Front;
        public bool IsSteeringWheel => steeringWheel;
        public bool IsDrivenWheel => drivenWheel;
        public bool IsHandbrakeWheel => handbrakeWheel;
        public bool Grounded { get; private set; }
        public float AngularVelocity => Tire?.AngularVelocity ?? 0;
        public float AngularAcceleration => Tire?.AngularAcceleration ?? 0;
        public float SteerAngle => steerAngle;
        public float ContactForwardSpeed => Tire?.RoadSpeed ?? 0;
        public float LateralSpeed => Tire?.LateralSpeed ?? 0;
        public float CurrentSlip => Tire?.Slip ?? 0;
        public float LongitudinalSlip => CurrentSlip / Mathf.Max(1, Mathf.Abs(ContactForwardSpeed));
        public float LateralSlipRadians => (Tire?.SlipAngle ?? 0) * 2f * Mathf.PI;
        public float NormalLoad => Tire?.Load ?? 0;
        public float LongitudinalForce => Tire?.LongitudinalForce ?? 0;
        public float LateralForce => Tire?.LateralForce ?? 0;
        public float SuspensionCompression => CompressionMeters / Mathf.Max(.001f, settings == null ? .1f : settings.travel[(int)axle]);
        public float SuspensionLength => settings == null ? 0 : settings.rideHeight[(int)axle] - CompressionMeters;
        public Vector3 ContactPoint { get; private set; }
        public Vector3 ContactNormal { get; private set; } = Vector3.up;
        public string SurfaceName { get; private set; } = "Air";
        public float SurfaceGrip { get; private set; } = 1;
        public float SurfaceRollingResistance { get; private set; } = 1;
        public SensorySurface SurfaceKind => !Grounded ? SensorySurface.Air : contactSurface != null ? contactSurface.Kind : SensorySurface.AsphaltDry;
        public float CameraRoughness => contactSurface != null && contactSurface.Profile != null ? contactSurface.Profile.cameraRoughness : .08f;

        public void Setup(VehicleAxle configuredAxle, bool configuredSteering, bool configuredDriven, bool configuredHandbrakeWheel, Transform wheelVisual)
        { axle = configuredAxle; steeringWheel = configuredSteering; drivenWheel = configuredDriven; handbrakeWheel = configuredHandbrakeWheel; visual = wheelVisual; }
        public void SetGroundMask(LayerMask mask) => groundMask = mask;
        public void SetVisualReferenceRadius(float authoredRadius)
        {
            if (!float.IsFinite(authoredRadius) || authoredRadius < .05f) throw new System.ArgumentOutOfRangeException(nameof(authoredRadius));
            visualReferenceRadius = authoredRadius;
            if (fitmentBaselineCaptured) baselineRadius = authoredRadius;
        }
        public void SetVisualRotationOffset(Vector3 degrees)
        {
            if (!float.IsFinite(degrees.x) || !float.IsFinite(degrees.y) || !float.IsFinite(degrees.z)) throw new System.ArgumentException("Wheel visual rotation must be finite.");
            visualRotationOffset = degrees;
        }
        public void Configure(Rigidbody configuredBody, VehicleTuning tuning)
        {
            if (configuredBody == null || tuning == null) throw new System.ArgumentNullException(nameof(tuning));
            if (!fitmentBaselineCaptured)
            {
                baselineLocalPosition = configuredBody.transform.InverseTransformPoint(transform.position);
                baselineVisualScale = visual != null ? visual.localScale : Vector3.one;
                baselineRadius = Mathf.Max(.05f, visualReferenceRadius > 0 ? visualReferenceRadius : radius);
                fitmentBaselineCaptured = true;
            }
            body = configuredBody;
            settings = tuning.mostWanted;
            int a = (int)axle;
            radius = tuning.tires.wheelRadius * (a == 0 ? 1 : settings.rearWheelRadiusScale);
            var box = body.GetComponent<BoxCollider>();
            float halfHeight = box != null ? box.size.y * .5f : .65f;
            float halfTrack = (settings.trackWidth[a] - settings.sectionWidth[a] * .001f) * .5f;
            LocalArm = new Vector3((baselineLocalPosition.x < 0 ? -1 : 1) * halfTrack,
                -halfHeight, settings.frontAxle - (a == 0 ? 0 : settings.wheelBase));
            transform.position = body.transform.TransformPoint(LocalArm);
            int index = a * 2 + (baselineLocalPosition.x < 0 ? 0 : 1);
            Tire = new MostWantedTire(radius, index, new MostWantedTireSpecification(
                new[] { settings.gripScale.x, settings.gripScale.y },
                new[] { settings.dynamicGrip.x, settings.dynamicGrip.y },
                new[] { settings.staticGrip.x, settings.staticGrip.y }, settings.brakeTorque[a], settings.handbrake,
                brakeLockFactor: settings.brakeLock[a]));
            if (visual != null) visual.localScale = baselineVisualScale * (radius / baselineRadius);
            ResetSimulation();
        }
        public void ResetSimulation()
        {
            Tire?.MatchSpeed(0);
            CompressionMeters = AirTime = BottomOutCorrection = steerAngle = visualSpinAngle = 0;
            Grounded = false; WorldForce = Vector3.zero; ContactNormal = Vector3.up;
            if (body != null && settings != null)
                ForcePosition = body.transform.TransformPoint(LocalArm - Vector3.up * settings.rideHeight[(int)axle]);
            contactCollider = null; contactSurface = null; weatherRegion = null;
            SurfaceName = "Air"; SurfaceGrip = SurfaceRollingResistance = 1;
        }
        public void SetInitialForwardSpeed(float speedMetersPerSecond) => Tire?.MatchSpeed(speedMetersPerSecond);

        // The controller snapshots both axle compressions before calling any wheel, as the source does.
        public void Simulate(float deltaTime, float steeringDegrees, float swayForce)
        {
            if (body == null || Tire == null) return;
            steerAngle = steeringDegrees;
            int a = (int)axle;
            Vector3 up = body.transform.up;
            Vector3 arm = body.transform.TransformPoint(LocalArm);
            float rideHeight = settings.rideHeight[a];
            float queryHeight = Mathf.Max(radius, settings.travel[a]);
            int mask = groundMask.value == 0 ? Physics.DefaultRaycastLayers : groundMask.value;
            bool contact = body.gameObject.scene.GetPhysicsScene().Raycast(arm + up * queryHeight, -up,
                out RaycastHit hit, queryHeight + rideHeight + radius, mask, QueryTriggerInteraction.Ignore);
            float compression = 0;
            float upness = 0;
            if (contact)
            {
                ContactPoint = hit.point; ContactNormal = hit.normal.normalized;
                upness = Mathf.Clamp01(Vector3.Dot(ContactNormal, up));
                float penetration = Vector3.Dot(hit.point - arm, ContactNormal);
                compression = rideHeight * upness + penetration;
            }
            BottomOutCorrection = Mathf.Max(0, compression - settings.travel[a]);
            if (BottomOutCorrection > 0) Tire.BottomOutTime = Time.fixedTime;
            compression = Mathf.Clamp(compression, 0, settings.travel[a]);
            Grounded = contact && compression > 0 && upness > .2f;
            WorldForce = Vector3.zero;
            if (Grounded)
            {
                if (contactCollider != hit.collider)
                {
                    contactCollider = hit.collider;
                    contactSurface = contactCollider.GetComponentInParent<VehicleSurface>();
                    weatherRegion = contactCollider.GetComponentInParent<WeatherSurfaceRegion>();
                }
                SurfaceGrip = contactSurface != null ? contactSurface.GripMultiplier : 1;
                SurfaceRollingResistance = contactSurface != null ? contactSurface.RollingResistanceMultiplier : 1;
                SurfaceName = contactSurface != null ? contactSurface.SurfaceName : "Road";
                float wetness = weatherRegion == null ? 0 : Mathf.Clamp01(weatherRegion.Wetness);
                float grip = SurfaceGrip * Mathf.Lerp(1, .72f, wetness);
                Tire.SetSurface(grip, grip, SurfaceRollingResistance);
                float rise = (compression - CompressionMeters) / deltaTime;
                float valving = settings.shockValving[a];
                float digression = 1f - settings.shockDigression[a];
                if (valving > 1e-6f && digression < 1 && Mathf.Abs(rise) > valving)
                    rise = Mathf.Sign(rise) * valving * Mathf.Pow(Mathf.Abs(rise) / valving, digression);
                float damp = rise * (rise > 0 ? settings.shockStiffness[a] : settings.shockExtensionStiffness[a]);
                if (damp > settings.shockBlowout * 9.81f * body.mass) damp = 0;
                float spring = compression * settings.springStiffness[a] * (compression * settings.springProgression[a] + 1f);
                float springForce = Mathf.Max(0, damp + spring + swayForce);
                Vector3 forward = Quaternion.AngleAxis(steerAngle, up) * body.transform.forward;
                Vector3 lateral = Vector3.Cross(ContactNormal, forward).normalized;
                Vector3 c = Vector3.Cross(Vector3.Cross(forward, ContactNormal), forward);
                float load = Mathf.Max(Vector3.Dot(c, ContactNormal) * 4 - 3, .3f) * springForce;
                Vector3 pointVelocity = body.GetPointVelocity(arm);
                float xspeed = Vector3.Dot(pointVelocity, lateral);
                float zspeed = Vector3.Dot(pointVelocity, forward);
                float force = Tire.UpdateLoaded(xspeed, zspeed, body.linearVelocity.magnitude, load, deltaTime);
                float maxTraction = Mathf.Abs(xspeed / deltaTime * .25f * body.mass);
                WorldForce = lateral * Mathf.Clamp(force, -maxTraction, maxTraction)
                    + Vector3.Cross(lateral, ContactNormal).normalized * Tire.LongitudinalForce + up * springForce;
            }
            else
            {
                Tire.UpdateFree(deltaTime); SurfaceName = "Air";
                contactCollider = null; contactSurface = null; weatherRegion = null;
            }
            AirTime = compression == 0 ? AirTime + deltaTime : 0;
            CompressionMeters = compression;
            ForcePosition = body.transform.TransformPoint(LocalArm + Vector3.up * (compression - rideHeight));
        }
        public void ApplyVisualPose(float deltaTime)
        {
            if (visual == null || body == null) return;
            Vector3 up = Grounded ? ContactNormal : body.transform.up;
            Vector3 forward = Quaternion.AngleAxis(steerAngle, body.transform.up) * body.transform.forward;
            forward = Vector3.ProjectOnPlane(forward, up).normalized;
            if (forward.sqrMagnitude < .001f) forward = body.transform.forward;
            visualSpinAngle += AngularVelocity * deltaTime * Mathf.Rad2Deg;
            visual.position = ForcePosition + body.transform.up * radius;
            visual.rotation = Quaternion.LookRotation(forward, up) * Quaternion.AngleAxis(visualSpinAngle, Vector3.right) * Quaternion.Euler(visualRotationOffset);
        }
        private void OnDrawGizmosSelected()
        {
            if (!drawDebugGizmos) return;
            Gizmos.color = Grounded ? Color.green : Color.red;
            Gizmos.DrawLine(transform.position, transform.position - transform.up * (radius + (settings?.rideHeight[(int)axle] ?? .2f)));
            if (Grounded) { Gizmos.DrawSphere(ContactPoint, .035f); Gizmos.DrawLine(ContactPoint, ContactPoint + ContactNormal * .35f); }
        }
    }
}
