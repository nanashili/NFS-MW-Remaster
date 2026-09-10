using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.WorldValidation
{
    [Serializable]
    public sealed class WorldValidationSuppression
    {
        public string ruleId = string.Empty;
        public string affectedId = string.Empty;
        public string sourceRevision = string.Empty;
        public string scopePath = string.Empty;
        public string reason = string.Empty;
        public string author = string.Empty;
        public string createdUtc = string.Empty;
        public string expiresUtc = string.Empty;

        public bool Matches(WorldValidationResult result)
        {
            if (result == null || !string.Equals(ruleId, result.ruleId, StringComparison.Ordinal)) return false;
            if (!string.IsNullOrEmpty(affectedId)
                && !(result.affectedIds ?? Array.Empty<string>()).Contains(affectedId, StringComparer.Ordinal)) return false;
            if (!string.IsNullOrEmpty(sourceRevision)
                && !string.Equals(sourceRevision, result.sourceRevision, StringComparison.Ordinal)) return false;
            if (!string.IsNullOrEmpty(scopePath)
                && !string.Equals(scopePath, result.assetPath, StringComparison.Ordinal)
                && !string.Equals(scopePath, result.scenePath, StringComparison.Ordinal)) return false;
            if (string.IsNullOrEmpty(expiresUtc)) return true;
            return !DateTime.TryParse(expiresUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime expiry)
                || DateTime.UtcNow <= expiry.ToUniversalTime();
        }
    }

    [Serializable]
    public sealed class WorldValidationBaselineEntry
    {
        public string key = string.Empty;
        public string ruleId = string.Empty;
        public string sourceRevision = string.Empty;
        public string note = string.Empty;
        public string createdUtc = string.Empty;
    }

    /// <summary>
    /// Explicit, reviewable policy for budgets, debt and targeted waivers.
    /// Policies are editor data; they never alter runtime saves or gameplay.
    /// </summary>
    [CreateAssetMenu(menuName = "NFS MW Remaster/Validation/World Validation Policy", fileName = "WorldValidationPolicy")]
    public sealed class WorldValidationPolicy : ScriptableObject
    {
        public const int CurrentSchema = 1;

        public int schema = CurrentSchema;
        public int maximumTextureMegabytes = 64;
        public int maximumMeshVertices = 200000;
        public int maximumLoadedSceneObjects = 250000;
        public WorldValidationSuppression[] suppressions = Array.Empty<WorldValidationSuppression>();
        public WorldValidationBaselineEntry[] baselines = Array.Empty<WorldValidationBaselineEntry>();

        public static WorldValidationPolicy Find(string preferredPath = "")
        {
            if (!string.IsNullOrEmpty(preferredPath))
            {
                var preferred = AssetDatabase.LoadAssetAtPath<WorldValidationPolicy>(preferredPath);
                if (preferred != null) return preferred;
            }

            string[] guids = AssetDatabase.FindAssets("t:WorldValidationPolicy");
            foreach (string guid in guids.OrderBy(value => value, StringComparer.Ordinal))
            {
                var policy = AssetDatabase.LoadAssetAtPath<WorldValidationPolicy>(AssetDatabase.GUIDToAssetPath(guid));
                if (policy != null) return policy;
            }
            return null;
        }

        public static WorldValidationPolicy CreateDefaultAsset()
        {
            const string root = "Assets/NfsMw/Modules/Driving/Data";
            const string folder = root + "/WorldValidation";
            EnsureFolder("Assets/NfsMw/Modules/Driving");
            EnsureFolder(root);
            EnsureFolder(folder);

            string path = folder + "/WorldValidationPolicy.asset";
            var existing = AssetDatabase.LoadAssetAtPath<WorldValidationPolicy>(path);
            if (existing != null) return existing;

            var policy = CreateInstance<WorldValidationPolicy>();
            AssetDatabase.CreateAsset(policy, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return policy;
        }

        public WorldValidationReport Apply(WorldValidationReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));

            var baselinesByKey = (baselines ?? Array.Empty<WorldValidationBaselineEntry>())
                .Where(entry => entry != null && !string.IsNullOrEmpty(entry.key))
                .GroupBy(entry => entry.key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var results = new List<WorldValidationResult>();

            foreach (WorldValidationResult source in report.results ?? Array.Empty<WorldValidationResult>())
            {
                if (source == null) continue;
                var result = source.Clone();
                if (result.status != WorldValidationStatus.Passed
                    && result.status != WorldValidationStatus.NotEvaluated
                    && result.status != WorldValidationStatus.Unsupported
                    && result.status != WorldValidationStatus.Cancelled)
                {
                    WorldValidationSuppression suppression = (suppressions ?? Array.Empty<WorldValidationSuppression>())
                        .FirstOrDefault(value => value != null && value.Matches(result));
                    if (suppression != null)
                    {
                        result.originalStatus = result.status.ToString();
                        result.status = WorldValidationStatus.Suppressed;
                        result.severity = WorldValidationSeverity.Info;
                        result.message = result.message + " Suppressed: " + suppression.reason;
                    }

                    if (baselinesByKey.TryGetValue(result.Key, out WorldValidationBaselineEntry baseline))
                    {
                        seen.Add(result.Key);
                        bool revisionMatches = string.IsNullOrEmpty(baseline.sourceRevision)
                            || string.Equals(baseline.sourceRevision, result.sourceRevision, StringComparison.Ordinal);
                        result.baselineState = revisionMatches
                            ? WorldValidationBaselineState.ExistingDebt
                            : WorldValidationBaselineState.New;
                    }
                    else
                    {
                        result.baselineState = WorldValidationBaselineState.New;
                    }
                }
                results.Add(result);
            }

            report.results = results.ToArray();
            report.resolvedBaselineKeys = baselinesByKey.Keys
                .Where(key => !seen.Contains(key))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
            report.NormalizeAndSummarize();
            return report;
        }

        public void AddSuppression(WorldValidationResult result, string scopePath, string reason, string author = "")
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A suppression reason is required.", nameof(reason));
            var list = new List<WorldValidationSuppression>(suppressions ?? Array.Empty<WorldValidationSuppression>())
            {
                new WorldValidationSuppression
                {
                    ruleId = result.ruleId,
                    affectedId = result.affectedIds != null && result.affectedIds.Length > 0 ? result.affectedIds[0] : string.Empty,
                    sourceRevision = result.sourceRevision ?? string.Empty,
                    scopePath = scopePath ?? string.Empty,
                    reason = reason.Trim(),
                    author = author ?? string.Empty,
                    createdUtc = DateTime.UtcNow.ToString("O"),
                    expiresUtc = string.Empty
                }
            };
            suppressions = list.ToArray();
        }

        public void AddBaseline(WorldValidationResult result, string note = "")
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            var list = new List<WorldValidationBaselineEntry>(baselines ?? Array.Empty<WorldValidationBaselineEntry>());
            list.RemoveAll(entry => entry != null && string.Equals(entry.key, result.Key, StringComparison.Ordinal));
            list.Add(new WorldValidationBaselineEntry
            {
                key = result.Key,
                ruleId = result.ruleId,
                sourceRevision = result.sourceRevision ?? string.Empty,
                note = note ?? string.Empty,
                createdUtc = DateTime.UtcNow.ToString("O")
            });
            baselines = list.OrderBy(entry => entry.key, StringComparer.Ordinal).ToArray();
        }

        public static int DefaultTextureBytes => 64 * 1024 * 1024;
        public static int DefaultMeshVertices => 200000;

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name)) return;
            EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
