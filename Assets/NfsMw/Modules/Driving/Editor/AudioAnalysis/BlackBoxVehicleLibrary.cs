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
    /// <summary>Creates model-independent vehicle drafts and audio from the installed PC catalog.</summary>
    public static class BlackBoxVehicleLibrary
    {
        public const string DefaultRoot = "Assets/NfsMw/Content/Vehicles";
        [Serializable] public sealed class Vehicle
        {
            public string key, model, category, folder, draftPath, audioPath, status, reason, engine;
            public int sources, carId;
            public float idleRpm, maximumRpm;
        }
        [Serializable] public sealed class Report
        {
            public int schema = 2;
            public string gameFolder, outputRoot, generatedUtc;
            public bool cancelled;
            public List<Vehicle> vehicles = new List<Vehicle>();
            public string[] excluded = Array.Empty<string>();
            public int Ready => vehicles.Count(v => v.status == "Ready");
        }
        [MenuItem("Racing Tools/Audio/Import Most Wanted vehicle library")]
        public static void OpenImport()
        {
            string game = EditorUtility.OpenFolderPanel("Installed Most Wanted folder (GLOBAL, SOUND and CARS)", FindInstallation(), "");
            if (game.Length == 0) return;
            try
            {
                EditorPrefs.SetString("BlackBox.MostWantedInstallation", game);
                var report = Import(game, DefaultRoot);
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<TextAsset>(DefaultRoot + "/ImportReport.json");
                Debug.Log("Most Wanted library: " + report.Ready + "/" + report.vehicles.Count + " audio profiles ready at " + DefaultRoot + ".");
            }
            catch (Exception error) { Debug.LogException(error); }
        }
        internal static string FindInstallation()
        {
            string saved = EditorPrefs.GetString("BlackBox.MostWantedInstallation", "");
            if (Directory.Exists(Path.Combine(saved, "GLOBAL"))) return saved;
            string crossover = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library/Application Support/CrossOver/Bottles/NFS MW/drive_c/Program Files (x86)/NFS Most Wanted");
            return Directory.Exists(Path.Combine(crossover, "GLOBAL")) ? crossover : "";
        }
        /// <summary>Command-line entry point; the destination must be a task-owned project.</summary>
        public static void ImportBatch()
        {
            string game = Environment.GetEnvironmentVariable("BLACKBOX_GAME") ?? FindInstallation();
            string output = Environment.GetEnvironmentVariable("BLACKBOX_LIBRARY_OUTPUT") ?? DefaultRoot;
            var report = Import(game, output);
            Debug.Log("MOST_WANTED_LIBRARY ready=" + report.Ready + " vehicles=" + report.vehicles.Count + " root=" + output);
        }
        public static Report Import(string gameFolder, string outputRoot)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before importing the vehicle library.");
            var catalog = MostWantedVehicleCatalog.Scan(gameFolder);
            var context = MostWantedAudioSetup.CreateStockContext(gameFolder);
            var entries = catalog.Concrete.ToArray();
            var report = new Report { gameFolder = Path.GetFullPath(gameFolder), outputRoot = outputRoot, generatedUtc = DateTime.UtcNow.ToString("O"),
                excluded = catalog.Entries.Where(e => !entries.Contains(e)).Select(e => e.Key + ": " + e.Reason).ToArray() };
            BlackBoxCompleteAudioExport.EnsureFolder(outputRoot);
            try
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    var entry = entries[i];
                    if (!Application.isBatchMode && EditorUtility.DisplayCancelableProgressBar("Most Wanted vehicle library", entry.Key.ToUpperInvariant() + " (" + (i + 1) + "/" + entries.Length + ")", i / (float)entries.Length))
                    { report.cancelled = true; break; }
                    string folder = outputRoot + "/" + MostWantedCarFolders.ForModel(entry.Model);
                    var item = new Vehicle { key = entry.Key, model = entry.Model, category = entry.Kind.ToString(), folder = folder, status = "Needs source review", reason = entry.Reason };
                    report.vehicles.Add(item);
                    foreach (string child in new[] { "Textures", "HDRP Materials", "Editor", "Sound/Profiles", "Sound/Clips", "Sound/Analysis" })
                    {
                        BlackBoxCompleteAudioExport.EnsureFolder(folder + "/" + child);
                        if ((child == "Textures" || child == "HDRP Materials") && !Directory.EnumerateFileSystemEntries(folder + "/" + child).Any())
                            File.WriteAllText(folder + "/" + child + "/.gitkeep", "");
                    }
                    string draftPath = folder + "/Editor/" + entry.Key.ToUpperInvariant() + ".asset";
                    item.draftPath = draftPath;
                    var draft = AssetDatabase.LoadAssetAtPath<VehicleProfileDraft>(draftPath);
                    bool createdDraft = draft == null;
                    if (createdDraft)
                    {
                        draft = ScriptableObject.CreateInstance<VehicleProfileDraft>(); draft.name = entry.Key.ToUpperInvariant();
                        draft.tags = new[] { "Most Wanted", entry.Kind.ToString(), entry.Key };
                        draft.description = "Original Most Wanted vehicle " + entry.Key + "; source MODEL: " + entry.Model + ". Place the 3D model in the car folder beside Sound, with textures and materials in their own folders, then assemble and bind the vehicle prefab. Model identity and year remain to be authored.";
                        draft.provenance = "Generated from the installed PC CARS directory and resolved pvehicle/engineaudio database references. " + MostWantedAudioSetup.Evidence;
                        AssetDatabase.CreateAsset(draft, draftPath);
                    }
                    string audioFolder = folder + "/Sound/Profiles";
                    string profilePath = audioFolder + "/" + entry.Key.ToUpperInvariant() + ".asset";
                    bool createdAudio = false;
                    try
                    {
                        if (!entry.IsConcrete) throw new InvalidDataException(entry.Reason ?? "No resolved stock vehicle record.");
                        var setup = MostWantedAudioSetup.PrepareStock(gameFolder, entry.DatabaseKey, context);
                        if (setup.Unsupported.Count > 0) throw new InvalidDataException(string.Join("; ", setup.Unsupported));
                        string fingerprint = BlackBoxCompleteAttachment.SetupFingerprint(setup);
                        var profile = AssetDatabase.LoadAssetAtPath<VehicleSensoryProfile>(profilePath);
                        if (profile != null)
                        {
                            if (!profile.HasCompleteAudio || profile.mostWantedAudio.fingerprint != fingerprint || !profile.Validate(out _))
                                throw new InvalidDataException("The existing profile differs from this source setup; preserved for review.");
                        }
                        else
                        {
                            if (File.Exists(profilePath)) throw new IOException("An unreadable asset already exists at the profile path; preserved for review.");
                            createdAudio = true;
                            setup.VerifySources();
                            profile = BlackBoxCompleteAudioExport.Write(setup, audioFolder, null, fingerprint, folder + "/Sound/Clips",
                                entry.Key.ToUpperInvariant(), folder + "/Sound/Analysis/" + entry.Key.ToUpperInvariant() + "-sources.txt", true);
                            setup.VerifySources();
                        }
                        if (draft.audio == null) { draft.audio = profile; EditorUtility.SetDirty(draft); }
                        else if (draft.audio != profile) throw new InvalidDataException("The vehicle draft already uses another audio profile; preserved for review.");
                        item.audioPath = profilePath; item.status = "Ready"; item.reason = "Stock audio ready; assemble and bind a vehicle prefab when its model is available.";
                        item.engine = setup.Engine; item.carId = setup.CarId; item.idleRpm = setup.IdleRpm; item.maximumRpm = setup.MaximumRpm; item.sources = setup.Sources.Count;
                    }
                    catch (Exception error) when (error is InvalidDataException || error is InvalidOperationException || error is IOException || error is ArgumentException || error is OverflowException)
                    {
                        item.reason = error.Message;
                        if (createdAudio) AssetDatabase.DeleteAsset(profilePath);
                    }
                    finally { context.ReleaseVehicleSources(); }
                    AssetDatabase.SaveAssetIfDirty(draft);
                    Debug.Log("MW_LIBRARY " + item.key + " " + item.status + " " + item.reason);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                File.WriteAllText(outputRoot + "/ImportReport.json", JsonUtility.ToJson(report, true), new UTF8Encoding(false));
                AssetDatabase.SaveAssets(); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            return report;
        }
    }
}
