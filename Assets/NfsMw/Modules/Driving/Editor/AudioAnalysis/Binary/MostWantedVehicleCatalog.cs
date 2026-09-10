using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NfsMwRemaster.Driving.AudioAnalysis
{
    /// <summary>Read-only stock pvehicle inventory, including variants with an explicit installed MODEL binding.</summary>
    public sealed class MostWantedVehicleCatalog
    {
        public enum VehicleKind { Player, Police, Traffic, Challenge, Aircraft, SharedPart, TrailerOrProp, Unknown }
        public sealed class Entry
        {
            public string Key, Directory, DatabaseKey, Model, Reason;
            public VehicleKind Kind;
            public bool HasDirectory, HasPVehicle, IsConcrete, IsTemplate, IsAlias;
            public uint EngineAudioKey;
        }
        // Stock names hash-matched against pvehicle rows. This supplies labels only;
        // installed records, inheritance and MODEL references determine admission.
        private static readonly string[] VariantAndTemplateNames = {
            "semilog", "truck", "tractors", "semicon", "cs_mustang_copsuv", "copsuvpatrol", "cs_semi", "m3gtre46careerstart", "speedtest",
            "cs_trafcement", "copmidsize_nis_ld", "cs_gto_copgto", "challenge_traffic", "cs_viper_copmidsize", "cs_cts_traf_minivan",
            "cs_cts_traffictruck", "cs_clio_traftaxi", "wrx_demo", "street", "cs_c6_copsporthench", "copmidsize_nis", "racers", "cs_trafgarb",
            "cops", "semib", "choppers", "semicmt", "cs_clio_trafpizza", "trailers", "van", "copcross", "semia", "traffic", "cars", "semicrate", "default", "mustang_demo"
        };
        public string GameFolder { get; private set; }
        public MostWantedAudioDatabase Database { get; private set; }
        public readonly List<Entry> Entries = new List<Entry>();
        public IEnumerable<Entry> Concrete => Entries.Where(e => e.IsConcrete);
        public IEnumerable<Entry> Unsupported => Entries.Where(e => !e.IsConcrete);
        public int ConcreteCount => Concrete.Count();

        public static MostWantedVehicleCatalog Scan(string gameFolder)
        {
            if (string.IsNullOrWhiteSpace(gameFolder)) throw new ArgumentException("Game folder is required.", nameof(gameFolder));
            var result = new MostWantedVehicleCatalog { GameFolder = Path.GetFullPath(gameFolder), Database = new MostWantedAudioDatabase() };
            foreach (string name in new[] { "attributes.bin", "FE_ATTRIB.bin", "gameplay.bin" })
                result.Database.Load(File.ReadAllBytes(MostWantedAudioSetup.ResolveFile(result.GameFolder, "GLOBAL/" + name)));
            string cars = MostWantedAudioSetup.ResolveFile(result.GameFolder, "CARS");
            var directories = Directory.GetDirectories(cars).ToDictionary(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
            var names = VariantAndTemplateNames.Concat(directories.Keys.Select(n => n.ToLowerInvariant())).Distinct()
                .ToDictionary(MostWantedAudioDatabase.Hash);
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var direct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in result.Database.RowsOf("pvehicle"))
            {
                bool named = names.TryGetValue(row.Key, out string key);
                key = key ?? "0x" + row.Key.ToString("X8");
                string model = ModelName(result.Database, row);
                bool bound = !string.IsNullOrEmpty(model) && directories.TryGetValue(model, out _);
                if (bound) referenced.Add(model);
                if (directories.ContainsKey(key)) direct.Add(key);
                uint engine = EngineAudioKey(result.Database, row);
                var kind = Classify(key, model);
                var entry = new Entry { Key = key, DatabaseKey = named ? key : null, Model = model, HasPVehicle = true,
                    HasDirectory = bound, Directory = bound ? directories[model] : null, EngineAudioKey = engine, Kind = kind,
                    IsTemplate = string.IsNullOrEmpty(model), IsAlias = bound && !key.Equals(model, StringComparison.OrdinalIgnoreCase),
                    IsConcrete = named && bound && engine != 0 && kind != VehicleKind.Aircraft && kind != VehicleKind.TrailerOrProp };
                entry.Reason = entry.IsConcrete ? null : !named ? "Unresolved pvehicle name; hash retained for source review."
                    : entry.IsTemplate ? "No MODEL binding; base, template or incomplete record."
                    : !bound ? "The database MODEL has no installed CARS directory."
                    : kind == VehicleKind.Aircraft ? "Aircraft; ground-vehicle engine audio does not apply."
                    : kind == VehicleKind.TrailerOrProp ? "Trailer or prop; no independent engine audio."
                    : "No engineaudio collection.";
                result.Entries.Add(entry);
            }
            foreach (var directory in directories.Where(d => !direct.Contains(d.Key)))
            {
                bool alias = referenced.Contains(directory.Key);
                result.Entries.Add(new Entry { Key = directory.Key.ToLowerInvariant(), Directory = directory.Value, HasDirectory = true,
                    Model = directory.Key, Kind = Classify(directory.Key, directory.Key), IsAlias = alias,
                    Reason = alias ? "Model directory used by a separately named pvehicle; its draft uses the database vehicle ID."
                    : "No pvehicle or MODEL reference; unused asset, shared part or unresolved source directory." });
            }
            result.Entries.Sort((a, b) => StringComparer.Ordinal.Compare(a.Key, b.Key));
            return result;
        }
        private static string ModelName(MostWantedAudioDatabase db, MostWantedAudioDatabase.Row row)
        {
            var values = db.Resolve(row);
            return values.TryGetValue(MostWantedAudioDatabase.Hash("MODEL"), out var value) ? value.Text() : null;
        }
        private static uint EngineAudioKey(MostWantedAudioDatabase db, MostWantedAudioDatabase.Row row)
        {
            if (!db.Resolve(row).TryGetValue(MostWantedAudioDatabase.Hash("engineaudio"), out var value)) return 0;
            var items = value.Items(); return items.Length == 0 ? 0 : items[0].CollectionKey();
        }
        private static VehicleKind Classify(string key, string model)
        {
            string upper = (model ?? key).ToUpperInvariant();
            if (upper == "COPHELI") return VehicleKind.Aircraft;
            if (upper.StartsWith("TRAILER", StringComparison.Ordinal) || upper.StartsWith("SPOILER", StringComparison.Ordinal) || upper == "BRAKES" || upper == "WHEELS" || upper == "ROOF" || upper == "PLATES") return VehicleKind.TrailerOrProp;
            if (key.StartsWith("cs_", StringComparison.Ordinal)) return VehicleKind.Challenge;
            if (upper.StartsWith("COP", StringComparison.Ordinal) || upper.StartsWith("POLICE", StringComparison.Ordinal)) return VehicleKind.Police;
            if (upper.StartsWith("TRAF", StringComparison.Ordinal) || upper == "TAXI" || upper == "SEMI" || upper == "MINIVAN" || upper == "PICKUPA" || upper == "PIZZA" || upper == "CEMTR" || upper == "GARB") return VehicleKind.Traffic;
            return VehicleKind.Player;
        }
    }
}
