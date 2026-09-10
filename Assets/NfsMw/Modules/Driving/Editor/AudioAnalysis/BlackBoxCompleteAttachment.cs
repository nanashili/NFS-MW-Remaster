using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NfsMwRemaster.Driving.AudioAnalysis;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    public sealed class BlackBoxCompleteAttachment
    {
        public MostWantedAudioSetup Setup { get; private set; }
        public VehicleAudio Target { get; private set; }
        public VehicleSensoryProfile Original { get; private set; }
        public string Fingerprint { get; private set; }
        public static BlackBoxCompleteAttachment Prepare(string pack, string game, VehicleAudio target)
        {
            BlackBoxVehicleAttachment.ValidateTarget(target);
            var setup = MostWantedAudioSetup.Prepare(pack, game);
            foreach (var source in setup.Sources)
            {
                if (source.Programs.Length == 0) Gin(source, null);
                foreach (var program in source.Programs)
                {
                    if (program.players.Length == 0) continue;
                    // A constructor evaluation catches unsupported graph nodes before any project writes.
                    var graph = new AemsEvaluator(program); graph.Step(25);
                }
            }
            return new BlackBoxCompleteAttachment { Setup = setup, Target = target, Original = target.Profile, Fingerprint = SetupFingerprint(setup) };
        }
        internal static string SetupFingerprint(MostWantedAudioSetup setup) => BlackBoxSourceAccess.Hash(Encoding.UTF8.GetBytes("mw-unity-setup/3\n" + setup.Vehicle + "\n" + setup.Engine + "\n"
            + string.Join("\n", setup.Inputs.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => e.Key + "\t" + e.Value))));
        public VehicleSensoryProfile Apply()
        {
            BlackBoxVehicleAttachment.ValidateTarget(Target);
            if (Target.Profile != Original) throw new InvalidOperationException("The target profile changed. Prepare the complete setup again.");
            Setup.VerifySources();
            if (Original != null && Original.HasCompleteAudio && Original.mostWantedAudio.fingerprint == Fingerprint && Original.Validate(out _)) return Original;
            string parent = "Assets/NfsMw/Content/Audio";
            if (!AssetDatabase.IsValidFolder(parent)) AssetDatabase.CreateFolder("Assets", "Audio");
            string folder = parent + "/" + Setup.Vehicle + "-BlackBox-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            if (AssetDatabase.CreateFolder(parent, Path.GetFileName(folder)).Length == 0) throw new IOException("Could not create the complete audio output folder.");
            bool bound = false;
            try
            {
                var profile = BlackBoxCompleteAudioExport.Write(Setup, folder, Original, Fingerprint);
                AssetDatabase.SaveAssets(); Setup.VerifySources();
                BlackBoxVehicleAttachment.Bind(Target, profile); bound = true;
                return profile;
            }
            finally { if (!bound) AssetDatabase.DeleteAsset(folder); }
        }
        public static int SampleId(string id)
        {
            int colon = id.IndexOf(':');
            if (colon < 0 || !int.TryParse(id.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 0) throw new InvalidDataException("Invalid decoded sample identity.");
            if (id.StartsWith("s10a:", StringComparison.Ordinal)) return checked(value + 1);
            if (id.StartsWith("bnk:", StringComparison.Ordinal) && value > 0) return value;
            throw new InvalidDataException("Unsupported decoded sample identity: " + id);
        }
        public static EngineAudioRegion Gin(MostWantedAudioSetup.Source source, AudioClip clip)
        {
            if (source.Report.Recordings.Count != 1) throw new InvalidDataException("Expected one GIN recording.");
            var recording = source.Report.Recordings[0]; var table = source.Report.Tables.Single(t => t.Name == "table_a");
            float Endpoint(string name) => float.Parse(source.Report.Fields.Single(f => f.Name == name).Value, CultureInfo.InvariantCulture);
            float first = Endpoint("candidate_endpoint_0"), last = Endpoint("candidate_endpoint_1");
            if (table.Entries.Count < 2 || table.Entries.Count > 16384 || first <= 0 || last <= 0 || first == last) throw new InvalidDataException("Invalid GIN frequency table.");
            var anchors = table.Entries.Select((entry, i) => new EngineRpmAnchor(Mathf.Lerp(first, last, i / (float)(table.Entries.Count - 1)), checked((int)Math.Min((uint)(recording.ValidFrames - 1), entry.RawValue)))).ToArray();
            if (!EngineAudioCompiler.TryCompile(recording.Pcm, 0, recording.ValidFrames, recording.SampleRate, recording.Channels, anchors, out var region, source.Role))
                throw new InvalidDataException("The recovered GIN frequency table is invalid.");
            region.sourceClip = clip; region.sourceHash = source.Hash; region.recordingId = recording.Id;
            region.provenance = "Recovered GIN frequency-to-sample table";
            region.evidence = "GinsuDataFrequencyToSample: uniform frequency segments into preserved table A sample coordinates, including descending mappings. Unity grain rendering and load blend are adapted.";
            if (clip != null) region.compiledPcm = Array.Empty<float>();
            return region;
        }
    }
}
