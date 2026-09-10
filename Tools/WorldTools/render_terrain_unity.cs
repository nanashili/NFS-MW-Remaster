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
        var cameraObject=new GameObject("Rockport map verification camera");result.RegisterObjectCreation(cameraObject);
        var lightObject=new GameObject("Rockport map verification sun");result.RegisterObjectCreation(lightObject);
        var fillObject=new GameObject("Rockport map verification fill");result.RegisterObjectCreation(fillObject);
        var volumeObject=new GameObject("Rockport map verification exposure");result.RegisterObjectCreation(volumeObject);
        var roots=SceneManager.GetActiveScene().GetRootGameObjects();GameObject source=null,terrain=null;
        foreach(var root in roots){if(root.name=="Rockport Original Ground - Exact Source Meshes")source=root;if(root.name=="Rockport Generated Terrain - 1m Heightmap")terrain=root;}
        if(!source||!terrain)throw new System.InvalidOperationException("Missing terrain roots.");
        bool sourceActive=source.activeSelf,terrainActive=terrain.activeSelf;
        RenderTexture target=null;Texture2D image=null;VolumeProfile profile=null;
        try {
            source.SetActive(false);terrain.SetActive(true);
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
            File.WriteAllBytes("Art/RockportGround/unity-heightmap-downtown.png",image.EncodeToPNG());
            camera.transform.position=new Vector3(-1700,8000,-2150);camera.transform.rotation=Quaternion.Euler(90,0,0);camera.orthographicSize=4000;
            RenderPipeline.SubmitRenderRequest(camera,request);RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1400,1000),0,0);image.Apply();RenderTexture.active=prior;
            File.WriteAllBytes("Art/RockportGround/unity-heightmap-overview.png",image.EncodeToPNG());result.Log("Saved HDRP combined map renders.");
        } finally {
            source.SetActive(sourceActive);terrain.SetActive(terrainActive);
            result.DestroyObject(cameraObject);result.DestroyObject(lightObject);result.DestroyObject(volumeObject);result.DestroyObject(fillObject);
            if(profile)Object.DestroyImmediate(profile);if(image)Object.DestroyImmediate(image);if(target){target.Release();Object.DestroyImmediate(target);}
        }
    }
}
