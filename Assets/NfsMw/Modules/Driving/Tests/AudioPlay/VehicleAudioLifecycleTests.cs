using System;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleAudioLifecycleTests
    {
        private static void SetSounds(VehicleAudio audio, VehicleAudioSound[] sounds)
        {
            typeof(VehicleAudio).GetField("sounds", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(audio, sounds);
        }

        [Test]
        public void ManualSoundStartsWithoutAControllerOrPriorTelemetry()
        {
            var root = new GameObject("manual vehicle sound"); var worldRoot = new GameObject("manual audio world");
            var clip = AudioClip.Create("manual sound", 4800, 1, 48000, false);
            try
            {
                root.SetActive(false);
                var world = worldRoot.AddComponent<SensoryAudioWorld>(); var audio = root.AddComponent<VehicleAudio>();
                SetSounds(audio, new[] { new VehicleAudioSound { id = "beep", clip = clip } });
                audio.Configure(null, world, null, true); root.SetActive(true);
                Assert.True(audio.Play("beep")); audio.Advance(.025f);
                Assert.AreEqual(1, audio.ActiveLayers); Assert.AreEqual(1, world.ActiveVoices); Assert.AreEqual(1, audio.ControlTicks);
                audio.enabled = false; Assert.AreEqual(0, world.ActiveVoices);
                audio.SetHorn(true); Assert.AreEqual(0, audio.ActiveLayers, "disabled control calls must not acquire content");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(worldRoot); UnityEngine.Object.DestroyImmediate(clip); }
        }

        [Test]
        public void VehicleAudioUsesControllerSamplerWithoutAddingFeedbackComponent()
        {
            var root = new GameObject("vehicle");
            try
            {
                root.SetActive(false);
                root.AddComponent<VehicleController>();
                var audio = root.AddComponent<VehicleAudio>();
                root.SetActive(true);
                Assert.That(audio.Source, Is.TypeOf<VehicleFeedbackSampler>());
                Assert.AreEqual(1, root.GetComponents<VehicleAudio>().Length);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void ExplicitControllerOwnsSamplingEvenWhenAnAdapterIsPresent()
        {
            var vehicle = new GameObject("vehicle"); var audioRoot = new GameObject("audio");
            try
            {
                vehicle.SetActive(false); audioRoot.SetActive(false);
                var controller = vehicle.AddComponent<VehicleController>();
                var adapter = audioRoot.AddComponent<TestFeedbackSource>();
                var audio = audioRoot.AddComponent<VehicleAudio>();
                audio.Configure(controller, null, null, true);
                audioRoot.SetActive(true);
                Assert.That(audio.Source, Is.TypeOf<VehicleFeedbackSampler>());
                Assert.AreSame(controller, ((VehicleFeedbackSampler)audio.Source).Vehicle);
                Assert.AreEqual(0, adapter.SampleSubscribers);
                var first = audio.Source;
                audio.Rebuild();
                Assert.AreNotSame(first, audio.Source);
                Assert.AreSame(controller, ((VehicleFeedbackSampler)audio.Source).Vehicle);
                audio.enabled = false;
                Assert.IsNull(audio.Source);
                Assert.AreEqual(0, adapter.SampleSubscribers);
            }
            finally { UnityEngine.Object.DestroyImmediate(audioRoot); UnityEngine.Object.DestroyImmediate(vehicle); }
        }

        [Test]
        public void SuppliedSourceHasOneSubscriptionAcrossConfigureEnableAndReset()
        {
            var root = new GameObject("vehicle");
            try
            {
                root.SetActive(false);
                var source = root.AddComponent<TestFeedbackSource>();
                var audio = root.AddComponent<VehicleAudio>();
                audio.Configure(source, null, null, true);
                root.SetActive(true);
                audio.Configure(source, null, null, true);
                root.SetActive(false); root.SetActive(true);
                Assert.AreEqual(3, source.SampleAdds);
                Assert.AreEqual(2, source.SampleRemoves);
                Assert.AreEqual(1, source.SampleSubscribers);
                Assert.AreEqual(3, source.ImpactAdds);
                Assert.AreEqual(2, source.ImpactRemoves);
                Assert.AreEqual(1, source.ImpactSubscribers);
                source.Publish(new VehicleFeedbackFrame { Sequence = 1, Epoch = 1, Rotation = Quaternion.identity });
                Assert.AreEqual(1, audio.Frame.Sequence);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void CustomPcmUsesOneLayerAndRebuildReleasesFailedRuntime()
        {
            var root = new GameObject("vehicle");
            var clip = AudioClip.Create("custom", 64, 1, 48000, false);
            var worldRoot = new GameObject("audio world");
            var world = worldRoot.AddComponent<SensoryAudioWorld>();
            var valid = new[] { new VehicleAudioSound { id = "custom", channel = VehicleAudioChannel.Custom, trigger = VehicleAudioTrigger.Speed, clip = clip, loop = true } };
            try
            {
                root.SetActive(false);
                var audio = root.AddComponent<VehicleAudio>();
                audio.ManualInput = true;
                SetSounds(audio, valid);
                audio.Configure(null, world, null, true);
                root.SetActive(true);
                audio.SubmitFrame(new VehicleFeedbackFrame { Sequence = 1, Epoch = 1, Rotation = Quaternion.identity });
                audio.Advance(.025f);
                Assert.AreEqual(1, audio.ActiveLayers);
                Assert.AreEqual(1, world.ActiveVoices);
                root.SetActive(false);
                Assert.AreEqual(0, world.ActiveVoices);
                root.SetActive(true);
                SetSounds(audio, new[] { new VehicleAudioSound { id = "broken", channel = VehicleAudioChannel.Custom, trigger = VehicleAudioTrigger.Speed } });
                audio.Rebuild();
                LogAssert.Expect(LogType.Error, new Regex("Vehicle audio stopped: broken: assign either a decoded bank or one PCM clip\\."));
                audio.SubmitFrame(new VehicleFeedbackFrame { Sequence = 2, Epoch = 1, Rotation = Quaternion.identity });
                Assert.AreEqual(0, audio.ActiveLayers);
                SetSounds(audio, valid);
                audio.Rebuild();
                audio.SubmitFrame(new VehicleFeedbackFrame { Sequence = 3, Epoch = 1, Rotation = Quaternion.identity });
                audio.Advance(.025f);
                Assert.AreEqual(1, audio.ActiveLayers);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(worldRoot); UnityEngine.Object.DestroyImmediate(clip); }
        }

        [Test]
        public void InvalidTelemetryAndImpactsFailClosedBeforePlayback()
        {
            var root = new GameObject("vehicle");
            var impactRoot = new GameObject("impact vehicle");
            var clip = AudioClip.Create("custom", 8, 1, 48000, false);
            try
            {
                root.SetActive(false);
                var audio = root.AddComponent<VehicleAudio>(); audio.ManualInput = true;
                SetSounds(audio, new[] { new VehicleAudioSound { id = "custom", clip = clip, loop = true } });
                root.SetActive(true);
                LogAssert.Expect(LogType.Error, new Regex("Vehicle audio stopped: Vehicle audio received invalid telemetry\\."));
                audio.SubmitFrame(new VehicleFeedbackFrame { WheelCount = 1, Wheel0 = new WheelFeedback { Point = new Vector3(float.NaN, 0, 0) } });
                Assert.AreEqual(0, audio.ActiveLayers);

                impactRoot.SetActive(false);
                var impactAudio = impactRoot.AddComponent<VehicleAudio>(); impactAudio.ManualInput = true;
                SetSounds(impactAudio, new[] { new VehicleAudioSound { id = "custom", clip = clip, loop = true } });
                impactRoot.SetActive(true);
                LogAssert.Expect(LogType.Error, new Regex("Vehicle audio stopped: Vehicle audio received invalid impact telemetry\\."));
                impactAudio.SubmitImpact(new FeedbackImpact { Point = new Vector3(float.NaN, 0, 0) });
                Assert.AreEqual(0, impactAudio.ActiveLayers);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(impactRoot); UnityEngine.Object.DestroyImmediate(clip); }
        }
    }

    public sealed class TestFeedbackSource : MonoBehaviour, IVehicleFeedbackSource
    {
        private Action<VehicleFeedbackFrame> sampled;
        private Action<FeedbackImpact> impact;
        public int SampleAdds, SampleRemoves, ImpactAdds, ImpactRemoves;
        public int SampleSubscribers => sampled == null ? 0 : sampled.GetInvocationList().Length;
        public int ImpactSubscribers => impact == null ? 0 : impact.GetInvocationList().Length;
        public VehicleFeedbackFrame Frame { get; private set; }
        public event Action<VehicleFeedbackFrame> Sampled { add { SampleAdds++; sampled += value; } remove { SampleRemoves++; sampled -= value; } }
        public event Action<FeedbackImpact> Impact { add { ImpactAdds++; impact += value; } remove { ImpactRemoves++; impact -= value; } }
        public void Publish(VehicleFeedbackFrame frame) { Frame = frame; sampled?.Invoke(frame); }
    }
}
