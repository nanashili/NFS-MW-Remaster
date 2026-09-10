using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Editor.DrivingMechanics
{
    /// <summary>
    /// Explicit, repeatable migration of saved scene tuning. Batch execution is restricted to
    /// a validation copy; publishing its hash-checked changes is a separate operation.
    /// Existing asset GUIDs, scene object IDs and model bindings remain authoritative.
    /// </summary>
    public static class MostWantedSceneMigration
    {
        public const string StreetTuningPath = "Assets/NfsMw/Modules/Driving/Data/MW2005StreetTuning.asset";
        public const string CapturePath = "Tools/DrivingMechanics/Evidence/handling-local-20260908.json";
        public const string ControllerGuid = "d731bad1d60834fa19a4c6502eac41cc";
        public const string TuningGuid = "ac296239dbe2842f5a57ca32dba789f7";
        private const float AeroCalibrationSpeedMps = 100f / 3.6f;

        [Serializable] public sealed class ChangedFile
        {
            public string path, beforeSha256, afterSha256, backup;
            public bool created;
        }
        [Serializable] public sealed class ProfileResult
        {
            public string path, guid, policy, name;
            public long localId;
            public bool alreadyEnabled;
            public MostWantedDrivingImportReport provenance;
        }
        [Serializable] public sealed class SceneResult
        {
            public string path, status, sha256;
            public int serializedControllers, embeddedProfiles;
            public string[] profileDependencies;
        }
        [Serializable] public sealed class MigrationReport
        {
            public int schema = 1;
            public string generatedUtc, captureSha256;
            public bool completed;
            public List<ChangedFile> changedFiles = new List<ChangedFile>();
            public List<ProfileResult> profiles = new List<ProfileResult>();
            public List<SceneResult> scenes = new List<SceneResult>();
        }

        /// <summary>
        /// BMW-backed player profiles import available source scalars while retaining the
        /// authored wheel/rig dimensions. Anonymous traffic and demonstration cars retain
        /// their own mass, power, gears and geometry, using a labelled reference adaptation.
        /// No anonymous profile is relabelled as an decoded model of that vehicle.
        /// </summary>
        public static MostWantedDrivingImport CreateCandidate(VehicleTuning baseline,
            MostWantedHandlingReport capture, bool importBmwSource, bool npc)
        {
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));
            RacingLineSnapshot.ValidateTuning(baseline);
            string donorName = !importBmwSource && baseline.driveLayout == VehicleDriveLayout.Fwd ? "gti" : "bmwm3gtre46";
            var donor = capture?.vehicles?.SingleOrDefault(vehicle => vehicle.name == donorName)
                ?? throw new InvalidDataException("The preserved capture has no " + donorName + " record.");
            var result = MostWantedDrivingImporter.Create(capture, donor,
                MostWantedDrivingImporter.FirstReferences(donor), baseline);
            try
            {
                if (baseline.UsesMostWantedReference)
                {
                    Object.DestroyImmediate(result.Tuning);
                    result.Tuning = baseline.CreateRuntimeCopy();
                    result.Report.mapped.Clear();
                    result.Report.fidelity = "Already-enabled reference profile retained without recalibration.";
                }
                else if (importBmwSource)
                {
                    var tuning = result.Tuning;
                    // Mesh radius is model-space fitment, not permission to resize a user's car.
                    tuning.tires.wheelRadius = baseline.tires.wheelRadius;
                    tuning.tires.wheelWidth = baseline.tires.wheelWidth;
                    tuning.mostWanted.rearWheelRadiusScale = 1f;
                    result.Report.mapped.RemoveAll(mapping => mapping.field == "RIM_SIZE"
                        || mapping.field == "SECTION_WIDTH" || mapping.field == "ASPECT_RATIO");
                    result.Report.retainedAdaptations.Insert(0,
                        "Scene migration retains authored wheel radius/width, mesh scale, wheel anchors, center of mass, rest length and travel. Decoded tyre dimensions remain evidence, not an automatic model rescale.");
                    result.Report.fidelity = "BMW first-linked source mechanics with preserved scene fitment. Not a 1:1 original-game vehicle or a confirmed active upgrade configuration.";
                }
                else
                {
                    var sourceTuning = result.Tuning;
                    var adapted = baseline.CreateRuntimeCopy();
                    adapted.simulationModel = VehicleSimulationModel.MostWantedReference;
                    adapted.mostWanted = JsonUtility.FromJson<MostWantedDrivingSettings>(JsonUtility.ToJson(sourceTuning.mostWanted));
                    var m = adapted.mostWanted;
                    var engine = adapted.engine;
                    float domainRatio = (sourceTuning.mostWanted.torqueTableMaximumRpm - sourceTuning.engine.idleRpm)
                        / (sourceTuning.engine.redlineRpm - sourceTuning.engine.idleRpm);
                    m.torqueTableMaximumRpm = engine.idleRpm + (engine.redlineRpm - engine.idleRpm) * domainRatio;
                    m.gearEfficiency = Enumerable.Repeat(engine.drivelineEfficiency, engine.gearRatios.Length).ToArray();
                    m.reverseGearEfficiency = engine.drivelineEfficiency;
                    m.rearWheelRadiusScale = m.rearSpringScale = m.rearDamperScale = 1f;
                    m.frontReboundDamperScale = m.rearReboundDamperScale = 1f;
                    m.inductionLowBoost = m.inductionHighBoost = engine.forcedInduction ? Mathf.Max(0f, engine.boostTorqueMultiplier - 1f) : 0f;
                    m.inductionVacuum = 0f;
                    m.nitrousTorqueBoost = engine.nitrousFuelSeconds > 0f && engine.maxTorqueNewtonMeters > 0f
                        ? engine.nitrousTorque / engine.maxTorqueNewtonMeters : 0f;
                    m.linearDownforceCoefficient = adapted.aero.downforceCoefficient * AeroCalibrationSpeedMps;
                    adapted.aero.downforceCoefficient = 0f;
                    adapted.chassis.rollingResistance = 0f;
                    // NPC controllers already produce steering commands for their own lane-following
                    // control law. Do not stack gamepad input histories or new shift targets on it.
                    m.useSteeringTables = !npc;
                    m.torqueBasedShifting = !npc;
                    if (!npc) adapted.controls.maxSteerAngle = MostWantedSteeringModel.AbsoluteMaximumDegrees;
                    adapted.assists.stabilityControl = adapted.assists.countersteering = false;
                    adapted.assists.abs = adapted.assists.tractionControl = false;
                    adapted.handling.brakeToDrift = false;
                    adapted.handling.driftBias = 0f;
                    Object.DestroyImmediate(sourceTuning);
                    result.Tuning = adapted;
                    result.Report.mapped.RemoveAll(mapping => mapping.field != "TORQUE" && mapping.field != "ENGINE_BRAKING");
                    foreach (var mapping in result.Report.mapped)
                    {
                        mapping.destination = mapping.field == "TORQUE" ? "mostWanted.normalizedTorque" : "mostWanted.engineBraking";
                        mapping.interpretation = "Donor " + donorName + " curve shape, adapted to this profile's authored idle/redline and power. Not decoded coefficients for the anonymous vehicle.";
                    }
                    result.Report.fidelity = "Reference-mode adaptation of authored profile '" + baseline.displayName
                        + "'; donor shape " + donorName + ". Original mass, peak torque, gears, drive layout, wheels, springs and brakes retained. No original-vehicle fidelity claim.";
                    result.Report.retainedAdaptations.Insert(0,
                        "Anonymous profile migration preserves authored vehicle-specific statistics and contact calibration. Linear downforce is matched to the previous quadratic value at 100 km/h only. Gear efficiencies use the authored driveline efficiency; nitrous fraction is authored peak nitrous torque divided by peak engine torque.");
                    if (npc) result.Report.retainedAdaptations.Insert(0,
                        "NPC profile: existing AI steering shaping and shift thresholds retained; gamepad history steering and torque-crossover shifts are disabled. Engine, nitrous and aerodynamic reference paths remain enabled.");
                }
                result.Tuning.name = baseline.name;
                result.Tuning.displayName = baseline.displayName;
                RacingLineSnapshot.ValidateTuning(result.Tuning);
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        public static bool IsRecoveryScene(string path)
            => path.Replace('\\', '/').StartsWith("Assets/_Recovery/", StringComparison.Ordinal);

        /// <summary>Migration entry point for a closed, task-owned Unity validation copy only.</summary>
        public static void ApplyInValidationCopy()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string marker = Path.Combine(project, ".mw-driving-validation-source");
            if (!Application.isBatchMode || !File.Exists(marker)
                || string.Equals(Path.GetFullPath(File.ReadAllText(marker).Trim()), project, StringComparison.Ordinal))
                throw new InvalidOperationException("Run scene migration in a task-owned validation copy, not the live project.");
            string output = Environment.GetEnvironmentVariable("MW_SCENE_MIGRATION_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("Set MW_SCENE_MIGRATION_OUTPUT to a new report directory.");
            output = Path.GetFullPath(output);
            if (Directory.Exists(output)) throw new IOException("Migration report directory already exists.");
            Directory.CreateDirectory(output);
            var report = new MigrationReport { generatedUtc = DateTime.UtcNow.ToString("O"), captureSha256 = HashFile(CapturePath) };
            var capture = MostWantedDrivingWindow.ReadReport(CapturePath);
            var changes = new Dictionary<string, ChangedFile>(StringComparer.Ordinal);
            try
            {
                var paths = AssetDatabase.FindAssets("t:VehicleTuning", new[] { "Assets" })
                    .Select(AssetDatabase.GUIDToAssetPath).Where(path => !IsRecoveryScene(path)).Distinct().OrderBy(path => path, StringComparer.Ordinal).ToArray();
                foreach (string path in paths)
                {
                    var tuning = AssetDatabase.LoadAssetAtPath<VehicleTuning>(path);
                    bool sourceBmw = path == StreetTuningPath || path == "Assets/NfsMw/Content/Vehicles/Street/BMW/M3 E42/Framework/Factory.asset";
                    bool npc = path.StartsWith("Assets/NfsMw/Modules/Driving/Data/Traffic/", StringComparison.Ordinal) || path.EndsWith("PoliceVehicleTuning.asset", StringComparison.Ordinal);
                    var item = new ProfileResult { path = path, guid = AssetDatabase.AssetPathToGUID(path), name = tuning.name,
                        policy = sourceBmw ? "BMW source scalars; authored fitment" : npc ? "Authored NPC reference adaptation" : "Authored vehicle reference adaptation", alreadyEnabled = tuning.UsesMostWantedReference };
                    report.profiles.Add(item);
                    if (item.alreadyEnabled) { RacingLineSnapshot.ValidateTuning(tuning); continue; }
                    Backup(path, output, changes);
                    using (var candidate = CreateCandidate(tuning, capture, sourceBmw, npc))
                    {
                        item.provenance = candidate.Report;
                        HideFlags flags = tuning.hideFlags;
                        EditorUtility.CopySerialized(candidate.Tuning, tuning);
                        tuning.hideFlags = flags;
                        EditorUtility.SetDirty(tuning);
                        AssetDatabase.SaveAssetIfDirty(tuning);
                    }
                }

                foreach (string guid in AssetDatabase.FindAssets("t:VehicleDefinition", new[] { "Assets" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var definition = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(path);
                    if (definition.factoryTuning == null || !definition.factoryTuning.UsesMostWantedReference) continue;
                    string evidence = "Most Wanted reference mode enabled by scene migration. See Tools/DrivingMechanics/SCENE_MIGRATION.md and its hash/provenance manifest. Authored fitment retained; anonymous cars retain authored statistics. Original-game fidelity is not established.";
                    if (definition.referenceEvidence == evidence) continue;
                    Backup(path, output, changes);
                    definition.referenceEvidence = evidence;
                    EditorUtility.SetDirty(definition);
                    AssetDatabase.SaveAssetIfDirty(definition);
                }

                var scenePaths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                    .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.Ordinal).ToArray();
                foreach (string path in scenePaths)
                {
                    string original = File.ReadAllText(path);
                    var entry = new SceneResult { path = path, serializedControllers = CountScript(original, ControllerGuid),
                        embeddedProfiles = CountScript(original, TuningGuid), sha256 = HashFile(path) };
                    report.scenes.Add(entry);
                    if (IsRecoveryScene(path)) { entry.status = "Recovery snapshot preserved; not a gameplay scene"; entry.profileDependencies = Array.Empty<string>(); continue; }
                    if (entry.embeddedProfiles > 0) MigrateEmbedded(path, original, capture, output, changes, report);
                    entry.profileDependencies = AssetDatabase.GetDependencies(path, true)
                        .Where(dependency => paths.Contains(dependency)).OrderBy(dependency => dependency, StringComparer.Ordinal).ToArray();
                    entry.status = entry.serializedControllers > 0 || entry.profileDependencies.Length > 0
                        ? "Reference mode enabled through shared or embedded tuning"
                        : "No vehicle physics to migrate; scene preserved";
                    entry.sha256 = HashFile(path);
                }
                foreach (var change in changes.Values)
                {
                    change.afterSha256 = HashFile(change.path);
                    if (change.beforeSha256 != change.afterSha256) report.changedFiles.Add(change);
                }
                report.completed = true;
                File.WriteAllText(Path.Combine(output, "migration.json"), JsonConvert.SerializeObject(report, Formatting.Indented), new UTF8Encoding(false));
                Debug.Log("MW_SCENE_MIGRATION scenes=" + report.scenes.Count + " profiles=" + report.profiles.Count + " changedFiles=" + report.changedFiles.Count + " report=" + output);
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
                throw;
            }
        }

        private static void MigrateEmbedded(string path, string original, MostWantedHandlingReport capture,
            string output, Dictionary<string, ChangedFile> changes, MigrationReport report)
        {
            var blocks = new Dictionary<ulong, string>();
            var owners = new Dictionary<ulong, ulong>();
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            bool changed = false;
            try
            {
                var controllers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<VehicleController>(true)).ToArray();
                var inline = controllers.Select(vehicle => vehicle.FactoryTuning).Where(tuning => tuning != null
                        && (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(tuning)) || AssetDatabase.GetAssetPath(tuning) == path))
                    .Distinct().ToArray();
                foreach (var tuning in inline)
                {
                    // Embedded ScriptableObjects can have a zero GlobalObjectId in EditMode.
                    // Resolve their serialized identity from an owning controller instead of
                    // inventing a new ID or relying on the transient native instance ID.
                    var controller = controllers.First(vehicle => vehicle.FactoryTuning == tuning);
                    ulong ownerId = GlobalObjectId.GetGlobalObjectIdSlow(controller).targetObjectId;
                    ulong id = LocalTuningReference(Block(original, ownerId));
                    string block = Block(original, id);
                    if (!block.Contains(TuningGuid)) throw new InvalidDataException("Inline tuning object identity mismatch in " + path);
                    var item = new ProfileResult { path = path, localId = checked((long)id), name = tuning.name,
                        policy = "Scene-embedded BMW reference scalars; authored fitment", alreadyEnabled = tuning.UsesMostWantedReference };
                    report.profiles.Add(item);
                    if (item.alreadyEnabled) continue;
                    blocks.Add(id, block);
                    owners.Add(id, ownerId);
                    using var candidate = CreateCandidate(tuning, capture, true, false);
                    item.provenance = candidate.Report;
                    HideFlags flags = tuning.hideFlags;
                    EditorUtility.CopySerialized(candidate.Tuning, tuning);
                    tuning.hideFlags = flags;
                    EditorUtility.SetDirty(tuning);
                    changed = true;
                }
                if (!changed) return;
                Backup(path, output, changes);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("Could not serialize migrated tuning in " + path);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
            // Saving in the validation copy may normalize unrelated Unity YAML. Publish only
            // the tuning object's block, leaving all scene geometry/component bytes intact.
            string saved = File.ReadAllText(path);
            string patched = original;
            foreach (var pair in blocks)
            {
                ulong savedId = LocalTuningReference(Block(saved, owners[pair.Key]));
                string updated = Block(saved, savedId);
                if (!updated.Contains(TuningGuid)) throw new InvalidDataException("Saved inline object is not a vehicle tuning.");
                updated = Regex.Replace(updated, @"\A--- !u!114 &\d+", "--- !u!114 &" + pair.Key.ToString(CultureInfo.InvariantCulture));
                patched = patched.Replace(pair.Value, updated);
            }
            File.WriteAllText(path, patched, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        public static int CountScript(string text, string guid)
            => Regex.Matches(text, @"(?m)^  m_Script: \{fileID: 11500000, guid: " + Regex.Escape(guid) + @", type: 3\}").Count;

        private static string Block(string text, ulong id)
        {
            var match = Regex.Match(text, @"(?ms)^--- !u!114 &" + id.ToString(CultureInfo.InvariantCulture) + @"\r?\n.*?(?=^--- !u!|\z)");
            if (!match.Success) throw new InvalidDataException("Serialized inline tuning block not found: " + id);
            return match.Value;
        }
        private static ulong LocalTuningReference(string controllerBlock)
        {
            var reference = Regex.Match(controllerBlock, @"(?m)^  stockTuning: \{fileID: (\d+)\}\r?$");
            if (!reference.Success || !ulong.TryParse(reference.Groups[1].Value, out ulong id) || id == 0)
                throw new InvalidDataException("Expected a nonzero scene-local tuning reference.");
            return id;
        }
        private static void Backup(string path, string output, Dictionary<string, ChangedFile> changes)
        {
            if (changes.ContainsKey(path)) return;
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || path.Split('/').Contains("..")) throw new InvalidDataException("Invalid migration asset path.");
            string backup = Path.Combine("Before", path);
            string destination = Path.Combine(output, backup);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(path, destination, false);
            changes.Add(path, new ChangedFile { path = path, beforeSha256 = HashFile(path), backup = backup.Replace('\\', '/') });
        }
        public static string HashFile(string path)
        {
            using var hash = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
