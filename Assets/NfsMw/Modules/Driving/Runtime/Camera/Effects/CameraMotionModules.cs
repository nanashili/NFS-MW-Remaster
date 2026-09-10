using UnityEngine;

namespace NfsMwRemaster.Driving
{
    internal static class CameraShakeModule
    {
        public static void Apply(VehicleCameraProfile.Shake p, float speed, float time, bool enabled, ref CameraContributions c)
        {
            if (!enabled || speed < .5f) { c.shake = 0; return; }
            c.shake = Mathf.Clamp(c.shake + CameraResponse.Curve(p.speedCurve, speed), 0, p.maximumStrength);
            if (c.shake <= .0001f) return;
            float t = time * p.noiseFrequency;
            var noise = new Vector3(CameraResponse.Noise(t, 1.3f), CameraResponse.Noise(t, 5.7f), CameraResponse.Noise(t, 9.1f));
            c.offset += Vector3.Scale(noise, p.positionAmplitude) * c.shake;
            c.angles += Vector3.Scale(noise, p.rotationAmplitude) * c.shake;
        }
    }
    internal sealed class RoadMotionModule
    {
        private float suspension;
        private Vector3 normal = Vector3.up;
        public void Reset() { suspension = 0; normal = Vector3.up; }
        public void Apply(VehicleCameraProfile.Road p, in VehicleCameraFrame f, float time, float dt, bool enabled, ref CameraContributions c)
        {
            bool active = enabled && f.grounded && f.speedKph > 2;
            suspension = CameraResponse.Smooth(suspension, active ? f.suspensionRate : 0, 8, dt);
            normal = Vector3.Slerp(normal, active ? f.roadNormal : Vector3.up, CameraResponse.Blend(4, dt));
            float strength = active ? Mathf.Clamp01(CameraResponse.Curve(p.speedCurve, f.speedKph)) * Mathf.Clamp01(f.roughness) : 0;
            float noise = strength > 0 ? CameraResponse.Noise(time * p.frequency, 21.7f) * strength : 0;
            c.offset.y += noise * p.positionAmplitude - Mathf.Clamp(suspension, -1, 1) * p.suspensionInfluence;
            c.angles.x += noise * p.rotationAmplitude;
            Vector3 localNormal = Quaternion.Inverse(Quaternion.Euler(0, f.rotation.eulerAngles.y, 0)) * normal;
            c.angles.x += Mathf.Clamp(Mathf.Atan2(localNormal.z, localNormal.y) * Mathf.Rad2Deg, -15, 15) * p.normalInfluence;
        }
    }
    internal sealed class LandingResponseModule
    {
        private float airTime, impact, downward, groundedTime;
        private bool wasGrounded = true;
        public void Reset() { airTime = impact = downward = groundedTime = 0; wasGrounded = true; }
        public void Apply(VehicleCameraProfile.Landing p, in VehicleCameraFrame f, bool enabled, float dt, ref CameraContributions c)
        {
            impact *= Mathf.Exp(-p.recoveryResponse * dt);
            if (!f.grounded)
            {
                airTime += dt; groundedTime = 0;
                downward = Mathf.Max(0, -f.velocity.y);
            }
            else
            {
                groundedTime += dt;
                if (!wasGrounded && airTime >= p.minimumAirTime && enabled)
                    impact = Mathf.Max(impact, Mathf.InverseLerp(p.minimumImpactSpeed, Mathf.Max(p.minimumImpactSpeed + .1f, p.fullImpactSpeed), downward));
                if (groundedTime > .06f) { airTime = 0; downward = 0; }
            }
            wasGrounded = f.grounded;
            if (!enabled) impact = 0;
            c.offset.y -= impact * p.positionImpulse; c.angles.x += impact * p.pitchImpulse; c.landing = impact;
        }
    }
}
