using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NfsMwRemaster.Driving.AudioAnalysis;
using NfsMwRemaster.Driving.Editor;
using NfsMwRemaster.Driving.Editor.AudioAnalysis;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Unity.Profiling;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class BlackBoxVehicleLibraryTests
    {
        private static string InstalledGame()
        {
            string game = Environment.GetEnvironmentVariable("BLACKBOX_GAME");
            if (string.IsNullOrEmpty(game)) Assert.Ignore("Optional stock database integration: set BLACKBOX_GAME.");
            return game;
        }
        private static string ExportedRoot()
        {
            string root = Environment.GetEnvironmentVariable("BLACKBOX_LIBRARY_TEST_ROOT");
            if (string.IsNullOrEmpty(root)) Assert.Ignore("Optional exported library integration: set BLACKBOX_LIBRARY_TEST_ROOT.");
            return root;
        }
        [Test] public void StockContextPreservesVehicleAssignmentsAndStableInputSets()
        {
            string game = InstalledGame(); var catalog = MostWantedVehicleCatalog.Scan(game);
            Assert.That(catalog.ConcreteCount, Is.GreaterThan(30));
            Assert.True(catalog.Concrete.Any(e => e.Kind == MostWantedVehicleCatalog.VehicleKind.Police));
            Assert.True(catalog.Concrete.Any(e => e.Kind == MostWantedVehicleCatalog.VehicleKind.Traffic));
            var context = MostWantedAudioSetup.CreateStockContext(game);
            var bmw = MostWantedAudioSetup.PrepareStock(game, "bmwm3gtre46", context);
            Assert.That(bmw.MasterGain, Is.EqualTo(23500f / 32767).Within(.00001f), "Read the stock UInt16 Master_Vol instead of substituting full volume.");
            var career = catalog.Concrete.Single(e => e.Key == "m3gtre46careerstart");
            Assert.AreEqual("BMWM3GTRE46", career.Model); Assert.True(career.IsAlias);
            Assert.False(catalog.Concrete.Any(e => e.Key == "copheli" || e.Key == "default"));
            context.ReleaseVehicleSources();
            var cobalt = MostWantedAudioSetup.PrepareStock(game, "cobaltss", context);
            Assert.AreNotEqual(bmw.Sources.Single(s => s.Role == "engine").Hash, cobalt.Sources.Single(s => s.Role == "engine").Hash);
            context.ReleaseVehicleSources();
            var repeated = MostWantedAudioSetup.PrepareStock(game, "bmwm3gtre46", context);
            CollectionAssert.AreEquivalent(bmw.Inputs, repeated.Inputs);
            CollectionAssert.AreEquivalent(bmw.Sources.Select(s => s.Role), repeated.Sources.Select(s => s.Role));
            Assert.AreEqual(1, repeated.Sources.Count(s => s.Role == "acceleration"));
            Assert.AreEqual(1, repeated.Sources.Count(s => s.Role == "deceleration"));
            Assert.AreEqual(13, repeated.Surfaces.Count);
            Assert.IsEmpty(bmw.Unsupported); Assert.IsEmpty(cobalt.Unsupported); Assert.IsEmpty(repeated.Unsupported);
        }
        [Test] public void ExportedLibraryKeepsSoundWithEachCarAndRendersEveryProfile()
        {
            string root = ExportedRoot(); var catalog = MostWantedVehicleCatalog.Scan(InstalledGame());
            var report = JsonUtility.FromJson<BlackBoxVehicleLibrary.Report>(File.ReadAllText(root + "/ImportReport.json"));
            Assert.False(report.cancelled); Assert.AreEqual(catalog.ConcreteCount, report.Ready);
            var banks = new Dictionary<string, AemsAudioBank>();
            using (var positive = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 32, ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                GC.KeepAlive(new byte[4096]); positive.Stop();
                Assert.True(positive.Valid); Assert.Greater(positive.Count, 0, "allocation probe must detect its positive control");
            }
            var go = new GameObject("Stock library rendering test");
            try
            {
                foreach (var entry in catalog.Concrete)
                {
                    var row = report.vehicles.Single(v => v.key == entry.Key);
                    var draft = AssetDatabase.LoadAssetAtPath<VehicleProfileDraft>(row.draftPath);
                    var profile = AssetDatabase.LoadAssetAtPath<VehicleSensoryProfile>(row.audioPath);
                    Assert.NotNull(draft, entry.Key); Assert.NotNull(profile, entry.Key); Assert.AreSame(profile, draft.audio);
                    Assert.True(Directory.Exists(row.folder + "/Textures")); Assert.True(Directory.Exists(row.folder + "/HDRP Materials")); Assert.IsNull(draft.vehiclePrefab);
                    StringAssert.StartsWith(row.folder + "/Sound/Profiles/", row.audioPath);
                    foreach (string dependency in AssetDatabase.GetDependencies(row.audioPath).Where(p => p.EndsWith(".wav") || p.EndsWith(".asset")))
                        StringAssert.StartsWith(row.folder + "/Sound/", dependency, entry.Key + " depends on another car's sound");
                    Assert.IsNull(draft.GeneratedProduct); Assert.False(draft.availableInStore);
                    Assert.True(profile.Validate(out string failure), entry.Key + ": " + failure);
                    Assert.AreEqual(entry.Key, profile.vehicleIdentity);
                    var setup = profile.mostWantedAudio;
                    foreach (var bank in new[] { setup.engine, setup.sweeteners, setup.transmission, setup.whine, setup.shifts, setup.skids, setup.road, setup.wind, setup.nitrous })
                    {
                        Assert.NotNull(bank, entry.Key);
                        string localSource = row.folder + "/" + bank.sourceHash;
                        if (banks.TryGetValue(localSource, out var existing)) Assert.AreSame(existing, bank, "Variants duplicated a source within their car folder");
                        else banks.Add(localSource, bank);
                        foreach (var program in bank.programs) Assert.DoesNotThrow(() => new AemsEvaluator(program).Step(25), entry.Key + "/" + program.interfaceName);
                    }
                    foreach (var region in new[] { setup.acceleration, setup.deceleration })
                    {
                        Assert.True(EngineAudioCompiler.TryBuildSnapshot(new[] { region }, 1, out var snapshot), entry.Key);
                        var renderer = new EngineAudioRenderer(); renderer.SetTelemetry(snapshot); var output = new float[4096];
                        float rpm = (region.rpmAnchors.Min(a => a.rpm) + region.rpmAnchors.Max(a => a.rpm)) / 2;
                        Assert.AreEqual(1, renderer.Render(rpm, .5f, 48000, output.Length, 1, 1, output), entry.Key + "/" + region.id);
                        Assert.True(output.All(SensoryMath.IsFinite), entry.Key); Assert.True(output.Any(s => Math.Abs(s) > .00001f), entry.Key + " engine remained silent");
                    }
                    using (var runtime = new VehicleAudioRuntime(setup, null, go.transform, SensoryCategory.OtherVehicle, profile.exhaustPorts[0]))
                    {
                        var output = new float[2400]; double energy = 0;
                        for (int i = 0; i < 24; i++)
                        {
                            var frame = new VehicleFeedbackFrame { Sequence = i + 1, EngineRunning = true, EngineRpm = setup.idleRpm + (setup.maximumRpm - setup.idleRpm) * .5f,
                                EngineLoad = .7f, Throttle = i < 3 ? .8f : 0, Speed = 25, Brake = i == 5 ? 1 : 0, NitroIntensity = i == 2 ? 1 : 0,
                                Edges = i == 3 ? FeedbackEdges.Upshift : FeedbackEdges.None };
                            runtime.Sample(frame); runtime.Update(frame, .025f);
                            Assert.That(runtime.ActiveLayers, Is.GreaterThan(2), entry.Key + " lost its recovered layers");
                            runtime.Renderer.RenderInto(output, 2, 48000);
                            bool finite = true, bounded = true;
                            foreach (float sample in output)
                            {
                                finite &= SensoryMath.IsFinite(sample); bounded &= Math.Abs(sample) <= 1;
                                energy += sample * sample;
                            }
                            Assert.True(finite, entry.Key + " mixed output is not finite");
                            Assert.True(bounded, entry.Key + " output exceeded full scale");
                        }
                        Assert.That(energy, Is.GreaterThan(.01), entry.Key + " complete vehicle mixer remained silent");
                        using (var allocations = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 64, ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
                        {
                            runtime.Renderer.RenderInto(output, 2, 48000); allocations.Stop();
                            Assert.True(allocations.Valid); Assert.AreEqual(0, allocations.Count, entry.Key + " mixed callback allocated after warmup");
                        }
                    }
                }
                Assert.That(banks.Count, Is.LessThan(catalog.ConcreteCount * 9));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        [Test] public void ReimportReusesAssetsAndPreservesExistingDraftsAndModels()
        {
            string root = ExportedRoot();
            if (!Application.isBatchMode) Assert.Ignore("Run the reimport fixture only in a disposable batch project.");
            var before = JsonUtility.FromJson<BlackBoxVehicleLibrary.Report>(File.ReadAllText(root + "/ImportReport.json"));
            var row = before.vehicles.First(v => v.status == "Ready");
            var draft = AssetDatabase.LoadAssetAtPath<VehicleProfileDraft>(row.draftPath);
            string oldDescription = draft.description, sentinel = row.folder + "/retained-model-" + Guid.NewGuid().ToString("N") + ".txt";
            var profile = draft.audio;
            var guids = AssetDatabase.FindAssets("t:AemsAudioBank", new[] { root });
            try
            {
                File.WriteAllText(sentinel, "Existing model content must remain untouched.");
                draft.description = "Retained authoring notes"; EditorUtility.SetDirty(draft); AssetDatabase.SaveAssetIfDirty(draft);
                var after = BlackBoxVehicleLibrary.Import(InstalledGame(), root);
                Assert.AreEqual(before.Ready, after.Ready); Assert.AreEqual(before.vehicles.Count, after.vehicles.Count);
                Assert.AreEqual("Retained authoring notes", draft.description); Assert.AreSame(profile, draft.audio);
                Assert.AreEqual("Existing model content must remain untouched.", File.ReadAllText(sentinel));
                CollectionAssert.AreEquivalent(guids, AssetDatabase.FindAssets("t:AemsAudioBank", new[] { root }));
            }
            finally
            {
                draft.description = oldDescription; EditorUtility.SetDirty(draft); AssetDatabase.SaveAssetIfDirty(draft);
                AssetDatabase.DeleteAsset(sentinel); if (File.Exists(sentinel)) File.Delete(sentinel);
            }
        }
    }
}
