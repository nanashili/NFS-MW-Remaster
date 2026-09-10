#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.Weather
{
    /// <summary>
    /// Asset manifest for the weather presentation. Runtime behaviour remains in
    /// LocalRain, WeatherSurfaceCoverage, WeatherSurfaceRegion, and
    /// RacingSurfaces.hlsl; this editor-only manifest prevents builders and
    /// studio tooling from silently drifting to duplicate assets.
    /// </summary>
    public static class WeatherPresentationSourceRegistry
    {
        public const string RainPrefabPath = "Assets/NfsMw/Modules/Driving/Data/Rendering/Local Rain.prefab";
        public const string RainDropTexturePath = "Assets/NfsMw/Modules/Driving/ThirdParty/Weatherade/Particles/RainDrop.tif";
        public const string RainDropNormalPath = "Assets/NfsMw/Modules/Driving/ThirdParty/Weatherade/Particles/RainDrop_n.tif";
        public const string RainShaderPath = "Assets/NfsMw/Modules/Driving/Runtime/Rendering/Shaders/WeatheradeRainHDRP.shader";
        public const string RainMaterialPath = "Assets/NfsMw/Modules/Driving/Data/Weather/WeatheradeRainHDRP.mat";
        public const string SurfaceShaderIncludePath = "Assets/NfsMw/Modules/Driving/Runtime/Rendering/RacingSurfaces.hlsl";
        public const string PuddleBrushPath = "Assets/NfsMw/Modules/Driving/Examples/GrimePainter/Puddle.asset";
        public const string WetRoadMaterialPath = "Assets/NfsMw/Modules/Driving/Data/Rendering/Materials/Asphalt dry to wet.mat";
        public const string RainSurfaceMaterialPath = "Assets/NfsMw/Modules/Driving/Data/Rendering/Materials/Rain.mat";
        public const string WindAmbienceClipPath = "Assets/NfsMw/Modules/Driving/Audio/Diagnostic/wind.wav";

        public static bool Validate(out string failure)
        {
            if (!AssetExists<GameObject>(RainPrefabPath)) return Fail("Rain prefab is missing: " + RainPrefabPath, out failure);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RainPrefabPath);
            LocalRain rain = prefab.GetComponent<LocalRain>();
            ParticleSystem source = prefab.GetComponentInChildren<ParticleSystem>(true);
            if (!rain || !source) return Fail("Rain prefab must contain LocalRain and a particle source: " + RainPrefabPath, out failure);

            Texture2D drop = AssetDatabase.LoadAssetAtPath<Texture2D>(RainDropTexturePath);
            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(RainDropNormalPath);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(RainShaderPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(RainMaterialPath);
            if (!drop || !normal || !shader || !material
                || material.shader != shader
                || material.GetTexture("_MainTex") != drop
                || material.GetTexture("_Normal") != normal
                || !material.GetShaderPassEnabled("DistortionVectors"))
                return Fail("Weatherade rain texture, normal, shader, or material is not bound.", out failure);

            // Shader includes are imported as TextAssets in the editor, but use
            // the main-asset lookup so this check remains valid if Unity changes
            // the importer type while the authored path stays canonical.
            if (AssetDatabase.LoadMainAssetAtPath(SurfaceShaderIncludePath) == null)
                return Fail("The shared road surface include is missing: " + SurfaceShaderIncludePath, out failure);
            if (!AssetExists<GrimeBrush>(PuddleBrushPath))
                return Fail("The authored puddle brush is missing: " + PuddleBrushPath, out failure);
            if (!AssetExists<Material>(WetRoadMaterialPath) || !AssetExists<Material>(RainSurfaceMaterialPath))
                return Fail("The authored wet-road material sources are incomplete.", out failure);
            if (!AssetExists<AudioClip>(WindAmbienceClipPath))
                return Fail("The decoded wind ambience clip is missing: " + WindAmbienceClipPath, out failure);

            failure = string.Empty;
            return true;
        }

        public static string Describe()
        {
            return "Rain: " + RainPrefabPath
                + " | Weatherade: " + RainMaterialPath
                + " | Wind: " + WindAmbienceClipPath
                + " | Puddles: " + PuddleBrushPath
                + " | Road shader: " + SurfaceShaderIncludePath;
        }

        static bool AssetExists<T>(string path) where T : UnityEngine.Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(path) != null;
        }

        static bool Fail(string message, out string failure)
        {
            failure = message;
            return false;
        }
    }
}
#endif
