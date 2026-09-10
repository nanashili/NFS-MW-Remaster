using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    // Detached computation input: no ScriptableObjects are touched by the planner.
    public sealed class RacingLineSnapshot
    {
        public string SourceId { get; }
        public string Fingerprint { get; }
        public string VehicleFingerprint { get; }
        public RacingCorridorSample[] Corridor { get; }
        public RacingPlannerSettings Settings { get; }
        public RacingLineHint[] Hints { get; }
        public RacingLineExclusion[] Exclusions { get; }
        public RacingCapabilityPoint[] Capability { get; }
        public Vector3 Dimensions { get; }
        public bool Closed { get; }
        public bool Measured { get; }
        public float ShiftSeconds { get; }
        public Vector3 Gravity { get; }
        public RacingVerificationSettings Verification { get; }
        public float Length => Corridor[Corridor.Length - 1].station;

        private RacingLineSnapshot(RacingLineSource source, RacingCorridorSample[] corridor, string fingerprint,
            string vehicleFingerprint, float shift)
        {
            SourceId = source.id; Fingerprint = fingerprint; VehicleFingerprint = vehicleFingerprint;
            Corridor = corridor; Settings = Clone(source.planner); Hints = CloneArray(source.hints);
            Exclusions = CloneArray(source.exclusions); Capability = (RacingCapabilityPoint[])source.capability.points.Clone();
            Dimensions = source.vehicle.dimensions; Closed = source.route.closed;
            Measured = source.capability.longitudinalMeasured; ShiftSeconds = shift;
            Gravity = Physics.gravity; Verification = Clone(source.verification);
        }

        public static RacingLineSnapshot Capture(RacingLineSource source)
        {
            if (source == null || source.schema != RacingLineSource.CurrentSchema || !Identity(source.id))
                throw new ArgumentException("LINE_SCHEMA: select a current studio document; migrate legacy documents explicitly.");
            var route = source.route; var setup = source.vehicle; var p = source.planner; var cap = source.capability;
            if (route == null || route.schema != RacingLineRoute.CurrentSchema || !Identity(route.id) || route.network == null
                || route.network.SchemaVersion != RoadNetworkAsset.CurrentSchema || route.spans == null || route.spans.Length == 0 || route.spans.Length > 256)
                throw new ArgumentException("LINE_ROUTE: a published road network and ordered legal lane occurrences are required.");
            ValidateSetup(setup);
            if (p == null || !Finite(p.spacing) || p.spacing < 0.5f || p.spacing > 10 || p.maximumSamples < 3 || p.maximumSamples > 4096
                || p.iterations < 0 || p.iterations > 300 || !Range(p.refinementStrength, 0, 1) || !Range(p.clearance, 0, 10)
                || !Range(p.maximumSpeed, 1, 150) || !Range(p.entrySpeed, 0, p.maximumSpeed) || !Range(p.exitSpeed, 0, p.maximumSpeed)
                || !Range(p.gripSafety, 0.1f, 1) || !Range(p.brakingDelay, 0, 2) || !Range(p.maximumVerticalAcceleration, 0.1f, 20))
                throw new ArgumentException("LINE_SETTINGS: invalid spacing, sample budget, speed, clearance or iteration limits.");
            if (cap == null || cap.schema != 1 || cap.points == null || cap.points.Length < 2 || cap.points.Length > 256)
                throw new ArgumentException("LINE_CAPABILITY: select a supported capability profile with at least two speed samples.");
            for (int i = 0; i < cap.points.Length; i++)
            {
                var c = cap.points[i];
                if (!Range(c.speed, 0, 150) || i > 0 && c.speed <= cap.points[i - 1].speed || !Range(c.acceleration, 0.01f, 30)
                    || !Range(c.braking, 0.1f, 30) || !Range(c.lateralAcceleration, 0.1f, 30))
                    throw new ArgumentException("LINE_CAPABILITY: speeds must increase; finite positive acceleration/braking/lateral limits are required.");
            }
            if (cap.points[0].speed != 0) throw new ArgumentException("LINE_CAPABILITY: the profile must cover standstill.");
            using var digest = new RacingDigest();
            digest.Add("racing-line-planner.3"); digest.Add(source.id); digest.Add(route.id); digest.Add(route.closed ? "closed" : "open");
            digest.Add(RacingVerificationReport.Revision);
            digest.Add(route.network.NetworkId.ToString()); digest.Add(route.network.Fingerprint);
            var lanes = new Dictionary<string, RoadBakedLane>(StringComparer.Ordinal);
            foreach (var lane in route.network.Lanes)
            {
                if (lane == null || !lane.Id.IsValid || lanes.ContainsKey(lane.Id.ToString())) throw new ArgumentException("LINE_LANE_ID: duplicate/missing lane identity.");
                lanes.Add(lane.Id.ToString(), lane);
            }
            var list = new List<RacingCorridorSample>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            RoadBakedLane previous = null; float previousEnd = 0, along = 0;
            for (int occurrence = 0; occurrence < route.spans.Length; occurrence++)
            {
                var span = route.spans[occurrence];
                if (span == null || !Identity(span.id) || !ids.Add(span.id) || string.IsNullOrWhiteSpace(span.branch)
                    || string.IsNullOrEmpty(span.laneId) || !lanes.TryGetValue(span.laneId, out var lane))
                    throw new ArgumentException("LINE_SPAN: missing/duplicate occurrence or unknown lane ID. Remap deliberately in the route inspector.");
                float end = span.endMetres == -1 ? lane.Length : span.endMetres;
                if (!Range(span.startMetres, 0, lane.Length) || !Range(end, span.startMetres + 0.1f, lane.Length))
                    throw new ArgumentException("LINE_SPAN_RANGE: start/end must be an increasing legal lane interval.");
                if (previous != null) CheckJoin(previous, previousEnd, lane, span.startMetres);
                digest.Add(JsonUtility.ToJson(span));
                float length = end - span.startMetres;
                int count = Mathf.Max(1, Mathf.CeilToInt(length / p.spacing));
                if ((long)list.Count + count + 1 > p.maximumSamples) throw new ArgumentException("LINE_SAMPLE_BUDGET: increase spacing or explicitly raise the bounded sample budget.");
                for (int i = 0; i <= count; i++)
                {
                    if (occurrence > 0 && i == 0) continue;
                    float distance = span.startMetres + length * i / count;
                    var s = lane.Sample(distance);
                    var sample = new RacingCorridorSample { occurrenceId = span.id, branchId = span.branch, laneId = span.laneId,
                        station = along + distance - span.startMetres, roadStation = s.station, position = s.position, forward = s.forward,
                        up = s.up, left = s.left, width = s.width, speedLimit = lane.Speed,
                        grip = lane.Surface == null ? 1 : lane.Surface.grip };
                    if (!Finite(sample.position) || !Finite(sample.forward) || !Finite(sample.up) || !Finite(sample.left)
                        || sample.forward.sqrMagnitude < 0.9f || sample.up.sqrMagnitude < 0.9f || sample.left.sqrMagnitude < 0.9f
                        || Mathf.Abs(Vector3.Dot(sample.forward, sample.up)) > 0.15f || !Range(sample.width, 0.1f, 200)
                        || !Range(sample.grip, 0.05f, 5) || !Range(sample.speedLimit, 0.1f, 150))
                        throw new ArgumentException("LINE_CORRIDOR: invalid 3D lane frame, surface or dimensions.");
                    list.Add(sample); digest.Add(JsonUtility.ToJson(sample));
                }
                previous = lane; previousEnd = end; along += length;
            }
            if (list.Count < 3) throw new ArgumentException("LINE_ROUTE_SHORT: at least three distinct samples are required.");
            if (route.closed)
            {
                var first = route.spans[0]; CheckJoin(previous, previousEnd, lanes[first.laneId], first.startMetres);
                if (Vector3.Distance(list[0].position, list[list.Count - 1].position) > 0.06f)
                    throw new ArgumentException("LINE_CLOSED: closed route seam does not meet.");
            }
            if (source.hints == null || source.exclusions == null || source.hints.Length > 256 || source.exclusions.Length > 128)
                throw new ArgumentException("LINE_HINTS: require non-null collections, at most 256 hints and 128 exclusions.");
            ids.Clear();
            foreach (var h in source.hints)
            {
                if (h == null || !Identity(h.id) || !ids.Add(h.id) || !Enum.IsDefined(typeof(RacingHintKind), h.kind)
                    || !Range(h.station, 0, along) || !Range(h.radius, 0.1f, along + 1) || !Finite(h.lateral)
                    || !Range(h.speed, 0, 150) || !Range(h.brake, 0, 1) || !Range(h.cueSeconds, 0.02f, 2) || !Range(h.cueSteering, -1, 1))
                    throw new ArgumentException("LINE_HINT: invalid identity, station, radius, speed or control cue.");
                digest.Add(JsonUtility.ToJson(h));
            }
            foreach (var e in source.exclusions)
            {
                if (e == null || !Identity(e.id) || !ids.Add(e.id) || !Range(e.start, 0, along) || !Range(e.end, e.start, along)
                    || !Finite(e.minimumLateral) || !Finite(e.maximumLateral) || e.minimumLateral >= e.maximumLateral)
                    throw new ArgumentException("LINE_EXCLUSION: invalid interval or duplicate identity.");
                digest.Add(JsonUtility.ToJson(e));
            }
            ValidateVerification(source.verification);
            string vehicleFingerprint = VehicleFingerprintOf(setup);
            if (!string.IsNullOrEmpty(cap.vehicleFingerprint) && cap.vehicleFingerprint != vehicleFingerprint)
                throw new ArgumentException("LINE_CAPABILITY_STALE: tuning, rig, upgrades, controller or fixed step changed. Recalibrate for this setup.");
            if (cap.longitudinalMeasured && cap.vehicleFingerprint != vehicleFingerprint)
                throw new ArgumentException("LINE_CAPABILITY_PROVENANCE: measured data must identify the exact vehicle setup.");
            digest.Add(vehicleFingerprint); digest.Add(JsonUtility.ToJson(p)); digest.Add(JsonUtility.ToJson(source.verification));
            digest.Add(JsonUtility.ToJson(cap));
            using var tuning = new RacingTuningLease(setup.CreateEffectiveTuning());
            return new RacingLineSnapshot(source, list.ToArray(), digest.Finish(), vehicleFingerprint, tuning.Value.engine.shiftDuration);
        }

        public static string VehicleFingerprintOf(RacingVehicleSetup setup)
        {
            ValidateSetup(setup);
            using var tuning = new RacingTuningLease(setup.CreateEffectiveTuning());
            using var digest = new RacingDigest();
            digest.Add(setup.id); digest.Add(VehicleController.SimulationRevision); digest.Add(RacingLineTracker.Revision);
            if (setup.definition != null) { digest.Add(setup.definition.vehicleId); digest.Add(setup.definition.variantId); }
            digest.Add(Application.unityVersion); digest.Add(JsonUtility.ToJson(tuning.Value));
            digest.Add(SystemInfo.operatingSystemFamily.ToString());
            digest.Add(setup.dimensions.ToString("R", CultureInfo.InvariantCulture)); digest.Add(setup.wheelbase); digest.Add(setup.trackWidth);
            digest.Add(setup.fixedStep); digest.Add(JsonUtility.ToJson(setup.controller));
            digest.Add(Physics.gravity.ToString("R", CultureInfo.InvariantCulture));
            digest.Add(Physics.defaultSolverIterations.ToString(CultureInfo.InvariantCulture));
            digest.Add(Physics.defaultSolverVelocityIterations.ToString(CultureInfo.InvariantCulture));
            digest.Add(Physics.defaultContactOffset);
            digest.Add(Physics.defaultMaxDepenetrationVelocity); digest.Add(Physics.sleepThreshold);
            digest.Add(Physics.GetIgnoreLayerCollision(0, 2) ? "ground-ignored" : "ground-collides");
            digest.Add(Physics.GetIgnoreLayerCollision(2, 2) ? "vehicles-ignored" : "vehicles-collide");
            foreach (var upgrade in setup.upgrades) { digest.Add(upgrade.UpgradeId); digest.Add(upgrade.GetType().FullName); digest.Add(JsonUtility.ToJson(upgrade)); }
            foreach (var part in setup.bodyParts ?? Array.Empty<VehicleCustomizationDefinition>())
            { digest.Add(part.CustomizationId); digest.Add(JsonUtility.ToJson(part)); }
            if (setup.physicsPrefab != null)
            {
                RacingVehicleRig.ValidatePrefab(setup.physicsPrefab, setup.dimensions);
                var body = setup.physicsPrefab.GetComponent<Rigidbody>();
                digest.Add(setup.physicsPrefab.UsesBuiltInAero ? "builtin-aero" : "no-builtin-aero");
                digest.Add(body.useGravity ? "gravity" : "no-gravity"); digest.Add(body.isKinematic ? "kinematic" : "dynamic");
                digest.Add(((int)body.constraints).ToString(CultureInfo.InvariantCulture));
                digest.Add(body.solverIterations.ToString(CultureInfo.InvariantCulture));
                digest.Add(body.solverVelocityIterations.ToString(CultureInfo.InvariantCulture));
                digest.Add(body.maxDepenetrationVelocity); digest.Add(body.sleepThreshold);
                foreach (var wheel in setup.physicsPrefab.GetComponentsInChildren<VehicleWheel>(true))
                {
                    digest.Add(setup.physicsPrefab.transform.InverseTransformPoint(wheel.transform.position).ToString("R", CultureInfo.InvariantCulture));
                    digest.Add(wheel.IsSteeringWheel ? "steer" : "fixed"); digest.Add(wheel.IsDrivenWheel ? "drive" : "idle");
                    digest.Add(wheel.IsHandbrakeWheel ? "handbrake" : "service"); digest.Add(wheel.Axle.ToString());
                    digest.Add(setup.physicsPrefab.transform.InverseTransformDirection(wheel.transform.up).ToString("R", CultureInfo.InvariantCulture));
                }
                foreach (var box in setup.physicsPrefab.GetComponentsInChildren<BoxCollider>(true))
                { digest.Add(box.center.ToString("R", CultureInfo.InvariantCulture)); digest.Add(box.size.ToString("R", CultureInfo.InvariantCulture)); digest.Add(box.contactOffset); }
            }
            else digest.Add("synthetic-four-wheel-rig.v1");
            return digest.Finish();
        }

        public static void ValidateSetup(RacingVehicleSetup s)
        {
            if (s == null || !Identity(s.id) || (s.tuning == null && s.definition == null) || !Finite(s.dimensions) || s.dimensions.x < 0.5f || s.dimensions.y < 0.2f
                || s.dimensions.z < 1 || !Range(s.wheelbase, 0.5f, s.dimensions.z) || !Range(s.trackWidth, 0.5f, s.dimensions.x)
                || !Range(s.fixedStep, 0.005f, 0.05f) || s.controller == null || !Range(s.controller.minimumLookahead, 1, 100)
                || !Range(s.controller.lookaheadSeconds, 0, 3) || !Range(s.controller.speedGain, 0.01f, 10)
                || !Range(s.controller.integralGain, 0, 10) || !Range(s.controller.yawDamping, 0, 1))
                throw new ArgumentException("LINE_SETUP: invalid vehicle identity, dimensions, timing or controller settings.");
            if (s.upgrades == null || s.upgrades.Length > 64 || Array.Exists(s.upgrades, u => u == null) || !Finite(Physics.gravity) || Physics.gravity.y >= -0.1f
                || Mathf.Abs(Physics.gravity.x) > 0.001f || Mathf.Abs(Physics.gravity.z) > 0.001f)
                throw new ArgumentException("LINE_SETUP: upgrades must exist and the ground-vehicle planner requires downward world gravity.");
            ValidateTuning(s.definition != null ? s.definition.factoryTuning : s.tuning);
        }
        public static void ValidateTuning(VehicleTuning tuning)
        {
            if (tuning == null || tuning.chassis == null || tuning.engine == null || tuning.tires == null || tuning.controls == null
                || tuning.assists == null || tuning.handling == null || tuning.aero == null)
                throw new ArgumentException("LINE_TUNING: every shared tuning section must exist.");
            // Data-only settings have primitive fields; validate all floats, including newly added settings, before physics sees them.
            if (!Enum.IsDefined(typeof(VehicleSimulationModel), tuning.simulationModel))
                throw new ArgumentException("LINE_TUNING: unknown vehicle simulation model.");
            if (tuning.UsesMostWantedReference && tuning.mostWanted == null)
                throw new ArgumentException("MW_TUNING: reference settings are missing.");
            var sections = new List<object> { tuning.chassis, tuning.engine, tuning.tires, tuning.controls, tuning.assists, tuning.handling, tuning.aero };
            if (tuning.UsesMostWantedReference) sections.Add(tuning.mostWanted);
            foreach (var section in sections)
                foreach (var field in section.GetType().GetFields())
                {
                    object value = field.GetValue(section);
                    if (value is float scalar && !Finite(scalar) || value is Vector3 vector && !Finite(vector)
                        || field.FieldType == typeof(float[]) && (value is not float[] array || Array.Exists(array, v => !Finite(v))))
                        throw new ArgumentException("LINE_TUNING: non-finite or missing field " + field.Name + ".");
                    if (value is float number)
                    {
                        var minimum = (MinAttribute)Attribute.GetCustomAttribute(field, typeof(MinAttribute));
                        var range = (RangeAttribute)Attribute.GetCustomAttribute(field, typeof(RangeAttribute));
                        if (Mathf.Abs(number) > 10000000 || minimum != null && number < minimum.min || range != null && !Range(number, range.min, range.max))
                            throw new ArgumentException("LINE_TUNING: field " + field.Name + " exceeds the shared setting's supported range.");
                    }
                }
            if (tuning.chassis.mass < 100 || tuning.engine.gearRatios.Length == 0 || tuning.tires.wheelRadius < 0.05f
                || tuning.tires.suspensionRestLength < 0.05f || tuning.tires.wheelMass <= 0 || tuning.tires.springRate <= 0
                || tuning.controls.maxSteerAngle <= 0 || tuning.controls.serviceBrakeTorque <= 0)
                throw new ArgumentException("LINE_TUNING: invalid mass, gears, wheel, suspension or control dimensions.");
            if (tuning.UsesMostWantedReference) tuning.mostWanted.Validate(tuning);
        }
        public static void ValidateVerification(RacingVerificationSettings v)
        {
            if (v == null || v.trials < 1 || v.trials > 25 || !Range(v.maximumSeconds, 1, 600) || !Range(v.entrySpeedPerturbation, 0, 10)
                || !Range(v.entryOffsetPerturbation, 0, 3) || !Range(v.gripPerturbation, 0, 0.3f) || !Range(v.reactionDelayPerturbation, 0, 1)
                || !Range(v.maximumLateralError, 0.05f, 10) || !Range(v.maximumSpeedError, 0.1f, 20) || !Range(v.maximumAirborneSeconds, 0, 2)
                || !Range(v.maximumSaturationFraction, 0, 1) || !Range(v.maximumSlipDegrees, 0, 90))
                throw new ArgumentException("LINE_VERIFICATION: invalid robustness bounds or trial budget.");
        }
        private static void CheckJoin(RoadBakedLane from, float end, RoadBakedLane to, float start)
        {
            bool continuation = from.Id == to.Id && Mathf.Abs(end - start) < 0.01f;
            bool successor = false; foreach (var id in from.Successors) if (id == to.Id) successor = true;
            if (!continuation && (!successor || Mathf.Abs(end - from.Length) > 0.01f || start > 0.01f))
                throw new ArgumentException("LINE_TOPOLOGY: consecutive occurrences are not a declared successor or contiguous same-lane span.");
            var a = from.Sample(end); var b = to.Sample(start);
            if (Vector3.Distance(a.position, b.position) > 0.06f || Vector3.Dot(a.forward, b.forward) < 0.9f)
                throw new ArgumentException("LINE_JOIN: disconnected or sharply opposed corridor ends; author a legal transition road.");
        }
        public static T Clone<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        private static T[] CloneArray<T>(T[] values) { var copy = new T[values.Length]; for (int i = 0; i < copy.Length; i++) copy[i] = Clone(values[i]); return copy; }
        public static bool Identity(string value) => Guid.TryParseExact(value, "N", out _);
        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        public static bool Range(float value, float minimum, float maximum) => Finite(value) && value >= minimum && value <= maximum;
    }

    public sealed class RacingDigest : IDisposable
    {
        private readonly SHA256 hash = SHA256.Create();
        public void Add(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "<null>");
            byte[] count = BitConverter.GetBytes(bytes.Length);
            if (!BitConverter.IsLittleEndian) Array.Reverse(count);
            hash.TransformBlock(count, 0, count.Length, null, 0);
            hash.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        public void Add(float value) => Add(value.ToString("R", CultureInfo.InvariantCulture));
        public string Finish() { hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0); return BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant(); }
        public void Dispose() => hash.Dispose();
    }

    public sealed class RacingTuningLease : IDisposable
    {
        public VehicleTuning Value { get; }
        public RacingTuningLease(VehicleTuning value) { Value = value; }
        public void Dispose() { if (Value == null) return; if (Application.isPlaying) UnityEngine.Object.Destroy(Value); else UnityEngine.Object.DestroyImmediate(Value); }
    }
}
