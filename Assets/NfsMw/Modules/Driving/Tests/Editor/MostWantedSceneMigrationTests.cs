using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NfsMwRemaster.Driving.Editor;
using NfsMwRemaster.Driving.Editor.DrivingMechanics;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Tests
{
    /// <summary>
    /// Offline migration contracts plus read-only, post-migration integration checks.
    /// Integration cases require the migration's isolated-copy marker. They never save a
    /// scene, create an asset, refresh the source installation or step the default world.
    /// Prefab smoke checks use the production controller, not a second solver.
    /// </summary>
    [Category("DrivingMechanicsOffline")]
    public sealed class MostWantedSceneMigrationTests
    {
        private const string CaptureSha256 = "47bdd24fb8ef208c0ec41f920944d388588603b404a6855b5313be8791984614";
        private const string BaselineRelativePath = "Tools/VehicleFramework/Evidence/Baseline/reference-data.json";
        private const string BaselineSha256 = "17a9d3a9b68387e2fd6bb3a7c9bcaa76ee5fe4595c4b2b557126471c2bbc3ccc";
        private const string BmwDraftPath = "Assets/NfsMw/Content/Vehicles/Street/BMW/M3 E42/Editor/BMWM3GTRE46.asset";
        private const string ExampleRoot = "Assets/NfsMw/Modules/Driving/Examples/VehicleFramework/";
        private const float FixedStep = .02f;
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        { TypeNameHandling = TypeNameHandling.None, MaxDepth = 64 };

        private readonly List<Object> owned = new List<Object>();
        private readonly Dictionary<string, string> fileHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<Object, string> assetJson = new Dictionary<Object, string>();
        private readonly Dictionary<Object, bool> assetDirty = new Dictionary<Object, bool>();
        private MostWantedHandlingReport original, capture;
        private string originalJson, capturePath, baselinePath;

        [OneTimeSetUp]
        public void LoadHistoricalEvidenceWithoutRecapturing()
        {
            string root = Environment.GetEnvironmentVariable("MOST_WANTED_DRIVING_TEST_ROOT");
            if (string.IsNullOrWhiteSpace(root)) root = ProjectRoot;
            capturePath = Path.Combine(root, MostWantedSceneMigration.CapturePath);
            baselinePath = Path.Combine(root, BaselineRelativePath);
            Assert.That(File.Exists(capturePath), Is.True, "Include offline Tools evidence or set MOST_WANTED_DRIVING_TEST_ROOT.");
            Assert.That(File.Exists(baselinePath), Is.True, "Historical baseline evidence is required, not regenerated.");
            AssertEvidenceHashes();
            original = MostWantedDrivingWindow.ReadReport(capturePath);
            originalJson = JsonConvert.SerializeObject(original, JsonSettings);
        }

        [SetUp]
        public void CloneCaptureForEachTest()
        {
            capture = JsonConvert.DeserializeObject<MostWantedHandlingReport>(originalJson, JsonSettings);
            Assert.That(capture, Is.Not.SameAs(original));
            Assert.That(capture.records[0].fields, Is.Not.SameAs(original.records[0].fields));
        }

        [TearDown]
        public void DisposeTransientObjectsAndVerifyNoSourceWasChanged()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
            try
            {
                foreach (var pair in fileHashes)
                    Assert.That(MostWantedSceneMigration.HashFile(pair.Key), Is.EqualTo(pair.Value), "File changed: " + pair.Key);
                foreach (var pair in assetJson)
                {
                    Assert.That(pair.Key != null, Is.True, "A persistent source asset was destroyed.");
                    if (pair.Key == null) continue;
                    Assert.That(EditorJsonUtility.ToJson(pair.Key), Is.EqualTo(pair.Value), "Shared asset changed: " + AssetDatabase.GetAssetPath(pair.Key));
                    Assert.That(EditorUtility.IsDirty(pair.Key), Is.EqualTo(assetDirty[pair.Key]), "Shared asset dirtied: " + AssetDatabase.GetAssetPath(pair.Key));
                }
                Assert.That(JsonConvert.SerializeObject(original, JsonSettings), Is.EqualTo(originalJson), "Shared capture graph changed.");
                Assert.That(JsonConvert.SerializeObject(capture, JsonSettings), Is.EqualTo(originalJson), "Candidate generation changed its source clone.");
                AssertEvidenceHashes();
            }
            finally { fileHashes.Clear(); assetJson.Clear(); assetDirty.Clear(); }
        }

        [TestCase(true, .46782207f)]
        [TestCase(false, .46782207f)]
        [TestCase(false, .381f)]
        public void CandidateRetainsAuthoredContactGeometry(bool bmwSource, float radius)
        {
            var baseline = AuthoredBaseline();
            baseline.tires.wheelRadius = radius;
            string before = JsonUtility.ToJson(baseline);
            using (var candidate = MostWantedSceneMigration.CreateCandidate(baseline, capture, bmwSource, false))
            {
                AssertReference(candidate.Tuning, "candidate");
                AssertGeometry(baseline, candidate.Tuning);
                Assert.That(candidate.Tuning.mostWanted.rearWheelRadiusScale, Is.EqualTo(1f));
                Assert.That(candidate.Tuning.name, Is.EqualTo(baseline.name));
                Assert.That(candidate.Tuning.displayName, Is.EqualTo(baseline.displayName));
                Assert.That(candidate.Report.mapped.Any(item => item.field == "RIM_SIZE"
                    || item.field == "SECTION_WIDTH" || item.field == "ASPECT_RATIO"), Is.False);
                if (bmwSource)
                {
                    Assert.That(candidate.Tuning.chassis.mass, Is.EqualTo(1300f));
                    Assert.That(candidate.Tuning.engine.maxTorqueNewtonMeters, Is.EqualTo(633.1586f).Within(.001f));
                    Assert.That(candidate.Tuning.tires.wheelRadius, Is.Not.EqualTo(.3433f), "Source tyre dimensions must not resize the authored BMW mesh.");
                }
            }
            Assert.That(JsonUtility.ToJson(baseline), Is.EqualTo(before));
        }

        [TestCase(VehicleDriveLayout.Fwd)]
        [TestCase(VehicleDriveLayout.Rwd)]
        [TestCase(VehicleDriveLayout.Awd)]
        [TestCase(VehicleDriveLayout.LegacyBindings)]
        public void GenericCandidateRetainsVehicleStatisticsAndLabelsDonorAdaptation(VehicleDriveLayout layout)
        {
            var baseline = AuthoredBaseline();
            baseline.driveLayout = layout;
            string before = JsonUtility.ToJson(baseline);
            using var candidate = MostWantedSceneMigration.CreateCandidate(baseline, capture, false, false);
            var tuning = candidate.Tuning;
            AssertReference(tuning, "generic " + layout);
            AssertGeometry(baseline, tuning);
            Assert.That(tuning.chassis.mass, Is.EqualTo(baseline.chassis.mass));
            Assert.That(tuning.chassis.maxSpeedKph, Is.EqualTo(baseline.chassis.maxSpeedKph));
            Assert.That(tuning.chassis.yawInertiaMultiplier, Is.EqualTo(baseline.chassis.yawInertiaMultiplier));
            Assert.That(tuning.driveLayout, Is.EqualTo(layout));
            Assert.That(tuning.awdFrontTorqueBias, Is.EqualTo(baseline.awdFrontTorqueBias));
            Assert.That(tuning.differential, Is.EqualTo(baseline.differential));
            Assert.That(tuning.differentialPreload, Is.EqualTo(baseline.differentialPreload));
            Assert.That(tuning.differentialLockStrength, Is.EqualTo(baseline.differentialLockStrength));
            Assert.That(tuning.transmissionMode, Is.EqualTo(baseline.transmissionMode));
            Assert.That(tuning.speedGovernor, Is.EqualTo(baseline.speedGovernor));
            Assert.That(JsonUtility.ToJson(tuning.engine), Is.EqualTo(JsonUtility.ToJson(baseline.engine)), "Power, torque curve, ratios, shift points and induction remain authored.");
            Assert.That(JsonUtility.ToJson(tuning.tires), Is.EqualTo(JsonUtility.ToJson(baseline.tires)), "Contact, spring, damping and grip calibration remain authored.");
            Assert.That(tuning.controls.serviceBrakeTorque, Is.EqualTo(baseline.controls.serviceBrakeTorque));
            Assert.That(tuning.controls.handbrakeTorque, Is.EqualTo(baseline.controls.handbrakeTorque));
            Assert.That(tuning.controls.frontBrakeBias, Is.EqualTo(baseline.controls.frontBrakeBias));
            Assert.That(tuning.mostWanted.gearEfficiency, Is.EqualTo(Enumerable.Repeat(baseline.engine.drivelineEfficiency, baseline.engine.gearRatios.Length)));
            Assert.That(tuning.mostWanted.reverseGearEfficiency, Is.EqualTo(baseline.engine.drivelineEfficiency));
            Assert.That(tuning.mostWanted.rearSpringScale, Is.EqualTo(1f));
            Assert.That(tuning.mostWanted.rearDamperScale, Is.EqualTo(1f));
            Assert.That(tuning.mostWanted.frontReboundDamperScale, Is.EqualTo(1f));
            Assert.That(tuning.mostWanted.rearReboundDamperScale, Is.EqualTo(1f));
            Assert.That(tuning.mostWanted.linearDownforceCoefficient, Is.EqualTo(baseline.aero.downforceCoefficient * (100f / 3.6f)).Within(.0001f));
            Assert.That(tuning.mostWanted.nitrousTorqueBoost, Is.EqualTo(baseline.engine.nitrousTorque / baseline.engine.maxTorqueNewtonMeters).Within(.00001f));
            Assert.That(candidate.Report.vehicle, Is.EqualTo(layout == VehicleDriveLayout.Fwd ? "gti" : "bmwm3gtre46"));
            Assert.That(candidate.Report.fidelity, Does.Contain("adaptation").And.Contain(baseline.displayName));
            Assert.That(candidate.Report.mapped.All(item => item.field == "TORQUE" || item.field == "ENGINE_BRAKING"), Is.True);
            Assert.That(JsonUtility.ToJson(baseline), Is.EqualTo(before));
        }

        [TestCase(VehicleDriveLayout.Fwd)]
        [TestCase(VehicleDriveLayout.Rwd)]
        public void NpcCandidateKeepsExistingSteeringAndShiftControlLaws(VehicleDriveLayout layout)
        {
            var baseline = AuthoredBaseline();
            baseline.driveLayout = layout;
            using var candidate = MostWantedSceneMigration.CreateCandidate(baseline, capture, false, true);
            AssertReference(candidate.Tuning, "NPC candidate");
            AssertNpcControlPolicy(candidate.Tuning, "NPC candidate");
            Assert.That(JsonUtility.ToJson(candidate.Tuning.controls), Is.EqualTo(JsonUtility.ToJson(baseline.controls)));
            Assert.That(JsonUtility.ToJson(candidate.Tuning.engine), Is.EqualTo(JsonUtility.ToJson(baseline.engine)));
            Assert.That(candidate.Report.retainedAdaptations.Any(item => item.Contains("NPC")), Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CandidateOwnsDeepCopiesAndDisposalDoesNotDestroyBaseline(bool bmwSource)
        {
            var baseline = AuthoredBaseline();
            string before = JsonUtility.ToJson(baseline);
            VehicleTuning temporary;
            using (var candidate = MostWantedSceneMigration.CreateCandidate(baseline, capture, bmwSource, false))
            {
                temporary = candidate.Tuning;
                Assert.That(temporary, Is.Not.SameAs(baseline));
                Assert.That(EditorUtility.IsPersistent(temporary), Is.False);
                Assert.That(temporary.chassis, Is.Not.SameAs(baseline.chassis));
                Assert.That(temporary.tires, Is.Not.SameAs(baseline.tires));
                Assert.That(temporary.engine.gearRatios, Is.Not.SameAs(baseline.engine.gearRatios));
                Assert.That(temporary.engine.torqueCurve, Is.Not.SameAs(baseline.engine.torqueCurve));
                Assert.That(temporary.mostWanted, Is.Not.SameAs(baseline.mostWanted));
                temporary.engine.gearRatios[0] = 9f;
                temporary.engine.torqueCurve.MoveKey(0, new Keyframe(0f, .123f));
                temporary.tires.wheelRadius = .8f;
                temporary.chassis.centerOfMass = Vector3.one;
                temporary.mostWanted.normalizedTorque[0] = .123f;
            }
            Assert.That(temporary == null, Is.True, "Disposable candidate leaked its transient tuning.");
            Assert.That(baseline != null, Is.True);
            Assert.That(JsonUtility.ToJson(baseline), Is.EqualTo(before));
        }

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(false, true)]
        public void AlreadyEnabledCandidateIsRetainedWithoutRecalibration(bool bmwSource, bool npc)
        {
            var authored = AuthoredBaseline();
            using var first = MostWantedSceneMigration.CreateCandidate(authored, capture, bmwSource, npc);
            var enabled = first.Tuning;
            enabled.chassis.mass = 1537f;
            enabled.mostWanted.normalizedTorque[0] = .123f;
            enabled.mostWanted.steeringCoefficient = .83f;
            enabled.mostWanted.rearWheelRadiusScale = 1.07f;
            enabled.mostWanted.useSteeringTables = false;
            enabled.mostWanted.torqueBasedShifting = false;
            string before = JsonUtility.ToJson(enabled);
            using (var second = MostWantedSceneMigration.CreateCandidate(enabled, capture, bmwSource, npc))
            {
                AssertReference(second.Tuning, "already enabled");
                Assert.That(JsonUtility.ToJson(second.Tuning), Is.EqualTo(before));
                Assert.That(second.Report.mapped, Is.Empty);
                Assert.That(second.Report.fidelity, Does.Contain("Already-enabled"));
                Assert.That(second.Tuning.mostWanted.normalizedTorque, Is.Not.SameAs(enabled.mostWanted.normalizedTorque));
                second.Tuning.mostWanted.normalizedTorque[0] = .7f;
                second.Tuning.engine.gearRatios[0] = 9f;
            }
            Assert.That(JsonUtility.ToJson(enabled), Is.EqualTo(before));
        }

        [TestCase("Assets/_Recovery/0.unity", true)]
        [TestCase("Assets\\_Recovery\\0 (1).unity", true)]
        [TestCase("Assets/NfsMw/Scenes/Game/DrivingDemo.unity", false)]
        [TestCase("Assets/NfsMw/Scenes/Tests/Traffic/TrafficStop.unity", false)]
        [TestCase("Assets/NfsMw/Modules/Driving/Examples/VehicleFramework/hatch-fwd/Driving.unity", false)]
        [TestCase("Assets/_RecoveryExamples/Driving.unity", false)]
        public void RecoveryExclusionIsExplicitAndDoesNotHideDrivingExamples(string path, bool excluded)
            => Assert.That(MostWantedSceneMigration.IsRecoveryScene(path), Is.EqualTo(excluded));

        [TestCase("bmwm3gtre46", VehicleDriveLayout.Rwd, .46782207f)]
        [TestCase("hatch-fwd", VehicleDriveLayout.Fwd, .34f)]
        [TestCase("coupe-rwd", VehicleDriveLayout.Rwd, .34f)]
        [Category("DrivingMechanicsPostMigration")]
        public void ExistingPrefabDraftDefinitionAndSetupKeepIdentityAndFitment(string id, VehicleDriveLayout layout, float radius)
        {
            RequireValidationCopy();
            var draft = LoadDraft(id);
            var definition = draft.runtimeDefinition;
            var setup = draft.physicsLabSetup;
            Assert.That(definition, Is.Not.Null, id);
            Assert.That(setup, Is.Not.Null, id);
            Assert.That(definition.vehicleId, Is.EqualTo(id));
            Assert.That(definition.variantId, Is.EqualTo(id == "bmwm3gtre46" ? "scene-stock" : "street"));
            Assert.That(definition.manufacturer, Is.EqualTo(id == "bmwm3gtre46" ? "BMW" : "Framework"));
            Assert.That(definition.model, Is.EqualTo(id == "bmwm3gtre46" ? "M3 E42" : id == "hatch-fwd" ? "Compact 180" : "Coupe 390"));
            Assert.That(definition.year, Is.EqualTo(2005));
            Assert.That(draft.modelYear, Is.EqualTo(definition.year));
            Assert.That(definition.factoryTuning, Is.SameAs(draft.tuning));
            Assert.That(setup.tuning, Is.SameAs(draft.tuning));
            Assert.That(setup.definition, Is.SameAs(definition));
            Assert.That(definition.prefab.gameObject, Is.SameAs(draft.vehiclePrefab));
            var controller = draft.vehiclePrefab.GetComponent<VehicleController>();
            Assert.That(controller.FactoryTuning, Is.SameAs(draft.tuning));
            Assert.That(controller.GetComponent<VehicleConfiguration>().Definition, Is.SameAs(definition));
            AssertReference(draft.tuning, id);
            Assert.That(draft.tuning.driveLayout, Is.EqualTo(layout));
            Assert.That(draft.tuning.tires.wheelRadius, Is.EqualTo(radius).Within(.000001f));
            Assert.That(draft.tuning.mostWanted.rearWheelRadiusScale, Is.EqualTo(1f));
            Assert.That(controller.Wheels, Has.Length.EqualTo(4));
            Assert.That(controller.Wheels.All(wheel => wheel != null && wheel.transform.IsChildOf(controller.transform)), Is.True);
            var effective = setup.CreateEffectiveTuning();
            try { AssertReference(effective, "Setup " + id); AssertGeometry(draft.tuning, effective); }
            finally { if (effective != null) Object.DestroyImmediate(effective); }
            if (id != "bmwm3gtre46") Assert.That(draft.modelId, Is.EqualTo(id), "Generic examples must not be relabelled as the donor car.");
        }

        [TestCase("bmwm3gtre46")]
        [TestCase("hatch-fwd")]
        [TestCase("coupe-rwd")]
        [Category("DrivingMechanicsPostMigration")]
        public void ActualPrefabUsesDefinitionPriorityAcrossConfigureRestoreAndPoolReuse(string id)
        {
            RequireValidationCopy();
            var draft = LoadDraft(id);
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var physics = scene.GetPhysicsScene();
                Assert.That(physics.IsValid(), Is.True);
                Assert.That(physics, Is.Not.EqualTo(Physics.defaultPhysicsScene));
                var root = (GameObject)PrefabUtility.InstantiatePrefab(draft.vehiclePrefab, scene);
                root.SetActive(false);
                root.transform.SetPositionAndRotation(new Vector3(0f, 1.1f, 0f), Quaternion.identity);
                var vehicle = root.GetComponent<VehicleController>();
                var configuration = root.GetComponent<VehicleConfiguration>();
                var geometry = vehicle.Wheels.Select(wheel => new WheelGeometry(wheel)).ToArray();
                var input = root.AddComponent<RacingSimulationInput>();
                var decoy = AuthoredBaseline();
                decoy.chassis.mass = 1111f;
                vehicle.SetManualSimulation(true);
                // A legacy stock argument deliberately disagrees with the migrated definition.
                // Only the production module chain can demonstrate which source wins.
                vehicle.ConfigureForRuntime(decoy, input, vehicle.Wheels);
                Assert.That(vehicle.FactoryTuning, Is.SameAs(decoy));
                AssertReference(vehicle.Tuning, id + " definition overrides legacy stock");
                Assert.That(vehicle.Body.mass, Is.EqualTo(draft.tuning.chassis.mass).Within(.001f));
                for (int pass = 0; pass < 3; pass++)
                {
                    vehicle.ConfigureForRuntime(draft.tuning, input, vehicle.Wheels);
                    foreach (var wheel in geometry) wheel.AssertUnchanged(draft.tuning);
                    AssertReference(vehicle.Tuning, id + " configure " + pass);
                }
                var saved = new CareerVehicleData();
                configuration.Capture(saved);
                root.GetComponent<VehiclePerformanceSystem>().Capture(saved);
                root.GetComponent<VehicleCustomizationSystem>()?.Capture(saved);
                var ground = new GameObject("Migration test asphalt");
                SceneManager.MoveGameObjectToScene(ground, scene);
                ground.transform.position = new Vector3(0f, -.5f, 100f);
                ground.AddComponent<BoxCollider>().size = new Vector3(100f, 1f, 500f);
                ground.AddComponent<VehicleSurface>().Configure("Migration test asphalt", 1f);
                root.SetActive(true);
                Physics.SyncTransforms();
                for (int tick = 0; tick < 100; tick++) // Exactly two seconds; no default-scene stepping.
                {
                    input.Current = tick < 20 ? new VehicleInputState { Brake = 1f, Handbrake = true }
                        : new VehicleInputState { Throttle = .7f, Steering = tick >= 80 ? .1f : 0f };
                    if (tick == 40)
                        Assert.That(configuration.TrySetAdjustments(new[] { new VehicleTuningAdjustment
                        { parameter = VehiclePhysicsLabTuningParameter.Mass, value = draft.tuning.chassis.mass + 10f } }, out string failure), Is.True, failure);
                    if (tick == 70)
                        Assert.That(configuration.Restore(saved, out string restoreFailure), Is.True, restoreFailure);
                    vehicle.StepSimulation(FixedStep);
                    physics.Simulate(FixedStep);
                    AssertReference(vehicle.Tuning, id + " tick " + tick);
                    AssertFiniteVehicle(vehicle);
                    if (tick == 40) Assert.That(vehicle.Body.mass, Is.EqualTo(draft.tuning.chassis.mass + 10f).Within(.001f));
                    if (tick == 70) Assert.That(vehicle.Body.mass, Is.EqualTo(draft.tuning.chassis.mass).Within(.001f));
                }
                root.SetActive(false);
                vehicle.ResetSimulationForPool();
                AssertReference(vehicle.Tuning, id + " pool reset");
                Assert.That(configuration.Definition, Is.SameAs(draft.runtimeDefinition));
                foreach (var wheel in geometry) wheel.AssertUnchanged(draft.tuning);
            }
            finally { if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene); }
        }

        private VehicleProfileDraft LoadDraft(string id)
        {
            string path = id == "bmwm3gtre46" ? BmwDraftPath : ExampleRoot + id + "/Profile.asset";
            var draft = AssetDatabase.LoadAssetAtPath<VehicleProfileDraft>(path);
            Assert.That(draft, Is.Not.Null, path);
            Assert.That(draft.vehiclePrefab, Is.Not.Null, path);
            GuardAsset(draft); GuardAsset(draft.tuning); GuardAsset(draft.runtimeDefinition); GuardAsset(draft.physicsLabSetup);
            GuardFile(AssetDatabase.GetAssetPath(draft.vehiclePrefab));
            return draft;
        }

        private VehicleTuning AuthoredBaseline()
        {
            var tuning = Own(VehicleTuning.CreateStreetRacer());
            tuning.name = "Migration authored fixture"; tuning.displayName = "Anonymous authored vehicle";
            tuning.chassis.mass = 1627f; tuning.chassis.centerOfMass = new Vector3(.12f, -.41f, .07f);
            tuning.chassis.yawInertiaMultiplier = 1.15f; tuning.chassis.maxSpeedKph = 237f;
            tuning.driveLayout = VehicleDriveLayout.Rwd;
            tuning.awdFrontTorqueBias = .37f; tuning.differential = VehicleDifferentialMode.LimitedSlip;
            tuning.differentialPreload = 33f; tuning.differentialLockStrength = .22f;
            tuning.engine.maxTorqueNewtonMeters = 287f; tuning.engine.gearRatios = new[] { 3.6f, 2.25f, 1.47f, 1.05f, .81f };
            tuning.engine.drivelineEfficiency = .83f; tuning.engine.finalDrive = 3.73f;
            tuning.engine.forcedInduction = true; tuning.engine.boostTorqueMultiplier = 1.17f;
            tuning.tires.wheelRadius = .46782207f; tuning.tires.wheelWidth = .281f;
            tuning.tires.wheelLateralOffset = .027f; tuning.tires.suspensionRestLength = .373f;
            tuning.tires.suspensionTravel = .191f; tuning.tires.wheelMass = 23f;
            tuning.tires.springRate = 37651f; tuning.tires.damperRate = 5123f;
            RacingLineSnapshot.ValidateTuning(tuning);
            return tuning;
        }

        private static void AssertGeometry(VehicleTuning expected, VehicleTuning actual)
        {
            Assert.That(actual.chassis.centerOfMass, Is.EqualTo(expected.chassis.centerOfMass));
            Assert.That(actual.tires.wheelRadius, Is.EqualTo(expected.tires.wheelRadius));
            Assert.That(actual.tires.wheelWidth, Is.EqualTo(expected.tires.wheelWidth));
            Assert.That(actual.tires.wheelLateralOffset, Is.EqualTo(expected.tires.wheelLateralOffset));
            Assert.That(actual.tires.suspensionRestLength, Is.EqualTo(expected.tires.suspensionRestLength));
            Assert.That(actual.tires.suspensionTravel, Is.EqualTo(expected.tires.suspensionTravel));
            Assert.That(actual.tires.wheelMass, Is.EqualTo(expected.tires.wheelMass));
        }

        private sealed class WheelGeometry
        {
            private readonly VehicleWheel wheel;
            private readonly Transform visual;
            private readonly Vector3 anchor, scale;
            public WheelGeometry(VehicleWheel wheel)
            {
                this.wheel = wheel;
                visual = new SerializedObject(wheel).FindProperty("visual").objectReferenceValue as Transform;
                Assert.That(visual, Is.Not.Null, wheel.name + ": authored visual binding missing.");
                anchor = wheel.transform.localPosition; scale = visual.localScale;
            }
            public void AssertUnchanged(VehicleTuning tuning)
            {
                Assert.That(Vector3.Distance(wheel.transform.localPosition, anchor), Is.LessThan(.0001f), wheel.name + ": wheel anchor moved.");
                Assert.That(Vector3.Distance(visual.localScale, scale), Is.LessThan(.0001f), wheel.name + ": wheel mesh was rescaled.");
                Assert.That(wheel.Radius, Is.EqualTo(tuning.tires.wheelRadius).Within(.000001f));
            }
        }

        private static void AssertReference(VehicleTuning tuning, string context)
        {
            Assert.That(tuning, Is.Not.Null, context);
            Assert.That(tuning.UsesMostWantedReference, Is.True, context + ": reference mode is not enabled.");
            Assert.DoesNotThrow(() => RacingLineSnapshot.ValidateTuning(tuning), context);
        }

        private static void AssertNpcControlPolicy(VehicleTuning tuning, string context)
        {
            Assert.That(tuning.mostWanted.useSteeringTables, Is.False, context + ": do not stack player steering on the NPC control law.");
            Assert.That(tuning.mostWanted.torqueBasedShifting, Is.False, context + ": retain authored NPC shift thresholds.");
        }

        private static void AssertFiniteVehicle(VehicleController vehicle)
        {
            Assert.That(float.IsFinite(vehicle.Body.position.sqrMagnitude) && float.IsFinite(vehicle.Body.linearVelocity.sqrMagnitude)
                && float.IsFinite(vehicle.Body.angularVelocity.sqrMagnitude) && float.IsFinite(vehicle.Telemetry.EngineRpm), Is.True);
            foreach (var wheel in vehicle.Wheels)
                Assert.That(float.IsFinite(wheel.NormalLoad) && float.IsFinite(wheel.LongitudinalForce) && float.IsFinite(wheel.LateralForce), Is.True);
        }

        private void GuardAsset(Object asset)
        {
            if (asset == null || !EditorUtility.IsPersistent(asset) || assetJson.ContainsKey(asset)) return;
            assetJson.Add(asset, EditorJsonUtility.ToJson(asset)); assetDirty.Add(asset, EditorUtility.IsDirty(asset));
            GuardFile(AssetDatabase.GetAssetPath(asset));
        }

        private void GuardFile(string path)
        {
            path = FullPath(path);
            if (!fileHashes.ContainsKey(path)) fileHashes.Add(path, MostWantedSceneMigration.HashFile(path));
        }

        private void AssertEvidenceHashes()
        {
            Assert.That(MostWantedSceneMigration.HashFile(capturePath), Is.EqualTo(CaptureSha256), "Preserved actual capture was overwritten.");
            Assert.That(MostWantedSceneMigration.HashFile(baselinePath), Is.EqualTo(BaselineSha256), "Historical baseline was overwritten.");
        }

        private static void RequireValidationCopy()
        {
            string marker = Path.Combine(ProjectRoot, ".mw-driving-validation-source");
            if (!File.Exists(marker)) Assert.Ignore("Post-migration integration requires a task-owned validation copy marked .mw-driving-validation-source; run candidate tests without it.");
            string sourceRoot = File.ReadAllText(marker).Trim();
            Assert.That(sourceRoot, Is.Not.Empty);
            Assert.That(Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar), Is.Not.EqualTo(ProjectRoot.TrimEnd(Path.DirectorySeparatorChar)), "Do not run post-migration scene integration in the source project.");
            Assert.That(EditorApplication.isPlayingOrWillChangePlaymode, Is.False);
        }

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string FullPath(string path) => Path.IsPathRooted(path) ? path : Path.Combine(ProjectRoot, path);
        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
    }
}
