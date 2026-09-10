using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace NfsMwRemaster.Lighting.Editor
{
    public static class LightingSequence
    {
        public static void Capture(AtmosphereController owner,LightingCameraSet cameras,string directory)
        {
            if(!owner||!owner.fallback||!cameras||cameras.schemaVersion!=1||cameras.poses.Length==0)throw new InvalidOperationException("Choose an owner and version-one camera set with poses.");
            if(cameras.poses.Length>200)throw new InvalidOperationException("Split review sets larger than 200 poses.");
            foreach(var pose in cameras.poses)if(pose==null)throw new InvalidOperationException("Camera set contains a missing pose.");
            string folder=Path.Combine(directory,"ZoneSequence_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff"));Directory.CreateDirectory(folder);
            int completed=0;
            try
            {
                using(var preview=new LightingPreview(owner))
                foreach(var pose in cameras.poses)
                {
                    if(EditorUtility.DisplayCancelableProgressBar("Zone review sequence",pose.name,completed/(float)cameras.poses.Length))break;
                    var look=AtmosphereResolver.Resolve(owner.fallback,owner.zones,pose.position);var timer=System.Diagnostics.Stopwatch.StartNew();var image=preview.Capture(look,pose.position,Quaternion.Euler(pose.euler),960,540,pose.fieldOfView,owner.quality,owner.fallback.lowFixtureIntensity,owner.fallback.lowDecorativeShadows);timer.Stop();
                    try{File.WriteAllBytes(Path.Combine(folder,completed.ToString("D3")+".png"),image.EncodeToPNG());var metadata=LightingComparison.Metadata(owner,owner.fallback,pose.position,Quaternion.Euler(pose.euler),960,540,pose.fieldOfView,timer.Elapsed.TotalMilliseconds,"Static zone composition; no time integration. Pose: "+pose.name+" / Camera set "+cameras.id);metadata.exposure=look.exposure;File.WriteAllText(Path.Combine(folder,completed.ToString("D3")+".json"),JsonUtility.ToJson(metadata,true));}finally{UnityEngine.Object.DestroyImmediate(image);}completed++;
                }
            }
            finally{EditorUtility.ClearProgressBar();File.WriteAllText(Path.Combine(folder,"status.txt"),$"{completed}/{cameras.poses.Length} captures. "+(completed==cameras.poses.Length?"Complete":"Incomplete or cancelled")+". Static composition does not measure runtime tunnel adaptation.");}
        }
    }
}
