using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public struct VehiclePursuitDecisionInput
    {
        public VehiclePoliceUnitRole Role;

        public VehiclePursuitPhase Phase;

        public float DistanceToTarget;

        public float TargetSpeed;

        public float Aggression;

        public bool TargetVisible;
        public bool AllowContactExtensions;

        public int Slot;
    }

    public struct VehiclePursuitDecision
    {
        public VehiclePursuitDecision(
            VehiclePoliceTactic configuredTactic,
            float configuredDesiredSpeedMultiplier,
            bool configuredCommitsToContact)
        {
            Tactic = configuredTactic;
            DesiredSpeedMultiplier = configuredDesiredSpeedMultiplier;
            CommitsToContact = configuredCommitsToContact;
        }

        public VehiclePoliceTactic Tactic;

        public float DesiredSpeedMultiplier;

        public bool CommitsToContact;
    }

    /// <summary>
    /// Pure role/tactic selection. Movement, collision, and spawning remain in
    /// separate adapters so this model can later be reused by network or
    /// replay police without importing a scene implementation.
    /// </summary>
    public static class VehiclePursuitDecisionModel
    {
        public static VehiclePursuitDecision Decide(
            VehiclePursuitDecisionInput input)
        {
            float aggression = Mathf.Clamp01(input.Aggression);

            if (!input.TargetVisible || input.Phase == VehiclePursuitPhase.Search
                || (input.Phase == VehiclePursuitPhase.Cooldown
                    && !input.TargetVisible))
            {
                return new VehiclePursuitDecision(
                    VehiclePoliceTactic.Search,
                    0.92f,
                    false);
            }

            if (!input.AllowContactExtensions)
                return new VehiclePursuitDecision(input.Role == VehiclePoliceUnitRole.Interceptor
                    ? VehiclePoliceTactic.Intercept : VehiclePoliceTactic.Chase, 1f, false);

            if (input.Role == VehiclePoliceUnitRole.Heavy
                && input.DistanceToTarget <= Mathf.Lerp(24f, 34f, aggression))
            {
                return new VehiclePursuitDecision(
                    VehiclePoliceTactic.Ram,
                    1.12f,
                    true);
            }

            if (input.Role == VehiclePoliceUnitRole.Boxer)
            {
                return new VehiclePursuitDecision(
                    input.Slot % 2 == 0
                        ? VehiclePoliceTactic.BoxLeft
                        : VehiclePoliceTactic.BoxRight,
                    1.06f,
                    true);
            }

            if (input.Role == VehiclePoliceUnitRole.Interceptor)
            {
                return new VehiclePursuitDecision(
                    VehiclePoliceTactic.Intercept,
                    1.12f,
                    false);
            }

            if (input.DistanceToTarget <= Mathf.Lerp(9f, 17f, aggression)
                && aggression >= 0.42f)
            {
                return new VehiclePursuitDecision(
                    VehiclePoliceTactic.Ram,
                    1.12f,
                    true);
            }

            if (input.DistanceToTarget <= Mathf.Lerp(28f, 42f, aggression)
                && aggression >= 0.50f)
            {
                return new VehiclePursuitDecision(
                    VehiclePoliceTactic.Intercept,
                    1.08f,
                    false);
            }

            return new VehiclePursuitDecision(
                VehiclePoliceTactic.Chase,
                1.02f,
                false);
        }
    }
}
