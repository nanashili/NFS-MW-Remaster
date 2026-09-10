using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum PoliceEncounterState { Patrol, Observed, TrafficStop, Pursuit, Cooldown, OutcomePending }
    public enum PoliceOutcomeKind { PaidFine, Escaped, Arrested }
    public enum PoliceOffence { TrafficInfraction, PropertyDamage, Collision, UnitDisabled, RoadblockEvaded, SpikesEvaded }

    /// <summary>Provisional design settings, NOT measured 2015 constants. Timers use scaled simulation seconds.</summary>
    [Serializable]
    public sealed class PolicePursuitRules
    {
        [Min(1)] public int paymentLimitExclusive = 500;
        [Min(1)] public int trafficFine = 150, propertyFine = 250, collisionFine = 300, disabledFine = 1000;
        [Min(1)] public int resistanceFine = 350, reacquisitionFine = 150;
        public int[] engagementStarts = { 0, 500, 1500, 5000, 11000 };
        [Min(0.01f)] public float observationSeconds = 0.5f, contactGraceSeconds = 0.7f;
        [Min(0.1f)] public float trafficStopSeconds = 8f;
        [Min(0f)] public float complianceSpeedKph = 8f;
        [Min(0)] public int escapeReputationPerLevel = 100;
        // Neutral until matched captures establish whether engine-off changes the timer.
        [Min(1f)] public float engineOffCooldownRate = 1f;
        public bool allowContactExtensions;

        public void Validate()
        {
            if (paymentLimitExclusive < 1 || trafficFine < 1 || propertyFine < 1 || collisionFine < 1 || disabledFine < 1
                || resistanceFine < 1 || reacquisitionFine < 1 || escapeReputationPerLevel < 0
                || engagementStarts == null || engagementStarts.Length != 5 || engagementStarts[0] != 0)
                throw new ArgumentException("Invalid police fine policy or five-level engagement curve.");
            for (int i = 1; i < engagementStarts.Length; i++)
                if (engagementStarts[i] <= engagementStarts[i - 1]) throw new ArgumentException("Engagement thresholds must increase.");
            foreach (float value in new[] { observationSeconds, contactGraceSeconds, trafficStopSeconds, engineOffCooldownRate })
                if (!Finite(value) || value <= 0f) throw new ArgumentException("Police durations/rates must be finite and positive.");
            if (!Finite(complianceSpeedKph) || complianceSpeedKph < 0f || engineOffCooldownRate < 1f)
                throw new ArgumentException("Invalid compliance/ignition policy.");
        }
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public int FineFor(PoliceOffence offence)
        {
            switch (offence)
            {
                case PoliceOffence.TrafficInfraction: return trafficFine;
                case PoliceOffence.PropertyDamage: return propertyFine;
                case PoliceOffence.Collision: return collisionFine;
                case PoliceOffence.UnitDisabled: return disabledFine;
                case PoliceOffence.RoadblockEvaded:
                case PoliceOffence.SpikesEvaded: return resistanceFine;
                default: throw new ArgumentOutOfRangeException(nameof(offence));
            }
        }
    }

    [Serializable]
    public sealed class PoliceOutcome
    {
        public string encounterId, targetId;
        public PoliceOutcomeKind kind;
        public int assessedFine, engagementLevel, reputation, historicalBounty;
        public PoliceOutcome Copy() => (PoliceOutcome)MemberwiseClone();
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(encounterId) || encounterId.Length > 128 || string.IsNullOrWhiteSpace(targetId)
                || !Enum.IsDefined(typeof(PoliceOutcomeKind), kind) || assessedFine < 0 || engagementLevel < 1
                || engagementLevel > 5 || reputation < 0 || historicalBounty < 0
                || kind != PoliceOutcomeKind.Escaped && (reputation != 0 || historicalBounty != 0))
                throw new ArgumentException("Invalid police outcome.");
        }
    }

    public interface IPoliceOutcomeSettlement
    {
        // False means not acknowledged. Retry the SAME frozen outcome, never invent a new transaction id.
        bool TrySettlePolice(PoliceOutcome outcome, out string failure);
    }

    public struct PoliceObservation
    {
        public bool targetAvailable, visible, engineOff;
        public Vector3 position, velocity;
        public int containingUnits;
    }

    /// <summary>Single pursuit authority. Only observed positions enter knowledge; wallet/bounty never control this model.</summary>
    public sealed class PolicePursuitModel
    {
        private readonly PolicePursuitRules rules;
        private string encounterId, targetId;
        private PoliceOutcome outcome;
        private bool resisted;
        private float bustTime, cooldownTime;
        public PoliceEncounterState State { get; private set; }
        public int Fine { get; private set; }
        public int EngagementLevel
        {
            get { int level = 1; for (int i = 1; i < rules.engagementStarts.Length; i++) if (Fine >= rules.engagementStarts[i]) level++; return level; }
        }
        public bool Active => State != PoliceEncounterState.Patrol;
        public string EncounterId => Active ? encounterId : null;
        public bool CanOfferPayment => State == PoliceEncounterState.TrafficStop && !resisted && Fine < rules.paymentLimitExclusive;
        public float TimeInState { get; private set; }
        public float TimeUnseen { get; private set; }
        public Vector3 LastKnownPosition { get; private set; }
        public Vector3 LastKnownVelocity { get; private set; }
        public float BustProgress { get; private set; }
        public float CooldownProgress { get; private set; }
        public PoliceOutcome PendingOutcome => outcome?.Copy();

        public PolicePursuitModel(PolicePursuitRules policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            policy.Validate();
            rules = JsonUtility.FromJson<PolicePursuitRules>(JsonUtility.ToJson(policy));
        }

        public bool ObserveOffence(PoliceOffence offence, string id, string target, Vector3 observedPosition, Vector3 observedVelocity)
        {
            if (State == PoliceEncounterState.OutcomePending || !Finite(observedPosition) || !Finite(observedVelocity)) return false;
            int addition = rules.FineFor(offence);
            if (!Active)
            {
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(target)) return false;
                encounterId = id; targetId = target; Fine = 0; resisted = false;
                bustTime = cooldownTime = BustProgress = CooldownProgress = TimeUnseen = 0f;
                Change(PoliceEncounterState.Observed);
            }
            Fine = SaturatingAdd(Fine, addition);
            Remember(observedPosition, observedVelocity);
            if (State == PoliceEncounterState.Cooldown)
            {
                Fine = SaturatingAdd(Fine, rules.reacquisitionFine);
                cooldownTime = CooldownProgress = 0f; Change(PoliceEncounterState.Pursuit);
            }
            if (State == PoliceEncounterState.TrafficStop && !CanOfferPayment) Resist();
            return true;
        }

        public bool BeginScripted(string id, string target, Vector3 position, Vector3 velocity)
        {
            if (Active || !ObserveOffence(PoliceOffence.TrafficInfraction, id, target, position, velocity)) return false;
            Resist(); return true;
        }

        public void Step(float dt, PoliceObservation observation, VehiclePoliceResponseTier tier)
        {
            if (!Active || State == PoliceEncounterState.OutcomePending || tier == null
                || !PolicePursuitRules.Finite(dt) || dt <= 0f) return;
            // A missing streamed target pauses the encounter, not an escape or arrest.
            if (!observation.targetAvailable || !Finite(observation.position) || !Finite(observation.velocity)) return;
            TimeInState += dt;
            if (observation.visible) Remember(observation.position, observation.velocity);
            else TimeUnseen += dt;
            float speedKph = observation.velocity.magnitude * 3.6f;
            if (State == PoliceEncounterState.Observed && TimeInState >= rules.observationSeconds)
            {
                if (Fine < rules.paymentLimitExclusive) Change(PoliceEncounterState.TrafficStop);
                else Resist();
            }
            else if (State == PoliceEncounterState.TrafficStop)
            {
                if (TimeInState >= rules.trafficStopSeconds || TimeInState >= 2f && speedKph > rules.complianceSpeedKph)
                    Resist();
            }
            else if (State == PoliceEncounterState.Pursuit && !observation.visible && TimeUnseen >= rules.contactGraceSeconds)
            {
                cooldownTime = CooldownProgress = 0f; Change(PoliceEncounterState.Cooldown);
            }
            else if (State == PoliceEncounterState.Cooldown)
            {
                if (observation.visible)
                {
                    Fine = SaturatingAdd(Fine, rules.reacquisitionFine);
                    cooldownTime = CooldownProgress = 0f; Change(PoliceEncounterState.Pursuit);
                }
                else
                {
                    cooldownTime += dt * (observation.engineOff && speedKph < 1f ? rules.engineOffCooldownRate : 1f);
                    float duration = tier.SearchDuration + tier.CooldownDuration;
                    CooldownProgress = Mathf.Clamp01(cooldownTime / duration);
                    if (cooldownTime >= duration) Freeze(PoliceOutcomeKind.Escaped);
                }
            }
            if (State == PoliceEncounterState.Pursuit && observation.visible && observation.containingUnits >= tier.BustingUnitCount
                && speedKph <= tier.BustSpeedKph)
            {
                bustTime += dt; BustProgress = Mathf.Clamp01(bustTime / tier.BustHoldDuration);
                if (bustTime >= tier.BustHoldDuration) Freeze(PoliceOutcomeKind.Arrested);
            }
            else { bustTime = 0f; BustProgress = 0f; }
        }

        public bool PayFine(bool stoppedAndVisible)
        {
            if (!CanOfferPayment || !stoppedAndVisible) return false;
            Freeze(PoliceOutcomeKind.PaidFine); return true;
        }
        public bool RequestOutcome(PoliceOutcomeKind kind)
        {
            if (!Active || State == PoliceEncounterState.OutcomePending || kind == PoliceOutcomeKind.PaidFine
                || !Enum.IsDefined(typeof(PoliceOutcomeKind), kind)) return false;
            Freeze(kind); return true;
        }
        public bool Acknowledge(string id)
        {
            if (outcome == null || outcome.encounterId != id) return false;
            outcome = null; Fine = 0; resisted = false; bustTime = cooldownTime = TimeUnseen = BustProgress = CooldownProgress = 0f;
            Change(PoliceEncounterState.Patrol); return true;
        }
        public bool PrimeEngagement(int level)
        {
            if (level < 1 || level > 5 || State == PoliceEncounterState.OutcomePending) return false;
            Fine = rules.engagementStarts[level - 1]; return true;
        }
        private void Resist()
        {
            if (!resisted) Fine = SaturatingAdd(Fine, rules.resistanceFine);
            resisted = true; Change(PoliceEncounterState.Pursuit);
        }
        private void Freeze(PoliceOutcomeKind kind)
        {
            outcome = new PoliceOutcome { encounterId = encounterId, targetId = targetId, kind = kind, assessedFine = Fine,
                engagementLevel = EngagementLevel, reputation = kind == PoliceOutcomeKind.Escaped
                    ? (int)Math.Min(int.MaxValue, (long)rules.escapeReputationPerLevel * EngagementLevel) : 0 };
            Change(PoliceEncounterState.OutcomePending);
        }
        private void Remember(Vector3 position, Vector3 velocity) { LastKnownPosition = position; LastKnownVelocity = velocity; TimeUnseen = 0f; }
        private void Change(PoliceEncounterState next) { State = next; TimeInState = 0f; }
        private static int SaturatingAdd(int left, int right) => (int)Math.Min(int.MaxValue, (long)left + right);
        private static bool Finite(Vector3 value) => PolicePursuitRules.Finite(value.x) && PolicePursuitRules.Finite(value.y) && PolicePursuitRules.Finite(value.z);
    }
}
