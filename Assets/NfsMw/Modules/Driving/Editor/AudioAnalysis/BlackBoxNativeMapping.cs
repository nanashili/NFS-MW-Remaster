using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NfsMwRemaster.Driving.AudioAnalysis;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    public sealed class BlackBoxMappingPlan
    {
        public BlackBoxSession Session { get; internal set; }
        public BlackBoxDocument[] Documents { get; internal set; }
        public string Fingerprint { get; internal set; }
        public string ProfilePath { get; internal set; }
        public string ExistingProfileHash { get; internal set; }
        public string TemplateHash { get; internal set; }
        public bool Unchanged { get; internal set; }
        public string[] Changes { get; internal set; }
        public string[] Losses { get; internal set; }
        public VehicleAudio SceneTarget { get; internal set; }
        public VehicleSensoryProfile SceneOriginalProfile { get; internal set; }
    }

    public static class BlackBoxNativeMapping
    {
        public const string CompilerVersion = "black-box-native/3";
        public const string RecoveryRoot = "Library/BlackBoxAudio/Recovery";
        [Serializable] private sealed class Inputs { public string version, id; public BlackBoxRegionReview[] regions; public string[] hashes; }
        [Serializable] private sealed class RecoveryEntry { public string path, backup; public bool existed; }
        [Serializable] private sealed class Recovery { public string state = "Prepared"; public RecoveryEntry[] entries; }
        [Serializable] private sealed class MappingManifest
        {
            public int schema = 1;
            public string profileId, fingerprint, compiler, fidelity = "Authored approximation; original EA control parity unverified";
            public BlackBoxRegionReview[] mappings;
            public BlackBoxWaveManifest[] recordings;
            public BlackBoxSource[] sourceReferences;
            public BlackBoxEvidenceExport[] evidence;
            public string[] losses, dependencies;
        }

        public static string Fingerprint(BlackBoxSession session)
        {
            return BlackBoxSourceAccess.Hash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Inputs { version = CompilerVersion,
                id = session.id, regions = session.regions.ToArray(), hashes = session.sources.Select(s => s.hash).ToArray() })));
        }

        public static BlackBoxMappingPlan Prepare(BlackBoxSession session, BlackBoxDocument[] documents, bool allowApproximation)
        {
            if (session == null) throw new InvalidOperationException("Create an analysis session first.");
            session.ValidateSchema();
            if (session.profile != null && session.profile.engineAudioSchema != 1)
                throw new InvalidOperationException("Unknown native template schema; preserve the profile and use a compatible compiler.");
            if (!allowApproximation) throw new InvalidOperationException("Verified original-control conversion is blocked: original playback semantics are unresolved. Explicitly review an authored approximation to continue.");
            if (session.regions.Count < 1 || session.regions.Count > 16) throw new InvalidOperationException("Review one to sixteen native regions.");
            if (documents == null || documents.Length == 0) throw new InvalidOperationException("Inspect attached sources first.");
            ValidateOutputFolder(session.outputFolder);
            string path = string.IsNullOrEmpty(session.generatedProfilePath) ? session.outputFolder + "/Profiles/Engine-authored.asset" : session.generatedProfilePath;
            if (!path.StartsWith(session.outputFolder + "/Profiles/", StringComparison.Ordinal)) throw new InvalidOperationException("Generated profile belongs to a different output root; preserve it and create a separate session.");
            if (File.Exists(path) && (string.IsNullOrEmpty(session.generatedProfileHash) || FileHash(path) != session.generatedProfileHash))
                throw new InvalidOperationException("Native output has manual changes or belongs to another author. Preserve it; use a separate output/session or reconcile explicitly.");
            foreach (var document in documents)
            {
                if (document.IsStale || BlackBoxSourceAccess.Hash(BlackBoxSourceAccess.Read(document.Attachment)) != document.Report.Source.Sha256)
                    throw new InvalidOperationException("A source changed. Reinspect and review the new revision before generation.");
            }
            ValidateRegions(session, documents);
            bool outputsComplete = ValidateExistingOutputs(session, documents);
            string fingerprint = Fingerprint(session);
            return new BlackBoxMappingPlan { Session = session, Documents = documents, ProfilePath = path, Fingerprint = fingerprint,
                ExistingProfileHash = FileHash(path), TemplateHash = session.profile == null ? "" : FileHash(AssetDatabase.GetAssetPath(session.profile)),
                Unchanged = session.generatedFingerprint == fingerprint && File.Exists(path) && outputsComplete,
                Changes = new[] { path, session.outputFolder + "/Decoded/<source-hash>-<recording-id>.wav (one per physical recording)",
                    session.outputFolder + "/Analysis/native-mapping.json", "Explicit Apply binds the generated profile on the selected prefab and vehicle draft; tuning is untouched." },
                Losses = new[] { "Original control interpreter, transitions, hidden state and interactive parity remain unverified.",
                    "RPM endpoints or explicit table-anchor hypotheses, piecewise interpolation, load gates and grain policy are authoring decisions.",
                    "Any source decode truncation remains in the evidence ledger; only actual decoded frames are eligible.",
                    "No automatic idle/redline retuning, filename role inference, channel reduction or sample-rate conversion." } };
        }

        public static void ValidateRegions(BlackBoxSession session, BlackBoxDocument[] documents)
        {
            session.ValidateSchema();
            if (session.regions.Count < 1 || session.regions.Count > 16 || documents == null) throw new InvalidOperationException("Review one to sixteen regions from available source evidence.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var review in session.regions)
            {
                if (review == null || string.IsNullOrEmpty(review.id) || !ids.Add(review.id) || !review.reviewed || review.provenance != "Authored override" || string.IsNullOrWhiteSpace(review.assumptions))
                    throw new InvalidOperationException("Every region needs a unique ID, an explicit authoring assumption and a review decision. Approval cannot turn an authored/inferred map into recovered metadata.");
                var pair = Find(documents, review.sourceHash, review.recordingId);
                var recording = pair.Item2;
                if (pair.Item1.IsStale || !recording.Decoded || recording.Pcm == null || recording.Pcm.Length != (long)recording.ValidFrames * recording.Channels)
                    throw new InvalidOperationException("Missing or inconsistent decoded PCM cannot compile to silence.");
                if (review.startFrame < 0 || review.endFrame <= review.startFrame + 1 || review.endFrame > recording.ValidFrames ||
                    !Finite(review.startRpm) || !Finite(review.endRpm) || review.startRpm <= 0 || review.endRpm <= 0 ||
                    !Finite(review.minimumLoad) || !Finite(review.maximumLoad) || review.minimumLoad < 0 || review.maximumLoad > 1 || review.minimumLoad > review.maximumLoad ||
                    !Finite(review.gain) || review.gain < 0 || review.gain > 1)
                    throw new InvalidOperationException("Invalid source-frame, RPM, load or gain interval: " + review.label);
                if (review.rpmAnchors != null && review.rpmAnchors.Length > 0 &&
                    (review.rpmAnchors.Length < 2 || review.rpmAnchors.Any(a => !Finite(a.rpm) || a.rpm <= 0 || a.sourceFrame < review.startFrame || a.sourceFrame >= review.endFrame)))
                    throw new InvalidOperationException("Mapping anchors must remain inside decoded source frames: " + review.label);
            }
        }

        public static VehicleSensoryProfile Apply(BlackBoxMappingPlan plan, bool bindVehicle = true)
            => ApplyCore(plan, bindVehicle, null);

        public static VehicleSensoryProfile ApplyToScene(BlackBoxMappingPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            BlackBoxVehicleAttachment.ValidateTarget(plan.SceneTarget);
            if (plan.SceneTarget.Profile != plan.SceneOriginalProfile)
                throw new InvalidOperationException("The vehicle's audio profile changed. Prepare the attachment again.");
            return ApplyCore(plan, false, plan.SceneTarget);
        }

        private static VehicleSensoryProfile ApplyCore(BlackBoxMappingPlan plan, bool bindVehicle, VehicleAudio sceneTarget)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var session = plan.Session;
            var current = Prepare(session, plan.Documents, true);
            if (current.Fingerprint != plan.Fingerprint || current.ProfilePath != plan.ProfilePath || current.ExistingProfileHash != plan.ExistingProfileHash || current.TemplateHash != plan.TemplateHash)
                throw new InvalidOperationException("Native mapping plan is stale. Build and review it again.");
            string prefabPath = "";
            if (bindVehicle)
            {
                if (session.vehicle == null || session.vehicle.vehiclePrefab == null || !PrefabUtility.IsPartOfPrefabAsset(session.vehicle.vehiclePrefab))
                    throw new InvalidOperationException("Apply requires a saved vehicle draft with an existing prefab.");
                if (session.vehicle.vehiclePrefab.GetComponentsInChildren<VehicleAudio>(true).Length != 1)
                    throw new InvalidOperationException("Vehicle prefab needs exactly one existing VehicleAudio; configure its telemetry and world binding first.");
                prefabPath = AssetDatabase.GetAssetPath(session.vehicle.vehiclePrefab);
            }
            if (Directory.Exists(RecoveryRoot) && Directory.EnumerateFiles(RecoveryRoot, "journal.json", SearchOption.AllDirectories)
                .Any(p => { string state = JsonUtility.FromJson<Recovery>(File.ReadAllText(p)).state; return state != "Committed" && state != "Restored"; }))
                throw new InvalidOperationException("An interrupted generation requires recovery review in " + RecoveryRoot + ". Prior output is preserved.");
            var existing = AssetDatabase.LoadAssetAtPath<VehicleSensoryProfile>(plan.ProfilePath);
            bool bindingCurrent = sceneTarget != null ? existing != null && sceneTarget.Profile == existing :
                !bindVehicle || existing != null && session.vehicle.audio == existing && session.vehicle.vehiclePrefab.GetComponentInChildren<VehicleAudio>(true).Profile == existing;
            if (plan.Unchanged && bindingCurrent) return existing;
            var recordingPairs = session.regions.Select(r => Find(plan.Documents, r.sourceHash, r.recordingId))
                .GroupBy(p => p.Item1.Report.Source.Sha256 + ":" + p.Item2.Id).Select(g => g.First()).ToArray();
            string manifestPath = session.outputFolder + "/Analysis/native-mapping.json";
            var targets = new List<string> { plan.ProfilePath, manifestPath, AssetDatabase.GetAssetPath(session) };
            foreach (var pair in recordingPairs) { targets.Add(WavePath(session, pair.Item1, pair.Item2)); targets.Add(WavePath(session, pair.Item1, pair.Item2) + ".json"); }
            if (bindVehicle) { targets.Add(prefabPath); targets.Add(AssetDatabase.GetAssetPath(session.vehicle)); }
            targets.RemoveAll(string.IsNullOrEmpty);
            targets.AddRange(targets.Select(p => p + ".meta").ToArray());
            foreach (string target in targets)
                if (File.Exists(target) && (new FileInfo(target).IsReadOnly || !AssetDatabase.IsOpenForEdit(target))) throw new IOException("Output is read-only: " + target);
            string recoveryFolder = RecoveryRoot + "/" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(recoveryFolder);
            var journal = new Recovery { entries = targets.Distinct().Select((p, i) => new RecoveryEntry { path = p, existed = File.Exists(p), backup = recoveryFolder + "/" + i + ".bak" }).ToArray() };
            foreach (var entry in journal.entries) if (entry.existed) File.Copy(entry.path, entry.backup);
            string journalPath = recoveryFolder + "/journal.json";
            File.WriteAllText(journalPath, JsonUtility.ToJson(journal, true));
            int undo = Undo.GetCurrentGroup(); Undo.IncrementCurrentGroup(); undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Apply reviewed engine audio");
            try
            {
                EnsureFolder(session.outputFolder + "/Decoded"); EnsureFolder(session.outputFolder + "/Profiles"); EnsureFolder(session.outputFolder + "/Analysis");
                var manifests = new List<BlackBoxWaveManifest>();
                foreach (var pair in recordingPairs)
                {
                    string wave = WavePath(session, pair.Item1, pair.Item2);
                    if (!File.Exists(wave))
                    {
                        var manifest = BlackBoxWaveExport.ExportNew(wave, pair.Item1, pair.Item2, 0, pair.Item2.ValidFrames);
                        WriteIfChanged(wave + ".json", JsonUtility.ToJson(manifest, true));
                    }
                    if (!File.Exists(wave + ".json")) throw new IOException("An decoding exists without its provenance manifest: " + wave);
                    var saved = JsonUtility.FromJson<BlackBoxWaveManifest>(File.ReadAllText(wave + ".json"));
                    if (saved.sourceHash != pair.Item1.Report.Source.Sha256 || saved.recordingId != pair.Item2.Id || saved.outputHash != FileHash(wave))
                        throw new IOException("Decoding evidence changed; preserved for review: " + wave);
                    manifests.Add(saved);
                    AssetDatabase.ImportAsset(wave, ImportAssetOptions.ForceSynchronousImport);
                    var importer = AssetImporter.GetAtPath(wave) as AudioImporter;
                    if (importer == null) throw new IOException("Unity did not import decoded PCM: " + wave);
                    var settings = importer.defaultSampleSettings;
                    if (settings.compressionFormat != AudioCompressionFormat.PCM || settings.loadType != AudioClipLoadType.DecompressOnLoad || settings.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate || importer.forceToMono || !settings.preloadAudioData)
                    {
                        settings.compressionFormat = AudioCompressionFormat.PCM; settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate; settings.preloadAudioData = true;
                        importer.defaultSampleSettings = settings; importer.forceToMono = false; importer.loadInBackground = false; importer.SaveAndReimport();
                    }
                }
                var profile = existing;
                if (!plan.Unchanged)
                {
                    if (profile == null)
                    {
                        profile = session.profile == null ? ScriptableObject.CreateInstance<VehicleSensoryProfile>() : UnityEngine.Object.Instantiate(session.profile);
                        profile.name = "Engine authored"; AssetDatabase.CreateAsset(profile, plan.ProfilePath);
                        Undo.RegisterCreatedObjectUndo(profile, "Create authored engine profile");
                    }
                    Undo.RecordObject(profile, "Compile reviewed engine mapping");
                    var regions = CreateRegions(session, plan.Documents, true);
                    profile.mostWantedAudio = null;
                    profile.engineLayers = new[] { new EngineSoundLayer { gain = 1, nativeRegions = regions, regions = Array.Empty<EngineRpmRegion>() } };
                    profile.engineAudioId = session.id; profile.engineAudioRevision = session.revision + 1;
                    EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                    WriteIfChanged(manifestPath, JsonUtility.ToJson(new MappingManifest { profileId = session.id, fingerprint = plan.Fingerprint, compiler = CompilerVersion,
                        mappings = session.regions.ToArray(), recordings = manifests.ToArray(), losses = plan.Losses,
                        sourceReferences = session.sources.ToArray(), evidence = plan.Documents.Select(BlackBoxEvidenceExport.From).ToArray(),
                        dependencies = plan.Documents.SelectMany(d => d.Report.References.Select(r => d.Report.Source.Sha256 + " / " + r.Kind + " -> " + r.Target + " / " + r.Notes)).ToArray() }, true));
                }
                if (bindVehicle && !bindingCurrent) Bind(session.vehicle, profile, prefabPath);
                if (sceneTarget != null && !bindingCurrent) BlackBoxVehicleAttachment.Bind(sceneTarget, profile);
                if (sceneTarget != null) BlackBoxVehicleAttachment.RememberTarget(session, sceneTarget);
                Undo.RecordObject(session, "Record engine mapping generation");
                session.generatedProfilePath = plan.ProfilePath; session.generatedProfileHash = FileHash(plan.ProfilePath); session.generatedFingerprint = plan.Fingerprint;
                session.generatedManifestHash = FileHash(manifestPath);
                EditorUtility.SetDirty(session); AssetDatabase.SaveAssetIfDirty(session);
                journal.state = "Committed"; File.WriteAllText(journalPath, JsonUtility.ToJson(journal, true));
                Undo.CollapseUndoOperations(undo);
                return profile;
            }
            catch (Exception failure)
            {
                try
                {
                    Undo.RevertAllDownToGroup(undo);
                    Restore(journalPath, journal);
                }
                catch (Exception recoveryFailure)
                {
                    journal.state = "Interrupted: " + recoveryFailure.Message; File.WriteAllText(journalPath, JsonUtility.ToJson(journal, true));
                    throw new IOException("Generation failed and automatic restore needs review in " + recoveryFolder, new AggregateException(failure, recoveryFailure));
                }
                throw;
            }
        }

        private static void Bind(VehicleProfileDraft vehicle, VehicleSensoryProfile profile, string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var presenter = root.GetComponentInChildren<VehicleAudio>(true);
                using (var serialized = new SerializedObject(presenter))
                { serialized.FindProperty("profile").objectReferenceValue = profile; serialized.ApplyModifiedPropertiesWithoutUndo(); }
                if (PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new IOException("Could not save vehicle profile binding.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Undo.RecordObject(vehicle, "Bind reviewed engine profile"); vehicle.audio = profile; EditorUtility.SetDirty(vehicle); AssetDatabase.SaveAssetIfDirty(vehicle);
        }

        public static EngineAudioRegion[] CreateRegions(BlackBoxSession session, BlackBoxDocument[] documents, bool persistent)
        {
            ValidateRegions(session, documents);
            return session.regions.Select(review =>
            {
                var pair = Find(documents, review.sourceHash, review.recordingId); var recording = pair.Item2;
                var region = new EngineAudioRegion { id = review.id, sourceHash = review.sourceHash, recordingId = review.recordingId,
                    sourceStartFrame = review.startFrame, sourceEndFrame = review.endFrame, sampleRate = recording.SampleRate, channels = recording.Channels,
                    gain = review.gain, minimumLoad = review.minimumLoad, maximumLoad = review.maximumLoad, provenance = review.provenance,
                    evidence = review.evidence + " | " + review.assumptions + " | " + review.lookupPolicy,
                    rpmAnchors = review.rpmAnchors != null && review.rpmAnchors.Length > 0 ? (EngineRpmAnchor[])review.rpmAnchors.Clone() :
                        new[] { new EngineRpmAnchor(review.startRpm, review.startFrame), new EngineRpmAnchor(review.endRpm, review.endFrame - 1) } };
                if (persistent) region.sourceClip = AssetDatabase.LoadAssetAtPath<AudioClip>(WavePath(session, pair.Item1, recording));
                else { region.compiledPcm = new float[checked((review.endFrame - review.startFrame) * recording.Channels)]; Array.Copy(recording.Pcm, review.startFrame * recording.Channels, region.compiledPcm, 0, region.compiledPcm.Length); }
                return region;
            }).ToArray();
        }

        // Restores only declared output targets; the interrupted state is retained alongside the backup.
        public static int RecoverInterrupted()
        {
            if (!Directory.Exists(RecoveryRoot)) return 0;
            int count = 0;
            foreach (string path in Directory.GetFiles(RecoveryRoot, "journal.json", SearchOption.AllDirectories))
            {
                var journal = JsonUtility.FromJson<Recovery>(File.ReadAllText(path));
                if (journal.state == "Committed" || journal.state == "Restored") continue;
                Restore(path, journal); count++;
            }
            return count;
        }
        private static void Restore(string journalPath, Recovery journal)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(journalPath));
            foreach (var entry in journal.entries)
            {
                if (!entry.path.StartsWith("Assets/", StringComparison.Ordinal) || !Path.GetFullPath(entry.backup).StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new IOException("Recovery path is outside the recorded output boundary.");
                BlackBoxSourceAccess.Resolve(Path.GetFullPath("Assets"), entry.path.Substring(7));
                if (entry.existed && !File.Exists(entry.backup)) throw new IOException("Recovery backup is missing: " + entry.backup);
            }
            foreach (var entry in journal.entries)
            {
                if (File.Exists(entry.path)) File.Copy(entry.path, entry.backup + ".interrupted-" + Guid.NewGuid().ToString("N"));
                if (entry.existed) File.Copy(entry.backup, entry.path, true);
                else if (File.Exists(entry.path)) File.Delete(entry.path);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            journal.state = "Restored"; File.WriteAllText(journalPath, JsonUtility.ToJson(journal, true));
        }

        public static Tuple<BlackBoxDocument, Recording> Find(BlackBoxDocument[] documents, string hash, string recordingId)
        {
            foreach (var document in documents)
                if (document.Report.Source.Sha256 == hash)
                    foreach (var recording in document.Report.Recordings) if (recording.Id == recordingId) return Tuple.Create(document, recording);
            throw new InvalidOperationException("Missing exact source/recording identity: " + hash + " / " + recordingId);
        }
        public static string FileHash(string path) => !string.IsNullOrEmpty(path) && File.Exists(path) ? BlackBoxSourceAccess.Hash(File.ReadAllBytes(path)) : "";
        private static bool ValidateExistingOutputs(BlackBoxSession session, BlackBoxDocument[] documents)
        {
            string manifest = session.outputFolder + "/Analysis/native-mapping.json";
            bool complete = File.Exists(manifest);
            if (complete && (string.IsNullOrEmpty(session.generatedManifestHash) || FileHash(manifest) != session.generatedManifestHash))
                throw new InvalidOperationException("Mapping evidence has manual changes or belongs to another session; preserve it and reconcile before generation.");
            foreach (var review in session.regions)
            {
                var pair = Find(documents, review.sourceHash, review.recordingId); string wave = WavePath(session, pair.Item1, pair.Item2);
                if (!File.Exists(wave) && !File.Exists(wave + ".json")) { complete = false; continue; }
                if (!File.Exists(wave) || !File.Exists(wave + ".json")) throw new IOException("An existing decoding is incomplete; preserve and review it: " + wave);
                var saved = JsonUtility.FromJson<BlackBoxWaveManifest>(File.ReadAllText(wave + ".json"));
                if (saved == null || saved.sourceHash != review.sourceHash || saved.recordingId != review.recordingId || saved.outputHash != FileHash(wave))
                    throw new IOException("Decoding evidence changed; preserved for review: " + wave);
            }
            return complete;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static string WavePath(BlackBoxSession session, BlackBoxDocument document, Recording recording)
            => session.outputFolder + "/Decoded/" + document.Report.Source.Sha256 + "-" + BlackBoxSourceAccess.Hash(Encoding.UTF8.GetBytes(recording.Id)).Substring(0, 12) + ".wav";
        private static void ValidateOutputFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains("..") || path.Contains('\\') || path.Split('/').Any(p => p.Equals("Resources", StringComparison.OrdinalIgnoreCase) || p.Equals("StreamingAssets", StringComparison.OrdinalIgnoreCase) || p.Equals("Editor", StringComparison.OrdinalIgnoreCase)) || !AssetDatabase.IsValidFolder(path))
                throw new InvalidOperationException("Choose an existing runtime content folder under Assets, outside Editor, Resources and StreamingAssets.");
            BlackBoxSourceAccess.Resolve(Path.GetFullPath("Assets"), path.Substring("Assets/".Length));
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        private static void WriteIfChanged(string path, string text)
        { if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text, new UTF8Encoding(false)); }
    }
}
