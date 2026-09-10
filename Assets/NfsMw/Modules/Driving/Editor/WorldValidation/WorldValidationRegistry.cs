using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace NfsMwRemaster.Driving.Editor.WorldValidation
{
    /// <summary>Stable extension point for domain-owned editor validation rules.</summary>
    public static class WorldValidationRuleRegistry
    {
        private static readonly Dictionary<string, IWorldValidationRule> Rules =
            new Dictionary<string, IWorldValidationRule>(StringComparer.Ordinal);
        private static readonly Dictionary<string, IWorldValidationFixProvider> Fixes =
            new Dictionary<string, IWorldValidationFixProvider>(StringComparer.Ordinal);
        private static readonly List<string> Errors = new List<string>();

        public static IReadOnlyList<IWorldValidationRule> All => Rules.Values
            .OrderBy(rule => rule.Descriptor.category)
            .ThenBy(rule => rule.Descriptor.id, StringComparer.Ordinal)
            .ToArray();

        public static IReadOnlyList<IWorldValidationFixProvider> FixProviders => Fixes.Values
            .OrderBy(provider => provider.Id, StringComparer.Ordinal)
            .ToArray();

        public static IReadOnlyList<string> RegistrationErrors => Errors.ToArray();

        public static bool Register(IWorldValidationRule rule)
        {
            if (rule == null || rule.Descriptor == null)
            {
                Errors.Add("A validation provider attempted to register a null rule or descriptor.");
                return false;
            }

            WorldValidationRuleDescriptor descriptor = rule.Descriptor;
            if (string.IsNullOrWhiteSpace(descriptor.id) || descriptor.version < 1 || string.IsNullOrWhiteSpace(descriptor.displayName))
            {
                Errors.Add("Invalid validation descriptor: rule IDs, versions and display names are required.");
                return false;
            }
            if (Rules.ContainsKey(descriptor.id))
            {
                Errors.Add("Duplicate validation rule ID: " + descriptor.id + ". The first registered owner remains authoritative.");
                return false;
            }
            Rules.Add(descriptor.id, rule);
            return true;
        }

        public static bool RegisterFixProvider(IWorldValidationFixProvider provider)
        {
            if (provider == null || string.IsNullOrWhiteSpace(provider.Id))
            {
                Errors.Add("A validation provider attempted to register an invalid fix provider.");
                return false;
            }
            if (Fixes.ContainsKey(provider.Id))
            {
                Errors.Add("Duplicate validation fix ID: " + provider.Id + ".");
                return false;
            }
            Fixes.Add(provider.Id, provider);
            return true;
        }

        public static bool TryGet(string id, out IWorldValidationRule rule)
        {
            rule = null;
            return !string.IsNullOrEmpty(id) && Rules.TryGetValue(id, out rule);
        }

        public static bool TryGetFix(string id, out IWorldValidationFixProvider provider)
        {
            provider = null;
            return !string.IsNullOrEmpty(id) && Fixes.TryGetValue(id, out provider);
        }

        /// <summary>
        /// Gets a read-only, provider-owned repair preview. The dashboard does
        /// not infer edits from a diagnostic and never treats a missing or
        /// malformed preview as permission to mutate project content.
        /// </summary>
        public static bool TryPreviewFix(WorldValidationResult result,
            out WorldValidationFixPreview preview, out string failure)
        {
            preview = null;
            failure = string.Empty;
            if (result == null)
            {
                failure = "No validation result was supplied.";
                return false;
            }
            if (!result.canFix || string.IsNullOrWhiteSpace(result.fixId))
            {
                failure = "This result does not advertise a provider-owned safe fix.";
                return false;
            }
            if (!TryGetFix(result.fixId, out IWorldValidationFixProvider provider))
            {
                failure = "The fix provider is not registered: " + result.fixId;
                return false;
            }

            try
            {
                if (!provider.CanHandle(result))
                {
                    failure = "The registered fix provider cannot handle this result.";
                    return false;
                }
                preview = provider.Preview(result);
                if (preview == null)
                {
                    failure = "The fix provider returned no preview.";
                    return false;
                }
                if (!string.Equals(preview.fixId, provider.Id, StringComparison.Ordinal))
                {
                    failure = "The fix preview ID does not match its provider ID.";
                    preview = null;
                    return false;
                }
                if (string.IsNullOrWhiteSpace(preview.title)
                    || string.IsNullOrWhiteSpace(preview.rationale)
                    || string.IsNullOrWhiteSpace(preview.risk)
                    || string.IsNullOrWhiteSpace(preview.rollback))
                {
                    failure = "The fix preview must include a title, rationale, risk and rollback strategy.";
                    preview = null;
                    return false;
                }
                preview.affectedIds = (preview.affectedIds ?? Array.Empty<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                return true;
            }
            catch (Exception exception)
            {
                preview = null;
                failure = "The fix preview failed: " + exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        internal static void ResetForTests()
        {
            Rules.Clear();
            Fixes.Clear();
            Errors.Clear();
        }
    }

    [InitializeOnLoad]
    internal static class WorldValidationBuiltInRegistration
    {
        static WorldValidationBuiltInRegistration()
        {
            WorldValidationRuleRegistry.Register(new WorldValidationIdentityRule());
            WorldValidationRuleRegistry.Register(new WorldValidationRoadRule());
            WorldValidationRuleRegistry.Register(new WorldValidationRaceRouteRule());
            WorldValidationRuleRegistry.Register(new WorldValidationEventPlacementRule());
            WorldValidationRuleRegistry.Register(new WorldValidationMissionCareerRule());
            WorldValidationRuleRegistry.Register(new WorldValidationTrafficPoliceRule());
            WorldValidationRuleRegistry.Register(new WorldValidationWorldArtRule());
            WorldValidationRuleRegistry.Register(new WorldValidationCityRule());
            WorldValidationRuleRegistry.Register(new WorldValidationRuntimeScenarioRule());
        }
    }
}
