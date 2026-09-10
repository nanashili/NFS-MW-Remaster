using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Small external seam for vehicle behaviour. Modules can participate in
    /// setup, the fixed-step simulation, or presentation without the vehicle
    /// controller knowing their concrete type.
    /// </summary>
    public interface IVehicleModule
    {
        string ModuleId { get; }

        int ExecutionOrder { get; }

        void Initialize(VehicleModuleContext context);
    }

    public interface IVehiclePrePhysicsModule : IVehicleModule
    {
        void BeforePhysics(VehicleModuleContext context, float deltaTime);
    }

    public interface IVehiclePostPhysicsModule : IVehicleModule
    {
        void AfterPhysics(VehicleModuleContext context, float deltaTime);
    }

    public interface IVehiclePresentationModule : IVehicleModule
    {
        void Present(VehicleModuleContext context, float deltaTime);
    }

    public interface IVehiclePerformanceInstaller : IVehicleModule
    {
        bool TryInstall(IVehiclePerformanceUpgrade upgrade, out string failure);

        bool TryRemove(VehiclePerformanceCategory category, out string failure);
    }

    public interface IVehiclePerformanceIdInstaller : IVehiclePerformanceInstaller
    {
        bool TryInstallById(string upgradeId, out string failure);
    }

    /// <summary>
    /// Optional read-only capability used by store UI and purchase adapters
    /// to validate an upgrade without changing the vehicle build.
    /// </summary>
    public interface IVehiclePerformanceValidator : IVehicleModule
    {
        bool CanInstall(IVehiclePerformanceUpgrade upgrade, out string failure);
    }

    public interface IVehicleCustomizationInstaller : IVehicleModule
    {
        bool TryInstall(IVehicleCustomizationItem customization, out string failure);

        bool TryRemove(VehicleCustomizationCategory category, out string failure);
    }

    public interface IVehicleCustomizationIdInstaller : IVehicleCustomizationInstaller
    {
        bool TryInstallById(string customizationId, out string failure);
    }

    /// <summary>
    /// Optional read-only capability used by store UI and purchase adapters
    /// to validate a visual item without changing the vehicle build.
    /// </summary>
    public interface IVehicleCustomizationValidator : IVehicleModule
    {
        bool CanInstall(IVehicleCustomizationItem customization, out string failure);
    }

    /// <summary>
    /// The shared context at the vehicle-module seam. It contains the stable
    /// facts modules need, while the host hides discovery and call ordering.
    /// </summary>
    public sealed class VehicleModuleContext
    {
        public VehicleModuleContext(
            VehicleController vehicle,
            Rigidbody body,
            VehicleTuning tuning,
            VehicleWheel[] wheels)
        {
            Vehicle = vehicle;
            Body = body;
            Tuning = tuning;
            Wheels = wheels ?? Array.Empty<VehicleWheel>();
        }

        public VehicleController Vehicle { get; }

        public Rigidbody Body { get; }

        public VehicleTuning Tuning { get; private set; }

        public VehicleWheel[] Wheels { get; }

        public VehicleInputState Input { get; set; }

        public VehicleTelemetry Telemetry { get; internal set; }

        public void ReplaceTuning(VehicleTuning replacement)
        {
            if (replacement == null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            Tuning = replacement;
        }

        public void RequestReconfigure()
        {
            reconfigureRequested = true;
        }

        private bool reconfigureRequested;

        internal bool ConsumeReconfigureRequest()
        {
            bool result = reconfigureRequested;
            reconfigureRequested = false;
            return result;
        }
    }
}
