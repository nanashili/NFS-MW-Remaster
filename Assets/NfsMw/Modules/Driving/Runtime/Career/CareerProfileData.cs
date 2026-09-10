using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Versioned, Unity-serializable career save contract. It contains stable
    /// IDs and primitive values only, so no scene object or asset reference is
    /// required to persist a profile.
    /// </summary>
    [Serializable]
    public sealed class CareerProfileData
    {
        public const int CurrentVersion = 4;

        public int saveVersion = CurrentVersion;
        public string profileId = string.Empty;
        public string playerName = string.Empty;
        public string activeVehicleId = string.Empty;
        public CareerWalletData wallet = new CareerWalletData();
        public CareerStoreData store = new CareerStoreData();
        public CareerBountyData bounty = new CareerBountyData();
        public CareerStatisticsData statistics = new CareerStatisticsData();
        public CareerFreeRoamData freeRoam = new CareerFreeRoamData();
        public CareerEconomyData economy = new CareerEconomyData();
        public CareerMissionData missions = new CareerMissionData();
        public CareerPoliceData police = new CareerPoliceData();
        // Optional so profiles written before dynamic weather remain loadable.
        public CareerWeatherData weather;
        public List<CareerVehicleData> vehicles = new List<CareerVehicleData>();

        public static CareerProfileData Create(string id, string name)
        {
            CareerProfileData profile = new CareerProfileData
            {
                profileId = id == null ? string.Empty : id.Trim(),
                playerName = name == null ? string.Empty : name.Trim(),
                activeVehicleId = "player_vehicle"
            };
            profile.Normalize();
            return profile;
        }

        public CareerVehicleData FindVehicle(string vehicleId)
        {
            if (string.IsNullOrWhiteSpace(vehicleId) || vehicles == null)
            {
                return null!;
            }

            for (int i = 0; i < vehicles.Count; i++)
            {
                CareerVehicleData vehicle = vehicles[i];
                if (vehicle != null
                    && string.Equals(
                        vehicle.vehicleId,
                        vehicleId,
                        StringComparison.Ordinal))
                {
                    return vehicle;
                }
            }

            return null!;
        }

        public CareerVehicleData GetOrCreateVehicle(string vehicleId)
        {
            if (string.IsNullOrWhiteSpace(vehicleId))
            {
                return null!;
            }

            Normalize();
            CareerVehicleData existing = FindVehicle(vehicleId.Trim());
            if (existing != null)
            {
                return existing;
            }

            CareerVehicleData created = new CareerVehicleData
            {
                vehicleId = vehicleId.Trim()
            };
            vehicles.Add(created);
            return created;
        }

        public void Normalize()
        {
            // Version one had no settlement journal. Never infer rewards/rival
            // victories during migration; the economy imports only opening cash.
            if (economy == null) economy = new CareerEconomyData();
            if (missions == null) missions = new CareerMissionData();
            if (police == null) police = new CareerPoliceData();
            if (saveVersion == 1 || saveVersion == 2 || saveVersion == 3) saveVersion = CurrentVersion;
            if (saveVersion <= 0)
            {
                saveVersion = CurrentVersion;
            }

            profileId = profileId == null ? string.Empty : profileId.Trim();
            playerName = playerName == null ? string.Empty : playerName.Trim();
            activeVehicleId = activeVehicleId == null
                ? string.Empty
                : activeVehicleId.Trim();

            if (wallet == null)
            {
                wallet = new CareerWalletData();
            }

            wallet.balance = Mathf.Max(0, wallet.balance);

            if (store == null)
            {
                store = new CareerStoreData();
            }

            if (store.ownedProductIds == null)
            {
                store.ownedProductIds = new List<string>();
            }

            if (store.ownedVehicleIds == null)
            {
                store.ownedVehicleIds = new List<string>();
            }

            NormalizeIds(store.ownedProductIds);
            NormalizeIds(store.ownedVehicleIds);

            if (bounty == null)
            {
                bounty = new CareerBountyData();
            }

            bounty.Normalize();

            if (statistics == null)
            {
                statistics = new CareerStatisticsData();
            }

            statistics.Normalize();

            if (freeRoam == null) freeRoam = new CareerFreeRoamData();
            if (freeRoam.discoveredLocationIds == null) freeRoam.discoveredLocationIds = new List<string>();
            if (freeRoam.completedEventIds == null) freeRoam.completedEventIds = new List<string>();
            NormalizeIds(freeRoam.discoveredLocationIds);
            NormalizeIds(freeRoam.completedEventIds);

            if (vehicles == null)
            {
                vehicles = new List<CareerVehicleData>();
            }

            HashSet<string> vehicleIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = vehicles.Count - 1; i >= 0; i--)
            {
                CareerVehicleData vehicle = vehicles[i];
                if (vehicle == null || string.IsNullOrWhiteSpace(vehicle.vehicleId))
                {
                    vehicles.RemoveAt(i);
                    continue;
                }

                vehicle.vehicleId = vehicle.vehicleId.Trim();
                if (!vehicleIds.Add(vehicle.vehicleId))
                {
                    vehicles.RemoveAt(i);
                    continue;
                }

                vehicle.Normalize();
            }
        }

        public bool Validate(string expectedProfileId, out string failure)
        {
            Normalize();
            if (saveVersion > CurrentVersion)
            {
                failure = "Profile save version is newer than this game build.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(profileId))
            {
                failure = "Profile must have a stable ID.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(activeVehicleId))
            {
                failure = "Profile must have an active vehicle ID.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(expectedProfileId)
                && !string.Equals(
                    profileId,
                    expectedProfileId.Trim(),
                    StringComparison.Ordinal))
            {
                failure = "Profile ID does not match the requested save slot.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static void NormalizeIds(List<string> ids)
        {
            if (ids == null)
            {
                return;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = ids.Count - 1; i >= 0; i--)
            {
                string value = ids[i] == null ? string.Empty : ids[i].Trim();
                if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
                {
                    ids.RemoveAt(i);
                    continue;
                }

                ids[i] = value;
            }
        }
    }

    [Serializable]
    public sealed class CareerFreeRoamData
    {
        public string safehouseId = "safehouse";
        public List<string> discoveredLocationIds = new List<string>();
        public List<string> completedEventIds = new List<string>();
        public CareerMissionResumeData missionResume;
    }

    [Serializable]
    public sealed class CareerMissionResumeData
    {
        public bool active;
        public string eventId = "", claimId = "";
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public float countdown;
    }

    [Serializable]
    public sealed class CareerWalletData
    {
        public int balance;
    }

    [Serializable]
    public sealed class CareerStoreData
    {
        public List<string> ownedProductIds = new List<string>();
        public List<string> ownedVehicleIds = new List<string>();
    }

    [Serializable]
    public sealed class CareerVehicleData
    {
        public int schemaVersion = 2;
        public string vehicleId = string.Empty;
        public string vehicleDefinitionId = string.Empty;
        public string vehicleVariantId = string.Empty;
        public List<string> performanceUpgradeIds = new List<string>();
        public List<string> customizationIds = new List<string>();
        public List<VehicleTuningAdjustment> tuningAdjustments = new List<VehicleTuningAdjustment>();
        public string paintId = string.Empty;
        public string paintFinishId = string.Empty;
        public string rimFinishId = string.Empty;
        public string glassTintId = string.Empty;

        public void Normalize()
        {
            vehicleId = vehicleId == null ? string.Empty : vehicleId.Trim();
            vehicleDefinitionId = vehicleDefinitionId == null ? string.Empty : vehicleDefinitionId.Trim();
            vehicleVariantId = vehicleVariantId == null ? string.Empty : vehicleVariantId.Trim();
            if (performanceUpgradeIds == null)
            {
                performanceUpgradeIds = new List<string>();
            }

            if (customizationIds == null)
            {
                customizationIds = new List<string>();
            }

            NormalizeIds(performanceUpgradeIds);
            NormalizeIds(customizationIds);
            if (tuningAdjustments == null) tuningAdjustments = new List<VehicleTuningAdjustment>();
            paintId = paintId == null ? string.Empty : paintId.Trim();
            paintFinishId = paintFinishId == null ? string.Empty : paintFinishId.Trim();
            rimFinishId = rimFinishId == null ? string.Empty : rimFinishId.Trim();
            glassTintId = glassTintId == null ? string.Empty : glassTintId.Trim();
        }

        private static void NormalizeIds(List<string> ids)
        {
            if (ids == null)
            {
                return;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = ids.Count - 1; i >= 0; i--)
            {
                string value = ids[i] == null ? string.Empty : ids[i].Trim();
                if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
                {
                    ids.RemoveAt(i);
                    continue;
                }

                ids[i] = value;
            }
        }
    }

    [Serializable]
    public sealed class CareerBountyData
    {
        public int totalBounty;
        public int currentPursuitBounty;
        public int heatLevel;
        public bool pursuitActive;
        public float pursuitDurationSeconds;
        public int awardedPursuitSeconds;
        public int policeVehiclesDisabled;
        public int roadblocksDodged;
        public int spikeStripsDodged;
        public int propertyDamageEvents;
        public int costToState;
        public int tradePaintEvents;
        public int trafficInfractions;
        public int pursuitsEscaped;
        public int pursuitsBusted;

        public void Normalize()
        {
            totalBounty = Mathf.Max(0, totalBounty);
            currentPursuitBounty = Mathf.Max(0, currentPursuitBounty);
            heatLevel = Mathf.Max(0, heatLevel);
            pursuitDurationSeconds = float.IsNaN(pursuitDurationSeconds)
                || float.IsInfinity(pursuitDurationSeconds)
                ? 0f
                : Mathf.Max(0f, pursuitDurationSeconds);
            awardedPursuitSeconds = Mathf.Max(0, awardedPursuitSeconds);
            policeVehiclesDisabled = Mathf.Max(0, policeVehiclesDisabled);
            roadblocksDodged = Mathf.Max(0, roadblocksDodged);
            spikeStripsDodged = Mathf.Max(0, spikeStripsDodged);
            propertyDamageEvents = Mathf.Max(0, propertyDamageEvents);
            costToState = Mathf.Max(0, costToState);
            tradePaintEvents = Mathf.Max(0, tradePaintEvents);
            trafficInfractions = Mathf.Max(0, trafficInfractions);
            pursuitsEscaped = Mathf.Max(0, pursuitsEscaped);
            pursuitsBusted = Mathf.Max(0, pursuitsBusted);
            if (!pursuitActive)
            {
                currentPursuitBounty = 0;
                pursuitDurationSeconds = 0f;
                awardedPursuitSeconds = 0;
                policeVehiclesDisabled = 0;
                roadblocksDodged = 0;
                spikeStripsDodged = 0;
                propertyDamageEvents = 0;
                costToState = 0;
                tradePaintEvents = 0;
                trafficInfractions = 0;
            }
        }
    }

    [Serializable]
    public sealed class CareerStatisticsData
    {
        public int racesWon;
        public int racesLost;
        public int eventsCompleted;
        public int totalCostToState;

        public void Normalize()
        {
            racesWon = Mathf.Max(0, racesWon);
            racesLost = Mathf.Max(0, racesLost);
            eventsCompleted = Mathf.Max(0, eventsCompleted);
            totalCostToState = Mathf.Max(0, totalCostToState);
        }
    }
}
