using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using NfsMwRemaster.Driving;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class HdrpQualityTests
    {
        [Test]
        public void FiveDefaultsMatchPlayerQualityOrder()
        {
            string[] names = { "Very Low", "Low", "Medium", "High", "Ultra" };
            for (int i = 0; i < names.Length; i++)
            {
                HdrpQualityPreset preset = HdrpQualityPreset.Defaults((HdrpQualityTier)i);
                Assert.That(preset.displayName, Is.EqualTo(names[i]));
                Assert.That(preset.shadowDistance, Is.GreaterThan(0));
                Assert.That(preset.resolutionScale, Is.InRange(.5f, 1f));
                Assert.That(preset.textureMipmapLimit, Is.InRange(0, 3));
            }
            Assert.That(HdrpQualityPreset.Defaults(HdrpQualityTier.VeryLow).shadowDistance,
                Is.LessThan(HdrpQualityPreset.Defaults(HdrpQualityTier.Ultra).shadowDistance));
            Assert.That(HdrpQualityPreset.Defaults(HdrpQualityTier.Medium).taaQuality,
                Is.LessThanOrEqualTo(HdrpQualityPreset.Defaults(HdrpQualityTier.Ultra).taaQuality));
        }

        [Test]
        public void GeneratedCatalogContainsAllFivePipelineEntries()
        {
            const string catalogPath = "Assets/NfsMw/Settings/Rendering/HDRP/Resources/NFS HDRP Quality Profiles.asset";
            var catalog = AssetDatabase.LoadMainAssetAtPath(catalogPath);
            Assert.That(catalog, Is.Not.Null);
            var serialized = new SerializedObject(catalog);
            Assert.That(serialized.FindProperty("tiers").arraySize, Is.EqualTo(5));
            Assert.That(QualitySettings.names, Is.EqualTo(new[] { "Very Low", "Low", "Medium", "High", "Ultra" }));
            for (int i = 0; i < 5; i++)
                Assert.That(QualitySettings.GetRenderPipelineAssetAt(i), Is.Not.Null, "quality " + i);
        }
    }
}
