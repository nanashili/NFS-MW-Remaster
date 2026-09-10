using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NfsMwRemaster.Driving.Editor;
using NfsMwRemaster.Driving.Editor.DrivingMechanics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Tests
{
    /// <summary>Production Unity solver regressions, not original-game fidelity measurements.</summary>
    public sealed class MostWantedDrivingPhysicsTests
    {
        private readonly List<Object> owned = new List<Object>();
        private readonly List<MostWantedDrivingImport> imports = new List<MostWantedDrivingImport>();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in imports) item.Dispose(); imports.Clear();
            foreach (var item in owned.AsEnumerable().Reverse()) if (item != null) Object.DestroyImmediate(item);
            owned.Clear();
        }

        [TestCase("bmwm3gtre46")]
        [TestCase("gti")]
        [TestCase("punto")]
        public void CapturedVehicleLaunchesInProductionSolver(string key)
        {
            var definition = Fixture(key, VehiclePhysicsLabExperimentKind.StandingLaunch, 6f);
            using var runner = new VehiclePhysicsLabRunner(definition, () => new VehicleInputState { Throttle = 1f });
            Complete(runner);
            Assert.That(runner.Report.samples.Max(sample => sample.speedKph), Is.GreaterThan(10f));
            Assert.That(runner.Report.samples.Last().position.z, Is.GreaterThan(3f));
            SaveEvidence(key + "-launch", runner.Report);
        }

        [TestCase("bmwm3gtre46")]
        [TestCase("gti")]
        public void CapturedBrakesReduceEightyKphToAStop(string key)
        {
            var definition = Fixture(key, VehiclePhysicsLabExperimentKind.Braking, 8f);
            definition.warmupSeconds = 0f; // Capture the full seeded-speed stop, not just its post-warmup tail.
            definition.startingSpeedKph = 80; definition.brakingStartSpeedKph = 40;
            definition.evaluation.targetStopSpeedKph = 2;
            using var runner = new VehiclePhysicsLabRunner(definition, () => VehicleInputState.Neutral);
            Complete(runner);
            Assert.That(runner.Report.samples[0].speedKph, Is.GreaterThan(75f), "Stopping evidence must begin near the seeded 80 km/h, without an unrecorded braking warmup.");
            var metric = VehiclePhysicsLabAnalysis.FindMetric(runner.Report.metrics, "stopping_distance");
            Assert.That(metric.available, Is.True, "The run must actually reach the stopping threshold.");
            Assert.That(metric.value, Is.GreaterThan(0).And.LessThan(250));
            SaveEvidence(key + "-braking", runner.Report);
        }

        [Test]
        public void RecoveredSteeringTurnsAndUsesSmallerOutsideWheelAngle()
        {
            var definition = Fixture("bmwm3gtre46", VehiclePhysicsLabExperimentKind.Custom, 2f);
            definition.startingSpeedKph = 50; definition.track.width = 80;
            definition.input.mode = VehiclePhysicsLabInputMode.Live;
            using var runner = new VehiclePhysicsLabRunner(definition, () => new VehicleInputState { Throttle = .7f, Steering = .4f });
            Complete(runner);
            Assert.That(runner.Report.samples.Max(sample => Mathf.Abs(sample.position.x)), Is.GreaterThan(.5f));
            var vehicle = runner.PreviewVehicle.GetComponent<VehicleController>();
            var front = vehicle.Wheels.Where(wheel => wheel.IsFrontWheel).OrderBy(wheel => wheel.transform.localPosition.x).ToArray();
            Assert.That(front, Has.Length.EqualTo(2));
            Assert.That(Mathf.Abs(front[0].SteerAngle), Is.LessThan(Mathf.Abs(front[1].SteerAngle)));
            SaveEvidence("bmwm3gtre46-steering", runner.Report);
        }

        [Test]
        public void ReferenceLowSpeedSteeringUsesRecoveredPhysicalAngleInsteadOfAuthoredCap()
        {
            var definition = Fixture("bmwm3gtre46", VehiclePhysicsLabExperimentKind.Custom, 1f);
            definition.vehicle.tuning.controls.maxSteerAngle = 20f;
            definition.warmupSeconds = 0f;
            definition.input.mode = VehiclePhysicsLabInputMode.Live;
            using var runner = new VehiclePhysicsLabRunner(definition, () => new VehicleInputState { Steering = 1f, Throttle = 1f });
            Complete(runner);
            var vehicle = runner.PreviewVehicle.GetComponent<VehicleController>();
            float insideAngle = vehicle.Wheels.Where(wheel => wheel.IsFrontWheel).Max(wheel => Mathf.Abs(wheel.SteerAngle));
            Assert.That(insideAngle, Is.GreaterThan(vehicle.Tuning.controls.maxSteerAngle + 1f));
            Assert.That(insideAngle, Is.LessThanOrEqualTo(MostWantedSteeringModel.AbsoluteMaximumDegrees));
        }

        [Test]
        public void ReferenceAutomaticReverseLatchesAndAcceleratesThroughFormerTransitionGap()
        {
            var definition = Fixture("bmwm3gtre46", VehiclePhysicsLabExperimentKind.Custom, 3f);
            definition.warmupSeconds = 0f;
            definition.input.mode = VehiclePhysicsLabInputMode.Live;
            definition.capture.captureFilteredInput = true;
            using var runner = new VehiclePhysicsLabRunner(definition, () => new VehicleInputState { Brake = 1f });
            Complete(runner);
            var reverseSamples = runner.Report.samples.Where(sample => sample.gear < 0).ToArray();
            Assert.That(reverseSamples, Is.Not.Empty, "Holding brake at rest must engage automatic reverse.");
            Assert.That(reverseSamples.Min(sample => sample.forwardSpeedKph), Is.LessThan(-4f), "Reverse must accelerate past the old -2 to -4 km/h request gap.");
            Assert.That(reverseSamples.Count(sample => sample.finalInput.Throttle > .5f), Is.GreaterThan(10));
            Assert.That(runner.Report.samples.SkipWhile(sample => sample.gear >= 0).Any(sample => sample.gear > 0), Is.False,
                "Once reverse is latched it must not flick back into first gear while accelerating backward.");
        }

        [Test]
        public void ReferenceAutomaticReverseToForwardBrakesThenChangesDirectionNearStop()
        {
            var definition = Fixture("bmwm3gtre46", VehiclePhysicsLabExperimentKind.Custom, 5f);
            definition.warmupSeconds = 0f;
            definition.input.mode = VehiclePhysicsLabInputMode.Live;
            definition.capture.captureFilteredInput = true;
            float elapsed = 0f;
            using var runner = new VehiclePhysicsLabRunner(definition, () =>
            {
                return elapsed < 2f ? new VehicleInputState { Brake = 1f } : new VehicleInputState { Throttle = 1f };
            });
            var vehicle = runner.PreviewVehicle.GetComponent<VehicleController>();
            vehicle.PhysicsSampled += dt => elapsed += dt;
            Complete(runner);
            int firstReverse = Array.FindIndex(runner.Report.samples, sample => sample.gear < 0);
            int firstForwardAfterReverse = firstReverse < 0 ? -1 : Array.FindIndex(runner.Report.samples, firstReverse + 1,
                sample => sample.gear > 0);
            Assert.That(firstReverse, Is.GreaterThanOrEqualTo(0));
            Assert.That(firstForwardAfterReverse, Is.GreaterThan(firstReverse));
            Assert.That(Mathf.Abs(runner.Report.samples[firstForwardAfterReverse].forwardSpeedKph), Is.LessThan(3f),
                "Forward gear must not be selected while the car is still materially reversing.");
            Assert.That(runner.Report.samples.Skip(firstForwardAfterReverse).Max(sample => sample.forwardSpeedKph), Is.GreaterThan(3f));
        }

        [Test]
        public void ReferenceSteeringAndDrivetrainDoNotDependOnPresentationCadence()
        {
            Vector3 referencePosition = default; float referenceSpeed = 0;
            foreach (int fps in new[] { 30, 60, 144 })
            {
                var definition = Fixture("gti", VehiclePhysicsLabExperimentKind.Custom, 5f);
                using var runner = new VehiclePhysicsLabRunner(definition);
                var vehicle = runner.PreviewVehicle.GetComponent<VehicleController>();
                var input = runner.PreviewVehicle.GetComponent<RacingSimulationInput>();
                var physics = runner.PreviewScene.GetPhysicsScene();
                double accumulated = 0; int ticks = 0;
                while (ticks < 200)
                {
                    accumulated += 1d / fps;
                    while (accumulated >= .02 && ticks < 200)
                    {
                        input.Current = new VehicleInputState { Throttle = .8f, Steering = ticks > 75 ? .12f : 0 };
                        vehicle.StepSimulation(.02f); physics.Simulate(.02f); accumulated -= .02; ticks++;
                    }
                    vehicle.ModuleHost.RunPresentation(1f / fps);
                }
                if (fps == 30) { referencePosition = vehicle.Body.position; referenceSpeed = vehicle.Telemetry.SpeedKph; }
                else
                {
                    Assert.That(Vector3.Distance(vehicle.Body.position, referencePosition), Is.LessThan(.15f), fps + " Hz position");
                    Assert.That(vehicle.Telemetry.SpeedKph, Is.EqualTo(referenceSpeed).Within(.5f), fps + " Hz speed");
                }
                Assert.That(float.IsFinite(vehicle.Telemetry.SpeedKph), Is.True);
            }
        }

        private VehiclePhysicsLabDefinition Fixture(string key, VehiclePhysicsLabExperimentKind kind, float duration)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/DrivingMechanics/Evidence/handling-local-20260908.json"));
            Assert.That(File.Exists(path), Is.True, "Offline captured source evidence must be included in the validation project.");
            var capture = JsonConvert.DeserializeObject<MostWantedHandlingReport>(File.ReadAllText(path));
            var vehicle = capture.vehicles.Single(item => item.name == key);
            var imported = MostWantedDrivingImporter.Create(capture, vehicle, MostWantedDrivingImporter.FirstReferences(vehicle)); imports.Add(imported);
            var setup = Own(ScriptableObject.CreateInstance<RacingVehicleSetup>());
            setup.id = Guid.NewGuid().ToString("N"); setup.tuning = imported.Tuning; setup.upgrades = Array.Empty<VehiclePerformanceUpgradeDefinition>();
            var track = Own(ScriptableObject.CreateInstance<VehiclePhysicsLabTrack>());
            track.id = Guid.NewGuid().ToString("N"); track.kind = VehiclePhysicsLabTrackKind.FlatStraight;
            track.length = 1000f; track.width = 40;
            var definition = Own(ScriptableObject.CreateInstance<VehiclePhysicsLabDefinition>());
            definition.id = Guid.NewGuid().ToString("N"); definition.vehicle = setup; definition.track = track;
            definition.experiment = kind; definition.warmupSeconds = .5f; definition.safety.maximumSeconds = duration;
            definition.capture.maximumSamples = 1024; definition.evaluation.requireTargetSpeed = false;
            definition.capture.captureFilteredInput = true;
            return definition;
        }

        private static void Complete(VehiclePhysicsLabRunner runner)
        {
            int guard = 0; while (!runner.IsDone && guard++ < 2000) runner.Advance(20d);
            Assert.That(runner.IsDone, Is.True, "Simulation budget exhausted.");
            Assert.That(runner.Report.Passed, Is.True, runner.Report.failureMessage);
            Assert.That(runner.Report.samples, Is.Not.Empty);
            foreach (var sample in runner.Report.samples)
            {
                Assert.That(float.IsFinite(sample.speedKph) && float.IsFinite(sample.engineRpm) && float.IsFinite(sample.angularVelocity.sqrMagnitude), Is.True);
                Assert.That(sample.angularVelocity.magnitude, Is.LessThan(20f));
                foreach (var wheel in sample.wheels)
                    Assert.That(float.IsFinite(wheel.normalLoad) && float.IsFinite(wheel.longitudinalForce) && float.IsFinite(wheel.lateralForce), Is.True);
            }
        }

        private static void SaveEvidence(string name, VehiclePhysicsLabRunReport report)
        {
            string root = Environment.GetEnvironmentVariable("MW_DRIVING_EVIDENCE_DIRECTORY");
            if (string.IsNullOrWhiteSpace(root)) return;
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, name + ".unity-measurement.json");
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(file);
            writer.Write(JsonConvert.SerializeObject(new {
                kind = "Unity production-solver measurement; not an original-game comparison",
                simulationRevision = VehicleController.SimulationRevision,
                sampleCount = report.samples.Length,
                peakSpeedKph = report.samples.Max(sample => sample.speedKph),
                finalSpeedKph = report.samples.Last().speedKph,
                metrics = report.metrics,
                samples = report.samples.Select(sample => new {
                    sample.time, sample.speedKph, sample.engineRpm, sample.gear,
                    x = sample.position.x, y = sample.position.y, z = sample.position.z,
                    sample.groundedWheels, wheels = sample.wheels.Select(wheel => new {
                        wheel.wheelIndex, wheel.front, wheel.driven, wheel.grounded,
                        wheel.normalLoad, wheel.longitudinalSlip, wheel.lateralSlipRadians,
                        wheel.longitudinalForce, wheel.lateralForce, wheel.suspensionCompression,
                        wheel.contactForwardSpeedMps, wheel.surfaceName, wheel.surfaceGrip
                    })
                })
            }, Formatting.Indented));
        }

        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
    }
}
