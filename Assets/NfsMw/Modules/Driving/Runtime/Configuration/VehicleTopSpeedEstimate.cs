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
            if (tuning.UsesMostWantedReference && tuning.mostWanted != null)
            {
                float frontShare = tuning.driveLayout == VehicleDriveLayout.Fwd ? 1f
                    : tuning.driveLayout == VehicleDriveLayout.Rwd ? 0f : tuning.awdFrontTorqueBias;
                radius *= Mathf.Lerp(tuning.mostWanted.rearWheelRadiusScale, 1f, frontShare);
            }
            float finalDrive = Mathf.Abs(e.finalDrive);
            float idleRpm = Mathf.Max(1f, e.idleRpm);
            float redline = Mathf.Max(idleRpm + 1f, e.redlineRpm);
            float mass = Mathf.Max(100f, c.mass);
            float gravity = Physics.gravity.magnitude;
            float dragFactor = .5f * Mathf.Max(0f, a.airDensity) * Mathf.Max(0f, a.dragCoefficient) * Mathf.Max(0f, a.frontalArea);
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
                    if (tuning.UsesMostWantedReference) rawRpm = idleRpm + rawRpm * (redline - idleRpm) / redline;
                    float rpm = Mathf.Clamp(rawRpm, idleRpm, redline);
                    float baseTorque = VehicleMath.EvaluateEngineTorque(tuning, rpm);
                    float induction = tuning.UsesMostWantedReference ? 1f + MostWantedVehicleMath.InductionBoost(tuning, rpm, 1f)
                        : e.forcedInduction ? Mathf.Max(1f, e.boostTorqueMultiplier) : 1f;
                    float force = baseTorque * induction * ratio
                        * Mathf.Clamp01(MostWantedVehicleMath.GearEfficiency(tuning, gear + 1)) * Mathf.Clamp01(e.clutchEngagement) / radius;
                    float downforce = Mathf.Max(0f, a.downforceCoefficient) * speed * speed;
                    if (tuning.UsesMostWantedReference) downforce = tuning.mostWanted.linearDownforceCoefficient * speed;
                    float normalLoad = mass * gravity + downforce;
                    float rolling = (tuning.UsesMostWantedReference ? 0f : Mathf.Max(0f, c.rollingResistance) * mass * gravity)
                        + (Mathf.Max(0f, t.rollingResistance) * normalLoad);
                    if (force >= rolling + dragFactor * speed * speed) best = speedKph;
                }
                estimated = Mathf.Max(estimated, best);
            }
            if (tuning.speedGovernor) estimated = Mathf.Min(estimated, Mathf.Max(0f, c.maxSpeedKph));
            return new VehicleTopSpeedEstimate(estimated, ceiling);
        }
    }
}
