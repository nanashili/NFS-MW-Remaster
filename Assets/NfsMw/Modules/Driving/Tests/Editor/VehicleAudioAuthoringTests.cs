using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleAudioAuthoringTests
    {
        [Test]
        public void InspectorEditsKeepTheMixObjectUsedByTheRuntime()
        {
            var go = new GameObject("audio authoring test");
            try
            {
                var audio = go.AddComponent<VehicleAudio>(); var mix = audio.Mix;
                using (var serialized = new SerializedObject(audio))
                {
                    serialized.FindProperty("mix").FindPropertyRelative("gain").floatValue = .35f;
                    serialized.FindProperty("mix").FindPropertyRelative("channels").GetArrayElementAtIndex(0).FindPropertyRelative("tempo").floatValue = .5f;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                Assert.AreSame(mix, audio.Mix, "live runtime must see inspector edits without recreating voices");
                Assert.AreEqual(.35f, mix.gain); Assert.AreEqual(.5f, mix.Find(VehicleAudioChannel.Acceleration).tempo);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ExistingWeatherSceneResolvesTheRenamedAudioComponentAndKeepsProfiles()
        {
            string path = AssetDatabase.GUIDToAssetPath("93bd998b4329c409a9c76ccd0ba11004");
            Assert.AreEqual(typeof(VehicleAudio), AssetDatabase.LoadAssetAtPath<MonoScript>(path).GetClass());
            var scene = EditorSceneManager.OpenScene("Assets/NfsMw/Scenes/Showcase/WeatherDemo.unity", OpenSceneMode.Additive);
            try
            {
                var audio = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<VehicleAudio>(true)).ToArray();
                Assert.That(audio.Length, Is.GreaterThanOrEqualTo(2));
                foreach (var component in audio) Assert.NotNull(component.Profile, component.name + " lost its audio profile");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
