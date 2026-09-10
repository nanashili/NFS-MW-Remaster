using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Discovers vehicle modules, orders them once, and provides the common
    /// lifecycle calls. Unity serializes MonoBehaviours; the host converts
    /// them to interfaces at the seam so future modules remain decoupled.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleModuleHost : MonoBehaviour
    {
        [SerializeField] private bool autoDiscoverModules = true;
        [SerializeField] private MonoBehaviour[] moduleComponents =
            System.Array.Empty<MonoBehaviour>();

        private readonly List<IVehicleModule> modules = new List<IVehicleModule>();
        private VehicleModuleContext context = null!;
        private bool configured;

        public VehicleModuleContext Context
        {
            get { return context; }
        }

        public IReadOnlyList<IVehicleModule> Modules
        {
            get { return modules; }
        }

        public void Configure(
            VehicleController vehicle,
            Rigidbody body,
            VehicleTuning tuning,
            VehicleWheel[] wheels)
        {
            DiscoverModules();
            context = new VehicleModuleContext(vehicle, body, tuning, wheels);

            for (int i = 0; i < modules.Count; i++)
            {
                modules[i].Initialize(context);
            }

            configured = true;
        }

        public void RunBeforePhysics(float deltaTime)
        {
            if (!configured)
            {
                return;
            }

            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i] is IVehiclePrePhysicsModule module)
                {
                    module.BeforePhysics(context, deltaTime);
                }
            }
        }

        public void RunAfterPhysics(float deltaTime)
        {
            if (!configured)
            {
                return;
            }

            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i] is IVehiclePostPhysicsModule module)
                {
                    module.AfterPhysics(context, deltaTime);
                }
            }
        }

        public void RunPresentation(float deltaTime)
        {
            if (!configured)
            {
                return;
            }

            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i] is IVehiclePresentationModule module)
                {
                    module.Present(context, deltaTime);
                }
            }
        }

        public bool TryInstallPerformanceUpgrade(
            IVehiclePerformanceUpgrade upgrade,
            out string failure)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i] is IVehiclePerformanceInstaller installer)
                {
                    return installer.TryInstall(upgrade, out failure);
                }
            }

            failure = "No performance installer module is attached to this vehicle.";
            return false;
        }

        public bool TryRemovePerformanceUpgrade(
            VehiclePerformanceCategory category,
            out string failure)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i] is IVehiclePerformanceInstaller installer)
                {
                    return installer.TryRemove(category, out failure);
                }
            }

            failure = "No performance installer module is attached to this vehicle.";
            return false;
        }

        public bool TryInstallPerformanceUpgradeById(
            string upgradeId,
            out string failure)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i] is IVehiclePerformanceIdInstaller installer)
                {
                    return installer.TryInstallById(upgradeId, out failure);
                }
            }

            failure = "No catalog-backed performance installer is attached to this vehicle.";
            return false;
        }

        public bool CanInstallPerformanceUpgrade(
            IVehiclePerformanceUpgrade upgrade,
            out string failure)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i] is IVehiclePerformanceValidator validator)
                {
                    return validator.CanInstall(upgrade, out failure);
                }
            }

            failure = "No performance validator module is attached to this vehicle.";
            return false;
        }

        public bool TryInstallCustomization(
            IVehicleCustomizationItem customization,
            out string failure)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i] is IVehicleCustomizationInstaller installer)
                {
                    return installer.TryInstall(customization, out failure);
                }
            }

            failure = "No customization installer module is attached to this vehicle.";
            return false;
        }

        public bool TryRemoveCustomization(
            VehicleCustomizationCategory category,
            out string failure)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i] is IVehicleCustomizationInstaller installer)
                {
                    return installer.TryRemove(category, out failure);
                }
            }

            failure = "No customization installer module is attached to this vehicle.";
            return false;
        }

        public bool TryInstallCustomizationById(
            string customizationId,
            out string failure)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i] is IVehicleCustomizationIdInstaller installer)
                {
                    return installer.TryInstallById(customizationId, out failure);
                }
            }

            failure = "No catalog-backed customization installer is attached to this vehicle.";
            return false;
        }

        public bool CanInstallCustomization(
            IVehicleCustomizationItem customization,
            out string failure)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i] is IVehicleCustomizationValidator validator)
                {
                    return validator.CanInstall(customization, out failure);
                }
            }

            failure = "No customization validator module is attached to this vehicle.";
            return false;
        }

        public bool ConsumeReconfigureRequest()
        {
            return context != null && context.ConsumeReconfigureRequest();
        }

        public T FindModule<T>() where T : class, IVehicleModule
        {
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i] is T module)
                {
                    return module;
                }
            }

            return null!;
        }

        private void DiscoverModules()
        {
            modules.Clear();
            MonoBehaviour[] candidates = autoDiscoverModules
                ? GetComponentsInChildren<MonoBehaviour>(true)
                : moduleComponents;

            if (candidates != null)
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (candidates[i] is IVehicleModule module && !modules.Contains(module))
                    {
                        modules.Add(module);
                    }
                }
            }

            modules.Sort(CompareModules);
        }

        private static int CompareModules(IVehicleModule left, IVehicleModule right)
        {
            int orderComparison = left.ExecutionOrder.CompareTo(right.ExecutionOrder);
            return orderComparison != 0
                ? orderComparison
                : string.Compare(left.ModuleId, right.ModuleId, System.StringComparison.Ordinal);
        }
    }
}
