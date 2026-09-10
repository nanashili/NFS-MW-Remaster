using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.WorldValidation
{
    [Serializable]
    internal sealed class WorldValidationDependencyIndex
    {
        public int schema = 2;
        public WorldValidationDependencyEntry[] entries = Array.Empty<WorldValidationDependencyEntry>();
    }

    [Serializable]
    internal sealed class WorldValidationDependencyEntry
    {
        public string path = string.Empty;
        public string revision = string.Empty;
        public string[] dependencies = Array.Empty<string>();
    }

    /// <summary>Conservative project dependency index used only for invalidation.</summary>
    public static class WorldValidationDependencyCache
    {
        private static string RootPath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "NfsMwRemaster", "WorldValidation");
        private static string IndexPath => Path.Combine(RootPath, "dependency-index.json");

        public static bool HasIndex => File.Exists(IndexPath);

        public static IReadOnlyList<string> GetChangedPaths()
        {
            if (!TryLoad(out WorldValidationDependencyIndex index)) return Array.Empty<string>();
            string[] currentPaths = CurrentAssetPaths().ToArray();
            var currentSet = new HashSet<string>(currentPaths, StringComparer.Ordinal);
            var previous = (index.entries ?? Array.Empty<WorldValidationDependencyEntry>())
                .Where(entry => entry != null && !string.IsNullOrEmpty(entry.path))
                .GroupBy(entry => entry.path, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var changed = new HashSet<string>(StringComparer.Ordinal);

            // A pre-v2 index has no reverse edges. Treat all current inputs as
            // changed once so the next update cannot silently under-scan.
            if (index.schema < 2)
                foreach (string path in currentPaths) changed.Add(path);

            foreach (string path in currentPaths)
            {
                string current = WorldValidationFingerprint.AssetRevision(path);
                if (!previous.TryGetValue(path, out WorldValidationDependencyEntry prior)
                    || !string.Equals(prior.revision ?? string.Empty, current, StringComparison.Ordinal))
                    changed.Add(path);
            }

            // Deleted inputs are changes too. A dependent scene or generated
            // asset must be revisited when one of its old dependencies vanishes.
            foreach (string path in previous.Keys)
                if (!currentSet.Contains(path)) changed.Add(path);

            // Entries store the transitive AssetDatabase dependency set. The
            // fixed point loop also handles older/custom indexes that recorded
            // only direct edges.
            bool expanded;
            do
            {
                expanded = false;
                foreach (WorldValidationDependencyEntry entry in previous.Values)
                {
                    if (entry == null || changed.Contains(entry.path)) continue;
                    if ((entry.dependencies ?? Array.Empty<string>()).Any(changed.Contains))
                        expanded |= changed.Add(entry.path);
                }
            } while (expanded);

            return changed.Where(currentSet.Contains).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }

        public static bool TryUpdate(out string failure)
        {
            failure = string.Empty;
            try
            {
                Directory.CreateDirectory(RootPath);
                var entries = CurrentAssetPaths()
                    .Select(path => new WorldValidationDependencyEntry
                    {
                        path = path,
                        revision = WorldValidationFingerprint.AssetRevision(path),
                        dependencies = DependenciesFor(path)
                    })
                    .ToArray();
                string json = JsonUtility.ToJson(new WorldValidationDependencyIndex { entries = entries }, true);
                File.WriteAllText(IndexPath, json);
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
        }

        private static bool TryLoad(out WorldValidationDependencyIndex index)
        {
            index = null;
            try
            {
                if (!File.Exists(IndexPath)) return false;
                index = JsonUtility.FromJson<WorldValidationDependencyIndex>(File.ReadAllText(IndexPath));
                return index != null;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<string> CurrentAssetPaths()
        {
            return AssetDatabase.GetAllAssetPaths()
                .Where(path => !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal)
                    && !AssetDatabase.IsValidFolder(path))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal);
        }

        private static string[] DependenciesFor(string path)
        {
            try
            {
                return AssetDatabase.GetDependencies(path, true)
                    .Where(value => !string.IsNullOrEmpty(value) && value.StartsWith("Assets/", StringComparison.Ordinal)
                        && !AssetDatabase.IsValidFolder(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
            }
            catch
            {
                // A missing dependency edge is conservative for consumers of
                // this cache: the owning asset still appears as changed via
                // its own revision on the next run.
                return Array.Empty<string>();
            }
        }
    }
}
