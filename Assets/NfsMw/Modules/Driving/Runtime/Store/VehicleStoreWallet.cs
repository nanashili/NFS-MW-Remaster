using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Small local wallet for the demo and offline career prototype. It also
    /// participates in the career profile, while VehicleStoreSystem still
    /// consumes only the smaller store wallet port.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleStoreWallet :
        MonoBehaviour,
        IVehicleWallet,
        ICareerProfileParticipant
    {
        [SerializeField, Min(0)] private int balance;
        private VehicleWalletLedger ledger = new VehicleWalletLedger();
        private bool ledgerSynchronized;

        public string ProfileSectionId
        {
            get { return "wallet"; }
        }

        public int Balance
        {
            get { return Ledger.Balance; }
        }

        public bool CanAfford(int amount, out string failure)
        {
            return Ledger.CanAfford(amount, out failure);
        }

        public bool TrySpend(int amount, out string failure)
        {
            if (!Ledger.TrySpend(amount, out failure))
            {
                return false;
            }

            balance = Ledger.Balance;
            return true;
        }

        public void AddFunds(int amount)
        {
            TryAdd(amount, out _);
        }

        public bool TryAdd(int amount, out string failure)
        {
            if (!Ledger.TryAdd(amount, out failure))
            {
                return false;
            }

            balance = Ledger.Balance;
            return true;
        }

        public void SetBalance(int configuredBalance)
        {
            Ledger.SetBalance(Mathf.Max(0, configuredBalance));
            balance = Ledger.Balance;
        }

        public void Capture(CareerProfileData profile)
        {
            if (profile == null)
            {
                return;
            }

            profile.Normalize();
            profile.wallet.balance = Balance;
        }

        public bool Restore(CareerProfileData profile, out string failure)
        {
            if (profile == null)
            {
                failure = "Profile is null.";
                return false;
            }

            profile.Normalize();
            SetBalance(profile.wallet.balance);
            failure = string.Empty;
            return true;
        }

        private VehicleWalletLedger Ledger
        {
            get
            {
                if (ledger == null)
                {
                    ledger = new VehicleWalletLedger();
                    ledgerSynchronized = false;
                }

                if (!ledgerSynchronized)
                {
                    ledger.SetBalance(balance);
                    ledgerSynchronized = true;
                }

                return ledger;
            }
        }

        private void Awake()
        {
            ledgerSynchronized = false;
            _ = Ledger;
        }

        private void OnValidate()
        {
            balance = Mathf.Max(0, balance);
            ledgerSynchronized = false;
        }
    }
}
