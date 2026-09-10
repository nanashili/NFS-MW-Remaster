using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace NfsMwRemaster.Driving
{
    /// <summary>HDRP stays in the existing downstream rendering assembly.</summary>
    public static class MostWantedShowroomRendering
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => MostWantedShowroom.ConfigureRendering = Configure;

        public static void ConfigureBackground(Camera camera)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 0;
            camera.orthographic = true;
            var data = camera.GetComponent<HDAdditionalCameraData>() ?? camera.gameObject.AddComponent<HDAdditionalCameraData>();
            data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            data.backgroundColorHDR = Color.black;
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            data.volumeLayerMask = 0;
            data.customRenderingSettings = true;
            // This camera only clears the screen behind the UI. World lighting and
            // post-processing passes otherwise run even with an empty culling mask.
            foreach (var field in new[] { FrameSettingsField.SSGI, FrameSettingsField.SSR,
                FrameSettingsField.SSAO, FrameSettingsField.ShadowMaps, FrameSettingsField.ContactShadows,
                FrameSettingsField.Volumetrics, FrameSettingsField.AtmosphericScattering,
                FrameSettingsField.Postprocess, FrameSettingsField.MotionVectors,
                FrameSettingsField.OpaqueObjects, FrameSettingsField.TransparentObjects })
            {
                data.renderingPathCustomFrameSettings.SetEnabled(field, false);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
            }
        }

        public static void Configure(Camera camera, Transform stage)
        {
            var data = camera.GetComponent<HDAdditionalCameraData>() ?? camera.gameObject.AddComponent<HDAdditionalCameraData>();
            data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            data.backgroundColorHDR = new Color(.06f, .075f, .055f);
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            data.volumeLayerMask = 1 << MostWantedShowroom.PreviewLayer;
            data.volumeAnchorOverride = camera.transform;
            // The isolated, directly lit warehouse has no world GI dependency.
            // Inheriting Ultra SSGI here more than doubled the measured preview cost.
            data.customRenderingSettings = true;
            data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.SSGI, false);
            data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.SSGI] = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            // The showroom must not inherit world/weather fog from HDRP's default profile.
            var fog = profile.Add<Fog>();
            fog.enabled.Override(false);
            fog.enableVolumetricFog.Override(false);
            var exposure = profile.Add<Exposure>();
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(9.1f);
            var tone = profile.Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>();
            color.saturation.Override(-16);
            color.contrast.Override(17);
            color.colorFilter.Override(new Color(1f, .97f, .78f));
            var bloom = profile.Add<Bloom>();
            bloom.intensity.Override(.18f);
            bloom.threshold.Override(1.1f);
            var node = new GameObject("Showroom-only HDRP volume") { layer = MostWantedShowroom.PreviewLayer };
            node.transform.SetParent(stage, false);
            var volume = node.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 500;
            volume.sharedProfile = profile;
            var cleanup = node.AddComponent<MostWantedShowroomVolumeOwner>();
            cleanup.Profile = profile;
            foreach (var light in stage.GetComponentsInChildren<Light>())
            {
                var hd = light.GetComponent<HDAdditionalLightData>() ?? light.gameObject.AddComponent<HDAdditionalLightData>();
                if (light.type == LightType.Directional) { light.lightUnit = LightUnit.Lux; light.intensity = 19000; }
                else { light.lightUnit = LightUnit.Lumen; light.intensity = 16000; }
                hd.SetShadowResolution(1024);
            }
        }
    }

    public sealed class MostWantedShowroomVolumeOwner : MonoBehaviour
    {
        public VolumeProfile Profile;
        private void OnDestroy()
        {
            if (Profile == null) return;
            foreach (var component in Profile.components) if (component != null) Destroy(component);
            Destroy(Profile);
        }
    }
}
