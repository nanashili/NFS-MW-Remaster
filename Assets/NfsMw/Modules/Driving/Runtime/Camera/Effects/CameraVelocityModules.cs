using UnityEngine;

namespace NfsMwRemaster.Driving
{
    internal sealed class SpeedFovModule
    {
        private float value;
        public void Reset() => value = 0;
        public void Apply(VehicleCameraProfile.Speed p, float speed, bool enabled, float dt, ref CameraContributions c)
        {
            float target = enabled ? CameraResponse.Curve(p.speedToFov, speed, p.minimumFov) - p.minimumFov : 0;
            value = CameraResponse.Smooth(value, target, target > value ? p.expansionResponse : p.contractionResponse, dt);
            c.fov += value; c.speedFov = value;
        }
    }
    internal sealed class SpeedDistanceModule
    {
        private float value;
        public void Reset() => value = 0;
        public void Apply(VehicleCameraProfile.Speed p, float speed, bool enabled, float dt, ref CameraContributions c)
        {
            float target = enabled ? Mathf.Clamp01(CameraResponse.Curve(p.speedToDistance, speed)) : 0;
            value = CameraResponse.Smooth(value, target, p.distanceResponse, dt);
            c.extraDistance += value * p.maximumExtraDistance; c.heightReduction = value * p.heightReduction;
        }
    }
    internal sealed class AccelerationResponseModule
    {
        private float fov, pitch, brake;
        private Vector3 offset;
        public void Reset() { fov = pitch = brake = 0; offset = Vector3.zero; }
        public void Apply(VehicleCameraProfile.Acceleration p, in VehicleCameraFrame f, bool enabled, float dt, ref CameraContributions c)
        {
            float a = enabled && f.speedKph > .5f ? f.localAcceleration.z : 0;
            float positive = Mathf.Max(0, a - p.threshold), negative = Mathf.Max(0, -a - p.threshold);
            float targetFov = Mathf.Min(p.maximumAdditionalFov, positive * p.fovStrength) - Mathf.Clamp01(negative / 9.81f) * p.brakingFov;
            fov = CameraResponse.Smooth(fov, targetFov, Mathf.Abs(targetFov) > Mathf.Abs(fov) ? p.attackResponse : p.recoveryResponse, dt);
            Vector3 targetOffset = new Vector3(enabled && f.speedKph > .5f ? -f.localAcceleration.x / 9.81f * p.lateralLag : 0,
                0, -Mathf.Clamp01(positive / 9.81f) * p.forwardLag + Mathf.Clamp01(negative / 9.81f) * p.brakingCompression);
            offset = Vector3.Lerp(offset, Vector3.ClampMagnitude(targetOffset, p.maximumOffset), CameraResponse.Blend(p.lagRecovery, dt));
            pitch = CameraResponse.Smooth(pitch, Mathf.Clamp(-a / 9.81f, -1, 1) * p.pitchStrength, p.lagRecovery, dt);
            // Brief onset response; constant braking does not sustain a large vibration.
            float nextBrake = Mathf.Clamp01(negative / 9.81f);
            c.shake += Mathf.Max(0, nextBrake - brake) * p.brakingShake;
            brake = CameraResponse.Smooth(brake, nextBrake, p.attackResponse, dt);
            c.offset += offset; c.angles.x += pitch; c.fov += fov; c.accelerationFov = fov;
        }
    }
    internal sealed class SteeringResponseModule
    {
        private float value;
        public void Reset() => value = 0;
        public void Apply(VehicleCameraProfile.Cornering p, in VehicleCameraFrame f, bool enabled, float dt, ref CameraContributions c)
        {
            float yaw = Mathf.Sign(f.yawRate) * CameraResponse.Curve(p.yawRateResponse, Mathf.Abs(f.yawRate));
            float steer = Mathf.Sign(f.steeringAngle) * CameraResponse.Curve(p.steeringAngleResponse, Mathf.Abs(f.steeringAngle));
            float target = enabled && f.grounded && f.speedKph > 2 ? (yaw * .8f + steer * .2f) * CameraResponse.Curve(p.speedResponse, f.speedKph) : 0;
            value = CameraResponse.Smooth(value, Mathf.Clamp(target, -1, 1), p.response, dt);
            c.angles.y += value * p.yawStrength; c.angles.z -= value * p.roll;
            c.offset.x -= value * p.lateralOffset;
        }
    }
    internal sealed class DriftCameraModule
    {
        private float blend, angle;
        public void Reset() { blend = angle = 0; }
        public void Apply(VehicleCameraProfile.Drift p, in VehicleCameraFrame f, bool enabled, float dt, ref CameraContributions c)
        {
            float target = enabled && f.grounded && f.speedKph >= p.minimumSpeed ?
                Mathf.Clamp01(CameraResponse.Curve(p.blend, Mathf.InverseLerp(p.slipThreshold, Mathf.Max(p.slipThreshold + .1f, p.fullSlipAngle), Mathf.Abs(f.slipAngle)))) : 0;
            blend = CameraResponse.Smooth(blend, target, target > blend ? p.attackResponse : p.recoveryResponse, dt);
            angle = CameraResponse.Smooth(angle, f.slipAngle * blend, p.attackResponse, dt);
            c.angles.y += angle * p.yawInfluence;
            c.offset.x += Mathf.Clamp(angle / 35, -1, 1) * p.horizontalOffset;
            c.offset.z -= blend * p.additionalLag;
            c.driftFov = blend * p.additionalFov; c.fov += c.driftFov; c.drift = blend;
        }
    }
    internal sealed class NitrousCameraModule
    {
        private float blend;
        public void Reset() => blend = 0;
        public void Apply(VehicleCameraProfile.Nitrous p, bool active, float dt, ref CameraContributions c)
        {
            blend = CameraResponse.Smooth(blend, active ? 1 : 0, active ? p.attackResponse : p.recoveryResponse, dt);
            c.nitrous = blend; c.nitrousFov = blend * p.fovBoost; c.fov += c.nitrousFov;
            c.offset.z -= blend * (p.pullback + p.additionalLag); c.shake += blend * p.shakeBoost;
        }
    }
}
