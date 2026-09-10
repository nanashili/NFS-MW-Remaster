using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(menuName = "NFS MW Remaster/Driving/Vehicle Physics Lab Experiment", fileName = "PhysicsLabExperiment")]
    public sealed class VehiclePhysicsLabDefinition : ScriptableObject
    {
        public const int CurrentSchema = 1;

        [HideInInspector] public int schema = CurrentSchema;
        [HideInInspector] public string id = Guid.NewGuid().ToString("N");
        public string displayName = "Standing launch";
        public RacingVehicleSetup vehicle;
        public VehiclePhysicsLabTrack track;
        public RacingCapabilityProfile publishToCapability;
        public VehiclePhysicsLabExperimentKind experiment = VehiclePhysicsLabExperimentKind.StandingLaunch;
        [Min(0.005f)] public float fixedStep = 0.02f;
        [Min(0f)] public float warmupSeconds = 1f;
        [Min(0f)] public float startingSpeedKph;
        [Min(0f)] public float targetSpeedKph = 100f;
        [Min(0f)] public float brakingStartSpeedKph = 100f;
        [Min(0f)] public int seed = 1;
        public VehiclePhysicsLabInputSchedule input = new VehiclePhysicsLabInputSchedule();
        public VehiclePhysicsLabEvaluationSettings evaluation = new VehiclePhysicsLabEvaluationSettings();
        public VehiclePhysicsLabCaptureSettings capture = new VehiclePhysicsLabCaptureSettings();
        public VehiclePhysicsLabSafetySettings safety = new VehiclePhysicsLabSafetySettings();
        public VehiclePhysicsLabTuningOverride[] temporaryOverrides = Array.Empty<VehiclePhysicsLabTuningOverride>();
        public VehiclePhysicsLabReferenceEvidence[] referenceEvidence = Array.Empty<VehiclePhysicsLabReferenceEvidence>();

        public bool IsValid(out string failure)
        {
            failure = string.Empty;
            if (schema != CurrentSchema || !Guid.TryParseExact(id, "N", out _))
            {
                failure = "PHYSICS_LAB_SCHEMA: migrate the experiment asset before use.";
                return false;
            }

            if (vehicle == null)
            {
                failure = "PHYSICS_LAB_VEHICLE: assign a canonical Racing Vehicle Setup.";
                return false;
            }

            if (track == null)
            {
                failure = "PHYSICS_LAB_TRACK: assign a track.";
                return false;
            }

            if (!track.IsValid(out failure))
            {
                return false;
            }

            if (!Finite(fixedStep) || fixedStep < 0.005f || fixedStep > 0.05f || !Finite(warmupSeconds) || warmupSeconds < 0f
                || !Finite(startingSpeedKph) || startingSpeedKph < 0f || !Finite(targetSpeedKph) || targetSpeedKph < 0f
                || !Finite(brakingStartSpeedKph) || brakingStartSpeedKph < 0f || input == null || evaluation == null
                || capture == null || safety == null || capture.maximumSamples < 32 || safety.maximumSeconds < 1f
                || safety.maximumLinearSpeedMps <= 0f || safety.maximumAngularSpeedRadPerSec <= 0f
                || !Finite(input.stepTime) || input.stepTime < 0f || !Finite(input.steering) || input.steering < -1f || input.steering > 1f
                || !Finite(input.throttle) || input.throttle < 0f || input.throttle > 1f
                || !Finite(input.brake) || input.brake < 0f || input.brake > 1f || !Finite(input.sineFrequencyHz) || input.sineFrequencyHz <= 0f
                || !Finite(input.sineAmplitude) || input.sineAmplitude < 0f || input.sineAmplitude > 1f
                || !Finite(input.transitionSeconds) || input.transitionSeconds < 0f
                || !Finite(evaluation.targetSpeedKph) || evaluation.targetSpeedKph < 0f
                || !Finite(evaluation.targetStopSpeedKph) || evaluation.targetStopSpeedKph < 0f
                || !Finite(evaluation.targetToleranceKph) || evaluation.targetToleranceKph < 0f
                || !Finite(evaluation.maximumSlipAngleDegrees) || evaluation.maximumSlipAngleDegrees < 0f
                || !Finite(evaluation.minimumGroundedFraction) || evaluation.minimumGroundedFraction < 0f || evaluation.minimumGroundedFraction > 1f
                || !Finite(evaluation.maximumAirborneSeconds) || evaluation.maximumAirborneSeconds < 0f
                || !Finite(evaluation.maximumRolloverDegrees) || evaluation.maximumRolloverDegrees < 0f
                || !Finite(evaluation.maximumLateralErrorMetres) || evaluation.maximumLateralErrorMetres < 0f
                || !Finite(capture.sampleIntervalSeconds) || capture.sampleIntervalSeconds < 0.005f
                || !Finite(safety.maximumSeconds) || !Finite(safety.fixtureHalfExtent))
            {
                failure = "PHYSICS_LAB_VALUES: timing, capture and safety settings must be finite and within their supported ranges.";
                return false;
            }

            if (input.keys == null || input.keys.Length > 4096)
            {
                failure = "PHYSICS_LAB_INPUT: recorded input must contain between zero and 4096 keys.";
                return false;
            }

            float previousKeyTime = -1f;
            for (int i = 0; i < input.keys.Length; i++)
            {
                VehiclePhysicsLabControlKey key = input.keys[i];
                if (!Finite(key.time) || key.time < 0f || key.time < previousKeyTime
                    || !Finite(key.input.Steering) || key.input.Steering < -1f || key.input.Steering > 1f
                    || !Finite(key.input.Throttle) || key.input.Throttle < 0f || key.input.Throttle > 1f
                    || !Finite(key.input.Brake) || key.input.Brake < 0f || key.input.Brake > 1f)
                {
                    failure = "PHYSICS_LAB_INPUT: recorded keys must have non-decreasing times and controls within steering [-1,1] and pedal [0,1] ranges.";
                    return false;
                }
                previousKeyTime = key.time;
            }

            try
            {
                RacingLineSnapshot.ValidateSetup(vehicle);
            }
            catch (ArgumentException exception)
            {
                failure = "PHYSICS_LAB_VEHICLE: " + exception.Message;
                return false;
            }

            if (temporaryOverrides == null || temporaryOverrides.Length > 128)
            {
                failure = "PHYSICS_LAB_OVERRIDES: provide at most 128 temporary overrides.";
                return false;
            }

            for (int i = 0; i < temporaryOverrides.Length; i++)
            {
                if (temporaryOverrides[i].enabled && !Finite(temporaryOverrides[i].value))
                {
                    failure = "PHYSICS_LAB_OVERRIDES: override " + i + " is not finite.";
                    return false;
                }
            }

            if (referenceEvidence == null || referenceEvidence.Length > 64)
            {
                failure = "PHYSICS_LAB_REFERENCE: provide at most 64 provenance records.";
                return false;
            }

            for (int i = 0; i < referenceEvidence.Length; i++)
            {
                VehiclePhysicsLabReferenceEvidence evidence = referenceEvidence[i];
                if (evidence == null || !Finite(evidence.frameRate) || evidence.frameRate < 0f
                    || !Finite(evidence.timingUncertaintySeconds) || evidence.timingUncertaintySeconds < 0f)
                {
                    failure = "PHYSICS_LAB_REFERENCE: reference evidence contains invalid timing metadata.";
                    return false;
                }
            }

            failure = string.Empty;
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
