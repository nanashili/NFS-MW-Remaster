using System;
using Unity.Profiling;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Vehicle telemetry capture owned directly by VehicleAudio.</summary>
    public sealed class VehicleFeedbackSampler : IVehicleFeedbackSource
    {
        private readonly VehicleController vehicle;
        private readonly SensorySurface chassisMaterial;
        private readonly FeedbackNormalizer normalizer;
        private Vector3 previousVelocity;
        private bool hasSample;
        private int sequence, epoch, impactSequence;
        private float scrapeUntil, scrapeTarget, scrape;
        private FeedbackImpact lastImpact;
        private SensorySurface scrapeMaterial;
        private static readonly ProfilerMarker CaptureMarker = new ProfilerMarker("Sensory.Telemetry");
        private static readonly ProfilerMarker NormalizeMarker = new ProfilerMarker("Sensory.Normalize");
        private static readonly ProfilerMarker ImpactMarker = new ProfilerMarker("Sensory.CollisionDispatch");

        public VehicleFeedbackSampler(VehicleController vehicle, FeedbackResponse response = null,
            SensorySurface chassisMaterial = SensorySurface.Metal)
        {
            this.vehicle = vehicle;
            this.chassisMaterial = chassisMaterial;
            normalizer = new FeedbackNormalizer(response ?? new FeedbackResponse());
            ResetHistory();
        }

        public VehicleFeedbackFrame Frame { get; private set; }
        public event Action<VehicleFeedbackFrame> Sampled;
        public event Action<FeedbackImpact> Impact;
        public VehicleController Vehicle => vehicle;

        public void ResetHistory()
        {
            epoch++; hasSample = false; normalizer.Reset(); Frame = default; lastImpact = default;
            scrape = scrapeTarget = scrapeUntil = 0;
        }

        public void Capture(float dt)
        {
            using (CaptureMarker.Auto())
            {
                if (vehicle == null) return;
                var body = vehicle.Body; var tuning = vehicle.Tuning;
                if (body == null || tuning == null || dt <= 0) return;
                var t = vehicle.Telemetry; var velocity = body.linearVelocity;
                var local = body.transform.InverseTransformDirection(velocity);
                var frame = new VehicleFeedbackFrame
                {
                    Sequence = ++sequence, Epoch = epoch, Time = Time.fixedTimeAsDouble,
                    Capabilities = FeedbackCapabilities.Motion | FeedbackCapabilities.Engine | FeedbackCapabilities.Wheels | FeedbackCapabilities.Nitrous,
                    Position = body.position, Rotation = body.rotation, Velocity = velocity, LocalVelocity = local,
                    AccelerationG = hasSample ? Vector3.ClampMagnitude(body.transform.InverseTransformDirection((velocity - previousVelocity) / dt) / 9.81f, 20) : Vector3.zero,
                    Speed = velocity.magnitude, YawRate = body.transform.InverseTransformDirection(body.angularVelocity).y,
                    SlipAngle = Mathf.Atan2(local.x, Mathf.Abs(local.z) + 0.1f), EngineRpm = t.EngineRpm, EngineRunning = vehicle.EngineRunning,
                    NormalizedRpm = Mathf.InverseLerp(tuning.engine.idleRpm, tuning.engine.redlineRpm, t.EngineRpm),
                    EngineTorque = t.EngineTorque, DrivetrainTorque = t.DrivetrainTorque,
                    EngineLoad = Mathf.Clamp(t.EngineTorque / Mathf.Max(1, tuning.engine.maxTorqueNewtonMeters), -1, 1),
                    Gear = t.Gear, Shifting = t.IsShifting, Throttle = t.Throttle, Brake = t.Brake,
                    NitrousFlow = t.NitrousActive ? t.Throttle : 0, NitrousRemaining = t.NitrousSeconds, LastImpact = lastImpact,
                    BodyMaterial = chassisMaterial
                };
                var wheels = vehicle.Wheels ?? Array.Empty<VehicleWheel>();
                if (wheels.Length > 8) { Debug.LogError("Feedback supports up to eight authored wheels.", vehicle); return; }
                frame.WheelCount = wheels.Length;
                for (int i = 0; i < frame.WheelCount; i++)
                {
                    var w = wheels[i];
                    frame.SetWheel(i, new WheelFeedback
                    {
                        Front = w.IsFrontWheel, Driven = w.IsDrivenWheel, Grounded = w.Grounded,
                        AngularSpeed = w.AngularVelocity, LinearSpeed = w.AngularVelocity * w.Radius,
                        RoadSpeed = w.ContactForwardSpeed, LongitudinalSlip = w.LongitudinalSlip,
                        LateralSlip = w.LateralSlipRadians, Load = w.NormalLoad, Compression = w.SuspensionCompression,
                        Point = w.ContactPoint, Normal = w.ContactNormal, Surface = w.SurfaceKind
                    });
                }
                if (hasSample)
                {
                    if (!Frame.EngineRunning && frame.EngineRunning) frame.Edges |= FeedbackEdges.Startup;
                    if (frame.Gear > Frame.Gear) frame.Edges |= FeedbackEdges.Upshift;
                    if (frame.Gear < Frame.Gear) frame.Edges |= FeedbackEdges.Downshift;
                    if (frame.NitrousFlow > 0 && Frame.NitrousFlow <= 0) frame.Edges |= FeedbackEdges.NitroOn;
                    if (frame.NitrousFlow <= 0 && Frame.NitrousFlow > 0) frame.Edges |= FeedbackEdges.NitroOff;
                    if (frame.NormalizedRpm >= 0.99f && Frame.NormalizedRpm < 0.99f) frame.Edges |= FeedbackEdges.Limiter;
                    if (frame.EngineLoad <= 0 && Frame.EngineLoad > 0.3f && frame.NormalizedRpm > 0.5f) frame.Edges |= FeedbackEdges.Overrun;
                }
                scrape = SensoryMath.Envelope(scrape, Time.time < scrapeUntil ? scrapeTarget : 0, dt, 0.08f, 0.15f);
                frame.Scrape = scrape; frame.ScrapeMaterial = scrapeMaterial; scrapeTarget = 0;
                using (NormalizeMarker.Auto()) Frame = normalizer.Step(frame, dt);
                previousVelocity = velocity; hasSample = true; Sampled?.Invoke(Frame);
            }
        }

        public void RecordImpact(Collision collision)
        {
            if (collision == null || collision.contactCount == 0 || vehicle == null || vehicle.Body == null) return;
            using (ImpactMarker.Auto())
            {
                var fact = Classify(collision);
                if (fact.Severity < 0.035f) return;
                lastImpact = fact; Impact?.Invoke(fact);
            }
        }

        public void RecordScrape(Collision collision)
        {
            if (collision == null || collision.contactCount == 0 || vehicle == null || vehicle.Body == null) return;
            var point = collision.GetContact(0); var surface = collision.collider.GetComponentInParent<VehicleSurface>();
            float tangent = Vector3.ProjectOnPlane(collision.relativeVelocity, point.normal).magnitude;
            float intensity = SensoryMath.Unit(tangent / 20f) * SensoryMath.Unit(collision.impulse.magnitude / Mathf.Max(1, vehicle.Body.mass));
            if (Time.time >= scrapeUntil) scrapeTarget = 0;
            if (intensity >= scrapeTarget) scrapeMaterial = surface != null ? surface.Kind : SensorySurface.Unknown;
            scrapeTarget = Mathf.Max(scrapeTarget, intensity); scrapeUntil = Time.time + Time.fixedDeltaTime * 2.1f;
        }

        private FeedbackImpact Classify(Collision collision)
        {
            ContactPoint point = collision.GetContact(0); float strongest = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, point.normal));
            for (int i = 1; i < collision.contactCount; i++)
            {
                var candidate = collision.GetContact(i); float candidateSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, candidate.normal));
                if (candidateSpeed > strongest) { strongest = candidateSpeed; point = candidate; }
            }
            float normal = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, point.normal));
            var surface = collision.collider.GetComponentInParent<VehicleSurface>();
            return new FeedbackImpact
            {
                Sequence = ++impactSequence, Time = Time.timeAsDouble, Point = point.point, Normal = point.normal,
                LocalDirection = vehicle.transform.InverseTransformDirection(point.normal), NormalSpeed = normal,
                TangentSpeed = Vector3.ProjectOnPlane(collision.relativeVelocity, point.normal).magnitude, Impulse = collision.impulse.magnitude,
                Severity = ImpactClassifier.Severity(normal, collision.impulse.magnitude, vehicle.Body.mass),
                Material = surface != null ? surface.Kind : SensorySurface.Unknown, BodyMaterial = chassisMaterial
            };
        }
    }
}
