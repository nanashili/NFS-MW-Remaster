using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(
        menuName = "NFS MW Remaster/Driving/Customization Catalog",
        fileName = "VehicleCustomizationCatalog")]
    public sealed class VehicleCustomizationCatalog : ScriptableObject
    {
        [SerializeField] private VehicleCustomizationDefinition[] items =
            Array.Empty<VehicleCustomizationDefinition>();

        public IReadOnlyList<VehicleCustomizationDefinition> Items
        {
            get { return items ?? Array.Empty<VehicleCustomizationDefinition>(); }
        }

        public VehicleCustomizationDefinition Find(string customizationId)
        {
            if (string.IsNullOrWhiteSpace(customizationId) || items == null)
            {
                return null!;
            }

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] != null
                    && string.Equals(
                        items[i].CustomizationId,
                        customizationId,
                        StringComparison.Ordinal))
                {
                    return items[i];
                }
            }

            return null!;
        }

        public VehicleCustomizationDefinition[] GetForCategory(
            VehicleCustomizationCategory category)
        {
            List<VehicleCustomizationDefinition> result =
                new List<VehicleCustomizationDefinition>();
            if (items == null)
            {
                return result.ToArray();
            }

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] != null && items[i].Category == category)
                {
                    result.Add(items[i]);
                }
            }

            return result.ToArray();
        }

        public void SetItems(VehicleCustomizationDefinition[] configuredItems)
        {
            items = configuredItems ?? Array.Empty<VehicleCustomizationDefinition>();
        }
    }
}
