using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Resolved inputs for the recovered source model. Common SI quantities remain in
    /// VehicleTuning so the existing parts, configuration resolver and telemetry retain ownership.
    /// Provenance and unmapped source fields live in the editor's import report, not in the tick loop.
    /// </summary>
    [Serializable]
    public sealed class MostWantedDrivingSettings
    {
        // Complete resolved handling payload. Physical source units are converted
        // to SI during import; ratios and grip values remain dimensionless.
        [Header("Resolved chassis (SI)")]
        public Vector2 shockDigression = new Vector2(.2f, .2f);
        public Vector2 springProgression = new Vector2(.1f, .1f);
        public Vector2 travel = new Vector2(.18f, .18f);
        public Vector2 rideHeight = new Vector2(.32f, .32f);
        public Vector2 trackWidth = new Vector2(1.72f, 1.72f);
        public Vector2 shockExtensionStiffness = new Vector2(4700f, 4700f);
        public Vector2 shockStiffness = new Vector2(4700f, 4700f);
        public Vector2 springStiffness = new Vector2(31000f, 31000f);
        public Vector2 shockValving = new Vector2(1f, 1f);
        public Vector2 swaybarStiffness = new Vector2(1000f, 1000f);
        public float rollCenter = .15f;
        public float wheelBase = 2.76f;
        public float shockBlowout = 1f;
        public float aeroCenterOfGravity = 50f;
        public float renderMotion = 1f;
        public float frontAxle = 1.38f;
        public float aeroCoefficient = .32f;
        public float frontWeightBias = 50f;
        public float dragCoefficient = .32f;

        [Header("Resolved tires (SI / dimensionless)")]
        public Vector4 yawControl = new Vector4(1f, 1f, 1f, 1f);
        public Vector2 gripScale = new Vector2(1f, 1f);
        public Vector2 dynamicGrip = new Vector2(1f, 1f);
        public Vector2 staticGrip = new Vector2(1f, 1f);
        public Vector2 rimSizeInches = new Vector2(17f, 17f);
        public Vector2 sectionWidth = new Vector2(260f, 260f);
        public Vector2 aspectRatio = new Vector2(45f, 45f);
        public float steering = 1f;
        public float yawSpeed = 1f;

        [Header("Resolved brakes (SI / dimensionless)")]
        public Vector2 brakeLock = new Vector2(1f, 1f);
        public Vector2 brakeTorque = new Vector2(5200f, 5200f);
        public float handbrake = 6200f;

        [Header("Resolved transmission (dimensionless)")]
        public Vector3 differential = new Vector3(.2f, .2f, .2f);
        public float torqueConverter = 1f;
        public float clutchSlip = .1f;
        public float optimalShift = .9f;

        [Tooltip("Source MAX_RPM: the end of the torque table, not the rev limiter (RED_LINE).")]
        [Min(500f)] public float torqueTableMaximumRpm = 9000f;
        [Tooltip("Equally spaced from IDLE to MAX_RPM, normalized by engine.maxTorqueNewtonMeters. Linear interpolation only.")]
        // Generic playable fixture only; imported vehicles replace these with the
        // resolved source table and carry source provenance in the import report.
        public float[] normalizedTorque = { .7f, .84f, .96f, 1f, .98f, .91f, .82f, .72f, .62f };
        [Tooltip("Source ENGINE_BRAKING fractions on the same RPM domain as TORQUE.")]
        public float[] engineBraking = { .2f, .2f, .25f };
        public float[] speedLimiterMph = Array.Empty<float>();
        [Tooltip("One efficiency per forward gear. Reverse and neutral source entries are not forward gears.")]
        public float[] gearEfficiency = { .90f, .90f, .89f, .88f, .87f, .86f };
        [Range(0f, 1f)] public float reverseGearEfficiency = 1f;
        [Tooltip("Use torque crossover to choose per-gear upshifts.")]
        public bool torqueBasedShifting = true;

        [Header("Induction: source dimensionless boost fractions")]
        [Range(0f, 1f)] public float inductionSpoolRpmFraction;
        [Min(0f)] public float inductionLowBoost;
        [Min(0f)] public float inductionHighBoost;
        [Range(-1f, 0f)] public float inductionVacuum;
        [Min(0f)] public float inductionSpoolDownSeconds = 0.2f;
        [Min(0f)] public float inductionPsi;

        [Header("Nitrous")]
        [Min(0f)] public float nitrousTorqueBoost;
        [Min(0f)] public float nitrousDisengageSeconds;
        [Min(0f)] public float nitrousRechargeMinimumKph;
        [Min(0f)] public float nitrousRechargeMaximumKph;
        [Tooltip("Time to recharge an empty tank, not units of fuel per second.")]
        [Min(0f)] public float nitrousRechargeMinimumSeconds;
        [Min(0f)] public float nitrousRechargeMaximumSeconds;
        [Min(0f)] public float nitrousFlowRate;

        [Header("Steering")]
        [Range(0.01f, 3f)] public float steeringCoefficient = 1f;
        [Range(-1f, 1f)] public float steeringTuning;
        [Tooltip("Gamepad/keyboard reference tables use signed forward speed in metres per second.")]
        public bool useSteeringTables = true;
        [Header("Aerodynamics")]
        [Tooltip("Source AERO_COEFFICIENT × 2000. Reference downforce is linear in speed, not quadratic.")]
        [Min(0f)] public float linearDownforceCoefficient;

        [Header("Axle differences: multiply the common front-axle settings")]
        [Range(0.25f, 4f)] public float rearWheelRadiusScale = 1f;
        [Range(0.05f, 20f)] public float rearSpringScale = 1f;
        [Range(0.05f, 20f)] public float rearDamperScale = 1f;
        [Tooltip("Rebound damping relative to the common compression damper, per axle.")]
        [Range(0.01f, 20f)] public float frontReboundDamperScale = 1f;
        [Range(0.01f, 20f)] public float rearReboundDamperScale = 1f;

        public void Validate(VehicleTuning tuning)
        {
            if (normalizedTorque == null || normalizedTorque.Length < 2 || normalizedTorque.Length > 256)
                throw new ArgumentException("MW_TORQUE: provide 2–256 equally spaced torque samples.");
            if (tuning == null || tuning.engine == null || !float.IsFinite(torqueTableMaximumRpm)
                || !float.IsFinite(tuning.engine.redlineRpm) || !float.IsFinite(tuning.engine.idleRpm)
                || tuning.engine.redlineRpm <= tuning.engine.idleRpm || torqueTableMaximumRpm < tuning.engine.redlineRpm)
                throw new ArgumentException("MW_RPM: IDLE < RED_LINE <= MAX_RPM is required.");
            foreach (float sample in normalizedTorque)
                if (!float.IsFinite(sample) || sample < 0f || sample > 1.00001f)
                    throw new ArgumentException("MW_TORQUE: normalized samples must be finite and between zero and one.");
            if (engineBraking == null || engineBraking.Length < 1 || engineBraking.Length > 256)
                throw new ArgumentException("MW_BRAKING: provide 1–256 engine braking samples.");
            foreach (float sample in engineBraking)
                if (!float.IsFinite(sample) || sample < 0f || sample > 1f)
                    throw new ArgumentException("MW_BRAKING: engine braking fractions must be between zero and one.");
            if (tuning.engine.gearRatios == null || tuning.engine.gearRatios.Length == 0 || gearEfficiency == null || gearEfficiency.Length != tuning.engine.gearRatios.Length)
                throw new ArgumentException("MW_GEARS: provide exactly one efficiency for each forward gear.");
            foreach (float efficiency in gearEfficiency)
                if (!float.IsFinite(efficiency) || efficiency < 0f || efficiency > 1f)
                    throw new ArgumentException("MW_GEARS: efficiencies must be finite and between zero and one.");
            foreach (float ratio in tuning.engine.gearRatios)
                if (!float.IsFinite(ratio) || ratio <= 0f)
                    throw new ArgumentException("MW_GEARS: forward ratios must be positive; exclude reverse and neutral.");
            if (tuning.engine.finalDrive <= 0f || tuning.engine.reverseRatio >= 0f)
                throw new ArgumentException("MW_GEARS: final drive must be positive and Unity reverse ratio negative.");
            if (nitrousRechargeMaximumKph < nitrousRechargeMinimumKph)
                throw new ArgumentException("MW_NOS: recharge maximum speed cannot be below the minimum.");
        }
    }
}
