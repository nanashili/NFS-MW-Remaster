using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using NfsMwRemaster.Lighting;
using NfsMwRemaster.Driving;

namespace NfsMwRemaster.Driving.Editor.Rendering
{
    /// <summary>Authoritative editor builder for the five player-facing HDRP quality tiers.</summary>
    public static class HdrpQualityPresetBuilder
    {
        const string Folder = "Assets/NfsMw/Settings/Rendering/HDRP";
        const string Resources = Folder + "/Resources";
        const string CatalogPath = Resources + "/NFS HDRP Quality Profiles.asset";
        static readonly string[] Names = { "Very Low", "Low", "Medium", "High", "Ultra" };

        [MenuItem("NFS MW Remaster/HDRP/Build five quality presets")]
        public static void BuildFivePresets()
        {
            Directory.CreateDirectory(Resources);
            AssetDatabase.Refresh();
            HdrpQualityPreset[] presets = new HdrpQualityPreset[5];
            for (int i = 0; i < presets.Length; i++) presets[i] = HdrpQualityPreset.Defaults((HdrpQualityTier)i);
            HdrpQualityCatalog catalog = AssetDatabase.LoadAssetAtPath<HdrpQualityCatalog>(CatalogPath);
            if (!catalog)
            {
                catalog = ScriptableObject.CreateInstance<HdrpQualityCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.schemaVersion = 1;
            catalog.tiers = presets;
            EditorUtility.SetDirty(catalog);

            HDRenderPipelineAsset[] pipelines = new HDRenderPipelineAsset[5];
            for (int i = 0; i < pipelines.Length; i++)
            {
                string path = Folder + "/HDRP " + Names[i] + ".asset";
                EnsurePipelineCopy(i, path);
                HDRenderPipelineAsset asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(path);
                if (!asset) throw new InvalidOperationException("Could not create " + path);
                asset.name = "HDRP " + Names[i];
                asset.volumeProfile = BuildVolumeProfile(i, presets[i]);
                var settings = asset.currentPlatformRenderPipelineSettings;
                ConfigurePipeline(ref settings, presets[i]);
                asset.currentPlatformRenderPipelineSettings = settings;
                EditorUtility.SetDirty(asset);
                pipelines[i] = asset;
            }

            ApplyQualitySettings(pipelines, presets);
            GraphicsSettings.defaultRenderPipeline = pipelines[(int)HdrpQualityTier.Medium];
            QualitySettings.SetQualityLevel((int)HdrpQualityTier.Medium, true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void EnsurePipelineCopy(int index, string path)
        {
            if (AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(path)) return;
            string source = index <= 1 ? Folder + "/HDRP Medium.asset" : index == 2 ? Folder + "/HDRP Medium.asset" : Folder + "/HDRP " + Names[index - 1] + ".asset";
            if (!File.Exists(source)) source = Folder + "/HDRP High.asset";
            if (!AssetDatabase.CopyAsset(source, path)) throw new InvalidOperationException("Could not copy HDRP asset to " + path);
        }

        static VolumeProfile BuildVolumeProfile(int index, HdrpQualityPreset preset)
        {
            string path = Folder + "/" + Names[index] + " Volume.asset";
            if (!AssetDatabase.LoadAssetAtPath<VolumeProfile>(path))
            {
                string source = index <= 1 ? Folder + "/Medium Volume.asset" : Folder + "/" + Names[index] + " Volume.asset";
                if (File.Exists(source) && source != path) AssetDatabase.CopyAsset(source, path);
            }
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            profile.name = Names[index] + " Volume";
            AtmosphereLook look = AtmosphereLook.Neutral;
            look.fog = true;
            look.fogDensity = .0005f;
            look.fogEnd = Mathf.Max(1000, preset.shadowDistance * 8);
            look.ApplyPost(profile);
            Fog fog = Get<Fog>(profile);
            fog.enableVolumetricFog.Override(preset.volumetricFog);
            fog.quality.Override(preset.volumetricQuality);
            fog.maxFogDistance.Override(Mathf.Max(1000, preset.shadowDistance * 8));
            ScreenSpaceReflection ssr = Get<ScreenSpaceReflection>(profile);
            ssr.enabled.Override(preset.screenSpaceReflections);
            ssr.enabledTransparent.Override(preset.screenSpaceReflectionQuality >= 3);
            ssr.quality.Override(preset.screenSpaceReflectionQuality);
            ssr.tracing.Override(RayCastingMode.RayMarching);
            ssr.usedAlgorithm.Override(ScreenSpaceReflectionAlgorithm.PBRAccumulation);
            ssr.accumulationFactor.Override(preset.screenSpaceReflectionQuality >= 2 ? .7f : .5f);
            ssr.enableWorldSpeedRejection.Override(true);
            ssr.minSmoothness = .55f;
            ssr.smoothnessFadeStart = .8f;
            ssr.depthBufferThickness.Override(.15f);
            ssr.screenFadeDistance.Override(.12f);
            ScreenSpaceAmbientOcclusion ao = Get<ScreenSpaceAmbientOcclusion>(profile);
            ao.intensity.Override(preset.ambientOcclusion ? .7f : 0);
            ao.radius.Override(.65f);
            ao.directLightingStrength.Override(0);
            ao.quality.Override(preset.ambientOcclusionQuality);
            ao.temporalAccumulation.Override(preset.ambientOcclusionQuality > 0);
            ao.ghostingReduction.Override(.65f);
            ContactShadows contact = Get<ContactShadows>(profile);
            contact.enable.Override(preset.contactShadows);
            contact.length.Override(.18f);
            contact.maxDistance.Override(Mathf.Min(40, preset.shadowDistance * .25f));
            contact.opacity.Override(preset.contactShadows ? .75f : 0);
            contact.halfResolution.Override(preset.contactShadowQuality < 2);
            contact.quality.Override(preset.contactShadowQuality);
            MicroShadowing micro = Get<MicroShadowing>(profile);
            micro.enable.Override(preset.ambientOcclusion);
            micro.opacity.Override(.65f);
            HDShadowSettings shadows = Get<HDShadowSettings>(profile);
            shadows.maxShadowDistance.Override(preset.shadowDistance);
            shadows.cascadeShadowSplitCount.Override(preset.shadowQuality == "Very Low" ? 2 : 4);
            shadows.cascadeShadowSplit0.Override(.08f);
            shadows.cascadeShadowSplit1.Override(.22f);
            shadows.cascadeShadowSplit2.Override(.5f);
            shadows.cascadeShadowBorder3.Override(.15f);
            GlobalIllumination gi = Get<GlobalIllumination>(profile);
            gi.enable.Override(index >= (int)HdrpQualityTier.Ultra);
            gi.tracing.Override(RayCastingMode.RayMarching);
            gi.fullResolutionSS.Override(index >= (int)HdrpQualityTier.Ultra);
            gi.quality.Override(Mathf.Clamp(preset.ambientOcclusionQuality, 0, 3));
            Get<MotionBlur>(profile).intensity.Override(0);
            Get<DepthOfField>(profile).focusMode.Override(DepthOfFieldMode.Off);
            Get<ChromaticAberration>(profile).intensity.Override(0);
            Get<Vignette>(profile).intensity.Override(0);
            SaveProfile(profile);
            return profile;
        }

        static T Get<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet<T>(out var component)) component = profile.Add<T>();
            return component;
        }

        static void SaveProfile(VolumeProfile profile)
        {
            foreach (var component in profile.components)
            {
                if (!AssetDatabase.Contains(component)) AssetDatabase.AddObjectToAsset(component, profile);
                EditorUtility.SetDirty(component);
            }
            EditorUtility.SetDirty(profile);
        }

        static void ConfigurePipeline(ref RenderPipelineSettings settings, HdrpQualityPreset p)
        {
            settings.supportedLitShaderMode = RenderPipelineSettings.SupportedLitShaderMode.DeferredOnly;
            settings.msaaSampleCount = MSAASamples.None;
            settings.supportRayTracing = false;
            settings.supportVFXRayTracing = false;
            settings.supportSSR = p.screenSpaceReflections;
            settings.supportSSRTransparent = p.screenSpaceReflectionQuality >= 3;
            settings.supportSSAO = p.ambientOcclusion;
            settings.supportSSGI = p.ambientOcclusionQuality >= 3;
            settings.supportVolumetrics = p.volumetricFog;
            settings.supportVolumetricClouds = p.cloudQuality >= HdrpCloudQuality.Volumetric;
            settings.supportWater = false;
            settings.supportSubsurfaceScattering = p.vehicleReflectionDetail >= 2;
            settings.supportDecals = p.decalQuality > 0;
            settings.supportDecalLayers = p.decalQuality >= 2;
            settings.supportLightLayers = p.reflectionQuality != "Very Low";
            settings.supportMotionVectors = true;
            settings.supportDistortion = p.volumetricQuality >= 2;
            settings.supportTransparentBackface = p.reflectionQuality != "Very Low";
            settings.supportTransparentDepthPrepass = true;
            settings.supportTransparentDepthPostpass = p.reflectionQuality == "High" || p.reflectionQuality == "Ultra";
            settings.supportCustomPass = false;
            settings.supportScreenSpaceLensFlare = false;
            settings.supportHighQualityLineRendering = p.particleQuality >= 2;
            settings.lightProbeSystem = RenderPipelineSettings.LightProbeSystem.LegacyLightProbes;
            settings.colorBufferFormat = RenderPipelineSettings.ColorBufferFormat.R11G11B10;
            settings.hdShadowInitParams.maxShadowRequests = 32;
            settings.hdShadowInitParams.supportContactShadows = p.contactShadows;
            settings.hdShadowInitParams.maxDirectionalShadowMapResolution = p.shadowResolution;
            int shadowLow = Mathf.Max(256, p.shadowResolution / 4);
            int shadowMedium = Mathf.Max(512, p.shadowResolution / 2);
            settings.hdShadowInitParams.shadowResolutionDirectional = new IntScalableSetting(new[] { shadowLow, shadowMedium, p.shadowResolution, Mathf.Min(4096, p.shadowResolution * 2) }, ScalableSettingSchemaId.With4Levels);
            settings.hdShadowInitParams.punctualLightShadowAtlas.shadowAtlasResolution = p.shadowResolution <= 1024 ? 1024 : p.shadowResolution >= 4096 ? 4096 : 2048;
            settings.hdShadowInitParams.areaLightShadowAtlas.shadowAtlasResolution = Mathf.Max(512, p.shadowResolution / 2);
            settings.hdShadowInitParams.punctualShadowFilteringQuality = p.contactShadowQuality >= 2 ? HDShadowFilteringQuality.High : HDShadowFilteringQuality.Medium;
            settings.hdShadowInitParams.directionalShadowFilteringQuality = p.shadowQuality == "Ultra" ? HDShadowFilteringQuality.High : HDShadowFilteringQuality.Medium;
            settings.cubeReflectionResolution = new RenderPipelineSettings.ReflectionProbeResolutionScalableSetting(new[]
            {
                CubeReflectionResolution.CubeReflectionResolution128,
                p.reflectionProbeUpdates >= 2 ? CubeReflectionResolution.CubeReflectionResolution256 : CubeReflectionResolution.CubeReflectionResolution128,
                p.reflectionQuality == "Ultra" ? CubeReflectionResolution.CubeReflectionResolution1024 : p.reflectionQuality == "High" ? CubeReflectionResolution.CubeReflectionResolution512 : CubeReflectionResolution.CubeReflectionResolution256
            }, ScalableSettingSchemaId.With3Levels);
            settings.dynamicResolutionSettings.enabled = p.dynamicResolution;
            settings.dynamicResolutionSettings.minPercentage = p.dynamicResolution ? p.resolutionScale * 100 : 100;
            settings.dynamicResolutionSettings.maxPercentage = 100;
            settings.dynamicResolutionSettings.dynResType = DynamicResolutionType.Hardware;
            settings.dynamicResolutionSettings.upsampleFilter = DynamicResUpscaleFilter.CatmullRom;
            settings.lightingQualitySettings.SSRMaxRaySteps = new[] { 24, 48, 96 };
            settings.lightingQualitySettings.AOFullRes = new[] { false, p.ambientOcclusionQuality >= 2, p.ambientOcclusionQuality >= 3 };
            settings.lightingQualitySettings.ContactShadowSampleCount = new[] { 4, 8, 16 };
            settings.lodBias = new FloatScalableSetting(new[] { p.environmentLodBias, p.environmentLodBias, p.environmentLodBias }, ScalableSettingSchemaId.With3Levels);
            settings.maximumLODLevel = new IntScalableSetting(new[] { p.environmentLodBias < .7f ? 1 : 0, 0, 0 }, ScalableSettingSchemaId.With3Levels);
        }

        static void ApplyQualitySettings(HDRenderPipelineAsset[] assets, HdrpQualityPreset[] presets)
        {
            SerializedObject quality = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            SerializedProperty list = quality.FindProperty("m_QualitySettings");
            list.arraySize = assets.Length;
            for (int i = 0; i < assets.Length; i++)
            {
                SerializedProperty q = list.GetArrayElementAtIndex(i);
                HdrpQualityPreset p = presets[i];
                q.FindPropertyRelative("name").stringValue = p.displayName;
                q.FindPropertyRelative("customRenderPipeline").objectReferenceValue = assets[i];
                q.FindPropertyRelative("antiAliasing").intValue = 0;
                q.FindPropertyRelative("shadows").intValue = i == 0 ? 1 : 2;
                q.FindPropertyRelative("shadowDistance").floatValue = p.shadowDistance;
                q.FindPropertyRelative("shadowmaskMode").intValue = 1;
                q.FindPropertyRelative("globalTextureMipmapLimit").intValue = p.textureMipmapLimit;
                q.FindPropertyRelative("anisotropicTextures").intValue = p.anisotropicSamples > 0 ? 2 : 0;
                q.FindPropertyRelative("realtimeReflectionProbes").boolValue = p.reflectionProbeUpdates > 0;
                q.FindPropertyRelative("streamingMipmapsActive").boolValue = true;
                q.FindPropertyRelative("streamingMipmapsMemoryBudget").floatValue = new[] { 256f, 384f, 512f, 768f, 1024f }[i];
                q.FindPropertyRelative("streamingMipmapsMaxLevelReduction").intValue = p.textureMipmapLimit;
                q.FindPropertyRelative("particleRaycastBudget").intValue = Mathf.Clamp(p.particleBudget / 4, 64, 4096);
                q.FindPropertyRelative("vSyncCount").intValue = 1;
                q.FindPropertyRelative("lodBias").floatValue = p.environmentLodBias;
                q.FindPropertyRelative("maximumLODLevel").intValue = p.environmentLodBias < .7f ? 1 : 0;
            }
            quality.FindProperty("m_CurrentQuality").intValue = (int)HdrpQualityTier.Medium;
            quality.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
