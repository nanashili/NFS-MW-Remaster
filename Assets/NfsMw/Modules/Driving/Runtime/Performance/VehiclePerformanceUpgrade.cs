using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// The seven performance-shop slots used by the 2005-style career loop.
    /// New categories can be added without changing VehicleController.
    /// </summary>
    public enum VehiclePerformanceCategory
    {
        Engine,
        Transmission,
        ForcedInduction,
        Nitrous,
        Tires,
        Brakes,
        Suspension,
        Clutch,
        Differential,
        WeightReduction,
        Aerodynamics
    }

    public enum VehiclePerformanceTier
    {
        Stock,
        Street,
        Pro,
        Super,
        Ultimate,
        Junkman
    }

    /// <summary>
    /// The narrow interface at the upgrade seam. A custom upgrade can be a
    /// ScriptableObject, a career reward, a network payload, or a test fake.
    /// It only needs identity, shop metadata, and a tuning transformation.
    /// </summary>
    public interface IVehiclePerformanceUpgrade
    {
        string UpgradeId { get; }

        string DisplayName { get; }

        VehiclePerformanceCategory Category { get; }

        VehiclePerformanceTier Tier { get; }

        int Price { get; }

        bool IsJunkman { get; }

        void Apply(VehicleTuning tuning);
    }

    /// <summary>Optional compatibility metadata; legacy test/content implementations remain valid.</summary>
    public interface IVehiclePerformancePartMetadata
    {
        IReadOnlyList<string> CompatibleVehicleIds { get; }
        IReadOnlyList<string> CompatibleVariantIds { get; }
        IReadOnlyList<string> RequiredUpgradeIds { get; }
        IReadOnlyList<string> ExcludedUpgradeIds { get; }
        IReadOnlyList<VehicleTuningLimit> TuningLimits { get; }
        VehicleCapabilities RequiredCapabilities { get; }
    }

    /// <summary>
    /// Base class for asset-backed upgrades. Derive from this when a future
    /// part needs behaviour outside the built-in numeric modifier set.
    /// </summary>
    public abstract class VehiclePerformanceUpgradeDefinition :
        ScriptableObject,
        IVehiclePerformanceUpgrade,
        IVehicleStoreProduct,
        IVehiclePerformancePartMetadata
    {
        [SerializeField] private string upgradeId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private VehiclePerformanceCategory category;
        [SerializeField] private VehiclePerformanceTier tier = VehiclePerformanceTier.Street;
        [SerializeField, Min(0)] private int price;
        [SerializeField] private bool junkman;
        [SerializeField] private string[] compatibleVehicleIds = Array.Empty<string>();
        [SerializeField] private string[] compatibleVariantIds = Array.Empty<string>();
        [SerializeField] private string[] requiredUpgradeIds = Array.Empty<string>();
        [SerializeField] private string[] excludedUpgradeIds = Array.Empty<string>();
        [SerializeField] private VehicleTuningLimit[] tuningLimits = Array.Empty<VehicleTuningLimit>();
        [SerializeField] private VehicleCapabilities requiredCapabilities;

        public string UpgradeId
        {
            get
            {
                return string.IsNullOrWhiteSpace(upgradeId)
                    ? name
                    : upgradeId;
            }
        }

        public string DisplayName
        {
            get
            {
                return string.IsNullOrWhiteSpace(displayName)
                    ? Category + " " + Tier
                    : displayName;
            }
        }

        public VehiclePerformanceCategory Category
        {
            get { return category; }
        }

        public VehiclePerformanceTier Tier
        {
            get { return tier; }
        }

        public int Price
        {
            get { return Mathf.Max(0, price); }
        }

        public string ProductId
        {
            get { return UpgradeId; }
        }

        public VehicleStoreCategory StoreCategory
        {
            get { return VehicleStoreCategory.PerformanceShop; }
        }

        public VehicleStoreProductKind ProductKind
        {
            get { return VehicleStoreProductKind.PerformanceUpgrade; }
        }

        public bool IsAvailable
        {
            get { return true; }
        }

        public bool IsJunkman
        {
            get { return junkman || tier == VehiclePerformanceTier.Junkman; }
        }

        public IReadOnlyList<string> CompatibleVehicleIds => compatibleVehicleIds ?? Array.Empty<string>();
        public IReadOnlyList<string> CompatibleVariantIds => compatibleVariantIds ?? Array.Empty<string>();
        public IReadOnlyList<string> RequiredUpgradeIds => requiredUpgradeIds ?? Array.Empty<string>();
        public IReadOnlyList<string> ExcludedUpgradeIds => excludedUpgradeIds ?? Array.Empty<string>();
        public IReadOnlyList<VehicleTuningLimit> TuningLimits => tuningLimits ?? Array.Empty<VehicleTuningLimit>();
        public VehicleCapabilities RequiredCapabilities => requiredCapabilities;

        public void ConfigureMetadata(
            string configuredId,
            string configuredDisplayName,
            VehiclePerformanceCategory configuredCategory,
            VehiclePerformanceTier configuredTier,
            int configuredPrice,
            bool configuredJunkman)
        {
            upgradeId = configuredId;
            displayName = configuredDisplayName;
            category = configuredCategory;
            tier = configuredTier;
            price = Mathf.Max(0, configuredPrice);
            junkman = configuredJunkman;
        }

        public abstract void Apply(VehicleTuning tuning);
    }

    /// <summary>
    /// Numeric effects are deliberately additive to the tuning model rather
    /// than hardcoded into the controller. Multipliers compose cleanly across
    /// tiers and leave room for custom upgrade implementations.
    /// </summary>
    [Serializable]
    public sealed class VehiclePerformanceModifier
    {
        [Min(0.1f)] public float massMultiplier = 1f;
        [Min(0.1f)] public float maxSpeedMultiplier = 1f;

        [Min(0.1f)] public float engineTorqueMultiplier = 1f;
        [Min(0.1f)] public float engineRedlineMultiplier = 1f;
        [Min(0.1f)] public float engineInertiaMultiplier = 1f;
        [Min(0.1f)] public float peakTorqueRpmMultiplier = 1f;

        [Min(0.1f)] public float gearRatioMultiplier = 1f;
        [Min(0.1f)] public float finalDriveMultiplier = 1f;
        [Min(0.1f)] public float shiftDurationMultiplier = 1f;

        [Min(0.1f)] public float nitrousTorqueMultiplier = 1f;
        [Min(0.1f)] public float nitrousFuelMultiplier = 1f;

        [Min(0.1f)] public float tireLongitudinalGripMultiplier = 1f;
        [Min(0.1f)] public float tireLateralGripMultiplier = 1f;
        [Min(0.1f)] public float tirePeakSlipMultiplier = 1f;
        [Min(0.1f)] public float tireRollingResistanceMultiplier = 1f;

        [Min(0.1f)] public float brakeTorqueMultiplier = 1f;
        [Range(-0.25f, 0.25f)] public float frontBrakeBiasDelta;

        [Min(0.1f)] public float suspensionSpringMultiplier = 1f;
        [Min(0.1f)] public float suspensionDamperMultiplier = 1f;
        [Min(0.1f)] public float suspensionTravelMultiplier = 1f;

        [Min(0.1f)] public float dragMultiplier = 1f;
        [Min(0.1f)] public float downforceMultiplier = 1f;

        public void ApplyTo(VehicleTuning tuning)
        {
            float massScale = Positive(massMultiplier);
            tuning.chassis.mass *= massScale;
            tuning.chassis.maxSpeedKph *= Positive(maxSpeedMultiplier);

            tuning.engine.maxTorqueNewtonMeters *= Positive(engineTorqueMultiplier);
            float redlineScale = Positive(engineRedlineMultiplier);
            tuning.engine.redlineRpm *= redlineScale;
            if (tuning.UsesMostWantedReference) tuning.mostWanted.torqueTableMaximumRpm *= redlineScale;
            tuning.engine.shiftUpRpm *= redlineScale;
            tuning.engine.shiftDownRpm *= redlineScale;
            tuning.engine.engineInertia *= Positive(engineInertiaMultiplier);
            tuning.engine.peakTorqueRpm *= Positive(peakTorqueRpmMultiplier);
            tuning.engine.peakTorqueRpm = Mathf.Clamp(
                tuning.engine.peakTorqueRpm,
                tuning.engine.idleRpm,
                Mathf.Max(tuning.engine.idleRpm + 1f, tuning.engine.redlineRpm));
            tuning.engine.finalDrive *= Positive(finalDriveMultiplier);
            tuning.engine.shiftDuration *= Positive(shiftDurationMultiplier);

            if (tuning.engine.gearRatios != null)
            {
                for (int i = 0; i < tuning.engine.gearRatios.Length; i++)
                {
                    tuning.engine.gearRatios[i] *= Positive(gearRatioMultiplier);
                }
            }

            tuning.engine.nitrousTorque *= Positive(nitrousTorqueMultiplier);
            if (tuning.UsesMostWantedReference) tuning.mostWanted.nitrousTorqueBoost *= Positive(nitrousTorqueMultiplier);
            tuning.engine.nitrousFuelSeconds *= Positive(nitrousFuelMultiplier);

            tuning.tires.longitudinalGrip *= Positive(tireLongitudinalGripMultiplier);
            tuning.tires.lateralGrip *= Positive(tireLateralGripMultiplier);
            tuning.tires.peakLongitudinalSlip *= Positive(tirePeakSlipMultiplier);
            tuning.tires.peakLateralSlipRadians *= Positive(tirePeakSlipMultiplier);
            tuning.tires.rollingResistance *= Positive(tireRollingResistanceMultiplier);

            tuning.controls.serviceBrakeTorque *= Positive(brakeTorqueMultiplier);
            tuning.controls.handbrakeTorque *= Positive(brakeTorqueMultiplier);
            tuning.controls.frontBrakeBias = Mathf.Clamp01(
                tuning.controls.frontBrakeBias + frontBrakeBiasDelta);

            tuning.tires.springRate *= Positive(suspensionSpringMultiplier);
            tuning.tires.damperRate *= Positive(suspensionDamperMultiplier);
            tuning.tires.suspensionTravel *= Positive(suspensionTravelMultiplier);

            tuning.aero.dragCoefficient *= Positive(dragMultiplier);
            tuning.aero.downforceCoefficient *= Positive(downforceMultiplier);
            if (tuning.UsesMostWantedReference) tuning.mostWanted.linearDownforceCoefficient *= Positive(downforceMultiplier);
        }

        private static float Positive(float value)
        {
            return Mathf.Max(0.1f, value);
        }
    }

    /// <summary>
    /// Pure build state. The shop, career rewards, save system, and UI can all
    /// use this interface without knowing how Unity assets are stored.
    /// </summary>
    public sealed class VehiclePerformanceBuild
    {
        private readonly List<IVehiclePerformanceUpgrade> installed =
            new List<IVehiclePerformanceUpgrade>();

        public IReadOnlyList<IVehiclePerformanceUpgrade> Installed
        {
            get { return installed; }
        }

        public VehiclePerformanceBuild Clone()
        {
            VehiclePerformanceBuild copy = new VehiclePerformanceBuild();
            for (int i = 0; i < installed.Count; i++) copy.installed.Add(installed[i]);
            return copy;
        }

        public void ReplaceWith(VehiclePerformanceBuild candidate)
        {
            installed.Clear();
            if (candidate == null) return;
            for (int i = 0; i < candidate.installed.Count; i++) installed.Add(candidate.installed[i]);
        }

        public bool CanInstall(IVehiclePerformanceUpgrade upgrade, out string failure)
        {
            if (upgrade == null)
            {
                failure = "Upgrade is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(upgrade.UpgradeId))
            {
                failure = "Upgrade must have a stable ID.";
                return false;
            }

            if (upgrade.Tier == VehiclePerformanceTier.Stock)
            {
                failure = "Stock is the removal state; install a performance tier instead.";
                return false;
            }

            IVehiclePerformanceUpgrade current = Get(upgrade.Category);
            if (current == null)
            {
                failure = string.Empty;
                return true;
            }

            if (string.Equals(current.UpgradeId, upgrade.UpgradeId, StringComparison.Ordinal))
            {
                failure = "This upgrade is already installed.";
                return false;
            }

            if ((int)upgrade.Tier < (int)current.Tier)
            {
                failure = "A lower tier cannot replace the installed upgrade. Remove it first.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        public bool CanInstall(IVehiclePerformanceUpgrade upgrade, string vehicleId, string variantId, out string failure)
        {
            if (!CanInstall(upgrade, out failure)) return false;
            if (upgrade is IVehiclePerformancePartMetadata metadata)
            {
                if (metadata.CompatibleVehicleIds.Count > 0 && !Contains(metadata.CompatibleVehicleIds, vehicleId))
                { failure = "Upgrade is not compatible with this vehicle."; return false; }
                if (metadata.CompatibleVariantIds.Count > 0 && !Contains(metadata.CompatibleVariantIds, variantId))
                { failure = "Upgrade is not compatible with this vehicle variant."; return false; }
                for (int i = 0; i < metadata.RequiredUpgradeIds.Count; i++)
                    if (Find(metadata.RequiredUpgradeIds[i]) == null)
                    { failure = "Required upgrade is not installed: " + metadata.RequiredUpgradeIds[i]; return false; }
                for (int i = 0; i < metadata.ExcludedUpgradeIds.Count; i++)
                    if (Find(metadata.ExcludedUpgradeIds[i]) != null)
                    { failure = "Upgrade conflicts with installed upgrade: " + metadata.ExcludedUpgradeIds[i]; return false; }
            }
            failure = string.Empty;
            return true;
        }

        public bool TryInstall(IVehiclePerformanceUpgrade upgrade, out string failure)
        {
            if (!CanInstall(upgrade, out failure))
            {
                return false;
            }

            for (int i = 0; i < installed.Count; i++)
            {
                if (installed[i].Category == upgrade.Category)
                {
                    installed[i] = upgrade;
                    return true;
                }
            }

            installed.Add(upgrade);
            return true;
        }

        public bool ValidateCompatibility(string vehicleId, string variantId, out string failure)
        {
            for (int i = 0; i < installed.Count; i++)
            {
                if (!(installed[i] is IVehiclePerformancePartMetadata metadata)) continue;
                if (metadata.CompatibleVehicleIds.Count > 0 && !Contains(metadata.CompatibleVehicleIds, vehicleId)) { failure = "Installed upgrade is not compatible with this vehicle."; return false; }
                if (metadata.CompatibleVariantIds.Count > 0 && !Contains(metadata.CompatibleVariantIds, variantId)) { failure = "Installed upgrade is not compatible with this variant."; return false; }
                for (int j = 0; j < metadata.RequiredUpgradeIds.Count; j++) if (Find(metadata.RequiredUpgradeIds[j]) == null) { failure = "Installed upgrade dependency is missing: " + metadata.RequiredUpgradeIds[j]; return false; }
                for (int j = 0; j < metadata.ExcludedUpgradeIds.Count; j++) if (Find(metadata.ExcludedUpgradeIds[j]) != null && !string.Equals(metadata.ExcludedUpgradeIds[j], installed[i].UpgradeId, StringComparison.Ordinal)) { failure = "Installed upgrade conflict: " + metadata.ExcludedUpgradeIds[j]; return false; }
            }
            failure = string.Empty; return true;
        }

        public bool ValidateComplete(string vehicleId, string variantId, VehicleCapabilities capabilities, out string failure)
        {
            if (!ValidateCompatibility(vehicleId, variantId, out failure)) return false;
            for (int i = 0; i < installed.Count; i++)
                if (installed[i] is IVehiclePerformancePartMetadata metadata && (capabilities & metadata.RequiredCapabilities) != metadata.RequiredCapabilities)
                { failure = "Installed upgrade requires unsupported vehicle capability."; return false; }
            failure = string.Empty; return true;
        }

        public bool TryRemove(VehiclePerformanceCategory category, out string failure)
        {
            for (int i = 0; i < installed.Count; i++)
            {
                if (installed[i].Category == category)
                {
                    installed.RemoveAt(i);
                    failure = string.Empty;
                    return true;
                }
            }

            failure = "No upgrade is installed in that category.";
            return false;
        }

        public IVehiclePerformanceUpgrade Get(VehiclePerformanceCategory category)
        {
            for (int i = 0; i < installed.Count; i++)
            {
                if (installed[i].Category == category)
                {
                    return installed[i];
                }
            }

            return null!;
        }

        private IVehiclePerformanceUpgrade Find(string id)
        {
            for (int i = 0; i < installed.Count; i++)
                if (installed[i] != null && string.Equals(installed[i].UpgradeId, id, StringComparison.Ordinal)) return installed[i];
            return null!;
        }

        public void ApplyTo(VehicleTuning tuning)
        {
            if (tuning == null)
            {
                return;
            }

            for (int i = 0; i < installed.Count; i++)
            {
                installed[i].Apply(tuning);
            }
        }

        private static bool Contains(IReadOnlyList<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++) if (string.Equals(values[i], value, StringComparison.Ordinal)) return true;
            return false;
        }
    }

}
