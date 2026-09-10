using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// A small replaceable boost resource. Powertrain owns torque; this module
    /// owns only activation and fuel so races can swap refill rules later.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleNitrous : MonoBehaviour
    {
        private float remainingSeconds;
        private float capacitySeconds;
        private VehicleTuning tuning;
        private float engagement;
        public bool RechargeAllowed { get; set; } = true;

        public bool IsActive { get; private set; }
        public float NormalizedFuel => capacitySeconds > 0f ? Mathf.Clamp01(remainingSeconds / capacitySeconds) : 0f;

        public float RemainingSeconds
        {
            get { return remainingSeconds; }
        }

        public void Configure(VehicleTuning tuning)
        {
            this.tuning = tuning;
            float previousFraction = capacitySeconds > 0.001f ? remainingSeconds / capacitySeconds : 1f;
            capacitySeconds = tuning == null ? 0f : Mathf.Max(0f, tuning.engine.nitrousFuelSeconds);
            remainingSeconds = capacitySeconds * Mathf.Clamp01(previousFraction);
            IsActive = false;
            engagement = 0f;
        }

        public void ResetForSpawn() { remainingSeconds = capacitySeconds; IsActive = false; engagement = 0f; RechargeAllowed = true; }

        public bool Tick(float deltaTime, bool requested, float speedKph)
            => Tick(deltaTime, requested, speedKph, 1);

        public bool Tick(float deltaTime, bool requested, float speedKph, int gear)
        {
            if (!float.IsFinite(deltaTime) || deltaTime <= 0f || !float.IsFinite(speedKph)) { IsActive = false; return false; }
            if (tuning != null && tuning.UsesMostWantedReference) return TickReference(deltaTime, requested, speedKph, gear);
            bool canBoost = requested && remainingSeconds > 0.001f && speedKph > 8f;
            IsActive = canBoost;
            if (IsActive)
            {
                remainingSeconds = Mathf.Max(0f, remainingSeconds - Mathf.Max(0f, deltaTime));
            }

            return IsActive;
        }

        private bool TickReference(float deltaTime, bool requested, float speedKph, int gear)
        {
            var settings = tuning.mostWanted;
            IsActive = false;
            if (capacitySeconds <= 0f || settings.nitrousTorqueBoost <= 0f) { engagement = 0f; return false; }
            float minimumSpeed = (engagement >= 1f ? 5f : 10f) * MostWantedVehicleMath.ReferenceMphToKph;
            if (requested && gear > 0 && Mathf.Abs(speedKph) >= minimumSpeed && remainingSeconds > 0f)
            {
                remainingSeconds = Mathf.Max(0f, remainingSeconds - deltaTime);
                engagement = 1f;
                IsActive = true;
            }
            else if (engagement > 0f && settings.nitrousDisengageSeconds > 0f)
                engagement = Mathf.Max(0f, engagement - deltaTime / settings.nitrousDisengageSeconds);
            else
            {
                engagement = 0f;
                if (RechargeAllowed && gear > 0 && Mathf.Abs(speedKph) >= settings.nitrousRechargeMinimumKph && remainingSeconds < capacitySeconds)
                {
                    float fraction = Mathf.InverseLerp(settings.nitrousRechargeMinimumKph, settings.nitrousRechargeMaximumKph, Mathf.Abs(speedKph));
                    float seconds = Mathf.Lerp(settings.nitrousRechargeMinimumSeconds, settings.nitrousRechargeMaximumSeconds, fraction);
                    if (seconds > 0f) remainingSeconds = Mathf.Min(capacitySeconds, remainingSeconds + capacitySeconds * deltaTime / seconds);
                }
            }
            return IsActive;
        }

        public void Refill()
        {
            remainingSeconds = capacitySeconds;
            engagement = 0f;
            IsActive = false;
        }
    }
}
