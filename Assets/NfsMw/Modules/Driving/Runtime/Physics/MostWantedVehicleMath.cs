using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Independent C# implementations of recovered scalar relationships. Source/version and
    /// PC corroboration are recorded in Tools/DrivingMechanics/REVERSE_ENGINEERING.md.
    /// This is not a replacement collision engine or a claim of reference-game driving parity.
    /// </summary>
    public static class MostWantedVehicleMath
    {
        // Preserve the reference's rounded constants rather than silently substituting textbook units.
        public const float FootPoundsToNewtonMeters = 1.3558f;
        public const float PoundsPerInchToNewtonsPerMeter = 175.1268f;
        public const float InchesToMeters = 0.0254f;
        public const float ReferenceMphToKph = 0.44703001f * 3.6f;

        public static float SampleUniform(float[] values, float input, float minimum, float maximum)
        {
            if (values == null || values.Length == 0 || !float.IsFinite(input)) return 0f;
            if (values.Length == 1 || maximum <= minimum) return values[0];
            float position = Mathf.Clamp01((input - minimum) / (maximum - minimum)) * (values.Length - 1);
            int lower = Mathf.Min((int)position, values.Length - 2);
            return Mathf.LerpUnclamped(values[lower], values[lower + 1], position - lower);
        }

        public static float Torque(VehicleTuning tuning, float rpm)
        {
            var e = tuning.engine;
            float boundedRpm = Mathf.Clamp(rpm, e.idleRpm, e.redlineRpm);
            return Mathf.Max(0f, e.maxTorqueNewtonMeters) * SampleUniform(tuning.mostWanted.normalizedTorque,
                boundedRpm, e.idleRpm, tuning.mostWanted.torqueTableMaximumRpm);
        }

        public static float GearEfficiency(VehicleTuning tuning, int gear)
        {
            if (!tuning.UsesMostWantedReference) return tuning.engine.drivelineEfficiency;
            if (gear < 0) return tuning.mostWanted.reverseGearEfficiency;
            var values = tuning.mostWanted.gearEfficiency;
            return gear > 0 && values != null && gear <= values.Length ? values[gear - 1] : 0f;
        }

        public static float WheelRadius(float rimInches, float sectionWidthMillimeters, float aspectPercent)
            => (rimInches * InchesToMeters + 2f * sectionWidthMillimeters * 0.001f * aspectPercent * 0.01f) * 0.5f;

        public static float LoadedEngineInertia(float flywheelMass) => flywheelMass * 0.025f + 0.25f;

        public static float EngineBrakingFraction(VehicleTuning tuning, float rpm)
            => Mathf.Clamp01(SampleUniform(tuning.mostWanted.engineBraking,
                Mathf.Clamp(rpm, tuning.engine.idleRpm, tuning.engine.redlineRpm),
                tuning.engine.idleRpm, tuning.mostWanted.torqueTableMaximumRpm));

        public static float NetEngineTorque(VehicleTuning tuning, float rpm, float throttle, float spool, bool nitrous)
        {
            float fullTorque = Torque(tuning, rpm) * (1f + InductionBoost(tuning, rpm, spool));
            if (nitrous) fullTorque *= 1f + tuning.mostWanted.nitrousTorqueBoost;
            float gas = Mathf.Clamp01(throttle);
            return fullTorque * (gas - (1f - gas) * EngineBrakingFraction(tuning, rpm));
        }

        public static float ShiftDuration(float timeScale, float newRatio, bool downshift)
            => Mathf.Max(0f, timeScale) * Mathf.Abs(newRatio) * (downshift ? .25f : 1f);

        public static float OutsideSteerDegrees(float insideDegrees, float wheelbase, float trackWidth)
        {
            float angle = Mathf.Abs(insideDegrees) * Mathf.Deg2Rad;
            float length = Mathf.Max(0.01f, wheelbase);
            return Mathf.Sign(insideDegrees) * length * angle / (Mathf.Max(0f, trackWidth) * angle + length) * Mathf.Rad2Deg;
        }

        public static float ReferenceDownforce(float speed, float coefficient, float forwardness, float upness, int contacts)
        {
            float attitude = contacts >= 2 ? 1f : Mathf.Clamp01(upness);
            float direction = Mathf.Max(0.4f, Mathf.Sqrt(Mathf.Clamp01(forwardness)));
            return Mathf.Max(0f, speed) * Mathf.Max(0f, coefficient) * attitude * direction * (contacts == 0 ? 0.8f : 1f);
        }

        public static float InductionThresholdRpm(VehicleTuning tuning)
            => Mathf.Lerp(tuning.engine.idleRpm, tuning.engine.redlineRpm, tuning.mostWanted.inductionSpoolRpmFraction);

        public static float InductionBoost(VehicleTuning tuning, float rpm, float spool)
        {
            var m = tuning.mostWanted;
            if (!tuning.engine.forcedInduction || m.inductionLowBoost <= 0f && m.inductionHighBoost <= 0f) return 0f;
            float threshold = InductionThresholdRpm(tuning);
            float boost = rpm >= threshold
                ? Mathf.Lerp(m.inductionLowBoost, m.inductionHighBoost, Mathf.InverseLerp(threshold, tuning.engine.redlineRpm, rpm))
                : m.inductionVacuum * Mathf.InverseLerp(tuning.engine.idleRpm, threshold, rpm);
            return boost * Mathf.Clamp01(spool);
        }

        /// <summary>
        /// Recovered 50-RPM torque-crossover search for the naturally aspirated case.
        /// Induction is evaluated at the candidate RPM here; the public reconstruction flags
        /// a swapped-argument ambiguity in its original shift-point helper. That correction
        /// is deliberate and therefore not claimed to reproduce the ambiguous routine exactly.
        /// </summary>
        public static float UpshiftRpm(VehicleTuning tuning, int gear)
        {
            var e = tuning.engine;
            if (gear < 1 || gear >= e.gearRatios.Length) return e.redlineRpm;
            float ratio = e.gearRatios[gear] / e.gearRatios[gear - 1];
            float rpm = (e.redlineRpm + e.idleRpm) * 0.5f;
            for (int i = 0; i < 4096 && rpm < e.redlineRpm; i++, rpm += 50f)
            {
                float current = Torque(tuning, rpm) * (1f + InductionBoost(tuning, rpm, 1f));
                float nextRpm = rpm * ratio;
                float next = Torque(tuning, nextRpm) * (1f + InductionBoost(tuning, nextRpm, 1f)) * ratio;
                if (next > current) return rpm;
            }
            return Mathf.Max(e.idleRpm, e.redlineRpm - 100f);
        }
    }
}
