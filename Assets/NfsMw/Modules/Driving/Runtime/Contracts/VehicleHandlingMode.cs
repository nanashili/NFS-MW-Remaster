namespace NfsMwRemaster.Driving
{
    /// <summary>Stable handling state emitted by the vehicle simulation.</summary>
    public enum VehicleHandlingMode
    {
        Grip,
        Initiating,
        Drifting,
        Recovering,
        Airborne
    }
}
