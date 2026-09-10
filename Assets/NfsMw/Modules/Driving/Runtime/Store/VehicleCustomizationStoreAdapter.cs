using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Fulfillment adapter for all body-shop visual products.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleCustomizationStoreAdapter :
        MonoBehaviour,
        IVehicleStoreProductAdapter
    {
        public bool CanHandle(IVehicleStoreProduct product)
        {
            return product is IVehicleCustomizationItem
                && product.ProductKind == VehicleStoreProductKind.Customization;
        }

        public bool CanReceive(
            VehicleStorePurchaseContext context,
            IVehicleStoreProduct product,
            out string failure)
        {
            if (!(product is IVehicleCustomizationItem customization)
                || !CanHandle(product))
            {
                failure = "This is not a body-shop product.";
                return false;
            }

            if (context == null || context.CurrentVehicle == null)
            {
                failure = "A current vehicle is required for a body-shop purchase.";
                return false;
            }

            return context.CurrentVehicle.CanInstallCustomization(customization, out failure);
        }

        public bool TryReceive(
            VehicleStorePurchaseContext context,
            IVehicleStoreProduct product,
            out string failure)
        {
            if (!(product is IVehicleCustomizationItem customization)
                || !CanHandle(product))
            {
                failure = "This is not a body-shop product.";
                return false;
            }

            if (context == null || context.CurrentVehicle == null)
            {
                failure = "A current vehicle is required for a body-shop purchase.";
                return false;
            }

            return context.CurrentVehicle.TryInstallCustomization(customization, out failure);
        }
    }
}
