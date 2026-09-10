using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// The shop fronts exposed by the career loop. OneStopShop is the shared
    /// front used by locations that sell every product family.
    /// </summary>
    public enum VehicleStoreCategory
    {
        BodyShop,
        PerformanceShop,
        CarShow,
        OneStopShop
    }

    public enum VehicleStoreProductKind
    {
        Customization,
        PerformanceUpgrade,
        Vehicle
    }

    /// <summary>
    /// Stable store metadata shared by body parts, performance parts, and
    /// vehicle entries. The store never needs to know the concrete asset type.
    /// </summary>
    public interface IVehicleStoreProduct
    {
        string ProductId { get; }

        string DisplayName { get; }

        VehicleStoreCategory StoreCategory { get; }

        VehicleStoreProductKind ProductKind { get; }

        int Price { get; }

        bool IsAvailable { get; }
    }

    /// <summary>
    /// Additional data required by the car-show adapter. A car can be listed
    /// before its prefab arrives; the garage adapter decides when it can be
    /// acquired.
    /// </summary>
    public interface IVehicleCarStoreProduct : IVehicleStoreProduct
    {
        string VehicleDefinitionId { get; }

        GameObject VehiclePrefab { get; }

        Sprite Preview { get; }
    }

}
