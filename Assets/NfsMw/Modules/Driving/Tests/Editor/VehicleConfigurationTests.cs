using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleConfigurationTests
    {
        private VehicleTuning factory;
        private VehicleDefinition definition;
        [SetUp] public void SetUp()
        {
            factory = VehicleTuning.CreateStreetRacer();
            definition = ScriptableObject.CreateInstance<VehicleDefinition>();
            definition.vehicleId = "framework-test"; definition.variantId = "sport";
            definition.factoryTuning = factory; definition.capabilities = VehicleCapabilities.All;
        }
        [TearDown] public void TearDown()
        { UnityEngine.Object.DestroyImmediate(definition); UnityEngine.Object.DestroyImmediate(factory); }

        [Test] public void RepeatedResolutionIsIsolatedAndRemovalRestoresFactory()
        {
            var build = new VehiclePerformanceBuild();
            Assert.That(build.TryInstall(new Part("engine", VehiclePerformanceCategory.Engine, 1.2f, 10), out _), Is.True);
            float expected = factory.engine.maxTorqueNewtonMeters * 1.2f + 10;
            using var first = VehicleConfigurationResolver.Resolve(factory, build, null, definition: definition);
            using var second = VehicleConfigurationResolver.Resolve(factory, build, null, definition: definition);
            Assert.That(first.Tuning.engine.maxTorqueNewtonMeters, Is.EqualTo(expected).Within(0.001));
            first.Tuning.engine.maxTorqueNewtonMeters = 5;
            Assert.That(second.Tuning.engine.maxTorqueNewtonMeters, Is.EqualTo(expected).Within(0.001));
            Assert.That(factory.engine.maxTorqueNewtonMeters, Is.EqualTo(390));
            Assert.That(build.TryRemove(VehiclePerformanceCategory.Engine, out _), Is.True);
            using var restored = VehicleConfigurationResolver.Resolve(factory, build, null, definition: definition);
            Assert.That(restored.Tuning.engine.maxTorqueNewtonMeters, Is.EqualTo(factory.engine.maxTorqueNewtonMeters));
        }

        [Test] public void ModifierOrderDoesNotDependOnInstallOrderAndExplainsContributions()
        {
            var first = new VehiclePerformanceBuild(); var second = new VehiclePerformanceBuild();
            var engine = new Part("engine", VehiclePerformanceCategory.Engine, 1, 10);
            var gearbox = new Part("gearbox", VehiclePerformanceCategory.Transmission, 2, 0);
            first.TryInstall(engine, out _); first.TryInstall(gearbox, out _);
            second.TryInstall(gearbox, out _); second.TryInstall(engine, out _);
            using var a = VehicleConfigurationResolver.Resolve(factory, first, null);
            using var b = VehicleConfigurationResolver.Resolve(factory, second, null);
            Assert.That(a.Tuning.engine.maxTorqueNewtonMeters, Is.EqualTo(800));
            Assert.That(b.Tuning.engine.maxTorqueNewtonMeters, Is.EqualTo(800));
            var row = Find(a, VehiclePhysicsLabTuningParameter.EngineTorque);
            Assert.That(row.factoryValue, Is.EqualTo(390)); Assert.That(row.finalValue, Is.EqualTo(800));
            Assert.That(row.unit, Is.EqualTo("N m")); Assert.That(row.contributions.Count, Is.EqualTo(2));
            Assert.That(row.contributions[0].source, Does.Contain("engine"));
            Assert.That(row.contributions[1].before, Is.EqualTo(400));
        }

        [Test] public void TuningLimitsIntersectDefinitionAndPartAndInvalidCandidateDoesNotMutateFactory()
        {
            var parameter = VehiclePhysicsLabTuningParameter.FinalDrive;
            definition.tuningLimits = new[] { new VehicleTuningLimit { parameter = parameter, minimum = 2, maximum = 5 } };
            var part = new Part("transmission", VehiclePerformanceCategory.Transmission, 1, 0)
            { limits = new[] { new VehicleTuningLimit { parameter = parameter, minimum = 3, maximum = 4 } } };
            var build = new VehiclePerformanceBuild(); build.TryInstall(part, out _);
            Assert.Throws<ArgumentException>(() => VehicleConfigurationResolver.Resolve(factory, build, null,
                new[] { new VehicleTuningAdjustment { parameter = parameter, value = 4.5f } }, definition));
            Assert.That(factory.engine.finalDrive, Is.EqualTo(3.42f));
            using var result = VehicleConfigurationResolver.Resolve(factory, build, null,
                new[] { new VehicleTuningAdjustment { parameter = parameter, value = 3.8f } }, definition);
            Assert.That(result.Tuning.engine.finalDrive, Is.EqualTo(3.8f));
            Assert.That(Find(result, parameter).contributions[0].source, Does.StartWith("Tuning:"));
        }

        [Test] public void ResolverRejectsWrongVariantAndBrokenDependenciesIncludingRemoval()
        {
            var dependency = new Part("engine", VehiclePerformanceCategory.Engine, 1.1f, 0);
            var dependent = new Part("turbo", VehiclePerformanceCategory.ForcedInduction, 1.2f, 0)
            { required = new[] { "engine" }, variants = new[] { "sport" } };
            var build = new VehiclePerformanceBuild(); build.TryInstall(dependent, out _);
            Assert.Throws<ArgumentException>(() => VehicleConfigurationResolver.Resolve(factory, build, null, definition: definition));
            build.TryInstall(dependency, out _);
            using (VehicleConfigurationResolver.Resolve(factory, build, null, definition: definition)) { }
            definition.variantId = "base";
            Assert.Throws<ArgumentException>(() => VehicleConfigurationResolver.Resolve(factory, build, null, definition: definition));
            definition.variantId = "sport"; build.TryRemove(VehiclePerformanceCategory.Engine, out _);
            Assert.Throws<ArgumentException>(() => VehicleConfigurationResolver.Resolve(factory, build, null, definition: definition));
        }

        [Test] public void PhysicsLabResolvesTheSameDefinitionPartsAndAdjustments()
        {
            var setup = ScriptableObject.CreateInstance<RacingVehicleSetup>();
            try
            {
                setup.definition = definition;
                setup.tuningAdjustments = new[] { new VehicleTuningAdjustment { parameter = VehiclePhysicsLabTuningParameter.Mass, value = 1234 } };
                using var resolved = VehicleConfigurationResolver.Resolve(factory, null, null, setup.tuningAdjustments, definition);
                var lab = setup.CreateEffectiveTuning();
                try
                { Assert.That(JsonUtility.ToJson(lab), Is.EqualTo(JsonUtility.ToJson(resolved.Tuning))); }
                finally { UnityEngine.Object.DestroyImmediate(lab); }
            }
            finally { UnityEngine.Object.DestroyImmediate(setup); }
        }

        [Test] public void NonFiniteAndDuplicateTuningAreRejected()
        {
            var parameter = VehiclePhysicsLabTuningParameter.Mass;
            Assert.Throws<ArgumentException>(() => VehicleConfigurationResolver.Resolve(factory, null, null,
                new[] { new VehicleTuningAdjustment { parameter = parameter, value = float.NaN } }, definition));
            Assert.Throws<ArgumentException>(() => VehicleConfigurationResolver.Resolve(factory, null, null,
                new[] { new VehicleTuningAdjustment { parameter = parameter, value = 1200 }, new VehicleTuningAdjustment { parameter = parameter, value = 1300 } }, definition));
        }

        private static VehicleResolvedParameter Find(ResolvedVehicleConfiguration result, VehiclePhysicsLabTuningParameter parameter)
        { foreach (var row in result.Breakdown) if (row.parameter == parameter) return row; throw new AssertionException("Missing parameter breakdown."); }

        private sealed class Part : IVehiclePerformanceUpgrade, IVehiclePerformancePartMetadata
        {
            public Part(string id, VehiclePerformanceCategory category, float multiply, float add)
            { UpgradeId = id; Category = category; this.multiply = multiply; this.add = add; }
            private readonly float multiply, add;
            public string UpgradeId { get; } public string DisplayName => UpgradeId;
            public VehiclePerformanceCategory Category { get; }
            public VehiclePerformanceTier Tier => VehiclePerformanceTier.Street;
            public int Price => 0; public bool IsJunkman => false;
            public string[] required = Array.Empty<string>(), variants = Array.Empty<string>();
            public VehicleTuningLimit[] limits = Array.Empty<VehicleTuningLimit>();
            public IReadOnlyList<string> CompatibleVehicleIds => Array.Empty<string>();
            public IReadOnlyList<string> CompatibleVariantIds => variants;
            public IReadOnlyList<string> RequiredUpgradeIds => required;
            public IReadOnlyList<string> ExcludedUpgradeIds => Array.Empty<string>();
            public IReadOnlyList<VehicleTuningLimit> TuningLimits => limits;
            public VehicleCapabilities RequiredCapabilities => VehicleCapabilities.None;
            public void Apply(VehicleTuning tuning) => tuning.engine.maxTorqueNewtonMeters = tuning.engine.maxTorqueNewtonMeters * multiply + add;
        }
    }
}
