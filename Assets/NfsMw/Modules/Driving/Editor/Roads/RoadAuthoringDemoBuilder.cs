using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

namespace NfsMwRemaster.Driving.Editor
{
    public static partial class DrivingDemoBuilder
    {
        public const string RoadDemoScenePath = "Assets/NfsMw/Modules/Driving/Examples/RoadAuthoringDemo.unity";
        public const string ValidatedRoadDemoScenePath = "Assets/NfsMw/Modules/Driving/Examples/ValidatedRoadAuthoringDemo.unity";
        public const string RoadDrivingValidationScenePath = "Assets/NfsMw/Modules/Driving/Examples/RoadDrivingValidation.unity";

        [MenuItem("NFS MW Remaster/Roads/Create Driving Example")]
        public static void BuildRoadAuthoringDemo()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new ArgumentException("Exit Play mode before creating an example.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(RoadDemoScenePath) != null)
                throw new ArgumentException("The road example already exists. Open it from Assets/NfsMw/Modules/Driving/Examples.");
            EnsureFolder("Assets/NfsMw/Modules/Driving/Examples"); EnsureFolder("Assets/NfsMw/Modules/Driving/Examples/RoadData");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var profile = RoadProfileInspector.GetDefaultProfile();
            var road = RoadAuthoringCommands.Create(profile, new[] { Vector3.zero, Vector3.forward * 100 });
            road.name = "Hillside Loop";
            const float r = 80, handle = r * 0.55228475f;
            road.Reference.Spline = new Spline(new[]
            {
                new BezierKnot(new Vector3(0, 0, r), Vector3.left * handle, Vector3.right * handle),
                new BezierKnot(new Vector3(r, 5, 0), Vector3.forward * handle, Vector3.back * handle),
                new BezierKnot(new Vector3(0, 0, -r), Vector3.right * handle, Vector3.left * handle),
                new BezierKnot(new Vector3(-r, 0, 0), Vector3.back * handle, Vector3.forward * handle),
                new BezierKnot(new Vector3(0, 0, r), Vector3.left * handle, Vector3.right * handle)
            });
            var source = RoadAuthoringCommands.CreateNetwork(new[] { road });
            source.connections = new[]
            {
                new RoadLaneConnection { from = road.Bands[0].id, to = road.Bands[0].id },
                new RoadLaneConnection { from = road.Bands[1].id, to = road.Bands[1].id }
            };
            RoadNetworkBake.Publish(source, "Assets/NfsMw/Modules/Driving/Examples/RoadData/HillsideLoop.asset");
            CreateRoadExampleVehicle(RoadGeometry.Evaluate(road).Sample(20));
            RoadBuildGuard.ValidateScene(scene); EditorSceneManager.SaveScene(scene, RoadDemoScenePath); AssetDatabase.SaveAssets();
            Selection.activeGameObject = road.gameObject;
            Debug.Log("ROAD_EXAMPLE_READY: " + RoadDemoScenePath);
        }

        private static void CreateRoadExampleVehicle(RoadFrame sample)
        {
            var tuning = AssetDatabase.LoadAssetAtPath<VehicleTuning>(TuningPath);
            if (tuning == null) throw new ArgumentException("The existing driving tuning asset is required for the example.");
            var vehicle = CreateVehicle(tuning,
                AssetDatabase.LoadAssetAtPath<VehiclePerformanceCatalog>(PerformanceCatalogPath),
                AssetDatabase.LoadAssetAtPath<VehicleCustomizationCatalog>(CustomizationCatalogPath),
                AssetDatabase.LoadAssetAtPath<VehicleStoreCatalog>(StoreCatalogPath),
                AssetDatabase.LoadAssetAtPath<AssetVehicleStorefront>(OneStopShopPath),
                AssetDatabase.LoadAssetAtPath<VehicleBountyRules>(BountyRulesPath),
                AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/CarPaint.mat"),
                AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/Glass.mat"),
                AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/Tire.mat"),
                AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/Headlight.mat"));
            vehicle.transform.SetPositionAndRotation(sample.At(-1.75f, 0.72f), Quaternion.LookRotation(sample.Forward, sample.Up));
            vehicle.GetComponent<CareerProfileSystem>().SetProfileId("road_authoring_example");
            vehicle.GetComponent<CareerProfileSystem>().ConfigureAutomaticPersistence(false, false, false);
            var camera = CreateCamera(vehicle);
            camera.transform.SetPositionAndRotation(vehicle.transform.position - vehicle.transform.forward * 7 + Vector3.up * 3, vehicle.transform.rotation);
            vehicle.ConfigureForRuntime(tuning, vehicle.GetComponent<PlayerVehicleInput>(), vehicle.Wheels, camera);
            CreateLighting(); RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.6f, 0.68f, 0.78f); RenderSettings.ambientEquatorColor = Color.gray;
        }

        // Run only in a disposable batch project. The source example and its existing publication remain intact.
        public static void PrepareRoadAuthoringValidation()
        {
            if (!Application.isBatchMode) throw new ArgumentException("Prepare road validation in an isolated batch project.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(RoadDemoScenePath) == null) BuildRoadAuthoringDemo();
            var loop = EditorSceneManager.OpenScene(RoadDemoScenePath, OpenSceneMode.Single);
            foreach (var root in loop.GetRootGameObjects())
                foreach (var network in root.GetComponentsInChildren<RoadNetworkAuthoring>())
                    RoadNetworkBake.Publish(network, "Assets/NfsMw/Modules/Driving/Examples/RoadData/ValidatedHillsideLoop.asset");
            RoadBuildGuard.ValidateScene(loop);
            EditorSceneManager.SaveScene(loop, ValidatedRoadDemoScenePath);
            var straight = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var profile = RoadProfileInspector.GetDefaultProfile();
            var road = RoadAuthoringCommands.Create(profile, new[] { Vector3.zero, Vector3.forward * 300 });
            road.name = "Driving validation — chunk crossing";
            var source = RoadAuthoringCommands.CreateNetwork(new[] { road });
            RoadNetworkBake.Publish(source, "Assets/NfsMw/Modules/Driving/Examples/RoadData/DrivingValidation.asset");
            CreateRoadExampleVehicle(RoadGeometry.Evaluate(road).Sample(90));
            RoadBuildGuard.ValidateScene(straight); EditorSceneManager.SaveScene(straight, RoadDrivingValidationScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("ROAD_VALIDATION_SCENES_READY");
        }

        public static void BuildRoadAuthoringPlayer()
        {
            string output = Environment.GetEnvironmentVariable("ROAD_VALIDATION_PLAYER");
            if (string.IsNullOrEmpty(output)) throw new BuildFailedException("Set ROAD_VALIDATION_PLAYER to a disposable output path.");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ValidatedRoadDemoScenePath, RoadDrivingValidationScenePath },
                locationPathName = output, target = BuildTarget.StandaloneOSX, options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Road example player build failed.");
            Debug.Log("ROAD_PLAYER_BUILD_PASSED: " + report.summary.totalSize + " bytes");
        }
    }
}
