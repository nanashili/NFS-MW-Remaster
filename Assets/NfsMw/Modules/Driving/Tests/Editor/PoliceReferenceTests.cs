using System;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class PoliceReferenceTests
    {
        private static readonly VehiclePoliceResponseTier Tier = new VehiclePoliceResponseDefaultProfile().GetTier(1);
        private static PoliceObservation Seen(float speed = 0f, int containing = 0) => new PoliceObservation
        { targetAvailable = true, visible = true, velocity = Vector3.forward * speed, containingUnits = containing };
        private static PolicePursuitModel New() => new PolicePursuitModel(new PolicePursuitRules());
        private static void Observe(PolicePursuitModel model) => Assert.That(model.ObserveOffence(PoliceOffence.TrafficInfraction, "encounter", "player", Vector3.zero, Vector3.zero), Is.True);

        [Test] public void LawfulDrivingDoesNotStartAnEncounter()
        { var model = New(); model.Step(100, Seen(30), Tier); Assert.That(model.Active, Is.False); Assert.That(model.Fine, Is.Zero); }

        [Test] public void StopOffersExplicitPaymentAndWaitsForAcknowledgement()
        {
            var model = New(); Observe(model); model.Step(0.6f, Seen(), Tier);
            Assert.That(model.State, Is.EqualTo(PoliceEncounterState.TrafficStop)); Assert.That(model.CanOfferPayment, Is.True);
            Assert.That(model.PayFine(false), Is.False); Assert.That(model.PayFine(true), Is.True);
            model.Step(100, Seen(), Tier); Assert.That(model.State, Is.EqualTo(PoliceEncounterState.OutcomePending));
            Assert.That(model.Acknowledge("wrong"), Is.False); Assert.That(model.Acknowledge("encounter"), Is.True);
            Assert.That(model.Active, Is.False);
        }

        [TestCase(499, true)] [TestCase(500, false)] [TestCase(501, false)]
        public void PaymentBoundaryIsExplicitAndExclusive(int fine, bool offered)
        {
            var model = new PolicePursuitModel(new PolicePursuitRules { trafficFine = fine });
            Observe(model); model.Step(0.6f, Seen(), Tier); Assert.That(model.CanOfferPayment, Is.EqualTo(offered));
        }

        [Test] public void ResistancePermanentlyRemovesThePaymentChoice()
        {
            var model = New(); Observe(model); model.Step(0.6f, Seen(), Tier); model.Step(2.1f, Seen(20), Tier);
            Assert.That(model.State, Is.EqualTo(PoliceEncounterState.Pursuit)); Assert.That(model.Fine, Is.EqualTo(500));
            Assert.That(model.PayFine(true), Is.False);
        }

        [Test] public void HiddenTargetDoesNotUpdateKnowledgeAndReacquisitionRaisesFine()
        {
            var model = New(); model.BeginScripted("encounter", "player", new Vector3(5, 0, 5), Vector3.forward * 10);
            int fine = model.Fine;
            var hidden = Seen(20); hidden.visible = false; hidden.position = new Vector3(999, 0, 999);
            model.Step(1f, hidden, Tier);
            Assert.That(model.State, Is.EqualTo(PoliceEncounterState.Cooldown));
            Assert.That(model.LastKnownPosition, Is.EqualTo(new Vector3(5, 0, 5)));
            model.Step(1f, Seen(), Tier);
            Assert.That(model.State, Is.EqualTo(PoliceEncounterState.Pursuit)); Assert.That(model.Fine, Is.GreaterThan(fine));
            Assert.That(model.CooldownProgress, Is.Zero);
        }

        [Test] public void MissingTargetPausesAndEngineOffHasNoInventedDefaultBonus()
        {
            var model = New(); model.BeginScripted("id", "target", Vector3.zero, Vector3.zero);
            model.Step(100f, default, Tier); Assert.That(model.State, Is.EqualTo(PoliceEncounterState.Pursuit));
            var hidden = Seen(); hidden.visible = false; model.Step(1f, hidden, Tier);
            hidden.engineOff = true; model.Step(1f, hidden, Tier);
            Assert.That(model.CooldownProgress, Is.EqualTo(1f / (Tier.SearchDuration + Tier.CooldownDuration)).Within(0.0001f));
            model.Step(30f, hidden, Tier); Assert.That(model.PendingOutcome.kind, Is.EqualTo(PoliceOutcomeKind.Escaped));
            Assert.That(model.PendingOutcome.reputation, Is.GreaterThan(0));
        }

        [Test] public void ArrestRequiresContinuousVisibleContainmentNotHealthOrDistanceAlone()
        {
            var model = New(); model.BeginScripted("id", "target", Vector3.zero, Vector3.zero);
            model.Step(0.7f, Seen(0, 1), Tier);
            var hidden = Seen(0, 1); hidden.visible = false; model.Step(0.1f, hidden, Tier);
            Assert.That(model.BustProgress, Is.Zero);
            model.Step(0.7f, Seen(0, 1), Tier); Assert.That(model.State, Is.EqualTo(PoliceEncounterState.Pursuit));
            model.Step(0.6f, Seen(0, 1), Tier); Assert.That(model.PendingOutcome.kind, Is.EqualTo(PoliceOutcomeKind.Arrested));
        }

        [Test] public void FrozenOutcomeCannotBeChangedByCallerOrOffences()
        {
            var model = New(); Observe(model); model.RequestOutcome(PoliceOutcomeKind.Escaped);
            var copy = model.PendingOutcome; copy.assessedFine = 100000;
            Assert.That(model.ObserveOffence(PoliceOffence.Collision, "x", "p", Vector3.zero, Vector3.zero), Is.False);
            Assert.That(model.PendingOutcome.assessedFine, Is.EqualTo(150));
        }

        [Test] public void RuleValidationRejectsNonfiniteAndInvalidEngagementOrder()
        {
            Assert.Throws<ArgumentException>(() => new PolicePursuitModel(new PolicePursuitRules { observationSeconds = float.NaN }));
            Assert.Throws<ArgumentException>(() => new PolicePursuitModel(new PolicePursuitRules { engagementStarts = new[] { 0, 2, 1, 3, 4 } }));
        }

        [Test] public void HiddenContactNeverChoosesRamEvenWithExtensions()
        {
            var result = VehiclePursuitDecisionModel.Decide(new VehiclePursuitDecisionInput { Role = VehiclePoliceUnitRole.Heavy,
                Phase = VehiclePursuitPhase.Engaged, TargetVisible = false, AllowContactExtensions = true, Aggression = 1f });
            Assert.That(result.Tactic, Is.EqualTo(VehiclePoliceTactic.Search)); Assert.That(result.CommitsToContact, Is.False);
        }

        [TestCase(PoliceOutcomeKind.Escaped, 1000, 100)]
        [TestCase(PoliceOutcomeKind.PaidFine, 850, 0)]
        [TestCase(PoliceOutcomeKind.Arrested, 850, 0)]
        public void AtomicOutcomeRetryDoesNotDuplicateCashRepOrHistory(PoliceOutcomeKind kind, int cash, int rep)
        {
            var root = new GameObject("Police settlement");
            try
            {
                var wallet = root.AddComponent<VehicleStoreWallet>(); wallet.SetBalance(1000);
                var bounty = root.AddComponent<VehicleBountySystem>();
                var profile = root.AddComponent<CareerProfileSystem>(); var storage = root.AddComponent<MissionTestStorage>();
                profile.ConfigureAutomaticPersistence(false, false, false); profile.SetStorage(storage); profile.TryCreateNewProfile(out _);
                var outcome = new PoliceOutcome { encounterId = "once", targetId = "player", kind = kind, assessedFine = 150,
                    engagementLevel = 1, reputation = rep, historicalBounty = kind == PoliceOutcomeKind.Escaped ? 250 : 0 };
                storage.Fail = true; Assert.That(profile.TrySettlePolice(outcome, out _), Is.False); Assert.That(wallet.Balance, Is.EqualTo(1000));
                storage.Fail = false; Assert.That(profile.TrySettlePolice(outcome, out string failure), Is.True, failure);
                Assert.That(profile.TrySettlePolice(outcome, out failure), Is.True, failure);
                Assert.That(wallet.Balance, Is.EqualTo(cash)); Assert.That(profile.CurrentProfile.economy.reputation, Is.EqualTo(rep));
                Assert.That(profile.CurrentProfile.police.settlements.Count, Is.EqualTo(1));
                Assert.That(CareerSaveCodec.Validate(profile.ProfileId, storage.Json), Is.EqualTo(CareerProfileData.CurrentVersion));
                outcome.assessedFine++;
                Assert.That(profile.TrySettlePolice(outcome, out _), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test] public void VersionThreeMigrationPreservesMissionsAndBountyWithoutPoliceAwards()
        {
            var profile = CareerProfileData.Create("career", "Driver"); profile.bounty.totalBounty = 42;
            var json = Newtonsoft.Json.Linq.JObject.Parse(JsonUtility.ToJson(profile)); json["saveVersion"] = 3; json.Remove("police");
            string missions = json["missions"].ToString();
            var result = Newtonsoft.Json.Linq.JObject.Parse(CareerSaveCodec.Migrate("career", json.ToString()));
            Assert.That(result["missions"].ToString(), Is.EqualTo(missions)); Assert.That((int)result["bounty"]["totalBounty"], Is.EqualTo(42));
            Assert.That((Newtonsoft.Json.Linq.JArray)result["police"]["settlements"], Is.Empty);
        }

        [Test] public void IgnitionOffProducesNoCombustionOrNitrousTorque()
        {
            var root = new GameObject("Ignition test"); var tuning = VehicleTuning.CreateStreetRacer();
            try
            {
                var powertrain = root.AddComponent<VehiclePowertrain>(); powertrain.Configure(tuning, Array.Empty<VehicleWheel>());
                powertrain.SetIgnition(false); var output = powertrain.Simulate(0.02f, 0f, 1f, false, true);
                Assert.That(output.EngineRpm, Is.Zero); Assert.That(output.WheelTorque, Is.Zero);
                powertrain.SetIgnition(true); Assert.That(powertrain.Simulate(0.02f, 0f, 1f, false, false).EngineRpm, Is.GreaterThan(0));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(tuning); }
        }

        [Test] public void ObservedOffenceDuringCooldownReacquiresWithoutWaitingForAnotherTick()
        {
            var model = New(); model.BeginScripted("id", "player", Vector3.zero, Vector3.zero);
            var hidden = Seen(); hidden.visible = false; model.Step(1f, hidden, Tier);
            int fine = model.Fine; Observe(model);
            Assert.That(model.State, Is.EqualTo(PoliceEncounterState.Pursuit));
            Assert.That(model.Fine, Is.EqualTo(fine + 150 + 150));
        }

        [Test] public void NewEncounterDoesNotBankUnfinishedLegacyPursuitRewards()
        {
            var root = new GameObject("Legacy encounter");
            try
            {
                var target = root.AddComponent<VehiclePursuitTargetAdapter>(); var history = root.AddComponent<VehicleBountySystem>();
                var data = CareerProfileData.Create("legacy", "Driver"); data.bounty.totalBounty = 42;
                history.Restore(data, out _); history.TryStartPursuit(out _);
                history.TryRecordEvent(VehicleBountyEventKind.PropertyDamage, 1, out _, out _);
                Assert.That(history.CurrentPursuitBounty, Is.GreaterThan(0));
                var director = root.AddComponent<VehiclePursuitDirector>(); director.Configure(target, history, null);
                Assert.That(director.TryStartPursuit(out _), Is.True);
                Assert.That(history.CurrentPursuitBounty, Is.Zero);
                var result = CareerProfileData.Create("legacy", "Driver"); history.Capture(result);
                Assert.That(result.bounty.totalBounty, Is.EqualTo(42));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test] public void DirectorRejectsUnobservedOffencesAndDoesNotRevealHiddenPositionInCommands()
        {
            var root = new GameObject("Knowledge test"); var wall = new GameObject("Occluder");
            try
            {
                var target = root.AddComponent<VehiclePursuitTargetAdapter>(); root.transform.position = Vector3.forward * 20;
                var director = root.AddComponent<VehiclePursuitDirector>(); director.Configure(target, null, null);
                var observer = new Observer(); director.RegisterUnit(observer);
                wall.transform.position = new Vector3(0, 1, 10); wall.AddComponent<BoxCollider>().size = new Vector3(40, 8, 1);
                Physics.SyncTransforms();
                Assert.That(director.ReportObservedOffence(observer, PoliceOffence.TrafficInfraction, out _), Is.False);
                Assert.That(director.IsActive, Is.False);
                wall.SetActive(false); Physics.SyncTransforms();
                Assert.That(director.ReportObservedOffence(observer, PoliceOffence.TrafficInfraction, out _), Is.True);
                Assert.That(director.LastKnownPosition, Is.EqualTo(root.transform.position));
                Assert.That(director.TrySetHeatLevel(3, out _), Is.True);
                director.Tick(0.6f);
                Vector3 known = director.LastKnownPosition;
                wall.SetActive(true); root.transform.position = new Vector3(8, 0, 30); Physics.SyncTransforms();
                director.Tick(1f);
                Assert.That(director.LastKnownPosition, Is.EqualTo(known));
                Assert.That(observer.Command.TargetVisible, Is.False);
                Assert.That(observer.Command.AimPoint, Is.EqualTo(known));
                Assert.That(observer.Command.Tactic, Is.EqualTo(VehiclePoliceTactic.Search));
                Assert.That(director.CanDisplayUnit(observer), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(wall); }
        }

        [TestCase(PoliceRoadHazardKind.Roadblock, 3)] [TestCase(PoliceRoadHazardKind.SpikeStrip, 5)]
        public void AuthoredHazardsRequireTierHiddenClearPlacementAndDeduplicateContacts(PoliceRoadHazardKind kind, int level)
        {
            var root = new GameObject("Hazard target"); var siteObject = new GameObject("Hazard site");
            var cameraObject = new GameObject("View"); var obstruction = new GameObject("Occupied placement");
            try
            {
                var target = root.AddComponent<VehiclePursuitTargetAdapter>(); var collider = root.AddComponent<BoxCollider>();
                var director = root.AddComponent<VehiclePursuitDirector>(); director.Configure(target, null, null);
                siteObject.transform.position = Vector3.forward * 150;
                var site = siteObject.AddComponent<PoliceRoadHazard>();
                var content = new GameObject("Physical content"); content.transform.SetParent(siteObject.transform, false); content.SetActive(false);
                site.Configure(director, kind, content, level);
                var view = cameraObject.AddComponent<Camera>(); view.transform.rotation = Quaternion.LookRotation(Vector3.back);
                Assert.That(site.TryDeploy(view), Is.False);
                director.TryStartPursuit(out _); Assert.That(site.TryDeploy(view), Is.False);
                director.TrySetHeatLevel(level, out _); Assert.That(site.TryDeploy(null), Is.False);
                view.transform.rotation = Quaternion.identity; Assert.That(site.TryDeploy(view), Is.False);
                view.transform.rotation = Quaternion.LookRotation(Vector3.back);
                obstruction.transform.position = siteObject.transform.position + Vector3.up;
                obstruction.AddComponent<BoxCollider>(); Physics.SyncTransforms(); Assert.That(site.TryDeploy(view), Is.False);
                obstruction.SetActive(false); Physics.SyncTransforms(); Assert.That(site.TryDeploy(view), Is.True);
                Assert.That(site.TryDeploy(view), Is.False); Assert.That(site.ReportSpikeContact(null), Is.False);
                int contacts = 0; site.SpikeContact += _ => contacts++;
                Assert.That(site.ReportSpikeContact(collider), Is.False, "Remote contact must not trigger a strip.");
                root.transform.position = siteObject.transform.position; Physics.SyncTransforms();
                Assert.That(site.ReportSpikeContact(collider), Is.EqualTo(kind == PoliceRoadHazardKind.SpikeStrip));
                Assert.That(site.ReportSpikeContact(collider), Is.False);
                Assert.That(contacts, Is.EqualTo(kind == PoliceRoadHazardKind.SpikeStrip ? 1 : 0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(siteObject);
                UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(obstruction);
            }
        }

        [TestCase(PoliceOutcomeKind.PaidFine, false, 50)] [TestCase(PoliceOutcomeKind.Arrested, true, 0)]
        public void InsufficientFundsPolicyIsExplicit(PoliceOutcomeKind kind, bool accepted, int balance)
        {
            var root = new GameObject("Limited wallet");
            try
            {
                var wallet = root.AddComponent<VehicleStoreWallet>(); wallet.SetBalance(50); root.AddComponent<VehicleBountySystem>();
                var profile = root.AddComponent<CareerProfileSystem>(); var storage = root.AddComponent<MissionTestStorage>();
                profile.ConfigureAutomaticPersistence(false, false, false); profile.SetStorage(storage); profile.TryCreateNewProfile(out _);
                Assert.That(profile.TrySettlePolice(new PoliceOutcome { encounterId = "poor", targetId = "player", kind = kind,
                    assessedFine = 150, engagementLevel = 1 }, out _), Is.EqualTo(accepted));
                Assert.That(wallet.Balance, Is.EqualTo(balance));
                Assert.That(profile.CurrentProfile.police.settlements.Count, Is.EqualTo(accepted ? 1 : 0));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test] public void LostWriteAcknowledgementReconcilesExactlyOnceWithInitializedEconomyJournal()
        {
            var root = new GameObject("Lost acknowledgement");
            try
            {
                var wallet = root.AddComponent<VehicleStoreWallet>(); wallet.SetBalance(1000); root.AddComponent<VehicleBountySystem>();
                var profile = root.AddComponent<CareerProfileSystem>(); var storage = root.AddComponent<PoliceLostAckStorage>();
                profile.ConfigureAutomaticPersistence(false, false, false); profile.SetStorage(storage); profile.TryCreateNewProfile(out _);
                Assert.That(profile.TrySave(out _), Is.True);
                var saved = profile.CurrentProfile; saved.economy.initialized = true; saved.economy.openingCash = 1000;
                storage.Json = JsonUtility.ToJson(saved); Assert.That(profile.TryLoad(out string failure), Is.True, failure);
                storage.ThrowAfterWrite = true; int writes = storage.Writes;
                var outcome = new PoliceOutcome { encounterId = "committed", targetId = "player", kind = PoliceOutcomeKind.PaidFine,
                    assessedFine = 150, engagementLevel = 1 };
                Assert.That(profile.TrySettlePolice(outcome, out failure), Is.True, failure);
                Assert.That(profile.TrySettlePolice(outcome, out failure), Is.True, failure);
                Assert.That(storage.Writes, Is.EqualTo(writes + 1)); Assert.That(wallet.Balance, Is.EqualTo(850));
                Assert.That(profile.CurrentProfile.economy.receipts.Count, Is.EqualTo(1));
                Assert.That(profile.CurrentProfile.police.pendingWorldOutcomeId, Is.EqualTo("committed"));
                Assert.That(CareerSaveCodec.Validate(profile.ProfileId, storage.Json), Is.EqualTo(CareerProfileData.CurrentVersion));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test] public void SaveCodecRejectsMalformedOrConflictingPoliceHistory()
        {
            var saved = CareerProfileData.Create("test", "Driver");
            saved.police.settlements.Add(new PoliceSettlementRecord { outcome = new PoliceOutcome { encounterId = "id", targetId = "p",
                kind = PoliceOutcomeKind.PaidFine, assessedFine = 150, engagementLevel = 1 }, cashPaid = 150 });
            var json = Newtonsoft.Json.Linq.JObject.Parse(JsonUtility.ToJson(saved));
            var outcome = (Newtonsoft.Json.Linq.JObject)json["police"]["settlements"][0]["outcome"];
            outcome.Remove("kind"); Assert.Throws<SaveException>(() => CareerSaveCodec.Validate("test", json.ToString()));
            outcome["kind"] = 0; outcome["reputation"] = 10;
            Assert.Throws<SaveException>(() => CareerSaveCodec.Validate("test", json.ToString()));
            outcome["reputation"] = 0;
            ((Newtonsoft.Json.Linq.JArray)json["police"]["settlements"]).Add(json["police"]["settlements"][0].DeepClone());
            Assert.Throws<SaveException>(() => CareerSaveCodec.Validate("test", json.ToString()));
        }

        private sealed class Observer : IVehiclePoliceUnit
        {
            public string UnitId => "observer";
            public VehiclePoliceUnitRole Role => VehiclePoliceUnitRole.Pursuer;
            public VehiclePoliceUnitState State { get; private set; } = VehiclePoliceUnitState.Released;
            public VehiclePoliceTactic Tactic => Command.Tactic;
            public Vector3 Position => Vector3.zero;
            public float Integrity => 100f;
            public bool IsDisabled => false;
            public VehiclePoliceUnitCommand Command;
            public void SetPursuitCommand(VehiclePoliceUnitCommand command, IVehiclePursuitTarget target, VehiclePoliceResponseTier tier)
            { Command = command; State = VehiclePoliceUnitState.Engaged; }
            public void ReleaseFromPursuit() { State = VehiclePoliceUnitState.Released; }
            public void ApplyDamage(float amount) { }
        }
    }

    public sealed class PoliceLostAckStorage : MonoBehaviour, ICareerProfileStorage
    {
        public string Json;
        public bool ThrowAfterWrite;
        public int Writes;
        public bool TrySave(string id, string value, out string failure)
        { Json = value; Writes++; failure = ""; if (ThrowAfterWrite) throw new InvalidOperationException("Lost acknowledgement"); return true; }
        public bool TryLoad(string id, out string value, out string failure) { value = Json; failure = ""; return value != null; }
    }
}
