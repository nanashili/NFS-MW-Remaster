#if UNITY_EDITOR
using NUnit.Framework;
using UnityEditor;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleScriptImportTests
    {
        [Test]
        public void GeneratedVehicleAssetsResolveToTheirConcreteScriptTypes()
        {
            Assert.That(
                AssetDatabase.LoadAssetAtPath<VehiclePerformanceCatalog>(
                    "Assets/NfsMw/Modules/Driving/Data/Performance/VehiclePerformanceCatalog.asset"),
                Is.Not.Null,
                "The performance catalog is missing its VehiclePerformanceCatalog script.");
            Assert.That(
                AssetDatabase.LoadAssetAtPath<TunedVehiclePerformanceUpgrade>(
                    "Assets/NfsMw/Modules/Driving/Data/Performance/mw2005_engine_street.asset"),
                Is.Not.Null,
                "The performance upgrade is missing its TunedVehiclePerformanceUpgrade script.");
            Assert.That(
                AssetDatabase.LoadAssetAtPath<VehicleCustomizationCatalog>(
                    "Assets/NfsMw/Modules/Driving/Data/Tests/Shops/VehicleShopTestCustomizationCatalog.asset"),
                Is.Not.Null,
                "The customization catalog is missing its VehicleCustomizationCatalog script.");
            Assert.That(
                AssetDatabase.LoadAssetAtPath<AssetVehicleCustomization>(
                    "Assets/NfsMw/Modules/Driving/Data/Tests/Shops/TestBodyKit.asset"),
                Is.Not.Null,
                "The body-kit product is missing its AssetVehicleCustomization script.");
            Assert.That(
                AssetDatabase.LoadAssetAtPath<AssetVehicleStorefront>(
                    "Assets/NfsMw/Modules/Driving/Data/Tests/Shops/TestBodyShop.asset"),
                Is.Not.Null,
                "The storefront is missing its AssetVehicleStorefront script.");
            Assert.That(
                AssetDatabase.LoadAssetAtPath<AssetVehicleCarStoreProduct>(
                    "Assets/NfsMw/Modules/Driving/Data/Tests/Shops/TestCar.asset"),
                Is.Not.Null,
                "The car-show product is missing its AssetVehicleCarStoreProduct script.");
            Assert.That(
                AssetDatabase.LoadAssetAtPath<VehiclePoliceResponseProfile>(
                    "Assets/NfsMw/Modules/Driving/Data/Career/VehiclePoliceResponseProfile.asset"),
                Is.Not.Null,
                "The police response profile is missing its VehiclePoliceResponseProfile script.");
        }
    }
}
#endif
