using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>A read-only snapshot of authoritative simulation, with interpolated render pose.</summary>
    public struct VehicleCameraFrame
    {
        public Vector3 position, velocity, localAcceleration, roadNormal, cockpitPosition;
        public Quaternion rotation, cockpitRotation;
        public float speedKph, yawRate, slipAngle, steeringAngle, roughness, suspensionRate;
        public bool grounded, nitrous, hasCockpit;
    }

    public struct VehicleCameraDiagnostics
    {
        public float speed, acceleration, longitudinalAcceleration, lateralAcceleration, yawRate, slipAngle;
        public float currentFov, baseFov, speedFov, accelerationFov, nitrousFov, driftFov;
        public float distance, lag, driftBlend, shakeStrength, lookAhead, landingStrength, motionBlur, peripheral;
        public bool parked;
    }

    public struct VehicleCameraPose
    {
        public Vector3 position, collisionOrigin;
        public Quaternion rotation;
        public float fieldOfView;
        public VehicleCameraDiagnostics debug;
    }

    internal struct CameraContributions
    {
        public Vector3 offset, angles;
        public float fov, extraDistance, heightReduction, shake, drift, landing, lookAhead, nitrous;
        public float speedFov, accelerationFov, nitrousFov, driftFov;
    }

    internal static class CameraResponse
    {
        public static float Blend(float response, float dt) => 1 - Mathf.Exp(-Mathf.Max(.01f, response) * dt);
        public static float Smooth(float value, float target, float rate, float dt) => Mathf.Lerp(value, target, Blend(rate, dt));
        public static float Curve(AnimationCurve curve, float x, float fallback = 0) => curve != null && curve.length > 0 ? curve.Evaluate(x) : fallback;
        public static float Noise(float time, float seed) => Mathf.PerlinNoise(time, seed) * 2 - 1;
    }
}
