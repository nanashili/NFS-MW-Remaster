using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public readonly struct VehicleTopSpeedEstimate
    {
        public readonly float estimatedKph, gearingCeilingKph;
        public VehicleTopSpeedEstimate(float estimated, float ceiling)
        { estimatedKph = estimated; gearingCeilingKph = ceiling; }
    }

    /// <summary>Engineering flat-road estimate with ideal gear selection. It ignores automatic shift logic, slip, transients, assists and nitrous.</summary>
    public static class VehicleTopSpeedEstimator
    {
        public static VehicleTopSpeedEstimate Estimate(VehicleTuning tuning)
        {
            if (tuning == null || tuning.engine == null || tuning.tires == null || tuning.aero == null || tuning.chassis == null)
                return new VehicleTopSpeedEstimate(0f, 0f);
            var e = tuning.engine; var t = tuning.tires; var a = tuning.aero; var c = tuning.chassis;
            float radius = Mathf.Max(.05f, t.wheelRadius);
            float frontShare = tuning.driveLayout == VehicleDriveLayout.Fwd ? 1f
                : tuning.driveLayout == VehicleDriveLayout.Rwd ? 0f : tuning.awdFrontTorqueBias;
            radius *= Mathf.Lerp(tuning.mostWanted.rearWheelRadiusScale, 1f, frontShare);
            float finalDrive = Mathf.Abs(e.finalDrive);
            float idleRpm = Mathf.Max(1f, e.idleRpm);
            float redline = Mathf.Max(idleRpm + 1f, e.redlineRpm);
            float mass = Mathf.Max(100f, c.mass);
            float gravity = Physics.gravity.magnitude;
            float dragFactor = Mathf.Max(0f, tuning.mostWanted.dragCoefficient);
            float ceiling = 0f, estimated = 0f;
            int gears = e.gearRatios == null ? 0 : e.gearRatios.Length;
            for (int gear = 0; gear < gears; gear++)
            {
                float ratio = Mathf.Abs(e.gearRatios[gear] * finalDrive);
                if (ratio < .001f) continue;
                float gearCeiling = redline / ratio * (2f * Mathf.PI * radius) * .06f;
                ceiling = Mathf.Max(ceiling, gearCeiling);
                float best = 0f;
                const int samples = 240;
                for (int i = 1; i <= samples; i++)
                {
                    float speedKph = gearCeiling * i / samples;
                    float speed = speedKph / 3.6f;
                    float rawRpm = speed / (2f * Mathf.PI * radius) * ratio * 60f;
                    rawRpm = idleRpm + rawRpm * (redline - idleRpm) / redline;
                    float rpm = Mathf.Clamp(rawRpm, idleRpm, redline);
                    float baseTorque = MostWantedVehicleMath.Torque(tuning, rpm);
                    float induction = 1f + MostWantedVehicleMath.InductionBoost(tuning, rpm, 1f);
                    float force = baseTorque * induction * ratio
                        * Mathf.Clamp01(MostWantedVehicleMath.GearEfficiency(tuning, gear + 1)) / radius;
                    var limiter = tuning.mostWanted.speedLimiterMph;
                    if (limiter != null && limiter.Length >= 2 && limiter[1] > 0f)
                        force *= 1f - Mathf.Clamp01((speedKph / MostWantedVehicleMath.ReferenceMphToKph - limiter[0]) / limiter[1]);
                    float rolling = 0f;
                    if (force >= rolling + dragFactor * speed * speed) best = speedKph;
                }
                estimated = Mathf.Max(estimated, best);
            }
            return new VehicleTopSpeedEstimate(estimated, ceiling);
        }
    }
}
