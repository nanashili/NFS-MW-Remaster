using UnityEngine;

namespace NfsMwRemaster.Driving
{
    // Harness-owned ordinary input. No automatic updates and no vehicle state writes.
    public sealed class RacingSimulationInput : MonoBehaviour, IVehicleInputSource, IVehicleInputControlPolicy
    {
        public bool ApplyPlayerControls { get; set; }
        public VehicleInputState Current { get; set; }
        public bool ConsumeResetRequest() => false;
        public bool ConsumeCameraToggleRequest() => false;
    }
}
