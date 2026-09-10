using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public sealed class FreeRoamVehicleInput : MonoBehaviour, IVehicleInputSource
    {
        [SerializeField] private PlayerVehicleInput player;
        [SerializeField] private FreeRoamSession session;
        public VehicleInputState Current => session != null && session.CanDrive && player != null
            ? player.Current : new VehicleInputState { Brake = 1, Handbrake = true };
        public bool ConsumeResetRequest() { if (player != null) player.ConsumeResetRequest(); return false; }
        public bool ConsumeCameraToggleRequest()
        {
            bool requested = player != null && player.ConsumeCameraToggleRequest();
            return session != null && session.CanDrive && requested;
        }
        public void Configure(PlayerVehicleInput input, FreeRoamSession owner) { player = input; session = owner; }
    }
}
