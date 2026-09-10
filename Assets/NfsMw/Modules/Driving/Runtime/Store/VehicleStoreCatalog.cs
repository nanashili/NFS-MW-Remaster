using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// One product inventory shared by every shop front. Unity serializes the
    /// assets as ScriptableObjects, while callers consume only the product
    /// interface, so future product families do not change the store API.
    /// </summary>
    [CreateAssetMenu(
        menuName = "NFS MW Remaster/Driving/Vehicle Store Catalog",
        fileName = "VehicleStoreCatalog")]
    public sealed class VehicleStoreCatalog : ScriptableObject
    {
        [SerializeField] private ScriptableObject[] products =
            Array.Empty<ScriptableObject>();
        private readonly List<IVehicleStoreProduct> runtimeProducts =
            new List<IVehicleStoreProduct>();

        public IReadOnlyList<ScriptableObject> ProductAssets
        {
            get { return products ?? Array.Empty<ScriptableObject>(); }
        }

        public IVehicleStoreProduct[] GetForStore(VehicleStoreCategory category)
        {
            List<IVehicleStoreProduct> result = new List<IVehicleStoreProduct>();
            if (products == null)
            {
                return result.ToArray();
            }

            for (int i = 0; i < products.Length; i++)
            {
                AddIfVisible(products[i] as IVehicleStoreProduct, category, result);
            }

            for (int i = 0; i < runtimeProducts.Count; i++)
            {
                AddIfVisible(runtimeProducts[i], category, result);
            }

            return result.ToArray();
        }

        public IVehicleStoreProduct Find(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId) || products == null)
            {
                return null!;
            }

            for (int i = 0; i < products.Length; i++)
            {
                if (!(products[i] is IVehicleStoreProduct product))
                {
                    continue;
                }

                if (string.Equals(product.ProductId, productId, StringComparison.Ordinal))
                {
                    return product;
                }
            }

            for (int i = 0; i < runtimeProducts.Count; i++)
            {
                IVehicleStoreProduct product = runtimeProducts[i];
                if (product != null
                    && string.Equals(product.ProductId, productId, StringComparison.Ordinal))
                {
                    return product;
                }
            }

            return null!;
        }

        public void SetProducts(ScriptableObject[] configuredProducts)
        {
            products = configuredProducts ?? Array.Empty<ScriptableObject>();
        }

        /// <summary>
        /// Adds non-asset products for career rewards, network inventories, or
        /// tests. This list is runtime-only and never changes the serialized
        /// catalog asset.
        /// </summary>
        public void SetRuntimeProducts(IVehicleStoreProduct[] configuredProducts)
        {
            runtimeProducts.Clear();
            if (configuredProducts == null)
            {
                return;
            }

            for (int i = 0; i < configuredProducts.Length; i++)
            {
                if (configuredProducts[i] != null)
                {
                    runtimeProducts.Add(configuredProducts[i]);
                }
            }
        }

        private static void AddIfVisible(
            IVehicleStoreProduct product,
            VehicleStoreCategory category,
            List<IVehicleStoreProduct> result)
        {
            if (product == null || !product.IsAvailable)
            {
                return;
            }

            if (category == VehicleStoreCategory.OneStopShop
                || product.StoreCategory == category)
            {
                result.Add(product);
            }
        }
    }
}
