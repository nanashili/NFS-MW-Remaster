using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Fulfillment adapter for performance products. It validates through the
    /// vehicle facade, then delegates installation to the performance module.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehiclePerformanceStoreAdapter :
        MonoBehaviour,
        IVehicleStoreProductAdapter
    {
        public bool CanHandle(IVehicleStoreProduct product)
        {
            return product is IVehiclePerformanceUpgrade
                && product.ProductKind == VehicleStoreProductKind.PerformanceUpgrade;
        }

        public bool CanReceive(
            VehicleStorePurchaseContext context,
            IVehicleStoreProduct product,
            out string failure)
        {
            if (!(product is IVehiclePerformanceUpgrade upgrade)
                || !CanHandle(product))
            {
                failure = "This is not a performance product.";
                return false;
            }

            if (context == null || context.CurrentVehicle == null)
            {
                failure = "A current vehicle is required for a performance purchase.";
                return false;
            }

            return context.CurrentVehicle.CanInstallPerformanceUpgrade(upgrade, out failure);
        }

        public bool TryReceive(
            VehicleStorePurchaseContext context,
            IVehicleStoreProduct product,
            out string failure)
        {
            if (!(product is IVehiclePerformanceUpgrade upgrade)
                || !CanHandle(product))
            {
                failure = "This is not a performance product.";
                return false;
            }

            if (context == null || context.CurrentVehicle == null)
            {
                failure = "A current vehicle is required for a performance purchase.";
                return false;
            }

            return context.CurrentVehicle.TryInstallPerformanceUpgrade(upgrade, out failure);
        }
    }
}
