using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Coordinates store navigation and checkout, but does not own vehicle
    /// physics, visual rendering, currency persistence, or garage spawning.
    /// Attach it to a vehicle for body/performance purchases or to a shop
    /// scene object and inject the current vehicle and garage explicitly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleStoreSystem : MonoBehaviour, IVehicleStoreSession
    {
        [SerializeField] private VehicleStoreCatalog catalog = null!;
        [SerializeField] private AssetVehicleStorefront defaultStore = null!;
        [SerializeField] private MonoBehaviour currentVehicleComponent = null!;
        [SerializeField] private MonoBehaviour walletComponent = null!;
        [SerializeField] private MonoBehaviour ownershipComponent = null!;
        [SerializeField] private MonoBehaviour garageComponent = null!;
        [SerializeField] private bool autoDiscoverAdapters = true;
        [SerializeField] private MonoBehaviour[] adapterComponents =
            Array.Empty<MonoBehaviour>();

        private readonly List<IVehicleStoreProduct> visibleProducts =
            new List<IVehicleStoreProduct>();
        private readonly List<IVehicleStoreProductAdapter> adapters =
            new List<IVehicleStoreProductAdapter>();
        private VehicleStoreCatalog activeCatalog = null!;
        private IVehicleStorefront activeStore;
        private VehicleStoreCategory activeCategory = VehicleStoreCategory.OneStopShop;
        private string activeStoreId = "one-stop";
        private VehicleController currentVehicle;
        private IVehicleStoreWallet wallet;
        private IVehicleStoreOwnership ownership;
        private IVehicleGarage garage;

        public event Action<VehicleStoreCategory> StoreOpened;

        public event Action<IVehicleStoreProduct> ProductPurchased;

        public VehicleStoreCatalog Catalog
        {
            get { return catalog; }
        }

        public IVehicleStorefront ActiveStore
        {
            get { return activeStore; }
        }

        public VehicleStoreCategory ActiveCategory
        {
            get { return activeCategory; }
        }

        public string ActiveStoreId
        {
            get { return activeStoreId; }
        }

        public IReadOnlyList<IVehicleStoreProduct> VisibleProducts
        {
            get { return visibleProducts; }
        }

        public VehicleController CurrentVehicle
        {
            get
            {
                ResolveDependencies();
                return currentVehicle;
            }
        }

        public IVehicleStoreWallet Wallet
        {
            get
            {
                ResolveDependencies();
                return wallet;
            }
        }

        public IVehicleStoreOwnership Ownership
        {
            get
            {
                ResolveDependencies();
                return ownership;
            }
        }

        public IVehicleGarage Garage
        {
            get
            {
                ResolveDependencies();
                return garage;
            }
        }

        private void Awake()
        {
            ResolveDependencies();
            ResolveAdapters();

            if (defaultStore != null)
            {
                OpenStore(defaultStore, out _);
            }
            else
            {
                OpenCategory(VehicleStoreCategory.OneStopShop, out _);
            }
        }

        public bool OpenStore(IVehicleStorefront store, out string failure)
        {
            if (store == null)
            {
                failure = "Storefront is null.";
                return false;
            }

            VehicleStoreCatalog selectedCatalog = store.Catalog != null
                ? store.Catalog
                : catalog;
            if (selectedCatalog == null)
            {
                failure = "No catalog is assigned to the storefront.";
                return false;
            }

            if (!IsValidCategory(store.Category))
            {
                failure = "Storefront has an invalid category.";
                return false;
            }

            activeStore = store;
            activeCatalog = selectedCatalog;
            activeCategory = store.Category;
            activeStoreId = string.IsNullOrWhiteSpace(store.StoreId)
                ? store.Category.ToString()
                : store.StoreId;
            RefreshVisibleProducts();
            StoreOpened?.Invoke(activeCategory);
            failure = string.Empty;
            return true;
        }

        public bool OpenCategory(VehicleStoreCategory category, out string failure)
        {
            if (!IsValidCategory(category))
            {
                failure = "Store category is invalid.";
                return false;
            }

            if (catalog == null)
            {
                failure = "No vehicle store catalog is assigned.";
                return false;
            }

            activeStore = null!;
            activeCatalog = catalog;
            activeCategory = category;
            activeStoreId = category.ToString();
            RefreshVisibleProducts();
            StoreOpened?.Invoke(activeCategory);
            failure = string.Empty;
            return true;
        }

        public bool TryPurchaseById(string productId, out string failure)
        {
            IVehicleStoreProduct product = FindVisibleProduct(productId);
            if (product == null)
            {
                failure = "The active store does not sell that product.";
                return false;
            }

            return TryPurchase(product, out failure);
        }

        public bool TryPurchase(IVehicleStoreProduct product, out string failure)
        {
            if (product == null)
            {
                failure = "Product is null.";
                return false;
            }

            IVehicleStoreProduct listedProduct = FindVisibleProduct(product.ProductId);
            if (listedProduct == null)
            {
                failure = "The active store does not sell that product.";
                return false;
            }

            ResolveDependencies();
            ResolveAdapters();
            VehicleStorePurchaseContext context = new VehicleStorePurchaseContext(
                currentVehicle,
                wallet,
                ownership,
                garage);

            for (int i = 0; i < adapters.Count; i++)
            {
                IVehicleStoreProductAdapter adapter = adapters[i];
                if (!adapter.CanHandle(listedProduct))
                {
                    continue;
                }

                bool purchased = VehicleStoreCheckout.TryPurchase(
                    listedProduct,
                    context,
                    adapter,
                    out failure);
                if (purchased)
                {
                    ProductPurchased?.Invoke(listedProduct);
                }

                return purchased;
            }

            failure = "No product adapter is attached for this product kind.";
            return false;
        }

        public void SetCatalog(VehicleStoreCatalog configuredCatalog)
        {
            catalog = configuredCatalog;
            if (activeStore == null)
            {
                activeCatalog = configuredCatalog;
                RefreshVisibleProducts();
            }
        }

        public void SetDefaultStore(AssetVehicleStorefront configuredStore)
        {
            defaultStore = configuredStore;
            if (configuredStore != null)
            {
                OpenStore(configuredStore, out _);
            }
        }

        public void SetCurrentVehicle(VehicleController configuredVehicle)
        {
            currentVehicle = configuredVehicle;
            currentVehicleComponent = configuredVehicle;
        }

        public void SetWallet(MonoBehaviour configuredWallet)
        {
            walletComponent = configuredWallet;
            wallet = configuredWallet as IVehicleStoreWallet;
        }

        public void SetOwnership(MonoBehaviour configuredOwnership)
        {
            ownershipComponent = configuredOwnership;
            ownership = configuredOwnership as IVehicleStoreOwnership;
        }

        public void SetGarage(MonoBehaviour configuredGarage)
        {
            garageComponent = configuredGarage;
            garage = configuredGarage as IVehicleGarage;
        }

        public void SetAdapters(MonoBehaviour[] configuredAdapters)
        {
            adapterComponents = configuredAdapters ?? Array.Empty<MonoBehaviour>();
            autoDiscoverAdapters = false;
            ResolveAdapters();
        }

        private IVehicleStoreProduct FindVisibleProduct(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                return null!;
            }

            for (int i = 0; i < visibleProducts.Count; i++)
            {
                if (string.Equals(
                    visibleProducts[i].ProductId,
                    productId,
                    StringComparison.Ordinal))
                {
                    return visibleProducts[i];
                }
            }

            return null!;
        }

        private void RefreshVisibleProducts()
        {
            visibleProducts.Clear();
            if (activeCatalog == null)
            {
                return;
            }

            IVehicleStoreProduct[] products = activeCatalog.GetForStore(activeCategory);
            for (int i = 0; i < products.Length; i++)
            {
                visibleProducts.Add(products[i]);
            }
        }

        private void ResolveDependencies()
        {
            if (currentVehicle == null)
            {
                currentVehicle = currentVehicleComponent as VehicleController;
                if (currentVehicle == null)
                {
                    currentVehicle = GetComponent<VehicleController>();
                }

                if (currentVehicle == null)
                {
                    currentVehicle = GetComponentInParent<VehicleController>();
                }
            }

            MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (wallet == null && behaviour is IVehicleStoreWallet discoveredWallet)
                {
                    wallet = discoveredWallet;
                    walletComponent = behaviour;
                }

                if (ownership == null
                    && behaviour is IVehicleStoreOwnership discoveredOwnership)
                {
                    ownership = discoveredOwnership;
                    ownershipComponent = behaviour;
                }

                if (garage == null && behaviour is IVehicleGarage discoveredGarage)
                {
                    garage = discoveredGarage;
                    garageComponent = behaviour;
                }
            }

            if (wallet == null && walletComponent != null)
            {
                wallet = walletComponent as IVehicleStoreWallet;
            }

            if (ownership == null && ownershipComponent != null)
            {
                ownership = ownershipComponent as IVehicleStoreOwnership;
            }

            if (garage == null && garageComponent != null)
            {
                garage = garageComponent as IVehicleGarage;
            }
        }

        private void ResolveAdapters()
        {
            adapters.Clear();
            MonoBehaviour[] candidates = autoDiscoverAdapters
                ? GetComponentsInChildren<MonoBehaviour>(true)
                : adapterComponents;
            if (candidates == null)
            {
                return;
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] is IVehicleStoreProductAdapter adapter
                    && !adapters.Contains(adapter))
                {
                    adapters.Add(adapter);
                }
            }
        }

        private static bool IsValidCategory(VehicleStoreCategory category)
        {
            return Enum.IsDefined(typeof(VehicleStoreCategory), category);
        }
    }
}
