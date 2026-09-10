using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Unity adapter for fine-driven pursuit, observed knowledge and acknowledged outcomes.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RoadPolicePerception))]
    [DefaultExecutionOrder(-120)]
    public sealed class VehiclePursuitDirector : MonoBehaviour, IVehiclePursuitDirector
    {
        [SerializeField] private VehiclePoliceResponseProfile responseProfile;
        [SerializeField] private MonoBehaviour targetComponent, bountyComponent, perceptionComponent, settlementComponent;
        [SerializeField] private VehiclePoliceUnit[] configuredUnits = Array.Empty<VehiclePoliceUnit>();
        [SerializeField] private PolicePursuitRules rules = new PolicePursuitRules();
        private readonly List<IVehiclePoliceUnit> units = new List<IVehiclePoliceUnit>(16);
        private readonly List<IVehiclePoliceUnit> visibleUnits = new List<IVehiclePoliceUnit>(16);
        private readonly VehiclePoliceResponseDefaultProfile fallback = new VehiclePoliceResponseDefaultProfile();
        private PolicePursuitModel model;
        private PoliceCareerAdapter career;
        private IVehiclePursuitTarget target;
        private IPoliceOutcomeSettlement settlementOverride;
        private PoliceOutcome frozenOutcome;
        private float contactCooldown, retryAt;
        private string status = "Patrolling. Lawful driving is ignored.";
        public event Action StateChanged;
        public event Action<PoliceOutcome> OutcomeAcknowledged;
        private PolicePursuitModel Model => model ?? (model = new PolicePursuitModel(rules));
        public PoliceEncounterState EncounterState => Model.State;
        public string EncounterId => Model.EncounterId;
        public int CurrentFine => Model.Fine;
        public bool CanPayFine => Model.CanOfferPayment;
        public float BustProgress => Model.BustProgress;
        public float CooldownProgress => Model.CooldownProgress;
        public PoliceOutcome PendingOutcome => frozenOutcome?.Copy() ?? Model.PendingOutcome;
        public int HeatLevel => IsActive ? Model.EngagementLevel : 0;
        public int MaxHeatLevel => 5;
        public bool IsActive => Model.Active;
        public VehiclePursuitPhase Phase
        {
            get
            {
                switch (Model.State)
                {
                    case PoliceEncounterState.Observed: return VehiclePursuitPhase.Alert;
                    case PoliceEncounterState.TrafficStop: return VehiclePursuitPhase.TrafficStop;
                    case PoliceEncounterState.Pursuit: return VehiclePursuitPhase.Engaged;
                    case PoliceEncounterState.Cooldown: return VehiclePursuitPhase.Cooldown;
                    case PoliceEncounterState.OutcomePending: return VehiclePursuitPhase.OutcomePending;
                    default: return VehiclePursuitPhase.Dormant;
                }
            }
        }
        public int ActiveUnitCount
        {
            get { int count = 0; foreach (var unit in units) if (Operational(unit) && IsResponding(unit)) count++; return IsActive ? count : 0; }
        }
        public int RegisteredUnitCount => units.Count;
        public float TimeInPhase => Model.TimeInState;
        public float TimeSinceTargetSeen => Model.TimeUnseen;
        public Vector3 LastKnownPosition => Model.LastKnownPosition;
        public VehiclePoliceResponseTier CurrentTier => (responseProfile != null ? (IVehiclePoliceResponseProfile)responseProfile : fallback).GetTier(Model.EngagementLevel);
        public string Status => status;
        public IVehiclePursuitTarget Target => target ?? (target = targetComponent as IVehiclePursuitTarget);
        private IVehiclePursuitPerception Perception => perceptionComponent as IVehiclePursuitPerception;
        public void Configure(MonoBehaviour configuredTarget, MonoBehaviour configuredBounty, VehiclePoliceResponseProfile profile)
        {
            if (IsActive) throw new InvalidOperationException("Cannot rebind an active police encounter.");
            targetComponent = configuredTarget; bountyComponent = configuredBounty; responseProfile = profile;
            career = new PoliceCareerAdapter(configuredBounty as VehicleBountySystem);
            ResolvePorts();
        }
        public void ConfigureRules(PolicePursuitRules policy)
        {
            if (IsActive) throw new InvalidOperationException("Cannot retune a live encounter.");
            policy.Validate(); rules = JsonUtility.FromJson<PolicePursuitRules>(JsonUtility.ToJson(policy));
            model = new PolicePursuitModel(rules);
        }
        public void SetSettlement(IPoliceOutcomeSettlement adapter) { settlementOverride = adapter; }
        public void SetPerception(MonoBehaviour perception) { perceptionComponent = perception; }
        public void ConfigureUnits(VehiclePoliceUnit[] configured)
        {
            foreach (var unit in units) unit?.ReleaseFromPursuit();
            units.Clear(); configuredUnits = configured ?? Array.Empty<VehiclePoliceUnit>();
            foreach (var unit in configuredUnits) RegisterUnit(unit);
        }
        public void RegisterUnit(VehiclePoliceUnit unit) => RegisterUnit((IVehiclePoliceUnit)unit);
        public void RegisterUnit(IVehiclePoliceUnit unit)
        {
            if (unit == null || units.Contains(unit)) return;
            units.Add(unit); if (unit is VehiclePoliceUnit concrete) concrete.SetDirector(this);
        }
        public void UnregisterUnit(VehiclePoliceUnit unit) => UnregisterUnit((IVehiclePoliceUnit)unit);
        public void UnregisterUnit(IVehiclePoliceUnit unit) { units.Remove(unit); visibleUnits.Remove(unit); }
        public bool CanDisplayUnit(IVehiclePoliceUnit unit)
            => IsActive && Phase == VehiclePursuitPhase.Engaged && Operational(unit) && IsResponding(unit);
        public bool ReportObservedOffence(IVehiclePoliceUnit observer, PoliceOffence offence, out string failure)
        {
            ResolvePorts();
            if (!units.Contains(observer) || !Operational(observer) || Target == null || !Target.IsAvailable
                || Perception == null || !Perception.CanSee(observer, Target, CurrentTier.DetectionRadius))
                return Fail("No operational officer observed this offence.", out failure);
            if (!Enum.IsDefined(typeof(PoliceOffence), offence)) return Fail("Unknown offence.", out failure);
            bool newEncounter = !IsActive;
            if (!Model.ObserveOffence(offence, Guid.NewGuid().ToString("N"), Target.TargetId, Target.Position, Target.Velocity))
                return Fail("The encounter is awaiting settlement.", out failure);
            if (newEncounter) career?.Begin();
            career?.Record(ToBounty(offence), 1); career?.Sync(Model.EngagementLevel);
            status = "Observed " + offence + ". Fine $" + CurrentFine + ".";
            RefreshVisibility(); PushCommands(); Publish(); failure = ""; return true;
        }

        // Scripted mission/test entry, intentionally distinct from observer-gated world offences.
        public bool TryStartPursuit(out string failure)
        {
            ResolvePorts();
            if (Target == null || !Target.IsAvailable) return Fail("No available pursuit target.", out failure);
            if (!Model.BeginScripted(Guid.NewGuid().ToString("N"), Target.TargetId, Target.Position, Target.Velocity))
                return Fail("A police encounter is already active.", out failure);
            career?.Begin(); career?.Sync(Model.EngagementLevel); RefreshVisibility(); PushCommands();
            status = "Scripted pursuit started."; Publish(); failure = ""; return true;
        }
        public bool TrySetHeatLevel(int level, out string failure)
        {
            if (!IsActive || !Model.PrimeEngagement(level)) return Fail("Engagement override requires an active unsettled encounter and level 1–5.", out failure);
            career?.Sync(Model.EngagementLevel); PushCommands(); Publish(); failure = ""; return true;
        }
        public bool TryPayFine(out string failure)
        {
            RefreshVisibility();
            var wallet = targetComponent != null ? targetComponent.GetComponent<VehicleStoreWallet>() : null;
            if (wallet == null || wallet.Balance < CurrentFine) return Fail("Insufficient funds to pay this fine.", out failure);
            if (!Model.PayFine(Target != null && Target.IsAvailable && visibleUnits.Count > 0
                && Target.Velocity.magnitude * 3.6f <= rules.complianceSpeedKph))
                return Fail("Stop within police sight while the payment offer is available.", out failure);
            return TryAcknowledgeOutcome(out failure);
        }
        public bool TryForceEscape(out string failure) => Force(PoliceOutcomeKind.Escaped, out failure);
        public bool TryForceBust(out string failure) => Force(PoliceOutcomeKind.Arrested, out failure);
        private bool Force(PoliceOutcomeKind kind, out string failure)
        {
            if (Model.State == PoliceEncounterState.OutcomePending)
            {
                if (Model.PendingOutcome.kind != kind) return Fail("A different outcome is pending.", out failure);
            }
            else if (!Model.RequestOutcome(kind)) return Fail("No active encounter.", out failure);
            return TryAcknowledgeOutcome(out failure);
        }
        public bool TryAcknowledgeOutcome(out string failure)
        {
            if (Model.State != PoliceEncounterState.OutcomePending) return Fail("No frozen police outcome.", out failure);
            if (frozenOutcome == null)
            {
                frozenOutcome = Model.PendingOutcome;
                frozenOutcome.historicalBounty = frozenOutcome.kind == PoliceOutcomeKind.Escaped ? career?.PendingBounty ?? 0 : 0;
            }
            var sink = settlementOverride ?? settlementComponent as IPoliceOutcomeSettlement;
            if (sink == null) { status = "Police outcome awaiting a profile settlement adapter."; return Fail(status, out failure); }
            try
            {
                if (!sink.TrySettlePolice(frozenOutcome.Copy(), out failure))
                { status = failure; Publish(); return false; }
            }
            catch (Exception exception)
            { status = "Police settlement adapter failed: " + exception.Message; Publish(); return Fail(status, out failure); }
            PoliceOutcome result = frozenOutcome.Copy();
            Model.Acknowledge(result.encounterId); frozenOutcome = null;
            foreach (var unit in units) unit?.ReleaseFromPursuit();
            status = result.kind == PoliceOutcomeKind.Escaped ? "Escaped. Fine cancelled; REP secured."
                : result.kind == PoliceOutcomeKind.PaidFine ? "Fine paid. Free to leave." : "Arrested. Fine settled; returning to garage.";
            Publish();
            if (OutcomeAcknowledged != null)
                foreach (Action<PoliceOutcome> listener in OutcomeAcknowledged.GetInvocationList())
                    try { listener(result.Copy()); } catch (Exception exception) { Debug.LogException(exception); }
            failure = ""; return true;
        }
        public bool TryRecordBountyEvent(VehicleBountyEventKind kind, int count, out string failure)
        {
            // Compatibility fact entry is observer-gated too; bounty can no longer manufacture heat.
            if (count != 1 || !TryOffence(kind, out var offence)) return Fail("Report one concrete observed offence.", out failure);
            foreach (var unit in units)
                if (Operational(unit) && Perception != null && Perception.CanSee(unit, Target, CurrentTier.DetectionRadius))
                    return ReportObservedOffence(unit, offence, out failure);
            return Fail("No observer can verify this fact.", out failure);
        }
        public void ReportTradePaint(VehiclePoliceUnit unit)
        {
            if (contactCooldown > 0f) return;
            if (ReportObservedOffence(unit, PoliceOffence.Collision, out _)) contactCooldown = 1f;
        }
        public void ReportUnitDisabled(VehiclePoliceUnit unit)
        {
            // Another operational witness is required; disabled actors cannot remotely report facts.
            TryRecordBountyEvent(VehicleBountyEventKind.PoliceVehicleDisabled, 1, out _);
        }
        public bool TryDisableLeadUnit(out string failure)
        {
            foreach (var unit in units) if (Operational(unit)) { unit.ApplyDamage(1000f); failure = ""; return true; }
            return Fail("No operational unit.", out failure);
        }
        private void FixedUpdate() => Tick(Time.fixedDeltaTime);
        public void Tick(float dt)
        {
            if (!PolicePursuitRules.Finite(dt) || dt <= 0f) return;
            contactCooldown = Mathf.Max(0f, contactCooldown - dt);
            if (!IsActive) return;
            if (targetComponent != null && targetComponent.GetComponent<FreeRoamSession>() is FreeRoamSession session
                && session.State == FreeRoamState.Paused) return;
            ResolvePorts(); RefreshVisibility();
            var before = Model.State; int fineBefore = Model.Fine;
            int containing = 0;
            if (Target != null)
                foreach (var unit in visibleUnits)
                    if (IsResponding(unit) && ContainmentDistance(unit, Target) <= CurrentTier.BustRadius) containing++;
            bool available = Target != null && Target.IsAvailable;
            Model.Step(dt, new PoliceObservation { targetAvailable = available, visible = visibleUnits.Count > 0,
                position = available ? Target.Position : Vector3.zero, velocity = available ? Target.Velocity : Vector3.zero,
                containingUnits = containing, engineOff = Target is IPoliceIgnitionState ignition && !ignition.EngineRunning }, CurrentTier);
            if (available && Model.State != PoliceEncounterState.OutcomePending) career?.Advance(dt);
            career?.Sync(Model.EngagementLevel); PushCommands();
            if (before != Model.State || fineBefore != Model.Fine) { status = "Police: " + Model.State + "."; Publish(); }
            if (Model.State == PoliceEncounterState.OutcomePending)
            {
                retryAt -= dt;
                if (retryAt <= 0f) { retryAt = 1f; TryAcknowledgeOutcome(out _); }
            }
        }
        private void RefreshVisibility()
        {
            visibleUnits.Clear();
            if (Target == null || !Target.IsAvailable || Perception == null) return;
            foreach (var unit in units)
                if (Operational(unit) && Perception.CanSee(unit, Target, CurrentTier.DetectionRadius)) visibleUnits.Add(unit);
        }
        private void PushCommands()
        {
            int slot = 0;
            foreach (var unit in units)
            {
                if (!Operational(unit)) continue;
                if (!IsActive || slot >= CurrentTier.MaxUnits || Phase == VehiclePursuitPhase.OutcomePending)
                { unit.ReleaseFromPursuit(); continue; }
                bool seen = visibleUnits.Contains(unit);
                // Even during contact grace, hidden targets never contribute live position/velocity.
                Vector3 known = Model.LastKnownPosition, velocity = Model.LastKnownVelocity;
                var decision = VehiclePursuitDecisionModel.Decide(new VehiclePursuitDecisionInput { Role = unit.Role,
                    Phase = Phase, TargetVisible = seen, DistanceToTarget = Vector3.Distance(unit.Position, known),
                    TargetSpeed = velocity.magnitude, Aggression = CurrentTier.Aggression, Slot = slot,
                    AllowContactExtensions = rules.allowContactExtensions });
                Vector3 aim = known;
                if (seen && Phase == VehiclePursuitPhase.Engaged)
                    aim += Vector3.ClampMagnitude(velocity * CurrentTier.InterceptLookAhead, 35f);
                var command = new VehiclePoliceUnitCommand(Phase, decision.Tactic, aim, decision.DesiredSpeedMultiplier,
                    CurrentTier.Aggression, 0f, 0f, slot) { TargetVisible = seen };
                unit.SetPursuitCommand(command, Target, CurrentTier); slot++;
            }
        }
        private void ResolvePorts()
        {
            target = targetComponent as IVehiclePursuitTarget;
            if (career == null) career = new PoliceCareerAdapter(bountyComponent as VehicleBountySystem);
            if (perceptionComponent == null) perceptionComponent = GetComponent<RoadPolicePerception>();
            if (settlementComponent == null && targetComponent != null) settlementComponent = targetComponent.GetComponent<CareerProfileSystem>();
        }
        private static bool Operational(IVehiclePoliceUnit unit) => unit != null && !(unit is UnityEngine.Object obj && obj == null) && !unit.IsDisabled;
        private static float ContainmentDistance(IVehiclePoliceUnit unit, IVehiclePursuitTarget suspect)
        {
            var policeCollider = (unit as Component)?.GetComponent<Collider>();
            var targetCollider = suspect.Body != null ? suspect.Body.GetComponent<Collider>() : null;
            if (policeCollider == null || targetCollider == null) return Vector3.Distance(unit.Position, suspect.Position);
            // Body-edge clearance, not overlapping vehicle centres. Physical obstacle braking still permits containment.
            Vector3 targetEdge = targetCollider.ClosestPoint(unit.Position);
            return Vector3.Distance(policeCollider.ClosestPoint(targetEdge), targetEdge);
        }
        private static bool IsResponding(IVehiclePoliceUnit unit) => unit.State != VehiclePoliceUnitState.Released && unit.State != VehiclePoliceUnitState.Dormant;
        private void Publish()
        {
            if (StateChanged == null) return;
            foreach (Action listener in StateChanged.GetInvocationList())
                try { listener(); } catch (Exception exception) { Debug.LogException(exception); }
        }
        private static bool Fail(string message, out string failure) { failure = message; return false; }
        private void Awake() { ResolvePorts(); foreach (var unit in configuredUnits) RegisterUnit(unit); }
        private static VehicleBountyEventKind ToBounty(PoliceOffence offence)
        {
            switch (offence)
            {
                case PoliceOffence.TrafficInfraction: return VehicleBountyEventKind.TrafficInfraction;
                case PoliceOffence.PropertyDamage: return VehicleBountyEventKind.PropertyDamage;
                case PoliceOffence.Collision: return VehicleBountyEventKind.TradePaint;
                case PoliceOffence.UnitDisabled: return VehicleBountyEventKind.PoliceVehicleDisabled;
                case PoliceOffence.RoadblockEvaded: return VehicleBountyEventKind.RoadblockDodged;
                default: return VehicleBountyEventKind.SpikeStripDodged;
            }
        }
        private static bool TryOffence(VehicleBountyEventKind kind, out PoliceOffence offence)
        {
            switch (kind)
            {
                case VehicleBountyEventKind.TrafficInfraction: offence = PoliceOffence.TrafficInfraction; return true;
                case VehicleBountyEventKind.PropertyDamage: offence = PoliceOffence.PropertyDamage; return true;
                case VehicleBountyEventKind.TradePaint: offence = PoliceOffence.Collision; return true;
                case VehicleBountyEventKind.PoliceVehicleDisabled: offence = PoliceOffence.UnitDisabled; return true;
                case VehicleBountyEventKind.RoadblockDodged: offence = PoliceOffence.RoadblockEvaded; return true;
                case VehicleBountyEventKind.SpikeStripDodged: offence = PoliceOffence.SpikesEvaded; return true;
                default: offence = default; return false;
            }
        }
    }
}
