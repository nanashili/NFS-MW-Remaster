using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Committed parts and a private resolved tuning image; validate before publishing changes.</summary>
    [DisallowMultipleComponent]
    public sealed class VehiclePerformanceSystem : MonoBehaviour, IVehicleModule,
        IVehiclePerformanceIdInstaller, IVehiclePerformanceValidator, ICareerVehicleStateParticipant
    {
        [SerializeField] private VehiclePerformanceCatalog catalog;
        [SerializeField] private List<VehiclePerformanceUpgradeDefinition> installedUpgrades = new();
        private readonly List<IVehiclePerformanceUpgrade> runtimeUpgrades = new();
        private VehiclePerformanceBuild build = new();
        private VehicleModuleContext context;
        private VehicleTuning factoryTuning;
        private ResolvedVehicleConfiguration resolvedConfiguration;
        private bool buildLoaded;
        public string ModuleId => "performance";
        public int ExecutionOrder => -1000;
        public string VehicleStateSectionId => "performance";
        public VehiclePerformanceCatalog Catalog => catalog;
        public VehiclePerformanceBuild Build { get { EnsureBuildLoaded(); return build; } }
        public ResolvedVehicleConfiguration Resolved => resolvedConfiguration;

        public void Initialize(VehicleModuleContext newContext)
        {
            context = newContext;
            factoryTuning = newContext.Tuning;
            EnsureBuildLoaded();
            var configuration = GetComponent<VehicleConfiguration>();
            var customization = GetComponent<VehicleCustomizationSystem>();
            var next = VehicleConfigurationResolver.Resolve(factoryTuning, build,
                customization != null ? customization.EffectiveBuild : null,
                configuration != null ? configuration.Adjustments : null,
                configuration != null ? configuration.Definition : null);
            var old = resolvedConfiguration;
            resolvedConfiguration = next;
            newContext.ReplaceTuning(next.Tuning);
            old?.Dispose();
        }
        internal bool ValidateCandidate(VehiclePerformanceBuild candidate, VehicleCustomizationBuild body,
            VehicleTuningAdjustment[] adjustments, out string failure)
        {
            var configuration = GetComponent<VehicleConfiguration>();
            var definition = configuration != null ? configuration.Definition : null;
            var factory = definition != null ? definition.factoryTuning : factoryTuning;
            if (factory == null) factory = GetComponent<VehicleController>()?.FactoryTuning;
            try
            {
                // Existing shop adapters can intentionally have no simulation attached.
                if (factory == null)
                    return candidate.ValidateComplete(string.Empty, string.Empty, VehicleCapabilities.All, out failure)
                        && (body == null || body.ValidateComplete(string.Empty, Array.Empty<string>(), out failure));
                using var result = VehicleConfigurationResolver.Resolve(factory, candidate, body, adjustments, definition);
                failure = string.Empty; return true;
            }
            catch (ArgumentException error) { failure = error.Message; return false; }
        }
        private bool Validate(VehiclePerformanceBuild candidate, out string failure)
        {
            var customization = GetComponent<VehicleCustomizationSystem>();
            var configuration = GetComponent<VehicleConfiguration>();
            return ValidateCandidate(candidate, customization != null ? customization.Build : null,
                configuration != null ? configuration.Adjustments : null, out failure);
        }
        public bool CanInstall(IVehiclePerformanceUpgrade upgrade, out string failure)
        {
            var candidate = Build.Clone();
            return candidate.TryInstall(upgrade, out failure) && Validate(candidate, out failure);
        }
        public bool CanInstallForVehicle(IVehiclePerformanceUpgrade upgrade, string vehicleId, string variantId, out string failure)
        {
            var candidate = Build.Clone();
            return candidate.TryInstall(upgrade, out failure)
                && candidate.ValidateCompatibility(vehicleId, variantId, out failure) && Validate(candidate, out failure);
        }
        public bool TryInstall(IVehiclePerformanceUpgrade upgrade, out string failure)
        {
            var candidate = Build.Clone();
            if (!candidate.TryInstall(upgrade, out failure) || !Validate(candidate, out failure)) return false;
            CommitBuild(candidate); return true;
        }
        public bool TryRemove(VehiclePerformanceCategory category, out string failure)
        {
            var candidate = Build.Clone();
            if (!candidate.TryRemove(category, out failure) || !Validate(candidate, out failure)) return false;
            CommitBuild(candidate); return true;
        }
        internal void CommitBuild(VehiclePerformanceBuild candidate)
        {
            build.ReplaceWith(candidate);
            installedUpgrades ??= new();
            installedUpgrades.Clear(); runtimeUpgrades.Clear();
            foreach (var part in build.Installed)
                if (part is VehiclePerformanceUpgradeDefinition asset) installedUpgrades.Add(asset);
                else runtimeUpgrades.Add(part);
            buildLoaded = true;
            context?.RequestReconfigure();
        }
        public bool TryInstallById(string id, out string failure)
        {
            var item = catalog != null ? catalog.Find(id) : null;
            if (item == null) { failure = "The performance catalog does not contain upgrade " + id + "."; return false; }
            return TryInstall(item, out failure);
        }
        public IVehiclePerformanceUpgrade GetInstalled(VehiclePerformanceCategory category) => Build.Get(category);
        public VehiclePerformanceUpgradeDefinition[] GetAvailable(VehiclePerformanceCategory category)
            => catalog != null ? catalog.GetForCategory(category) : Array.Empty<VehiclePerformanceUpgradeDefinition>();
        public void SetCatalog(VehiclePerformanceCatalog value) => catalog = value;
        public void Capture(CareerVehicleData vehicle)
        {
            if (vehicle == null) return;
            vehicle.performanceUpgradeIds ??= new();
            vehicle.performanceUpgradeIds.Clear();
            foreach (var part in Build.Installed) vehicle.performanceUpgradeIds.Add(part.UpgradeId);
        }
        internal bool TryBuildSaved(CareerVehicleData vehicle, out VehiclePerformanceBuild candidate, out string failure)
        {
            candidate = new(); failure = string.Empty;
            if (vehicle == null) { failure = "Vehicle save data is missing."; return false; }
            var categories = new HashSet<VehiclePerformanceCategory>();
            foreach (var id in vehicle.performanceUpgradeIds ?? new List<string>())
            {
                var part = catalog != null ? catalog.Find(id) : null;
                if (part == null) { failure = "The performance catalog cannot restore upgrade " + id + "."; return false; }
                if (!categories.Add(part.Category)) { failure = "Saved upgrades repeat category " + part.Category + "."; return false; }
                if (!candidate.TryInstall(part, out failure)) return false;
            }
            return true;
        }
        public bool Restore(CareerVehicleData vehicle, out string failure)
        {
            var configuration = GetComponent<VehicleConfiguration>();
            if (configuration != null && configuration.Definition != null) return configuration.Restore(vehicle, out failure);
            if (!TryBuildSaved(vehicle, out var candidate, out failure) || !Validate(candidate, out failure)) return false;
            CommitBuild(candidate); return true;
        }
        private void EnsureBuildLoaded()
        {
            if (buildLoaded) return;
            build = new VehiclePerformanceBuild();
            foreach (var part in installedUpgrades ?? new List<VehiclePerformanceUpgradeDefinition>())
                if (part != null && !build.TryInstall(part, out string failure)) throw new ArgumentException(failure);
            foreach (var part in runtimeUpgrades)
                if (part != null && !build.TryInstall(part, out string failure)) throw new ArgumentException(failure);
            buildLoaded = true;
        }
        private void OnValidate() { buildLoaded = false; context?.RequestReconfigure(); }
        private void OnDestroy() { resolvedConfiguration?.Dispose(); resolvedConfiguration = null; }
    }
}
