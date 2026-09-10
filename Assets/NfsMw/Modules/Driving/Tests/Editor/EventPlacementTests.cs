using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using NfsMwRemaster.Driving.Editor;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class EventPlacementTests
    {
        private string folder;
        private GameObject root, floor;
        private EventPlacementSource source;
        private WorldActivityDefinition definition;
        private RoadNetworkAsset network;
        private RoadId lane;
        [SetUp] public void Setup()
        {
            ActivityRegistry.Clear(); ActivityMapRegistry.Clear();
            folder = "Assets/ActivityTest_" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            definition = ScriptableObject.CreateInstance<WorldActivityDefinition>(); definition.adapter = "service"; definition.serviceKind = WorldLocationKind.Garage;
            AssetDatabase.CreateAsset(definition, folder + "/Definition.asset");
            lane = RoadId.New(); network = ScriptableObject.CreateInstance<RoadNetworkAsset>();
            network.Initialize(RoadId.New(), "roads-1", new[] { new RoadBakedLane(lane, RoadId.New(), default, 20, new[] {
                new RoadLaneSample { position = new Vector3(0, 0, 0), distance = 0, width = 10, forward = Vector3.forward, up = Vector3.up, left = Vector3.left },
                new RoadLaneSample { position = new Vector3(0, 0, 100), distance = 100, width = 10, forward = Vector3.forward, up = Vector3.up, left = Vector3.left }
            }, Array.Empty<RoadId>(), null) }, Array.Empty<RoadBakedChunk>());
            AssetDatabase.CreateAsset(network, folder + "/Roads.asset");
            root = new GameObject("Activity test"); source = root.AddComponent<EventPlacementSource>(); source.definition = definition;
            source.anchor.worldPosition = new Vector3(0, 0, 30); source.access = new ActivityAnchor { kind = ActivityAnchorKind.Lane, network = network, laneId = lane.ToString(), station = 30, sourceRevision = "roads-1" };
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Activity test support"; floor.transform.position = new Vector3(0, -.5f, 50); floor.transform.localScale = new Vector3(100, 1, 200); Physics.SyncTransforms();
        }
        [TearDown] public void Cleanup()
        {
            ActivityRegistry.Clear(); ActivityMapRegistry.Clear(); UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(floor); Undo.ClearAll(); AssetDatabase.DeleteAsset(folder);
        }
        private ActivityPlan Build() => EventPlacementCompiler.Build(source);
        private EventPlacementPublication Publish() => EventPlacementCommands.Publish(source, folder + "/Published.asset");
        [Test] public void EntranceAndIconAreSeparateAndGpsUsesRoadAccess()
        {
            source.iconOffset = new Vector3(8, 10, 0); var plan = Build(); Assert.That(plan.Valid, Is.True, string.Join(";", plan.errors));
            Assert.That(plan.record.icon, Is.Not.EqualTo(plan.record.interaction)); Assert.That(Vector3.Distance(plan.record.access, new Vector3(0, 0, 30)), Is.LessThan(.001f));
        }
        [Test] public void RaceStagingUsesPublishedGridInsteadOfIndependentOffset()
        {
            var race = ScriptableObject.CreateInstance<RaceRouteDefinition>(); race.network = network; race.gridCount = 1;
            race.legs = new[] { new RaceRouteLeg { paths = new[] { new RaceRoutePath { spans = new[] { new RacingRouteSpan { laneId = lane.ToString(), startMetres = 40, endMetres = 80 } } } } } };
            AssetDatabase.CreateAsset(race, folder + "/Race.asset");
            race.published = RaceRouteCommands.Publish(race, RaceRouteCompiler.Build(race), folder + "/RoutePublished.asset");
            definition.adapter = "race"; definition.race = race; source.stagingOffset = Vector3.one * 100;
            var plan = EventPlacementCompiler.Build(source, false);
            Assert.That(plan.Valid, Is.True, string.Join(";", plan.errors));
            Assert.That(plan.record.staging, Is.EqualTo(race.published.GridPosition(0)));
        }
        [Test] public void BridgeAboveEntranceIsRejected()
        { source.anchor.worldPosition += Vector3.up * 10; Assert.That(Build().errors.Any(e => e.Contains("height or grade")), Is.True); }
        [Test] public void WallBlocksAccessEvenWhenEntranceIsNearby()
        {
            source.anchor.worldPosition += Vector3.right * 12;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(6, 3, 30); wall.transform.localScale = new Vector3(1, 6, 20); Physics.SyncTransforms();
            try { Assert.That(Build().errors.Any(e => e.Contains("obstructed")), Is.True); } finally { UnityEngine.Object.DestroyImmediate(wall); }
        }
        [Test] public void MissingCollisionGeometryBlocksPublication()
        { UnityEngine.Object.DestroyImmediate(floor); Physics.SyncTransforms(); Assert.That(Build().errors.Any(e => e.Contains("supporting surface")), Is.True); }
        [Test] public void RemovedLaneDoesNotRemapToNearbyLane()
        { source.access.laneId = "missing"; Assert.That(EventPlacementCompiler.Resolve(source.access, out _, out var failure), Is.False); StringAssert.Contains("unresolved", failure); }
        [Test] public void ChangedRoadRequiresExplicitRevisionAcceptance()
        { source.access.sourceRevision = "old"; Assert.That(Build().Valid, Is.False); EventPlacementCommands.AcceptRevision(source, true); Assert.That(Build().Valid, Is.True); }
        [Test] public void OutOfRangeStationDoesNotClamp()
        { source.access.station = 101; Assert.That(EventPlacementCompiler.Resolve(source.access, out _, out _), Is.False); }
        [Test] public void UnknownAdapterRemainsInspectableButCannotPublish()
        { definition.adapter = "collectible"; Assert.That(Build().errors.Any(e => e.Contains("Missing runtime adapter")), Is.True); Assert.Throws<InvalidOperationException>(() => Publish()); Assert.That(source.definition, Is.SameAs(definition)); }
        [Test] public void DuplicateHasFreshIdentityAndNoPublication()
        {
            Publish(); var copy = EventPlacementCommands.Duplicate(source);
            try { Assert.That(copy.id, Is.Not.EqualTo(source.id)); Assert.That(copy.published, Is.Null); Assert.That(copy.GetComponent<WorldActivityInstance>(), Is.Null); }
            finally { UnityEngine.Object.DestroyImmediate(copy.gameObject); }
        }
        [Test] public void UniqueCollectibleCannotDuplicate()
        { definition.uniqueDefinition = true; definition.completionScope = ActivityCompletionScope.Definition; Assert.Throws<InvalidOperationException>(() => EventPlacementCommands.Duplicate(source)); }
        [Test] public void PublishedRecordDoesNotFollowSourceEdits()
        {
            var publication = Publish(); var before = publication.Snapshot; source.iconOffset += Vector3.right * 100;
            Assert.That(publication.Snapshot.icon, Is.EqualTo(before.icon)); var mutableCopy = publication.Snapshot; mutableCopy.exits[0] = Vector3.one * 99;
            Assert.That(publication.Snapshot.exits[0], Is.EqualTo(before.exits[0])); Assert.Throws<InvalidOperationException>(() => publication.Initialize(before));
        }
        [Test] public void DuplicateUnregisterCannotRemoveWinningInstance()
        {
            var publication = Publish(); var first = root.GetComponent<WorldActivityInstance>(); Assert.That(ActivityRegistry.Register(first), Is.True);
            var duplicate = new GameObject("Duplicate runtime"); var second = duplicate.AddComponent<WorldActivityInstance>(); second.Configure(publication);
            try { Assert.That(ActivityRegistry.Register(second), Is.False); ActivityRegistry.Unregister(second); Assert.That(ActivityRegistry.Owns(first), Is.True); }
            finally { UnityEngine.Object.DestroyImmediate(duplicate); }
        }
        [Test] public void UnloadingAndReloadingPreservesIdentity()
        { Publish(); var instance = root.GetComponent<WorldActivityInstance>(); ActivityRegistry.Register(instance); ActivityRegistry.Unregister(instance); Assert.That(ActivityRegistry.Owns(instance), Is.False); Assert.That(ActivityRegistry.Register(instance), Is.True); Assert.That(instance.Record.id, Is.EqualTo(source.id)); }
        [Test] public void CareerPreviewIsDetachedAndGatedRuntimeFailsClosed()
        {
            definition.availability = new CareerRequirementDefinition { kind = CareerRequirementKind.Fact, fact = CareerFactKind.Reputation, required = 10 };
            Publish(); var instance = root.GetComponent<WorldActivityInstance>(); Assert.That(instance.Eligible(null, out _), Is.False);
            var facts = new ActivityPreviewFacts(); Assert.That(instance.Eligible(facts, out _), Is.False); facts.Set(CareerFactKind.Reputation, "", 10); Assert.That(instance.Eligible(facts, out _), Is.True);
        }
        [Test] public void VerticalTriggerRejectsTunnelVehicle()
        { var r = Build().record; Assert.That(r.Contains(r.interaction), Is.True); Assert.That(r.Contains(r.interaction - Vector3.up * 10), Is.False); }
        [Test] public void OrientedReservationsRespectVerticalSeparation()
        {
            Assert.That(EventPlacementCompiler.Overlaps(Vector3.zero, Quaternion.identity, Vector3.one * 4, Vector3.up * 10, Quaternion.identity, Vector3.one * 4), Is.False);
            Assert.That(EventPlacementCompiler.Overlaps(Vector3.zero, Quaternion.identity, Vector3.one * 4, Vector3.one, Quaternion.Euler(0, 45, 0), Vector3.one * 4), Is.True);
        }
        [Test] public void ExportImportResolvesGuidsAndCreatesFreshId()
        {
            var json = EventPlacementTransfer.Export(source); StringAssert.DoesNotContain("instanceID", json);
            var imported = EventPlacementTransfer.Import(json);
            try { Assert.That(imported.id, Is.Not.EqualTo(source.id)); Assert.That(imported.definition, Is.SameAs(definition)); Assert.That(imported.access.network, Is.SameAs(network)); }
            finally { UnityEngine.Object.DestroyImmediate(imported.gameObject); }
        }
        [Test] public void CatalogKeepsOneMarkerAcrossLoadUnload()
        {
            var publication = Publish(); var catalog = root.AddComponent<ActivityMapCatalog>();
            Assert.That(ActivityMapRegistry.Register(catalog, new[] { publication }, out _), Is.True);
            Assert.That(ActivityMapRegistry.Markers.Count(), Is.EqualTo(1)); Assert.That(ActivityMapRegistry.Markers.Single().Loaded, Is.False);
            var instance = root.GetComponent<WorldActivityInstance>(); ActivityRegistry.Register(instance);
            Assert.That(ActivityMapRegistry.Markers.Count(), Is.EqualTo(1)); Assert.That(ActivityMapRegistry.Markers.Single().Loaded, Is.True);
            ActivityRegistry.Unregister(instance); Assert.That(ActivityMapRegistry.Markers.Single().Loaded, Is.False);
            ActivityMapRegistry.Remove(catalog); Assert.That(ActivityMapRegistry.Markers, Is.Empty);
        }
        [Test] public void ConflictingCatalogRevisionIsRejected()
        {
            var publication = Publish(); var catalog = root.AddComponent<ActivityMapCatalog>();
            ActivityMapRegistry.Register(catalog, new[] { publication }, out _);
            var next = ScriptableObject.CreateInstance<EventPlacementPublication>(); var record = publication.Snapshot; record.fingerprint = "different"; next.Initialize(record);
            try { var instance = root.GetComponent<WorldActivityInstance>(); instance.Configure(next); Assert.That(ActivityRegistry.Register(instance), Is.False); }
            finally { UnityEngine.Object.DestroyImmediate(next); }
        }
        [Test] public void UnloadedCatalogRejectsDuplicatedUniqueDefinitions()
        {
            definition.uniqueDefinition = true; definition.completionScope = ActivityCompletionScope.Definition;
            var publication = Publish(); var other = ScriptableObject.CreateInstance<EventPlacementPublication>();
            var record = publication.Snapshot; record.id = Guid.NewGuid().ToString("N"); record.fingerprint = "other"; other.Initialize(record);
            var catalog = root.AddComponent<ActivityMapCatalog>();
            try { Assert.That(ActivityMapRegistry.Register(catalog, new[] { publication, other }, out _), Is.False); Assert.That(ActivityMapRegistry.Markers, Is.Empty); }
            finally { UnityEngine.Object.DestroyImmediate(other); }
        }
        [Test] public void RuntimeRecordInspectionCannotMutateLivePolicy()
        {
            Publish(); var instance = root.GetComponent<WorldActivityInstance>(); var record = instance.Record; record.maximumSpeed = 999;
            Assert.That(instance.Record.maximumSpeed, Is.EqualTo(source.maximumSpeed));
        }
        [Test] public void ImportRejectsUnknownSchema() => Assert.Throws<ArgumentException>(() => EventPlacementTransfer.Import("{\"schema\":99,\"data\":\"{}\"}"));
        [Test] public void NanDimensionsAreRejected() { source.vehicleSize.x = float.NaN; Assert.That(Build().Valid, Is.False); }
        [Test] public void PublicationUndoRestoresDraftAndKeepsRevisionAsset()
        { Publish(); Undo.PerformUndo(); Assert.That(source.published, Is.Null); Assert.That(AssetDatabase.LoadAssetAtPath<EventPlacementPublication>(folder + "/Published.asset"), Is.Not.Null); }
    }
}
