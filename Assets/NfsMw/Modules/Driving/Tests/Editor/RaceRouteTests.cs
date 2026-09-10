using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using NfsMwRemaster.Driving.Editor;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class RaceRouteTests
    {
        private string folder;
        private RaceRouteDefinition source;
        private RoadNetworkAsset roads;
        private RoadId laneId;

        [SetUp] public void Setup()
        {
            folder = "Assets/RouteTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            laneId = RoadId.New();
            roads = Network(Lane(laneId, 0, 300));
            AssetDatabase.CreateAsset(roads, folder + "/Roads.asset");
            source = RaceRouteCommands.Create(folder + "/Route.asset");
            source.network = roads;
            source.legs = new[] { new RaceRouteLeg { paths = new[] { new RaceRoutePath { spans = new[] { Span(laneId, 40, 270) } } } } };
        }
        [TearDown] public void Cleanup() { Undo.ClearAll(); AssetDatabase.DeleteAsset(folder); }
        private static RacingRouteSpan Span(RoadId id, float start, float end) => new RacingRouteSpan { laneId = id.ToString(), startMetres = start, endMetres = end };
        private static RoadBakedLane Lane(RoadId id, float start, float end, RoadId[] successors = null, float width = 8)
        {
            var samples = new RoadLaneSample[11];
            for (int i = 0; i < samples.Length; i++) samples[i] = new RoadLaneSample { distance = (end-start)*i/10, station = (end-start)*i/10, width = width, position = new Vector3(0,0,Mathf.Lerp(start,end,i/10f)), forward = Vector3.forward, up = Vector3.up, left = Vector3.left };
            return new RoadBakedLane(id, RoadId.New(), default, 20, samples, successors ?? Array.Empty<RoadId>(), null);
        }
        private static RoadNetworkAsset Network(params RoadBakedLane[] lanes)
        {
            var network = ScriptableObject.CreateInstance<RoadNetworkAsset>();
            network.Initialize(RoadId.New(), Guid.NewGuid().ToString("N"), lanes, Array.Empty<RoadBakedChunk>());
            return network;
        }
        private RaceRoutePlan Plan()
        {
            var plan = RaceRouteCompiler.Build(source);
            Assert.That(plan.Valid, Is.True, string.Join("\n", plan.issues)); return plan;
        }
        private RaceRoutePublication Publish() => RaceRouteCommands.Publish(source, Plan(), folder + "/Published.asset");
        private static RaceRouteGate Gate(float z, float x = 0) => new RaceRouteGate { id = Guid.NewGuid().ToString("N"), laneId = "fixture", position = new Vector3(x,0,z), forward = Vector3.forward, up = Vector3.up, width = 4, height = 4, distance = z };
        private RaceRoutePublication Fixture(params RaceRouteGate[][] paths)
        {
            var publication = ScriptableObject.CreateInstance<RaceRoutePublication>();
            publication.Initialize(source.id, Guid.NewGuid().ToString("N"), roads, new[] { new RaceRouteBakedLeg { id = Guid.NewGuid().ToString("N"), paths = paths.Select(p => new RaceRouteBakedPath { id = Guid.NewGuid().ToString("N"), length = 100, gates = p }).ToArray() } });
            AssetDatabase.CreateAsset(publication, AssetDatabase.GenerateUniqueAssetPath(folder + "/Fixture.asset")); return publication;
        }
        [Test] public void FastSweepCompletesOnceAndFlyoverDoesNot()
        {
            var publication = Publish();
            using var course = new MissionCourse(publication.Checkpoints, 1, 180, publishedRoute: publication);
            course.Advance(.02f, new Vector3(0,10,0), new Vector3(0,10,300), 200);
            Assert.That(course.CheckpointsPassed, Is.Zero);
            course.Advance(.02f, Vector3.zero, Vector3.forward*300, 200);
            Assert.That(course.CheckpointsPassed, Is.EqualTo(1)); Assert.That(course.Outcome, Is.EqualTo(MissionState.Succeeded));
            course.Advance(.02f, Vector3.forward*300, Vector3.zero, 200);
            course.Advance(.02f, Vector3.zero, Vector3.forward*300, 200);
            Assert.That(course.CheckpointsPassed, Is.EqualTo(1));
        }
        [Test] public void WrongDirectionAndSkippedFinishDoNotProgress()
        {
            var publication = Fixture(new[] { Gate(10), Gate(20), Gate(30) });
            var traversal = new RaceRouteTraversal(publication);
            Assert.That(traversal.Advance(0, Vector3.forward*40, Vector3.zero, out _), Is.False);
            Assert.That(traversal.Advance(0, Vector3.forward*25, Vector3.forward*35, out _), Is.False);
            Assert.That(traversal.Gate, Is.Zero);
        }
        [Test] public void DifferentBranchesMustReachTheirOwnOrderedRejoin()
        {
            var publication = Fixture(new[] { Gate(10), Gate(20,-10), Gate(30) }, new[] { Gate(10), Gate(15,10), Gate(20,10), Gate(25,10), Gate(30) });
            var traversal = new RaceRouteTraversal(publication);
            Assert.That(traversal.Advance(0, Vector3.zero, Vector3.forward*11, out _), Is.False);
            Assert.That(traversal.Path, Is.EqualTo(-1));
            Assert.That(traversal.Advance(0, new Vector3(10,0,11), new Vector3(10,0,16), out _), Is.False);
            Assert.That(traversal.Path, Is.EqualTo(1));
            Assert.That(traversal.Advance(0, Vector3.forward*29, Vector3.forward*31, out _), Is.False);
            Assert.That(traversal.Advance(0, new Vector3(10,0,16), new Vector3(10,0,26), out _), Is.False);
            Assert.That(traversal.Advance(0, Vector3.forward*29, Vector3.forward*31, out _), Is.True);
        }
        [Test] public void SaveRestoreKeepsGateCursorAndRejectsDifferentRevision()
        {
            var publication = Publish();
            using var first = new MissionCourse(publication.Checkpoints,1,180,publishedRoute:publication);
            first.Advance(.02f, Vector3.forward*39, Vector3.forward*41, 100);
            var saved = first.Runtime.Capture();
            using var resumed = new MissionCourse(publication.Checkpoints,1,180,restore:saved,publishedRoute:publication);
            resumed.Advance(.02f, Vector3.forward*41, Vector3.forward*280,100);
            Assert.That(resumed.Outcome, Is.EqualTo(MissionState.Succeeded));
            source.legs[0].gateHeight += 1;
            var changed = Publish();
            Assert.Throws<ArgumentException>(() => new MissionCourse(changed.Checkpoints,1,180,restore:saved,publishedRoute:changed));
        }
        [Test] public void CircuitRequiresEachOccurrenceOnEveryLap()
        {
            var publication = Fixture(new[] { Gate(10), Gate(20), Gate(30) });
            using var course = new MissionCourse(publication.Checkpoints,2,180,publishedRoute:publication);
            course.Advance(.02f,Vector3.zero,Vector3.forward*40,100);
            Assert.That(course.CheckpointsPassed, Is.EqualTo(1));
            course.Advance(.02f,Vector3.forward*29,Vector3.forward*31,100);
            Assert.That(course.CheckpointsPassed, Is.EqualTo(1));
            course.Advance(.02f,Vector3.zero,Vector3.forward*40,100);
            Assert.That(course.Outcome, Is.EqualTo(MissionState.Succeeded));
        }
        [Test] public void PublicationIsImmutableAndCopiesGeometry()
        {
            var publication = Publish(); var leg = publication.Leg(0); leg.paths[0].gates[0].position = Vector3.one*999;
            Assert.That(publication.Leg(0).paths[0].gates[0].position.z, Is.LessThan(100));
            Assert.Throws<InvalidOperationException>(()=>publication.Initialize(source.id,"again",roads,new[]{leg}));
        }
        [Test] public void StaleReviewCannotReplaceExistingPublication()
        {
            var first = Publish(); var plan = Plan(); source.vehicleWidth += .1f;
            Assert.Throws<ArgumentException>(()=>RaceRouteCommands.Publish(source,plan,folder+"/Stale.asset"));
            Assert.That(source.published, Is.SameAs(first));
        }
        [Test] public void UnrelatedLaneDoesNotInvalidateButReferencedLaneDoes()
        {
            var original = Plan().fingerprint;
            var unrelated = ScriptableObject.CreateInstance<RoadNetworkAsset>();
            unrelated.Initialize(roads.NetworkId,"unrelated-revision",new[]{roads.Lanes[0], Lane(RoadId.New(),400,500)},Array.Empty<RoadBakedChunk>());
            source.network = unrelated;
            Assert.That(RaceRouteCompiler.Fingerprint(source), Is.EqualTo(original));
            UnityEngine.Object.DestroyImmediate(unrelated);
            source.network = Network(Lane(laneId,0,301));
            Assert.That(RaceRouteCompiler.Fingerprint(source), Is.Not.EqualTo(original));
            UnityEngine.Object.DestroyImmediate(source.network); source.network = roads;
        }
        [Test] public void MissingSplitLaneFailsExplicitly()
        {
            source.legs[0].paths[0].spans[0].laneId = RoadId.New().ToString();
            Assert.That(RaceRouteCompiler.Build(source).issues.Any(i=>i.rule=="ROUTE_UNRESOLVED"), Is.True);
        }
        [Test] public void DuplicateAndUndoPreserveDistinctStableIdentities()
        {
            var copy = RaceRouteCommands.Duplicate(source,folder+"/Copy.asset");
            Assert.That(copy.id, Is.Not.EqualTo(source.id)); Assert.That(copy.legs[0].paths[0].spans[0].id, Is.Not.EqualTo(source.legs[0].paths[0].spans[0].id));
            RaceRouteCommands.Edit(source,"Change route",()=>source.laps=9); Undo.PerformUndo(); Assert.That(source.laps,Is.EqualTo(1));
            Undo.PerformRedo(); Assert.That(source.laps,Is.EqualTo(9));
        }
        [Test] public void DuplicateIdsAndUnsupportedSchemaFailValidation()
        {
            source.legs[0].id=source.id;
            Assert.That(RaceRouteCompiler.Build(source).issues.Any(i=>i.rule=="ROUTE_ID"),Is.True);
            source.schema=99; Assert.That(RaceRouteCompiler.Build(source).Valid,Is.False);
        }
        [Test] public void LargestEntrantMustFitEveryLaneAndGrid()
        {
            source.vehicleWidth=9; Assert.That(RaceRouteCompiler.Build(source).Valid,Is.False);
            source.vehicleWidth=2.5f;source.legs[0].paths[0].spans[0].startMetres=1;
            Assert.That(RaceRouteCompiler.Build(source).issues.Any(i=>i.rule=="ROUTE_GRID"),Is.True);
        }
        [Test] public void DisconnectedSuggestionFailsWithoutFabricatingRoute()
        {
            var disconnected=RoadId.New(); var network=Network(roads.Lanes[0],Lane(disconnected,400,500));source.network=network;
            try{Assert.Throws<ArgumentException>(()=>RaceRouteCommands.Suggest(source,laneId.ToString(),40,disconnected.ToString(),80,RaceRouteSuggestion.Distance));}
            finally{UnityEngine.Object.DestroyImmediate(network);source.network=roads;}
        }
        [Test] public void TenGeometryReplaysLeaveNoTransientPublications()
        {
            int before=Resources.FindObjectsOfTypeAll<RaceRoutePublication>().Length;
            var plan=Plan();for(int i=0;i<10;i++)StringAssert.Contains("Succeeded",RaceRouteTestPreview.Replay(source,plan));
            Assert.That(Resources.FindObjectsOfTypeAll<RaceRoutePublication>().Length,Is.EqualTo(before));
        }
        [Test] public void PlacementUndoRedoPreservesBindingAndGridPose()
        {
            var publication=Publish();var plan=Plan();var placed=RaceRouteCommands.PlaceEvent(source,plan);var go=placed.gameObject;
            Assert.That(placed.RoutePublication,Is.SameAs(publication));Assert.That(go.transform.position,Is.EqualTo(plan.grid[0]));
            Undo.PerformUndo();Assert.That(go==null,Is.True);Undo.PerformRedo();
            var restored=UnityEngine.Object.FindObjectsByType<FreeRoamEventDefinition>(FindObjectsSortMode.None).Single(e=>e.RoutePublication==publication);
            Assert.That(restored.transform.position,Is.EqualTo(plan.grid[0]));UnityEngine.Object.DestroyImmediate(restored.gameObject);
        }
        [Test] public void ProductionPreviewTenRunsAndCancellationsReleaseWorlds()
        {
            var sample=AssetDatabase.LoadAssetAtPath<RaceRouteDefinition>("Assets/NfsMw/Modules/Driving/Examples/RaceRoutes/GrayboxSprint.asset");
            Assert.That(sample,Is.Not.Null,"Build the shipped sample using RaceRouteDemo.BuildScene in the validation project.");
            var copy=UnityEngine.Object.Instantiate(sample);copy.id=Guid.NewGuid().ToString("N");copy.published=null;
            AssetDatabase.CreateAsset(copy,folder+"/Preview.asset");
            var plan=RaceRouteCompiler.Build(copy);RaceRouteCommands.Publish(copy,plan,folder+"/PreviewPublished.asset");
            int before=UnityEngine.SceneManagement.SceneManager.sceneCount;
            int rigs=Resources.FindObjectsOfTypeAll<VehicleController>().Length;
            for(int run=0;run<10;run++)
            {
                using(var preview=new RaceRouteTestPreview(copy))
                {
                    if(run%3==0)preview.Advance();
                    else
                    {
                        int slices=0;while(!preview.Done&&slices++<20000)preview.Advance();
                        Assert.That(preview.Done,Is.True);Assert.That(preview.AllTrialsSucceeded,Is.True,preview.Summary);
                    }
                }
                Assert.That(UnityEngine.SceneManagement.SceneManager.sceneCount,Is.EqualTo(before));
                Assert.That(Resources.FindObjectsOfTypeAll<VehicleController>().Length,Is.EqualTo(rigs));
            }
        }
        [Test] public void WindowContainsTenWorkspacesAndCanReopen()
        {
            for(int repeat=0;repeat<3;repeat++)
            {
                var window=ScriptableObject.CreateInstance<RaceRouteWindow>();
                try
                {
                    window.CreateGUI();
                    var selector=UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.DropdownField>(window.rootVisualElement,"race-route-section");
                    Assert.That(selector,Is.Not.Null);
                    Assert.That(selector.choices.Count,Is.EqualTo(10));
                    foreach(var choice in selector.choices){selector.value=choice;Assert.That(selector.value,Is.EqualTo(choice));}
                }
                finally{UnityEngine.Object.DestroyImmediate(window);}
                Assert.That(RaceRouteWindow.Active==null,Is.True);
            }
        }
        [Test] public void WorkspaceRouteEventHandoffRejectsBrokenAnchorAndUndoRestoresExactLane()
        {
            Publish();
            var placement=NfsMwRemaster.Driving.Editor.Workspace.RacingRouteEventWorkflow.Create(source,folder+"/Activity.asset");
            try
            {
                Assert.That(placement.definition.race,Is.SameAs(source));
                Assert.That(placement.published,Is.Null,"Handoff must not publish without placement review.");
                Assert.That(placement.access.laneId,Is.EqualTo(laneId.ToString()));
                var link=NfsMwRemaster.Driving.Editor.Workspace.RacingDocumentLink.For("event-placement",placement,"access");
                Assert.That(link.Resolve(),Is.SameAs(placement));
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(placement,"Break exact lane fixture");
                placement.access.laneId="missing-lane";Undo.FlushUndoRecordObjects();
                Assert.That(EventPlacementCompiler.Resolve(placement.access,out _,out _),Is.False);
                Assert.That(EventPlacementCompiler.Build(placement,false).Valid,Is.False);
                Undo.PerformUndo();
                Assert.That(placement.access.laneId,Is.EqualTo(laneId.ToString()));
                Assert.That(EventPlacementCompiler.Resolve(placement.access,out _,out _),Is.True);
                Assert.That(placement.definition.race,Is.SameAs(source));
            }
            finally{UnityEngine.Object.DestroyImmediate(placement.gameObject);}
        }
        [Test] public void SaveReloadPreservesPublicationFingerprint()
        {
            var publication=Publish();string fingerprint=publication.Fingerprint;
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(folder+"/Route.asset",ImportAssetOptions.ForceUpdate);
            var reloaded=AssetDatabase.LoadAssetAtPath<RaceRouteDefinition>(folder+"/Route.asset");
            Assert.That(RaceRouteCompiler.Fingerprint(reloaded),Is.EqualTo(fingerprint));
        }
        [Test] public void ManySectorsInOneSweepKeepNumericOrder()
        {
            source.legs=Enumerable.Range(0,15).Select(i=>new RaceRouteLeg {paths=new[]{new RaceRoutePath {spans=new[]{Span(laneId,40+i*10,50+i*10)}}}}).ToArray();
            var publication=Publish();using var course=new MissionCourse(publication.Checkpoints,1,180,publishedRoute:publication);
            course.Advance(.02f,Vector3.zero,Vector3.forward*250,100);
            Assert.That(course.CheckpointsPassed,Is.EqualTo(15));Assert.That(course.Outcome,Is.EqualTo(MissionState.Succeeded));
        }
        [Test] public void MissingVehicleAdapterIsExplicitlyGeometryOnly()
        {
            Publish();var error=Assert.Throws<ArgumentException>(()=>new RaceRouteTestPreview(source));StringAssert.Contains("Geometry-only",error.Message);
        }
    }
}
