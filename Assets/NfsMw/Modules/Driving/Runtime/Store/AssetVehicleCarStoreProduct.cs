using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Asset-backed car listing for the car-show category. The optional
    /// references keep the store usable while vehicle art is still pending.
    /// </summary>
    [CreateAssetMenu(
        menuName = "NFS MW Remaster/Driving/Car Show Product",
        fileName = "CarShowProduct")]
    public sealed class AssetVehicleCarStoreProduct :
        ScriptableObject,
        IVehicleCarStoreProduct
    {
        [SerializeField] private string productId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private string vehicleDefinitionId = string.Empty;
        [SerializeField, Min(0)] private int price;
        [SerializeField] private bool available = true;
        [SerializeField] private GameObject vehiclePrefab = null!;
        [SerializeField] private Sprite preview = null!;

        public string ProductId
        {
            get
            {
                return string.IsNullOrWhiteSpace(productId)
                    ? name
                    : productId;
            }
        }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    return displayName;
                }

                return string.IsNullOrWhiteSpace(vehicleDefinitionId)
                    ? ProductId
                    : vehicleDefinitionId;
            }
        }

        public VehicleStoreCategory StoreCategory
        {
            get { return VehicleStoreCategory.CarShow; }
        }

        public VehicleStoreProductKind ProductKind
        {
            get { return VehicleStoreProductKind.Vehicle; }
        }

        public int Price
        {
            get { return Mathf.Max(0, price); }
        }

        public bool IsAvailable
        {
            get { return available; }
        }

        public string VehicleDefinitionId
        {
            get
            {
                return string.IsNullOrWhiteSpace(vehicleDefinitionId)
                    ? ProductId
                    : vehicleDefinitionId;
            }
        }

        public GameObject VehiclePrefab
        {
            get { return vehiclePrefab; }
        }

        public Sprite Preview
        {
            get { return preview; }
        }

        public void ConfigureMetadata(
            string configuredProductId,
            string configuredDisplayName,
            string configuredVehicleDefinitionId,
            int configuredPrice,
            bool configuredAvailable)
        {
            productId = configuredProductId;
            displayName = configuredDisplayName;
            vehicleDefinitionId = configuredVehicleDefinitionId;
            price = Mathf.Max(0, configuredPrice);
            available = configuredAvailable;
        }

        public void ConfigurePresentation(
            GameObject configuredPrefab,
            Sprite configuredPreview)
        {
            vehiclePrefab = configuredPrefab;
            preview = configuredPreview;
        }
    }
}
