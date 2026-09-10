using UnityEngine;

namespace NfsMwRemaster.Driving
{
    internal sealed class VehicleCameraTelemetrySampler
    {
        private Vector3 previousVelocity, acceleration;
        private float previousCompression;
        private bool initialized;
        private VehicleCameraFrame sample;
        public void Reset() { initialized = false; acceleration = Vector3.zero; sample = default; }
        public void Sample(Rigidbody body, VehicleWheel[] wheels, bool nitrous, float dt)
        {
            if (!body || dt <= 0) return;
            Vector3 velocity = body.linearVelocity;
            bool valid = initialized && dt < .2f;
            Vector3 measured = valid ? (velocity - previousVelocity) / dt : Vector3.zero;
            // Gravity is not longitudinal acceleration when airborne. A jump must not create a throttle kick.
            int grounded = 0; float compression = 0, roughness = 0, steer = 0; int steerCount = 0;
            Vector3 normal = Vector3.zero;
            if (wheels != null) for (int i = 0; i < wheels.Length; i++)
            {
                VehicleWheel wheel = wheels[i]; if (!wheel) continue;
                if (wheel.IsSteeringWheel) { steer += wheel.SteerAngle; steerCount++; }
                if (!wheel.Grounded) continue;
                grounded++; compression += wheel.SuspensionCompression; normal += wheel.ContactNormal;
                roughness += wheel.CameraRoughness;
            }
            bool onGround = wheels == null || wheels.Length == 0 || grounded > 0;
            if (!onGround) measured -= Physics.gravity;
            if (velocity.magnitude < .14f) measured = Vector3.zero;
            acceleration = Vector3.Lerp(acceleration, Vector3.ClampMagnitude(measured, 40), CameraResponse.Blend(12, dt));
            float avg = grounded > 0 ? compression / grounded : 0;
            sample = new VehicleCameraFrame {
                velocity = velocity, speedKph = velocity.magnitude * 3.6f,
                localAcceleration = Quaternion.Inverse(body.rotation) * acceleration,
                yawRate = body.angularVelocity.y * Mathf.Rad2Deg, steeringAngle = steerCount > 0 ? steer / steerCount : 0,
                grounded = onGround, roadNormal = grounded > 0 ? normal.normalized : Vector3.up,
                roughness = grounded > 0 ? roughness / grounded : 0,
                suspensionRate = valid && grounded > 0 ? Mathf.Clamp((avg - previousCompression) / dt, -3, 3) : 0,
                nitrous = nitrous
            };
            Vector3 localVelocity = Quaternion.Inverse(body.rotation) * velocity;
            sample.slipAngle = sample.speedKph > 2 ? Mathf.Atan2(localVelocity.x, Mathf.Max(.1f, Mathf.Abs(localVelocity.z))) * Mathf.Rad2Deg : 0;
            previousVelocity = velocity; previousCompression = avg; initialized = true;
        }
        public VehicleCameraFrame Frame(Transform target, Transform cockpit)
        {
            var frame = sample;
            frame.position = target.position; frame.rotation = target.rotation;
            frame.hasCockpit = cockpit;
            frame.cockpitPosition = cockpit ? cockpit.position : target.TransformPoint(new Vector3(0, 1.15f, .75f));
            frame.cockpitRotation = cockpit ? cockpit.rotation : target.rotation;
            return frame;
        }
    }
}
