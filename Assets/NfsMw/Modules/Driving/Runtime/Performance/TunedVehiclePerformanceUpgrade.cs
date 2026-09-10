using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Data-only upgrade implementation covering the stock MW-style parts.
    /// The abstract definition remains the extension point for custom parts.
    /// </summary>
    [CreateAssetMenu(
        menuName = "NFS MW Remaster/Driving/Performance Upgrade",
        fileName = "PerformanceUpgrade")]
    public sealed class TunedVehiclePerformanceUpgrade : VehiclePerformanceUpgradeDefinition
    {
        [SerializeField] private VehiclePerformanceModifier modifier = new VehiclePerformanceModifier();

        public VehiclePerformanceModifier Modifier
        {
            get { return modifier; }
        }

        public void ConfigureModifier(VehiclePerformanceModifier configuredModifier)
        {
            modifier = configuredModifier ?? new VehiclePerformanceModifier();
        }

        public override void Apply(VehicleTuning tuning)
        {
            if (tuning == null)
            {
                return;
            }

            if (modifier != null)
            {
                modifier.ApplyTo(tuning);
            }
        }
    }
}
