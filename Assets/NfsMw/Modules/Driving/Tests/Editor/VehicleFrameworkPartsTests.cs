using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleFrameworkPartsTests
    {
        [Test]
        public void FullKitCanClaimTwoSemanticMountSlots()
        {
            var build = new VehicleCustomizationBuild();
            var kit = new Part("kit", VehicleCustomizationCategory.BodyKit, new[] { "front", "rear" });
            Assert.That(build.TryInstall(kit, "m3", new[] { "front", "rear" }, out var error), Is.True, error);
            Assert.That(build.ValidateComplete("m3", new[] { "front", "rear" }, out error), Is.True, error);
        }

        [Test]
        public void MissingMountSlotAndInvalidFitmentAreRejectedWithoutChangingBuild()
        {
            var build = new VehicleCustomizationBuild();
            var good = new Part("good", VehicleCustomizationCategory.BodyKit, new[] { "front" });
            Assert.That(build.TryInstall(good, "m3", new[] { "front" }, out _), Is.True);
            var bad = new Part("bad", VehicleCustomizationCategory.BodyKit, new[] { "rear" }) { WheelRadiusDelta = 2f };
            Assert.That(build.TryInstall(bad, "m3", new[] { "front" }, out _), Is.False);
            Assert.That(build.Get(VehicleCustomizationCategory.BodyKit).CustomizationId, Is.EqualTo("good"));
        }

        [Test]
        public void DependencyPreventsRemovalAndPreservesInstalledParts()
        {
            var build = new VehicleCustomizationBuild();
            var basePart = new Part("base", VehicleCustomizationCategory.BodyKit, Array.Empty<string>());
            var dependent = new Part("dependent", VehicleCustomizationCategory.Spoiler, Array.Empty<string>(), new[] { "base" });
            Assert.That(build.TryInstall(basePart, out _), Is.True);
            Assert.That(build.TryInstall(dependent, out _), Is.True);
            Assert.That(build.TryRemove(VehicleCustomizationCategory.BodyKit, out var error), Is.False);
            StringAssert.Contains("depends", error);
            Assert.That(build.Get(VehicleCustomizationCategory.BodyKit), Is.SameAs(basePart));
        }

        [Test]
        public void VariantMismatchIsRejectedWithoutChangingBuild()
        {
            var build = new VehicleCustomizationBuild();
            var stock = new VariantPart("stock", "coupe");
            Assert.That(build.TryInstall(stock, "m3", "coupe", Array.Empty<string>(), out _), Is.True);
            var incompatible = new VariantPart("replacement", "convertible");
            Assert.That(build.TryInstall(incompatible, "m3", "coupe", Array.Empty<string>(), out var failure), Is.False);
            StringAssert.Contains("variant", failure);
            Assert.That(build.Get(VehicleCustomizationCategory.BodyKit).CustomizationId, Is.EqualTo("stock"));
        }

        private class Part : IVehicleCustomizationItem, IVehicleCustomizationPartMetadata
        {
            public Part(string id, VehicleCustomizationCategory category, IReadOnlyList<string> mounts, IReadOnlyList<string> required = null)
            { CustomizationId = id; Category = category; MountSlotIds = mounts; RequiredPartIds = required ?? Array.Empty<string>(); }
            public string CustomizationId { get; }
            public string DisplayName => CustomizationId;
            public VehicleCustomizationCategory Category { get; }
            public VehicleCustomizationStyle Style => VehicleCustomizationStyle.Standard;
            public int Price => 0;
            public bool IsStock => false;
            public IReadOnlyList<string> SupportedVehicleIds => Array.Empty<string>();
            public IReadOnlyList<string> RequiredSlotIds => MountSlotIds;
            public IReadOnlyList<string> RequiredPartIds { get; }
            public IReadOnlyList<string> ConflictingPartIds => Array.Empty<string>();
            public IReadOnlyList<string> MountSlotIds { get; }
            public float WheelRadiusDelta { get; set; }
            public float WheelOffsetDelta => 0f;
            public float TrackWidthDelta => 0f;
            public float ClearanceDelta => 0f;
        }

        private sealed class VariantPart : Part, IVehicleCustomizationVariantMetadata
        {
            public VariantPart(string id, string variant) : base(id, VehicleCustomizationCategory.BodyKit, Array.Empty<string>()) { SupportedVariantIds = new[] { variant }; }
            public IReadOnlyList<string> SupportedVariantIds { get; }
        }
    }
}
