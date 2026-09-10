using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace NfsMwRemaster.Driving.Editor
{
    public readonly struct RoadFrame
    {
        public readonly float Station, Distance, Parameter;
        public readonly Vector3 Position, Forward, Left, Up;
        public RoadFrame(float station, float distance, float parameter, Vector3 position, Vector3 forward, float bank)
        {
            Station = station; Distance = distance; Parameter = parameter; Position = position;
            Forward = forward.normalized;
            var right = Vector3.Cross(Vector3.up, Forward).normalized;
            var rotation = Quaternion.AngleAxis(bank * Mathf.Rad2Deg, Forward);
            Left = rotation * -right; Up = rotation * Vector3.Cross(Forward, right).normalized;
        }
        public Vector3 At(float lateral, float height = 0) => Position + Left * lateral + Up * height;
    }

    public sealed class RoadEvaluation
    {
        private readonly RoadFrame[] frames;
        private readonly IReadOnlyList<RoadFrame> frameView;
        private readonly AnimationCurve bank;
        public IReadOnlyList<RoadFrame> Frames => frameView;
        public float Length => frames[frames.Length - 1].Station;
        public float TravelLength => frames[frames.Length - 1].Distance;
        internal RoadEvaluation(RoadFrame[] frames, AnimationCurve bank)
        { this.frames = frames; frameView = Array.AsReadOnly(frames); this.bank = bank; }
        public RoadFrame Sample(float station)
        {
            if (!RoadGeometry.Finite(station) || station < 0 || station > Length)
                throw new ArgumentOutOfRangeException(nameof(station));
            int low = 0, high = frames.Length - 1;
            while (high - low > 1) { int mid = (low + high) / 2; if (frames[mid].Station <= station) low = mid; else high = mid; }
            var a = frames[low]; var b = frames[high];
            float t = (station - a.Station) / (b.Station - a.Station);
            return new RoadFrame(station, Mathf.Lerp(a.Distance, b.Distance, t), Mathf.Lerp(a.Parameter, b.Parameter, t),
                Vector3.Lerp(a.Position, b.Position, t), Vector3.Slerp(a.Forward, b.Forward, t), bank.Evaluate(station));
        }
    }

    /// <summary>Shared evaluated reference; stations use XZ length and distance includes elevation.</summary>
    public static class RoadGeometry
    {
        private const int MaximumDepth = 18, MaximumSamples = 200000;
        private struct Point { public Vector3 position, tangent; public float parameter; }
        public static RoadEvaluation Evaluate(RoadAuthoring road)
        {
            Validate(road);
            var points = new List<Point>();
            var spline = road.Reference.Spline;
            var matrix = road.Reference.transform.localToWorldMatrix;
            for (int i = 0; i < spline.Count - 1; i++)
            {
                var curve = spline.GetCurve(i);
                var world = new BezierCurve(matrix.MultiplyPoint3x4(curve.P0), matrix.MultiplyPoint3x4(curve.P1),
                    matrix.MultiplyPoint3x4(curve.P2), matrix.MultiplyPoint3x4(curve.P3));
                RejectCusps(world);
                if (i == 0) points.Add(EvaluatePoint(world, spline, i, 0));
                Subdivide(world, spline, i, 0, 1, 0, road.chordTolerance, road.maximumSampleSpacing, points);
            }
            var bank = new AnimationCurve(road.bankRadians.keys)
            { preWrapMode = road.bankRadians.preWrapMode, postWrapMode = road.bankRadians.postWrapMode };
            var frames = new RoadFrame[points.Count];
            float station = 0, distance = 0;
            for (int i = 0; i < points.Count; i++)
            {
                if (i > 0)
                {
                    var delta = points[i].position - points[i - 1].position;
                    float planar = new Vector2(delta.x, delta.z).magnitude;
                    if (planar < 0.000001f) throw new ArgumentException("ROAD_STATION: coincident or vertical reference span.");
                    station += planar; distance += delta.magnitude;
                }
                var p = points[i];
                if (Vector3.Cross(Vector3.up, p.tangent.normalized).sqrMagnitude < 0.0001f)
                    throw new ArgumentException("ROAD_FRAME: reference tangent is vertical or undefined.");
                frames[i] = new RoadFrame(station, distance, p.parameter, p.position, p.tangent, bank.Evaluate(station));
            }
            if (station < 0.01f) throw new ArgumentException("ROAD_LENGTH: reference must exceed 1 cm.");
            return new RoadEvaluation(frames, bank);
        }

        public static void Validate(RoadAuthoring road)
        {
            if (road == null || !road.Id.IsValid || road.SchemaVersion != RoadAuthoring.CurrentSchema)
                throw new ArgumentException("ROAD_ID: missing identity or unsupported source schema.");
            if (road.Reference == null || road.Reference.gameObject != road.gameObject || road.Reference.Splines.Count != 1)
                throw new ArgumentException("ROAD_REFERENCE: exactly one local reference spline is required.");
            if (road.Reference.Spline.Closed || road.Reference.Spline.Count < 2)
                throw new ArgumentException("ROAD_REFERENCE: an open spline needs at least two knots; close routes with explicit connections.");
            for (var parent = road.transform; parent != null; parent = parent.parent)
                if ((parent.localScale - Vector3.one).sqrMagnitude > 0.00000001f)
                    throw new ArgumentException("ROAD_SCALE: bake scale into source geometry before authoring.");
            if (!Finite(road.chordTolerance) || road.chordTolerance < 0.0001f || !Finite(road.maximumSampleSpacing) || road.maximumSampleSpacing < 0.1f)
                throw new ArgumentException("ROAD_TOLERANCE: invalid sampling settings.");
            if (!Finite(road.chunkLength) || road.chunkLength < 1) throw new ArgumentException("ROAD_CHUNK: invalid chunk length.");
            if (road.Profile == null || road.Profile.bands == null || road.Profile.bands.Length == 0 || road.Bands == null || road.Bands.Length != road.Profile.bands.Length)
                throw new ArgumentException("ROAD_PROFILE: assign a profile using the road tool; band changes require reconciliation.");
            var ids = new HashSet<RoadId> { road.Id };
            var templates = new HashSet<RoadId>();
            for (int i = 0; i < road.Bands.Length; i++)
            {
                var b = road.Profile.bands[i]; var binding = road.Bands[i];
                if (b == null || !b.id.IsValid || !templates.Add(b.id) || binding == null || binding.templateId != b.id || !binding.id.IsValid || !ids.Add(binding.id))
                    throw new ArgumentException("ROAD_BAND_ID: missing, duplicate or unmapped band identity.");
                if (!Finite(b.width) || b.width <= 0 || !Finite(b.height) || !Finite(b.crossfall))
                    throw new ArgumentException("ROAD_WIDTH: invalid band dimensions.");
            }
            if (!Finite(road.Profile.lateralOffset) || !Finite(road.Profile.speedLimit) || road.Profile.speedLimit <= 0 || !Finite(road.Profile.textureMetres) || road.Profile.textureMetres <= 0)
                throw new ArgumentException("ROAD_PROFILE: invalid offset, speed or texture repeat.");
            if (road.bankRadians == null || road.bankRadians.length == 0) throw new ArgumentException("ROAD_BANK: missing bank curve.");
            foreach (var key in road.bankRadians.keys)
                if (!Finite(key.time) || !Finite(key.value) || !Finite(key.inTangent) || !Finite(key.outTangent) || Mathf.Abs(key.value) > 1.4f)
                    throw new ArgumentException("ROAD_BANK: bank keys must be finite and below 80 degrees.");
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void RejectCusps(BezierCurve curve)
        {
            Vector3 p0 = curve.P0, p1 = curve.P1, p2 = curve.P2, p3 = curve.P3;
            Vector3 a = -p0 + 3 * p1 - 3 * p2 + p3, b = 2 * (p0 - 2 * p1 + p2), c = p1 - p0;
            int axis = 0;
            for (int i = 1; i < 3; i++)
                if (Mathf.Abs(a[i]) + Mathf.Abs(b[i]) + Mathf.Abs(c[i]) > Mathf.Abs(a[axis]) + Mathf.Abs(b[axis]) + Mathf.Abs(c[axis])) axis = i;
            // All three derivative components must vanish. Candidate roots from the strongest component suffice.
            if (Mathf.Abs(a[axis]) < 0.00000001f)
            {
                if (Mathf.Abs(b[axis]) > 0.00000001f) Check(-c[axis] / b[axis]);
                return;
            }
            double discriminant = (double)b[axis] * b[axis] - 4.0 * a[axis] * c[axis];
            if (discriminant < 0) return;
            double root = Math.Sqrt(discriminant);
            Check((float)((-b[axis] - root) / (2.0 * a[axis])));
            Check((float)((-b[axis] + root) / (2.0 * a[axis])));
            void Check(float t)
            {
                if (t <= 0 || t >= 1) return;
                if (((a * t + b) * t + c).sqrMagnitude < 0.00000001f && (2 * a * t + b).sqrMagnitude > 0.00000001f)
                    throw new ArgumentException("ROAD_CUSP: the reference reverses at a zero tangent; separate or reshape this span.");
            }
        }
        private static Point EvaluatePoint(BezierCurve curve, Spline spline, int index, float t)
        {
            Vector3 position = CurveUtility.EvaluatePosition(curve, t), tangent = CurveUtility.EvaluateTangent(curve, t);
            if (tangent.sqrMagnitude < 0.00000001f && (t == 0 || t == 1))
                tangent = t == 0 ? (Vector3)(curve.P2 - curve.P0) : (Vector3)(curve.P3 - curve.P1);
            if (tangent.sqrMagnitude < 0.00000001f) tangent = (Vector3)(curve.P3 - curve.P0);
            if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z) || !Finite(tangent.x) || !Finite(tangent.y) || !Finite(tangent.z))
                throw new ArgumentException("ROAD_FINITE: reference contains nonfinite geometry.");
            return new Point { position = position, tangent = tangent, parameter = spline.CurveToSplineT(index + t) };
        }
        private static void Subdivide(BezierCurve curve, Spline spline, int index, float a, float b, int depth, float tolerance, float spacing, List<Point> output)
        {
            var start = EvaluatePoint(curve, spline, index, a); var end = EvaluatePoint(curve, spline, index, b);
            float error = 0;
            for (int i = 1; i < 4; i++)
            {
                var p = EvaluatePoint(curve, spline, index, Mathf.Lerp(a, b, i * 0.25f));
                error = Mathf.Max(error, DistanceToSegment(p.position, start.position, end.position));
            }
            if (error > tolerance || Vector3.Distance(start.position, end.position) > spacing)
            {
                if (depth == MaximumDepth || output.Count >= MaximumSamples)
                    throw new ArgumentException("ROAD_SAMPLE_BUDGET: subdivision cannot meet the requested tolerance.");
                float mid = (a + b) * 0.5f;
                Subdivide(curve, spline, index, a, mid, depth + 1, tolerance, spacing, output);
                Subdivide(curve, spline, index, mid, b, depth + 1, tolerance, spacing, output);
            }
            else
            {
                if (output.Count >= MaximumSamples) throw new ArgumentException("ROAD_SAMPLE_BUDGET: reference exceeds the sample limit.");
                output.Add(end);
            }
        }
        internal static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var d = b - a;
            return Vector3.Distance(p, a + d * (d.sqrMagnitude > 0 ? Mathf.Clamp01(Vector3.Dot(p - a, d) / d.sqrMagnitude) : 0));
        }
    }
}
