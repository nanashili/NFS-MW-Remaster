using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NfsMwRemaster.Driving;
using NfsMwRemaster.Maps.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace NfsMwRemaster.Maps.Tests
{
    public sealed class MapStudioTests
    {
        private RoadNetworkAsset roads;
        private MapDefinition definition;
        private MapStyle style;
        private readonly List<string> outputFolders=new List<string>();
        private string folder;
        [SetUp]public void Setup()
        {
            folder="Assets/MapTests_"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            roads=MapDemo.Fixture();AssetDatabase.CreateAsset(roads,folder+"/Roads.asset");
            style=ScriptableObject.CreateInstance<MapStyle>();AssetDatabase.CreateAsset(style,folder+"/Style.asset");
            definition=ScriptableObject.CreateInstance<MapDefinition>();definition.roads=roads;definition.style=style;definition.tileSize=128;
            definition.levels=roads.Lanes.Select((l,i)=>new MapLaneLevel{lane=l.Id,level=i==4?1:i==5?-1:0,structure=i==4?MapRoadStructure.Bridge:i==5?MapRoadStructure.Tunnel:MapRoadStructure.Surface}).ToArray();
            AssetDatabase.CreateAsset(definition,folder+"/Map.asset");EditorUtility.SetDirty(definition);AssetDatabase.SaveAssets();
        }
        [TearDown]public void Teardown(){typeof(MapInputFocus).GetMethod("Reset",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,null);foreach(var path in outputFolders)AssetDatabase.DeleteAsset(path);outputFolders.Clear();AssetDatabase.DeleteAsset(folder);}
        private MapBakeResult Bake(){var result=MapBaker.Bake(definition);outputFolders.Add(Path.GetDirectoryName(AssetDatabase.GetAssetPath(result.publication)));return result;}
        [TestCase(0,false)][TestCase(30,false)][TestCase(90,false)][TestCase(-75,true)][TestCase(180,true)]
        public void CoordinateRoundTrip(double yaw,bool mirror)
        {
            var frame=new MapFrame{origin=new LogicalOrigin{x=1e12,z=-1e12},yawDegrees=yaw,mirrorEast=mirror,unitsPerMeter=2.5};
            var point=new MapPoint(1e12+100.125,-1e12+333.5);var mapped=frame.Source(point.x,point.y);var round=frame.ToSource(mapped);
            Assert.That(round.x,Is.EqualTo(point.x).Within(.001));Assert.That(round.y,Is.EqualTo(point.y).Within(.001));
        }
        [Test]public void FloatingOriginKeepsMapAndHeadingStable()
        {
            var frame=new MapFrame{yawDegrees=27,mirrorEast=true};var a=frame.Shifted(new Vector3(100,3,200),default);
            var b=frame.Shifted(new Vector3(-900,3,-1800),new LogicalOrigin{x=1000,z=2000});Assert.That((a-b).Square,Is.LessThan(1e-12));Assert.That(frame.Heading(Vector3.forward),Is.EqualTo(27).Within(.001));
        }
        [TestCase(0)][TestCase(45)][TestCase(-120)]public void ViewportRoundTripAndPivotZoom(float angle)
        {
            var view=new MapViewport{rotation=angle,center=new MapPoint(500,-800),pixelsPerUnit=2,anchor=new Vector2(.2f,.7f)};var rect=new Rect(70,90,640,360);var p=new MapPoint(510,-810);
            Assert.That((view.FromUI(view.ToUI(p,rect),rect)-p).Square,Is.LessThan(1e-6));var pivot=new Vector2(150,170);var before=view.FromUI(pivot,rect);view.Zoom(1.7f,pivot,rect);Assert.That((view.FromUI(pivot,rect)-before).Square,Is.LessThan(1e-8));
        }
        [TestCase(0)][TestCase(-1)][TestCase(double.NaN)]public void InvalidFrameFails(double scale){var frame=new MapFrame{unitsPerMeter=scale};Assert.Throws<ArgumentException>(frame.Validate);}
        [Test]public void SimplificationPreservesProtectedVertex()
        {
            var points=new[]{new MapPoint(0,0),new MapPoint(1,.1),new MapPoint(2,0),new MapPoint(3,0)};
            CollectionAssert.AreEqual(new[]{0,3},MapGeometry.Simplify(points,1));CollectionAssert.AreEqual(new[]{0,1,3},MapGeometry.Simplify(points,1,new HashSet<int>{1}));
        }
        [Test]public void TileSeamHasIdenticalIntersection()
        {var a=new MapPoint(-10,7);var b=new MapPoint(30,21);Assert.IsTrue(MapGeometry.Clip(a,b,-100,-100,0,100,out _,out double exit));Assert.IsTrue(MapGeometry.Clip(a,b,0,-100,100,100,out double enter,out _));Assert.That(exit,Is.EqualTo(enter));}
        [Test]public void BakeDoesNotNeedLoadedCityAndPreservesLevels()
        {
            var result=Bake();Assert.That(result.publication.Tiles.Count,Is.GreaterThan(8));var levels=new HashSet<int>();
            using(var cache=new MapTileCache(result.publication))foreach(var tile in result.publication.Tiles)foreach(var s in cache.Get(tile).segments){levels.Add(s.level);Assert.IsTrue(s.lane.IsValid);}
            CollectionAssert.AreEquivalent(new[]{-1,0,1},levels);
        }
        [Test]public void CrossingAuditIncludesUnconnectedSampleEndpoints()
        {Assert.That(MapAudit.Crossings(Bake().publication),Is.Empty);foreach(var level in definition.levels)level.level=0;Assert.That(MapAudit.Crossings(Bake().publication),Has.Some.Contains("Stacked crossing shares level"));}
        [Test]public void OverviewRetainsEveryLaneWithoutDependingOnDetailedTileLoads()
        {var p=Bake().publication;using(var cache=new MapTileCache(p,1)){var data=cache.Get(p.Overview);Assert.NotNull(data);CollectionAssert.AreEquivalent(roads.Lanes.Select(l=>l.Id),data.segments.Select(s=>s.lane).Distinct());Assert.That(data.segments.All(s=>s.detail==2),Is.True);Assert.That(cache.Count,Is.EqualTo(1));}}
        [Test]public void SvgExportIsValidAndPreservesSourceLinks()
        {var p=Bake().publication;string path=folder+"/Map.svg";MapExport.Svg(p,style,path);var document=new System.Xml.XmlDocument();document.Load(path);Assert.That(document.DocumentElement.LocalName,Is.EqualTo("svg"));Assert.That(File.ReadAllText(path),Does.Contain(roads.Lanes[0].Id.ToString()));}
        [Test]public void DuplicateDefinitionIdentityIsRejected()
        {var copy=Object.Instantiate(definition);AssetDatabase.CreateAsset(copy,folder+"/Duplicate.asset");Assert.That(MapBaker.Validate(definition),Has.Some.Contains("Duplicate map identity"));}
        [Test]public void SecondBakeReusesAllTilesAndHasSameFingerprint()
        {var a=Bake();var b=Bake();Assert.That(b.written,Is.Zero);Assert.That(b.reused,Is.EqualTo(a.publication.Tiles.Count));Assert.That(a.publication.Fingerprint,Is.EqualTo(b.publication.Fingerprint));}
        [Test]public void EditingOneLaneOnlyRebuildsAffectedTiles()
        {
            Bake();var original=roads.Lanes[0];var samples=original.Samples.ToArray();samples[32].position.z+=5;
            var lanes=roads.Lanes.ToArray();lanes[0]=new RoadBakedLane(original.Id,original.RoadId,original.Class,original.Speed,samples,original.Successors.ToArray(),original.Surface,original.CompatibilityId);
            var revised=ScriptableObject.CreateInstance<RoadNetworkAsset>();revised.Initialize(roads.NetworkId,"edited",lanes,Array.Empty<RoadBakedChunk>());AssetDatabase.CreateAsset(revised,folder+"/RoadRevision.asset");definition.roads=revised;
            var result=Bake();Assert.That(result.written,Is.GreaterThan(0));Assert.That(result.reused,Is.GreaterThan(0));
        }
        [Test]public void FrameChangeCannotReuseTiles()
        {Bake();definition.frame.origin.x=10;var result=Bake();Assert.That(result.reused,Is.Zero);}
        [Test]public void StyleChangeDoesNotInvalidateGeometry(){Bake();string hash=MapBaker.Fingerprint(definition);style.road=Color.magenta;Assert.That(MapBaker.Fingerprint(definition),Is.EqualTo(hash));}
        [Test]public void CancelKeepsPreviousPublication(){var original=Bake().publication;Assert.Throws<OperationCanceledException>(()=>MapBaker.Bake(definition,_=>true));Assert.That(definition.publication,Is.SameAs(original));}
        [Test]public void CancelDuringStagingKeepsPreviousPublication()
        {var original=Bake().publication;int calls=0;Assert.Throws<OperationCanceledException>(()=>MapBaker.Bake(definition,_=>++calls>roads.Lanes.Count+2));Assert.That(definition.publication,Is.SameAs(original));}
        [Test]public void UnclassifiedOrObsoleteLaneFailsValidation()
        {definition.levels=Array.Empty<MapLaneLevel>();Assert.That(MapBaker.Validate(definition).Count,Is.EqualTo(6));definition.levels=new[]{new MapLaneLevel{lane=RoadId.New()}};Assert.That(MapBaker.Validate(definition),Has.Some.Contains("obsolete"));}
        [Test]public void FutureSchemaRejected(){definition.schema=999;Assert.That(MapBaker.Validate(definition),Has.Some.Contains("schema"));Assert.Throws<InvalidOperationException>(()=>MapBaker.Bake(definition));}
        [Test]public void PublicationCannotBeReinitialized()
        {var p=Bake().publication;Assert.Throws<InvalidOperationException>(()=>p.Initialize(definition.id,"new","frame",roads,definition.frame,128,Array.Empty<MapTileEntry>(),Array.Empty<MapLandmark>(),default,default));}
        [Test]public void TileCacheEvictsAndHonorsBudget()
        {var p=Bake().publication;using(var cache=new MapTileCache(p,1)){foreach(var tile in p.Tiles.Take(3))Assert.NotNull(cache.Get(tile));Assert.That(cache.Count,Is.EqualTo(1));Assert.That(cache.Evictions,Is.EqualTo(2));}}
        [Test]public void MissingTileIsNotValidGeometry(){var p=Bake().publication;using(var cache=new MapTileCache(p,2,1000000,_=>null)){Assert.IsNull(cache.Get(p.Tiles[0]));Assert.That(cache.Missing,Is.EqualTo(1));}}
        [Test]public void CorruptTileRejected(){var p=Bake().publication;using(var cache=new MapTileCache(p,2,1000000,_=>"{}")){Assert.IsNull(cache.Get(p.Tiles[0]));Assert.That(cache.Count,Is.Zero);}}
        [Test]public void OversizedTileRejected(){var p=Bake().publication;using(var cache=new MapTileCache(p,1,1)){Assert.IsNull(cache.Get(p.Tiles[0]));Assert.That(cache.Bytes,Is.Zero);}}
        [Test]public void GpsOldAAndCancelledRepliesCannotReplaceB()
        {var request=new MapRouteRequest();long a=request.Begin(),b=request.Begin();Assert.IsFalse(request.Publish(a,new[]{Vector3.zero,Vector3.one}));Assert.IsTrue(request.Publish(b,new[]{Vector3.right,Vector3.forward}));Assert.That(request.Points[0],Is.EqualTo(Vector3.right));request.Clear();Assert.IsFalse(request.Publish(b,new[]{Vector3.zero,Vector3.one}));}
        [Test]public void GpsFailureNeverDrawsStraightFallback()
        {var request=new MapRouteRequest();request.Publish(request.Begin(),null,"Disconnected");Assert.That(request.State,Is.EqualTo(MapRouteState.Unavailable));Assert.That(request.Points,Is.Empty);}
        [Test]public void DirectedRouterRespectsSelectedBridgeAndDisconnection()
        {var owner=new RoadRuntimeNetwork(roads);var request=new MapRouteRequest();var bridge=roads.Lanes[4];MapNavigation.Route(owner,bridge.Samples[0].position,new MapDestination(roads.Fingerprint,new RoadLaneAnchor(bridge.Id,bridge.Length),1),request);Assert.That(request.State,Is.EqualTo(MapRouteState.Ready));Assert.That(request.Points.All(p=>p.y==12),Is.True);
            MapNavigation.Route(owner,roads.Lanes[0].Samples[0].position,new MapDestination(roads.Fingerprint,new RoadLaneAnchor(bridge.Id,10),1),request);Assert.That(request.State,Is.EqualTo(MapRouteState.Unavailable));}
        [Test]public void GpsClosureAndRevisionAreHonored()
        {var request=new MapRouteRequest();var lane=roads.Lanes[0];MapNavigation.Route(new RoadRuntimeNetwork(roads,_=>true),lane.Samples[0].position,new MapDestination(roads.Fingerprint,new RoadLaneAnchor(lane.Id,10),0),request);Assert.That(request.State,Is.EqualTo(MapRouteState.Unavailable));MapNavigation.Route(new RoadRuntimeNetwork(roads),Vector3.zero,new MapDestination("stale",new RoadLaneAnchor(lane.Id,10),0),request);Assert.That(request.Failure,Does.Contain("revision"));}
        [Test]public void RouteCannotRevealUnauthorizedLaneGeometry()
        {var request=new MapRouteRequest();var lane=roads.Lanes[0];MapNavigation.Route(new RoadRuntimeNetwork(roads),lane.Samples[0].position,new MapDestination(roads.Fingerprint,new RoadLaneAnchor(lane.Id,10),0),request,false,_=>false);Assert.That(request.State,Is.EqualTo(MapRouteState.Unavailable));Assert.That(request.Points,Is.Empty);}
        [Test]public void RouteVisibilityRevokesImmediatelyWithoutWaitingForReroute()
        {bool allowed=true;var request=new MapRouteRequest();var lane=roads.Lanes[0];MapNavigation.Route(new RoadRuntimeNetwork(roads),lane.Samples[0].position,new MapDestination(roads.Fingerprint,new RoadLaneAnchor(lane.Id,10),0),request,false,_=>allowed);Assert.IsTrue(request.Visible);allowed=false;Assert.IsFalse(request.Visible);}
        [Test]public void MarkerSnapshotRevokesImmediatelyOnLifetimeEnd()
        {var registry=new MapMarkerRegistry();var lease=registry.Register(new MapMarker{id="id",category="Approved"});var snapshot=registry.Visible(new Policy()).Single();Assert.IsTrue(snapshot.Authorized(new Policy()));lease.Dispose();Assert.IsFalse(snapshot.Authorized(new Policy()));}
        private sealed class Policy:IMapKnowledge
        {public bool RoadVisible(RoadId id)=>false;public bool MarkerVisible(string id,string category)=>category=="Approved";public bool ActivityVisible(ActivityMapMarker m)=>false;public string Localize(string k,string f)=>f;}
        [Test]public void MissingPolicyAndHiddenPoliceNeverReachRenderer()
        {var registry=new MapMarkerRegistry();registry.Register(new MapMarker{id="hidden",category="Police",position=new MapPoint(0,0)});registry.Register(new MapMarker{id="visible",category="Approved",position=new MapPoint(1,0)});Assert.That(registry.Visible(null),Is.Empty);Assert.That(registry.Visible(new Policy()).Select(m=>m.id),Is.EqualTo(new[]{"visible"}));}
        private sealed class ActivityPolicy:IMapKnowledge
        {public bool RoadVisible(RoadId id)=>true;public bool MarkerVisible(string id,string category)=>false;public bool ActivityVisible(ActivityMapMarker marker)=>true;public string Localize(string key,string fallback)=>fallback;}
        [TestCase(false,false)][TestCase(true,false)][TestCase(true,true)]
        public void ActivityDestinationRequiresCurrentExplicitAccessAnchor(bool hasAnchor,bool stale)
        {
            var map=Bake().publication;var go=new GameObject("Activity catalog");var catalog=go.AddComponent<ActivityMapCatalog>();var publication=ScriptableObject.CreateInstance<EventPlacementPublication>();
            try
            {
                var lane=roads.Lanes[4];publication.Initialize(new ActivityRecord{id=Guid.NewGuid().ToString("N"),fingerprint="fixture",label="Bridge activity",adapter="race",availabilityJson="{\"kind\":1,\"children\":[]}",
                    accessLane=hasAnchor?lane.Id:default,accessNetworkId=roads.NetworkId.ToString(),accessRoadRevision=stale?"old":roads.Fingerprint,accessDistance=12});
                Assert.IsTrue(ActivityMapRegistry.Register(catalog,new[]{publication},out var error),error);
                var marker=MapMarkers.Activities(map,new ActivityPolicy()).Single(m=>m.id==publication.Id);
                Assert.That(marker.destination.HasValue,Is.EqualTo(hasAnchor&&!stale));
                if(marker.destination.HasValue){Assert.That(marker.destination.Value.anchor.LaneId,Is.EqualTo(lane.Id));Assert.That(marker.destination.Value.anchor.Distance,Is.EqualTo(12));Assert.That(marker.level,Is.EqualTo(1));}
            }
            finally{ActivityMapRegistry.Remove(catalog);Object.DestroyImmediate(go);Object.DestroyImmediate(publication);}
        }
        [Test]public void OldMarkerLifetimeCannotUnregisterStreamedReplacement()
        {var registry=new MapMarkerRegistry();var a=registry.Register(new MapMarker{id="id",category="Approved"});var b=registry.Register(new MapMarker{id="id",category="Approved",label="replacement"});a.Dispose();Assert.That(registry.Visible(new Policy()).Single().label,Is.EqualTo("replacement"));b.Dispose();Assert.That(registry.Visible(new Policy()),Is.Empty);}
        [Test]public void SelectedAndCriticalMarkersDoNotCluster()
        {var markers=new[]{new MapMarker{id="a"},new MapMarker{id="b"},new MapMarker{id="selected"},new MapMarker{id="critical",critical=true}};var clusters=MapMarkers.Cluster(markers,100,"selected");Assert.That(clusters.Count,Is.EqualTo(3));Assert.That(clusters.Sum(c=>c.members.Count),Is.EqualTo(4));}
        [Test]public void WindowLifecycleWorksWithoutHostedGraphics(){var window=ScriptableObject.CreateInstance<MapStudioWindow>();Object.DestroyImmediate(window);}
        [Test]public void MapInputOwnershipDoesNotReleaseAnotherOwner()
        {object a=new object(),b=new object();try{MapInputFocus.Acquire(a);MapInputFocus.Acquire(b);MapInputFocus.Release(a);Assert.IsTrue(MapInputFocus.Captured);}finally{MapInputFocus.Release(a);MapInputFocus.Release(b);}}
    }
}
