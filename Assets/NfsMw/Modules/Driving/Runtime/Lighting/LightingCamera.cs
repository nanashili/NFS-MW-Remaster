using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace NfsMwRemaster.Lighting
{
    public static class LightingCamera
    {
        public static HDAdditionalCameraData GetLightingCameraData(this Camera camera)
        {
            if(!camera.TryGetComponent<HDAdditionalCameraData>(out var data))data=camera.gameObject.AddComponent<HDAdditionalCameraData>();
            return data;
        }
        public static void SetPostProcessing(HDAdditionalCameraData data,bool enabled)
        {
            data.customRenderingSettings=true;
            data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.Postprocess]=true;
            data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Postprocess,enabled);
        }

        public static void SetVolumetricClouds(HDAdditionalCameraData data,bool enabled)
        {
            data.customRenderingSettings=true;
            data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.VolumetricClouds]=true;
            data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.VolumetricClouds,enabled);
            data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.FullResolutionCloudsForSky]=true;
            data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.FullResolutionCloudsForSky,false);
        }
    }
}
