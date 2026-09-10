using System;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class EconomySettlementTests
    {
        private sealed class Storage : ICareerProfileStorage
        {
            public string Data;
            public bool Reject, ThrowAfterWrite;
            public int Writes;
            public bool TrySave(string id, string value, out string failure)
            {
                failure = Reject ? "Disk full" : string.Empty;
                if (Reject) return false;
                Data = value; Writes++;
                if (ThrowAfterWrite) throw new InvalidOperationException("Lost acknowledgement");
                return true;
            }
            public bool TryLoad(string id, out string value, out string failure)
            { value = Data; failure = string.Empty; return value != null; }
        }

        private static CareerProfileData Profile(int cash = 1000)
        {
            var profile = CareerProfileData.Create("test", "Driver");
            profile.wallet.balance = cash;
            profile.GetOrCreateVehicle("player_vehicle");
            return profile;
        }

        private static EconomyDefinition Definition() => new EconomyDefinition
        {
            items = new[]
            {
                new EconomyItemDefinition { id = "car.reward", name = "Rival car", kind = EconomyItemKind.Vehicle, price = 500,
                    performanceIds = new[] { "part.engine" }, customizationIds = new[] { "visual.paint" } },
                new EconomyItemDefinition { id = "part.engine", name = "Engine", kind = EconomyItemKind.Upgrade, price = 100 },
                new EconomyItemDefinition { id = "visual.paint", name = "Paint", kind = EconomyItemKind.Customization }
            },
            policies = new[]
            {
                new EconomyActivityPolicy { id = "race", positionCash = new[] { 100, 20 }, firstWinCash = 50, firstWinReputation = 10,
                    repeatBasisPoints = 5000, difficultyBasisPoints = new[] { 10000, 11000 },
                    firstWinGrants = new[] { new EconomyGrant { kind = EconomyItemKind.Vehicle, itemId = "car.reward" } } },
                new EconomyActivityPolicy { id = "pursuit", kind = EconomyActivityKind.Pursuit, finePerHeat = 100, finePerInfraction = 20,
                    maximumFine = 1000, fineWalletBasisPoints = 5000, walletFloor = 100 }
            }
        };

        private static void Begin(EconomySettlement engine, string id = "attempt.1", string policy = "race", int difficulty = 0)
        {
            Assert.That(engine.TryBegin(new EconomyActivityRegistration { activityId = id, policyId = policy,
                vehicleId = "player_vehicle", eventId = "event.a", difficulty = difficulty, startedUtcSeconds = 1 }, out string error), Is.True, error);
        }
        private static EconomyActivityOutcome Win() => new EconomyActivityOutcome
        { outcome = EconomyOutcomeKind.Finished, position = 1, elapsedMilliseconds = 30000 };

        [Test]
        public void FirstWinAndRepeatAreDistinctAndReceiptReconciles()
        {
            var storage = new Storage(); var engine = new EconomySettlement(Profile(), Definition(), storage);
            Begin(engine);
            Assert.That(engine.TrySettle("attempt.1", Win(), 10, out var receipt, out string error), Is.True, error);
            Assert.That(receipt.previousCash, Is.EqualTo(1000)); Assert.That(receipt.newCash, Is.EqualTo(1150));
            Assert.That(engine.Reputation, Is.EqualTo(10)); Assert.That(engine.IsUnlocked("car.reward"), Is.True);
            Assert.That(engine.Snapshot().store.ownedVehicleIds, Is.Empty);
            Begin(engine, "attempt.2");
            Assert.That(engine.TrySettle("attempt.2", Win(), 20, out receipt, out error), Is.True, error);
            Assert.That(receipt.newCash, Is.EqualTo(1200)); Assert.That(engine.Reputation, Is.EqualTo(10));
        }

        [Test]
        public void DuplicateReturnsSavedReceiptAfterReloadWithoutWriting()
        {
            var storage = new Storage(); var config = Definition(); var engine = new EconomySettlement(Profile(), config, storage);
            Begin(engine); Assert.That(engine.TrySettle("attempt.1", Win(), 10, out var receipt, out _), Is.True);
            int writes = storage.Writes;
            var resumed = new EconomySettlement(JsonUtility.FromJson<CareerProfileData>(storage.Data), config, storage);
            Assert.That(resumed.TrySettle("attempt.1", Win(), 100, out var replay, out _), Is.True);
            Assert.That(JsonUtility.ToJson(replay), Is.EqualTo(JsonUtility.ToJson(receipt)));
            Assert.That(storage.Writes, Is.EqualTo(writes));
            var changed = Win(); changed.position = 2;
            Assert.That(resumed.TrySettle("attempt.1", changed, 100, out _, out _), Is.False);
        }

        [Test]
        public void RejectedSaveLeavesCashClaimsAndOwnershipUnchanged()
        {
            var storage = new Storage(); var engine = new EconomySettlement(Profile(), Definition(), storage); Begin(engine);
            string before = JsonUtility.ToJson(engine.Snapshot()); storage.Reject = true;
            Assert.That(engine.TrySettle("attempt.1", Win(), 10, out _, out _), Is.False);
            Assert.That(JsonUtility.ToJson(engine.Snapshot()), Is.EqualTo(before));
            storage.Reject = false;
            Assert.That(engine.TrySettle("attempt.1", Win(), 10, out _, out _), Is.True);
            Assert.That(engine.Balance, Is.EqualTo(1150));
        }

        [Test]
        public void LostAcknowledgementReconcilesPublishedSnapshot()
        {
            var storage = new Storage(); var engine = new EconomySettlement(Profile(), Definition(), storage); Begin(engine);
            storage.ThrowAfterWrite = true;
            Assert.That(engine.TrySettle("attempt.1", Win(), 10, out _, out _), Is.True);
            Assert.That(engine.RequiresReload, Is.False); Assert.That(engine.Balance, Is.EqualTo(1150));
        }

        [TestCase(EconomyOutcomeKind.Restarted)]
        [TestCase(EconomyOutcomeKind.Abandoned)]
        [TestCase(EconomyOutcomeKind.Invalid)]
        public void NonFinishesAreTerminalAndCannotLaterPay(EconomyOutcomeKind outcome)
        {
            var engine = new EconomySettlement(Profile(), Definition(), new Storage()); Begin(engine);
            Assert.That(engine.TrySettle("attempt.1", new EconomyActivityOutcome { outcome = outcome }, 10, out var receipt, out _), Is.True);
            Assert.That(receipt.newCash, Is.EqualTo(1000));
            Assert.That(engine.TrySettle("attempt.1", Win(), 11, out _, out _), Is.False);
        }

        [Test]
        public void DifficultyAndPolicyAreFrozenAtStart()
        {
            var config = Definition(); var storage = new Storage(); var engine = new EconomySettlement(Profile(), config, storage);
            Begin(engine, difficulty: 1); config.policies[0].positionCash[0] = 9999;
            var resumed = new EconomySettlement(JsonUtility.FromJson<CareerProfileData>(storage.Data), config, storage);
            Assert.That(resumed.TrySettle("attempt.1", Win(), 10, out var receipt, out _), Is.True);
            Assert.That(receipt.newCash, Is.EqualTo(1160));
        }

        [Test]
        public void UnlockDoesNotGrantAndPurchasePreservesAuthoredVehicle()
        {
            var storage = new Storage(); var engine = new EconomySettlement(Profile(), Definition(), storage);
            Assert.That(engine.TryPurchase("purchase.locked", "car.reward", "", 2, out _, out _), Is.False);
            Begin(engine); engine.TrySettle("attempt.1", Win(), 10, out _, out _);
            storage.Reject = true;
            Assert.That(engine.TryPurchase("purchase.1", "car.reward", "", 11, out _, out _), Is.False);
            Assert.That(engine.Balance, Is.EqualTo(1150)); Assert.That(engine.Snapshot().store.ownedVehicleIds, Is.Empty);
            storage.Reject = false;
            Assert.That(engine.TryPurchase("purchase.1", "car.reward", "", 11, out _, out string error), Is.True, error);
            Assert.That(engine.Balance, Is.EqualTo(650));
            Assert.That(engine.Snapshot().FindVehicle("car.reward").performanceUpgradeIds, Is.EqualTo(new[] { "part.engine" }));
            Assert.That(engine.Snapshot().FindVehicle("car.reward").customizationIds, Is.EqualTo(new[] { "visual.paint" }));
            Assert.That(engine.TryPurchase("purchase.1", "car.reward", "", 50, out _, out _), Is.True);
            Assert.That(engine.TryPurchase("purchase.2", "car.reward", "", 50, out _, out _), Is.False);
            Assert.That(engine.Balance, Is.EqualTo(650));
        }

        [TestCase(0, 0)]
        [TestCase(100, 100)]
        [TestCase(150, 100)]
        [TestCase(1000, 500)]
        public void BustFineRespectsFloorAndPercentage(int cash, int expected)
        {
            var engine = new EconomySettlement(Profile(cash), Definition(), new Storage()); Begin(engine, policy: "pursuit");
            Assert.That(engine.TrySettle("attempt.1", new EconomyActivityOutcome { outcome = EconomyOutcomeKind.Busted,
                pendingBounty = 5000, heat = 10, infractions = 10 }, 10, out var receipt, out _), Is.True);
            Assert.That(engine.Balance, Is.EqualTo(expected)); Assert.That(receipt.newBounty, Is.Zero);
        }

        [Test]
        public void EscapeBanksBountyButNeverCash()
        {
            var engine = new EconomySettlement(Profile(), Definition(), new Storage()); Begin(engine, policy: "pursuit");
            Assert.That(engine.TrySettle("attempt.1", new EconomyActivityOutcome { outcome = EconomyOutcomeKind.Escaped,
                pendingBounty = 5000 }, 10, out var receipt, out _), Is.True);
            Assert.That(receipt.newBounty, Is.EqualTo(5000)); Assert.That(engine.Balance, Is.EqualTo(1000));
        }

        [Test]
        public void OverflowRejectsWholeSettlement()
        {
            var engine = new EconomySettlement(Profile(int.MaxValue), Definition(), new Storage()); Begin(engine);
            Assert.That(engine.TrySettle("attempt.1", Win(), 10, out _, out _), Is.False);
            Assert.That(engine.Balance, Is.EqualTo(int.MaxValue)); Assert.That(engine.Reputation, Is.Zero);
            Assert.That(engine.IsUnlocked("car.reward"), Is.False);
        }

        [Test]
        public void CallerCannotMutateAuthoritativeStateThroughReceiptOrSnapshot()
        {
            var engine = new EconomySettlement(Profile(), Definition(), new Storage()); Begin(engine);
            engine.TrySettle("attempt.1", Win(), 10, out var receipt, out _);
            receipt.newCash = 0; receipt.lines.Clear(); engine.Snapshot().wallet.balance = 0;
            Assert.That(engine.Balance, Is.EqualTo(1150));
            engine.TrySettle("attempt.1", Win(), 10, out receipt, out _);
            Assert.That(receipt.newCash, Is.EqualTo(1150)); Assert.That(receipt.lines, Is.Not.Empty);
        }

        [Test]
        public void InvalidCatalogAndOutOfBandWalletChangesAreRejected()
        {
            var config = Definition(); config.items[0].price = -1;
            Assert.Throws<ArgumentException>(() => new EconomySettlement(Profile(), config, new Storage()));
            var engine = new EconomySettlement(Profile(), Definition(), new Storage()); Begin(engine);
            var snapshot = engine.Snapshot(); snapshot.wallet.balance++;
            Assert.Throws<ArgumentException>(() => new EconomySettlement(snapshot, Definition(), new Storage()));
        }

        [Test]
        public void ReputationAndBountyMustReconcileAfterReload()
        {
            var engine = new EconomySettlement(Profile(), Definition(), new Storage()); Begin(engine);
            engine.TrySettle("attempt.1", Win(), 10, out _, out _);
            var snapshot = engine.Snapshot(); snapshot.economy.reputation++;
            Assert.Throws<ArgumentException>(() => new EconomySettlement(snapshot, Definition(), new Storage()));
            snapshot = engine.Snapshot(); snapshot.bounty.totalBounty++;
            Assert.Throws<ArgumentException>(() => new EconomySettlement(snapshot, Definition(), new Storage()));
        }

        [Test]
        public void LegacyMigrationPreservesBalanceWithoutRetroactiveBonuses()
        {
            var profile = JsonUtility.FromJson<CareerProfileData>("{\"saveVersion\":1,\"profileId\":\"test\",\"activeVehicleId\":\"player_vehicle\",\"wallet\":{\"balance\":1000},\"freeRoam\":{\"completedEventIds\":[\"event.a\"]}}");
            profile.Normalize(); profile.GetOrCreateVehicle("player_vehicle");
            var engine = new EconomySettlement(profile, Definition(), new Storage()); Begin(engine);
            Assert.That(engine.TrySettle("attempt.1", Win(), 10, out _, out _), Is.True);
            Assert.That(engine.Balance, Is.EqualTo(1050)); Assert.That(engine.Reputation, Is.Zero);
            Assert.That(engine.IsUnlocked("car.reward"), Is.False);
            Assert.That(engine.Snapshot().saveVersion, Is.EqualTo(CareerProfileData.CurrentVersion));
        }

        [Test]
        public void PurchaseCannotDebitWhenInsufficientOrIncompatible()
        {
            var config = Definition(); config.items[0].initiallyUnlocked = true;
            var engine = new EconomySettlement(Profile(100), config, new Storage());
            Assert.That(engine.TryPurchase("buy", "car.reward", "", 10, out _, out _), Is.False);
            Assert.That(engine.Balance, Is.EqualTo(100));
            config.items[1].initiallyUnlocked = true;
            config.items[1].compatibleVehicleIds = new[] { "car.reward" };
            engine = new EconomySettlement(Profile(), config, new Storage());
            Assert.That(engine.TryPurchase("buy", "part.engine", "player_vehicle", 10, out _, out _), Is.False);
            Assert.That(engine.Balance, Is.EqualTo(1000));
        }

        [Test]
        public void ConfiguredGrantDoesNotNeedPurchaseOrInstallationCallbacks()
        {
            var config = Definition(); config.policies[0].firstWinGrants[0].ownership = true;
            var engine = new EconomySettlement(Profile(), config, new Storage()); Begin(engine);
            Assert.That(engine.TrySettle("attempt.1", Win(), 10, out var receipt, out _), Is.True);
            Assert.That(engine.Snapshot().store.ownedVehicleIds, Does.Contain("car.reward"));
            Assert.That(receipt.lines.Exists(x => x.kind == EconomyLineKind.VehicleGrant && x.itemId == "car.reward"), Is.True);
            Assert.That(engine.IsUnlocked("car.reward"), Is.False, "Direct ownership is distinct from dealership availability.");
        }

        [Test]
        public void SeededSimulationUsesRealLedgerAndReconcilesIncomeSpending()
        {
            var scenario = new EconomySimulationScenario { racePolicyId = "race", pursuitPolicyId = "pursuit",
                eventIds = new[] { "event.a", "event.b", "event.c" }, desiredVehicleIds = new[] { "car.reward" },
                startingCash = 1000, seed = 42 };
            var first = EconomySimulation.Run(Definition(), scenario);
            var second = EconomySimulation.Run(Definition(), scenario);
            Assert.That(first.WalletTimeline, Is.EqualTo(second.WalletTimeline));
            Assert.That(first.FinalCash, Is.EqualTo(scenario.startingCash + first.Income - first.Spending));
            Assert.That(first.Attempts, Is.EqualTo(second.Attempts));
        }

        [Test]
        public void SimulationReportsUnwinnableScenarioWithoutInfiniteLoop()
        {
            var scenario = new EconomySimulationScenario { racePolicyId = "race", pursuitPolicyId = "pursuit",
                eventIds = new[] { "event.a" }, winBasisPoints = 0, pursuitBasisPoints = 0, attemptsPerEvent = 3 };
            var result = EconomySimulation.Run(Definition(), scenario);
            Assert.That(result.AttemptBudgetExhausted, Is.True); Assert.That(result.Attempts, Is.EqualTo(3));
            Assert.That(result.FinalCash, Is.Zero);
        }

        [Test]
        public void RealFileStorageReloadRetainsReceiptAndDoesNotPayAgain()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nfs-economy-test-" + Guid.NewGuid().ToString("N"));
            var root = new GameObject("Economy file test");
            try
            {
                var storage = root.AddComponent<JsonCareerProfileStorage>(); storage.SetDirectory(path);
                var engine = new EconomySettlement(Profile(), Definition(), storage); Begin(engine);
                Assert.That(engine.TrySettle("attempt.1", Win(), 10, out var receipt, out string error), Is.True, error);
                Assert.That(storage.TryLoad("test", out string data, out error), Is.True, error);
                var resumed = new EconomySettlement(JsonUtility.FromJson<CareerProfileData>(data), Definition(), storage);
                Assert.That(resumed.TrySettle("attempt.1", Win(), 20, out var replay, out error), Is.True, error);
                Assert.That(JsonUtility.ToJson(replay), Is.EqualTo(JsonUtility.ToJson(receipt)));
                Assert.That(resumed.Balance, Is.EqualTo(1150));
                Assert.That(System.IO.Directory.GetFiles(path, "*.pending-*"), Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if (System.IO.Directory.Exists(path)) System.IO.Directory.Delete(path, true);
            }
        }
    }
}
