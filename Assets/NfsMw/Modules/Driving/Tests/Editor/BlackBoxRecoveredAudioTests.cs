using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NfsMwRemaster.Driving.AudioAnalysis;
using NfsMwRemaster.Driving.Editor.AudioAnalysis;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class BlackBoxRecoveredAudioTests
    {
        [Test] public void LegacyProfileKeepsCompleteAudioDisabledAfterSerialization()
        {
            var profile = ScriptableObject.CreateInstance<VehicleSensoryProfile>();
            var restored = ScriptableObject.CreateInstance<VehicleSensoryProfile>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(profile), restored);
                Assert.False(restored.HasCompleteAudio); Assert.True(restored.Validate(out _));
                restored.mostWantedAudio = new MostWantedVehicleAudio { schema = 1 }; Assert.False(restored.Validate(out _));
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); UnityEngine.Object.DestroyImmediate(restored); }
        }
        [Test] public void PcmSustainDoesNotReplayTheAttackAndCompletesOneShots()
        {
            var recording = new AemsPcmRenderer.Recording { SampleId = 1, SampleRate = 1000, Channels = 1, Pcm = new[] { .9f, .1f, .2f, .3f }, LoopStart = 1, LoopEnd = 3 };
            var renderer = new AemsPcmRenderer(new[] { recording }, 1);
            var players = new[] { new AemsEvaluator.Player { Active = true, Control = 1, Generation = 1, Sample = 1, Gain = 1, Pitch = 1 } };
            renderer.Publish(players); var output = new float[1000]; renderer.RenderInto(output, 1, 1000);
            Assert.That(output.Skip(600).Max(), Is.LessThan(.21f), "loop replayed its .9 attack or .3 tail");
            renderer.Publish(players); Assert.False(players[0].Completed);
            recording.LoopEnd = 0; var oneShot = new AemsPcmRenderer(new[] { recording }, 1);
            oneShot.Publish(players); oneShot.RenderInto(output, 1, 1000); oneShot.Publish(players);
            Assert.True(players[0].Completed); Assert.That(output.Skip(4).Max(), Is.EqualTo(0));
        }
        [Test] public void ClipBackedGinCompilesWithoutItsTransientPcmCache()
        {
            var clip = AudioClip.Create("reloaded GIN", 64, 1, 48000, false);
            try
            {
                clip.SetData(Enumerable.Range(0, 64).Select(i => .2f * Mathf.Sin(i * .2f)).ToArray(), 0);
                Assert.True(EngineAudioCompiler.TryCompile(clip, 0, 64, new[] { new EngineRpmAnchor(1000, 0), new EngineRpmAnchor(2000, 63) }, out var region));
                region.compiledPcm = null;
                Assert.True(region.IsValid); Assert.True(EngineAudioCompiler.TryBuildSnapshot(new[] { region }, 1, out var snapshot));
                var renderer = new EngineAudioRenderer(); renderer.SetTelemetry(snapshot); var output = new float[256];
                Assert.AreEqual(1, renderer.Render(1500, .5f, 48000, output.Length, 1, 1, output));
                Assert.True(output.Any(s => Math.Abs(s) > .001));
            }
            finally { UnityEngine.Object.DestroyImmediate(clip); }
        }
        [Test] public void DecoderIdentityUsesTheEmbeddedFormatAndMalformedProgramsFailClosed()
        {
            Assert.AreEqual(1, BlackBoxCompleteAttachment.SampleId("bnk:1"));
            Assert.AreEqual(1, BlackBoxCompleteAttachment.SampleId("s10a:0"));
            Assert.Throws<InvalidDataException>(() => BlackBoxCompleteAttachment.SampleId("bnk:0"));
            Assert.Throws<InvalidDataException>(() => BlackBoxAemsCompiler.Compile(new byte[128]));
            var graph = new AemsEvaluator(new AemsProgram { initialState = new byte[32], tables = new byte[4], players = Array.Empty<int>(),
                instructions = new[] { new AemsInstruction(0, 40, 0) }, outputs = Array.Empty<AemsClassOutput>() });
            Assert.Throws<InvalidDataException>(() => graph.Step(25));
        }
        [Test] public void SourcePathsRejectTraversalAndAmbiguousCase()
        {
            string folder = Path.Combine(Path.GetTempPath(), "blackbox-path-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            try { Assert.Throws<InvalidDataException>(() => MostWantedAudioSetup.ResolveFile(folder, "../anything")); Assert.IsNull(MostWantedAudioSetup.ResolveFile(folder, "missing.abk", false)); }
            finally { Directory.Delete(folder); }
        }
        private static MostWantedAudioSetup Installed()
        {
            string pack = Environment.GetEnvironmentVariable("BLACKBOX_PACK"), game = Environment.GetEnvironmentVariable("BLACKBOX_GAME");
            if (string.IsNullOrEmpty(pack) || string.IsNullOrEmpty(game)) Assert.Ignore("Optional original-file integration: set BLACKBOX_PACK and BLACKBOX_GAME.");
            return MostWantedAudioSetup.Prepare(pack, game);
        }
        [Test] public void InstalledBmwResolvesInheritedBanksAndSeparateShiftAndBrakeSamples()
        {
            var setup = Installed(); Assert.AreEqual("bmwm3gtre46", setup.Vehicle); Assert.AreEqual("tvr_cerb", setup.Parent);
            Assert.AreEqual(1100, setup.IdleRpm); Assert.AreEqual(7900, setup.MaximumRpm); Assert.AreEqual("1.5", setup.Overrides["GINSUMix_S_RPM"]);
            Assert.AreEqual("GIN_TVR_Cerbera_DCL.gin", Path.GetFileName(setup.Sources.Single(s => s.Role == "deceleration").Path));
            var shifts = setup.Sources.Single(s => s.Role == "shifts"); Assert.AreEqual("GEAR_MED_Lev3.abk", Path.GetFileName(shifts.Path));
            var program = shifts.Programs.Single(p => p.interfaceName == "FX_SHIFTING_01");
            for (int id = 0; id < 3; id++)
            {
                var graph = new AemsEvaluator(program); graph.SetParameter(0, id); graph.SetParameter(1, 32767); graph.SetParameter(2, 4096);
                graph.Step(25); Assert.That(graph.Players.Single().Sample, Is.EqualTo(id + 1)); Assert.True(graph.Players.Single().Active);
            }
            foreach (var source in setup.Sources) foreach (var p in source.Programs) Assert.DoesNotThrow(() => new AemsEvaluator(p).Step(25), source.Role + "/" + p.interfaceName);
            setup.VerifySources();
        }
        [Test] public void InstalledCompleteProfileExportsBindsRendersAndCanBeUndone()
        {
            Installed();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var open = SceneManager.GetSceneAt(i);
                if (!Application.isBatchMode && (open.isDirty || string.IsNullOrEmpty(open.path) && open.rootCount > 0))
                    Assert.Ignore("Save edited scenes first, or run this integration test in a disposable project copy.");
            }
            var previous = EditorSceneManager.GetSceneManagerSetup();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string root = "Assets/BlackBoxCompleteTest-" + Guid.NewGuid().ToString("N"), generated = null;
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(root));
            try
            {
                var vehicle = new GameObject("BMW integration vehicle"); vehicle.SetActive(false);
                var source = vehicle.AddComponent<VehicleController>(); var target = vehicle.AddComponent<VehicleAudio>();
                var world = new GameObject("BMW test audio world").AddComponent<SensoryAudioWorld>();
                var template = ScriptableObject.CreateInstance<VehicleSensoryProfile>(); template.exhaustPorts = new[] { new Vector3(0, .4f, -2) };
                AssetDatabase.CreateAsset(template, root + "/Original.asset"); target.Configure(source, world, template, true);
                var plan = BlackBoxCompleteAttachment.Prepare(Environment.GetEnvironmentVariable("BLACKBOX_PACK"), Environment.GetEnvironmentVariable("BLACKBOX_GAME"), target);
                var profile = plan.Apply(); generated = Path.GetDirectoryName(AssetDatabase.GetAssetPath(profile)).Replace('\\', '/');
                Assert.AreSame(profile, target.Profile); Assert.AreNotSame(template, profile); Assert.False(template.HasCompleteAudio);
                CollectionAssert.AreEqual(template.exhaustPorts, profile.exhaustPorts); Assert.AreEqual(0, profile.engineLayers.Length);
                Assert.AreEqual(11, plan.Setup.Sources.Count);
                Assert.AreEqual(5, profile.mostWantedAudio.surfaces.Single(s => s.surface == SensorySurface.AsphaltDry).roadLoop);
                Assert.AreSame(profile, BlackBoxCompleteAttachment.Prepare(Environment.GetEnvironmentVariable("BLACKBOX_PACK"), Environment.GetEnvironmentVariable("BLACKBOX_GAME"), target).Apply());
                using (var runtime = new VehicleAudioRuntime(profile.mostWantedAudio, world, vehicle.transform, SensoryCategory.Player, profile.exhaustPorts[0]))
                {
                    var peaks = new Dictionary<string, float>(); var bankOutput = new float[1200];
                    var voices = (IList)typeof(VehicleAudioRuntime).GetField("voices", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runtime);
                    for (int i = 0; i < 120; i++)
                    {
                        var frame = new VehicleFeedbackFrame { Sequence = i + 1, EngineRunning = true, EngineRpm = 1100 + i * 50, EngineLoad = i < 60 ? .8f : .1f,
                            Speed = i * .4f, Throttle = i < 60 ? .9f : 0, Brake = i > 90 ? 1 : 0, BrakeLock = i > 100 ? .7f : 0, FrontSlip = .3f, RearSlip = .4f,
                            NitroIntensity = i > 30 && i < 60 ? 1 : 0, Gear = 3, Shifting = i == 45, Edges = i == 45 ? FeedbackEdges.Upshift : FeedbackEdges.None };
                        frame.WheelCount = 4;
                        for (int wheel = 0; wheel < 4; wheel++) frame.SetWheel(wheel, new WheelFeedback { Grounded = true, Surface = i < 70 ? SensorySurface.AsphaltDry : SensorySurface.Gravel, Load = 1 });
                        runtime.Sample(frame); runtime.Update(frame, .025f);
                        foreach (var voice in voices)
                        {
                            var type = voice.GetType(); var program = (AemsProgram)type.GetField("Program").GetValue(voice);
                            if (program == null) continue; // Acceleration/deceleration GIN layers use EngineAudioRenderer and are covered separately.
                            var pcm = (AemsPcmRenderer)type.GetField("Pcm").GetValue(voice);
                            Assert.NotNull(pcm, program.interfaceName + " did not create a PCM renderer");
                            pcm.RenderInto(bankOutput, 1, 48000);
                            Assert.True(bankOutput.All(SensoryMath.IsFinite), program.interfaceName + " produced non-finite PCM");
                            peaks.TryGetValue(program.interfaceName, out float peak);
                            peaks[program.interfaceName] = Math.Max(peak, bankOutput.Max(Math.Abs));
                        }
                    }
                    foreach (string name in new[] { "CAR", "CAR_TRANNY", "FX_SHIFTING_01", "FX_SKID", "FX_ROADNOISE", "FX_NITROUS" })
                        Assert.That(peaks[name], Is.GreaterThan(.00001f), name + " remained silent through its driving scenario");
                    TestContext.WriteLine(string.Join("\n", peaks.Select(pair => pair.Key + " peak=" + pair.Value)));
                }
                Assert.AreEqual(0, world.ActiveVoices);
                Assert.True(EngineAudioCompiler.TryBuildSnapshot(new[] { profile.mostWantedAudio.acceleration, profile.mostWantedAudio.deceleration }, 1, out var snapshot));
                var renderer = new EngineAudioRenderer(); renderer.SetTelemetry(snapshot); var output = new float[4096];
                Assert.AreEqual(2, renderer.Render(4000, .5f, 48000, output.Length, 1, 1, output)); Assert.True(output.Any(s => Math.Abs(s) > .001));
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Assert.AreSame(template, target.Profile);
                Undo.PerformRedo(); Assert.AreSame(profile, target.Profile);
                using var serialized = new SerializedObject(target);
                Assert.AreSame(world, serialized.FindProperty("world").objectReferenceValue); Assert.AreSame(source, serialized.FindProperty("sourceComponent").objectReferenceValue);
                Assert.True(EditorSceneManager.SaveScene(vehicle.scene, root + "/Vehicle.unity"));
                EditorSceneManager.OpenScene(root + "/Vehicle.unity");
                target = UnityEngine.Object.FindFirstObjectByType<VehicleAudio>(FindObjectsInactive.Include);
                Assert.AreSame(profile, target.Profile); Assert.True(profile.Validate(out string failure), failure);
                using var reopened = new SerializedObject(target);
                world = (SensoryAudioWorld)reopened.FindProperty("world").objectReferenceValue;
                Assert.NotNull(world); Assert.NotNull(reopened.FindProperty("sourceComponent").objectReferenceValue);
                using (var runtime = new VehicleAudioRuntime(profile.mostWantedAudio, world, target.transform, SensoryCategory.Player, profile.exhaustPorts[0]))
                {
                    var frame = new VehicleFeedbackFrame { EngineRunning = true, EngineRpm = 4000, EngineLoad = .5f, Speed = 20 };
                    runtime.Sample(frame); runtime.Update(frame, .025f);
                    Assert.True(EngineAudioCompiler.TryBuildSnapshot(new[] { profile.mostWantedAudio.acceleration, profile.mostWantedAudio.deceleration }, 1, out _));
                }
            }
            finally
            {
                Undo.ClearAll();
                if (previous.Any(s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(previous);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (generated != null) AssetDatabase.DeleteAsset(generated); AssetDatabase.DeleteAsset(root);
            }
        }
    }
}
