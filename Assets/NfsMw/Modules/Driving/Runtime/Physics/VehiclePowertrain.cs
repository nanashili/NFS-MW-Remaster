using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public struct VehiclePowertrainOutput
    {
        public float EngineRpm, EngineTorque, WheelTorque;
        public int Gear;
        public bool IsShifting;
        public string GearLabel => Gear < 0 ? "R" : Gear == 0 ? "N" : Gear.ToString();
    }

    /// <summary>EngineRacer port (nfsmw commit 3bb4ab2), adapted only at the wheel torque boundary.</summary>
    [DisallowMultipleComponent]
    public sealed class VehiclePowertrain : MonoBehaviour
    {
        const float ClutchStiffness = 20f, ClutchLimiter = 300f, IdleClutchRpm = 800f;
        const float ClutchEngageTime = .25f, FirstGearClutchEngageTime = .05f;
        VehicleTuning tuning;
        VehicleWheel[] wheels;
        float angularVelocity, angularAcceleration, transmissionVelocity, engineRpm, driveTorque, shiftTimer, spool, clutchRpmDiff;
        bool configured;
        float sportShifting;
        int shiftPotential;
        int gear = 1;
        Clutch clutch;
        struct Clutch
        {
            public int state;
            public float time, engageTime;
            public void Reset()
            {
                state = 0;
                time = engageTime = 0f;
            }

            public void Disengage()
            {
                if (state == 0)
                    state = 2;
            }

            public void Engage(float t)
            {
                if (state == 2)
                {
                    state = t > 0f ? 1 : 0;
                    time = engageTime = t;
                }
            }

            public float Update(float dt)
            {
                if (time > 0f)
                {
                    time -= dt;
                    if (time <= 0f && state == 1)
                        state = 0;
                }

                return state == 2 ? .25f : state == 1 ? 1f - Mathf.Clamp01(time / Mathf.Max(.00001f, engageTime)) * .75f : 1f;
            }
        }

        public bool EngineRunning { get; private set; } = true;
        public float EngineRpm => engineRpm;
        public int Gear => gear;
        public bool IsShifting => shiftTimer > 0f;

        public void SetIgnition(bool running)
        {
            EngineRunning = running;
            if (!running)
            {
                angularVelocity = transmissionVelocity = engineRpm = 0f;
                driveTorque = 0f;
            }
            else if (tuning != null && engineRpm < tuning.engine.idleRpm)
            {
                engineRpm = tuning.engine.idleRpm;
                angularVelocity = RpmToRps(engineRpm);
            }
        }

        public void Configure(VehicleTuning t, VehicleWheel[] w)
        {
            tuning = t != null ? t : VehicleTuning.CreateStreetRacer();
            wheels = w;
            if (!configured)
            {
                gear = 1;
                engineRpm = tuning.engine.idleRpm;
                angularVelocity = RpmToRps(engineRpm);
                clutch.Reset();
                configured = true;
            }
        }

        public VehiclePowertrainOutput Simulate(float dt, float speed, float throttle, bool nitrousActive) => Simulate(dt, speed, throttle, nitrousActive ? 1f : 0f);
        public VehiclePowertrainOutput Simulate(float dt, float speed, float throttle, float nitrousMultiplier)
        {
            Ensure();
            if (!float.IsFinite(dt) || dt <= 0f || !float.IsFinite(speed) || !float.IsFinite(throttle))
                return Output();
            if (!EngineRunning)
            {
                engineRpm = 0f;
                driveTorque = 0f;
                return Output();
            }

            throttle = Mathf.Clamp01(throttle);
            var e = tuning.engine;
            if (tuning.mostWanted.speedLimiterMph != null && tuning.mostWanted.speedLimiterMph.Length >= 2 && gear > 0)
            {
                float limiter = tuning.mostWanted.speedLimiterMph[0], cutoff = tuning.mostWanted.speedLimiterMph[1];
                float mph = Mathf.Abs(speed) / MostWantedVehicleMath.ReferenceMphToKph * 3.6f;
                if (cutoff > 0f && mph > limiter)
                    throttle *= 1f - Mathf.Clamp01((mph - limiter) / cutoff);
            }

            // InputPlayer already selected the gear before chassis/NOS processing.
            if (tuning.transmissionMode == VehicleTransmissionMode.Automatic && shiftTimer <= 0f && sportShifting <= 0f)
                AutoShift(throttle);
            shiftTimer = Mathf.Max(0f, shiftTimer - dt);
            if (sportShifting > 0f && shiftPotential != 0)
                sportShifting = Mathf.Max(0f, sportShifting - dt * (2f - throttle));
            float ratio = Ratio(gear), minW = RpmToRps(e.idleRpm), maxW = RpmToRps(e.redlineRpm), axleW = DifferentialVelocity(false), differentialW = DifferentialVelocity(true), clutchRatio = clutch.Update(dt), direction = gear < 0 ? -1f : 1f, totalRatio = ratio * e.finalDrive * direction;
            if (gear == -1 || gear == 1)
            {
                float idleLimit = RpmToRps(IdleClutchRpm * (gear < 0 ? 3f : 1f)) + minW;
                if (angularVelocity > idleLimit || transmissionVelocity > idleLimit || throttle >= .1f)
                    clutch.Engage(FirstGearClutchEngageTime);
                else
                    clutch.Disengage();
            }
            else if (gear == 0)
                clutch.Disengage();
            else
                clutch.Engage(ClutchEngageTime);
            if (tuning.mostWanted.torqueConverter > 0f)
                totalRatio *= 1f + tuning.mostWanted.torqueConverter * throttle * (1f - Mathf.Clamp01(Mathf.InverseLerp(e.idleRpm, e.peakTorqueRpm, RpsToRpm(angularVelocity)))) * (IsShifting ? clutchRatio : 1f);
            if (totalRatio == 0f && gear != 0)
                return Output();
            float rpm = RpsToRpm(angularVelocity), induction = Induction(dt, throttle, rpm), engineTorque = BaseTorque(rpm) * (1f + induction);
            if (nitrousMultiplier > 0f)
                engineTorque *= 1f + tuning.mostWanted.nitrousTorqueBoost * Mathf.Clamp01(nitrousMultiplier);
            float braking = MostWantedVehicleMath.EngineBrakingTorque(tuning, engineTorque, rpm, gear, clutch.state == 0, axleW);
            float totalEngineTorque = engineTorque * throttle + braking * (1f - throttle), roadTorque = 0f;
            driveTorque = 0f;
            float oldTransmission = transmissionVelocity;
            float targetTransmission = minW + differentialW * totalRatio * (maxW - minW) / Mathf.Max(.0001f, maxW);
            transmissionVelocity = targetTransmission;
            float transmissionAcceleration = (transmissionVelocity - oldTransmission) / dt;
            float inertia = Mathf.Max(.001f, MostWantedVehicleMath.LoadedEngineInertia((e.engineInertia - .25f) / .025f) * (gear == 0 ? .35f : 1f));
            if (gear == 0)
            {
                // EngineRacer still integrates a free engine in neutral. The
                // transmission has no road reaction in this state.
                angularAcceleration = totalEngineTorque / inertia;
                angularVelocity = Mathf.Clamp(angularVelocity + angularAcceleration * dt, minW, maxW);
            }
            else
            {
                if (clutch.state == 0)
                {
                    driveTorque = totalEngineTorque;
                    roadTorque -= totalEngineTorque * GroundedRatio();
                }
                else if (clutch.state == 1)
                {
                    float diff = Mathf.Clamp(angularVelocity - targetTransmission, -ClutchLimiter, ClutchLimiter), rd = RpsToRpm(targetTransmission - angularVelocity), stiffness = clutchRpmDiff != 0f && rd * clutchRpmDiff < 0f && Mathf.Abs(rd) > 2000f && Mathf.Abs(clutchRpmDiff) > 2000f ? 10f : ClutchStiffness;
                    clutchRpmDiff = rd;
                    float ct = diff * stiffness * clutchRatio;
                    driveTorque += ct;
                    roadTorque -= ct * GroundedRatio();
                }
                else
                    clutchRpmDiff = 0f;
                if (clutch.state == 0)
                {
                    float acceleration = (totalEngineTorque + roadTorque) / inertia;
                    float response = 1f / inertia + .1f * totalRatio * totalRatio * .5f;
                    float correction = (acceleration - transmissionAcceleration) / response;
                    driveTorque += Mathf.Min(correction, 0f);
                    roadTorque -= correction * GroundedRatio();
                }

                if (clutch.state == 0 && GroundedCount() > 0)
                    roadTorque += Mathf.Clamp((targetTransmission - angularVelocity) * ClutchStiffness, -Mathf.Abs(totalEngineTorque), Mathf.Abs(totalEngineTorque));
                if ((gear == 1 || gear < 0) && roadTorque < totalEngineTorque && totalEngineTorque + roadTorque < 0f)
                    roadTorque += ClutchPlay(totalEngineTorque + roadTorque) * tuning.mostWanted.clutchSlip;
                angularAcceleration = (totalEngineTorque + roadTorque) / inertia;
                angularVelocity = Mathf.Clamp(angularVelocity + angularAcceleration * dt, minW, maxW);
                driveTorque *= totalRatio * Efficiency(gear);
            }

            engineRpm = Smooth(engineRpm, RpsToRpm(angularVelocity), dt, inertia);
            shiftPotential = clutch.state == 0 ? FindShiftPotential(gear, RpsToRpm(transmissionVelocity), throttle) : 0;
            return new VehiclePowertrainOutput
            {
                EngineRpm = engineRpm,
                EngineTorque = totalEngineTorque,
                WheelTorque = driveTorque,
                Gear = gear,
                IsShifting = IsShifting
            };
        }

        float Induction(float dt, float throttle, float rpm)
        {
            if (!tuning.engine.forcedInduction)
                return 0f;
            float desired = Mathf.Clamp01(throttle * 2f);
            if (IsShifting || rpm < MostWantedVehicleMath.InductionThresholdRpm(tuning))
                desired = 0f;
            float t = spool > desired ? tuning.mostWanted.inductionSpoolDownSeconds : tuning.engine.boostSpoolSeconds;
            spool = t > .00001f ? Mathf.MoveTowards(spool, desired, dt / t) : desired;
            return MostWantedVehicleMath.InductionBoost(tuning, rpm, spool);
        }

        float ClutchPlay(float torque)
        {
            float x = torque * 1000f;
            if (x <= -10f)
                return torque;
            if (x <= -7.5f)
                return Mathf.Lerp(.96f, 1f, Mathf.InverseLerp(-7.5f, -10f, x)) * torque;
            if (x <= -3.5f)
                return Mathf.Lerp(.925f, .96f, Mathf.InverseLerp(-3.5f, -7.5f, x)) * torque;
            if (x <= -.3f)
                return Mathf.Lerp(.875f, .925f, Mathf.InverseLerp(-.3f, -3.5f, x)) * torque;
            if (x < -.05f)
                return Mathf.Lerp(0f, .875f, Mathf.InverseLerp(-.05f, -.3f, x)) * torque;
            return 0f;
        }

        float BaseTorque(float rpm) => MostWantedVehicleMath.Torque(tuning, rpm);
        float Smooth(float old, float next, float dt, float inertia)
        {
            float max = -2.5f * 1000f / inertia;
            if ((next - old) / dt < max)
                next = Mathf.Max(next, old + max * dt);
            return next * .55f + old * .45f;
        }

        float GroundedRatio() => Mathf.Max(.25f, GroundedCount() * .25f);
        int GroundedCount()
        {
            int n = 0;
            if (wheels != null)
                for (int i = 0; i < wheels.Length; i++)
                    if (wheels[i].Grounded)
                        n++;
            return n;
        }

        float DifferentialVelocity(bool locked)
        {
            float average = 0f;
            float maximum = 0f, minimum = 0f;
            int n = 0;
            if (wheels != null)
                for (int i = 0; i < wheels.Length; i++)
                    if (GetDriveTorqueShare(wheels[i], wheels) > 0f)
                    {
                        VehicleWheel wheel = wheels[i];
                        float velocity = wheel.Tire != null && wheel.Tire.BrakeLocked ? 0f : !wheel.Grounded || wheel.Tire == null || !wheel.Tire.IsSlipping ? wheel.AngularVelocity : wheel.Tire.RoadSpeed / Mathf.Max(.001f, wheel.Tire.Radius);
                        average += velocity;
                        n++;
                        maximum = Mathf.Max(maximum, velocity);
                        minimum = Mathf.Min(minimum, velocity);
                    }

            return locked ? (gear < 0 ? minimum : maximum) : (n == 0 ? 0f : average / n);
        }

        float Ratio(int g) => g < 0 ? Mathf.Abs(tuning.engine.reverseRatio) : g == 0 || tuning.engine.gearRatios == null || tuning.engine.gearRatios.Length == 0 ? 0f : tuning.engine.gearRatios[Mathf.Clamp(g - 1, 0, tuning.engine.gearRatios.Length - 1)];
        float Efficiency(int g) => MostWantedVehicleMath.GearEfficiency(tuning, g);
        float RpmToRps(float x) => x * Mathf.PI / 30f;
        float RpsToRpm(float x) => x * 30f / Mathf.PI;
        void AutoShift(float gas)
        {
            if (gear < 0) return;
            if (gear == 0) { gear = 1; return; }
            if (shiftPotential > 0)
            {
                if (wheels == null || wheels.Length != 4) return;
                foreach (var wheel in wheels)
                    if (!wheel.Grounded || wheel.Tire.Slip >= 4f) return;
                ChangeGear(gear + 1);
                return;
            }
            if (shiftPotential < 0)
            {
                int next = gear - 1;
                float rpm = RpsToRpm(transmissionVelocity);
                while (next > 1 && FindShiftPotential(next, rpm * Ratio(next) / Ratio(gear), gas) < 0) next--;
                ChangeGear(next);
            }
        }

        int FindShiftPotential(int currentGear, float rpm, float gas)
        {
            if (currentGear <= 0) return 0;
            if (rpm >= MostWantedVehicleMath.UpshiftRpm(tuning, currentGear) && currentGear < tuning.engine.gearRatios.Length) return 1;
            return currentGear > 1 && rpm <= DownshiftRpm(currentGear, gas) ? -1 : 0;
        }

        float DownshiftRpm(int currentGear, float gas)
        {
            float down = tuning.engine.shiftDownRpm;
            if (currentGear <= 1)
                return down;
            float lowerRatio = tuning.engine.gearRatios[currentGear - 2];
            if (lowerRatio <= 0f)
                return down;
            float lowerGearUp = MostWantedVehicleMath.UpshiftRpm(tuning, currentGear - 1);
            float constrained = lowerGearUp * tuning.engine.gearRatios[currentGear - 1] / lowerRatio - 200f;
            float coasting = Mathf.Lerp(tuning.engine.idleRpm, down, .65f);
            return Mathf.Min(Mathf.Lerp(coasting, down, Mathf.Clamp01(gas)), constrained);
        }

        void ChangeGear(int g)
        {
            if (g == gear || g < -1 || g > tuning.engine.gearRatios.Length)
                return;
            int old = gear;
            gear = g;
            shiftPotential = 0;
            shiftTimer = MostWantedVehicleMath.ShiftDuration(tuning.engine.shiftDuration, Ratio(g), g < old);
            clutch.Disengage();
        }

        public void SetManualRequests(bool up, bool down, bool neutral = false, bool reverse = false)
        {
            Ensure();
            // Inputs are consumed shift events, not held buttons. Last queued direction wins.
            int direction = down ? -1 : up ? 1 : 0;
            if (direction != 0 && gear != -1)
            {
                int desired = Mathf.Clamp(gear + direction, 0, tuning.engine.gearRatios.Length);
                if (tuning.transmissionMode == VehicleTransmissionMode.Manual) ChangeGear(desired);
                else if (desired > 0 && !IsShifting && !(direction > 0 && shiftPotential < 0)
                    && !(direction < 0 && shiftPotential > 0) && desired != gear)
                {
                    ChangeGear(desired);
                    sportShifting = 1.25f;
                }
            }
            // Explicit N/R keys are project controls outside InputPlayer's shift actions.
            if (tuning != null && neutral)
                ChangeGear(0);
            else if (tuning != null && reverse && Mathf.Abs(DifferentialVelocity(false)) < 1f)
                ChangeGear(-1);
        }

        public VehicleInputState ApplyDriverPedals(VehicleInputState input, float speed)
        {
            Ensure();
            ChangeGear(MostWantedDriverControls.AutoReverseGear(gear, speed, input.Throttle, input.Brake));
            return MostWantedDriverControls.Pedals(input, gear, speed);
        }

        public float GetDriveTorqueShare(VehicleWheel wheel, VehicleWheel[] all)
        {
            if (tuning == null)
                Ensure();
            if (wheel == null || all == null)
                return 0f;
            bool driven = tuning.driveLayout == VehicleDriveLayout.Awd || tuning.driveLayout == VehicleDriveLayout.LegacyBindings && wheel.IsDrivenWheel || tuning.driveLayout == VehicleDriveLayout.Fwd && wheel.IsFrontWheel || tuning.driveLayout == VehicleDriveLayout.Rwd && !wheel.IsFrontWheel;
            if (!driven)
                return 0f;
            int n = 0;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && (tuning.driveLayout == VehicleDriveLayout.Awd || tuning.driveLayout == VehicleDriveLayout.LegacyBindings && all[i].IsDrivenWheel || tuning.driveLayout == VehicleDriveLayout.Fwd && all[i].IsFrontWheel || tuning.driveLayout == VehicleDriveLayout.Rwd && !all[i].IsFrontWheel))
                    n++;
            return n == 0 ? 0f : 1f / n;
        }

        public void ResetPowertrain()
        {
            Ensure();
            gear = 1;
            engineRpm = tuning.engine.idleRpm;
            angularVelocity = RpmToRps(engineRpm);
            transmissionVelocity = shiftTimer = spool = driveTorque = sportShifting = 0f;
            shiftPotential = 0;
            clutch.Reset();
        }

        VehiclePowertrainOutput Output() => new VehiclePowertrainOutput
        {
            EngineRpm = engineRpm,
            EngineTorque = 0f,
            WheelTorque = driveTorque,
            Gear = gear,
            IsShifting = IsShifting
        };
        void Ensure()
        {
            if (tuning == null)
                Configure(VehicleTuning.CreateStreetRacer(), GetComponentsInChildren<VehicleWheel>());
        }
    }
}
