using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Small pure functions shared by the runtime and edit-mode tests.
    /// Keeping the control curves here makes tuning deterministic and easy to
    /// replace with AI/replay inputs later.
    /// </summary>
    public static class VehicleMath
    {
        // Braking is a dissipative impulse, not a signed engine torque. An explicit signed Euler
        // brake can overshoot zero, reverse the wheel, and inject energy on alternating ticks.
        // Resolve free angular momentum first, then oppose it by at most the available brake impulse.
        public static float IntegrateBrakedWheel(float angularVelocity, float externalTorque, float brakeTorque, float inertia, float dt)
        {
            float free = angularVelocity + externalTorque * dt / Mathf.Max(0.01f, inertia);
            float braked = Mathf.MoveTowards(free, 0, Mathf.Max(0, brakeTorque) * dt / Mathf.Max(0.01f, inertia));
            return Mathf.Clamp(braked, -2500, 2500);
        }
        public static float SmoothInput(float current, float target, float response, float deltaTime)
        {
            return Mathf.MoveTowards(current, target, Mathf.Max(0f, response) * Mathf.Max(0f, deltaTime));
        }

        public static float ShapeSteering(float input, float speedKph, VehicleTuning.ControlSettings settings)
        {
            float shaped = Mathf.Sign(input) * Mathf.Pow(Mathf.Abs(input), Mathf.Max(0.1f, settings.steeringExponent));
            float speedT = Mathf.InverseLerp(0f, Mathf.Max(1f, settings.highSpeedSteerKph), Mathf.Abs(speedKph));
            float speedScale = Mathf.Lerp(1f, settings.highSpeedSteerScale, speedT);
            return Mathf.Clamp(shaped * speedScale, -1f, 1f);
        }

        public static float EvaluateEngineTorque(
            VehicleTuning tuning, float rpm)
        {
            if (tuning.UsesMostWantedReference) return MostWantedVehicleMath.Torque(tuning, rpm);
            var settings = tuning.engine;
            float curve = settings.torqueCurve == null ? 1f : Mathf.Max(0f, settings.torqueCurve.Evaluate(
                Mathf.InverseLerp(settings.idleRpm, settings.redlineRpm, rpm)));
            return EvaluateEngineTorque(rpm, settings.idleRpm, settings.redlineRpm,
                settings.peakTorqueRpm, settings.maxTorqueNewtonMeters) * curve;
        }

        public static float EvaluateEngineTorque(
            float rpm,
            float idleRpm,
            float redlineRpm,
            float peakTorqueRpm,
            float maxTorqueNewtonMeters)
        {
            float safeIdle = Mathf.Max(1f, idleRpm);
            float safeRedline = Mathf.Max(safeIdle + 1f, redlineRpm);
            float safePeak = Mathf.Clamp(peakTorqueRpm, safeIdle, safeRedline);
            float normalized;

            if (rpm <= safePeak)
            {
                normalized = Mathf.InverseLerp(safeIdle, safePeak, Mathf.Max(safeIdle, rpm));
                normalized = Mathf.Lerp(0.72f, 1f, Mathf.SmoothStep(0f, 1f, normalized));
            }
            else
            {
                normalized = Mathf.InverseLerp(safePeak, safeRedline, Mathf.Min(safeRedline, rpm));
                normalized = Mathf.Lerp(1f, 0.62f, normalized);
            }

            if (rpm > safeRedline)
            {
                float limiterT = Mathf.InverseLerp(safeRedline, safeRedline + 500f, rpm);
                normalized *= 1f - Mathf.SmoothStep(0f, 1f, limiterT);
            }

            return Mathf.Max(0f, maxTorqueNewtonMeters) * Mathf.Max(0f, normalized);
        }

        public static float EvaluateTireForce(
            float slip,
            float peakSlip,
            float peakForce,
            float postPeakGrip)
        {
            float safePeakSlip = Mathf.Max(0.0001f, peakSlip);
            float normalizedSlip = Mathf.Abs(slip) / safePeakSlip;
            float magnitude;

            if (normalizedSlip <= 1f)
            {
                magnitude = normalizedSlip;
            }
            else
            {
                float falloff = Mathf.Clamp01((normalizedSlip - 1f) * 0.30f);
                magnitude = Mathf.Lerp(1f, Mathf.Clamp01(postPeakGrip), falloff);
            }

            return Mathf.Sign(slip) * magnitude * Mathf.Max(0f, peakForce);
        }

        public static Vector2 ApplyFrictionCircle(Vector2 force, float maxLongitudinal, float maxLateral)
        {
            float safeLongitudinal = Mathf.Max(0.0001f, maxLongitudinal);
            float safeLateral = Mathf.Max(0.0001f, maxLateral);
            Vector2 normalized = new Vector2(force.x / safeLongitudinal, force.y / safeLateral);
            float magnitude = normalized.magnitude;

            if (magnitude <= 1f)
            {
                return force;
            }

            normalized /= magnitude;
            return new Vector2(normalized.x * safeLongitudinal, normalized.y * safeLateral);
        }

        public static float KphToMetersPerSecond(float kph)
        {
            return kph / 3.6f;
        }

        public static float MetersPerSecondToKph(float metersPerSecond)
        {
            return metersPerSecond * 3.6f;
        }
    }
}
