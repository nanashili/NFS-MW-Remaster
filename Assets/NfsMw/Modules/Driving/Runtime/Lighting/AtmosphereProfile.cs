using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace NfsMwRemaster.Lighting
{
    [Flags] public enum LookCategory { None=0, Lighting=1, Fog=2, Exposure=4, Reflections=8, All=15 }
    public enum LightingQuality { Low, High }
    public enum AtmosphereSky { PhysicallyBased, Gradient }
    [Serializable]
    public struct AtmosphereLook
    {
        public Color sky, equator, ground, keyColor, fogColor, filter;
        [Tooltip("Directional illuminance in lux.")][Range(0,150000)] public float keyIntensity;
        public AtmosphereSky skyModel;
        [Tooltip("Camera exposure at ISO 100; higher values darken the image.")][Range(-5,20)] public float exposureEV100;
        public bool automaticExposure;
        public Vector3 keyEuler;
        public bool fog;
        public FogMode fogMode;
        [Range(0,.1f)] public float fogDensity;
        [Min(0)] public float fogStart;
        [Min(1)] public float fogEnd;
        [Range(-5,5)] public float exposure;
        [Range(-100,100)] public float contrast, saturation;
        [Range(0,5)] public float bloom;
        [Range(0,1)] public float vignette;
        public TonemappingMode tonemapping;
        [Range(0,2)] public float reflectionIntensity;
        public static AtmosphereLook Neutral => new AtmosphereLook {
            sky=new Color(.35f,.4f,.5f),equator=new Color(.2f,.23f,.28f),ground=new Color(.1f,.12f,.15f),
            keyColor=Color.white,keyIntensity=90000,keyEuler=new Vector3(40,-30,0),fogColor=new Color(.4f,.46f,.5f),
            skyModel=AtmosphereSky.PhysicallyBased,exposureEV100=13,automaticExposure=true,
            fogMode=FogMode.ExponentialSquared,fogDensity=.003f,fogStart=50,fogEnd=500,filter=Color.white,
            tonemapping=TonemappingMode.ACES,reflectionIntensity=1 };
        public void Blend(AtmosphereLook b, Vector4 w)
        {
            sky=Color.Lerp(sky,b.sky,w.x);equator=Color.Lerp(equator,b.equator,w.x);ground=Color.Lerp(ground,b.ground,w.x);
            keyColor=Color.Lerp(keyColor,b.keyColor,w.x);keyIntensity=Mathf.Lerp(keyIntensity,b.keyIntensity,w.x);
            if(w.x>=.5f)skyModel=b.skyModel;
            keyEuler=Quaternion.Slerp(Quaternion.Euler(keyEuler),Quaternion.Euler(b.keyEuler),w.x).eulerAngles;
            // Keep a disabled fog endpoint optically clear while crossing its boundary.
            float aDensity=fog?fogDensity:0, bDensity=b.fog?b.fogDensity:0;
            fogDensity=Mathf.Lerp(aDensity,bDensity,w.y);fogColor=Color.Lerp(fogColor,b.fogColor,w.y);
            fogStart=Mathf.Lerp(fog?fogStart:100000,b.fog?b.fogStart:100000,w.y);
            fogEnd=Mathf.Lerp(fog?fogEnd:100001,b.fog?b.fogEnd:100001,w.y);
            if(w.y>0)fog=fog||b.fog;if(w.y>=1)fog=b.fog;if(w.y>=.5f)fogMode=b.fogMode;
            exposure=Mathf.Lerp(exposure,b.exposure,w.z);contrast=Mathf.Lerp(contrast,b.contrast,w.z);saturation=Mathf.Lerp(saturation,b.saturation,w.z);
            exposureEV100=Mathf.Lerp(exposureEV100,b.exposureEV100,w.z);if(w.z>=.5f)automaticExposure=b.automaticExposure;
            filter=Color.Lerp(filter,b.filter,w.z);bloom=Mathf.Lerp(bloom,b.bloom,w.z);vignette=Mathf.Lerp(vignette,b.vignette,w.z);
            if(w.z>=.5f)tonemapping=b.tonemapping;reflectionIntensity=Mathf.Lerp(reflectionIntensity,b.reflectionIntensity,w.w);
        }
        public void ApplyEnvironment(Light key)
        {
            if(key){key.color=keyColor;key.lightUnit=LightUnit.Lux;key.intensity=keyIntensity;key.transform.rotation=Quaternion.Euler(keyEuler);}
        }
        public void ApplyPost(VolumeProfile profile, bool deterministicExposure=false, float minimumFogDistance=1000, bool naturalFog=false)
        {
            if(!profile.TryGet<VisualEnvironment>(out var environment))environment=profile.Add<VisualEnvironment>();
            environment.skyType.Override((int)(skyModel==AtmosphereSky.PhysicallyBased?SkyType.PhysicallyBased:SkyType.Gradient));
            environment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            if(!profile.TryGet<PhysicallyBasedSky>(out var physicalSky))physicalSky=profile.Add<PhysicallyBasedSky>();
            physicalSky.type.Override(PhysicallyBasedSkyModel.EarthSimple);
            if(!profile.TryGet<GradientSky>(out var gradient))gradient=profile.Add<GradientSky>();
            gradient.top.Override(sky);gradient.middle.Override(equator);gradient.bottom.Override(ground);
            // Legacy gradient swatches now illuminate a physical scene, with brightness tied to its EV.
            gradient.skyIntensityMode.Override(SkyIntensityMode.Multiplier);gradient.multiplier.Override(Mathf.Pow(2,exposureEV100));
            if(!profile.TryGet<Fog>(out var haze))haze=profile.Add<Fog>();
            haze.enabled.Override(fog);haze.colorMode.Override(naturalFog?FogColorMode.SkyColor:FogColorMode.ConstantColor);
            haze.color.Override(naturalFog?Color.white:fogColor);haze.albedo.Override(naturalFog?Color.white:Color.Lerp(Color.white,fogColor,.25f));
            haze.multipleScatteringIntensity.Override(naturalFog?1:0);
            // HDRP uses extinction distance and height fog. Legacy linear/exponential modes are authored density hints.
            haze.meanFreePath.Override(fogDensity>0?Mathf.Clamp(1/fogDensity,10,100000):100000);
            haze.baseHeight.Override(0);haze.maximumHeight.Override(80);haze.maxFogDistance.Override(Mathf.Max(fogEnd,minimumFogDistance));
            haze.enableVolumetricFog.Override(fog);haze.depthExtent.Override(96);haze.denoisingMode.Override(FogDenoisingMode.Gaussian);
            if(!profile.TryGet<Exposure>(out var meter))meter=profile.Add<Exposure>();
            meter.mode.Override(automaticExposure&&!deterministicExposure?ExposureMode.AutomaticHistogram:ExposureMode.Fixed);
            meter.fixedExposure.Override(exposureEV100);meter.compensation.Override(exposure);
            meter.limitMin.Override(0);meter.limitMax.Override(16);
            meter.adaptationSpeedDarkToLight.Override(3);meter.adaptationSpeedLightToDark.Override(1);
            if(!profile.TryGet<IndirectLightingController>(out var indirect))indirect=profile.Add<IndirectLightingController>();
            indirect.reflectionLightingMultiplier.Override(reflectionIntensity);
            if(!profile.TryGet<ColorAdjustments>(out var color))color=profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0);color.contrast.Override(contrast);color.saturation.Override(saturation);color.colorFilter.Override(filter);
            if(!profile.TryGet<Bloom>(out var glow))glow=profile.Add<Bloom>(true);glow.intensity.Override(bloom);
            if(!profile.TryGet<Vignette>(out var edge))edge=profile.Add<Vignette>(true);edge.intensity.Override(vignette);
            if(!profile.TryGet<Tonemapping>(out var tone))tone=profile.Add<Tonemapping>(true);tone.mode.Override(tonemapping);
        }
    }
    [CreateAssetMenu(menuName="NFS MW/Lighting/Atmosphere Profile")]
    public sealed class AtmosphereProfile : ScriptableObject
    {
        public const int CurrentSchema=2;
        public int schemaVersion=CurrentSchema;
        public string id=Guid.NewGuid().ToString("N");
        [TextArea] public string intent="Synthetic look; requires art approval.";
        public string referenceNotes, semanticVariant="dry-day";
        public AtmosphereLook look=AtmosphereLook.Neutral;
        [Min(.01f)] public float lightingSeconds=1, fogSeconds=2, exposureSeconds=1, reflectionSeconds=2;
        public string bakedScenarioId;
        public bool allowRealtimeFallback=true;
        [Range(0,1)] public float lowFixtureIntensity=1;
        public bool lowDecorativeShadows;
        [Min(0)] public int realtimeBudget=32, shadowBudget=8;
        public string Revision => Hash128.Compute(JsonUtility.ToJson(this)).ToString();
        public bool IsValid
        {
            get
            {
                if(schemaVersion!=CurrentSchema||string.IsNullOrWhiteSpace(id)||(!allowRealtimeFallback&&!string.IsNullOrEmpty(bakedScenarioId)))return false;
                foreach(float value in new[]{look.keyIntensity,look.exposureEV100,look.keyEuler.x,look.keyEuler.y,look.keyEuler.z,look.fogDensity,look.fogStart,look.fogEnd,look.exposure,look.contrast,look.saturation,look.bloom,look.vignette,look.reflectionIntensity,lightingSeconds,fogSeconds,exposureSeconds,reflectionSeconds,lowFixtureIntensity})if(!Finite(value))return false;
                foreach(Color c in new[]{look.sky,look.equator,look.ground,look.keyColor,look.fogColor,look.filter})if(!Finite(c.r)||!Finite(c.g)||!Finite(c.b)||!Finite(c.a))return false;
                return look.keyIntensity>=0&&look.keyIntensity<=150000&&look.exposureEV100>=-5&&look.exposureEV100<=20&&look.fogDensity>=0&&look.fogDensity<=.1f&&look.fogStart>=0&&look.fogEnd>look.fogStart&&Mathf.Abs(look.exposure)<=5&&Mathf.Abs(look.contrast)<=100&&Mathf.Abs(look.saturation)<=100&&look.bloom>=0&&look.bloom<=5&&look.vignette>=0&&look.vignette<=1&&look.reflectionIntensity>=0&&look.reflectionIntensity<=2&&lightingSeconds>0&&fogSeconds>0&&exposureSeconds>0&&reflectionSeconds>0&&lowFixtureIntensity>=0&&lowFixtureIntensity<=1&&Enum.IsDefined(typeof(FogMode),look.fogMode)&&Enum.IsDefined(typeof(TonemappingMode),look.tonemapping)&&Enum.IsDefined(typeof(AtmosphereSky),look.skyModel);
            }
        }
        static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
    }
}
