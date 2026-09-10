using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace NfsMwRemaster.Driving.Editor.Rendering
{
    public static class HdrpSceneDefaults
    {
        public static void Camera(Camera camera,bool sky=true)
        {
            if(!camera.TryGetComponent<HDAdditionalCameraData>(out var data))data=camera.gameObject.AddComponent<HDAdditionalCameraData>();
            data.antialiasing=camera.orthographic?HDAdditionalCameraData.AntialiasingMode.None:HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            data.TAAQuality=HDAdditionalCameraData.TAAQualityLevel.Medium;data.volumeLayerMask=1;
            data.clearColorMode=sky?HDAdditionalCameraData.ClearColorMode.Sky:HDAdditionalCameraData.ClearColorMode.Color;
        }
        public static void Sun(Light light,float lux=90000)
        {
            light.type=LightType.Directional;
            if(!light.TryGetComponent<HDAdditionalLightData>(out var data))data=light.gameObject.AddComponent<HDAdditionalLightData>();
            light.lightUnit=LightUnit.Lux;light.intensity=lux;light.shadows=LightShadows.Soft;
            data.useContactShadow.useOverride=true;data.useContactShadow.@override=true;data.SetShadowResolution(2048);
        }
        public static void PlayerHeadlights(Transform vehicle)
        {
            if(vehicle.Find("HDRP low beams"))return;
            var root=new GameObject("HDRP low beams");root.transform.SetParent(vehicle,false);
            foreach(float x in new[]{-.65f,.65f})
            {
                var go=new GameObject(x<0?"Left low beam":"Right low beam");go.transform.SetParent(root.transform,false);
                go.transform.localPosition=new Vector3(x,.1f,2.15f);go.transform.localRotation=Quaternion.Euler(3,0,0);
                var light=go.AddComponent<Light>();light.type=LightType.Spot;
                var data=go.AddComponent<HDAdditionalLightData>();light.lightUnit=LightUnit.Lumen;light.intensity=1100;
                light.spotAngle=60;light.innerSpotAngle=32;light.range=65;light.color=new Color(1,.96f,.88f);
                light.shadows=LightShadows.Soft;data.SetShadowResolution(512);data.shadowDimmer=.9f;
                data.fadeDistance=90;data.volumetricDimmer=.25f;
            }
        }
    }
}
