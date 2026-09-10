using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEditor;
using System.IO;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var cameraObject=new GameObject("Building map verification camera");result.RegisterObjectCreation(cameraObject);
        var lightObject=new GameObject("Building map verification sun");result.RegisterObjectCreation(lightObject);
        var fillObject=new GameObject("Building map verification fill");result.RegisterObjectCreation(fillObject);
        var volumeObject=new GameObject("Building map verification exposure");result.RegisterObjectCreation(volumeObject);
        RenderTexture target=null;Texture2D image=null;VolumeProfile profile=null;
        try {
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.nearClipPlane=1;camera.farClipPlane=15000;
            camera.transform.position=new Vector3(-400,1000,950);camera.transform.LookAt(new Vector3(-1135,120,43));camera.orthographic=true;camera.orthographicSize=450;
            var hd=cameraObject.AddComponent<HDAdditionalCameraData>();hd.clearColorMode=HDAdditionalCameraData.ClearColorMode.Color;hd.backgroundColorHDR=new Color(.055f,.065f,.08f);hd.antialiasing=HDAdditionalCameraData.AntialiasingMode.None;
            var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=100000;light.transform.rotation=Quaternion.Euler(50,-25,0);lightObject.AddComponent<HDAdditionalLightData>();
            var fill=fillObject.AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=45000;fill.transform.rotation=Quaternion.Euler(65,155,0);fillObject.AddComponent<HDAdditionalLightData>();
            var volume=volumeObject.AddComponent<Volume>();volume.isGlobal=true;volume.priority=100000;profile=ScriptableObject.CreateInstance<VolumeProfile>();volume.sharedProfile=profile;
            var exposure=profile.Add<Exposure>(true);exposure.mode.Override(ExposureMode.Fixed);exposure.fixedExposure.Override(12);
            target=new RenderTexture(1400,1000,24,RenderTextureFormat.ARGB32);target.Create();
            var request=new RenderPipeline.StandardRequest();request.destination=target;RenderPipeline.SubmitRenderRequest(camera,request);
            var prior=RenderTexture.active;RenderTexture.active=target;image=new Texture2D(1400,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1400,1000),0,0);image.Apply();RenderTexture.active=prior;
            File.WriteAllBytes("Art/RockportBuildings/unity-downtown.png",image.EncodeToPNG());result.Log("Saved HDRP building map render.");
        } finally {
            result.DestroyObject(cameraObject);result.DestroyObject(lightObject);result.DestroyObject(volumeObject);result.DestroyObject(fillObject);
            if(profile)Object.DestroyImmediate(profile);if(image)Object.DestroyImmediate(image);if(target){target.Release();Object.DestroyImmediate(target);}
        }
    }
}
