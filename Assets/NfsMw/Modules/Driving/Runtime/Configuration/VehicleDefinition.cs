using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Flags]
    public enum VehicleCapabilities
    {
        None = 0, Lighting = 1, Glass = 2, Mirrors = 4, Cockpit = 8,
        Wipers = 16, Windows = 32, Nitrous = 64, Induction = 128,
        All = Lighting | Glass | Mirrors | Cockpit | Wipers | Windows | Nitrous | Induction
    }

    [Serializable]
    public struct VehicleTuningAdjustment
    {
        // This enum lives in the runtime assembly. Its existing numeric values remain save compatible.
        public VehiclePhysicsLabTuningParameter parameter;
        public float value;
    }

    [Serializable]
    public struct VehicleTuningLimit
    {
        public VehiclePhysicsLabTuningParameter parameter;
        public float minimum, maximum;
    }

    /// <summary>Shared authoring data. Mutable installed state belongs to the components on each instance.</summary>
    [CreateAssetMenu(menuName = "NFS MW Remaster/Driving/Vehicle Definition", fileName = "VehicleDefinition")]
    public sealed class VehicleDefinition : ScriptableObject
    {
        public string vehicleId = string.Empty;
        public string variantId = "stock";
        public string manufacturer = string.Empty;
        public string model = string.Empty;
        [Range(1886, 2200)] public int year = 2005;
        public VehicleTuning factoryTuning;
        public VehicleController prefab;
        public VehiclePerformanceCatalog performanceCatalog;
        public VehicleCustomizationCatalog customizationCatalog;
        public VehicleCapabilities capabilities = VehicleCapabilities.Lighting;
        public string[] supportedSlots = Array.Empty<string>();
        [Tooltip("Optional author limits, intersected with installed part limits and runtime safety ranges.")]
        public VehicleTuningLimit[] tuningLimits = Array.Empty<VehicleTuningLimit>();
        [TextArea] public string referenceEvidence = "Original approximation; no measured reference-game calibration.";

        public bool Supports(VehicleCapabilities capability) => (capabilities & capability) == capability;

        public bool Validate(out string failure)
        {
            if (string.IsNullOrWhiteSpace(vehicleId) || string.IsNullOrWhiteSpace(variantId))
            { failure = "Definition: assign a stable vehicle ID and variant ID."; return false; }
            if (year < 1886 || year > 2200) { failure = "Definition: year must be between 1886 and 2200."; return false; }
            try { RacingLineSnapshot.ValidateTuning(factoryTuning); }
            catch (ArgumentException error) { failure = "Definition factory tuning: " + error.Message; return false; }
            var slots = new HashSet<string>(StringComparer.Ordinal);
            foreach (string slot in supportedSlots ?? Array.Empty<string>())
                if (string.IsNullOrWhiteSpace(slot) || !slots.Add(slot))
                { failure = "Definition mounting slots: use unique, non-empty semantic IDs."; return false; }
            var parameters = new HashSet<VehiclePhysicsLabTuningParameter>();
            foreach (var limit in tuningLimits ?? Array.Empty<VehicleTuningLimit>())
                if (!parameters.Add(limit.parameter) || !VehiclePhysicsLabTuningOverrides.TryGetRange(limit.parameter, out float min, out float max)
                    || !float.IsFinite(limit.minimum) || !float.IsFinite(limit.maximum)
                    || limit.minimum < min || limit.maximum > max || limit.minimum > limit.maximum)
                { failure = "Definition tuning limit " + limit.parameter + ": use one finite interval within runtime safety bounds."; return false; }
            failure = string.Empty; return true;
        }
    }
}
