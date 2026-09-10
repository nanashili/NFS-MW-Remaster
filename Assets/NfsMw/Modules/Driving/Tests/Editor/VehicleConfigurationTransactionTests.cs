using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleConfigurationTransactionTests
    {
        private readonly List<Object> owned = new();
        private VehicleDefinition definition;
        private VehicleConfiguration configuration;
        private VehiclePerformanceSystem performance;
        private VehicleCustomizationSystem customization;
        private TunedVehiclePerformanceUpgrade engine, transmission;
        private AssetVehicleCustomization kit;
        private GameObject root;
        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
        [SetUp] public void SetUp()
        {
            definition = Own(ScriptableObject.CreateInstance<VehicleDefinition>());
            definition.vehicleId = "test-hatch"; definition.variantId = "sport";
            definition.factoryTuning = Own(VehicleTuning.CreateStreetRacer());
            definition.capabilities = VehicleCapabilities.All;
            engine = Own(ScriptableObject.CreateInstance<TunedVehiclePerformanceUpgrade>());
            engine.ConfigureMetadata("engine", "Engine", VehiclePerformanceCategory.Engine, VehiclePerformanceTier.Street, 100, false);
            engine.Modifier.engineTorqueMultiplier = 1.2f;
            transmission = Own(ScriptableObject.CreateInstance<TunedVehiclePerformanceUpgrade>());
            transmission.ConfigureMetadata("gears", "Gears", VehiclePerformanceCategory.Transmission, VehiclePerformanceTier.Street, 100, false);
            SetStrings(transmission, "requiredUpgradeIds", "engine");
            kit = Own(ScriptableObject.CreateInstance<AssetVehicleCustomization>());
            kit.ConfigureMetadata("kit", "Kit", VehicleCustomizationCategory.BodyKit, VehicleCustomizationStyle.Standard, 100, false);
            kit.PhysicalModifier.massMultiplier = .9f;
            definition.performanceCatalog = Own(ScriptableObject.CreateInstance<VehiclePerformanceCatalog>());
            definition.performanceCatalog.SetUpgrades(new VehiclePerformanceUpgradeDefinition[] { engine, transmission });
            definition.customizationCatalog = Own(ScriptableObject.CreateInstance<VehicleCustomizationCatalog>());
            definition.customizationCatalog.SetItems(new VehicleCustomizationDefinition[] { kit });
            root = Own(new GameObject("transaction-car")); root.SetActive(false);
            configuration = root.AddComponent<VehicleConfiguration>();
            customization = root.AddComponent<VehicleCustomizationSystem>(); performance = root.GetComponent<VehiclePerformanceSystem>();
            Assert.That(configuration.TryConfigure(definition, null, out string failure), Is.True, failure);
        }
        [TearDown] public void TearDown()
        { for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) Object.DestroyImmediate(owned[i]); owned.Clear(); }

        [Test] public void PreviewIsEffectiveButNeverCapturedAndSurvivesReconfigure()
        {
            Assert.That(customization.BeginPreview(kit, out var failure), Is.True, failure);
            var saved = new CareerVehicleData(); customization.Capture(saved);
            Assert.That(saved.customizationIds, Is.Empty);
            Assert.That(customization.Build.Installed, Is.Empty);
            using (var resolved = VehicleConfigurationResolver.Resolve(definition.factoryTuning, performance.Build, customization.EffectiveBuild, definition: definition))
                Assert.That(resolved.Tuning.chassis.mass, Is.EqualTo(1305).Within(.01));
            var context = new VehicleModuleContext(root.GetComponent<VehicleController>(), root.GetComponent<Rigidbody>(), definition.factoryTuning, Array.Empty<VehicleWheel>());
            performance.Initialize(context); customization.Initialize(context);
            Assert.That(customization.IsPreviewing, Is.True);
            Assert.That(performance.Resolved.Tuning.chassis.mass, Is.EqualTo(1305).Within(.01));
            customization.CancelPreview();
            Assert.That(customization.EffectiveBuild.Installed, Is.Empty);
            Assert.That(definition.factoryTuning.chassis.mass, Is.EqualTo(1450));
        }
        [Test] public void UndoRedoRebuildsInstalledStateFromSerializedParts()
        {
            Undo.RegisterCompleteObjectUndo(performance, "Install engine");
            Assert.That(performance.TryInstall(engine, out var failure), Is.True, failure);
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(performance.Build.Installed, Is.Empty);
            Undo.PerformRedo(); Assert.That(performance.GetInstalled(VehiclePerformanceCategory.Engine), Is.SameAs(engine));
            Undo.RegisterCompleteObjectUndo(customization, "Install kit");
            Assert.That(customization.TryInstall(kit, out failure), Is.True, failure);
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Assert.That(customization.Build.Installed, Is.Empty);
            Undo.PerformRedo(); Assert.That(customization.GetInstalled(VehicleCustomizationCategory.BodyKit), Is.SameAs(kit));
            Undo.ClearUndo(performance); Undo.ClearUndo(customization);
        }
        [Test] public void PreviewCommitPersistsAndFailedRestorePreservesInstalledState()
        {
            Assert.That(customization.BeginPreview(kit, out var failure), Is.True, failure);
            Assert.That(customization.TryApplyPreview(out failure), Is.True, failure);
            var saved = new CareerVehicleData(); customization.Capture(saved);
            CollectionAssert.AreEqual(new[] { "kit" }, saved.customizationIds);
            saved.customizationIds.Add("missing");
            Assert.That(customization.Restore(saved, out failure), Is.False);
            Assert.That(customization.GetInstalled(VehicleCustomizationCategory.BodyKit), Is.SameAs(kit));
            var recaptured = new CareerVehicleData(); customization.Capture(recaptured);
            CollectionAssert.AreEqual(new[] { "kit" }, recaptured.customizationIds);
        }
        [Test] public void DependentInstallAndRemovalValidateWholeCandidateBeforeCommit()
        {
            Assert.That(performance.CanInstall(transmission, out _), Is.False);
            Assert.That(performance.TryInstall(transmission, out _), Is.False);
            Assert.That(performance.Build.Installed, Is.Empty);
            Assert.That(performance.TryInstall(engine, out var failure), Is.True, failure);
            Assert.That(performance.TryInstall(transmission, out failure), Is.True, failure);
            Assert.That(performance.TryRemove(VehiclePerformanceCategory.Engine, out failure), Is.False);
            Assert.That(performance.GetInstalled(VehiclePerformanceCategory.Engine), Is.SameAs(engine));
        }
        [Test] public void RestoreStagesAllDependenciesIndependentOfOrderAndRejectsVariantWithoutMutation()
        {
            var saved = new CareerVehicleData { vehicleId = "garage-instance-17", vehicleDefinitionId = definition.vehicleId,
                vehicleVariantId = definition.variantId, performanceUpgradeIds = new() { "gears", "engine" }, customizationIds = new() { "kit" },
                tuningAdjustments = new() { new VehicleTuningAdjustment { parameter = VehiclePhysicsLabTuningParameter.Mass, value = 1200 } } };
            Assert.That(configuration.Restore(saved, out var failure), Is.True, failure);
            Assert.That(performance.Build.Installed.Count, Is.EqualTo(2));
            Assert.That(customization.Build.Installed.Count, Is.EqualTo(1));
            Assert.That(configuration.Adjustments[0].value, Is.EqualTo(1200));
            saved.vehicleVariantId = "wrong"; saved.performanceUpgradeIds.Clear();
            Assert.That(configuration.Restore(saved, out failure), Is.False);
            Assert.That(performance.Build.Installed.Count, Is.EqualTo(2));
        }
        [Test] public void LegacyInstanceIdentityDoesNotBecomeFactoryIdentity()
        {
            var legacy = new CareerVehicleData { vehicleId = "garage-instance-17", schemaVersion = 1 };
            legacy.Normalize();
            Assert.That(configuration.Restore(legacy, out var failure), Is.True, failure);
            configuration.Capture(legacy);
            Assert.That(legacy.vehicleId, Is.EqualTo("garage-instance-17"));
            Assert.That(legacy.vehicleDefinitionId, Is.EqualTo(definition.vehicleId));
            Assert.That(legacy.vehicleVariantId, Is.EqualTo(definition.variantId));
        }
        private static void SetStrings(Object target, string name, params string[] values)
        { using var serialized = new SerializedObject(target); var field = serialized.FindProperty(name); field.arraySize = values.Length;
          for (int i = 0; i < values.Length; i++) field.GetArrayElementAtIndex(i).stringValue = values[i]; serialized.ApplyModifiedPropertiesWithoutUndo(); }
    }
}
