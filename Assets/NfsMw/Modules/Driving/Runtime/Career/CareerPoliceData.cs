using System;
using System.Collections.Generic;

namespace NfsMwRemaster.Driving
{
    [Serializable]
    public sealed class PoliceSettlementRecord
    {
        public PoliceOutcome outcome;
        public int cashPaid;
    }
    [Serializable]
    public sealed class CareerPoliceData
    {
        public int version = 1;
        public string pendingWorldOutcomeId = string.Empty;
        public List<PoliceSettlementRecord> settlements = new List<PoliceSettlementRecord>();
        public void Validate()
        {
            if (version != 1 || settlements == null) throw new ArgumentException("Invalid police history version/collection.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in settlements)
            {
                if (record?.outcome == null) throw new ArgumentException("Null police outcome.");
                record.outcome.Validate();
                if (!seen.Add(record.outcome.encounterId) || record.cashPaid < 0 || record.cashPaid > record.outcome.assessedFine
                    || record.outcome.kind == PoliceOutcomeKind.Escaped && record.cashPaid != 0
                    || record.outcome.kind == PoliceOutcomeKind.PaidFine && record.cashPaid != record.outcome.assessedFine)
                    throw new ArgumentException("Invalid or duplicate police settlement.");
            }
            if (pendingWorldOutcomeId == null || pendingWorldOutcomeId.Length > 0 && !seen.Contains(pendingWorldOutcomeId))
                throw new ArgumentException("Pending police world outcome must reference a committed settlement.");
        }
    }
}
