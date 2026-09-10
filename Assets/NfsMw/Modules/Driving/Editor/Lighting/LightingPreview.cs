using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace NfsMwRemaster.Lighting.Editor
{
    public sealed class LightingPreview : IDisposable
    {
        readonly Scene scene;readonly Camera camera;readonly Light key;readonly VolumeProfile post;
        readonly List<Object> owned=new List<Object>();readonly List<Light> decorative=new List<Light>();
        bool disposed;public int MeshCount {get;private set;}
        public LightingPreview(AtmosphereController source)
        {
            if(!source)throw new ArgumentNullException(nameof(source));
            scene=EditorSceneManager.NewPreviewScene();
            try
            {
                foreach(var renderer in source.bakeGeometry)
                {
                    if(!renderer||!renderer.enabled||!renderer.gameObject.activeInHierarchy)continue;
                    var filter=renderer.GetComponent<MeshFilter>();if(!filter||!filter.sharedMesh)continue;
                    var go=Create(renderer.name);go.transform.SetPositionAndRotation(renderer.transform.position,renderer.transform.rotation);go.transform.localScale=renderer.transform.lossyScale;
                    go.AddComponent<MeshFilter>().sharedMesh=filter.sharedMesh;var copy=go.AddComponent<MeshRenderer>();copy.sharedMaterials=renderer.sharedMaterials;copy.shadowCastingMode=renderer.shadowCastingMode;copy.receiveShadows=renderer.receiveShadows;MeshCount++;
                }
                var seen=new HashSet<Light>();foreach(var fixture in source.fixtures)
                {
                    if(!fixture||!fixture.source||!fixture.source.enabled||!fixture.source.gameObject.activeInHierarchy||!seen.Add(fixture.source))continue;
                    var light=fixture.source;var go=Create(light.name);go.transform.SetPositionAndRotation(light.transform.position,light.transform.rotation);
                    var copy=go.AddComponent<Light>();copy.type=light.type;copy.color=light.color;copy.intensity=light.intensity;copy.range=light.range;copy.spotAngle=light.spotAngle;copy.innerSpotAngle=light.innerSpotAngle;copy.shadows=light.shadows;copy.cullingMask=~0;
                    go.AddComponent<HDAdditionalLightData>();copy.lightUnit=light.lightUnit;copy.intensity=light.intensity;
                    if(!fixture.Critical)decorative.Add(copy);
                }
                key=Create("Preview key").AddComponent<Light>();key.type=LightType.Directional;key.shadows=LightShadows.Soft;
                key.gameObject.AddComponent<HDAdditionalLightData>();
                camera=Create("Preview camera").AddComponent<Camera>();camera.enabled=false;camera.useOcclusionCulling=false;camera.allowHDR=true;camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.nearClipPlane=.1f;camera.farClipPlane=2000;
                var data=camera.GetLightingCameraData();LightingCamera.SetPostProcessing(data,true);data.volumeLayerMask=1<<27;
                data.clearColorMode=HDAdditionalCameraData.ClearColorMode.Sky;
                var v=Create("Preview grading");v.layer=27;var volume=v.AddComponent<Volume>();volume.isGlobal=true;volume.priority=10001;
                post=ScriptableObject.CreateInstance<VolumeProfile>();post.hideFlags=HideFlags.HideAndDontSave;owned.Add(post);volume.sharedProfile=post;
                // Registering a preview Volume is global in SRP; keep it disabled outside the render scope.
                volume.enabled=false;previewVolume=volume;
            }
            catch{Dispose();throw;}
        }
        readonly Volume previewVolume;
        GameObject Create(string name){var go=new GameObject(name){hideFlags=HideFlags.HideAndDontSave};SceneManager.MoveGameObjectToScene(go,scene);return go;}
        public Texture2D Capture(AtmosphereLook look,Vector3 position,Quaternion rotation,int width=960,int height=540,float fov=60,LightingQuality quality=LightingQuality.High,float lowMultiplier=1,bool lowShadows=false,bool radiance=false)
        {
            if(disposed)throw new ObjectDisposedException(nameof(LightingPreview));if(width<16||height<16||width>4096||height>4096)throw new ArgumentOutOfRangeException(nameof(width));
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)throw new InvalidOperationException("Capture needs a graphics device. Run Unity with Metal, without -nographics.");
            var oldRT=RenderTexture.active;var target=new RenderTexture(width,height,24,radiance?RenderTextureFormat.ARGBHalf:RenderTextureFormat.ARGB32,radiance?RenderTextureReadWrite.Linear:RenderTextureReadWrite.sRGB);Texture2D image=null;
            var fixtureStates=new List<(Light light,float intensity,LightShadows shadows)>();
            bool lightingOverride=false;
            try
            {
                if(!Unsupported.SetOverrideLightingSettings(scene))throw new InvalidOperationException("Cannot isolate preview lighting settings.");lightingOverride=true;look.ApplyEnvironment(key);look.ApplyPost(post,true);previewVolume.enabled=true;
                var data=camera.GetLightingCameraData();LightingCamera.SetPostProcessing(data,!radiance);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.ExposureControl]=true;
                data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.ExposureControl,!radiance);
                foreach(var light in decorative){fixtureStates.Add((light,light.intensity,light.shadows));if(quality==LightingQuality.Low){light.intensity*=lowMultiplier;if(!lowShadows)light.shadows=LightShadows.None;}}
                camera.transform.SetPositionAndRotation(position,rotation);camera.fieldOfView=fov;camera.backgroundColor=look.fogColor;camera.targetTexture=target;target.Create();
                camera.Render();RenderTexture.active=target;image=new Texture2D(width,height,radiance?TextureFormat.RGBAHalf:TextureFormat.RGB24,false,radiance);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();return image;
            }
            catch{if(image)Object.DestroyImmediate(image);throw;}
            finally
            {
                previewVolume.enabled=false;foreach(var state in fixtureStates){state.light.intensity=state.intensity;state.light.shadows=state.shadows;}
                camera.targetTexture=null;RenderTexture.active=oldRT;target.Release();Object.DestroyImmediate(target);if(lightingOverride)Unsupported.RestoreOverrideLightingSettings();
            }
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;if(previewVolume)previewVolume.enabled=false;
            if(post)foreach(var component in post.components)if(component)Object.DestroyImmediate(component);
            foreach(var asset in owned)if(asset)Object.DestroyImmediate(asset);owned.Clear();
            if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
