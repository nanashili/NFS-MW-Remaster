using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    /// <summary>Only explicitly attached roots. Reject symlinks instead of trusting lexical containment.</summary>
    public static class BlackBoxSourceAccess
    {
        public const int MaximumFileBytes = 128 * 1024 * 1024, MaximumFiles = 256;
        private static readonly HashSet<string> extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".gin", ".abk", ".bnk", ".ast", ".tmx", ".snr", ".sns", ".sps", ".wav", ".nfsms", ".json" };

        public static string Resolve(string approvedRoot, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(approvedRoot) || string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                throw new InvalidDataException("Choose an approved folder and a relative source path.");
            string root = CanonicalSystemPath(approvedRoot).TrimEnd(Path.DirectorySeparatorChar);
            string path = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("Source escapes its approved root.");
            RejectLinks(root);
            RejectLinks(path);
            return path;
        }

        private static void RejectLinks(string path)
        {
            for (string current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(current); }
                catch (FileNotFoundException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Symbolic links are not accepted as source roots or dependencies: " + current);
            }
        }

        public static byte[] Read(BlackBoxSource source, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            string path = Resolve(source.root, source.relativePath);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length < 1 || stream.Length > MaximumFileBytes) throw new InvalidDataException("Source exceeds the 128 MiB per-file budget or is empty.");
                var bytes = new byte[(int)stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int count = stream.Read(bytes, read, Math.Min(65536, bytes.Length - read));
                    if (count == 0) throw new EndOfStreamException("Source changed during inspection.");
                    read += count;
                }
                if (stream.Position != stream.Length) throw new IOException("Source changed during inspection.");
                return bytes;
            }
        }

        public static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public static BlackBoxSource[] Discover(string approvedRoot, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            string root = CanonicalSystemPath(approvedRoot).TrimEnd(Path.DirectorySeparatorChar);
            RejectLinks(root);
            var found = new List<BlackBoxSource>();
            var folders = new Stack<Tuple<string, int>>();
            folders.Push(Tuple.Create(root, 0));
            int visited = 0;
            long total = 0;
            while (folders.Count > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                var folder = folders.Pop();
                if (++visited > 4096 || folder.Item2 > 32) throw new InvalidDataException("Folder scan exceeds depth/directory budget; select a narrower root.");
                foreach (string path in Directory.EnumerateFileSystemEntries(folder.Item1))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var flags = File.GetAttributes(path);
                    if ((flags & FileAttributes.ReparsePoint) != 0) continue;
                    if ((flags & FileAttributes.Directory) != 0) { folders.Push(Tuple.Create(path, folder.Item2 + 1)); continue; }
                    if (!extensions.Contains(Path.GetExtension(path))) continue;
                    long length = new FileInfo(path).Length;
                    if (length > MaximumFileBytes || (total += length) > 512L * 1024 * 1024 || found.Count >= MaximumFiles)
                        throw new InvalidDataException("Selected set exceeds 256 sources / 128 MiB per file / 512 MiB total; select a narrower root.");
                    found.Add(new BlackBoxSource { root = root, relativePath = path.Substring(root.Length + 1) });
                }
            }
            found.Sort((a, b) => string.CompareOrdinal(a.relativePath, b.relativePath));
            return found.ToArray();
        }
        // macOS exposes its OS-owned temporary directories through /var and /tmp aliases.
        // Canonicalize those prefixes only; user-created links at/below the root remain rejected.
        private static string CanonicalSystemPath(string path)
        {
            string full = Path.GetFullPath(path);
#if UNITY_EDITOR_OSX
            if (full.StartsWith("/var/", StringComparison.Ordinal) || full == "/var" || full.StartsWith("/tmp/", StringComparison.Ordinal) || full == "/tmp") full = "/private" + full;
#endif
            return full;
        }
    }
}
