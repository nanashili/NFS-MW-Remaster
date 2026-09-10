using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Player-facing HDRP tiers. The order is also the QualitySettings order.</summary>
    public enum HdrpQualityTier
    {
        VeryLow = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Ultra = 4
    }

    public enum HdrpCloudQuality { Simple2D, Enhanced, Volumetric, UltraVolumetric }
    public enum HdrpUpscalingMode { Performance, Balanced, Quality, Native }

    /// <summary>
    /// Serializable rendering contract for one quality tier. Values describe the intended
    /// player setting and are consumed by HdrpQualityRuntime for the settings Unity exposes
    /// at runtime. Some rows (planar reflections, mirrors and vegetation) remain explicit
    /// budgets until the corresponding authored systems exist in a scene.
    /// </summary>
    [Serializable]
    public sealed class HdrpQualityPreset
    {
        public string displayName;

        [Header("Textures")]
        public string textureQuality;
        public string textureResolution;
        [Range(0, 3)] public int textureMipmapLimit;
        [Min(0)] public int anisotropicSamples;

        [Header("Shadows and lighting")]
        public string shadowQuality;
        [Min(256)] public int shadowResolution;
        [Min(0)] public float shadowDistance;
        public bool contactShadows;
        [Range(0, 3)] public int contactShadowQuality;
        public bool ambientOcclusion;
        [Range(0, 3)] public int ambientOcclusionQuality;

        [Header("Reflections and atmosphere")]
        public string reflectionQuality;
        public bool screenSpaceReflections;
        [Range(0, 3)] public int screenSpaceReflectionQuality;
        [Range(0, 3)] public int reflectionProbeUpdates;
        public bool planarReflections;
        public bool volumetricFog;
        [Range(0, 3)] public int volumetricQuality;
        public HdrpCloudQuality cloudQuality;

        [Header("Weather")]
        [Range(0, 3)] public int weatherQuality;
        [Range(0, 1)] public float rainDensity;
        [Range(0, 1)] public float rainSplashScale;
        [Range(0, 1)] public float tyreSprayScale;
        [Range(0, 3)] public int puddleQuality;
        public bool roadWetness;
        public bool preserveGameplayFogVisibility;

        [Header("World detail")]
        [Range(0, 3)] public int particleQuality;
        [Min(0)] public float particleDistance;
        [Min(128)] public int particleBudget;
        [Range(0, 3)] public int decalQuality;
        [Min(0)] public float decalDistance;
        [Min(0.1f)] public float vehicleLodBias;
        [Min(0.1f)] public float environmentLodBias;
        [Min(0)] public float drawDistance;
        [Min(0)] public float vegetationDistance;

        [Header("Vehicles and traffic")]
        [Range(0, 3)] public int vehicleReflectionDetail;
        [Min(64)] public int mirrorResolution;
        [Min(0)] public float mirrorDistance;
        [Range(0, 8)] public int headlightShadowCount;
        [Range(0, 3)] public int trafficDetail;

        [Header("Temporal and resolution")]
        public bool motionBlurUserControlled = true;
        public bool bloomUserControlled = true;
        public bool depthOfFieldUserControlled = true;
        [Range(0, 3)] public int taaQuality;
        public bool dynamicResolution;
        [Range(0.5f, 1)] public float resolutionScale;
        public HdrpUpscalingMode upscaling;

        public static HdrpQualityPreset Defaults(HdrpQualityTier tier)
        {
            switch (tier)
            {
                case HdrpQualityTier.VeryLow:
                    return Make("Very Low", "Low", "0.5–1K", 2, 2, "Very Low", 512, 40, false, 0, true, 0, "Very Low", false, 0, 0, false, false, 0, HdrpCloudQuality.Simple2D, 0, .25f, .2f, .25f, 0, true, true, 0, 45, 256, 0, 35, .6f, .5f, 350, 150, 0, 256, 40, 1, 0, 0, true, true, true, 0, true, .65f, HdrpUpscalingMode.Performance);
                case HdrpQualityTier.Low:
                    return Make("Low", "Medium", "1K", 1, 4, "Low", 1024, 80, false, 0, true, 1, "Low", false, 1, 1, false, false, 0, HdrpCloudQuality.Simple2D, 1, .4f, .35f, .4f, 1, true, true, 1, 70, 512, 1, 55, .8f, .75f, 600, 250, 1, 512, 60, 1, 1, 0, true, true, true, 0, true, .75f, HdrpUpscalingMode.Balanced);
                case HdrpQualityTier.Medium:
                    return Make("Medium", "High", "1–2K", 1, 8, "Medium", 2048, 160, true, 1, true, 2, "Medium", true, 2, 2, true, true, 1, HdrpCloudQuality.Enhanced, 2, .6f, .55f, .6f, 2, true, true, 2, 100, 1024, 2, 100, 1f, 1f, 900, 450, 2, 1024, 90, 2, 2, 1, true, true, true, 1, true, .85f, HdrpUpscalingMode.Balanced);
                case HdrpQualityTier.High:
                    return Make("High", "High", "2–4K", 0, 16, "High", 3072, 250, true, 2, true, 3, "High", true, 3, 3, true, true, 2, HdrpCloudQuality.Volumetric, 3, .8f, .75f, .8f, 3, true, true, 3, 150, 1536, 3, 180, 1.35f, 1.25f, 1400, 700, 3, 1536, 120, 4, 3, 2, true, true, true, 2, false, .95f, HdrpUpscalingMode.Quality);
                default:
                    return Make("Ultra", "Ultra", "4K+", 0, 16, "Ultra", 4096, 400, true, 3, true, 3, "Ultra", true, 3, 3, true, true, 3, HdrpCloudQuality.UltraVolumetric, 3, 1, 1, 1, 3, true, true, 3, 220, 2048, 3, 260, 1.7f, 1.5f, 2200, 1100, 3, 2048, 180, 8, 3, 3, true, true, true, 3, false, 1f, HdrpUpscalingMode.Native);
            }
        }

        static HdrpQualityPreset Make(string name, string texture, string resolution, int mip, int aniso,
            string shadows, int shadowRes, float shadowDistance, bool contact, int contactQuality,
            bool ao, int aoQuality, string reflections, bool ssr, int ssrQuality, int probeUpdates,
            bool planar, bool volumetric, int volumetricQuality, HdrpCloudQuality clouds,
            int weather, float rain, float splash, float spray, int puddles, bool wetness, bool fogVisibility,
            int particles, float particleDistance, int particleBudget, int decals, float decalDistance,
            float vehicleLod, float environmentLod, float drawDistance, float vegetationDistance,
            int vehicleReflection, int mirrorResolution, float mirrorDistance, int headlightShadows, int traffic,
            int motionBlurQuality, bool motionBlur, bool bloom, bool dof, int taa, bool dynamic, float scale, HdrpUpscalingMode upscaling)
        {
            return new HdrpQualityPreset
            {
                displayName = name, textureQuality = texture, textureResolution = resolution,
                textureMipmapLimit = mip, anisotropicSamples = aniso, shadowQuality = shadows,
                shadowResolution = shadowRes, shadowDistance = shadowDistance, contactShadows = contact,
                contactShadowQuality = contactQuality, ambientOcclusion = ao, ambientOcclusionQuality = aoQuality,
                reflectionQuality = reflections, screenSpaceReflections = ssr,
                screenSpaceReflectionQuality = ssrQuality, reflectionProbeUpdates = probeUpdates,
                planarReflections = planar, volumetricFog = volumetric, volumetricQuality = volumetricQuality,
                cloudQuality = clouds, weatherQuality = weather, rainDensity = rain,
                rainSplashScale = splash, tyreSprayScale = spray, puddleQuality = puddles,
                roadWetness = wetness, preserveGameplayFogVisibility = fogVisibility,
                particleQuality = particles, particleDistance = particleDistance, particleBudget = particleBudget,
                decalQuality = decals, decalDistance = decalDistance, vehicleLodBias = vehicleLod,
                environmentLodBias = environmentLod, drawDistance = drawDistance, vegetationDistance = vegetationDistance,
                vehicleReflectionDetail = vehicleReflection, mirrorResolution = mirrorResolution,
                mirrorDistance = mirrorDistance, headlightShadowCount = headlightShadows,
                trafficDetail = traffic, motionBlurUserControlled = motionBlur, bloomUserControlled = bloom,
                depthOfFieldUserControlled = dof, taaQuality = taa, dynamicResolution = dynamic,
                resolutionScale = scale, upscaling = upscaling
            };
        }
    }

}
