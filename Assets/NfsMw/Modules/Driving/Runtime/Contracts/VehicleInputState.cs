using System;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Device-agnostic vehicle intent. Input devices only produce this value;
    /// the vehicle never knows whether the player used a keyboard, pad, wheel,
    /// replay, or AI driver.
    ///
    /// This type is part of the Unity-free simulation contract. Keep Unity
    /// presentation and object references out of it so tools and alternate
    /// simulation hosts can consume the same seam.
    /// </summary>
    [Serializable]
    public struct VehicleInputState
    {
        public float Steering;
        public float Throttle;
        public float Brake;
        public bool Handbrake;
        public bool Nitrous;
        public bool GearUp;
        public bool GearDown;
        public bool GearNeutral;
        public bool GearReverse;

        public static VehicleInputState Neutral
        {
            get { return new VehicleInputState(); }
        }

        public VehicleInputState Clamped()
        {
            VehicleInputState result = this;
            result.Steering = Clamp(FiniteOrZero(result.Steering), -1f, 1f);
            result.Throttle = Clamp(FiniteOrZero(result.Throttle), 0f, 1f);
            result.Brake = Clamp(FiniteOrZero(result.Brake), 0f, 1f);
            return result;
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            if (value < minimum) return minimum;
            if (value > maximum) return maximum;
            return value;
        }

        private static float FiniteOrZero(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    /// <summary>
    /// Source seam for player, AI, replay, and test drivers.
    /// </summary>
    public interface IVehicleInputSource
    {
        VehicleInputState Current { get; }

        bool ConsumeResetRequest();

        bool ConsumeCameraToggleRequest();
    }

}
