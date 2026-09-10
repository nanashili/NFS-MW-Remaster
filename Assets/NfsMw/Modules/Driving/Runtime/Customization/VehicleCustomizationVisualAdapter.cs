using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Visual application seam. VehicleCustomizationSystem only asks an
    /// adapter to apply a build; a vehicle can replace this component with a
    /// skinned-mesh, pooled-prefab, or UI-specific implementation.
    /// </summary>
    public interface IVehicleCustomizationVisualAdapter
    {
        void Apply(VehicleCustomizationBuild build);
    }

    /// <summary>
    /// Default slot adapter for future content assets. Empty slots are safe:
    /// the customization system still owns the build and persistence state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleCustomizationVisualAdapter :
        MonoBehaviour,
        IVehicleCustomizationVisualAdapter
    {
        [SerializeField] private VehicleCustomizationVisualSlot[] slots =
            Array.Empty<VehicleCustomizationVisualSlot>();

        public void Apply(VehicleCustomizationBuild build)
        {
            if (build == null)
            {
                return;
            }

            VehicleCustomizationVisualSlot[] configuredSlots = slots;
            if (configuredSlots == null || configuredSlots.Length == 0)
            {
                configuredSlots = GetComponentsInChildren<VehicleCustomizationVisualSlot>(true);
            }

            if (configuredSlots == null)
            {
                return;
            }

            var prepared = new VehicleCustomizationVisualSlot.PreparedVisual[configuredSlots.Length];
            try
            {
                for (int i = 0; i < configuredSlots.Length; i++)
                {
                    if (configuredSlots[i] != null)
                    {
                        prepared[i] = PrepareSlot(configuredSlots[i], build);
                    }
                }
                for (int i = 0; i < configuredSlots.Length; i++) if (configuredSlots[i] != null) configuredSlots[i].Commit(prepared[i]);
                NotifyPresentation(configuredSlots);
            }
            finally
            {
                foreach (var candidate in prepared) candidate?.Dispose();
            }
        }

        private void NotifyPresentation(VehicleCustomizationVisualSlot[] configuredSlots)
        {
            var presentation = GetComponent<VehiclePresentationModule>();
            if (!presentation) return;
            var replacements = new List<VehiclePresentationBindings>();
            for (int i = 0; i < configuredSlots.Length; i++)
                if (configuredSlots[i] != null && configuredSlots[i].SpawnedVisual != null)
                    replacements.AddRange(configuredSlots[i].SpawnedVisual.GetComponentsInChildren<VehiclePresentationBindings>(true));
            presentation.SetReplacementBindings(replacements);
        }

        private static VehicleCustomizationVisualSlot.PreparedVisual PrepareSlot(VehicleCustomizationVisualSlot slot, VehicleCustomizationBuild build)
        {
            IVehicleCustomizationItem item = build != null ? build.Get(slot.Category) : null;
            VehicleCustomizationVisualPayload payload = null;
            if (!string.IsNullOrWhiteSpace(slot.SlotId) && item is IVehicleCustomizationPartMetadata metadata)
            {
                bool ownsSlot = false;
                for (int m = 0; m < metadata.MountSlotIds.Count; m++) ownsSlot |= string.Equals(metadata.MountSlotIds[m], slot.SlotId, StringComparison.Ordinal);
                if (!ownsSlot) item = null;
                if (item is IVehicleCustomizationVisualSource source && source is VehicleCustomizationDefinition definition)
                    for (int m = 0; m < definition.Mounts.Count; m++) if (string.Equals(definition.Mounts[m].SlotId, slot.SlotId, StringComparison.Ordinal)) { payload = definition.Mounts[m].Visual; break; }
            }
            return slot.Prepare(item, payload);
        }
    }

}
