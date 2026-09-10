using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace NfsMwRemaster.Driving.Editor
{
    public static class GrimeDemo
    {
        public const string Folder="Assets/NfsMw/Modules/Driving/Examples/GrimePainter";
        public static void CreateBrushes()
        {
            System.IO.Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            foreach(GrimePattern pattern in Enum.GetValues(typeof(GrimePattern)))
            {
                string path=Folder+"/"+pattern+".asset";if(AssetDatabase.LoadAssetAtPath<GrimeBrush>(path))continue;
                var b=ScriptableObject.CreateInstance<GrimeBrush>();b.pattern=pattern;b.label=pattern.ToString();b.rotationJitter=pattern==GrimePattern.TireWear||pattern==GrimePattern.Crack?4:35;
                b.aspect=pattern==GrimePattern.Crack?4:pattern==GrimePattern.TireWear?2:1;b.width=pattern==GrimePattern.Crack?.4f:1.5f;
                if(pattern==GrimePattern.Oil||pattern==GrimePattern.Puddle){b.smoothness=.9f;b.tint=new Color(.04f,.06f,.07f,.8f);}
                if(pattern==GrimePattern.Graffiti){b.tint=new Color(.8f,.2f,.07f,.95f);b.category="Walls";}
                if(pattern==GrimePattern.Repair)b.tint=new Color(.09f,.1f,.12f,.9f);
                AssetDatabase.CreateAsset(b,path);
            }
            AssetDatabase.SaveAssets();
        }
        public static GrimeReceiver Surface(string name,Mesh mesh,Material material,Vector3 position)
        {
            var go=new GameObject(name);go.transform.position=position;go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;go.AddComponent<MeshCollider>().sharedMesh=mesh;
            var r=go.AddComponent<GrimeReceiver>();r.surface=go.GetComponent<MeshCollider>();r.surfaceRenderer=go.GetComponent<MeshRenderer>();return r;
        }
        public static Mesh Plane(float width,float length)
        {
            var mesh=new Mesh{name="Synthetic surface"};mesh.vertices=new[]{new Vector3(-width/2,0,-length/2),new Vector3(width/2,0,-length/2),new Vector3(-width/2,0,length/2),new Vector3(width/2,0,length/2)};mesh.triangles=new[]{0,2,1,1,2,3};mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        [MenuItem("Tools/NFS MW/Surface Dressing/Build Demonstration")]
        public static void Build()
        {
            CreateBrushes();string folder=Folder+"/Demo_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");System.IO.Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            if(Application.isBatchMode&&SceneManager.GetActiveScene().path==""&&SceneManager.GetActiveScene().rootCount==0)EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),folder+"/Empty.unity");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var asphalt=new Material(Shader.Find("HDRP/Lit")){name="Synthetic asphalt"};asphalt.SetColor("_BaseColor",new Color(.32f,.34f,.36f));AssetDatabase.CreateAsset(asphalt,folder+"/Asphalt.mat");
            var flat=Plane(14,36);AssetDatabase.CreateAsset(flat,folder+"/Flat.asset");var lower=Surface("Lower road",flat,asphalt,Vector3.zero);var bridge=Surface("Overpass",flat,asphalt,new Vector3(0,5,0));bridge.transform.rotation=Quaternion.Euler(0,90,0);
            var wallMesh=Plane(12,8);AssetDatabase.CreateAsset(wallMesh,folder+"/WallMesh.asset");var wall=Surface("Opaque graffiti wall",wallMesh,asphalt,new Vector3(-10,4,3));wall.transform.rotation=Quaternion.Euler(-90,0,0);wall.surfaceCategory="concrete";
            var glass=new Material(asphalt){name="Unsupported glass fixture",renderQueue=3000};glass.SetFloat("_SurfaceType",1);glass.SetFloat("_SrcBlend",5);glass.SetFloat("_DstBlend",10);glass.SetFloat("_ZWrite",0);glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(glass);glass.SetColor("_BaseColor",new Color(.2f,.7f,.9f,.2f));AssetDatabase.CreateAsset(glass,folder+"/Glass.mat");var glassReceiver=Surface("Glass - must reject",wallMesh,glass,new Vector3(-10,4,-8));glassReceiver.transform.rotation=Quaternion.Euler(-90,0,0);
            // The synthetic lane is an actual RoadNetworkAsset consumer fixture, not alternate topology.
            var laneId=RoadId.New();var roadId=RoadId.New();var samples=new List<RoadLaneSample>();var vertices=new List<Vector3>();var indices=new List<int>();float distance=0;Vector3 previous=default;
            for(int i=0;i<=80;i++)
            {
                float angle=i/80f*1.3f;var p=new Vector3(40+Mathf.Cos(angle)*16,2+Mathf.Sin(angle)*2,-16+Mathf.Sin(angle)*16);var forward=new Vector3(-Mathf.Sin(angle),.125f*Mathf.Cos(angle),Mathf.Cos(angle)).normalized;var up=Quaternion.AngleAxis(10,forward)*Vector3.up;var left=Vector3.Cross(forward,up).normalized;up=Vector3.Cross(left,forward).normalized;
                if(i>0)distance+=Vector3.Distance(previous,p);previous=p;samples.Add(new RoadLaneSample{station=distance,distance=distance,width=8,position=p,forward=forward,left=left,up=up});vertices.Add(p+left*4);vertices.Add(p-left*4);if(i>0){int a=(i-1)*2;indices.AddRange(new[]{a,a+2,a+1,a+1,a+2,a+3});}
            }
            var curveMesh=new Mesh{name="Banked road fixture"};curveMesh.SetVertices(vertices);curveMesh.SetTriangles(indices,0);curveMesh.RecalculateNormals();curveMesh.RecalculateBounds();AssetDatabase.CreateAsset(curveMesh,folder+"/Curve.asset");var curve=Surface("Curved banked road",curveMesh,asphalt,Vector3.zero);
            var network=ScriptableObject.CreateInstance<RoadNetworkAsset>();network.Initialize(RoadId.New(),"synthetic-grime-road-v1",new[]{new RoadBakedLane(laneId,roadId,default,15,samples.ToArray(),Array.Empty<RoadId>(),null)},Array.Empty<RoadBakedChunk>());AssetDatabase.CreateAsset(network,folder+"/Road.asset");
            var shifted=samples.Select(s=>{s.position+=Vector3.up*.1f;return s;}).ToArray();var revision=ScriptableObject.CreateInstance<RoadNetworkAsset>();revision.Initialize(network.NetworkId,"synthetic-grime-road-v2",new[]{new RoadBakedLane(laneId,roadId,default,15,shifted,Array.Empty<RoadId>(),null)},Array.Empty<RoadBakedChunk>());AssetDatabase.CreateAsset(revision,folder+"/RoadRevision.asset");
            var canvas=new GameObject("Synthetic surface dressing").AddComponent<GrimeCanvas>();canvas.requiredCategory="";canvas.maximumSlope=180;
            Physics.SyncTransforms();
            GrimeBrush Brush(GrimePattern p)=>AssetDatabase.LoadAssetAtPath<GrimeBrush>(Folder+"/"+p+".asset");
            void Stroke(GrimeReceiver r,GrimePattern pattern,params Vector3[] points)
            {var b=Brush(pattern);var s=new GrimeStroke{brush=b,brushRevision=GrimeGeometry.BrushRevision(b),layerId=canvas.layers[0].id,width=b.width};foreach(var p in points){var n=r.transform.up;if(!r.surface.Raycast(new Ray(p+n*2,-n),out var hit,4))throw new InvalidOperationException("Demo ray missed "+r.name);s.samples.Add(GrimeGeometry.Capture(r,hit,r.transform.forward));}canvas.strokes.Add(s);}
            Stroke(lower,GrimePattern.TireWear,new Vector3(-2,0,-12),new Vector3(-2,0,12));Stroke(lower,GrimePattern.Crack,new Vector3(2,0,-8),new Vector3(3,0,9));Stroke(lower,GrimePattern.Oil,new Vector3(3,0,-12));Stroke(lower,GrimePattern.Repair,new Vector3(0,0,8));
            Stroke(bridge,GrimePattern.Dirt,new Vector3(-12,5,0),new Vector3(12,5,0));Stroke(wall,GrimePattern.Graffiti,wall.transform.position);canvas.strokes.Add(GrimeCommands.RoadRange(canvas,Brush(GrimePattern.TireWear),curve,network,laneId.ToString(),1,network.Lanes[0].Length-1,0,321));
            canvas.masks.Add(new GrimeMask{name="Synthetic stop line protection",origin=new Vector3(0,0,-8),polygon=new List<Vector2>{new Vector2(-6,-.3f),new Vector2(6,-.3f),new Vector2(6,.3f),new Vector2(-6,.3f)}});
            var white=new Material(asphalt);white.SetColor("_BaseColor",Color.white);AssetDatabase.CreateAsset(white,folder+"/Marking.mat");var stripe=GameObject.CreatePrimitive(PrimitiveType.Cube);stripe.name="Synthetic critical stop line";stripe.transform.position=new Vector3(0,.012f,-8);stripe.transform.localScale=new Vector3(12,.02f,.25f);stripe.GetComponent<MeshRenderer>().sharedMaterial=white;UnityEngine.Object.DestroyImmediate(stripe.GetComponent<Collider>());
            var light=new GameObject("Daylight").AddComponent<Light>();light.type=LightType.Directional;Rendering.HdrpSceneDefaults.Sun(light);light.transform.rotation=Quaternion.Euler(50,-30,0);RenderSettings.ambientLight=new Color(.45f,.48f,.55f);
            var camera=new GameObject("Demo camera").AddComponent<Camera>(); NfsMwRemaster.Driving.Editor.Rendering.HdrpSceneDefaults.Camera(camera);camera.transform.position=new Vector3(25,45,-50);camera.transform.LookAt(new Vector3(22,0,0));camera.farClipPlane=300;camera.backgroundColor=new Color(.12f,.15f,.19f);camera.clearFlags=CameraClearFlags.SolidColor;
            EditorSceneManager.SaveScene(scene,folder+"/GrimePainter.unity");
            GrimeCommands.Bake(canvas,folder+"/Dressing.asset");EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();Selection.activeGameObject=canvas.gameObject;
            System.IO.File.WriteAllText("/tmp/nfs-grime-demo-path.txt",folder);Debug.Log("GRIME_DEMO "+folder);
        }
    }
}
