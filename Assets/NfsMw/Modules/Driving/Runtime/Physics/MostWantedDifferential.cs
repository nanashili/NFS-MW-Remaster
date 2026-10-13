using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// Pure translation of SuspensionRacer::Differential::CalcSplit.
    public struct MostWantedDifferential
    {
        public Vector2 AngularVelocity;
        public Vector2 HasTraction;
        public float Bias;
        public float Factor;
        public Vector2 TorqueSplit;
        public void CalcSplit(bool locked)
        {
            if (HasTraction.x == 0 || HasTraction.y == 0 || locked || Factor <= 0)
            {
                TorqueSplit = new Vector2(Bias, 1f - Bias);
                return;
            }

            float av0 = AngularVelocity.x * (1f - Bias), av1 = AngularVelocity.y * Bias;
            float combined = Mathf.Abs(av0 + av1);
            if (combined > 1e-6f)
            {
                TorqueSplit.x = (1f - Factor) * Bias + Factor * Mathf.Abs(av1) / combined;
                TorqueSplit.y = (1f - Factor) * (1f - Bias) + Factor * Mathf.Abs(av0) / combined;
            }
            else
                TorqueSplit = new Vector2(Bias, 1f - Bias);
            TorqueSplit.x = Mathf.Clamp01(TorqueSplit.x);
            TorqueSplit.y = Mathf.Clamp01(TorqueSplit.y);
        }
    }
}
