using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Local ownership ledger for the prototype. A save-game implementation
    /// can implement the same interface and preserve the store boundary.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleStoreOwnership :
        MonoBehaviour,
        IVehicleStoreOwnership,
        IVehicleStoreOwnershipRollback,
        ICareerProfileParticipant
    {
        [SerializeField] private List<string> ownedProductIds =
            new List<string>();

        public string ProfileSectionId
        {
            get { return "store.owned-products"; }
        }

        public IReadOnlyList<string> OwnedProductIds
        {
            get { return ownedProductIds; }
        }

        public bool IsOwned(string productId)
        {
            return !string.IsNullOrWhiteSpace(productId)
                && ownedProductIds != null
                && ownedProductIds.Contains(productId);
        }

        public bool CanGrant(string productId, out string failure)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                failure = "Owned products must have a stable ID.";
                return false;
            }

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

            if (ownedProductIds == null)
            {
                ownedProductIds = new List<string>();
            }

            ownedProductIds.Add(productId);
            return true;
        }

        public bool TryRevoke(string productId, out string failure)
        {
            if (ownedProductIds == null || !ownedProductIds.Remove(productId))
            {
                failure = "Owned product was not present.";
                return false;
            }
            failure = string.Empty;
            return true;
        }

        public void Capture(CareerProfileData profile)
        {
            if (profile == null)
            {
                return;
            }

            profile.Normalize();
            profile.store.ownedProductIds = ownedProductIds == null
                ? new List<string>()
                : new List<string>(ownedProductIds);
            profile.Normalize();
        }

        public bool Restore(CareerProfileData profile, out string failure)
        {
            if (profile == null)
            {
                failure = "Profile is null.";
                return false;
            }

            profile.Normalize();
            ownedProductIds = new List<string>(profile.store.ownedProductIds);
            failure = string.Empty;
            return true;
        }
    }
}
