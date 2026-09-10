using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Pure installed-visual state. Every category has one item or is stock
    /// when absent. Replacing a category is intentional: the shop never
    /// leaves two conflicting spoilers, paints, or vinyls installed.
    /// </summary>
    public sealed class VehicleCustomizationBuild
    {
        private readonly List<IVehicleCustomizationItem> installed =
            new List<IVehicleCustomizationItem>();

        public IReadOnlyList<IVehicleCustomizationItem> Installed
        {
            get { return installed; }
        }

        public VehicleCustomizationBuild Clone()
        {
            VehicleCustomizationBuild copy = new VehicleCustomizationBuild();
            for (int i = 0; i < installed.Count; i++) copy.installed.Add(installed[i]);
            return copy;
        }

        public void ReplaceWith(VehicleCustomizationBuild candidate)
        {
            installed.Clear();
            if (candidate == null) return;
            for (int i = 0; i < candidate.installed.Count; i++) installed.Add(candidate.installed[i]);
        }

        public bool CanInstall(
            IVehicleCustomizationItem customization,
            out string failure)
        {
            if (customization == null)
            {
                failure = "Customization is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(customization.CustomizationId))
            {
                failure = "Customization must have a stable ID.";
                return false;
            }

            if (customization.IsStock)
            {
                failure = "Stock is the removal state; remove the category instead.";
                return false;
            }

            IVehicleCustomizationItem current = Get(customization.Category);
            if (current != null
                && string.Equals(
                    current.CustomizationId,
                    customization.CustomizationId,
                    StringComparison.Ordinal))
            {
                failure = "This customization is already installed.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        public bool TryInstall(
            IVehicleCustomizationItem customization,
            out string failure)
        {
            if (!CanInstall(customization, out failure))
            {
                return false;
            }

            for (int i = 0; i < installed.Count; i++)
            {
                if (installed[i].Category == customization.Category)
                {
                    installed[i] = customization;
                    return true;
                }
            }

            installed.Add(customization);
            return true;
        }

        public bool CanInstall(
            IVehicleCustomizationItem customization,
            string vehicleId,
            IReadOnlyCollection<string> supportedSlots,
            out string failure)
        {
            if (!CanInstall(customization, out failure)) return false;
            if (customization is IVehicleCustomizationPartMetadata metadata)
            {
                if (!float.IsFinite(metadata.WheelRadiusDelta) || Mathf.Abs(metadata.WheelRadiusDelta) > 1f
                    || !float.IsFinite(metadata.WheelOffsetDelta) || Mathf.Abs(metadata.WheelOffsetDelta) > 2f
                    || !float.IsFinite(metadata.TrackWidthDelta) || Mathf.Abs(metadata.TrackWidthDelta) > 2f
                    || !float.IsFinite(metadata.ClearanceDelta) || Mathf.Abs(metadata.ClearanceDelta) > 1f)
                { failure = "Customization physical fitment delta exceeds the authored safety bounds."; return false; }
                if (metadata.SupportedVehicleIds.Count > 0
                    && (string.IsNullOrWhiteSpace(vehicleId) || !Contains(metadata.SupportedVehicleIds, vehicleId)))
                { failure = "Customization is not compatible with this vehicle variant."; return false; }
                if (!ContainsAll(supportedSlots, metadata.RequiredSlotIds))
                { failure = "Required customization mounting slot is unavailable."; return false; }
                for (int i = 0; i < metadata.RequiredPartIds.Count; i++)
                    if (!ContainsId(metadata.RequiredPartIds[i]))
                    { failure = "Required customization part is not installed: " + metadata.RequiredPartIds[i]; return false; }
                for (int i = 0; i < metadata.ConflictingPartIds.Count; i++)
                    if (ContainsId(metadata.ConflictingPartIds[i]))
                    { failure = "Customization conflicts with installed part: " + metadata.ConflictingPartIds[i]; return false; }
            }
            failure = string.Empty;
            return true;
        }

        public bool TryInstall(IVehicleCustomizationItem item, string vehicleId,
            IReadOnlyCollection<string> supportedSlots, out string failure)
        {
            var candidate = Clone();
            if (!candidate.TryInstall(item, out failure)
                || !candidate.ValidateComplete(vehicleId, supportedSlots, out failure)) return false;
            ReplaceWith(candidate);
            return true;
        }

        public bool TryInstall(IVehicleCustomizationItem item, string vehicleId, string variantId,
            IReadOnlyCollection<string> supportedSlots, out string failure)
        {
            var candidate = Clone();
            if (!candidate.TryInstall(item, out failure)
                || !candidate.ValidateComplete(vehicleId, variantId, supportedSlots, out failure)) return false;
            ReplaceWith(candidate);
            return true;
        }

        public void ApplyPhysicalEffects(VehicleTuning tuning)
        {
            for (int i = 0; i < installed.Count; i++)
                if (installed[i] is IVehicleCustomizationPhysicalEffect effect) effect.ApplyPhysicalEffects(tuning);
        }

        public bool ValidateCompatibility(string vehicleId, IReadOnlyCollection<string> supportedSlots, out string failure)
            => ValidateCompatibility(vehicleId, string.Empty, supportedSlots, out failure);

        public bool ValidateCompatibility(string vehicleId, string variantId, IReadOnlyCollection<string> supportedSlots, out string failure)
        {
            var claimedMounts = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < installed.Count; i++)
            {
                if (!(installed[i] is IVehicleCustomizationPartMetadata metadata)) continue;
                if (!float.IsFinite(metadata.WheelRadiusDelta) || Mathf.Abs(metadata.WheelRadiusDelta) > 1f
                    || !float.IsFinite(metadata.WheelOffsetDelta) || Mathf.Abs(metadata.WheelOffsetDelta) > 2f
                    || !float.IsFinite(metadata.TrackWidthDelta) || Mathf.Abs(metadata.TrackWidthDelta) > 2f
                    || !float.IsFinite(metadata.ClearanceDelta) || Mathf.Abs(metadata.ClearanceDelta) > 1f) { failure = "Installed customization physical fitment delta exceeds the authored safety bounds."; return false; }
                if (metadata.SupportedVehicleIds.Count > 0 && !Contains(metadata.SupportedVehicleIds, vehicleId)) { failure = "Installed customization is not compatible with this vehicle."; return false; }
                if (installed[i] is IVehicleCustomizationVariantMetadata variants && variants.SupportedVariantIds.Count > 0
                    && (string.IsNullOrWhiteSpace(variantId) || !Contains(variants.SupportedVariantIds, variantId))) { failure = "Installed customization is not compatible with this vehicle variant."; return false; }
                if (!ContainsAll(supportedSlots, metadata.RequiredSlotIds)) { failure = "Installed customization mounting slot is unavailable."; return false; }
                var mounts = new HashSet<string>(StringComparer.Ordinal);
                for (int m = 0; m < metadata.MountSlotIds.Count; m++)
                {
                    string mount = metadata.MountSlotIds[m];
                    if (string.IsNullOrWhiteSpace(mount) || !mounts.Add(mount) || !claimedMounts.Add(mount)) { failure = "Installed customizations claim the same mounting slot."; return false; }
                    if (!ContainsAll(supportedSlots, new[] { mount })) { failure = "Customization mounting slot is unavailable: " + mount; return false; }
                }
                for (int j = 0; j < metadata.RequiredPartIds.Count; j++) if (!ContainsId(metadata.RequiredPartIds[j])) { failure = "Installed customization dependency is missing: " + metadata.RequiredPartIds[j]; return false; }
                for (int j = 0; j < metadata.ConflictingPartIds.Count; j++) if (ContainsId(metadata.ConflictingPartIds[j]) && !string.Equals(metadata.ConflictingPartIds[j], installed[i].CustomizationId, StringComparison.Ordinal)) { failure = "Installed customization conflict: " + metadata.ConflictingPartIds[j]; return false; }
            }
            failure = string.Empty; return true;
        }

        public bool ValidateComplete(string vehicleId, IReadOnlyCollection<string> supportedSlots, out string failure)
            => ValidateCompatibility(vehicleId, supportedSlots, out failure);

        public bool ValidateComplete(string vehicleId, string variantId, IReadOnlyCollection<string> supportedSlots, out string failure)
            => ValidateCompatibility(vehicleId, variantId, supportedSlots, out failure);

        private bool ContainsId(string id)
        {
            for (int i = 0; i < installed.Count; i++)
                if (installed[i] != null && string.Equals(installed[i].CustomizationId, id, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool Contains(IReadOnlyList<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++) if (string.Equals(values[i], value, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool ContainsAll(IReadOnlyCollection<string> available, IReadOnlyList<string> required)
        {
            if (required == null || required.Count == 0) return true;
            if (available == null) return false;
            for (int i = 0; i < required.Count; i++)
            {
                bool found = false;
                foreach (string value in available) if (string.Equals(value, required[i], StringComparison.Ordinal)) { found = true; break; }
                if (!found) return false;
            }
            return true;
        }

        public bool TryRemove(VehicleCustomizationCategory category, out string failure)
        {
            IVehicleCustomizationItem removed = Get(category);
            if (removed != null)
            {
                for (int j = 0; j < installed.Count; j++)
                    if (installed[j] is IVehicleCustomizationPartMetadata metadata)
                        for (int d = 0; d < metadata.RequiredPartIds.Count; d++)
                            if (string.Equals(metadata.RequiredPartIds[d], removed.CustomizationId, StringComparison.Ordinal))
                            { failure = "Cannot remove customization; " + installed[j].CustomizationId + " depends on it."; return false; }
            }
            for (int i = 0; i < installed.Count; i++)
            {
                if (installed[i].Category == category)
                {
                    installed.RemoveAt(i);
                    failure = string.Empty;
                    return true;
                }
            }

            failure = "No customization is installed in that category.";
            return false;
        }

        public IVehicleCustomizationItem Get(VehicleCustomizationCategory category)
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
    }

}
