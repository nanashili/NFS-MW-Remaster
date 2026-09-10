using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Scene adapter for the testable weather simulation. It owns one cached set of
    /// presentation references and never searches the scene from its update path.
    /// </summary>
    [DefaultExecutionOrder(-150), DisallowMultipleComponent]
    public sealed class DynamicWeatherWorld : MonoBehaviour, ICareerProfileParticipant
    {
        public static DynamicWeatherWorld Active { get; private set; }

        [Header("Authoring")]
        [SerializeField] private WeatherClimateProfile climate;
        [SerializeField] private WeatherPresetCatalog catalog;
        [SerializeField] private int seed;
        [SerializeField] private bool startPaused;
        [SerializeField] private bool useUnscaledTime = true;

        [Header("Presentation adapters")]
        [SerializeField] private Camera primaryCamera;
        [SerializeField] private LocalRain rain;
        [SerializeField] private WeatherSurfaceCoverage surfaceCoverage;
        [Tooltip("Legacy global road adapter retained for scenes built before WeatherSurfaceCoverage.")]
        [SerializeField] private RoadWetness roadWetness;
        [SerializeField] private SensoryAudioWorld audioWorld;
        [SerializeField] private AudioClip rainAmbienceClip;
        [SerializeField] private AudioClip windAmbienceClip;
        [Range(0, 1), SerializeField] private float rainAmbienceGain = .65f;
        [Range(0, 1), SerializeField] private float windAmbienceGain = .35f;
        [SerializeField] private AudioClip thunderClip;
        [SerializeField] private Light lightningLight;
        [SerializeField] private bool reduceLightningFlashes;

        [Header("Presentation only")]
        [SerializeField] private WeatherPresentationQuality presentationQuality = WeatherPresentationQuality.High;

        WeatherSimulation simulation;
        WeatherClimateProfile runtimeClimate;
        WeatherPresetCatalog runtimeCatalog;
        bool ownsRuntimeClimate;
        bool ownsRuntimeCatalog;
        float lightningFlashUntil;
        float originalLightningIntensity;
        bool originalLightningEnabled;
        Color originalLightningColor;
        FeedbackVoiceLease rainAmbienceVoice;
        FeedbackVoiceLease windAmbienceVoice;
        float surfaceRefreshTimer;
        float lastSurfaceWetness = -1;
        [Min(.05f), SerializeField] float surfaceRefreshSeconds = .25f;
        [Min(1), SerializeField] int maximumSurfaceRegions = 128;

        public string ProfileSectionId => "weather";
        public WeatherSimulation Simulation => simulation;
        public WeatherSnapshot Snapshot => simulation == null ? default : simulation.Snapshot;
        public WeatherPresentationQuality PresentationQuality => presentationQuality;
        public Camera PrimaryCamera => primaryCamera;
        public WeatherSurfaceCoverage SurfaceCoverage => surfaceCoverage;
        public WeatherClimateProfile Climate => runtimeClimate ?? climate;
        public WeatherPresetCatalog Catalog => runtimeCatalog ?? catalog;
        public float DroppedCatchUpSeconds => simulation == null ? 0 : simulation.DroppedCatchUpSeconds;
        public int PendingLightningCount => simulation == null ? 0 : simulation.PendingLightningCount;

        public event Action<WeatherSnapshot> SnapshotChanged;

        void OnEnable()
        {
            if (Active != null && Active != this)
            {
                Debug.LogError("Only one DynamicWeatherWorld may own the active scene weather state.", this);
                enabled = false;
                return;
            }
            Active = this;
            ResolveReferencesOnce();
            runtimeClimate = climate != null ? climate : WeatherClimateProfile.CreateRuntimeDefaults();
            runtimeCatalog = catalog != null ? catalog : WeatherPresetCatalog.CreateRuntimeDefaults();
            ownsRuntimeClimate = climate == null;
            ownsRuntimeCatalog = catalog == null;
            simulation = new WeatherSimulation(runtimeClimate, runtimeCatalog, seed);
            if (!string.IsNullOrEmpty(simulation.LastConfigurationError))
            {
                Debug.LogError("Weather configuration is invalid: " + simulation.LastConfigurationError, this);
                enabled = false;
                return;
            }
            simulation.SetPaused(startPaused);
            simulation.SnapshotChanged += HandleSnapshot;
            if (lightningLight)
            {
                originalLightningIntensity = lightningLight.intensity;
                originalLightningEnabled = lightningLight.enabled;
                originalLightningColor = lightningLight.color;
            }
            ApplyPresentation(simulation.Snapshot);
        }

        void ResolveReferencesOnce()
        {
            if (!primaryCamera) primaryCamera = Camera.main;
            if (!rain) rain = GetComponentInChildren<LocalRain>(true);
            if (!surfaceCoverage) surfaceCoverage = GetComponentInChildren<WeatherSurfaceCoverage>(true);
            if (!roadWetness) roadWetness = GetComponentInChildren<RoadWetness>(true);
            if (surfaceCoverage && !surfaceCoverage.roadWetness) surfaceCoverage.roadWetness = roadWetness;
            if (!audioWorld) audioWorld = GetComponentInChildren<SensoryAudioWorld>(true);
        }

        void Update()
        {
            if (simulation == null) return;
            float delta = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            simulation.Advance(Mathf.Min(Mathf.Max(0, delta), 30));
            ApplyPresentation(simulation.Snapshot);
            DrainLightning();
            RestoreLightningFlashIfDue();
        }

        void HandleSnapshot(WeatherSnapshot value) => SnapshotChanged?.Invoke(value);

        void ApplyPresentation(WeatherSnapshot value)
        {
            if (rain) rain.ApplyWeather(value, presentationQuality, primaryCamera);
            if (surfaceCoverage)
            {
                surfaceCoverage.ApplyWeather(value, simulation, presentationQuality);
            }
            else
            {
                // Compatibility path for weather scenes authored before surface
                // coverage became its own adapter.
                WeatherSurfaceCoverage.ApplyGlobals(
                    value,
                    WeatherPresentationModel.Surface(value, presentationQuality));
                if (roadWetness && roadWetness.acceptWeather) roadWetness.ApplyWeather(value.surfaceWetness);
                RefreshLegacySurfaceRegions(value);
            }
            SyncAmbience(value);
        }

        void RefreshLegacySurfaceRegions(WeatherSnapshot value)
        {
            surfaceRefreshTimer -= Time.unscaledDeltaTime;
            if (surfaceRefreshTimer <= 0 || Mathf.Abs(lastSurfaceWetness - value.surfaceWetness) > .01f)
            {
                surfaceRefreshTimer = Mathf.Max(.05f, surfaceRefreshSeconds);
                lastSurfaceWetness = value.surfaceWetness;
                int count = Mathf.Min(WeatherSurfaceRegion.ActiveCount, Mathf.Max(1, maximumSurfaceRegions));
                for (int i = 0; i < count; i++)
                {
                    WeatherSurfaceRegion region = WeatherSurfaceRegion.GetActive(i);
                    if (region) region.ApplyWeather(simulation);
                }
            }
        }

        void SyncAmbience(WeatherSnapshot value)
        {
            if (!audioWorld) return;
            bool sheltered = primaryCamera && WeatherShelterVolume.IsSheltered(primaryCamera.transform.position);
            float rainGain = sheltered ? 0 : Mathf.Clamp01(value.precipitationIntensity) * Mathf.Clamp01(rainAmbienceGain);
            float windGain = Mathf.Clamp01(value.windSpeedMps / 24f) * Mathf.Clamp01(windAmbienceGain);
            SyncLoop(ref rainAmbienceVoice, rainAmbienceClip, rainGain, 210);
            SyncLoop(ref windAmbienceVoice, windAmbienceClip, windGain, 211);
        }

        void SyncLoop(ref FeedbackVoiceLease voice, AudioClip clip, float gain, int priority)
        {
            if (clip == null)
            {
                if (voice.IsValid) audioWorld.Release(voice);
                voice = default;
                return;
            }
            if (!audioWorld.Owns(voice, clip))
            {
                if (audioWorld.Owns(voice)) audioWorld.Release(voice);
                voice = audioWorld.Play(clip, SensoryCategory.Environment, primaryCamera ? primaryCamera.transform : transform, Vector3.zero, 0, 1, priority, true);
            }
            if (audioWorld.Owns(voice, clip)) audioWorld.UpdateVoice(voice, gain);
        }

        void DrainLightning()
        {
            while (simulation != null && simulation.TryDequeueLightning(out var strike))
            {
                if (lightningLight && !reduceLightningFlashes)
                {
                    lightningLight.enabled = true;
                    lightningLight.intensity = Mathf.Max(.01f, originalLightningIntensity) * 2.5f;
                    lightningLight.color = Color.Lerp(originalLightningColor, Color.white, .75f);
                    lightningFlashUntil = Mathf.Max(lightningFlashUntil, Time.unscaledTime + strike.flashDurationSeconds);
                }
                if (audioWorld && thunderClip)
                {
                    double scheduled = AudioSettings.dspTime + Mathf.Clamp(strike.thunderDelaySeconds, 0, 60);
                    audioWorld.Play(thunderClip, SensoryCategory.Environment, null, primaryCamera ? primaryCamera.transform.position : transform.position, .85f, 1, 80, false, scheduled);
                }
            }
        }

        void RestoreLightningFlashIfDue()
        {
            if (lightningLight && lightningFlashUntil > 0 && Time.unscaledTime >= lightningFlashUntil)
            {
                lightningLight.intensity = originalLightningIntensity;
                lightningLight.color = originalLightningColor;
                lightningLight.enabled = originalLightningEnabled;
                lightningFlashUntil = 0;
            }
        }

        public void SetPresentationQuality(WeatherPresentationQuality quality)
        {
            presentationQuality = quality;
            ApplyPresentation(Snapshot);
        }
        public void SetPaused(bool value) => simulation?.SetPaused(value);
        public void SetTimeScale(float value) => simulation?.SetTimeScale(value);
        public void SetWeatherSpeed(float value) => simulation?.SetWeatherSpeed(value);
        public void SetTimeOfDayHours(float hours) => simulation?.SetTimeOfDayHours(hours);
        public bool SetAutomaticWeather(bool value) { if (simulation == null) return false; simulation.SetAutomaticWeather(value); return true; }
        public bool TransitionToPreset(string id, bool immediate, out string failure)
        {
            if (simulation == null)
            {
                failure = "Weather simulation is not initialized.";
                return false;
            }

            return simulation.TransitionToPreset(id, immediate, out failure);
        }
        public CareerWeatherData CaptureWeatherState() => simulation?.CaptureState();

        public void Capture(CareerProfileData profile)
        {
            if (profile == null || simulation == null) return;
            profile.weather = simulation.CaptureState();
        }

        public bool Restore(CareerProfileData profile, out string failure)
        {
            if (simulation == null) { failure = "Weather simulation is not initialized."; return false; }
            if (profile == null || profile.weather == null) { simulation.Reset(seed); failure = string.Empty; return true; }
            return simulation.RestoreState(profile.weather, out failure);
        }

        void OnDisable()
        {
            if (simulation != null) simulation.SnapshotChanged -= HandleSnapshot;
            if (audioWorld)
            {
                if (rainAmbienceVoice.IsValid) audioWorld.Release(rainAmbienceVoice);
                if (windAmbienceVoice.IsValid) audioWorld.Release(windAmbienceVoice);
            }
            rainAmbienceVoice = default;
            windAmbienceVoice = default;
            if (lightningLight)
            {
                lightningLight.intensity = originalLightningIntensity;
                lightningLight.color = originalLightningColor;
                lightningLight.enabled = originalLightningEnabled;
            }
            if (rain) rain.ResetWeatherPresentation();
            if (surfaceCoverage) surfaceCoverage.ResetPresentation();
            else
            {
                if (roadWetness) roadWetness.ResetWeatherPresentation();
                WeatherSurfaceRegion.ClearAllWeatherPresentation();
            }
            if (Active == this) Active = null;
            if (simulation != null) simulation.Dispose();
            simulation = null;
            if (ownsRuntimeClimate && runtimeClimate) DestroyRuntimeAsset(runtimeClimate);
            if (ownsRuntimeCatalog && runtimeCatalog) DestroyRuntimeAsset(runtimeCatalog);
            runtimeClimate = null; runtimeCatalog = null; ownsRuntimeClimate = ownsRuntimeCatalog = false;
            WeatherSurfaceCoverage.ClearGlobals();
        }

        static void DestroyRuntimeAsset(UnityEngine.Object asset)
        {
            if (Application.isPlaying) Destroy(asset);
            else DestroyImmediate(asset);
        }
    }

    /// <summary>
    /// Authored local surface response. Weather supplies exposure potential; each
    /// region retains its own wetness and can be used by presentation consumers.
    /// Vehicle physics never reads this component automatically.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeatherSurfaceRegion : MonoBehaviour
    {
        static readonly List<WeatherSurfaceRegion> active = new List<WeatherSurfaceRegion>(16);
        const int MaximumActiveRegions = 256;
        static readonly int WetnessProperty = Shader.PropertyToID("_RacingWetness");
        static readonly int StandingWaterProperty = Shader.PropertyToID("_RacingStandingWater");
        [Range(0, 1)] public float exposure = 1;
        [Min(.01f)] public float drainageMultiplier = 1;
        public Renderer[] renderers = Array.Empty<Renderer>();
        MaterialPropertyBlock propertyBlock;
        public float Wetness { get; private set; }
        public float StandingWater { get; private set; }
        public static int ActiveCount => active.Count;
        public static WeatherSurfaceRegion GetActive(int index) => index >= 0 && index < active.Count ? active[index] : null;
        void OnEnable()
        {
            EnsurePropertyBlock();
            if (active.Contains(this)) return;
            if (active.Count >= MaximumActiveRegions) { Debug.LogError("Weather surface region cap reached; region is ignored.", this); return; }
            active.Add(this);
        }
        void OnDisable() { active.Remove(this); }
        public void ApplyWeather(WeatherSimulation simulation)
        {
            if (simulation == null) return;
            EnsurePropertyBlock();
            Wetness = simulation.EvaluateLocalWetness(exposure, drainageMultiplier);
            WeatherSnapshot snapshot = simulation.Snapshot;
            StandingWater = Mathf.Clamp01(snapshot.standingWater * exposure / Mathf.Max(.01f, drainageMultiplier));
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i]) continue;
                renderers[i].GetPropertyBlock(propertyBlock);
                propertyBlock.SetFloat(WetnessProperty, Wetness);
                propertyBlock.SetFloat(StandingWaterProperty, StandingWater);
                renderers[i].SetPropertyBlock(propertyBlock);
            }
        }
        public void ClearWeatherPresentation()
        {
            EnsurePropertyBlock();
            Wetness = 0; StandingWater = 0;
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i]) continue;
                renderers[i].GetPropertyBlock(propertyBlock);
                propertyBlock.SetFloat(WetnessProperty, 0);
                propertyBlock.SetFloat(StandingWaterProperty, 0);
                renderers[i].SetPropertyBlock(propertyBlock);
            }
        }
        void EnsurePropertyBlock()
        {
            if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
        }
        public static void ClearAllWeatherPresentation()
        {
            for (int i = 0; i < active.Count; i++) if (active[i]) active[i].ClearWeatherPresentation();
        }
    }

    /// <summary>Optional authored shelter volume used by the bounded precipitation adapter.</summary>
    [DisallowMultipleComponent]
    public sealed class WeatherShelterVolume : MonoBehaviour
    {
        static readonly List<WeatherShelterVolume> active = new List<WeatherShelterVolume>(8);
        [SerializeField] private Collider shelter;
        [Range(0, 1)] public float exposure = 0;
        public static int ActiveCount => active.Count;
        void OnEnable() { if (!shelter) shelter = GetComponent<Collider>(); if (!active.Contains(this)) active.Add(this); }
        void OnDisable() { active.Remove(this); }
        public static bool IsSheltered(Vector3 position)
        {
            for (int i = 0; i < active.Count; i++)
            {
                var volume = active[i];
                if (volume && volume.shelter && volume.shelter.bounds.Contains(position) && volume.exposure < .5f) return true;
            }
            return false;
        }
    }
}
