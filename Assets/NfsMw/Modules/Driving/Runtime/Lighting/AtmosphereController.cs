using System;
using System.Collections.Generic;
using UnityEngine;
using NfsMwRemaster.Driving;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace NfsMwRemaster.Lighting
{
    [DisallowMultipleComponent]
    public sealed class AtmosphereController : MonoBehaviour
    {
        public string id=Guid.NewGuid().ToString("N");
        public AtmosphereProfile fallback;
        public Camera worldCamera;
        public Light keyLight;
        public Light moonLight;
        [Min(0)] public float moonIntensity=.12f;
        [Tooltip("Optional authoritative weather snapshot. This controller remains the sole HDRP volume and key-light writer.")]
        public DynamicWeatherWorld weatherWorld;
        public AtmosphereZone[] zones=Array.Empty<AtmosphereZone>();
        public LightingFixture[] fixtures=Array.Empty<LightingFixture>();
        public LightingProbeBinding[] probes=Array.Empty<LightingProbeBinding>();
        public Renderer[] bakeGeometry=Array.Empty<Renderer>();
        public LightingBakeSet approvedBake;
        public LightingQuality quality=LightingQuality.High;
        public AtmosphereLook Effective {get;private set;}
        public WeatherCloudPresentation CloudPresentation {get;private set;}
        public bool VolumetricCloudsAvailable {get;private set;}
        public bool VolumetricCloudsEnabled {get;private set;}
        public readonly List<string> Contributors=new List<string>();
        // Weather fog density is an authored 0..1 gameplay signal. HDRP expects
        // extinction density, so keep the conversion gentle enough that its
        // finite volumetric froxel depth does not show horizontal bands.
        const float WeatherFogDensityScale=.0016f;
        // Authored visibility is the distance where the scene should be barely
        // discernible. Three optical depths leave about five percent contrast.
        const float VisibilityLimitOpticalDepth=3f;
        // The demo profile's fixed EV is authored for a lit daytime road. Keep
        // the same fixed-exposure path deterministic, but open it for the
        // moonlit range when the weather clock reaches night; otherwise HDRP's
        // physically based sky and the road lights both quantize to black.
        const float DaylightExposureReferenceLux=12000f;
        const float MaximumDaylightExposureLiftEV100=3f;
        const float NightExposureEV100=-2f;
        const float NightExposureDaylightThreshold=.3f;
        // Layers 28..31 are reserved for four independent camera atmospheres; 27 is Studio preview.
        public const int RuntimeVolumeMask=unchecked((int)0xf0000000);
        static readonly AtmosphereController[] Owners=new AtmosphereController[4];
        static readonly Dictionary<Light,AtmosphereController> LightOwners=new Dictionary<Light,AtmosphereController>();
        readonly List<Light> claimedLights=new List<Light>();
        readonly List<FixtureState> fixtureState=new List<FixtureState>();
        struct FixtureState {public Light light;public float intensity;public LightShadows shadows;}
        GameObject volumeObject;VolumeProfile transient;HDAdditionalCameraData cameraData;
        LightingEnvironmentSnapshot snapshot;LayerMask mask;bool custom,initialized;Camera registeredCamera;
        float moonOriginalIntensity;Color moonOriginalColor;LightShadows moonOriginalShadows;
        FrameSettings frames;FrameSettingsOverrideMask overrides;int slot=-1;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOwners(){Array.Clear(Owners,0,Owners.Length);LightOwners.Clear();}
        void OnEnable()
        {
            if(!Application.isPlaying)return;
            try
            {
                if(!worldCamera||!fallback||!fallback.IsValid||!(GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset))
                    throw new InvalidOperationException("Atmosphere needs HDRP, an explicit world camera and valid fallback.");
                cameraData=worldCamera.GetComponent<HDAdditionalCameraData>();
                if(!cameraData)throw new InvalidOperationException("World camera needs HDRP camera data.");
                foreach(var owner in Owners)if(owner&&owner.worldCamera==worldCamera)throw new InvalidOperationException("Another atmosphere already owns this camera.");
                slot=Array.FindIndex(Owners,owner=>!owner);
                if(slot<0)throw new InvalidOperationException("All four reserved atmosphere camera layers are in use.");
                Owners[slot]=this;registeredCamera=worldCamera;
                ClaimLight(keyLight);
                if(moonLight&&moonLight!=keyLight){moonOriginalIntensity=moonLight.intensity;moonOriginalColor=moonLight.color;moonOriginalShadows=moonLight.shadows;ClaimLight(moonLight);}
                foreach(var fixture in fixtures)if(fixture&&fixture.source&&!fixture.Critical&&fixture.source!=keyLight&&!claimedLights.Contains(fixture.source))
                {ClaimLight(fixture.source);fixtureState.Add(new FixtureState{light=fixture.source,intensity=fixture.source.intensity,shadows=fixture.source.shadows});}
                snapshot=new LightingEnvironmentSnapshot(keyLight);
                mask=cameraData.volumeLayerMask;custom=cameraData.customRenderingSettings;
                frames=cameraData.renderingPathCustomFrameSettings;overrides=cameraData.renderingPathCustomFrameSettingsOverrideMask;
                cameraData.volumeLayerMask=(mask.value&~RuntimeVolumeMask&~(1<<27))|(1<<(28+slot));
                LightingCamera.SetPostProcessing(cameraData,true);
                transient=ScriptableObject.CreateInstance<VolumeProfile>();transient.hideFlags=HideFlags.HideAndDontSave;
                WeatherVolumetricClouds.Initialize(transient);
                volumeObject=new GameObject("Atmosphere runtime state"){hideFlags=HideFlags.HideAndDontSave,layer=28+slot};
                var volume=volumeObject.AddComponent<Volume>();volume.isGlobal=true;volume.priority=10000;volume.sharedProfile=transient;
                Apply(0);
            }
            catch(Exception ex){Debug.LogError(ex.Message,this);Release();enabled=false;}
        }
        void ClaimLight(Light light)
        {
            if(!light)return;
            if(LightOwners.TryGetValue(light,out var owner)&&owner&&owner!=this)
                throw new InvalidOperationException("Two camera atmospheres cannot independently control the same world light: "+light.name);
            LightOwners[light]=this;claimedLights.Add(light);
        }
        void LateUpdate()
        {
            if(slot<0)return;
            try{Apply(Time.unscaledDeltaTime);}
            catch(Exception ex){Debug.LogError(ex.Message,this);enabled=false;}
        }
        void Apply(float dt)
        {
            if(worldCamera!=registeredCamera)throw new InvalidOperationException("Camera binding changed. Disable and re-enable the atmosphere owner to rebind.");
            var target=AtmosphereResolver.Resolve(fallback,zones,registeredCamera.transform.position,Contributors);
            WeatherCloudPresentation cloudPresentation=default;
            bool naturalFog=false;
            if(weatherWorld&&weatherWorld.Simulation!=null)
            {
                naturalFog=true;
                WeatherSnapshot weather=weatherWorld.Snapshot;
                cloudPresentation=WeatherPresentationModel.Clouds(weather,weatherWorld.PresentationQuality);
                float daylight=Mathf.Clamp01(weather.daylight);
                // Direct solar energy is attenuated by the derived optical depth.
                // The sky/equator terms remain separate, so overcast weather keeps
                // diffuse illumination instead of becoming a global brightness cut.
                float sunTransmission=cloudPresentation.sunTransmission>0?cloudPresentation.sunTransmission:1;
                target.keyIntensity*=sunTransmission*Mathf.Lerp(.16f,1,daylight);
                // Treat the authored fixed exposure as the clouded baseline, then
                // close it by one EV per doubling of direct daylight. Clear noon
                // receives the full three-stop correction without darkening fog.
                float daylightExposureLift=Mathf.Clamp(
                    Mathf.Log(Mathf.Max(DaylightExposureReferenceLux,target.keyIntensity)/DaylightExposureReferenceLux,2),
                    0,MaximumDaylightExposureLiftEV100);
                target.exposureEV100+=daylightExposureLift;
                float nightExposureBlend=Mathf.InverseLerp(0,NightExposureDaylightThreshold,daylight);
                target.exposureEV100=Mathf.Lerp(NightExposureEV100,target.exposureEV100,nightExposureBlend);
                target.keyColor=Color.Lerp(target.keyColor,weather.sunColor,Mathf.Clamp01(daylight*.45f));
                target.keyColor=Color.Lerp(target.keyColor,new Color(.68f,.74f,.82f),weather.cloudCover*.6f);
                target.sky=Color.Lerp(target.sky,new Color(.64f,.71f,.8f),weather.cloudCover*.22f);
                target.equator=Color.Lerp(target.equator,new Color(.42f,.47f,.54f),weather.cloudCover*.18f);
                target.keyEuler.x=weather.sunElevationDegrees;
                float hazeDensity=Mathf.Max(weather.fogDensity,weather.atmosphere.hazeDensity);
                float visibilityDensity=VisibilityLimitOpticalDepth/Mathf.Max(1,weather.visibilityMeters);
                target.fog=target.fog||hazeDensity>.001f||weather.visibilityMeters<target.fogEnd;
                target.fogDensity=Mathf.Max(target.fogDensity,Mathf.Clamp(Mathf.Max(hazeDensity*WeatherFogDensityScale,visibilityDensity),0,.1f));
                target.fogEnd=Mathf.Min(target.fogEnd,Mathf.Max(50,weather.visibilityMeters));
                target.fogColor=Color.Lerp(target.fogColor,new Color(.56f,.63f,.7f),hazeDensity*.7f);
                if(moonLight&&moonLight!=keyLight)
                {
                    moonLight.color=Color.Lerp(new Color(.2f,.28f,.5f),Color.white,.25f);
                    moonLight.lightUnit=LightUnit.Lux;
                    moonLight.intensity=moonIntensity*weather.moonlight;
                    moonLight.transform.rotation=Quaternion.Euler(-weather.sunElevationDegrees+18,target.keyEuler.y+180,0);
                }
            }
            Effective=initialized?AtmosphereResolver.Step(Effective,target,fallback,dt):target;initialized=true;
            Effective.ApplyEnvironment(keyLight);Effective.ApplyPost(transient,false,Mathf.Max(1000,registeredCamera.farClipPlane+1),naturalFog);
            CloudPresentation=cloudPresentation;
            VolumetricCloudsAvailable=WeatherVolumetricClouds.IsSupported();
            LightingCamera.SetVolumetricClouds(cameraData,cloudPresentation.enabled&&VolumetricCloudsAvailable);
            VolumetricCloudsEnabled=WeatherVolumetricClouds.Apply(transient,cloudPresentation);
            foreach(var state in fixtureState)if(state.light)
            {
                state.light.intensity=state.intensity*(quality==LightingQuality.Low?fallback.lowFixtureIntensity:1);
                state.light.shadows=quality==LightingQuality.Low&&!fallback.lowDecorativeShadows?LightShadows.None:state.shadows;
            }
        }
        void OnDisable()=>Release();
        void Release()
        {
            if(snapshot!=null)
            {
                snapshot.Dispose();snapshot=null;
                if(cameraData){cameraData.volumeLayerMask=mask;cameraData.customRenderingSettings=custom;cameraData.renderingPathCustomFrameSettings=frames;cameraData.renderingPathCustomFrameSettingsOverrideMask=overrides;}
            }
            foreach(var state in fixtureState)if(state.light){state.light.intensity=state.intensity;state.light.shadows=state.shadows;}fixtureState.Clear();
            if(moonLight){moonLight.intensity=moonOriginalIntensity;moonLight.color=moonOriginalColor;moonLight.shadows=moonOriginalShadows;}
            foreach(var light in claimedLights)if(light&&LightOwners.TryGetValue(light,out var owner)&&owner==this)LightOwners.Remove(light);claimedLights.Clear();
            if(slot>=0&&Owners[slot]==this)Owners[slot]=null;slot=-1;initialized=false;
            CloudPresentation=default;VolumetricCloudsAvailable=false;VolumetricCloudsEnabled=false;
            if(volumeObject){volumeObject.SetActive(false);Destroy(volumeObject);}
            if(transient){foreach(var component in transient.components)if(component)Destroy(component);Destroy(transient);}
        }
    }
}
