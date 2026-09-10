using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Currency is a port rather than a field on the store. The same shop can
    /// therefore use career cash, a server-backed wallet, or a test balance.
    /// </summary>
    public interface IVehicleStoreWallet
    {
        int Balance { get; }

        bool CanAfford(int amount, out string failure);

        bool TrySpend(int amount, out string failure);
    }

    /// <summary>
    /// Career-facing wallet contract. Store checkout only needs to spend, so
    /// the smaller IVehicleStoreWallet remains compatible with server-backed
    /// checkout ports; rewards and save participants use this richer seam.
    /// </summary>
    public interface IVehicleWallet : IVehicleStoreWallet
    {
        bool TryAdd(int amount, out string failure);
    }

    /// <summary>
    /// Product ownership is deliberately separate from installation. A part
    /// can be purchased once and then installed, replaced, or sold by a later
    /// system without changing the catalog.
    /// </summary>
    public interface IVehicleStoreOwnership
    {
        bool IsOwned(string productId);

        bool CanGrant(string productId, out string failure);

        bool TryGrant(string productId, out string failure);
    }

    public interface IVehicleStoreOwnershipRollback
    {
        bool TryRevoke(string productId, out string failure);
    }

    /// <summary>
    /// Car-show products are acquired by a garage rather than installed on the
    /// currently active vehicle.
    /// </summary>
    public interface IVehicleGarage
    {
        bool OwnsVehicle(string vehicleDefinitionId);

        bool CanAcquire(IVehicleCarStoreProduct product, out string failure);

        bool TryAcquire(IVehicleCarStoreProduct product, out string failure);
    }

    /// <summary>
    /// Product-family adapter seam. Performance, body, car-show, DLC, and
    /// future product types can each own their fulfillment rules without a
    /// growing switch statement in VehicleStoreSystem.
    /// </summary>
    public interface IVehicleStoreProductAdapter
    {
        bool CanHandle(IVehicleStoreProduct product);

        bool CanReceive(
            VehicleStorePurchaseContext context,
            IVehicleStoreProduct product,
            out string failure);

        bool TryReceive(
            VehicleStorePurchaseContext context,
            IVehicleStoreProduct product,
            out string failure);
    }

    /// <summary>
    /// UI-facing store session seam. A menu can bind to this interface while
    /// the runtime implementation, catalog source, and product adapters are
    /// replaced independently.
    /// </summary>
    public interface IVehicleStoreSession
    {
        VehicleStoreCategory ActiveCategory { get; }

        string ActiveStoreId { get; }

        IReadOnlyList<IVehicleStoreProduct> VisibleProducts { get; }

        bool OpenStore(IVehicleStorefront store, out string failure);

        bool OpenCategory(VehicleStoreCategory category, out string failure);

        bool TryPurchaseById(string productId, out string failure);

        bool TryPurchase(IVehicleStoreProduct product, out string failure);
    }

    /// <summary>
    /// Runtime dependencies passed to an adapter during checkout. All ports
    /// are optional except for the adapter itself; priced products require a
    /// wallet, while car-show products require a garage.
    /// </summary>
    public sealed class VehicleStorePurchaseContext
    {
        public VehicleStorePurchaseContext(
            VehicleController currentVehicle,
            IVehicleStoreWallet wallet,
            IVehicleStoreOwnership ownership,
            IVehicleGarage garage)
        {
            CurrentVehicle = currentVehicle;
            Wallet = wallet;
            Ownership = ownership;
            Garage = garage;
        }

        public VehicleController CurrentVehicle { get; }

        public IVehicleStoreWallet Wallet { get; }

        public IVehicleStoreOwnership Ownership { get; }

        public IVehicleGarage Garage { get; }
    }

    /// <summary>
    /// Pure checkout orchestration. The preflight calls must be side-effect
    /// free; concrete adapters then receive the product, after which the
    /// already-validated wallet and ownership ports are committed. Unity
    /// gameplay is single-threaded, so this keeps the transaction small while
    /// leaving persistence and backend atomicity to injected ports.
    /// </summary>
    public static class VehicleStoreCheckout
    {
        public static bool TryPurchase(
            IVehicleStoreProduct product,
            VehicleStorePurchaseContext context,
            IVehicleStoreProductAdapter adapter,
            out string failure)
        {
            if (product == null)
            {
                failure = "Product is null.";
                return false;
            }

            if (context == null)
            {
                failure = "Purchase context is null.";
                return false;
            }

            if (adapter == null)
            {
                failure = "No product adapter is available.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(product.ProductId))
            {
                failure = "Product must have a stable ID.";
                return false;
            }

            if (product.Price < 0)
            {
                failure = "Product price cannot be negative.";
                return false;
            }

            if (!product.IsAvailable)
            {
                failure = "This product is not currently available.";
                return false;
            }

            if (!adapter.CanHandle(product))
            {
                failure = "No product adapter handles this product kind.";
                return false;
            }

            if (context.Ownership != null)
            {
                if (context.Ownership.IsOwned(product.ProductId))
                {
                    failure = "This product is already owned.";
                    return false;
                }

                if (!context.Ownership.CanGrant(product.ProductId, out failure))
                {
                    return false;
                }
            }

            if (!adapter.CanReceive(context, product, out failure))
            {
                return false;
            }

            if (product.Price > 0 && context.Wallet == null)
            {
                failure = "A wallet is required for a priced product.";
                return false;
            }

            if (context.Wallet != null
                && !context.Wallet.CanAfford(product.Price, out failure))
            {
                return false;
            }

            if (product.Price > 0 && !(context.Wallet is IVehicleWallet))
            {
                failure = "A reversible wallet transaction is required for live purchase fulfillment.";
                return false;
            }
            if (context.Ownership != null && !(context.Ownership is IVehicleStoreOwnershipRollback))
            {
                failure = "A reversible ownership transaction is required for live purchase fulfillment.";
                return false;
            }

            // Commit currency and ownership only after all validation, with compensating
            // actions if the live adapter rejects the staged operation.
            if (context.Wallet != null && !context.Wallet.TrySpend(product.Price, out failure))
            {
                return false;
            }

            bool granted = context.Ownership == null
                || context.Ownership.TryGrant(product.ProductId, out failure);
            if (!granted)
            {
                if (context.Wallet != null && product.Price > 0 && context.Wallet is IVehicleWallet wallet
                    && !wallet.TryAdd(product.Price, out string refundFailure))
                    failure += " Refund failed: " + refundFailure;
                return false;
            }

            if (!adapter.TryReceive(context, product, out failure))
            {
                if (context.Ownership is IVehicleStoreOwnershipRollback rollback
                    && !rollback.TryRevoke(product.ProductId, out string revokeFailure))
                    failure += " Ownership rollback failed: " + revokeFailure;
                if (context.Wallet != null && product.Price > 0 && context.Wallet is IVehicleWallet wallet
                    && !wallet.TryAdd(product.Price, out string refundFailure))
                    failure += " Refund failed: " + refundFailure;
                return false;
            }

            failure = string.Empty;
            return true;
        }
    }
}
