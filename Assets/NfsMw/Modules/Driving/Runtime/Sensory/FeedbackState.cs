using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Flags] public enum FeedbackCapabilities { Motion = 1, Wheels = 2, Engine = 4, Nitrous = 8, Boost = 16, Clutch = 32, Damage = 64, Water = 128 }
    [Flags] public enum FeedbackEdges { None = 0, Startup = 1, Upshift = 2, Downshift = 4, NitroOn = 8, NitroOff = 16, Limiter = 32, Overrun = 64 }
    public enum SensorySurface { AsphaltDry, AsphaltWet, Concrete, Gravel, Dirt, Grass, Metal, Glass, Water, Wood, Plastic, Air, Unknown }
    public enum SensoryCategory { Player, OtherVehicle, Tires, Impacts, Environment, Radio, Sirens, Music, UI }

    [Serializable]
    public struct WheelFeedback
    {
        public bool Grounded, Driven, Front;
        public float AngularSpeed, LinearSpeed, RoadSpeed, LongitudinalSlip, LateralSlip, Load, Compression;
        public Vector3 Point, Normal;
        public SensorySurface Surface;
        public float Slip, Spin, Lock;
    }

    /// <summary>Value snapshot. Consumers receive copies, never a mutable wheel array. SI units except RPM.</summary>
    [Serializable]
    public struct VehicleFeedbackFrame
    {
        public int Sequence, Epoch, Gear, WheelCount;
        public double Time;
        public FeedbackCapabilities Capabilities;
        public FeedbackEdges Edges;
        public bool EngineRunning, Shifting, Airborne;
        public float EngineRpm, NormalizedRpm, EngineLoad, EngineTorque, DrivetrainTorque, Throttle, Brake;
        public float Clutch, Boost, BodyDamage, WaterDepth, NitrousFlow, NitrousRemaining;
        public float Speed, SlipAngle, YawRate;
        public Vector3 Position, Velocity, LocalVelocity, AccelerationG;
        public Quaternion Rotation;
        public float FrontSlip, RearSlip, Wheelspin, BrakeLock, Drift, SpeedIntensity, NitroIntensity, EngineStress, Landing, Scrape;
        public WheelFeedback Wheel0, Wheel1, Wheel2, Wheel3, Wheel4, Wheel5, Wheel6, Wheel7;
        public FeedbackImpact LastImpact;
        public SensorySurface ScrapeMaterial, BodyMaterial;
        public WheelFeedback GetWheel(int index)
        {
            switch (index)
            {
                case 0: return Wheel0; case 1: return Wheel1; case 2: return Wheel2; case 3: return Wheel3;
                case 4: return Wheel4; case 5: return Wheel5; case 6: return Wheel6; case 7: return Wheel7;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
        public void SetWheel(int index, WheelFeedback value)
        {
            switch (index)
            {
                case 0: Wheel0 = value; break; case 1: Wheel1 = value; break; case 2: Wheel2 = value; break; case 3: Wheel3 = value; break;
                case 4: Wheel4 = value; break; case 5: Wheel5 = value; break; case 6: Wheel6 = value; break; case 7: Wheel7 = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    [Serializable]
    public struct FeedbackImpact
    {
        public int Sequence;
        public double Time;
        public Vector3 Point, Normal, LocalDirection;
        public float Severity, NormalSpeed, TangentSpeed, Impulse;
        public SensorySurface Material;
        public SensorySurface BodyMaterial;
    }

    /// <summary>Main-thread interface. Disabled sources expose a neutral frame. Replay never emits gameplay facts.</summary>
    public interface IVehicleFeedbackSource
    {
        VehicleFeedbackFrame Frame { get; }
        event Action<VehicleFeedbackFrame> Sampled;
        event Action<FeedbackImpact> Impact;
    }

    public static class SensoryMath
    {
        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        public static float Unit(float value) => Mathf.Clamp01(Finite(value));
        public static float Envelope(float current, float target, float dt, float attack, float release)
        {
            current = Finite(current);
            if (Finite(dt) <= 0) return current;
            target = Finite(target);
            float tau = Mathf.Max(0.001f, Finite(target > current ? attack : release));
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-Mathf.Min(dt, 0.25f) / tau));
        }
    }

    [Serializable]
    public sealed class FeedbackResponse
    {
        [Min(0.001f)] public float attack = 0.06f, release = 0.24f;
        [Range(0, 1)] public float slipEnter = 0.12f, slipExit = 0.06f;
        public AnimationCurve speed = AnimationCurve.EaseInOut(0, 0, 90, 1);
        public AnimationCurve slip = AnimationCurve.EaseInOut(0, 0, 0.45f, 1);
        public AnimationCurve lateralSlip = AnimationCurve.EaseInOut(0, 0, 0.3f, 1);
    }

    /// <summary>Fixed-cadence interpretation, shared by every presentation output.</summary>
    public sealed class FeedbackNormalizer
    {
        private readonly FeedbackResponse response;
        private VehicleFeedbackFrame previous;
        private bool sampled;
        private readonly bool[] slipping = new bool[8];
        public FeedbackNormalizer(FeedbackResponse response = null) { this.response = response ?? new FeedbackResponse(); }
        public void Reset() { previous = default; sampled = false; Array.Clear(slipping, 0, slipping.Length); }
        public VehicleFeedbackFrame Step(VehicleFeedbackFrame input, float dt)
        {
            if (SensoryMath.Finite(dt) <= 0) return previous;
            input.WheelCount = Mathf.Clamp(input.WheelCount, 0, 8);
            float front = 0, rear = 0, spin = 0, locked = 0;
            bool grounded = false;
            for (int i = 0; i < input.WheelCount; i++)
            {
                var w = input.GetWheel(i); var old = previous.GetWheel(i);
                float contact = w.Grounded ? SensoryMath.Unit(w.Load / 800f) : 0;
                float raw = contact * Mathf.Max(
                    response.slip.Evaluate(Mathf.Abs(SensoryMath.Finite(w.LongitudinalSlip))),
                    response.lateralSlip.Evaluate(Mathf.Abs(SensoryMath.Finite(w.LateralSlip))) * SensoryMath.Unit(input.Speed / 3f));
                slipping[i] = raw >= (slipping[i] ? response.slipExit : response.slipEnter);
                w.Slip = SensoryMath.Envelope(old.Slip, slipping[i] ? SensoryMath.Unit(raw) : 0, dt, response.attack, response.release);
                float roadSpeed = Mathf.Abs(w.RoadSpeed);
                // Compare magnitudes, not the sign of slip: reverse acceleration is still wheelspin.
                float rawSpin = w.Driven && Mathf.Abs(w.LinearSpeed) > roadSpeed + 0.5f
                    ? contact * SensoryMath.Unit(response.slip.Evaluate(Mathf.Abs(w.LongitudinalSlip))) : 0;
                float rawLock = roadSpeed > 2 && Mathf.Abs(w.LinearSpeed) + 0.5f < roadSpeed
                    ? contact * SensoryMath.Unit(response.slip.Evaluate(Mathf.Abs(w.LongitudinalSlip))) : 0;
                w.Spin = SensoryMath.Envelope(old.Spin, rawSpin, dt, response.attack, response.release);
                w.Lock = SensoryMath.Envelope(old.Lock, rawLock, dt, response.attack, response.release);
                input.SetWheel(i, w); grounded |= w.Grounded;
                if (w.Front) front = Mathf.Max(front, w.Slip); else rear = Mathf.Max(rear, w.Slip);
                spin = Mathf.Max(spin, w.Spin); locked = Mathf.Max(locked, w.Lock);
            }
            input.FrontSlip = front; input.RearSlip = rear; input.Wheelspin = spin; input.BrakeLock = locked;
            input.Airborne = input.WheelCount > 0 && !grounded;
            float landing = sampled && previous.Airborne && grounded ? SensoryMath.Unit(-previous.LocalVelocity.y / 12f) : 0;
            input.Landing = Mathf.Max(landing, SensoryMath.Envelope(previous.Landing, 0, dt, 0.01f, 0.25f));
            input.Drift = SensoryMath.Envelope(previous.Drift,
                grounded ? SensoryMath.Unit(Mathf.Abs(input.SlipAngle) / 0.45f) * rear * SensoryMath.Unit(input.Speed / 10f) : 0,
                dt, response.attack, response.release);
            input.SpeedIntensity = SensoryMath.Unit(response.speed.Evaluate(SensoryMath.Finite(input.Speed)));
            input.NitroIntensity = SensoryMath.Envelope(previous.NitroIntensity, SensoryMath.Unit(input.NitrousFlow), dt, 0.04f, 0.15f);
            input.EngineStress = SensoryMath.Unit((input.NormalizedRpm - 0.75f) * 4f);
            previous = input; sampled = true; return input;
        }
    }

    public static class ImpactClassifier
    {
        // Delta-v from total pair impulse, not replicated as energy per contact.
        public static float Severity(float normalSpeed, float impulse, float mass)
            => SensoryMath.Unit(Mathf.Max(SensoryMath.Finite(normalSpeed), SensoryMath.Finite(impulse) / Mathf.Max(1, SensoryMath.Finite(mass))) / 20f);
    }
}
