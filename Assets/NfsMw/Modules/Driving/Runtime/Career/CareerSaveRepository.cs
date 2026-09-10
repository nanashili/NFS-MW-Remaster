using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace NfsMwRemaster.Driving
{
    public sealed class SaveCommitUncertainException : IOException
    { public SaveCommitUncertainException(string message, Exception cause) : base(message, cause) { } }
    public enum SaveWriteStage { Validated, Created, QuarterWritten, HalfWritten, Written, Flushed, Verified, Promoted, Retained }

    public sealed class SaveHeader
    {
        public int format = 1;
        public int schema;
        public string slot;
        public long generation;
        public string operation;
        public string parent;
        public long utcTicks;
        public string displayName;
        public int payloadBytes;
        public string payloadHash;
    }

    public sealed class SaveReadResult
    {
        public SaveHeader Header { get; internal set; }
        public string Payload { get; internal set; }
        public string HeadStamp { get; internal set; }
        public string Recovery { get; internal set; }
    }

    public sealed class SaveSlotInfo
    {
        public string Slot { get; internal set; }
        public SaveHeader Header { get; internal set; }
        public string Status { get; internal set; }
    }

    public sealed class SaveArtifactInfo
    {
        public string Path { get; internal set; }
        public long Bytes { get; internal set; }
        public SaveHeader Header { get; internal set; }
        public string Status { get; internal set; }
    }

    /// <summary>
    /// Local immutable generations. All operations take the same exclusive file lock.
    /// Expected-head tokens are mandatory for existing slots. No Unity objects or clocks
    /// from gameplay are accessed, so detached snapshots can be committed on a worker.
    /// </summary>
    public sealed class CareerSaveRepository
    {
        private const int MaximumHeader = 4096;
        private const int MaximumFiles = 1024;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("NFSSAVE1");
        private readonly string root;
        private readonly int retention;
        private readonly Action<SaveWriteStage> fault;
        // Cache domain validation only after hashing the complete bytes on every read.
        // Immutable content hashes, not timestamps, authorize reuse. Bounded per backend.
        private readonly Dictionary<string, int> validatedPayloads = new Dictionary<string, int>(StringComparer.Ordinal);
        public string LastWarning { get; private set; } = string.Empty;
        public double LastMilliseconds { get; private set; }

        public CareerSaveRepository(string directory, int retainedGenerations = 3, Action<SaveWriteStage> faultInjector = null)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Save directory is required.");
            root = Path.GetFullPath(directory);
            if (retainedGenerations < 2 || retainedGenerations > 32) throw new ArgumentOutOfRangeException(nameof(retainedGenerations));
            retention = retainedGenerations; fault = faultInjector;
        }

        public bool Exists(string slot)
        {
            ValidateSlot(slot);
            using (Lock()) return Candidates(slot).Count != 0;
        }

        public SaveReadResult Load(string slot)
        {
            ValidateSlot(slot);
            using (Lock()) return Scan(slot);
        }

        public SaveReadResult Commit(string slot, string payload, string expectedHead, bool createOnly = false, string operationId = null)
        {
            ValidateSlot(slot);
            int schema = CareerSaveCodec.Validate(slot, payload, out string displayName);
            byte[] bytes = CareerSaveCodec.Utf8.GetBytes(payload);
            string operation = operationId ?? Guid.NewGuid().ToString("N");
            if (!Guid.TryParseExact(operation, "N", out _)) throw new ArgumentException("Operation must be a compact GUID.");
            var watch = Stopwatch.StartNew();
            LastWarning = string.Empty;
            using (Lock())
            {
                var candidates = Candidates(slot);
                SaveReadResult previous = candidates.Count == 0 ? null : Scan(slot);
                // Only a retry of the current committed operation is acknowledged.
                if (previous != null && previous.Header.operation == operation)
                {
                    if (previous.Payload != payload) throw Conflict("Operation ID reused for different state.");
                    return previous;
                }
                if (createOnly && candidates.Count != 0) throw Conflict("This save slot already exists.");
                if (candidates.Any(c => !c.Legacy && c.Path.EndsWith("-" + operation + ".save", StringComparison.Ordinal)))
                    throw Conflict("This operation ID belongs to an older retained generation.");
                if ((previous?.HeadStamp ?? string.Empty) != (expectedHead ?? string.Empty))
                    throw Conflict("Save changed since it was loaded. Reload or resolve the conflict before saving.");
                long generation = checked(candidates.Count == 0 ? 1 : candidates.Max(c => c.Generation) + 1);
                var header = new SaveHeader
                {
                    schema = schema, slot = slot, generation = generation, operation = operation,
                    parent = previous?.Header.operation ?? string.Empty, utcTicks = DateTime.UtcNow.Ticks,
                    displayName = displayName,
                    payloadBytes = bytes.Length, payloadHash = Hash(bytes)
                };
                RememberValidated(slot + ":" + header.payloadHash, schema);
                if (header.displayName.Length > 128) header.displayName = header.displayName.Substring(0, 128);
                string folder = SlotDirectory(slot); Directory.CreateDirectory(folder);
                string staged = Path.Combine(folder, operation + ".pending");
                string destination = Path.Combine(folder, generation.ToString("D20") + "-" + operation + ".save");
                bool promoted = false;
                try
                {
                    fault?.Invoke(SaveWriteStage.Validated);
                    byte[] encodedHeader = CareerSaveCodec.Utf8.GetBytes(JsonConvert.SerializeObject(header));
                    if (encodedHeader.Length > MaximumHeader) throw CareerSaveCodec.Invalid("Header exceeds its limit.");
                    using (var stream = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var writer = new BinaryWriter(stream, CareerSaveCodec.Utf8, true))
                    {
                        fault?.Invoke(SaveWriteStage.Created);
                        writer.Write(Magic); writer.Write(encodedHeader.Length); writer.Write(encodedHeader);
                        writer.Write(Digest(encodedHeader));
                        int first = bytes.Length / 4, second = bytes.Length / 2;
                        writer.Write(bytes, 0, first); fault?.Invoke(SaveWriteStage.QuarterWritten);
                        writer.Write(bytes, first, second - first); fault?.Invoke(SaveWriteStage.HalfWritten);
                        writer.Write(bytes, second, bytes.Length - second); writer.Flush(); fault?.Invoke(SaveWriteStage.Written);
                        stream.Flush(true); fault?.Invoke(SaveWriteStage.Flushed);
                    }
                    ReadFile(staged, slot, false); fault?.Invoke(SaveWriteStage.Verified);
                    File.Move(staged, destination); // Commit point: old files have not been touched.
                    promoted = true;
                    fault?.Invoke(SaveWriteStage.Promoted);
                    Retain(slot); fault?.Invoke(SaveWriteStage.Retained);
                }
                catch (Exception exception) when (promoted)
                {
                    // A retention/observer error cannot retroactively cancel publication.
                    LastWarning = "Save committed; maintenance failed: " + exception.Message;
                }
                finally
                {
                    // Delete only this operation's uncommitted staging file. A process kill
                    // bypasses finally; such evidence is retained and never auto-promoted.
                    try { if (!promoted && File.Exists(staged)) File.Delete(staged); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    LastMilliseconds = watch.Elapsed.TotalMilliseconds;
                }
                // A failure here is an uncertain acknowledgement, not a definite non-commit.
                try { return Scan(slot); }
                catch (Exception exception)
                { throw new SaveCommitUncertainException("Generation was published, but acknowledgement failed. Reload before retrying.", exception); }
            }
        }

        public IReadOnlyList<SaveSlotInfo> Enumerate()
        {
            using (Lock())
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (string directory in Directory.EnumerateDirectories(root, "slot-*"))
                {
                    string hex = Path.GetFileName(directory).Substring(5);
                    try
                    {
                        if (hex.Length > 512 || hex.Length % 2 != 0) continue;
                        var bytes = new byte[hex.Length / 2];
                        for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                        string id = CareerSaveCodec.Utf8.GetString(bytes); ValidateSlot(id); ids.Add(id);
                    }
                    catch (ArgumentException) { }
                    catch (FormatException) { }
                    if (ids.Count > MaximumFiles) throw CareerSaveCodec.Invalid("Too many save slots to enumerate safely.");
                }
                foreach (string file in Directory.EnumerateFiles(root, "*.json").Concat(Directory.EnumerateFiles(root, "*.json.bak")))
                {
                    string name = Path.GetFileName(file);
                    string id = name.Substring(0, name.Length - (name.EndsWith(".bak", StringComparison.Ordinal) ? 9 : 5));
                    try { ValidateSlot(id); ids.Add(id); }
                    catch (ArgumentException) { continue; } // Unrelated/invalid names must not hide healthy slots.
                    if (ids.Count > MaximumFiles) throw CareerSaveCodec.Invalid("Too many save slots to enumerate safely.");
                }
                var result = new List<SaveSlotInfo>();
                foreach (string id in ids.OrderBy(value => value, StringComparer.Ordinal))
                {
                    var info = new SaveSlotInfo { Slot = id };
                    try
                    {
                        Candidate candidate = Candidates(id).FirstOrDefault();
                        if (candidate == null)
                        { info.Status = "No committed generation; inspect staged artifacts."; result.Add(info); continue; }
                        info.Header = candidate.Legacy ? ReadLegacy(candidate.Path, id).Header : ReadFile(candidate.Path, id, true).Header;
                        info.Status = candidate.Legacy ? "Legacy: validation on load" : "Header verified; payload validation on load";
                    }
                    catch (Exception exception) when (IsStorageError(exception)) { info.Status = exception.Message; }
                    result.Add(info);
                }
                return result;
            }
        }

        /// <summary>Copy validated logical state, never alias the source's physical files.</summary>
        public SaveReadResult Copy(string source, string destination)
        {
            ValidateSlot(destination);
            SaveReadResult read = Load(source);
            var profile = CareerSaveCodec.Parse(CareerSaveCodec.Migrate(source, read.Payload));
            profile["profileId"] = destination;
            // A copied playthrough owns a separate journal identity, preserving all claims.
            foreach (var receipt in (Newtonsoft.Json.Linq.JArray)profile["economy"]["receipts"])
                receipt["profileId"] = destination;
            return Commit(destination, profile.ToString(Formatting.None), null, true);
        }

        public IReadOnlyList<SaveArtifactInfo> InspectRecovery(string slot)
        {
            ValidateSlot(slot);
            using (Lock())
            {
                var result = new List<SaveArtifactInfo>();
                List<Candidate> candidates = Candidates(slot);
                string directory = SlotDirectory(slot);
                if (Directory.Exists(directory)) foreach (string path in Directory.EnumerateFiles(directory, "*.pending"))
                {
                    candidates.Add(new Candidate { Path = path });
                    if (candidates.Count > MaximumFiles) throw CareerSaveCodec.Invalid("Too many recovery artifacts to inspect at once.");
                }
                foreach (Candidate candidate in candidates)
                {
                    var info = new SaveArtifactInfo { Path = candidate.Path, Bytes = new FileInfo(candidate.Path).Length };
                    try
                    {
                        info.Header = candidate.Legacy ? ReadLegacy(candidate.Path, slot).Header : ReadFile(candidate.Path, slot, false).Header;
                        info.Status = candidate.Path.EndsWith(".pending", StringComparison.Ordinal)
                            ? "Valid staging file, NOT committed" : "Validated generation";
                    }
                    catch (Exception exception) when (IsStorageError(exception)) { info.Status = exception.Message; }
                    result.Add(info);
                }
                return result;
            }
        }

        /// <summary>Explicit rollback is a new commit, never a destructive pointer rewind.</summary>
        public SaveReadResult RecoverGeneration(string slot, long generation, string expectedHead)
        {
            ValidateSlot(slot);
            string payload;
            using (Lock())
            {
                SaveReadResult current = Scan(slot);
                if (current.HeadStamp != expectedHead) throw Conflict("Slot changed before recovery.");
                Candidate candidate = Candidates(slot).FirstOrDefault(c => c.Generation == generation);
                if (candidate == null) throw new SaveException(SaveError.Missing, "Requested generation is not retained.");
                payload = candidate.Legacy ? ReadLegacy(candidate.Path, slot).Payload : ReadFile(candidate.Path, slot, false).Payload;
            }
            return Commit(slot, CareerSaveCodec.Migrate(slot, payload), expectedHead);
        }

        /// <summary>Recoverable removal. Legacy import evidence is deliberately not moved implicitly.</summary>
        public string Archive(string slot, string expectedHead, string activeSlot)
        {
            ValidateSlot(slot);
            if (slot == activeSlot) throw Conflict("Cannot archive the active career.");
            using (Lock())
            {
                var read = Scan(slot);
                if (read.HeadStamp != expectedHead) throw Conflict("Save changed before archive; refresh the slot browser.");
                if (Candidates(slot).Any(c => c.Legacy))
                    throw Conflict("This slot retains legacy migration files. Export and review those files before archiving.");
                string archives = Path.Combine(root, ".archives"); Directory.CreateDirectory(archives);
                string target = Path.Combine(archives, Path.GetFileName(SlotDirectory(slot)) + "-" + Guid.NewGuid().ToString("N"));
                Directory.Move(SlotDirectory(slot), target);
                return target;
            }
        }

        public void RestoreArchive(string archivedPath)
        {
            string parent = Path.Combine(root, ".archives");
            string path = Path.GetFullPath(archivedPath);
            if (Path.GetDirectoryName(path) != parent) throw new ArgumentException("Select an archive from this repository.");
            string name = Path.GetFileName(path);
            if (!name.StartsWith("slot-", StringComparison.Ordinal) || name.Length < 40) throw new ArgumentException("Invalid archive name.");
            string folder = name.Substring(0, name.Length - 33);
            if (!Guid.TryParseExact(name.Substring(name.Length - 32), "N", out _)) throw new ArgumentException("Invalid archive identity.");
            string hex = folder.Substring(5);
            if (hex.Length > 512 || hex.Length % 2 != 0) throw new ArgumentException("Invalid archive slot.");
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            string slot = CareerSaveCodec.Utf8.GetString(bytes); ValidateSlot(slot);
            using (Lock())
            {
                if (Candidates(slot).Count != 0 || Directory.Exists(SlotDirectory(slot))) throw Conflict("Slot already exists; restore cannot overwrite it.");
                Directory.Move(path, SlotDirectory(slot));
            }
        }

        public static void ValidateSlot(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot) || slot.Length > 64 || slot != slot.Trim())
                throw new ArgumentException("Slot IDs must be 1–64 letters, numbers, hyphens or underscores.");
            foreach (char c in slot)
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_') throw new ArgumentException("Invalid save slot ID.");
        }

        private FileStream Lock()
        {
            Directory.CreateDirectory(root);
            return new FileStream(Path.Combine(root, ".save.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        private string SlotDirectory(string slot) => Path.Combine(root, "slot-" + BitConverter.ToString(CareerSaveCodec.Utf8.GetBytes(slot)).Replace("-", "").ToLowerInvariant());
        private sealed class Candidate { public string Path; public long Generation; public bool Legacy; }
        private List<Candidate> Candidates(string slot)
        {
            var result = new List<Candidate>();
            string directory = SlotDirectory(slot);
            if (Present(directory))
                foreach (string file in Directory.EnumerateFiles(directory, "*.save"))
                {
                    string name = Path.GetFileName(file);
                    if (name.Length != 58 || name[20] != '-' || !long.TryParse(name.Substring(0, 20), out long generation)
                        || generation < 1 || !Guid.TryParseExact(name.Substring(21, 32), "N", out _))
                        throw CareerSaveCodec.Invalid("Unrecognized generation filename; inspect the slot before writing.");
                    result.Add(new Candidate { Path = file, Generation = generation });
                    if (result.Count > MaximumFiles) throw CareerSaveCodec.Invalid("Too many generations; inspect retention failures.");
                }
            foreach (string suffix in new[] { ".json", ".json.bak" })
            {
                string file = Path.Combine(root, slot + suffix);
                if (Present(file)) result.Add(new Candidate { Path = file, Generation = 0, Legacy = true });
            }
            return result.OrderByDescending(item => item.Generation).ThenBy(item => item.Path, StringComparer.Ordinal).ToList();
        }

        private SaveReadResult Scan(string slot)
        {
            List<Candidate> candidates = Candidates(slot);
            if (candidates.Count == 0) throw new SaveException(SaveError.Missing, "No saved profile exists for this slot.");
            if (candidates.Where(c => !c.Legacy).GroupBy(c => c.Generation).Any(g => g.Count() != 1))
                throw Conflict("Divergent save generations detected; no automatic cloud merge is permitted.");
            var failures = new List<string>();
            SaveReadResult selected = null;
            SaveHeader newer = null;
            // Validate all headers for forward versions even if an earlier candidate is valid.
            foreach (Candidate candidate in candidates)
            {
                try
                {
                    SaveReadResult read = candidate.Legacy ? ReadLegacy(candidate.Path, slot) : ReadFile(candidate.Path, slot, false);
                    if (!candidate.Legacy && (read.Header.generation != candidate.Generation
                        || !candidate.Path.EndsWith("-" + read.Header.operation + ".save", StringComparison.Ordinal)))
                        throw CareerSaveCodec.Invalid("Filename and save header disagree.");
                    if (newer != null && !candidate.Legacy && newer.generation == read.Header.generation + 1
                        && newer.parent != read.Header.operation)
                        throw Conflict("Divergent parent history detected; retain both branches and resolve explicitly.");
                    if (!candidate.Legacy) newer = read.Header;
                    if (selected == null) selected = read;
                }
                catch (SaveException exception) when (exception.Error == SaveError.InvalidData)
                { if (selected == null) failures.Add(Path.GetFileName(candidate.Path) + ": " + exception.Message); }
            }
            if (selected == null) throw CareerSaveCodec.Invalid("No valid recovery generation. " + string.Join("; ", failures));
            selected.HeadStamp = Stamp(candidates);
            selected.Recovery = failures.Count == 0 ? string.Empty : "Recovered generation " + selected.Header.generation + ". " + string.Join("; ", failures);
            return selected;
        }

        private static string Stamp(List<Candidate> candidates)
        {
            using (var hash = SHA256.Create())
            using (var buffer = new MemoryStream())
            using (var writer = new BinaryWriter(buffer))
            {
                foreach (Candidate candidate in candidates)
                {
                    writer.Write(Path.GetFileName(candidate.Path));
                    using (var stream = new FileStream(candidate.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        writer.Write(stream.Length);
                        if (stream.Length > CareerSaveCodec.MaximumBytes + MaximumHeader + 44)
                        {
                            byte[] prefix = new byte[MaximumHeader];
                            int count = stream.Read(prefix, 0, prefix.Length);
                            writer.Write(hash.ComputeHash(prefix, 0, count));
                        }
                        else writer.Write(hash.ComputeHash(stream));
                    }
                }
                writer.Flush(); return Hash(buffer.ToArray());
            }
        }

        private SaveReadResult ReadFile(string path, string slot, bool headerOnly)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(stream, CareerSaveCodec.Utf8, true))
                {
                    if (stream.Length < 44 || stream.Length > CareerSaveCodec.MaximumBytes + MaximumHeader + 44)
                        throw CareerSaveCodec.Invalid("Truncated or oversized container.");
                    if (!reader.ReadBytes(8).SequenceEqual(Magic)) throw CareerSaveCodec.Invalid("Invalid save magic.");
                    int size = reader.ReadInt32();
                    if (size < 2 || size > MaximumHeader || stream.Length < 44L + size) throw CareerSaveCodec.Invalid("Invalid header size.");
                    byte[] headerBytes = reader.ReadBytes(size);
                    if (!reader.ReadBytes(32).SequenceEqual(Digest(headerBytes))) throw CareerSaveCodec.Invalid("Header checksum mismatch.");
                    var json = CareerSaveCodec.Parse(CareerSaveCodec.Utf8.GetString(headerBytes));
                    if (!int.TryParse(json["format"]?.ToString(), out int format) || format < 1) throw CareerSaveCodec.Invalid("Missing format.");
                    if (format > 1) throw new SaveException(SaveError.UnsupportedVersion, "Save container is newer than this build.");
                    SaveHeader header = json.ToObject<SaveHeader>();
                    if (header.schema > CareerProfileData.CurrentVersion) throw new SaveException(SaveError.UnsupportedVersion, "Save schema is newer than this build.");
                    if (header.schema < 1 || header.slot != slot || header.generation < 1 || header.utcTicks < 0 || header.utcTicks > DateTime.MaxValue.Ticks
                        || !Guid.TryParseExact(header.operation, "N", out _) || header.parent == null
                        || (header.parent.Length != 0 && !Guid.TryParseExact(header.parent, "N", out _))
                        || header.displayName == null || header.displayName.Length > 128 || header.payloadBytes < 0
                        || header.payloadBytes > CareerSaveCodec.MaximumBytes || header.payloadHash == null || header.payloadHash.Length != 64
                        || stream.Length != 44L + size + header.payloadBytes) throw CareerSaveCodec.Invalid("Invalid save header fields.");
                    if (headerOnly) return new SaveReadResult { Header = header };
                    byte[] payload = reader.ReadBytes(header.payloadBytes);
                    if (stream.Position != stream.Length) throw CareerSaveCodec.Invalid("Container changed while it was being read.");
                    if (Hash(payload) != header.payloadHash) throw CareerSaveCodec.Invalid("Payload checksum mismatch.");
                    string text = CareerSaveCodec.Utf8.GetString(payload);
                    if (ValidateCached(slot, text, header.payloadHash) != header.schema) throw CareerSaveCodec.Invalid("Header/profile schema mismatch.");
                    return new SaveReadResult { Header = header, Payload = text };
                }
            }
            catch (EndOfStreamException) { throw CareerSaveCodec.Invalid("Truncated save container."); }
            catch (DecoderFallbackException) { throw CareerSaveCodec.Invalid("Invalid UTF-8 save data."); }
            catch (JsonException exception) { throw CareerSaveCodec.Invalid("Malformed header: " + exception.Message); }
        }

        private SaveReadResult ReadLegacy(string path, string slot)
        {
            string text;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(stream, CareerSaveCodec.Utf8, true))
                {
                    if (stream.Length > CareerSaveCodec.MaximumBytes) throw CareerSaveCodec.Invalid("Legacy file exceeds its size limit.");
                    int expected = (int)stream.Length;
                    byte[] bytes = reader.ReadBytes(expected);
                    if (bytes.Length != expected || stream.Position != stream.Length) throw CareerSaveCodec.Invalid("Legacy file changed while reading.");
                    text = CareerSaveCodec.Utf8.GetString(bytes);
                    if (text.Length != 0 && text[0] == '\uFEFF') text = text.Substring(1);
                }
            }
            catch (DecoderFallbackException) { throw CareerSaveCodec.Invalid("Invalid UTF-8 legacy save."); }
            int schema = ValidateCached(slot, text, Hash(CareerSaveCodec.Utf8.GetBytes(text)));
            return new SaveReadResult { Payload = text, Header = new SaveHeader
            { format = 0, schema = schema, slot = slot, generation = 0, operation = string.Empty, displayName = slot } };
        }
        private void Retain(string slot)
        {
            int valid = 0;
            foreach (Candidate candidate in Candidates(slot))
            {
                if (candidate.Legacy) continue; // Original pre-container files are never rotated away.
                try
                {
                    SaveReadResult read = ReadFile(candidate.Path, slot, false);
                    if (read.Header.schema < CareerProfileData.CurrentVersion) continue; // Pre-migration evidence.
                    if (++valid > retention) File.Delete(candidate.Path);
                }
                catch (SaveException exception) when (exception.Error == SaveError.InvalidData) { }
            }
        }
        private int ValidateCached(string slot, string text, string digest)
        {
            string key = slot + ":" + digest;
            if (validatedPayloads.TryGetValue(key, out int schema)) return schema;
            schema = CareerSaveCodec.Validate(slot, text);
            RememberValidated(key, schema);
            return schema;
        }
        private void RememberValidated(string key, int schema)
        {
            if (validatedPayloads.Count >= 64) validatedPayloads.Clear();
            validatedPayloads[key] = schema;
        }
        private static byte[] Digest(byte[] data) { using (var sha = SHA256.Create()) return sha.ComputeHash(data); }
        private static bool Present(string path)
        {
            try { File.GetAttributes(path); return true; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }
        private static string Hash(byte[] data) => BitConverter.ToString(Digest(data)).Replace("-", "").ToLowerInvariant();
        private static SaveException Conflict(string message) => new SaveException(SaveError.Conflict, message);
        internal static bool IsStorageError(Exception exception) => exception is IOException || exception is UnauthorizedAccessException
            || exception is ArgumentException || exception is OverflowException;
    }
}
