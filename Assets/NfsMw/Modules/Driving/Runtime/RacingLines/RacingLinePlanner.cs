using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    // Cooperative bounded numerical work. Step never touches an asset or a scene.
    public sealed class RacingLinePlanner : IDisposable
    {
        private readonly RacingLineSnapshot input;
        private readonly RacingLineFamily family;
        private readonly float[] offsets, target, trial;
        private readonly bool[] pinned;
        private readonly List<RacingLineDiagnostic> diagnostics = new List<RacingLineDiagnostic>();
        private readonly Stopwatch total = new Stopwatch();
        private readonly RacingLineCandidate preserved;
        private readonly float sectorStart, sectorEnd;
        private int iteration;
        private bool done, cancelled;
        private float bestCost, baseline, step;
        private RacingTrajectorySample[] finalSamples;
        private int geometryCursor, sweepCursor;
        private string terminationReason;
        public RacingLineCandidate Result { get; private set; }
        public bool IsDone => done;
        public bool IsCancelled => cancelled;
        public float Progress => done ? 1 : (float)iteration / Mathf.Max(1, input.Settings.iterations);

        public RacingLinePlanner(RacingLineSnapshot input, RacingLineFamily family, RacingLineCandidate preserve = null,
            float sectorStart = 0, float sectorEnd = float.PositiveInfinity)
        {
            this.input = input ?? throw new ArgumentNullException(nameof(input)); this.family = family;
            if (!Enum.IsDefined(typeof(RacingLineFamily), family)) throw new ArgumentException("LINE_FAMILY: unsupported line family.");
            this.sectorStart = sectorStart; this.sectorEnd = sectorEnd; preserved = preserve;
            if (preserve != null && (preserve.samples.Length != input.Corridor.Length || preserve.family != family
                || !RacingLineSnapshot.Range(sectorStart, 0, input.Length) || !RacingLineSnapshot.Range(sectorEnd, sectorStart + 0.1f, input.Length)))
                throw new ArgumentException("LINE_SECTOR: preserve requires the same sample mapping/family and a valid nonempty interval.");
            int n = input.Corridor.Length; offsets = new float[n]; target = new float[n]; trial = new float[n]; pinned = new bool[n];
            if (preserve != null)
                for (int i = 0; i < n; i++)
                    if (preserve.samples[i].occurrenceId != input.Corridor[i].occurrenceId || preserve.samples[i].laneId != input.Corridor[i].laneId
                        || Mathf.Abs(preserve.samples[i].station - input.Corridor[i].station) > 0.001f
                        || Vector3.Distance(preserve.samples[i].position - input.Corridor[i].left * preserve.samples[i].lateral, input.Corridor[i].position) > 0.01f)
                        throw new ArgumentException("LINE_SECTOR_MAPPING: the route geometry changed; regenerate the full line.");
            step = input.Settings.refinementStrength;
            float entryBendSide = -1;
            for (int i = 1; i < n - 1; i++)
                if (Mathf.Abs(Bend(i)) > 0.0001f) { entryBendSide = -Mathf.Sign(Bend(i)); break; }
            for (int i = 0; i < n; i++)
            {
                var c = input.Corridor[i]; float room = c.width * 0.5f - input.Dimensions.x * 0.5f - input.Settings.clearance;
                if (room < 0) Error("FOOTPRINT", "Vehicle plus clearance is wider than the corridor.", c.station);
                float bend = Bend(i); float side = Mathf.Abs(bend) < 0.0001f ? 0 : -Mathf.Sign(bend);
                // Pair identity follows the entry bend; do not make the cars exchange sides at every S-bend inflection.
                if (family == RacingLineFamily.Inside || family == RacingLineFamily.Outside || family == RacingLineFamily.SideBySide)
                    side = entryBendSide;
                float preference = family == RacingLineFamily.Inside ? 0.65f : family == RacingLineFamily.Outside ? -0.65f
                    : family == RacingLineFamily.SideBySide ? 0.35f : family == RacingLineFamily.Drift ? -0.25f : 0;
                target[i] = side * Mathf.Max(0, room) * preference;
                if (family == RacingLineFamily.SideBySide && side == 0) target[i] = Mathf.Max(0, room) * 0.35f;
                if (family == RacingLineFamily.RecoveryEntry)
                    target[i] = Mathf.Max(0, room) * 0.5f * (1 - Mathf.SmoothStep(0, 1, c.station / Mathf.Max(20, input.Dimensions.z * 5)));
                foreach (var hint in input.Hints)
                {
                    float distance = Mathf.Abs(c.station - hint.station);
                    if (distance > hint.radius || hint.kind == RacingHintKind.SpeedLimit || hint.kind == RacingHintKind.BrakeToDrift) continue;
                    float weight = hint.kind == RacingHintKind.Pin ? 1 : Mathf.SmoothStep(1, 0, distance / hint.radius);
                    target[i] = Mathf.Lerp(target[i], hint.lateral, weight);
                    if (hint.kind == RacingHintKind.Pin || distance < input.Settings.spacing * 0.5f) pinned[i] = true;
                    if (Mathf.Abs(hint.lateral) > room) Error("HINT_OUTSIDE", "Authored hint exceeds footprint clearance. Move the hint; the road was not widened.", hint.station);
                }
                if (!input.Closed && (i <= 1 || i >= n - 2)) pinned[i] = true;
                if (preserve != null && (c.station <= sectorStart + input.Settings.spacing * 2 || c.station >= sectorEnd - input.Settings.spacing * 2))
                { target[i] = preserve.samples[i].lateral; pinned[i] = true; }
                offsets[i] = Bound(i, target[i]);
            }
            if (input.Closed) offsets[n - 1] = offsets[0];
            baseline = bestCost = Cost(offsets);
            if (!input.Measured) Warn("SURROGATE", "Capability is an unmeasured assumption. Production rollouts are required; this is not a feasibility certificate.");
            if (family == RacingLineFamily.Drift) Warn("DRIFT_SURROGATE", "Drift uses conservative grip speeds and authored ordinary-input cues; engagement and recovery require measurement.");
        }

        public void Step(double milliseconds = 4)
        {
            if (done) return;
            if (milliseconds <= 0 || double.IsNaN(milliseconds)) throw new ArgumentOutOfRangeException(nameof(milliseconds));
            var slice = Stopwatch.StartNew(); total.Start();
            try
            {
                do
                {
                    if (finalSamples != null) { AdvanceFinalization(slice, milliseconds); continue; }
                    if (iteration >= input.Settings.iterations || step < 0.00001f || input.Settings.iterations == 0)
                    { BeginFinalization(step < 0.00001f ? "Refinement converged" : "Iteration budget"); continue; }
                    Refine(); iteration++;
                } while (!done && slice.Elapsed.TotalMilliseconds < milliseconds);
            }
            finally { total.Stop(); if (Result != null) Result.computeMilliseconds = total.Elapsed.TotalMilliseconds; }
        }
        public void Cancel() { if (done) return; cancelled = done = true; Result = null; total.Stop(); }
        public void Dispose() => Cancel();

        private void Refine()
        {
            int n = offsets.Length;
            Array.Copy(offsets, trial, n);
            for (int i = 0; i < n - (input.Closed ? 1 : 0); i++)
            {
                if (pinned[i] || !input.Closed && (i < 2 || i >= n - 2)) continue;
                Vector3 gradient = Position(i - 2, offsets) - 4 * Position(i - 1, offsets) + 6 * Position(i, offsets)
                    - 4 * Position(i + 1, offsets) + Position(i + 2, offsets);
                float adjustment = -step * (Vector3.Dot(gradient, input.Corridor[i].left) + 0.025f * (offsets[i] - target[i]));
                trial[i] = Bound(i, offsets[i] + Mathf.Clamp(adjustment, -0.15f, 0.15f));
            }
            if (input.Closed) trial[n - 1] = trial[0];
            float cost = Cost(trial);
            if (cost <= bestCost + 0.000001f) { Array.Copy(trial, offsets, n); bestCost = cost; }
            else step *= 0.5f;
        }
        private float Cost(float[] values)
        {
            float value = 0;
            for (int i = 1; i < values.Length - 1; i++)
            {
                Vector3 difference = Position(i - 1, values) - 2 * Position(i, values) + Position(i + 1, values);
                value += difference.sqrMagnitude + 0.025f * (values[i] - target[i]) * (values[i] - target[i]);
            }
            return value;
        }
        private int Wrap(int index)
        {
            int n = offsets.Length - 1;
            return input.Closed ? (index % n + n) % n : Mathf.Clamp(index, 0, n);
        }
        private Vector3 Position(int i, float[] values) { i = Wrap(i); return input.Corridor[i].position + input.Corridor[i].left * values[i]; }
        private float Bend(int i)
        {
            var a = input.Corridor[Wrap(i - 1)]; var b = input.Corridor[Wrap(i + 1)];
            return Vector3.Dot(Vector3.Cross(a.forward, b.forward), input.Corridor[i].up);
        }
        private float Bound(int index, float value)
        {
            var c = input.Corridor[index]; float half = input.Dimensions.x * 0.5f + input.Settings.clearance;
            float room = Mathf.Max(0, c.width * 0.5f - half); value = Mathf.Clamp(value, -room, room);
            foreach (var e in input.Exclusions)
            {
                if (c.station + input.Dimensions.z * 0.5f < e.start || c.station - input.Dimensions.z * 0.5f > e.end) continue;
                float low = e.minimumLateral - half, high = e.maximumLateral + half;
                if (value < low || value > high) continue;
                if (low >= -room && high <= room) value = Mathf.Abs(value - low) < Mathf.Abs(value - high) ? low - 0.01f : high + 0.01f;
                else if (low >= -room) value = low - 0.01f;
                else if (high <= room) value = high + 0.01f;
                else { Error("EXCLUSION_BLOCKS", "Excluded region leaves no footprint-sized passage.", c.station); return value; }
                value = Mathf.Clamp(value, -room, room);
            }
            return value;
        }

        private void BeginFinalization(string reason)
        {
            var samples = new RacingTrajectorySample[offsets.Length];
            finalSamples = samples; terminationReason = reason;
        }
        private void AdvanceFinalization(Stopwatch slice, double milliseconds)
        {
            var samples = finalSamples;
            while (geometryCursor < samples.Length)
            {
                int i = geometryCursor++;
                var c = input.Corridor[i]; Vector3 position = c.position + c.left * offsets[i];
                Vector3 before = Position(i - 1, offsets), after = Position(i + 1, offsets);
                Vector3 tangent = (after - before).normalized;
                if (!input.Closed && i == 0) before = position - (after - position);
                if (!input.Closed && i == samples.Length - 1) after = position + (position - before);
                Vector3 first = position - before, second = after - position;
                float a = first.magnitude, b = second.magnitude;
                if (a < 0.001f || b < 0.001f) Error("COINCIDENT", "A trajectory edge has zero length.", c.station);
                Vector3 curvature = 2 * Vector3.Cross(first, second) / Mathf.Max(0.0001f, a * b * (after - before).magnitude);
                float heading = Vector3.Angle(tangent, c.forward) * Mathf.Deg2Rad;
                float occupied = 0.5f * (input.Dimensions.x * Mathf.Abs(Mathf.Cos(heading)) + input.Dimensions.z * Mathf.Abs(Mathf.Sin(heading)));
                float clearance = c.width * 0.5f - Mathf.Abs(offsets[i]) - occupied;
                if (clearance < input.Settings.clearance - 0.02f) Error("SWEPT_WIDTH", "Heading and body footprint exceed available width.", c.station);
                foreach (var e in input.Exclusions)
                    if (c.station + input.Dimensions.z * 0.5f >= e.start && c.station - input.Dimensions.z * 0.5f <= e.end
                        && offsets[i] + occupied + input.Settings.clearance > e.minimumLateral
                        && offsets[i] - occupied - input.Settings.clearance < e.maximumLateral)
                        Error("EXCLUSION_FOOTPRINT", "Generated footprint intersects excluded occupancy; edit the corridor or hints.", c.station);
                if (Vector3.Angle(first, second) > 30) Error("HEADING_BREAK", "Heading changes by more than the 30 degree project tolerance between samples.", c.station);
                samples[i] = new RacingTrajectorySample { occurrenceId = c.occurrenceId, branchId = c.branchId, laneId = c.laneId,
                    station = c.station, roadStation = c.roadStation, position = position, tangent = tangent, normal = c.up,
                    lateral = offsets[i], curvature = Vector3.Dot(curvature, c.up), verticalCurvature = Vector3.Dot(curvature, c.left),
                    arcLength = i == 0 ? 0 : samples[i - 1].arcLength + Vector3.Distance(samples[i - 1].position, position), clearance = clearance };
                if (slice.Elapsed.TotalMilliseconds >= milliseconds) return;
            }
            while (sweepCursor < samples.Length)
            {
                int i = sweepCursor++;
                var a = samples[i]; var b = samples[Mathf.Min(i + 1, samples.Length - 1)];
                // Include intermediate poses; centre points alone miss front/rear corner encroachment.
                for (int part = 0; part <= 4; part++)
                {
                    float t = part * 0.25f; float along = Mathf.Lerp(a.station, b.station, t);
                    var orientation = Quaternion.LookRotation(Vector3.Slerp(a.tangent, b.tangent, t), Vector3.Slerp(a.normal, b.normal, t));
                    float clearance = RacingCorridorGeometry.FootprintClearance(input, Vector3.Lerp(a.position, b.position, t), orientation, along);
                    samples[i].clearance = Mathf.Min(samples[i].clearance, clearance);
                    if (clearance < input.Settings.clearance - 0.02f) Error("BODY_SWEEP", "Sampled body corners encroach on road edges or excluded occupancy.", along);
                }
                if (slice.Elapsed.TotalMilliseconds >= milliseconds) return;
            }
            PlanSpeed(samples);
            var cues = new List<RacingControlCue>();
            foreach (var h in input.Hints) if (h.kind == RacingHintKind.BrakeToDrift && family == RacingLineFamily.Drift)
                cues.Add(new RacingControlCue { hintId = h.id, station = h.station, seconds = h.cueSeconds, brake = h.brake, steering = h.cueSteering });
            cues.Sort((a, b) => a.station.CompareTo(b.station));
            if (family == RacingLineFamily.Drift && cues.Count == 0) Warn("DRIFT_NO_CUE", "Add a BrakeToDrift hint to test an actual initiation sequence.");
            if (preserved != null)
            {
                for (int i = 0; i < samples.Length; i++)
                    if (samples[i].station < sectorStart || samples[i].station > sectorEnd)
                    {
                        if (samples[i].targetSpeed < preserved.samples[i].targetSpeed - 0.05f)
                            Error("SECTOR_SPEED_BOUNDARY", "New sector requires braking outside the selected range. Expand the regeneration sector.", samples[i].station);
                        samples[i] = preserved.samples[i];
                    }
                RecomputeTime(samples);
            }
            Result = new RacingLineCandidate { id = input.SourceId + "." + family, family = family, fingerprint = input.Fingerprint,
                samples = samples, cues = cues.ToArray(), diagnostics = diagnostics.ToArray(), iterations = iteration,
                baselineCost = baseline, geometricCost = bestCost, estimatedSeconds = samples[samples.Length - 1].time,
                termination = terminationReason, state = RacingLineState.Generated };
            if (Result.HasErrors) Result.state = RacingLineState.Failed;
            done = true;
        }

        private void PlanSpeed(RacingTrajectorySample[] s)
        {
            int n = s.Length;
            for (int i = 0; i < n; i++)
            {
                var cap = CapabilityAt(input.Capability, input.Settings.maximumSpeed);
                foreach (var point in input.Capability) cap.lateralAcceleration = Mathf.Min(cap.lateralAcceleration, point.lateralAcceleration);
                float lateral = Lateral(cap, i);
                float speed = Mathf.Min(input.Settings.maximumSpeed, input.Corridor[i].speedLimit, input.Capability[input.Capability.Length - 1].speed);
                if (lateral <= 0) { Error("BANK_GRIP", "Gravity consumes the available lateral envelope.", s[i].station); speed = 0; }
                else if (Mathf.Abs(s[i].curvature) > 0.00001f) speed = Mathf.Min(speed, Mathf.Sqrt(lateral / Mathf.Abs(s[i].curvature)));
                if (Mathf.Abs(s[i].verticalCurvature) > 0.00001f)
                    speed = Mathf.Min(speed, Mathf.Sqrt(input.Settings.maximumVerticalAcceleration / Mathf.Abs(s[i].verticalCurvature)));
                foreach (var h in input.Hints) if (h.kind == RacingHintKind.SpeedLimit && Mathf.Abs(h.station - s[i].station) <= h.radius) speed = Mathf.Min(speed, h.speed);
                s[i].speedLimit = s[i].targetSpeed = speed;
            }
            s[0].targetSpeed = Mathf.Min(s[0].targetSpeed, input.Settings.entrySpeed);
            s[n - 1].targetSpeed = Mathf.Min(s[n - 1].targetSpeed, input.Closed ? input.Settings.entrySpeed : input.Settings.exitSpeed);
            Propagate(s);
            // Move deceleration caps upstream by the explicit response delay. Never raise a target.
            var original = (RacingTrajectorySample[])s.Clone();
            for (int i = 0; i < n - 1; i++)
            {
                float future = s[i].station + s[i].targetSpeed * input.Settings.brakingDelay;
                int j = i; while (j + 1 < n && original[j + 1].station < future) j++;
                if (j + 1 < n)
                {
                    float delayed = Mathf.Lerp(original[j].targetSpeed, original[j + 1].targetSpeed,
                        Mathf.InverseLerp(original[j].station, original[j + 1].station, future));
                    s[i].targetSpeed = Mathf.Min(s[i].targetSpeed, delayed);
                }
            }
            Propagate(s); // Tightening a downstream cap must preserve upstream braking feasibility.
            RecomputeTime(s);
        }
        private void Propagate(RacingTrajectorySample[] s)
        {
            int n = s.Length;
            // Speed-dependent combined-demand propagation is tightened to a fixed-point tolerance.
            bool converged = false;
            for (int pass = 0; pass < 64; pass++)
            {
                float change = 0;
                for (int i = 1; i < n; i++)
                {
                    float previous = s[i].targetSpeed;
                    float a = Longitudinal(s[i - 1], i - 1, false);
                    float reach = s[i - 1].targetSpeed * s[i - 1].targetSpeed + 2 * a * (s[i].arcLength - s[i - 1].arcLength);
                    if (reach < -0.01f) Error("GRADE_STALL", "Available acceleration cannot sustain forward travel on this grade.", s[i - 1].station);
                    s[i].targetSpeed = Mathf.Min(previous, Mathf.Sqrt(Mathf.Max(0, reach))); change = Mathf.Max(change, previous - s[i].targetSpeed);
                }
                for (int i = n - 2; i >= 0; i--)
                {
                    float previous = s[i].targetSpeed; float b = Longitudinal(s[i], i, true);
                    if (b < -0.001f) Error("BRAKING_GRADE", "Combined grip and downhill grade leave no usable braking margin.", s[i].station);
                    float reach = s[i + 1].targetSpeed * s[i + 1].targetSpeed + 2 * b * (s[i + 1].arcLength - s[i].arcLength);
                    s[i].targetSpeed = Mathf.Min(previous, Mathf.Sqrt(Mathf.Max(0, reach))); change = Mathf.Max(change, previous - s[i].targetSpeed);
                }
                if (change < 0.001f) { converged = true; break; }
            }
            if (!converged) Error("SPEED_CONVERGENCE", "Speed propagation exhausted its bounded iteration budget.");
        }
        private void RecomputeTime(RacingTrajectorySample[] s)
        {
            s[0].time = 0;
            for (int i = 1; i < s.Length; i++)
            {
                float sum = s[i].targetSpeed + s[i - 1].targetSpeed;
                if (sum < 0.01f) Error("ZERO_SPEED_SECTOR", "Consecutive zero-speed samples cannot be traversed.", s[i].station);
                s[i].time = s[i - 1].time + 2 * Vector3.Distance(s[i - 1].position, s[i].position) / Mathf.Max(0.01f, sum);
            }
        }
        private float Lateral(RacingCapabilityPoint cap, int i)
        {
            var c = input.Corridor[i];
            return cap.lateralAcceleration * input.Settings.gripSafety * c.grip * Mathf.Max(0, c.up.y)
                - Mathf.Abs(Vector3.Dot(input.Gravity, c.left));
        }
        private float Longitudinal(RacingTrajectorySample s, int i, bool braking)
        {
            var cap = CapabilityAt(input.Capability, s.targetSpeed); float lateral = Lateral(cap, i);
            float demand = s.targetSpeed * s.targetSpeed * Mathf.Abs(s.curvature);
            float physicalLateral = lateral / input.Settings.gripSafety;
            float ratio = physicalLateral > 0 ? demand / physicalLateral : 1;
            float combined = Mathf.Sqrt(Mathf.Max(0, 1 - ratio * ratio));
            float grade = -Vector3.Dot(input.Gravity, s.tangent);
            float grip = input.Corridor[i].grip * Mathf.Max(0, input.Corridor[i].up.y);
            return braking ? cap.braking * combined * grip + grade : cap.acceleration * combined * grip / (1 + input.ShiftSeconds) - grade;
        }
        public static RacingCapabilityPoint CapabilityAt(RacingCapabilityPoint[] points, float speed)
        {
            int i = 0; while (i + 1 < points.Length - 1 && points[i + 1].speed < speed) i++;
            var a = points[i]; var b = points[i + 1]; float t = Mathf.InverseLerp(a.speed, b.speed, speed);
            return new RacingCapabilityPoint { speed = speed, acceleration = Mathf.Lerp(a.acceleration, b.acceleration, t),
                braking = Mathf.Lerp(a.braking, b.braking, t), lateralAcceleration = Mathf.Lerp(a.lateralAcceleration, b.lateralAcceleration, t) };
        }
        private void Error(string code, string message, float station = 0) => Diagnostic(RacingDiagnosticSeverity.Error, code, message, station);
        private void Warn(string code, string message) => Diagnostic(RacingDiagnosticSeverity.Warning, code, message, 0);
        private void Diagnostic(RacingDiagnosticSeverity severity, string code, string message, float station)
        {
            if (diagnostics.Count >= 256 || diagnostics.Exists(d => d.code == code && Mathf.Abs(d.station - station) < input.Settings.spacing * 2)) return;
            diagnostics.Add(new RacingLineDiagnostic(severity, code, message, station));
        }
    }
}
