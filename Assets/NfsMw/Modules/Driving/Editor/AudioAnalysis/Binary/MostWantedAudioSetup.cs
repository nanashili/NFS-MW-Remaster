using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace NfsMwRemaster.Driving.AudioAnalysis
{
    /// <summary>Resolves a base-upgrade PC vehicle plus an ordered, data-only engineaudio mod script.</summary>
    public sealed class MostWantedAudioSetup
    {
        public sealed class Source
        {
            public string Role, Path, Hash;
            public AnalysisReport Report;
            public AemsProgram[] Programs;
        }
        public string Vehicle, Engine, Parent, Script;
        public int CarId;
        public float IdleRpm, MaximumRpm, MasterGain;
        public readonly List<Source> Sources = new List<Source>();
        public readonly Dictionary<string, string> Inputs = new Dictionary<string, string>();
        public readonly Dictionary<string, string> Overrides = new Dictionary<string, string>();
        public readonly List<string> Unsupported = new List<string>();
        public sealed class Surface { public string Name; public int Loop, Enter, Exit, Skid; }
        public readonly List<Surface> Surfaces = new List<Surface>();
        public const string Evidence = "PC database inheritance and ordered script fields; recovered AEMS sample selection, sustain loops and surface references. Unity adapts telemetry, volume, GIN blending and spatialization. Base sound upgrades. Original-game time-stretch/filter DSP, random sequence and sputter mixer callbacks are not reproduced.";

        /// <summary>Shared read-only database and bounded decoded-source cache for a stock vehicle batch.</summary>
        public sealed class MostWantedAudioImportContext
        {
            internal readonly MostWantedAudioDatabase Database = new MostWantedAudioDatabase();
            internal readonly Dictionary<string, Source> Sources = new Dictionary<string, Source>(StringComparer.OrdinalIgnoreCase);
            internal readonly Dictionary<string, string> Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            internal long DecodedPcmBytes;
            public readonly string GameFolder;
            public MostWantedAudioImportContext(string gameFolder)
            {
                GameFolder = Path.GetFullPath(gameFolder ?? throw new ArgumentNullException(nameof(gameFolder)));
                foreach (string name in new[] { "attributes.bin", "FE_ATTRIB.bin", "gameplay.bin" })
                {
                    string path = ResolveFile(GameFolder, "GLOBAL/" + name); byte[] bytes = File.ReadAllBytes(path); Database.Load(bytes); Inputs[path] = Digest(bytes);
                }
            }
            internal Source Load(string path, string role, string expectedInterface)
            {
                string cacheKey = path + "|" + (expectedInterface ?? "");
                if (Sources.TryGetValue(cacheKey, out var cached)) return new Source { Role = role, Path = cached.Path, Hash = cached.Hash, Report = cached.Report, Programs = cached.Programs };
                if (new FileInfo(path).Length > 32L * 1024 * 1024) throw new InvalidDataException("Source exceeds the 32 MiB file budget.");
                byte[] bytes = File.ReadAllBytes(path); var report = new BinaryAnalysisParser().Parse(bytes, path);
                if (expectedInterface != null && report.Gaps.Any(g => g.Code.StartsWith("bnk-", StringComparison.Ordinal) || g.Code.StartsWith("s10a-", StringComparison.Ordinal)))
                    throw new InvalidDataException("The stock setup requires every bank recording to decode: " + path);
                if (report.Recordings.Count == 0 || report.Recordings.Any(r => !r.Decoded || r.Pcm == null || r.ValidFrames < 1 || r.LoopStart < 0 || r.LoopEnd > r.ValidFrames || r.LoopEnd > 0 && r.LoopEnd <= r.LoopStart))
                    throw new InvalidDataException("Not every recording can be decoded with valid loop bounds: " + path);
                var programs = expectedInterface == null ? Array.Empty<AemsProgram>() : BlackBoxAemsCompiler.Compile(bytes);
                if (expectedInterface != null && !programs.Any(p => p.interfaceName == expectedInterface)) throw new InvalidDataException(path + " does not contain " + expectedInterface);
                var source = new Source { Role = role, Path = path, Hash = Digest(bytes), Report = report, Programs = programs };
                long decodedBytes = report.Recordings.Sum(r => (long)r.Pcm.Length * 4);
                if (DecodedPcmBytes + decodedBytes > 256L * 1024 * 1024) throw new InvalidDataException("Stock audio import exceeds its 256 MiB decoded PCM budget.");
                DecodedPcmBytes += decodedBytes;
                Sources.Add(cacheKey, source); return new Source { Role = role, Path = source.Path, Hash = source.Hash, Report = source.Report, Programs = source.Programs };
            }
            /// <summary>Drop per-vehicle decoded PCM after export while retaining shared world banks.</summary>
            public void ReleaseVehicleSources()
            {
                var remove = Sources.Where(pair => IsVehicleRole(pair.Value.Role)).ToArray();
                foreach (var pair in remove)
                {
                    DecodedPcmBytes -= pair.Value.Report.Recordings.Sum(r => (long)r.Pcm.Length * 4);
                    Sources.Remove(pair.Key);
                }
            }
            private static bool IsVehicleRole(string role) => role == "acceleration" || role == "deceleration" || role == "engine" || role == "sweeteners" || role == "whine" || role == "shifts";
        }

        public static MostWantedAudioImportContext CreateStockContext(string gameFolder) => new MostWantedAudioImportContext(gameFolder);

        /// <summary>Prepare one stock pvehicle by stable CARS identifier; global banks are shared by context.</summary>
        public static MostWantedAudioSetup PrepareStock(string gameFolder, string vehicleKey, MostWantedAudioImportContext context = null)
        {
            if (string.IsNullOrWhiteSpace(vehicleKey)) throw new ArgumentException("Vehicle key is required.", nameof(vehicleKey));
            context = context ?? new MostWantedAudioImportContext(gameFolder);
            if (!string.Equals(context.GameFolder, Path.GetFullPath(gameFolder), StringComparison.Ordinal))
                throw new ArgumentException("The stock audio context belongs to a different game folder.", nameof(context));
            string stableKey = vehicleKey.ToLowerInvariant();
            if (!context.Database.TryFind("pvehicle", stableKey, out var vehicleRow)) throw new InvalidDataException("Missing stock pvehicle record: " + vehicleKey);
            var result = new MostWantedAudioSetup { Vehicle = stableKey, Script = null };
            var db = context.Database; var car = db.Resolve(vehicleRow);
            ValueText(car, "engineaudio", out var engineValue);
            if (engineValue == null) { result.Unsupported.Add("pvehicle has no engineaudio collection"); return result; }
            uint engineKey = engineValue.Items().First().CollectionKey(); var engineRow = db.Find(MostWantedAudioDatabase.Hash("engineaudio"), engineKey);
            result.Engine = "0x" + engineKey.ToString("X8"); result.Parent = engineRow.Parent == 0 ? null : "0x" + engineRow.Parent.ToString("X8");
            var fields = db.Resolve(engineRow);
            string Text(string key) => fields.TryGetValue(MostWantedAudioDatabase.Hash(key), out var value) ? value.Text() : null;
            float Number(string key, float fallback) => fields.TryGetValue(MostWantedAudioDatabase.Hash(key), out var value) ? value.Number() : fallback;
            result.CarId = checked((int)Number("CarID", 0)); result.IdleRpm = Number("MinRPM", 0); result.MaximumRpm = Number("MaxRPM", 0); result.MasterGain = Math.Max(0, Math.Min(1, Number("Master_Vol", 32767) / 32767));
            if (float.IsNaN(result.IdleRpm) || float.IsInfinity(result.IdleRpm) || result.IdleRpm <= 0 || result.MaximumRpm <= result.IdleRpm
                || float.IsNaN(result.MaximumRpm) || float.IsInfinity(result.MaximumRpm) || float.IsNaN(result.MasterGain))
                throw new InvalidDataException("Invalid stock engine RPM range or master volume.");
            void Add(string role, string directory, string filename, string iface, bool required)
            {
                if (string.IsNullOrEmpty(filename)) { if (required) result.Unsupported.Add(role + ": missing database filename"); return; }
                string path = ResolveFile(context.GameFolder, "SOUND/" + directory + "/" + filename, false);
                if (path == null) { result.Unsupported.Add(role + ": missing source " + filename); return; }
                try { var source = context.Load(path, role, iface); result.Sources.Add(source); result.Inputs[path] = source.Hash; } catch (InvalidDataException ex) { result.Unsupported.Add(role + ": " + ex.Message); }
            }
            Add("acceleration", "ENGINE", Text("Filename_GinsuAccel"), null, false); Add("deceleration", "ENGINE", Text("Filename_GinsuDecel"), null, false); Add("engine", "ENGINE", Text("BankName_mainRAM"), "CAR", true);
            if (fields.TryGetValue(MostWantedAudioDatabase.Hash("SweetBank"), out var sweets)) { var items = sweets.Items(); if (items.Length > 0) Add("sweeteners", "ENGINE", items[0].Text(), "CAR_SWTN", false); if (items.Length > 1) Add("whine", "ENGINE", items[1].Text(), "CAR_WHINE", false); }
            if (car.TryGetValue(MostWantedAudioDatabase.Hash("ShiftSND"), out var shiftValue)) { uint shift = shiftValue.Items().First().CollectionKey(); var shiftFields = db.Resolve(db.Find(MostWantedAudioDatabase.Hash("shiftpattern"), shift)); Add("shifts", "SHIFTING", shiftFields[MostWantedAudioDatabase.Hash("BankName")].Text(), "FX_SHIFTING_01", false); } else result.Unsupported.Add("shifts: pvehicle has no ShiftSND");
            var global = db.Resolve(db.Find("audiosystem", "mostwanted")); string Global(string key) => global.TryGetValue(MostWantedAudioDatabase.Hash(key), out var value) ? value.Items().First().Text() : null;
            Add("skids", "SKIDS", Global("AEMS_SkidBanks"), "FX_SKID", false); Add("road", "IG_GLOBAL", Global("AEMS_RNBanks"), "FX_ROADNOISE", false); Add("wind", "IG_GLOBAL", Global("AEMS_WNBanks"), "FX_WIND", false); Add("nitrous", "NOS", Global("AEMS_NOSBanks"), "FX_NITROUS", false);
            string misc = Global("AEMS_MiscBanks"); if (global.TryGetValue(MostWantedAudioDatabase.Hash("AEMS_MiscBanks"), out var miscValue)) misc = miscValue.Items().Select(v => v.Text()).FirstOrDefault(n => n.Equals("CAR_TRANNY.abk", StringComparison.OrdinalIgnoreCase)); Add("transmission", "ENGINE", misc, "CAR_TRANNY", false);
            foreach (var name in new[] { "asphalt", "wetpaved", "concrete", "gravel", "dirt", "grass", "metal", "glass", "water", "wood", "plastic", "null", "unknown" })
            {
                var surface = db.Resolve(db.Find("simsurface", name));
                int Value(string key, int fallback) => surface.TryGetValue(MostWantedAudioDatabase.Hash(key), out var value) ? checked((int)value.Number()) : fallback;
                result.Surfaces.Add(new Surface { Name = name, Loop = Value("Aud_Roadnoise_LOOP", -1), Enter = Value("Aud_RoadNoise_TransON", -1), Exit = Value("Aud_RoadNoise_TransOFF", -1), Skid = Value("Aud_Skid_Type", 0) });
            }
            foreach (var input in context.Inputs) result.Inputs[input.Key] = input.Value;
            return result;
        }
        private static void ValueText(Dictionary<uint, MostWantedAudioDatabase.Value> values, string key, out MostWantedAudioDatabase.Value value) => values.TryGetValue(MostWantedAudioDatabase.Hash(key), out value);
        public static MostWantedAudioSetup Prepare(string packFolder, string gameFolder)
        {
            var result = new MostWantedAudioSetup();
            string[] scripts = Directory.GetFiles(packFolder, "*.nfsms", SearchOption.TopDirectoryOnly);
            if (scripts.Length != 1) throw new InvalidDataException("Choose the MW pack folder containing one .nfsms script and SOUND.");
            result.Script = scripts[0];
            var db = new MostWantedAudioDatabase();
            foreach (string name in new[] { "attributes.bin", "FE_ATTRIB.bin", "gameplay.bin" })
                db.Load(result.Read(ResolveFile(gameFolder, "GLOBAL/" + name)));
            string script = System.Text.Encoding.UTF8.GetString(result.Read(result.Script));
            string addedParent = null, copiedParent = null, engine = null, vehicle = null;
            foreach (string line in script.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || parts[0].StartsWith("#", StringComparison.Ordinal)) continue;
                if (parts[0] == "add_node" && parts.Length == 4 && parts[1] == "engineaudio" && engine == null)
                { addedParent = parts[2]; engine = parts[3]; }
                else if (parts[0] == "copy_fields" && parts.Length == 5 && parts[1] == "engineaudio" && parts[3] == engine && parts[4] == "base|optional|overwrite" && copiedParent == null)
                    copiedParent = parts[2];
                else if (parts[0] == "update_field" && (parts.Length == 5 || parts.Length == 6) && parts[1] == "engineaudio" && parts[2] == engine && copiedParent != null)
                    result.Overrides[parts[3] + (parts.Length == 6 ? "." + parts[4] : "")] = parts[parts.Length - 1];
                else if (parts[0] == "update_field" && parts.Length == 6 && parts[1] == "pvehicle" && parts[3] == "engineaudio[0]" && parts[4] == "Collection" && parts[5] == engine && vehicle == null)
                    vehicle = parts[2];
                else throw new InvalidDataException("Unsupported or out-of-order modscript operation: " + line);
            }
            if (engine == null || copiedParent == null || vehicle == null) throw new InvalidDataException("The script must identify its inherited engine and target vehicle.");
            result.Engine = engine; result.Parent = copiedParent; result.Vehicle = vehicle;
            var fields = db.Resolve(db.Find("engineaudio", addedParent));
            // copy_fields copies the source's own base and optional entries; target inheritance remains unchanged.
            foreach (var field in db.Find("engineaudio", copiedParent).Own) fields[field.Key] = field.Value;
            string Text(string key) => result.Overrides.TryGetValue(key, out string value) ? value : fields[MostWantedAudioDatabase.Hash(key)].Text();
            float Number(string key) => result.Overrides.TryGetValue(key, out string value) ? float.Parse(value, CultureInfo.InvariantCulture) : fields[MostWantedAudioDatabase.Hash(key)].Number();
            result.CarId = checked((int)Number("CarID")); result.IdleRpm = Number("MinRPM"); result.MaximumRpm = Number("MaxRPM"); result.MasterGain = Math.Max(0, Math.Min(1, Number("Master_Vol") / 32767));
            if (result.IdleRpm <= 0 || result.MaximumRpm <= result.IdleRpm || float.IsNaN(result.MaximumRpm)) throw new InvalidDataException("Invalid engine RPM range.");
            void Audio(string role, string directory, string filename, string expectedInterface = null)
            {
                string relative = "SOUND/" + directory + "/" + filename;
                string path = ResolveFile(packFolder, relative, false) ?? ResolveFile(gameFolder, relative);
                byte[] bytes = result.Read(path); var report = new BinaryAnalysisParser().Parse(bytes, path);
                if (expectedInterface != null && report.Gaps.Any(g => g.Code.StartsWith("bnk-", StringComparison.Ordinal) || g.Code.StartsWith("s10a-", StringComparison.Ordinal)))
                    throw new InvalidDataException("The complete setup requires every bank recording to decode: " + filename);
                if (report.Recordings.Count == 0 || report.Recordings.Any(r => !r.Decoded || r.Pcm == null || r.ValidFrames < 1 || r.LoopStart < 0 || r.LoopEnd > r.ValidFrames || r.LoopEnd > 0 && r.LoopEnd <= r.LoopStart))
                    throw new InvalidDataException("Not every recording can be decoded with valid loop bounds: " + filename);
                var programs = expectedInterface == null ? Array.Empty<AemsProgram>() : BlackBoxAemsCompiler.Compile(bytes);
                if (expectedInterface != null && !programs.Any(p => p.interfaceName == expectedInterface)) throw new InvalidDataException(filename + " does not contain " + expectedInterface + "; found " + string.Join(",", programs.Select(p => p.interfaceName)));
                result.Sources.Add(new Source { Role = role, Path = path, Hash = report.Source.Sha256, Report = report, Programs = programs });
                if (result.Sources.Sum(s => s.Report.Recordings.Sum(r => (long)r.Pcm.Length * 4)) > 256L * 1024 * 1024) throw new InvalidDataException("Complete setup exceeds its 256 MiB PCM budget.");
            }
            Audio("acceleration", "ENGINE", Text("Filename_GinsuAccel")); Audio("deceleration", "ENGINE", Text("Filename_GinsuDecel"));
            Audio("engine", "ENGINE", Text("BankName_mainRAM"), "CAR");
            var sweets = fields[MostWantedAudioDatabase.Hash("SweetBank")].Items();
            Audio("sweeteners", "ENGINE", sweets[0].Text(), "CAR_SWTN");
            if (sweets.Length > 1) Audio("whine", "ENGINE", sweets[1].Text(), "CAR_WHINE");
            var car = db.Resolve(db.Find("pvehicle", vehicle));
            uint shift = car[MostWantedAudioDatabase.Hash("ShiftSND")].Items()[0].CollectionKey();
            Audio("shifts", "SHIFTING", db.Resolve(db.Find(MostWantedAudioDatabase.Hash("shiftpattern"), shift))[MostWantedAudioDatabase.Hash("BankName")].Text(), "FX_SHIFTING_01");
            var global = db.Resolve(db.Find("audiosystem", "mostwanted"));
            string Global(string key, int index = 0) => global[MostWantedAudioDatabase.Hash(key)].Items()[index].Text();
            Audio("skids", "SKIDS", Global("AEMS_SkidBanks"), "FX_SKID");
            Audio("road", "IG_GLOBAL", Global("AEMS_RNBanks"), "FX_ROADNOISE");
            Audio("wind", "IG_GLOBAL", Global("AEMS_WNBanks"), "FX_WIND");
            Audio("nitrous", "NOS", Global("AEMS_NOSBanks"), "FX_NITROUS");
            string tranny = global[MostWantedAudioDatabase.Hash("AEMS_MiscBanks")].Items().Select(v => v.Text()).Single(n => n.Equals("CAR_TRANNY.abk", StringComparison.OrdinalIgnoreCase));
            Audio("transmission", "ENGINE", tranny, "CAR_TRANNY");
            foreach (string name in new[] { "asphalt", "wetpaved", "concrete", "gravel", "dirt", "grass", "metal", "glass", "water", "wood", "plastic", "null", "unknown" })
            {
                var surface = db.Resolve(db.Find("simsurface", name));
                int Value(string key, int fallback) => surface.TryGetValue(MostWantedAudioDatabase.Hash(key), out var value) ? checked((int)value.Number()) : fallback;
                result.Surfaces.Add(new Surface { Name = name, Loop = Value("Aud_Roadnoise_LOOP", -1), Enter = Value("Aud_RoadNoise_TransON", -1), Exit = Value("Aud_RoadNoise_TransOFF", -1), Skid = Value("Aud_Skid_Type", 0) });
            }
            return result;
        }
        private byte[] Read(string path)
        {
            if (new FileInfo(path).Length > 32L * 1024 * 1024) throw new InvalidDataException("Source exceeds the 32 MiB file budget.");
            byte[] bytes = File.ReadAllBytes(path); Inputs[path] = Digest(bytes); return bytes;
        }
        public void VerifySources()
        { foreach (var entry in Inputs) if (Digest(File.ReadAllBytes(entry.Key)) != entry.Value) throw new InvalidDataException("Source changed after preparation: " + entry.Key); }
        private static string Digest(byte[] bytes)
        { using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        public static string ResolveFile(string root, string relative, bool required = true)
        {
            string current = Path.GetFullPath(root);
            foreach (string part in relative.Replace('\\', '/').Split('/'))
            {
                if (part.Length == 0 || part == "." || part == ".." || part.IndexOf(':') >= 0) throw new InvalidDataException("Unsafe audio reference.");
                string[] matches = Directory.Exists(current) ? Directory.GetFileSystemEntries(current).Where(p => Path.GetFileName(p).Equals(part, StringComparison.OrdinalIgnoreCase)).ToArray() : Array.Empty<string>();
                if (matches.Length > 1) throw new InvalidDataException("Ambiguous case-insensitive audio reference: " + relative);
                if (matches.Length == 0) { if (!required) return null; throw new FileNotFoundException("Missing inherited audio source: " + relative); }
                current = matches[0];
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Audio references cannot traverse a symbolic link.");
            }
            return current;
        }
    }
}
