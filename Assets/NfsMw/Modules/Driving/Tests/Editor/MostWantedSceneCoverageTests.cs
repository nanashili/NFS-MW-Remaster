using System;
using System.IO;
using System.Linq;
using NfsMwRemaster.Driving.Editor;
using NfsMwRemaster.Driving.Editor.DrivingMechanics;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    /// <summary>Post-migration tests of saved project content; never save an opened scene.</summary>
    public sealed class MostWantedSceneCoverageTests
    {
        [Test]
        public void EveryRegularDrivingSceneResolvesReferenceTuningIncludingInactiveVehicles()
        {
            int scenes = 0, vehicles = 0, embedded = 0;
            foreach (string path in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.Ordinal))
            {
                if (MostWantedSceneMigration.IsRecoveryScene(path)) continue;
                string text = File.ReadAllText(path);
                bool vehicleDependency = AssetDatabase.GetDependencies(path, true).Any(dependency => dependency.EndsWith(".prefab", StringComparison.Ordinal)
                    && AssetDatabase.LoadAssetAtPath<GameObject>(dependency)?.GetComponentInChildren<VehicleController>(true) != null);
                if (MostWantedSceneMigration.CountScript(text, MostWantedSceneMigration.ControllerGuid) == 0 && !vehicleDependency) continue;
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    var controllers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<VehicleController>(true)).ToArray();
                    Assert.That(controllers, Is.Not.Empty, path);
                    foreach (var controller in controllers)
                    {
                        var factory = controller.FactoryTuning;
                        AssertReference(factory, path + "/" + controller.name);
                        var configuration = controller.GetComponent<VehicleConfiguration>();
                        if (configuration?.Definition != null)
                        {
                            AssertReference(configuration.Definition.factoryTuning, path + " definition");
                            Assert.That(factory, Is.SameAs(configuration.Definition.factoryTuning), path + " has shadowed factory tuning");
                        }
                        var traffic = controller.GetComponent<RoadVehicleMotor>();
                        if (traffic?.VehicleProfile != null)
                            AssertReference(traffic.VehicleProfile.tuning, path + " traffic profile");
                        if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(factory)) || AssetDatabase.GetAssetPath(factory) == path) embedded++;
                        vehicles++;
                    }
                    scenes++;
                }
                finally { EditorSceneManager.CloseScene(scene, true); }
            }
            Assert.That(scenes, Is.GreaterThanOrEqualTo(21));
            Assert.That(embedded, Is.GreaterThanOrEqualTo(5));
            TestContext.WriteLine($"MIGRATED_SCENE_COVERAGE scenes={scenes} vehicles={vehicles} embeddedVehicles={embedded}");
        }

        [Test]
        public void EverySavedTuningAndConfiguredPhysicsSetupUsesReferenceMode()
        {
            int profiles = 0;
            foreach (string path in AssetDatabase.FindAssets("t:VehicleTuning", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                if (MostWantedSceneMigration.IsRecoveryScene(path)) continue;
                AssertReference(AssetDatabase.LoadAssetAtPath<VehicleTuning>(path), path);
                profiles++;
            }
            Assert.That(profiles, Is.GreaterThanOrEqualTo(15));
            foreach (string path in AssetDatabase.FindAssets("t:RacingVehicleSetup", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var setup = AssetDatabase.LoadAssetAtPath<RacingVehicleSetup>(path);
                var factory = setup.definition != null ? setup.definition.factoryTuning : setup.tuning;
                if (factory == null) continue; // An empty editor draft is not a drivable scene configuration.
                AssertReference(factory, path);
                var effective = setup.CreateEffectiveTuning();
                try { AssertReference(effective, path + " resolved"); }
                finally { UnityEngine.Object.DestroyImmediate(effective); }
            }
        }

        [Test]
        public void PlayerReferenceDoesNotDriveBackwardWithoutThrottle()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<VehicleTuning>(MostWantedSceneMigration.StreetTuningPath);
            var setup = ScriptableObject.CreateInstance<RacingVehicleSetup>();
            var track = ScriptableObject.CreateInstance<VehiclePhysicsLabTrack>();
            var experiment = ScriptableObject.CreateInstance<VehiclePhysicsLabDefinition>();
            try
            {
                setup.tuning = tuning;
                experiment.vehicle = setup; experiment.track = track;
                experiment.experiment = VehiclePhysicsLabExperimentKind.Custom;
                experiment.input.mode = VehiclePhysicsLabInputMode.Live;
                experiment.warmupSeconds = 1f; experiment.safety.maximumSeconds = 4f;
                experiment.evaluation.requireTargetSpeed = false;
                using var runner = new VehiclePhysicsLabRunner(experiment, () => VehicleInputState.Neutral);
                int guard = 0; while (!runner.IsDone && guard++ < 500) runner.Advance(20d);
                Assert.That(runner.IsDone, Is.True);
                Assert.That(runner.Report.samples, Is.Not.Empty);
                float drift = runner.Report.samples.Max(sample => Mathf.Abs(sample.position.z));
                Assert.That(drift, Is.LessThan(.5f), "Zero-throttle source braking must not propel an idle car backward.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(experiment); UnityEngine.Object.DestroyImmediate(track); UnityEngine.Object.DestroyImmediate(setup);
            }
        }

        private static void AssertReference(VehicleTuning tuning, string subject)
        {
            Assert.That(tuning, Is.Not.Null, subject);
            Assert.That(tuning.UsesMostWantedReference, Is.True, subject);
            Assert.DoesNotThrow(() => RacingLineSnapshot.ValidateTuning(tuning), subject);
        }
    }
}
