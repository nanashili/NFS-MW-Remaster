using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(menuName = "NFS MW/Weather/Preset Catalog", fileName = "Weather Preset Catalog")]
    public sealed class WeatherPresetCatalog : ScriptableObject
    {
        public int schemaVersion = 1;
        public WeatherPresetDefinition[] presets = Array.Empty<WeatherPresetDefinition>();

        public WeatherPresetDefinition Find(string id)
        {
            if (presets == null || string.IsNullOrWhiteSpace(id)) return null;
            for (int i = 0; i < presets.Length; i++)
            {
                var preset = presets[i];
                if (preset != null && string.Equals(preset.id, id, StringComparison.Ordinal)) return preset;
            }
            return null;
        }

        public bool Validate(out string failure)
        {
            if (presets == null || presets.Length == 0) { failure = "Weather preset catalog is empty."; return false; }
            for (int i = 0; i < presets.Length; i++)
            {
                var preset = presets[i];
                if (preset == null) { failure = "Weather catalog contains a null preset."; return false; }
                if (string.IsNullOrWhiteSpace(preset.id)) { failure = "Weather preset IDs cannot be empty."; return false; }
                if (!Enum.IsDefined(typeof(WeatherCondition), preset.condition)
                    || !Enum.IsDefined(typeof(WeatherPrecipitationType), preset.precipitationType)
                    || !Finite(preset.precipitationIntensity) || preset.precipitationIntensity < 0 || preset.precipitationIntensity > 1
                    || !Finite(preset.cloudCover) || preset.cloudCover < 0 || preset.cloudCover > 1
                    || !Finite(preset.gustStrength) || preset.gustStrength < 0 || preset.gustStrength > 1
                    || !Finite(preset.fogDensity) || preset.fogDensity < 0 || preset.fogDensity > 1
                    || !Finite(preset.electricalActivity) || preset.electricalActivity < 0 || preset.electricalActivity > 1
                    || !Finite(preset.humidity) || preset.humidity < 0 || preset.humidity > 1
                    || !Finite(preset.windDirectionDegrees)
                    || !Finite(preset.windSpeedMps) || preset.windSpeedMps < 0
                    || !Finite(preset.visibilityMeters) || preset.visibilityMeters <= 0
                    || !Finite(preset.temperatureC) || preset.temperatureC < -80 || preset.temperatureC > 80
                    || !Finite(preset.transitionSeconds) || preset.transitionSeconds <= 0
                    || !Finite(preset.minimumDurationSeconds) || preset.minimumDurationSeconds < 0
                    || !Finite(preset.maximumDurationSeconds) || preset.maximumDurationSeconds < preset.minimumDurationSeconds || preset.maximumDurationSeconds > 604800
                    || !Finite(preset.wetnessContribution) || preset.wetnessContribution < 0 || preset.wetnessContribution > 1
                    || !Finite(preset.standingWaterContribution) || preset.standingWaterContribution < 0 || preset.standingWaterContribution > 1)
                { failure = "Weather preset contains an out-of-range value: " + preset.id; return false; }
                for (int j = i + 1; j < presets.Length; j++)
                    if (presets[j] != null && string.Equals(preset.id, presets[j].id, StringComparison.Ordinal))
                    { failure = "Weather preset IDs must be unique: " + preset.id; return false; }
            }
            failure = string.Empty;
            return true;
        }

        public static WeatherPresetCatalog CreateRuntimeDefaults()
        {
            var catalog = CreateInstance<WeatherPresetCatalog>();
            catalog.hideFlags = HideFlags.HideAndDontSave;
            catalog.presets = DefaultPresets();
            return catalog;
        }

        public static WeatherPresetDefinition[] DefaultPresets()
        {
            return new[]
            {
                Preset("clear", "Clear", WeatherCondition.Clear, WeatherPrecipitationType.None, 0, .08f, 270, 2, .08f, 30000, 0, 0, 22, .42f, .015f, 0),
                Preset("partly-cloudy", "Partly Cloudy", WeatherCondition.PartlyCloudy, WeatherPrecipitationType.None, 0, .38f, 260, 4, .15f, 24000, 0, 0, 20, .5f, .015f, 0),
                Preset("cloudy", "Cloudy", WeatherCondition.Cloudy, WeatherPrecipitationType.None, 0, .62f, 250, 5, .2f, 18000, .01f, 0, 18, .62f, .02f, 0),
                Preset("overcast", "Overcast", WeatherCondition.Overcast, WeatherPrecipitationType.None, 0, .86f, 240, 6, .22f, 12000, .04f, 0, 16, .72f, .025f, .01f),
                Preset("drizzle", "Drizzle", WeatherCondition.Drizzle, WeatherPrecipitationType.Drizzle, .2f, .9f, 235, 7, .25f, 7000, .1f, 0, 15, .82f, .08f, .02f),
                Preset("light-rain", "Light Rain", WeatherCondition.LightRain, WeatherPrecipitationType.Rain, .38f, .92f, 230, 8, .28f, 5500, .14f, 0, 14, .88f, .14f, .08f),
                Preset("rain", "Rain", WeatherCondition.Rain, WeatherPrecipitationType.Rain, .62f, .95f, 225, 10, .34f, 4000, .2f, 0, 13, .92f, .28f, .2f),
                Preset("heavy-rain", "Heavy Rain", WeatherCondition.HeavyRain, WeatherPrecipitationType.Rain, .86f, .98f, 220, 13, .42f, 2600, .28f, 0, 12, .96f, .5f, .55f),
                Preset("storm-rain", "Storm Rain", WeatherCondition.StormRain, WeatherPrecipitationType.Rain, .95f, 1, 215, 17, .65f, 1800, .38f, 0, 11, .98f, .7f, .8f),
                Preset("thunderstorm", "Thunderstorm", WeatherCondition.Thunderstorm, WeatherPrecipitationType.Rain, .9f, 1, 210, 18, .72f, 1600, .42f, .9f, 12, .99f, .72f, .85f),
                Preset("mist", "Mist", WeatherCondition.Mist, WeatherPrecipitationType.None, 0, .5f, 250, 3, .12f, 1800, .42f, 0, 14, .86f, .01f, .02f),
                Preset("fog", "Fog", WeatherCondition.Fog, WeatherPrecipitationType.None, 0, .64f, 245, 3, .15f, 800, .7f, 0, 13, .9f, .01f, .02f),
                Preset("heavy-fog", "Heavy Fog", WeatherCondition.HeavyFog, WeatherPrecipitationType.None, 0, .78f, 240, 2, .12f, 260, .9f, 0, 12, .94f, .01f, .02f),
                Preset("windy", "Windy", WeatherCondition.Windy, WeatherPrecipitationType.None, 0, .35f, 250, 16, .7f, 18000, .01f, 0, 17, .5f, .015f, 0),
                Preset("strong-wind", "Strong Wind", WeatherCondition.StrongWind, WeatherPrecipitationType.None, 0, .55f, 240, 24, .9f, 12000, .04f, 0, 15, .62f, .02f, .01f)
            };
        }

        static WeatherPresetDefinition Preset(string id, string name, WeatherCondition condition,
            WeatherPrecipitationType precipitation, float intensity, float clouds, float direction, float wind,
            float gust, float visibility, float fog, float electrical, float temperature, float humidity,
            float wetness, float standingWater)
        {
            return new WeatherPresetDefinition
            {
                id = id, displayName = name, condition = condition, precipitationType = precipitation,
                precipitationIntensity = intensity, cloudCover = clouds, windDirectionDegrees = direction,
                windSpeedMps = wind, gustStrength = gust, visibilityMeters = visibility, fogDensity = fog,
                electricalActivity = electrical, temperatureC = temperature, humidity = humidity,
                wetnessContribution = wetness, standingWaterContribution = standingWater,
                minimumDurationSeconds = condition == WeatherCondition.Thunderstorm ? 120 : 180,
                maximumDurationSeconds = condition == WeatherCondition.Thunderstorm ? 480 : 1200,
                transitionSeconds = condition == WeatherCondition.Clear ? 90 : 60
            };
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
