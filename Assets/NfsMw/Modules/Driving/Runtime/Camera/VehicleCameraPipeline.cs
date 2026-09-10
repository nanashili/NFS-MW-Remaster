using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>One deterministic compositor. Modules never access a Camera or change the vehicle.</summary>
    public sealed class VehicleCameraPipeline
    {
        private readonly CameraFollowModule follow = new CameraFollowModule();
        private readonly SpeedFovModule speedFov = new SpeedFovModule();
        private readonly SpeedDistanceModule distance = new SpeedDistanceModule();
        private readonly AccelerationResponseModule acceleration = new AccelerationResponseModule();
        private readonly SteeringResponseModule steering = new SteeringResponseModule();
        private readonly DriftCameraModule drift = new DriftCameraModule();
        private readonly NitrousCameraModule nitrous = new NitrousCameraModule();
        private readonly RoadMotionModule road = new RoadMotionModule();
        private readonly LandingResponseModule landing = new LandingResponseModule();
        private float time, stationaryTime, fov;
        private bool initialized, parked;
        private VehicleCameraFrame parkedFrame;
        private VehicleCameraPose last;
        public void Reset()
        {
            follow.Reset(); speedFov.Reset(); distance.Reset(); acceleration.Reset(); steering.Reset();
            drift.Reset(); nitrous.Reset(); road.Reset(); landing.Reset();
            time = stationaryTime = 0; initialized = parked = false;
        }
        public VehicleCameraPose Resolve(VehicleCameraProfile profile, VehicleCameraFrame frame, bool cockpit, VehicleCameraEffects mask, float dt, float aspect = 16f / 9f)
        {
            if (dt <= 0 && initialized) return last;
            if (dt > .25f) Reset();
            dt = Mathf.Clamp(dt, .0001f, .05f); time += dt;
            var p = cockpit ? profile.cockpit : profile.chase;
            bool stable = frame.grounded && frame.speedKph < .5f && Mathf.Abs(frame.yawRate) < 1;
            stationaryTime = stable ? stationaryTime + dt : 0;
            if (parked && (!stable || Vector3.Distance(frame.position, parkedFrame.position) > .12f || Quaternion.Angle(frame.rotation, parkedFrame.rotation) > 2))
            { parked = false; stationaryTime = 0; }
            if (!parked && stationaryTime >= .35f) { parkedFrame = frame; parked = true; }
            if (parked)
            {
                frame = parkedFrame; frame.speedKph = 0; frame.velocity = frame.localAcceleration = Vector3.zero;
                frame.yawRate = frame.slipAngle = frame.steeringAngle = frame.suspensionRate = 0;
                frame.roadNormal = Vector3.up; frame.nitrous = false;
            }
            var effects = mask & profile.effects;
            var c = new CameraContributions();
            speedFov.Apply(p.speed, frame.speedKph, Has(effects, VehicleCameraEffects.SpeedFov), dt, ref c);
            distance.Apply(p.speed, frame.speedKph, !cockpit && Has(effects, VehicleCameraEffects.SpeedDistance), dt, ref c);
            acceleration.Apply(p.acceleration, frame, Has(effects, VehicleCameraEffects.Acceleration), dt, ref c);
            steering.Apply(p.cornering, frame, Has(effects, VehicleCameraEffects.Steering), dt, ref c);
            drift.Apply(p.drift, frame, Has(effects, VehicleCameraEffects.Drift), dt, ref c);
            nitrous.Apply(p.nitrous, frame.nitrous && frame.speedKph > .5f && Has(effects, VehicleCameraEffects.Nitrous), dt, ref c);
            landing.Apply(p.landing, frame, Has(effects, VehicleCameraEffects.Landing), dt, ref c);
            CameraShakeModule.Apply(p.shake, frame.speedKph, time, Has(effects, VehicleCameraEffects.Shake), ref c);
            road.Apply(p.road, frame, time, dt, Has(effects, VehicleCameraEffects.RoadMotion), ref c);
            var result = follow.Resolve(p, frame, cockpit, Has(effects, VehicleCameraEffects.LookAhead), dt, c);
            float targetFov = Mathf.Clamp(p.speed.minimumFov + c.fov, p.speed.minimumFov, Mathf.Max(p.speed.minimumFov, p.speed.maximumFov));
            // Effects have their own attack/recovery; one final smoother also protects live profile edits.
            if (!initialized) fov = p.speed.minimumFov;
            fov = CameraResponse.Smooth(fov, targetFov, targetFov > fov ? Mathf.Max(p.speed.expansionResponse, c.nitrous > .01f ? p.nitrous.attackResponse : p.acceleration.attackResponse) : p.speed.contractionResponse, dt);
            result.fieldOfView = profile.fovAxis == VehicleCameraFovAxis.Horizontal ? Camera.HorizontalToVerticalFieldOfView(fov, Mathf.Max(.1f, aspect)) : fov;
            var d = result.debug;
            d.speed = frame.speedKph; d.acceleration = frame.localAcceleration.magnitude;
            d.longitudinalAcceleration = frame.localAcceleration.z; d.lateralAcceleration = frame.localAcceleration.x;
            d.yawRate = frame.yawRate; d.slipAngle = frame.slipAngle; d.currentFov = result.fieldOfView;
            d.baseFov = p.speed.minimumFov; d.speedFov = c.speedFov; d.accelerationFov = c.accelerationFov; d.nitrousFov = c.nitrousFov; d.driftFov = c.driftFov;
            d.driftBlend = c.drift; d.shakeStrength = c.shake; d.landingStrength = c.landing; d.parked = parked;
            var post = profile.postProcessing;
            float amount = frame.speedKph >= post.speedThreshold ? Mathf.Clamp01(CameraResponse.Curve(post.speedResponse, frame.speedKph)) : 0;
            float scale = cockpit ? post.cockpitScale : 1;
            d.motionBlur = Has(effects, VehicleCameraEffects.MotionBlur) ? Mathf.Clamp(amount * post.maximumMotionBlur + c.nitrous * post.nitrousAdditionalBlur, 0, .4f) * scale : 0;
            d.peripheral = Has(effects, VehicleCameraEffects.Peripheral) ? Mathf.Clamp01(amount * .35f + c.nitrous) * post.peripheralChromatic * scale : 0;
            result.debug = d; last = result; initialized = true; return result;
        }
        private static bool Has(VehicleCameraEffects mask, VehicleCameraEffects effect) => (mask & effect) != 0;
    }
}
