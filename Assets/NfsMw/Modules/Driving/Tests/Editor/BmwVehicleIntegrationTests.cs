using System;
using System.Linq;
using NfsMwRemaster.Driving.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class BmwVehicleIntegrationTests
    {
        private const string DraftPath = "Assets/NfsMw/Content/Vehicles/Street/BMW/M3 E42/Editor/BMWM3GTRE46.asset";
        private VehicleProfileDraft Draft => AssetDatabase.LoadAssetAtPath<VehicleProfileDraft>(DraftPath);
        [Test]
        public void RebuiltBmwPrefabKeepsAuthoredWheelSize()
        {
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/BmwWheelFitmentTest.prefab");
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var prefab = VehicleProfileAssembly.CreatePrefab(Draft, path);
                var vehicle = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, scene)).GetComponent<VehicleController>();
                vehicle.ConfigureForRuntime(Draft.tuning, null, vehicle.Wheels);
                foreach (var wheel in vehicle.Wheels)
                {
                    var visual = (Transform)new SerializedObject(wheel).FindProperty("visual").objectReferenceValue;
                    Assert.That(Vector3.Distance(visual.localScale, Vector3.one), Is.LessThan(.0001f), "Rebuilding through Vehicle Profiles must preserve imported wheel scale.");
                }
            }
            finally { EditorSceneManager.CloseScene(scene, true); AssetDatabase.DeleteAsset(path); }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void BmwWheelSizeRemainsAuthoredWhenFactoryConfigurationStarts(bool prefab)
        {
            var scene = prefab ? EditorSceneManager.NewPreviewScene() : EditorSceneManager.OpenScene("Assets/NfsMw/Scenes/Showcase/WeatherDemo.unity", OpenSceneMode.Additive);
            try
            {
                var vehicle = prefab
                    ? ((GameObject)PrefabUtility.InstantiatePrefab(Draft.vehiclePrefab, scene)).GetComponent<VehicleController>()
                    : scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<VehicleController>()).Single(v => v.name == "Player Vehicle - BMW M3 E42");
                var visuals = vehicle.Wheels.Select(w => (Transform)new SerializedObject(w).FindProperty("visual").objectReferenceValue).ToArray();
                var scales = visuals.Select(v => v.localScale).ToArray();
                vehicle.ConfigureForRuntime(Draft.tuning, null, vehicle.Wheels);
                for (int i = 0; i < visuals.Length; i++)
                    Assert.That(Vector3.Distance(visuals[i].localScale, scales[i]), Is.LessThan(.0001f),
                        $"Wheel {i} grew from {scales[i]} to {visuals[i].localScale} at factory radius {Draft.tuning.tires.wheelRadius}.");
                var larger = Object.Instantiate(Draft.tuning);
                try
                {
                    larger.tires.wheelRadius *= 1.1f;
                    for (int pass = 0; pass < 3; pass++)
                        for (int i = 0; i < visuals.Length; i++)
                        {
                            vehicle.Wheels[i].Configure(vehicle.Body, larger);
                            Assert.That(Vector3.Distance(visuals[i].localScale, scales[i] * 1.1f), Is.LessThan(.0001f), "Intentional fitment must scale once, without accumulating.");
                        }
                    foreach (var wheel in vehicle.Wheels) wheel.Configure(vehicle.Body, Draft.tuning);
                    for (int i = 0; i < visuals.Length; i++)
                        Assert.That(Vector3.Distance(visuals[i].localScale, scales[i]), Is.LessThan(.0001f), "Restoring factory tires must restore authored size.");
                }
                finally { Object.DestroyImmediate(larger); }
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
        [Test]
        public void BmwProfileHasPortableGeometryAndSharedConfiguration()
        {
            var draft = Draft; Assert.That(draft.runtimeDefinition, Is.Not.Null); Assert.That(VehicleProfileAssembly.Validate(draft), Is.Empty);
            Assert.That(draft.runtimeDefinition.Validate(out var failure), Is.True, failure);
            Assert.That(draft.runtimeDefinition.factoryTuning, Is.SameAs(draft.tuning)); Assert.That(draft.tuning.driveLayout, Is.EqualTo(VehicleDriveLayout.Rwd));
            var root = draft.vehiclePrefab; var presentation = root.GetComponent<VehiclePresentationBindings>();
            Assert.That(root.GetComponent<VehicleConfiguration>().Definition, Is.SameAs(draft.runtimeDefinition));
            Assert.That(root.GetComponent<VehicleAudio>().Profile, Is.SameAs(draft.audio));
            Assert.That(root.GetComponent<VehicleInputAuthority>(), Is.Not.Null);
            Assert.That(presentation.wheels.All(w => w.caliper && w.caliper.IsChildOf(root.transform)), Is.True);
            Assert.That(presentation.sideWindows, Has.Length.EqualTo(2)); Assert.That(root.GetComponent<VehicleMirrorRenderer>().Mirrors, Has.Length.EqualTo(2));
            foreach (var socket in draft.assembly.sockets.Where(s => s.id == "paint"))
                Assert.That(socket.stockRenderers.All(r => r.sharedMaterials.Length == 1 && r.sharedMaterial.name == "primary"), Is.True, "Paint must not also recolor glass or interior.");
            var controller = root.GetComponent<VehicleController>();
            for (int i = 0; i < 4; i++)
            {
                var wheel = controller.Wheels[i]; Assert.That(wheel.IsDrivenWheel, Is.EqualTo(i >= 2));
                var visual = new SerializedObject(wheel).FindProperty("visual").objectReferenceValue as Transform;
                var meshes = visual.GetComponentsInChildren<MeshRenderer>(); var b = meshes[0].bounds; foreach (var mesh in meshes) b.Encapsulate(mesh.bounds);
                Assert.That(Vector3.Distance(b.center, visual.position), Is.LessThan(.025f));
                Assert.That(b.size.y * .5f, Is.EqualTo(draft.tuning.tires.wheelRadius).Within(.005f));
            }
        }
        [Test]
        public void WeatherSceneRetainsExistingVehicleAndCareerComponents()
        {
            var scene = EditorSceneManager.OpenScene("Assets/NfsMw/Scenes/Showcase/WeatherDemo.unity", OpenSceneMode.Additive);
            try
            {
                var vehicle = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<VehicleController>()).Single(v => v.name == "Player Vehicle - BMW M3 E42");
                Assert.That(GlobalObjectId.GetGlobalObjectIdSlow(vehicle).targetObjectId, Is.EqualTo(1685628246));
                Assert.That(vehicle.GetComponent<CareerProfileSystem>(), Is.Not.Null); Assert.That(vehicle.GetComponent<VehicleStoreWallet>(), Is.Not.Null);
                Assert.That(vehicle.CameraRig, Is.Not.Null); Assert.That(vehicle.GetComponent<VehicleAudio>().Profile, Is.SameAs(Draft.audio));
                Assert.That(vehicle.GetComponent<VehicleConfiguration>().Definition, Is.SameAs(Draft.runtimeDefinition));
                Assert.That(vehicle.transform.Find("BMW M3 E42 Visual").GetComponentsInChildren<MeshRenderer>().All(r => !r.enabled), Is.True);
                Assert.That(vehicle.GetComponent<VehiclePresentationModule>(), Is.Not.Null);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
        [Test]
        public void ActualBmwPrefabLaunchesSteersAndAppliesPartsWithoutChangingFactory()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var draft = Draft; var factoryJson = EditorJsonUtility.ToJson(draft.tuning);
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(draft.vehiclePrefab, scene); root.transform.position = new Vector3(0, 1.1f, 0);
                var ground = new GameObject("Test asphalt"); SceneManager.MoveGameObjectToScene(ground, scene); ground.transform.position = new Vector3(0, -.5f, 100); ground.AddComponent<BoxCollider>().size = new Vector3(100, 1, 1000); ground.AddComponent<VehicleSurface>().Configure("Test asphalt", 1);
                var vehicle = root.GetComponent<VehicleController>(); vehicle.SetManualSimulation(true);
                var input = root.AddComponent<FrameworkTestInput>(); input.state = new VehicleInputState { Throttle = 1 };
                vehicle.ConfigureForRuntime(draft.tuning, input, vehicle.Wheels); Physics.SyncTransforms(); var physics = scene.GetPhysicsScene();
                for (int i = 0; i < 300; i++) { vehicle.StepSimulation(.02f); physics.Simulate(.02f); }
                Assert.That(vehicle.Telemetry.SpeedKph, Is.GreaterThan(20)); Assert.That(vehicle.Wheels.Count(w => w.Grounded), Is.GreaterThanOrEqualTo(3));
                float launchSpeed = vehicle.Telemetry.SpeedKph;
                input.state = new VehicleInputState { Throttle = .3f, Steering = .4f };
                for (int i = 0; i < 15; i++) { vehicle.StepSimulation(.02f); physics.Simulate(.02f); }
                Assert.That(Mathf.Abs(vehicle.Wheels[0].SteerAngle), Is.GreaterThan(1));
                var custom = root.GetComponent<VehicleCustomizationSystem>(); var paint = draft.customization.Items.Single(p => p.CustomizationId == "bmw-graphite");
                Assert.That(custom.BeginPreview(paint, out var failure), Is.True, failure); Assert.That(custom.TryApplyPreview(out failure), Is.True, failure);
                var configuration = root.GetComponent<VehicleConfiguration>(); Assert.That(configuration.TrySetAdjustments(new[] { new VehicleTuningAdjustment { parameter = VehiclePhysicsLabTuningParameter.Mass, value = 1400 } }, out failure), Is.True, failure);
                vehicle.StepSimulation(.02f); Assert.That(vehicle.Tuning.chassis.mass, Is.EqualTo(1400));
                Assert.That(EditorJsonUtility.ToJson(draft.tuning), Is.EqualTo(factoryJson));
                TestContext.WriteLine("BMW six-second speed: " + launchSpeed + " km/h; steering=" + vehicle.Wheels[0].SteerAngle);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
