using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class FreeRoamSessionTests
    {
        private GameObject player;
        private GameObject world;
        private VehicleController vehicle;
        private VehicleTuning tuning;
        private FreeRoamSession session;
        private WorldLocation garage;
        private VehicleBountySystem bounty;
        private FreeRoamEventDefinition race;
        private float initialTimeScale;

        [SetUp]
        public void SetUp()
        {
            initialTimeScale = Time.timeScale;
            player = new GameObject("Free Roam Test Player"); world = new GameObject("Free Roam Test World");
            vehicle = player.AddComponent<VehicleController>(); tuning = VehicleTuning.CreateStreetRacer();
            vehicle.ConfigureForRuntime(tuning, null, new VehicleWheel[0]);
            bounty = player.AddComponent<VehicleBountySystem>();
            var roads = world.AddComponent<RoadNetwork>();
            roads.Configure(new[] { new RoadNode { position = Vector3.zero, exits = new[] { 1 } },
                new RoadNode { position = Vector3.forward * 100, exits = new[] { 0 } } });
            garage = world.AddComponent<WorldLocation>(); garage.Configure("garage", "Garage", WorldLocationKind.Garage, null);
            race = world.AddComponent<FreeRoamEventDefinition>(); race.Configure("sprint", "Sprint", FreeRoamEventKind.Sprint,
                new[] { Vector3.forward * 100 }, 1, 60, 1000);
            session = player.AddComponent<FreeRoamSession>(); session.Configure(vehicle, roads, null, new[] { garage }, new[] { race }, false);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(player); Object.DestroyImmediate(world); Object.DestroyImmediate(tuning);
            Time.timeScale = initialTimeScale;
        }

        [Test]
        public void MissionCourseSnapshotRestoresProgressAndClaimIdentity()
        {
            race.Configure("sprint", "Sprint", FreeRoamEventKind.Sprint, new[] { Vector3.forward * 100, Vector3.forward * 200 }, 1, 60, 1000);
            Assert.That(session.TryStartEvent(race, out _), Is.True);
            session.EventProgress.Advance(1, Vector3.forward * 100, Vector3.forward * 100, 90);
            var data = CareerProfileData.Create("career", "Driver"); session.Capture(data);
            string claim = data.freeRoam.missionResume.claimId;
            var loaded = JsonUtility.FromJson<CareerProfileData>(JsonUtility.ToJson(data));
            Assert.That(CareerSaveCodec.Validate("career", JsonUtility.ToJson(data)), Is.EqualTo(CareerProfileData.CurrentVersion));
            session.ExitActivity();
            Assert.That(session.Restore(loaded, out var failure), Is.True, failure);
            Assert.That(session.EventProgress.CheckpointsPassed, Is.EqualTo(1));
            Assert.That(session.EventProgress.Runtime.Capture().claimId, Is.EqualTo(claim));
            Assert.That(session.State, Is.EqualTo(FreeRoamState.Event));
        }

        [Test]
        public void RestoredRaceCanRestartAsANewAttempt()
        {
            race.Configure("sprint", "Sprint", FreeRoamEventKind.Sprint,
                new[] { Vector3.forward * 100, Vector3.forward * 200 }, 1, 60, 1000);
            Assert.That(session.TryStartEvent(race, out _), Is.True);
            session.EventProgress.Advance(1, Vector3.forward * 100, Vector3.forward * 100, 90);
            var data = CareerProfileData.Create("career", "Driver");
            session.Capture(data);
            string restoredClaim = data.freeRoam.missionResume.claimId;
            CareerProfileData recovered = JsonUtility.FromJson<CareerProfileData>(
                JsonUtility.ToJson(data));

            session.ExitActivity();
            Assert.That(session.Restore(recovered, out string restoreFailure), Is.True, restoreFailure);
            MissionCourse restoredAttempt = session.EventProgress;
            session.TogglePause();

            Assert.That(session.RetryEvent(out string restartFailure), Is.True, restartFailure);
            Assert.That(restoredAttempt.Outcome, Is.EqualTo(MissionState.Aborted));
            Assert.That(session.EventProgress, Is.Not.SameAs(restoredAttempt));
            Assert.That(session.EventProgress.Runtime.Capture().claimId, Is.Not.EqualTo(restoredClaim));
            Assert.That(session.EventProgress.Elapsed, Is.Zero);
            Assert.That(session.Countdown, Is.EqualTo(3));
        }

        [Test]
        public void LocationRequiresProximityAndStoppedVehicleThenReleasesInputOnExit()
        {
            IFreeRoamSession flow = session;
            player.transform.position = Vector3.forward * 50;
            Assert.That(flow.TryEnter(garage, out _), Is.False);
            player.transform.position = Vector3.zero; vehicle.Body.linearVelocity = Vector3.forward * 10;
            Assert.That(flow.TryEnter(garage, out _), Is.False);
            vehicle.Body.linearVelocity = Vector3.zero;
            Assert.That(flow.TryEnter(garage, out _), Is.True);
            Assert.That(flow.State, Is.EqualTo(FreeRoamState.Location));
            Assert.That(flow.CanDrive, Is.False);
            Assert.That(vehicle.Body.isKinematic, Is.True);
            flow.ExitActivity();
            Assert.That(flow.CanDrive, Is.True);
            Assert.That(vehicle.Body.isKinematic, Is.False);
            Assert.That(vehicle.enabled, Is.True);
        }

        [Test]
        public void CommittedArrestReplaysGarageRelocationAfterLoadWithoutAnotherSettlement()
        {
            var data = CareerProfileData.Create("career", "Driver"); data.wallet.balance = 850;
            data.police.settlements.Add(new PoliceSettlementRecord { cashPaid = 150,
                outcome = new PoliceOutcome { encounterId = "arrest", targetId = "player", assessedFine = 150,
                    engagementLevel = 1, kind = PoliceOutcomeKind.Arrested } });
            data.police.pendingWorldOutcomeId = "arrest";
            player.transform.position = Vector3.forward * 50;
            Assert.That(session.Restore(data, out string failure), Is.True, failure);
            Assert.That(session.CanDrive, Is.False);
            typeof(FreeRoamSession).GetMethod("ApplyPoliceWorldOutcome",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(session, null);
            Assert.That(Vector3.Distance(player.transform.position, garage.Position), Is.LessThan(15));
            Assert.That(session.CanDrive, Is.True); session.Capture(data);
            Assert.That(data.police.pendingWorldOutcomeId, Is.Empty);
            Assert.That(data.wallet.balance, Is.EqualTo(850)); Assert.That(data.police.settlements.Count, Is.EqualTo(1));
        }

        [Test]
        public void TerminalPoliceReceiptCanResumePursuitMissionWithoutReconstructingActivePolice()
        {
            var target = player.AddComponent<VehiclePursuitTargetAdapter>(); var director = world.AddComponent<VehiclePursuitDirector>();
            director.Configure(target, bounty, null);
            race.Configure("challenge", "Challenge", FreeRoamEventKind.Pursuit, new[] { Vector3.forward * 100 }, 1, 120, 1000);
            session.Configure(vehicle, world.GetComponent<RoadNetwork>(), director, new[] { garage }, new[] { race }, false);
            Assert.That(session.TryStartEvent(race, out _), Is.True);
            var data = CareerProfileData.Create("career", "Driver"); session.Capture(data);
            Assert.That(session.Restore(data, out _), Is.False, "An active encounter still requires a police-world checkpoint.");
            data.police.settlements.Add(new PoliceSettlementRecord { outcome = new PoliceOutcome { encounterId = "escape",
                targetId = "player", assessedFine = 500, engagementLevel = 2, kind = PoliceOutcomeKind.Escaped, reputation = 200 } });
            data.police.pendingWorldOutcomeId = "escape";
            Assert.That(session.Restore(data, out string failure), Is.True, failure);
            Assert.That(session.Countdown, Is.Zero); Assert.That(session.ActiveEvent, Is.SameAs(race));
            Assert.That(director.IsActive, Is.False);
        }

        [Test]
        public void PauseRestoresPreviousStateAndTimeScale()
        {
            Time.timeScale = 0.5f;
            Assert.That(session.TryEnter(garage, out _), Is.True);
            session.TogglePause();
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(session.CanDrive, Is.False);
            session.TogglePause();
            Assert.That(Time.timeScale, Is.EqualTo(0.5f));
            Assert.That(session.State, Is.EqualTo(FreeRoamState.Location));
            Assert.That(vehicle.Body.isKinematic, Is.True);
        }

        [Test]
        public void PursuitBlocksShopsEventsSavingLoadingAndRecovery()
        {
            Assert.That(bounty.TryStartPursuit(out _), Is.True);
            Assert.That(session.TryEnter(garage, out _), Is.False);
            Assert.That(session.TryStartEvent(race, out _), Is.False);
            Assert.That(session.TryRecover(out _), Is.False);
            Assert.That(session.TrySave(out _), Is.False);
            Assert.That(session.TryLoad(out _), Is.False);
            Assert.That(session.TryPurchase("arbitrary", out _), Is.False);
        }

        [Test]
        public void EventCountdownLocksInputAndAbandonReleasesItWithoutReward()
        {
            Assert.That(session.TryStartEvent(race, out _), Is.True);
            Assert.That(session.State, Is.EqualTo(FreeRoamState.Event));
            Assert.That(session.CanDrive, Is.False);
            Assert.That(session.TryRecover(out _), Is.False);
            Assert.That(session.TrySave(out _), Is.False);
            session.TogglePause();
            Assert.That(session.TrySave(out _), Is.False);
            session.TogglePause(); session.ExitActivity();
            Assert.That(session.EventProgress.Outcome, Is.EqualTo(MissionState.Aborted));
            Assert.That(session.CanDrive, Is.True);
            Assert.That(session.CompletedEvents, Is.Empty);
        }

        [Test]
        public void ManagedRacePreparationDoesNotStartTheCountdownUntilActivated()
        {
            session.UseApplicationFlow();
            Assert.That(session.TryStartEvent(race, out _), Is.True);
            Assert.That(session.FlowState, Is.EqualTo(GameFlowState.EventLoading));
            Assert.That(session.CanDrive, Is.False);
            Assert.That(session.TryExecute(GameFlowCommand.Pause, out _), Is.False);
            Assert.That(session.TrySave(out _), Is.False);
            Assert.That(session.TryExecute(GameFlowCommand.ActivateEvent, out _), Is.True);
            Assert.That(session.FlowState, Is.EqualTo(GameFlowState.RaceActive));
            Assert.That(session.Countdown, Is.EqualTo(3));
            Assert.That(session.TryExecute(GameFlowCommand.ActivateEvent, out _), Is.False);
        }

        [Test]
        public void RestartAbandonsOnlyTheOldAttemptAndResetsTheCountdown()
        {
            Assert.That(session.TryStartEvent(race, out _), Is.True);
            var oldAttempt = session.EventProgress;
            session.TogglePause();
            Assert.That(session.RetryEvent(out _), Is.True);
            Assert.That(oldAttempt.Outcome, Is.EqualTo(MissionState.Aborted));
            Assert.That(session.EventProgress, Is.Not.SameAs(oldAttempt));
            Assert.That(session.EventProgress.Elapsed, Is.Zero); Assert.That(session.Countdown, Is.EqualTo(3));
            Assert.That(Time.timeScale, Is.EqualTo(initialTimeScale));
        }

        [Test]
        public void OccupiedGridDoesNotDiscardThePausedAttempt()
        {
            Assert.That(session.TryStartEvent(race, out _), Is.True); session.TogglePause();
            var oldAttempt = session.EventProgress;
            var obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                obstruction.transform.position = Vector3.up; obstruction.transform.localScale = new Vector3(40, 4, 40);
                Physics.SyncTransforms();
                Assert.That(session.RetryEvent(out _), Is.False);
                Assert.That(session.State, Is.EqualTo(FreeRoamState.Paused)); Assert.That(Time.timeScale, Is.Zero);
                Assert.That(session.EventProgress, Is.SameAs(oldAttempt));
                Assert.That(oldAttempt.Outcome, Is.EqualTo(MissionState.Active));
            }
            finally { Object.DestroyImmediate(obstruction); }
        }

        [Test]
        public void PursuitCannotBeEscapedThroughRestartOrMainMenu()
        {
            Assert.That(session.TryStartEvent(race, out _), Is.True); session.TogglePause();
            Assert.That(bounty.TryStartPursuit(out _), Is.True);
            Assert.That(session.RetryEvent(out _), Is.False); Assert.That(session.TrySaveForExit(out _), Is.False);
            Assert.That(session.State, Is.EqualTo(FreeRoamState.Paused));
        }

        [Test]
        public void ProfileCaptureIsAnIndependentSnapshot()
        {
            Assert.That(session.TryEnter(garage, out _), Is.True);
            var profile = CareerProfileData.Create("free_roam", "Tester");
            session.Capture(profile);
            Assert.That(profile.freeRoam.discoveredLocationIds, Does.Contain("garage"));
            profile.freeRoam.discoveredLocationIds.Clear();
            session.Capture(profile);
            Assert.That(profile.freeRoam.discoveredLocationIds, Does.Contain("garage"));
        }
    }
}
