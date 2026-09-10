using System;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using NfsMwRemaster.Driving.Editor;
using NfsMwRemaster.Driving.Editor.AudioAnalysis;
using NfsMwRemaster.Driving.AudioAnalysis;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class BlackBoxNativeGenerationTests
    {
        string root, external;
        BlackBoxSession session;
        BlackBoxDocument[] documents;

        [SetUp] public void SetUp()
        {
            root = "Assets/__BlackBoxTest" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(root));
            AssetDatabase.CreateFolder(root, "Editor"); AssetDatabase.CreateFolder(root, "Sound");
            external = Path.Combine(Path.GetTempPath(), "blackbox-source-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(external);
            var pcm = new float[4410]; for (int i = 0; i < pcm.Length; i++) pcm[i] = (float)Math.Sin(i * 0.087) * 0.2f;
            using (var stream = File.Create(Path.Combine(external, "synthetic.wav"))) BlackBoxWaveExport.Write(stream, pcm, 44100, 1, 0, pcm.Length);
            var source = new BlackBoxSource { root = external, relativePath = "synthetic.wav" };
            documents = BlackBoxInspection.Inspect(new[] { source }); source.hash = documents[0].Report.Source.Sha256;
            session = ScriptableObject.CreateInstance<BlackBoxSession>(); session.sources.Add(source); session.outputFolder = root + "/Sound";
            AssetDatabase.CreateAsset(session, root + "/Editor/Analysis.asset");
            var recording = documents[0].Report.Recordings.Single();
            session.regions.Add(new BlackBoxRegionReview { sourceHash = source.hash, recordingId = recording.Id, startFrame = 0, endFrame = recording.ValidFrames,
                startRpm = 1000, endRpm = 7000, reviewed = true, assumptions = "Synthetic authored sweep; never recovered original metadata." });
        }

        [TearDown] public void TearDown()
        { Undo.ClearAll(); if (!string.IsNullOrEmpty(root)) AssetDatabase.DeleteAsset(root); if (Directory.Exists(external)) Directory.Delete(external, true); }

        [Test] public void GeneratedNativeProfilePreservesGuidReimportAndAvoidsUnnecessaryWrites()
        {
            var profile = BlackBoxNativeMapping.Apply(BlackBoxNativeMapping.Prepare(session, documents, true), false);
            string path = session.generatedProfilePath, guid = AssetDatabase.AssetPathToGUID(path), hash = session.generatedProfileHash;
            DateTime written = File.GetLastWriteTimeUtc(path);
            var plan = BlackBoxNativeMapping.Prepare(session, documents, true); Assert.True(plan.Unchanged);
            Assert.AreSame(profile, BlackBoxNativeMapping.Apply(plan, false)); Assert.AreEqual(written, File.GetLastWriteTimeUtc(path));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path)); Assert.AreEqual(hash, session.generatedProfileHash);
            session.regions[0].startRpm = 1200;
            BlackBoxNativeMapping.Apply(BlackBoxNativeMapping.Prepare(session, documents, true), false);
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path)); Assert.AreEqual(1200, profile.engineLayers[0].nativeRegions[0].rpmAnchors[0].rpm);
            Assert.False(AssetDatabase.GetDependencies(path, true).Any(p => p.Contains("/Editor/")), "runtime profile references editor evidence");
            Assert.True(EngineAudioCompiler.TryBuildSnapshot(profile.engineLayers[0].nativeRegions, 1, out var snapshot));
            var renderer = new EngineAudioRenderer(); renderer.SetTelemetry(snapshot); var output = new float[4096];
            Assert.AreEqual(1, renderer.Render(4000, 0.8f, 48000, output.Length, 1, 1, output)); Assert.True(output.Any(v => Math.Abs(v) > 0.001));
        }

        [Test] public void ApplyBindsTheExistingPrefabPresenterAndDraftWithoutReplacingThem()
        {
            var go = new GameObject("Synthetic existing vehicle"); go.AddComponent<VehicleAudio>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, root + "/Vehicle.prefab"); UnityEngine.Object.DestroyImmediate(go);
            var draft = ScriptableObject.CreateInstance<VehicleProfileDraft>(); draft.vehiclePrefab = prefab; AssetDatabase.CreateAsset(draft, root + "/Editor/Vehicle.asset"); session.vehicle = draft;
            string guid = AssetDatabase.AssetPathToGUID(root + "/Vehicle.prefab");
            var profile = BlackBoxNativeMapping.Apply(BlackBoxNativeMapping.Prepare(session, documents, true));
            Assert.AreSame(profile, draft.audio); Assert.AreSame(profile, prefab.GetComponent<VehicleAudio>().Profile);
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(root + "/Vehicle.prefab"));
            Assert.AreEqual(1, prefab.GetComponentsInChildren<VehicleAudio>(true).Length);
        }

        [Test] public void SceneAttachmentPreservesVehicleConnectionsAndOtherSoundSettingsAndPersists()
        {
            var scene = CreateAttachmentScene(out var previous);
            string path = root + "/VehicleScene.unity";
            try
            {
                var vehicle = new GameObject("Existing scene vehicle"); vehicle.SetActive(false);
                var source = vehicle.AddComponent<VehicleController>();
                var world = new GameObject("Existing audio world").AddComponent<SensoryAudioWorld>();
                var target = vehicle.AddComponent<VehicleAudio>();
                var template = ScriptableObject.CreateInstance<VehicleSensoryProfile>();
                template.vehicleIdentity = "Keep this vehicle identity";
                template.mostWantedAudio = new MostWantedVehicleAudio { schema = 1 };
                template.exhaustPorts = new[] { new Vector3(1, 2, 3) };
                template.windGain = AnimationCurve.Linear(0, 0.25f, 90, 0.75f);
                AssetDatabase.CreateAsset(template, root + "/ExistingAudio.asset");
                string templateHash = BlackBoxNativeMapping.FileHash(root + "/ExistingAudio.asset");
                target.Configure(source, world, template, true);
                var visual = new GameObject("Source model");
                var prefab = PrefabUtility.SaveAsPrefabAsset(visual, root + "/Model.prefab"); UnityEngine.Object.DestroyImmediate(visual);
                PrefabUtility.InstantiatePrefab(prefab, vehicle.transform);
                Assert.True(EditorSceneManager.SaveScene(scene, path));
                CollectionAssert.AreEqual(new[] { target }, BlackBoxVehicleAttachment.FindSceneVehicles(prefab));
                CollectionAssert.AreEqual(new[] { target }, BlackBoxVehicleAttachment.FindSceneVehicles(AssetDatabase.LoadAssetAtPath<DefaultAsset>(root)));
                var plan = BlackBoxVehicleAttachment.Prepare(session, documents, target);
                var profile = BlackBoxNativeMapping.ApplyToScene(plan);
                Assert.AreSame(profile, target.Profile); Assert.AreNotSame(template, profile);
                Assert.True(template.HasCompleteAudio); Assert.False(profile.HasCompleteAudio);
                Assert.AreEqual(templateHash, BlackBoxNativeMapping.FileHash(root + "/ExistingAudio.asset"));
                Assert.AreEqual(template.vehicleIdentity, profile.vehicleIdentity);
                CollectionAssert.AreEqual(template.exhaustPorts, profile.exhaustPorts);
                Assert.AreEqual(template.windGain.Evaluate(30), profile.windGain.Evaluate(30));
                using (var serialized = new SerializedObject(target))
                {
                    Assert.AreSame(source, serialized.FindProperty("sourceComponent").objectReferenceValue);
                    Assert.AreSame(world, serialized.FindProperty("world").objectReferenceValue);
                    Assert.True(serialized.FindProperty("player").boolValue);
                }
                Assert.True(scene.isDirty);
                Assert.AreSame(target, BlackBoxVehicleAttachment.RestoreTarget(session));
                Assert.True(EngineAudioCompiler.TryBuildProfile(target.Profile, out var snapshot));
                target.SetNativeTelemetry(snapshot); var output = new float[4096];
                Assert.AreEqual(1, target.RenderNative(4000, 0.8f, 48000, output.Length, 1, 1, output));
                Assert.True(output.Any(v => Math.Abs(v) > 0.001));
                string profileGuid = AssetDatabase.AssetPathToGUID(plan.ProfilePath);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Assert.AreSame(template, target.Profile);
                Undo.PerformRedo(); profile = AssetDatabase.LoadAssetAtPath<VehicleSensoryProfile>(plan.ProfilePath);
                Assert.IsNotNull(profile); Assert.AreSame(profile, target.Profile);
                Assert.AreEqual(profileGuid, AssetDatabase.AssetPathToGUID(plan.ProfilePath));
                Assert.True(EditorSceneManager.SaveScene(scene));
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                Assert.AreSame(profile, BlackBoxVehicleAttachment.RestoreTarget(session).Profile);
            }
            finally { RestoreAttachmentScenes(previous); }
        }

        [Test] public void SceneAttachmentAcceptsUnconfiguredVehicleAudioAndRejectsChangedTargetBeforeWriting()
        {
            CreateAttachmentScene(out var previous);
            try
            {
                var vehicle = new GameObject("Scene attachment checks"); vehicle.SetActive(false);
                var target = vehicle.AddComponent<VehicleAudio>();
                Assert.DoesNotThrow(() => BlackBoxVehicleAttachment.Prepare(session, documents, target));
                var source = vehicle.AddComponent<VehicleController>();
                var world = new GameObject("Audio world").AddComponent<SensoryAudioWorld>();
                target.Configure(source, world, null, true);
                var plan = BlackBoxVehicleAttachment.Prepare(session, documents, target);
                var replacement = ScriptableObject.CreateInstance<VehicleSensoryProfile>();
                AssetDatabase.CreateAsset(replacement, root + "/ChangedAudio.asset");
                target.Configure(source, world, replacement, true);
                Assert.Throws<InvalidOperationException>(() => BlackBoxNativeMapping.ApplyToScene(plan));
                Assert.AreSame(replacement, target.Profile); Assert.False(File.Exists(plan.ProfilePath));
                session.regions[0].reviewed = false;
                Assert.Throws<InvalidOperationException>(() => BlackBoxVehicleAttachment.Prepare(session, documents, target));
                Assert.False(File.Exists(plan.ProfilePath));
            }
            finally { RestoreAttachmentScenes(previous); }
        }

        private static Scene CreateAttachmentScene(out SceneSetup[] previous)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!Application.isBatchMode && (scene.isDirty || string.IsNullOrEmpty(scene.path) && scene.rootCount > 0))
                    Assert.Ignore("Save/close edited scenes before this scene attachment test, or use a disposable project copy.");
            }
            previous = EditorSceneManager.GetSceneManagerSetup();
            return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static void RestoreAttachmentScenes(SceneSetup[] previous)
        {
            if (previous.Any(s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(previous);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test] public void GinDraftPreservesNonlinearTablePositionsAndTrimsOnlyUndecodedTail()
        {
            var report = documents[0].Report;
            report.Fields.Add(new FieldEvidence("candidate_endpoint_0", new ByteRange(8, 4), "", "1000", EvidenceProvenance.Decoded));
            report.Fields.Add(new FieldEvidence("candidate_endpoint_1", new ByteRange(12, 4), "", "7000", EvidenceProvenance.Decoded));
            report.Fields.Add(new FieldEvidence("decoded_sample_frames", new ByteRange(24, 4), "", "4500", EvidenceProvenance.Decoded));
            report.Tables.Add(new StructuralTable { Name = "table_a", Entries = { new TableEntry { Index = 0, RawValue = 0 }, new TableEntry { Index = 1, RawValue = 200 }, new TableEntry { Index = 2, RawValue = 2000 }, new TableEntry { Index = 3, RawValue = 4500 } } });
            var draft = BlackBoxMappingDrafts.FromGinTable(documents[0], report.Recordings[0]);
            Assert.False(draft.reviewed); Assert.AreEqual("Authored override", draft.provenance);
            CollectionAssert.AreEqual(new[] { 0, 200, 2000, 4409 }, draft.rpmAnchors.Select(a => a.sourceFrame));
            Assert.AreEqual(3000, draft.rpmAnchors[1].rpm); Assert.AreEqual(5000, draft.rpmAnchors[2].rpm);
            Assert.AreEqual(6927.2f, draft.rpmAnchors[3].rpm, 0.01f);
            session.regions.Clear(); session.regions.Add(draft);
            Assert.Throws<InvalidOperationException>(() => BlackBoxNativeMapping.Prepare(session, documents, true));
            draft.reviewed = true;
            var profile = BlackBoxNativeMapping.Apply(BlackBoxNativeMapping.Prepare(session, documents, true), false);
            CollectionAssert.AreEqual(draft.rpmAnchors, profile.engineLayers[0].nativeRegions[0].rpmAnchors);
            StringAssert.Contains("rpmAnchors", File.ReadAllText(session.outputFolder + "/Analysis/native-mapping.json"));
            Assert.True(EngineAudioCompiler.TryBuildProfile(profile, out var snapshot));
            var renderer = new EngineAudioRenderer(); renderer.SetTelemetry(snapshot); var pcm = new float[1024];
            Assert.AreEqual(1, renderer.Render(3000, 0.8f, 48000, pcm.Length, 1, 1, pcm));
            Assert.True(pcm.Any(v => Math.Abs(v) > 0.001));
            report.Tables.Single(t => t.Name == "table_a").Entries[2].RawValue = 100;
            Assert.Throws<InvalidOperationException>(() => BlackBoxMappingDrafts.FromGinTable(documents[0], report.Recordings[0]));
        }

        [Test] public void PackageContainsProfileClipsMappingsAndManifestsWithoutAVehicle()
        {
            Assert.IsNull(session.vehicle);
            BlackBoxNativeMapping.Apply(BlackBoxNativeMapping.Prepare(session, documents, true), false);
            var assets = BlackBoxAudioExport.PackageAssets(session, documents);
            CollectionAssert.Contains(assets, session.generatedProfilePath);
            CollectionAssert.Contains(assets, session.outputFolder + "/Analysis/native-mapping.json");
            string wave = assets.Single(p => p.EndsWith(".wav", StringComparison.Ordinal));
            CollectionAssert.Contains(assets, wave + ".json");
            Assert.False(assets.Any(p => p.Contains("/Editor/")));
            string package = Path.Combine(external, "audio.unitypackage");
            BlackBoxAudioExport.ExportPackage(package, session, documents);
            Assert.Greater(new FileInfo(package).Length, 100);
            Assert.Throws<IOException>(() => BlackBoxAudioExport.ExportPackage(package, session, documents));
            session.regions[0].startRpm = 1200;
            Assert.Throws<InvalidOperationException>(() => BlackBoxAudioExport.PackageAssets(session, documents));
        }

        [Test] public void BulkExportNeedsNoMappingAndCancellationRemovesOnlyItsNewOutput()
        {
            session.regions.Clear();
            string folder = BlackBoxAudioExport.ExportRecordings(external, documents);
            string wave = Directory.GetFiles(folder, "*.wav", SearchOption.AllDirectories).Single();
            Assert.True(File.Exists(wave + ".json"));
            Assert.True(Directory.GetFiles(folder, "source-evidence.json", SearchOption.AllDirectories).Any());
            var evidence = JsonUtility.FromJson<BlackBoxEvidenceExport>(File.ReadAllText(Directory.GetFiles(folder, "source-evidence.json", SearchOption.AllDirectories).Single()));
            Assert.AreEqual(documents[0].Report.Recordings[0].Id, evidence.recordings[0].id);
            Assert.True(evidence.recordings[0].decoded); Assert.AreEqual(4410, evidence.recordings[0].validFrames);
            var exported = BlackBoxInspection.Inspect(new[] { new BlackBoxSource { root = Path.GetDirectoryName(wave), relativePath = Path.GetFileName(wave) } })[0].Report.Recordings.Single();
            CollectionAssert.AreEqual(documents[0].Report.Recordings[0].Pcm, exported.Pcm);
            int priorFolders = Directory.GetDirectories(external).Length;
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => BlackBoxAudioExport.ExportRecordings(external, documents, cancel.Token));
            Assert.AreEqual(priorFolders, Directory.GetDirectories(external).Length);
            Assert.True(File.Exists(Path.Combine(external, "synthetic.wav")));
            string duplicate = BlackBoxAudioExport.ExportRecordings(external, new[] { documents[0], documents[0] });
            Assert.AreEqual(1, Directory.GetFiles(duplicate, "*.wav", SearchOption.AllDirectories).Length);
        }

        [Test] public void StalePlanManualEditsAndUnknownSchemaAreRejected()
        {
            var plan = BlackBoxNativeMapping.Prepare(session, documents, true); session.regions[0].startRpm = 1250;
            Assert.Throws<InvalidOperationException>(() => BlackBoxNativeMapping.Apply(plan, false));
            var profile = BlackBoxNativeMapping.Apply(BlackBoxNativeMapping.Prepare(session, documents, true), false);
            profile.engineLayers[0].nativeRegions[0].gain = 0.13f; EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
            Assert.Throws<InvalidOperationException>(() => BlackBoxNativeMapping.Prepare(session, documents, true));
            session.schema = 999; Assert.Throws<InvalidOperationException>(() => session.ValidateSchema());
        }

        [Test] public void ChangedSourceRequiresRemappingAndMissingAudioDoesNotCompile()
        {
            File.AppendAllText(Path.Combine(external, "synthetic.wav"), "changed");
            Assert.Throws<InvalidOperationException>(() => BlackBoxNativeMapping.Prepare(session, documents, true));
            documents[0].Report.Recordings[0].Pcm = null;
            Assert.Throws<InvalidOperationException>(() => BlackBoxNativeMapping.CreateRegions(session, documents, false));
        }

        [Test] public void UnknownTemplateSchemaAndUnsafeSessionIdentityCannotGenerate()
        {
            var template = ScriptableObject.CreateInstance<VehicleSensoryProfile>(); template.engineAudioSchema = 999; session.profile = template;
            try
            {
                Assert.Throws<InvalidOperationException>(() => BlackBoxNativeMapping.Prepare(session, documents, true));
                Assert.False(Directory.Exists(session.outputFolder + "/Profiles"));
                session.profile = null; session.id = "../../outside-reports";
                Assert.Throws<InvalidOperationException>(() => session.ValidateSchema());
                Assert.Throws<InvalidOperationException>(() => BlackBoxNativeMapping.Prepare(session, documents, true));
            }
            finally { UnityEngine.Object.DestroyImmediate(template); }
        }

        [Test] public void ChangedDecodingAfterReviewPreservesPriorProfileAndEvidence()
        {
            BlackBoxNativeMapping.Apply(BlackBoxNativeMapping.Prepare(session, documents, true), false);
            string path = session.generatedProfilePath; byte[] previous = File.ReadAllBytes(path);
            session.regions[0].startRpm = 1500; var plan = BlackBoxNativeMapping.Prepare(session, documents, true);
            string wave = AssetDatabase.GetDependencies(path).Single(p => p.EndsWith(".wav", StringComparison.Ordinal));
            File.WriteAllText(wave + ".json", "{}");
            Assert.Throws<IOException>(() => BlackBoxNativeMapping.Apply(plan, false));
            CollectionAssert.AreEqual(previous, File.ReadAllBytes(path));
            Assert.AreEqual(0, BlackBoxNativeMapping.RecoverInterrupted(), "handled failure left an unresolved recovery journal");
        }

        [Test] public void UnchangedGenerationRejectsManualMappingEvidenceEdits()
        {
            BlackBoxNativeMapping.Apply(BlackBoxNativeMapping.Prepare(session, documents, true), false);
            string manifest = session.outputFolder + "/Analysis/native-mapping.json", profileHash = session.generatedProfileHash;
            StringAssert.Contains(documents[0].Report.Source.Sha256, File.ReadAllText(manifest));
            File.AppendAllText(manifest, "\nmanual evidence annotation");
            Assert.Throws<InvalidOperationException>(() => BlackBoxNativeMapping.Prepare(session, documents, true));
            Assert.AreEqual(profileHash, BlackBoxNativeMapping.FileHash(session.generatedProfilePath));
            StringAssert.EndsWith("manual evidence annotation", File.ReadAllText(manifest));
        }

        [Serializable] sealed class InterruptedEntry { public string path, backup; public bool existed; }
        [Serializable] sealed class InterruptedJournal { public string state = "Prepared"; public InterruptedEntry[] entries; }
        [Test] public void InterruptedGenerationRestoresExactPriorFilesAndKeepsInterruptedCopy()
        {
            string folder = BlackBoxNativeMapping.RecoveryRoot + "/test-" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(folder);
            string path = root + "/Sound/recovery.txt", backup = folder + "/0.bak", newPath = root + "/Sound/partial.txt";
            File.WriteAllText(backup, "prior valid output"); File.WriteAllText(path, "interrupted output"); File.WriteAllText(newPath, "partial new output");
            File.WriteAllText(folder + "/journal.json", JsonUtility.ToJson(new InterruptedJournal { entries = new[]
            { new InterruptedEntry { path = path, backup = backup, existed = true }, new InterruptedEntry { path = newPath, backup = folder + "/1.bak", existed = false } } }));
            try
            {
                Assert.AreEqual(1, BlackBoxNativeMapping.RecoverInterrupted());
                Assert.AreEqual("prior valid output", File.ReadAllText(path)); Assert.False(File.Exists(newPath));
                Assert.True(Directory.GetFiles(folder, "0.bak.interrupted-*").Any(p => File.ReadAllText(p) == "interrupted output"));
                Assert.AreEqual(0, BlackBoxNativeMapping.RecoverInterrupted());
            }
            finally { Directory.Delete(folder, true); }
        }

        [Test] public void OverlayUndoRedoAndCancelledWaveExportPreserveEvidence()
        {
            Undo.RecordObject(session, "Author test RPM"); session.regions[0].startRpm = 1700; Undo.FlushUndoRecordObjects();
            Undo.PerformUndo(); Assert.AreEqual(1000, session.regions[0].startRpm); Undo.PerformRedo(); Assert.AreEqual(1700, session.regions[0].startRpm);
            string path = Path.Combine(external, "cancelled.wav");
            using (var cancellation = new CancellationTokenSource())
            { cancellation.Cancel(); Assert.Throws<OperationCanceledException>(() => BlackBoxWaveExport.ExportNew(path, documents[0], documents[0].Report.Recordings[0], 0, 4410, cancellation.Token)); }
            Assert.False(File.Exists(path)); Assert.False(Directory.GetFiles(external, "*.tmp").Any());
        }
    }
}
