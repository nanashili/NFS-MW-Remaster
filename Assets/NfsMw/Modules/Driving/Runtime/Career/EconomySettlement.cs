using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Exclusive profile writer for activity settlement and catalog purchases.
    /// Inputs/outputs are copied. Storage must publish snapshots atomically and
    /// return false only when no commit occurred. Never run alongside legacy
    /// wallet/profile mutators for the same slot. Unity main-thread use only.
    /// </summary>
    public sealed partial class EconomySettlement
    {
        private CareerProfileData profile;
        private readonly ICareerProfileStorage storage;
        private readonly Dictionary<string, EconomyActivityPolicy> policies = new Dictionary<string, EconomyActivityPolicy>(StringComparer.Ordinal);
        private readonly Dictionary<string, EconomyItemDefinition> items = new Dictionary<string, EconomyItemDefinition>(StringComparer.Ordinal);
        private bool busy;
        private bool uncertain;
        public long Balance => profile.wallet.balance;
        public long Reputation => profile.economy.reputation;
        public bool RequiresReload => uncertain;
        public CareerProfileData Snapshot() => Copy(profile);

        public EconomySettlement(CareerProfileData initial, EconomyDefinition definition, ICareerProfileStorage storage)
        {
            this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
            if (initial == null || definition == null) throw new ArgumentNullException();
            profile = Copy(initial);
            if (profile.wallet == null || profile.wallet.balance < 0) throw new ArgumentException("Invalid wallet.");
            if (!profile.Validate(initial.profileId, out string failure)) throw new ArgumentException(failure);
            var config = Copy(definition);
            if (config.items == null || config.policies == null) throw new ArgumentException("Null economy catalog.");
            foreach (var item in config.items)
            {
                if (item == null) throw new ArgumentException("Null economy item.");
                Id(item.id); if (items.ContainsKey(item.id)) throw new ArgumentException("Duplicate economy item: " + item.id);
                if (!Enum.IsDefined(typeof(EconomyItemKind), item.kind) || item.price < 0 || string.IsNullOrWhiteSpace(item.name)
                    || item.compatibleVehicleIds == null || item.performanceIds == null || item.customizationIds == null)
                    throw new ArgumentException("Invalid item: " + item.id);
                items.Add(item.id, item);
            }
            foreach (var item in items.Values)
            {
                foreach (string id in item.compatibleVehicleIds) RequireItem(id, EconomyItemKind.Vehicle);
                foreach (string id in item.performanceIds) RequireItem(id, EconomyItemKind.Upgrade);
                foreach (string id in item.customizationIds) RequireItem(id, EconomyItemKind.Customization);
                if (item.kind != EconomyItemKind.Vehicle && (item.performanceIds.Length != 0 || item.customizationIds.Length != 0))
                    throw new ArgumentException("Only vehicles can contain installed configurations.");
            }
            foreach (var policy in config.policies)
            {
                ValidatePolicy(policy);
                if (policies.ContainsKey(policy.id)) throw new ArgumentException("Duplicate policy: " + policy.id);
                policies.Add(policy.id, policy);
            }
            ValidateState();
            if (!profile.economy.initialized)
            {
                profile.economy.initialized = true;
                profile.economy.openingCash = profile.wallet.balance;
                profile.economy.openingReputation = profile.economy.reputation;
                profile.economy.openingBounty = profile.bounty.totalBounty;
                // Historical completions suppress retroactive first-win bonuses,
                // but grant neither reputation nor new career evidence.
                profile.economy.firstWinEventIds = new List<string>(profile.freeRoam.completedEventIds);
            }
        }

        public bool IsUnlocked(string itemId)
        {
            var item = GetItem(itemId);
            return item.initiallyUnlocked || profile.economy.unlocks.Exists(x => x.kind == item.kind && x.itemId == itemId);
        }

        public int Price(string itemId) => GetItem(itemId).price;

        public bool TryBegin(EconomyActivityRegistration registration, out string failure)
        {
            failure = string.Empty;
            if (!Enter(out failure)) return false;
            try
            {
                if (registration == null) return Fail("Missing activity registration.", out failure);
                var input = Copy(registration);
                Id(input.activityId); Id(input.eventId); Id(input.vehicleId); Id(input.policyId);
                if (!policies.TryGetValue(input.policyId, out var policy)) return Fail("Unknown reward policy.", out failure);
                if (input.difficulty < 0 || input.difficulty >= policy.difficultyBasisPoints.Length || input.startedUtcSeconds < 0)
                    return Fail("Invalid start difficulty or timestamp.", out failure);
                if (profile.FindVehicle(input.vehicleId) == null) return Fail("Activity vehicle is not in the profile.", out failure);
                var previous = profile.economy.activities.Find(x => x.registration.activityId == input.activityId);
                if (previous != null)
                    return JsonUtility.ToJson(previous.registration) == JsonUtility.ToJson(input)
                        || Fail("Activity ID reused with different start data.", out failure);
                if (profile.economy.receipts.Exists(x => x.transactionId == input.activityId)) return Fail("Transaction ID already used.", out failure);
                var candidate = Copy(profile);
                candidate.economy.activities.Add(new EconomyPendingActivity { registration = input, policy = Copy(policy) });
                return Commit(candidate, out failure);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is OverflowException)
            { return Fail(exception.Message, out failure); }
            finally { busy = false; }
        }

        public bool TrySettle(string activityId, EconomyActivityOutcome outcome, long utcSeconds,
            out EconomyReceiptData receipt, out string failure)
        {
            receipt = null; failure = string.Empty;
            if (!Enter(out failure)) return false;
            try
            {
                Id(activityId);
                if (outcome == null) return Fail("Missing activity outcome.", out failure);
                var input = Copy(outcome);
                string fingerprint = "activity:" + JsonUtility.ToJson(input);
                var existing = profile.economy.receipts.Find(x => x.transactionId == activityId);
                if (existing != null) return Replay(existing, fingerprint, out receipt, out failure);
                var attempt = profile.economy.activities.Find(x => x.registration.activityId == activityId);
                if (attempt == null) return Fail("Activity was not registered before play.", out failure);
                ValidateOutcome(attempt, input, utcSeconds);
                var candidate = Copy(profile);
                var planned = Receipt(activityId, fingerprint, attempt.registration.eventId, utcSeconds);
                planned.operation = attempt.policy.kind.ToString();
                planned.vehicleId = attempt.registration.vehicleId;
                planned.outcome = input.outcome;
                var policy = attempt.policy;
                if (policy.kind == EconomyActivityKind.Pursuit)
                {
                    candidate.bounty.pursuitActive = false;
                    candidate.bounty.heatLevel = input.outcome == EconomyOutcomeKind.Busted ? 0 : input.heat;
                    candidate.bounty.Normalize();
                }
                if (input.outcome == EconomyOutcomeKind.Finished)
                {
                    bool firstWin = input.position == 1 && !candidate.economy.firstWinEventIds.Contains(attempt.registration.eventId);
                    long cash = policy.positionCash[input.position - 1];
                    if (!firstWin) cash = Scale(cash, policy.repeatBasisPoints);
                    cash = Scale(cash, policy.difficultyBasisPoints[attempt.registration.difficulty]);
                    Add(planned, EconomyLineKind.Cash, cash, firstWin ? "Race payout" : "Repeat/placement payout");
                    if (input.position == 1)
                    {
                        if (firstWin)
                        {
                            Add(planned, EconomyLineKind.Cash, policy.firstWinCash, "First victory bonus");
                            Add(planned, EconomyLineKind.Reputation, policy.firstWinReputation, "First victory reputation");
                            foreach (var grant in policy.firstWinGrants) Grant(candidate, planned, grant);
                            candidate.economy.firstWinEventIds.Add(attempt.registration.eventId);
                        }
                        if (input.clean) Add(planned, EconomyLineKind.Cash, policy.cleanWinCash, "Clean victory");
                        if (policy.targetMilliseconds > 0 && input.elapsedMilliseconds <= policy.targetMilliseconds)
                            Add(planned, EconomyLineKind.Cash, policy.targetWinCash, "Target time beaten");
                        if (!candidate.freeRoam.completedEventIds.Contains(attempt.registration.eventId)) candidate.freeRoam.completedEventIds.Add(attempt.registration.eventId);
                        candidate.statistics.racesWon = checked(candidate.statistics.racesWon + 1);
                    }
                    else candidate.statistics.racesLost = checked(candidate.statistics.racesLost + 1);
                    candidate.statistics.eventsCompleted = checked(candidate.statistics.eventsCompleted + 1);
                }
                else if (input.outcome == EconomyOutcomeKind.Escaped)
                {
                    Add(planned, EconomyLineKind.Bounty, input.pendingBounty, "Pursuit bounty secured");
                    candidate.bounty.pursuitsEscaped = checked(candidate.bounty.pursuitsEscaped + 1);
                }
                else if (input.outcome == EconomyOutcomeKind.Busted)
                {
                    long assessed = checked((long)input.infractions * policy.finePerInfraction + (long)input.heat * policy.finePerHeat);
                    long payable = Math.Min(Math.Min(assessed, policy.maximumFine), Math.Min(Scale(Balance, policy.fineWalletBasisPoints), Math.Max(0, Balance - policy.walletFloor)));
                    Add(planned, EconomyLineKind.Fine, -payable, "Police fine (capped)");
                    Add(planned, EconomyLineKind.Forfeiture, input.pendingBounty, "Unsecured bounty forfeited");
                    candidate.bounty.pursuitsBusted = checked(candidate.bounty.pursuitsBusted + 1);
                }
                else if (policy.kind == EconomyActivityKind.Pursuit)
                    Add(planned, EconomyLineKind.Forfeiture, input.pendingBounty, "Unsecured bounty discarded: " + input.outcome);
                return Finalize(candidate, planned, out receipt, out failure);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is OverflowException)
            { return Fail(exception.Message, out failure); }
            finally { busy = false; }
        }

        public bool TryPurchase(string transactionId, string itemId, string vehicleId, long utcSeconds,
            out EconomyReceiptData receipt, out string failure)
        {
            receipt = null; failure = string.Empty;
            if (!Enter(out failure)) return false;
            try
            {
                Id(transactionId); Id(itemId); if (!string.IsNullOrEmpty(vehicleId)) Id(vehicleId);
                string fingerprint = "purchase:" + itemId + ":" + (vehicleId ?? string.Empty);
                var previous = profile.economy.receipts.Find(x => x.transactionId == transactionId);
                if (previous != null) return Replay(previous, fingerprint, out receipt, out failure);
                if (profile.economy.activities.Exists(x => x.registration.activityId == transactionId)) return Fail("Transaction ID belongs to an activity.", out failure);
                var item = GetItem(itemId);
                if (utcSeconds < 0) return Fail("Invalid timestamp.", out failure);
                if (!IsUnlocked(itemId)) return Fail("Item is locked.", out failure);
                if (Balance < item.price) return Fail("Insufficient funds.", out failure);
                if (item.kind != EconomyItemKind.Vehicle && item.kind != EconomyItemKind.Upgrade && item.kind != EconomyItemKind.Customization)
                    return Fail("This content is not purchasable.", out failure);
                bool car = item.kind == EconomyItemKind.Vehicle;
                if (car && !string.IsNullOrEmpty(vehicleId)) return Fail("Vehicle purchases must not target another vehicle.", out failure);
                if (!car && (profile.FindVehicle(vehicleId) == null || item.compatibleVehicleIds.Length > 0 && Array.IndexOf(item.compatibleVehicleIds, vehicleId) < 0))
                    return Fail("Item is not compatible with the target vehicle.", out failure);
                if (car ? profile.store.ownedVehicleIds.Contains(itemId) || profile.FindVehicle(itemId) != null : profile.store.ownedProductIds.Contains(itemId))
                    return Fail("Item is already owned.", out failure);
                var candidate = Copy(profile);
                var planned = Receipt(transactionId, fingerprint, itemId, utcSeconds);
                planned.operation = "Purchase"; planned.vehicleId = vehicleId ?? string.Empty;
                Add(planned, EconomyLineKind.Cash, -item.price, "Purchase: " + item.name);
                Grant(candidate, planned, new EconomyGrant { kind = item.kind, itemId = itemId, ownership = true });
                return Finalize(candidate, planned, out receipt, out failure);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is OverflowException)
            { return Fail(exception.Message, out failure); }
            finally { busy = false; }
        }

        private bool Finalize(CareerProfileData candidate, EconomyReceiptData planned, out EconomyReceiptData receipt, out string failure)
        {
            receipt = null;
            long cash = Balance, rep = Reputation, bounty = profile.bounty.totalBounty;
            foreach (var line in planned.lines)
            {
                if (line.kind == EconomyLineKind.Cash || line.kind == EconomyLineKind.Fine) cash = checked(cash + line.amount);
                else if (line.kind == EconomyLineKind.Reputation) rep = checked(rep + line.amount);
                else if (line.kind == EconomyLineKind.Bounty) bounty = checked(bounty + line.amount);
            }
            if (cash < 0 || cash > int.MaxValue || rep < 0 || bounty < 0 || bounty > int.MaxValue)
                return Fail("Settlement exceeds profile numeric limits.", out failure);
            candidate.wallet.balance = (int)cash; candidate.economy.reputation = rep; candidate.bounty.totalBounty = (int)bounty;
            planned.newCash = cash; planned.newReputation = rep; planned.newBounty = bounty;
            candidate.economy.receipts.Add(planned);
            if (!Commit(candidate, out failure)) return false;
            receipt = Copy(planned); return true;
        }

        private void Grant(CareerProfileData candidate, EconomyReceiptData receipt, EconomyGrant grant)
        {
            RequireItem(grant.itemId, grant.kind);
            var item = items[grant.itemId];
            if (!grant.ownership)
            {
                if (candidate.economy.unlocks.Exists(x => x.kind == grant.kind && x.itemId == grant.itemId) || item.initiallyUnlocked) return;
                candidate.economy.unlocks.Add(Copy(grant));
                receipt.lines.Add(new EconomyLine { kind = EconomyLineKind.Unlock, itemKind = grant.kind, itemId = grant.itemId, reason = "Unlocked: " + item.name });
            }
            else if (grant.kind == EconomyItemKind.Vehicle)
            {
                if (candidate.store.ownedVehicleIds.Contains(grant.itemId) || candidate.FindVehicle(grant.itemId) != null) return;
                candidate.store.ownedVehicleIds.Add(grant.itemId);
                candidate.vehicles.Add(new CareerVehicleData { vehicleId = grant.itemId,
                    performanceUpgradeIds = new List<string>(item.performanceIds), customizationIds = new List<string>(item.customizationIds) });
                receipt.lines.Add(new EconomyLine { kind = EconomyLineKind.VehicleGrant, itemKind = grant.kind, itemId = grant.itemId, reason = "Vehicle acquired: " + item.name });
            }
            else
            {
                if (candidate.store.ownedProductIds.Contains(grant.itemId)) return;
                candidate.store.ownedProductIds.Add(grant.itemId);
                receipt.lines.Add(new EconomyLine { kind = EconomyLineKind.ProductGrant, itemKind = grant.kind, itemId = grant.itemId, reason = "Item acquired (not installed): " + item.name });
            }
        }

        private EconomyReceiptData Receipt(string id, string fingerprint, string source, long time)
            => new EconomyReceiptData { transactionId = id, fingerprint = fingerprint, sourceId = source, profileId = profile.profileId,
                sequence = checked(profile.economy.revision + 1), utcSeconds = time, previousCash = Balance, previousReputation = Reputation,
                previousBounty = profile.bounty.totalBounty };

        private bool Commit(CareerProfileData candidate, out string failure)
        {
            candidate.economy.revision = checked(profile.economy.revision + 1);
            string serialized = JsonUtility.ToJson(candidate);
            try
            {
                if (!storage.TrySave(profile.profileId, serialized, out failure)) return false;
            }
            catch (Exception exception)
            {
                // An adapter may throw after publication. Reconcile the exact
                // candidate before allowing any further write or claiming failure.
                try
                {
                    if (storage.TryLoad(profile.profileId, out string observed, out _) && observed == serialized)
                    { profile = candidate; failure = string.Empty; return true; }
                }
                catch (Exception) { }
                uncertain = true;
                return Fail("Storage outcome uncertain; reload before retrying: " + exception.Message, out failure);
            }
            profile = candidate; failure = string.Empty; return true;
        }

        private bool Enter(out string failure)
        {
            if (busy || uncertain) return Fail(uncertain ? "Economy requires reload." : "Settlement is already in progress.", out failure);
            busy = true; failure = string.Empty; return true;
        }
        private static bool Replay(EconomyReceiptData existing, string fingerprint, out EconomyReceiptData receipt, out string failure)
        {
            receipt = null;
            if (existing.fingerprint != fingerprint) return Fail("Transaction ID reused with a different outcome.", out failure);
            receipt = Copy(existing); failure = string.Empty; return true;
        }
        private static void Add(EconomyReceiptData receipt, EconomyLineKind kind, long amount, string reason)
        { if (amount != 0) receipt.lines.Add(new EconomyLine { kind = kind, amount = amount, reason = reason }); }
        private static long Scale(long amount, int basisPoints) => checked(amount * basisPoints) / 10000;
        private static bool Fail(string message, out string failure) { failure = message; return false; }
        internal static T Copy<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        private static void Id(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 200) throw new ArgumentException("Invalid economy ID.");
            foreach (char c in id) if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_') throw new ArgumentException("Invalid economy ID: " + id);
        }
        private EconomyItemDefinition GetItem(string id)
        { if (id == null || !items.TryGetValue(id, out var item)) throw new ArgumentException("Unknown item: " + id); return item; }
        private void RequireItem(string id, EconomyItemKind kind)
        { if (GetItem(id).kind != kind) throw new ArgumentException("Wrong item kind: " + id); }

        private void ValidatePolicy(EconomyActivityPolicy policy)
        {
            if (policy == null) throw new ArgumentException("Null reward policy.");
            Id(policy.id);
            if (policy.version < 1 || !Enum.IsDefined(typeof(EconomyActivityKind), policy.kind)
                || policy.positionCash == null || policy.positionCash.Length == 0 || policy.positionCash.Length > 64
                || policy.difficultyBasisPoints == null || policy.difficultyBasisPoints.Length == 0 || policy.difficultyBasisPoints.Length > 16
                || policy.repeatBasisPoints < 0 || policy.repeatBasisPoints > 10000 || policy.firstWinCash < 0
                || policy.firstWinReputation < 0 || policy.cleanWinCash < 0 || policy.targetWinCash < 0 || policy.targetMilliseconds < 0
                || policy.finePerHeat < 0 || policy.finePerInfraction < 0 || policy.maximumFine < 0 || policy.walletFloor < 0
                || policy.fineWalletBasisPoints < 0 || policy.fineWalletBasisPoints > 10000 || policy.firstWinGrants == null)
                throw new ArgumentException("Invalid policy: " + policy.id);
            foreach (int cash in policy.positionCash) if (cash < 0) throw new ArgumentException("Negative position payout.");
            foreach (int modifier in policy.difficultyBasisPoints) if (modifier < 0 || modifier > 20000) throw new ArgumentException("Difficulty modifier outside 0..200%.");
            foreach (var grant in policy.firstWinGrants)
            {
                if (grant == null) throw new ArgumentException("Null grant.");
                RequireItem(grant.itemId, grant.kind);
                if (grant.ownership && grant.kind != EconomyItemKind.Vehicle && grant.kind != EconomyItemKind.Upgrade && grant.kind != EconomyItemKind.Customization)
                    throw new ArgumentException("This content supports unlock only.");
            }
        }

        private static void ValidateOutcome(EconomyPendingActivity attempt, EconomyActivityOutcome outcome, long utcSeconds)
        {
            if (!Enum.IsDefined(typeof(EconomyOutcomeKind), outcome.outcome) || outcome.elapsedMilliseconds < 0
                || outcome.pendingBounty < 0 || outcome.heat < 0 || outcome.infractions < 0 || utcSeconds < attempt.registration.startedUtcSeconds)
                throw new ArgumentException("Invalid activity outcome.");
            bool race = attempt.policy.kind == EconomyActivityKind.Race;
            if (race && (outcome.pendingBounty != 0 || outcome.heat != 0 || outcome.infractions != 0
                || outcome.outcome == EconomyOutcomeKind.Escaped || outcome.outcome == EconomyOutcomeKind.Busted))
                throw new ArgumentException("Race cannot report pursuit rewards.");
            if (!race && outcome.outcome == EconomyOutcomeKind.Finished) throw new ArgumentException("Pursuit needs an escape or bust outcome.");
            if (outcome.outcome == EconomyOutcomeKind.Finished && (outcome.position < 1 || outcome.position > attempt.policy.positionCash.Length || outcome.elapsedMilliseconds == 0))
                throw new ArgumentException("Invalid finishing position/time.");
        }

        private void ValidateState()
        {
            var state = profile.economy;
            if (state.version != 1 || state.revision < 0 || state.reputation < 0 || state.openingCash < 0
                || state.openingReputation < 0 || state.openingBounty < 0 || state.activities == null
                || state.receipts == null || state.unlocks == null || state.firstWinEventIds == null)
                throw new ArgumentException("Invalid economy state.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var attempt in state.activities)
            {
                if (attempt == null || attempt.registration == null) throw new ArgumentException("Invalid saved activity.");
                Id(attempt.registration.activityId);
                Id(attempt.registration.eventId); Id(attempt.registration.vehicleId);
                if (!ids.Add(attempt.registration.activityId)) throw new ArgumentException("Duplicate activity ID in save.");
                ValidatePolicy(attempt.policy);
                if (attempt.registration.policyId != attempt.policy.id || attempt.registration.startedUtcSeconds < 0
                    || attempt.registration.difficulty < 0 || attempt.registration.difficulty >= attempt.policy.difficultyBasisPoints.Length)
                    throw new ArgumentException("Invalid saved difficulty.");
            }
            ids.Clear();
            long previousSequence = 0, expectedCash = state.openingCash;
            long expectedRep = state.openingReputation, expectedBounty = state.openingBounty;
            foreach (var receipt in state.receipts)
            {
                if (receipt == null || receipt.lines == null || string.IsNullOrEmpty(receipt.fingerprint)) throw new ArgumentException("Invalid saved receipt.");
                Id(receipt.transactionId);
                if (!ids.Add(receipt.transactionId) || receipt.profileId != profile.profileId || receipt.sequence <= previousSequence
                    || receipt.sequence > state.revision || receipt.previousCash != expectedCash || receipt.newCash < 0 || receipt.newCash > int.MaxValue)
                    throw new ArgumentException("Broken economy ledger chain.");
                if (receipt.previousReputation != expectedRep || receipt.previousBounty != expectedBounty || receipt.utcSeconds < 0)
                    throw new ArgumentException("Broken progression ledger chain.");
                long net = 0, repDelta = 0, bountyDelta = 0;
                foreach (var line in receipt.lines)
                {
                    if (line == null || !Enum.IsDefined(typeof(EconomyLineKind), line.kind)) throw new ArgumentException("Invalid ledger line.");
                    if (line.kind == EconomyLineKind.Cash || line.kind == EconomyLineKind.Fine) net = checked(net + line.amount);
                    if (line.kind == EconomyLineKind.Fine && line.amount > 0) throw new ArgumentException("Fine must not credit cash.");
                    if (line.kind == EconomyLineKind.Reputation) repDelta = checked(repDelta + line.amount);
                    if (line.kind == EconomyLineKind.Bounty) bountyDelta = checked(bountyDelta + line.amount);
                    if (line.kind == EconomyLineKind.Unlock || line.kind == EconomyLineKind.VehicleGrant || line.kind == EconomyLineKind.ProductGrant)
                        RequireItem(line.itemId, line.itemKind);
                }
                if (checked(receipt.previousCash + net) != receipt.newCash) throw new ArgumentException("Ledger does not reconcile.");
                if (checked(expectedRep + repDelta) != receipt.newReputation || receipt.newReputation < 0
                    || checked(expectedBounty + bountyDelta) != receipt.newBounty || receipt.newBounty < 0 || receipt.newBounty > int.MaxValue)
                    throw new ArgumentException("Progression ledger does not reconcile.");
                previousSequence = receipt.sequence; expectedCash = receipt.newCash;
                expectedRep = receipt.newReputation; expectedBounty = receipt.newBounty;
            }
            if (state.initialized && expectedCash != profile.wallet.balance) throw new ArgumentException("Wallet changed outside settlement ledger.");
            if (state.initialized && (expectedRep != state.reputation || expectedBounty != profile.bounty.totalBounty))
                throw new ArgumentException("Progression changed outside settlement ledger.");
            if (!state.initialized && (state.receipts.Count != 0 || state.activities.Count != 0 || state.revision != 0))
                throw new ArgumentException("Uninitialized economy contains transactions.");
            foreach (var unlock in state.unlocks)
            {
                if (unlock == null || unlock.ownership) throw new ArgumentException("Invalid unlock record.");
                RequireItem(unlock.itemId, unlock.kind);
            }
        }
    }
}
