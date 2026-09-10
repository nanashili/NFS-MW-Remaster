using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Serializable]
    public sealed class TrafficDemandFlow
    {
        public int originLaneId, destinationLaneId;
        [Min(0)] public float vehiclesPerHour = 30;
    }
    [Serializable]
    public sealed class TrafficDistrictDemand
    {
        public int district;
        [Range(0, 3)] public float multiplier = 1;
        [Tooltip("Optional 24 hourly multipliers; empty means unchanged. Simulation hours, not OS time.")]
        public float[] hourlyMultipliers = Array.Empty<float>();
        [Tooltip("Optional Compact, Sedan, SUV, Pickup, Van, Taxi, Commercial weights.")]
        public float[] vehicleWeights = Array.Empty<float>();
    }

    [CreateAssetMenu(menuName = "NFS MW Remaster/Traffic/World Profile")]
    public sealed class TrafficWorldProfile : ScriptableObject
    {
        public string profileId = "NFS2015_TRAFFIC_FIDELITY";
        [TextArea] public string calibrationStatus = "Uncalibrated engineering baseline. Not measured NFS 2015 density.";
        public int worldSeed = 2015;
        [Range(1, 4096)] public int maximumLogicalAgents = 256;
        [Min(0)] public float defaultPortalDemandPerHour = 30;
        [Range(0, 3)] public float demandScale = 1;
        [Min(0.1f)] public float populationInterval = 0.5f;
        [Min(100)] public float physicalRadius = 180;
        [Min(20)] public float lodHysteresis = 60;
        [Min(2)] public float promotionLookAheadSeconds = 5;
        [Min(5)] public float disabledMinimumLifetime = 45;
        [Range(1, 256)] public int maximumPendingPerFlow = 32;
        [Min(1)] public float minimumPortalHeadway = 3;
        [Range(0, 24)] public float startingHour = 22;
        [Min(0)] public float gameHoursPerRealHour = 1;
        [Tooltip("Local, Arterial, Highway, Industrial, Canyon demand multipliers.")]
        public float[] roadClassDemand = { 0.45f, 1, 1.25f, 0.5f, 0.2f };
        public float[] vehicleWeights = { 20, 38, 12, 8, 8, 10, 4 };
        public TrafficDistrictDemand[] districts = Array.Empty<TrafficDistrictDemand>();
        public TrafficDriverPopulationProfile drivers;
        public TrafficDemandFlow[] demand = Array.Empty<TrafficDemandFlow>();

        public float DemandMultiplier(RoadLane origin, float simulationSeconds)
        {
            if (origin == null || !TrafficDriver.Finite(simulationSeconds) || simulationSeconds < 0
                || !TrafficDriver.Finite(startingHour) || !TrafficDriver.Finite(gameHoursPerRealHour) || gameHoursPerRealHour < 0) return 0;
            int roadClass = (int)origin.Class;
            float road = roadClassDemand != null && roadClass >= 0 && roadClass < roadClassDemand.Length ? roadClassDemand[roadClass] : 1;
            float result = SafeScale(demandScale) * SafeScale(road);
            var district = District(origin.District); if (district == null) return result;
            float hour = Mathf.Repeat(startingHour + simulationSeconds * gameHoursPerRealHour / 3600, 24);
            if (!TrafficDriver.Finite(hour)) return 0;
            float hourly = district.hourlyMultipliers?.Length == 24 ? district.hourlyMultipliers[Mathf.FloorToInt(hour)] : 1;
            return Mathf.Clamp(result * SafeScale(district.multiplier) * SafeScale(hourly), 0, 100);
        }
        public TrafficVehicleCategory VehicleCategory(int districtId, int seed)
        {
            var district = District(districtId);
            float[] weights = district?.vehicleWeights?.Length == 7 ? district.vehicleWeights : vehicleWeights;
            if (weights == null || weights.Length != 7) return TrafficVehicleCategory.Sedan;
            float total = 0; for (int i = 0; i < weights.Length; i++) total += SafeWeight(weights[i]);
            if (total <= 0) return TrafficVehicleCategory.Sedan;
            var generator = new TrafficRandom(seed); float draw = generator.Unit() * total;
            for (int i = 0; i < weights.Length; i++) { draw -= SafeWeight(weights[i]); if (draw < 0) return (TrafficVehicleCategory)i; }
            return TrafficVehicleCategory.Sedan;
        }
        private TrafficDistrictDemand District(int id)
        {
            if (districts != null) foreach (var row in districts) if (row != null && row.district == id) return row;
            return null;
        }
        private static float SafeScale(float value) => TrafficDriver.Finite(value) ? Mathf.Clamp(value, 0, 3) : 0;
        private static float SafeWeight(float value) => TrafficDriver.Finite(value) ? Mathf.Clamp(value, 0, 10000) : 0;
    }

}
