using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Selects the immutable definition; existing performance/customization modules own installed parts.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VehicleController), typeof(VehiclePerformanceSystem))]
    public sealed class VehicleConfiguration : MonoBehaviour, IVehicleModule, ICareerVehicleStateParticipant
    {
        [SerializeField] private VehicleDefinition definition;
        [SerializeField] private VehicleTuningAdjustment[] adjustments = Array.Empty<VehicleTuningAdjustment>();
        private VehicleModuleContext context;
        public VehicleDefinition Definition => definition;
        public VehicleTuningAdjustment[] Adjustments => (VehicleTuningAdjustment[])(adjustments ?? Array.Empty<VehicleTuningAdjustment>()).Clone();
        public string ModuleId => "vehicle-configuration";
        public int ExecutionOrder => -1100;
        public string VehicleStateSectionId => "vehicle-configuration";

        public void Initialize(VehicleModuleContext newContext)
        {
            context = newContext;
            if (definition == null) return; // Legacy prefabs keep their existing factory tuning.
            if (!definition.Validate(out string failure)) throw new ArgumentException(failure);
            if (definition.performanceCatalog != null) GetComponent<VehiclePerformanceSystem>().SetCatalog(definition.performanceCatalog);
            if (definition.customizationCatalog != null) GetComponent<VehicleCustomizationSystem>()?.SetCatalog(definition.customizationCatalog);
            context.ReplaceTuning(definition.factoryTuning);
        }

        public bool TryConfigure(VehicleDefinition selected, VehicleTuningAdjustment[] requested, out string failure)
        {
            if (selected == null) { failure = "Select a vehicle definition."; return false; }
            try
            {
                var performance = GetComponent<VehiclePerformanceSystem>();
                var customization = GetComponent<VehicleCustomizationSystem>();
                using var result = VehicleConfigurationResolver.Resolve(selected.factoryTuning, performance != null ? performance.Build : null,
                    customization != null ? customization.Build : null, requested, selected);
                definition = selected;
                adjustments = (VehicleTuningAdjustment[])(requested ?? Array.Empty<VehicleTuningAdjustment>()).Clone();
                if (selected.performanceCatalog != null) performance?.SetCatalog(selected.performanceCatalog);
                if (selected.customizationCatalog != null) customization?.SetCatalog(selected.customizationCatalog);
                if (context != null) context.RequestReconfigure();
                else GetComponent<VehicleController>().Tuning = selected.factoryTuning;
                failure = string.Empty; return true;
            }
            catch (ArgumentException error) { failure = error.Message; return false; }
        }

        public bool TrySetAdjustments(VehicleTuningAdjustment[] requested, out string failure) => TryConfigure(definition, requested, out failure);

        public void Capture(CareerVehicleData vehicle)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            vehicle.schemaVersion = 2;
            if (definition != null) { vehicle.vehicleDefinitionId = definition.vehicleId; vehicle.vehicleVariantId = definition.variantId; }
            vehicle.tuningAdjustments ??= new System.Collections.Generic.List<VehicleTuningAdjustment>();
            vehicle.tuningAdjustments.Clear();
            vehicle.tuningAdjustments.AddRange(adjustments ?? Array.Empty<VehicleTuningAdjustment>());
        }

        public bool Restore(CareerVehicleData vehicle, out string failure)
        {
            if (vehicle == null) { failure = "Vehicle save data is missing."; return false; }
            string savedId = vehicle.vehicleDefinitionId;
            // Legacy saves identify the garage instance only; that is not a factory definition ID.
            if (definition != null && !string.IsNullOrWhiteSpace(savedId) && !string.Equals(savedId, definition.vehicleId, StringComparison.Ordinal))
            { failure = "Saved definition " + savedId + " does not match " + definition.vehicleId + ". Spawn the saved vehicle definition before restoring."; return false; }
            if (definition == null)
            {
                if (vehicle.tuningAdjustments != null && vehicle.tuningAdjustments.Count > 0)
                { failure = "Saved tuning requires an assigned vehicle definition. Migrate this prefab before restoring tuning."; return false; }
                adjustments = Array.Empty<VehicleTuningAdjustment>(); failure = string.Empty; return true;
            }
            if (!string.IsNullOrWhiteSpace(vehicle.vehicleVariantId)
                && !string.Equals(vehicle.vehicleVariantId, definition.variantId, StringComparison.Ordinal))
            { failure = "Saved variant does not match this vehicle definition."; return false; }
            var performance = GetComponent<VehiclePerformanceSystem>();
            var customization = GetComponent<VehicleCustomizationSystem>();
            if (!performance.TryBuildSaved(vehicle, out var savedPerformance, out failure)) return false;
            var savedCustomization = new VehicleCustomizationBuild();
            if (customization != null)
            { if (!customization.TryBuildSaved(vehicle, out savedCustomization, out failure)) return false; }
            else if (vehicle.customizationIds != null && vehicle.customizationIds.Count != 0)
            { failure = "Saved body parts require a customization module."; return false; }
            var requested = vehicle.tuningAdjustments?.ToArray() ?? Array.Empty<VehicleTuningAdjustment>();
            try
            {
                using var resolved = VehicleConfigurationResolver.Resolve(definition.factoryTuning,
                    savedPerformance, savedCustomization, requested, definition);
                // All dependent parts and tuning are validated together before any participant publishes state.
                // Visual preparation is the only fallible publication step. Complete it
                // before changing performance IDs or tuning adjustments.
                customization?.CommitBuild(savedCustomization);
                performance.CommitBuild(savedPerformance);
                adjustments = requested;
                context?.RequestReconfigure();
                failure = string.Empty; return true;
            }
            catch (ArgumentException error) { failure = error.Message; return false; }
            catch (InvalidOperationException error) { failure = error.Message; return false; }
        }
    }
}
