using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleAudioRuntimeContinuousTests
    {
        [Test]
        public void CompletedContinuousBankRestartsItsProgramAfterSilentInterval()
        {
            var clip = AudioClip.Create("continuous test", 32, 1, 1000, false);
            var bank = ScriptableObject.CreateInstance<AemsAudioBank>();
            try
            {
                var state = new byte[64];
                Buffer.BlockCopy(BitConverter.GetBytes(1), 0, state, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(1), 0, state, 28, 4);
                // Increment parameter 0 and finish. A finished evaluator can run again only after a real reset.
                bank.programs = new[] { new AemsProgram { interfaceName = "CUSTOM", initialState = state,
                    parameterCount = 1, parameterOffset = 4, players = new[] { 0 }, instructions = new[] {
                        new AemsInstruction(2, 0, 36), new AemsInstruction(1, 4), new AemsInstruction(2, 16, 4) } } };
                bank.recordings = new[] { new AemsRecording { sampleId = 1, clip = clip } };
                var sound = new VehicleAudioSound { id = "horn", bank = bank, interfaceName = "CUSTOM", parameters = new[] { 0 }, trigger = VehicleAudioTrigger.Horn };
                using (var runtime = new VehicleAudioRuntime(null, null, null, SensoryCategory.OtherVehicle, Vector3.zero, sounds: new[] { sound }))
                {
                    var voices = (System.Collections.IList)typeof(VehicleAudioRuntime).GetField("voices", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runtime);
                    var voice = voices[0];
                    var graph = (AemsEvaluator)voice.GetType().GetField("Graph").GetValue(voice);
                    for (int i = 0; i < 10; i++) runtime.Update(default, .025f);
                    Assert.AreEqual(0, graph.GetParameter(0)); Assert.False(graph.Finished);
                    runtime.SetHorn(true); runtime.Update(default, .025f);
                    Assert.AreEqual(1, graph.GetParameter(0)); Assert.True(graph.Finished);
                    graph.SetParameter(0, 9);
                    runtime.SetHorn(false);
                    for (int i = 0; i < 10; i++) runtime.Update(default, .025f);
                    Assert.AreEqual(9, graph.GetParameter(0), "inactive graph must stay stopped");
                    runtime.SetHorn(true); runtime.Update(default, .025f);
                    Assert.AreEqual(1, graph.GetParameter(0), "activation must restore initial state and execute the finished graph again");
                    Assert.True(graph.Finished); Assert.AreEqual(1, runtime.ActiveLayers);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(bank); UnityEngine.Object.DestroyImmediate(clip); }
        }
    }
}
