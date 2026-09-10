using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public interface IVehiclePoliceResponseProfile
    {
        int MaxHeatLevel { get; }

        VehiclePoliceResponseTier GetTier(int heatLevel);
    }

    [Serializable]
    public sealed class VehiclePoliceResponseTier
    {
        [SerializeField, Min(1)] private int heatLevel = 1;
        [SerializeField, Min(1)] private int maxUnits = 2;
        [SerializeField, Min(1)] private int minimumPursuitUnits = 1;
        [SerializeField, Min(1)] private int bustingUnitCount = 1;
        [SerializeField, Min(1f)] private float detectionRadius = 70f;
        [SerializeField, Min(1f)] private float disengageRadius = 150f;
        [SerializeField, Min(0.1f)] private float searchDuration = 8f;
        [SerializeField, Min(0.1f)] private float cooldownDuration = 4f;
        [SerializeField, Min(0f)] private float interceptLookAhead = 0.7f;
        [SerializeField, Range(0f, 1f)] private float aggression = 0.45f;
        [SerializeField, Min(0f)] private float ramForce = 8f;
        [SerializeField, Min(0f)] private float boxStrength = 2f;
        [SerializeField, Min(0.1f)] private float topSpeedMultiplier = 1f;
        [SerializeField, Min(0.1f)] private float accelerationMultiplier = 1f;
        [SerializeField, Min(0.1f)] private float bustRadius = 3.8f;
        [SerializeField, Min(0.1f)] private float bustHoldDuration = 1.2f;
        [SerializeField, Min(0.1f)] private float bustSpeedKph = 12f;

        public int HeatLevel
        {
            get { return Mathf.Max(1, heatLevel); }
        }

        public int MaxUnits
        {
            get { return Mathf.Max(1, maxUnits); }
        }

        public int MinimumPursuitUnits
        {
            get { return Mathf.Max(1, minimumPursuitUnits); }
        }

        public int BustingUnitCount
        {
            get { return Mathf.Max(1, bustingUnitCount); }
        }

        public float DetectionRadius
        {
            get { return Mathf.Max(1f, detectionRadius); }
        }

        public float DisengageRadius
        {
            get { return Mathf.Max(1f, disengageRadius); }
        }

        public float SearchDuration
        {
            get { return Mathf.Max(0.1f, searchDuration); }
        }

        public float CooldownDuration
        {
            get { return Mathf.Max(0.1f, cooldownDuration); }
        }

        public float InterceptLookAhead
        {
            get { return Mathf.Max(0f, interceptLookAhead); }
        }

        public float Aggression
        {
            get { return Mathf.Clamp01(aggression); }
        }

        public float RamForce
        {
            get { return Mathf.Max(0f, ramForce); }
        }

        public float BoxStrength
        {
            get { return Mathf.Max(0f, boxStrength); }
        }

        public float TopSpeedMultiplier
        {
            get { return Mathf.Max(0.1f, topSpeedMultiplier); }
        }

        public float AccelerationMultiplier
        {
            get { return Mathf.Max(0.1f, accelerationMultiplier); }
        }

        public float BustRadius
        {
            get { return Mathf.Max(0.1f, bustRadius); }
        }

        public float BustHoldDuration
        {
            get { return Mathf.Max(0.1f, bustHoldDuration); }
        }

        public float BustSpeedKph
        {
            get { return Mathf.Max(0.1f, bustSpeedKph); }
        }

        public void Configure(
            int configuredHeatLevel,
            int configuredMaxUnits,
            int configuredMinimumPursuitUnits,
            int configuredBustingUnitCount,
            float configuredDetectionRadius,
            float configuredDisengageRadius,
            float configuredSearchDuration,
            float configuredCooldownDuration,
            float configuredInterceptLookAhead,
            float configuredAggression,
            float configuredRamForce,
            float configuredBoxStrength,
            float configuredTopSpeedMultiplier,
            float configuredAccelerationMultiplier,
            float configuredBustRadius,
            float configuredBustHoldDuration,
            float configuredBustSpeedKph)
        {
            heatLevel = Mathf.Max(1, configuredHeatLevel);
            maxUnits = Mathf.Max(1, configuredMaxUnits);
            minimumPursuitUnits = Mathf.Max(1, configuredMinimumPursuitUnits);
            bustingUnitCount = Mathf.Max(1, configuredBustingUnitCount);
            detectionRadius = Mathf.Max(1f, configuredDetectionRadius);
            disengageRadius = Mathf.Max(1f, configuredDisengageRadius);
            searchDuration = Mathf.Max(0.1f, configuredSearchDuration);
            cooldownDuration = Mathf.Max(0.1f, configuredCooldownDuration);
            interceptLookAhead = Mathf.Max(0f, configuredInterceptLookAhead);
            aggression = Mathf.Clamp01(configuredAggression);
            ramForce = Mathf.Max(0f, configuredRamForce);
            boxStrength = Mathf.Max(0f, configuredBoxStrength);
            topSpeedMultiplier = Mathf.Max(0.1f, configuredTopSpeedMultiplier);
            accelerationMultiplier = Mathf.Max(0.1f, configuredAccelerationMultiplier);
            bustRadius = Mathf.Max(0.1f, configuredBustRadius);
            bustHoldDuration = Mathf.Max(0.1f, configuredBustHoldDuration);
            bustSpeedKph = Mathf.Max(0.1f, configuredBustSpeedKph);
        }
    }

    /// <summary>
    /// Asset-backed police response curve. Heat changes the unit cap, speed,
    /// aggression, contact tactics, and how long search/cooldown can last.
    /// </summary>
    [CreateAssetMenu(
        menuName = "NFS MW Remaster/Driving/Police Response Profile",
        fileName = "VehiclePoliceResponseProfile")]
    public sealed class VehiclePoliceResponseProfile :
        ScriptableObject,
        IVehiclePoliceResponseProfile
    {
        [SerializeField, Min(1)] private int maxHeatLevel = 5;
        [SerializeField] private VehiclePoliceResponseTier[] tiers =
            CreateDefaultTiers();

        public int MaxHeatLevel
        {
            get { return Mathf.Max(1, maxHeatLevel); }
        }

        public VehiclePoliceResponseTier GetTier(int heatLevel)
        {
            EnsureTiers();
            int requestedHeat = Mathf.Clamp(heatLevel, 1, MaxHeatLevel);
            VehiclePoliceResponseTier closest = tiers[0];
            int closestDistance = int.MaxValue;

            for (int i = 0; i < tiers.Length; i++)
            {
                VehiclePoliceResponseTier tier = tiers[i];
                if (tier == null)
                {
                    continue;
                }

                int distance = Mathf.Abs(tier.HeatLevel - requestedHeat);
                if (distance < closestDistance)
                {
                    closest = tier;
                    closestDistance = distance;
                }

                if (tier.HeatLevel == requestedHeat)
                {
                    return tier;
                }
            }

            return closest;
        }

        public void Configure(
            int configuredMaxHeatLevel,
            VehiclePoliceResponseTier[] configuredTiers)
        {
            maxHeatLevel = Mathf.Max(1, configuredMaxHeatLevel);
            tiers = configuredTiers ?? CreateDefaultTiers();
            EnsureTiers();
        }

        public void ConfigureDefault()
        {
            maxHeatLevel = 5;
            tiers = CreateDefaultTiers();
        }

        public static VehiclePoliceResponseTier[] CreateDefaultTiers()
        {
            return new[]
            {
                CreateTier(
                    1,
                    2,
                    1,
                    1,
                    70f,
                    150f,
                    8f,
                    4f,
                    0.7f,
                    0.45f,
                    8f,
                    2f,
                    1.00f,
                    1.00f,
                    3.8f,
                    1.2f,
                    12f),
                CreateTier(
                    2,
                    3,
                    2,
                    1,
                    80f,
                    165f,
                    10f,
                    5f,
                    1.0f,
                    0.60f,
                    10f,
                    3f,
                    1.05f,
                    1.08f,
                    3.9f,
                    1.15f,
                    13f),
                CreateTier(
                    3,
                    4,
                    3,
                    2,
                    95f,
                    185f,
                    12f,
                    6f,
                    1.3f,
                    0.72f,
                    13f,
                    4f,
                    1.10f,
                    1.16f,
                    4.0f,
                    1.1f,
                    14f),
                CreateTier(
                    4,
                    5,
                    4,
                    2,
                    110f,
                    210f,
                    15f,
                    8f,
                    1.6f,
                    0.84f,
                    16f,
                    5f,
                    1.15f,
                    1.24f,
                    4.1f,
                    1.05f,
                    15f),
                CreateTier(
                    5,
                    6,
                    5,
                    2,
                    125f,
                    240f,
                    20f,
                    10f,
                    2.0f,
                    0.94f,
                    20f,
                    6f,
                    1.22f,
                    1.32f,
                    4.2f,
                    1.0f,
                    16f)
            };
        }

        private static VehiclePoliceResponseTier CreateTier(
            int heat,
            int maxUnits,
            int minimumUnits,
            int bustingUnits,
            float detectionRadius,
            float disengageRadius,
            float searchDuration,
            float cooldownDuration,
            float interceptLookAhead,
            float aggression,
            float ramForce,
            float boxStrength,
            float topSpeedMultiplier,
            float accelerationMultiplier,
            float bustRadius,
            float bustHoldDuration,
            float bustSpeedKph)
        {
            VehiclePoliceResponseTier tier = new VehiclePoliceResponseTier();
            tier.Configure(
                heat,
                maxUnits,
                minimumUnits,
                bustingUnits,
                detectionRadius,
                disengageRadius,
                searchDuration,
                cooldownDuration,
                interceptLookAhead,
                aggression,
                ramForce,
                boxStrength,
                topSpeedMultiplier,
                accelerationMultiplier,
                bustRadius,
                bustHoldDuration,
                bustSpeedKph);
            return tier;
        }

        private void EnsureTiers()
        {
            if (tiers == null || tiers.Length == 0)
            {
                tiers = CreateDefaultTiers();
            }
        }
    }

    public sealed class VehiclePoliceResponseDefaultProfile :
        IVehiclePoliceResponseProfile
    {
        private readonly VehiclePoliceResponseTier[] tiers =
            VehiclePoliceResponseProfile.CreateDefaultTiers();

        public int MaxHeatLevel
        {
            get { return 5; }
        }

        public VehiclePoliceResponseTier GetTier(int heatLevel)
        {
            int requestedHeat = Mathf.Clamp(heatLevel, 1, MaxHeatLevel);
            return tiers[requestedHeat - 1];
        }
    }
}
