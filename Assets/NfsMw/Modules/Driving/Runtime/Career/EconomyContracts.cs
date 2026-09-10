using System;
using System.Collections.Generic;

namespace NfsMwRemaster.Driving
{
    public enum EconomyActivityKind { Race, Pursuit }
    public enum EconomyOutcomeKind { Finished, Escaped, Busted, Abandoned, Restarted, Invalid, PaidFine }
    public enum EconomyItemKind { Vehicle, Upgrade, Customization, District, Event, Safehouse }
    public enum EconomyLineKind { Cash, Reputation, Bounty, Unlock, VehicleGrant, ProductGrant, Fine, Forfeiture }

    [Serializable]
    public sealed class EconomyGrant
    {
        public EconomyItemKind kind;
        public string itemId = string.Empty;
        public bool ownership;
    }

    [Serializable]
    public sealed class EconomyItemDefinition
    {
        public string id = string.Empty;
        public string name = string.Empty;
        public EconomyItemKind kind;
        public int price;
        public bool initiallyUnlocked;
        public string[] compatibleVehicleIds = Array.Empty<string>();
        public string[] performanceIds = Array.Empty<string>();
        public string[] customizationIds = Array.Empty<string>();
    }

    [Serializable]
    public sealed class EconomyActivityPolicy
    {
        public string id = string.Empty;
        public int version = 1;
        public EconomyActivityKind kind;
        public int[] positionCash = new[] { 1500 };
        public int[] difficultyBasisPoints = new[] { 10000, 10000, 10000 };
        public int repeatBasisPoints = 10000;
        public int firstWinCash;
        public int firstWinReputation;
        public int cleanWinCash;
        public int targetWinCash;
        public long targetMilliseconds;
        public int finePerInfraction;
        public int finePerHeat;
        public int maximumFine;
        public int fineWalletBasisPoints = 10000;
        public int walletFloor;
        public EconomyGrant[] firstWinGrants = Array.Empty<EconomyGrant>();
    }

    [Serializable]
    public sealed class EconomyDefinition
    {
        public EconomyActivityPolicy[] policies = Array.Empty<EconomyActivityPolicy>();
        public EconomyItemDefinition[] items = Array.Empty<EconomyItemDefinition>();
    }

    [Serializable]
    public sealed class EconomyActivityRegistration
    {
        public string activityId = string.Empty;
        public string eventId = string.Empty;
        public string vehicleId = string.Empty;
        public string policyId = string.Empty;
        public int difficulty;
        public long startedUtcSeconds;
    }

    [Serializable]
    public sealed class EconomyActivityOutcome
    {
        public EconomyOutcomeKind outcome;
        public int position;
        public long elapsedMilliseconds;
        public bool clean;
        public int pendingBounty;
        public int heat;
        public int infractions;
    }

    [Serializable]
    public sealed class EconomyLine
    {
        public EconomyLineKind kind;
        public EconomyItemKind itemKind;
        public string itemId = string.Empty;
        public string reason = string.Empty;
        public long amount;
    }

    [Serializable]
    public sealed class EconomyReceiptData
    {
        public string transactionId = string.Empty;
        public string fingerprint = string.Empty;
        public string sourceId = string.Empty;
        public string profileId = string.Empty;
        public string operation = string.Empty;
        public string vehicleId = string.Empty;
        public EconomyOutcomeKind outcome;
        public long sequence;
        public long utcSeconds;
        public long previousCash, newCash, previousReputation, newReputation, previousBounty, newBounty;
        public List<EconomyLine> lines = new List<EconomyLine>();
    }

    [Serializable]
    public sealed class EconomyPendingActivity
    {
        public EconomyActivityRegistration registration;
        public EconomyActivityPolicy policy;
    }

    [Serializable]
    public sealed class CareerEconomyData
    {
        public int version = 1;
        public long revision;
        public long reputation;
        public int openingCash;
        public long openingReputation;
        public int openingBounty;
        public bool initialized;
        public List<string> firstWinEventIds = new List<string>();
        public List<EconomyGrant> unlocks = new List<EconomyGrant>();
        public List<EconomyPendingActivity> activities = new List<EconomyPendingActivity>();
        public List<EconomyReceiptData> receipts = new List<EconomyReceiptData>();
    }
}
