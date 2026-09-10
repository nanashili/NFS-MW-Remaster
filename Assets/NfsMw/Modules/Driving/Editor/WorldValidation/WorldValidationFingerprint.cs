using System;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.WorldValidation
{
    /// <summary>
    /// Editor-only source revision helpers. Revisions deliberately use Unity's
    /// dependency database so a rule can invalidate when a referenced asset
    /// changes without inventing a second content hashing scheme.
    /// </summary>
    public static class WorldValidationFingerprint
    {
        public static string AssetRevision(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                return string.Empty;

            try
            {
                if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
                    return "missing:" + AssetDatabase.AssetPathToGUID(assetPath);

                Hash128 dependencyHash = AssetDatabase.GetAssetDependencyHash(assetPath);
                string value = dependencyHash.ToString();
                if (!string.IsNullOrEmpty(value)) return value;
                return "guid:" + AssetDatabase.AssetPathToGUID(assetPath);
            }
            catch (Exception exception)
            {
                return "unavailable:" + exception.GetType().Name;
            }
        }

        public static string RuleRevision(WorldValidationRuleDescriptor descriptor, string sourceRevision)
        {
            if (descriptor == null) return Hash128.Compute(sourceRevision ?? string.Empty).ToString();
            return Hash128.Compute(descriptor.id + "|" + descriptor.version + "|" + (sourceRevision ?? string.Empty)).ToString();
        }
    }
}
