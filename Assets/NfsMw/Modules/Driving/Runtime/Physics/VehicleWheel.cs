using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving
{
    public enum VehicleAxle
    {
        Front,
        Rear
    }

    public struct VehicleWheelCommand
    {
        public float SteerAngle;
        public float DriveTorque;
        public float BrakeTorque;
        public float LongitudinalGripMultiplier;
        public float LateralGripMultiplier;
    }

    /// <summary>
    /// One raycast wheel with sprung suspension and a combined-slip tire
    /// model. The controller only supplies commands; wheel contact, load,
    /// slip, and force generation stay encapsulated here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleWheel : MonoBehaviour
    {
        [SerializeField] private VehicleAxle axle = VehicleAxle.Front;
        [SerializeField] private bool steeringWheel;
        [SerializeField] private bool drivenWheel = true;
        [SerializeField] private bool handbrakeWheel;
        [SerializeField] private float radius = 0.34f;
        [SerializeField] private float suspensionRestLength = 0.32f;
        [SerializeField] private float suspensionTravel = 0.18f;
        [SerializeField] private float springRate = 31000f;
        [SerializeField] private float damperRate = 4700f;
        [SerializeField] private float wheelMass = 19f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private Transform visual;
        [SerializeField, Min(0f), Tooltip("Radius represented by the authored visual at its original scale. Zero uses the legacy wheel radius.")]
        private float visualReferenceRadius;
        [SerializeField, Tooltip("Visual-only basis correction. Existing cylinder rigs use Z=90; imported +X-axle rigs use zero.")]
        private Vector3 visualRotationOffset = new Vector3(0f, 0f, 90f);
        [SerializeField] private bool drawDebugGizmos = true;

        private Rigidbody body;
        private VehicleTuning.TireSettings tireSettings;
        private float wheelAngularVelocity;
        private float suspensionLength;
        private float suspensionCompression;
        private float steerAngle;
        private float visualSpinAngle;
        private Vector3 contactPoint;
        private Vector3 contactNormal = Vector3.up;
        private bool fitmentBaselineCaptured;
        private Vector3 baselineLocalPosition;
        private Vector3 baselineVisualScale;
        private float baselineRadius;
        private Collider contactCollider;
        private VehicleSurface contactSurface;
        private WeatherSurfaceRegion weatherRegion;
        public float Radius => radius;
        public int GroundMask => groundMask.value;
        public float ContactForwardSpeed { get; private set; }
        public SensorySurface SurfaceKind => !Grounded ? SensorySurface.Air : contactSurface != null ? contactSurface.Kind : SensorySurface.AsphaltDry;
        public float CameraRoughness => contactSurface != null && contactSurface.Profile != null ? contactSurface.Profile.cameraRoughness : .08f;
        private float surfaceGrip = 1f;
        private float reboundDamperRate;
        private float surfaceRollingResistance = 1f;
        private string surfaceName = "Air";

        public VehicleAxle Axle
        {
            get { return axle; }
        }

        public bool IsFrontWheel
        {
            get { return axle == VehicleAxle.Front; }
        }

        public bool IsSteeringWheel
        {
            get { return steeringWheel; }
        }

        public bool IsDrivenWheel
        {
            get { return drivenWheel; }
        }

        public bool IsHandbrakeWheel
        {
            get { return handbrakeWheel; }
        }

        public bool Grounded { get; private set; }

        public float AngularVelocity
        {
            get { return wheelAngularVelocity; }
        }

        public float SteerAngle => steerAngle;

        public float LongitudinalSlip { get; private set; }

        public float LateralSlipRadians { get; private set; }

        public float NormalLoad { get; private set; }

        public float LongitudinalForce { get; private set; }

        public float LateralForce { get; private set; }

        public float SuspensionCompression
        {
            get { return suspensionCompression; }
        }

        public float SuspensionLength => suspensionLength;

        public string SurfaceName
        {
            get { return surfaceName; }
        }

        public float SurfaceGrip
        {
            get { return surfaceGrip; }
        }

        public float SurfaceRollingResistance
        {
            get { return surfaceRollingResistance; }
        }

        public Vector3 ContactPoint
        {
            get { return contactPoint; }
        }

        public Vector3 ContactNormal
        {
            get { return contactNormal; }
        }

        public void Setup(
            VehicleAxle configuredAxle,
            bool configuredSteering,
            bool configuredDriven,
            bool configuredHandbrakeWheel,
            Transform wheelVisual)
        {
            axle = configuredAxle;
            steeringWheel = configuredSteering;
            drivenWheel = configuredDriven;
            handbrakeWheel = configuredHandbrakeWheel;
            visual = wheelVisual;
        }

        public void SetGroundMask(LayerMask mask)
        {
            groundMask = mask;
        }

        /// <summary>Binds imported geometry to its authored tire size without changing physics tuning.</summary>
        public void SetVisualReferenceRadius(float authoredRadius)
        {
            if (!float.IsFinite(authoredRadius) || authoredRadius < .05f)
                throw new System.ArgumentOutOfRangeException(nameof(authoredRadius));
            visualReferenceRadius = authoredRadius;
            if (fitmentBaselineCaptured) baselineRadius = authoredRadius;
        }

        /// <summary>Changes mesh orientation only; steering, tyre forces and suspension axes are unchanged.</summary>
        public void SetVisualRotationOffset(Vector3 degrees)
        {
            if (!float.IsFinite(degrees.x) || !float.IsFinite(degrees.y) || !float.IsFinite(degrees.z))
                throw new System.ArgumentException("Wheel visual rotation must be finite.", nameof(degrees));
            visualRotationOffset = degrees;
        }

        public void Configure(Rigidbody configuredBody, VehicleTuning tuning)
        {
            if (!fitmentBaselineCaptured)
            {
                baselineLocalPosition = transform.localPosition;
                baselineVisualScale = visual != null ? visual.localScale : Vector3.one;
                baselineRadius = Mathf.Max(0.05f, visualReferenceRadius > 0f ? visualReferenceRadius : radius);
                fitmentBaselineCaptured = true;
            }
            body = configuredBody;
            tireSettings = tuning != null ? tuning.tires : new VehicleTuning.TireSettings();
            radius = Mathf.Max(0.05f, tireSettings.wheelRadius);
            suspensionRestLength = Mathf.Max(0.05f, tireSettings.suspensionRestLength);
            suspensionTravel = Mathf.Max(0f, tireSettings.suspensionTravel);
            springRate = Mathf.Max(0f, tireSettings.springRate);
            damperRate = Mathf.Max(0f, tireSettings.damperRate);
            wheelMass = Mathf.Max(0.1f, tireSettings.wheelMass);
            reboundDamperRate = damperRate;
            if (tuning != null && tuning.UsesMostWantedReference)
            {
                var reference = tuning.mostWanted;
                if (!IsFrontWheel)
                {
                    radius *= reference.rearWheelRadiusScale;
                    springRate *= reference.rearSpringScale;
                    damperRate *= reference.rearDamperScale;
                }
                reboundDamperRate = tireSettings.damperRate * (IsFrontWheel ? reference.frontReboundDamperScale : reference.rearReboundDamperScale);
            }
            suspensionLength = suspensionRestLength;

            Vector3 anchor = baselineLocalPosition;
            float lateralDelta = Mathf.Max(0f, tireSettings.wheelLateralOffset);
            if (lateralDelta > 0.0001f)
                anchor.x += (baselineLocalPosition.x < 0f ? -1f : 1f) * lateralDelta;
            transform.localPosition = anchor;
            if (visual != null)
                visual.localScale = baselineVisualScale * (radius / baselineRadius);
        }

        public void ResetSimulation()
        {
            wheelAngularVelocity = suspensionCompression = steerAngle = visualSpinAngle = 0;
            suspensionLength = suspensionRestLength; contactPoint = Vector3.zero; contactNormal = Vector3.up;
            contactCollider = null; contactSurface = null; weatherRegion = null; surfaceGrip = surfaceRollingResistance = 1; surfaceName = "Air";
            Grounded = false; LongitudinalSlip = LateralSlipRadians = NormalLoad = ContactForwardSpeed = 0;
            LongitudinalForce = LateralForce = 0;
        }

        /// <summary>
        /// Seeds rotational state for an isolated experiment that starts at a
        /// non-zero vehicle speed. This is intentionally a narrow calibration
        /// seam and is not used by gameplay or save systems.
        /// </summary>
        public void SetInitialForwardSpeed(float speedMetersPerSecond)
        {
            wheelAngularVelocity = speedMetersPerSecond / Mathf.Max(0.05f, radius);
        }

        public void Simulate(float deltaTime, VehicleWheelCommand command)
        {
            if (body == null)
            {
                return;
            }

            if (tireSettings == null)
            {
                tireSettings = new VehicleTuning.TireSettings();
            }

            steerAngle = command.SteerAngle;
            int mask = groundMask.value == 0 ? Physics.DefaultRaycastLayers : groundMask.value;
            Vector3 up = transform.up;
            Vector3 down = -up;
            Vector3 origin = transform.position;
            float rayLength = suspensionRestLength + suspensionTravel + radius;

            RaycastHit hit;
            if (!body.gameObject.scene.GetPhysicsScene().Raycast(origin, down, out hit, rayLength, mask, QueryTriggerInteraction.Ignore))
            {
                SimulateAirborne(deltaTime, command);
                return;
            }

            Grounded = true;
            if (contactCollider != hit.collider)
            {
                contactCollider = hit.collider;
                contactSurface = contactCollider.GetComponentInParent<VehicleSurface>();
                weatherRegion = contactCollider.GetComponentInParent<WeatherSurfaceRegion>();
            }
            contactPoint = hit.point;
            contactNormal = hit.normal.sqrMagnitude > 0.001f ? hit.normal.normalized : up;
            suspensionLength = Mathf.Clamp(hit.distance - radius, 0f, suspensionRestLength + suspensionTravel);
            suspensionCompression = Mathf.Clamp01(
                (suspensionRestLength - suspensionLength) / Mathf.Max(0.001f, suspensionTravel));

            float suspensionVelocity = Vector3.Dot(body.GetPointVelocity(origin), up);
            float springForce = Mathf.Max(
                0f,
                (suspensionRestLength - suspensionLength) * springRate
                - suspensionVelocity * (suspensionVelocity > 0f ? reboundDamperRate : damperRate));
            NormalLoad = Mathf.Max(0f, springForce);
            body.AddForceAtPosition(up * springForce, origin, ForceMode.Force);

            VehicleSurface surface = contactSurface;
            surfaceGrip = surface != null ? surface.GripMultiplier : 1f;
            surfaceRollingResistance = surface != null ? surface.RollingResistanceMultiplier : 1f;
            surfaceName = surface != null ? surface.SurfaceName : "Road";

            Vector3 steeredForward = Quaternion.AngleAxis(steerAngle, up) * transform.forward;
            Vector3 forward = Vector3.ProjectOnPlane(steeredForward, contactNormal).normalized;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.ProjectOnPlane(transform.forward, contactNormal).normalized;
            }

            Vector3 right = Vector3.Cross(contactNormal, forward).normalized;
            Vector3 pointVelocity = body.GetPointVelocity(contactPoint);
            float longitudinalVelocity = Vector3.Dot(pointVelocity, forward);
            ContactForwardSpeed = longitudinalVelocity;
            float lateralVelocity = Vector3.Dot(pointVelocity, right);
            float speedDenominator = Mathf.Max(Mathf.Abs(longitudinalVelocity), 1f);

            LongitudinalSlip = (wheelAngularVelocity * radius - longitudinalVelocity) / speedDenominator;
            LateralSlipRadians = Mathf.Atan2(lateralVelocity, Mathf.Abs(longitudinalVelocity) + 1f);

            float wetFactor = weatherRegion == null ? 0f : Mathf.Clamp01(weatherRegion.Wetness);
            float surfaceFactor = Mathf.Max(0f, surfaceGrip) * Mathf.Lerp(1f, 0.72f, wetFactor);
            float maxLongitudinalForce = NormalLoad
                * tireSettings.longitudinalGrip
                * surfaceFactor
                * Mathf.Max(0f, command.LongitudinalGripMultiplier);
            float maxLateralForce = NormalLoad
                * tireSettings.lateralGrip
                * surfaceFactor
                * Mathf.Max(0f, command.LateralGripMultiplier);

            float lateralForce = -VehicleMath.EvaluateTireForce(
                LateralSlipRadians,
                tireSettings.peakLateralSlipRadians,
                maxLateralForce,
                tireSettings.postPeakGrip);

            // Wheel rotation/tire slip is much stiffer than chassis motion, especially at walking
            // speed. Integrate this small numerical subsystem in bounded substeps; raycasts, spring
            // forces and Rigidbody simulation still run once per physics tick. A single 20 ms Euler
            // step alternated positive/negative tire impulses even while the driver held full brakes.
            float inertia = Mathf.Max(0.01f, 0.5f * wheelMass * radius * radius);
            float peakSlip = Mathf.Max(0.0001f, tireSettings.peakLongitudinalSlip);
            float stiffness = maxLongitudinalForce * radius * radius
                / (peakSlip * speedDenominator * inertia);
            int substeps = Mathf.Clamp(Mathf.CeilToInt(deltaTime * stiffness / 0.8f), 1, 256);
            float substep = deltaTime / substeps;
            // Extreme authored stiffness cannot silently break the stability bound at the work
            // cap. Regularize the near-zero slip slope only when 256 substeps are insufficient.
            peakSlip = Mathf.Max(peakSlip, substep * maxLongitudinalForce * radius * radius / (0.8f * speedDenominator * inertia));
            Vector2 accumulated = Vector2.zero;
            for (int i = 0; i < substeps; i++)
            {
                float slip = (wheelAngularVelocity * radius - longitudinalVelocity) / speedDenominator;
                float longitudinalForce = VehicleMath.EvaluateTireForce(slip, peakSlip,
                    maxLongitudinalForce, tireSettings.postPeakGrip);
                Vector2 force = VehicleMath.ApplyFrictionCircle(new Vector2(longitudinalForce, lateralForce),
                    maxLongitudinalForce, maxLateralForce);
                accumulated += force;
                float rollingTorque = Mathf.Max(0, tireSettings.rollingResistance * surfaceRollingResistance * NormalLoad * radius);
                IntegrateWheelAngularVelocity(substep, command.DriveTorque, command.BrakeTorque, rollingTorque, force.x * radius);
            }
            LongitudinalSlip = (wheelAngularVelocity * radius - longitudinalVelocity) / speedDenominator;
            Vector2 average = accumulated / substeps;
            LongitudinalForce = average.x;
            LateralForce = average.y;
            body.AddForceAtPosition(forward * average.x + right * average.y, contactPoint, ForceMode.Force);
        }

        public void ApplyVisualPose(float deltaTime)
        {
            if (visual == null)
            {
                return;
            }

            Vector3 up = Grounded ? contactNormal : transform.up;
            Vector3 steeredForward = Quaternion.AngleAxis(steerAngle, transform.up) * transform.forward;
            Vector3 forward = Vector3.ProjectOnPlane(steeredForward, up).normalized;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = transform.forward;
            }

            visualSpinAngle += wheelAngularVelocity * deltaTime * Mathf.Rad2Deg;
            visual.position = transform.position - transform.up * suspensionLength;
            Quaternion wheelOrientation = Quaternion.LookRotation(forward, up);
            visual.rotation = wheelOrientation
                * Quaternion.AngleAxis(visualSpinAngle, Vector3.right)
                * Quaternion.Euler(visualRotationOffset);
        }

        private void SimulateAirborne(float deltaTime, VehicleWheelCommand command)
        {
            Grounded = false;
            NormalLoad = 0f;
            LongitudinalForce = 0f;
            LateralForce = 0f;
            LongitudinalSlip = 0f;
            LateralSlipRadians = 0f;
            contactCollider = null;
            contactSurface = null;
            weatherRegion = null;
            ContactForwardSpeed = 0;
            contactPoint = transform.position - transform.up * (suspensionRestLength + radius);
            contactNormal = transform.up;
            suspensionLength = suspensionRestLength + suspensionTravel;
            suspensionCompression = 0f;
            surfaceGrip = 0f;
            surfaceRollingResistance = 0f;
            surfaceName = "Air";
            IntegrateWheelAngularVelocity(deltaTime, command.DriveTorque, command.BrakeTorque, 0f, 0f);
        }

        private void IntegrateWheelAngularVelocity(
            float deltaTime,
            float driveTorque,
            float brakeTorque,
            float rollingTorque,
            float tireReactionTorque)
        {
            float inertia = Mathf.Max(0.01f, 0.5f * wheelMass * radius * radius);
            wheelAngularVelocity = VehicleMath.IntegrateBrakedWheel(wheelAngularVelocity,
                driveTorque - tireReactionTorque, Mathf.Max(0, brakeTorque) + Mathf.Max(0, rollingTorque), inertia, deltaTime);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawDebugGizmos)
            {
                return;
            }

            Gizmos.color = Grounded ? Color.green : Color.red;
            Gizmos.DrawLine(
                transform.position,
                transform.position - transform.up * (suspensionRestLength + suspensionTravel + radius));
            if (Grounded)
            {
                Gizmos.DrawSphere(contactPoint, 0.035f);
                Gizmos.DrawLine(contactPoint, contactPoint + contactNormal * 0.35f);
            }
        }
    }
}
