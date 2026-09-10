using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Recovered gamepad/keyboard tables, input history, throttle/brake range adjustment and
    /// steering-angle rate limit. Histories are fixed-capacity, time-expiring sample means.
    /// Wheel-device paths, speedbreaker and wall-contact steering are not synthesized.
    /// </summary>
    public sealed class MostWantedSteeringModel
    {
        public const float AbsoluteMaximumDegrees = 45f;
        private static readonly float[] Range = { 40f, 20f, 10f, 5.5f, 4.5f, 3.25f, 2.9f, 2.9f, 2.9f, 2.9f };
        private static readonly float[] Speed = { 1f, 1f, 1f, .56f, .5f, .35f, .3f, .3f, .3f, .3f };
        private static readonly float[] InputSpeed = { 1f, 1.05f, 1.1f, 1.5f, 2.2f, 3.1f };
        private static readonly float[] InputRange = { 1f, 1.05f, 1.1f, 1.2f, 1.3f, 1.4f };
        // Selected remap affects history/steering speed, not the already-computed target angle.
        private static readonly float[] HistoryRemap = { -1f, -.736f, -.542f, -.4f, -.292f, -.214f, -.16f, -.123f, -.078f, -.036f, 0f,
            .036f, .078f, .123f, .16f, .214f, .292f, .4f, .542f, .736f, 1f };
        private readonly SampleWindow input = new SampleWindow(33, .55);
        private readonly SampleWindow inputSpeed = new SampleWindow(9, .15);
        private double time;
        private float previousRawInput, previousAngle, previousMaximum = AbsoluteMaximumDegrees;
        public float AngleDegrees => previousAngle;
        public float MaximumDegrees { get; private set; }

        public static float BaseRangeDegrees(float forwardMetersPerSecond)
            => MostWantedVehicleMath.SampleUniform(Range, forwardMetersPerSecond, 0f, 160f);

        public float Step(MostWantedDrivingSettings settings, VehicleInputState controls, float forwardSpeed,
            float rearSlipDegrees, float deltaTime)
        {
            if (settings == null || !float.IsFinite(deltaTime) || deltaTime <= 0f || !float.IsFinite(forwardSpeed) || !float.IsFinite(rearSlipDegrees))
            { Reset(); return 0f; }
            controls = controls.Clamped();
            time += deltaTime;
            float speedCoefficient = MostWantedVehicleMath.SampleUniform(Speed, forwardSpeed, 0f, 160f);
            float historyMagnitude = Mathf.Abs(input.Mean);
            float inputCoefficient = MostWantedVehicleMath.SampleUniform(InputRange, historyMagnitude, 0f, 1f);
            float coastBrake = 1f - (controls.Throttle + 1f - (controls.Brake + (controls.Handbrake ? 1f : 0f)) * .5f) * .5f;
            float maximum = BaseRangeDegrees(forwardSpeed) * (1f + 1.45f * coastBrake * speedCoefficient)
                * inputCoefficient * (1f + .2f * settings.steeringTuning);
            if (controls.Steering * rearSlipDegrees > 0f)
                maximum = Mathf.Max(maximum, Mathf.Abs(rearSlipDegrees));
            else if (historyMagnitude >= .5f)
                maximum = Mathf.Max(maximum, previousMaximum); // recovered HardTurnTightenSpeed is zero
            MaximumDegrees = previousMaximum = Mathf.Clamp(maximum, 0f, AbsoluteMaximumDegrees);
            float target = Mathf.Clamp(controls.Steering * MaximumDegrees * settings.steeringCoefficient,
                -AbsoluteMaximumDegrees, AbsoluteMaximumDegrees);
            // Playability adaptation: the public reconstruction compares the current raw
            // control with the previously remapped history sample. With the still-unverified
            // active PC remap that makes a held partial analogue steer look like a fresh,
            // high-speed movement every tick in this Unity input path. Keep the recovered
            // remap for the history/range logic, but derive slew speed in one coherent raw
            // input domain so a steady stick actually settles.
            float inputRate = Mathf.Abs((controls.Steering - previousRawInput) / deltaTime);
            inputSpeed.Add(MostWantedVehicleMath.SampleUniform(InputSpeed, inputRate, 0f, 10f), time);
            float rate = 180f * speedCoefficient * inputSpeed.Mean * inputCoefficient * settings.steeringCoefficient;
            previousAngle = Mathf.MoveTowards(previousAngle, target, Mathf.Max(0f, rate) * deltaTime);
            previousRawInput = controls.Steering;
            input.Add(MostWantedVehicleMath.SampleUniform(HistoryRemap, controls.Steering, -1f, 1f), time);
            return previousAngle;
        }

        public void Reset()
        {
            input.Reset(); inputSpeed.Reset(); time = 0;
            previousRawInput = previousAngle = 0f;
            previousMaximum = MaximumDegrees = AbsoluteMaximumDegrees;
        }

        private sealed class SampleWindow
        {
            private readonly float[] values;
            private readonly double[] timestamps;
            private readonly double duration;
            private int first, count;
            private double sum;
            public float Mean => count == 0 ? 0f : (float)(sum / count);
            public SampleWindow(int capacity, double seconds) { values = new float[capacity]; timestamps = new double[capacity]; duration = seconds; }
            public void Add(float value, double now)
            {
                while (count > 0 && now - timestamps[first] > duration) RemoveFirst();
                if (count == values.Length) RemoveFirst();
                int index = (first + count) % values.Length;
                values[index] = value; timestamps[index] = now; count++; sum += value;
            }
            private void RemoveFirst() { sum -= values[first]; first = (first + 1) % values.Length; count--; }
            public void Reset() { Array.Clear(values, 0, values.Length); Array.Clear(timestamps, 0, timestamps.Length); first = count = 0; sum = 0; }
        }
    }
}
