using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Fulfillment adapter for car-show products. It does not replace the
    /// active vehicle; ownership and spawning belong to the garage boundary.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleCarShowStoreAdapter :
        MonoBehaviour,
        IVehicleStoreProductAdapter
    {
        public bool CanHandle(IVehicleStoreProduct product)
        {
            return product is IVehicleCarStoreProduct
                && product.ProductKind == VehicleStoreProductKind.Vehicle;
        }

        public bool CanReceive(
            VehicleStorePurchaseContext context,
            IVehicleStoreProduct product,
            out string failure)
        {
            if (!(product is IVehicleCarStoreProduct car)
                || !CanHandle(product))
            {
                failure = "This is not a car-show product.";
                return false;
            }

            if (context == null || context.Garage == null)
            {
                failure = "A garage is required for a car-show purchase.";
                return false;
            }

            return context.Garage.CanAcquire(car, out failure);
        }

        public bool TryReceive(
            VehicleStorePurchaseContext context,
            IVehicleStoreProduct product,
            out string failure)
        {
            if (!(product is IVehicleCarStoreProduct car)
                || !CanHandle(product))
            {
                failure = "This is not a car-show product.";
                return false;
            }

            if (context == null || context.Garage == null)
            {
                failure = "A garage is required for a car-show purchase.";
                return false;
            }

            return context.Garage.TryAcquire(car, out failure);
        }
    }
}
