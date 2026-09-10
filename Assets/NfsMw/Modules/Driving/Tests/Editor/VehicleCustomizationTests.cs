using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleCustomizationTests
    {
        [Test]
        public void InstallUsesOneSlotPerCategoryAndPreservesOtherCategories()
        {
            VehicleCustomizationBuild build = new VehicleCustomizationBuild();
            FakeCustomization bodyKitA = new FakeCustomization(
                "bodykit_01",
                VehicleCustomizationCategory.BodyKit);
            FakeCustomization bodyKitB = new FakeCustomization(
                "bodykit_02",
                VehicleCustomizationCategory.BodyKit);
            FakeCustomization spoiler = new FakeCustomization(
                "spoiler_01",
                VehicleCustomizationCategory.Spoiler);

            Assert.That(build.TryInstall(bodyKitA, out string firstFailure), Is.True, firstFailure);
            Assert.That(build.TryInstall(spoiler, out string secondFailure), Is.True, secondFailure);
            Assert.That(build.TryInstall(bodyKitB, out string replacementFailure), Is.True, replacementFailure);

            Assert.That(build.Installed.Count, Is.EqualTo(2));
            Assert.That(
                build.Get(VehicleCustomizationCategory.BodyKit),
                Is.SameAs(bodyKitB));
            Assert.That(
                build.Get(VehicleCustomizationCategory.Spoiler),
                Is.SameAs(spoiler));
        }

        [Test]
        public void RemovingCategoryReturnsItToStock()
        {
            VehicleCustomizationBuild build = new VehicleCustomizationBuild();
            FakeCustomization vinyl = new FakeCustomization(
                "vinyl_flame_01",
                VehicleCustomizationCategory.Vinyl,
                VehicleCustomizationStyle.Flame);

            Assert.That(build.TryInstall(vinyl, out string installFailure), Is.True, installFailure);
            Assert.That(
                build.TryRemove(VehicleCustomizationCategory.Vinyl, out string removeFailure),
                Is.True,
                removeFailure);
            Assert.That(build.Get(VehicleCustomizationCategory.Vinyl), Is.Null);
            Assert.That(
                build.TryRemove(VehicleCustomizationCategory.Vinyl, out _),
                Is.False);
        }

        [Test]
        public void StockAndDuplicateItemsAreRejected()
        {
            VehicleCustomizationBuild build = new VehicleCustomizationBuild();
            FakeCustomization stock = new FakeCustomization(
                "stock_bodykit",
                VehicleCustomizationCategory.BodyKit,
                VehicleCustomizationStyle.Standard,
                true);
            FakeCustomization paint = new FakeCustomization(
                "paint_black",
                VehicleCustomizationCategory.Paint);

            Assert.That(build.TryInstall(stock, out string stockFailure), Is.False);
            Assert.That(stockFailure, Does.Contain("removal state"));
            Assert.That(build.TryInstall(paint, out string installFailure), Is.True, installFailure);
            Assert.That(build.TryInstall(paint, out string duplicateFailure), Is.False);
            Assert.That(duplicateFailure, Does.Contain("already installed"));
        }

        [Test]
        public void CustomInterfaceItemDoesNotRequireAUnityAsset()
        {
            VehicleCustomizationBuild build = new VehicleCustomizationBuild();
            FakeCustomization gauge = new FakeCustomization(
                "gauge_neon_blue",
                VehicleCustomizationCategory.CustomGauges);

            Assert.That(build.TryInstall(gauge, out string failure), Is.True, failure);
            Assert.That(
                build.Get(VehicleCustomizationCategory.CustomGauges).CustomizationId,
                Is.EqualTo("gauge_neon_blue"));
        }

        [Test]
        public void AuthoredFitmentChangesRadiusClearanceAndLateralOffset()
        {
            var item = ScriptableObject.CreateInstance<TestCustomization>();
            var tuning = VehicleTuning.CreateStreetRacer();
            float stockRadius = tuning.tires.wheelRadius;
            float stockClearance = tuning.tires.suspensionRestLength;
            item.ConfigurePhysicalFitment(0.05f, 0.03f, 0.20f, 0.04f);
            item.ApplyPhysicalEffects(tuning);
            Assert.That(tuning.tires.wheelRadius, Is.EqualTo(stockRadius + 0.05f).Within(0.0001f));
            Assert.That(tuning.tires.wheelLateralOffset, Is.EqualTo(0.13f).Within(0.0001f));
            Assert.That(tuning.tires.suspensionRestLength, Is.EqualTo(stockClearance + 0.04f).Within(0.0001f));
            Object.DestroyImmediate(item); Object.DestroyImmediate(tuning);
        }

        [Test]
        public void WheelFitmentReconfigureRestoresAuthoredAnchorAndVisualScale()
        {
            var root = new GameObject("wheel fitment test");
            var body = root.AddComponent<Rigidbody>();
            var wheelObject = new GameObject("wheel"); wheelObject.transform.SetParent(root.transform, false);
            wheelObject.transform.localPosition = new Vector3(-1f, 0f, 0f);
            var visual = new GameObject("visual").transform; visual.SetParent(wheelObject.transform, false);
            visual.localScale = new Vector3(1f, 2f, 1f);
            var wheel = wheelObject.AddComponent<VehicleWheel>(); wheel.Setup(VehicleAxle.Front, true, true, false, visual);
            var tuning = VehicleTuning.CreateStreetRacer();
            Vector3 stockAnchor = wheelObject.transform.localPosition; Vector3 stockScale = visual.localScale;
            wheel.Configure(body, tuning);
            tuning.tires.wheelRadius += 0.1f; tuning.tires.wheelLateralOffset = 0.2f;
            wheel.Configure(body, tuning);
            Assert.That(wheelObject.transform.localPosition.x, Is.EqualTo(-1.2f).Within(0.0001f));
            Assert.That(wheel.Radius, Is.EqualTo(tuning.tires.wheelRadius).Within(0.0001f));
            Assert.That(visual.localScale.x, Is.EqualTo(stockScale.x * (tuning.tires.wheelRadius / 0.34f)).Within(0.0001f));
            var restored = VehicleTuning.CreateStreetRacer();
            wheel.Configure(body, restored);
            Assert.That(wheelObject.transform.localPosition, Is.EqualTo(stockAnchor));
            Assert.That(visual.localScale, Is.EqualTo(stockScale));
            Object.DestroyImmediate(restored); Object.DestroyImmediate(tuning); Object.DestroyImmediate(root);
        }

        public sealed class TestCustomization : VehicleCustomizationDefinition { }

        private sealed class FakeCustomization : IVehicleCustomizationItem
        {
            public FakeCustomization(
                string id,
                VehicleCustomizationCategory category,
                VehicleCustomizationStyle style = VehicleCustomizationStyle.Standard,
                bool isStock = false)
            {
                CustomizationId = id;
                DisplayName = id;
                Category = category;
                Style = style;
                IsStock = isStock;
            }

            public string CustomizationId { get; }

            public string DisplayName { get; }

            public VehicleCustomizationCategory Category { get; }

            public VehicleCustomizationStyle Style { get; }

            public int Price
            {
                get { return 0; }
            }

            public bool IsStock { get; }
        }
    }
}
