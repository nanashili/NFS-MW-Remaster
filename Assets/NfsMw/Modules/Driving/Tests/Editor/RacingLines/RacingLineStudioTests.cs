using System;
using System.Collections.Generic;
using System.Linq;
using NfsMwRemaster.Driving.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class RacingLineStudioTests
    {
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private RacingLineSource source;
        private T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
        [SetUp] public void SetUp()
        {
            source = Own(ScriptableObject.CreateInstance<RacingLineSource>());
            source.vehicle = Own(ScriptableObject.CreateInstance<RacingVehicleSetup>());
            source.vehicle.tuning = Own(VehicleTuning.CreateStreetRacer()); source.vehicle.tuning.handling.brakeToDrift = false;
            source.capability = Own(ScriptableObject.CreateInstance<RacingCapabilityProfile>());
            source.route = Own(ScriptableObject.CreateInstance<RacingLineRoute>());
            source.planner.maximumSpeed = 10; source.verification.trials = 1;
            SetRoad(10, false);
        }
        [TearDown] public void TearDown()
        {
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }
        private void SetRoad(float width, bool curved, float elevation = 0)
        {
            int count = 65; var samples = new RoadLaneSample[count]; var vertices = new Vector3[count * 2]; var triangles = new int[(count - 1) * 6];
            float along = 0;
            for (int i = 0; i < count; i++)
            {
                float angle = i / (float)(count - 1) * Mathf.PI * 0.5f;
                Vector3 position = curved ? new Vector3(50 * (1 - Mathf.Cos(angle)), elevation, 50 * Mathf.Sin(angle)) : new Vector3(0, elevation, i * 2);
                Vector3 forward = curved ? new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) : Vector3.forward;
                Vector3 left = Vector3.Cross(forward, Vector3.up);
                if (i > 0) along += Vector3.Distance(position, samples[i - 1].position);
                samples[i] = new RoadLaneSample { station = along, distance = along, width = width, position = position, forward = forward, left = left, up = Vector3.up };
                vertices[i * 2] = position + left * width * 0.5f; vertices[i * 2 + 1] = position - left * width * 0.5f;
                if (i > 0)
                {
                    int index = (i - 1) * 6, previous = (i - 1) * 2;
                    triangles[index] = previous; triangles[index + 1] = previous + 2; triangles[index + 2] = previous + 1;
                    triangles[index + 3] = previous + 1; triangles[index + 4] = previous + 2; triangles[index + 5] = previous + 3;
                }
            }
            var mesh = Own(new Mesh { vertices = vertices, triangles = triangles }); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var laneId = RoadId.New(); var roadId = RoadId.New();
            var network = Own(ScriptableObject.CreateInstance<RoadNetworkAsset>());
            network.Initialize(RoadId.New(), "synthetic-test-" + elevation, new[] { new RoadBakedLane(laneId, roadId, RoadClass.Local, 30, samples, Array.Empty<RoadId>(), null) },
                new[] { new RoadBakedChunk(roadId, laneId, mesh, Vector3.zero, null, null, true) });
            source.route.network = network; source.route.spans = new[] { new RacingRouteSpan { laneId = laneId.ToString(), startMetres = 5, endMetres = along - 5 } };
        }
        private RacingLineCandidate Generate(RacingLineFamily family = RacingLineFamily.Grip)
        {
            using var planner = new RacingLinePlanner(RacingLineSnapshot.Capture(source), family);
            int slices = 0; while (!planner.IsDone && slices++ < 500) planner.Step(4);
            Assert.That(planner.IsDone, Is.True); return planner.Result;
        }
        private RacingVerificationReport ContractReport(RacingLineCandidate candidate)
            => new RacingVerificationReport { fingerprint = candidate.fingerprint, candidateId = candidate.id, passed = true,
                trajectoryFingerprint = candidate.GeometryFingerprint(),
                engineVersion = Application.unityVersion, controllerRevision = RacingLineTracker.Revision,
                trials = new[] { new RacingTrialReport { completed = true, passed = true } } };

        [Test] public void RebuildIsDeterministicAndDoesNotMutateSource()
        {
            string before = JsonUtility.ToJson(source); var a = Generate(); var b = Generate();
            a.computeMilliseconds = b.computeMilliseconds = 0;
            Assert.That(JsonUtility.ToJson(a), Is.EqualTo(JsonUtility.ToJson(b)));
            Assert.That(JsonUtility.ToJson(source), Is.EqualTo(before)); Assert.That(a.HasErrors, Is.False);
            Assert.That(a.geometricCost, Is.LessThanOrEqualTo(a.baselineCost + 0.0001f));
        }
        [Test] public void ThreeDimensionalFrameRetainsElevatedRoad()
        {
            SetRoad(10, true, 25); var candidate = Generate(); Assert.That(candidate.HasErrors, Is.False);
            Assert.That(candidate.samples.All(s => Mathf.Abs(s.position.y - 25) < 0.001f), Is.True);
            Assert.That(candidate.samples.Any(s => Mathf.Abs(s.curvature) > 0.01f), Is.True);
        }
        [Test] public void MissingOrDuplicateOccurrenceFailsWithActionableError()
        {
            source.route.spans = new[] { source.route.spans[0], source.route.spans[0] };
            Assert.That(() => RacingLineSnapshot.Capture(source), Throws.ArgumentException.With.Message.Contains("LINE_SPAN"));
        }
        [Test] public void NonFiniteCapabilityIsRejected()
        {
            source.capability.points[1].braking = float.NaN;
            Assert.That(() => RacingLineSnapshot.Capture(source), Throws.ArgumentException.With.Message.Contains("LINE_CAPABILITY"));
        }
        [Test] public void NonFiniteSharedTuningIsRejectedBeforeSimulation()
        {
            source.vehicle.tuning.handling.yawGain = float.NaN;
            Assert.That(() => RacingLineSnapshot.Capture(source), Throws.ArgumentException.With.Message.Contains("LINE_TUNING"));
        }
        [Test] public void InsideOutsidePairKeepsDistinctEntryFootprints()
        {
            SetRoad(14, true); var a = Generate(RacingLineFamily.Inside); var b = Generate(RacingLineFamily.Outside);
            Assert.That(a.HasErrors || b.HasErrors, Is.False);
            Assert.That(Vector3.Distance(a.samples[0].position, b.samples[0].position), Is.GreaterThan(source.vehicle.dimensions.x * 2));
            using var run = new RacingLineRollout(source, a, b); while (!run.IsDone) run.Advance(8);
            Assert.That(run.Result.companionRun, Is.True);
            Assert.That(run.Result.trials[0].telemetry.Any(s => s.vehicleIndex == 1), Is.True);
            Assert.That(run.Result.trials[0].collisions, Is.Zero, run.Result.trials[0].diagnosis);
            Assert.That(run.Result.passed, Is.True, run.Result.trials[0].diagnosis);
        }
        [Test] public void InitiallyOverlappingCarsCannotPassAnOccupancyTest()
        {
            var line = Generate(); using var run = new RacingLineRollout(source, line, line);
            while (!run.IsDone) run.Advance(8);
            Assert.That(run.Result.passed, Is.False); Assert.That(run.Result.trials[0].collisions, Is.GreaterThan(0));
        }
        [Test] public void VehicleWiderThanLaneIsRejectedNotClampedIntoValidity()
        {
            SetRoad(1.5f, false); var result = Generate();
            Assert.That(result.HasErrors, Is.True); Assert.That(result.diagnostics.Any(d => d.code == "FOOTPRINT"), Is.True);
        }
        [Test] public void ExcludedOccupancyCannotBePublishedAsFreeSpace()
        {
            source.exclusions = new[] { new RacingLineExclusion { start = 30, end = 50, minimumLateral = -5, maximumLateral = 5 } };
            Assert.That(Generate(RacingLineFamily.TrafficBypass).HasErrors, Is.True);
        }
        [Test] public void PinOutsideRoadRemainsAuthoredAndProducesDiagnostic()
        {
            source.hints = new[] { new RacingLineHint { kind = RacingHintKind.Pin, station = 50, lateral = 50 } };
            Assert.That(Generate().diagnostics.Any(d => d.code == "HINT_OUTSIDE"), Is.True);
            Assert.That(source.hints[0].lateral, Is.EqualTo(50));
        }
        [Test] public void BrakingPropagatesBeforeSpeedLimitedSectorAndHonorsAcceleration()
        {
            source.planner.maximumSpeed = 20;
            source.hints = new[] { new RacingLineHint { kind = RacingHintKind.SpeedLimit, station = 90, radius = 5, speed = 3 } };
            var result = Generate(); Assert.That(result.HasErrors, Is.False);
            var samples = result.samples;
            for (int i = 1; i < samples.Length; i++)
            {
                float distance = samples[i].arcLength - samples[i - 1].arcLength;
                float acceleration = (samples[i].targetSpeed * samples[i].targetSpeed - samples[i - 1].targetSpeed * samples[i - 1].targetSpeed) / (2 * distance);
                Assert.That(acceleration, Is.LessThanOrEqualTo(2.001f)); Assert.That(acceleration, Is.GreaterThanOrEqualTo(-4.001f));
                Assert.That(RacingLineSnapshot.Finite(samples[i].time), Is.True);
            }
            Assert.That(samples.First(s => s.station >= 80).targetSpeed, Is.LessThan(12));
        }
        [Test] public void TuningControllerSurfaceAndFixedStepInvalidateFingerprint()
        {
            var first = RacingLineSnapshot.Capture(source).Fingerprint;
            source.vehicle.tuning.tires.lateralGrip += 0.1f; Assert.That(RacingLineSnapshot.Capture(source).Fingerprint, Is.Not.EqualTo(first));
            first = RacingLineSnapshot.Capture(source).Fingerprint; source.vehicle.controller.minimumLookahead++;
            Assert.That(RacingLineSnapshot.Capture(source).Fingerprint, Is.Not.EqualTo(first));
            first = RacingLineSnapshot.Capture(source).Fingerprint; source.vehicle.fixedStep = 0.01f;
            Assert.That(RacingLineSnapshot.Capture(source).Fingerprint, Is.Not.EqualTo(first));
        }
        [Test] public void MeasuredCapabilityIsTuneSpecific()
        {
            source.capability.longitudinalMeasured = true; source.capability.vehicleFingerprint = RacingLineSnapshot.VehicleFingerprintOf(source.vehicle);
            Assert.DoesNotThrow(() => RacingLineSnapshot.Capture(source)); source.vehicle.tuning.handling.driftBias += 0.1f;
            Assert.That(() => RacingLineSnapshot.Capture(source), Throws.ArgumentException.With.Message.Contains("STALE"));
        }
        [Test] public void CancellationProducesNoResultAndNeverChangesPublishedReference()
        {
            var old = Own(ScriptableObject.CreateInstance<RacingLineArtifact>()); source.published = old;
            using var planner = new RacingLinePlanner(RacingLineSnapshot.Capture(source), RacingLineFamily.Grip); planner.Cancel(); planner.Step();
            Assert.That(planner.Result, Is.Null); Assert.That(source.published, Is.SameAs(old));
        }
        [Test] public void SectorRegenerationPreservesOutsideGeometryAndRejectsChangedMapping()
        {
            var before = Generate(); using var planner = new RacingLinePlanner(RacingLineSnapshot.Capture(source), RacingLineFamily.Grip, before, 30, 90);
            while (!planner.IsDone) planner.Step();
            for (int i = 0; i < before.samples.Length; i++)
                if (before.samples[i].station < 30 || before.samples[i].station > 90) Assert.That(planner.Result.samples[i].position, Is.EqualTo(before.samples[i].position));
            SetRoad(10, false, 5);
            Assert.That(() => new RacingLinePlanner(RacingLineSnapshot.Capture(source), RacingLineFamily.Grip, before, 30, 90), Throws.ArgumentException);
        }
        [Test] public void ArtifactIsImmutableDetachedAndRejectsStaleDependencies()
        {
            var candidate = Generate(); var artifact = Own(ScriptableObject.CreateInstance<RacingLineArtifact>());
            artifact.Initialize(RacingLineSnapshot.Capture(source), candidate, ContractReport(candidate));
            Assert.That(artifact.TryOpen(candidate.fingerprint, out var reader, out _), Is.True);
            Vector3 point = reader.Sample(10).position; candidate.samples[5].position += Vector3.left * 10;
            Assert.That(reader.Sample(10).position, Is.EqualTo(point));
            Assert.That(artifact.TryOpen("stale", out _, out _), Is.False);
            var pose = Own(new GameObject("Entry condition probe"));
            pose.transform.SetPositionAndRotation(reader[0].position, Quaternion.LookRotation(reader[0].tangent, reader[0].normal));
            Assert.That(artifact.Entry.Contains(reader[0], pose.transform, Vector3.zero), Is.True);
            pose.transform.position += Vector3.right * 5;
            Assert.That(artifact.Entry.Contains(reader[0], pose.transform, Vector3.zero), Is.False);
            Assert.That(() => artifact.Initialize(RacingLineSnapshot.Capture(source), candidate, ContractReport(candidate)), Throws.InvalidOperationException);
        }
        [Test] public void FailedAndCompanionReportsDoNotAuthorizePublication()
        {
            var candidate = Generate(); var artifact = Own(ScriptableObject.CreateInstance<RacingLineArtifact>()); var report = ContractReport(candidate);
            report.passed = false; Assert.That(() => artifact.Initialize(RacingLineSnapshot.Capture(source), candidate, report), Throws.ArgumentException);
            report.passed = true; report.companionRun = true;
            Assert.That(() => artifact.Initialize(RacingLineSnapshot.Capture(source), candidate, report), Throws.ArgumentException);
        }
        [Test] public void EditingGeneratedGeometryInvalidatesExistingReport()
        {
            var candidate = Generate(); var report = ContractReport(candidate);
            var artifact = Own(ScriptableObject.CreateInstance<RacingLineArtifact>());
            candidate.samples[10].position += Vector3.left;
            Assert.That(() => artifact.Initialize(RacingLineSnapshot.Capture(source), candidate, report), Throws.ArgumentException);
        }
        [Test] public void OnlyDriftCandidateReceivesOrdinaryInputCue()
        {
            source.hints = new[] { new RacingLineHint { kind = RacingHintKind.BrakeToDrift, station = 30 } };
            Assert.That(Generate().cues, Is.Empty); Assert.That(Generate(RacingLineFamily.Drift).cues.Length, Is.EqualTo(1));
        }
        [Test] public void SchemaMigrationAndHintEditingAreUndoable()
        {
            source.schema = 1; source.verification = null;
            RacingLineEditorOperations.MigrateV1(source); Undo.FlushUndoRecordObjects(); Assert.That(source.schema, Is.EqualTo(2));
            Undo.PerformUndo(); Assert.That(source.schema, Is.EqualTo(1));
            source.schema = 2; source.verification = new RacingVerificationSettings();
            RacingLineEditorOperations.AddHint(source, RacingHintKind.Apex, 25); Undo.FlushUndoRecordObjects();
            Assert.That(source.hints.Length, Is.EqualTo(1)); Undo.PerformUndo(); Assert.That(source.hints.Length, Is.EqualTo(0));
        }
        [Test] public void DuplicateGetsNewDocumentAndHintIdsButKeepsRoadOwner()
        {
            source.hints = new[] { new RacingLineHint { station = 20 } };
            var copy = Own(RacingLineEditorOperations.Duplicate(source));
            Assert.That(copy.id, Is.Not.EqualTo(source.id)); Assert.That(copy.hints[0].id, Is.Not.EqualTo(source.hints[0].id));
            Assert.That(copy.route, Is.SameAs(source.route)); Assert.That(copy.published, Is.Null);
        }
        [Test] public void ManualPhysicsCannotStepDefaultWorld()
        {
            var go = Own(new GameObject("Default world guard")); go.SetActive(false); var vehicle = go.AddComponent<VehicleController>();
            Assert.That(() => vehicle.SetManualSimulation(true), Throws.InvalidOperationException);
            Assert.That(() => vehicle.StepSimulation(0.02f), Throws.InvalidOperationException);
        }
        [Test] public void ManualPhysicsCannotFollowARigIntoTheDefaultWorld()
        {
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = Own(new GameObject("Moved manual rig")); go.SetActive(false);
                SceneManager.MoveGameObjectToScene(go, preview); var vehicle = go.AddComponent<VehicleController>();
                vehicle.SetManualSimulation(true);
                SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
                Assert.That(() => vehicle.StepSimulation(0.02f), Throws.InvalidOperationException);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        [Test] public void PhysicsPrefabCannotReferenceAnotherChassisBeforeCloning()
        {
            var go = Own(new GameObject("Fixture root")); go.SetActive(false); var vehicle = go.AddComponent<VehicleController>();
            var external = Own(new GameObject("Unrelated chassis")); external.SetActive(false);
            var serialized = new SerializedObject(vehicle);
            serialized.FindProperty("powertrain").objectReferenceValue = external.AddComponent<VehiclePowertrain>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(vehicle.HasLocalPhysicsBindings, Is.False);
            Assert.That(() => RacingVehicleRig.ValidatePrefab(vehicle, source.vehicle.dimensions), Throws.ArgumentException.With.Message.Contains("LINE_PREFAB_BINDINGS"));
        }
        [Test] public void ImportedReportsRejectNullTracesAndNonFiniteMetrics()
        {
            var candidate = Generate(); var report = ContractReport(candidate);
            Assert.DoesNotThrow(() => RacingLineEditorOperations.ValidateImportedReport(report, candidate));
            report.trials[0].telemetry = null;
            Assert.Throws<ArgumentException>(() => RacingLineEditorOperations.ValidateImportedReport(report, candidate));
            report.trials[0].telemetry = Array.Empty<RacingTelemetrySample>(); report.trials[0].maximumLateralError = float.NaN;
            Assert.Throws<ArgumentException>(() => RacingLineEditorOperations.ValidateImportedReport(report, candidate));
        }
        [Test] public void CompanionContactLossIsIncludedInMeasuredFailure()
        {
            SetRoad(14, false); var inside = Generate(RacingLineFamily.Inside); var outside = Generate(RacingLineFamily.Outside);
            using var run = new RacingLineRollout(source, inside, outside);
            while (run.Elapsed < 0.1f && !run.IsDone) run.Advance(4, true);
            Assert.That(run.IsDone, Is.False);
            var cars = run.PreviewScene.GetRootGameObjects().Where(go => go.GetComponent<RacingSimulationInput>() != null).ToArray();
            // Explicit test fault injection, never a controller/production-rollout correction.
            cars[1].GetComponent<Rigidbody>().position += Vector3.up * 5;
            while (!run.IsDone) run.Advance(8);
            Assert.That(run.Result.passed, Is.False);
            Assert.That(run.Result.trials[0].airborneSeconds, Is.GreaterThan(source.verification.maximumAirborneSeconds));
        }
        [Test] public void LocalRolloutUsesActualWheelContactsAndCleansUp()
        {
            int scenes = SceneManager.sceneCount; float fixedStep = Time.fixedDeltaTime; var mode = Physics.simulationMode;
            var candidate = Generate(); RacingVerificationReport report;
            using (var run = new RacingLineRollout(source, candidate))
            {
                Assert.That(run.PreviewScene.GetPhysicsScene(), Is.Not.EqualTo(Physics.defaultPhysicsScene));
                int slices = 0; while (!run.IsDone && slices++ < 10000) run.Advance(8);
                Assert.That(run.IsDone, Is.True); report = run.Result;
            }
            Assert.That(SceneManager.sceneCount, Is.EqualTo(scenes)); Assert.That(Time.fixedDeltaTime, Is.EqualTo(fixedStep)); Assert.That(Physics.simulationMode, Is.EqualTo(mode));
            Assert.That(report.trials[0].telemetry.Any(s => s.contacts >= 2 && s.actualSpeed > 2), Is.True, report.trials[0].diagnosis);
            Assert.That(report.passed, Is.True, report.trials[0].diagnosis);
        }
        [Test] public void CancelledRolloutImmediatelyDisposesItsScene()
        {
            int scenes = SceneManager.sceneCount; Scene preview;
            using (var run = new RacingLineRollout(source, Generate())) { preview = run.PreviewScene; run.Advance(1); }
            Assert.That(preview.IsValid(), Is.False);
            Assert.That(SceneManager.sceneCount, Is.EqualTo(scenes));
        }
        [Test] public void StaleRolloutRejectsChangedSetupAndDisposesPreview()
        {
            using var run = new RacingLineRollout(source, Generate()); var preview = run.PreviewScene;
            source.vehicle.tuning.controls.steeringResponse += 1;
            Assert.That(() => run.Advance(), Throws.ArgumentException.With.Message.Contains("STALE"));
            Assert.That(preview.IsValid(), Is.False);
        }
        [Test] public void RepeatRolloutStartsFromFreshIntegratorAndAssistState()
        {
            var candidate = Generate(); RacingVerificationReport a, b;
            using (var run = new RacingLineRollout(source, candidate)) { while (!run.IsDone) run.Advance(8); a = run.Result; }
            using (var run = new RacingLineRollout(source, candidate)) { while (!run.IsDone) run.Advance(8); b = run.Result; }
            Assert.That(b.trials[0].elapsed, Is.EqualTo(a.trials[0].elapsed).Within(source.vehicle.fixedStep));
            Assert.That(b.trials[0].maximumLateralError, Is.EqualTo(a.trials[0].maximumLateralError).Within(0.02f));
        }
        [Test] public void StudioCanOpenSelectAndCloseWithoutKeepingPreviewScenes()
        {
            int scenes = SceneManager.sceneCount; var window = ScriptableObject.CreateInstance<RacingLineStudioWindow>();
            try { window.SelectDocument(source); window.CreateGUI(); window.BeginGeneration(RacingLineFamily.Grip); Assert.That(window.Document, Is.SameAs(source)); }
            finally { UnityEngine.Object.DestroyImmediate(window); }
            Assert.That(SceneManager.sceneCount, Is.EqualTo(scenes));
        }
        [Test] public void LegacyJsonFixtureMigratesWithoutChangingAuthoredIdentity()
        {
            string fixture = System.IO.File.ReadAllText("Assets/NfsMw/Modules/Driving/Tests/Editor/RacingLines/Fixtures/SourceV1.json");
            JsonUtility.FromJsonOverwrite(fixture, source); string identity = source.id;
            RacingLineEditorOperations.MigrateV1(source);
            Assert.That(source.id, Is.EqualTo(identity)); Assert.That(source.schema, Is.EqualTo(RacingLineSource.CurrentSchema));
            Assert.That(source.hints[0].lateral, Is.EqualTo(1.25f));
        }
        [Test] public void PublicationFailurePreservesPreviousAssetAndSourceReference()
        {
            var old = Own(ScriptableObject.CreateInstance<RacingLineArtifact>()); source.published = old;
            var candidate = Generate(); var report = ContractReport(candidate); report.passed = false;
            Assert.That(() => RacingLineEditorOperations.Publish(source, candidate, report, "Assets/RejectedLineMustNotExist.asset"), Throws.ArgumentException);
            Assert.That(source.published, Is.SameAs(old)); Assert.That(AssetDatabase.LoadAssetAtPath<RacingLineArtifact>("Assets/RejectedLineMustNotExist.asset"), Is.Null);
        }
    }
}
