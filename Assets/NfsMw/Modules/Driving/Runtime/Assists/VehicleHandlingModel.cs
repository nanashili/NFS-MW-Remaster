using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Serializable]
    public sealed class VehicleHandlingSettings
    {
        public bool brakeToDrift = true;
        [Range(0f, 1f)] public float driftBias = 0.5f;
        [Min(0f)] public float minimumSpeedKph = 30f;
        [Min(0f)] public float retriggerSeconds = 0.7f;
        [Min(0f)] public float maximumYawTorque = 2400f;
        [Min(0f)] public float yawGain = 1800f;
    }

    /// <summary>Fixed-step handling decisions; outputs torque in Nm, never changes physical state.</summary>
    public sealed class VehicleHandlingModel
    {
        public VehicleHandlingMode Mode { get; private set; }
        public float YawTorque { get; private set; }
        private bool brakeHeld;
        private bool handbrakeHeld;
        private float cooldown;
        private float phaseTime;
        private float direction;

        public void Reset()
        {
            Mode = VehicleHandlingMode.Grip;
            brakeHeld = false;
            handbrakeHeld = false;
            cooldown = phaseTime = direction = YawTorque = 0f;
        }

        public float Step(VehicleHandlingSettings settings, VehicleInputState input,
            float forwardSpeed, float lateralSpeed, float yawRate, bool supported,
            bool stabilityEnabled, float stabilityStrength, float deltaTime)
        {
            YawTorque = 0f;
            if (settings == null || !Finite(deltaTime) || deltaTime <= 0f ||
                !Finite(forwardSpeed) || !Finite(lateralSpeed) || !Finite(yawRate))
            {
                Reset();
                return 0f;
            }
            input = input.Clamped();
            bool brakeEdge = input.Brake >= 0.35f && !brakeHeld;
            bool handbrakeEdge = input.Handbrake && !handbrakeHeld;
            handbrakeHeld = input.Handbrake;
            if (input.Brake <= 0.15f) brakeHeld = false;
            else if (input.Brake >= 0.35f) brakeHeld = true;
            cooldown = Mathf.Max(0f, cooldown - deltaTime);
            phaseTime += deltaTime;
            if (!supported)
            {
                SetMode(VehicleHandlingMode.Airborne);
                return 0f;
            }
            if (Mode == VehicleHandlingMode.Airborne) SetMode(VehicleHandlingMode.Grip);
            float bias = Mathf.Clamp01(settings.driftBias);
            float minimumSpeed = Mathf.Max(0f, settings.minimumSpeedKph) / 3.6f;
            bool eligible = forwardSpeed >= minimumSpeed && Mathf.Abs(input.Steering) >= 0.2f;
            bool trigger = handbrakeEdge || (settings.brakeToDrift && brakeEdge && input.Throttle < 0.75f);
            if (Mode == VehicleHandlingMode.Grip && eligible && trigger && cooldown <= 0f && bias > 0f)
            {
                direction = Mathf.Sign(input.Steering);
                cooldown = Mathf.Max(0f, settings.retriggerSeconds);
                SetMode(VehicleHandlingMode.Initiating);
            }
            float angle = Mathf.Atan2(lateralSpeed, Mathf.Max(1f, Mathf.Abs(forwardSpeed)));
            if (Mode == VehicleHandlingMode.Initiating && phaseTime >= 0.25f)
                SetMode(Mathf.Abs(angle) >= 0.08f ? VehicleHandlingMode.Drifting : VehicleHandlingMode.Recovering);
            if ((Mode == VehicleHandlingMode.Drifting || Mode == VehicleHandlingMode.Initiating) &&
                (forwardSpeed < minimumSpeed * 0.75f || (!input.Handbrake && input.Throttle < 0.1f && phaseTime > 0.5f)))
                SetMode(VehicleHandlingMode.Recovering);
            if (Mode == VehicleHandlingMode.Drifting && Mathf.Abs(angle) < 0.04f && phaseTime > 0.5f)
                SetMode(VehicleHandlingMode.Recovering);
            if (Mode == VehicleHandlingMode.Recovering && phaseTime >= 0.4f)
                SetMode(VehicleHandlingMode.Grip);

            float targetYaw = input.Steering * Mathf.Clamp(forwardSpeed / 15f, -1.5f, 1.5f);
            float gain = stabilityEnabled ? Mathf.Clamp01(stabilityStrength) : 0f;
            if (Mode == VehicleHandlingMode.Initiating)
            {
                targetYaw = direction * Mathf.Lerp(0.4f, 1.2f, bias);
                gain = bias;
            }
            else if (Mode == VehicleHandlingMode.Drifting)
            {
                // Countersteer reduces the target rather than instantly reversing drift direction.
                targetYaw = direction * Mathf.Lerp(0.2f, 0.85f, bias) + input.Steering * 0.3f;
                gain = bias * Mathf.Clamp01(input.Throttle + 0.25f);
            }
            float torque = (targetYaw - yawRate) * Mathf.Max(0f, settings.yawGain) * gain;
            float cap = Mathf.Max(0f, settings.maximumYawTorque);
            YawTorque = Finite(torque) && Finite(cap) ? Mathf.Clamp(torque, -cap, cap) : 0f;
            if (Mathf.Abs(forwardSpeed) < 2f) YawTorque = 0f;
            return YawTorque;
        }

        public float Step(VehicleHandlingSettings settings, VehicleInputState input,
            float forwardSpeed, float lateralSpeed, float yawRate, bool supported,
            bool stabilityEnabled, float deltaTime) => Step(settings, input, forwardSpeed,
                lateralSpeed, yawRate, supported, stabilityEnabled, 0.25f, deltaTime);

        private void SetMode(VehicleHandlingMode next)
        {
            if (Mode == next) return;
            Mode = next;
            phaseTime = 0f;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
