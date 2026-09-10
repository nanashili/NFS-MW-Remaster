using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleCareerTests
    {
        [Test]
        public void WalletLedgerSpendsAndAcceptsRewards()
        {
            VehicleWalletLedger wallet = new VehicleWalletLedger(100);

            Assert.That(wallet.TrySpend(40, out string spendFailure), Is.True, spendFailure);
            Assert.That(wallet.TryAdd(25, out string addFailure), Is.True, addFailure);
            Assert.That(wallet.Balance, Is.EqualTo(85));
        }

        [Test]
        public void WalletLedgerRejectsInvalidAmountsWithoutMutation()
        {
            VehicleWalletLedger wallet = new VehicleWalletLedger(100);

            Assert.That(wallet.TrySpend(-1, out string spendFailure), Is.False);
            Assert.That(wallet.TryAdd(-1, out string addFailure), Is.False);
            Assert.That(wallet.Balance, Is.EqualTo(100));
            Assert.That(spendFailure, Does.Contain("negative"));
            Assert.That(addFailure, Does.Contain("negative"));
        }

        [Test]
        public void BountyEscapeCommitsPursuitBountyAndTracksMilestoneFacts()
        {
            VehicleBountyProgress bounty = new VehicleBountyProgress();
            bounty.SetHeatLevel(4);
            Assert.That(bounty.TryStartPursuit(out string startFailure), Is.True, startFailure);

            Assert.That(
                bounty.TryAdvanceTime(
                    2.25f,
                    out VehicleBountyAward timeAward,
                    out string timeFailure),
                Is.True,
                timeFailure);
            Assert.That(timeAward.Bounty, Is.EqualTo(50));
            Assert.That(
                bounty.TryRecord(
                    VehicleBountyEventKind.PoliceVehicleDisabled,
                    1,
                    out _,
                    out string disabledFailure),
                Is.True,
                disabledFailure);
            Assert.That(
                bounty.TryRecord(
                    VehicleBountyEventKind.RoadblockDodged,
                    2,
                    out _,
                    out string roadblockFailure),
                Is.True,
                roadblockFailure);
            Assert.That(
                bounty.TryRecord(
                    VehicleBountyEventKind.SpikeStripDodged,
                    1,
                    out _,
                    out string spikeFailure),
                Is.True,
                spikeFailure);
            Assert.That(
                bounty.TryRecord(
                    VehicleBountyEventKind.PropertyDamage,
                    3,
                    out _,
                    out string damageFailure),
                Is.True,
                damageFailure);
            Assert.That(
                bounty.TryRecord(
                    VehicleBountyEventKind.TrafficInfraction,
                    1,
                    out VehicleBountyAward infractionAward,
                    out string infractionFailure),
                Is.True,
                infractionFailure);

            Assert.That(infractionAward.HeatDelta, Is.EqualTo(1));
            Assert.That(bounty.CurrentPursuitBounty, Is.EqualTo(1900));
            Assert.That(bounty.HeatLevel, Is.EqualTo(5));
            Assert.That(bounty.RoadblocksDodged, Is.EqualTo(2));
            Assert.That(bounty.SpikeStripsDodged, Is.EqualTo(1));

            Assert.That(
                bounty.TryEscape(
                    out VehicleBountyPursuitResult result,
                    out string escapeFailure),
                Is.True,
                escapeFailure);
            Assert.That(result.BountyEarned, Is.EqualTo(1900));
            Assert.That(bounty.TotalBounty, Is.EqualTo(1900));
            Assert.That(bounty.CurrentPursuitBounty, Is.Zero);
            Assert.That(bounty.PursuitActive, Is.False);
        }

        [Test]
        public void BountyBustDiscardsProvisionalBounty()
        {
            VehicleBountyProgress bounty = new VehicleBountyProgress();
            bounty.SetHeatLevel(2);
            bounty.TryStartPursuit(out _);
            bounty.TryRecord(
                VehicleBountyEventKind.PoliceVehicleDisabled,
                1,
                out _,
                out _);
            bounty.TryAdvanceTime(1f, out _, out _);

            Assert.That(
                bounty.TryBust(
                    out VehicleBountyPursuitResult result,
                    out string failure),
                Is.True,
                failure);
            Assert.That(result.BountyLost, Is.EqualTo(525));
            Assert.That(bounty.TotalBounty, Is.Zero);
            Assert.That(bounty.HeatLevel, Is.EqualTo(1));
            Assert.That(bounty.PursuitActive, Is.False);
        }

        [Test]
        public void BountyProgressRestoresAnActivePursuitSnapshot()
        {
            VehicleBountyProgress bounty = new VehicleBountyProgress();
            bounty.TryStartPursuit(out _);
            bounty.TryAdvanceTime(1.5f, out _, out _);
            bounty.TryRecord(
                VehicleBountyEventKind.PropertyDamage,
                2,
                out _,
                out _);

            CareerBountyData saved = new CareerBountyData();
            bounty.Capture(saved);

            VehicleBountyProgress restored = new VehicleBountyProgress();
            Assert.That(
                restored.Restore(saved, out string restoreFailure),
                Is.True,
                restoreFailure);
            Assert.That(restored.PursuitActive, Is.True);
            Assert.That(restored.CurrentPursuitBounty, Is.EqualTo(225));
            Assert.That(restored.PursuitDurationSeconds, Is.EqualTo(1.5f));
            Assert.That(restored.PropertyDamageEvents, Is.EqualTo(2));

            Assert.That(
                restored.TryAdvanceTime(
                    0.5f,
                    out VehicleBountyAward award,
                    out string advanceFailure),
                Is.True,
                advanceFailure);
            Assert.That(award.Bounty, Is.EqualTo(25));
            Assert.That(restored.CurrentPursuitBounty, Is.EqualTo(250));
        }

        [Test]
        public void BountyFacadeExposesTheSnapshotThroughItsInterface()
        {
            GameObject root = new GameObject("Bounty Facade Test");
            try
            {
                VehicleBountySystem system = root.AddComponent<VehicleBountySystem>();
                system.SetStartingHeatLevel(3);
                IVehicleBountyTracker tracker = system;

                Assert.That(tracker.MaxHeatLevel, Is.EqualTo(5));
                Assert.That(tracker.Snapshot.HeatLevel, Is.EqualTo(3));
                Assert.That(tracker.PursuitsEscaped, Is.Zero);
                Assert.That(tracker.PursuitsBusted, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CareerProfileRoundTripPreservesStableGameState()
        {
            CareerProfileData profile = CareerProfileData.Create(
                "slot_01",
                "Mia");
            profile.wallet.balance = 4200;
            profile.store.ownedProductIds.Add("engine_pro");
            profile.store.ownedVehicleIds.Add("vehicle_supra");
            CareerVehicleData vehicle = profile.GetOrCreateVehicle("vehicle_supra");
            vehicle.performanceUpgradeIds.Add("engine_pro");
            vehicle.customizationIds.Add("bodykit_01");
            profile.bounty.totalBounty = 12000;
            profile.bounty.heatLevel = 3;

            string json = JsonUtility.ToJson(profile);
            CareerProfileData roundTrip = JsonUtility.FromJson<CareerProfileData>(json);

            Assert.That(roundTrip.Validate("slot_01", out string failure), Is.True, failure);
            Assert.That(roundTrip.wallet.balance, Is.EqualTo(4200));
            Assert.That(roundTrip.store.ownedVehicleIds, Contains.Item("vehicle_supra"));
            Assert.That(
                roundTrip.FindVehicle("vehicle_supra").performanceUpgradeIds,
                Contains.Item("engine_pro"));
            Assert.That(roundTrip.bounty.totalBounty, Is.EqualTo(12000));
        }

        [Test]
        public void ProfileSystemUsesInjectedStorageAndParticipants()
        {
            GameObject root = new GameObject("Career Profile Test");
            try
            {
                FakeProfileStorage storage = root.AddComponent<FakeProfileStorage>();
                FakeProfileParticipant participant =
                    root.AddComponent<FakeProfileParticipant>();
                CareerProfileSystem profile = root.AddComponent<CareerProfileSystem>();
                profile.SetStorage(storage);
                profile.SetParticipants(new MonoBehaviour[] { participant });
                profile.SetProfileId("slot_02");
                profile.SetPlayerName("Razor");
                profile.SetActiveVehicleId("vehicle_demo");
                participant.Value = 7;

                Assert.That(
                    profile.TrySave(out string saveFailure),
                    Is.True,
                    saveFailure);
                participant.Value = 0;

                Assert.That(
                    profile.TryLoad(out string loadFailure),
                    Is.True,
                    loadFailure);
                Assert.That(participant.Value, Is.EqualTo(7));
                Assert.That(profile.CurrentProfile.profileId, Is.EqualTo("slot_02"));
                Assert.That(profile.CurrentProfile.activeVehicleId, Is.EqualTo("vehicle_demo"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private sealed class FakeProfileStorage : MonoBehaviour, ICareerProfileStorage
        {
            private string serializedProfile;

            public bool TrySave(
                string profileId,
                string serialized,
                out string failure)
            {
                serializedProfile = serialized;
                failure = string.Empty;
                return true;
            }

            public bool TryLoad(
                string profileId,
                out string serialized,
                out string failure)
            {
                serialized = serializedProfile;
                failure = serializedProfile == null
                    ? "Nothing was saved."
                    : string.Empty;
                return serializedProfile != null;
            }
        }

        private sealed class FakeProfileParticipant :
            MonoBehaviour,
            ICareerProfileParticipant
        {
            public int Value { get; set; }

            public string ProfileSectionId
            {
                get { return "test"; }
            }

            public void Capture(CareerProfileData profile)
            {
                profile.statistics.eventsCompleted = Value;
            }

            public bool Restore(CareerProfileData profile, out string failure)
            {
                Value = profile.statistics.eventsCompleted;
                failure = string.Empty;
                return true;
            }
        }
    }
}
