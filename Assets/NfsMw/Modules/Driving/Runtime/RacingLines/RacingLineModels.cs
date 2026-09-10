using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum RacingLineState { Authored, Generated, Verified, Failed, Stale }
    public enum RacingLineFamily { Grip, Drift, Inside, Outside, SideBySide, TrafficBypass, RecoveryEntry }
    public enum RacingHintKind { Entry, Apex, Exit, Pin, SpeedLimit, BrakeToDrift }
    public enum RacingDiagnosticSeverity { Info, Warning, Error }

    [Serializable]
    public sealed class RacingRouteSpan
    {
        public string id = Guid.NewGuid().ToString("N");
        public string branch = "main";
        public string laneId;
        [Min(0)] public float startMetres;
        [Tooltip("-1 uses the lane's end. Distances follow the lane's legal travel direction.")]
        public float endMetres = -1;
    }

    [Serializable]
    public sealed class RacingLineHint
    {
        public string id = Guid.NewGuid().ToString("N");
        public RacingHintKind kind = RacingHintKind.Apex;
        [Min(0)] public float station;
        [Min(0.1f)] public float radius = 10;
        [Tooltip("Metres left of the directed lane centre, not world X.")]
        public float lateral;
        [Min(0)] public float speed = 15;
        [Range(0, 1)] public float brake = 0.6f;
        [Range(0.02f, 2)] public float cueSeconds = 0.14f;
        [Range(-1, 1)] public float cueSteering = 0.55f;
    }

    [Serializable]
    public sealed class RacingLineExclusion
    {
        public string id = Guid.NewGuid().ToString("N");
        public string label = "Excluded corridor";
        public float start, end = 20, minimumLateral = -1, maximumLateral = 1;
    }

    [Serializable]
    public sealed class RacingPlannerSettings
    {
        [Range(0.5f, 10)] public float spacing = 2;
        [Range(0, 300)] public int iterations = 60;
        [Range(0, 1)] public float refinementStrength = 0.08f;
        [Min(0)] public float clearance = 0.3f;
        [Min(1)] public float maximumSpeed = 30;
        [Min(0)] public float entrySpeed = 0;
        [Min(0)] public float exitSpeed = 8;
        [Range(0.1f, 1)] public float gripSafety = 0.7f;
        [Range(0, 2)] public float brakingDelay = 0.25f;
        [Min(0.1f)] public float maximumVerticalAcceleration = 3;
        [Range(1, 4096)] public int maximumSamples = 2048;
        public int seed = 2005;
    }

    [Serializable]
    public sealed class RacingControllerSettings
    {
        [Min(1)] public float minimumLookahead = 5;
        [Min(0)] public float lookaheadSeconds = 0.5f;
        [Min(0.01f)] public float speedGain = 0.6f;
        [Min(0)] public float integralGain = 0.06f;
        [Range(0, 1)] public float yawDamping = 0.1f;
    }

    [Serializable]
    public sealed class RacingVerificationSettings
    {
        [Range(1, 25)] public int trials = 5;
        [Min(1)] public float maximumSeconds = 180;
        [Min(0)] public float entrySpeedPerturbation = 1;
        [Min(0)] public float entryOffsetPerturbation = 0.25f;
        [Range(0, 0.3f)] public float gripPerturbation = 0.05f;
        [Range(0, 1)] public float reactionDelayPerturbation = 0.05f;
        [Min(0.05f)] public float maximumLateralError = 1.5f;
        [Min(0.1f)] public float maximumSpeedError = 5;
        [Min(0)] public float maximumAirborneSeconds = 0.2f;
        [Range(0, 1)] public float maximumSaturationFraction = 0.15f;
        [Min(0)] public float maximumSlipDegrees = 65;
    }

    [Serializable]
    public struct RacingCorridorSample
    {
        public string occurrenceId, branchId, laneId;
        public float station, roadStation, width, speedLimit, grip;
        public Vector3 position, forward, left, up;
    }

    [Serializable]
    public struct RacingTrajectorySample
    {
        public string occurrenceId, branchId, laneId;
        public float station, roadStation, arcLength, time, lateral, curvature, verticalCurvature;
        public float targetSpeed, speedLimit, clearance;
        public Vector3 position, tangent, normal;
    }

    [Serializable]
    public struct RacingControlCue
    {
        public string hintId;
        public float station, seconds, brake, steering;
    }

    [Serializable]
    public sealed class RacingLineDiagnostic
    {
        public RacingDiagnosticSeverity severity;
        public string code, message;
        public float station;
        public RacingLineDiagnostic(RacingDiagnosticSeverity severity, string code, string message, float station = 0)
        { this.severity = severity; this.code = code; this.message = message; this.station = station; }
    }

    [Serializable]
    public sealed class RacingLineCandidate
    {
        public string id, fingerprint;
        public RacingLineFamily family;
        public RacingLineState state;
        public RacingTrajectorySample[] samples = Array.Empty<RacingTrajectorySample>();
        public RacingControlCue[] cues = Array.Empty<RacingControlCue>();
        public RacingLineDiagnostic[] diagnostics = Array.Empty<RacingLineDiagnostic>();
        public float baselineCost, geometricCost, estimatedSeconds;
        public int iterations;
        public double computeMilliseconds;
        public string termination;
        public bool HasErrors => diagnostics == null || Array.Exists(diagnostics, d => d == null || d.severity == RacingDiagnosticSeverity.Error);
        public string GeometryFingerprint()
        {
            using var digest = new RacingDigest(); digest.Add("trajectory-geometry.1"); digest.Add(id); digest.Add(family.ToString());
            foreach (var sample in samples) digest.Add(JsonUtility.ToJson(sample));
            foreach (var cue in cues) digest.Add(JsonUtility.ToJson(cue));
            return digest.Finish();
        }
    }

    [Serializable]
    public struct RacingTelemetrySample
    {
        public float time, station, targetSpeed, actualSpeed, lateralError, headingError;
        public float steering, throttle, brake, slipDegrees, yawRate, assistTorque, clearance;
        public int contacts, vehicleIndex;
        public VehicleHandlingMode handling;
        public Vector3 position;
    }

    [Serializable]
    public sealed class RacingTrialReport
    {
        public int trial;
        public bool completed, passed, driftEngaged, driftRecovered;
        public string diagnosis;
        public float elapsed, maximumLateralError, rmsSpeedError, minimumClearance, saturationFraction;
        public float maximumSlipDegrees, airborneSeconds, entrySpeed, entryOffset, gripScale, reactionDelay;
        public float minimumCompanionSeparation;
        public int collisions, boundaryViolations;
        public RacingTelemetrySample[] telemetry = Array.Empty<RacingTelemetrySample>();
    }

    [Serializable]
    public sealed class RacingVerificationReport
    {
        public const string Revision = "racing-line-verifier.3";
        public string verificationRevision = Revision;
        public string fingerprint, trajectoryFingerprint, candidateId, utc, engineVersion, machine, controllerRevision;
        public bool passed, companionRun;
        public float telemetryIntervalSeconds;
        public double computeMilliseconds;
        public RacingTrialReport[] trials = Array.Empty<RacingTrialReport>();
    }
}
