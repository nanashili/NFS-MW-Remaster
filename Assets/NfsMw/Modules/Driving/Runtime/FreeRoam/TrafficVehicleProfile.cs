using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum TrafficVehicleCategory { Compact, Sedan, Suv, Pickup, Van, Taxi, Commercial }

    [CreateAssetMenu(menuName = "NFS MW Remaster/Traffic/Vehicle Profile")]
    public sealed class TrafficVehicleProfile : ScriptableObject
    {
        public TrafficVehicleCategory category;
        public VehicleTuning tuning;
        public Vector3 colliderSize = new Vector3(1.8f, 1.2f, 4.1f);
        public Vector3 colliderCenter = new Vector3(0, 0.1f, 0);
        [Min(0.1f)] public float comfortAccelerationLimit = 2;
        [Tooltip("Conservative available deceleration in m/s². Measure on the vehicle and supported surface before increasing.")]
        [Min(4)] public float brakingLimit = 6;
        [Min(0.5f)] public float wheelbase = 2.5f;
        [Min(0.1f)] public float maximumLateralAcceleration = 2.5f;
    }
}
