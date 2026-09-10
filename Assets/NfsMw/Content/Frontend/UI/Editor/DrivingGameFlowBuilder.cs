#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public static class DrivingGameFlowBuilder
    {
        public const string BootScenePath = "Assets/NfsMw/Content/Frontend/UI/Scenes/Boot.unity";
        public const string SettingsPath = "Assets/NfsMw/Content/Frontend/UI/Data/GameFlowSettings.asset";
        public const string SensoryMixPath = "Assets/NfsMw/Modules/Driving/Data/Sensory/SensoryMix.asset";
        public const string SoundtrackPath = "Assets/NfsMw/Modules/Driving/Data/AdaptiveMusic/AdaptiveMusicSoundtrack.asset";

        [MenuItem("NFS MW Remaster/Build Game Flow Boot Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Exit Play mode before building game flow.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(RockportStreamingMigration.EntryScenePath) == null)
                throw new System.InvalidOperationException("Build the Free Roam scene first.");
            var settings = AssetDatabase.LoadAssetAtPath<GameFlowSettings>(SettingsPath);
            if (settings == null) { settings = ScriptableObject.CreateInstance<GameFlowSettings>(); AssetDatabase.CreateAsset(settings, SettingsPath); }
            MostWantedShowroomPublisher.Publish();
            MostWantedFrontendContentPublisher.Publish();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Game Flow Application"); root.AddComponent<GameFlowRuntime>().Configure(settings);
            var camera = new GameObject("Boot Background Camera").AddComponent<Camera>(); MostWantedShowroomRendering.ConfigureBackground(camera);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.cullingMask = 0; camera.depth = -100;
            EditorSceneManager.SaveScene(scene, BootScenePath);
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(BootScenePath, true) };
            bool hasWorld = false;
            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            {
                if (existing.path == BootScenePath) continue;
                bool world = existing.path == settings.WorldScenePath; hasWorld |= world;
                scenes.Add(new EditorBuildSettingsScene(existing.path, world || existing.enabled));
            }
            if (!hasWorld) scenes.Add(new EditorBuildSettingsScene(settings.WorldScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray(); AssetDatabase.SaveAssets();
            Debug.Log("Game flow ready. Open Assets/NfsMw/Content/Frontend/UI/Scenes/Boot.unity and press Play. Existing world scenes were not rebuilt.");
        }
    }
}
#endif
