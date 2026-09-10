using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public interface IVehicleDiscreteInputSource
    {
        VehicleInputState ConsumeDiscreteActions();
    }

    /// <summary>Single owner for player, AI, replay and cutscene vehicle intent.</summary>
    [DisallowMultipleComponent]
    public sealed class VehicleInputAuthority : MonoBehaviour, IVehicleInputSource, IVehicleDiscreteInputSource
    {
        [SerializeField, Tooltip("Optional initial driver. Handover requires releasing its ownership before acquiring another source.")]
        private MonoBehaviour defaultSource;
        private IVehicleInputSource source;
        private object owner;
        private VehicleInputState current;
        public object Owner => owner;
        public void ConfigureDefault(MonoBehaviour input)
        {
            if (input != null && input is not IVehicleInputSource) throw new ArgumentException("Default driver must implement IVehicleInputSource.");
            if (input == this) throw new ArgumentException("An authority cannot drive itself.");
            defaultSource = input;
            if (owner == null && input is IVehicleInputSource driver) TryAcquire(input, driver);
        }
        private void OnEnable() { if (owner == null && defaultSource is IVehicleInputSource driver) TryAcquire(defaultSource, driver); }
        private void OnDisable() => SetNeutral();
        public VehicleInputState Current => source == null ? current : source.Current.Clamped();
        public bool ConsumeResetRequest() => source != null && source.ConsumeResetRequest();
        public bool ConsumeCameraToggleRequest() => source != null && source.ConsumeCameraToggleRequest();
        public VehicleInputState ConsumeDiscreteActions() => source is IVehicleDiscreteInputSource discrete
            ? discrete.ConsumeDiscreteActions() : VehicleInputState.Neutral;

        public bool TryAcquire(object requester, IVehicleInputSource input)
        {
            if (requester == null || input == null) return false;
            if (owner != null && !ReferenceEquals(owner, requester)) return false;
            owner = requester; source = input; return true;
        }

        public bool Release(object requester)
        {
            if (owner == null || !ReferenceEquals(owner, requester)) return false;
            owner = null; source = null; current = VehicleInputState.Neutral; return true;
        }

        public void SetNeutral() { source = null; owner = null; current = VehicleInputState.Neutral; }
    }
}
