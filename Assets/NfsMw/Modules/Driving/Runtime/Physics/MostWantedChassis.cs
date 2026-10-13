using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Chassis-side part of the recovered MW 2005 SuspensionRacer model.
    /// This class owns the state and equations which sit around the tire solver:
    /// steering tables, Ackermann, speedbreaker, wall steering, drift yaw control,
    /// burnout/fishtail traction, and reference aerodynamics.
    ///
    /// Source: dbalatoni13/nfsmw, 3bb4ab24df54840a290310fe6d249bc13abe8235,
    /// src/Speed/Indep/Src/Physics/Behaviors/SuspensionRacer.cpp
    /// (DoHumanSteering, CalculateMaxSteering, CalculateSteeringSpeed,
    /// DoWallSteer, Burnout::Update, DoDrifting, TuneWheelParams, OnTaskSimulate).
    /// </summary>
    public sealed class MostWantedChassis
    {
        public const float AbsoluteMaximumDegrees = 45f;
        private const float Mph = 0.44703001f;
        private static readonly float[] SteeringRange =
        {
            40f,
            20f,
            10f,
            5.5f,
            4.5f,
            3.25f,
            2.9f,
            2.9f,
            2.9f,
            2.9f
        };
        private static readonly float[] SteeringWheelRange =
        {
            45f,
            15f,
            11f,
            8f,
            7f,
            7f,
            7f,
            7f,
            7f,
            7f
        };
        private static readonly float[] SteeringSpeed =
        {
            1f,
            1f,
            1f,
            .56f,
            .5f,
            .35f,
            .3f,
            .3f,
            .3f,
            .3f
        };
        private static readonly float[] InputSpeed =
        {
            1f,
            1.05f,
            1.1f,
            1.5f,
            2.2f,
            3.1f
        };
        private static readonly float[] InputCoeff =
        {
            1f,
            1.05f,
            1.1f,
            1.2f,
            1.3f,
            1.4f
        };
        // JoystickInputToSteerRemap2, STEER_REMAP_MEDIUM, from SuspensionRacer.cpp.
        private static readonly float[] JoystickRemap =
        {
            -1f,
            -.736f,
            -.542f,
            -.4f,
            -.292f,
            -.214f,
            -.16f,
            -.123f,
            -.078f,
            -.036f,
            0f,
            .036f,
            .078f,
            .123f,
            .16f,
            .214f,
            .292f,
            .4f,
            .542f,
            .736f,
            1f
        };
        private static readonly Vector2[] BurnoutFriction =
        {
            new Vector2(0f, 1f), new Vector2(5f, .8f), new Vector2(9f, .9f),
            new Vector2(12.6f, .833f), new Vector2(17.1f, .72f), new Vector2(25f, .65f)
        };
        private static readonly float[] DriftRearFriction =
        {
            1.1f,
            .95f,
            .87f,
            .77f,
            .67f,
            .6f,
            .51f,
            .43f,
            .37f,
            .34f
        };
        private static readonly float[] LateralGripScale =
        {
            .833f,
            .958f,
            1.008f,
            1.0167f,
            1.033f,
            1.033f,
            1.033f,
            1.0167f,
            1f,
            1f
        };
        private static readonly float[] TractionScale =
        {
            .909f,
            1.045f,
            1.09f,
            1.09f,
            1.09f,
            1.09f,
            1.09f,
            1.045f,
            1f,
            1f
        };
        private static readonly Vector2[] DriftStabilizer =
        {
            new Vector2(0f, 0f),
            new Vector2(.2617994f, .1f),
            new Vector2(.52359879f, .45f),
            new Vector2(.78539819f, .85f),
            new Vector2(1.0471976f, .95f),
            new Vector2(1.5533431f, 1.15f),
            new Vector2(1.5707964f, 0f)
        };
        private readonly SampleWindow inputAverage = new SampleWindow(33, .55);
        private readonly SampleWindow inputSpeedAverage = new SampleWindow(9, .15);
        private float previousInput;
        private float previousSteer;
        private float lastMaximum = AbsoluteMaximumDegrees;
        private float yawControl = 1f;
        private float collisionTimer;
        private float driftValue;
        private DriftState driftState;
        private int burnoutState;
        private float burnoutTime;
        private float burnoutAllow;
        private float gameBreaker;
        private float now;
        private float rearLeftSlip;
        private float rearRightSlip;
        private float wallNoseTurn;
        private float wallSideTurn;
        private float jumpTime;
        private float jumpMaxAltitude;
        private enum DriftState
        {
            Out,
            Enter,
            In,
            Exit
        }

        public float SteeringAngleDegrees => previousSteer;
        public float MaximumSteeringDegrees { get; private set; }
        public float DriftAmount => driftValue;
        public float RearDriftFriction { get; private set; } = 1f;
        public float YawFrictionMultiplier { get; private set; } = 1f;
        public float BurnoutTraction { get; private set; } = 1f;
        public float YawControl => yawControl;
        public float GameBreaker => gameBreaker;

        public readonly struct Frame
        {
            public readonly VehicleInputState Input;
            public readonly float DeltaTime, ForwardSpeed, SlipAngleRadians, LocalYawRate, LocalLateralSpeed, BodySpeed;
            public readonly int GroundedWheels, Gear, BlownTires;
            public readonly bool PlayerSteering, WheelSpeedSensitive, WheelSpeedInsensitive, DragStyle, Staging;
            public readonly float NosBoost, ShiftBoost, PerfectLaunch;
            public readonly float SteeringCoefficient, HandlingTuning, BrakeTuning;
            public readonly bool GameBreakerActive;
            public readonly float FrontAxle, RearAxle, FrontTrack, Grade;
            public readonly float MaxSpeedometer;
            public readonly float[] YawControlTable;
            public readonly float MaxSlip, GripScale, TractionScale, SteerDragReduction;
            public Frame(VehicleInputState input, float deltaTime, float forwardSpeed, float slipAngleRadians, float localYawRate, int groundedWheels, int gear = 1, bool playerSteering = true, bool wheelSpeedSensitive = false, bool wheelSpeedInsensitive = false, bool dragStyle = false, float steeringCoefficient = 1f, float handlingTuning = 0f, float brakeTuning = 0f, bool gameBreakerActive = false, float frontAxle = 1.3f, float rearAxle = -1.3f, float frontTrack = 1.5f, float localLateralSpeed = 0f, float maxSpeedometer = 0f, float[] yawControlTable = null, float maxSlip = 0f, float gripScale = 1f, float tractionScale = 1f, float steerDragReduction = .15f, float grade = 0f, float nosBoost = 1f, float shiftBoost = 1f, bool staging = false, int blownTires = 0, float perfectLaunch = 0f, float bodySpeed = 0f)
            {
                Input = input.Clamped();
                DeltaTime = deltaTime;
                ForwardSpeed = forwardSpeed;
                SlipAngleRadians = slipAngleRadians;
                LocalYawRate = localYawRate;
                LocalLateralSpeed = localLateralSpeed;
                GroundedWheels = groundedWheels;
                Gear = gear;
                PlayerSteering = playerSteering;
                WheelSpeedSensitive = wheelSpeedSensitive;
                WheelSpeedInsensitive = wheelSpeedInsensitive;
                DragStyle = dragStyle;
                Staging = staging;
                NosBoost = nosBoost;
                ShiftBoost = shiftBoost;
                BlownTires = blownTires;
                PerfectLaunch = perfectLaunch;
                SteeringCoefficient = steeringCoefficient;
                HandlingTuning = handlingTuning;
                BrakeTuning = brakeTuning;
                GameBreakerActive = gameBreakerActive;
                FrontAxle = frontAxle;
                RearAxle = rearAxle;
                FrontTrack = frontTrack;
                Grade = grade;
                MaxSpeedometer = maxSpeedometer;
                YawControlTable = yawControlTable;
                MaxSlip = maxSlip;
                GripScale = gripScale;
                TractionScale = tractionScale;
                SteerDragReduction = steerDragReduction;
                BodySpeed = bodySpeed > 0f ? bodySpeed : Mathf.Sqrt(forwardSpeed * forwardSpeed + localLateralSpeed * localLateralSpeed);
            }
        }

        public readonly struct Result
        {
            public readonly float FrontLeftDegrees, FrontRightDegrees, SteeringDegrees;
            public readonly float BrakeFront, BrakeRear, RearGripMultiplier, TractionMultiplier;
            public Result(float left, float right, float steer, float frontBrake, float rearBrake, float rearGrip, float traction)
            {
                FrontLeftDegrees = left;
                FrontRightDegrees = right;
                SteeringDegrees = steer;
                BrakeFront = frontBrake;
                BrakeRear = rearBrake;
                RearGripMultiplier = rearGrip;
                TractionMultiplier = traction;
            }
        }

        public static float BaseRangeDegrees(float speed) => Sample(SteeringRange, speed, 0f, 160f);
        public static float ComputeMaxSlip(float speed, int gear)
        {
            if (gear < 0)
                return 71f;
            return Mathf.Clamp01((Mathf.Abs(speed) - 10f) / (71f - 10f)) + .5f;
        }

        public static float ComputeLateralGripScale(float speed, bool dragStyle)
        {
            if (dragStyle)
                return 3f;
            float r = Mathf.Clamp01(Mathf.Abs(speed) / (85f * Mph));
            return Sample(LateralGripScale, r, 0f, 1f) * 1.2f;
        }

        public static float ComputeTractionScale(float speed, int gear, bool dragStyle)
        {
            if (gear < 0)
                return 2f;
            if (dragStyle)
                return 1.1f;
            float r = Mathf.Clamp01(Mathf.Abs(speed) / (85f * Mph));
            return Sample(TractionScale, r, 0f, 1f) * 1.1f;
        }

        /// <summary>Begins the source tire frame. Call once before drive and wheel-force work.</summary>
        public void BeginStep(in Frame frame, MostWantedTire[] tires)
        {
            if (tires == null)
                return;
            gameBreaker = 0f; // Source OnTaskSimulate clears this before checking its player state.
            if (frame.GameBreakerActive)
                gameBreaker = 1f;
            rearLeftSlip = rearRightSlip = 0f;
            float speed = frame.BodySpeed;
            float steerDrag = Mathf.Lerp(frame.SteerDragReduction, 1f, gameBreaker);
            float maxSlip = ComputeMaxSlip(speed, frame.Gear);
            float grip = ComputeLateralGripScale(speed, frame.DragStyle);
            float traction = ComputeTractionScale(speed, frame.Gear, frame.DragStyle);
            if (frame.MaxSlip > 0f)
                maxSlip = frame.MaxSlip;
            grip *= frame.GripScale;
            traction *= frame.TractionScale;
            if (!frame.Staging && frame.PerfectLaunch > 0f)
                traction += frame.PerfectLaunch * .25f;
            for (int i = 0; i < tires.Length; i++)
                tires[i]?.BeginFrame(maxSlip, grip, traction, steerDrag);
        }

        /// <summary>Runs steering and TuneWheelParams, including source tire-side modifiers.</summary>
        public Result SteerAndTune(in Frame frame, VehicleTuning tuning, MostWantedTire[] tires, float rearLeftSlipRadians = 0f, float rearRightSlipRadians = 0f, float maximumSlip = -1f, int maximumSlipWheel = 0)
        {
            Result result = Step(frame, tuning, rearLeftSlipRadians, rearRightSlipRadians, maximumSlip, maximumSlipWheel);
            if (tires == null)
                return result;
            float speedMph = Mathf.Abs(frame.ForwardSpeed) / Mph;
            float carYaw = Mathf.Abs(frame.SlipAngleRadians);
            for (int i = 0; i < tires.Length; i++)
            {
                MostWantedTire tire = tires[i];
                if (tire == null)
                    continue;
                float brake = i < 2 ? result.BrakeFront : result.BrakeRear;
                // TuneWheelParams: low-speed throttle/brake blending and rear-only e-brake.
                if (frame.Input.Throttle > .8f && frame.Input.Brake > .5f && speedMph < 10f && tire.DriveTorque != 0f)
                    tire.SetBrake(speedMph * .05f);
                else
                    tire.SetBrake(brake);
                float ebrake = frame.Input.Handbrake ? 1f : 0f;
                if (ebrake > .2f && frame.SlipAngleRadians > .3f && frame.ForwardSpeed / Mph < 80f)
                    ebrake += .5f;
                tire.SetEBrake(i >= 2 ? ebrake : 0f);
                float overBoost = gameBreaker > 0f && i < 2 ? gameBreaker * Mathf.Abs(frame.Input.Steering) * .75f + 1f : 1f;
                tire.SetLateralBoost(overBoost);
                tire.SetDriftFriction(i >= 2 ? result.RearGripMultiplier : 1f);
                tire.SetTractionCircle(new Vector2(1f + frame.HandlingTuning * .2f, 1f - frame.HandlingTuning * .2f));
                tire.ScaleTractionBoost(frame.NosBoost * frame.ShiftBoost);
                if (i >= 2)
                    tire.ScaleTractionBoost(YawFrictionMultiplier);
                if (gameBreaker > 0f && i < 2)
                    tire.ScaleTractionBoost(overBoost);
                if ((frame.BlownTires & (1 << i)) != 0)
                {
                    tire.SetEBrake(0f);
                    tire.SetBrake(1f);
                    tire.ScaleTractionBoost(.3f);
                }

                if (frame.Staging)
                    tire.ScaleTractionBoost(.25f);
            }

            return result;
        }

        public void Reset()
        {
            inputAverage.Reset();
            inputSpeedAverage.Reset();
            previousInput = previousSteer = 0f;
            lastMaximum = AbsoluteMaximumDegrees;
            yawControl = 1f;
            collisionTimer = driftValue = 0f;
            driftState = DriftState.Out;
            burnoutState = 0;
            burnoutTime = burnoutAllow = 0f;
            gameBreaker = 0f;
            now = 0f;
            MaximumSteeringDegrees = 0f;
            RearDriftFriction = YawFrictionMultiplier = BurnoutTraction = 1f;
        }

        /// <summary>Source Chassis::DoJumpStabilizer, including landing gravity and active pitch/roll correction.</summary>
        public void ApplyJumpStabilizer(Rigidbody body, in Frame frame, Vector3 groundNormal, float altitude, bool inGroundContact)
        {
            if (body == null)
                return;
            int touching = frame.GroundedWheels;
            Vector3 normal = groundNormal.sqrMagnitude > .001f ? groundNormal.normalized : Vector3.up;
            float groundDot = Vector3.Dot(body.transform.up, normal);
            Vector3 force = Vector3.zero;
            if (touching == 0)
            {
                jumpTime += frame.DeltaTime;
                jumpMaxAltitude = Mathf.Max(jumpMaxAltitude, altitude);
                float accel = 3f;
                if (jumpTime > 1f && groundDot > .96f && jumpMaxAltitude > 2f && body.linearVelocity.y < 0f && altitude < 6f)
                    accel += (1f - Mathf.Clamp01(altitude / 6f)) * 10f * Mathf.Clamp01(body.linearVelocity.magnitude / 20f);
                force = Vector3.down * body.mass * accel;
            }
            else
            {
                jumpTime = 0f;
                jumpMaxAltitude = 0f;
            }

            Vector3 torque = Vector3.zero;
            if (touching < 2 && body.transform.up.y > .8f && !inGroundContact)
            {
                float speedRamp = Mathf.Clamp01(body.linearVelocity.magnitude / 5f);
                Vector3 localAngular = body.transform.InverseTransformDirection(body.angularVelocity);
                float damping = speedRamp * JumpStabilization(Mathf.Min(2f, localAngular.magnitude));
                torque = body.transform.TransformDirection(Vector3.Scale(localAngular, body.inertiaTensor) * -damping);
                torque.y = 0f;
            }

            if (touching == 0 && normal.y > .9f && body.transform.up.y > .1f && groundDot > .8f && altitude > .0001f)
            {
                float altitudeRamp = 1f - Mathf.Clamp01(altitude / 15f), speedRamp = Mathf.Clamp01(body.linearVelocity.magnitude / 5f);
                Vector3 flatForward = Vector3.Cross(body.transform.right, normal).normalized;
                float dot = Vector3.Dot(flatForward, body.transform.forward);
                if (dot < .99f)
                    torque += Vector3.Cross(flatForward, body.transform.forward).normalized * (body.mass * speedRamp * altitudeRamp * 40f * (dot - 1f));
                Vector3 flatRight = Vector3.Cross(normal, body.transform.forward).normalized;
                dot = Vector3.Dot(flatRight, body.transform.right);
                if (dot < .99f)
                    torque += Vector3.Cross(flatRight, body.transform.right).normalized * (body.mass * speedRamp * 20f * altitudeRamp * (dot - 1f));
            }

            if (force.sqrMagnitude > 0f)
                body.AddForce(force, ForceMode.Force);
            if (torque.sqrMagnitude > 0f)
                body.AddTorque(torque, ForceMode.Force);
        }

        private static float JumpStabilization(float x)
        {
            if (x <= .4f)
                return Mathf.Lerp(0f, .15f, x / .4f);
            return Mathf.Lerp(.15f, 5f, Mathf.Clamp01((x - .4f) / 1.6f));
        }

        /// <summary>Feeds the all-wheel slip maximum into Burnout::Update (source call occurs once per frame).</summary>
        public void UpdateBurnoutState(in Frame frame, float maximumSlip, int maximumSlipWheel)
        {
            if (!frame.DragStyle && frame.Gear == 1 && frame.Input.Throttle >= 1f)
                UpdateBurnout(frame, Mathf.Max(0f, maximumSlip), maximumSlipWheel, Mathf.Abs(frame.SlipAngleRadians) * Mathf.Rad2Deg);
            else
                ResetBurnout();
        }

        /// <summary>Applies the source drift stabilizer moment to the Unity rigidbody.</summary>
        public void ApplyDriftYawTorque(Rigidbody body, Vector3 up)
        {
            if (body == null || Mathf.Abs(ApplyDriftYaw) < 0.00001f)
                return;
            float inertia = Mathf.Max(.001f, body.inertiaTensor.y);
            body.AddTorque(up.normalized * ApplyDriftYaw * inertia, ForceMode.Force);
        }

        /// <summary>Runs the source frame state machine and returns front wheel commands.</summary>
        public Result Step(in Frame frame, VehicleTuning tuning, float rearLeftSlipRadians = 0f, float rearRightSlipRadians = 0f, float maximumSlip = -1f, int maximumSlipWheel = 0)
        {
            if (tuning == null || !float.IsFinite(frame.DeltaTime) || frame.DeltaTime <= 0f)
            {
                Reset();
                return default;
            }

            now += frame.DeltaTime;
            collisionTimer = Mathf.Max(0f, collisionTimer - frame.DeltaTime);
            // The original source clears mGameBreaker before its ramp-out branch. Keep that
            // ordering for parity; the ramp-out branch is consequently unreachable.
            // gameBreaker is established by BeginStep. Do not clear it here: source
            // steering and TuneWheelParams consume the value from this same frame.
            rearLeftSlip = rearLeftSlipRadians;
            rearRightSlip = rearRightSlipRadians;
            float steer = frame.PlayerSteering ? HumanSteering(frame, tuning) : Mathf.Clamp(frame.Input.Steering * AbsoluteMaximumDegrees, -AbsoluteMaximumDegrees, AbsoluteMaximumDegrees);
            float left = steer, right = steer;
            Ackermann(steer, frame.FrontAxle, frame.RearAxle, frame.FrontTrack, out left, out right);
            TuneWheelParams(frame, tuning);
            float rearSlip = Mathf.Max(Mathf.Abs(rearLeftSlipRadians), Mathf.Abs(rearRightSlipRadians));
            UpdateBurnoutState(frame, maximumSlip >= 0f ? maximumSlip : rearSlip, maximumSlip >= 0f ? maximumSlipWheel : 2);
            UpdateDrift(frame, tuning);
            return new Result(left, right, steer, frame.Input.Brake * (1f + frame.BrakeTuning * .5f), frame.Input.Brake * (1f - frame.BrakeTuning * .5f), RearDriftFriction, BurnoutTraction);
        }

        private float HumanSteering(in Frame f, VehicleTuning tuning)
        {
            var m = tuning.mostWanted;
            float range = f.WheelSpeedInsensitive ? AbsoluteMaximumDegrees : Sample(f.WheelSpeedSensitive ? SteeringWheelRange : SteeringRange, f.ForwardSpeed, 0f, 160f);
            float brakeScale = 1f - (f.Input.Throttle + 1f - (f.Input.Brake + (f.Input.Handbrake ? 1f : 0f)) * .5f) * .5f;
            range *= 1.45f * brakeScale * Sample(SteeringSpeed, f.ForwardSpeed, 0f, 160f) + 1f;
            range *= Sample(InputCoeff, Mathf.Abs(inputAverage.Mean), 0f, 1f);
            range *= 1f + f.HandlingTuning * .2f;
            float rear = f.Input.Steering > 0f ? rearRightSlip : rearLeftSlip;
            if (f.Input.Steering * rear > 0f)
                range = Mathf.Min(AbsoluteMaximumDegrees, Mathf.Max(range, Mathf.Abs(rear) * Mathf.Rad2Deg));
            else if (Mathf.Abs(inputAverage.Mean) >= .5f)
                range = Mathf.Max(range, lastMaximum);
            range = Mathf.Clamp(range, 0f, AbsoluteMaximumDegrees);
            lastMaximum = MaximumSteeringDegrees = range;
            float target = Mathf.Clamp(f.Input.Steering * range * m.steeringCoefficient, -AbsoluteMaximumDegrees, AbsoluteMaximumDegrees);
            float remappedInput = Sample(JoystickRemap, f.Input.Steering, -1f, 1f);
            // The source rate filter operates on the remapped stick history. Comparing
            // raw input against remapped history makes a held partial steer look like a
            // fresh movement every tick.
            float rateInput = Mathf.Abs((remappedInput - previousInput) / f.DeltaTime);
            inputSpeedAverage.Add(Sample(InputSpeed, rateInput, 0f, 10f), now);
            float rate = 180f * Sample(SteeringSpeed, f.ForwardSpeed, 0f, 160f) * inputSpeedAverage.Mean * Sample(InputCoeff, Mathf.Abs(inputAverage.Mean), 0f, 1f) * m.steeringCoefficient;
            previousSteer = Mathf.MoveTowards(previousSteer, target, Mathf.Max(0f, rate) * f.DeltaTime);
            inputAverage.Add(remappedInput, now);
            previousInput = remappedInput;
            return previousSteer;
        }

        public static void Ackermann(float inside, float frontAxle, float rearAxle, float track, out float left, out float right)
        {
            float wb = Mathf.Max(.01f, frontAxle - rearAxle);
            float a = Mathf.Abs(inside) * Mathf.Deg2Rad;
            float outside = Mathf.Sign(inside) * wb * a / (Mathf.Max(0f, track) * a + wb) * Mathf.Rad2Deg;
            // Positive steering is the right turn in the source vehicle convention.
            if (inside >= 0f)
            {
                left = outside;
                right = inside;
            }
            else
            {
                left = inside;
                right = outside;
            }
        }

        private void TuneWheelParams(in Frame f, VehicleTuning t)
        {
            if (f.Input.Handbrake)
                yawControl = Mathf.Max(.5f, yawControl - 20f * f.DeltaTime);
            else
                yawControl = Mathf.Min(1f, yawControl + f.DeltaTime);
            float yaw = Mathf.Abs(f.SlipAngleRadians);
            float speed = Mathf.Abs(f.ForwardSpeed);
            float boost = YawFrictionBoost(yaw, f.Input.Handbrake ? 1f : 0f, speed, CalcYawControlLimit(f, t), f.Grade) - 1f;
            YawFrictionMultiplier = 1f + yawControl * (1f - driftValue) * boost;
        }

        /// <summary>
        /// Source DoDriveForces. Call after the powertrain has produced drive torque and
        /// before wheel force integration. Grounded is ordered FL, FR, RL, RR.
        /// </summary>
        public void ApplyDriveForces(Rigidbody body, VehicleTuning tuning, MostWantedTire[] tires, bool[] grounded, float driveTorque, in Frame frame, bool legacyFrontDriven = true, bool legacyRearDriven = true)
        {
            if (tires == null || tires.Length < 4 || grounded == null || grounded.Length < 4 || Mathf.Abs(driveTorque) <= .00001f)
                return;
            float centerBias = Mathf.Clamp01(tuning != null ? tuning.awdFrontTorqueBias : .5f);
            bool frontDriven = tuning == null || tuning.driveLayout == VehicleDriveLayout.LegacyBindings && legacyFrontDriven || tuning.driveLayout == VehicleDriveLayout.Fwd || tuning.driveLayout == VehicleDriveLayout.Awd;
            bool rearDriven = tuning == null || tuning.driveLayout == VehicleDriveLayout.LegacyBindings && legacyRearDriven || tuning.driveLayout == VehicleDriveLayout.Rwd || tuning.driveLayout == VehicleDriveLayout.Awd;
            float centerFactor = tuning != null && tuning.mostWanted != null ? tuning.mostWanted.differential.z : 0f;
            MostWantedDifferential center = new MostWantedDifferential
            {
                Bias = centerBias,
                Factor = centerFactor,
                AngularVelocity = new Vector2((tires[0].AngularVelocity + tires[1].AngularVelocity) * .5f, (tires[2].AngularVelocity + tires[3].AngularVelocity) * .5f),
                HasTraction = new Vector2((grounded[0] || grounded[1]) ? 1f : 0f, (grounded[2] || grounded[3]) ? 1f : 0f)
            };
            center.CalcSplit(false);
            float[] axleTorque =
            {
                frontDriven && rearDriven ? driveTorque * center.TorqueSplit.x : frontDriven ? driveTorque : 0f,
                rearDriven && frontDriven ? driveTorque * center.TorqueSplit.y : rearDriven ? driveTorque : 0f
            };
            for (int axle = 0; axle < 2; axle++)
            {
                if (Mathf.Abs(axleTorque[axle]) <= .00001f)
                    continue;
                int a = axle * 2;
                float[] tractionControl =
                {
                    1f,
                    1f
                }, tractionBoost =
                {
                    1f,
                    1f
                };
                float fwdSlip = (tires[a].Slip + tires[a + 1].Slip) * .5f;
                float latSlip = (tires[a].LateralSpeed + tires[a + 1].LateralSpeed) * .5f;
                float deltaLat = latSlip - frame.LocalLateralSpeed;
                float deltaFwd = fwdSlip - frame.ForwardSpeed;
                if (deltaLat * frame.Input.Steering > 0f && axleTorque[axle] * fwdSlip > 0f)
                {
                    float limit = Mathf.Clamp01((Mathf.Abs(deltaFwd) - 1f) / 19f);
                    float angle = Mathf.Abs(frame.Input.Steering * Mathf.Atan2(deltaLat, Mathf.Abs(deltaFwd)) * Mathf.Rad2Deg);
                    limit *= Mathf.Clamp01((angle - 1f) / 15f);
                    if (limit > 0f)
                    {
                        int side = deltaLat > 0f ? 1 : 0;
                        tractionControl[side] = 1f - limit;
                        tractionControl[1 - side] = 1f - limit * .5f;
                        tractionBoost[1 - side] = 1f + limit;
                    }
                }

                float factor = tuning != null && tuning.mostWanted != null ? tuning.mostWanted.differential[axle] : 0f;
                MostWantedDifferential diff = new MostWantedDifferential
                {
                    Bias = burnoutState != 0 ? (burnoutState & 1) != 0 ? BurnoutTraction * .5f : 1f - BurnoutTraction * .5f : .5f,
                    Factor = factor,
                    AngularVelocity = new Vector2(tires[a].AngularVelocity, tires[a + 1].AngularVelocity),
                    HasTraction = new Vector2(grounded[a] ? 1f : 0f, grounded[a + 1] ? 1f : 0f)
                };
                diff.CalcSplit(burnoutState != 0);
                for (int side = 0; side < 2; side++)
                    if (grounded[a + side])
                    {
                        tires[a + side].ApplyDriveTorque(axleTorque[axle] * (side == 0 ? diff.TorqueSplit.x : diff.TorqueSplit.y) * tractionControl[side]);
                        tires[a + side].ScaleTractionBoost(tractionBoost[side]);
                    }
            }
        }

        public static float YawFrictionBoost(float yaw, float ebrake, float speed, float yawControl, float grade)
        {
            float result = 1f + Mathf.Abs(grade);
            if (ebrake > .5f && Mathf.Abs(yaw) < 20f * Mathf.Deg2Rad)
                return result;
            float speedFactor = (Mathf.Abs(speed) - 0f) / 30f;
            float bonus = Mathf.Abs(yaw) * yawControl * speedFactor;
            return result + Mathf.Min(.35f, bonus);
        }

        private static float CalcYawControlLimit(in Frame frame)
        {
            if (frame.MaxSpeedometer <= 0f || frame.YawControlTable == null || frame.YawControlTable.Length == 0)
                return 0f;
            float p = Mathf.Clamp01(Mathf.Abs(frame.ForwardSpeed) / frame.MaxSpeedometer) * (frame.YawControlTable.Length - 1);
            int i = Mathf.Min(Mathf.FloorToInt(p), frame.YawControlTable.Length - 2);
            return frame.YawControlTable.Length == 1 ? frame.YawControlTable[0] : Mathf.Lerp(frame.YawControlTable[i], frame.YawControlTable[i + 1], p - i);
        }

        private static float CalcYawControlLimit(in Frame frame, VehicleTuning tuning)
        {
            if (tuning?.mostWanted == null)
                return CalcYawControlLimit(frame);
            float maxSpeed = frame.MaxSpeedometer > 0f ? frame.MaxSpeedometer : Mathf.Max(.001f, tuning.chassis.maxSpeedKph / 3.6f);
            float p = Mathf.Clamp01(Mathf.Abs(frame.ForwardSpeed) / maxSpeed) * 3f;
            int i = Mathf.Min(Mathf.FloorToInt(p), 2);
            Vector4 y = tuning.mostWanted.yawControl;
            float a = i == 0 ? y.x : i == 1 ? y.y : y.z, b = i == 0 ? y.y : i == 1 ? y.z : y.w;
            return Mathf.Lerp(a, b, p - i);
        }

        private void UpdateBurnout(in Frame f, float maxSlip, int maxSlipWheel, float slipDegrees)
        {
            if (burnoutState != 0)
            {
                if (f.ForwardSpeed / Mph > 5f && slipDegrees * Mathf.Deg2Rad > .5f)
                    ResetBurnout();
                else if (maxSlip < .5f)
                {
                    burnoutAllow += f.DeltaTime;
                    if (burnoutAllow > 1f)
                        ResetBurnout();
                }
                else
                {
                    burnoutAllow = 0f;
                    burnoutTime -= f.DeltaTime;
                    if (burnoutTime < 0f)
                    {
                        burnoutState--;
                        burnoutTime = 2f;
                    }
                }
            }
            else if (f.ForwardSpeed / Mph < 20f && maxSlip > .5f)
            {
                float c = SamplePairs(BurnoutFriction, maxSlip);
                BurnoutTraction = c / 1.4f;
                burnoutState = (int)(6f * (1.5f - c) + (maxSlipWheel & 1));
                burnoutTime = 2f;
            }
        }

        private void ResetBurnout()
        {
            burnoutState = 0;
            burnoutTime = burnoutAllow = 0f;
            BurnoutTraction = 1f;
        }

        private void UpdateDrift(in Frame f, VehicleTuning t)
        {
            if (driftState != DriftState.Out && f.DragStyle)
            {
                driftState = DriftState.Out;
                driftValue = 0f;
                return;
            }

            float change = driftState == DriftState.In || driftState == DriftState.Enter ? 8f : -2f;
            driftValue = Mathf.Clamp01(driftValue + change * f.DeltaTime);
            float slip = Mathf.Abs(f.SlipAngleRadians);
            if (driftValue == 0f)
                driftState = DriftState.Out;
            else if (driftValue == 1f)
                driftState = DriftState.In;
            if (driftState > DriftState.Enter)
            {
                bool hold = f.LocalYawRate * f.SlipAngleRadians < 0f && slip <= .25f && Mathf.Abs(f.ForwardSpeed) > 30f * Mph && inputAverage.Mean * f.SlipAngleRadians <= 0f && slip > 12f * Mathf.Deg2Rad;
                if (hold || f.Input.Throttle * f.Input.Steering * f.SlipAngleRadians > 12f * Mathf.Deg2Rad && Mathf.Abs(f.ForwardSpeed) > 30f * Mph)
                    driftState = DriftState.In;
                else if (f.Input.Throttle * slip > 12f * Mathf.Deg2Rad)
                    driftState = DriftState.Enter;
                else
                    driftState = DriftState.Exit;
            }
            else if (Mathf.Abs(f.ForwardSpeed) > 30f * Mph && (f.Input.Handbrake || slip > 12f * Mathf.Deg2Rad))
                driftState = DriftState.Enter;
            if (driftValue <= 0f)
            {
                RearDriftFriction = 1f;
                return;
            }

            float coeff = slip * .5f + (f.Input.Steering * f.SlipAngleRadians > 0f ? Mathf.Abs(f.Input.Steering) * 4f : 0f) + Mathf.Abs(f.LocalYawRate) * .5f;
            RearDriftFriction = Sample(DriftRearFriction, coeff * driftValue, 0f, 1f);
            if (f.SlipAngleRadians * f.LocalYawRate < 0f && f.GroundedWheels >= 2)
            {
                float stabilizer = SamplePairs(DriftStabilizer, slip) * 4f; /* applied by ApplyDriftYaw */
                ApplyDriftYaw = -f.LocalYawRate * stabilizer * driftValue;
            }
        }

        public float ApplyDriftYaw { get; private set; }

        public void ApplyAerodynamics(Rigidbody body, VehicleTuning tuning, VehicleWheel[] wheels, float throttle, bool dragStyle)
        {
            float frontAxle = tuning?.mostWanted?.frontAxle ?? 1.3f;
            float rearAxle = tuning?.mostWanted != null ? frontAxle - tuning.mostWanted.wheelBase : -1.3f;
            ApplyAerodynamics(body, tuning, wheels, throttle, dragStyle, frontAxle, rearAxle);
        }

        public void ApplyAerodynamics(Rigidbody body, VehicleTuning tuning, VehicleWheel[] wheels, float throttle, bool dragStyle, float frontAxleZ, float rearAxleZ)
        {
            if (body == null || tuning == null)
                return;
            Vector3 v = body.linearVelocity;
            float speed = v.magnitude;
            if (speed <= .05f)
                return;
            var a = tuning.aero;
            var mw = tuning.mostWanted;
            float dragCoefficient = mw != null ? mw.dragCoefficient : a.dragCoefficient;
            float drag = speed * dragCoefficient;
            float dragPct = 1f - gameBreaker * .75f;
            float aeroPct = dragStyle ? 1.5f : 1f;
            drag += drag * (2f - 1f) * (1f - Mathf.Clamp01(throttle));
            Vector3 dragCenter = body.centerOfMass;
            int contacts = 0;
            if (wheels != null)
                foreach (var w in wheels)
                    if (w != null && w.Grounded)
                        contacts++;
            if (contacts >= 2)
                dragCenter.y -= .1f * (1f - Mathf.Clamp01(throttle));
            body.AddForceAtPosition(-v * drag * dragPct, transformPoint(body, dragCenter), ForceMode.Force);
            float upness = contacts >= 2 ? 1f : Mathf.Max(body.transform.up.y, 0f);
            float forwardness = Mathf.Max(Vector3.Dot(v / speed, body.transform.forward), 0f);
            forwardness = Mathf.Max(.4f, Mathf.Pow(forwardness, .5f));
            float force = aeroPct * upness * forwardness * speed * tuning.mostWanted.linearDownforceCoefficient;
            if (contacts == 0)
                force *= .8f;
            Vector3 aeroCenter = body.centerOfMass;
            float downforceBalance = mw != null ? mw.aeroCenterOfGravity * .01f : a.downforceBalance;
            if (contacts > 0)
                aeroCenter.z = Mathf.Lerp(rearAxleZ, frontAxleZ, downforceBalance);
            body.AddForceAtPosition(-body.transform.up * Mathf.Max(0f, force), transformPoint(body, aeroCenter), ForceMode.Force);
        }

        private static Vector3 transformPoint(Rigidbody b, Vector3 p) => b.transform.TransformPoint(p);
        public void OnWallCollision(Vector3 normal, Vector3 position, Vector3 closingVelocity, Vector3 objectVelocity, Rigidbody body)
        {
            if (body == null || Mathf.Abs(normal.y) >= .1f || closingVelocity.sqrMagnitude >= 56.25f)
                return;
            Vector3 f = body.transform.forward, r = body.transform.right;
            Vector3 local = position - body.position;
            if (objectVelocity.sqrMagnitude < 6.25f && Vector3.Dot(normal, f) <= 0f && Vector3.Dot(local, f) > body.transform.lossyScale.z * .75f)
            {
                collisionTimer = Mathf.Max(collisionTimer, 1f);
                float dot = Vector3.Dot(normal, r);
                wallNoseTurn = (dot > 0f ? 1f : -1f) - dot;
            }

            if (Mathf.Abs(Vector3.Dot(normal, r)) > .00001f && Mathf.Abs(Vector3.Dot(local, f)) > body.transform.lossyScale.z * .75f)
                wallSideTurn = Vector3.Dot(normal, r);
        }

        /// <summary>Source DoWallSteer: artificial yaw velocity used to pull away from a wall.</summary>
        public void ApplyWallSteer(Rigidbody body, in Frame frame, int groundedWheels, bool forwardGear)
        {
            if (body == null || groundedWheels <= 2)
                return;
            float dW;
            if (wallNoseTurn != 0f && frame.Input.Throttle > 0f)
            {
                dW = frame.Input.Steering * frame.Input.Throttle * .125f;
                if (wallNoseTurn * dW >= 0f)
                    body.AddTorque(body.transform.up * dW * Mathf.Abs(wallNoseTurn), ForceMode.VelocityChange);
            }

            if (wallSideTurn * frame.Input.Steering * frame.Input.Throttle > 0f && !forwardGear)
            {
                dW = -frame.Input.Steering * frame.Input.Throttle * .125f;
                body.AddTorque(body.transform.up * dW * Mathf.Abs(wallSideTurn), ForceMode.VelocityChange);
            }

            wallNoseTurn = wallSideTurn = 0f;
        }

        private static float Sample(float[] values, float x, float min, float max) => MostWantedVehicleMath.SampleUniform(values, x, min, max);
        private static float SamplePairs(Vector2[] values, float x)
        {
            if (x <= values[0].x)
                return values[0].y;
            for (int i = 1; i < values.Length; i++)
                if (x <= values[i].x)
                    return Mathf.Lerp(values[i - 1].y, values[i].y, Mathf.InverseLerp(values[i - 1].x, values[i].x, x));
            return values[values.Length - 1].y;
        }

        private sealed class SampleWindow
        {
            readonly float[] values;
            readonly double[] stamps;
            readonly double duration;
            int first, count;
            double sum;
            public float Mean => count == 0 ? 0f : (float)(sum / count);

            public SampleWindow(int capacity, double seconds)
            {
                values = new float[capacity];
                stamps = new double[capacity];
                duration = seconds;
            }

            public void Add(float value, double time)
            {
                while (count > 0 && time - stamps[first] > duration)
                    Remove();
                if (count == values.Length)
                    Remove();
                int i = (first + count) % values.Length;
                values[i] = value;
                stamps[i] = time;
                count++;
                sum += value;
            }

            void Remove()
            {
                sum -= values[first];
                first = (first + 1) % values.Length;
                count--;
            }

            public void Reset()
            {
                Array.Clear(values, 0, values.Length);
                Array.Clear(stamps, 0, stamps.Length);
                first = count = 0;
                sum = 0;
            }
        }
    }
}
