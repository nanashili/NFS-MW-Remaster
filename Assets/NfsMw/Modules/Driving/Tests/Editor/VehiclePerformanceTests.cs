using System;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehiclePerformanceTests
    {
        [Test]
        public void InstallAllowsOnePartPerCategoryAndRejectsDowngrade()
        {
            VehiclePerformanceBuild build = new VehiclePerformanceBuild();
            FakeUpgrade proEngine = new FakeUpgrade(
                "engine_pro",
                VehiclePerformanceCategory.Engine,
                VehiclePerformanceTier.Pro,
                _ => { });
            FakeUpgrade streetEngine = new FakeUpgrade(
                "engine_street",
                VehiclePerformanceCategory.Engine,
                VehiclePerformanceTier.Street,
                _ => { });

            Assert.That(build.TryInstall(proEngine, out string installFailure), Is.True, installFailure);
            Assert.That(build.TryInstall(streetEngine, out string downgradeFailure), Is.False);
            Assert.That(downgradeFailure, Does.Contain("lower tier"));
            Assert.That(build.Get(VehiclePerformanceCategory.Engine), Is.SameAs(proEngine));
        }

        [Test]
        public void InstallSameTierReplacesTheExistingVariant()
        {
            VehiclePerformanceBuild build = new VehiclePerformanceBuild();
            FakeUpgrade turboA = new FakeUpgrade(
                "turbo_a",
                VehiclePerformanceCategory.ForcedInduction,
                VehiclePerformanceTier.Super,
                _ => { });
            FakeUpgrade turboB = new FakeUpgrade(
                "turbo_b",
                VehiclePerformanceCategory.ForcedInduction,
                VehiclePerformanceTier.Super,
                _ => { });

            Assert.That(build.TryInstall(turboA, out string firstFailure), Is.True, firstFailure);
            Assert.That(build.TryInstall(turboB, out string secondFailure), Is.True, secondFailure);
            Assert.That(build.Installed.Count, Is.EqualTo(1));
            Assert.That(build.Get(VehiclePerformanceCategory.ForcedInduction), Is.SameAs(turboB));
        }

        [Test]
        public void RemovingPartReturnsCategoryToStock()
        {
            VehiclePerformanceBuild build = new VehiclePerformanceBuild();
            FakeUpgrade tires = new FakeUpgrade(
                "tires_ultimate",
                VehiclePerformanceCategory.Tires,
                VehiclePerformanceTier.Ultimate,
                _ => { });

            Assert.That(build.TryInstall(tires, out string installFailure), Is.True, installFailure);
            Assert.That(build.TryRemove(VehiclePerformanceCategory.Tires, out string removeFailure), Is.True, removeFailure);
            Assert.That(build.Get(VehiclePerformanceCategory.Tires), Is.Null);
            Assert.That(build.TryRemove(VehiclePerformanceCategory.Tires, out _), Is.False);
        }

        [Test]
        public void ModifierChangesOnlyTheTuningDomainsItOwns()
        {
            VehicleTuning tuning = VehicleTuning.CreateStreetRacer();
            try
            {
                float stockMass = tuning.chassis.mass;
                float stockTorque = tuning.engine.maxTorqueNewtonMeters;
                float stockLateralGrip = tuning.tires.lateralGrip;
                VehiclePerformanceModifier modifier = new VehiclePerformanceModifier
                {
                    engineTorqueMultiplier = 1.2f,
                    tireLateralGripMultiplier = 1.1f
                };

                modifier.ApplyTo(tuning);

                Assert.That(tuning.chassis.mass, Is.EqualTo(stockMass).Within(0.001f));
                Assert.That(
                    tuning.engine.maxTorqueNewtonMeters,
                    Is.EqualTo(stockTorque * 1.2f).Within(0.001f));
                Assert.That(
                    tuning.tires.lateralGrip,
                    Is.EqualTo(stockLateralGrip * 1.1f).Within(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tuning);
            }
        }

        [Test]
        public void CustomInterfaceUpgradeCanTransformTheTuning()
        {
            VehiclePerformanceBuild build = new VehiclePerformanceBuild();
            FakeUpgrade customSuspension = new FakeUpgrade(
                "race_suspension_custom",
                VehiclePerformanceCategory.Suspension,
                VehiclePerformanceTier.Ultimate,
                tuning => tuning.tires.springRate *= 1.5f);
            VehicleTuning tuningAsset = VehicleTuning.CreateStreetRacer();
            try
            {
                float stockSpringRate = tuningAsset.tires.springRate;
                Assert.That(build.TryInstall(customSuspension, out string failure), Is.True, failure);

                build.ApplyTo(tuningAsset);

                Assert.That(
                    tuningAsset.tires.springRate,
                    Is.EqualTo(stockSpringRate * 1.5f).Within(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tuningAsset);
            }
        }

        private sealed class FakeUpgrade : IVehiclePerformanceUpgrade
        {
            private readonly Action<VehicleTuning> apply;

            public FakeUpgrade(
                string id,
                VehiclePerformanceCategory category,
                VehiclePerformanceTier tier,
                Action<VehicleTuning> apply)
            {
                UpgradeId = id;
                DisplayName = id;
                Category = category;
                Tier = tier;
                this.apply = apply;
            }

            public string UpgradeId { get; }

            public string DisplayName { get; }

            public VehiclePerformanceCategory Category { get; }

            public VehiclePerformanceTier Tier { get; }

            public int Price
            {
                get { return 0; }
            }

            public bool IsJunkman
            {
                get { return Tier == VehiclePerformanceTier.Junkman; }
            }

            public void Apply(VehicleTuning tuning)
            {
                apply(tuning);
            }
        }
    }
}
