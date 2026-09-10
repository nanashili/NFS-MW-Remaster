using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Minimal garage receiver for car-show purchases. Vehicle spawning,
    /// slots, and active-car selection remain outside the store module.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleStoreGarage :
        MonoBehaviour,
        IVehicleGarage,
        ICareerProfileParticipant
    {
        [SerializeField] private List<string> ownedVehicleIds =
            new List<string>();

        public string ProfileSectionId
        {
            get { return "store.owned-vehicles"; }
        }

        public IReadOnlyList<string> OwnedVehicleIds
        {
            get { return ownedVehicleIds; }
        }

        public bool OwnsVehicle(string vehicleDefinitionId)
        {
            return !string.IsNullOrWhiteSpace(vehicleDefinitionId)
                && ownedVehicleIds != null
                && ownedVehicleIds.Contains(vehicleDefinitionId);
        }

        public bool CanAcquire(
            IVehicleCarStoreProduct product,
            out string failure)
        {
            if (product == null)
            {
                failure = "Car product is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(product.VehicleDefinitionId))
            {
                failure = "Car product must have a stable vehicle ID.";
                return false;
            }

            if (OwnsVehicle(product.VehicleDefinitionId))
            {
                failure = "This vehicle is already in the garage.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        public bool TryAcquire(
            IVehicleCarStoreProduct product,
            out string failure)
        {
            if (!CanAcquire(product, out failure))
            {
                return false;
            }

            if (ownedVehicleIds == null)
            {
                ownedVehicleIds = new List<string>();
            }

            ownedVehicleIds.Add(product.VehicleDefinitionId);
            return true;
        }

        public void Capture(CareerProfileData profile)
        {
            if (profile == null)
            {
                return;
            }

            profile.Normalize();
            profile.store.ownedVehicleIds = ownedVehicleIds == null
                ? new List<string>()
                : new List<string>(ownedVehicleIds);
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
            ownedVehicleIds = new List<string>(profile.store.ownedVehicleIds);
            failure = string.Empty;
            return true;
        }
    }
}
