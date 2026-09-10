using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Pursuit actions are facts emitted by police, traffic, and world
    /// systems. Bounty rules decide their reward and heat impact separately.
    /// </summary>
    public enum VehicleBountyEventKind
    {
        PursuitTime,
        PoliceVehicleDisabled,
        RoadblockDodged,
        SpikeStripDodged,
        PropertyDamage,
        CostToState,
        TradePaint,
        TrafficInfraction
    }

    public interface IVehicleBountyRules
    {
        int MaxHeatLevel { get; }

        int BountyPerSecond { get; }

        int BustHeatDelta { get; }

        int GetBounty(VehicleBountyEventKind eventKind);

        int GetHeatDelta(VehicleBountyEventKind eventKind);
    }

    /// <summary>
    /// Pursuit-facing bounty contract. Police AI, milestone tracking, HUDs,
    /// and replay systems can submit the same facts without depending on the
    /// Unity component or the rules asset.
    /// </summary>
    public interface IVehicleBountyTracker
    {
        VehicleBountySnapshot Snapshot { get; }

        int MaxHeatLevel { get; }

        int TotalBounty { get; }

        int CurrentPursuitBounty { get; }

        int HeatLevel { get; }

        int PursuitsEscaped { get; }

        int PursuitsBusted { get; }

        bool PursuitActive { get; }

        bool TryStartPursuit(out string failure);

        bool TryRecordEvent(
            VehicleBountyEventKind eventKind,
            int count,
            out VehicleBountyAward award,
            out string failure);

        bool TryAdvancePursuitTime(
            float seconds,
            out VehicleBountyAward award,
            out string failure);

        bool TryEscape(
            out VehicleBountyPursuitResult result,
            out string failure);

        bool TryBust(
            out VehicleBountyPursuitResult result,
            out string failure);
    }

    [Serializable]
    public sealed class VehicleBountyEventRule
    {
        public VehicleBountyEventKind eventKind;
        [Min(0)] public int bounty;
        [Range(-5, 5)] public int heatDelta;

        public VehicleBountyEventRule()
        {
        }

        public VehicleBountyEventRule(
            VehicleBountyEventKind configuredEventKind,
            int configuredBounty,
            int configuredHeatDelta)
        {
            eventKind = configuredEventKind;
            bounty = Mathf.Max(0, configuredBounty);
            heatDelta = configuredHeatDelta;
        }
    }

    /// <summary>
    /// Asset-backed ruleset. Designers can tune the MW-style pursuit economy
    /// without changing the bounty state machine or its callers.
    /// </summary>
    [CreateAssetMenu(
        menuName = "NFS MW Remaster/Driving/Bounty Rules",
        fileName = "VehicleBountyRules")]
    public sealed class VehicleBountyRules : ScriptableObject, IVehicleBountyRules
    {
        [SerializeField, Min(1)] private int maxHeatLevel = 5;
        [SerializeField, Min(0)] private int bountyPerSecond = 25;
        [SerializeField, Range(-5, 0)] private int bustHeatDelta = -1;
        [SerializeField] private VehicleBountyEventRule[] eventRules =
        {
            new VehicleBountyEventRule(
                VehicleBountyEventKind.PoliceVehicleDisabled,
                500,
                0),
            new VehicleBountyEventRule(
                VehicleBountyEventKind.RoadblockDodged,
                250,
                0),
            new VehicleBountyEventRule(
                VehicleBountyEventKind.SpikeStripDodged,
                500,
                0),
            new VehicleBountyEventRule(
                VehicleBountyEventKind.PropertyDamage,
                100,
                0),
            new VehicleBountyEventRule(
                VehicleBountyEventKind.CostToState,
                200,
                0),
            new VehicleBountyEventRule(
                VehicleBountyEventKind.TradePaint,
                300,
                0),
            new VehicleBountyEventRule(
                VehicleBountyEventKind.TrafficInfraction,
                50,
                1)
        };

        public int MaxHeatLevel
        {
            get { return Mathf.Max(1, maxHeatLevel); }
        }

        public int BountyPerSecond
        {
            get { return Mathf.Max(0, bountyPerSecond); }
        }

        public int BustHeatDelta
        {
            get { return Mathf.Min(0, bustHeatDelta); }
        }

        public int GetBounty(VehicleBountyEventKind eventKind)
        {
            if (eventKind == VehicleBountyEventKind.PursuitTime)
            {
                return BountyPerSecond;
            }

            if (eventRules == null)
            {
                return 0;
            }

            for (int i = 0; i < eventRules.Length; i++)
            {
                VehicleBountyEventRule rule = eventRules[i];
                if (rule != null && rule.eventKind == eventKind)
                {
                    return Mathf.Max(0, rule.bounty);
                }
            }

            return 0;
        }

        public int GetHeatDelta(VehicleBountyEventKind eventKind)
        {
            if (eventKind == VehicleBountyEventKind.PursuitTime
                || eventRules == null)
            {
                return 0;
            }

            for (int i = 0; i < eventRules.Length; i++)
            {
                VehicleBountyEventRule rule = eventRules[i];
                if (rule != null && rule.eventKind == eventKind)
                {
                    return rule.heatDelta;
                }
            }

            return 0;
        }

        public void Configure(
            int configuredMaxHeatLevel,
            int configuredBountyPerSecond,
            int configuredBustHeatDelta,
            VehicleBountyEventRule[] configuredEventRules)
        {
            maxHeatLevel = Mathf.Max(1, configuredMaxHeatLevel);
            bountyPerSecond = Mathf.Max(0, configuredBountyPerSecond);
            bustHeatDelta = Mathf.Min(0, configuredBustHeatDelta);
            eventRules = configuredEventRules ?? Array.Empty<VehicleBountyEventRule>();
        }
    }

    /// <summary>
    /// Safe fallback for tests and scenes that have not assigned a rules asset.
    /// </summary>
    public sealed class VehicleBountyDefaultRules : IVehicleBountyRules
    {
        public int MaxHeatLevel
        {
            get { return 5; }
        }

        public int BountyPerSecond
        {
            get { return 25; }
        }

        public int BustHeatDelta
        {
            get { return -1; }
        }

        public int GetBounty(VehicleBountyEventKind eventKind)
        {
            switch (eventKind)
            {
                case VehicleBountyEventKind.PoliceVehicleDisabled:
                    return 500;
                case VehicleBountyEventKind.RoadblockDodged:
                    return 250;
                case VehicleBountyEventKind.SpikeStripDodged:
                    return 500;
                case VehicleBountyEventKind.PropertyDamage:
                    return 100;
                case VehicleBountyEventKind.CostToState:
                    return 200;
                case VehicleBountyEventKind.TradePaint:
                    return 300;
                case VehicleBountyEventKind.TrafficInfraction:
                    return 50;
                case VehicleBountyEventKind.PursuitTime:
                    return BountyPerSecond;
                default:
                    return 0;
            }
        }

        public int GetHeatDelta(VehicleBountyEventKind eventKind)
        {
            return eventKind == VehicleBountyEventKind.TrafficInfraction ? 1 : 0;
        }
    }
}
