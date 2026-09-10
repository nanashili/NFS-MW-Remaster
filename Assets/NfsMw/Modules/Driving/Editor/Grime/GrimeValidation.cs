using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace NfsMwRemaster.Driving.Editor
{
    public static class GrimeValidation
    {
        public static void Capture()
        {
            string folder=System.IO.File.ReadAllText("/tmp/nfs-grime-demo-path.txt");EditorSceneManager.OpenScene(folder+"/GrimePainter.unity");
            var canvas=UnityEngine.Object.FindFirstObjectByType<GrimeCanvas>();
            if(!GrimeCommands.OwnedOutputIntact(canvas))throw new InvalidOperationException("Saved publication failed ownership/reload check.");
            var camera=UnityEngine.Object.FindFirstObjectByType<Camera>();var light=UnityEngine.Object.FindFirstObjectByType<Light>();
            var shader=Shader.Find("NFS/Authoring Grime");var output="/tmp/nfs-grime-render";System.IO.Directory.CreateDirectory(output);
            var rt=new RenderTexture(1280,720,24);var image=new Texture2D(1280,720,TextureFormat.RGB24,false);var old=RenderTexture.active;
            var profile=ScriptableObject.CreateInstance<VolumeProfile>();var volume=new GameObject("Grime capture exposure").AddComponent<Volume>();volume.isGlobal=true;volume.priority=20000;volume.sharedProfile=profile;
            var exposure=profile.Add<Exposure>();exposure.mode.Override(ExposureMode.Fixed);exposure.fixedExposure.Override(13);
            float oldIntensity=light.intensity;var oldUnit=light.lightUnit;light.lightUnit=LightUnit.Lux;light.intensity=90000;
            try
            {
                camera.targetTexture=rt;rt.Create();
                foreach(string mode in new[]{"day","night","grazing","wall","bank"})
                {
                    if(mode=="night"){light.intensity=.25f;exposure.fixedExposure.Override(4);}
                    if(mode=="grazing"){light.intensity=90000;exposure.fixedExposure.Override(13);camera.transform.position=new Vector3(5,2,-23);camera.transform.LookAt(new Vector3(0,0,8));}
                    if(mode=="wall"){camera.transform.position=new Vector3(-10,4,-9);camera.transform.LookAt(new Vector3(-10,4,3));}
                    if(mode=="bank"){camera.transform.position=new Vector3(53,13,-17);camera.transform.LookAt(new Vector3(50,2,-4));}
                    camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();System.IO.File.WriteAllBytes(output+"/"+mode+".png",image.EncodeToPNG());
                }
                var errors=ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").ToArray();if(errors.Length>0)throw new InvalidOperationException(string.Join("\n",errors.Select(e=>e.message)));
                using(var build=GrimeCompiler.Build(canvas))
                {
                    if(!build.valid)throw new InvalidOperationException(string.Join(";",build.diagnostics));
                    string report=$"Unity {Application.unityVersion}\nCPU {SystemInfo.processorType}\nGPU {SystemInfo.graphicsDeviceName} / {SystemInfo.graphicsDeviceType}\nPublication reload ownership PASS\nShader errors {errors.Length}\nDemo: {build.stamps} stamps, {build.rejected} rejected, {build.chunks.Count} draws, {build.vertices} vertices, {build.milliseconds:F2} ms cold Editor build\n";
                    var timings=new double[5];for(int i=0;i<5;i++)using(var warm=GrimeCompiler.Build(canvas))timings[i]=warm.milliseconds;
                    report+="Warm Editor builds ms: "+string.Join(", ",timings.Select(t=>t.ToString("F2")))+"\n";
                    // Duplicate source strokes with independent identities to measure a dense art workload.
                    var originals=canvas.strokes.ToArray();for(int i=0;i<9;i++)foreach(var s in originals){var copy=JsonUtility.FromJson<GrimeStroke>(JsonUtility.ToJson(s));copy.id=Guid.NewGuid().ToString("N");canvas.strokes.Add(copy);}
                    using(var dense=GrimeCompiler.Build(canvas))report+=$"Dense 10x source: valid={dense.valid}, {dense.stamps} stamps, {dense.chunks.Count} draws, {dense.vertices} vertices, {dense.milliseconds:F2} ms; diagnostics={string.Join(";",dense.diagnostics)}\n";
                    report+="Synthetic Editor CPU measurements; no target-player GPU frame budget claimed. Render captures cover day, night and grazing view.\n";System.IO.File.WriteAllText(output+"/measurement.txt",report);Debug.Log(report);
                }
            }
            finally{light.lightUnit=oldUnit;light.intensity=oldIntensity;camera.targetTexture=null;RenderTexture.active=old;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(volume.gameObject);foreach(var component in profile.components)UnityEngine.Object.DestroyImmediate(component);UnityEngine.Object.DestroyImmediate(profile);}
        }
    }
}
