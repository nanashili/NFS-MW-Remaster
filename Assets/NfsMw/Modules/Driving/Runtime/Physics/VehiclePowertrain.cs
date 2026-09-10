using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public struct VehiclePowertrainOutput
    {
        public float EngineRpm;
        public float EngineTorque;
        public float WheelTorque;
        public int Gear;
        public bool IsShifting;

        public string GearLabel
        {
            get
            {
                if (Gear < 0)
                {
                    return "R";
                }

                return Gear == 0 ? "N" : Gear.ToString();
            }
        }
    }

    /// <summary>
    /// Automatic engine, gearbox, and open-diff torque source. It does not
    /// apply forces itself; that boundary belongs to VehicleWheel.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehiclePowertrain : MonoBehaviour
    {
        private VehicleTuning tuning;
        private VehicleWheel[] wheels;
        private float engineRpm;
        private float shiftTimer;
        private float boost;
        private bool gearUpHeld;
        private bool gearDownHeld;
        private bool limiterActive;
        private bool hasRuntimeState;
        private int gear = 1;
        private float[] referenceShiftPoints = System.Array.Empty<float>();
        public bool EngineRunning { get; private set; } = true;
        public void SetIgnition(bool running)
        {
            EngineRunning = running;
            if (!running) { engineRpm = 0f; shiftTimer = 0f; }
            else if (tuning != null && tuning.UsesMostWantedReference && engineRpm < tuning.engine.idleRpm)
                engineRpm = tuning.engine.idleRpm;
        }

        public float EngineRpm
        {
            get { return engineRpm; }
        }

        public int Gear
        {
            get { return gear; }
        }

        public bool IsShifting
        {
            get { return shiftTimer > 0f; }
        }

        public void Configure(VehicleTuning configuredTuning, VehicleWheel[] configuredWheels)
        {
            tuning = configuredTuning != null ? configuredTuning : VehicleTuning.CreateStreetRacer();
            wheels = configuredWheels;
            if (!hasRuntimeState)
            {
                engineRpm = tuning.engine.idleRpm;
                gear = 1;
                shiftTimer = 0f;
                hasRuntimeState = true;
            }
            else
            {
                engineRpm = Mathf.Clamp(engineRpm, tuning.engine.idleRpm, tuning.engine.redlineRpm + 500f);
                int maxGear = tuning.engine.gearRatios == null ? 0 : tuning.engine.gearRatios.Length;
                gear = Mathf.Clamp(gear, -1, maxGear);
            }
            boost = 0f; gearUpHeld = gearDownHeld = false;
            if (tuning.UsesMostWantedReference)
            {
                RacingLineSnapshot.ValidateTuning(tuning);
                referenceShiftPoints = new float[tuning.engine.gearRatios.Length];
                for (int i = 0; i < referenceShiftPoints.Length; i++)
                    referenceShiftPoints[i] = MostWantedVehicleMath.UpshiftRpm(tuning, i + 1);
            }
            else referenceShiftPoints = System.Array.Empty<float>();
        }

        public VehiclePowertrainOutput Simulate(
            float deltaTime,
            float forwardSpeed,
            float throttle,
            bool reverseRequested,
            bool nitrousActive)
        {
            EnsureConfiguration();

            if (!float.IsFinite(deltaTime) || deltaTime <= 0f || !float.IsFinite(forwardSpeed) || !float.IsFinite(throttle))
                return new VehiclePowertrainOutput { Gear = gear, EngineRpm = engineRpm, IsShifting = shiftTimer > 0f };
            throttle = Mathf.Clamp01(throttle);

            if (!EngineRunning)
            {
                engineRpm = 0f;
                return new VehiclePowertrainOutput { Gear = gear };
            }

            VehicleTuning.EngineSettings settings = tuning.engine;
            float averageWheelAngularVelocity = GetAverageDrivenWheelAngularVelocity();
            bool stopped = Mathf.Abs(forwardSpeed) < 0.75f;
            bool drivelineStopped = Mathf.Abs(averageWheelAngularVelocity) < 2f;
            bool automaticDirectionMismatch = tuning.transmissionMode == VehicleTransmissionMode.Automatic
                && (reverseRequested ? gear >= 0 : gear < 0);

            if (stopped && drivelineStopped && tuning.transmissionMode == VehicleTransmissionMode.Automatic)
            {
                if (reverseRequested && gear != -1)
                {
                    ChangeGear(-1, settings);
                }
                else if (!reverseRequested && gear != 1)
                {
                    ChangeGear(1, settings);
                }
            }
            // If the chassis is effectively stopped but the driven wheels are still spinning,
            // wait for the driveline to settle before applying propulsion through the old gear.
            // Engine braking can then dissipate the residual wheel speed without an F/R torque fight.
            if (stopped && automaticDirectionMismatch && !drivelineStopped)
                throttle = 0f;

            // Manual requests are edge-triggered and only accepted once the clutch is settled.
            if (tuning.transmissionMode == VehicleTransmissionMode.Manual && shiftTimer <= 0f)
            {
                int maxGear = settings.gearRatios == null ? 1 : settings.gearRatios.Length;
                if (VehicleManualUp && !gearUpHeld && gear >= 1) ChangeGear(Mathf.Min(gear + 1, maxGear), settings);
                else if (VehicleManualDown && !gearDownHeld && gear > 1) ChangeGear(gear - 1, settings);
            }
            gearUpHeld = VehicleManualUp; gearDownHeld = VehicleManualDown;

            shiftTimer = Mathf.Max(0f, shiftTimer - deltaTime);
            float ratio = GetCurrentRatio(settings);
            float wheelRpm = Mathf.Abs(averageWheelAngularVelocity) * 60f / (2f * Mathf.PI);
            float targetRpm = wheelRpm * Mathf.Abs(ratio * settings.finalDrive);
            if (tuning.UsesMostWantedReference)
                targetRpm = settings.idleRpm + targetRpm * (settings.redlineRpm - settings.idleRpm) / settings.redlineRpm;

            if (Mathf.Abs(averageWheelAngularVelocity) < 0.25f || tuning.UsesMostWantedReference && gear == 0)
            {
                float freeRevTarget = Mathf.Lerp(settings.idleRpm, settings.redlineRpm, throttle);
                targetRpm = Mathf.Lerp(targetRpm, freeRevTarget, throttle);
            }

            targetRpm = Mathf.Clamp(Mathf.Max(settings.idleRpm, targetRpm), settings.idleRpm, settings.redlineRpm + 500f);
            float rpmResponse = 1f / Mathf.Max(0.01f, settings.engineInertia);
            engineRpm = Mathf.MoveTowards(engineRpm, targetRpm, 4200f * rpmResponse * deltaTime);

            if (tuning.transmissionMode == VehicleTransmissionMode.Automatic && shiftTimer <= 0f && gear > 0)
            {
                int highestGear = settings.gearRatios == null ? 0 : settings.gearRatios.Length;
                bool referenceShifting = tuning.UsesMostWantedReference && tuning.mostWanted.torqueBasedShifting;
                float upshift = referenceShifting && gear <= referenceShiftPoints.Length ? referenceShiftPoints[gear - 1] : settings.shiftUpRpm;
                float downshift = referenceShifting && gear > 1
                    ? referenceShiftPoints[gear - 2] * settings.gearRatios[gear - 1] / settings.gearRatios[gear - 2] - settings.shiftDownHysteresisRpm
                    : settings.shiftDownRpm;
                if (highestGear > 0 && engineRpm >= upshift && gear < highestGear && throttle > 0.25f)
                {
                    ChangeGear(gear + 1, settings);
                }
                else if (engineRpm <= downshift && gear > 1 && throttle < 0.90f)
                {
                    ChangeGear(gear - 1, settings);
                }
            }

            float baseEngineTorque = VehicleMath.EvaluateEngineTorque(tuning, engineRpm);
            float targetBoost = settings.forcedInduction ? Mathf.Clamp01(throttle) : 0f;
            if (!tuning.UsesMostWantedReference)
                boost = Mathf.MoveTowards(boost, targetBoost, deltaTime / Mathf.Max(0.01f, settings.boostSpoolSeconds));
            float induction = settings.forcedInduction ? Mathf.Lerp(1f, Mathf.Max(1f, settings.boostTorqueMultiplier), boost) : 1f;
            if (tuning.UsesMostWantedReference)
            {
                var reference = tuning.mostWanted;
                targetBoost = settings.forcedInduction ? Mathf.Clamp01(throttle * 2f) : 0f;
                if (shiftTimer > 0f || reference.inductionSpoolRpmFraction > 0f && engineRpm < MostWantedVehicleMath.InductionThresholdRpm(tuning))
                    targetBoost = 0f;
                float duration = boost > targetBoost ? reference.inductionSpoolDownSeconds : settings.boostSpoolSeconds;
                boost = duration > 0.00001f ? Mathf.MoveTowards(boost, targetBoost, deltaTime / duration) : targetBoost;
                induction = 1f + MostWantedVehicleMath.InductionBoost(tuning, engineRpm, boost);
            }
            float combustionTorque = baseEngineTorque * induction * Mathf.Clamp01(throttle);
            float engineBrakeFactor = Mathf.InverseLerp(settings.idleRpm, settings.redlineRpm, engineRpm);
            float engineBrakingTorque = settings.engineBrakingTorque * engineBrakeFactor * (1f - Mathf.Clamp01(throttle));
            if (tuning.UsesMostWantedReference)
                engineBrakingTorque = baseEngineTorque * induction * MostWantedVehicleMath.EngineBrakingFraction(tuning, engineRpm) * (1f - Mathf.Clamp01(throttle));
            float nitrousTorque = nitrousActive
                ? tuning.UsesMostWantedReference ? combustionTorque * tuning.mostWanted.nitrousTorqueBoost : settings.nitrousTorque * Mathf.Clamp01(throttle)
                : 0f;
            float signedEngineTorque = combustionTorque + nitrousTorque - engineBrakingTorque;
            if (tuning.UsesMostWantedReference)
                signedEngineTorque = MostWantedVehicleMath.NetEngineTorque(tuning, engineRpm, throttle, boost, nitrousActive);
            if (limiterActive && engineRpm < settings.redlineRpm - settings.revLimiterHysteresisRpm) limiterActive = false;
            if (engineRpm >= settings.redlineRpm) limiterActive = true;
            if (limiterActive && throttle > 0f) signedEngineTorque = Mathf.Min(0f, signedEngineTorque);
            ratio = GetCurrentRatio(settings);
            float wheelTorque = signedEngineTorque * ratio * settings.finalDrive * MostWantedVehicleMath.GearEfficiency(tuning, gear)
                * Mathf.Clamp01(settings.clutchEngagement);
            if (shiftTimer > 0f)
            {
                wheelTorque *= 0.12f;
            }

            return new VehiclePowertrainOutput
            {
                EngineRpm = engineRpm,
                EngineTorque = signedEngineTorque,
                WheelTorque = wheelTorque,
                Gear = gear,
                IsShifting = shiftTimer > 0f
            };
        }

        private bool VehicleManualUp { get; set; }
        private bool VehicleManualDown { get; set; }
        public void SetManualRequests(bool up, bool down, bool neutral = false, bool reverse = false)
        {
            VehicleManualUp = up; VehicleManualDown = down;
            if (tuning != null && tuning.transmissionMode == VehicleTransmissionMode.Manual && shiftTimer <= 0f)
            {
                if (neutral) gear = 0;
                else if (reverse && Mathf.Abs(GetAverageDrivenWheelAngularVelocity()) < 1f) gear = -1;
            }
        }

        public float GetDriveTorqueShare(VehicleWheel wheel, VehicleWheel[] allWheels)
        {
            if (wheel == null || allWheels == null) return 0f;
            bool legacy = tuning == null || tuning.driveLayout == VehicleDriveLayout.LegacyBindings;
            bool driven = legacy ? wheel.IsDrivenWheel
                : tuning.driveLayout == VehicleDriveLayout.Awd
                    || (tuning.driveLayout == VehicleDriveLayout.Fwd && wheel.IsFrontWheel)
                    || (tuning.driveLayout == VehicleDriveLayout.Rwd && !wheel.IsFrontWheel);
            if (!driven) return 0f;
            int count = 0;
            float front = 0f, rear = 0f;
            for (int i = 0; i < allWheels.Length; i++)
            {
                VehicleWheel other = allWheels[i];
                bool otherDriven = legacy ? other.IsDrivenWheel
                    : tuning.driveLayout == VehicleDriveLayout.Awd
                        || (tuning.driveLayout == VehicleDriveLayout.Fwd && other.IsFrontWheel)
                        || (tuning.driveLayout == VehicleDriveLayout.Rwd && !other.IsFrontWheel);
                if (otherDriven) { count++; if (other.IsFrontWheel) front += 1f; else rear += 1f; }
            }
            if (count == 0) return 0f;
            if (tuning.driveLayout != VehicleDriveLayout.Awd) return 1f / count;
            float axleShare = wheel.IsFrontWheel ? tuning.awdFrontTorqueBias : 1f - tuning.awdFrontTorqueBias;
            float axleCount = wheel.IsFrontWheel ? front : rear;
            float lockStrength = tuning.differential == VehicleDifferentialMode.Locked ? 1f
                : tuning.differential == VehicleDifferentialMode.LimitedSlip ? tuning.differentialLockStrength : 0f;
            float ownSpeed = Mathf.Abs(wheel.AngularVelocity);
            float axleSpeed = wheel.IsFrontWheel ? AverageAxleSpeed(allWheels, true) : AverageAxleSpeed(allWheels, false);
            float preloadLock = Mathf.Clamp01(tuning.differentialPreload / (tuning.differentialPreload + ownSpeed + 0.001f));
            float effectiveLock = Mathf.Max(lockStrength, preloadLock);
            float tractionBias = Mathf.Lerp(1f, Mathf.Clamp(0.5f + (axleSpeed - ownSpeed) * 0.1f, 0.25f, 1.75f), effectiveLock);
            return axleShare * tractionBias / Mathf.Max(1f, axleCount);
        }

        private static float AverageAxleSpeed(VehicleWheel[] source, bool front)
        {
            float total = 0f; int count = 0;
            for (int i = 0; i < source.Length; i++) if (source[i].IsFrontWheel == front) { total += Mathf.Abs(source[i].AngularVelocity); count++; }
            return count == 0 ? 0f : total / count;
        }

        public void ResetPowertrain()
        {
            EnsureConfiguration();
            gear = 1;
            engineRpm = tuning.engine.idleRpm;
            shiftTimer = 0f;
            limiterActive = false;
            boost = 0f;
            gearUpHeld = gearDownHeld = false;
            hasRuntimeState = true;
        }

        private void EnsureConfiguration()
        {
            if (tuning != null)
            {
                return;
            }

            Configure(VehicleTuning.CreateStreetRacer(), GetComponentsInChildren<VehicleWheel>());
        }

        private float GetAverageDrivenWheelAngularVelocity()
        {
            if (wheels == null || wheels.Length == 0)
            {
                return 0f;
            }

            float total = 0f;
            int count = 0;
            for (int i = 0; i < wheels.Length; i++)
            {
                if (GetDriveTorqueShare(wheels[i], wheels) <= 0f) continue;

                total += wheels[i].AngularVelocity;
                count++;
            }

            return count == 0 ? 0f : total / count;
        }

        private float GetCurrentRatio(VehicleTuning.EngineSettings settings)
        {
            if (gear < 0)
            {
                return settings.reverseRatio;
            }

            if (settings.gearRatios == null || settings.gearRatios.Length == 0 || gear == 0)
            {
                return 0f;
            }

            return settings.gearRatios[Mathf.Clamp(gear - 1, 0, settings.gearRatios.Length - 1)];
        }

        private void ChangeGear(int newGear, VehicleTuning.EngineSettings settings)
        {
            if (newGear == gear) return;
            int previousGear = gear;
            gear = newGear;
            bool directionChange = previousGear != 0 && newGear != 0 && Mathf.Sign(previousGear) != Mathf.Sign(newGear);
            shiftTimer = tuning.UsesMostWantedReference
                ? MostWantedVehicleMath.ShiftDuration(settings.shiftDuration, Mathf.Abs(GetCurrentRatio(settings)), newGear < previousGear || directionChange)
                : Mathf.Max(0f, settings.shiftDuration);
        }
    }
}
