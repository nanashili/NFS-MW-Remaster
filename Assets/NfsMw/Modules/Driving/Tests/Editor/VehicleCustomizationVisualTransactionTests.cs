using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleCustomizationVisualTransactionTests
    {
        [Test]
        public void MultiSlotFailureRestoresPreviousReplacementAndMaterials()
        {
            var root = new GameObject("visual adapter");
            var first = MakeSlot(root.transform, VehicleCustomizationCategory.BodyKit, "bodykit");
            var second = MakeSlot(root.transform, VehicleCustomizationCategory.Spoiler, "spoiler");
            var stock = new Material(Shader.Find("Standard"));
            var replacement = new Material(Shader.Find("Standard"));
            var good = new VisualPart("good", VehicleCustomizationCategory.BodyKit, new VehicleCustomizationVisualPayload(new[] { replacement }));
            var bad = new ThrowingVisualPart("bad", VehicleCustomizationCategory.Spoiler);
            var build = new VehicleCustomizationBuild(); build.TryInstall(good, out _);
            var adapter = root.AddComponent<VehicleCustomizationVisualAdapter>();
            first.Configure(VehicleCustomizationCategory.BodyKit, first.transform, new[] { root.AddComponent<MeshRenderer>() }, false);
            var firstRenderer = root.GetComponent<MeshRenderer>(); firstRenderer.sharedMaterials = new[] { stock };
            adapter.Apply(build);
            Material previousMaterial = firstRenderer.sharedMaterials[0];
            build.TryInstall(bad, out _);
            Assert.Throws<InvalidOperationException>(() => adapter.Apply(build));
            Assert.That(firstRenderer.sharedMaterials[0], Is.SameAs(previousMaterial), "Preparation failure must preserve the live object.");
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(stock); UnityEngine.Object.DestroyImmediate(replacement);
        }

        [Test]
        public void PreviewCancelRestoresStockVisual()
        {
            var root = new GameObject("preview vehicle");
            var slot = MakeSlot(root.transform, VehicleCustomizationCategory.BodyKit, "bodykit");
            var renderer = root.AddComponent<MeshRenderer>(); slot.ConfigureSlot("bodykit", slot.transform, new[] { renderer }, false); var stock = new Material(Shader.Find("Standard")); renderer.sharedMaterials = new[] { stock };
            var adapter = root.AddComponent<VehicleCustomizationVisualAdapter>();
            var system = root.AddComponent<VehicleCustomizationSystem>(); system.SetVisualAdapter(adapter);
            var definition = ScriptableObject.CreateInstance<VehicleDefinition>();
            var tuning = VehicleTuning.CreateStreetRacer(); definition.vehicleId = "preview-test"; definition.variantId = "standard"; definition.factoryTuning = tuning; definition.supportedSlots = new[] { "bodykit" };
            Assert.That(root.AddComponent<VehicleConfiguration>().TryConfigure(definition, Array.Empty<VehicleTuningAdjustment>(), out var configureFailure), Is.True, configureFailure);
            var item = new VisualPart("kit", VehicleCustomizationCategory.BodyKit, new VehicleCustomizationVisualPayload(new[] { new Material(Shader.Find("Standard")) }));
            Assert.That(system.BeginPreview(item, out var failure), Is.True, failure);
            system.CancelPreview();
            Assert.That(renderer.sharedMaterials[0], Is.SameAs(stock));
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(stock); UnityEngine.Object.DestroyImmediate(definition); UnityEngine.Object.DestroyImmediate(tuning); UnityEngine.Object.DestroyImmediate(item.Visual.MaterialOverrides[0]);
        }

        [Test]
        public void MaterialOverridesAreIsolatedPerInstance()
        {
            var a = new GameObject("a"); var b = new GameObject("b");
            var source = new Material(Shader.Find("Standard"));
            var overrideMaterial = new Material(Shader.Find("Standard"));
            var ra = a.AddComponent<MeshRenderer>(); var rb = b.AddComponent<MeshRenderer>(); ra.sharedMaterials = new[] { source }; rb.sharedMaterials = new[] { source };
            var sa = a.AddComponent<VehicleCustomizationVisualSlot>(); sa.Configure(VehicleCustomizationCategory.Paint, a.transform, new[] { ra }, false);
            var sb = b.AddComponent<VehicleCustomizationVisualSlot>(); sb.Configure(VehicleCustomizationCategory.Paint, b.transform, new[] { rb }, false);
            var item = new VisualPart("paint", VehicleCustomizationCategory.Paint, new VehicleCustomizationVisualPayload(new[] { overrideMaterial }));
            sa.Apply(item); sb.Apply(item);
            Assert.That(ra.sharedMaterials[0], Is.Not.SameAs(overrideMaterial));
            Assert.That(rb.sharedMaterials[0], Is.Not.SameAs(overrideMaterial));
            Assert.That(ra.sharedMaterials[0], Is.Not.SameAs(rb.sharedMaterials[0]));
            UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(overrideMaterial);
        }

        [Test]
        public void ExistingPropertyBlockSurvivesApplyAndStockRestore()
        {
            var root = new GameObject("property block vehicle");
            var renderer = root.AddComponent<MeshRenderer>();
            var original = new Material(Shader.Find("Standard")); renderer.sharedMaterials = new[] { original };
            var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", Color.cyan); block.SetFloat("_Wetness", .2f); renderer.SetPropertyBlock(block);
            var slot = root.AddComponent<VehicleCustomizationVisualSlot>(); slot.Configure(VehicleCustomizationCategory.Paint, root.transform, new[] { renderer }, false);
            var payload = new VehicleCustomizationVisualPayload(); payload.Configure(null, Array.Empty<Material>(), null, null, Color.red, true, 1f);
            var item = new VisualPart("paint", VehicleCustomizationCategory.Paint, payload);
            slot.Apply(item); var weather = new MaterialPropertyBlock(); renderer.GetPropertyBlock(weather); weather.SetFloat("_Wetness", .8f); renderer.SetPropertyBlock(weather); slot.Apply(null);
            var restored = new MaterialPropertyBlock(); renderer.GetPropertyBlock(restored);
            Assert.That(restored.GetColor("_BaseColor"), Is.EqualTo(Color.cyan));
            Assert.That(restored.GetFloat("_Wetness"), Is.EqualTo(.8f));
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(original);
        }

        private static VehicleCustomizationVisualSlot MakeSlot(Transform parent, VehicleCustomizationCategory category, string id)
        {
            var go = new GameObject(id); go.transform.SetParent(parent); var slot = go.AddComponent<VehicleCustomizationVisualSlot>(); slot.Configure(category, go.transform, Array.Empty<Renderer>(), false); return slot;
        }

        private class VisualPart : IVehicleCustomizationItem, IVehicleCustomizationPartMetadata, IVehicleCustomizationVisualSource
        {
            public VisualPart(string id, VehicleCustomizationCategory category, VehicleCustomizationVisualPayload visual) { CustomizationId = id; Category = category; Visual = visual; }
            public string CustomizationId { get; } public string DisplayName => CustomizationId; public VehicleCustomizationCategory Category { get; } public VehicleCustomizationStyle Style => VehicleCustomizationStyle.Standard; public int Price => 0; public bool IsStock => false; public IReadOnlyList<string> SupportedVehicleIds => Array.Empty<string>(); public IReadOnlyList<string> RequiredSlotIds => Array.Empty<string>(); public IReadOnlyList<string> RequiredPartIds => Array.Empty<string>(); public IReadOnlyList<string> ConflictingPartIds => Array.Empty<string>(); public IReadOnlyList<string> MountSlotIds => new[] { Category.ToString().ToLowerInvariant() }; public float WheelRadiusDelta => 0; public float WheelOffsetDelta => 0; public float TrackWidthDelta => 0; public float ClearanceDelta => 0; public virtual VehicleCustomizationVisualPayload Visual { get; }
        }

        private sealed class ThrowingVisualPart : VisualPart, IVehicleCustomizationVisualSource
        {
            public ThrowingVisualPart(string id, VehicleCustomizationCategory category) : base(id, category, null) { }
            public override VehicleCustomizationVisualPayload Visual => throw new InvalidOperationException("visual construction failed");
        }
    }
}
