using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NfsMwRemaster.Driving.AudioAnalysis;

namespace NfsMwRemaster.Driving.Editor.DrivingMechanics
{
    /// <summary>Read-only, bounded, engine-independent decoding using the existing audio database reader.</summary>
    public static class MostWantedHandlingReader
    {
        public const string Version = "mw-handling-evidence/1";
        public const int MaximumPackBytes = 32 * 1024 * 1024;
        public const int MaximumTotalBytes = 128 * 1024 * 1024;
        private const int MaximumRecords = 20000;
        private sealed class CaptureBudget
        {
            private int fields, values;
            public void Accept(MostWantedHandlingField field)
            {
                fields++; values = checked(values + field.values.Count);
                if (fields > 250000 || values > 1000000) throw new InvalidDataException("Resolved handling evidence budget exceeded.");
            }
        }
        private static readonly string[] SourceNames = { "GLOBAL/attributes.bin", "GLOBAL/FE_ATTRIB.bin", "GLOBAL/gameplay.bin" };
        private static readonly string[] Classes = { "pvehicle", "engine", "transmission", "tires", "chassis", "brakes", "nos", "induction", "rigidbodyspecs", "junkman" };
        private static readonly Dictionary<uint, string> ClassNames = Classes.ToDictionary(MostWantedAudioDatabase.Hash);
        // Names are labels only, admitted by their exact lookup2 hash. Every other field is still emitted.
        private static readonly Dictionary<uint, string> FieldNames = BuildFieldNames();
        private static readonly string[] TypeLabels = { "EA::Reflection::Float", "EA::Reflection::Int32", "EA::Reflection::UInt32",
            "EA::Reflection::Int16", "EA::Reflection::UInt16", "EA::Reflection::Int8", "EA::Reflection::UInt8",
            "EA::Reflection::Boolean", "EA::Reflection::Bool", "EA::Reflection::Text", "Attrib::StringKey", "Attrib::RefSpec",
            "UpgradeSpecs", "AxlePair", "UMath::Vector2", "UMath::Vector3", "UMath::Vector4", "JunkmanMod" };
        private static readonly Dictionary<uint, string> TypeNames = TypeLabels.ToDictionary(MostWantedAudioDatabase.Hash);

        /// <summary>Reads only the three explicitly named GLOBAL packs. No output or game executable is opened for writing.</summary>
        public static MostWantedHandlingReport Decode(string gameFolder, string[] vehicleNames = null)
        {
            if (string.IsNullOrWhiteSpace(gameFolder)) throw new ArgumentException("Game folder is required.", nameof(gameFolder));
            string root = Path.GetFullPath(gameFolder);
            var paths = SourceNames.Select(relative => ResolveFile(root, relative)).ToArray();
            var sources = new List<MostWantedHandlingSourceInput>();
            foreach (var pair in paths.Select((path, index) => new { path, index }))
                sources.Add(new MostWantedHandlingSourceInput(SourceNames[pair.index], ReadBounded(pair.path)));
            var report = DecodeSources(sources, vehicleNames);
            for (int i = 0; i < paths.Length; i++)
            {
                var source = report.sources[i];
                using (var stream = new FileStream(paths[i], FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = SHA256.Create())
                    source.afterSha256 = Hex(sha.ComputeHash(stream));
                source.unchanged = source.sha256 == source.afterSha256;
                source.verification = "File SHA-256 before parsing and after decoding";
                if (!source.unchanged) throw new IOException("Source changed during read-only decoding: " + source.relativePath);
            }
            return report;
        }

        /// <summary>All pvehicle rows are decoded when vehicleNames is null. Empty selection is rejected, not silently expanded.</summary>
        public static MostWantedHandlingReport DecodeSources(IReadOnlyList<MostWantedHandlingSourceInput> sources, string[] vehicleNames = null)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            if (sources.Count < 1 || sources.Count > 16) throw new ArgumentException("Supply between one and sixteen source packs.", nameof(sources));
            if (vehicleNames != null && (vehicleNames.Length == 0 || vehicleNames.Any(string.IsNullOrWhiteSpace)))
                throw new ArgumentException("Vehicle selection must contain nonempty names or hexadecimal row keys.", nameof(vehicleNames));
            var labels = new Dictionary<uint, string>();
            if (vehicleNames != null)
                foreach (string name in vehicleNames)
                {
                    uint hash = MostWantedAudioDatabase.Hash(name);
                    if (labels.TryGetValue(hash, out string prior) && prior != name)
                        throw new ArgumentException("Ambiguous vehicle labels hash to the same key.", nameof(vehicleNames));
                    labels[hash] = name;
                }
            var report = new MostWantedHandlingReport(); var db = new MostWantedAudioDatabase();
            var snapshots = new List<byte[]>(); var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var input in sources)
            {
                if (input == null) throw new ArgumentException("Null source input.", nameof(sources));
                string name = input.RelativePath.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) || name.Split('/').Any(p => p == ".." || p == "." || p.Length == 0))
                    throw new ArgumentException("Source identities must be relative paths without traversal.", nameof(sources));
                if (!identities.Add(name)) throw new ArgumentException("Duplicate source identity: " + name, nameof(sources));
                total += input.Bytes.Length;
                if (input.Bytes.Length > MaximumPackBytes || total > MaximumTotalBytes) throw new InvalidDataException("Handling source byte budget exceeded.");
                var bytes = (byte[])input.Bytes.Clone(); snapshots.Add(bytes);
                report.sources.Add(new MostWantedHandlingSource { relativePath = name, byteLength = bytes.Length,
                    sha256 = Sha256(bytes), verification = "Caller-provided byte snapshot; installation file not verified" });
                db.LoadSource(bytes, name, Classes);
            }
            var rows = db.RowsOf("pvehicle").OrderBy(row => row.Key).ToArray();
            if (rows.Length == 0) Issue(report, "no-vehicles", "pvehicle", "No pvehicle records were found.");
            var cache = new Dictionary<string, MostWantedHandlingRecord>(StringComparer.Ordinal); var budget = new CaptureBudget();
            foreach (var row in rows)
            {
                if (vehicleNames != null && !labels.ContainsKey(row.Key)) continue;
                var record = CaptureRecord(db, row, report, cache, budget);
                string label = labels.TryGetValue(row.Key, out string requested) ? requested : HexKey(row.Key);
                string labelEvidence = requested == null || requested.StartsWith("0x", StringComparison.Ordinal)
                    ? "Unresolved row name; hash is authoritative" : "Caller label matched to stored row hash";
                var nameField = record.fields.Find(f => f.name == "CollectionName");
                string storedName = nameField?.values.FirstOrDefault(v => v.status == "decoded")?.text;
                if (!string.IsNullOrEmpty(storedName) && MostWantedAudioDatabase.Hash(storedName) == row.Key)
                { label = storedName; labelEvidence = "Stored CollectionName text matched to row hash"; }
                var modelField = record.fields.Find(f => f.name == "MODEL");
                report.vehicles.Add(new MostWantedHandlingVehicle { recordId = record.id, rowKey = record.rowKey,
                    name = label, nameEvidence = labelEvidence, model = modelField?.values.FirstOrDefault(v => v.status == "decoded")?.text,
                    links = record.links.Where(IsHandlingLink).ToList() });
            }
            if (vehicleNames != null)
                foreach (var key in labels.Keys.OrderBy(key => key))
                    if (!rows.Any(row => row.Key == key)) Issue(report, "missing-vehicle", labels[key], "Requested pvehicle row was not found.");
            // Follow the class key in the actual payload, not the role's English name. Keep every edge/index, deduplicate only records.
            var queue = new Queue<MostWantedHandlingRecord>(cache.Values);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (queue.Count > 0)
            {
                var record = queue.Dequeue(); if (!visited.Add(record.id)) continue;
                foreach (var link in record.links)
                {
                    if (!IsHandlingLink(link) || link.status != "pending") continue;
                    uint classKey = MostWantedAudioDatabase.Hash(link.targetClassKey), rowKey = MostWantedAudioDatabase.Hash(link.targetRowKey);
                    if (rowKey == 0) { link.status = "null-reference"; link.reason = "Stored collection key is zero; no default row substituted."; continue; }
                    if (!ClassNames.ContainsKey(classKey))
                    { link.status = "unsupported-class"; link.reason = "Reference class is not a supported handling class; no role-name substitution."; Issue(report, link.status, record.id + "/" + link.fieldKey, link.reason); continue; }
                    if (!db.TryFind(classKey, rowKey, out var target))
                    { link.status = "missing-row"; link.reason = "Stored class/collection pair is absent from supplied source packs."; Issue(report, link.status, record.id + "/" + link.fieldKey, link.reason); continue; }
                    if (cache.Count >= MaximumRecords && !cache.ContainsKey(Id(target))) throw new InvalidDataException("Handling graph record budget exceeded.");
                    var captured = CaptureRecord(db, target, report, cache, budget);
                    link.targetRecordId = captured.id; link.status = "resolved"; queue.Enqueue(captured);
                }
            }
            report.records = cache.Values.OrderBy(record => record.id, StringComparer.Ordinal).ToList();
            for (int i = 0; i < snapshots.Count; i++)
            {
                var source = report.sources[i]; source.afterSha256 = Sha256(snapshots[i]); source.unchanged = source.sha256 == source.afterSha256;
                if (!source.unchanged) throw new InvalidDataException("Reader unexpectedly changed an input snapshot.");
            }
            return report;
        }

        private static MostWantedHandlingRecord CaptureRecord(MostWantedAudioDatabase db, MostWantedAudioDatabase.Row row,
            MostWantedHandlingReport report, Dictionary<string, MostWantedHandlingRecord> cache, CaptureBudget budget)
        {
            string id = Id(row); if (cache.TryGetValue(id, out var known)) return known;
            if (cache.Count >= MaximumRecords) throw new InvalidDataException("Handling graph record budget exceeded.");
            var record = new MostWantedHandlingRecord { id = id, classKey = HexKey(row.Class), rowKey = HexKey(row.Key),
                parentKey = HexKey(row.Parent), className = ClassNames.TryGetValue(row.Class, out string label) ? label : HexKey(row.Class), source = Location(row.Source) };
            cache.Add(id, record);
            Dictionary<uint, MostWantedAudioDatabase.ResolvedValue> resolved;
            try
            {
                record.inheritance = db.Inheritance(row).Select(Ancestor).ToList();
                resolved = db.ResolveWithProvenance(row); record.inheritanceResolved = true;
            }
            catch (InvalidDataException error)
            {
                // Direct bytes remain evidence, but never pretend partial inheritance is the final resolved value.
                Issue(report, "unresolved-inheritance", id, error.Message);
                record.inheritance.Add(Ancestor(row));
                resolved = row.Own.ToDictionary(entry => entry.Key, entry => new MostWantedAudioDatabase.ResolvedValue
                    { Value = entry.Value, Owner = row, Inheritance = new[] { row } });
            }
            foreach (var pair in resolved.OrderBy(entry => entry.Key))
            {
                var field = CaptureField(row, pair.Value); budget.Accept(field); record.fields.Add(field);
                if (field.status != "decoded") Issue(report, "unsupported-field", id + "/" + field.fieldKey, field.unsupportedReason);
                foreach (var value in field.values.Where(v => v.kind == "ref-spec" || v.kind == "upgrade-specs"))
                {
                    var link = new MostWantedHandlingLink { fieldKey = field.fieldKey, fieldName = field.name, typeKey = field.typeKey,
                        index = value.index, targetClassKey = value.referenceClassKey, targetRowKey = value.referenceRowKey,
                        source = value.source, status = value.status == "decoded" ? "pending" : "unsupported-reference", reason = value.unsupportedReason };
                    if (!IsHandlingLink(link) && link.status == "pending")
                    { link.status = "outside-handling-scope"; link.reason = "Reference retained, but its target class is not traversed by the handling reader."; }
                    record.links.Add(link);
                }
            }
            return record;
        }

        private static MostWantedHandlingField CaptureField(MostWantedAudioDatabase.Row requested, MostWantedAudioDatabase.ResolvedValue resolved)
        {
            var value = resolved.Value; var definition = value.Definition;
            bool named = FieldNames.TryGetValue(definition.Key, out string label);
            var field = new MostWantedHandlingField { fieldKey = HexKey(definition.Key), name = named ? label : HexKey(definition.Key),
                nameEvidence = named ? "Candidate field label matched to stored hash" : "Unresolved name; raw field hash retained",
                typeKey = HexKey(definition.Type), typeName = TypeNames.TryGetValue(definition.Type, out string type) ? type : HexKey(definition.Type),
                ownerRowKey = HexKey(resolved.Owner.Key), inherited = requested.Key != resolved.Owner.Key,
                isArray = definition.Array, elementSize = definition.Size, maximum = definition.Maximum, flags = definition.Flags,
                alignment = definition.Alignment, layoutOffset = definition.Offset, definitionSource = Location(definition.Source),
                source = Location(value.Source), ownerSource = Location(resolved.Owner.Source),
                inheritance = resolved.Inheritance.Select(Ancestor).ToList(), status = "decoded" };
            try
            {
                if (definition.Array)
                {
                    field.arrayHeaderRawHex = Hex(value.RawBytes(8));
                    var header = value.RelocatedBytes(8); field.arrayCapacity = U16(header, 0); field.arrayCount = U16(header, 2);
                }
                foreach (var item in value.Items())
                {
                    var decoded = Decode(item); field.values.Add(decoded);
                    if (decoded.status != "decoded") { field.status = "unsupported"; field.unsupportedReason = decoded.unsupportedReason; }
                }
            }
            catch (Exception error) when (IsDataFailure(error))
            { field.status = "unsupported"; field.unsupportedReason = error.Message; }
            return field;
        }

        /// <summary>Decodes a single scalar/array item. Width and hash must both agree; no float reinterpretation of unknown types.</summary>
        public static MostWantedHandlingValue Decode(MostWantedAudioDatabase.Value value)
        {
            if (value == null || value.Definition == null) throw new ArgumentNullException(nameof(value));
            var result = new MostWantedHandlingValue { index = value.ArrayIndex, source = Location(value.Source), status = "decoded", kind = "unsupported" };
            try
            {
                if (value.Definition.Array && value.ArrayIndex < 0) throw new InvalidDataException("Decode expects one array element; use Value.Items() first.");
                int size = value.Definition.Size; uint type = value.Definition.Type;
                var bytes = value.RelocatedBytes(size); result.rawHex = Hex(value.RawBytes(size)); result.relocatedHex = Hex(bytes);
                string name = TypeNames.TryGetValue(type, out string known) ? known : null;
                switch (name)
                {
                    case "EA::Reflection::Float":
                        Width(size, 4); result.kind = "float32"; var scalar = Float(bytes, 0, "Value");
                        result.floatBits = scalar.floatBits; result.numberText = scalar.numberText; result.numericValue = scalar.numericValue;
                        if (!scalar.finite) Unsupported(result, "Non-finite float32; exact bits retained, not a usable physical value.");
                        break;
                    case "EA::Reflection::Int32": Width(size, 4); result.kind = "int32"; result.signedValue = unchecked((int)U32(bytes, 0)); break;
                    case "EA::Reflection::UInt32": Width(size, 4); result.kind = "uint32"; result.unsignedValue = U32(bytes, 0); break;
                    case "EA::Reflection::Int16": Width(size, 2); result.kind = "int16"; result.signedValue = unchecked((short)U16(bytes, 0)); break;
                    case "EA::Reflection::UInt16": Width(size, 2); result.kind = "uint16"; result.unsignedValue = U16(bytes, 0); break;
                    case "EA::Reflection::Int8": Width(size, 1); result.kind = "int8"; result.signedValue = unchecked((sbyte)bytes[0]); break;
                    case "EA::Reflection::UInt8": Width(size, 1); result.kind = "uint8"; result.unsignedValue = bytes[0]; break;
                    case "EA::Reflection::Boolean":
                    case "EA::Reflection::Bool":
                        Width(size, 1); result.kind = "boolean"; result.booleanValue = bytes[0] != 0;
                        if (bytes[0] > 1) Unsupported(result, "Noncanonical Boolean byte; raw value preserved.");
                        break;
                    case "AxlePair":
                        Width(size, 8); result.kind = "axle-pair"; Components(result, bytes, "Front", "Rear"); break;
                    case "UMath::Vector2": Width(size, 8); result.kind = "vector2"; Components(result, bytes, "x", "y"); break;
                    case "UMath::Vector3": Width(size, 12); result.kind = "vector3"; Components(result, bytes, "x", "y", "z"); break;
                    case "UMath::Vector4": Width(size, 16); result.kind = "vector4"; Components(result, bytes, "x", "y", "z", "w"); break;
                    case "EA::Reflection::Text": Width(size, 4); result.kind = "text"; result.text = value.Text(); break;
                    case "Attrib::StringKey":
                        Width(size, 16); result.kind = "string-key"; result.text = value.Text(); result.unsignedValue = U32(bytes, 0);
                        result.uninterpretedWords = new[] { HexKey(U32(bytes, 4)), HexKey(U32(bytes, 8)), HexKey(U32(bytes, 12)) }; break;
                    case "Attrib::RefSpec":
                        Width(size, 12); result.kind = "ref-spec"; result.referenceClassKey = HexKey(U32(bytes, 0)); result.referenceRowKey = HexKey(U32(bytes, 4));
                        result.uninterpretedWords = new[] { HexKey(U32(bytes, 8)) }; break;
                    case "JunkmanMod":
                        Width(size, 12); result.kind = "junkman-mod"; result.referenceClassKey = HexKey(U32(bytes, 0));
                        result.definitionKey = HexKey(U32(bytes, 4)); result.components.Add(Float(bytes, 8, "Scale"));
                        if (!result.components[0].finite) Unsupported(result, "Non-finite JunkmanMod scale; original bytes retained.");
                        break;
                    case "UpgradeSpecs":
                        result.kind = "upgrade-specs";
                        // Existing CollectionKey() proves only the collection word. Do not invent the rest of this layout.
                        if (size < 8) throw new InvalidDataException("UpgradeSpecs is too short for its collection key.");
                        result.referenceRowKey = HexKey(value.CollectionKey());
                        Unsupported(result, "UpgradeSpecs collection key retained; class/extra-word layout and stage semantics are not established."); break;
                    default: Unsupported(result, "Unsupported declared type " + HexKey(type) + "; original bytes retained without reinterpretation."); break;
                }
            }
            catch (Exception error) when (IsDataFailure(error)) { Unsupported(result, error.Message); }
            return result;
        }

        private static void Components(MostWantedHandlingValue result, byte[] bytes, params string[] names)
        {
            for (int i = 0; i < names.Length; i++) result.components.Add(Float(bytes, i * 4, names[i]));
            if (result.components.Any(component => !component.finite)) Unsupported(result, "Non-finite float32 component; exact bits retained.");
        }
        private static MostWantedHandlingComponent Float(byte[] bytes, int offset, string name)
        {
            uint bits = U32(bytes, offset); float number = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
            bool finite = !float.IsNaN(number) && !float.IsInfinity(number);
            return new MostWantedHandlingComponent { name = name, floatBits = HexKey(bits),
                numberText = number.ToString("R", CultureInfo.InvariantCulture), numericValue = finite ? number : 0, finite = finite };
        }
        private static bool IsHandlingLink(MostWantedHandlingLink link) =>
            ClassNames.ContainsKey(MostWantedAudioDatabase.Hash(link.targetClassKey)) || Classes.Contains(link.fieldName);
        private static bool IsDataFailure(Exception error) => error is InvalidDataException || error is OverflowException || error is ArgumentOutOfRangeException;
        private static void Unsupported(MostWantedHandlingValue value, string reason) { value.status = "unsupported"; value.unsupportedReason = reason; }
        private static void Width(int actual, int expected) { if (actual != expected) throw new InvalidDataException("Declared type width mismatch: " + actual + ", expected " + expected + "."); }
        private static uint U32(byte[] bytes, int at) => (uint)bytes[at] | ((uint)bytes[at + 1] << 8) | ((uint)bytes[at + 2] << 16) | ((uint)bytes[at + 3] << 24);
        private static ushort U16(byte[] bytes, int at) => (ushort)(bytes[at] | (bytes[at + 1] << 8));
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        private static string HexKey(uint key) => "0x" + key.ToString("X8", CultureInfo.InvariantCulture);
        private static string Sha256(byte[] bytes) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes)); }
        private static string Id(MostWantedAudioDatabase.Row row) => HexKey(row.Class) + "/" + HexKey(row.Key);
        private static void Issue(MostWantedHandlingReport report, string code, string subject, string message)
        { report.complete = false; report.issues.Add(new MostWantedHandlingIssue { code = code, subject = subject, message = message }); }
        private static MostWantedHandlingLocation Location(MostWantedAudioDatabase.Origin source) => source == null ? null :
            new MostWantedHandlingLocation { sourceFile = source.SourceFile, sha256 = source.SourceSha256, segment = source.Segment,
                vaultIndex = source.VaultIndex, segmentStart = source.SegmentStart, segmentOffset = source.Position, packOffset = source.PackOffset };
        private static MostWantedHandlingAncestor Ancestor(MostWantedAudioDatabase.Row row) => new MostWantedHandlingAncestor
            { classKey = HexKey(row.Class), rowKey = HexKey(row.Key), parentKey = HexKey(row.Parent), source = Location(row.Source) };

        private static Dictionary<uint, string> BuildFieldNames()
        {
            string names = "CollectionName MODEL MASS TENSOR_SCALE HandlingRating PlayerUsable " +
                "ENGINE_BRAKING FLYWHEEL_MASS IDLE MAX_RPM RED_LINE SPEED_LIMITER TORQUE " +
                "CLUTCH_SLIP DIFFERENTIAL FINAL_GEAR GEAR_EFFICIENCY GEAR_RATIO OPTIMAL_SHIFT SHIFT_SPEED TORQUE_CONVERTER TORQUE_SPLIT " +
                "ASPECT_RATIO DYNAMIC_GRIP GRIP_SCALE RIM_SIZE SECTION_WIDTH STATIC_GRIP STEERING YAW_CONTROL YAW_SPEED " +
                "AERO_CG AERO_COEFFICIENT DRAG_COEFFICIENT FRONT_AXLE FRONT_WEIGHT_BIAS RENDER_MOTION RIDE_HEIGHT ROLL_CENTER " +
                "SHOCK_BLOWOUT SHOCK_DIGRESSION SHOCK_EXT_STIFFNESS SHOCK_STIFFNESS SHOCK_VALVING SPRING_PROGRESSION SPRING_STIFFNESS SWAYBAR_STIFFNESS TRACK_WIDTH TRAVEL WHEEL_BASE " +
                "BRAKES BRAKE_LOCK EBRAKE FLOW_RATE NOS_CAPACITY NOS_DISENGAGE RECHARGE_MAX RECHARGE_MAX_SPEED RECHARGE_MIN RECHARGE_MIN_SPEED TORQUE_BOOST " +
                "HIGH_BOOST LOW_BOOST PSI SPOOL SPOOL_TIME_DOWN SPOOL_TIME_UP VACUUM BEHAVIOR_MECHANIC_ENGINE BEHAVIOR_MECHANIC_INPUT BEHAVIOR_MECHANIC_RIGIDBODY BEHAVIOR_MECHANIC_SUSPENSION " +
                "BASE_MATERIAL CG COLLISION_BOX_PAD DEFAULT_COL_BOX DRAG DRAG_ANGULAR GRAVITY GROUND_ELASTICITY GROUND_FRICTION GROUND_MOMENT_SCALE " +
                "IMMOBILE_OBJECT_COLLISIONS INSTANCE_COLLISIONS_3D NATURAL_ANGULAR_DAMPING NO_GROUND_COLLISIONS NO_OBJ_COLLISIONS NO_WORLD_COLLISIONS " +
                "OBJ_ELASTICITY OBJ_FRICTION OBJ_MOMENT_SCALE SLEEP_VELOCITY WALL_ELASTICITY WALL_FRICTION WORLD_MOMENT_SCALE";
            return names.Split(' ').Concat(Classes).Concat(Classes.SelectMany(name => new[] { name + "_current", name + "_upgrades", name + "_package" }))
                .Distinct(StringComparer.Ordinal).ToDictionary(MostWantedAudioDatabase.Hash);
        }

        private static byte[] ReadBounded(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length < 8 || stream.Length > MaximumPackBytes) throw new InvalidDataException("Source file size exceeds VPAK limits: " + path);
                byte[] bytes = new byte[checked((int)stream.Length)]; int offset = 0;
                while (offset < bytes.Length) { int count = stream.Read(bytes, offset, bytes.Length - offset); if (count == 0) throw new EndOfStreamException("Source truncated while reading."); offset += count; }
                if (stream.ReadByte() != -1) throw new IOException("Source grew while reading.");
                return bytes;
            }
        }

        private static string ResolveFile(string root, string relative)
        {
            string current = root;
            foreach (string segment in relative.Split('/'))
            {
                string exact = Path.Combine(current, segment);
                if (File.Exists(exact) || Directory.Exists(exact)) { current = exact; continue; }
                if (!Directory.Exists(current)) throw new DirectoryNotFoundException(current);
                var matches = Directory.EnumerateFileSystemEntries(current).Where(path => string.Equals(Path.GetFileName(path), segment, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1) throw new FileNotFoundException("Missing or ambiguous source path: " + relative);
                current = matches[0];
            }
            return current;
        }
    }
}
