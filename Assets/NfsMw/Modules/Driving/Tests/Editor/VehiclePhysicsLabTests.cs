using System;
using System.Collections.Generic;
using NfsMwRemaster.Driving.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehiclePhysicsLabTests
    {
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            }

            owned.Clear();
        }

        [Test]
        public void TimeToTargetInterpolatesOnTheMeasurementClock()
        {
            VehiclePhysicsLabSample[] samples =
            {
                Sample(0f, 0f),
                Sample(100f, 1f)
            };

            Assert.That(VehiclePhysicsLabAnalysis.TryTimeAtOrAbove(samples, 50f, out float time), Is.True);
            Assert.That(time, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void TimeToTargetDoesNotExtrapolateAnUnreachedTarget()
        {
            VehiclePhysicsLabSample[] samples =
            {
                Sample(0f, 0f),
                Sample(40f, 1f)
            };

            Assert.That(VehiclePhysicsLabAnalysis.TryTimeAtOrAbove(samples, 50f, out _), Is.False);
        }

        [Test]
        public void StoppingDistanceStartsAtTheFirstBrakeSample()
        {
            VehiclePhysicsLabSample[] samples =
            {
                Sample(60f, 0f, Vector3.zero),
                Sample(35f, 1f, new Vector3(0f, 0f, 5f), brake: true),
                Sample(0f, 2f, new Vector3(0f, 0f, 12f), brake: true)
            };

            Assert.That(VehiclePhysicsLabAnalysis.TryStoppingDistance(samples, out float distance), Is.True);
            Assert.That(distance, Is.EqualTo(7f).Within(0.0001f));
        }

        [Test]
        public void StationaryBrakeHoldDoesNotCountAsAStop()
        {
            VehiclePhysicsLabSample[] samples =
            {
                Sample(0f, 0f, Vector3.zero, brake: true),
                Sample(0f, 1f, Vector3.zero, brake: true)
            };

            Assert.That(VehiclePhysicsLabAnalysis.TryStoppingDistance(samples, out _), Is.False);
        }

        [Test]
        public void StoppingDistanceSkipsTheStationaryHoldBeforeDriving()
        {
            VehiclePhysicsLabSample[] samples =
            {
                Sample(0f, 0f, Vector3.zero, brake: true),
                Sample(60f, 1f, new Vector3(0f, 0f, 10f)),
                Sample(35f, 2f, new Vector3(0f, 0f, 20f), brake: true),
                Sample(0f, 3f, new Vector3(0f, 0f, 27f), brake: true)
            };

            Assert.That(VehiclePhysicsLabAnalysis.TryStoppingDistance(samples, out float distance), Is.True);
            Assert.That(distance, Is.EqualTo(7f).Within(0.0001f));
        }

        [Test]
        public void ComparisonInterpolatesCandidateByAbsoluteMeasurementTime()
        {
            VehiclePhysicsLabSample[] baseline =
            {
                Sample(0f, 0f),
                Sample(100f, 1f)
            };
            VehiclePhysicsLabSample[] candidate = { Sample(60f, 0.5f) };

            VehiclePhysicsLabComparisonSample[] comparison = VehiclePhysicsLabAnalysis.CompareByTime(baseline, candidate);
            Assert.That(comparison, Has.Length.EqualTo(1));
            Assert.That(comparison[0].speedDeltaKph, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(comparison[0].time, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void TuningOverridesAreBoundedAndKeepDiagnostics()
        {
            VehicleTuning tuning = Own(VehicleTuning.CreateStreetRacer());
            var valid = new VehiclePhysicsLabTuningOverride
            {
                enabled = true,
                parameter = VehiclePhysicsLabTuningParameter.Mass,
                value = 1700f
            };

            Assert.That(VehiclePhysicsLabTuningOverrides.Apply(tuning, new[] { valid }, out string[] validDiagnostics), Is.True);
            Assert.That(VehiclePhysicsLabTuningOverrides.Read(tuning, VehiclePhysicsLabTuningParameter.Mass), Is.EqualTo(1700f));
            Assert.That(validDiagnostics, Has.Length.EqualTo(1));

            var invalid = valid;
            invalid.value = 99f;
            Assert.That(VehiclePhysicsLabTuningOverrides.Apply(tuning, new[] { invalid }, out string[] invalidDiagnostics), Is.False);
            Assert.That(invalidDiagnostics[0], Does.Contain("PHYSICS_LAB_OVERRIDE_RANGE"));
            Assert.That(VehiclePhysicsLabTuningOverrides.Read(tuning, VehiclePhysicsLabTuningParameter.Mass), Is.EqualTo(1700f));
        }

        [Test]
        public void TrackValidationProvidesDeterministicFrames()
        {
            VehiclePhysicsLabTrack track = Own(ScriptableObject.CreateInstance<VehiclePhysicsLabTrack>());
            track.kind = VehiclePhysicsLabTrackKind.Slalom;
            track.length = 180f;

            Assert.That(track.IsValid(out string failure), Is.True, failure);
            VehiclePhysicsLabTrackSample sample = track.Sample(45f);
            Assert.That(sample.forward.sqrMagnitude, Is.GreaterThan(0.99f));
            Assert.That(sample.left.sqrMagnitude, Is.GreaterThan(0.99f));
            Assert.That(sample.up.sqrMagnitude, Is.GreaterThan(0.99f));
            Assert.That(Vector3.Dot(sample.forward, sample.up), Is.EqualTo(0f).Within(0.0001f));

            track.width = 1f;
            Assert.That(track.IsValid(out failure), Is.False);
            Assert.That(failure, Does.Contain("PHYSICS_LAB_TRACK_VALUES"));
        }

        [Test]
        public void SweepEnumeratesUniqueMixedRadixTrialsWithinBudget()
        {
            VehiclePhysicsLabDefinition experiment = CreateDefinition();
            VehiclePhysicsLabSweepDefinition sweep = Own(ScriptableObject.CreateInstance<VehiclePhysicsLabSweepDefinition>());
            sweep.baseExperiment = experiment;
            sweep.maximumTrials = 6;
            sweep.axes = new[]
            {
                new VehiclePhysicsLabSweepAxis
                {
                    parameter = VehiclePhysicsLabTuningParameter.Mass,
                    minimum = 1200f,
                    maximum = 1800f,
                    steps = 2
                },
                new VehiclePhysicsLabSweepAxis
                {
                    parameter = VehiclePhysicsLabTuningParameter.EngineTorque,
                    minimum = 300f,
                    maximum = 400f,
                    steps = 3
                }
            };

            Assert.That(sweep.IsValid(out string failure), Is.True, failure);
            Assert.That(sweep.TrialCount, Is.EqualTo(6));
            var labels = new HashSet<string>();
            for (int i = 0; i < sweep.TrialCount; i++)
            {
                Assert.That(sweep.TryGetTrial(i, out VehiclePhysicsLabTuningOverride[] overrides, out string label), Is.True);
                Assert.That(overrides, Has.Length.EqualTo(2));
                Assert.That(labels.Add(label), Is.True);
            }
        }

        [Test]
        public void ReportValidationRejectsNonFiniteOrUnboundedSamples()
        {
            var report = new VehiclePhysicsLabRunReport
            {
                samples = new[] { Sample(0f, 0f) },
                metrics = Array.Empty<VehiclePhysicsLabMetric>(),
                diagnostics = Array.Empty<string>(),
                collisions = Array.Empty<VehiclePhysicsLabCollisionEvent>(),
                appliedOverrides = Array.Empty<VehiclePhysicsLabTuningOverride>()
            };
            VehiclePhysicsLabEditorOperations.ValidateReport(report);

            report.samples[0].speedKph = float.NaN;
            Assert.That(() => VehiclePhysicsLabEditorOperations.ValidateReport(report),
                Throws.ArgumentException.With.Message.Contains("PHYSICS_LAB_REPORT_SAMPLE"));
        }

        [Test]
        public void IsolatedRunnerUsesLocalPhysicsAndCleansPreviewScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Assert.Ignore("The physics lab is intentionally editor-only and cannot run in Play mode.");
            }

            VehiclePhysicsLabDefinition experiment = CreateDefinition();
            int scenesBefore = SceneManager.sceneCount;
            VehiclePhysicsLabRunReport report;
            using (var runner = new VehiclePhysicsLabRunner(experiment, () => VehicleInputState.Neutral))
            {
                Assert.That(runner.PreviewScene.GetPhysicsScene().IsValid(), Is.True);
                Assert.That(runner.PreviewScene.GetPhysicsScene().Equals(Physics.defaultPhysicsScene), Is.False);

                int guard = 0;
                while (!runner.IsDone && guard++ < 200)
                {
                    runner.Advance(20d);
                }

                Assert.That(runner.IsDone, Is.True, "The bounded runner did not terminate within the editor test budget.");
                report = runner.Report;
            }

            Assert.That(report, Is.Not.Null);
            Assert.That(report.samples, Is.Not.Null.And.Not.Empty);
            Assert.That(report.status, Is.EqualTo(VehiclePhysicsLabResultStatus.Passed));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(scenesBefore));
        }

        private VehiclePhysicsLabDefinition CreateDefinition()
        {
            RacingVehicleSetup setup = Own(ScriptableObject.CreateInstance<RacingVehicleSetup>());
            setup.id = Guid.NewGuid().ToString("N");
            setup.tuning = Own(VehicleTuning.CreateStreetRacer());
            setup.upgrades = Array.Empty<VehiclePerformanceUpgradeDefinition>();

            VehiclePhysicsLabTrack track = Own(ScriptableObject.CreateInstance<VehiclePhysicsLabTrack>());
            track.id = Guid.NewGuid().ToString("N");

            VehiclePhysicsLabDefinition experiment = Own(ScriptableObject.CreateInstance<VehiclePhysicsLabDefinition>());
            experiment.id = Guid.NewGuid().ToString("N");
            experiment.vehicle = setup;
            experiment.track = track;
            experiment.warmupSeconds = 0f;
            experiment.safety.maximumSeconds = 1f;
            experiment.capture.maximumSamples = 128;
            experiment.evaluation.requireTargetSpeed = false;
            return experiment;
        }

        private T Own<T>(T value) where T : UnityEngine.Object
        {
            owned.Add(value);
            return value;
        }

        private static VehiclePhysicsLabSample Sample(
            float speedKph,
            float measurementTime,
            Vector3? position = null,
            bool brake = false)
        {
            return new VehiclePhysicsLabSample
            {
                time = measurementTime,
                measurementTime = measurementTime,
                position = position ?? Vector3.zero,
                rotation = Quaternion.identity,
                velocity = Vector3.forward * (speedKph / 3.6f),
                acceleration = Vector3.zero,
                speedKph = speedKph,
                lateralAccelerationMps2 = 0f,
                finalInput = new VehicleInputState { Brake = brake ? 1f : 0f },
                wheels = Array.Empty<VehiclePhysicsLabWheelSample>()
            };
        }
    }
}
