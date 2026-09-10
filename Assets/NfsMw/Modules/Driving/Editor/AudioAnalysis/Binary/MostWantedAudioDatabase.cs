using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NfsMwRemaster.Driving.AudioAnalysis
{
    /// <summary>Read-only PC VPAK reader. Resolves audio records without loading or executing game code.</summary>
    public sealed class MostWantedAudioDatabase
    {
        public const string Version = "mw-audio-vault/1";
        /// <summary>Location in the original, unmodified VPAK, not a relocated runtime address.</summary>
        public sealed class Origin
        {
            public string SourceFile, SourceSha256, Segment;
            public int VaultIndex, SegmentStart, Position;
            public int PackOffset => checked(SegmentStart + Position);
            public Origin At(int position) => new Origin { SourceFile = SourceFile, SourceSha256 = SourceSha256,
                Segment = Segment, VaultIndex = VaultIndex, SegmentStart = SegmentStart, Position = position };
        }
        public sealed class Field
        {
            public uint Key, Type;
            public int Offset, Size, Maximum, Flags, Alignment;
            public Origin Source;
            public bool Array => (Flags & 1) != 0;
            public bool Base => (Flags & 2) != 0;
        }
        public sealed class Value
        {
            public Field Definition;
            public byte[] Data, Strings;
            public byte[] OriginalData;
            public int Position;
            public int ArrayIndex = -1;
            public Origin Source;
            public byte[] RawBytes(int size) => Slice(OriginalData ?? Data, Position, size);
            public byte[] RelocatedBytes(int size) => Slice(Data, Position, size);
            public string Text()
            {
                int pointer = Definition.Type == Hash("Attrib::StringKey") ? Position + 12 : Position;
                if (Definition.Type != Hash("Attrib::StringKey") && Definition.Type != Hash("EA::Reflection::Text"))
                    throw new InvalidDataException("Expected a database string.");
                return String(Strings, Offset(Data, pointer));
            }
            public float Number()
            {
                uint type = Definition.Type;
                if (type == Hash("EA::Reflection::Float")) return BitConverter.ToSingle(Data, Checked(Data, Position, 4));
                if (type == Hash("EA::Reflection::UInt32")) return U32(Data, Position);
                if (type == Hash("EA::Reflection::UInt16")) return U16(Data, Position);
                if (type == Hash("EA::Reflection::Int16")) return unchecked((short)U16(Data, Position));
                if (type == Hash("EA::Reflection::Int32") || type == Hash("eENGINE_GROUP") || type == Hash("FXROADNOISE_LOOP") || type == Hash("FXROADNOISE_TRANSITION")) return unchecked((int)U32(Data, Position));
                if (type == Hash("EA::Reflection::Boolean")) return Data[Checked(Data, Position, 1)] == 0 ? 0 : 1;
                throw new InvalidDataException("Expected a supported database number.");
            }
            public uint CollectionKey()
            {
                if (Definition.Type != Hash("Attrib::RefSpec") && Definition.Type != Hash("UpgradeSpecs"))
                    throw new InvalidDataException("Expected a collection reference.");
                return U32(Data, Position + 4);
            }
            public Value[] Items()
            {
                if (!Definition.Array || ArrayIndex >= 0) return new[] { this };
                int capacity = U16(Data, Position), count = U16(Data, Position + 2), size = U16(Data, Position + 4);
                if (count > capacity || capacity > 4096 || size != Definition.Size || size == 0) throw new InvalidDataException("Invalid database array.");
                int at = Align(Position + 8, Definition.Alignment);
                Checked(Data, at, checked(capacity * size));
                var values = new Value[count];
                for (int i = 0; i < count; i++) values[i] = new Value { Definition = Definition, Data = Data, Strings = Strings,
                    OriginalData = OriginalData, Position = at + i * size, ArrayIndex = i, Source = Source?.At(at + i * size) };
                return values;
            }
        }
        public sealed class Row
        {
            public uint Class, Key, Parent;
            public Origin Source;
            public readonly Dictionary<uint, Value> Own = new Dictionary<uint, Value>();
        }
        private readonly Dictionary<uint, Dictionary<uint, Field>> classes = new Dictionary<uint, Dictionary<uint, Field>>();
        private readonly Dictionary<ulong, Row> rows = new Dictionary<ulong, Row>();
        private int analysisFields, analysisValues;
        private long analysisVaultBytes;
        public IEnumerable<Row> Rows => rows.Values;
        public IEnumerable<Row> RowsOf(string className) => RowsOf(Hash(className));
        public IEnumerable<Row> RowsOf(uint classKey) => rows.Values.Where(row => row.Class == classKey);
        public Row Find(string className, string name) => Find(Hash(className), Hash(name));
        public Row Find(uint classKey, uint key) => rows.TryGetValue(((ulong)classKey << 32) | key, out var row) ? row : throw new InvalidDataException("Missing database record " + classKey.ToString("X8") + "/" + key.ToString("X8"));
        public bool TryFind(string className, string name, out Row row) => rows.TryGetValue(((ulong)Hash(className) << 32) | Hash(name), out row);
        public bool TryFind(uint classKey, uint key, out Row row) => rows.TryGetValue(((ulong)classKey << 32) | key, out row);
        public sealed class ResolvedValue
        {
            public Value Value;
            public Row Owner;
            /// <summary>Requested row through declaring row, in lookup order (inclusive).</summary>
            public Row[] Inheritance;
        }
        public Row[] Inheritance(Row row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            var result = new List<Row>(); var visited = new HashSet<uint>();
            for (var current = row; current != null; current = current.Parent == 0 ? null : Find(current.Class, current.Parent))
            {
                if (!visited.Add(current.Key) || visited.Count > 128) throw new InvalidDataException("Invalid database inheritance chain.");
                result.Add(current);
            }
            return result.ToArray();
        }
        public Dictionary<uint, ResolvedValue> ResolveWithProvenance(Row row)
        {
            var chain = Inheritance(row); var result = new Dictionary<uint, ResolvedValue>();
            for (int i = 0; i < chain.Length; i++)
                foreach (var entry in chain[i].Own)
                    if (!result.ContainsKey(entry.Key)) result.Add(entry.Key, new ResolvedValue { Value = entry.Value,
                        Owner = chain[i], Inheritance = chain.Take(i + 1).ToArray() });
            return result;
        }
        public Dictionary<uint, Value> Resolve(Row row)
        {
            var stack = new Stack<Row>(); var visited = new HashSet<uint>();
            for (var current = row; current != null; current = current.Parent == 0 ? null : Find(current.Class, current.Parent))
            {
                if (!visited.Add(current.Key) || visited.Count > 128) throw new InvalidDataException("Invalid database inheritance chain.");
                stack.Push(current);
            }
            var merged = new Dictionary<uint, Value>();
            while (stack.Count > 0) foreach (var entry in stack.Pop().Own) merged[entry.Key] = entry.Value;
            return merged;
        }
        public void Load(byte[] pack, params string[] additionalClasses)
            => LoadCore(pack, null, additionalClasses);
        /// <summary>Opt-in analysis origins and original payloads. Input bytes are never mutated.</summary>
        public void LoadSource(byte[] pack, string sourceFile, params string[] additionalClasses)
        {
            if (string.IsNullOrWhiteSpace(sourceFile)) throw new ArgumentException("Source identity is required.", nameof(sourceFile));
            LoadCore(pack, sourceFile, additionalClasses);
        }
        private void LoadCore(byte[] pack, string sourceFile, string[] additionalClasses)
        {
            if (pack == null || pack.Length > 32 * 1024 * 1024 || U32(pack, 0) != 0x4b415056) throw new InvalidDataException("Expected a PC Most Wanted VPAK database.");
            if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("The PC VPAK reader requires a little-endian host.");
            string hash = null;
            if (sourceFile != null)
                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(pack)).Replace("-", "").ToLowerInvariant();
            int count = Offset(pack, 4);
            if (count < 1 || count > 512) throw new InvalidDataException("Invalid vault count.");
            Checked(pack, 16, count * 20);
            for (int i = 0; i < count; i++)
            {
                int entry = 16 + i * 20;
                int binStart = Offset(pack, entry + 12), vltStart = Offset(pack, entry + 16);
                int binSize = Offset(pack, entry + 4), vltSize = Offset(pack, entry + 8);
                if (sourceFile != null && (analysisVaultBytes += (long)binSize + vltSize) > 128 * 1024 * 1024)
                    throw new InvalidDataException("Analysis vault byte budget exceeded; repeated or overlapping segments are bounded.");
                var bin = Slice(pack, binStart, binSize);
                var vlt = Slice(pack, vltStart, vltSize);
                Origin OriginFor(string segment, int start) => sourceFile == null ? null : new Origin { SourceFile = sourceFile,
                    SourceSha256 = hash, VaultIndex = i, Segment = segment, SegmentStart = start };
                LoadVault(bin, vlt, additionalClasses, OriginFor("bin", binStart), OriginFor("vlt", vltStart));
            }
        }
        private void LoadVault(byte[] bin, byte[] vlt, string[] additionalClasses, Origin binSource, Origin vltSource)
        {
            byte[] originalBin = binSource == null ? null : (byte[])bin.Clone();
            byte[] originalVlt = vltSource == null ? null : (byte[])vlt.Clone();
            var exports = new List<Tuple<uint, int>>();
            for (int at = 0; at < vlt.Length;)
            {
                uint id = U32(vlt, at); int length = checked(Offset(vlt, at + 4) - 8), start = at + 8;
                Checked(vlt, start, length); int end = checked(start + length);
                if (id == 0x5074724e)
                {
                    bool targetVlt = false;
                    for (int p = start; p + 12 <= end; p += 12)
                    {
                        int type = U16(vlt, p + 4), index = U16(vlt, p + 6);
                        if (type == 0) break;
                        if (type == 2) { targetVlt = index == 0; continue; }
                        if (type != 1 && type != 3) continue;
                        var target = targetVlt ? vlt : bin;
                        int fixup = Offset(vlt, p); uint destination = U32(vlt, p + 8);
                        Checked(target, fixup, 4); Array.Copy(BitConverter.GetBytes(destination), 0, target, fixup, 4);
                    }
                }
                if (id == 0x4578704e)
                {
                    int count = Offset(vlt, start);
                    if (count > 100000 || 4L + count * 20L > length) throw new InvalidDataException("Invalid export table.");
                    for (int i = 0; i < count; i++)
                    {
                        int p = start + 4 + i * 20, offset = Offset(vlt, p + 16);
                        Checked(vlt, offset, Offset(vlt, p + 12)); exports.Add(Tuple.Create(U32(vlt, p + 4), offset));
                    }
                }
                at = end;
            }
            foreach (var export in exports.Where(e => e.Item1 == 0x5e970cbc))
            {
                int at = export.Item2, count = Offset(vlt, at + 8), definitions = Offset(vlt, at + 12);
                if (count > 4096) throw new InvalidDataException("Too many database fields.");
                if (binSource != null && (analysisFields = checked(analysisFields + count)) > 250000)
                    throw new InvalidDataException("Analysis database field-definition budget exceeded.");
                Checked(bin, definitions, count * 16); var fields = new Dictionary<uint, Field>();
                for (int i = 0; i < count; i++)
                {
                    int p = definitions + i * 16;
                    if (bin[p + 15] > 4) throw new InvalidDataException("Unsupported field alignment.");
                    var field = new Field { Key = U32(bin, p), Type = U32(bin, p + 4), Offset = U16(bin, p + 8), Size = U16(bin, p + 10), Maximum = U16(bin, p + 12), Flags = bin[p + 14], Alignment = 1 << bin[p + 15], Source = binSource?.At(p) };
                    fields.Add(field.Key, field);
                }
                classes.Add(U32(vlt, at), fields);
            }
            var selected = new HashSet<uint>(new[] { Hash("engineaudio"), Hash("shiftpattern"), Hash("audiosystem"), Hash("pvehicle"), Hash("acceltrans"), Hash("simsurface"), Hash("ecar"), Hash("chopshop"), Hash("carinfo"), Hash("model") });
            if (additionalClasses != null)
                foreach (string className in additionalClasses)
                {
                    if (string.IsNullOrWhiteSpace(className)) throw new ArgumentException("Additional database classes must be named.", nameof(additionalClasses));
                    selected.Add(Hash(className));
                }
            foreach (var export in exports.Where(e => e.Item1 == 0x8e112eb7))
            {
                int at = export.Item2; uint classKey = U32(vlt, at + 4);
                if (!selected.Contains(classKey)) continue;
                if (!classes.TryGetValue(classKey, out var fields)) throw new InvalidDataException("Missing database class definition.");
                var row = new Row { Key = U32(vlt, at), Class = classKey, Parent = U32(vlt, at + 8), Source = vltSource?.At(at) };
                int layout = Offset(vlt, at + 28), entries = Offset(vlt, at + 20), types = Offset(vlt, at + 24);
                if (entries > 4096 || types > 4096) throw new InvalidDataException("Invalid row entry budget.");
                if (layout != 0) foreach (var field in fields.Values.Where(f => f.Base)) row.Own.Add(field.Key, MakeValue(field, bin, bin, checked(layout + field.Offset), originalBin, binSource));
                int optional = checked(at + 32 + types * 4); Checked(vlt, optional, entries * 12);
                for (int i = 0; i < entries; i++)
                {
                    int p = optional + i * 12;
                    if (!fields.TryGetValue(U32(vlt, p), out var field)) throw new InvalidDataException("Unknown database row field.");
                    bool inline = field.Size <= 4 && !field.Array;
                    row.Own[field.Key] = MakeValue(field, inline ? vlt : bin, bin, inline ? p + 4 : Offset(vlt, p + 4),
                        inline ? originalVlt : originalBin, inline ? vltSource : binSource);
                }
                if (vltSource != null && rows.Count >= 100000) throw new InvalidDataException("Analysis database row budget exceeded.");
                rows.Add(((ulong)row.Class << 32) | row.Key, row);
            }
        }
        private Value MakeValue(Field field, byte[] data, byte[] strings, int position, byte[] original, Origin source)
        {
            Checked(data, position, field.Array ? 8 : field.Size);
            if (source != null && ++analysisValues > 1000000) throw new InvalidDataException("Analysis database value budget exceeded.");
            return new Value { Definition = field, Data = data, Strings = strings,
                Position = position, OriginalData = original, Source = source?.At(position) };
        }
        private static int Align(int offset, int alignment) => checked((offset + alignment - 1) & ~(alignment - 1));
        private static int Checked(byte[] data, int offset, int size)
        { if (data == null || offset < 0 || size < 0 || offset > data.Length - size) throw new InvalidDataException("Database pointer outside source bounds."); return offset; }
        private static byte[] Slice(byte[] data, int offset, int size)
        { Checked(data, offset, size); var result = new byte[size]; Array.Copy(data, offset, result, 0, size); return result; }
        private static uint U32(byte[] data, int offset) => BitConverter.ToUInt32(data, Checked(data, offset, 4));
        private static int U16(byte[] data, int offset) => BitConverter.ToUInt16(data, Checked(data, offset, 2));
        private static int Offset(byte[] data, int offset) { uint value = U32(data, offset); if (value > int.MaxValue) throw new InvalidDataException("Database offset exceeds limits."); return (int)value; }
        private static string String(byte[] data, int offset)
        {
            if (offset == 0) return ""; Checked(data, offset, 1); int end = offset;
            while (end < data.Length && end - offset <= 4096 && data[end] != 0) end++;
            if (end == data.Length || end - offset > 4096) throw new InvalidDataException("Unterminated database string.");
            return Encoding.UTF8.GetString(data, offset, end - offset);
        }
        // Jenkins lookup2 with the VLT seed. Layout references: NFSTools/VaultLib (MIT), see THIRD_PARTY.md.
        public static uint Hash(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            if (text.StartsWith("0x", StringComparison.Ordinal) && uint.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint key)) return key;
            unchecked
            {
                byte[] bytes = Encoding.ASCII.GetBytes(text); uint a = 0x9e3779b9, b = a, c = 0xabcdef00; int p = 0, left = bytes.Length;
                while (left >= 12) { a += U32(bytes, p); b += U32(bytes, p + 4); c += U32(bytes, p + 8); Mix(ref a, ref b, ref c); p += 12; left -= 12; }
                c += (uint)bytes.Length;
                for (int i = 0; i < left; i++) { if (i < 4) a += (uint)bytes[p + i] << (8 * i); else if (i < 8) b += (uint)bytes[p + i] << (8 * (i - 4)); else c += (uint)bytes[p + i] << (8 * (i - 7)); }
                Mix(ref a, ref b, ref c); return c;
            }
        }
        private static void Mix(ref uint a, ref uint b, ref uint c)
        {
            unchecked
            {
                a -= b; a -= c; a ^= c >> 13; b -= c; b -= a; b ^= a << 8; c -= a; c -= b; c ^= b >> 13;
                a -= b; a -= c; a ^= c >> 12; b -= c; b -= a; b ^= a << 16; c -= a; c -= b; c ^= b >> 5;
                a -= b; a -= c; a ^= c >> 3; b -= c; b -= a; b ^= a << 10; c -= a; c -= b; c ^= b >> 15;
            }
        }
    }
}
