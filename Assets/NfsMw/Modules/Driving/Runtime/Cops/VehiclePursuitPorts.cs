using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum VehiclePoliceUnitRole
    {
        Pursuer,
        Interceptor,
        Boxer,
        Heavy
    }

    public enum VehiclePoliceUnitState
    {
        Dormant,
        Responding,
        Engaged,
        Searching,
        Disabled,
        Released
    }

    public enum VehiclePursuitPhase
    {
        Dormant,
        Alert,
        Engaged,
        Search,
        Cooldown,
        TrafficStop,
        OutcomePending
    }

    public enum VehiclePoliceTactic
    {
        Chase,
        Intercept,
        Ram,
        BoxLeft,
        BoxRight,
        Search
    }

    /// <summary>
    /// Small adapter boundary between pursuit logic and whatever vehicle or
    /// replay system owns the target. The director never reaches into a
    /// VehicleController directly.
    /// </summary>
    public interface IVehiclePursuitTarget
    {
        string TargetId { get; }

        Transform Transform { get; }

        Rigidbody Body { get; }

        Vector3 Position { get; }

        Vector3 Velocity { get; }

        bool IsAvailable { get; }
    }

    public interface IVehiclePursuitPerception
    {
        bool CanSee(IVehiclePoliceUnit observer, IVehiclePursuitTarget target, float range);
    }

    public interface IPoliceIgnitionState { bool EngineRunning { get; } }

    /// <summary>
    /// Historical bounty compatibility contract. The fine-driven police director
    /// does not read this as authority; PoliceCareerAdapter only mirrors engagement into it.
    /// </summary>
    public interface IVehiclePursuitHeatSink
    {
        int HeatLevel { get; }

        int MaxHeatLevel { get; }

        void SetHeatLevel(int configuredHeatLevel);
    }

    public struct VehiclePoliceUnitCommand
    {
        public VehiclePoliceUnitCommand(
            VehiclePursuitPhase configuredPhase,
            VehiclePoliceTactic configuredTactic,
            Vector3 configuredAimPoint,
            float configuredDesiredSpeedMultiplier,
            float configuredAggression,
            float configuredRamForce,
            float configuredBoxStrength,
            int configuredSlot)
        {
            Phase = configuredPhase;
            Tactic = configuredTactic;
            AimPoint = configuredAimPoint;
            DesiredSpeedMultiplier = configuredDesiredSpeedMultiplier;
            Aggression = configuredAggression;
            RamForce = configuredRamForce;
            BoxStrength = configuredBoxStrength;
            Slot = configuredSlot;
            TargetVisible = false;
        }

        public VehiclePursuitPhase Phase;

        public VehiclePoliceTactic Tactic;

        public Vector3 AimPoint;

        public float DesiredSpeedMultiplier;

        public float Aggression;

        public float RamForce;

        public float BoxStrength;

        public int Slot;
        public bool TargetVisible;
    }

    /// <summary>
    /// Stable director contract for HUDs, mission logic, replay tools, and
    /// alternate police implementations.
    /// </summary>
    public interface IVehiclePursuitDirector
    {
        VehiclePursuitPhase Phase { get; }

        int HeatLevel { get; }

        int MaxHeatLevel { get; }

        bool IsActive { get; }

        IVehiclePursuitTarget Target { get; }

        int ActiveUnitCount { get; }

        int RegisteredUnitCount { get; }

        float TimeInPhase { get; }

        float TimeSinceTargetSeen { get; }

        Vector3 LastKnownPosition { get; }

        VehiclePoliceResponseTier CurrentTier { get; }

        bool TryStartPursuit(out string failure);

        bool TrySetHeatLevel(int configuredHeatLevel, out string failure);

        bool TryForceEscape(out string failure);

        bool TryForceBust(out string failure);

        bool TryRecordBountyEvent(
            VehicleBountyEventKind eventKind,
            int count,
            out string failure);
    }

    /// <summary>
    /// Unit boundary. A unit can be replaced by a prefab, a network proxy, or
    /// a different steering implementation without changing the director.
    /// </summary>
    public interface IVehiclePoliceUnit
    {
        string UnitId { get; }

        VehiclePoliceUnitRole Role { get; }

        VehiclePoliceUnitState State { get; }

        VehiclePoliceTactic Tactic { get; }

        Vector3 Position { get; }

        float Integrity { get; }

        bool IsDisabled { get; }

        void SetPursuitCommand(
            VehiclePoliceUnitCommand command,
            IVehiclePursuitTarget target,
            VehiclePoliceResponseTier tier);

        void ReleaseFromPursuit();

        void ApplyDamage(float amount);
    }
}
