using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// Data consumed by the recovered SuspensionRacer::Tire. BrakeTorqueNm values are
    /// the original FTLB2NM values; the source's BrakingTorque (4) and EBrakingTorque
    /// (10) multipliers are deliberately applied by the simulation below.
    public readonly struct MostWantedTireSpecification
    {
        public readonly float[] GripScale, DynamicGrip, StaticGrip;
        public readonly float BrakeTorqueNm, EBrakeTorqueNm;
        /// Source BRAKE_LOCK axle attribute. It scales service brake torque only
        /// during lock detection; the source does not apply it to wheel torque.
        public readonly float BrakeLockFactor;
        /// Source StaticToDynamicBrakeForceRatio (normally 1.2), exposed so tuning can
        /// provide the raw value instead of baking a policy into the adapter.
        public readonly float StaticToDynamicBrakeForceRatio;
        public readonly float[][] LoadSensitivity;
        public readonly float SurfaceLateralGrip, SurfaceDriveGrip, SurfaceRollingResistance;
        public MostWantedTireSpecification(float[] gripScale, float[] dynamicGrip, float[] staticGrip, float brakeTorqueNm, float eBrakeTorqueNm, float[][] loadSensitivity = null, float surfaceLateralGrip = 1f, float surfaceDriveGrip = 1f, float surfaceRollingResistance = 1f, float staticToDynamicBrakeForceRatio = 1.2f, float brakeLockFactor = 1f)
        {
            GripScale = gripScale ?? new[]
            {
                1f,
                1f
            };
            DynamicGrip = dynamicGrip ?? new[]
            {
                1f,
                1f
            };
            StaticGrip = staticGrip ?? new[]
            {
                1f,
                1f
            };
            BrakeTorqueNm = brakeTorqueNm;
            EBrakeTorqueNm = eBrakeTorqueNm;
            StaticToDynamicBrakeForceRatio = staticToDynamicBrakeForceRatio;
            BrakeLockFactor = brakeLockFactor;
            LoadSensitivity = loadSensitivity ?? MostWantedTire.DefaultLoadSensitivity;
            SurfaceLateralGrip = surfaceLateralGrip;
            SurfaceDriveGrip = surfaceDriveGrip;
            SurfaceRollingResistance = surfaceRollingResistance;
        }

        public static MostWantedTireSpecification Default => new MostWantedTireSpecification(null, null, null, 0, 0);

        internal float Axle(float[] values, int axle) => values[Mathf.Clamp(axle, 0, values.Length - 1)];
    }

    /// Pure translation of SuspensionRacer::Tire. It computes forces and angular state;
    /// the vehicle adapter owns transforms, contacts, and application of those forces.
    public sealed class MostWantedTire
    {
        public const float WheelMomentOfInertia = 10f, RollingFriction = 2f;
        public const float BrakingTorque = 4f, EBrakingTorque = 10f;
        public static readonly float[][] DefaultLoadSensitivity =
        {
            new[]
            {
                0f,
                0f,
                0f,
                0f,
                0f,
                0f
            },
            new[]
            {
                0f,
                1.2f,
                2.3f,
                3f,
                3f,
                2.8f
            },
            new[]
            {
                0f,
                1.7f,
                3.2f,
                4.3f,
                5.1f,
                5.2f
            },
            new[]
            {
                0f,
                1.8f,
                3.5f,
                4.9f,
                5.8f,
                6.1f
            },
            new[]
            {
                0f,
                1.83f,
                3.6f,
                5f,
                5.96f,
                6.4f
            },
            new[]
            {
                0f,
                1.86f,
                3.7f,
                5.1f,
                6.13f,
                6.7f
            },
            new[]
            {
                0f,
                1.9f,
                3.8f,
                5.2f,
                6.3f,
                7.1f
            }
        };
        public enum LastRotationSign
        {
            WasPositive,
            WasZero,
            WasNegative
        }

        readonly MostWantedTireSpecification specs;
        readonly int wheelIndex, axleIndex;
        readonly float radius;
        float brake, eBrake, angularVelocity, load, lateralForce, longitudinalForce, driveTorque, brakeTorque;
        float lateralBoost = 1f, tractionBoost = 1f, slip, lastTorque, roadSpeed, angularAcceleration, traction = 1f;
        float bottomOutTime, slipAngle, maxSlip = .5f, gripBoost = 1f, driftFriction = 1f, lateralSpeed, dragReduction;
        float surfaceLateralGrip, surfaceDriveGrip, surfaceRollingResistance;
        Vector2 tractionCircle = Vector2.one;
        bool brakeLocked;
        LastRotationSign lastSign = LastRotationSign.WasZero;
        public MostWantedTire(float radius, int wheelIndex, MostWantedTireSpecification specs)
        {
            this.radius = Mathf.Max(radius, .1f);
            this.wheelIndex = wheelIndex;
            axleIndex = wheelIndex >> 1;
            this.specs = specs;
            surfaceLateralGrip = specs.SurfaceLateralGrip;
            surfaceDriveGrip = specs.SurfaceDriveGrip;
            surfaceRollingResistance = specs.SurfaceRollingResistance;
        }

        public float Radius => radius;
        public int WheelIndex => wheelIndex;
        public int AxleIndex => axleIndex;
        public float Brake { get => brake; set => brake = value; }
        public float EBrake { get => eBrake; set => eBrake = value; }
        public float AngularVelocity { get => angularVelocity; set => angularVelocity = value; }
        public float AngularAcceleration => angularAcceleration;
        public float Load => load;
        public float LateralForce => lateralForce;
        public float LongitudinalForce => longitudinalForce;
        public float DriveTorque => driveTorque;
        public float BrakeTorque => brakeTorque;
        public float TotalTorque => driveTorque + brakeTorque;
        public float Traction => traction;
        public float Slip => slip;
        public float ToleratedSlip => maxSlip;
        public float RoadSpeed => roadSpeed;
        public float LateralSpeed => lateralSpeed;
        public float SlipAngle => slipAngle;
        public bool BrakeLocked => brakeLocked;
        public float BottomOutTime { get => bottomOutTime; set => bottomOutTime = value; }
        public bool IsSteeringWheel => wheelIndex < 2;
        public bool IsSlipping => traction >= 1f;
        public float SurfaceLateralGrip => surfaceLateralGrip;
        public float SurfaceDriveGrip => surfaceDriveGrip;
        public float SurfaceRollingResistance => surfaceRollingResistance;

        public void SetSurface(float lateralGrip, float driveGrip, float rollingResistance)
        {
            surfaceLateralGrip = lateralGrip;
            surfaceDriveGrip = driveGrip;
            surfaceRollingResistance = rollingResistance;
        }

        public void SetBrake(float value) => brake = value;
        public void SetEBrake(float value) => eBrake = value;
        public void SetLateralBoost(float value) => lateralBoost = value;
        public void SetDriftFriction(float value) => driftFriction = value;
        public void ScaleTractionBoost(float value) => tractionBoost *= value;
        public void SetTractionCircle(Vector2 value) => tractionCircle = value;
        public void ApplyDriveTorque(float value)
        {
            if (!brakeLocked)
                driveTorque += value;
        }

        public void ApplyBrakeTorque(float value)
        {
            if (!brakeLocked)
                brakeTorque += value;
        }

        public void BeginFrame(float maxSlip, float gripBoost, float tractionBoost, float dragReduction)
        {
            this.maxSlip = maxSlip;
            driveTorque = brakeTorque = 0;
            lateralForce = longitudinalForce = 0;
            tractionCircle = Vector2.one;
            this.tractionBoost = tractionBoost;
            this.gripBoost = gripBoost;
            driftFriction = 1;
            this.dragReduction = dragReduction;
        }

        public void MatchSpeed(float speed)
        {
            angularVelocity = speed / radius;
            roadSpeed = speed;
            traction = 1;
            brakeLocked = false;
            slip = angularAcceleration = lastTorque = 0;
            brake = eBrake = lateralSpeed = 0;
        }

        static float Table(float[] t, float x)
        {
            x = Mathf.Clamp(x, 0, 10);
            float p = x * .5f;
            int i = Mathf.FloorToInt(p);
            if (i >= 5)
                return t[5];
            return Mathf.Lerp(t[i], t[i + 1], p - i);
        }

        float ComputeLateralForce(float tireLoad, float angle)
        {
            float norm = angle * 360f * .5f;
            int table = Mathf.FloorToInt(norm);
            float loadValue = tireLoad * .001f * .8f;
            float[][] curves = specs.LoadSensitivity;
            if (table > 5)
                return specs.Axle(specs.GripScale, axleIndex) * 1000f * gripBoost * 2.5f * Table(curves[6], loadValue);
            float low = Table(curves[table], loadValue), high = Table(curves[table + 1], loadValue);
            return specs.Axle(specs.GripScale, axleIndex) * 1000f * gripBoost * 2.5f * ((norm - table) * (high - low) + low);
        }

        float PilotFactor(float speed)
        {
            if (brakeLocked || angularVelocity < 0 || IsSteeringWheel)
                return 1;
            return Mathf.Lerp(.85f, 1f, Mathf.Clamp01((speed - 13.4112f) / 8.9408f));
        }

        void CheckSign()
        {
            if (lastSign == LastRotationSign.WasPositive && angularVelocity < 0)
                angularVelocity = 0;
            else if (lastSign == LastRotationSign.WasNegative && angularVelocity > 0)
                angularVelocity = 0;
            lastSign = angularVelocity > 1e-6f ? LastRotationSign.WasPositive : angularVelocity < -1e-6f ? LastRotationSign.WasNegative : LastRotationSign.WasZero;
        }

        void CheckForBrakeLock(float groundForce)
        {
            float bt = brake * specs.BrakeTorqueNm * 4 * specs.BrakeLockFactor, ebt = eBrake * specs.EBrakeTorqueNm * 10, available = (bt + ebt) * specs.StaticToDynamicBrakeForceRatio;
            bool canLock = available > groundForce * radius + Mathf.Abs(angularVelocity) * 100;
            brakeLocked = canLock && available > 1;
            if (canLock)
                angularVelocity = 0;
        }

        public void UpdateFree(float dt)
        {
            load = slip = traction = slipAngle = 0;
            CheckForBrakeLock(0);
            if (!brakeLocked)
            {
                float bt = brake * specs.BrakeTorqueNm * 4, ebt = eBrake * specs.EBrakeTorqueNm * 10;
                ApplyBrakeTorque(angularVelocity > 0 ? -bt : bt);
                ApplyBrakeTorque(angularVelocity > 0 ? -ebt : ebt);
                angularAcceleration = TotalTorque / WheelMomentOfInertia;
                angularVelocity += angularAcceleration * dt;
            }
            else
            {
                angularAcceleration = 0f;
                angularVelocity = 0f;
            }

            CheckSign();
            lateralForce = longitudinalForce = 0;
        }

        static float Limit(float value, float limit)
        {
            if (value * limit <= 0)
                return value;
            return value > 0 ? Mathf.Min(value, limit) : Mathf.Max(value, limit);
        }

        public float UpdateLoaded(float latVel, float fwdVel, float bodySpeed, float tireLoad, float dt)
        {
            float brakeSpec = specs.BrakeTorqueNm * BrakingTorque;
            float eBrakeSpec = specs.EBrakeTorqueNm * EBrakingTorque;
            float dynamicGrip = specs.Axle(specs.DynamicGrip, axleIndex);
            float staticGrip = specs.Axle(specs.StaticGrip, axleIndex);
            if (load <= 0f && !brakeLocked)
                angularVelocity = fwdVel / radius;
            float forwardAcceleration = (fwdVel - roadSpeed) / dt;
            roadSpeed = fwdVel;
            load = Mathf.Max(tireLoad, 0f);
            lateralSpeed = latVel;
            float brakeTorque = brake * brakeSpec;
            float eBrakeTorque = eBrake * eBrakeSpec;
            if (Mathf.Abs(fwdVel) < 1f)
            {
                brakeTorque = -brake * tireLoad * fwdVel / radius;
                eBrakeTorque = -eBrake * tireLoad * fwdVel / radius;
                ApplyDriveTorque(-driveTorque * eBrake);
                ApplyBrakeTorque(brakeTorque);
                ApplyBrakeTorque(eBrakeTorque);
            }
            else
            {
                ApplyBrakeTorque(angularVelocity > 0f ? -brakeTorque : brakeTorque);
                ApplyBrakeTorque(angularVelocity > 0f ? -eBrakeTorque : eBrakeTorque);
            }

            slipAngle = Mathf.Atan2(latVel, Mathf.Abs(fwdVel)) / (Mathf.PI * 2f);
            float slipSpeed = angularVelocity * radius - fwdVel;
            slip = slipSpeed;
            float dynamicFriction = 1f;
            float groundFriction = 0f;
            float skidSpeed = Mathf.Sqrt(slipSpeed * slipSpeed + latVel * latVel);
            float pilotFactor = PilotFactor(bodySpeed);
            if (skidSpeed > 1e-6f && (latVel != 0f || fwdVel != 0f))
            {
                dynamicFriction = dynamicGrip * tractionBoost * pilotFactor;
                groundFriction = load * dynamicFriction / skidSpeed;
                float slipGroundFriction = load * dynamicFriction / Mathf.Sqrt(fwdVel * fwdVel + latVel * latVel);
                CheckForBrakeLock(Mathf.Abs(fwdVel) * slipGroundFriction);
            }

            if (traction < 1f || brakeLocked)
            {
                longitudinalForce = groundFriction * slipSpeed;
                lateralForce = -groundFriction * latVel;
                if (bodySpeed < .44704f && dynamicFriction > .1f)
                {
                    lateralForce /= dynamicFriction;
                    longitudinalForce /= dynamicFriction;
                }

                longitudinalForce = Limit(longitudinalForce, TotalTorque / radius);
            }
            else
            {
                brakeLocked = false;
                longitudinalForce = TotalTorque / radius;
                lateralForce = ComputeLateralForce(load, Mathf.Abs(slipAngle));
                if (latVel > 0f)
                    lateralForce = -lateralForce;
            }

            lateralForce *= lateralBoost;
            if (traction >= 1f && !brakeLocked)
                longitudinalForce += (angularAcceleration * radius - forwardAcceleration) * WheelMomentOfInertia / radius;
            bool useEllipse = TotalTorque * fwdVel > 0f && !brakeLocked;
            if (useEllipse)
                longitudinalForce *= 1.5f;
            lateralForce *= tractionCircle.x;
            longitudinalForce *= tractionCircle.y;
            float forceLength = Mathf.Sqrt(lateralForce * lateralForce + longitudinalForce * longitudinalForce);
            float maximumForce = load * staticGrip * tractionBoost * driftFriction * pilotFactor;
            traction = 1f;
            float toleratedSlip = maxSlip;
            if (forceLength > maximumForce && forceLength > .001f)
            {
                float ratio = maximumForce / forceLength;
                traction = ratio;
                lateralForce *= ratio;
                longitudinalForce *= ratio;
                toleratedSlip *= ratio * ratio;
            }
            else if (useEllipse)
                longitudinalForce /= 1.5f;
            if (Mathf.Abs(slipSpeed) > toleratedSlip)
                traction *= toleratedSlip / Mathf.Abs(slipSpeed);
            lateralForce *= surfaceLateralGrip;
            longitudinalForce *= surfaceDriveGrip;
            if (fwdVel > 1f)
                longitudinalForce -= Mathf.Sin(slipAngle * 2f * Mathf.PI) * lateralForce * dragReduction / specs.Axle(specs.GripScale, axleIndex);
            else
                lateralForce *= Mathf.Min(Mathf.Abs(latVel), 1f);
            if (brakeLocked)
                angularAcceleration = 0f;
            else
            {
                if (traction < 1f)
                {
                    float torque = (TotalTorque - longitudinalForce * radius + lastTorque) * .5f;
                    lastTorque = torque;
                    float rollingResistance = RollingFriction * surfaceRollingResistance;
                    angularAcceleration = torque / WheelMomentOfInertia - angularVelocity * rollingResistance / WheelMomentOfInertia - traction * slip / (radius * dt);
                }

                angularAcceleration = Mathf.Lerp(angularAcceleration, forwardAcceleration / radius, traction);
            }

            angularVelocity += angularAcceleration * dt;
            CheckSign();
            return lateralForce;
        }
    }
}
