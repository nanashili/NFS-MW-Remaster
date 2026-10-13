namespace NfsMwRemaster.Driving
{
    /// <summary>Units and the single recovered engine torque lookup shared by tools and runtime.</summary>
    public static class VehicleMath
    {
        public static float EvaluateEngineTorque(VehicleTuning tuning, float rpm)
            => MostWantedVehicleMath.Torque(tuning, rpm);
        public static float KphToMetersPerSecond(float kph) => kph / 3.6f;
        public static float MetersPerSecondToKph(float metersPerSecond) => metersPerSecond * 3.6f;
    }
}
