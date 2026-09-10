#if UNITY_EDITOR
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Builds a zero-external-asset driving sandbox. It is available both from
    /// the Unity menu and through -executeMethod for reproducible CLI setup.
    /// </summary>
    public static partial class DrivingDemoBuilder
    {
        private const string ScenePath = "Assets/NfsMw/Scenes/Game/DrivingDemo.unity";
        private const string TuningPath = "Assets/NfsMw/Modules/Driving/Data/MW2005StreetTuning.asset";
        private const string PerformanceFolder = "Assets/NfsMw/Modules/Driving/Data/Performance";
        private const string PerformanceCatalogPath =
            PerformanceFolder + "/VehiclePerformanceCatalog.asset";
        private const string CustomizationFolder = "Assets/NfsMw/Modules/Driving/Data/Customization";
        private const string CustomizationCatalogPath =
            CustomizationFolder + "/VehicleCustomizationCatalog.asset";
        private const string StoreFolder = "Assets/NfsMw/Modules/Driving/Data/Store";
        private const string StoreCatalogPath =
            StoreFolder + "/VehicleStoreCatalog.asset";
        private const string BodyShopPath = StoreFolder + "/BodyShop.asset";
        private const string PerformanceShopPath =
            StoreFolder + "/PerformanceShop.asset";
        private const string CarShowPath = StoreFolder + "/CarShow.asset";
        private const string OneStopShopPath = StoreFolder + "/OneStopShop.asset";
        private const string CareerFolder = "Assets/NfsMw/Modules/Driving/Data/Career";
        private const string BountyRulesPath =
            CareerFolder + "/VehicleBountyRules.asset";
        private const string PoliceResponseProfilePath =
            CareerFolder + "/VehiclePoliceResponseProfile.asset";
        private const string MaterialsFolder = "Assets/NfsMw/Modules/Driving/Data/Materials";

        [MenuItem("NFS MW Remaster/Build Driving Demo")]
        public static void Build()
        {
            EnsureFolder("Assets/NfsMw/Modules/Driving/Data");
            EnsureFolder(PerformanceFolder);
            EnsureFolder(CustomizationFolder);
            EnsureFolder(StoreFolder);
            EnsureFolder(CareerFolder);
            EnsureFolder(MaterialsFolder);
            VehicleTuning tuning = GetOrCreateTuning();
            VehiclePerformanceCatalog performanceCatalog = GetOrCreatePerformanceCatalog();
            VehicleCustomizationCatalog customizationCatalog =
                GetOrCreateCustomizationCatalog();
            VehicleStoreCatalog storeCatalog =
                GetOrCreateStoreCatalog(performanceCatalog);
            VehicleBountyRules bountyRules = GetOrCreateBountyRules();
            GetOrCreateStorefront(
                BodyShopPath,
                "body-shop",
                "Body Shop",
                VehicleStoreCategory.BodyShop,
                storeCatalog);
            GetOrCreateStorefront(
                PerformanceShopPath,
                "performance-shop",
                "Performance Shop",
                VehicleStoreCategory.PerformanceShop,
                storeCatalog);
            GetOrCreateStorefront(
                CarShowPath,
                "car-show",
                "Car Show",
                VehicleStoreCategory.CarShow,
                storeCatalog);
            AssetVehicleStorefront oneStopShop = GetOrCreateStorefront(
                OneStopShopPath,
                "one-stop-shop",
                "One-Stop Shop",
                VehicleStoreCategory.OneStopShop,
                storeCatalog);

            Material asphalt = GetOrCreateMaterial("Asphalt.mat", new Color(0.055f, 0.065f, 0.075f), 0.05f, 0.72f);
            Material grass = GetOrCreateMaterial("Grass.mat", new Color(0.13f, 0.22f, 0.12f), 0f, 0.25f);
            Material curb = GetOrCreateMaterial("Curb.mat", new Color(0.72f, 0.10f, 0.06f), 0.05f, 0.42f);
            Material laneMarking = GetOrCreateMaterial("LaneMarking.mat", new Color(0.92f, 0.78f, 0.22f), 0f, 0.58f);
            Material carPaint = GetOrCreateMaterial("CarPaint.mat", new Color(0.70f, 0.025f, 0.035f), 0.65f, 0.82f);
            Material glass = GetOrCreateMaterial("Glass.mat", new Color(0.015f, 0.055f, 0.09f), 0.35f, 0.92f);
            Material tire = GetOrCreateMaterial("Tire.mat", new Color(0.012f, 0.012f, 0.014f), 0f, 0.28f);
            Material headlight = GetOrCreateMaterial("Headlight.mat", new Color(1f, 0.82f, 0.42f), 0.1f, 0.78f);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.14f, 0.18f, 0.27f);
            RenderSettings.ambientEquatorColor = new Color(0.08f, 0.10f, 0.13f);
            RenderSettings.ambientGroundColor = new Color(0.025f, 0.03f, 0.025f);

            CreateLighting();
            CreateTrack(asphalt, grass, curb, laneMarking);
            VehicleController vehicle = CreateVehicle(
                tuning,
                performanceCatalog,
                customizationCatalog,
                storeCatalog,
                oneStopShop,
                bountyRules,
                carPaint,
                glass,
                tire,
                headlight);
            VehicleCameraRig cameraRig = CreateCamera(vehicle);
            Rendering.HdrpSceneDefaults.PlayerHeadlights(vehicle.transform);
            vehicle.ConfigureForRuntime(
                tuning,
                vehicle.GetComponent<PlayerVehicleInput>(),
                vehicle.Wheels,
                cameraRig);

            InstallSensoryInOpenScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = vehicle.gameObject;
            Debug.Log("Built NFS MW Remaster driving demo at " + ScenePath);
        }

        private static VehicleTuning GetOrCreateTuning()
        {
            VehicleTuning tuning = LoadOrDeleteInvalidAsset<VehicleTuning>(TuningPath);
            if (tuning != null)
            {
                return tuning;
            }

            tuning = VehicleTuning.CreateStreetRacer();
            AssetDatabase.CreateAsset(tuning, TuningPath);
            AssetDatabase.SaveAssets();
            return tuning;
        }

        private static VehiclePerformanceCatalog GetOrCreatePerformanceCatalog()
        {
            VehiclePerformanceCatalog catalog =
                LoadOrDeleteInvalidAsset<VehiclePerformanceCatalog>(PerformanceCatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<VehiclePerformanceCatalog>();
                AssetDatabase.CreateAsset(catalog, PerformanceCatalogPath);
            }

            VehiclePerformanceCategory[] categories =
            {
                VehiclePerformanceCategory.Engine,
                VehiclePerformanceCategory.Transmission,
                VehiclePerformanceCategory.ForcedInduction,
                VehiclePerformanceCategory.Nitrous,
                VehiclePerformanceCategory.Tires,
                VehiclePerformanceCategory.Brakes,
                VehiclePerformanceCategory.Suspension
            };
            VehiclePerformanceTier[] tiers =
            {
                VehiclePerformanceTier.Street,
                VehiclePerformanceTier.Pro,
                VehiclePerformanceTier.Super,
                VehiclePerformanceTier.Ultimate,
                VehiclePerformanceTier.Junkman
            };
            VehiclePerformanceUpgradeDefinition[] upgrades =
                new VehiclePerformanceUpgradeDefinition[categories.Length * tiers.Length];

            int index = 0;
            for (int categoryIndex = 0; categoryIndex < categories.Length; categoryIndex++)
            {
                for (int tierIndex = 0; tierIndex < tiers.Length; tierIndex++)
                {
                    VehiclePerformanceCategory category = categories[categoryIndex];
                    VehiclePerformanceTier tier = tiers[tierIndex];
                    string id = "mw2005_"
                        + category.ToString().ToLowerInvariant()
                        + "_"
                        + tier.ToString().ToLowerInvariant();
                    string path = PerformanceFolder + "/" + id + ".asset";
                    TunedVehiclePerformanceUpgrade upgrade =
                        LoadOrDeleteInvalidAsset<TunedVehiclePerformanceUpgrade>(path);
                    if (upgrade == null)
                    {
                        upgrade = ScriptableObject.CreateInstance<TunedVehiclePerformanceUpgrade>();
                        AssetDatabase.CreateAsset(upgrade, path);
                    }

                    upgrade.ConfigureMetadata(
                        id,
                        GetUpgradeDisplayName(category, tier),
                        category,
                        tier,
                        GetUpgradePrice(category, tier),
                        tier == VehiclePerformanceTier.Junkman);
                    upgrade.ConfigureModifier(CreateUpgradeModifier(category, tier));
                    EditorUtility.SetDirty(upgrade);
                    upgrades[index++] = upgrade;
                }
            }

            catalog.SetUpgrades(upgrades);
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static VehicleCustomizationCatalog GetOrCreateCustomizationCatalog()
        {
            VehicleCustomizationCatalog catalog =
                LoadOrDeleteInvalidAsset<VehicleCustomizationCatalog>(
                    CustomizationCatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<VehicleCustomizationCatalog>();
                AssetDatabase.CreateAsset(catalog, CustomizationCatalogPath);
            }

            return catalog;
        }

        private static VehicleStoreCatalog GetOrCreateStoreCatalog(
            VehiclePerformanceCatalog performanceCatalog)
        {
            VehicleStoreCatalog catalog =
                LoadOrDeleteInvalidAsset<VehicleStoreCatalog>(StoreCatalogPath);
            bool created = catalog == null;
            if (created)
            {
                catalog = ScriptableObject.CreateInstance<VehicleStoreCatalog>();
                AssetDatabase.CreateAsset(catalog, StoreCatalogPath);

                IReadOnlyList<VehiclePerformanceUpgradeDefinition> upgrades =
                    performanceCatalog.Upgrades;
                ScriptableObject[] products = new ScriptableObject[upgrades.Count];
                for (int i = 0; i < upgrades.Count; i++)
                {
                    products[i] = upgrades[i];
                }

                catalog.SetProducts(products);
            }

            if (created)
            {
                EditorUtility.SetDirty(catalog);
            }

            return catalog;
        }

        private static AssetVehicleStorefront GetOrCreateStorefront(
            string path,
            string id,
            string displayName,
            VehicleStoreCategory category,
            VehicleStoreCatalog catalog)
        {
            AssetVehicleStorefront store =
                LoadOrDeleteInvalidAsset<AssetVehicleStorefront>(path);
            if (store == null)
            {
                store = ScriptableObject.CreateInstance<AssetVehicleStorefront>();
                AssetDatabase.CreateAsset(store, path);
                store.Configure(id, displayName, category, catalog);
                EditorUtility.SetDirty(store);
            }

            return store;
        }

        private static VehicleBountyRules GetOrCreateBountyRules()
        {
            VehicleBountyRules rules =
                LoadOrDeleteInvalidAsset<VehicleBountyRules>(BountyRulesPath);
            if (rules == null)
            {
                rules = ScriptableObject.CreateInstance<VehicleBountyRules>();
                AssetDatabase.CreateAsset(rules, BountyRulesPath);
                EditorUtility.SetDirty(rules);
            }

            return rules;
        }

        private static VehiclePoliceResponseProfile
            GetOrCreatePoliceResponseProfile()
        {
            VehiclePoliceResponseProfile profile =
                LoadOrDeleteInvalidAsset<VehiclePoliceResponseProfile>(
                    PoliceResponseProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VehiclePoliceResponseProfile>();
                profile.ConfigureDefault();
                AssetDatabase.CreateAsset(profile, PoliceResponseProfilePath);
                EditorUtility.SetDirty(profile);
            }

            return profile;
        }

        /// <summary>
        /// A typed load returning null can mean either "not created yet" or a
        /// stale YAML asset whose script binding was lost. These paths are all
        /// generated demo data, so remove only that exact invalid asset before
        /// the caller recreates it with the current script GUID.
        /// </summary>
        private static T LoadOrDeleteInvalidAsset<T>(string assetPath)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
            {
                return asset;
            }

            string projectRelativePath = assetPath.StartsWith("Assets/")
                ? assetPath.Substring("Assets/".Length)
                : assetPath;
            string diskPath = Path.Combine(Application.dataPath, projectRelativePath);
            if (File.Exists(diskPath))
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            return null!;
        }

        private static string GetUpgradeDisplayName(
            VehiclePerformanceCategory category,
            VehiclePerformanceTier tier)
        {
            if (tier == VehiclePerformanceTier.Junkman)
            {
                return category + " Junkman";
            }

            if (category == VehiclePerformanceCategory.ForcedInduction)
            {
                return tier + " Turbo";
            }

            return tier + " " + category;
        }

        private static int GetUpgradePrice(
            VehiclePerformanceCategory category,
            VehiclePerformanceTier tier)
        {
            if (tier == VehiclePerformanceTier.Junkman)
            {
                return 0;
            }

            return ((int)tier * 3500) + ((int)category * 400);
        }

        private static VehiclePerformanceModifier CreateUpgradeModifier(
            VehiclePerformanceCategory category,
            VehiclePerformanceTier tier)
        {
            float level = (int)tier;
            VehiclePerformanceModifier modifier = new VehiclePerformanceModifier();
            switch (category)
            {
                case VehiclePerformanceCategory.Engine:
                    modifier.maxSpeedMultiplier = 1f + (0.015f * level);
                    modifier.engineTorqueMultiplier = 1f + (0.055f * level);
                    modifier.engineRedlineMultiplier = 1f + (0.012f * level);
                    modifier.engineInertiaMultiplier = 1f - (0.045f * level);
                    break;
                case VehiclePerformanceCategory.Transmission:
                    modifier.gearRatioMultiplier = 1f + (0.02f * level);
                    modifier.finalDriveMultiplier = 1f + (0.018f * level);
                    modifier.shiftDurationMultiplier = 1f - (0.10f * level);
                    break;
                case VehiclePerformanceCategory.ForcedInduction:
                    modifier.engineTorqueMultiplier = 1f + (0.10f * level);
                    modifier.peakTorqueRpmMultiplier = 1f + (0.02f * level);
                    modifier.engineRedlineMultiplier = 1f + (0.008f * level);
                    break;
                case VehiclePerformanceCategory.Nitrous:
                    modifier.nitrousTorqueMultiplier = 1f + (0.12f * level);
                    modifier.nitrousFuelMultiplier = 1f + (0.10f * level);
                    break;
                case VehiclePerformanceCategory.Tires:
                    modifier.tireLongitudinalGripMultiplier = 1f + (0.045f * level);
                    modifier.tireLateralGripMultiplier = 1f + (0.05f * level);
                    modifier.tirePeakSlipMultiplier = 1f - (0.02f * level);
                    modifier.tireRollingResistanceMultiplier = 1f - (0.025f * level);
                    break;
                case VehiclePerformanceCategory.Brakes:
                    modifier.brakeTorqueMultiplier = 1f + (0.10f * level);
                    break;
                case VehiclePerformanceCategory.Suspension:
                    modifier.suspensionSpringMultiplier = 1f + (0.09f * level);
                    modifier.suspensionDamperMultiplier = 1f + (0.08f * level);
                    modifier.suspensionTravelMultiplier = 1f + (0.015f * level);
                    break;
            }

            return modifier;
        }

        private static VehicleController CreateVehicle(
            VehicleTuning tuning,
            VehiclePerformanceCatalog performanceCatalog,
            VehicleCustomizationCatalog customizationCatalog,
            VehicleStoreCatalog storeCatalog,
            AssetVehicleStorefront defaultStore,
            VehicleBountyRules bountyRules,
            Material carPaint,
            Material glass,
            Material tire,
            Material headlight,
            GameObject vehicleVisualPrefab = null)
        {
            GameObject vehicleObject = new GameObject("Player Vehicle - MW Street Racer");
            vehicleObject.transform.position = new Vector3(0f, 0.72f, -66f);
            SetLayerRecursively(vehicleObject, 2);

            Rigidbody body = vehicleObject.AddComponent<Rigidbody>();
            body.mass = tuning.chassis.mass;
            BoxCollider chassisCollider = vehicleObject.AddComponent<BoxCollider>();
            chassisCollider.center = new Vector3(0f, 0.25f, 0f);
            chassisCollider.size = new Vector3(1.9f, 0.64f, 4.25f);

            if (vehicleVisualPrefab == null)
            {
                CreateVisualBox("Chassis", vehicleObject.transform, new Vector3(0f, 0.25f, 0f), new Vector3(1.9f, 0.55f, 4.25f), carPaint);
                CreateVisualBox("Cabin Glass", vehicleObject.transform, new Vector3(0f, 0.62f, -0.10f), new Vector3(1.43f, 0.28f, 1.58f), glass);
                CreateVisualBox("Hood", vehicleObject.transform, new Vector3(0f, 0.48f, 1.25f), new Vector3(1.72f, 0.18f, 1.16f), carPaint);
                CreateVisualBox("Rear Deck", vehicleObject.transform, new Vector3(0f, 0.46f, -1.48f), new Vector3(1.72f, 0.17f, 0.72f), carPaint);
                CreateVisualBox("Rear Wing", vehicleObject.transform, new Vector3(0f, 0.77f, -1.88f), new Vector3(1.82f, 0.08f, 0.18f), carPaint);
                CreateVisualBox("Front Light L", vehicleObject.transform, new Vector3(-0.60f, 0.49f, 2.14f), new Vector3(0.35f, 0.13f, 0.06f), headlight);
                CreateVisualBox("Front Light R", vehicleObject.transform, new Vector3(0.60f, 0.49f, 2.14f), new Vector3(0.35f, 0.13f, 0.06f), headlight);
            }

            PlayerVehicleInput input = vehicleObject.AddComponent<PlayerVehicleInput>();
            VehiclePowertrain powertrain = vehicleObject.AddComponent<VehiclePowertrain>();
            VehicleAssists assists = vehicleObject.AddComponent<VehicleAssists>();
            vehicleObject.AddComponent<VehicleNitrous>();
            vehicleObject.AddComponent<VehicleModuleHost>();
            VehiclePerformanceSystem performance =
                vehicleObject.AddComponent<VehiclePerformanceSystem>();
            performance.SetCatalog(performanceCatalog);
            VehicleCustomizationSystem customization =
                vehicleObject.AddComponent<VehicleCustomizationSystem>();
            customization.SetCatalog(customizationCatalog);
            vehicleObject.AddComponent<VehicleCustomizationVisualAdapter>();
            VehicleController controller = vehicleObject.AddComponent<VehicleController>();
            vehicleObject.AddComponent<VehicleTelemetryHud>();
            AttachVehicleCareer(vehicleObject, storeCatalog, defaultStore, bountyRules);

            VehicleWheel[] wheels = new VehicleWheel[4];
            wheels[0] = CreateWheel(vehicleObject.transform, "Wheel FL", new Vector3(-0.86f, -0.18f, 1.38f), VehicleAxle.Front, true, false, false, tire, tuning);
            wheels[1] = CreateWheel(vehicleObject.transform, "Wheel FR", new Vector3(0.86f, -0.18f, 1.38f), VehicleAxle.Front, true, false, false, tire, tuning);
            wheels[2] = CreateWheel(vehicleObject.transform, "Wheel RL", new Vector3(-0.86f, -0.18f, -1.38f), VehicleAxle.Rear, false, true, true, tire, tuning);
            wheels[3] = CreateWheel(vehicleObject.transform, "Wheel RR", new Vector3(0.86f, -0.18f, -1.38f), VehicleAxle.Rear, false, true, true, tire, tuning);

            controller.Tuning = tuning;
            controller.SetInputSource(input);
            controller.Wheels = wheels;
            powertrain.Configure(tuning, wheels);
            assists.Configure(body, tuning, wheels);
            if (vehicleVisualPrefab != null)
            {
                AttachImportedVehicleVisual(vehicleObject, vehicleVisualPrefab);
            }

            return controller;
        }


        // Shared gameplay composition for authored prefabs and the existing scene builders.
        internal static void AttachVehicleCareer(GameObject vehicleObject, VehicleStoreCatalog storeCatalog, AssetVehicleStorefront defaultStore, VehicleBountyRules bountyRules)
        {
            VehicleStoreSystem store = vehicleObject.AddComponent<VehicleStoreSystem>();
            store.SetCatalog(storeCatalog);
            store.SetDefaultStore(defaultStore);
            VehicleStoreWallet wallet = vehicleObject.AddComponent<VehicleStoreWallet>();
            VehicleStoreOwnership ownership =
                vehicleObject.AddComponent<VehicleStoreOwnership>();
            VehicleStoreGarage garage = vehicleObject.AddComponent<VehicleStoreGarage>();
            wallet.SetBalance(25000);
            store.SetWallet(wallet);
            store.SetOwnership(ownership);
            store.SetGarage(garage);
            vehicleObject.AddComponent<VehiclePerformanceStoreAdapter>();
            vehicleObject.AddComponent<VehicleCustomizationStoreAdapter>();
            vehicleObject.AddComponent<VehicleCarShowStoreAdapter>();
            VehicleBountySystem bounty = vehicleObject.AddComponent<VehicleBountySystem>();
            bounty.SetRules(bountyRules);
            vehicleObject.AddComponent<VehiclePursuitTargetAdapter>();
            JsonCareerProfileStorage profileStorage =
                vehicleObject.AddComponent<JsonCareerProfileStorage>();
            CareerProfileSystem profile = vehicleObject.AddComponent<CareerProfileSystem>();
            profile.SetStorage(profileStorage);
            profile.SetProfileId("demo_profile");
            profile.SetPlayerName("Street Racer");
            profile.SetActiveVehicleId("demo_vehicle");

        }

        internal static void AttachImportedVehicleVisual(GameObject vehicle, GameObject visualPrefab)
        {
            if (visualPrefab == null)
            {
                throw new System.ArgumentNullException(nameof(visualPrefab));
            }

            Transform visualRoot = vehicle.transform.Find("BMW M3 E42 Visual");
            if (visualRoot == null)
            {
                GameObject visualRootObject = new GameObject("BMW M3 E42 Visual");
                visualRoot = visualRootObject.transform;
                visualRoot.SetParent(vehicle.transform, false);
            }

            GameObject existingInstance = visualRoot.childCount == 0
                ? null
                : visualRoot.GetChild(0).gameObject;
            if (existingInstance == null
                || PrefabUtility.GetCorrespondingObjectFromSource(existingInstance) != visualPrefab)
            {
                for (int i = visualRoot.childCount - 1; i >= 0; i--)
                    Undo.DestroyObjectImmediate(visualRoot.GetChild(i).gameObject);
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab, visualRoot);
                instance.name = "BMW M3 E42";
            }

            // The FBX's imported forward axis points toward -Z. The vehicle
            // controller, wheel mounts and chase camera use +Z as forward, so
            // correct the presentation on the wrapper instead of changing the
            // authored model or physics orientation.
            visualRoot.localRotation = Quaternion.Euler(0f, 180f, 0f);
            SetLayerRecursively(visualRoot.gameObject, 2);
            HidePlaceholderVehicleVisuals(vehicle.transform);
        }

        private static void HidePlaceholderVehicleVisuals(Transform vehicle)
        {
            string[] placeholderNames =
            {
                "Chassis",
                "Cabin Glass",
                "Hood",
                "Rear Deck",
                "Rear Wing",
                "Front Light L",
                "Front Light R",
                "Wheel FL Visual",
                "Wheel FR Visual",
                "Wheel RL Visual",
                "Wheel RR Visual"
            };

            Transform[] children = vehicle.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                for (int nameIndex = 0; nameIndex < placeholderNames.Length; nameIndex++)
                {
                    if (children[i].name == placeholderNames[nameIndex])
                    {
                        children[i].gameObject.SetActive(false);
                        break;
                    }
                }
            }
        }

        private static VehicleWheel CreateWheel(
            Transform vehicle,
            string name,
            Vector3 localPosition,
            VehicleAxle axle,
            bool steering,
            bool driven,
            bool handbrake,
            Material tireMaterial,
            VehicleTuning tuning)
        {
            GameObject mount = new GameObject(name);
            mount.transform.SetParent(vehicle, false);
            mount.transform.localPosition = localPosition;
            mount.transform.localRotation = Quaternion.identity;
            SetLayerRecursively(mount, 2);

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visual.name = name + " Visual";
            visual.transform.SetParent(mount.transform, false);
            visual.transform.localScale = new Vector3(
                tuning.tires.wheelRadius,
                tuning.tires.wheelWidth * 0.5f,
                tuning.tires.wheelRadius);
            visual.GetComponent<Renderer>().sharedMaterial = tireMaterial;
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            SetLayerRecursively(visual, 2);

            VehicleWheel wheel = mount.AddComponent<VehicleWheel>();
            wheel.Setup(axle, steering, driven, handbrake, visual.transform);
            wheel.SetGroundMask(Physics.DefaultRaycastLayers);
            wheel.Configure(vehicle.GetComponent<Rigidbody>(), tuning);
            return wheel;
        }

        private static VehicleCameraRig CreateCamera(VehicleController vehicle)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 3.2f, -72f);
            Camera camera = cameraObject.AddComponent<Camera>(); Rendering.HdrpSceneDefaults.Camera(camera);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.045f, 0.08f);
            camera.fieldOfView = 64f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 850f;
            cameraObject.AddComponent<AudioListener>();
            VehicleCameraRig rig = cameraObject.AddComponent<VehicleCameraRig>();
            VehicleCameraPresets.Ensure();
            rig.SetProfile(AssetDatabase.LoadAssetAtPath<VehicleCameraProfile>(VehicleCameraPresets.Folder + "/MostWantedInspired.asset"));
            cameraObject.AddComponent<VehicleCameraPostProcessing>();
            rig.SetTarget(vehicle.transform, vehicle.Body);
            return rig;
        }

        private static void CreateLighting()
        {
            GameObject lightObject = new GameObject("Daylight sun");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, .96f, .9f);
            Rendering.HdrpSceneDefaults.Sun(light,90000);
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(38f, -28f, 0f);
        }

        private static void CreateTrack(Material asphalt, Material grass, Material curb, Material laneMarking)
        {
            GameObject track = new GameObject("Driving Sandbox");
            GameObject ground = CreateBox("Grass Field", track.transform, new Vector3(0f, -0.55f, 0f), new Vector3(260f, 1f, 260f), grass, true);
            VehicleSurface groundSurface = ground.AddComponent<VehicleSurface>();
            groundSurface.Configure("Grass", 0.70f, 1.8f);

            GameObject longRoad = CreateBox("Main Straight", track.transform, new Vector3(0f, -0.035f, 0f), new Vector3(15f, 0.12f, 220f), asphalt, true);
            VehicleSurface longSurface = longRoad.AddComponent<VehicleSurface>();
            longSurface.Configure("Asphalt", 1.02f, 0.92f);

            GameObject crossRoad = CreateBox("Cross Road", track.transform, new Vector3(0f, -0.025f, 14f), new Vector3(180f, 0.10f, 15f), asphalt, true);
            VehicleSurface crossSurface = crossRoad.AddComponent<VehicleSurface>();
            crossSurface.Configure("Asphalt", 1.02f, 0.92f);

            CreateBox("West Curb", track.transform, new Vector3(-8.2f, 0.13f, 0f), new Vector3(0.28f, 0.28f, 220f), curb, true);
            CreateBox("East Curb", track.transform, new Vector3(8.2f, 0.13f, 0f), new Vector3(0.28f, 0.28f, 220f), curb, true);
            CreateBox("North Curb", track.transform, new Vector3(0f, 0.13f, 22f), new Vector3(180f, 0.28f, 0.28f), curb, true);
            CreateBox("South Curb", track.transform, new Vector3(0f, 0.13f, 6f), new Vector3(180f, 0.28f, 0.28f), curb, true);

            for (int z = -100; z <= 100; z += 12)
            {
                CreateVisualBox("Center Mark", track.transform, new Vector3(0f, 0.038f, z), new Vector3(0.18f, 0.012f, 5.5f), laneMarking);
            }

            for (int x = -78; x <= 78; x += 12)
            {
                CreateVisualBox("Cross Mark", track.transform, new Vector3(x, 0.038f, 14f), new Vector3(5.5f, 0.012f, 0.18f), laneMarking);
            }

            CreateBox("North Barrier L", track.transform, new Vector3(-12f, 0.65f, 86f), new Vector3(0.45f, 1.1f, 8f), curb, true);
            CreateBox("North Barrier R", track.transform, new Vector3(12f, 0.65f, 86f), new Vector3(0.45f, 1.1f, 8f), curb, true);
            CreateBox("South Barrier L", track.transform, new Vector3(-12f, 0.65f, -86f), new Vector3(0.45f, 1.1f, 8f), curb, true);
            CreateBox("South Barrier R", track.transform, new Vector3(12f, 0.65f, -86f), new Vector3(0.45f, 1.1f, 8f), curb, true);
        }

        private static GameObject CreateBox(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            bool keepCollider)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = localScale;
            box.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider)
            {
                Object.DestroyImmediate(box.GetComponent<Collider>());
            }

            return box;
        }

        private static GameObject CreateVisualBox(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            return CreateBox(name, parent, localPosition, localScale, material, false);
        }

        private static Material GetOrCreateMaterial(
            string fileName,
            Color color,
            float metallic,
            float smoothness)
        {
            string path = MaterialsFolder + "/" + fileName;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("HDRP/Lit");
                if (shader == null) throw new System.InvalidOperationException("HDRP Lit is required.");

                material = fileName == "Asphalt.mat" ? Rendering.HdrpMaterialDefaults.Road() : new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            if (fileName == "Asphalt.mat")
            {
                material.SetColor("_BaseColor", new Color(.16f, .165f, .17f));
                material.SetFloat("_Smoothness", .4f);
            }
            if (fileName == "CarPaint.mat") material.SetFloat("_CoatMask", .75f);
            if (fileName == "Glass.mat")
            {
                color.a = .22f; material.SetColor("_BaseColor", color); material.SetFloat("_Metallic", 0);
                UnityEngine.Rendering.HighDefinition.HDMaterial.SetSurfaceType(material, true);
            }
            if (fileName == "Headlight.mat") material.SetColor("_EmissiveColor", color * 15000);
            Rendering.HdrpMaterialDefaults.Validate(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            for (int i = 0; i < root.transform.childCount; i++)
            {
                SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
            }
        }
    }
}
#endif
