using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>InputPlayer pedal processing from nfsmw 3bb4ab24, PInput.cpp.
    /// Local gears are source GearID minus one: reverse -1, neutral 0, first 1.</summary>
    public static class MostWantedDriverControls
    {
        // PAD_DEAD_ZONE's value is not defined in the recovered source; the device adapter supplies it.
        public static float SnapPedal(float value, float deadZone)
        {
            if (value > 1f - deadZone) value = 1f;
            if (value < deadZone) value = 0f;
            return value;
        }

        public static int AutoReverseGear(int gear, float speed, float gas, float brake)
        {
            if (gear != -1)
            {
                if (speed < 2.5f && brake > 0f && gas == 0f) return -1;
            }
            else if (speed > -5f && (brake == 0f || gas > 0f)) return 1;
            return gear;
        }

        // Called after shifting, using the actual accepted gear and the original pedals.
        public static VehicleInputState Pedals(VehicleInputState input, int gear, float speed)
        {
            float gas = input.Throttle, brake = input.Brake;
            if (gear == -1) { input.Throttle = brake; input.Brake = gas; }
            if (gas <= 0f && brake <= 0f)
            {
                if (gear > -1)
                {
                    if (speed < 8f) input.Brake = 1f - Mathf.Clamp01(speed / 8f) * .75f;
                }
                else input.Brake = 1f - Mathf.Clamp01(-speed / 10f) * .75f;
            }
            if (input.Handbrake) input.Brake = 0f;
            return input;
        }
    }

    /// <summary>AI and force-stop adapters supply final controls instead of player pedal intent.</summary>
    public interface IVehicleInputControlPolicy
    {
        bool ApplyPlayerControls { get; }
    }
}
