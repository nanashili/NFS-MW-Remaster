using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum VehiclePhysicsLabExperimentKind
    {
        StandingLaunch,
        RollingAcceleration,
        CoastDown,
        Braking,
        RepeatedBraking,
        Gearshift,
        Nitrous,
        StepSteer,
        SineSteer,
        Slalom,
        Skidpad,
        LaneChange,
        CombinedBrakingTurn,
        BrakeToDrift,
        SustainedDrift,
        DriftTransition,
        SurfaceSweep,
        RampLanding,
        Contact,
        Custom
    }

    public enum VehiclePhysicsLabTrackKind
    {
        FlatStraight,
        ConstantRadius,
        Slalom,
        LaneChange,
        Ramp,
        SurfaceSweep,
        ContactArena
    }

    public enum VehiclePhysicsLabInputMode
    {
        Generated,
        Recorded,
        Live
    }

    public enum VehiclePhysicsLabSamplePhase
    {
        AfterManualVehicleStep,
        AfterPhysicsSceneSimulation
    }

    public enum VehiclePhysicsLabResultStatus
    {
        Pending,
        Running,
        Passed,
        Failed,
        Cancelled,
        Invalid,
        Incomplete
    }

    public enum VehiclePhysicsLabTuningParameter
    {
        Mass,
        MaxSpeedKph,
        EngineTorque,
        EngineInertia,
        FinalDrive,
        ShiftDuration,
        NitrousTorque,
        NitrousFuelSeconds,
        LongitudinalGrip,
        LateralGrip,
        PeakSlip,
        RollingResistance,
        ServiceBrakeTorque,
        FrontBrakeBias,
        SuspensionSpringRate,
        SuspensionDamperRate,
        SuspensionTravel,
        DragCoefficient,
        DownforceCoefficient,
        MaxSteerAngle,
        SteeringResponse,
        ThrottleResponse,
        BrakeResponse,
        HighSpeedSteerScale,
        DriftBias,
        DriftYawGain,
        DriftMaximumYawTorque,
        StabilityStrength,
        WheelRadius,
        WheelLateralOffset,
        SuspensionRestLength
    }

    [Serializable]
    public struct VehiclePhysicsLabControlKey
    {
        [Min(0f)] public float time;
        public VehicleInputState input;
    }

    [Serializable]
    public sealed class VehiclePhysicsLabInputSchedule
    {
        public VehiclePhysicsLabInputMode mode = VehiclePhysicsLabInputMode.Generated;
        [Min(0f)] public float stepTime = 1f;
        [Range(-1f, 1f)] public float steering;
        [Range(0f, 1f)] public float throttle = 1f;
        [Range(0f, 1f)] public float brake;
        public bool handbrake;
        public bool nitrous;
        [Min(0.01f)] public float sineFrequencyHz = 0.5f;
        [Range(0f, 1f)] public float sineAmplitude = 0.6f;
        [Min(0f)] public float transitionSeconds = 0.6f;
        public VehiclePhysicsLabControlKey[] keys = Array.Empty<VehiclePhysicsLabControlKey>();
    }

    [Serializable]
    public sealed class VehiclePhysicsLabEvaluationSettings
    {
        public bool requireTargetSpeed;
        [Min(0f)] public float targetSpeedKph = 100f;
        [Min(0f)] public float targetStopSpeedKph = 1f;
        [Min(0f)] public float targetToleranceKph = 1f;
        [Min(0f)] public float maximumSlipAngleDegrees = 80f;
        [Range(0f, 1f)] public float minimumGroundedFraction = 0.5f;
        [Min(0f)] public float maximumAirborneSeconds = 2f;
        [Min(0f)] public float maximumRolloverDegrees = 80f;
        [Min(0f)] public float maximumLateralErrorMetres = 12f;
    }

    [Serializable]
    public sealed class VehiclePhysicsLabCaptureSettings
    {
        [Min(0.005f)] public float sampleIntervalSeconds = 0.02f;
        [Min(32)] public int maximumSamples = 12000;
        [Min(1)] public int maximumCollisionEvents = 256;
        public bool captureWheelChannels = true;
        public bool captureRawInput = true;
        public bool captureFilteredInput = true;
        public bool capturePresentationChannels;
    }

    [Serializable]
    public sealed class VehiclePhysicsLabSafetySettings
    {
        [Min(1f)] public float maximumSeconds = 60f;
        [Min(1f)] public float fixtureHalfExtent = 500f;
        [Min(0.01f)] public float maximumLinearSpeedMps = 180f;
        [Min(0.01f)] public float maximumAngularSpeedRadPerSec = 80f;
        public bool stopOnFirstInvalidSample = true;
    }

    [Serializable]
    public sealed class VehiclePhysicsLabReferenceEvidence
    {
        [Tooltip("Project-relative or external source path. The lab stores provenance; it does not copy or decode the source.")]
        public string sourcePath;
        public string provenance;
        public string vehicleSetup;
        [Min(0f)] public float frameRate;
        [Min(0f)] public float timingUncertaintySeconds;
        public bool inputTraceAvailable;
        [TextArea(2, 6)] public string annotations;
    }

    [Serializable]
    public struct VehiclePhysicsLabTuningOverride
    {
        public bool enabled;
        public VehiclePhysicsLabTuningParameter parameter;
        public float value;
    }



    [Serializable]
    public struct VehiclePhysicsLabTrackSample
    {
        public float distance;
        public Vector3 position;
        public Vector3 forward;
        public Vector3 left;
        public Vector3 up;
        public float width;
    }





    [CreateAssetMenu(menuName = "NFS MW Remaster/Driving/Vehicle Physics Lab Suite", fileName = "PhysicsLabSuite")]
    public sealed class VehiclePhysicsLabSuite : ScriptableObject
    {
        public const int CurrentSchema = 1;

        [HideInInspector] public int schema = CurrentSchema;
        [HideInInspector] public string id = Guid.NewGuid().ToString("N");
        public string displayName = "Vehicle handling regression suite";
        public VehiclePhysicsLabDefinition[] experiments = Array.Empty<VehiclePhysicsLabDefinition>();
        public int repetitions = 1;
        public bool stopOnFailure = true;

        public bool IsValid(out string failure)
        {
            failure = string.Empty;
            if (schema != CurrentSchema || !Guid.TryParseExact(id, "N", out _))
            {
                failure = "PHYSICS_LAB_SUITE_SCHEMA: migrate the suite asset before use.";
                return false;
            }

            if (experiments == null || experiments.Length == 0 || experiments.Length > 256 || repetitions < 1 || repetitions > 128)
            {
                failure = "PHYSICS_LAB_SUITE_VALUES: provide 1-256 experiments and 1-128 repetitions.";
                return false;
            }

            var ids = new HashSet<string>();
            for (int i = 0; i < experiments.Length; i++)
            {
                VehiclePhysicsLabDefinition experiment = experiments[i];
                if (experiment == null || !ids.Add(experiment.id) || !experiment.IsValid(out failure))
                {
                    if (string.IsNullOrEmpty(failure)) failure = "PHYSICS_LAB_SUITE_DUPLICATE: experiment references must be non-null and unique.";
                    return false;
                }
            }

            return true;
        }
    }

    [Serializable]
    public sealed class VehiclePhysicsLabSweepAxis
    {
        public bool enabled = true;
        public VehiclePhysicsLabTuningParameter parameter = VehiclePhysicsLabTuningParameter.Mass;
        public float minimum = 1200f;
        public float maximum = 1800f;
        [Min(2)] public int steps = 3;

        public bool IsValid(out string failure)
        {
            failure = string.Empty;
            if (!enabled) return true;
            if (!VehiclePhysicsLabTuningOverrides.TryGetRange(parameter, out float legalMinimum, out float legalMaximum))
            {
                failure = "PHYSICS_LAB_SWEEP_PARAMETER: " + parameter + " is not an approved tunable parameter.";
                return false;
            }
            if (!Finite(minimum) || !Finite(maximum) || minimum > maximum || minimum < legalMinimum || maximum > legalMaximum || steps < 2 || steps > 32)
            {
                failure = "PHYSICS_LAB_SWEEP_AXIS: " + parameter + " must stay within [" + legalMinimum.ToString("R") + ", " + legalMaximum.ToString("R") + "] and use 2-32 steps.";
                return false;
            }
            return true;
        }

        public float ValueAt(int index)
        {
            if (steps <= 1) return minimum;
            return Mathf.Lerp(minimum, maximum, Mathf.Clamp01(index / (steps - 1f)));
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }



    [Serializable]
    public struct VehiclePhysicsLabWheelSample
    {
        public int wheelIndex;
        public bool grounded;
        public bool front;
        public bool driven;
        public bool handbrake;
        public float longitudinalSlip;
        public float lateralSlipRadians;
        public float normalLoad;
        public float suspensionCompression;
        public float contactForwardSpeedMps;
        public float longitudinalForce;
        public float lateralForce;
        public Vector3 contactPoint;
        public Vector3 contactNormal;
        public string surfaceName;
        public float surfaceGrip;
    }

    [Serializable]
    public struct VehiclePhysicsLabSample
    {
        public int tick;
        public float time;
        public float measurementTime;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 angularVelocity;
        public Vector3 acceleration;
        public float speedKph;
        public float forwardSpeedKph;
        public float lateralSpeedMps;
        public float yawRateRadPerSec;
        public float lateralAccelerationMps2;
        public float slipAngleDegrees;
        public bool slipAngleValid;
        public bool airborne;
        public int groundedWheels;
        public float engineRpm;
        public float engineTorque;
        public float drivetrainTorque;
        public int gear;
        public bool shifting;
        public bool nitrousActive;
        public float nitrousSeconds;
        public VehicleInputState rawInput;
        public VehicleInputState finalInput;
        public VehicleHandlingMode handling;
        public float assistYawTorque;
        public float tractionControlReduction, absReduction, countersteeringContribution;
        public float averageLongitudinalSlip;
        public VehiclePhysicsLabWheelSample[] wheels;
    }

    [Serializable]
    public struct VehiclePhysicsLabCollisionEvent
    {
        public float time;
        public string otherName;
        public Vector3 point;
        public Vector3 normal;
        public float impulse;
        public int contactCount;
    }

    [Serializable]
    public struct VehiclePhysicsLabMetric
    {
        public string key;
        public string unit;
        public float value;
        public int sampleCount;
        public bool available;
        public string note;
    }

    [Serializable]
    public sealed class VehiclePhysicsLabRunReport
    {
        public const int CurrentSchema = 1;

        public int schema = CurrentSchema;
        public string runId = Guid.NewGuid().ToString("N");
        public string definitionId;
        public string definitionFingerprint;
        public string vehicleFingerprint;
        public string engineVersion;
        public string platform;
        public string experiment;
        public string inputMode;
        public string samplePhase;
        public string configurationEvidence;
        public string sweepId;
        public int sweepTrial = -1;
        public string sweepLabel;
        public VehiclePhysicsLabTuningOverride[] appliedOverrides = Array.Empty<VehiclePhysicsLabTuningOverride>();
        public float fixedStep;
        public float warmupSeconds;
        public int seed;
        public VehiclePhysicsLabResultStatus status = VehiclePhysicsLabResultStatus.Pending;
        public bool complete;
        public bool targetReached;
        public float elapsedSeconds;
        public float measuredSeconds;
        public int totalTicks;
        public int droppedSamples;
        public string failureCode;
        public string failureMessage;
        public string[] diagnostics = Array.Empty<string>();
        public VehiclePhysicsLabMetric[] metrics = Array.Empty<VehiclePhysicsLabMetric>();
        public VehiclePhysicsLabSample[] samples = Array.Empty<VehiclePhysicsLabSample>();
        public VehiclePhysicsLabCollisionEvent[] collisions = Array.Empty<VehiclePhysicsLabCollisionEvent>();

        public bool Passed => status == VehiclePhysicsLabResultStatus.Passed && complete;
    }

    public static class VehiclePhysicsLabAnalysis
    {
        public static VehiclePhysicsLabMetric[] Analyze(
            VehiclePhysicsLabRunReport report,
            VehiclePhysicsLabDefinition definition)
        {
            if (report == null || report.samples == null || report.samples.Length == 0)
            {
                return new[] { Metric("sample_count", "samples", 0f, false, 0, "No samples were captured.") };
            }

            VehiclePhysicsLabSample[] samples = report.samples;
            var metrics = new List<VehiclePhysicsLabMetric>
            {
                Metric("sample_count", "samples", samples.Length, true, samples.Length, string.Empty),
                Metric("duration", "s", Mathf.Max(0f, samples[samples.Length - 1].measurementTime - samples[0].measurementTime), true, samples.Length, string.Empty),
                Metric("peak_speed", "km/h", Max(samples, s => s.speedKph), true, samples.Length, string.Empty),
                Metric("peak_acceleration", "m/s^2", Max(samples, s => s.acceleration.magnitude), true, samples.Length, string.Empty),
                Metric("peak_braking", "m/s^2", Max(samples, s => Mathf.Max(0f, -Vector3.Dot(s.acceleration, s.rotation * Vector3.forward))), true, samples.Length, string.Empty),
                Metric("peak_lateral_acceleration", "m/s^2", Max(samples, s => Mathf.Abs(s.lateralAccelerationMps2)), true, samples.Length, string.Empty),
                Metric("peak_slip_angle", "deg", Max(samples, s => s.slipAngleValid ? Mathf.Abs(s.slipAngleDegrees) : 0f), true, samples.Length, string.Empty),
                Metric("average_longitudinal_slip", "ratio", Mean(samples, s => Mathf.Abs(s.averageLongitudinalSlip)), true, samples.Length, string.Empty),
                Metric("grounded_fraction", "fraction", Mean(samples, s => s.groundedWheels > 0 ? 1f : 0f), true, samples.Length, string.Empty),
                Metric("airborne_seconds", "s", AccumulateWhere(samples, s => s.airborne), true, samples.Length, "Estimated from sample cadence."),
                Metric("drift_seconds", "s", AccumulateWhere(samples, s => s.handling == VehicleHandlingMode.Drifting), true, samples.Length, "Shared handling model state."),
                Metric("maximum_assist_yaw_torque", "N m", Max(samples, s => Mathf.Abs(s.assistYawTorque)), true, samples.Length, string.Empty),
                Metric("collision_count", "events", report.collisions == null ? 0f : report.collisions.Length, true, samples.Length, string.Empty)
            };

            float target = definition == null ? 0f : Mathf.Max(0f, definition.evaluation.targetSpeedKph);
            bool targetRequired = definition != null && definition.evaluation.requireTargetSpeed;
            float timeToTarget;
            if (target > 0f && TryTimeAtOrAbove(samples, target, out timeToTarget))
            {
                metrics.Add(Metric("time_to_target", "s", timeToTarget, true, samples.Length, string.Empty));
            }
            else
            {
                metrics.Add(Metric("time_to_target", "s", 0f, false, samples.Length,
                    targetRequired ? "Target was not reached; result is censored, not extrapolated." : "Target was not reached."));
            }

            float stopDistance;
            float stopSpeedKph = definition != null && definition.experiment == VehiclePhysicsLabExperimentKind.Braking
                ? Mathf.Max(0f, definition.evaluation.targetStopSpeedKph)
                : 1f;
            if (TryStoppingDistance(samples, stopSpeedKph, out stopDistance))
            {
                metrics.Add(Metric("stopping_distance", "m", stopDistance, true, samples.Length, "Distance from first moving brake sample to the configured stop threshold."));
            }
            else
            {
                metrics.Add(Metric("stopping_distance", "m", 0f, false, samples.Length, "No complete braking interval was captured."));
            }

            return metrics.ToArray();
        }

        public static bool TryTimeAtOrAbove(VehiclePhysicsLabSample[] samples, float targetKph, out float time)
        {
            time = 0f;
            if (samples == null || samples.Length == 0 || !Finite(targetKph) || targetKph < 0f)
            {
                return false;
            }

            float previousTime = float.NegativeInfinity;
            for (int i = 0; i < samples.Length; i++)
            {
                if (!Finite(samples[i].measurementTime) || samples[i].measurementTime < previousTime || !Finite(samples[i].speedKph)) return false;
                previousTime = samples[i].measurementTime;
                if (samples[i].speedKph >= targetKph)
                {
                    if (i == 0)
                    {
                        time = samples[i].measurementTime;
                        return true;
                    }

                    float previous = samples[i - 1].speedKph;
                    float denominator = samples[i].speedKph - previous;
                    float blend = Mathf.Abs(denominator) < 0.0001f ? 1f : Mathf.Clamp01((targetKph - previous) / denominator);
                    time = Mathf.Lerp(samples[i - 1].measurementTime, samples[i].measurementTime, blend);
                    return true;
                }
            }

            return false;
        }

        public static bool TryStoppingDistance(VehiclePhysicsLabSample[] samples, out float distance)
        {
            return TryStoppingDistance(samples, 1f, out distance);
        }

        public static bool TryStoppingDistance(VehiclePhysicsLabSample[] samples, float stopSpeedKph, out float distance)
        {
            distance = 0f;
            if (samples == null || samples.Length < 2 || !Finite(stopSpeedKph) || stopSpeedKph < 0f)
            {
                return false;
            }

            int start = -1;
            for (int i = 0; i < samples.Length; i++)
            {
                if ((samples[i].finalInput.Brake > 0.5f || samples[i].finalInput.Handbrake)
                    && Finite(samples[i].speedKph) && samples[i].speedKph > stopSpeedKph
                    && Finite(samples[i].position))
                {
                    start = i;
                    break;
                }
            }

            if (start < 0)
            {
                return false;
            }

            for (int i = start; i < samples.Length; i++)
            {
                if (Finite(samples[i].speedKph) && Finite(samples[i].position) && samples[i].speedKph <= stopSpeedKph
                    && Finite(samples[start].position))
                {
                    distance = Vector3.Distance(samples[start].position, samples[i].position);
                    return true;
                }
            }

            return false;
        }

        public static VehiclePhysicsLabMetric FindMetric(VehiclePhysicsLabMetric[] metrics, string key)
        {
            if (metrics != null)
            {
                for (int i = 0; i < metrics.Length; i++)
                {
                    if (string.Equals(metrics[i].key, key, StringComparison.Ordinal))
                    {
                        return metrics[i];
                    }
                }
            }

            return Metric(key, string.Empty, 0f, false, 0, "Metric is unavailable.");
        }

        public static VehiclePhysicsLabComparisonSample[] CompareByTime(
            VehiclePhysicsLabSample[] baseline,
            VehiclePhysicsLabSample[] candidate)
        {
            if (baseline == null || candidate == null || baseline.Length == 0 || candidate.Length == 0
                || !ValidMeasurementClock(baseline) || !ValidMeasurementClock(candidate))
            {
                return Array.Empty<VehiclePhysicsLabComparisonSample>();
            }

            var result = new List<VehiclePhysicsLabComparisonSample>(Mathf.Min(baseline.Length, candidate.Length));
            for (int i = 0; i < candidate.Length; i++)
            {
                VehiclePhysicsLabSample sample = candidate[i];
                VehiclePhysicsLabSample reference;
                if (!TryInterpolateByTime(baseline, sample.measurementTime, out reference))
                {
                    continue;
                }

                result.Add(new VehiclePhysicsLabComparisonSample
                {
                    time = sample.measurementTime,
                    speedDeltaKph = sample.speedKph - reference.speedKph,
                    lateralAccelerationDelta = sample.lateralAccelerationMps2 - reference.lateralAccelerationMps2,
                    yawRateDelta = sample.yawRateRadPerSec - reference.yawRateRadPerSec,
                    slipAngleDeltaDegrees = sample.slipAngleDegrees - reference.slipAngleDegrees
                });
            }

            return result.ToArray();
        }

        private static bool TryInterpolateByTime(VehiclePhysicsLabSample[] samples, float time, out VehiclePhysicsLabSample result)
        {
            result = default;
            if (samples == null || samples.Length == 0 || !Finite(time) || !ValidMeasurementClock(samples)
                || time < samples[0].measurementTime || time > samples[samples.Length - 1].measurementTime)
            {
                return false;
            }

            int index = 0;
            while (index + 1 < samples.Length && samples[index + 1].measurementTime < time)
            {
                index++;
            }

            if (index + 1 >= samples.Length)
            {
                result = samples[index];
                return true;
            }

            VehiclePhysicsLabSample a = samples[index];
            VehiclePhysicsLabSample b = samples[index + 1];
            float blend = Mathf.InverseLerp(a.measurementTime, b.measurementTime, time);
            result = a;
            result.measurementTime = time;
            result.speedKph = Mathf.Lerp(a.speedKph, b.speedKph, blend);
            result.lateralAccelerationMps2 = Mathf.Lerp(a.lateralAccelerationMps2, b.lateralAccelerationMps2, blend);
            result.yawRateRadPerSec = Mathf.Lerp(a.yawRateRadPerSec, b.yawRateRadPerSec, blend);
            result.slipAngleDegrees = Mathf.Lerp(a.slipAngleDegrees, b.slipAngleDegrees, blend);
            return true;
        }

        private static float Max(VehiclePhysicsLabSample[] samples, Func<VehiclePhysicsLabSample, float> selector)
        {
            float value = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float candidate = selector(samples[i]);
                if (Finite(candidate))
                {
                    value = Mathf.Max(value, candidate);
                }
            }

            return value;
        }

        private static float Mean(VehiclePhysicsLabSample[] samples, Func<VehiclePhysicsLabSample, float> selector)
        {
            double total = 0d;
            int count = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float value = selector(samples[i]);
                if (Finite(value))
                {
                    total += value;
                    count++;
                }
            }

            return count == 0 ? 0f : (float)(total / count);
        }

        private static float AccumulateWhere(VehiclePhysicsLabSample[] samples, Func<VehiclePhysicsLabSample, bool> predicate)
        {
            if (samples.Length < 2)
            {
                return 0f;
            }

            float total = 0f;
            for (int i = 1; i < samples.Length; i++)
            {
                if (predicate(samples[i]))
                {
                    total += Mathf.Max(0f, samples[i].measurementTime - samples[i - 1].measurementTime);
                }
            }

            return total;
        }

        private static VehiclePhysicsLabMetric Metric(string key, string unit, float value, bool available, int sampleCount, string note)
        {
            return new VehiclePhysicsLabMetric { key = key, unit = unit, value = value, available = available, sampleCount = sampleCount, note = note };
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);

        private static bool ValidMeasurementClock(VehiclePhysicsLabSample[] samples)
        {
            float previous = float.NegativeInfinity;
            for (int i = 0; i < samples.Length; i++)
            {
                if (!Finite(samples[i].measurementTime) || samples[i].measurementTime < previous) return false;
                previous = samples[i].measurementTime;
            }
            return true;
        }
    }

    [Serializable]
    public struct VehiclePhysicsLabComparisonSample
    {
        public float time;
        public float speedDeltaKph;
        public float lateralAccelerationDelta;
        public float yawRateDelta;
        public float slipAngleDeltaDegrees;
    }

    public static class VehiclePhysicsLabTuningOverrides
    {
        public static bool Apply(
            VehicleTuning tuning,
            VehiclePhysicsLabTuningOverride[] overrides,
            out string[] diagnostics)
        {
            var messages = new List<string>();
            if (tuning == null)
            {
                diagnostics = new[] { "PHYSICS_LAB_OVERRIDE_TUNING: tuning is null." };
                return false;
            }

            if (overrides != null)
            {
                for (int i = 0; i < overrides.Length; i++)
                {
                    VehiclePhysicsLabTuningOverride item = overrides[i];
                    if (!item.enabled)
                    {
                        continue;
                    }

                    if (!Finite(item.value))
                    {
                        messages.Add("PHYSICS_LAB_OVERRIDE_VALUE: override " + i + " is not finite.");
                        continue;
                    }

                    float before = Read(tuning, item.parameter);
                    if (!Write(tuning, item.parameter, item.value))
                    {
                        messages.Add("PHYSICS_LAB_OVERRIDE_RANGE: " + item.parameter + " rejected value " + item.value.ToString("R") + ".");
                        continue;
                    }

                    messages.Add(item.parameter + " " + before.ToString("R") + " -> " + Read(tuning, item.parameter).ToString("R"));
                }
            }

            diagnostics = messages.ToArray();
            return messages.TrueForAll(value => !value.StartsWith("PHYSICS_LAB_", StringComparison.Ordinal));
        }

        public static float Read(VehicleTuning tuning, VehiclePhysicsLabTuningParameter parameter)
        {
            switch (parameter)
            {
                case VehiclePhysicsLabTuningParameter.Mass: return tuning.chassis.mass;
                case VehiclePhysicsLabTuningParameter.MaxSpeedKph: return tuning.chassis.maxSpeedKph;
                case VehiclePhysicsLabTuningParameter.EngineTorque: return tuning.engine.maxTorqueNewtonMeters;
                case VehiclePhysicsLabTuningParameter.EngineInertia: return tuning.engine.engineInertia;
                case VehiclePhysicsLabTuningParameter.FinalDrive: return tuning.engine.finalDrive;
                case VehiclePhysicsLabTuningParameter.ShiftDuration: return tuning.engine.shiftDuration;
                case VehiclePhysicsLabTuningParameter.NitrousTorque: return tuning.engine.nitrousTorque;
                case VehiclePhysicsLabTuningParameter.NitrousFuelSeconds: return tuning.engine.nitrousFuelSeconds;
                case VehiclePhysicsLabTuningParameter.LongitudinalGrip: return tuning.tires.longitudinalGrip;
                case VehiclePhysicsLabTuningParameter.LateralGrip: return tuning.tires.lateralGrip;
                case VehiclePhysicsLabTuningParameter.PeakSlip: return tuning.tires.peakLongitudinalSlip;
                case VehiclePhysicsLabTuningParameter.RollingResistance: return tuning.tires.rollingResistance;
                case VehiclePhysicsLabTuningParameter.ServiceBrakeTorque: return tuning.controls.serviceBrakeTorque;
                case VehiclePhysicsLabTuningParameter.FrontBrakeBias: return tuning.controls.frontBrakeBias;
                case VehiclePhysicsLabTuningParameter.SuspensionSpringRate: return tuning.tires.springRate;
                case VehiclePhysicsLabTuningParameter.SuspensionDamperRate: return tuning.tires.damperRate;
                case VehiclePhysicsLabTuningParameter.SuspensionTravel: return tuning.tires.suspensionTravel;
                case VehiclePhysicsLabTuningParameter.DragCoefficient: return tuning.aero.dragCoefficient;
                case VehiclePhysicsLabTuningParameter.DownforceCoefficient: return tuning.aero.downforceCoefficient;
                case VehiclePhysicsLabTuningParameter.MaxSteerAngle: return tuning.controls.maxSteerAngle;
                case VehiclePhysicsLabTuningParameter.SteeringResponse: return tuning.controls.steeringResponse;
                case VehiclePhysicsLabTuningParameter.ThrottleResponse: return tuning.controls.throttleResponse;
                case VehiclePhysicsLabTuningParameter.BrakeResponse: return tuning.controls.brakeResponse;
                case VehiclePhysicsLabTuningParameter.HighSpeedSteerScale: return tuning.controls.highSpeedSteerScale;
                case VehiclePhysicsLabTuningParameter.DriftBias: return tuning.handling.driftBias;
                case VehiclePhysicsLabTuningParameter.DriftYawGain: return tuning.handling.yawGain;
                case VehiclePhysicsLabTuningParameter.DriftMaximumYawTorque: return tuning.handling.maximumYawTorque;
                case VehiclePhysicsLabTuningParameter.StabilityStrength: return tuning.assists.stabilityStrength;
                case VehiclePhysicsLabTuningParameter.WheelRadius: return tuning.tires.wheelRadius;
                case VehiclePhysicsLabTuningParameter.WheelLateralOffset: return tuning.tires.wheelLateralOffset;
                case VehiclePhysicsLabTuningParameter.SuspensionRestLength: return tuning.tires.suspensionRestLength;
                default: throw new ArgumentOutOfRangeException(nameof(parameter), parameter, null);
            }
        }

        public static bool TryGetRange(VehiclePhysicsLabTuningParameter parameter, out float minimum, out float maximum)
        {
            switch (parameter)
            {
                case VehiclePhysicsLabTuningParameter.Mass: return Range(100f, 100000f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.MaxSpeedKph: return Range(1f, 1000f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.EngineTorque: return Range(0f, 10000f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.EngineInertia: return Range(0.01f, 100f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.FinalDrive: return Range(0.1f, 20f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.ShiftDuration: return Range(0f, 5f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.NitrousTorque: return Range(0f, 10000f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.NitrousFuelSeconds: return Range(0f, 120f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.LongitudinalGrip: return Range(0f, 10f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.LateralGrip: return Range(0f, 10f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.PeakSlip: return Range(0.001f, 2f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.RollingResistance: return Range(0f, 1f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.ServiceBrakeTorque: return Range(0f, 100000f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.FrontBrakeBias: return Range(0f, 1f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.SuspensionSpringRate: return Range(0f, 1000000f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.SuspensionDamperRate: return Range(0f, 100000f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.SuspensionTravel: return Range(0f, 5f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.DragCoefficient: return Range(0f, 10f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.DownforceCoefficient: return Range(0f, 100f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.MaxSteerAngle: return Range(1f, 60f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.SteeringResponse: return Range(0.1f, 60f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.ThrottleResponse: return Range(0.1f, 60f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.BrakeResponse: return Range(0.1f, 60f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.HighSpeedSteerScale: return Range(0.1f, 1f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.DriftBias: return Range(0f, 1f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.DriftYawGain: return Range(0f, 100000f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.DriftMaximumYawTorque: return Range(0f, 100000f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.StabilityStrength: return Range(0f, 1f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.WheelRadius: return Range(0.05f, 5f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.WheelLateralOffset: return Range(0f, 5f, out minimum, out maximum);
                case VehiclePhysicsLabTuningParameter.SuspensionRestLength: return Range(0.05f, 5f, out minimum, out maximum);
                default:
                    minimum = maximum = 0f;
                    return false;
            }
        }

        public static bool Write(VehicleTuning tuning, VehiclePhysicsLabTuningParameter parameter, float value)
        {
            switch (parameter)
            {
                case VehiclePhysicsLabTuningParameter.Mass: return Assign(value, 100f, 100000f, v => tuning.chassis.mass = v);
                case VehiclePhysicsLabTuningParameter.MaxSpeedKph: return Assign(value, 1f, 1000f, v => tuning.chassis.maxSpeedKph = v);
                case VehiclePhysicsLabTuningParameter.EngineTorque: return Assign(value, 0f, 10000f, v => tuning.engine.maxTorqueNewtonMeters = v);
                case VehiclePhysicsLabTuningParameter.EngineInertia: return Assign(value, 0.01f, 100f, v => tuning.engine.engineInertia = v);
                case VehiclePhysicsLabTuningParameter.FinalDrive: return Assign(value, 0.1f, 20f, v => tuning.engine.finalDrive = v);
                case VehiclePhysicsLabTuningParameter.ShiftDuration: return Assign(value, 0f, 5f, v => tuning.engine.shiftDuration = v);
                case VehiclePhysicsLabTuningParameter.NitrousTorque: return Assign(value, 0f, 10000f, v => tuning.engine.nitrousTorque = v);
                case VehiclePhysicsLabTuningParameter.NitrousFuelSeconds: return Assign(value, 0f, 120f, v => tuning.engine.nitrousFuelSeconds = v);
                case VehiclePhysicsLabTuningParameter.LongitudinalGrip: return Assign(value, 0f, 10f, v => tuning.tires.longitudinalGrip = v);
                case VehiclePhysicsLabTuningParameter.LateralGrip: return Assign(value, 0f, 10f, v => tuning.tires.lateralGrip = v);
                case VehiclePhysicsLabTuningParameter.PeakSlip: return Assign(value, 0.001f, 2f, v => { tuning.tires.peakLongitudinalSlip = v; tuning.tires.peakLateralSlipRadians = v; });
                case VehiclePhysicsLabTuningParameter.RollingResistance: return Assign(value, 0f, 1f, v => tuning.tires.rollingResistance = v);
                case VehiclePhysicsLabTuningParameter.ServiceBrakeTorque: return Assign(value, 0f, 100000f, v => { tuning.controls.serviceBrakeTorque = v; tuning.controls.handbrakeTorque = v; });
                case VehiclePhysicsLabTuningParameter.FrontBrakeBias: return Assign(value, 0f, 1f, v => tuning.controls.frontBrakeBias = v);
                case VehiclePhysicsLabTuningParameter.SuspensionSpringRate: return Assign(value, 0f, 1000000f, v => tuning.tires.springRate = v);
                case VehiclePhysicsLabTuningParameter.SuspensionDamperRate: return Assign(value, 0f, 100000f, v => tuning.tires.damperRate = v);
                case VehiclePhysicsLabTuningParameter.SuspensionTravel: return Assign(value, 0f, 5f, v => tuning.tires.suspensionTravel = v);
                case VehiclePhysicsLabTuningParameter.DragCoefficient: return Assign(value, 0f, 10f, v => tuning.aero.dragCoefficient = v);
                case VehiclePhysicsLabTuningParameter.DownforceCoefficient: return Assign(value, 0f, 100f, v => tuning.aero.downforceCoefficient = v);
                case VehiclePhysicsLabTuningParameter.MaxSteerAngle: return Assign(value, 1f, 60f, v => tuning.controls.maxSteerAngle = v);
                case VehiclePhysicsLabTuningParameter.SteeringResponse: return Assign(value, 0.1f, 60f, v => tuning.controls.steeringResponse = v);
                case VehiclePhysicsLabTuningParameter.ThrottleResponse: return Assign(value, 0.1f, 60f, v => tuning.controls.throttleResponse = v);
                case VehiclePhysicsLabTuningParameter.BrakeResponse: return Assign(value, 0.1f, 60f, v => tuning.controls.brakeResponse = v);
                case VehiclePhysicsLabTuningParameter.HighSpeedSteerScale: return Assign(value, 0.1f, 1f, v => tuning.controls.highSpeedSteerScale = v);
                case VehiclePhysicsLabTuningParameter.DriftBias: return Assign(value, 0f, 1f, v => tuning.handling.driftBias = v);
                case VehiclePhysicsLabTuningParameter.DriftYawGain: return Assign(value, 0f, 100000f, v => tuning.handling.yawGain = v);
                case VehiclePhysicsLabTuningParameter.DriftMaximumYawTorque: return Assign(value, 0f, 100000f, v => tuning.handling.maximumYawTorque = v);
                case VehiclePhysicsLabTuningParameter.StabilityStrength: return Assign(value, 0f, 1f, v => tuning.assists.stabilityStrength = v);
                case VehiclePhysicsLabTuningParameter.WheelRadius: return Assign(value, 0.05f, 5f, v => tuning.tires.wheelRadius = v);
                case VehiclePhysicsLabTuningParameter.WheelLateralOffset: return Assign(value, 0f, 5f, v => tuning.tires.wheelLateralOffset = v);
                case VehiclePhysicsLabTuningParameter.SuspensionRestLength: return Assign(value, 0.05f, 5f, v => tuning.tires.suspensionRestLength = v);
                default: throw new ArgumentOutOfRangeException(nameof(parameter), parameter, null);
            }
        }

        private static bool Assign(float value, float minimum, float maximum, Action<float> assign)
        {
            if (!Finite(value) || value < minimum || value > maximum)
            {
                return false;
            }

            assign(value);
            return true;
        }

        private static bool Range(float minimum, float maximum, out float resultMinimum, out float resultMaximum)
        {
            resultMinimum = minimum;
            resultMaximum = maximum;
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
