using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
namespace NfsMwRemaster.Lighting.Editor
{
    public static class LightingDemo
    {
        [MenuItem("Tools/NFS MW/Lighting/Create Synthetic Lighting Demonstration")]
        public static void Create()
        {
            string folder="Assets/NfsMw/Modules/Driving/Examples/LightingStudio/"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            var original=SceneManager.GetActiveScene();Scene scene=default;
            try
            {
                // Unity cannot save a preview scene. Keep the demonstration in a separate additive scene.
                if(string.IsNullOrEmpty(original.path))
                {
                    if(original.rootCount>0)throw new InvalidOperationException("Save the current untitled scene before creating the demonstration.");
                    if(!EditorSceneManager.SaveScene(original,folder+"/EmptyWorkspace.unity"))throw new InvalidOperationException("Could not preserve empty workspace scene.");
                }
                scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
                var profile=LightingCommands.CreateProfile(folder+"/District.asset");profile.intent="Synthetic industrial district, neutral baseline. Art direction requires review.";profile.look.keyEuler=new Vector3(75,-30,0);profile.lowFixtureIntensity=.6f;
                var night=LightingCommands.CreateProfile(folder+"/Night.asset",profile);var look=night.look;look.sky=new Color(.035f,.05f,.085f);look.equator=new Color(.02f,.025f,.04f);look.ground=Color.black;look.keyIntensity=.25f;look.exposureEV100=4;look.skyModel=AtmosphereSky.Gradient;look.exposure=.6f;look.bloom=.12f;look.fog=true;look.fogDensity=.012f;look.fogColor=new Color(.025f,.035f,.06f);night.look=look;night.semanticVariant="dry-night";
                var tunnel=LightingCommands.CreateProfile(folder+"/Tunnel.asset",night);look=tunnel.look;look.exposure=1.4f;look.fogDensity=.006f;look.keyIntensity=0;look.exposureEV100=7;tunnel.look=look;tunnel.intent="Synthetic nested tunnel. Review portal glare and traffic silhouette.";
                var frontend=LightingCommands.CreateProfile(folder+"/Frontend.asset",profile);look=frontend.look;look.keyEuler=new Vector3(65,120,0);look.keyIntensity=60000;look.exposureEV100=12;look.exposure=-.5f;frontend.look=look;frontend.intent="Separate synthetic frontend hero look; does not inherit district atmosphere.";
                var root=New("Synthetic Lighting District",scene);var owner=root.AddComponent<AtmosphereController>();owner.fallback=profile;
                var meshes=new List<Renderer>();var fixtures=new List<LightingFixture>();
                Material Material(string name,Color color,float metallic=0,float smooth=.3f){var mat=new Material(Shader.Find("HDRP/Lit")){name=name};mat.SetColor("_BaseColor",color);mat.SetFloat("_Metallic",metallic);mat.SetFloat("_Smoothness",smooth);AssetDatabase.CreateAsset(mat,folder+"/"+name+".mat");return mat;}
                var asphalt=Material("Asphalt",new Color(.065f,.075f,.085f));var concrete=Material("Concrete",new Color(.35f,.37f,.39f));var yellow=Material("Road Markings",new Color(.95f,.7f,.05f));
                GameObject Box(string name,Vector3 pos,Vector3 scale,Material mat){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(go,scene);go.name=name;go.transform.position=pos;go.transform.localScale=scale;var r=go.GetComponent<Renderer>();r.sharedMaterial=mat;meshes.Add(r);return go;}
                Box("Road",new Vector3(0,-.3f,25),new Vector3(16,.5f,110),asphalt);
                for(int i=0;i<13;i++)Box("Centre dash "+i,new Vector3(0,.01f,-20+i*8),new Vector3(.16f,.025f,3),yellow);
                Box("Tunnel left",new Vector3(-8,4,50),new Vector3(1,8,30),concrete);Box("Tunnel right",new Vector3(8,4,50),new Vector3(1,8,30),concrete);Box("Tunnel roof",new Vector3(0,8,50),new Vector3(17,1,30),concrete);
                for(int i=0;i<4;i++)Box("Building "+i,new Vector3(i%2==0?-16:16,6,i*16),new Vector3(10,12,12),concrete);
                Color[] paints={new Color(.015f,.015f,.02f),Color.white,new Color(.3f,.35f,.4f),Color.red};for(int i=0;i<4;i++){var paint=Material("Vehicle Swatch "+i,paints[i],i==2?.9f:.1f,.85f);Box("Synthetic vehicle paint swatch "+i,new Vector3(-5+i*3,1,4+i*5),new Vector3(1.9f,1.4f,4),paint);}
                for(int i=0;i<6;i++){var go=New("Existing fixture "+i,scene);go.transform.position=new Vector3(i%2==0?-6:6,5,8+i*10);var light=go.AddComponent<Light>();light.type=LightType.Point;light.range=16;go.AddComponent<HDAdditionalLightData>();light.lightUnit=UnityEngine.Rendering.LightUnit.Lumen;light.intensity=8400;light.color=i<3?new Color(1,.64f,.3f):new Color(.55f,.7f,1);light.shadows=i%2==0?LightShadows.Soft:LightShadows.None;var f=go.AddComponent<LightingFixture>();f.source=light;f.upstreamId="synthetic-fixture-"+i;f.role=i>2?FixtureRole.Tunnel:FixtureRole.Street;fixtures.Add(f);}
                var cue=New("Protected obstacle cue",scene);cue.transform.position=new Vector3(4,1,25);var cueLight=cue.AddComponent<Light>();cueLight.color=Color.red;cue.AddComponent<HDAdditionalLightData>();cueLight.lightUnit=UnityEngine.Rendering.LightUnit.Lumen;cueLight.intensity=1200;cueLight.range=6;var critical=cue.AddComponent<LightingFixture>();critical.source=cueLight;critical.role=FixtureRole.ObstacleCue;critical.upstreamId="synthetic-critical-cue";fixtures.Add(critical);
                var key=New("Key light",scene).AddComponent<Light>();key.type=LightType.Directional;key.gameObject.AddComponent<HDAdditionalLightData>();key.shadows=LightShadows.Soft;owner.keyLight=key;
                var camera=New("World camera",scene).AddComponent<Camera>();camera.transform.position=new Vector3(0,3,-20);camera.transform.LookAt(new Vector3(0,2,35));camera.nearClipPlane=.1f;camera.farClipPlane=400;LightingCamera.SetPostProcessing(camera.GetLightingCameraData(),true);owner.worldCamera=camera;
                var district=New("District zone",scene).AddComponent<AtmosphereZone>();district.profile=profile;district.size=new Vector3(100,40,200);district.cellId="synthetic-district";
                var inside=New("Tunnel zone",scene).AddComponent<AtmosphereZone>();inside.profile=tunnel;inside.priority=10;inside.transform.position=new Vector3(0,4,50);inside.size=new Vector3(14,8,26);inside.blendMeters=new Vector4(5,10,15,8);inside.cellId="synthetic-tunnel";
                owner.zones=new[]{district,inside};owner.fixtures=fixtures.ToArray();owner.bakeGeometry=meshes.ToArray();
                var bindings=new List<LightingProbeBinding>();foreach(float z in new[]{5f,50f}){var go=New("Reflection coverage "+z,scene);go.transform.position=new Vector3(0,2,z);var probe=go.AddComponent<ReflectionProbe>();probe.size=new Vector3(30,12,50);go.AddComponent<HDAdditionalReflectionData>();probe.resolution=256;probe.boxProjection=true;var b=go.AddComponent<LightingProbeBinding>();b.probe=probe;b.cellId=z<10?"synthetic-district":"synthetic-tunnel";bindings.Add(b);}owner.probes=bindings.ToArray();
                var cameras=ScriptableObject.CreateInstance<LightingCameraSet>();cameras.poses=new[]{new LightingCameraPose{name="District approach",position=camera.transform.position,euler=camera.transform.eulerAngles},new LightingCameraPose{name="Portal entry",position=new Vector3(0,2,30),euler=Vector3.zero},new LightingCameraPose{name="Tunnel interior",position=new Vector3(0,2,48),euler=Vector3.zero},new LightingCameraPose{name="Tunnel exit",position=new Vector3(0,2,42),euler=new Vector3(0,180,0)}};AssetDatabase.CreateAsset(cameras,folder+"/DrivingReviewCameras.asset");
                var probes=New("Legacy indirect probe authoring",scene).AddComponent<LightProbeGroup>();var positions=new List<Vector3>();for(int z=0;z<=70;z+=10)foreach(float x in new[]{-5f,5f})foreach(float y in new[]{1f,4f})positions.Add(new Vector3(x,y,z));probes.probePositions=positions.ToArray();
                foreach(var p in new[]{profile,night,tunnel,frontend})EditorUtility.SetDirty(p);profile.look.ApplyEnvironment(key);AssetDatabase.SaveAssets();string path=folder+"/LightingStudio.unity";
                if(!EditorSceneManager.SaveScene(scene,path))throw new InvalidOperationException("Could not save synthetic scene.");
                File.WriteAllText("/tmp/nfs-lighting-demo-path.txt",path);Debug.Log("Lighting demonstration: "+path);
            }
            finally{if(original.IsValid())SceneManager.SetActiveScene(original);if(scene.IsValid())EditorSceneManager.CloseScene(scene,true);}
            AssetDatabase.Refresh();
        }
        static GameObject New(string name,Scene scene){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);return go;}
        public static void VerifySaved()
        {
            string path=File.ReadAllText("/tmp/nfs-lighting-demo-path.txt");EditorSceneManager.OpenScene(path);var owner=UnityEngine.Object.FindAnyObjectByType<AtmosphereController>();
            if(!owner||!owner.approvedBake)throw new InvalidOperationException("Saved demonstration is missing approved reflections.");
            if(owner.approvedBake.sourceFingerprint!=LightingAudit.Fingerprint(owner))throw new InvalidOperationException("Saved reflection fingerprint is stale after reopening.");
            foreach(var entry in owner.approvedBake.reflections)if(!entry.texture)throw new InvalidOperationException("Saved cubemap missing.");
            foreach(var binding in owner.probes)if(!binding||!binding.probe||!binding.probe.customBakedTexture)throw new InvalidOperationException("Saved probe binding missing.");
            File.WriteAllText("/tmp/nfs-lighting-evidence/reload.txt","PASS: saved stage fingerprint, cubemap subassets and exact probe assignments survived a fresh Editor process.");
        }
        public static void CreateAndCapture(){Create();CaptureEvidence();}
        public static void CaptureEvidence()
        {
            string path=File.ReadAllText("/tmp/nfs-lighting-demo-path.txt");EditorSceneManager.OpenScene(path);var owner=UnityEngine.Object.FindAnyObjectByType<AtmosphereController>();string folder=Path.GetDirectoryName(path);string output="/tmp/nfs-lighting-evidence";Directory.CreateDirectory(output);
            var baseline=owner.fallback;Texture2D a=null,b=null,d=null;LightingCaptureMetadata am=null,bm=null;
            try
            {
                using(var preview=new LightingPreview(owner))
                foreach(string name in new[]{"District","Night","Tunnel","Frontend","Low"})
                {
                    var profile=AssetDatabase.LoadAssetAtPath<AtmosphereProfile>(folder+"/"+(name=="Low"?"Night":name)+".asset");owner.quality=name=="Low"?LightingQuality.Low:LightingQuality.High;var pos=name=="Tunnel"?new Vector3(0,2,43):owner.worldCamera.transform.position;var rot=name=="Tunnel"?Quaternion.Euler(0,180,0):owner.worldCamera.transform.rotation;
                    var timer=System.Diagnostics.Stopwatch.StartNew();var image=preview.Capture(profile.look,pos,rot,960,540,60,owner.quality,profile.lowFixtureIntensity,profile.lowDecorativeShadows);timer.Stop();File.WriteAllBytes(output+"/"+name+".png",image.EncodeToPNG());var meta=LightingComparison.Metadata(owner,profile,pos,rot,960,540,60,timer.Elapsed.TotalMilliseconds,"Synthetic static swatches; Editor Metal render + readback. Not production driving performance.");File.WriteAllText(output+"/"+name+".json",JsonUtility.ToJson(meta,true));if(name=="District"){a=image;am=meta;}else if(name=="Night"){b=image;bm=meta;}else UnityEngine.Object.DestroyImmediate(image);
                }
                owner.quality=LightingQuality.High;d=LightingComparison.Difference(a,b,out var error);LightingComparison.Archive(output,a,b,d,am,bm,"Unapproved diagnostic comparison; mean pixel error "+error);
                using(var job=new LightingBakeJob(owner,64)){while(!job.Finished)job.Tick();var stage=job.SaveStage(folder+"/StagedReflections.asset");LightingCommands.ApplyBake(owner,stage);EditorSceneManager.SaveScene(owner.gameObject.scene);}
                File.WriteAllText(output+"/validation.txt",string.Join("\n",LightingAudit.Validate(owner,owner.worldCamera.transform.position)));
            }
            finally{foreach(var image in new[]{a,b,d})if(image)UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
