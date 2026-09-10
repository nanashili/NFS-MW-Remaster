using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;
using Unity.Profiling;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class NativeAudioWorldPlayTests
    {
#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator StockLibraryProfilesUseAndReleaseTheLiveVoicePool()
        {
            string root = Environment.GetEnvironmentVariable("BLACKBOX_LIBRARY_TEST_ROOT");
            if (string.IsNullOrEmpty(root)) Assert.Ignore("Optional stock library playback: set BLACKBOX_LIBRARY_TEST_ROOT.");
            var profiles = UnityEditor.AssetDatabase.FindAssets("t:VehicleSensoryProfile", new[] { root });
            Assert.That(profiles.Length, Is.GreaterThan(30));
            var go = new GameObject("Stock library playback test"); var world = go.AddComponent<SensoryAudioWorld>();
            try
            {
                yield return null;
                foreach (string guid in profiles)
                {
                    var profile = UnityEditor.AssetDatabase.LoadAssetAtPath<VehicleSensoryProfile>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                    if (!profile.HasCompleteAudio) continue; // The existing M3 also retains its separately authored engine profile.
                    Assert.True(profile.Validate(out string failure), profile.name + ": " + failure);
                    using (var runtime = new VehicleAudioRuntime(profile.mostWantedAudio, world, go.transform, SensoryCategory.Player, profile.exhaustPorts[0]))
                    {
                        var frame = new VehicleFeedbackFrame { Sequence = 1, EngineRunning = true,
                            EngineRpm = (profile.mostWantedAudio.idleRpm + profile.mostWantedAudio.maximumRpm) / 2,
                            EngineLoad = .7f, Throttle = .8f, Speed = 25, NitroIntensity = 1, Edges = FeedbackEdges.Upshift };
                        runtime.Sample(frame); runtime.Update(frame, .025f);
                        Assert.That(world.ActiveVoices, Is.EqualTo(1), profile.name + " should use one pooled spatial voice");
                        Assert.That(runtime.ActiveLayers, Is.GreaterThan(2), profile.name + " should retain multiple active bank layers");
                        yield return null;
                    }
                    Assert.AreEqual(0, world.ActiveVoices, profile.name);
                }
            }
            finally { UnityEngine.Object.Destroy(go); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator InstalledCompleteProfileUsesTheLiveVoicePool()
        {
            string path = Environment.GetEnvironmentVariable("BLACKBOX_COMPLETE_PROFILE");
            if (string.IsNullOrEmpty(path)) Assert.Ignore("Optional complete-profile playback: set BLACKBOX_COMPLETE_PROFILE to an exported project asset.");
            var profile = UnityEditor.AssetDatabase.LoadAssetAtPath<VehicleSensoryProfile>(path);
            Assert.NotNull(profile); Assert.True(profile.HasCompleteAudio); Assert.True(profile.Validate(out string failure), failure);
            var go = new GameObject("Complete Black Box playback test"); var world = go.AddComponent<SensoryAudioWorld>();
            try
            {
                yield return null;
                using (var runtime = new VehicleAudioRuntime(profile.mostWantedAudio, world, go.transform, SensoryCategory.Player, profile.exhaustPorts[0]))
                {
                    for (int i = 0; i < 12; i++)
                    {
                        var frame = new VehicleFeedbackFrame { Sequence = i + 1, EngineRunning = true, EngineRpm = 3000 + i * 100, EngineLoad = .7f,
                            Speed = 25, Throttle = .8f, NitroIntensity = i > 5 ? 1 : 0, Edges = i == 6 ? FeedbackEdges.Upshift : FeedbackEdges.None };
                        runtime.Sample(frame); runtime.Update(frame, .025f);
                        Assert.That(world.ActiveVoices, Is.EqualTo(1), profile.name + " should use one pooled spatial voice");
                        Assert.That(runtime.ActiveLayers, Is.GreaterThan(2), profile.name + " should retain multiple active bank layers");
                        yield return null;
                    }
                }
                Assert.AreEqual(0, world.ActiveVoices);
            }
            finally { UnityEngine.Object.Destroy(go); }
            yield return null;
        }
#endif
        private static EngineAudioCompiledSnapshot Snapshot()
        {
            var pcm = new float[4800];
            for (int i = 0; i < pcm.Length; i++) pcm[i] = Mathf.Sin(i * 0.02f) * 0.1f;
            Assert.True(EngineAudioCompiler.TryCompile(pcm, 0, pcm.Length, 48000, 1,
                new[] { new EngineRpmAnchor(1000, 0), new EngineRpmAnchor(7000, pcm.Length - 1) }, out var region));
            return new EngineAudioCompiledSnapshot(new[] { region }, 3);
        }

        [UnityTest]
        public IEnumerator NativeVoiceUsesPoolAndSurvivesReenable()
        {
            var go = new GameObject("AudioAnalysisWorld");
            var world = go.AddComponent<SensoryAudioWorld>();
            yield return null;
            var renderer = new EngineAudioRenderer(); renderer.SetTelemetry(Snapshot());
            var lease = world.PlayNative(renderer, SensoryCategory.OtherVehicle, null, Vector3.zero, 1, 100);
            Assert.IsTrue(lease.IsValid); Assert.AreEqual(1, world.ActiveVoices);
            world.Release(lease); Assert.AreEqual(0, world.ActiveVoices);
            go.SetActive(false); yield return null; go.SetActive(true); yield return null;
            lease = world.PlayNative(renderer, SensoryCategory.OtherVehicle, null, Vector3.zero, 1, 100);
            Assert.IsTrue(lease.IsValid); Assert.AreEqual(1, world.ActiveVoices);
            UnityEngine.Object.Destroy(go); yield return null;
        }

        [Test]
        public void FractionalPhaseAndGrainTraceAreStableAcrossCallbackSizes()
        {
            var pcm = new float[48000];
            for (int i = 0; i < pcm.Length; i++) pcm[i] = Mathf.Sin(i * 0.013f) * 0.25f;
            Assert.True(EngineAudioCompiler.TryCompile(pcm, 0, pcm.Length, 48000, 1,
                new[] { new EngineRpmAnchor(1000, 0), new EngineRpmAnchor(7000, pcm.Length - 1) }, out var region));
            var snapshot = new EngineAudioCompiledSnapshot(new[] { region }, 9);
            var split = new EngineAudioRenderer(256); split.SetTelemetry(snapshot);
            var whole = new EngineAudioRenderer(256); whole.SetTelemetry(snapshot);
            var expected = new float[1024]; var actual = new float[1024]; var block = new float[257];
            whole.Render(4200, 0.8f, 44100, expected.Length, 1, 1, expected);
            int offset = 0;
            while (offset < actual.Length)
            {
                int count = Mathf.Min(block.Length, actual.Length - offset);
                split.Render(4200, 0.8f, 44100, count, 1, 1, block);
                Array.Copy(block, 0, actual, offset, count); offset += count;
            }
            for (int i = 0; i < expected.Length; i++) Assert.That(actual[i], Is.EqualTo(expected[i]).Within(1e-5f), "sample " + i);
            bool sawNonZero = false, sawGrain = false;
            for (int i = 0; i < actual.Length; i++) sawNonZero |= Mathf.Abs(actual[i]) > 1e-6f;
            for (int i = 0; i < split.Trace.Count; i++)
            {
                var trace = split.Trace.Read(i);
                sawGrain |= trace.grainLength > 1 && trace.hopRatio > 0 && trace.sourceOffset >= 0;
            }
            Assert.IsTrue(sawNonZero, "scheduler produced only zero samples");
            Assert.IsTrue(sawGrain, "grain trace did not expose source and overlap metadata");
        }

        [Test]
        public void AuthoredChannelsArePreservedAndMismatchFailsClosed()
        {
            var pcm = new float[] { 1, 10, 2, 20, 3, 30, 4, 40 };
            Assert.True(EngineAudioCompiler.TryCompile(pcm, 0, 4, 48000, 2,
                new[] { new EngineRpmAnchor(1000, 0), new EngineRpmAnchor(7000, 3) }, out var region));
            var snapshot = new EngineAudioCompiledSnapshot(new[] { region }, 11);
            var trace = new EngineAudioTraceRing(32); var stereo = new float[32];
            Assert.AreEqual(1, EngineAudioKernel.Render(snapshot, 4000, 0.5f, 48000, 16, 1, 1, stereo, trace, 4, 2));
            Assert.That(stereo[3], Is.GreaterThan(stereo[2]));
            trace.Clear(); var mono = new float[16];
            Assert.AreEqual(0, EngineAudioKernel.Render(snapshot, 4000, 0.5f, 48000, 16, 1, 1, mono, trace, 4, 1));
            Assert.AreEqual(EngineAudioTraceReason.ChannelMismatch, trace.Read(0).reason);
        }

        [UnityTest]
        public IEnumerator MultipleVehiclesRenderWithoutAllocationsAfterWarmup()
        {
            var first = new EngineAudioRenderer(); first.SetTelemetry(Snapshot());
            var second = new EngineAudioRenderer(); second.SetTelemetry(Snapshot());
            var a = new float[257]; var b = new float[257];
            first.SetInputs(2400, 0.7f, true); second.SetInputs(5100, 0.4f, true);
            first.Render(2400, 0.7f, 48000, 257, 1, 1, a);
            second.Render(5100, 0.4f, 48000, 257, 1, 1, b);
            yield return null;
            using var positive = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 32, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            var control = new byte[4096]; GC.KeepAlive(control); positive.Stop();
            Assert.True(positive.Valid); Assert.Greater(positive.Count, 0, "Counter must detect a known allocation.");
            using var allocations = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 128, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            for (int i = 0; i < 8; i++)
            {
                first.Render(2400 + i, 0.7f, 48000, 257, 1, 1, a);
                second.Render(5100 - i, 0.4f, 48000, 257, 1, 1, b);
            }
            allocations.Stop();
            Assert.True(allocations.Valid); Assert.AreEqual(0, allocations.Count, "native render callback allocated after warmup");
        }
    }
}
