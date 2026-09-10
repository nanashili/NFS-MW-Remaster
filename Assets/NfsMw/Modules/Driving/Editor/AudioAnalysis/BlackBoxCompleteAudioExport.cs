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
    /// <summary>Exports audio independently of a model or scene. Shared banks retain their original sample identities.</summary>
    internal static class BlackBoxCompleteAudioExport
    {
        public static VehicleSensoryProfile Write(MostWantedAudioSetup setup, string folder, VehicleSensoryProfile template,
            string fingerprint, string sharedRoot = null, string profileName = "VehicleAudio", string sourceNotesPath = null, bool namedSourceFolders = false)
        {
            if (!AssetDatabase.IsValidFolder(folder)) throw new IOException("Create the vehicle audio output folder first.");
            if (Path.GetFileName(profileName) != profileName || string.IsNullOrWhiteSpace(profileName)) throw new ArgumentException("Use a profile filename without directories.");
            if (File.Exists(folder + "/" + profileName + ".asset")) throw new IOException("An audio profile already exists at this destination.");
            foreach (var source in setup.Sources)
            {
                if (source.Programs.Length == 0) BlackBoxCompleteAttachment.Gin(source, null);
                foreach (var program in source.Programs) new AemsEvaluator(program).Step(25);
            }
            var content = new MostWantedVehicleAudio { schema = 1, carId = setup.CarId, idleRpm = setup.IdleRpm,
                maximumRpm = setup.MaximumRpm, masterGain = setup.MasterGain, evidence = MostWantedAudioSetup.Evidence, fingerprint = fingerprint };
            var surfaceIds = new[] { SensorySurface.AsphaltDry, SensorySurface.AsphaltWet, SensorySurface.Concrete, SensorySurface.Gravel, SensorySurface.Dirt,
                SensorySurface.Grass, SensorySurface.Metal, SensorySurface.Glass, SensorySurface.Water, SensorySurface.Wood, SensorySurface.Plastic, SensorySurface.Air, SensorySurface.Unknown };
            if (setup.Surfaces.Count != surfaceIds.Length) throw new InvalidDataException("Incomplete recovered surface assignments.");
            content.surfaces = setup.Surfaces.Select((s, i) => new MostWantedSurfaceAudio { surface = surfaceIds[i], roadLoop = s.Loop, enter = s.Enter, exit = s.Exit, skid = s.Skid }).ToArray();
            var banks = new Dictionary<string, AemsAudioBank>();
            foreach (var source in setup.Sources)
            {
                string sourceName = Path.GetFileNameWithoutExtension(source.Path);
                string destination = sharedRoot == null ? folder : sharedRoot + "/" + (namedSourceFolders ? sourceName + "-" + source.Hash.Substring(0, 12) : source.Hash);
                EnsureFolder(destination);
                string bankPath = destination + "/" + (sharedRoot == null ? source.Role : "Bank") + ".asset";
                var cached = source.Programs.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<AemsAudioBank>(bankPath);
                if (cached != null)
                {
                    if (cached.sourceHash != source.Hash || cached.recordings.Length != source.Report.Recordings.Count)
                        throw new InvalidDataException("The shared bank does not match its source: " + bankPath);
                    banks.Add(source.Role, cached); continue;
                }
                var recordings = new List<AemsRecording>();
                foreach (var recording in source.Report.Recordings)
                {
                    string wave = destination + "/" + (sharedRoot == null ? source.Role + "-" : namedSourceFolders ? sourceName + "-" : "") + recording.Id.Replace(':', '-') + ".wav";
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(wave);
                    if (clip == null)
                    {
                        using (var stream = new FileStream(wave, FileMode.CreateNew, FileAccess.Write))
                            BlackBoxWaveExport.Write(stream, recording.Pcm, recording.SampleRate, recording.Channels, 0, recording.ValidFrames);
                        AssetDatabase.ImportAsset(wave, ImportAssetOptions.ForceSynchronousImport);
                        var importer = (AudioImporter)AssetImporter.GetAtPath(wave);
                        var settings = importer.defaultSampleSettings; settings.loadType = AudioClipLoadType.DecompressOnLoad;
                        settings.compressionFormat = AudioCompressionFormat.PCM; settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                        settings.preloadAudioData = true;
                        importer.defaultSampleSettings = settings; importer.forceToMono = false; importer.loadInBackground = false; importer.SaveAndReimport();
                        clip = AssetDatabase.LoadAssetAtPath<AudioClip>(wave); clip.LoadAudioData();
                    }
                    if (clip == null || clip.samples != recording.ValidFrames || clip.channels != recording.Channels || clip.frequency != recording.SampleRate)
                        throw new InvalidDataException("Unity changed decoded PCM dimensions: " + wave);
                    if (source.Programs.Length == 0)
                    {
                        if (source.Role == "acceleration") content.acceleration = BlackBoxCompleteAttachment.Gin(source, clip);
                        else if (source.Role == "deceleration") content.deceleration = BlackBoxCompleteAttachment.Gin(source, clip);
                        else throw new InvalidDataException("Unknown GIN assignment: " + source.Role);
                    }
                    else recordings.Add(new AemsRecording { sampleId = BlackBoxCompleteAttachment.SampleId(recording.Id), clip = clip, loopStart = recording.LoopStart, loopEnd = recording.LoopEnd });
                }
                if (source.Programs.Length == 0) continue;
                var bank = ScriptableObject.CreateInstance<AemsAudioBank>(); bank.name = Path.GetFileNameWithoutExtension(source.Path);
                bank.programs = source.Programs; bank.sourceHash = source.Hash; bank.evidence = source.Path + "\n" + MostWantedAudioSetup.Evidence; bank.recordings = recordings.ToArray();
                AssetDatabase.CreateAsset(bank, bankPath); banks.Add(source.Role, bank);
            }
            AemsAudioBank Bank(string role) => banks.TryGetValue(role, out var value) ? value : null;
            content.engine = Bank("engine"); content.sweeteners = Bank("sweeteners"); content.transmission = Bank("transmission"); content.whine = Bank("whine");
            content.shifts = Bank("shifts"); content.skids = Bank("skids"); content.road = Bank("road"); content.wind = Bank("wind"); content.nitrous = Bank("nitrous");
            var profile = template != null ? UnityEngine.Object.Instantiate(template) : ScriptableObject.CreateInstance<VehicleSensoryProfile>();
            try
            {
                profile.name = setup.Vehicle + " · Black Box audio"; profile.mostWantedAudio = content; profile.engineLayers = Array.Empty<EngineSoundLayer>();
                profile.vehicleIdentity = setup.Vehicle; profile.engineAudioId = fingerprint; profile.engineAudioRevision++;
                if (!profile.Validate(out string failure)) throw new InvalidDataException(failure);
                AssetDatabase.CreateAsset(profile, folder + "/" + profileName + ".asset");
            }
            catch { if (!AssetDatabase.Contains(profile)) UnityEngine.Object.DestroyImmediate(profile); throw; }
            string notes = sourceNotesPath ?? folder + "/Sources.txt";
            File.WriteAllText(notes, "Vehicle: " + setup.Vehicle + "\nEngine: " + setup.Engine + " <- " + setup.Parent + "\n" + MostWantedAudioSetup.Evidence + "\n\n"
                + string.Join("\n", setup.Sources.Select(s => s.Role + "\t" + s.Path + "\t" + s.Hash))
                + "\n\nOrdered script fields:\n" + string.Join("\n", setup.Overrides.OrderBy(e => e.Key).Select(e => e.Key + "=" + e.Value))
                + "\n\nInput hashes:\n" + string.Join("\n", setup.Inputs.Select(e => e.Value + "\t" + e.Key)), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(notes, ImportAssetOptions.ForceSynchronousImport);
            return profile;
        }
        internal static void EnsureFolder(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || path.Split('/').Any(p => p.Length == 0 || p == "." || p == "..") || path.Contains('\\') || path.Contains(':'))
                throw new ArgumentException("Choose a project folder under Assets.");
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (parent != "Assets") EnsureFolder(parent);
            if (AssetDatabase.CreateFolder(parent, Path.GetFileName(path)).Length == 0) throw new IOException("Could not create " + path);
        }
    }
}
