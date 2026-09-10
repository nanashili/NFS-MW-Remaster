using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Surface-only weather adapter. It owns global road shader values and applies
    /// exposure-aware wetness to authored surface regions at a bounded cadence.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeatherSurfaceCoverage : MonoBehaviour
    {
        static readonly int WeatherTimeProperty = Shader.PropertyToID("_RacingWeatherTime");
        static readonly int RainIntensityProperty = Shader.PropertyToID("_RacingRainIntensity");
        static readonly int WindDirectionProperty = Shader.PropertyToID("_RacingWindDirection");
        static readonly int WindSpeedProperty = Shader.PropertyToID("_RacingWindSpeed");
        static readonly int WetnessProperty = Shader.PropertyToID("_RacingWetness");
        static readonly int StandingWaterProperty = Shader.PropertyToID("_RacingStandingWater");
        static readonly int RippleStrengthProperty = Shader.PropertyToID("_RacingRainRipples");
        static readonly int RainSpotStrengthProperty = Shader.PropertyToID("_RacingRainSpots");

        [Tooltip("Optional global road fallback. Authored WeatherSurfaceRegion components still receive their own exposure value.")]
        public RoadWetness roadWetness;
        [Min(.05f)] public float regionRefreshSeconds = .25f;
        [Min(1)] public int maximumSurfaceRegions = 128;

        float regionRefreshTimer;
        float lastSurfaceWetness = -1;
        WeatherSurfacePresentation current;

        public WeatherSurfacePresentation Current => current;

        public void ApplyWeather(
            WeatherSnapshot snapshot,
            WeatherSimulation simulation,
            WeatherPresentationQuality quality)
        {
            current = WeatherPresentationModel.Surface(snapshot, quality);
            ApplyGlobals(snapshot, current);
            if (roadWetness && roadWetness.acceptWeather) roadWetness.ApplyWeather(current.wetness);

            regionRefreshTimer -= Time.unscaledDeltaTime;
            if (regionRefreshTimer > 0 && Mathf.Abs(lastSurfaceWetness - current.wetness) <= .01f) return;

            regionRefreshTimer = Mathf.Max(.05f, regionRefreshSeconds);
            lastSurfaceWetness = current.wetness;
            int count = Mathf.Min(WeatherSurfaceRegion.ActiveCount, Mathf.Max(1, maximumSurfaceRegions));
            for (int i = 0; i < count; i++)
            {
                WeatherSurfaceRegion region = WeatherSurfaceRegion.GetActive(i);
                if (region) region.ApplyWeather(simulation);
            }
        }

        public void ResetPresentation()
        {
            current = default;
            regionRefreshTimer = 0;
            lastSurfaceWetness = -1;
            if (roadWetness) roadWetness.ResetWeatherPresentation();
            WeatherSurfaceRegion.ClearAllWeatherPresentation();
            ClearGlobals();
        }

        public static void ApplyGlobals(WeatherSnapshot snapshot, WeatherSurfacePresentation surface)
        {
            Shader.SetGlobalFloat(WeatherTimeProperty, snapshot.simulationSeconds);
            Shader.SetGlobalFloat(RainIntensityProperty, snapshot.precipitationIntensity);
            Shader.SetGlobalFloat(WetnessProperty, surface.wetness);
            Shader.SetGlobalFloat(StandingWaterProperty, surface.standingWater);
            Shader.SetGlobalFloat(RippleStrengthProperty, surface.rippleStrength);
            Shader.SetGlobalFloat(RainSpotStrengthProperty, surface.rainSpotStrength);
            Shader.SetGlobalFloat(WindSpeedProperty, snapshot.windSpeedMps);
            Vector3 wind = Quaternion.Euler(0, snapshot.windDirectionDegrees, 0) * Vector3.forward;
            Shader.SetGlobalVector(WindDirectionProperty, new Vector4(wind.x, 0, wind.z, 0));
        }

        public static void ClearGlobals()
        {
            Shader.SetGlobalFloat(WeatherTimeProperty, 0);
            Shader.SetGlobalFloat(RainIntensityProperty, 0);
            Shader.SetGlobalFloat(WetnessProperty, 0);
            Shader.SetGlobalFloat(StandingWaterProperty, 0);
            Shader.SetGlobalFloat(RippleStrengthProperty, 0);
            Shader.SetGlobalFloat(RainSpotStrengthProperty, 0);
            Shader.SetGlobalFloat(WindSpeedProperty, 0);
            Shader.SetGlobalVector(WindDirectionProperty, Vector4.zero);
        }

        void OnDisable()
        {
            ResetPresentation();
        }
    }
}
