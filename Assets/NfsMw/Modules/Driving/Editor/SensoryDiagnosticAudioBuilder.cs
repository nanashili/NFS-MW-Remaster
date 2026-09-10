#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>Explicit, non-overwriting installation into the shared reference profiles, never at runtime.</summary>
    public static class SensoryDiagnosticAudioBuilder
    {
        public const string AudioFolder = "Assets/NfsMw/Modules/Driving/Audio/Diagnostic";
        private const string Provenance = "Original deterministic diagnostic synthesis v1; not NFS recordings.";
        private static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private static int created, assigned;

        [MenuItem("NFS MW Remaster/Sensory/Install Diagnostic Audio")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Install diagnostic audio in Edit mode.");
            var vehicle = Required<VehicleSensoryProfile>("ReferenceVehicle.asset");
            var police = Required<PoliceSensoryProfile>("PoliceFeedback.asset");
            var music = Required<SensoryMusicProfile>("AdaptiveMusic.asset");
            if (!vehicle.Validate(out var failure) || !music.Validate(out failure)) throw new InvalidOperationException(failure);
            double duration = 60.0 / music.bpm * music.beatsPerBar * music.bars;
            if (duration > 60) throw new InvalidOperationException("Diagnostic music supports loops up to 60 seconds; use a separate short test profile.");
            clips.Clear(); created = assigned = 0;
            Directory.CreateDirectory(AudioFolder);
            Undo.RecordObject(vehicle, "Install diagnostic vehicle sounds");
            foreach (var layer in vehicle.engineLayers)
                foreach (var region in layer.regions)
                {
                    string key = "engine-" + layer.kind + "-" + region.rpm.ToString("0.###", CultureInfo.InvariantCulture);
                    Fill(ref region.onLoad, key + "-on", () => DiagnosticAudioSynthesis.Engine(layer.kind, region.rpm, true));
                    Fill(ref region.offLoad, key + "-off", () => DiagnosticAudioSynthesis.Engine(layer.kind, region.rpm, false));
                }
            Fill(ref vehicle.startup, "startup", () => DiagnosticAudioSynthesis.Vehicle("startup"));
            Fill(ref vehicle.shiftUp, "shift-up", () => DiagnosticAudioSynthesis.Vehicle("shift-up"));
            Fill(ref vehicle.shiftDown, "shift-down", () => DiagnosticAudioSynthesis.Vehicle("shift-down"));
            Fill(ref vehicle.limiter, "limiter", () => DiagnosticAudioSynthesis.Vehicle("limiter"));
            Fill(ref vehicle.overrun, "overrun", () => DiagnosticAudioSynthesis.Vehicle("overrun"));
            Fill(ref vehicle.nitrous, "nitrous", () => DiagnosticAudioSynthesis.Vehicle("nitrous"));
            Fill(ref vehicle.wind, "wind", () => DiagnosticAudioSynthesis.Vehicle("wind"));
            Fill(ref vehicle.trafficRoadNoise, "traffic", () => DiagnosticAudioSynthesis.Vehicle("traffic"));
            EditorUtility.SetDirty(vehicle);
            foreach (var surface in vehicle.surfaces)
            {
                if (surface == null) continue;
                Undo.RecordObject(surface, "Install diagnostic surface sounds");
                FillSurface(ref surface.rolling, surface.surface, "rolling"); FillSurface(ref surface.tireSlip, surface.surface, "slip");
                FillSurface(ref surface.impact, surface.surface, "impact"); FillSurface(ref surface.scrape, surface.surface, "scrape");
                FillSurface(ref surface.destruction, surface.surface, "destruction"); EditorUtility.SetDirty(surface);
            }
            if (vehicle.impactMaterials != null)
            {
                Undo.RecordObject(vehicle.impactMaterials, "Install diagnostic collision sounds");
                foreach (var pair in vehicle.impactMaterials.pairs)
                {
                    if (pair == null) continue;
                    var material = pair.b == SensorySurface.Metal ? pair.a : pair.b;
                    FillSurface(ref pair.light, material, "impact"); FillSurface(ref pair.heavy, material, "heavy");
                    FillSurface(ref pair.scrape, material, "scrape");
                }
                EditorUtility.SetDirty(vehicle.impactMaterials);
            }
            Undo.RecordObject(police, "Install diagnostic police sounds");
            if (police.sirens == null || police.sirens.Length == 0) police.sirens = new AudioClip[3];
            for (int i = 0; i < police.sirens.Length; i++)
            { int variant = i % 3; Fill(ref police.sirens[i], "siren-" + variant, () => DiagnosticAudioSynthesis.Siren(variant)); }
            foreach (var cue in police.radio)
            {
                if (cue == null) continue;
                if (cue.variants == null || cue.variants.Length == 0) cue.variants = new AudioClip[1];
                for (int i = 0; i < cue.variants.Length; i++)
                    Fill(ref cue.variants[i], "radio-tone-" + cue.kind, () => DiagnosticAudioSynthesis.Radio((int)cue.kind));
            }
            EditorUtility.SetDirty(police);
            Undo.RecordObject(music, "Install diagnostic music sounds");
            for (int i = 0; i < music.stems.Length; i++)
            {
                int stem = i;
                string key = "music-" + i + "-" + music.bpm.ToString("0.###", CultureInfo.InvariantCulture) + "bpm-" + music.beatsPerBar + "x" + music.bars;
                Fill(ref music.stems[i].clip, key, () => DiagnosticAudioSynthesis.Music(stem, music.bpm, music.beatsPerBar, music.bars));
            }
            Fill(ref music.escapedStinger, "outcome-escaped", () => DiagnosticAudioSynthesis.Stinger(0));
            Fill(ref music.arrestedStinger, "outcome-arrested", () => DiagnosticAudioSynthesis.Stinger(1));
            Fill(ref music.finePaidStinger, "outcome-fine-paid", () => DiagnosticAudioSynthesis.Stinger(2));
            EditorUtility.SetDirty(music);
            AssetDatabase.SaveAssets();
            if (!music.Validate(out failure)) throw new InvalidOperationException(failure);
            Debug.Log($"DIAGNOSTIC_AUDIO_INSTALLED created={created} assignedSlots={assigned}. Original test synthesis; no NFS recordings. Shared profiles update all installed scenes.");
            clips.Clear();
        }

        [MenuItem("NFS MW Remaster/Sensory/Install Diagnostic Audio", true)]
        private static bool CanInstall() => !EditorApplication.isPlayingOrWillChangePlaymode;

        private static T Required<T>(string name) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(DrivingDemoBuilder.SensoryFolder + "/" + name);
            if (asset == null) throw new InvalidOperationException("Missing " + name + ". Build the Sensory Test Scene first.");
            return asset;
        }
        private static void FillSurface(ref AudioClip slot, SensorySurface material, string role)
            => Fill(ref slot, "surface-" + material + "-" + role, () => DiagnosticAudioSynthesis.Surface(material, role));

        private static void Fill(ref AudioClip slot, string name, Func<float[]> synthesize)
        {
            if (slot != null) return;
            if (!clips.TryGetValue(name, out var clip))
            {
                string path = AudioFolder + "/" + name + ".wav";
                bool fresh = !File.Exists(path);
                if (fresh)
                {
                    var pcm = synthesize();
                    using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) DiagnosticAudioSynthesis.WriteWave(file, pcm);
                    created++;
                }
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                if (fresh)
                {
                    var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                    var settings = importer.defaultSampleSettings;
                    settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.compressionFormat = AudioCompressionFormat.PCM;
                    settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                    settings.preloadAudioData = true;
                    importer.defaultSampleSettings = settings; importer.loadInBackground = false;
                    importer.userData = Provenance; importer.SaveAndReimport();
                }
                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) throw new InvalidDataException("Could not import diagnostic WAV: " + path);
                clips.Add(name, clip);
            }
            slot = clip; assigned++;
        }
    }
}
#endif
