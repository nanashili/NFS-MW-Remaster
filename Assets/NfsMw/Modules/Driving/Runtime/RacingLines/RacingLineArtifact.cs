using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public interface IRacingTrajectory
    {
        string Id { get; }
        float Length { get; }
        RacingTrajectorySample Sample(float station);
    }

    public sealed class RacingLineArtifact : ScriptableObject
    {
        public const int CurrentSchema = 2;
        [SerializeField] private int schema;
        [SerializeField] private string fingerprint, vehicleFingerprint;
        [SerializeField] private RacingLineCandidate trajectory;
        [SerializeField] private RacingVerificationReport verification;
        [SerializeField] private RacingEntryEnvelope entry;
        public int Schema => schema;
        public string Fingerprint => fingerprint;
        public string VehicleFingerprint => vehicleFingerprint;
        public RacingVerificationReport Report => verification == null ? null : RacingLineSnapshot.Clone(verification);
        public RacingEntryEnvelope Entry => entry;

        public void Initialize(RacingLineSnapshot input, RacingLineCandidate candidate, RacingVerificationReport report)
        {
            if (schema != 0) throw new InvalidOperationException("LINE_IMMUTABLE: publish a new artifact revision.");
            if (input == null || candidate == null || report == null || !report.passed || report.companionRun
                || report.fingerprint != input.Fingerprint || candidate.fingerprint != input.Fingerprint || report.candidateId != candidate.id
                || candidate.HasErrors || report.trials == null || report.trials.Length != input.Verification.trials
                || report.engineVersion != Application.unityVersion || report.controllerRevision != RacingLineTracker.Revision
                || report.verificationRevision != RacingVerificationReport.Revision
                || report.trajectoryFingerprint != candidate.GeometryFingerprint()
                || Array.Exists(report.trials, trial => trial == null || !trial.passed || !trial.completed))
                throw new ArgumentException("LINE_UNVERIFIED: a matching successful production-physics report is required.");
            _ = new RacingTrajectoryReader(candidate);
            trajectory = RacingLineSnapshot.Clone(candidate); trajectory.state = RacingLineState.Verified;
            verification = RacingLineSnapshot.Clone(report); fingerprint = input.Fingerprint; vehicleFingerprint = input.VehicleFingerprint;
            // Raw traces remain in the authoring report/export. Players load samples and bounded summaries only.
            foreach (var trial in verification.trials) trial.telemetry = Array.Empty<RacingTelemetrySample>();
            entry = new RacingEntryEnvelope { minimumSpeed = Mathf.Max(0, candidate.samples[0].targetSpeed - input.Verification.entrySpeedPerturbation),
                maximumSpeed = candidate.samples[0].targetSpeed + input.Verification.entrySpeedPerturbation,
                maximumLateralOffset = input.Verification.entryOffsetPerturbation, maximumHeadingDegrees = 2, maximumLongitudinalOffset = 0.5f };
            schema = CurrentSchema;
        }
        public RacingLineCandidate CopyTrajectory() => trajectory == null ? null : RacingLineSnapshot.Clone(trajectory);

        public bool TryOpen(string expectedFingerprint, out RacingTrajectoryReader reader, out string failure)
        {
            reader = null;
            if (schema != CurrentSchema || trajectory == null || trajectory.state != RacingLineState.Verified
                || string.IsNullOrEmpty(expectedFingerprint) || expectedFingerprint != fingerprint)
            { failure = "LINE_INCOMPATIBLE: missing, stale or unsupported trajectory. Use the controller's deliberate safe fallback."; return false; }
            try { reader = new RacingTrajectoryReader(trajectory); failure = null; return true; }
            catch (ArgumentException exception) { failure = exception.Message; return false; }
        }
    }

    public sealed class RacingTrajectoryReader : IRacingTrajectory
    {
        private readonly RacingTrajectorySample[] points;
        private readonly RacingControlCue[] cues;
        public string Id { get; }
        public float Length => points[points.Length - 1].station;
        public int Count => points.Length;
        public RacingTrajectorySample this[int index] => points[index];
        public int CueCount => cues.Length;
        public RacingControlCue Cue(int index) => cues[index];
        public RacingTrajectoryReader(RacingLineCandidate candidate)
        {
            if (candidate == null || candidate.samples == null || candidate.samples.Length < 3 || candidate.samples.Length > 4096
                || candidate.cues == null || candidate.cues.Length > 256 || candidate.HasErrors)
                throw new ArgumentException("LINE_GEOMETRY: a finite, non-rejected sampled trajectory is required.");
            Id = candidate.id; points = (RacingTrajectorySample[])candidate.samples.Clone(); cues = (RacingControlCue[])candidate.cues.Clone();
            for (int i = 0; i < points.Length; i++)
                if (!RacingLineSnapshot.Finite(points[i].position) || !RacingLineSnapshot.Finite(points[i].tangent)
                    || points[i].tangent.sqrMagnitude < 0.9f || !RacingLineSnapshot.Finite(points[i].normal) || points[i].normal.sqrMagnitude < 0.9f
                    || !RacingLineSnapshot.Finite(points[i].curvature) || !RacingLineSnapshot.Finite(points[i].verticalCurvature)
                    || !RacingLineSnapshot.Finite(points[i].time) || !RacingLineSnapshot.Finite(points[i].arcLength)
                    || !RacingLineSnapshot.Range(points[i].targetSpeed, 0, 150) || !RacingLineSnapshot.Finite(points[i].station)
                    || i > 0 && points[i].station <= points[i - 1].station)
                    throw new ArgumentException("LINE_GEOMETRY: non-finite or unordered samples.");
            if (Mathf.Abs(points[0].station) > 0.001f) throw new ArgumentException("LINE_GEOMETRY: route station must start at zero.");
            for (int i = 0; i < cues.Length; i++)
                if (!RacingLineSnapshot.Range(cues[i].station, 0, Length) || !RacingLineSnapshot.Range(cues[i].seconds, 0.02f, 2)
                    || !RacingLineSnapshot.Range(cues[i].brake, 0, 1) || !RacingLineSnapshot.Range(cues[i].steering, -1, 1)
                    || i > 0 && cues[i].station < cues[i - 1].station)
                    throw new ArgumentException("LINE_CUE: invalid or unordered control cue.");
        }

        public RacingTrajectorySample Sample(float station)
        {
            if (!RacingLineSnapshot.Finite(station)) throw new ArgumentException("LINE_STATION: station must be finite.");
            station = Mathf.Clamp(station, points[0].station, Length);
            int i = Index(station); var a = points[i]; var b = points[i + 1];
            float t = Mathf.InverseLerp(a.station, b.station, station);
            var result = t < 1 ? a : b;
            result.position = Vector3.Lerp(a.position, b.position, t); result.tangent = Vector3.Slerp(a.tangent, b.tangent, t).normalized;
            result.normal = Vector3.Slerp(a.normal, b.normal, t).normalized; result.station = station;
            result.targetSpeed = Mathf.Lerp(a.targetSpeed, b.targetSpeed, t); result.curvature = Mathf.Lerp(a.curvature, b.curvature, t);
            result.clearance = Mathf.Lerp(a.clearance, b.clearance, t); result.lateral = Mathf.Lerp(a.lateral, b.lateral, t);
            result.time = Mathf.Lerp(a.time, b.time, t); result.arcLength = Mathf.Lerp(a.arcLength, b.arcLength, t);
            result.roadStation = a.laneId == b.laneId ? Mathf.Lerp(a.roadStation, b.roadStation, t) : result.roadStation;
            result.verticalCurvature = Mathf.Lerp(a.verticalCurvature, b.verticalCurvature, t);
            result.speedLimit = Mathf.Lerp(a.speedLimit, b.speedLimit, t);
            return result;
        }

        public int Index(float station)
        {
            int low = 0, high = points.Length - 2;
            while (low < high) { int mid = (low + high + 1) / 2; if (points[mid].station <= station) low = mid; else high = mid - 1; }
            return low;
        }

        // A bounded occurrence-local search prevents a crossing/bridge from jumping route progress.
        public float Project(Vector3 point, float previousStation, float searchMetres, out float lateralError)
        {
            int start = Index(Mathf.Max(0, previousStation - 5)); int end = Index(Mathf.Min(Length, previousStation + searchMetres));
            float best = float.PositiveInfinity, station = previousStation; lateralError = 0;
            for (int i = start; i <= end; i++)
            {
                var a = points[i]; var b = points[i + 1]; var edge = b.position - a.position;
                float t = edge.sqrMagnitude > 0.00001f ? Mathf.Clamp01(Vector3.Dot(point - a.position, edge) / edge.sqrMagnitude) : 0;
                Vector3 delta = point - a.position - edge * t;
                float error = delta.sqrMagnitude;
                if (error >= best) continue;
                best = error; station = Mathf.Lerp(a.station, b.station, t);
                lateralError = Vector3.Dot(delta, Vector3.Cross(a.tangent, a.normal).normalized);
            }
            return station;
        }
    }

    [Serializable]
    public struct RacingEntryEnvelope
    {
        public float minimumSpeed, maximumSpeed, maximumLateralOffset, maximumHeadingDegrees, maximumLongitudinalOffset;
        public bool Contains(RacingTrajectorySample start, Transform pose, Vector3 velocity)
        {
            var delta = pose.position - start.position;
            float speed = Vector3.Dot(velocity, start.tangent);
            return RacingLineSnapshot.Finite(velocity) && RacingLineSnapshot.Finite(pose.position)
                && speed >= minimumSpeed - 0.05f && speed <= maximumSpeed + 0.05f
                && Mathf.Abs(Vector3.Dot(delta, start.tangent)) <= maximumLongitudinalOffset
                && Mathf.Abs(Vector3.Dot(delta, Vector3.Cross(start.tangent, start.normal))) <= maximumLateralOffset + 0.02f
                && Vector3.Angle(pose.forward, start.tangent) <= maximumHeadingDegrees
                && Vector3.Angle(pose.up, start.normal) <= maximumHeadingDegrees;
        }
    }
}
