using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    // Cooperative main-thread owner of a disposable local physics world. Never simulates the default world.
    public sealed class RacingLineRollout : IDisposable
    {
        private readonly RacingLineSnapshot snapshot;
        private readonly RacingLineSource source;
        private readonly RacingVehicleSetup setup;
        private readonly RoadNetworkAsset network;
        private readonly RacingVerificationSettings limits;
        private readonly RacingLineCandidate candidate, companion;
        private readonly List<RacingTrialReport> reports = new List<RacingTrialReport>();
        private readonly List<RacingTelemetrySample> telemetry = new List<RacingTelemetrySample>();
        private readonly Queue<VehicleInputState>[] delay = { new Queue<VehicleInputState>(), new Queue<VehicleInputState>() };
        private readonly Collider[] overlaps = new Collider[64];
        private readonly Stopwatch cost = new Stopwatch();
        private readonly System.Random random;
        private Scene scene;
        private RacingVehicleRig[] rigs;
        private RacingLineTracker[] trackers;
        private RacingTrialReport trial;
        private int steps, saturation, samples, settling, delaySteps;
        private readonly int telemetryStride;
        private float speedSquare;
        private readonly float[] airborneStreak = new float[2], lastStation = new float[2], stuckSeconds = new float[2];
        private bool lastCollision, finished, disposed;
        public RacingVerificationReport Result { get; private set; }
        public event Action<int,int,float,Vector3,Vector3,float> VehicleStepped;
        private readonly Vector3[] previousPositions=new Vector3[2];
        public bool IsDone => finished;
        public bool Paused { get; set; }
        public int TrialIndex => reports.Count;
        public float Elapsed => trial?.elapsed ?? 0;
        public IReadOnlyList<RacingTelemetrySample> LiveTelemetry => telemetry;
        public string Fingerprint => snapshot.Fingerprint;
        public Scene PreviewScene => scene;

        public RacingLineRollout(RacingLineSource source, RacingLineCandidate candidate, RacingLineCandidate companion = null)
        {
            if (Application.isPlaying) throw new ArgumentException("LINE_ROLLOUT: exit Play mode; the editor owns this disposable simulation.");
            this.source = source; snapshot = RacingLineSnapshot.Capture(source); this.candidate = RacingLineSnapshot.Clone(candidate);
            this.companion = companion == null ? null : RacingLineSnapshot.Clone(companion);
            if (candidate == null || candidate.HasErrors || candidate.fingerprint != snapshot.Fingerprint
                || companion != null && (companion.HasErrors || companion.fingerprint != snapshot.Fingerprint))
                throw new ArgumentException("LINE_ROLLOUT_STALE: generate compatible, non-rejected candidates first.");
            setup = source.vehicle; network = source.route.network; limits = RacingLineSnapshot.Clone(source.verification);
            telemetryStride = Mathf.Max(5, Mathf.CeilToInt(limits.maximumSeconds / setup.fixedStep * limits.trials * (companion == null ? 1 : 2) / 50000));
            random = new System.Random(source.planner.seed);
            try { BeginTrial(); } catch { Dispose(); throw; }
        }

        public void Advance(double milliseconds = 4, bool singleStep = false)
        {
            if (finished || disposed || Paused && !singleStep) return;
            var slice = Stopwatch.StartNew(); cost.Start();
            try
            {
                if (RacingLineEditorOperations.Capture(source).Fingerprint != snapshot.Fingerprint)
                    throw new ArgumentException("LINE_ROLLOUT_STALE: dependencies changed between work slices; result discarded.");
                do { Tick(); if (singleStep) break; }
                while (!finished && slice.Elapsed.TotalMilliseconds < milliseconds);
            }
            catch { Dispose(); throw; }
            finally { cost.Stop(); if (Result != null) Result.computeMilliseconds = cost.Elapsed.TotalMilliseconds; }
        }

        private void BeginTrial()
        {
            CloseScene(); telemetry.Clear();
            scene = EditorSceneManager.NewPreviewScene();
            if (scene.GetPhysicsScene() == Physics.defaultPhysicsScene)
                throw new InvalidOperationException("LINE_PREVIEW_ISOLATION: this Editor did not allocate a local physics world.");
            trial = new RacingTrialReport { trial = reports.Count, minimumClearance = float.MaxValue,
                minimumCompanionSeparation = companion == null ? 0 : float.MaxValue,
                entrySpeed = Mathf.Max(0, candidate.samples[0].targetSpeed + Perturb(limits.entrySpeedPerturbation)),
                entryOffset = Perturb(limits.entryOffsetPerturbation), gripScale = 1 + Perturb(limits.gripPerturbation),
                reactionDelay = reports.Count == 0 ? 0 : (float)random.NextDouble() * limits.reactionDelayPerturbation };
            var corridorBounds = new Bounds(snapshot.Corridor[0].position, Vector3.zero);
            foreach (var sample in snapshot.Corridor)
            {
                float half = sample.width * 0.5f + snapshot.Dimensions.magnitude + Mathf.Max(10, limits.maximumLateralError * 4);
                corridorBounds.Encapsulate(sample.position + Vector3.one * half);
                corridorBounds.Encapsulate(sample.position - Vector3.one * half);
            }
            int chunkCount = 0; long vertices = 0;
            foreach (var chunk in network.Chunks)
            {
                if (!chunk.Collision || chunk.Mesh == null) continue;
                var bounds = chunk.Mesh.bounds; bounds.center += chunk.Origin;
                if (!bounds.Intersects(corridorBounds)) continue;
                vertices += chunk.Mesh.vertexCount;
                if (++chunkCount > 512 || vertices > 2000000)
                    throw new ArgumentException("LINE_COLLISION_BUDGET: selected route vicinity exceeds 512 collision chunks or 2 million vertices; validate shorter sectors.");
                var ground = new GameObject("Published road collision"); SceneManager.MoveGameObjectToScene(ground, scene);
                ground.transform.position = chunk.Origin; ground.AddComponent<MeshCollider>().sharedMesh = chunk.Mesh;
                ground.AddComponent<VehicleSurface>().Configure("Rollout road", (chunk.Surface == null ? 1 : chunk.Surface.grip) * trial.gripScale,
                    chunk.Surface == null ? 1 : chunk.Surface.rollingResistance);
            }
            if (scene.GetRootGameObjects().Length == 0) throw new ArgumentException("LINE_COLLISION_MISSING: road publication has no collision geometry.");
            rigs = new RacingVehicleRig[companion == null ? 1 : 2]; trackers = new RacingLineTracker[rigs.Length];
            for (int i = 0; i < rigs.Length; i++)
            {
                var line = i == 0 ? candidate : companion; var first = line.samples[0];
                var left = Vector3.Cross(first.tangent, first.normal).normalized;
                rigs[i] = new RacingVehicleRig(scene, setup, first.position + first.normal * RacingVehicleRig.SpawnHeight(setup)
                    + left * (i == 0 ? trial.entryOffset : -trial.entryOffset), Quaternion.LookRotation(first.tangent, first.normal));
                trackers[i] = new RacingLineTracker(new RacingTrajectoryReader(line), setup.controller, setup.wheelbase);
                delay[i].Clear(); rigs[i].Input.Current = new VehicleInputState { Handbrake = true };
            }
            steps = saturation = samples = 0; settling = Mathf.CeilToInt(1 / setup.fixedStep);
            speedSquare = 0; lastCollision = false;
            Array.Clear(airborneStreak, 0, 2); Array.Clear(lastStation, 0, 2); Array.Clear(stuckSeconds, 0, 2);
            delaySteps = Mathf.CeilToInt(trial.reactionDelay / setup.fixedStep);
            Physics.SyncTransforms();
            foreach (var rig in rigs)
                if (OverlapsObstacle(rig)) { trial.collisions++; lastCollision = true; break; }
        }
        private float Perturb(float range) => reports.Count == 0 ? 0 : ((float)random.NextDouble() * 2 - 1) * range;

        private void Tick()
        {
            float dt = setup.fixedStep;
            for (int i = 0; i < rigs.Length; i++)
            {
                var rig = rigs[i];
                previousPositions[i]=rig.Body.position;
                if (settling == 0)
                {
                    var input = trackers[i].Step(rig.Root.transform, rig.Body.linearVelocity, rig.Body.angularVelocity, rig.Vehicle.Tuning, dt);
                    delay[i].Enqueue(input);
                    rig.Input.Current = delay[i].Count > delaySteps ? delay[i].Dequeue() : VehicleInputState.Neutral;
                }
                rig.Vehicle.StepSimulation(dt);
            }
            scene.GetPhysicsScene().Simulate(dt); // Force/input update above is explicit; Simulate does not invoke FixedUpdate.
            if (settling > 0)
            {
                if (--settling == 0)
                    for (int i = 0; i < rigs.Length; i++)
                    {
                        // Initial-condition assignment only. No pose/velocity/yaw correction occurs during a measured rollout.
                        rigs[i].Body.linearVelocity = rigs[i].Root.transform.forward * trial.entrySpeed;
                        rigs[i].Assists.ResetHandling(); trackers[i].Reset();
                    }
                return;
            }
            steps++; trial.elapsed = steps * dt;
            for(int i=0;i<rigs.Length;i++)VehicleStepped?.Invoke(reports.Count,i,dt,previousPositions[i],rigs[i].Body.position,rigs[i].Body.linearVelocity.magnitude*3.6f);
            if (rigs.Length == 2) trial.minimumCompanionSeparation = Mathf.Min(trial.minimumCompanionSeparation, Separation(rigs[0], rigs[1]));
            bool collisionThisStep = false, allFinished = true;
            for (int i = 0; i < rigs.Length; i++)
            {
                var rig = rigs[i]; var tracker = trackers[i]; var body = rig.Body;
                var local = rig.Root.transform.InverseTransformDirection(body.linearVelocity);
                float slip = Mathf.Atan2(local.x, Mathf.Max(0.1f, Mathf.Abs(local.z))) * Mathf.Rad2Deg;
                int contacts = 0; foreach (var wheel in rig.Vehicle.Wheels) if (wheel.Grounded) contacts++;
                float clearance = RacingCorridorGeometry.FootprintClearance(snapshot, body.position, body.rotation, tracker.Station);
                var mode = rig.Assists.HandlingMode;
                trial.driftEngaged |= mode == VehicleHandlingMode.Drifting;
                trial.driftRecovered |= trial.driftEngaged && mode == VehicleHandlingMode.Grip;
                float actualSpeed = Mathf.Max(0, local.z); float error = actualSpeed - tracker.TargetSpeed;
                if (!RacingLineSnapshot.Finite(body.position) || !RacingLineSnapshot.Finite(actualSpeed))
                { EndTrial("NON_FINITE: physics state is invalid."); return; }
                trial.maximumLateralError = Mathf.Max(trial.maximumLateralError, Mathf.Abs(tracker.LateralError));
                trial.maximumSlipDegrees = Mathf.Max(trial.maximumSlipDegrees, Mathf.Abs(slip));
                trial.minimumClearance = Mathf.Min(trial.minimumClearance, clearance);
                if (clearance < snapshot.Settings.clearance - 0.02f) trial.boundaryViolations++;
                airborneStreak[i] = contacts < 2 ? airborneStreak[i] + dt : 0;
                trial.airborneSeconds = Mathf.Max(trial.airborneSeconds, airborneStreak[i]);
                if (tracker.Finished || tracker.Station > lastStation[i] + 0.5f) { lastStation[i] = tracker.Station; stuckSeconds[i] = 0; }
                else stuckSeconds[i] += dt;
                if (Mathf.Abs(rig.Input.Current.Steering) > 0.98f) saturation++;
                speedSquare += error * error; samples++;
                collisionThisStep |= OverlapsObstacle(rig);
                if (steps % telemetryStride == 0)
                    telemetry.Add(new RacingTelemetrySample { time = trial.elapsed, station = tracker.Station, targetSpeed = tracker.TargetSpeed,
                        actualSpeed = actualSpeed, lateralError = tracker.LateralError, headingError = tracker.HeadingError,
                        steering = rig.Input.Current.Steering, throttle = rig.Input.Current.Throttle, brake = rig.Input.Current.Brake,
                        slipDegrees = slip, yawRate = Vector3.Dot(body.angularVelocity, rig.Root.transform.up), assistTorque = rig.Assists.AppliedYawTorque,
                        contacts = contacts, clearance = clearance, handling = mode, position = body.position, vehicleIndex = i });
                allFinished &= tracker.Finished;
            }
            if (collisionThisStep && !lastCollision) trial.collisions++;
            lastCollision = collisionThisStep;
            if (allFinished) { trial.completed = true; EndTrial(null); }
            else if (trial.airborneSeconds > limits.maximumAirborneSeconds + 0.5f) EndTrial("AIRBORNE: sustained contact loss; inspect road collision and vertical speed envelope.");
            else if (trial.maximumLateralError > Mathf.Max(10, limits.maximumLateralError * 4)) EndTrial("TRACKING: vehicle left the local tracking corridor.");
            else if (stuckSeconds[0] > 12 || stuckSeconds[1] > 12) EndTrial("STALL: ordinary inputs failed to make route progress for a participating car.");
            else if (trial.elapsed >= limits.maximumSeconds) EndTrial("TIMEOUT: increase the explicit budget only after reviewing under-speed/control diagnostics.");
        }
        private bool OverlapsObstacle(RacingVehicleRig rig)
        {
            // EditMode simulation does not dispatch collision callbacks. Explicit local-world overlap evidence is used.
            var box = rig.Chassis;
            int count = scene.GetPhysicsScene().OverlapBox(box.transform.TransformPoint(box.center), box.size * 0.495f,
                overlaps, box.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) throw new InvalidOperationException("LINE_CONTACT_BUDGET: collision query saturated; cannot certify this run.");
            for (int i = 0; i < count; i++)
                if (overlaps[i] != null && overlaps[i].attachedRigidbody != rig.Body) return true;
            return false;
        }
        private static float Separation(RacingVehicleRig a, RacingVehicleRig b)
        {
            // SAT face/cross axes give a conservative separation lower bound, not Euclidean closest-point distance.
            Vector3 delta = b.Chassis.transform.TransformPoint(b.Chassis.center) - a.Chassis.transform.TransformPoint(a.Chassis.center);
            float separation = float.NegativeInfinity;
            for (int i = 0; i < 3; i++)
            {
                Vector3 axisA = Axis(a.Root.transform, i), axisB = Axis(b.Root.transform, i);
                Check(axisA); Check(axisB);
                for (int j = 0; j < 3; j++) Check(Vector3.Cross(axisA, Axis(b.Root.transform, j)));
            }
            return separation;
            void Check(Vector3 axis)
            {
                if (axis.sqrMagnitude < 0.00001f) return; axis.Normalize();
                float radius = 0;
                for (int k = 0; k < 3; k++)
                    radius += Mathf.Abs(Vector3.Dot(axis, Axis(a.Root.transform, k))) * a.Chassis.size[k] * 0.5f
                        + Mathf.Abs(Vector3.Dot(axis, Axis(b.Root.transform, k))) * b.Chassis.size[k] * 0.5f;
                separation = Mathf.Max(separation, Mathf.Abs(Vector3.Dot(delta, axis)) - radius);
            }
            Vector3 Axis(Transform pose, int index) => index == 0 ? pose.right : index == 1 ? pose.up : pose.forward;
        }
        private void EndTrial(string failure)
        {
            trial.rmsSpeedError = Mathf.Sqrt(speedSquare / Mathf.Max(1, samples)); trial.saturationFraction = (float)saturation / Mathf.Max(1, samples);
            var reasons = new List<string>(); if (failure != null) reasons.Add(failure);
            if (trial.collisions > 0) reasons.Add("COLLISION: chassis overlap recorded");
            if (trial.boundaryViolations > 0) reasons.Add("FOOTPRINT: measured clearance violated");
            if (trial.maximumLateralError > limits.maximumLateralError) reasons.Add("LATERAL: tracking error over tolerance");
            if (trial.rmsSpeedError > limits.maximumSpeedError) reasons.Add("SPEED: measured RMS error over tolerance");
            if (trial.airborneSeconds > limits.maximumAirborneSeconds) reasons.Add("CONTACT: airborne duration over tolerance");
            if (trial.saturationFraction > limits.maximumSaturationFraction) reasons.Add("CONTROL: steering saturation over tolerance");
            if (trial.maximumSlipDegrees > limits.maximumSlipDegrees) reasons.Add("SLIP: body slip over tolerance");
            if (candidate.family == RacingLineFamily.Drift && (!trial.driftEngaged || !trial.driftRecovered)) reasons.Add("DRIFT: initiation and recovery were not both measured");
            if (candidate.family != RacingLineFamily.Drift && trial.driftEngaged) reasons.Add("UNEXPECTED_DRIFT: grip candidate activated drifting");
            trial.passed = trial.completed && reasons.Count == 0;
            trial.diagnosis = trial.passed ? "PASS: measured within the configured entry envelope and thresholds." : string.Join("; ", reasons);
            trial.telemetry = telemetry.ToArray(); reports.Add(trial);
            if (reports.Count < limits.trials) { BeginTrial(); return; }
            Result = new RacingVerificationReport { candidateId = candidate.id, fingerprint = snapshot.Fingerprint,
                trajectoryFingerprint = candidate.GeometryFingerprint(),
                passed = reports.TrueForAll(r => r.passed), companionRun = companion != null, utc = DateTime.UtcNow.ToString("O"),
                engineVersion = Application.unityVersion, machine = SystemInfo.processorType + "; " + SystemInfo.systemMemorySize + " MB",
                controllerRevision = RacingLineTracker.Revision, trials = reports.ToArray() };
            Result.telemetryIntervalSeconds = telemetryStride * setup.fixedStep;
            finished = true; CloseScene();
        }
        private void CloseScene()
        {
            if (rigs != null) { foreach (var rig in rigs) rig?.Dispose(); rigs = null; }
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.ClosePreviewScene(scene);
            scene = default;
        }
        public void Dispose() { if (disposed) return; disposed = true; CloseScene(); }
    }
}
