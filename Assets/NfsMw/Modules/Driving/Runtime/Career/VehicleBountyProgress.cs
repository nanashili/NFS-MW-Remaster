using System;

namespace NfsMwRemaster.Driving
{
    public sealed class VehicleBountyAward
    {
        public VehicleBountyAward(
            VehicleBountyEventKind eventKind,
            int count,
            int bounty,
            int heatDelta)
        {
            EventKind = eventKind;
            Count = count;
            Bounty = bounty;
            HeatDelta = heatDelta;
        }

        public VehicleBountyEventKind EventKind { get; }

        public int Count { get; }

        public int Bounty { get; }

        public int HeatDelta { get; }
    }

    public sealed class VehicleBountyPursuitResult
    {
        public VehicleBountyPursuitResult(
            bool escaped,
            int bountyEarned,
            int bountyLost,
            int careerBounty,
            int heatLevel,
            float durationSeconds)
        {
            Escaped = escaped;
            BountyEarned = bountyEarned;
            BountyLost = bountyLost;
            CareerBounty = careerBounty;
            HeatLevel = heatLevel;
            DurationSeconds = durationSeconds;
        }

        public bool Escaped { get; }

        public int BountyEarned { get; }

        public int BountyLost { get; }

        public int CareerBounty { get; }

        public int HeatLevel { get; }

        public float DurationSeconds { get; }
    }

    public sealed class VehicleBountySnapshot
    {
        public VehicleBountySnapshot(
            int totalBounty,
            int currentPursuitBounty,
            int heatLevel,
            bool pursuitActive,
            float pursuitDurationSeconds,
            int policeVehiclesDisabled,
            int roadblocksDodged,
            int spikeStripsDodged,
            int propertyDamageEvents,
            int costToState,
            int tradePaintEvents,
            int trafficInfractions,
            int pursuitsEscaped,
            int pursuitsBusted)
        {
            TotalBounty = totalBounty;
            CurrentPursuitBounty = currentPursuitBounty;
            HeatLevel = heatLevel;
            PursuitActive = pursuitActive;
            PursuitDurationSeconds = pursuitDurationSeconds;
            PoliceVehiclesDisabled = policeVehiclesDisabled;
            RoadblocksDodged = roadblocksDodged;
            SpikeStripsDodged = spikeStripsDodged;
            PropertyDamageEvents = propertyDamageEvents;
            CostToState = costToState;
            TradePaintEvents = tradePaintEvents;
            TrafficInfractions = trafficInfractions;
            PursuitsEscaped = pursuitsEscaped;
            PursuitsBusted = pursuitsBusted;
        }

        public int TotalBounty { get; }

        public int CurrentPursuitBounty { get; }

        public int HeatLevel { get; }

        public bool PursuitActive { get; }

        public float PursuitDurationSeconds { get; }

        public int PoliceVehiclesDisabled { get; }

        public int RoadblocksDodged { get; }

        public int SpikeStripsDodged { get; }

        public int PropertyDamageEvents { get; }

        public int CostToState { get; }

        public int TradePaintEvents { get; }

        public int TrafficInfractions { get; }

        public int PursuitsEscaped { get; }

        public int PursuitsBusted { get; }
    }

    /// <summary>
    /// Pure pursuit bounty state. A pursuit accumulates provisional bounty;
    /// only escape commits it to career bounty, while bust discards it.
    /// </summary>
    public sealed class VehicleBountyProgress
    {
        private readonly IVehicleBountyRules rules;
        private int totalBounty;
        private int currentPursuitBounty;
        private int heatLevel;
        private bool pursuitActive;
        private float pursuitDurationSeconds;
        private int awardedPursuitSeconds;
        private int policeVehiclesDisabled;
        private int roadblocksDodged;
        private int spikeStripsDodged;
        private int propertyDamageEvents;
        private int costToState;
        private int tradePaintEvents;
        private int trafficInfractions;
        private int pursuitsEscaped;
        private int pursuitsBusted;

        public VehicleBountyProgress(IVehicleBountyRules configuredRules = null)
        {
            rules = configuredRules ?? new VehicleBountyDefaultRules();
        }

        public IVehicleBountyRules Rules
        {
            get { return rules; }
        }

        public int TotalBounty
        {
            get { return totalBounty; }
        }

        public int CurrentPursuitBounty
        {
            get { return currentPursuitBounty; }
        }

        public int HeatLevel
        {
            get { return heatLevel; }
        }

        public bool PursuitActive
        {
            get { return pursuitActive; }
        }

        public float PursuitDurationSeconds
        {
            get { return pursuitDurationSeconds; }
        }

        public int PoliceVehiclesDisabled
        {
            get { return policeVehiclesDisabled; }
        }

        public int RoadblocksDodged
        {
            get { return roadblocksDodged; }
        }

        public int SpikeStripsDodged
        {
            get { return spikeStripsDodged; }
        }

        public int PropertyDamageEvents
        {
            get { return propertyDamageEvents; }
        }

        public int CostToState
        {
            get { return costToState; }
        }

        public int TradePaintEvents
        {
            get { return tradePaintEvents; }
        }

        public int TrafficInfractions
        {
            get { return trafficInfractions; }
        }

        public int PursuitsEscaped
        {
            get { return pursuitsEscaped; }
        }

        public int PursuitsBusted
        {
            get { return pursuitsBusted; }
        }

        public bool TryStartPursuit(out string failure)
        {
            if (pursuitActive)
            {
                failure = "A pursuit is already active.";
                return false;
            }

            pursuitActive = true;
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
            failure = string.Empty;
            return true;
        }

        public bool TryRecord(
            VehicleBountyEventKind eventKind,
            int count,
            out VehicleBountyAward award,
            out string failure)
        {
            award = null!;
            if (!pursuitActive)
            {
                failure = "A pursuit must be active before recording bounty.";
                return false;
            }

            if (eventKind == VehicleBountyEventKind.PursuitTime)
            {
                failure = "Use TryAdvanceTime for pursuit time.";
                return false;
            }

            if (count <= 0)
            {
                failure = "Bounty event count must be positive.";
                return false;
            }

            award = ApplyAward(eventKind, count);
            IncrementCounter(eventKind, count);
            failure = string.Empty;
            return true;
        }

        public bool TryAdvanceTime(
            float seconds,
            out VehicleBountyAward award,
            out string failure)
        {
            award = null!;
            if (!pursuitActive)
            {
                failure = "A pursuit must be active before advancing time.";
                return false;
            }

            if (float.IsNaN(seconds)
                || float.IsInfinity(seconds)
                || seconds <= 0f)
            {
                failure = "Pursuit time must be a finite positive value.";
                return false;
            }

            float previousDuration = pursuitDurationSeconds;
            pursuitDurationSeconds = float.MaxValue - previousDuration < seconds
                ? float.MaxValue
                : previousDuration + seconds;
            int wholeSeconds = (int)Math.Min(
                int.MaxValue,
                Math.Floor(pursuitDurationSeconds));
            int newSeconds = Math.Max(0, wholeSeconds - awardedPursuitSeconds);
            if (newSeconds > 0)
            {
                award = ApplyAward(
                    VehicleBountyEventKind.PursuitTime,
                    newSeconds);
                awardedPursuitSeconds = wholeSeconds;
            }
            else
            {
                award = new VehicleBountyAward(
                    VehicleBountyEventKind.PursuitTime,
                    0,
                    0,
                    0);
            }

            failure = string.Empty;
            return true;
        }

        public bool TryEscape(
            out VehicleBountyPursuitResult result,
            out string failure)
        {
            result = null!;
            if (!pursuitActive)
            {
                failure = "No active pursuit can be escaped.";
                return false;
            }

            int earned = currentPursuitBounty;
            totalBounty = SaturatingAdd(totalBounty, earned);
            pursuitsEscaped = SaturatingAdd(pursuitsEscaped, 1);
            result = new VehicleBountyPursuitResult(
                true,
                earned,
                0,
                totalBounty,
                heatLevel,
                pursuitDurationSeconds);
            ResetPursuit();
            failure = string.Empty;
            return true;
        }

        public bool TryBust(
            out VehicleBountyPursuitResult result,
            out string failure)
        {
            result = null!;
            if (!pursuitActive)
            {
                failure = "No active pursuit can be busted.";
                return false;
            }

            int lost = currentPursuitBounty;
            pursuitsBusted = SaturatingAdd(pursuitsBusted, 1);
            heatLevel = ClampHeat((long)heatLevel + rules.BustHeatDelta);
            result = new VehicleBountyPursuitResult(
                false,
                0,
                lost,
                totalBounty,
                heatLevel,
                pursuitDurationSeconds);
            ResetPursuit();
            failure = string.Empty;
            return true;
        }

        public void SetHeatLevel(int configuredHeatLevel)
        {
            heatLevel = ClampHeat(configuredHeatLevel);
        }

        public VehicleBountySnapshot CreateSnapshot()
        {
            return new VehicleBountySnapshot(
                totalBounty,
                currentPursuitBounty,
                heatLevel,
                pursuitActive,
                pursuitDurationSeconds,
                policeVehiclesDisabled,
                roadblocksDodged,
                spikeStripsDodged,
                propertyDamageEvents,
                costToState,
                tradePaintEvents,
                trafficInfractions,
                pursuitsEscaped,
                pursuitsBusted);
        }

        public void Capture(CareerBountyData data)
        {
            if (data == null)
            {
                return;
            }

            data.totalBounty = totalBounty;
            data.currentPursuitBounty = currentPursuitBounty;
            data.heatLevel = heatLevel;
            data.pursuitActive = pursuitActive;
            data.pursuitDurationSeconds = pursuitDurationSeconds;
            data.awardedPursuitSeconds = awardedPursuitSeconds;
            data.policeVehiclesDisabled = policeVehiclesDisabled;
            data.roadblocksDodged = roadblocksDodged;
            data.spikeStripsDodged = spikeStripsDodged;
            data.propertyDamageEvents = propertyDamageEvents;
            data.costToState = costToState;
            data.tradePaintEvents = tradePaintEvents;
            data.trafficInfractions = trafficInfractions;
            data.pursuitsEscaped = pursuitsEscaped;
            data.pursuitsBusted = pursuitsBusted;
        }

        public bool Restore(CareerBountyData data, out string failure)
        {
            if (data == null)
            {
                failure = "Bounty save data is null.";
                return false;
            }

            data.Normalize();
            if (data.heatLevel > rules.MaxHeatLevel)
            {
                failure = "Bounty heat level exceeds the configured maximum.";
                return false;
            }

            totalBounty = data.totalBounty;
            heatLevel = data.heatLevel;
            pursuitActive = data.pursuitActive;
            currentPursuitBounty = pursuitActive
                ? data.currentPursuitBounty
                : 0;
            pursuitDurationSeconds = pursuitActive
                ? data.pursuitDurationSeconds
                : 0f;
            int wholeSeconds = (int)Math.Min(
                int.MaxValue,
                Math.Floor(pursuitDurationSeconds));
            awardedPursuitSeconds = Math.Min(
                wholeSeconds,
                pursuitActive ? data.awardedPursuitSeconds : 0);
            policeVehiclesDisabled = pursuitActive
                ? data.policeVehiclesDisabled
                : 0;
            roadblocksDodged = pursuitActive ? data.roadblocksDodged : 0;
            spikeStripsDodged = pursuitActive ? data.spikeStripsDodged : 0;
            propertyDamageEvents = pursuitActive
                ? data.propertyDamageEvents
                : 0;
            costToState = pursuitActive ? data.costToState : 0;
            tradePaintEvents = pursuitActive ? data.tradePaintEvents : 0;
            trafficInfractions = pursuitActive ? data.trafficInfractions : 0;
            pursuitsEscaped = data.pursuitsEscaped;
            pursuitsBusted = data.pursuitsBusted;
            failure = string.Empty;
            return true;
        }

        private VehicleBountyAward ApplyAward(
            VehicleBountyEventKind eventKind,
            int count)
        {
            int bounty = SaturatingMultiply(
                Math.Max(0, rules.GetBounty(eventKind)),
                count);
            long requestedHeatDelta = (long)rules.GetHeatDelta(eventKind)
                * count;

            int previousHeat = heatLevel;
            currentPursuitBounty = SaturatingAdd(currentPursuitBounty, bounty);
            heatLevel = ClampHeat(heatLevel + requestedHeatDelta);
            return new VehicleBountyAward(
                eventKind,
                count,
                bounty,
                heatLevel - previousHeat);
        }

        private void IncrementCounter(VehicleBountyEventKind eventKind, int count)
        {
            switch (eventKind)
            {
                case VehicleBountyEventKind.PoliceVehicleDisabled:
                    policeVehiclesDisabled = SaturatingAdd(policeVehiclesDisabled, count);
                    break;
                case VehicleBountyEventKind.RoadblockDodged:
                    roadblocksDodged = SaturatingAdd(roadblocksDodged, count);
                    break;
                case VehicleBountyEventKind.SpikeStripDodged:
                    spikeStripsDodged = SaturatingAdd(spikeStripsDodged, count);
                    break;
                case VehicleBountyEventKind.PropertyDamage:
                    propertyDamageEvents = SaturatingAdd(propertyDamageEvents, count);
                    break;
                case VehicleBountyEventKind.CostToState:
                    costToState = SaturatingAdd(costToState, count);
                    break;
                case VehicleBountyEventKind.TradePaint:
                    tradePaintEvents = SaturatingAdd(tradePaintEvents, count);
                    break;
                case VehicleBountyEventKind.TrafficInfraction:
                    trafficInfractions = SaturatingAdd(trafficInfractions, count);
                    break;
            }
        }

        private int ClampHeat(long value)
        {
            int maximum = Math.Max(0, rules.MaxHeatLevel);
            if (value <= 0)
            {
                return 0;
            }

            return value >= maximum ? maximum : (int)value;
        }

        private void ResetPursuit()
        {
            pursuitActive = false;
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

        private static int SaturatingAdd(int left, int right)
        {
            if (right > 0 && left > int.MaxValue - right)
            {
                return int.MaxValue;
            }

            if (right < 0 && left < int.MinValue - right)
            {
                return int.MinValue;
            }

            return left + right;
        }

        private static int SaturatingMultiply(int value, int count)
        {
            if (value <= 0 || count <= 0)
            {
                return 0;
            }

            return value > int.MaxValue / count
                ? int.MaxValue
                : value * count;
        }
    }
}
