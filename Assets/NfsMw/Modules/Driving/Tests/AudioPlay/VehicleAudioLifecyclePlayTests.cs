using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleAudioLifecyclePlayTests
    {
        [UnityTest]
        public IEnumerator ControllerAudioCreatesSamplerAndSurvivesDisableReenable()
        {
            var root = new GameObject("vehicle");
            root.SetActive(false);
            root.AddComponent<VehicleController>();
            var audio = root.AddComponent<VehicleAudio>();
            root.SetActive(true);
            yield return null;
            Assert.That(audio.Source, Is.TypeOf<VehicleFeedbackSampler>());
            Assert.AreEqual(1, root.GetComponents<VehicleAudio>().Length);
            root.SetActive(false); yield return null;
            root.SetActive(true); yield return null;
            Assert.That(audio.Source, Is.TypeOf<VehicleFeedbackSampler>());
            Object.Destroy(root);
        }
    }
}
