#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    public static partial class DrivingDemoBuilder
    {
        internal const string SensoryFolder = "Assets/NfsMw/Modules/Driving/Data/Sensory";
        [MenuItem("NFS MW Remaster/Sensory/Build Sensory Test Scene")]
        public static void BuildSensoryTestScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureTestFolders(); EnsureFolder(SensoryFolder);
            var tuning = GetOrCreateTuning();
            var performance = AssetDatabase.LoadAssetAtPath<VehiclePerformanceCatalog>(PerformanceCatalogPath);
            var customization = AssetDatabase.LoadAssetAtPath<VehicleCustomizationCatalog>(CustomizationCatalogPath);
            var store = AssetDatabase.LoadAssetAtPath<VehicleStoreCatalog>(StoreCatalogPath);
            var shop = AssetDatabase.LoadAssetAtPath<AssetVehicleStorefront>(OneStopShopPath);
            var bounty = GetOrCreateBountyRules();
            var asphalt = GetOrCreateMaterial("SensoryAsphalt.mat", new Color(0.08f, 0.09f, 0.11f), 0, 0.5f);
            var grass = GetOrCreateMaterial("SensoryGrass.mat", new Color(0.13f, 0.22f, 0.12f), 0, 0.2f);
            var paint = GetOrCreateMaterial("SensoryPaint.mat", new Color(0.08f, 0.3f, 0.7f), 0.6f, 0.7f);
            var white = GetOrCreateMaterial("SensoryWhite.mat", Color.white, 0, 0.3f);
            var red = GetOrCreateMaterial("SensoryRed.mat", Color.red, 0, 0.3f);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateLighting(); CreateTrack(asphalt, grass, red, white);
            var vehicle = CreateVehicle(tuning, performance, customization, store, shop, bounty, paint, asphalt, asphalt, white);
            var rig = CreateCamera(vehicle);
            vehicle.ConfigureForRuntime(tuning, vehicle.GetComponent<PlayerVehicleInput>(), vehicle.Wheels, rig);
            var profile = vehicle.GetComponent<CareerProfileSystem>();
            if (profile != null) profile.ConfigureAutomaticPersistence(false, false, false);
            var target = vehicle.GetComponent<VehiclePursuitTargetAdapter>();
            if (target == null) target = vehicle.gameObject.AddComponent<VehiclePursuitTargetAdapter>();
            var director = new GameObject("Sensory Pursuit").AddComponent<VehiclePursuitDirector>();
            director.Configure(target, vehicle.GetComponent<VehicleBountySystem>(), GetOrCreatePoliceResponseProfile());
            var fleet = new GameObject("24 physical police - stress grid");
            for (int i = 0; i < 24; i++)
            {
                var unit = CreatePoliceUnit(fleet.transform, "sensory-" + i, VehiclePoliceUnitRole.Pursuer,
                    new Vector3(12 + i % 6 * 5, 0.8f, -45 + i / 6 * 9), asphalt, white, red, paint);
                unit.SetDirector(director);
            }
            var roads = new GameObject("Stress traffic road network").AddComponent<RoadNetwork>();
            roads.Configure(new[] { new RoadNode { position = new Vector3(-66, 0, -66), exits = new[] { 1, 3 } },
                new RoadNode { position = new Vector3(66, 0, -66), exits = new[] { 0, 2 } },
                new RoadNode { position = new Vector3(66, 0, 66), exits = new[] { 1, 3 } },
                new RoadNode { position = new Vector3(-66, 0, 66), exits = new[] { 0, 2 } } });
            for (int i = 0; i < 16; i++)
            {
                var traffic = CreateBox("Stress civilian " + i, roads.transform, new Vector3(-60 + i * 7, 0.8f, 66), new Vector3(1.8f, 0.7f, 4), paint, true);
                traffic.layer = 2; traffic.AddComponent<Rigidbody>(); var motor = traffic.AddComponent<RoadVehicleMotor>();
                motor.Configure(roads, null, 12); motor.ConfigureDriver(i + 500);
            }
            InstallSensoryInOpenScene();
            var world = UnityEngine.Object.FindAnyObjectByType<DestructionWorld>();
            for (int i = 0; i < 12; i++)
            {
                var prop = new GameObject("Breakable sign " + i); prop.transform.position = new Vector3((i % 2 == 0 ? -1 : 1) * 8, 0.7f, -30 + i * 6);
                var visual = CreateBox("Sign", prop.transform, Vector3.zero, new Vector3(0.7f, 1.4f, 0.2f), white, false);
                visual.transform.localPosition = Vector3.zero;
                var collider = prop.AddComponent<BoxCollider>(); collider.size = new Vector3(0.7f, 1.4f, 0.2f);
                prop.AddComponent<DestructibleProp>().Configure("sensory.sign." + i, world, visual, new Collider[] { collider }, SensorySurface.Wood);
            }
            SaveTestScene(scene, "Assets/NfsMw/Scenes/Tests/SensoryTest.unity", vehicle.gameObject, "Built sensory test scene: ");
        }

        [MenuItem("NFS MW Remaster/Sensory/Install in Open Scene")]
        public static void InstallSensoryInOpenScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Install sensory components in Edit mode.");
            var rig = UnityEngine.Object.FindAnyObjectByType<VehicleCameraRig>();
            if (rig == null || rig.Target == null) throw new InvalidOperationException("Scene needs a VehicleCameraRig with an explicit player target.");
            var existing = UnityEngine.Object.FindAnyObjectByType<SensoryAudioWorld>();
            if (existing != null) { Debug.Log("Sensory world already installed; existing authored configuration preserved."); return; }
            EnsureFolder("Assets/NfsMw/Modules/Driving/Data"); EnsureFolder(SensoryFolder);
            var content = SensoryContentBuilder.Ensure(SensoryFolder);
            var root = new GameObject("Sensory World"); root.SetActive(false);
            var audio = root.AddComponent<SensoryAudioWorld>(); audio.Configure(rig.transform, rig.Target, SensoryMixerBuilder.GetOrCreate(SensoryFolder));
            var particles = root.AddComponent<SensoryEffectsWorld>(); particles.Configure(content.effects, rig.transform);
            var destruction = root.AddComponent<DestructionWorld>(); destruction.Configure(content.debris, rig.transform, audio, particles, content.vehicle.surfaces);
            var ignoredColliders = new List<Collider>();
            foreach (var vehicle in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (vehicle.GetComponent<VehicleController>() != null || vehicle.GetComponent<RoadVehicleMotor>() != null)
                    ignoredColliders.AddRange(vehicle.GetComponentsInChildren<Collider>(true));
            destruction.SetIgnoredVehicles(ignoredColliders.ToArray());
            var music = root.AddComponent<AdaptiveMusic>(); music.Configure(audio, content.music);
            var player = rig.Target.GetComponent<VehicleController>();
            foreach (var vehicle in UnityEngine.Object.FindObjectsByType<VehicleController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var sounds = vehicle.GetComponent<VehicleAudio>() ?? vehicle.gameObject.AddComponent<VehicleAudio>();
                var vehicleProfile = sounds.Profile != null ? sounds.Profile : content.vehicle;
                sounds.Configure(vehicle, audio, vehicleProfile, vehicle == player, vehicle == player ? rig : null);
                sounds.SetEffects(particles);
                var police = vehicle.GetComponent<PoliceVehicleFeedback>();
                if (police != null) police.ConfigureSensory(audio, content.police);
            }
            foreach (var surface in UnityEngine.Object.FindObjectsByType<VehicleSurface>(FindObjectsSortMode.None))
            {
                if (surface.Profile != null) continue;
                if (Enum.TryParse(surface.SurfaceName.Replace(" ", ""), true, out SensorySurface kind))
                { var entry = content.vehicle.Surface(kind); if (entry != null) surface.SetProfile(entry, false); }
                else if (surface.SurfaceName == "Asphalt") surface.SetProfile(content.vehicle.Surface(SensorySurface.AsphaltDry), false);
            }
            foreach (var traffic in UnityEngine.Object.FindObjectsByType<RoadVehicleMotor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (traffic.GetComponent<VehicleController>() != null) continue;
                var source = traffic.GetComponent<TrafficFeedbackSource>() ?? traffic.gameObject.AddComponent<TrafficFeedbackSource>();
                var sounds = traffic.GetComponent<VehicleAudio>() ?? traffic.gameObject.AddComponent<VehicleAudio>();
                sounds.Configure(source, audio, content.vehicle, false);
            }
            var director = UnityEngine.Object.FindAnyObjectByType<VehiclePursuitDirector>();
            var session = player.GetComponent<FreeRoamSession>();
            var radio = root.AddComponent<PursuitSensoryBridge>(); radio.Configure(director, session, audio, content.police,
                UnityEngine.Object.FindObjectsByType<VehiclePoliceUnit>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                UnityEngine.Object.FindObjectsByType<PoliceRoadHazard>(FindObjectsSortMode.None));
            music.SetPursuit(radio);
            radio.SetVehicleAudio(player.GetComponent<VehicleAudio>());
            root.AddComponent<DestructionGameplayAdapter>().Configure(destruction, player,
                UnityEngine.Object.FindAnyObjectByType<FreeRoamTraffic>(), player.GetComponent<MissionHost>());
            root.SetActive(true);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
            Debug.Log("Installed scene audio with VehicleAudio as the vehicle playback owner.");
        }
        public static void MigrateSensoryGameplayScenes()
        {
            if (!Application.isBatchMode || !Application.dataPath.StartsWith("/private/tmp/", StringComparison.Ordinal))
                throw new InvalidOperationException("Run batch scene migration on a backed-up isolated project.");
            foreach (string path in new[] { "Assets/NfsMw/Scenes/Game/DrivingDemo.unity", FreeRoamScenePath, "Assets/NfsMw/Scenes/Tests/PursuitTest.unity" })
            {
                var scene = EditorSceneManager.OpenScene(path); InstallSensoryInOpenScene();
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Cannot save sensory migration: " + path);
            }
            Debug.Log("SENSORY_SCENES_MIGRATED");
        }
    }
}
#endif
