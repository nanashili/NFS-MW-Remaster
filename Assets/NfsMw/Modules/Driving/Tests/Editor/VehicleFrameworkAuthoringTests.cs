using System;
using System.Linq;
using System.IO;
using NfsMwRemaster.Driving.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleFrameworkAuthoringTests
    {
        private string folder;
        private VehicleProfileDraft hatch, coupe;
        [OneTimeSetUp] public void CreateExamples()
        {
            folder = "Assets/FrameworkAuthoringTest-" + Guid.NewGuid().ToString("N");
            hatch = DrivingDemoBuilder.CreateFrameworkExample(folder, "hatch", VehicleDriveLayout.Fwd, Color.blue);
            coupe = DrivingDemoBuilder.CreateFrameworkExample(folder, "coupe", VehicleDriveLayout.Rwd, Color.red);
        }
        [OneTimeTearDown] public void Cleanup() { if (!string.IsNullOrEmpty(folder)) AssetDatabase.DeleteAsset(folder); }

        [Test] public void ModelBindingsAreRemappedToGeneratedPrefabAndUpdatingPreservesIdentity()
        {
            foreach (var draft in new[] { hatch, coupe })
            {
                var root = draft.vehiclePrefab;
                var bindings = root.GetComponent<VehiclePresentationBindings>();
                Assert.That(bindings.steeringWheel.IsChildOf(root.transform), Is.True);
                Assert.That(bindings.cockpitCameraAnchor.IsChildOf(root.transform), Is.True);
                Assert.That(bindings.headlights.lights.All(l => l != null && l.transform.IsChildOf(root.transform)), Is.True);
                Assert.That(bindings.wiperPivots.All(w => w.IsChildOf(root.transform)), Is.True);
                Assert.That(root.GetComponent<VehicleMirrorRenderer>().Mirrors.All(m => m.view.IsChildOf(root.transform)), Is.True);
                Assert.That(root.GetComponent<VehicleMirrorRenderer>().enabled, Is.True);
                Assert.That(root.GetComponent<VehicleInputAuthority>(), Is.Not.Null);
                Assert.That(VehicleProfileAssembly.Validate(draft), Is.Empty);
                var path = AssetDatabase.GetAssetPath(root); var guid = AssetDatabase.AssetPathToGUID(path);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(bindings.steeringWheel, out string oldGuid, out long oldLocalId);
                VehicleProfileAssembly.UpdatePrefabConfiguration(draft);
                Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
                var updated = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<VehiclePresentationBindings>();
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(updated.steeringWheel, out string newGuid, out long newLocalId);
                Assert.That(newGuid, Is.EqualTo(oldGuid)); Assert.That(newLocalId, Is.EqualTo(oldLocalId));
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void AuthoredVehicleDrivesInstallsPreviewsAndRestoresThroughCareer(bool useCoupe)
        {
            var draft = useCoupe ? coupe : hatch;
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(draft.vehiclePrefab, scene);
                root.transform.position = new Vector3(0, .8f, 0);
                var other = (GameObject)PrefabUtility.InstantiatePrefab(draft.vehiclePrefab, scene); other.transform.position = new Vector3(10, .8f, 0);
                var ground = new GameObject("Known flat asphalt"); SceneManager.MoveGameObjectToScene(ground, scene); ground.transform.position = new Vector3(0, -.5f, 100);
                ground.AddComponent<BoxCollider>().size = new Vector3(100, 1, 1000); ground.AddComponent<VehicleSurface>().Configure("Test asphalt", 1);
                var controller = root.GetComponent<VehicleController>(); controller.SetManualSimulation(true);
                var authority = root.GetComponent<VehicleInputAuthority>(); authority.SetNeutral();
                var input = root.AddComponent<FrameworkTestInput>(); input.state = new VehicleInputState { Throttle = 1 };
                Assert.That(authority.TryAcquire(this, input), Is.True);
                Assert.That(authority.TryAcquire(other, input), Is.False, "A competing driver must not replace the current authority.");
                controller.ConfigureForRuntime(draft.tuning, authority, controller.Wheels);
                Physics.SyncTransforms(); var physics = scene.GetPhysicsScene();
                for (int i = 0; i < 300; i++) { controller.StepSimulation(.02f); physics.Simulate(.02f); }
                Assert.That(controller.Telemetry.SpeedKph, Is.GreaterThan(15));
                Assert.That(controller.Telemetry.EngineRpm, Is.GreaterThan(draft.tuning.engine.idleRpm));
                var performance = root.GetComponent<VehiclePerformanceSystem>();
                Assert.That(performance.TryInstall(draft.performance.Upgrades[0], out string failure), Is.True, failure);
                controller.StepSimulation(.02f);
                Assert.That(controller.Tuning.engine.maxTorqueNewtonMeters, Is.EqualTo(draft.tuning.engine.maxTorqueNewtonMeters * 1.2f).Within(.01));
                Assert.That(other.GetComponent<VehiclePerformanceSystem>().Build.Installed, Is.Empty);
                var customization = root.GetComponent<VehicleCustomizationSystem>();
                var kit = draft.customization.Items[0];
                Assert.That(customization.BeginPreview(kit, out failure), Is.True, failure);
                Assert.That(customization.IsPreviewing, Is.True);
                Assert.That(customization.Build.Installed, Is.Empty);
                customization.CancelPreview(); Assert.That(customization.EffectiveBuild.Installed, Is.Empty);
                Assert.That(customization.BeginPreview(kit, out failure), Is.True, failure);
                Assert.That(customization.TryApplyPreview(out failure), Is.True, failure);
                Assert.That(customization.TryInstall(draft.customization.Items[1], out failure), Is.True, failure);
                var configuration = root.GetComponent<VehicleConfiguration>();
                Assert.That(configuration.TrySetAdjustments(new[] { new VehicleTuningAdjustment { parameter = VehiclePhysicsLabTuningParameter.Mass, value = draft.tuning.chassis.mass - 50 } }, out failure), Is.True, failure);
                var profile = root.AddComponent<CareerProfileSystem>(); profile.SetProfileId("integration"); profile.SetActiveVehicleId("instance-123"); profile.SetStorage(root.AddComponent<FrameworkTestStorage>());
                Assert.That(profile.TrySave(out failure), Is.True, failure);
                Assert.That(performance.TryRemove(VehiclePerformanceCategory.Engine, out failure), Is.True, failure);
                Assert.That(customization.TryRemove(VehicleCustomizationCategory.BodyKit, out failure), Is.True, failure);
                Assert.That(configuration.TrySetAdjustments(Array.Empty<VehicleTuningAdjustment>(), out failure), Is.True, failure);
                Assert.That(profile.TryLoad(out failure), Is.True, failure);
                Assert.That(performance.Build.Installed.Single().UpgradeId, Is.EqualTo(draft.performance.Upgrades[0].UpgradeId));
                Assert.That(customization.GetInstalled(VehicleCustomizationCategory.BodyKit), Is.SameAs(kit));
                Assert.That(configuration.Adjustments.Single().value, Is.EqualTo(draft.tuning.chassis.mass - 50));
                Assert.That(profile.ActiveVehicleId, Is.EqualTo("instance-123"));
                Assert.That(draft.tuning.chassis.mass, Is.EqualTo(useCoupe ? 1450 : 1120));
                Assert.That(VehicleFrameworkPanel.TryHandoff(draft, configuration, false, out failure), Is.True, failure);
                using var resolved = VehicleConfigurationResolver.Resolve(draft.tuning, performance.Build, customization.Build, configuration.Adjustments, draft.runtimeDefinition);
                var labTuning = draft.physicsLabSetup.CreateEffectiveTuning();
                try { Assert.That(labTuning.engine.maxTorqueNewtonMeters, Is.EqualTo(resolved.Tuning.engine.maxTorqueNewtonMeters)); Assert.That(labTuning.chassis.mass, Is.EqualTo(resolved.Tuning.chassis.mass)); }
                finally { Object.DestroyImmediate(labTuning); }
                root.SetActive(false); controller.ResetSimulationForPool(); Assert.That(controller.Telemetry.SpeedKph, Is.EqualTo(0));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        [Test] public void LegacyDraftMigrationKeepsItsIdentityAndAssignedAssets()
        {
            using (var obj = new SerializedObject(hatch)) { obj.FindProperty("schema").intValue = 1; obj.ApplyModifiedPropertiesWithoutUndo(); }
            var id = hatch.Id; var tuning = hatch.tuning;
            hatch.MigrateLegacySchema();
            Assert.That(hatch.Schema, Is.EqualTo(VehicleProfileDraft.CurrentSchema)); Assert.That(hatch.Id, Is.EqualTo(id)); Assert.That(hatch.tuning, Is.SameAs(tuning));
        }
        [Test] public void ExamplesHaveDifferentMeasuredLaunchesAndUpgradeImprovesAcceleration()
        {
            float hatchSpeed = MeasureLaunch(hatch, false);
            float coupeSpeed = MeasureLaunch(coupe, false);
            float upgradedSpeed = MeasureLaunch(hatch, true);
            Assert.That(Mathf.Abs(coupeSpeed - hatchSpeed), Is.GreaterThan(1), "Different authored vehicles must produce a measurable driving difference.");
            Assert.That(upgradedSpeed, Is.GreaterThan(hatchSpeed + .1f), "Engine upgrade must increase measured speed over the same six-second input.");
        }

        private static float MeasureLaunch(VehicleProfileDraft draft, bool upgraded)
        {
            var setup = Object.Instantiate(draft.physicsLabSetup);
            var experiment = Object.Instantiate(draft.physicsLabExperiment);
            try
            {
                setup.upgrades = upgraded ? new[] { draft.performance.Upgrades[0] } : Array.Empty<VehiclePerformanceUpgradeDefinition>();
                setup.bodyParts = Array.Empty<VehicleCustomizationDefinition>(); setup.tuningAdjustments = Array.Empty<VehicleTuningAdjustment>();
                experiment.vehicle = setup; experiment.warmupSeconds = 0; experiment.safety.maximumSeconds = 6;
                experiment.capture.maximumSamples = 512; experiment.evaluation.requireTargetSpeed = false;
                using var runner = new VehiclePhysicsLabRunner(experiment);
                while (!runner.IsDone) runner.Advance(20);
                Assert.That(runner.Report.Passed, Is.True, runner.Report.failureMessage);
                var output = Environment.GetEnvironmentVariable("VEHICLE_FRAMEWORK_EVIDENCE");
                if (!string.IsNullOrEmpty(output))
                {
                    string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)), "Examples"); Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, draft.modelId + (upgraded ? "-engine" : "-factory") + ".json"), JsonUtility.ToJson(runner.Report, true));
                }
                return runner.Report.samples.Last().speedKph;
            }
            finally { Object.DestroyImmediate(experiment); Object.DestroyImmediate(setup); }
        }
        [Test] public void PhysicsLabAssetsSurviveImportWithTheirScriptIdentity()
        {
            foreach (var draft in new[] { hatch, coupe })
            {
                foreach (var asset in new UnityEngine.Object[] { draft.physicsLabSetup, draft.physicsLabExperiment, draft.physicsLabExperiment.track })
                {
                    string path = AssetDatabase.GetAssetPath(asset); string guid = AssetDatabase.AssetPathToGUID(path);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    var loaded = AssetDatabase.LoadMainAssetAtPath(path);
                    Assert.That(loaded, Is.Not.Null); Assert.That(loaded.GetType(), Is.EqualTo(asset.GetType()));
                    Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
                }
                Assert.That(draft.audio, Is.Not.Null, "Each example must retain a real authored audio profile.");
            }
        }
    }
    public sealed class FrameworkTestInput : MonoBehaviour, IVehicleInputSource
    { public VehicleInputState state; public VehicleInputState Current => state; public bool ConsumeResetRequest() => false; public bool ConsumeCameraToggleRequest() => false; }
    public sealed class FrameworkTestStorage : MonoBehaviour, ICareerProfileStorage
    {
        private string payload;
        public bool TrySave(string id, string json, out string failure) { payload = json; failure = ""; return true; }
        public bool TryLoad(string id, out string json, out string failure) { json = payload; failure = ""; return payload != null; }
    }
}
