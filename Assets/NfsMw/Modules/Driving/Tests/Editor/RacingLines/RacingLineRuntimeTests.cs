using System;
using System.Collections;
using NfsMwRemaster.Driving.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class RacingLineRuntimeTests
    {
        private const string RestoreKey = "RacingLineRuntimeTests.Restore";
        [Serializable] private sealed class RestoreState
        { public bool enabled; public EnterPlayModeOptions options; public SavedScene[] scenes; }
        [Serializable] private sealed class SavedScene { public string path; public bool loaded, active; }

        [UnityTest] public IEnumerator PublishedLineRunsAndInvalidatesWithDomainReload() => Run(false);
        [UnityTest] public IEnumerator PublishedLineRunsAndInvalidatesWithoutDomainReload() => Run(true);

        private static IEnumerator Run(bool disableDomainReload)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!Application.isBatchMode && (scene.isDirty || string.IsNullOrEmpty(scene.path) && scene.rootCount > 0))
                    Assert.Ignore("Save/close edited scenes before this scene-changing integration test, or use a disposable project copy.");
            }
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(RacingLineStudioDemo.ScenePath), Is.Not.Null,
                "Create and validate the Studio example before running its Play Mode integration tests.");
            var setup = EditorSceneManager.GetSceneManagerSetup(); var saved = new SavedScene[setup.Length];
            for (int i = 0; i < setup.Length; i++) saved[i] = new SavedScene { path = setup[i].path, loaded = setup[i].isLoaded, active = setup[i].isActive };
            SessionState.SetString(RestoreKey, JsonUtility.ToJson(new RestoreState { enabled = EditorSettings.enterPlayModeOptionsEnabled,
                options = EditorSettings.enterPlayModeOptions, scenes = saved }));
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = disableDomainReload ? EnterPlayModeOptions.DisableDomainReload : EnterPlayModeOptions.None;
            EditorSceneManager.OpenScene(RacingLineStudioDemo.ScenePath);
            yield return new EnterPlayMode(!disableDomainReload);
            var input = UnityEngine.Object.FindFirstObjectByType<RacingLineInput>();
            Assert.That(input, Is.Not.Null); yield return null;
            Assert.That(input.Failure, Is.Null, input.Failure);
            float deadline = Time.realtimeSinceStartup + 20;
            while (input.Station < 30 && Time.realtimeSinceStartup < deadline)
            { Assert.That(input.Failure, Is.Null, input.Failure); yield return new WaitForFixedUpdate(); }
            Assert.That(input.Station, Is.GreaterThan(30), "Normal FixedUpdate must advance the real car along the published trajectory.");
            var vehicle = input.GetComponent<VehicleController>(); var original = vehicle.Tuning;
            var changed = original.CreateRuntimeCopy(); changed.tires.lateralGrip += 0.1f;
            vehicle.Tuning = changed;
            deadline = Time.realtimeSinceStartup + 2;
            while (input.Failure == null && Time.realtimeSinceStartup < deadline) yield return new WaitForFixedUpdate();
            Assert.That(input.Failure, Does.Contain("STALE"));
            Assert.That(input.Current.Throttle, Is.Zero); Assert.That(input.Current.Brake > 0 || input.Current.Handbrake, Is.True);
            vehicle.Tuning = original; UnityEngine.Object.Destroy(changed);
            yield return new ExitPlayMode();
        }
        [UnityTearDown] public IEnumerator Restore()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            string json = SessionState.GetString(RestoreKey, ""); SessionState.EraseString(RestoreKey);
            if (string.IsNullOrEmpty(json)) yield break;
            var state = JsonUtility.FromJson<RestoreState>(json);
            EditorSettings.enterPlayModeOptionsEnabled = state.enabled; EditorSettings.enterPlayModeOptions = state.options;
            var setup = new SceneSetup[state.scenes.Length]; bool hasSaved = false;
            for (int i = 0; i < setup.Length; i++)
            { setup[i] = new SceneSetup { path = state.scenes[i].path, isLoaded = state.scenes[i].loaded, isActive = state.scenes[i].active }; hasSaved |= !string.IsNullOrEmpty(setup[i].path); }
            if (!hasSaved) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            else EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }
}
