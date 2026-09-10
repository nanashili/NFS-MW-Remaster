#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using NfsMwRemaster.Lighting;

namespace NfsMwRemaster.Driving.Editor.Weather
{
    /// <summary>
    /// Builds a repeatable, asset-backed scene that exercises the weather
    /// simulation through the existing driving demo. The source scene is copied
    /// before any weather objects are added, so the original demo remains intact.
    /// </summary>
    public static class WeatherDemoBuilder
    {
        public const string ScenePath = "Assets/NfsMw/Scenes/Showcase/WeatherDemo.unity";
        public const string SourceScenePath = "Assets/NfsMw/Scenes/Game/DrivingDemo.unity";
        public const string DataFolder = "Assets/NfsMw/Modules/Driving/Data/Weather";
        public const string ClimatePath = DataFolder + "/WeatherDemoClimate.asset";
        public const string CatalogPath = DataFolder + "/WeatherDemoCatalog.asset";
        public const string AtmospherePath = DataFolder + "/WeatherDemoAtmosphere.asset";
        const string LegacyRoadMaterialPath = DataFolder + "/WeatherDemoRoad.mat";
        const string LegacyRoadNetworkPath = DataFolder + "/WeatherDemoRoadNetwork.asset";
        public const string DemoMapPath = "Assets/NfsMw/Content/World/Models/WeatherDemoCircuit/scene.gltf";
        public const string BmwModelPath = "Assets/NfsMw/Content/Vehicles/Street/BMW/M3 E42/model.fbx";
        public const string WeatheradeRainTexturePath = WeatherPresentationSourceRegistry.RainDropTexturePath;
        public const string WeatheradeRainNormalPath = WeatherPresentationSourceRegistry.RainDropNormalPath;
        public const string WeatheradeRainShaderPath = WeatherPresentationSourceRegistry.RainShaderPath;
        public const string WeatheradeRainMaterialPath = WeatherPresentationSourceRegistry.RainMaterialPath;
        const string WeatherRootName = "Dynamic Weather World";
        const string CircuitRootName = "Demo Circuit";
        const string PlayerVehicleName = "Player Vehicle - BMW M3 E42";
        const string LegacyPlayerVehicleName = "Player Vehicle - MW Street Racer";
        const string PlayerStartName = "Player Start";
        const string DemoMapGuid = "6c6b2517b661e4316844564e0f1d8980";
        const string BmwModelGuid = "d7dcfb174e2064dea8e86c17e6975661";
        const int FirstSurfaceMaterialIndex = 56;
        const int LastSurfaceMaterialIndex = 69;
        const int FirstWetTrackMaterialIndex = 63;
        static readonly Vector3 PlayerSpawnPosition = new Vector3(-505.063f, -5.940f, 519.657f);

        [MenuItem("NFS MW Remaster/Weather/Build Weather Demo Scene")]
        public static void Build()
        {
            EnsureEditMode();
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!WeatherPresentationSourceRegistry.Validate(out string sourceFailure))
                throw new InvalidOperationException("Weather presentation source registry is invalid: " + sourceFailure);

            EnsureFolder("Assets/NfsMw/Modules/Driving/Data");
            EnsureFolder(DataFolder);

            WeatherClimateProfile climate = ConfigureClimate(GetOrCreateAsset<WeatherClimateProfile>(ClimatePath, "WeatherDemoClimate"));
            WeatherPresetCatalog catalog = ConfigureCatalog(GetOrCreateAsset<WeatherPresetCatalog>(CatalogPath, "WeatherDemoCatalog"));
            AtmosphereProfile atmosphere = ConfigureAtmosphere(GetOrCreateAsset<AtmosphereProfile>(AtmospherePath, "WeatherDemoAtmosphere"));
            Material rainMaterial = ResolveRainMaterial();
            Material sprayMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/NfsMw/Modules/Driving/Data/Sensory/DiagnosticParticles.mat") ?? rainMaterial;
            Material shelterMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/NfsMw/Modules/Driving/Data/Materials/FreeRoamConcrete.mat");
            Material shelterAccent = AssetDatabase.LoadAssetAtPath<Material>("Assets/NfsMw/Modules/Driving/Data/Materials/FreeRoamBlue.mat");

            Scene scene = OpenOrCopyScene();
            GameObject oldWeatherRoot = GameObject.Find(WeatherRootName);
            if (oldWeatherRoot) UnityEngine.Object.DestroyImmediate(oldWeatherRoot);
            RemoveExistingRainObjects();
            RemoveExistingRoadAuthoring();
            RemoveGeneratedDrivingSandbox();
            RemoveExistingCircuit();

            Camera camera = FindRequired<Camera>("Main Camera");
            camera.allowHDR = true;
            Light daylight = FindRequired<Light>("Daylight sun");
            EnsureHdrpCelestialLight(daylight);
            RoadWetness roadWetness = FindRequired<RoadWetness>("Road surface wetness");
            GameObject circuit = CreateDemoCircuit();
            WeatherSurfaceRegion surfaceRegion = ConfigureDemoSurfaces(circuit);
            GameObject vehicle = ConfigurePlayerVehicle(circuit.transform);
            ConfigureCamera(camera, vehicle);
            SensoryAudioWorld audioWorld = FindOptional<SensoryAudioWorld>("Sensory World");
            roadWetness.acceptWeather = true;
            EditorUtility.SetDirty(roadWetness);

            GameObject weatherRoot = new GameObject(WeatherRootName);
            Undo.RegisterCreatedObjectUndo(weatherRoot, "Build weather demo");
            WeatherSurfaceCoverage coverage = weatherRoot.AddComponent<WeatherSurfaceCoverage>();
            coverage.roadWetness = roadWetness;
            coverage.regionRefreshSeconds = .25f;
            coverage.maximumSurfaceRegions = 32;
            DynamicWeatherWorld world = weatherRoot.AddComponent<DynamicWeatherWorld>();
            world.enabled = false;

            LocalRain rain = CreateRain(weatherRoot.transform, camera, rainMaterial, sprayMaterial);
            rain.sprayAnchor = vehicle.transform;
            Light lightning = CreateLightning(weatherRoot.transform);
            CreateShelter(weatherRoot.transform, shelterMaterial, shelterAccent);
            Light moon = CreateMoon(weatherRoot.transform);

            SerializedObject worldSerialized = new SerializedObject(world);
            SetObjectReference(worldSerialized, "climate", climate);
            SetObjectReference(worldSerialized, "catalog", catalog);
            SetObjectReference(worldSerialized, "primaryCamera", camera);
            SetObjectReference(worldSerialized, "rain", rain);
            SetObjectReference(worldSerialized, "surfaceCoverage", coverage);
            SetObjectReference(worldSerialized, "roadWetness", roadWetness);
            SetObjectReference(worldSerialized, "audioWorld", audioWorld);
            SetObjectReference(worldSerialized, "rainAmbienceClip", null);
            SetObjectReference(worldSerialized, "windAmbienceClip",
                AssetDatabase.LoadAssetAtPath<AudioClip>(WeatherPresentationSourceRegistry.WindAmbienceClipPath));
            SetObjectReference(worldSerialized, "thunderClip", null);
            SetObjectReference(worldSerialized, "lightningLight", lightning);
            worldSerialized.FindProperty("seed").intValue = climate.defaultSeed;
            worldSerialized.FindProperty("startPaused").boolValue = false;
            worldSerialized.FindProperty("useUnscaledTime").boolValue = true;
            worldSerialized.FindProperty("presentationQuality").enumValueIndex = (int)WeatherPresentationQuality.High;
            worldSerialized.FindProperty("surfaceRefreshSeconds").floatValue = .25f;
            worldSerialized.FindProperty("maximumSurfaceRegions").intValue = 32;
            worldSerialized.ApplyModifiedPropertiesWithoutUndo();
            world.enabled = true;

            AtmosphereController atmosphereController = weatherRoot.AddComponent<AtmosphereController>();
            atmosphereController.fallback = atmosphere;
            atmosphereController.worldCamera = camera;
            atmosphereController.keyLight = daylight;
            atmosphereController.moonLight = moon;
            atmosphereController.moonIntensity = .12f;
            atmosphereController.weatherWorld = world;
            atmosphereController.quality = LightingQuality.High;

            AddToBuildSettings();
            Selection.activeGameObject = weatherRoot;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Could not save weather demo scene: " + ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Validate(scene, climate, catalog, atmosphere, world, atmosphereController, rain, coverage, circuit, vehicle, surfaceRegion);
            Debug.Log("Built weather demo scene at " + ScenePath + ". Enter Play mode and open Weather Studio for controls.");
        }

        public static string ValidateActiveScene()
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            WeatherClimateProfile climate = AssetDatabase.LoadAssetAtPath<WeatherClimateProfile>(ClimatePath);
            WeatherPresetCatalog catalog = AssetDatabase.LoadAssetAtPath<WeatherPresetCatalog>(CatalogPath);
            AtmosphereProfile atmosphere = AssetDatabase.LoadAssetAtPath<AtmosphereProfile>(AtmospherePath);
            DynamicWeatherWorld world = FindRequired<DynamicWeatherWorld>(WeatherRootName);
            AtmosphereController controller = world.GetComponent<AtmosphereController>();
            LocalRain rain = world.GetComponentInChildren<LocalRain>(true);
            WeatherSurfaceCoverage coverage = world.GetComponent<WeatherSurfaceCoverage>();
            GameObject circuit = FindRequiredObject(CircuitRootName);
            GameObject vehicle = FindRequiredObject(PlayerVehicleName);
            WeatherSurfaceRegion region = circuit.GetComponent<WeatherSurfaceRegion>();
            Validate(scene, climate, catalog, atmosphere, world, controller, rain, coverage, circuit, vehicle, region);
            return "Weather demo validation passed: " + scene.path;
        }

        static void Validate(Scene scene, WeatherClimateProfile climate, WeatherPresetCatalog catalog,
            AtmosphereProfile atmosphere, DynamicWeatherWorld world, AtmosphereController controller,
            LocalRain rain, WeatherSurfaceCoverage coverage, GameObject circuit, GameObject vehicle,
            WeatherSurfaceRegion region)
        {
            if (!WeatherPresentationSourceRegistry.Validate(out string sourceFailure))
                throw new InvalidOperationException("Weather presentation source registry is invalid: " + sourceFailure);
            if (!scene.IsValid() || scene.path != ScenePath) throw new InvalidOperationException("Weather demo scene path is invalid.");
            if (!climate) throw new InvalidOperationException("Weather demo climate asset is missing.");
            string climateFailure;
            if (!climate.Validate(out climateFailure)) throw new InvalidOperationException("Weather demo climate is invalid: " + climateFailure);
            if (!catalog) throw new InvalidOperationException("Weather demo catalog asset is missing.");
            string catalogFailure;
            if (!catalog.Validate(out catalogFailure)) throw new InvalidOperationException("Weather demo catalog is invalid: " + catalogFailure);
            if (!atmosphere || !atmosphere.IsValid) throw new InvalidOperationException("Weather demo atmosphere profile is invalid.");
            if (!world || !controller || controller.fallback != atmosphere || controller.weatherWorld != world)
                throw new InvalidOperationException("Weather demo atmosphere is not bound to its weather world.");
            ValidateCircuit(circuit, vehicle, region);
            SerializedObject serialized = new SerializedObject(world);
            if (serialized.FindProperty("climate").objectReferenceValue != climate
                || serialized.FindProperty("catalog").objectReferenceValue != catalog
                || serialized.FindProperty("primaryCamera").objectReferenceValue == null
                || serialized.FindProperty("rain").objectReferenceValue != rain
                || serialized.FindProperty("surfaceCoverage").objectReferenceValue != coverage
                || serialized.FindProperty("roadWetness").objectReferenceValue == null
                || serialized.FindProperty("lightningLight").objectReferenceValue == null)
                throw new InvalidOperationException("Weather demo world references are incomplete.");
            HDAdditionalLightData moonData = controller.moonLight ? controller.moonLight.GetComponent<HDAdditionalLightData>() : null;
            if (moonData == null || moonData.surfaceTexture == null || moonData.celestialBodyShadingSource != HDAdditionalLightData.CelestialBodyShadingSource.ReflectSunLight)
                throw new InvalidOperationException("Weather demo moon is not configured as an HDRP celestial body.");
            if (!rain || !rain.HasCompleteLayerSet || !coverage || !coverage.roadWetness
                || !region || region.renderers == null || region.renderers.Length == 0)
                throw new InvalidOperationException("Weather demo precipitation or surface wiring is incomplete.");
            if (rain.sprayAnchor != vehicle.transform)
                throw new InvalidOperationException("Weather demo road spray is not attached to the BMW player.");
            ValidateRainAppearance(rain);
            ValidateRainCollision(rain);
            WeatherShelterVolume shelter = FindRequired<WeatherShelterVolume>("Shelter exposure volume");
            if (shelter.exposure >= .5f) throw new InvalidOperationException("Weather demo shelter volume exposure is not configured.");
        }

        static void ValidateCircuit(GameObject circuit, GameObject vehicle, WeatherSurfaceRegion region)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(DemoMapPath);
            UnityEngine.Object correspondingCircuit = circuit
                ? PrefabUtility.GetCorrespondingObjectFromSource(circuit)
                : null;
            if (!source || !circuit || circuit.name != CircuitRootName || correspondingCircuit != source
                || AssetDatabase.AssetPathToGUID(DemoMapPath) != DemoMapGuid)
                throw new InvalidOperationException("Weather demo circuit does not correspond to the imported Suzuka asset.");
            if (circuit.transform.position != source.transform.position
                || circuit.transform.rotation != source.transform.rotation
                || circuit.transform.localScale != source.transform.localScale)
                throw new InvalidOperationException("Weather demo circuit transform has drifted from its imported coordinates.");
            if (GameObject.Find("Driving Sandbox") || GameObject.Find("Road Lighting")
                || UnityEngine.Object.FindObjectsByType<RoadNetworkAuthoring>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length != 0)
                throw new InvalidOperationException("Legacy generated road objects remain in the weather demo.");
            if (AssetDatabase.LoadMainAssetAtPath(LegacyRoadNetworkPath)
                || AssetDatabase.LoadMainAssetAtPath(LegacyRoadMaterialPath))
                throw new InvalidOperationException("Legacy generated road assets remain in the weather demo data folder.");

            GameObject bmwSource = AssetDatabase.LoadAssetAtPath<GameObject>(BmwModelPath);
            Transform visualRoot = vehicle ? vehicle.transform.Find("BMW M3 E42 Visual") : null;
            GameObject bmwInstance = visualRoot && visualRoot.childCount == 1
                ? visualRoot.GetChild(0).gameObject
                : null;
            UnityEngine.Object correspondingBmw = bmwInstance
                ? PrefabUtility.GetCorrespondingObjectFromSource(bmwInstance)
                : null;
            if (!vehicle || vehicle.name != PlayerVehicleName || !bmwSource || !bmwInstance
                || correspondingBmw != bmwSource || AssetDatabase.AssetPathToGUID(BmwModelPath) != BmwModelGuid)
                throw new InvalidOperationException("Weather demo player does not use the imported BMW M3 E42 model.");
            if (Vector3.Distance(vehicle.transform.position, PlayerSpawnPosition) > .01f
                || Quaternion.Angle(vehicle.transform.rotation, Quaternion.identity) > .1f
                || Vector3.Dot(vehicle.transform.forward, Vector3.forward) < .999f)
                throw new InvalidOperationException("Weather demo BMW is not aligned to the pole grid slot in race direction.");

            Transform playerStart = circuit.transform.Find(PlayerStartName);
            if (!playerStart || Vector3.Distance(playerStart.position, PlayerSpawnPosition) > .01f
                || Quaternion.Angle(playerStart.rotation, Quaternion.identity) > .1f)
                throw new InvalidOperationException("Weather demo player start does not match the BMW grid pose.");

            var expectedWetTrack = new HashSet<Renderer>();
            MeshCollider gridCollider = null;
            int surfaceCount = 0;
            MeshRenderer[] renderers = circuit.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                int materialIndex = GetDemoSurfaceMaterialIndex(renderer);
                if (materialIndex < FirstSurfaceMaterialIndex || materialIndex > LastSurfaceMaterialIndex) continue;
                surfaceCount++;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                MeshCollider collider = renderer.GetComponent<MeshCollider>();
                VehicleSurface surface = renderer.GetComponent<VehicleSurface>();
                if (!filter || !filter.sharedMesh || !collider || collider.sharedMesh != filter.sharedMesh || !surface)
                    throw new InvalidOperationException("Demo circuit physical surface wiring is incomplete on " + renderer.name + ".");

                bool grass = IsGrassSurfaceMaterial(materialIndex);
                string expectedName = grass ? "Grass" : "Asphalt";
                float expectedGrip = grass ? .70f : 1.02f;
                float expectedResistance = grass ? 1.8f : .92f;
                if (surface.SurfaceName != expectedName
                    || !Mathf.Approximately(surface.GripMultiplier, expectedGrip)
                    || !Mathf.Approximately(surface.RollingResistanceMultiplier, expectedResistance))
                    throw new InvalidOperationException("Demo circuit surface physics changed on material 282_" + materialIndex + ".");
                if (materialIndex >= FirstWetTrackMaterialIndex) expectedWetTrack.Add(renderer);
                if (materialIndex == 64) gridCollider = collider;
            }

            int expectedSurfaceCount = LastSurfaceMaterialIndex - FirstSurfaceMaterialIndex + 1;
            int expectedWetTrackCount = LastSurfaceMaterialIndex - FirstWetTrackMaterialIndex + 1;
            var actualWetTrack = region && region.renderers != null
                ? new HashSet<Renderer>(region.renderers)
                : new HashSet<Renderer>();
            if (surfaceCount != expectedSurfaceCount || expectedWetTrack.Count != expectedWetTrackCount
                || !region || region.transform != circuit.transform || actualWetTrack.Count != expectedWetTrackCount
                || !actualWetTrack.SetEquals(expectedWetTrack))
                throw new InvalidOperationException("Demo circuit physical or wettable surface set is incomplete.");

            Physics.SyncTransforms();
            Ray gridRay = new Ray(new Vector3(PlayerSpawnPosition.x, PlayerSpawnPosition.y + 20, PlayerSpawnPosition.z), Vector3.down);
            RaycastHit gridHit;
            if (!gridCollider || !gridCollider.Raycast(gridRay, out gridHit, 50)
                || Mathf.Abs(gridHit.point.y - (PlayerSpawnPosition.y - .72f)) > .08f)
                throw new InvalidOperationException("Weather demo BMW is not attached to the imported starting-grid surface.");

            Camera camera = FindRequired<Camera>("Main Camera");
            VehicleCameraRig cameraRig = camera.GetComponent<VehicleCameraRig>();
            if (!cameraRig || cameraRig.Target != vehicle.transform || camera.farClipPlane < 3500)
                throw new InvalidOperationException("Weather demo chase camera is not configured for the BMW and full circuit.");
        }

        static void ValidateRainCollision(LocalRain rain)
        {
            ParticleSystem source = rain.nearField ? rain.nearField : rain.particles;
            if (!source || !rain.impactSplashes || !source.collision.enabled)
                throw new InvalidOperationException("Weather demo rain collision source is incomplete.");
            if (!rain.collisionImpacts
                || rain.collisionImpacts.source != source
                || rain.collisionImpacts.impacts != rain.impactSplashes)
                throw new InvalidOperationException("Weather demo rain impacts are not bound to the collision source.");
        }

        static void ValidateRainAppearance(LocalRain rain)
        {
            Texture2D drop = AssetDatabase.LoadAssetAtPath<Texture2D>(WeatheradeRainTexturePath);
            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(WeatheradeRainNormalPath);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(WeatheradeRainShaderPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(WeatheradeRainMaterialPath);
            if (!drop || !normal || !shader || !material || material.shader != shader
                || material.GetTexture("_MainTex") != drop || material.GetTexture("_Normal") != normal
                || !material.GetShaderPassEnabled("DistortionVectors"))
                throw new InvalidOperationException("Weatherade rain texture, normal, shader, or material is not bound.");
            if (AssetDatabase.AssetPathToGUID(WeatheradeRainTexturePath) != "a80ff68124023f34db9cecaf8066d572"
                || AssetDatabase.AssetPathToGUID(WeatheradeRainNormalPath) != "701123aac4fc8fa40a426cf07469dbda")
                throw new InvalidOperationException("Weatherade source texture GUIDs were not preserved.");

            ParticleSystem[] layers = { rain.particles, rain.nearField, rain.farField };
            for (int i = 0; i < layers.Length; i++)
            {
                ParticleSystem layer = layers[i];
                ParticleSystemRenderer renderer = layer ? layer.GetComponent<ParticleSystemRenderer>() : null;
                if (!renderer || renderer.sharedMaterial != material
                    || renderer.renderMode != ParticleSystemRenderMode.Stretch
                    || !Mathf.Approximately(renderer.lengthScale, WeatherRainSourceProfile.StretchMultiplier))
                    throw new InvalidOperationException("A rain layer does not use the Weatherade drop material and stretch profile.");
                if (!WeatherRainSourceProfile.Matches(layer))
                    throw new InvalidOperationException("A rain layer does not use the Weatherade size and lifetime profile.");
            }
            if (!WeatherRainSourceProfile.Matches(rain))
                throw new InvalidOperationException("The Weatherade rain emitter profile is incomplete.");
        }

        static Scene OpenOrCopyScene()
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
            {
                if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
                    throw new InvalidOperationException("Could not copy " + SourceScenePath + " to " + ScenePath);
                AssetDatabase.Refresh();
            }
            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        static void RemoveExistingRainObjects()
        {
            // DrivingDemo carries the older standalone Local Rain prefab. Keep
            // the weather demo single-owner so its bounded presentation is the
            // only emitter that responds to DynamicWeatherWorld.
            LocalRain[] existing = UnityEngine.Object.FindObjectsByType<LocalRain>(FindObjectsSortMode.None);
            for (int i = 0; i < existing.Length; i++)
                if (existing[i]) UnityEngine.Object.DestroyImmediate(existing[i].gameObject);
        }

        static void RemoveExistingRoadAuthoring()
        {
            foreach (RoadNetworkAuthoring network in UnityEngine.Object.FindObjectsByType<RoadNetworkAuthoring>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (network) UnityEngine.Object.DestroyImmediate(network.gameObject);
            GameObject lighting = GameObject.Find("Road Lighting");
            if (lighting) UnityEngine.Object.DestroyImmediate(lighting);
            if (AssetDatabase.LoadMainAssetAtPath(LegacyRoadNetworkPath)) AssetDatabase.DeleteAsset(LegacyRoadNetworkPath);
            if (AssetDatabase.LoadMainAssetAtPath(LegacyRoadMaterialPath)) AssetDatabase.DeleteAsset(LegacyRoadMaterialPath);
            foreach (string guid in AssetDatabase.FindAssets("t:RoadNetworkAsset", new[] { DataFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path).StartsWith("WeatherDemoRoadNetwork", StringComparison.Ordinal))
                    AssetDatabase.DeleteAsset(path);
            }
        }

        static void RemoveGeneratedDrivingSandbox()
        {
            GameObject sandbox = GameObject.Find("Driving Sandbox");
            if (sandbox) UnityEngine.Object.DestroyImmediate(sandbox);
        }

        static void RemoveExistingCircuit()
        {
            GameObject circuit = GameObject.Find(CircuitRootName);
            if (circuit) UnityEngine.Object.DestroyImmediate(circuit);
        }

        static GameObject CreateDemoCircuit()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(DemoMapPath);
            if (!source) throw new InvalidOperationException("Weather demo could not load the demo circuit: " + DemoMapPath);
            GameObject circuit = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (!circuit) throw new InvalidOperationException("Weather demo could not instantiate the demo circuit.");
            circuit.name = CircuitRootName;
            circuit.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            circuit.transform.localScale = source.transform.localScale;
            Undo.RegisterCreatedObjectUndo(circuit, "Create demo circuit");
            return circuit;
        }

        static WeatherSurfaceRegion ConfigureDemoSurfaces(GameObject circuit)
        {
            var wetTrackRenderers = new List<Renderer>(LastSurfaceMaterialIndex - FirstWetTrackMaterialIndex + 1);
            int surfaceCount = 0;
            MeshRenderer[] renderers = circuit.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                int materialIndex = GetDemoSurfaceMaterialIndex(renderer);
                if (materialIndex < FirstSurfaceMaterialIndex || materialIndex > LastSurfaceMaterialIndex) continue;

                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh)
                    throw new InvalidOperationException("Demo circuit surface has no mesh: " + renderer.name);
                MeshCollider collider = renderer.GetComponent<MeshCollider>();
                if (!collider) collider = renderer.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = false;

                VehicleSurface surface = renderer.GetComponent<VehicleSurface>();
                if (!surface) surface = renderer.gameObject.AddComponent<VehicleSurface>();
                if (IsGrassSurfaceMaterial(materialIndex)) surface.Configure("Grass", .70f, 1.8f);
                else surface.Configure("Asphalt", 1.02f, .92f);
                EditorUtility.SetDirty(renderer.gameObject);

                surfaceCount++;
                if (materialIndex >= FirstWetTrackMaterialIndex) wetTrackRenderers.Add(renderer);
            }

            int expectedSurfaceCount = LastSurfaceMaterialIndex - FirstSurfaceMaterialIndex + 1;
            int expectedWetTrackCount = LastSurfaceMaterialIndex - FirstWetTrackMaterialIndex + 1;
            if (surfaceCount != expectedSurfaceCount || wetTrackRenderers.Count != expectedWetTrackCount)
                throw new InvalidOperationException("Demo circuit surface discovery changed. Found " + surfaceCount
                    + " physical surfaces and " + wetTrackRenderers.Count + " wettable track surfaces.");

            WeatherSurfaceRegion region = circuit.GetComponent<WeatherSurfaceRegion>();
            if (!region) region = circuit.AddComponent<WeatherSurfaceRegion>();
            region.exposure = 1;
            region.drainageMultiplier = .9f;
            region.renderers = wetTrackRenderers.ToArray();
            EditorUtility.SetDirty(region);
            return region;
        }

        static GameObject ConfigurePlayerVehicle(Transform circuit)
        {
            GameObject vehicle = GameObject.Find(PlayerVehicleName);
            if (!vehicle) vehicle = GameObject.Find(LegacyPlayerVehicleName);
            if (!vehicle) throw new InvalidOperationException("Weather demo requires the driving demo player vehicle.");
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(BmwModelPath);
            if (!model) throw new InvalidOperationException("Weather demo could not load the BMW model: " + BmwModelPath);

            vehicle.name = PlayerVehicleName;
            vehicle.transform.SetPositionAndRotation(PlayerSpawnPosition, Quaternion.identity);
            DrivingDemoBuilder.AttachImportedVehicleVisual(vehicle, model);
            Rigidbody body = vehicle.GetComponent<Rigidbody>();
            if (!body) throw new InvalidOperationException("Weather demo player vehicle has no Rigidbody.");
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            EditorUtility.SetDirty(vehicle);

            GameObject existingStart = GameObject.Find(PlayerStartName);
            if (existingStart) UnityEngine.Object.DestroyImmediate(existingStart);
            GameObject playerStart = new GameObject(PlayerStartName);
            playerStart.transform.SetParent(circuit, false);
            playerStart.transform.SetPositionAndRotation(PlayerSpawnPosition, Quaternion.identity);
            Undo.RegisterCreatedObjectUndo(playerStart, "Create player start");
            return vehicle;
        }

        static void ConfigureCamera(Camera camera, GameObject vehicle)
        {
            VehicleCameraRig cameraRig = camera.GetComponent<VehicleCameraRig>();
            Rigidbody body = vehicle.GetComponent<Rigidbody>();
            if (!cameraRig || !body) throw new InvalidOperationException("Weather demo chase camera is incomplete.");
            cameraRig.SetTarget(vehicle.transform, body);
            Vector3 position = vehicle.transform.TransformPoint(new Vector3(0, 2.25f, -6.8f));
            Vector3 lookPoint = vehicle.transform.TransformPoint(new Vector3(0, .85f, 3.2f));
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookPoint - position, Vector3.up));
            camera.farClipPlane = 4000;
            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(cameraRig);
        }

        static int GetDemoSurfaceMaterialIndex(Renderer renderer)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (!material || !material.name.StartsWith("282_", StringComparison.Ordinal)) continue;
                int digitStart = 4;
                int digitEnd = digitStart;
                while (digitEnd < material.name.Length && char.IsDigit(material.name[digitEnd])) digitEnd++;
                int value;
                if (digitEnd > digitStart
                    && int.TryParse(material.name.Substring(digitStart, digitEnd - digitStart), out value)
                    && value >= FirstSurfaceMaterialIndex && value <= LastSurfaceMaterialIndex)
                    return value;
            }
            return -1;
        }

        static bool IsGrassSurfaceMaterial(int materialIndex)
        {
            return materialIndex == 56 || materialIndex == 57 || materialIndex == 58
                || materialIndex == 60 || materialIndex == 61 || materialIndex == 62;
        }

        static WeatherClimateProfile ConfigureClimate(WeatherClimateProfile climate)
        {
            WeatherClimateProfile defaults = WeatherClimateProfile.CreateRuntimeDefaults();
            try
            {
                climate.schemaVersion = defaults.schemaVersion;
                climate.defaultSeed = 240905;
                climate.initialPresetId = "thunderstorm";
                climate.fixedStepSeconds = .25f;
                climate.dayLengthRealSeconds = 900;
                climate.weatherTimeScale = 1;
                climate.weatherFollowsClockScale = false;
                climate.maxCatchUpSeconds = 8;
                // Keep the storm in the late-afternoon window so the road, rain
                // streaks, and wetness remain readable without an artificial look.
                climate.startingTimeHours = 16.0f;
                climate.dawnHours = 5.5f;
                climate.sunriseHours = 6.5f;
                climate.sunsetHours = 18.5f;
                climate.duskHours = 19.5f;
                climate.daylightIntensityCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
                climate.sunElevationCurve = AnimationCurve.Linear(0, -18, 1, 58);
                climate.daylightColorGradient = defaults.daylightColorGradient;
                climate.rainFrequency = .55f;
                climate.stormLikelihood = .12f;
                climate.dryingTendency = .38f;
                climate.temperatureC = 18;
                climate.atmosphericMoisture = .72f;
                climate.minimumPersistenceSeconds = 150;
                climate.maximumPersistenceSeconds = 780;
                climate.wetnessAccumulationPerSecond = .09f;
                climate.wetnessDrainagePerSecond = .025f;
                climate.standingWaterDrainagePerSecond = .035f;
                climate.sunlightDryingPerSecond = .018f;
                climate.windDryingPerSecond = .002f;
                climate.surfaceWetnessThreshold = .4f;
                climate.lightningQuietSecondsMin = 8;
                climate.lightningQuietSecondsMax = 36;
                climate.maximumLightningEvents = 4;
                climate.soundSpeedMetersPerSecond = 343;
                climate.transitions = WeatherClimateProfile.DefaultTransitions();
                climate.name = "WeatherDemoClimate";
                EditorUtility.SetDirty(climate);
                return climate;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(defaults);
            }
        }

        static WeatherPresetCatalog ConfigureCatalog(WeatherPresetCatalog catalog)
        {
            catalog.schemaVersion = 1;
            catalog.presets = WeatherPresetCatalog.DefaultPresets();
            catalog.name = "WeatherDemoCatalog";
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        static AtmosphereProfile ConfigureAtmosphere(AtmosphereProfile profile)
        {
            profile.schemaVersion = AtmosphereProfile.CurrentSchema;
            profile.id = "weather-demo-atmosphere";
            profile.intent = "Rainy dusk demonstration for the Dynamic Weather World; HDRP fallback with restrained fog and exposure.";
            profile.referenceNotes = "Generated by WeatherDemoBuilder. Replace with an art-approved look when the scene is production-bound.";
            profile.semanticVariant = "rainy-dusk";
            profile.look = AtmosphereLook.Neutral;
            profile.look.sky = new Color(.23f, .29f, .38f);
            profile.look.equator = new Color(.12f, .17f, .22f);
            profile.look.ground = new Color(.045f, .055f, .065f);
            profile.look.fog = true;
            // Keep the fallback extinction long enough for HDRP volumetrics to
            // stay smooth in the small demo while storm density still adds haze.
            profile.look.fogDensity = .0007f;
            profile.look.fogStart = 18;
            profile.look.fogEnd = 720;
            profile.look.automaticExposure = false;
            // HDRP fixed exposure is deliberately restrained for this overcast
            // storm; the physically based key and cloud shade still do the work.
            profile.look.exposureEV100 = 9.5f;
            profile.look.keyIntensity = 105000;
            profile.look.keyEuler = new Vector3(32, -28, 0);
            profile.look.fogStart = 50;
            profile.look.fogEnd = 650;
            profile.look.reflectionIntensity = 1.2f;
            profile.look.bloom = .08f;
            profile.look.vignette = .04f;
            profile.lightingSeconds = .8f;
            profile.fogSeconds = 1.5f;
            profile.exposureSeconds = .8f;
            profile.reflectionSeconds = 2;
            profile.allowRealtimeFallback = true;
            profile.lowFixtureIntensity = .75f;
            profile.lowDecorativeShadows = true;
            profile.realtimeBudget = 32;
            profile.shadowBudget = 8;
            profile.name = "WeatherDemoAtmosphere";
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static LocalRain CreateRain(Transform parent, Camera camera, Material material, Material sprayMaterial)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(WeatherPresentationSourceRegistry.RainPrefabPath);
            if (!source) throw new InvalidOperationException("Weatherade rain prefab is missing: " + WeatherPresentationSourceRegistry.RainPrefabPath);
            GameObject rainObject = PrefabUtility.InstantiatePrefab(source, parent) as GameObject;
            if (!rainObject) throw new InvalidOperationException("Could not instantiate the Weatherade rain prefab.");
            rainObject.name = "Weather particle volume";
            LocalRain rain = rainObject.GetComponent<LocalRain>();
            if (!rain) throw new InvalidOperationException("The Weatherade rain prefab has no LocalRain adapter.");
            ParticleSystem particles = rain.particles;
            if (!particles) particles = rainObject.GetComponentInChildren<ParticleSystem>(true);
            if (!particles) throw new InvalidOperationException("The Weatherade rain prefab has no particle source.");
            ConfigureWeatheradeRainLayer(particles, material, 3200);
            rain.view = camera.transform;
            rain.particles = particles;
            rain.dropsPerSecond = 4200;
            rain.turbulence = .3f;
            WeatherRainSourceProfile.ApplyTo(rain);
            rain.regionRadius = 72;
            rain.teleportDistance = 60;
            rain.shelterLayers = Physics.DefaultRaycastLayers;
            rain.emissionFadeInSeconds = .28f;
            rain.emissionFadeOutSeconds = .7f;
            rain.prewarmSeconds = 3;
            ParticleSystem near = CreateRainLayer("Near-field rain", rainObject.transform, material, 2200);
            ParticleSystem far = CreateRainLayer("Far-field rain", rainObject.transform, material, 1500);
            ParticleSystem impacts = CreateImpactSplashes(near.transform, sprayMaterial);
            RainCollisionImpactEmitter collisionImpacts = ConfigureCollisionImpacts(near, impacts);
            rain.nearField = near;
            rain.farField = far;
            rain.impactSplashes = impacts;
            rain.collisionImpacts = collisionImpacts;
            rain.nearFieldEmission = .34f;
            rain.farFieldEmission = .16f;
            rain.nearFieldFallSpeed = 10;
            rain.farFieldFallSpeed = 10;
            // Keep the legacy bindings populated so copied prefabs remain readable
            // by older tooling while new runtime code uses the named roles.
            rain.additionalLayers = new[] { near, far };
            rain.additionalLayerEmission = new[] { .34f, .16f };
            rain.additionalLayerFallSpeed = new[] { WeatherRainSourceProfile.FallSpeedMetersPerSecond, WeatherRainSourceProfile.FallSpeedMetersPerSecond };
            rain.surfaceMist = CreateSurfaceMist(rainObject.transform, sprayMaterial);
            rain.surfaceMistEmission = .032f;
            rain.surfaceMistDistance = 19;
            return rain;
        }

        static ParticleSystem CreateRainLayer(string name, Transform parent, Material material, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();
            ConfigureWeatheradeRainLayer(system, material, maxParticles);
            return system;
        }

        static void ConfigureWeatheradeRainLayer(ParticleSystem system, Material material, int maxParticles)
        {
            var main = system.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                WeatherRainSourceProfile.LifetimeMinSeconds,
                WeatherRainSourceProfile.LifetimeMaxSeconds);
            main.startSpeed = 0;
            main.startSize = new ParticleSystem.MinMaxCurve(
                WeatherRainSourceProfile.DropSizeMinMeters,
                WeatherRainSourceProfile.DropSizeMaxMeters);
            main.startColor = Color.white;
            main.maxParticles = maxParticles;
            var emission = system.emission; emission.enabled = true; emission.rateOverTime = 0;
            var shape = system.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(WeatherRainSourceProfile.EmitterFootprintMeters, .1f, WeatherRainSourceProfile.EmitterFootprintMeters);
            var colorOver = system.colorOverLifetime;
            colorOver.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(.9292453f, .9611543f, 1), 0),
                    new GradientColorKey(new Color(.92941177f, .9607843f, 1), 1)
                },
                new[]
                {
                    new GradientAlphaKey(0, 0),
                    new GradientAlphaKey(.627451f, .0588235f),
                    new GradientAlphaKey(.654902f, .907729f),
                    new GradientAlphaKey(0, 1)
                });
            colorOver.color = gradient;
            var noise = system.noise; noise.enabled = true; noise.strength = .3f; noise.frequency = WeatherRainSourceProfile.SwayFrequency; noise.scrollSpeed = .14f; noise.damping = true;
            var renderer = system.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.velocityScale = 0; renderer.lengthScale = WeatherRainSourceProfile.StretchMultiplier; renderer.alignment = ParticleSystemRenderSpace.View;
            if (material) renderer.sharedMaterial = material;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        static ParticleSystem CreateImpactSplashes(Transform parent, Material material)
        {
            var go = new GameObject("Rain impact splashes");
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();
            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.useUnscaledTime = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.12f, .28f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.22f, .75f);
            main.startSize = new ParticleSystem.MinMaxCurve(.055f, .18f);
            main.startColor = new Color(.72f, .83f, .92f, .36f);
            main.gravityModifier = .18f;
            main.maxParticles = 650;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.x = new ParticleSystem.MinMaxCurve(-.16f, .16f);
            velocity.y = new ParticleSystem.MinMaxCurve(.08f, .42f);
            velocity.z = new ParticleSystem.MinMaxCurve(-.16f, .16f);
            var colorOver = system.colorOverLifetime;
            colorOver.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(.68f, .8f, .9f), 0),
                    new GradientColorKey(new Color(.86f, .93f, 1), .35f),
                    new GradientColorKey(new Color(.68f, .8f, .9f), 1)
                },
                new[]
                {
                    new GradientAlphaKey(0, 0),
                    new GradientAlphaKey(.55f, .12f),
                    new GradientAlphaKey(.16f, .68f),
                    new GradientAlphaKey(0, 1)
                });
            colorOver.color = gradient;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            renderer.alignment = ParticleSystemRenderSpace.World;
            if (material) renderer.sharedMaterial = material;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return system;
        }

        static RainCollisionImpactEmitter ConfigureCollisionImpacts(ParticleSystem source, ParticleSystem impacts)
        {
            var collision = source.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.High;
            collision.collidesWith = Physics.DefaultRaycastLayers;
            collision.dampen = .92f;
            collision.bounce = .02f;
            collision.lifetimeLoss = .72f;
            collision.radiusScale = .25f;
            collision.maxCollisionShapes = 64;
            collision.enableDynamicColliders = false;
            collision.sendCollisionMessages = true;
            RainCollisionImpactEmitter emitter = source.gameObject.AddComponent<RainCollisionImpactEmitter>();
            emitter.source = source;
            emitter.impacts = impacts;
            emitter.maximumImpactsPerFrame = 48;
            return emitter;
        }

        static ParticleSystem CreateSurfaceMist(Transform parent, Material material)
        {
            var go = new GameObject("Road spray mist");
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();
            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.useUnscaledTime = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.24f, .62f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.12f, .8f);
            main.startSize = new ParticleSystem.MinMaxCurve(.06f, .28f);
            main.startColor = new Color(.67f, .79f, .88f, .1f);
            main.maxParticles = 260;
            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0;
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(6.2f, .65f, 7.5f);
            var colorOver = system.colorOverLifetime;
            colorOver.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(.62f, .76f, .86f), 0), new GradientColorKey(new Color(.8f, .9f, 1), .5f), new GradientColorKey(new Color(.62f, .76f, .86f), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.68f, .16f), new GradientAlphaKey(.18f, .68f), new GradientAlphaKey(0, 1) });
            colorOver.color = gradient;
            var noise = system.noise;
            noise.enabled = true;
            noise.strength = .32f;
            noise.frequency = .28f;
            noise.scrollSpeed = .22f;
            noise.damping = true;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            if (material) renderer.sharedMaterial = material;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return system;
        }

        static Light CreateLightning(Transform parent)
        {
            GameObject lightningObject = new GameObject("Lightning flash light");
            lightningObject.transform.SetParent(parent, false);
            lightningObject.transform.localPosition = new Vector3(0, 18, 0);
            Light lightning = lightningObject.AddComponent<Light>();
            lightning.type = LightType.Point;
            lightning.range = 120;
            lightning.intensity = 85000;
            lightning.color = new Color(.78f, .86f, 1);
            lightning.shadows = LightShadows.None;
            lightning.enabled = false;
            return lightning;
        }

        static Light CreateMoon(Transform parent)
        {
            GameObject moonObject = new GameObject("Moon light");
            moonObject.transform.SetParent(parent, false);
            moonObject.transform.localRotation = Quaternion.Euler(28, 145, 0);
            Light moon = moonObject.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(.28f, .36f, .68f);
            moon.intensity = .12f;
            moon.shadows = LightShadows.None;
            HDAdditionalLightData celestial = moonObject.AddComponent<HDAdditionalLightData>();
            HDAdditionalLightData.InitDefaultHDAdditionalLightData(celestial);
            celestial.celestialBodyShadingSource = HDAdditionalLightData.CelestialBodyShadingSource.ReflectSunLight;
            celestial.distance = 384400000;
            celestial.diameterMultiplerMode = false;
            celestial.diameterOverride = 3;
            celestial.sunLightOverride = FindRequired<Light>("Daylight sun");
            celestial.moonPhase = .22f;
            celestial.moonPhaseRotation = 18;
            celestial.earthshine = .8f;
            celestial.flareSize = .15f;
            celestial.flareMultiplier = .35f;
            celestial.surfaceTint = new Color(.72f, .78f, .92f);
            celestial.surfaceTexture = AssetDatabase.LoadAssetAtPath<Texture>(
                "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipelineResources/Texture/MoonAlbedo.png");
            if (!celestial.surfaceTexture) throw new InvalidOperationException("HDRP MoonAlbedo texture is unavailable.");
            // InitDefaultHDAdditionalLightData sets the legacy Light to HDRP's
            // directional default. The weather controller owns this value at
            // runtime, but the authored scene should remain moon-scaled in edit mode.
            moon.color = new Color(.28f, .36f, .68f);
            moon.intensity = .12f;
            moon.shadows = LightShadows.None;
            return moon;
        }

        static HDAdditionalLightData EnsureHdrpCelestialLight(Light light)
        {
            if (!light) throw new InvalidOperationException("Weather demo daylight light is missing.");
            HDAdditionalLightData data = light.GetComponent<HDAdditionalLightData>();
            if (!data) data = Undo.AddComponent<HDAdditionalLightData>(light.gameObject);
            HDAdditionalLightData.InitDefaultHDAdditionalLightData(data);
            data.interactsWithSky = true;
            EditorUtility.SetDirty(data);
            return data;
        }

        static void CreateShelter(Transform parent, Material body, Material accent)
        {
            GameObject shelter = new GameObject("Covered shelter (rain test)");
            shelter.transform.SetParent(parent, false);
            shelter.transform.localPosition = new Vector3(26, 0, -32);
            CreateBox("Shelter floor", shelter.transform, new Vector3(0, .05f, 0), new Vector3(10, .1f, 14), body, false);
            CreateBox("Shelter roof", shelter.transform, new Vector3(0, 5, 0), new Vector3(10, .45f, 14), body, false);
            CreateBox("Shelter post NW", shelter.transform, new Vector3(-4.5f, 2.5f, -6), new Vector3(.45f, 5, .45f), accent, false);
            CreateBox("Shelter post NE", shelter.transform, new Vector3(4.5f, 2.5f, -6), new Vector3(.45f, 5, .45f), accent, false);
            CreateBox("Shelter post SW", shelter.transform, new Vector3(-4.5f, 2.5f, 6), new Vector3(.45f, 5, .45f), accent, false);
            CreateBox("Shelter post SE", shelter.transform, new Vector3(4.5f, 2.5f, 6), new Vector3(.45f, 5, .45f), accent, false);

            GameObject volumeObject = new GameObject("Shelter exposure volume");
            volumeObject.transform.SetParent(shelter.transform, false);
            volumeObject.transform.localPosition = new Vector3(0, 2.5f, 0);
            BoxCollider collider = volumeObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(9, 5, 13);
            WeatherShelterVolume volume = volumeObject.AddComponent<WeatherShelterVolume>();
            volume.exposure = 0;
        }

        static GameObject CreateBox(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collider)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            box.transform.localScale = scale;
            Renderer renderer = box.GetComponent<Renderer>();
            if (renderer && material) renderer.sharedMaterial = material;
            if (!collider)
            {
                Collider sourceCollider = box.GetComponent<Collider>();
                if (sourceCollider) UnityEngine.Object.DestroyImmediate(sourceCollider);
            }
            return box;
        }

        static Material ResolveRainMaterial()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(WeatheradeRainShaderPath);
            Texture2D drop = AssetDatabase.LoadAssetAtPath<Texture2D>(WeatheradeRainTexturePath);
            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(WeatheradeRainNormalPath);
            if (!shader || !drop || !normal)
                throw new InvalidOperationException("Weatherade HDRP rain assets are missing.");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(WeatheradeRainMaterialPath);
            if (!material)
            {
                material = new Material(shader) { name = "WeatheradeRainHDRP" };
                AssetDatabase.CreateAsset(material, WeatheradeRainMaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetTexture("_MainTex", drop);
            material.SetTexture("_Normal", normal);
            material.SetColor("_Color", Color.white);
            material.SetFloat("_NearBlurDistance", 2);
            material.SetFloat("_NearBlurFalloff", 1);
            material.SetFloat("_OpacityFadeStartDistance", .5f);
            material.SetFloat("_OpacityFadeFalloff", .3f);
            material.SetFloat("_RefractionStrength", .5f);
            material.SetShaderPassEnabled("DistortionVectors", true);
            EditorUtility.SetDirty(material);
            return material;
        }

        static T GetOrCreateAsset<T>(string path, string name) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset)
            {
                // Unity can still return a typed object for an asset serialized
                // while its script was unavailable. Treat a missing m_Script as
                // stale so the demo never keeps a non-loadable climate/catalog.
                SerializedObject serialized = new SerializedObject(asset);
                SerializedProperty script = serialized.FindProperty("m_Script");
                if (script == null || script.objectReferenceValue == null)
                {
                    AssetDatabase.DeleteAsset(path);
                    asset = null;
                }
            }
            if (!asset)
            {
                AssetDatabase.DeleteAsset(path);
                asset = ScriptableObject.CreateInstance<T>();
                asset.name = name;
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            if (slash <= 0) throw new InvalidOperationException("Cannot create folder: " + path);
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        static void SetObjectReference(SerializedObject serialized, string name, UnityEngine.Object value)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null) throw new InvalidOperationException("Missing DynamicWeatherWorld field: " + name);
            property.objectReferenceValue = value;
        }

        static void AddToBuildSettings()
        {
            EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
            for (int i = 0; i < existing.Length; i++) if (existing[i].path == ScenePath) return;
            var scenes = new List<EditorBuildSettingsScene>(existing) { new EditorBuildSettingsScene(ScenePath, true) };
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static GameObject FindRequiredObject(string name)
        {
            GameObject result = GameObject.Find(name);
            if (!result) throw new InvalidOperationException("Weather demo requires scene object: " + name);
            return result;
        }

        static T FindRequired<T>(string name) where T : Component
        {
            GameObject result = FindRequiredObject(name);
            T component = result.GetComponent<T>();
            if (!component) throw new InvalidOperationException("Weather demo requires " + typeof(T).Name + " on " + name + ".");
            return component;
        }

        static T FindOptional<T>(string name) where T : Component
        {
            GameObject result = GameObject.Find(name);
            return result ? result.GetComponent<T>() : null;
        }

        static void EnsureEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Build the weather demo in Edit mode.");
        }
    }
}
#endif
