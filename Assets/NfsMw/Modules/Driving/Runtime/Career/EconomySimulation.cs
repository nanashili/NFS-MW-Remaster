using System;
using System.Collections.Generic;

namespace NfsMwRemaster.Driving
{
    [Serializable]
    public sealed class EconomySimulationScenario
    {
        public string name = "Scenario";
        public string racePolicyId = string.Empty;
        public string pursuitPolicyId = string.Empty;
        public string[] eventIds = Array.Empty<string>();
        public string[] desiredVehicleIds = Array.Empty<string>();
        public int startingCash;
        public int winBasisPoints = 7500;
        public int pursuitBasisPoints = 2500;
        public int bustBasisPoints = 2500;
        public int attemptsPerEvent = 20;
        public int pursuitBounty = 5000;
        public int pursuitHeat = 3;
        public int pursuitInfractions = 5;
        public int raceDurationSeconds = 180;
        public int pursuitDurationSeconds = 180;
        public int seed = 1;
    }

    public sealed class EconomySimulationReport
    {
        public int Attempts, Wins, Busts, Escapes;
        public long Income, Spending, Fines, FinalCash, ElapsedSeconds;
        public bool AttemptBudgetExhausted;
        public readonly List<long> WalletTimeline = new List<long>();
        public readonly List<string> UnaffordableOrLocked = new List<string>();
    }

    /// <summary>
    /// Bounded synthetic scenario using the real settlement engine. It does not
    /// simulate vehicle performance or claim to reconstruct Blacklist pacing.
    /// </summary>
    public static class EconomySimulation
    {
        private sealed class MemoryStorage : ICareerProfileStorage
        {
            private string value;
            public bool TrySave(string id, string data, out string failure) { value = data; failure = string.Empty; return true; }
            public bool TryLoad(string id, out string data, out string failure) { data = value; failure = string.Empty; return value != null; }
        }

        public static EconomySimulationReport Run(EconomyDefinition definition, EconomySimulationScenario input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var scenario = EconomySettlement.Copy(input);
            if (scenario.eventIds == null || scenario.desiredVehicleIds == null || scenario.eventIds.Length > 1000
                || scenario.attemptsPerEvent < 1 || scenario.attemptsPerEvent > 100
                || (long)scenario.eventIds.Length * scenario.attemptsPerEvent > 1000 || scenario.startingCash < 0
                || scenario.raceDurationSeconds < 1 || scenario.pursuitDurationSeconds < 1 || scenario.pursuitBounty < 0
                || scenario.pursuitHeat < 0 || scenario.pursuitInfractions < 0)
                throw new ArgumentException("Invalid simulation bounds.");
            foreach (int chance in new[] { scenario.winBasisPoints, scenario.pursuitBasisPoints, scenario.bustBasisPoints })
                if (chance < 0 || chance > 10000) throw new ArgumentException("Invalid simulation probability.");
            var profile = CareerProfileData.Create("simulation", scenario.name);
            profile.wallet.balance = scenario.startingCash; profile.GetOrCreateVehicle("player_vehicle");
            var economy = new EconomySettlement(profile, definition, new MemoryStorage());
            var report = new EconomySimulationReport();
            uint random = unchecked((uint)scenario.seed);
            if (random == 0) random = 1;
            int activity = 0;
            foreach (string eventId in scenario.eventIds)
            {
                bool won = false;
                for (int attempt = 0; attempt < scenario.attemptsPerEvent && !won; attempt++)
                {
                    report.Attempts++;
                    won = Chance(ref random, scenario.winBasisPoints);
                    Settle(economy, report, "sim." + ++activity, eventId, scenario.racePolicyId,
                        new EconomyActivityOutcome { outcome = won ? EconomyOutcomeKind.Finished : EconomyOutcomeKind.Abandoned,
                            position = won ? 1 : 0, elapsedMilliseconds = (long)scenario.raceDurationSeconds * 1000 }, scenario.raceDurationSeconds);
                    if (won) report.Wins++;
                    if (Chance(ref random, scenario.pursuitBasisPoints))
                    {
                        bool busted = Chance(ref random, scenario.bustBasisPoints);
                        if (busted) report.Busts++; else report.Escapes++;
                        Settle(economy, report, "sim." + ++activity, "pursuit", scenario.pursuitPolicyId,
                            new EconomyActivityOutcome { outcome = busted ? EconomyOutcomeKind.Busted : EconomyOutcomeKind.Escaped,
                                pendingBounty = scenario.pursuitBounty, heat = scenario.pursuitHeat, infractions = scenario.pursuitInfractions,
                                elapsedMilliseconds = (long)scenario.pursuitDurationSeconds * 1000 }, scenario.pursuitDurationSeconds);
                    }
                }
                if (!won) { report.AttemptBudgetExhausted = true; break; }
                foreach (string vehicleId in scenario.desiredVehicleIds)
                {
                    if (economy.Snapshot().store.ownedVehicleIds.Contains(vehicleId)) continue;
                    if (economy.TryPurchase("buy." + ++activity, vehicleId, "", report.ElapsedSeconds, out var receipt, out _)) Accumulate(report, receipt);
                }
            }
            foreach (string id in scenario.desiredVehicleIds)
                if (!economy.Snapshot().store.ownedVehicleIds.Contains(id)) report.UnaffordableOrLocked.Add(id);
            report.FinalCash = economy.Balance;
            return report;
        }

        private static void Settle(EconomySettlement economy, EconomySimulationReport report, string id, string eventId,
            string policy, EconomyActivityOutcome outcome, int seconds)
        {
            if (!economy.TryBegin(new EconomyActivityRegistration { activityId = id, eventId = eventId,
                policyId = policy, vehicleId = "player_vehicle", startedUtcSeconds = report.ElapsedSeconds }, out string failure))
                throw new ArgumentException(failure);
            report.ElapsedSeconds = checked(report.ElapsedSeconds + seconds);
            if (!economy.TrySettle(id, outcome, report.ElapsedSeconds, out var receipt, out failure)) throw new ArgumentException(failure);
            Accumulate(report, receipt);
        }
        private static void Accumulate(EconomySimulationReport report, EconomyReceiptData receipt)
        {
            foreach (var line in receipt.lines)
            {
                if (line.kind == EconomyLineKind.Fine) { report.Fines -= line.amount; report.Spending -= line.amount; }
                else if (line.kind == EconomyLineKind.Cash)
                { if (line.amount >= 0) report.Income += line.amount; else report.Spending -= line.amount; }
            }
            report.WalletTimeline.Add(receipt.newCash);
        }
        private static bool Chance(ref uint state, int basisPoints)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            return state % 10000 < basisPoints;
        }
    }
}
