using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using NfsMwRemaster.Driving;

namespace NfsMwRemaster.Lighting
{
    /// <summary>
    /// HDRP translation for the weather cloud presentation. AtmosphereController owns
    /// the transient VolumeProfile, so this adapter never creates a competing Volume.
    /// </summary>
    public static class WeatherVolumetricClouds
    {
        public static void Initialize(VolumeProfile profile)
        {
            if (!profile) return;
            if (!profile.TryGet<VisualEnvironment>(out var environment))
                environment = profile.Add<VisualEnvironment>();
            environment.renderingSpace.Override(RenderingSpace.Camera);

            if (!profile.TryGet<VolumetricClouds>(out var clouds))
                clouds = profile.Add<VolumetricClouds>(true);
            clouds.active = true;
            clouds.enable.Override(false);
            clouds.cloudControl.Override(VolumetricClouds.CloudControl.Simple);
            clouds.cloudPreset = VolumetricClouds.CloudPresets.Custom;
            clouds.fadeInMode.Override(VolumetricClouds.CloudFadeInMode.Automatic);

            // A fixed custom profile lets weather transitions interpolate scalar
            // controls without repeatedly applying HDRP's discrete presets.
            clouds.densityCurve.Override(new AnimationCurve(
                new Keyframe(0, 0),
                new Keyframe(.1f, 1),
                new Keyframe(.8f, .75f),
                new Keyframe(1, 0)));
            clouds.erosionCurve.Override(new AnimationCurve(
                new Keyframe(0, 1),
                new Keyframe(.1f, .9f),
                new Keyframe(1, 1)));
            clouds.ambientOcclusionCurve.Override(new AnimationCurve(
                new Keyframe(0, 0),
                new Keyframe(.25f, .45f),
                new Keyframe(1, 0)));

            clouds.fadeInStart.Override(0);
            clouds.fadeInDistance.Override(0);
            clouds.scatteringTint.Override(Color.black);
            clouds.erosionNoiseType.Override(VolumetricClouds.CloudErosionNoise.Perlin32);
            clouds.erosionOcclusion.Override(.1f);
            clouds.cloudMapSpeedMultiplier.Override(.5f);
            clouds.shapeSpeedMultiplier.Override(1);
            clouds.erosionSpeedMultiplier.Override(.25f);
            clouds.verticalShapeWindSpeed.Override(0);
            clouds.verticalErosionWindSpeed.Override(0);
            clouds.ghostingReduction.Override(true);
            clouds.perceptualBlending.Override(1);
            clouds.shadowOpacityFallback.Override(0);
        }

        public static bool Apply(VolumeProfile profile, WeatherCloudPresentation state)
        {
            if (!profile || !profile.TryGet<VolumetricClouds>(out var clouds)) return false;
            clouds.cloudSimpleMode.Override(state.qualityMode
                ? VolumetricClouds.CloudSimpleMode.Quality
                : VolumetricClouds.CloudSimpleMode.Performance);
            clouds.densityMultiplier.Override(state.densityMultiplier);
            clouds.shapeFactor.Override(state.shapeFactor);
            clouds.shapeScale.Override(state.shapeScale);
            clouds.erosionFactor.Override(state.erosionFactor);
            clouds.erosionScale.Override(state.erosionScale);
            clouds.shapeOffset.Override(state.shapeOffset);
            clouds.bottomAltitude.Override(state.bottomAltitude);
            clouds.altitudeRange.Override(state.altitudeRange);
            clouds.powderEffectIntensity.Override(state.powderEffectIntensity);
            clouds.multiScattering.Override(state.multiScattering);
            clouds.ambientLightProbeDimmer.Override(state.ambientLightProbeDimmer);
            clouds.sunLightDimmer.Override(state.sunLightDimmer);
            clouds.altitudeDistortion.Override(state.altitudeDistortion);
            clouds.temporalAccumulationFactor.Override(state.temporalAccumulation);
            clouds.numPrimarySteps.Override(state.primarySteps);
            clouds.numLightSteps.Override(state.lightSteps);

            clouds.globalWindSpeed.Override(CustomWind(state.windSpeedKph));
            clouds.orientation.Override(CustomWind(state.windOrientationDegrees));
            clouds.cloudMapSpeedMultiplier.Override(state.cloudMapSpeedMultiplier);
            clouds.shapeSpeedMultiplier.Override(state.shapeSpeedMultiplier);
            clouds.erosionSpeedMultiplier.Override(state.erosionSpeedMultiplier);
            clouds.verticalShapeWindSpeed.Override(state.verticalShapeWindSpeed);
            clouds.verticalErosionWindSpeed.Override(state.verticalErosionWindSpeed);

            clouds.shadows.Override(state.shadows);
            clouds.shadowResolution.Override(ShadowResolution(state.shadowResolution));
            clouds.shadowDistance.Override(state.shadowDistance);
            clouds.shadowOpacity.Override(state.shadowOpacity);
            bool enabled = state.enabled && IsSupported();
            clouds.enable.Override(enabled);
            return enabled;
        }

        public static bool IsSupported()
        {
            return GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset asset
                && asset.currentPlatformRenderPipelineSettings.supportVolumetricClouds;
        }

        static WindParameter.WindParamaterValue CustomWind(float value)
        {
            return new WindParameter.WindParamaterValue
            {
                mode = WindParameter.WindOverrideMode.Custom,
                customValue = value,
                additiveValue = 0,
                multiplyValue = 1
            };
        }

        static VolumetricClouds.CloudShadowResolution ShadowResolution(int value)
        {
            if (value >= 1024) return VolumetricClouds.CloudShadowResolution.Ultra1024;
            if (value >= 512) return VolumetricClouds.CloudShadowResolution.High512;
            if (value >= 256) return VolumetricClouds.CloudShadowResolution.Medium256;
            if (value >= 128) return VolumetricClouds.CloudShadowResolution.Low128;
            return VolumetricClouds.CloudShadowResolution.VeryLow64;
        }
    }
}
