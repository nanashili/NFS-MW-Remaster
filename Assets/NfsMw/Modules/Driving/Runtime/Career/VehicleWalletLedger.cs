using System;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Pure wallet ledger used by the Unity wallet adapter and tests. All
    /// mutations validate before changing the balance and reject overflow.
    /// </summary>
    public sealed class VehicleWalletLedger : IVehicleWallet
    {
        private int balance;

        public VehicleWalletLedger(int startingBalance = 0)
        {
            SetBalance(startingBalance);
        }

        public int Balance
        {
            get { return balance; }
        }

        public bool CanAfford(int amount, out string failure)
        {
            if (amount < 0)
            {
                failure = "Amount cannot be negative.";
                return false;
            }

            if (balance < amount)
            {
                failure = "Not enough cash for this purchase.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        public bool TrySpend(int amount, out string failure)
        {
            if (!CanAfford(amount, out failure))
            {
                return false;
            }

            balance -= amount;
            return true;
        }

        public bool TryAdd(int amount, out string failure)
        {
            if (amount < 0)
            {
                failure = "Amount cannot be negative.";
                return false;
            }

            if (amount > int.MaxValue - balance)
            {
                failure = "Wallet balance would exceed its maximum value.";
                return false;
            }

            balance += amount;
            failure = string.Empty;
            return true;
        }

        public void SetBalance(int configuredBalance)
        {
            balance = Math.Max(0, configuredBalance);
        }
    }
}
