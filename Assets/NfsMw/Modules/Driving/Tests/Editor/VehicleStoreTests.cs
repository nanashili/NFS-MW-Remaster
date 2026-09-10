using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleStoreTests
    {
        [Test]
        public void CatalogFiltersBodyPerformanceCarShowAndOneStopProducts()
        {
            VehicleStoreCatalog catalog =
                ScriptableObject.CreateInstance<VehicleStoreCatalog>();
            TunedVehiclePerformanceUpgrade performance =
                ScriptableObject.CreateInstance<TunedVehiclePerformanceUpgrade>();
            AssetVehicleCustomization body =
                ScriptableObject.CreateInstance<AssetVehicleCustomization>();
            AssetVehicleCarStoreProduct car =
                ScriptableObject.CreateInstance<AssetVehicleCarStoreProduct>();

            try
            {
                performance.ConfigureMetadata(
                    "engine_pro",
                    "Pro Engine",
                    VehiclePerformanceCategory.Engine,
                    VehiclePerformanceTier.Pro,
                    5000,
                    false);
                body.ConfigureMetadata(
                    "bodykit_01",
                    "Street Body Kit",
                    VehicleCustomizationCategory.BodyKit,
                    VehicleCustomizationStyle.Sport,
                    2500,
                    false);
                car.ConfigureMetadata(
                    "car_supra",
                    "Street Coupe",
                    "vehicle_supra",
                    30000,
                    true);
                catalog.SetProducts(new ScriptableObject[] { performance, body, car });

                Assert.That(
                    catalog.GetForStore(VehicleStoreCategory.BodyShop),
                    Has.Length.EqualTo(1));
                Assert.That(
                    catalog.GetForStore(VehicleStoreCategory.PerformanceShop),
                    Has.Length.EqualTo(1));
                Assert.That(
                    catalog.GetForStore(VehicleStoreCategory.CarShow),
                    Has.Length.EqualTo(1));
                Assert.That(
                    catalog.GetForStore(VehicleStoreCategory.OneStopShop),
                    Has.Length.EqualTo(3));
            }
            finally
            {
                Object.DestroyImmediate(car);
                Object.DestroyImmediate(body);
                Object.DestroyImmediate(performance);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void CatalogAcceptsRuntimeInterfaceProducts()
        {
            VehicleStoreCatalog catalog =
                ScriptableObject.CreateInstance<VehicleStoreCatalog>();
            FakeProduct reward = new FakeProduct(
                "reward_junkman",
                VehicleStoreCategory.PerformanceShop,
                VehicleStoreProductKind.PerformanceUpgrade,
                0);

            try
            {
                catalog.SetRuntimeProducts(new IVehicleStoreProduct[] { reward });

                Assert.That(
                    catalog.GetForStore(VehicleStoreCategory.PerformanceShop),
                    Has.Length.EqualTo(1));
                Assert.That(catalog.Find(reward.ProductId), Is.SameAs(reward));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void CheckoutReceivesProductChargesWalletAndGrantsOwnership()
        {
            FakeProduct product = new FakeProduct(
                "engine_pro",
                VehicleStoreCategory.PerformanceShop,
                VehicleStoreProductKind.PerformanceUpgrade,
                100);
            FakeWallet wallet = new FakeWallet(250);
            FakeOwnership ownership = new FakeOwnership();
            FakeAdapter adapter = new FakeAdapter();
            VehicleStorePurchaseContext context = new VehicleStorePurchaseContext(
                null,
                wallet,
                ownership,
                null);

            Assert.That(
                VehicleStoreCheckout.TryPurchase(
                    product,
                    context,
                    adapter,
                    out string failure),
                Is.True,
                failure);
            Assert.That(wallet.Balance, Is.EqualTo(150));
            Assert.That(adapter.ReceiveCount, Is.EqualTo(1));
            Assert.That(ownership.IsOwned(product.ProductId), Is.True);
        }

        [Test]
        public void InsufficientFundsDoNotReceiveOrGrantProduct()
        {
            FakeProduct product = new FakeProduct(
                "turbo_super",
                VehicleStoreCategory.PerformanceShop,
                VehicleStoreProductKind.PerformanceUpgrade,
                500);
            FakeWallet wallet = new FakeWallet(100);
            FakeOwnership ownership = new FakeOwnership();
            FakeAdapter adapter = new FakeAdapter();
            VehicleStorePurchaseContext context = new VehicleStorePurchaseContext(
                null,
                wallet,
                ownership,
                null);

            Assert.That(
                VehicleStoreCheckout.TryPurchase(
                    product,
                    context,
                    adapter,
                    out string failure),
                Is.False);
            Assert.That(failure, Does.Contain("Not enough"));
            Assert.That(wallet.Balance, Is.EqualTo(100));
            Assert.That(adapter.ReceiveCount, Is.EqualTo(0));
            Assert.That(ownership.IsOwned(product.ProductId), Is.False);
        }

        [Test]
        public void AdapterPreflightFailureDoesNotSpendWallet()
        {
            FakeProduct product = new FakeProduct(
                "bodykit_02",
                VehicleStoreCategory.BodyShop,
                VehicleStoreProductKind.Customization,
                200);
            FakeWallet wallet = new FakeWallet(500);
            FakeOwnership ownership = new FakeOwnership();
            FakeAdapter adapter = new FakeAdapter
            {
                CanReceiveResult = false
            };
            VehicleStorePurchaseContext context = new VehicleStorePurchaseContext(
                null,
                wallet,
                ownership,
                null);

            Assert.That(
                VehicleStoreCheckout.TryPurchase(
                    product,
                    context,
                    adapter,
                    out string failure),
                Is.False);
            Assert.That(failure, Does.Contain("not ready"));
            Assert.That(wallet.Balance, Is.EqualTo(500));
            Assert.That(adapter.ReceiveCount, Is.EqualTo(0));
        }

        [Test]
        public void OwnedProductCannotBePurchasedTwice()
        {
            FakeProduct product = new FakeProduct(
                "paint_black",
                VehicleStoreCategory.BodyShop,
                VehicleStoreProductKind.Customization,
                50);
            FakeWallet wallet = new FakeWallet(100);
            FakeOwnership ownership = new FakeOwnership();
            ownership.TryGrant(product.ProductId, out _);
            FakeAdapter adapter = new FakeAdapter();
            VehicleStorePurchaseContext context = new VehicleStorePurchaseContext(
                null,
                wallet,
                ownership,
                null);

            Assert.That(
                VehicleStoreCheckout.TryPurchase(
                    product,
                    context,
                    adapter,
                    out string failure),
                Is.False);
            Assert.That(failure, Does.Contain("already owned"));
            Assert.That(wallet.Balance, Is.EqualTo(100));
            Assert.That(adapter.ReceiveCount, Is.EqualTo(0));
        }

        private sealed class FakeProduct : IVehicleStoreProduct
        {
            public FakeProduct(
                string productId,
                VehicleStoreCategory storeCategory,
                VehicleStoreProductKind productKind,
                int price)
            {
                ProductId = productId;
                DisplayName = productId;
                StoreCategory = storeCategory;
                ProductKind = productKind;
                Price = price;
            }

            public string ProductId { get; }

            public string DisplayName { get; }

            public VehicleStoreCategory StoreCategory { get; }

            public VehicleStoreProductKind ProductKind { get; }

            public int Price { get; }

            public bool IsAvailable
            {
                get { return true; }
            }
        }

        private sealed class FakeAdapter : IVehicleStoreProductAdapter
        {
            public bool CanReceiveResult { get; set; } = true;

            public int ReceiveCount { get; private set; }

            public bool CanHandle(IVehicleStoreProduct product)
            {
                return true;
            }

            public bool CanReceive(
                VehicleStorePurchaseContext context,
                IVehicleStoreProduct product,
                out string failure)
            {
                failure = CanReceiveResult ? string.Empty : "Product is not ready.";
                return CanReceiveResult;
            }

            public bool TryReceive(
                VehicleStorePurchaseContext context,
                IVehicleStoreProduct product,
                out string failure)
            {
                ReceiveCount++;
                failure = string.Empty;
                return true;
            }
        }

        private sealed class FakeWallet : IVehicleStoreWallet
        {
            public FakeWallet(int balance)
            {
                Balance = balance;
            }

            public int Balance { get; private set; }

            public bool CanAfford(int amount, out string failure)
            {
                if (amount < 0 || Balance < amount)
                {
                    failure = "Not enough cash.";
                    return false;
                }

                failure = string.Empty;
                return true;
            }

            public bool TrySpend(int amount, out string failure)
            {
                if (!CanAfford(amount, out failure))
                {
                    return false;
                }

                Balance -= amount;
                return true;
            }
        }

        private sealed class FakeOwnership : IVehicleStoreOwnership
        {
            private readonly HashSet<string> owned = new HashSet<string>();

            public bool IsOwned(string productId)
            {
                return owned.Contains(productId);
            }

            public bool CanGrant(string productId, out string failure)
            {
                if (IsOwned(productId))
                {
                    failure = "This product is already owned.";
                    return false;
                }

                failure = string.Empty;
                return true;
            }

            public bool TryGrant(string productId, out string failure)
            {
                if (!CanGrant(productId, out failure))
                {
                    return false;
                }

                owned.Add(productId);
                return true;
            }
        }
    }
}
