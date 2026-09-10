using System;
using System.Collections.Generic;
using NfsMwRemaster.Driving.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Tests
{
    /// <summary>Bounded production-runner regressions. Thresholds are deliberately loose
    /// enough for PhysX platform variation while still detecting a dead drivetrain.</summary>
    public sealed class VehicleFrameworkPhysicsRegressionTests
    {
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [TestCase(VehicleDriveLayout.Fwd)]
        [TestCase(VehicleDriveLayout.Rwd)]
        [TestCase(VehicleDriveLayout.Awd)]
        public void ProductionRunnerLaunchesEachExplicitDrivetrain(VehicleDriveLayout layout)
        {
            VehiclePhysicsLabDefinition definition = MakeDefinition(layout, VehiclePhysicsLabExperimentKind.StandingLaunch);
            using (var runner = new VehiclePhysicsLabRunner(definition, () => new VehicleInputState { Throttle = 1f }))
            {
                Run(runner, 300);
                Assert.That(runner.Report.Passed, Is.True, runner.Report.failureMessage);
                Assert.That(runner.Report.samples, Is.Not.Empty);
                Assert.That(Max(runner.Report.samples, s => s.speedKph), Is.GreaterThan(2f));
                AssertFinite(runner.Report.samples);
            }
        }

        [Test]
        public void RecordedManualSequenceConsumesDiscreteActionsAndRejectsReverseWhileMoving()
        {
            VehiclePhysicsLabDefinition definition = MakeDefinition(VehicleDriveLayout.Rwd, VehiclePhysicsLabExperimentKind.Gearshift);
            definition.vehicle.tuning.transmissionMode = VehicleTransmissionMode.Manual;
            definition.input.mode = VehiclePhysicsLabInputMode.Recorded;
            definition.input.keys = new[]
            {
                Key(0f, new VehicleInputState { Throttle = 1f }),
                Key(0.35f, new VehicleInputState { Throttle = 1f, GearUp = true }),
                Key(0.37f, new VehicleInputState { Throttle = 1f }),
                Key(0.8f, new VehicleInputState { GearNeutral = true }),
                Key(1.0f, new VehicleInputState { GearReverse = true, Throttle = 0.4f })
            };
            using (var runner = new VehiclePhysicsLabRunner(definition, () => VehicleInputState.Neutral))
            {
                Run(runner, 300);
                Assert.That(runner.Report.Passed, Is.True, runner.Report.failureMessage);
                AssertFinite(runner.Report.samples);
                Assert.That(Max(runner.Report.samples, s => Mathf.Abs(s.engineRpm)), Is.LessThan(10000f));
                Assert.That(Max(runner.Report.samples, s => s.angularVelocity.magnitude), Is.LessThan(80f));
                Assert.That(Count(runner.Report.samples, s => s.rawInput.GearUp), Is.EqualTo(1));
                Assert.That(Count(runner.Report.samples, s => s.rawInput.GearNeutral), Is.EqualTo(1));
                Assert.That(Count(runner.Report.samples, s => s.rawInput.GearReverse), Is.EqualTo(1));
                Assert.That(Count(runner.Report.samples, s => s.gear == 0), Is.GreaterThan(0));
                Assert.That(Count(runner.Report.samples, s => s.gear < 0), Is.EqualTo(0));
            }
        }

        [Test]
        public void WetSurfaceResponseIsFiniteAndBounded()
        {
            var surfaceObject = new GameObject("wet response surface");
            owned.Add(surfaceObject);
            var surface = surfaceObject.AddComponent<VehicleSurface>();
            surface.Configure("test road", 1f);
            surface.SetWeatherWetness(0f); float dry = surface.GripMultiplier;
            surface.SetWeatherWetness(1f); float wet = surface.GripMultiplier;
            Assert.That(wet, Is.GreaterThan(0f).And.LessThan(dry));
            Assert.That(dry - wet, Is.LessThan(1f));
        }

        [Test]
        public void ProductionRunnerWetAndDryTracksProduceDifferentBoundedTraction()
        {
            VehiclePhysicsLabDefinition wet = MakeDefinition(VehicleDriveLayout.Rwd, VehiclePhysicsLabExperimentKind.RollingAcceleration);
            wet.track.surfaceGrip = 0.55f;
            wet.startingSpeedKph = 25f;
            wet.safety.maximumSeconds = 2f;
            VehiclePhysicsLabDefinition dry = MakeDefinition(VehicleDriveLayout.Rwd, VehiclePhysicsLabExperimentKind.RollingAcceleration);
            dry.track.surfaceGrip = 1.2f;
            dry.startingSpeedKph = 25f;
            dry.safety.maximumSeconds = 2f;
            float wetPeak;
            float dryPeak;
            using (var runner = new VehiclePhysicsLabRunner(wet, () => new VehicleInputState { Throttle = 1f }))
            { Run(runner, 300); Assert.That(runner.Report.Passed, Is.True, runner.Report.failureMessage); wetPeak = Max(runner.Report.samples, s => s.speedKph); AssertFinite(runner.Report.samples); }
            using (var runner = new VehiclePhysicsLabRunner(dry, () => new VehicleInputState { Throttle = 1f }))
            { Run(runner, 300); Assert.That(runner.Report.Passed, Is.True, runner.Report.failureMessage); dryPeak = Max(runner.Report.samples, s => s.speedKph); AssertFinite(runner.Report.samples); }
            Assert.That(dryPeak, Is.GreaterThan(wetPeak));
            Assert.That(dryPeak - wetPeak, Is.LessThan(100f));
        }

        [Test]
        public void ProductionRunnerRampLandingKeepsWheelForcesFiniteAndRegainsGround()
        {
            VehiclePhysicsLabDefinition definition = MakeDefinition(VehicleDriveLayout.Rwd, VehiclePhysicsLabExperimentKind.RampLanding);
            definition.track.kind = VehiclePhysicsLabTrackKind.Ramp;
            definition.track.length = 100f;
            definition.track.rampLength = 30f;
            definition.track.rampHeight = 2f;
            definition.startingSpeedKph = 80f;
            definition.input.throttle = 1f;
            definition.safety.maximumSeconds = 8f;
            definition.capture.maximumSamples = 1024;
            using (var runner = new VehiclePhysicsLabRunner(definition, () => new VehicleInputState { Throttle = 1f }))
            {
                Run(runner, 400);
                Assert.That(runner.Report.Passed, Is.True, runner.Report.failureMessage);
                AssertFinite(runner.Report.samples);
                Assert.That(runner.Report.samples, Is.Not.Empty);
                foreach (var sample in runner.Report.samples)
                    if (sample.wheels != null)
                        foreach (var wheel in sample.wheels)
                        { Assert.That(float.IsNaN(wheel.longitudinalForce) || float.IsInfinity(wheel.longitudinalForce), Is.False); Assert.That(float.IsNaN(wheel.normalLoad) || float.IsInfinity(wheel.normalLoad), Is.False); }
                Assert.That(Count(runner.Report.samples, s => s.groundedWheels == 0), Is.GreaterThan(0), "Ramp must actually create an airborne interval.");
                Assert.That(Count(runner.Report.samples, s => s.position.z > definition.track.length + 5 && s.groundedWheels > 0), Is.GreaterThan(0), "Vehicle must regain contact beyond the ramp.");
            }
        }

        [TestCase(true)] [TestCase(false)]
        public void BrakingReportsAbsOnlyWhenEnabled(bool enabled)
        {
            var definition = MakeDefinition(VehicleDriveLayout.Rwd, VehiclePhysicsLabExperimentKind.Custom);
            definition.startingSpeedKph = 80; definition.vehicle.tuning.assists.abs = enabled;
            definition.vehicle.tuning.controls.serviceBrakeTorque = 12000;
            definition.input.mode = VehiclePhysicsLabInputMode.Live;
            using var runner = new VehiclePhysicsLabRunner(definition, () => new VehicleInputState { Brake = 1 });
            Run(runner, 300); Assert.That(runner.Report.Passed, Is.True, runner.Report.failureMessage); AssertFinite(runner.Report.samples);
            float intervention = Max(runner.Report.samples, s => s.absReduction);
            if (enabled) Assert.That(intervention, Is.GreaterThan(.1f).And.LessThanOrEqualTo(1));
            else Assert.That(intervention, Is.EqualTo(0));
            Assert.That(runner.Report.samples[runner.Report.samples.Length - 1].speedKph, Is.LessThan(70));
        }

        [Test]
        public void HandbrakeLocksRearAxleWhileFrontWheelsKeepRolling()
        {
            var definition = MakeDefinition(VehicleDriveLayout.Rwd, VehiclePhysicsLabExperimentKind.Custom);
            definition.startingSpeedKph = 70; definition.input.mode = VehiclePhysicsLabInputMode.Live;
            using var runner = new VehiclePhysicsLabRunner(definition, () => new VehicleInputState { Handbrake = true });
            Run(runner, 300); Assert.That(runner.Report.Passed, Is.True, runner.Report.failureMessage);
            float front = 0, rear = 0; int count = 0;
            foreach (var sample in runner.Report.samples) if (sample.time > .5f && sample.speedKph > 15)
            { foreach (var wheel in sample.wheels) { if (wheel.front) front += Mathf.Abs(wheel.longitudinalSlip); else rear += Mathf.Abs(wheel.longitudinalSlip); } count++; }
            Assert.That(count, Is.GreaterThan(0)); Assert.That(rear, Is.GreaterThan(front * 1.5f));
            Assert.That(Max(runner.Report.samples, s => s.absReduction), Is.EqualTo(0), "ABS controls service brakes, not the mechanical handbrake.");
        }

        [Test]
        public void ProductionRunnerBrakingProducesFiniteStoppingMetric()
        {
            VehiclePhysicsLabDefinition definition = MakeDefinition(VehicleDriveLayout.Rwd, VehiclePhysicsLabExperimentKind.Braking);
            definition.startingSpeedKph = 80f; definition.brakingStartSpeedKph = 40f;
            definition.evaluation.targetStopSpeedKph = 2f; definition.safety.maximumSeconds = 6f;
            definition.capture.maximumSamples = 512;
            definition.capture.captureFilteredInput = true;
            using (var runner = new VehiclePhysicsLabRunner(definition, () => VehicleInputState.Neutral))
            {
                Run(runner, 600);
                Assert.That(runner.Report.Passed, Is.True, runner.Report.failureMessage);
                VehiclePhysicsLabMetric metric = VehiclePhysicsLabAnalysis.FindMetric(runner.Report.metrics, "stopping_distance");
                Assert.That(metric.available, Is.True);
                Assert.That(metric.value, Is.GreaterThan(0f).And.LessThan(500f));
            }
        }

        [Test]
        public void GovernorChangesMeasuredLimitWhileDisabledDoesNotClampSeededSpeed()
        {
            VehiclePhysicsLabDefinition governed = MakeDefinition(VehicleDriveLayout.Rwd, VehiclePhysicsLabExperimentKind.CoastDown);
            governed.vehicle.tuning.speedGovernor = true;
            governed.vehicle.tuning.chassis.maxSpeedKph = 30f;
            governed.startingSpeedKph = 80f;
            governed.safety.maximumSeconds = 2f;

            VehiclePhysicsLabDefinition ungoverned = MakeDefinition(VehicleDriveLayout.Rwd, VehiclePhysicsLabExperimentKind.CoastDown);
            ungoverned.vehicle.tuning.speedGovernor = false;
            ungoverned.vehicle.tuning.chassis.maxSpeedKph = 30f;
            ungoverned.startingSpeedKph = 80f;
            ungoverned.safety.maximumSeconds = 2f;

            float governedFinal;
            float ungovernedFinal;
            using (var runner = new VehiclePhysicsLabRunner(governed, () => VehicleInputState.Neutral))
            {
                Run(runner, 300);
                Assert.That(runner.Report.Passed, Is.True, runner.Report.failureMessage);
                governedFinal = runner.Report.samples[runner.Report.samples.Length - 1].speedKph;
            }
            using (var runner = new VehiclePhysicsLabRunner(ungoverned, () => VehicleInputState.Neutral))
            {
                Run(runner, 300);
                Assert.That(runner.Report.Passed, Is.True, runner.Report.failureMessage);
                ungovernedFinal = runner.Report.samples[runner.Report.samples.Length - 1].speedKph;
            }

            Assert.That(governedFinal, Is.GreaterThan(0f).And.LessThan(ungovernedFinal));
            Assert.That(ungovernedFinal, Is.GreaterThan(35f));
        }

        [Test]
        public void InputAuthorityAllowsHandoverButRejectsConcurrentOwners()
        {
            var authority = new GameObject("input authority").AddComponent<VehicleInputAuthority>(); owned.Add(authority.gameObject);
            var first = new FakeInput(); var second = new FakeInput();
            Assert.That(authority.TryAcquire(first, first), Is.True);
            Assert.That(authority.TryAcquire(second, second), Is.False);
            Assert.That(authority.Release(second), Is.False);
            Assert.That(authority.Release(first), Is.True);
            Assert.That(authority.TryAcquire(second, second), Is.True);
        }

        [Test]
        public void UnevenKerbsProduceIndependentContactAndRemainStable()
        {
            var definition = MakeDefinition(VehicleDriveLayout.Rwd, VehiclePhysicsLabExperimentKind.Custom);
            definition.startingSpeedKph = 40; definition.safety.maximumSeconds = 4; definition.capture.maximumSamples = 512;
            definition.input.mode = VehiclePhysicsLabInputMode.Live;
            using var runner = new VehiclePhysicsLabRunner(definition, () => new VehicleInputState { Throttle = .3f });
            foreach (float z in new[] { 8f, 15f, 24f })
            {
                var kerb = new GameObject("Known right-wheel kerb"); SceneManager.MoveGameObjectToScene(kerb, runner.PreviewScene);
                kerb.transform.position = new Vector3(.8f, .04f, z); kerb.AddComponent<BoxCollider>().size = new Vector3(.6f, .08f, 1.2f);
                kerb.AddComponent<VehicleSurface>().Configure("Kerb fixture", 1f);
            }
            Physics.SyncTransforms(); Run(runner, 300); Assert.That(runner.Report.Passed, Is.True, runner.Report.failureMessage);
            AssertFinite(runner.Report.samples);
            Assert.That(Count(runner.Report.samples, s => Array.Exists(s.wheels, w => w.grounded && w.surfaceName == "Kerb fixture")), Is.GreaterThan(0));
            Assert.That(Count(runner.Report.samples, s => s.groundedWheels >= 2 && s.position.z > 30), Is.GreaterThan(0));
            Assert.That(Max(runner.Report.samples, s => s.angularVelocity.magnitude), Is.LessThan(10));
        }

        [Test]
        public void FixedSimulationRemainsConsistentAcrossPresentationCadences()
        {
            Vector3 referencePosition = default; float referenceSpeed = 0;
            foreach (int fps in new[] { 30, 60, 144 })
            {
                var definition = MakeDefinition(VehicleDriveLayout.Rwd, VehiclePhysicsLabExperimentKind.Custom);
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
                        input.Current = new VehicleInputState { Throttle = .65f, Steering = ticks > 75 ? .15f : 0 };
                        vehicle.StepSimulation(.02f); physics.Simulate(.02f); accumulated -= .02; ticks++;
                    }
                    vehicle.ModuleHost.RunPresentation(1f / fps);
                }
                if (fps == 30) { referencePosition = vehicle.Body.position; referenceSpeed = vehicle.Telemetry.SpeedKph; }
                else
                {
                    Assert.That(Vector3.Distance(vehicle.Body.position, referencePosition), Is.LessThan(.15f), fps + " Hz presentation position tolerance");
                    Assert.That(vehicle.Telemetry.SpeedKph, Is.EqualTo(referenceSpeed).Within(.5f), fps + " Hz presentation speed tolerance");
                }
            }
        }

        private VehiclePhysicsLabDefinition MakeDefinition(VehicleDriveLayout layout, VehiclePhysicsLabExperimentKind kind)
        {
            var tuning = Own(VehicleTuning.CreateStreetRacer()); tuning.driveLayout = layout;
            var setup = Own(ScriptableObject.CreateInstance<RacingVehicleSetup>());
            setup.id = Guid.NewGuid().ToString("N"); setup.tuning = tuning; setup.upgrades = Array.Empty<VehiclePerformanceUpgradeDefinition>();
            var track = Own(ScriptableObject.CreateInstance<VehiclePhysicsLabTrack>());
            track.id = Guid.NewGuid().ToString("N"); track.kind = VehiclePhysicsLabTrackKind.FlatStraight;
            var definition = Own(ScriptableObject.CreateInstance<VehiclePhysicsLabDefinition>());
            definition.id = Guid.NewGuid().ToString("N"); definition.vehicle = setup; definition.track = track;
            definition.experiment = kind; definition.warmupSeconds = 0f; definition.safety.maximumSeconds = 2f;
            definition.capture.maximumSamples = 256; definition.evaluation.requireTargetSpeed = false;
            return definition;
        }

        private static VehiclePhysicsLabControlKey Key(float time, VehicleInputState input) => new VehiclePhysicsLabControlKey { time = time, input = input };
        private static void Run(VehiclePhysicsLabRunner runner, int guard)
        { int i = 0; while (!runner.IsDone && i++ < guard) runner.Advance(20d); Assert.That(runner.IsDone, Is.True); }
        private static float Max(VehiclePhysicsLabSample[] samples, Func<VehiclePhysicsLabSample, float> selector)
        { float value = 0f; foreach (var sample in samples) value = Mathf.Max(value, selector(sample)); return value; }
        private static int Count(VehiclePhysicsLabSample[] samples, Func<VehiclePhysicsLabSample, bool> selector)
        { int value = 0; foreach (var sample in samples) if (selector(sample)) value++; return value; }
        private static void AssertFinite(VehiclePhysicsLabSample[] samples)
        { foreach (var s in samples) Assert.That(float.IsNaN(s.speedKph) || float.IsInfinity(s.speedKph), Is.False); }
        private T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }

        private sealed class FakeInput : IVehicleInputSource
        {
            public VehicleInputState Current => VehicleInputState.Neutral;
            public bool ConsumeResetRequest() => false;
            public bool ConsumeCameraToggleRequest() => false;
        }
    }
}
