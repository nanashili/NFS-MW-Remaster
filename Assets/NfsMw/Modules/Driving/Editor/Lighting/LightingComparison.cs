using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace NfsMwRemaster.Lighting.Editor
{
    [Serializable] public sealed class LightingCaptureMetadata
    {
        public string utc,unity,device,backend,colorSpace,quality,sourceFingerprint,profileRevision,profileId,scene,notes;
        public Vector3 cameraPosition,cameraEuler;public int width,height;public float exposure,fov;public double captureMilliseconds;
    }
    public static class LightingComparison
    {
        public static Texture2D Difference(Texture2D a,Texture2D b,out float meanError)
        {
            if(!a||!b||a.width!=b.width||a.height!=b.height)throw new InvalidOperationException("Baseline and candidate dimensions must match.");
            var left=a.GetPixels();var right=b.GetPixels();var output=new Color[left.Length];double total=0;
            for(int i=0;i<left.Length;i++){var d=new Color(Mathf.Abs(left[i].r-right[i].r),Mathf.Abs(left[i].g-right[i].g),Mathf.Abs(left[i].b-right[i].b));total+=(d.r+d.g+d.b)/3;output[i]=d;}
            meanError=(float)(total/left.Length);var image=new Texture2D(a.width,a.height,TextureFormat.RGB24,false);image.SetPixels(output);image.Apply();return image;
        }
        public static LightingCaptureMetadata Metadata(AtmosphereController source,AtmosphereProfile profile,Vector3 point,Quaternion rotation,int width,int height,float fov,double ms,string notes)=>new LightingCaptureMetadata{
            utc=DateTime.UtcNow.ToString("O"),unity=Application.unityVersion,device=SystemInfo.graphicsDeviceName,backend=SystemInfo.graphicsDeviceType.ToString(),colorSpace=QualitySettings.activeColorSpace.ToString(),quality=source.quality.ToString(),sourceFingerprint=LightingAudit.Fingerprint(source),profileRevision=profile.Revision,profileId=profile.id,scene=source.gameObject.scene.path,cameraPosition=point,cameraEuler=rotation.eulerAngles,width=width,height=height,exposure=profile.look.exposure,fov=fov,captureMilliseconds=ms,notes=notes};
        public static void Archive(string directory,Texture2D baseline,Texture2D candidate,Texture2D difference,LightingCaptureMetadata baselineMeta,LightingCaptureMetadata candidateMeta,string review)
        {
            if(!baseline||!candidate||baselineMeta==null||candidateMeta==null)throw new InvalidOperationException("Capture both images first.");
            // Unique directory means a failed export can never replace an approved baseline.
            string folder=Path.Combine(directory,"Lighting_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff"));Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder,"baseline.png"),baseline.EncodeToPNG());File.WriteAllBytes(Path.Combine(folder,"candidate.png"),candidate.EncodeToPNG());if(difference)File.WriteAllBytes(Path.Combine(folder,"difference.png"),difference.EncodeToPNG());
            File.WriteAllText(Path.Combine(folder,"baseline.json"),JsonUtility.ToJson(baselineMeta,true));File.WriteAllText(Path.Combine(folder,"candidate.json"),JsonUtility.ToJson(candidateMeta,true));File.WriteAllText(Path.Combine(folder,"review.txt"),review+"\nPixel difference is not a perceptual score. Capture time includes rendering and GPU readback, not target-build frame time.");
            Debug.Log("Lighting comparison archived: "+folder);
        }
    }
}
