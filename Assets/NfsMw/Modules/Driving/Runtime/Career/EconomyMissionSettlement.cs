using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace NfsMwRemaster.Driving
{
    public sealed partial class EconomySettlement
    {
        // Stages one aggregate transaction; caller must persist before exposing live reward state.
        // Legacy wallet mode keeps its existing ownership; initialized journal mode adds a normal receipt.
        internal static bool StageMission(CareerProfileData candidate, MissionSnapshot mission, EconomyDefinition catalog, ICareerProfileStorage storage)
        {
            MissionRuntime.ValidateSnapshot(mission);
            if (mission.state != MissionState.Succeeded || mission.result == null) throw new ArgumentException("Only a frozen successful mission can settle.");
            string fingerprint = JsonConvert.SerializeObject(mission.result);
            var existing = candidate.missions.claims.Find(x => x.claimId == mission.claimId);
            if (existing != null)
            {
                if (existing.fingerprint != fingerprint || existing.missionId != mission.missionId) throw new ArgumentException("Mission claim identity reused with a different outcome.");
                return false;
            }
            int oldCash = candidate.wallet.balance;
            long oldReputation = candidate.economy.reputation;
            EconomySettlement grantValidator = null;
            if (mission.result.grants.Count > 0)
            {
                if (catalog == null) throw new ArgumentException("Mission grants require a validated economy catalog.");
                grantValidator = new EconomySettlement(candidate, catalog, storage);
            }
            var grantReceipt = new EconomyReceiptData();
            foreach (var grant in mission.result.grants) grantValidator.Grant(candidate, grantReceipt, grant);
            candidate.wallet.balance = checked(oldCash + mission.result.cash);
            candidate.economy.reputation = checked(oldReputation + mission.result.reputation);
            candidate.missions.claims.Add(new MissionClaim { claimId = mission.claimId, missionId = mission.missionId, fingerprint = fingerprint, cash = mission.result.cash });
            MissionPersistence.Upsert(candidate.missions, mission);
            if (!candidate.freeRoam.completedEventIds.Contains(mission.missionId)) candidate.freeRoam.completedEventIds.Add(mission.missionId);
            if (candidate.economy.initialized)
            {
                long revision = checked(candidate.economy.revision + 1);
                grantReceipt.lines.Insert(0, new EconomyLine { kind = EconomyLineKind.Cash, amount = mission.result.cash, reason = "Mission settlement" });
                grantReceipt.lines.Add(new EconomyLine { kind = EconomyLineKind.Reputation, amount = mission.result.reputation, reason = "Mission reputation" });
                candidate.economy.receipts.Add(new EconomyReceiptData { transactionId = "mission:" + mission.claimId,
                    fingerprint = fingerprint, sourceId = mission.missionId, profileId = candidate.profileId, operation = "Mission",
                    sequence = revision, utcSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), outcome = EconomyOutcomeKind.Finished,
                    previousCash = oldCash, newCash = candidate.wallet.balance,
                    previousReputation = oldReputation, newReputation = candidate.economy.reputation,
                    previousBounty = candidate.bounty.totalBounty, newBounty = candidate.bounty.totalBounty,
                    lines = grantReceipt.lines });
                candidate.economy.revision = revision;
            }
            return true;
        }
    }
}
