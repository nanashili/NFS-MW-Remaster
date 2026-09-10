using System;
using Newtonsoft.Json;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public sealed partial class EconomySettlement
    {
        internal static bool StagePolice(CareerProfileData candidate, PoliceOutcome outcome)
        {
            outcome.Validate();
            string id = "police:" + outcome.encounterId;
            string fingerprint = JsonConvert.SerializeObject(outcome);
            var existing = candidate.police.settlements.Find(x => x.outcome.encounterId == outcome.encounterId);
            if (existing != null)
            {
                if (JsonConvert.SerializeObject(existing.outcome) != fingerprint)
                    throw new ArgumentException("Police encounter identity reused with a different outcome.");
                return false;
            }
            if (candidate.economy.activities.Exists(x => x.registration.activityId == id))
                throw new ArgumentException("Police encounter ID collides with an activity.");
            var economy = candidate.economy;
            int oldCash = candidate.wallet.balance, oldBounty = candidate.bounty.totalBounty;
            long oldRep = economy.reputation;
            bool escape = outcome.kind == PoliceOutcomeKind.Escaped;
            // Provisional insufficient-funds policy: arrest collects available funds; voluntary payment requires the whole fine.
            if (outcome.kind == PoliceOutcomeKind.PaidFine && oldCash < outcome.assessedFine)
                throw new ArgumentException("Insufficient funds to pay the offered fine.");
            int paid = escape ? 0 : Math.Min(oldCash, outcome.assessedFine);
            candidate.wallet.balance = oldCash - paid;
            economy.reputation = checked(oldRep + outcome.reputation);
            candidate.bounty.totalBounty = checked(oldBounty + outcome.historicalBounty);
            if (escape) candidate.bounty.pursuitsEscaped = checked(candidate.bounty.pursuitsEscaped + 1);
            if (outcome.kind == PoliceOutcomeKind.Arrested) candidate.bounty.pursuitsBusted = checked(candidate.bounty.pursuitsBusted + 1);
            candidate.bounty.pursuitActive = false; candidate.bounty.heatLevel = 0; candidate.bounty.Normalize();
            long sequence = checked(economy.revision + 1);
            var receipt = new EconomyReceiptData { transactionId = id, fingerprint = fingerprint, sourceId = outcome.encounterId,
                profileId = candidate.profileId, operation = "Police", vehicleId = candidate.activeVehicleId,
                sequence = sequence, utcSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                outcome = escape ? EconomyOutcomeKind.Escaped : outcome.kind == PoliceOutcomeKind.Arrested ? EconomyOutcomeKind.Busted : EconomyOutcomeKind.PaidFine,
                previousCash = oldCash, newCash = candidate.wallet.balance, previousReputation = oldRep, newReputation = economy.reputation,
                previousBounty = oldBounty, newBounty = candidate.bounty.totalBounty };
            receipt.lines.Add(new EconomyLine { kind = EconomyLineKind.Fine, amount = -paid, reason = escape ? "Fine cancelled on escape" : "Police fine collected" });
            receipt.lines.Add(new EconomyLine { kind = EconomyLineKind.Reputation, amount = outcome.reputation, reason = "Escape REP (provisional policy)" });
            receipt.lines.Add(new EconomyLine { kind = EconomyLineKind.Bounty, amount = outcome.historicalBounty, reason = "Historical career bounty; not cash or pursuit authority" });
            candidate.police.settlements.Add(new PoliceSettlementRecord { outcome = outcome.Copy(), cashPaid = paid });
            candidate.police.pendingWorldOutcomeId = outcome.encounterId;
            // Do not activate the exclusive journal writer in scenes still using legacy shop wallet ownership.
            if (economy.initialized) { economy.receipts.Add(receipt); economy.revision = sequence; }
            return true;
        }
    }

    public sealed partial class CareerProfileSystem : IPoliceOutcomeSettlement
    {
        private bool policeSettlementInProgress;
        public bool TrySettlePolice(PoliceOutcome outcome, out string failure)
        {
            failure = "Police settlement unavailable during saving/restoring.";
            if (outcome == null || SaveInProgress || restoreFaulted || policeSettlementInProgress) return false;
            policeSettlementInProgress = true;
            try
            {
                ResolveStorage(); ResolveParticipants();
                var wallet = GetComponent<VehicleStoreWallet>();
                var bounty = GetComponent<VehicleBountySystem>();
                if (storage == null || wallet == null || bounty == null || !ValidateProfileIdentity(out failure))
                { failure = "Police settlement requires profile storage, wallet and historical bounty adapter."; return false; }
                var candidate = currentProfile == null ? CareerProfileData.Create(profileId, playerName)
                    : JsonUtility.FromJson<CareerProfileData>(JsonUtility.ToJson(currentProfile));
                CaptureParticipants(candidate, out failure); if (!string.IsNullOrEmpty(failure)) return false;
                if (!EconomySettlement.StagePolice(candidate, outcome)) { failure = ""; return true; }
                string json = JsonUtility.ToJson(candidate, true); CareerSaveCodec.Validate(profileId, json);
                try { if (!storage.TrySave(profileId, json, out failure)) return false; }
                catch (Exception exception)
                {
                    bool committed = false;
                    try { committed = storage.TryLoad(profileId, out string observed, out _) && observed == json; } catch { }
                    if (!committed)
                    {
                        restoreFaulted = true;
                        failure = "Police commit outcome uncertain; reload before further saves: " + exception.Message;
                        return false;
                    }
                }
                currentProfile = candidate;
                // Publish live state only after the complete wallet/REP/history receipt is durable.
                if (!wallet.Restore(candidate, out failure) || !bounty.Restore(candidate, out failure))
                { restoreFaulted = true; return false; }
                autosave.AcknowledgeCurrent(); SavedSnapshot(json); Notify(ProfileSaved, currentProfile);
                failure = ""; return true;
            }
            catch (Exception exception) { failure = "Police settlement not acknowledged: " + exception.Message; return false; }
            finally { policeSettlementInProgress = false; }
        }
    }
}
