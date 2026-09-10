using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// A reusable shop front. Multiple locations can point at the same
    /// catalog while exposing different categories.
    /// </summary>
    public interface IVehicleStorefront
    {
        string StoreId { get; }

        string DisplayName { get; }

        VehicleStoreCategory Category { get; }

        VehicleStoreCatalog Catalog { get; }
    }

    [CreateAssetMenu(
        menuName = "NFS MW Remaster/Driving/Vehicle Storefront",
        fileName = "VehicleStorefront")]
    public sealed class AssetVehicleStorefront : ScriptableObject, IVehicleStorefront
    {
        [SerializeField] private string storeId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private VehicleStoreCategory category;
        [SerializeField] private VehicleStoreCatalog catalog = null!;

        public string StoreId
        {
            get
            {
                return string.IsNullOrWhiteSpace(storeId)
                    ? name
                    : storeId;
            }
        }

        public string DisplayName
        {
            get
            {
                return string.IsNullOrWhiteSpace(displayName)
                    ? Category.ToString()
                    : displayName;
            }
        }

        public VehicleStoreCategory Category
        {
            get { return category; }
        }

        public VehicleStoreCatalog Catalog
        {
            get { return catalog; }
        }

        public void Configure(
            string configuredId,
            string configuredDisplayName,
            VehicleStoreCategory configuredCategory,
            VehicleStoreCatalog configuredCatalog)
        {
            storeId = configuredId;
            displayName = configuredDisplayName;
            category = configuredCategory;
            catalog = configuredCatalog;
        }
    }
}
