#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Creates repeatable sandbox scenes for the career-facing shop and bounty
    /// interfaces. The scenes use the same asset-backed systems as gameplay,
    /// with only the temporary IMGUI harness supplied by the test scene.
    /// </summary>
    public static partial class DrivingDemoBuilder
    {
        private const string ShopTestScenePath = "Assets/NfsMw/Scenes/Tests/ShopTest.unity";
        private const string BountyTestScenePath = "Assets/NfsMw/Scenes/Tests/BountyTest.unity";
        private const string PursuitTestScenePath = "Assets/NfsMw/Scenes/Tests/PursuitTest.unity";
        private const string TestDataFolder = "Assets/NfsMw/Modules/Driving/Data/Tests";
        private const string ShopTestDataFolder = TestDataFolder + "/Shops";
        private const string ShopTestCatalogPath =
            ShopTestDataFolder + "/VehicleShopTestCatalog.asset";
        private const string ShopTestCustomizationCatalogPath =
            ShopTestDataFolder + "/VehicleShopTestCustomizationCatalog.asset";
        private const string ShopTestBodyKitPath =
            ShopTestDataFolder + "/TestBodyKit.asset";
        private const string ShopTestSpoilerPath =
            ShopTestDataFolder + "/TestSpoiler.asset";
        private const string ShopTestPaintPath =
            ShopTestDataFolder + "/TestPaint.asset";
        private const string ShopTestCarPath =
            ShopTestDataFolder + "/TestCar.asset";
        private const string ShopTestBodyShopPath =
            ShopTestDataFolder + "/TestBodyShop.asset";
        private const string ShopTestPerformanceShopPath =
            ShopTestDataFolder + "/TestPerformanceShop.asset";
        private const string ShopTestCarShowPath =
            ShopTestDataFolder + "/TestCarShow.asset";
        private const string ShopTestOneStopShopPath =
            ShopTestDataFolder + "/TestOneStopShop.asset";
        private const string ShopTestMaterialFileName = "ShopTestPlatform.mat";
        private const string BountyTestMaterialFileName = "BountyTestPlatform.mat";
        private const string PursuitTestAsphaltMaterialFileName =
            "PursuitTestAsphalt.mat";
        private const string PursuitTestGrassMaterialFileName =
            "PursuitTestGrass.mat";
        private const string PursuitTestCurbMaterialFileName =
            "PursuitTestCurb.mat";
        private const string PursuitTestLaneMaterialFileName =
            "PursuitTestLaneMarking.mat";

        [MenuItem("NFS MW Remaster/Build Shop Test Scene")]
        public static void BuildShopTestScene()
        {
            EnsureTestFolders();
            VehicleTuning tuning = GetOrCreateTuning();
            VehiclePerformanceCatalog performanceCatalog =
                GetOrCreatePerformanceCatalog();
            ShopTestContent content = GetOrCreateShopTestContent(performanceCatalog);
            VehicleBountyRules bountyRules = GetOrCreateBountyRules();
            Material platformMaterial = GetOrCreateMaterial(
                ShopTestMaterialFileName,
                new Color(0.035f, 0.045f, 0.065f),
                0.15f,
                0.70f);
            Material carPaint = GetOrCreateMaterial(
                "ShopTestCarPaint.mat",
                new Color(0.05f, 0.32f, 0.72f),
                0.65f,
                0.82f);
            Material glass = GetOrCreateMaterial(
                "ShopTestGlass.mat",
                new Color(0.015f, 0.055f, 0.09f),
                0.35f,
                0.92f);
            Material tire = GetOrCreateMaterial(
                "ShopTestTire.mat",
                new Color(0.012f, 0.012f, 0.014f),
                0f,
                0.28f);
            Material headlight = GetOrCreateMaterial(
                "ShopTestHeadlight.mat",
                new Color(1f, 0.82f, 0.42f),
                0.1f,
                0.78f);

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            ConfigureShopTestRenderSettings();
            CreateLighting();
            CreateShopTestEnvironment(platformMaterial);
            VehicleController vehicle = CreateVehicle(
                tuning,
                performanceCatalog,
                content.CustomizationCatalog,
                content.StoreCatalog,
                content.OneStopShop,
                bountyRules,
                carPaint,
                glass,
                tire,
                headlight);
            VehicleCameraRig cameraRig = CreateCamera(vehicle);
            vehicle.ConfigureForRuntime(
                tuning,
                vehicle.GetComponent<PlayerVehicleInput>(),
                vehicle.Wheels,
                cameraRig);

            VehicleTelemetryHud telemetry = vehicle.GetComponent<VehicleTelemetryHud>();
            if (telemetry != null)
            {
                telemetry.enabled = false;
            }

            VehicleStoreWallet wallet = vehicle.GetComponent<VehicleStoreWallet>();
            wallet.SetBalance(100000);
            CareerProfileSystem profile = vehicle.GetComponent<CareerProfileSystem>();
            profile.SetProfileId("shop_test_profile");
            profile.SetPlayerName("Shop Test Driver");
            profile.SetActiveVehicleId("shop_test_vehicle");

            GameObject interfaceObject = new GameObject("Shop Test Interface");
            VehicleShopTestHarness harness =
                interfaceObject.AddComponent<VehicleShopTestHarness>();
            harness.Configure(
                vehicle.GetComponent<VehicleStoreSystem>(),
                wallet,
                vehicle.GetComponent<VehicleStoreOwnership>(),
                vehicle.GetComponent<VehicleStoreGarage>(),
                profile,
                new[]
                {
                    content.BodyShop,
                    content.PerformanceShop,
                    content.CarShow,
                    content.OneStopShop
                });

            SaveTestScene(
                scene,
                ShopTestScenePath,
                interfaceObject,
                "Built shop test scene at ");
        }

        [MenuItem("NFS MW Remaster/Build Bounty Test Scene")]
        public static void BuildBountyTestScene()
        {
            EnsureTestFolders();
            VehicleBountyRules bountyRules = GetOrCreateBountyRules();
            Material platformMaterial = GetOrCreateMaterial(
                BountyTestMaterialFileName,
                new Color(0.10f, 0.045f, 0.055f),
                0.10f,
                0.62f);

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            ConfigureBountyTestRenderSettings();
            CreateLighting();
            CreateBountyTestEnvironment(platformMaterial);

            GameObject careerObject = new GameObject("Bounty Career Test");
            VehicleBountySystem bounty = careerObject.AddComponent<VehicleBountySystem>();
            bounty.SetRules(bountyRules);
            bounty.SetStartingHeatLevel(1);
            JsonCareerProfileStorage storage =
                careerObject.AddComponent<JsonCareerProfileStorage>();
            CareerProfileSystem profile = careerObject.AddComponent<CareerProfileSystem>();
            profile.SetStorage(storage);
            profile.SetProfileId("bounty_test_profile");
            profile.SetPlayerName("Bounty Test Driver");
            profile.SetActiveVehicleId("bounty_test_vehicle");

            GameObject interfaceObject = new GameObject("Bounty Test Interface");
            VehicleBountyTestHarness harness =
                interfaceObject.AddComponent<VehicleBountyTestHarness>();
            harness.Configure(bounty, profile);
            CreateTestCamera();

            SaveTestScene(
                scene,
                BountyTestScenePath,
                interfaceObject,
                "Built bounty test scene at ");
        }

        [MenuItem("NFS MW Remaster/Build Pursuit Test Scene")]
        public static void BuildPursuitTestScene()
        {
            EnsureTestFolders();
            VehicleTuning tuning = GetOrCreateTuning();
            VehiclePerformanceCatalog performanceCatalog =
                GetOrCreatePerformanceCatalog();
            ShopTestContent content = GetOrCreateShopTestContent(performanceCatalog);
            VehicleBountyRules bountyRules = GetOrCreateBountyRules();
            VehiclePoliceResponseProfile responseProfile =
                GetOrCreatePoliceResponseProfile();
            Material asphalt = GetOrCreateMaterial(
                PursuitTestAsphaltMaterialFileName,
                new Color(0.035f, 0.042f, 0.052f),
                0.05f,
                0.76f);
            Material grass = GetOrCreateMaterial(
                PursuitTestGrassMaterialFileName,
                new Color(0.07f, 0.13f, 0.075f),
                0f,
                0.24f);
            Material curb = GetOrCreateMaterial(
                PursuitTestCurbMaterialFileName,
                new Color(0.72f, 0.035f, 0.045f),
                0.05f,
                0.42f);
            Material laneMarking = GetOrCreateMaterial(
                PursuitTestLaneMaterialFileName,
                new Color(0.98f, 0.62f, 0.08f),
                0f,
                0.58f);
            Material carPaint = GetOrCreateMaterial(
                "PursuitTestPlayerPaint.mat",
                new Color(0.04f, 0.31f, 0.82f),
                0.65f,
                0.82f);
            Material glass = GetOrCreateMaterial(
                "PursuitTestGlass.mat",
                new Color(0.012f, 0.04f, 0.075f),
                0.35f,
                0.92f);
            Material tire = GetOrCreateMaterial(
                "PursuitTestTire.mat",
                new Color(0.008f, 0.009f, 0.012f),
                0f,
                0.28f);
            Material headlight = GetOrCreateMaterial(
                "PursuitTestHeadlight.mat",
                new Color(1f, 0.82f, 0.42f),
                0.1f,
                0.78f);
            Material policePaint = GetOrCreateMaterial(
                "PursuitTestPolicePaint.mat",
                new Color(0.012f, 0.016f, 0.025f),
                0.55f,
                0.72f);
            Material policeStripe = GetOrCreateMaterial(
                "PursuitTestPoliceStripe.mat",
                new Color(0.08f, 0.13f, 0.22f),
                0.35f,
                0.68f);
            Material policeRed = GetOrCreateMaterial(
                "PursuitTestPoliceRed.mat",
                new Color(0.95f, 0.018f, 0.025f),
                0.05f,
                0.86f);
            Material policeBlue = GetOrCreateMaterial(
                "PursuitTestPoliceBlue.mat",
                new Color(0.02f, 0.22f, 1f),
                0.05f,
                0.86f);

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            ConfigurePursuitTestRenderSettings();
            CreateLighting();
            CreateTrack(asphalt, grass, curb, laneMarking);
            VehicleController vehicle = CreateVehicle(
                tuning,
                performanceCatalog,
                content.CustomizationCatalog,
                content.StoreCatalog,
                content.OneStopShop,
                bountyRules,
                carPaint,
                glass,
                tire,
                headlight);
            VehicleCameraRig cameraRig = CreateCamera(vehicle);
            vehicle.ConfigureForRuntime(
                tuning,
                vehicle.GetComponent<PlayerVehicleInput>(),
                vehicle.Wheels,
                cameraRig);

            VehicleTelemetryHud telemetry = vehicle.GetComponent<VehicleTelemetryHud>();
            if (telemetry != null)
            {
                telemetry.enabled = false;
            }

            VehicleBountySystem bounty = vehicle.GetComponent<VehicleBountySystem>();
            bounty.SetStartingHeatLevel(1);
            CareerProfileSystem profile = vehicle.GetComponent<CareerProfileSystem>();
            profile.SetProfileId("pursuit_test_profile");
            profile.SetPlayerName("Pursuit Test Driver");
            profile.SetActiveVehicleId("pursuit_test_vehicle");

            VehiclePursuitTargetAdapter target =
                vehicle.GetComponent<VehiclePursuitTargetAdapter>();
            if (target == null)
            {
                target = vehicle.gameObject.AddComponent<VehiclePursuitTargetAdapter>();
            }

            target.Configure("pursuit_test_player");

            GameObject directorObject = new GameObject("Pursuit Director");
            VehiclePursuitDirector director =
                directorObject.AddComponent<VehiclePursuitDirector>();
            director.Configure(target, bounty, responseProfile);

            GameObject unitsObject = new GameObject("Police Response Units");
            VehiclePoliceUnit[] units =
            {
                CreatePoliceUnit(
                    unitsObject.transform,
                    "pursuer_01",
                    VehiclePoliceUnitRole.Pursuer,
                    new Vector3(-4.5f, 0.14f, -88f),
                    policePaint,
                    policeStripe,
                    policeRed,
                    policeBlue),
                CreatePoliceUnit(
                    unitsObject.transform,
                    "pursuer_02",
                    VehiclePoliceUnitRole.Pursuer,
                    new Vector3(4.5f, 0.14f, -98f),
                    policePaint,
                    policeStripe,
                    policeRed,
                    policeBlue),
                CreatePoliceUnit(
                    unitsObject.transform,
                    "interceptor_01",
                    VehiclePoliceUnitRole.Interceptor,
                    new Vector3(-5.2f, 0.14f, -116f),
                    policePaint,
                    policeStripe,
                    policeRed,
                    policeBlue),
                CreatePoliceUnit(
                    unitsObject.transform,
                    "patrol_03",
                    VehiclePoliceUnitRole.Pursuer,
                    new Vector3(-5.5f, 0.14f, -75f),
                    policePaint,
                    policeStripe,
                    policeRed,
                    policeBlue),
                CreatePoliceUnit(
                    unitsObject.transform,
                    "patrol_04",
                    VehiclePoliceUnitRole.Pursuer,
                    new Vector3(5.5f, 0.14f, -78f),
                    policePaint,
                    policeStripe,
                    policeRed,
                    policeBlue),
                CreatePoliceUnit(
                    unitsObject.transform,
                    "interceptor_02",
                    VehiclePoliceUnitRole.Interceptor,
                    new Vector3(0f, 0.14f, -132f),
                    policePaint,
                    policeStripe,
                    policeRed,
                    policeBlue)
            };
            director.ConfigureUnits(units);
            CreatePoliceRoadHazard(director.transform, director, new Vector3(0, 0.2f, 110), Quaternion.identity,
                PoliceRoadHazardKind.Roadblock, policeStripe, policeRed);
            CreatePoliceRoadHazard(director.transform, director, new Vector3(0, 0.2f, 220), Quaternion.identity,
                PoliceRoadHazardKind.SpikeStrip, policeStripe, policeRed);

            GameObject interfaceObject = new GameObject("Pursuit Test Interface");
            VehiclePursuitTestHarness harness =
                interfaceObject.AddComponent<VehiclePursuitTestHarness>();
            harness.Configure(director, bounty);

            SaveTestScene(
                scene,
                PursuitTestScenePath,
                interfaceObject,
                "Built pursuit test scene at ");
        }

        [MenuItem("NFS MW Remaster/Build All Career Test Scenes")]
        public static void BuildAllCareerTestScenes()
        {
            BuildShopTestScene();
            BuildBountyTestScene();
            BuildPursuitTestScene();
            Debug.Log(
                "Built career test scenes: "
                + ShopTestScenePath
                + " and "
                + BountyTestScenePath
                + " and "
                + PursuitTestScenePath);
        }

        private static void EnsureTestFolders()
        {
            EnsureFolder("Assets/NfsMw/Scenes");
            EnsureFolder("Assets/NfsMw/Modules/Driving/Data");
            EnsureFolder(PerformanceFolder);
            EnsureFolder(CustomizationFolder);
            EnsureFolder(StoreFolder);
            EnsureFolder(CareerFolder);
            EnsureFolder(TestDataFolder);
            EnsureFolder(ShopTestDataFolder);
            EnsureFolder(MaterialsFolder);
        }

        private static ShopTestContent GetOrCreateShopTestContent(
            VehiclePerformanceCatalog performanceCatalog)
        {
            AssetVehicleCustomization bodyKit = GetOrCreateShopCustomization(
                ShopTestBodyKitPath,
                "test_bodykit",
                "Test Sport Body Kit",
                VehicleCustomizationCategory.BodyKit,
                VehicleCustomizationStyle.Sport,
                2500);
            AssetVehicleCustomization spoiler = GetOrCreateShopCustomization(
                ShopTestSpoilerPath,
                "test_spoiler",
                "Test Carbon Spoiler",
                VehicleCustomizationCategory.Spoiler,
                VehicleCustomizationStyle.SportCarbon,
                1200);
            AssetVehicleCustomization paint = GetOrCreateShopCustomization(
                ShopTestPaintPath,
                "test_paint",
                "Test Blue Paint",
                VehicleCustomizationCategory.Paint,
                VehicleCustomizationStyle.Standard,
                900);
            AssetVehicleCarStoreProduct car = GetOrCreateShopCar();

            VehicleCustomizationCatalog customizationCatalog =
                LoadOrDeleteInvalidAsset<VehicleCustomizationCatalog>(
                    ShopTestCustomizationCatalogPath);
            if (customizationCatalog == null)
            {
                customizationCatalog =
                    ScriptableObject.CreateInstance<VehicleCustomizationCatalog>();
                AssetDatabase.CreateAsset(
                    customizationCatalog,
                    ShopTestCustomizationCatalogPath);
            }

            customizationCatalog.SetItems(new VehicleCustomizationDefinition[]
            {
                bodyKit,
                spoiler,
                paint
            });
            EditorUtility.SetDirty(customizationCatalog);

            VehicleStoreCatalog storeCatalog =
                LoadOrDeleteInvalidAsset<VehicleStoreCatalog>(ShopTestCatalogPath);
            if (storeCatalog == null)
            {
                storeCatalog = ScriptableObject.CreateInstance<VehicleStoreCatalog>();
                AssetDatabase.CreateAsset(storeCatalog, ShopTestCatalogPath);
            }

            IReadOnlyList<VehiclePerformanceUpgradeDefinition> upgrades =
                performanceCatalog.Upgrades;
            List<ScriptableObject> products = new List<ScriptableObject>();
            for (int i = 0; i < upgrades.Count; i++)
            {
                products.Add(upgrades[i]);
            }

            products.Add(bodyKit);
            products.Add(spoiler);
            products.Add(paint);
            products.Add(car);
            storeCatalog.SetProducts(products.ToArray());
            EditorUtility.SetDirty(storeCatalog);

            AssetVehicleStorefront bodyShop = ConfigureTestStorefront(
                ShopTestBodyShopPath,
                "test-body-shop",
                "Test Body Shop",
                VehicleStoreCategory.BodyShop,
                storeCatalog);
            AssetVehicleStorefront performanceShop = ConfigureTestStorefront(
                ShopTestPerformanceShopPath,
                "test-performance-shop",
                "Test Performance Shop",
                VehicleStoreCategory.PerformanceShop,
                storeCatalog);
            AssetVehicleStorefront carShow = ConfigureTestStorefront(
                ShopTestCarShowPath,
                "test-car-show",
                "Test Car Show",
                VehicleStoreCategory.CarShow,
                storeCatalog);
            AssetVehicleStorefront oneStopShop = ConfigureTestStorefront(
                ShopTestOneStopShopPath,
                "test-one-stop-shop",
                "Test One-Stop Shop",
                VehicleStoreCategory.OneStopShop,
                storeCatalog);

            return new ShopTestContent(
                storeCatalog,
                customizationCatalog,
                bodyShop,
                performanceShop,
                carShow,
                oneStopShop);
        }

        private static AssetVehicleStorefront ConfigureTestStorefront(
            string path,
            string id,
            string displayName,
            VehicleStoreCategory category,
            VehicleStoreCatalog catalog)
        {
            AssetVehicleStorefront store = GetOrCreateStorefront(
                path,
                id,
                displayName,
                category,
                catalog);
            store.Configure(id, displayName, category, catalog);
            EditorUtility.SetDirty(store);
            return store;
        }

        private static AssetVehicleCustomization GetOrCreateShopCustomization(
            string path,
            string id,
            string displayName,
            VehicleCustomizationCategory category,
            VehicleCustomizationStyle style,
            int price)
        {
            AssetVehicleCustomization item =
                LoadOrDeleteInvalidAsset<AssetVehicleCustomization>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<AssetVehicleCustomization>();
                AssetDatabase.CreateAsset(item, path);
            }

            item.ConfigureMetadata(id, displayName, category, style, price, false);
            item.ConfigureVisual(new VehicleCustomizationVisualPayload());
            EditorUtility.SetDirty(item);
            return item;
        }

        private static AssetVehicleCarStoreProduct GetOrCreateShopCar()
        {
            AssetVehicleCarStoreProduct car =
                LoadOrDeleteInvalidAsset<AssetVehicleCarStoreProduct>(ShopTestCarPath);
            if (car == null)
            {
                car = ScriptableObject.CreateInstance<AssetVehicleCarStoreProduct>();
                AssetDatabase.CreateAsset(car, ShopTestCarPath);
            }

            car.ConfigureMetadata(
                "test_car_supra",
                "Test Tuner Coupe",
                "test_vehicle_supra",
                30000,
                true);
            car.ConfigurePresentation(null!, null!);
            EditorUtility.SetDirty(car);
            return car;
        }

        private static void ConfigureShopTestRenderSettings()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.025f, 0.08f, 0.16f);
            RenderSettings.ambientEquatorColor = new Color(0.06f, 0.10f, 0.15f);
            RenderSettings.ambientGroundColor = new Color(0.015f, 0.02f, 0.03f);
        }

        private static void ConfigureBountyTestRenderSettings()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.16f, 0.025f, 0.04f);
            RenderSettings.ambientEquatorColor = new Color(0.12f, 0.04f, 0.06f);
            RenderSettings.ambientGroundColor = new Color(0.025f, 0.01f, 0.015f);
        }

        private static void ConfigurePursuitTestRenderSettings()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.012f, 0.035f, 0.09f);
            RenderSettings.ambientEquatorColor = new Color(0.035f, 0.06f, 0.12f);
            RenderSettings.ambientGroundColor = new Color(0.008f, 0.012f, 0.025f);
        }

        private static void CreateShopTestEnvironment(Material platformMaterial)
        {
            GameObject environment = new GameObject("Shop Test Environment");
            CreateBox(
                "Shop Test Platform",
                environment.transform,
                new Vector3(0f, -0.55f, -66f),
                new Vector3(22f, 1f, 22f),
                platformMaterial,
                true);
            CreateBox(
                "Shop Test Backdrop",
                environment.transform,
                new Vector3(0f, 4f, -77f),
                new Vector3(22f, 9f, 0.5f),
                platformMaterial,
                true);
        }

        private static void CreateBountyTestEnvironment(Material platformMaterial)
        {
            GameObject environment = new GameObject("Bounty Test Environment");
            CreateBox(
                "Bounty Test Platform",
                environment.transform,
                new Vector3(0f, -0.55f, 0f),
                new Vector3(22f, 1f, 16f),
                platformMaterial,
                true);
        }

        private static VehiclePoliceUnit CreatePoliceUnit(
            Transform parent,
            string unitId,
            VehiclePoliceUnitRole role,
            Vector3 position,
            Material policePaint,
            Material policeStripe,
            Material policeRed,
            Material policeBlue)
        {
            GameObject unitObject = new GameObject("Police Unit - " + unitId);
            unitObject.transform.SetParent(parent, false);
            unitObject.transform.position = position;
            unitObject.transform.rotation = Quaternion.identity;

            Rigidbody body = unitObject.AddComponent<Rigidbody>();
            body.mass = 1550f;
            body.linearDamping = 0.08f;
            body.angularDamping = 0.16f;
            BoxCollider collider = unitObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.25f, 0f);
            collider.size = new Vector3(1.9f, 0.64f, 4.25f);

            CreateVisualBox(
                "Police Chassis",
                unitObject.transform,
                new Vector3(0f, 0.25f, 0f),
                new Vector3(1.9f, 0.55f, 4.25f),
                policePaint);
            CreateVisualBox(
                "Police Cabin",
                unitObject.transform,
                new Vector3(0f, 0.62f, -0.1f),
                new Vector3(1.43f, 0.28f, 1.58f),
                policeStripe);
            CreateVisualBox(
                "Police Door Stripe",
                unitObject.transform,
                new Vector3(0f, 0.34f, 0.18f),
                new Vector3(1.94f, 0.12f, 1.35f),
                policeStripe);
            CreateVisualBox(
                "Emergency Light Red",
                unitObject.transform,
                new Vector3(-0.25f, 0.82f, -0.08f),
                new Vector3(0.28f, 0.10f, 0.24f),
                policeRed);
            CreateVisualBox(
                "Emergency Light Blue",
                unitObject.transform,
                new Vector3(0.25f, 0.82f, -0.08f),
                new Vector3(0.28f, 0.10f, 0.24f),
                policeBlue);

            VehiclePoliceUnit unit = unitObject.AddComponent<VehiclePoliceUnit>();
            unit.Configure(unitId, role, null);
            ConfigurePoliceRig(unit, policePaint);
            return unit;
        }

        private static void CreateTestCamera()
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 4f, -10f);
            cameraObject.transform.rotation = Quaternion.Euler(18f, 0f, 0f);
            Camera camera = cameraObject.AddComponent<Camera>(); Rendering.HdrpSceneDefaults.Camera(camera);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.008f, 0.015f);
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 200f;
            cameraObject.AddComponent<AudioListener>();
        }

        private static void SaveTestScene(
            Scene scene,
            string path,
            GameObject selection,
            string logPrefix)
        {
            if (Object.FindAnyObjectByType<VehicleCameraRig>() != null && Object.FindAnyObjectByType<SensoryAudioWorld>() == null)
                InstallSensoryInOpenScene();
            // Runtime configuration methods also author scene references. Unity requires
            // explicit overrides for those assignments to survive a prefab scene reload.
            foreach (var root in scene.GetRootGameObjects())
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                    if (component != null && PrefabUtility.IsPartOfPrefabInstance(component))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
            AddSceneToBuildSettings(path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = selection;
            Debug.Log(logPrefix + path);
        }

        private static void AddSceneToBuildSettings(string path)
        {
            List<EditorBuildSettingsScene> scenes =
                new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == path)
                {
                    scenes[i] = new EditorBuildSettingsScene(path, true);
                    EditorBuildSettings.scenes = scenes.ToArray();
                    return;
                }
            }

            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private sealed class ShopTestContent
        {
            public ShopTestContent(
                VehicleStoreCatalog configuredStoreCatalog,
                VehicleCustomizationCatalog configuredCustomizationCatalog,
                AssetVehicleStorefront configuredBodyShop,
                AssetVehicleStorefront configuredPerformanceShop,
                AssetVehicleStorefront configuredCarShow,
                AssetVehicleStorefront configuredOneStopShop)
            {
                StoreCatalog = configuredStoreCatalog;
                CustomizationCatalog = configuredCustomizationCatalog;
                BodyShop = configuredBodyShop;
                PerformanceShop = configuredPerformanceShop;
                CarShow = configuredCarShow;
                OneStopShop = configuredOneStopShop;
            }

            public VehicleStoreCatalog StoreCatalog { get; }

            public VehicleCustomizationCatalog CustomizationCatalog { get; }

            public AssetVehicleStorefront BodyShop { get; }

            public AssetVehicleStorefront PerformanceShop { get; }

            public AssetVehicleStorefront CarShow { get; }

            public AssetVehicleStorefront OneStopShop { get; }
        }
    }
}
#endif
