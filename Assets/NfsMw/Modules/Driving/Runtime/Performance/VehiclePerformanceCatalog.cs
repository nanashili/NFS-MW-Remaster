using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(
        menuName = "NFS MW Remaster/Driving/Performance Catalog",
        fileName = "VehiclePerformanceCatalog")]
    public sealed class VehiclePerformanceCatalog : ScriptableObject
    {
        [SerializeField] private VehiclePerformanceUpgradeDefinition[] upgrades =
            Array.Empty<VehiclePerformanceUpgradeDefinition>();

        public IReadOnlyList<VehiclePerformanceUpgradeDefinition> Upgrades
        {
            get { return upgrades ?? Array.Empty<VehiclePerformanceUpgradeDefinition>(); }
        }

        public VehiclePerformanceUpgradeDefinition Find(string upgradeId)
        {
            if (string.IsNullOrWhiteSpace(upgradeId) || upgrades == null)
            {
                return null!;
            }

            for (int i = 0; i < upgrades.Length; i++)
            {
                if (upgrades[i] != null
                    && string.Equals(upgrades[i].UpgradeId, upgradeId, StringComparison.Ordinal))
                {
                    return upgrades[i];
                }
            }

            return null!;
        }

        public VehiclePerformanceUpgradeDefinition[] GetForCategory(
            VehiclePerformanceCategory category)
        {
            List<VehiclePerformanceUpgradeDefinition> result =
                new List<VehiclePerformanceUpgradeDefinition>();
            if (upgrades == null)
            {
                return result.ToArray();
            }

            for (int i = 0; i < upgrades.Length; i++)
            {
                if (upgrades[i] != null && upgrades[i].Category == category)
                {
                    result.Add(upgrades[i]);
                }
            }

            return result.ToArray();
        }

        public void SetUpgrades(VehiclePerformanceUpgradeDefinition[] configuredUpgrades)
        {
            upgrades = configuredUpgrades ?? Array.Empty<VehiclePerformanceUpgradeDefinition>();
        }
    }
}
