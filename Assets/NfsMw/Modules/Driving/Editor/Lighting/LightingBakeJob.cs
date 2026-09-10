using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace NfsMwRemaster.Lighting.Editor
{
    // One face per editor update keeps cancellation responsive. Publication is a separate operation.
    public sealed class LightingBakeJob : IDisposable
    {
        readonly AtmosphereController owner;readonly string fingerprint;readonly LightingPreview preview;readonly int resolution;
        readonly List<BakedReflection> captures=new List<BakedReflection>();Cubemap current;int index,face;bool disposed;
        public LightingBakeSet Result {get;private set;}public string Status {get;private set;}="Staging";
        public bool Finished {get;private set;}public float Progress=>(index*6+face)/(float)Mathf.Max(1,owner.probes.Length*6);
        public LightingBakeJob(AtmosphereController source,int size=128)
        {
            if(!source||!source.fallback||!source.fallback.IsValid||source.probes.Length==0)throw new InvalidOperationException("Assign a valid profile and registered reflection probes.");
            if(source.bakeGeometry.Length==0||Array.Exists(source.bakeGeometry,r=>!r||!(r is MeshRenderer)||!r.GetComponent<MeshFilter>()||!r.GetComponent<MeshFilter>().sharedMesh))throw new InvalidOperationException("Register complete static mesh geometry before staging reflections.");
            if(size!=64&&size!=128&&size!=256)throw new ArgumentOutOfRangeException(nameof(size));
            var ids=new HashSet<string>();foreach(var p in source.probes)if(!p||!p.probe||string.IsNullOrWhiteSpace(p.id)||!ids.Add(p.id))throw new InvalidOperationException("Probe references and unique IDs are required.");
            owner=source;resolution=size;fingerprint=LightingAudit.Fingerprint(source);preview=new LightingPreview(source);
        }
        public void Tick()
        {
            if(Finished||disposed)return;
            try
            {
                if(!owner||fingerprint!=LightingAudit.Fingerprint(owner))throw new InvalidOperationException("Sources changed while capturing. Previous approved bake is preserved.");
                var binding=owner.probes[index];if(!current)current=new Cubemap(resolution,TextureFormat.RGBAHalf,true){name="Reflection_"+binding.id};
                var forward=UnityEngine.Rendering.CoreUtils.lookAtList;
                var up=UnityEngine.Rendering.CoreUtils.upVectorList;
                var position=binding.probe.transform.position+binding.probe.center;var look=AtmosphereResolver.Resolve(owner.fallback,owner.zones,position);
                // Reflection captures contain scene radiance without camera exposure/grading.
                look.exposure=0;look.bloom=0;look.vignette=0;look.contrast=0;look.saturation=0;look.filter=Color.white;look.tonemapping=UnityEngine.Rendering.HighDefinition.TonemappingMode.None;
                var image=preview.Capture(look,position,Quaternion.LookRotation(forward[face],up[face]),resolution,resolution,90,owner.quality,owner.fallback.lowFixtureIntensity,owner.fallback.lowDecorativeShadows,true);
                try{current.SetPixels(image.GetPixels(),(CubemapFace)face);}finally{Object.DestroyImmediate(image);}
                face++;if(face==6){current.Apply();captures.Add(new BakedReflection{probeId=binding.id,texture=current});current=null;index++;face=0;}
                if(index==owner.probes.Length){Finished=true;Status="Ready to save a stage; approval has not changed any probe.";}
            }
            catch(Exception ex){Status="Failed: "+ex.Message;Finished=true;Dispose();throw;}
        }
        public LightingBakeSet SaveStage(string path)
        {
            if(disposed||!Finished||index!=owner.probes.Length||fingerprint!=LightingAudit.Fingerprint(owner))throw new InvalidOperationException("Only a complete, current capture can be saved.");
            if(!path.StartsWith("Assets/",StringComparison.Ordinal)||!path.EndsWith(".asset",StringComparison.Ordinal)||path.Contains(".."))throw new InvalidOperationException("Choose an .asset path under Assets.");
            path=AssetDatabase.GenerateUniqueAssetPath(path);var result=ScriptableObject.CreateInstance<LightingBakeSet>();result.sourceFingerprint=fingerprint;result.profileRevision=owner.fallback.Revision;result.scenePath=owner.gameObject.scene.path;result.createdUtc=DateTime.UtcNow.ToString("O");result.reflections=captures.ToArray();
            try{AssetDatabase.CreateAsset(result,path);foreach(var capture in captures)AssetDatabase.AddObjectToAsset(capture.texture,result);EditorUtility.SetDirty(result);AssetDatabase.SaveAssets();Result=result;captures.Clear();Status="Staged at "+path;return result;}
            catch{if(AssetDatabase.LoadMainAssetAtPath(path))AssetDatabase.DeleteAsset(path);else Object.DestroyImmediate(result);throw;}
        }
        public void Dispose()
        {if(disposed)return;disposed=true;preview?.Dispose();if(current)Object.DestroyImmediate(current);foreach(var capture in captures)if(capture.texture&&!AssetDatabase.Contains(capture.texture))Object.DestroyImmediate(capture.texture);captures.Clear();if(!Finished)Status="Cancelled; previous bake preserved.";Finished=true;}
    }
}
