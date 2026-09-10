#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>Builds the isolated play-mode fixture used to verify transitions and stingers.</summary>
    public static class AdaptiveMusicDemoBuilder
    {
        [MenuItem("NFS MW Remaster/Sensory/Adaptive Music/Build Test Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Build the adaptive music fixture in Edit mode.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var profile = AdaptiveMusicEditorModel.BuildSyntheticFixture();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientLight = new Color(0.07f, 0.09f, 0.13f);
            CreateCube("Fixture floor", new Vector3(0, -0.15f, 0), new Vector3(24, 0.3f, 24));
            CreateCube("Fixture marker", new Vector3(0, 0.5f, 4), new Vector3(0.5f, 1, 0.5f));

            var cameraObject = new GameObject("Adaptive Music Camera + AudioListener");
            cameraObject.transform.position = new Vector3(0, 2.3f, -10);
            cameraObject.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            var camera = cameraObject.AddComponent<Camera>(); NfsMwRemaster.Driving.Editor.Rendering.HdrpSceneDefaults.Camera(camera);
            camera.fieldOfView = 60; camera.nearClipPlane = 0.1f; camera.farClipPlane = 100;
            cameraObject.AddComponent<AudioListener>();

            var lightObject = new GameObject("Fixture key light");
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; Rendering.HdrpSceneDefaults.Sun(light);
            light.transform.rotation = Quaternion.Euler(45, -25, 0);

            var systems = new GameObject("Adaptive Music Runtime");
            var audio = systems.AddComponent<SensoryAudioWorld>();
            audio.Configure(cameraObject.transform, null, null);
            var director = systems.AddComponent<AdaptiveMusic>();
            director.Configure(audio, profile);
            systems.AddComponent<AdaptiveMusicTestHarness>();

            Selection.activeGameObject = systems;
            if (!EditorSceneManager.SaveScene(scene, AdaptiveMusicEditorModel.FixtureScenePath))
                throw new InvalidOperationException("Could not save adaptive music fixture scene.");
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("Built " + AdaptiveMusicEditorModel.FixtureScenePath + ". Play it and use 1–7, F, P and 0 to drive the music director.");
        }

        private static void CreateCube(string name, Vector3 position, Vector3 scale)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name; cube.transform.position = position; cube.transform.localScale = scale;
        }
    }
}
#endif
