using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Optional driver aids. They operate on wheel telemetry and chassis yaw,
    /// which keeps the tire model honest while giving the forgiving, tunable
    /// feel expected from a street-racing game.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleAssists : MonoBehaviour
    {
        private Rigidbody body;
        private VehicleTuning tuning;
        private VehicleWheel[] wheels;
        public float LastTractionControlReduction { get; private set; }
        public float LastAbsReduction { get; private set; }
        public float LastCountersteering { get; private set; }
        private float impactCooldown;

        public void BeginStep(float deltaTime)
        {
            LastTractionControlReduction = LastAbsReduction = LastCountersteering = 0f;
            impactCooldown = Mathf.Max(0, impactCooldown - deltaTime);
        }

        public float ResolveSteering(float steering, float forwardSpeed, bool handbrake)
        {
            if (body == null || tuning == null || !tuning.assists.countersteering || forwardSpeed < 3f || handbrake || impactCooldown > 0) return steering;
            int contacts = 0; if (wheels != null) foreach (var wheel in wheels) if (wheel && wheel.Grounded) contacts++;
            if (contacts < 2) return steering;
            float lateral = Vector3.Dot(body.linearVelocity, transform.right);
            float correction = Mathf.Atan2(lateral, forwardSpeed) * Mathf.Rad2Deg / Mathf.Max(1f, tuning.controls.maxSteerAngle);
            float resolved = Mathf.Clamp(steering + Mathf.Clamp(correction * tuning.assists.countersteeringStrength, -.35f, .35f), -1f, 1f);
            LastCountersteering = resolved - steering;
            return resolved;
        }

        private void OnCollisionEnter(Collision collision)
        { if (collision.relativeVelocity.sqrMagnitude > 4f) { impactCooldown = .15f; handling.Reset(); } }

        public void Configure(Rigidbody configuredBody, VehicleTuning configuredTuning, VehicleWheel[] configuredWheels)
        {
            body = configuredBody;
            tuning = configuredTuning != null ? configuredTuning : VehicleTuning.CreateStreetRacer();
            wheels = configuredWheels;
            handling.Reset();
            LastTractionControlReduction = LastAbsReduction = 0f;
            LastCountersteering = impactCooldown = 0f;
        }

        public float GetDriveTorqueMultiplier(VehicleWheel wheel)
        {
            if (!tuning.assists.tractionControl || wheel == null || !wheel.Grounded || !wheel.IsDrivenWheel)
            {
                return 1f;
            }

            float slip = Mathf.Abs(wheel.LongitudinalSlip);
            float limit = Mathf.Max(0.001f, tuning.assists.tractionSlipLimit);
            if (slip <= limit)
            {
                return 1f;
            }
            float result = Mathf.Lerp(1f, 0.10f, Mathf.InverseLerp(limit, limit + 0.24f, slip));
            LastTractionControlReduction = Mathf.Max(LastTractionControlReduction, 1f - result);
            return result;
        }

        public float GetBrakeTorqueMultiplier(VehicleWheel wheel, bool braking)
        {
            if (!braking || !tuning.assists.abs || wheel == null || !wheel.Grounded)
            {
                return 1f;
            }

            float slip = Mathf.Abs(wheel.LongitudinalSlip);
            float limit = Mathf.Max(0.001f, tuning.assists.absSlipLimit);
            if (slip <= limit)
            {
                return 1f;
            }
            float result = Mathf.Lerp(1f, 0.16f, Mathf.InverseLerp(limit, limit + 0.28f, slip));
            LastAbsReduction = Mathf.Max(LastAbsReduction, 1f - result);
            return result;
        }

        public VehicleHandlingMode HandlingMode => handling.Mode;
        public float AppliedYawTorque => handling.YawTorque;
        private readonly VehicleHandlingModel handling = new VehicleHandlingModel();

        public void ResetHandling() => handling.Reset();

        public void ApplyPostWheelForces(float forwardSpeed, VehicleInputState input, float deltaTime)
        {
            if (body == null || tuning == null || impactCooldown > 0) return;
            int contacts = 0;
            if (wheels != null)
                for (int i = 0; i < wheels.Length; i++)
                    if (wheels[i] != null && wheels[i].Grounded) contacts++;
            Vector3 localVelocity = transform.InverseTransformDirection(body.linearVelocity);
            float torque = handling.Step(tuning.handling, input, forwardSpeed, localVelocity.x,
                Vector3.Dot(body.angularVelocity, transform.up), contacts >= 2,
                tuning.assists.stabilityControl, tuning.assists.stabilityStrength, deltaTime);
            body.AddTorque(transform.up * torque, ForceMode.Force);
        }
    }
}
