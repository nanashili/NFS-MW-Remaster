using System;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleModuleHostTests
    {
        [Test]
        public void DiscoveryOrdersModulesByExecutionOrderThenStableId()
        {
            var root = new GameObject("Vehicle module ordering");
            VehicleTuning tuning = VehicleTuning.CreateStreetRacer();
            try
            {
                VehicleModuleHost host = root.AddComponent<VehicleModuleHost>();
                VehicleModuleProbe last = root.AddComponent<VehicleModuleProbe>();
                VehicleModuleProbe beta = root.AddComponent<VehicleModuleProbe>();
                VehicleModuleProbe alpha = root.AddComponent<VehicleModuleProbe>();
                last.Configure("last", 10);
                beta.Configure("beta", 0);
                alpha.Configure("alpha", 0);

                host.Configure(null, root.AddComponent<Rigidbody>(), tuning, Array.Empty<VehicleWheel>());

                Assert.That(host.Modules.Count, Is.EqualTo(3));
                Assert.That(host.Modules[0], Is.SameAs(alpha));
                Assert.That(host.Modules[1], Is.SameAs(beta));
                Assert.That(host.Modules[2], Is.SameAs(last));
                Assert.That(alpha.Initializations, Is.EqualTo(1));
                Assert.That(beta.Initializations, Is.EqualTo(1));
                Assert.That(last.Initializations, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(tuning);
            }
        }

        [Test]
        public void ExecutionPhasesShareContractsWithoutSteadyStateAllocations()
        {
            var root = new GameObject("Vehicle module phases");
            VehicleTuning tuning = VehicleTuning.CreateStreetRacer();
            try
            {
                VehicleModuleHost host = root.AddComponent<VehicleModuleHost>();
                VehicleModuleProbe probe = root.AddComponent<VehicleModuleProbe>();
                probe.Configure("probe", 0);
                host.Configure(null, root.AddComponent<Rigidbody>(), tuning, Array.Empty<VehicleWheel>());
                host.Context.Input = new VehicleInputState { Throttle = 0.75f };

                for (int i = 0; i < 32; i++) RunAllPhases(host);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 2000; i++) RunAllPhases(host);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(allocated, Is.Zero, "Vehicle module dispatch allocated after warmup.");
                Assert.That(probe.LastInput.Throttle, Is.EqualTo(0.75f));
                Assert.That(probe.LastTelemetry.GearLabel, Is.EqualTo("N"));
                Assert.That(probe.BeforePhysicsCalls, Is.EqualTo(2032));
                Assert.That(probe.AfterPhysicsCalls, Is.EqualTo(2032));
                Assert.That(probe.PresentationCalls, Is.EqualTo(2032));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(tuning);
            }
        }

        [Test]
        public void ExistingModulesRestoreSerializedCareerConfigurationThroughParticipantSeam()
        {
            var root = new GameObject("Vehicle module restoration");
            VehicleTuning tuning = VehicleTuning.CreateStreetRacer();
            var upgrade = ScriptableObject.CreateInstance<TunedVehiclePerformanceUpgrade>();
            var customization = ScriptableObject.CreateInstance<AssetVehicleCustomization>();
            var performanceCatalog = ScriptableObject.CreateInstance<VehiclePerformanceCatalog>();
            var customizationCatalog = ScriptableObject.CreateInstance<VehicleCustomizationCatalog>();
            try
            {
                upgrade.ConfigureMetadata("engine.street", "Street Engine",
                    VehiclePerformanceCategory.Engine, VehiclePerformanceTier.Street, 500, false);
                customization.ConfigureMetadata("paint.black", "Black Paint",
                    VehicleCustomizationCategory.Paint, VehicleCustomizationStyle.Standard, 100, false);
                performanceCatalog.SetUpgrades(new[] { upgrade });
                customizationCatalog.SetItems(new[] { customization });

                VehicleModuleHost host = root.AddComponent<VehicleModuleHost>();
                VehiclePerformanceSystem performance = root.AddComponent<VehiclePerformanceSystem>();
                VehicleCustomizationSystem visuals = root.AddComponent<VehicleCustomizationSystem>();
                performance.SetCatalog(performanceCatalog);
                visuals.SetCatalog(customizationCatalog);
                host.Configure(null, root.AddComponent<Rigidbody>(), tuning, Array.Empty<VehicleWheel>());

                Assert.That(host.Modules[0], Is.SameAs(performance));
                Assert.That(host.Modules[1], Is.SameAs(visuals));

                var saved = new CareerVehicleData();
                saved.performanceUpgradeIds.Add("engine.street");
                saved.customizationIds.Add("paint.black");
                CareerVehicleData recovered = JsonUtility.FromJson<CareerVehicleData>(JsonUtility.ToJson(saved));

                Assert.That(performance.Restore(recovered, out string performanceFailure), Is.True, performanceFailure);
                Assert.That(visuals.Restore(recovered, out string customizationFailure), Is.True, customizationFailure);
                Assert.That(host.ConsumeReconfigureRequest(), Is.True);

                var captured = new CareerVehicleData();
                performance.Capture(captured);
                visuals.Capture(captured);
                Assert.That(captured.performanceUpgradeIds, Is.EqualTo(new[] { "engine.street" }));
                Assert.That(captured.customizationIds, Is.EqualTo(new[] { "paint.black" }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(tuning);
                UnityEngine.Object.DestroyImmediate(upgrade);
                UnityEngine.Object.DestroyImmediate(customization);
                UnityEngine.Object.DestroyImmediate(performanceCatalog);
                UnityEngine.Object.DestroyImmediate(customizationCatalog);
            }
        }

        private static void RunAllPhases(VehicleModuleHost host)
        {
            host.RunBeforePhysics(0.02f);
            host.RunAfterPhysics(0.02f);
            host.RunPresentation(0.02f);
        }
    }

    public sealed class VehicleModuleProbe :
        MonoBehaviour,
        IVehiclePrePhysicsModule,
        IVehiclePostPhysicsModule,
        IVehiclePresentationModule
    {
        private string moduleId = string.Empty;
        private int executionOrder;

        public string ModuleId => moduleId;
        public int ExecutionOrder => executionOrder;
        public int Initializations { get; private set; }
        public int BeforePhysicsCalls { get; private set; }
        public int AfterPhysicsCalls { get; private set; }
        public int PresentationCalls { get; private set; }
        public VehicleInputState LastInput { get; private set; }
        public VehicleTelemetry LastTelemetry { get; private set; }

        public void Configure(string id, int order)
        {
            moduleId = id;
            executionOrder = order;
        }

        public void Initialize(VehicleModuleContext context)
        {
            Initializations++;
        }

        public void BeforePhysics(VehicleModuleContext context, float deltaTime)
        {
            BeforePhysicsCalls++;
            LastInput = context.Input;
        }

        public void AfterPhysics(VehicleModuleContext context, float deltaTime)
        {
            AfterPhysicsCalls++;
        }

        public void Present(VehicleModuleContext context, float deltaTime)
        {
            PresentationCalls++;
            LastTelemetry = context.Telemetry;
        }
    }
}
