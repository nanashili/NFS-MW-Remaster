using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Committed installed parts and an isolated, cancellable preview using the same resolver.</summary>
    [DisallowMultipleComponent]
    public sealed class VehicleCustomizationSystem : MonoBehaviour, IVehicleModule,
        IVehicleCustomizationIdInstaller, IVehicleCustomizationValidator, ICareerVehicleStateParticipant
    {
        [SerializeField] private VehicleCustomizationCatalog catalog;
        [SerializeField] private List<VehicleCustomizationDefinition> installedCustomizations = new();
        [SerializeField] private MonoBehaviour visualAdapterComponent;
        private readonly List<IVehicleCustomizationItem> runtimeCustomizations = new();
        private VehicleCustomizationBuild build = new();
        private VehicleCustomizationBuild preview;
        private IVehicleCustomizationVisualAdapter visualAdapter;
        private VehicleModuleContext context;
        private bool buildLoaded;
        public string ModuleId => "customization";
        public int ExecutionOrder => -900;
        public string VehicleStateSectionId => "customization";
        public VehicleCustomizationCatalog Catalog => catalog;
        public VehicleCustomizationBuild Build { get { EnsureBuildLoaded(); return build; } }
        public VehicleCustomizationBuild EffectiveBuild { get { EnsureBuildLoaded(); return preview ?? build; } }
        public bool IsPreviewing => preview != null;
        public IVehicleCustomizationVisualAdapter VisualAdapter { get { ResolveVisualAdapter(); return visualAdapter; } }
        public void Initialize(VehicleModuleContext newContext)
        { context = newContext; EnsureBuildLoaded(); ApplyVisuals(); }

        private bool Validate(VehicleCustomizationBuild candidate, out string failure)
        {
            var configuration = GetComponent<VehicleConfiguration>();
            var performance = GetComponent<VehiclePerformanceSystem>();
            if (performance != null)
                return performance.ValidateCandidate(performance.Build, candidate,
                    configuration != null ? configuration.Adjustments : null, out failure);
            var definition = configuration != null ? configuration.Definition : null;
            return candidate.ValidateComplete(definition != null ? definition.vehicleId : string.Empty,
                definition != null ? definition.supportedSlots : Array.Empty<string>(), out failure);
        }
        public bool CanInstall(IVehicleCustomizationItem item, out string failure)
        { var candidate = Build.Clone(); return candidate.TryInstall(item, out failure) && Validate(candidate, out failure); }
        public bool CanInstallForVehicle(IVehicleCustomizationItem item, string vehicleId, IReadOnlyCollection<string> supportedSlots, out string failure)
        {
            var candidate = Build.Clone();
            return candidate.TryInstall(item, out failure) && candidate.ValidateComplete(vehicleId, supportedSlots, out failure)
                && Validate(candidate, out failure);
        }
        public bool TryInstall(IVehicleCustomizationItem item, out string failure)
        {
            var candidate = Build.Clone();
            if (!candidate.TryInstall(item, out failure) || !Validate(candidate, out failure)) return false;
            CommitBuild(candidate); return true;
        }
        public bool TryRemove(VehicleCustomizationCategory category, out string failure)
        {
            var candidate = Build.Clone();
            if (!candidate.TryRemove(category, out failure) || !Validate(candidate, out failure)) return false;
            CommitBuild(candidate); return true;
        }
        internal void CommitBuild(VehicleCustomizationBuild candidate)
        {
            // The adapter prepares every replacement before publishing any visual. A failed
            // preparation must leave both the installed IDs and the live visual unchanged.
            ResolveVisualAdapter(); visualAdapter?.Apply(candidate);
            build.ReplaceWith(candidate); preview = null;
            installedCustomizations ??= new();
            installedCustomizations.Clear(); runtimeCustomizations.Clear();
            foreach (var part in build.Installed)
                if (part is VehicleCustomizationDefinition asset) installedCustomizations.Add(asset);
                else runtimeCustomizations.Add(part);
            buildLoaded = true;
            context?.RequestReconfigure();
        }
        public bool TryInstallById(string id, out string failure)
        {
            var item = catalog != null ? catalog.Find(id) : null;
            if (item == null) { failure = "The customization catalog does not contain item " + id + "."; return false; }
            return TryInstall(item, out failure);
        }
        public IVehicleCustomizationItem GetInstalled(VehicleCustomizationCategory category) => Build.Get(category);
        public VehicleCustomizationDefinition[] GetAvailable(VehicleCustomizationCategory category)
            => catalog != null ? catalog.GetForCategory(category) : Array.Empty<VehicleCustomizationDefinition>();
        public void SetCatalog(VehicleCustomizationCatalog value) => catalog = value;
        public void SetVisualAdapter(MonoBehaviour adapter)
        { visualAdapterComponent = adapter; visualAdapter = adapter as IVehicleCustomizationVisualAdapter; RefreshVisuals(); }
        public void RefreshVisuals() { EnsureBuildLoaded(); ApplyVisuals(); }
        public bool BeginPreview(IVehicleCustomizationItem item, out string failure)
        {
            var candidate = EffectiveBuild.Clone();
            if (!candidate.TryInstall(item, out failure) || !Validate(candidate, out failure)) return false;
            ResolveVisualAdapter(); visualAdapter?.Apply(candidate);
            preview = candidate; context?.RequestReconfigure(); return true;
        }
        public bool TryApplyPreview(out string failure)
        {
            failure = string.Empty;
            if (preview == null) return true;
            if (!Validate(preview, out failure)) return false;
            CommitBuild(preview); return true;
        }
        public void ApplyPreview()
        { if (!TryApplyPreview(out string failure)) throw new InvalidOperationException(failure); }
        public void CancelPreview()
        { if (preview == null) return; ResolveVisualAdapter(); visualAdapter?.Apply(Build); preview = null; context?.RequestReconfigure(); }
        public void ResetPreview() => CancelPreview();
        public void Capture(CareerVehicleData vehicle)
        {
            if (vehicle == null) return;
            vehicle.customizationIds ??= new(); vehicle.customizationIds.Clear();
            foreach (var part in Build.Installed) vehicle.customizationIds.Add(part.CustomizationId);
        }
        internal bool TryBuildSaved(CareerVehicleData vehicle, out VehicleCustomizationBuild candidate, out string failure)
        {
            candidate = new(); failure = string.Empty;
            if (vehicle == null) { failure = "Vehicle save data is missing."; return false; }
            var categories = new HashSet<VehicleCustomizationCategory>();
            foreach (var id in vehicle.customizationIds ?? new List<string>())
            {
                var part = catalog != null ? catalog.Find(id) : null;
                if (part == null) { failure = "The customization catalog cannot restore item " + id + "."; return false; }
                if (!categories.Add(part.Category)) { failure = "Saved customization repeats category " + part.Category + "."; return false; }
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
            build = new VehicleCustomizationBuild();
            foreach (var part in installedCustomizations ?? new List<VehicleCustomizationDefinition>())
                if (part != null && !build.TryInstall(part, out string failure)) throw new ArgumentException(failure);
            foreach (var part in runtimeCustomizations)
                if (part != null && !build.TryInstall(part, out string failure)) throw new ArgumentException(failure);
            buildLoaded = true;
        }
        private void ResolveVisualAdapter()
        {
            if (visualAdapterComponent is IVehicleCustomizationVisualAdapter configured) { visualAdapter = configured; return; }
            if (visualAdapter != null) return;
            foreach (var component in GetComponentsInChildren<MonoBehaviour>(true))
                if (component is IVehicleCustomizationVisualAdapter found)
                { visualAdapterComponent = component; visualAdapter = found; return; }
        }
        private void ApplyVisuals() { ResolveVisualAdapter(); visualAdapter?.Apply(EffectiveBuild); }
        private void OnValidate() { buildLoaded = false; preview = null; context?.RequestReconfigure(); }
    }
}
