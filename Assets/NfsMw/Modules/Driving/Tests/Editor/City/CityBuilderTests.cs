using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using NfsMwRemaster.Driving.Editor;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class CityBuilderTests
    {
        private string folder;
        private CityDistrict district;
        private CityKit kit;
        [SetUp] public void Setup()
        {
            folder="Assets/CityTest_"+Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets",folder.Substring(7));
            var material=new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(material,folder+"/Material.mat");
            kit=ScriptableObject.CreateInstance<CityKit>(); kit.wall=kit.roof=kit.trim=kit.glass=kit.yard=material; kit.contentId=Guid.NewGuid().ToString("N");
            AssetDatabase.CreateAsset(kit,folder+"/Kit.asset");
            var style=ScriptableObject.CreateInstance<CityStyle>();style.kit=kit;style.contentId=Guid.NewGuid().ToString("N"); AssetDatabase.CreateAsset(style,folder+"/Style.asset");
            district=CityCommands.Create(style,Vector3.zero,SceneManager.GetActiveScene());
            var block=CityCommands.AddBlock(district,CityPolygon.Rectangle(-80,-80,160,160));
            CityCommands.AddParcel(district,block,CityPolygon.Rectangle(-70,-70,70,70));
        }
        [TearDown] public void Cleanup()
        {
            if(district!=null) UnityEngine.Object.DestroyImmediate(district.gameObject);
            AssetDatabase.DeleteAsset(folder);Undo.ClearAll();
        }
        [Test] public void ConcaveHoleAndSetbackPreserveExcludedGeometry()
        {
            var p=CityPolygon.Rectangle(0,0,100,100);p.holes.Add(new CityRing { points=CityPolygon.Rectangle(40,40,20,20).outline });
            CityGeometry.Validate(p);Assert.That(CityGeometry.Area(p),Is.EqualTo(9600));
            var inset=CityGeometry.Inset(p,5).Single();Assert.That(CityGeometry.Area(inset),Is.EqualTo(7200).Within(0.1));
            Assert.That(CityGeometry.Contains(inset,new Vector2(50,50)),Is.False);
            var concave=new CityPolygon { outline=new System.Collections.Generic.List<Vector2>{new Vector2(0,0),new Vector2(80,0),new Vector2(80,20),new Vector2(20,20),new Vector2(20,80),new Vector2(0,80)} };
            Assert.That(CityGeometry.Inset(concave,2).Sum(CityGeometry.Area),Is.GreaterThan(100));
            var parts=CityGeometry.Cut(concave,new Vector2(10,-5),new Vector2(10,90));
            Assert.That(parts.Sum(CityGeometry.Area),Is.EqualTo(CityGeometry.Area(concave)).Within(0.01));
        }
        [Test] public void InvalidGeometryAndUnsatisfiableParcelNeverProduceValidOutput()
        {
            var crossing=new CityPolygon { outline=new System.Collections.Generic.List<Vector2>{Vector2.zero,new Vector2(10,10),new Vector2(0,10),new Vector2(10,0)} };
            Assert.Throws<ArgumentException>(()=>CityGeometry.Validate(crossing));
            district.parcels[0].setback=100;var plan=CityPlanning.Build(district);
            Assert.That(plan.Valid,Is.False);Assert.That(plan.instances,Is.Empty);
            Assert.Throws<ArgumentException>(()=>CityCommands.Commit(district,plan,folder+"/Bad.asset"));
        }
        [Test] public void SubdivisionConservesAreaAtLargeDistrictLocalCoordinates()
        {
            var p=CityPolygon.Rectangle(100000,100000,200,100);
            var parts=CityGeometry.Subdivide(p,2000,100);
            Assert.That(parts.Count,Is.GreaterThan(1));Assert.That(parts.Sum(CityGeometry.Area),Is.EqualTo(20000).Within(1));
            foreach(var part in parts) Assert.That(CityGeometry.Fits(p,part),Is.True);
        }
        [Test] public void DeterministicGenerationAndPublicationUndoShareOneRevision()
        {
            var a=CityPlanning.Build(district); Assert.That(a.Valid,Is.True,string.Join("\n",a.diagnostics));
            var b=CityPlanning.Build(district); Assert.That(b.instances.Select(i=>i.signature),Is.EqualTo(a.instances.Select(i=>i.signature)));
            var publication=CityCommands.Commit(district,a,folder+"/City.asset");
            Assert.That(district.GetComponent<CityLocations>().Publication,Is.SameAs(publication));
            Assert.That(publication.TryResolve(district.parcels[0].id,out var location),Is.True);
            Assert.That(location.id,Is.EqualTo(district.parcels[0].id));
            Undo.FlushUndoRecordObjects();Undo.PerformUndo();Assert.That(district.publication,Is.Null);Assert.That(CityCommands.Existing(district),Is.Empty);
            Undo.PerformRedo();Assert.That(district.publication,Is.SameAs(publication));Assert.That(CityCommands.Existing(district).Count,Is.EqualTo(a.instances.Count));
            location.label="mutated";publication.TryResolve(location.id,out var again);Assert.That(again.label,Is.Not.EqualTo("mutated"));
        }
        [Test] public void PinDetachAndCancellationPreserveManualWork()
        {
            var a=CityPlanning.Build(district);CityCommands.Commit(district,a,folder+"/City.asset");
            var items=CityCommands.Existing(district).Values.ToArray();var pinned=items[0];var detached=items[1].gameObject;
            CityCommands.SetOwnership(district,pinned,CityOwnership.Pinned);
            CityCommands.SetOwnership(district,items[1],CityOwnership.Detached);
            try
            {
                var position=pinned.transform.position;
                district.parcels[0].floors++;var b=CityPlanning.Build(district);
                var old=district.publication;int count=CityCommands.Existing(district).Count;
                Assert.Throws<OperationCanceledException>(()=>CityCommands.Commit(district,b,folder+"/Cancel.asset",progress=>progress>0.2f));
                Assert.That(district.publication,Is.SameAs(old));Assert.That(CityCommands.Existing(district).Count,Is.EqualTo(count));
                Assert.That(AssetDatabase.LoadAssetAtPath<CityPublication>(folder+"/Cancel.asset"),Is.Null);
                CityCommands.Commit(district,b,folder+"/City.asset");
                Assert.That(pinned.transform.position,Is.EqualTo(position));Assert.That(detached!=null,Is.True);
                Assert.That(detached.GetComponent<CityGeneratedInstance>(),Is.Null);
            }
            finally { if(detached!=null) UnityEngine.Object.DestroyImmediate(detached); }
        }
        [Test] public void StalePreviewAndManualMovesAreRejectedWithoutDeletingOutput()
        {
            var plan=CityPlanning.Build(district);district.seed++;
            Assert.Throws<ArgumentException>(()=>CityCommands.Commit(district,plan,folder+"/Stale.asset"));
            plan=CityPlanning.Build(district);CityCommands.Commit(district,plan,folder+"/City.asset");
            var item=CityCommands.Existing(district).Values.First();item.transform.localPosition+=Vector3.one;
            Assert.Throws<ArgumentException>(()=>CityCommands.Commit(district,CityPlanning.Build(district),folder+"/Moved.asset"));
            Assert.That(item!=null,Is.True);
        }
        [Test] public void DuplicateCreatesNewIdentitiesWithoutSharingSourceLists()
        {
            var copy=CityCommands.Duplicate(district,Vector3.right*300);
            try
            {
                Assert.That(copy.id,Is.Not.EqualTo(district.id));Assert.That(copy.parcels[0].id,Is.Not.EqualTo(district.parcels[0].id));
                copy.parcels[0].polygon.outline[0]=Vector2.one;
                Assert.That(district.parcels[0].polygon.outline[0],Is.Not.EqualTo(Vector2.one));
            }
            finally { UnityEngine.Object.DestroyImmediate(copy.gameObject); }
        }
        [Test] public void GradeSeparatedRoadDoesNotCarveSurfaceBlock()
        {
            var road=ScriptableObject.CreateInstance<RoadNetworkAsset>();var mesh=new Mesh();
            mesh.vertices=new[]{new Vector3(-4,20,-100),new Vector3(4,20,-100),new Vector3(4,20,100),new Vector3(-4,20,100)};
            mesh.triangles=new[]{0,1,2,0,2,3};
            road.Initialize(RoadId.New(),"fixture",Array.Empty<RoadBakedLane>(),new[]{new RoadBakedChunk(RoadId.New(),RoadId.New(),mesh,Vector3.zero,kit.wall,null,true)});district.roads=road;
            try { Assert.That(CityGeometry.DecodeBlocks(district),Is.Empty); }
            finally { UnityEngine.Object.DestroyImmediate(road);UnityEngine.Object.DestroyImmediate(mesh); }
        }
        [Test] public void PreviewDisposalDoesNotCreateSavedCollisionOrDirtySource()
        {
            var plan=CityPlanning.Build(district);int collisions=district.GetComponentsInChildren<Collider>().Length;
            using(var preview=new CityPreview(district,plan)) { Assert.That(preview.Running,Is.True); }
            Assert.That(district.GetComponentsInChildren<Collider>().Length,Is.EqualTo(collisions));
            Assert.That(Resources.FindObjectsOfTypeAll<GameObject>().Any(g=>g.name=="CITY PREVIEW — not saved"),Is.False);
        }
        [Test] public void MapScanSortsNumericallyAndReportsMissingDependencies()
        {
            File.WriteAllText(folder+"/scene 10.gltf","{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"missing.bin\",\"byteLength\":12}],\"nodes\":[],\"meshes\":[]}");
            File.WriteAllText(folder+"/scene 2.gltf","{\"asset\":{\"version\":\"2.0\"},\"nodes\":[],\"meshes\":[]}");
            var scan=CityMapLibrary.Scan(folder);Assert.That(scan.Select(s=>s.number),Is.EqualTo(new[]{2,10}));Assert.That(scan[1].missing,Has.Count.EqualTo(1));
        }
        [Test] public void MapAssemblyPreservesEmbeddedTransformsAndUndo()
        {
            var original=new GameObject("authored-part");var child=GameObject.CreatePrimitive(PrimitiveType.Cube);child.transform.SetParent(original.transform);child.transform.localPosition=new Vector3(80,4,-30);
            var prefab=PrefabUtility.SaveAsPrefabAsset(original,folder+"/Part.prefab");UnityEngine.Object.DestroyImmediate(original);
            var source=new CityMapSource { number=12,path=folder+"/Part.prefab",guid=AssetDatabase.AssetPathToGUID(folder+"/Part.prefab") };
            var assembly=CityMapLibrary.Assemble(new[]{source},district.gameObject.scene);
            try
            {
                var part=assembly.GetComponentInChildren<CityMapPart>();Assert.That(part.transform.GetChild(0).localPosition,Is.EqualTo(new Vector3(80,4,-30)));
                Assert.That(part.transform.localPosition,Is.EqualTo(prefab.transform.localPosition));
                Undo.FlushUndoRecordObjects();Undo.PerformUndo();Assert.That(assembly==null,Is.True);
            }
            finally { if(assembly!=null) UnityEngine.Object.DestroyImmediate(assembly); }
        }
        [Test] public void MissingPinnedInstanceAndChangedOutputBlockPublication()
        {
            CityCommands.Commit(district,CityPlanning.Build(district),folder+"/City.asset");
            var item=CityCommands.Existing(district).Values.First();CityCommands.SetOwnership(district,item,CityOwnership.Pinned);
            UnityEngine.Object.DestroyImmediate(item.gameObject);
            Assert.Throws<ArgumentException>(()=>CityCommands.Commit(district,CityPlanning.Build(district),folder+"/Next.asset"));
            Assert.That(CityBuildGuard.Validate(district).Any(d=>d.rule=="CITY_MISSING_PIN"),Is.True);
        }
        [Test] public void TerrainPreviewLeavesSourceDataUnchanged()
        {
            var data=new TerrainData {heightmapResolution=33,size=new Vector3(200,20,200)};
            var terrainRoot=Terrain.CreateTerrainGameObject(data);terrainRoot.transform.position=new Vector3(-100,0,-100);
            district.parcels[0].padHeight=4;
            try
            {
                using(var preview=new CityTerrainPreview(district,terrainRoot.GetComponent<Terrain>()))Assert.That(preview.FillCubicMetres,Is.GreaterThan(0));
                Assert.That(data.GetHeights(0,0,33,33).Cast<float>().All(h=>h==0),Is.True);
                Assert.That(SceneVisibilityManager.instance.IsHidden(terrainRoot),Is.False);
            }
            finally {UnityEngine.Object.DestroyImmediate(terrainRoot);UnityEngine.Object.DestroyImmediate(data);}
        }
        [Test] public void EditorWindowCreatesEveryWorkspaceAndClosesCleanly()
        {
            var window=ScriptableObject.CreateInstance<CityBuilderWindow>();
            try {window.CreateGUI();for(int i=0;i<9;i++)Assert.That(window.rootVisualElement.Query<UnityEngine.UIElements.Button>("city-view-"+i).First(),Is.Not.Null);}
            finally {UnityEngine.Object.DestroyImmediate(window);}
        }
    }
}
