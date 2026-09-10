using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    public static class EventPlacementDemo
    {
        public const string Folder = "Assets/NfsMw/Modules/Driving/Examples/EventPlacement";
        [MenuItem("Tools/NFS MW Remaster/Create Event Placement Sample")]
        public static void Create()
        {
            if (AssetDatabase.IsValidFolder(Folder)) { EditorUtility.DisplayDialog("Sample exists", "Open the existing EventPlacement sample scene. It will not be overwritten.", "OK"); return; }
            Build();
        }
        public static void Build()
        {
            if (AssetDatabase.IsValidFolder(Folder) && Directory.GetFiles(Folder, "*.asset").Length > 0) throw new InvalidOperationException("Sample folder already exists; existing content is preserved.");
            PrepareBatchScene();
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var original = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            try
            {
                var network = ScriptableObject.CreateInstance<RoadNetworkAsset>(); var lane = RoadId.New();
                network.Initialize(RoadId.New(), "activity-graybox-1", new[] { new RoadBakedLane(lane, RoadId.New(), default, 20, new[] {
                    new RoadLaneSample { position = Vector3.zero, distance = 0, width = 10, forward = Vector3.forward, up = Vector3.up, left = Vector3.left },
                    new RoadLaneSample { position = Vector3.forward * 200, distance = 200, width = 10, forward = Vector3.forward, up = Vector3.up, left = Vector3.left }
                }, Array.Empty<RoadId>(), null) }, Array.Empty<RoadBakedChunk>());
                AssetDatabase.CreateAsset(network, Folder + "/AccessRoad.asset");
                var definition = ScriptableObject.CreateInstance<WorldActivityDefinition>(); definition.displayName = "Graybox garage"; definition.category = "Services"; definition.adapter = "service"; definition.serviceKind = WorldLocationKind.Garage; definition.markerColor = Color.cyan;
                AssetDatabase.CreateAsset(definition, Folder + "/Garage.asset");
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "Approved access support"; ground.transform.position = new Vector3(0, -.5f, 100); ground.transform.localScale = new Vector3(100, 1, 240);
                var source = EventPlacementCommands.Create(definition, new Vector3(12, 0, 40)); source.iconOffset = new Vector3(0, 12, 0); source.district = "graybox"; source.streamingCell = "0,0";
                source.access = new ActivityAnchor { kind = ActivityAnchorKind.Lane, network = network, laneId = lane.ToString(), station = 40, sourceRevision = network.Fingerprint };
                source.serviceExitOffsets = new[] { new Vector3(-12, 0, 0), new Vector3(-12, 0, 12) };
                Physics.SyncTransforms(); EventPlacementCommands.Publish(source, Folder + "/GaragePublished.asset");
                var camera = new GameObject("Sample camera").AddComponent<Camera>(); NfsMwRemaster.Driving.Editor.Rendering.HdrpSceneDefaults.Camera(camera); camera.transform.position = new Vector3(40, 40, 5); camera.transform.LookAt(source.transform.position);
                var light = new GameObject("Sun").AddComponent<Light>(); Rendering.HdrpSceneDefaults.Sun(light); light.transform.rotation = Quaternion.Euler(50, -30, 0);
                EditorSceneManager.SaveScene(scene, Folder + "/EventPlacement.unity"); AssetDatabase.SaveAssets();
                Debug.Log("Activity sample created: " + Folder);
            }
            finally { EditorSceneManager.CloseScene(scene, true); if (original.IsValid()) SceneManager.SetActiveScene(original); }
        }
        private static void PrepareBatchScene()
        {
            var active = SceneManager.GetActiveScene();
            if (Application.isBatchMode && string.IsNullOrEmpty(active.path) && active.rootCount == 0)
                EditorSceneManager.SaveScene(active, "Assets/ActivityValidationEmpty.unity");
        }
        public static void ValidateOwner()
        {
            PrepareBatchScene();
            var scene = EditorSceneManager.OpenScene(Folder + "/EventPlacement.unity", OpenSceneMode.Additive);
            try
            {
                var source = scene.GetRootGameObjects(); EventPlacementSource placement = null;
                foreach (var go in source) if (go.TryGetComponent<EventPlacementSource>(out var candidate)) placement = candidate;
                var setup = AssetDatabase.LoadAssetAtPath<RacingVehicleSetup>("Assets/NfsMw/Modules/Driving/Examples/RaceRoutes/Vehicle.asset");
                if (setup == null)
                    foreach (string guid in AssetDatabase.FindAssets("t:RacingVehicleSetup", new[] { "Assets/NfsMw/Modules/Driving/Examples/RaceRoutes" }))
                    { setup = AssetDatabase.LoadAssetAtPath<RacingVehicleSetup>(AssetDatabase.GUIDToAssetPath(guid)); if (setup != null) break; }
                string result = ActivityOwnerPreview.Run(placement, setup); Debug.Log(result);
                File.WriteAllText("ActivityOwnerEvidence.txt", Application.unityVersion + "\n" + SystemInfo.processorType + "\n" + result);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
