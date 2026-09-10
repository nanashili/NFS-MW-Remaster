using System;
using System.IO;
using System.Linq;
using NfsMwRemaster.Driving.Editor;
using NfsMwRemaster.Driving.Editor.Workspace;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleProfileStudioTests
    {
        private string folder;
        private VehicleIdentityCatalog catalog;
        private VehicleProfileDraft draft;

        [SetUp]
        public void SetUp()
        {
            string name = "VehicleProfileTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name); folder = "Assets/" + name;
            catalog = Create<VehicleIdentityCatalog>("Catalogue");
            draft = Create<VehicleProfileDraft>("Draft"); draft.catalog = catalog;
            var brand = catalog.AddBrand("Synthetic Manufacturer");
            var model = catalog.AddModel(brand.Id, "Test Rig", "Fixture");
            catalog.AddYear(model.Id, 2005);
            var variant = catalog.AddVariant(model.Id, 2005, "Synthetic");
            draft.modelId = model.Id; draft.variantId = variant.Id; draft.modelYear = 2005;
        }

        [TearDown]
        public void TearDown() { if (!string.IsNullOrEmpty(folder)) AssetDatabase.DeleteAsset(folder); }

        [Test]
        public void LabelsDoNotChangeIdentityAndProductionYearsRemainUnknown()
        {
            var model = catalog.FindModel(draft.modelId);
            string identity = draft.Id, modelIdentity = model.Id, variant = draft.variantId;
            catalog.FindBrand(model.brandId).displayName = "Renamed Brand";
            model.displayName = "Renamed Model"; model.variants[0].displayName = "Renamed Variant";
            Assert.That(draft.Id, Is.EqualTo(identity)); Assert.That(model.Id, Is.EqualTo(modelIdentity));
            Assert.That(draft.variantId, Is.EqualTo(variant));
            Assert.That(model.productionStartYear, Is.Zero); Assert.That(model.productionEndYear, Is.Zero);
            Assert.That(catalog.ValidateRecords(), Is.Empty);
        }

        [Test]
        public void DuplicateYearAndUndeclaredVariantYearAreRejected()
        {
            Assert.Throws<ArgumentException>(() => catalog.AddYear(draft.modelId, 2005));
            Assert.Throws<ArgumentException>(() => catalog.AddVariant(draft.modelId, 2006, "Unknown"));
            Assert.Throws<ArgumentException>(() => catalog.AddModel("missing", "Model", ""));
            Assert.Throws<ArgumentException>(() => catalog.AddBrand(" "));
        }

        [Test]
        public void ReferencedVariantCannotBeRemoved()
        {
            Assert.Throws<InvalidOperationException>(() => catalog.RemoveVariant(draft.modelId, draft.variantId, new[] { draft }));
            catalog.RemoveVariant(draft.modelId, draft.variantId, Array.Empty<VehicleProfileDraft>());
            Assert.That(catalog.FindModel(draft.modelId).variants, Is.Empty);
        }

        [Test]
        public void DuplicateProfileTupleIsAnErrorRegardlessOfDisplayName()
        {
            var other = Create<VehicleProfileDraft>("DifferentLabel");
            other.catalog = catalog; other.modelId = draft.modelId; other.modelYear = 2005; other.variantId = draft.variantId;
            var report = VehicleProfileValidation.Inspect(draft, new[] { draft, other });
            Assert.That(report.Issues.Any(i => i.Code == "VP_DUPLICATE_IDENTITY" && i.Target == other), Is.True);
        }

        [TestCase("..")]
        [TestCase("CON")]
        [TestCase("con.asset")]
        [TestCase("LPT9.asset")]
        [TestCase("folder.")]
        [TestCase("folder ")]
        [TestCase("a/b")]
        [TestCase("a\\b")]
        [TestCase("a:b")]
        [TestCase("e\u0301")]
        public void InvalidCrossPlatformSegmentsAreRejected(string segment)
            => Assert.Throws<ArgumentException>(() => VehicleProfilePublication.ValidateSegment(segment));

        [TestCase("BMW")]
        [TestCase("2005")]
        [TestCase("Vehicle Listing.asset")]
        [TestCase("é")]
        public void ValidSegmentsAreAccepted(string segment)
            => Assert.DoesNotThrow(() => VehicleProfilePublication.ValidateSegment(segment));

        [Test]
        public void OutputTraversalAndCaseCollisionAreRejected()
        {
            Assert.Throws<ArgumentException>(() => VehicleProfilePublication.ValidateOutputPath(folder + "/../Listing.asset"));
            Assert.Throws<ArgumentException>(() => VehicleProfilePublication.ValidateOutputPath(folder + "/catalogue.asset"));
            Assert.That(VehicleProfilePublication.ValidateOutputPath(folder + "/Listing.asset"), Is.EqualTo(folder + "/Listing.asset"));
        }

        [Test]
        public void SchemaFutureVersionFailsClosedWithoutMutation()
        {
            var serialized = new SerializedObject(draft);
            serialized.FindProperty("schema").intValue = 999;
            serialized.ApplyModifiedPropertiesWithoutUndo(); serialized.Dispose();
            Assert.That(VehicleProfileValidation.Inspect(draft, Array.Empty<VehicleProfileDraft>()).Issues.Any(i => i.Code == "VP_SCHEMA"), Is.True);
            Assert.That(draft.Schema, Is.EqualTo(999));
        }

        [Test]
        public void IncompleteProfileCannotBePublished()
        {
            var report = VehicleProfileValidation.Inspect(draft, new[] { draft });
            Assert.That(report.CanPublish, Is.False);
            Assert.That(report.Issues.Any(i => i.Code == "VP_PREFAB"), Is.True);
            Assert.That(report.Issues.Single(i => i.Code == "VP_LOGO").Severity, Is.EqualTo(VehicleProfileSeverity.Warning));
            Assert.Throws<InvalidOperationException>(() => VehicleProfilePublication.Prepare(draft, folder + "/Listing.asset"));
        }

        [Test]
        public void PublicationUsesExistingStoreContractAndDoesNotCopyPrefab()
        {
            AssembleFixture();
            var plan = VehicleProfilePublication.Prepare(draft, folder + "/Listing.asset");
            var product = VehicleProfilePublication.Apply(plan);
            Assert.That(product.VehiclePrefab, Is.SameAs(draft.vehiclePrefab));
            Assert.That(product.VehicleDefinitionId, Is.EqualTo(draft.Id));
            Assert.That(draft.storeCatalog.Find(draft.Id), Is.SameAs(product));
            Assert.That(AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(product)), Does.Not.Contain(AssetDatabase.GetAssetPath(draft)));
            Assert.That(product.IsAvailable, Is.False, "Draft availability defaults to not for sale.");
        }

        [Test]
        public void RepeatPublicationDoesNotWriteOrChangeGuid()
        {
            AssembleFixture();
            var product = VehicleProfilePublication.Apply(VehicleProfilePublication.Prepare(draft, folder + "/Listing.asset"));
            string path = AssetDatabase.GetAssetPath(product), guid = AssetDatabase.AssetPathToGUID(path);
            byte[] bytes = File.ReadAllBytes(path);
            DateTime timestamp = File.GetLastWriteTimeUtc(path);
            var repeat = VehicleProfilePublication.Prepare(draft, "unused");
            Assert.That(repeat.Unchanged, Is.True);
            Assert.That(VehicleProfilePublication.Apply(repeat), Is.SameAs(product));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
            Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(timestamp));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
        }

        [Test]
        public void ChangedInputInvalidatesPlanAndUpdatePreservesGuid()
        {
            AssembleFixture();
            var oldPlan = VehicleProfilePublication.Prepare(draft, folder + "/Listing.asset");
            draft.price = 77; Save(draft);
            Assert.Throws<InvalidOperationException>(() => VehicleProfilePublication.Apply(oldPlan));
            var product = VehicleProfilePublication.Apply(VehicleProfilePublication.Prepare(draft, folder + "/Listing.asset"));
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(product));
            draft.price = 99; Save(draft);
            var updated = VehicleProfilePublication.Apply(VehicleProfilePublication.Prepare(draft, "unused"));
            Assert.That(updated.Price, Is.EqualTo(99));
            Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(updated)), Is.EqualTo(guid));
        }

        [Test]
        public void ArtistEditedListingIsNotOverwritten()
        {
            AssembleFixture();
            var product = VehicleProfilePublication.Apply(VehicleProfilePublication.Prepare(draft, folder + "/Listing.asset"));
            product.ConfigureMetadata(draft.Id, "Artist label", draft.Id, 500, true); Save(product);
            Assert.Throws<InvalidOperationException>(() => VehicleProfilePublication.Prepare(draft, "unused"));
            Assert.That(product.DisplayName, Is.EqualTo("Artist label"));
        }

        [Test]
        public void RenamingDoesNotRelocatePublishedAssets()
        {
            AssembleFixture();
            var product = VehicleProfilePublication.Apply(VehicleProfilePublication.Prepare(draft, folder + "/Listing.asset"));
            string path = AssetDatabase.GetAssetPath(product);
            catalog.FindModel(draft.modelId).displayName = "New label"; Save(catalog);
            var plan = VehicleProfilePublication.Prepare(draft, "ignored/new/location");
            Assert.That(plan.OutputPath, Is.EqualTo(path));
            VehicleProfilePublication.Apply(plan);
            Assert.That(product.DisplayName, Does.Contain("New label"));
        }

        [Test]
        public void BudgetFailureCanBeWarningOrEnforcedWithoutChangingContent()
        {
            AssembleFixture(); draft.budget.maximumLod0Triangles = 1;
            var warning = VehicleProfileValidation.Inspect(draft, new[] { draft });
            Assert.That(warning.Issues.Single(i => i.Code == "VP_TRIANGLES").Severity, Is.EqualTo(VehicleProfileSeverity.Warning));
            draft.budget.enforceAsErrors = true;
            Assert.That(VehicleProfileValidation.Inspect(draft, new[] { draft }).CanPublish, Is.False);
        }

        [Test]
        public void PreviewLeaseIsExclusiveAndReleasedOnFailureAndStopAll()
        {
            int before = RacingPreviewSessions.Count;
            var empty = new GameObject("Empty source");
            GameObject prefab;
            try { prefab = PrefabUtility.SaveAsPrefabAsset(empty, folder + "/Empty.prefab"); }
            finally { Object.DestroyImmediate(empty); }
            Assert.Throws<ArgumentException>(() => new VehicleProfileMeshPreview(prefab));
            Assert.That(RacingPreviewSessions.Count, Is.EqualTo(before));
            AssembleFixture();
            using (var preview = new VehicleProfileMeshPreview(draft.vehiclePrefab))
            {
                Assert.That(preview.DrawCount, Is.GreaterThan(0));
                Assert.Throws<InvalidOperationException>(() => new VehicleProfileMeshPreview(draft.vehiclePrefab));
                Assert.That(RacingPreviewSessions.Count, Is.EqualTo(before + 1));
                RacingPreviewSessions.StopAll();
                Assert.That(preview.IsDisposed, Is.True);
            }
            Assert.That(RacingPreviewSessions.Count, Is.Zero);
        }

        [Test]
        public void SerializedCatalogueReloadKeepsStableIdentityAndSharedLogoReference()
        {
            string id = catalog.Brands[0].Id, modelId = draft.modelId;
            Save(catalog);
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(catalog), ImportAssetOptions.ForceUpdate);
            var loaded = AssetDatabase.LoadAssetAtPath<VehicleIdentityCatalog>(folder + "/Catalogue.asset");
            Assert.That(loaded.Brands[0].Id, Is.EqualTo(id));
            Assert.That(loaded.FindModel(modelId), Is.Not.Null);
            Assert.That(loaded.ValidateRecords(), Is.Empty);
        }

        [Test]
        public void StaticAssemblyBuildsExistingControllerAndPreservesSharedMeshes()
        {
            PrepareAssembly();
            var prefab = VehicleProfileAssembly.CreatePrefab(draft, folder + "/Assembled.prefab");
            var controller = prefab.GetComponent<VehicleController>();
            Assert.That(controller, Is.Not.Null); Assert.That(controller.Tuning, Is.SameAs(draft.tuning));
            Assert.That(controller.Wheels.Length, Is.EqualTo(4));
            Assert.That(controller.Wheels.All(w => w.transform.IsChildOf(prefab.transform)), Is.True);
            Assert.That(prefab.GetComponent<BoxCollider>().size, Is.EqualTo(draft.assembly.colliderSize));
            Assert.That(prefab.GetComponentsInChildren<MeshFilter>(true).All(m => m.sharedMesh == draft.assembly.bodySource.GetComponent<MeshFilter>().sharedMesh), Is.True);
            Assert.That(prefab.GetComponentsInChildren<BoxCollider>(true).Length, Is.EqualTo(1), "Artist colliders are not copied.");
            Assert.That(prefab.transform.Find("Sockets/exhaust-left"), Is.Not.Null);
            Assert.Throws<ArgumentException>(() => VehicleProfileAssembly.CreatePrefab(draft, folder + "/Assembled.prefab"));
        }

        [Test]
        public void InvalidWheelAndDuplicateSocketBindingsFailBeforeWriting()
        {
            PrepareAssembly(); draft.assembly.wheels[0].suspensionAnchor = Vector3.zero;
            draft.assembly.sockets = new[] { new VehicleAssemblySocket { id = "exhaust" }, new VehicleAssemblySocket { id = "exhaust" } };
            Assert.That(VehicleProfileAssembly.Validate(draft).Length, Is.GreaterThanOrEqualTo(2));
            Assert.Throws<InvalidOperationException>(() => VehicleProfileAssembly.CreatePrefab(draft, folder + "/Invalid.prefab"));
            Assert.That(File.Exists(folder + "/Invalid.prefab"), Is.False);
        }

        [Test]
        public void AssembledInstancesHaveIndependentEffectiveTuningAndLocalWheels()
        {
            PrepareAssembly();
            var prefab = VehicleProfileAssembly.CreatePrefab(draft, folder + "/Independent.prefab");
            var first = Object.Instantiate(prefab); var second = Object.Instantiate(prefab);
            try
            {
                var a = first.GetComponent<VehicleController>(); var b = second.GetComponent<VehicleController>();
                a.ConfigureForRuntime(draft.tuning, null, a.Wheels);
                b.ConfigureForRuntime(draft.tuning, null, b.Wheels);
                Assert.That(a.Tuning, Is.Not.SameAs(b.Tuning));
                Assert.That(a.Tuning, Is.Not.SameAs(draft.tuning));
                float stock = draft.tuning.chassis.mass;
                a.Tuning.chassis.mass = stock + 100;
                Assert.That(b.Tuning.chassis.mass, Is.EqualTo(stock));
                Assert.That(draft.tuning.chassis.mass, Is.EqualTo(stock));
                Assert.That(a.Wheels.All(w => w.transform.IsChildOf(first.transform)), Is.True);
                Assert.That(b.Wheels.All(w => w.transform.IsChildOf(second.transform)), Is.True);
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        [Test]
        public void WheelVisualBasisDefaultRemainsCompatibleAndImportedBasisIsExplicit()
        {
            var root = new GameObject("Wheel Basis Test");
            try
            {
                var wheel = root.AddComponent<VehicleWheel>();
                var visual = new GameObject("Visual"); visual.transform.SetParent(root.transform);
                wheel.Setup(VehicleAxle.Front, true, false, false, visual.transform);
                wheel.ApplyVisualPose(0);
                Assert.That(Quaternion.Angle(visual.transform.rotation, Quaternion.Euler(0, 0, 90)), Is.LessThan(.01f));
                wheel.SetVisualRotationOffset(Vector3.zero); wheel.ApplyVisualPose(0);
                Assert.That(Quaternion.Angle(visual.transform.rotation, Quaternion.identity), Is.LessThan(.01f));
                Assert.Throws<ArgumentException>(() => wheel.SetVisualRotationOffset(new Vector3(float.NaN, 0, 0)));
            }
            finally { Object.DestroyImmediate(root); }
        }

        private void PrepareAssembly()
        {
            draft.tuning = Create<VehicleTuning>("AssemblyTuning");
            var source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject asset;
            try { asset = PrefabUtility.SaveAsPrefabAsset(source, folder + "/SourceGeometry.prefab"); }
            finally { Object.DestroyImmediate(source); }
            draft.assembly.bodySource = asset;
            // Independent references to the same mesh asset intentionally share immutable geometry.
            var wheelSource = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject wheelAsset;
            try { wheelAsset = PrefabUtility.SaveAsPrefabAsset(wheelSource, folder + "/WheelGeometry.prefab"); }
            finally { Object.DestroyImmediate(wheelSource); }
            draft.assembly.colliderSize = new Vector3(1.8f, .5f, 4);
            draft.assembly.sockets = new[] { new VehicleAssemblySocket { id = "exhaust-left", position = new Vector3(-.5f, 0, -2) } };
            for (int i = 0; i < 4; i++)
            {
                var wheel = draft.assembly.wheels[i]; wheel.visualSource = wheelAsset;
                wheel.suspensionAnchor = new Vector3(i % 2 == 0 ? -.8f : .8f, .4f, i < 2 ? 1.4f : -1.4f);
                wheel.driven = i >= 2; wheel.handbrake = i >= 2;
            }
        }

        private T Create<T>(string name) where T : ScriptableObject
        { var asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, folder + "/" + name + ".asset"); return asset; }
        private static void Save(Object asset) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }

        private void AssembleFixture()
        {
            draft.tuning = Create<VehicleTuning>("Tuning");
            draft.storeCatalog = Create<VehicleStoreCatalog>("Store");
            draft.budget.requireLods = false;
            var root = new GameObject("Synthetic Vehicle");
            root.SetActive(false);
            try
            {
                var controller = root.AddComponent<VehicleController>(); controller.Tuning = draft.tuning;
                controller.Wheels = new VehicleWheel[4];
                for (int i = 0; i < 4; i++)
                {
                    var child = new GameObject("Wheel" + i); child.transform.SetParent(root.transform);
                    controller.Wheels[i] = child.AddComponent<VehicleWheel>();
                }
                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube); visual.transform.SetParent(root.transform);
                draft.vehiclePrefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Vehicle.prefab");
            }
            finally { Object.DestroyImmediate(root); }
            Save(catalog); Save(draft.storeCatalog); Save(draft);
        }
    }
}
