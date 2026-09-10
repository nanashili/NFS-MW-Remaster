using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.HighDefinition;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Applies the project quality contract at the same boundary as Unity quality changes.
    /// Rendering quality is presentation-only: physics, input, audio and gameplay visibility
    /// remain owned by their existing systems.
    /// </summary>
    public static class HdrpQualityRuntime
    {
        const string CatalogResource = "NFS HDRP Quality Profiles";
        static HdrpQualityCatalog catalog;
        static bool installed;
        static int appliedLevel = -1;

        public static HdrpQualityPreset CurrentPreset => Preset(QualitySettings.GetQualityLevel());
        public static int CurrentLevel => QualitySettings.GetQualityLevel();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (installed) return;
            installed = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplyCurrent(true);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyCurrent(true);

        public static HdrpQualityPreset Preset(int level)
        {
            if (!catalog) catalog = Resources.Load<HdrpQualityCatalog>(CatalogResource);
            return catalog ? catalog.Get(level) : HdrpQualityPreset.Defaults((HdrpQualityTier)Mathf.Clamp(level, 0, 4));
        }

        public static void SetQuality(int level, bool applyImmediately = true)
        {
            int safe = Mathf.Clamp(level, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
            QualitySettings.SetQualityLevel(safe, true);
            if (applyImmediately) ApplyCurrent(true);
        }

        public static void ApplyCurrent(bool force = false)
        {
            int level = QualitySettings.GetQualityLevel();
            if (!force && appliedLevel == level) return;
            HdrpQualityPreset preset = Preset(level);

            QualitySettings.globalTextureMipmapLimit = Mathf.Clamp(preset.textureMipmapLimit, 0, 3);
            QualitySettings.anisotropicFiltering = preset.anisotropicSamples <= 0
                ? AnisotropicFiltering.Disable : AnisotropicFiltering.ForceEnable;
            QualitySettings.shadowDistance = preset.shadowDistance;
            QualitySettings.lodBias = Mathf.Max(.1f, preset.environmentLodBias);
            QualitySettings.maximumLODLevel = preset.environmentLodBias < .7f ? 1 : 0;
            QualitySettings.realtimeReflectionProbes = preset.reflectionProbeUpdates > 0;
            QualitySettings.particleRaycastBudget = Mathf.Clamp(preset.particleBudget / 4, 64, 4096);
            QualitySettings.streamingMipmapsActive = true;
            QualitySettings.streamingMipmapsMemoryBudget = Mathf.Max(128, preset.particleBudget * 2);

            // HDRP's dynamic-resolution handler consumes this scale when the selected pipeline
            // asset enables it. Native/optional tiers stay at full resolution by default.
            float scale = preset.dynamicResolution ? Mathf.Clamp01(preset.resolutionScale) : 1f;
            ScalableBufferManager.ResizeBuffers(scale, scale);

            foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                if (!camera.TryGetComponent<HDAdditionalCameraData>(out var data)) continue;
                if (camera.orthographic)
                {
                    data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                }
                else
                {
                    data.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
                    data.TAAQuality = (HDAdditionalCameraData.TAAQualityLevel)Mathf.Clamp(preset.taaQuality, 0, 2);
                }
            }

            foreach (var rain in UnityEngine.Object.FindObjectsByType<LocalRain>(FindObjectsInactive.Include))
                rain.ApplyQuality(preset.rainDensity, preset.particleDistance);
            WeatherPresentationQuality weatherQuality = level <= (int)HdrpQualityTier.VeryLow
                ? WeatherPresentationQuality.VeryLow
                : level == (int)HdrpQualityTier.Low ? WeatherPresentationQuality.Low
                : level == (int)HdrpQualityTier.Medium ? WeatherPresentationQuality.Medium
                : level == (int)HdrpQualityTier.High ? WeatherPresentationQuality.High
                : WeatherPresentationQuality.Ultra;
            foreach (var world in UnityEngine.Object.FindObjectsByType<DynamicWeatherWorld>(FindObjectsInactive.Include))
                world.SetPresentationQuality(weatherQuality);
            foreach (var particles in UnityEngine.Object.FindObjectsByType<SensoryEffectsWorld>(FindObjectsInactive.Include))
                particles.ApplyQuality(preset.particleBudget, preset.particleDistance);

            ApplyTerrainQuality(preset, level);

            int shadowedHeadlights = 0;
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (light.name.IndexOf("low beam", StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool enabled = shadowedHeadlights++ < preset.headlightShadowCount;
                light.shadows = enabled ? LightShadows.Soft : LightShadows.None;
            }
            appliedLevel = level;
        }

        static void ApplyTerrainQuality(HdrpQualityPreset preset, int level)
        {
            float vegetationDistance = Mathf.Max(0f, preset.vegetationDistance);
            float billboardDistance = Mathf.Min(vegetationDistance,
                Mathf.Clamp(vegetationDistance * .25f, 40f, 160f));
            float basemapDistance = Mathf.Clamp(preset.drawDistance * .55f, 200f, 1200f);
            int fullLodTrees = Mathf.Clamp(32 << Mathf.Clamp(level, 0, 4), 32, 512);
            float heightmapPixelError = level <= (int)HdrpQualityTier.VeryLow ? 48f
                : level == (int)HdrpQualityTier.Low ? 32f
                : level == (int)HdrpQualityTier.Medium ? 20f
                : level == (int)HdrpQualityTier.High ? 10f
                : 5f;

            foreach (var terrain in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
            {
                terrain.drawInstanced = true;
                terrain.basemapDistance = basemapDistance;
                if (terrain.drawHeightmap) terrain.heightmapPixelError = heightmapPixelError;
                if (!terrain.drawTreesAndFoliage) continue;
                terrain.treeDistance = vegetationDistance;
                terrain.treeBillboardDistance = billboardDistance;
                terrain.treeMaximumFullLODCount = fullLodTrees;
            }
        }
    }
}
