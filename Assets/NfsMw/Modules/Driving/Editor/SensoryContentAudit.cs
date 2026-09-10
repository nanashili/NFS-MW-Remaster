#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>Read-only authoring diagnostics. Missing recordings are a content gate, not synthesized substitutes.</summary>
    public static class SensoryContentAudit
    {
        [MenuItem("NFS MW Remaster/Sensory/Audit Content")]
        public static void Run()
        {
            var report = new StringBuilder("SENSORY CONTENT AUDIT (read-only; default imports, review platform overrides separately)\n");
            var visited = new HashSet<AudioClip>(); int missing = 0, warnings = 0, invalid = 0;
            foreach (var profile in Profiles<VehicleSensoryProfile>())
            {
                if (!profile.Validate(out string failure)) { report.AppendLine(profile.name + ": INVALID " + failure); invalid++; continue; }
                foreach (var layer in profile.engineLayers) foreach (var region in layer.regions)
                {
                    Check(region.onLoad, layer.kind + " " + region.rpm + " on-load", true, false);
                    Check(region.offLoad, layer.kind + " " + region.rpm + " off-load", true, false);
                }
                foreach (var clip in new[] { profile.startup, profile.shiftUp, profile.shiftDown, profile.limiter, profile.overrun }) Check(clip, "vehicle transient", false, false);
                foreach (var clip in new[] { profile.nitrous, profile.wind, profile.trafficRoadNoise }) Check(clip, "vehicle loop", true, false);
            }
            foreach (var surface in Profiles<SensorySurfaceProfile>())
            {
                Check(surface.rolling, surface.name + " road", true, false); Check(surface.tireSlip, surface.name + " slip", true, false);
                Check(surface.scrape, surface.name + " scrape", true, false); Check(surface.impact, surface.name + " impact", false, false);
                Check(surface.destruction, surface.name + " break", false, false);
            }
            foreach (var pairs in Profiles<ImpactMaterialLibrary>())
            {
                if (!pairs.Validate(out string failure)) { report.AppendLine(pairs.name + ": INVALID " + failure); invalid++; continue; }
                foreach (var pair in pairs.pairs)
                { Check(pair.light, "pair light impact", false, false); Check(pair.heavy, "pair heavy impact", false, false); Check(pair.scrape, "pair scrape", true, false); }
            }
            foreach (var police in Profiles<PoliceSensoryProfile>())
            {
                if (police.sirens.Length == 0) Check(null, "siren set", true, false);
                foreach (var clip in police.sirens) Check(clip, "siren", true, false);
                foreach (var cue in police.radio)
                {
                    if (cue == null) { invalid++; report.AppendLine("INVALID null radio cue"); continue; }
                    if (cue.variants == null || cue.variants.Length == 0) Check(null, "radio " + cue.kind, false, false);
                    else foreach (var clip in cue.variants) Check(clip, "radio " + cue.kind, false, false);
                }
            }
            foreach (var music in Profiles<SensoryMusicProfile>())
            {
                if (!music.Validate(out string failure)) { report.AppendLine(music.name + ": INVALID " + failure); invalid++; continue; }
                foreach (var stem in music.stems) Check(stem.clip, "music stem", true, true);
                foreach (var clip in new[] { music.escapedStinger, music.arrestedStinger, music.finePaidStinger }) Check(clip, "outcome stinger", false, true);
            }
            report.AppendLine($"SUMMARY uniqueAssignedClips={visited.Count}, unassignedSlots={missing}, importWarnings={warnings}, invalidProfiles={invalid}");
            report.AppendLine("Unassigned optional materials/roles may be intentional. This report cannot verify loop seams, loudness, phase, intelligibility or device output.");
            Debug.Log(report.ToString());

            void Check(AudioClip clip, string role, bool loop, bool music)
            {
                if (clip == null) { missing++; report.AppendLine("UNASSIGNED " + role); return; }
                if (!visited.Add(clip)) return;
                string path = AssetDatabase.GetAssetPath(clip);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null) { warnings++; report.AppendLine("REVIEW non-imported clip " + path); return; }
                var settings = importer.defaultSampleSettings;
                report.AppendLine($"CLIP {path}: {clip.channels}ch {clip.frequency}Hz {clip.length:F3}s {settings.loadType}");
                if (!settings.preloadAudioData) { warnings++; report.AppendLine("REVIEW preload is off: the world requires Loaded data; load asynchronously before admission."); }
                if (loop && !music && settings.loadType == AudioClipLoadType.Streaming) { warnings++; report.AppendLine("REVIEW short-loop streaming cost; audition DecompressOnLoad within memory budget."); }
                if (!music && clip.channels > 1) { warnings++; report.AppendLine("REVIEW stereo positional clip; mono is preferred for vehicle and world emitters."); }
                if (music && clip.length > 30 && settings.loadType != AudioClipLoadType.Streaming) { warnings++; report.AppendLine("REVIEW long music memory usage; consider Streaming."); }
            }
        }
        private static IEnumerable<T> Profiles<T>() where T : UnityEngine.Object
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { "Assets/NfsMw/Modules/Driving" }))
            { var profile = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)); if (profile != null) yield return profile; }
        }
    }
}
#endif
