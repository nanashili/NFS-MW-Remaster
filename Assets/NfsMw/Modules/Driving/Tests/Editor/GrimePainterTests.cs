using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using NfsMwRemaster.Driving.Editor;
namespace NfsMwRemaster.Driving.Tests
{
    public sealed class GrimePainterTests
    {
        string folder;GrimeCanvas canvas;GrimeReceiver receiver;GrimeBrush brush;Mesh mesh;Material material;
        [SetUp] public void Setup()
        {
            folder="Assets/GrimeTest_"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets",folder.Substring(7));
            mesh=GrimeDemo.Plane(30,30);AssetDatabase.CreateAsset(mesh,folder+"/Surface.asset");material=new Material(Shader.Find("HDRP/Lit"));AssetDatabase.CreateAsset(material,folder+"/Surface.mat");
            receiver=GrimeDemo.Surface("Test receiver",mesh,material,Vector3.zero);canvas=new GameObject("Test dressing").AddComponent<GrimeCanvas>();brush=ScriptableObject.CreateInstance<GrimeBrush>();brush.sizeJitter=0;brush.rotationJitter=0;AssetDatabase.CreateAsset(brush,folder+"/Brush.asset");Physics.SyncTransforms();
        }
        [TearDown] public void Cleanup(){UnityEngine.Object.DestroyImmediate(canvas.gameObject);UnityEngine.Object.DestroyImmediate(receiver.gameObject);Undo.ClearAll();AssetDatabase.DeleteAsset(folder);}
        GrimeStroke Stroke(params Vector3[] points)
        {
            var s=new GrimeStroke{layerId=canvas.layers[0].id,brush=brush,brushRevision=GrimeGeometry.BrushRevision(brush),width=1};
            foreach(var p in points){Assert.That(receiver.surface.Raycast(new Ray(p+Vector3.up*2,Vector3.down),out var hit,4),Is.True);s.samples.Add(GrimeGeometry.Capture(receiver,hit,Vector3.forward));}canvas.strokes.Add(s);return s;
        }
        [Test] public void SamePolylineResamplesIndependentlyOfInputDensity()
        {
            var a=GrimeGeometry.Resample(new[]{Vector3.zero,Vector3.forward*10},.35f);var b=GrimeGeometry.Resample(Enumerable.Range(0,101).Select(i=>Vector3.forward*i*.1f).ToArray(),.35f);Assert.That(a.Count,Is.EqualTo(b.Count));for(int i=0;i<a.Count;i++)Assert.That(Vector3.Distance(a[i],b[i]),Is.LessThan(.0001));
        }
        [Test] public void CornersPreserveCarriedSpacing(){var p=GrimeGeometry.Resample(new[]{Vector3.zero,Vector3.right*.5f,new Vector3(.5f,0,1)},1);Assert.That(p.Count,Is.EqualTo(2));Assert.That(p[1],Is.EqualTo(new Vector3(.5f,0,.5f)));}
        [Test] public void DuplicateSamplesDoNotLoop(){Assert.That(GrimeGeometry.Resample(new[]{Vector3.zero,Vector3.zero},1).Count,Is.EqualTo(1));}
        [Test] public void InvalidSpacingRejected(){Assert.Throws<ArgumentOutOfRangeException>(()=>GrimeGeometry.Resample(new[]{Vector3.zero},0));}
        [Test] public void SeedIsLocalToStroke(){float value=GrimeGeometry.Noise(1,"A",4,0);GrimeGeometry.Noise(99,"B",6,0);Assert.That(GrimeGeometry.Noise(1,"A",4,0),Is.EqualTo(value));Assert.That(GrimeGeometry.Noise(1,"B",4,0),Is.Not.EqualTo(value));}
        [Test] public void MeshBatchHasSharedMaterialAndNoProjectors(){Stroke(Vector3.back*5,Vector3.forward*5);using(var b=GrimeCompiler.Build(canvas)){Assert.That(b.valid,Is.True,string.Join(";",b.diagnostics));Assert.That(b.stamps,Is.GreaterThan(20));Assert.That(b.chunks.Count,Is.LessThan(b.stamps));Assert.That(b.chunks.Select(c=>c.material).Distinct().Count(),Is.EqualTo(1));Assert.That(b.chunks.All(c=>c.mesh.normals.All(n=>n.y>.99f)),Is.True);}}
        [Test] public void OnlyElevatedReceiverGetsVertices()
        {
            var lower=GrimeDemo.Surface("Lower level",mesh,material,Vector3.zero);
            try{receiver.transform.position=Vector3.up*5;Physics.SyncTransforms();Stroke(Vector3.up*5);using(var b=GrimeCompiler.Build(canvas)){Assert.That(b.valid,Is.True);Assert.That(b.chunks.SelectMany(c=>c.mesh.vertices).All(v=>v.y>4.99f),Is.True);}}finally{UnityEngine.Object.DestroyImmediate(lower.gameObject);}
        }
        [Test] public void TransparentReceiverExplicitlyRejected(){material.renderQueue=3000;Assert.That(GrimeGeometry.ReceiverAllowed(canvas,receiver,out var why),Is.False);StringAssert.Contains("Transparent",why);}
        [Test] public void PhysicsAndRenderingMasksAreIndependent(){canvas.physicsLayers=~0;canvas.renderingLayers=0;Assert.That(GrimeGeometry.ReceiverAllowed(canvas,receiver,out _),Is.False);canvas.renderingLayers=uint.MaxValue;canvas.physicsLayers=0;Assert.That(GrimeGeometry.ReceiverAllowed(canvas,receiver,out _),Is.False);}
        [Test] public void CategoryFilterRejectsWall(){receiver.surfaceCategory="glass";Assert.That(GrimeGeometry.ReceiverAllowed(canvas,receiver,out _),Is.False);}
        [Test] public void DynamicBodiesRejected(){var rb=receiver.gameObject.AddComponent<Rigidbody>();rb.isKinematic=true;Assert.That(GrimeGeometry.ReceiverAllowed(canvas,receiver,out _),Is.False);}
        [Test] public void ChangedMeshInvalidatesTriangleAnchors(){Stroke(Vector3.zero);mesh.vertices=mesh.vertices.Select(v=>v+Vector3.up*.1f).ToArray();EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssets();using(var b=GrimeCompiler.Build(canvas)){Assert.That(b.valid,Is.False);StringAssert.Contains("stale",string.Join(";",b.diagnostics));}}
        [Test] public void DeletedReceiverFailsRatherThanRemaps(){var s=Stroke(Vector3.zero);s.samples[0].receiver=null;using(var b=GrimeCompiler.Build(canvas))Assert.That(b.valid,Is.False);}
        [Test] public void NarrowStopLineRejectsWholeIntersectingStamp(){Stroke(Vector3.zero);canvas.masks.Add(new GrimeMask{polygon=new System.Collections.Generic.List<Vector2>{new Vector2(.12f,-2),new Vector2(.13f,-2),new Vector2(.13f,2),new Vector2(.12f,2)}});using(var b=GrimeCompiler.Build(canvas)){Assert.That(b.valid,Is.True);Assert.That(b.stamps,Is.Zero);Assert.That(b.rejected,Is.EqualTo(1));}}
        [Test] public void MaskDepthDoesNotExcludeOtherLevel(){Stroke(Vector3.zero);canvas.masks.Add(new GrimeMask{origin=Vector3.up*5,depth=1});using(var b=GrimeCompiler.Build(canvas))Assert.That(b.stamps,Is.EqualTo(1));}
        [Test] public void InclusionRejectsOutsideFootprint(){Stroke(Vector3.right*5);canvas.masks.Add(new GrimeMask{inclusion=true});using(var b=GrimeCompiler.Build(canvas))Assert.That(b.stamps,Is.Zero);}
        [Test] public void EdgeFootprintIsRejected(){Stroke(Vector3.right*14.9f);using(var b=GrimeCompiler.Build(canvas)){Assert.That(b.valid,Is.True);Assert.That(b.rejected,Is.EqualTo(1));}}
        [Test] public void DisabledLayerPreservesSource(){Stroke(Vector3.zero);canvas.layers[0].enabled=false;using(var b=GrimeCompiler.Build(canvas))Assert.That(b.stamps,Is.Zero);Assert.That(canvas.strokes.Count,Is.EqualTo(1));}
        [Test] public void StampBudgetFailsBeforePublishing(){Stroke(Vector3.back*5,Vector3.forward*5);canvas.maximumStamps=2;using(var b=GrimeCompiler.Build(canvas)){Assert.That(b.valid,Is.False);StringAssert.Contains("budget",string.Join(";",b.diagnostics));}}
        [Test] public void BrushChangeRequiresExplicitReview(){Stroke(Vector3.zero);brush.tint=Color.red;using(var b=GrimeCompiler.Build(canvas)){Assert.That(b.valid,Is.False);StringAssert.Contains("brush changed",string.Join(";",b.diagnostics));}}
        [Test] public void CancelStopsBuild(){Stroke(Vector3.zero);Assert.Throws<OperationCanceledException>(()=>GrimeCompiler.Build(canvas,_=>true));}
        [Test] public void PaintUndoRemovesOneCompleteStroke(){var s=Stroke(Vector3.zero,Vector3.forward);canvas.strokes.Clear();GrimeCommands.Add(canvas,s);Undo.FlushUndoRecordObjects();Undo.PerformUndo();Assert.That(canvas.strokes.Count,Is.Zero);Undo.PerformRedo();Assert.That(canvas.strokes.Count,Is.EqualTo(1));}
        [Test] public void OilAppearanceDoesNotChangeSurfaceMaterialOrRoadGraph(){brush.pattern=GrimePattern.Oil;Stroke(Vector3.zero);string before=EditorJsonUtility.ToJson(material);var physics=receiver.surface.sharedMaterial;using(var b=GrimeCompiler.Build(canvas))Assert.That(b.valid,Is.True);Assert.That(EditorJsonUtility.ToJson(material),Is.EqualTo(before));Assert.That(receiver.surface.sharedMaterial,Is.SameAs(physics));Assert.That(receiver.GetComponent<VehicleSurface>(),Is.Null);}
        [TestCase(1)] [TestCase(2)] [TestCase(3)] public void FailedBakeRetainsOldOutput(int step)
        {
            Stroke(Vector3.zero);var first=GrimeCommands.Bake(canvas,folder+"/First.asset");var root=canvas.generatedRoot;
            Assert.Throws<InvalidOperationException>(()=>GrimeCommands.Bake(canvas,folder+"/Failed.asset",s=>{if(s==step)throw new InvalidOperationException("Injected");}));
            Assert.That(canvas.published,Is.SameAs(first));Assert.That(canvas.generatedRoot,Is.SameAs(root));Assert.That(AssetDatabase.LoadMainAssetAtPath(folder+"/Failed.asset"),Is.Null);Assert.That(canvas.strokes.Count,Is.EqualTo(1));
        }
        [Test] public void ModifiedGeneratedMaterialBlocksReplacement(){Stroke(Vector3.zero);var p=GrimeCommands.Bake(canvas,folder+"/First.asset");p.Chunks[0].material.SetColor("_BaseColor",Color.magenta);Assert.That(GrimeCommands.OwnedOutputIntact(canvas),Is.False);Assert.Throws<InvalidOperationException>(()=>GrimeCommands.Bake(canvas,folder+"/Second.asset"));}
        [Test] public void AddedHandArtBlocksReplacement(){Stroke(Vector3.zero);GrimeCommands.Bake(canvas,folder+"/First.asset");new GameObject("Manual art").transform.SetParent(canvas.generatedRoot.transform);Assert.That(GrimeCommands.OwnedOutputIntact(canvas),Is.False);}
        [Test] public void PublicationFingerprintDoesNotDependOnPreviousBake(){Stroke(Vector3.zero);string before;using(var b=GrimeCompiler.Build(canvas))before=b.fingerprint;GrimeCommands.Bake(canvas,folder+"/First.asset");using(var b=GrimeCompiler.Build(canvas))Assert.That(b.fingerprint,Is.EqualTo(before));}
        [Test] public void FutureImportSchemaRejected(){Assert.Throws<ArgumentException>(()=>GrimeTransfer.Import("{\"schema\":99,\"data\":\"{}\",\"references\":[]}"));}
        [Test] public void ExportContainsNoTransientInstanceIds(){Stroke(Vector3.zero);StringAssert.DoesNotContain("instanceID",GrimeTransfer.Export(canvas));}
        [Test] public void RoadRangeRejectsMissingLane(){var n=ScriptableObject.CreateInstance<RoadNetworkAsset>();try{n.Initialize(RoadId.New(),"test",Array.Empty<RoadBakedLane>(),Array.Empty<RoadBakedChunk>());Assert.Throws<InvalidOperationException>(()=>GrimeCommands.RoadRange(canvas,brush,receiver,n,"missing",0,1,0,1));}finally{UnityEngine.Object.DestroyImmediate(n);}}
        RoadNetworkAsset Network(string revision, RoadId lane, float height)
        {
            var n=ScriptableObject.CreateInstance<RoadNetworkAsset>();n.Initialize(RoadId.New(),revision,new[]{new RoadBakedLane(lane,RoadId.New(),default,15,new[]{new RoadLaneSample{position=new Vector3(0,height,0),distance=0,station=0,width=6,forward=Vector3.forward,up=Vector3.up,left=Vector3.left},new RoadLaneSample{position=new Vector3(0,height,10),distance=10,station=10,width=6,forward=Vector3.forward,up=Vector3.up,left=Vector3.left}},Array.Empty<RoadId>(),null)},Array.Empty<RoadBakedChunk>());AssetDatabase.CreateAsset(n,folder+"/"+revision+".asset");return n;
        }
        [Test] public void RoadRevisionRequiresReviewAndRemapsExactLane()
        {
            var lane=RoadId.New();var first=Network("v1",lane,0);var second=Network("v2",lane,.1f);var s=GrimeCommands.RoadRange(canvas,brush,receiver,first,lane.ToString(),1,5,0,4);canvas.strokes.Add(s);
            s.samples[0].network=second;using(var b=GrimeCompiler.Build(canvas)){Assert.That(b.valid,Is.False);StringAssert.Contains("revision",string.Join(";",b.diagnostics));}
            Assert.That(GrimeCommands.PreviewRemap(canvas,second).Count,Is.EqualTo(s.samples.Count));GrimeCommands.Remap(canvas,second);Assert.That(s.samples.All(a=>a.roadRevision=="v2"),Is.True);Assert.That(s.samples[0].position.y,Is.EqualTo(.1f).Within(.0001));using(var b=GrimeCompiler.Build(canvas))Assert.That(b.valid,Is.True,string.Join(";",b.diagnostics));
        }
        [Test] public void RoadSplitNeverChoosesNearestLane(){var first=Network("v1",RoadId.New(),0);var second=Network("v2",RoadId.New(),0);canvas.strokes.Add(GrimeCommands.RoadRange(canvas,brush,receiver,first,first.Lanes[0].Id.ToString(),0,5,0,2));Assert.Throws<InvalidOperationException>(()=>GrimeCommands.PreviewRemap(canvas,second));}
        [Test] public void PortableRoundTripPreservesDisabledLayersAndFreshIdentities()
        {
            Stroke(Vector3.zero);canvas.layers[0].enabled=false;canvas.layers[0].locked=true;canvas.masks.Add(new GrimeMask{layerId=canvas.layers[0].id});
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(canvas.gameObject.scene,folder+"/Source.unity");
            var copy=GrimeTransfer.Import(GrimeTransfer.Export(canvas));try{Assert.That(copy.id,Is.Not.EqualTo(canvas.id));Assert.That(copy.layers[0].id,Is.Not.EqualTo(canvas.layers[0].id));Assert.That(copy.layers[0].enabled,Is.False);Assert.That(copy.layers[0].locked,Is.True);Assert.That(copy.strokes[0].layerId,Is.EqualTo(copy.layers[0].id));Assert.That(copy.masks[0].layerId,Is.EqualTo(copy.layers[0].id));Assert.That(copy.strokes[0].samples[0].receiver,Is.SameAs(receiver));Assert.That(copy.strokes[0].brush,Is.SameAs(brush));}finally{UnityEngine.Object.DestroyImmediate(copy.gameObject);}
        }
        [Test] public void DuplicateCanvasRequiresFreshIdentity(){Stroke(Vector3.zero);var clone=UnityEngine.Object.Instantiate(canvas.gameObject);try{using(var b=GrimeCompiler.Build(canvas))Assert.That(b.valid,Is.False);GrimeCommands.FreshIdentity(clone.GetComponent<GrimeCanvas>());using(var b=GrimeCompiler.Build(canvas))Assert.That(b.valid,Is.True);}finally{UnityEngine.Object.DestroyImmediate(clone);}}
        [Test] public void ConcaveInclusionIsRejected(){Stroke(Vector3.zero);canvas.masks.Add(new GrimeMask{inclusion=true,polygon=new System.Collections.Generic.List<Vector2>{new Vector2(-2,-2),new Vector2(2,-2),Vector2.zero,new Vector2(2,2),new Vector2(-2,2)}});using(var b=GrimeCompiler.Build(canvas))Assert.That(b.valid,Is.False);}
        [Test] public void NormalChannelRequiresNormalImporter(){var texture=new Texture2D(2,2);AssetDatabase.CreateAsset(texture,folder+"/ColorTexture.asset");brush.normalMap=texture;Stroke(Vector3.zero);using(var b=GrimeCompiler.Build(canvas)){Assert.That(b.valid,Is.False);StringAssert.Contains("Normal",string.Join(";",b.diagnostics));}}
        [Test] public void UnloadingPreviewWindowRestoresBakedRenderersAndReleasesMeshes()
        {
            Stroke(Vector3.zero);GrimeCommands.Bake(canvas,folder+"/Published.asset");
            int before=Resources.FindObjectsOfTypeAll<Mesh>().Count(m=>m.name=="Grime chunk");
            var window=ScriptableObject.CreateInstance<GrimePainterWindow>();
            try
            {
                var state=new SerializedObject(window);state.FindProperty("canvas").objectReferenceValue=canvas;state.ApplyModifiedPropertiesWithoutUndo();
                typeof(GrimePainterWindow).GetMethod("Preview",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(window,null);
                Assert.That(canvas.generatedRoot.GetComponentsInChildren<Renderer>().All(r=>r.forceRenderingOff),Is.True);
            }
            finally{UnityEngine.Object.DestroyImmediate(window);}
            Assert.That(canvas.generatedRoot.GetComponentsInChildren<Renderer>().Any(r=>r.forceRenderingOff),Is.False);
            Assert.That(Resources.FindObjectsOfTypeAll<Mesh>().Count(m=>m.name=="Grime chunk"),Is.EqualTo(before));
            Assert.That(GrimePainterWindow.Active,Is.Null);
        }
        [Test] public void ProtectionUsesSurfacePositionBeforeNormalBias(){Stroke(Vector3.zero);canvas.masks.Add(new GrimeMask{depth=.001f});using(var b=GrimeCompiler.Build(canvas)){Assert.That(b.valid,Is.True,string.Join(";",b.diagnostics));Assert.That(b.stamps,Is.Zero);}}
    }
}
