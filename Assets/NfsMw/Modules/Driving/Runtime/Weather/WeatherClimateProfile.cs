using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(menuName = "NFS MW/Weather/Climate Profile", fileName = "Weather Climate Profile")]
    public sealed class WeatherClimateProfile : ScriptableObject
    {
        public int schemaVersion = 1;
        public int defaultSeed = 0;
        public string initialPresetId = "clear";
        [Min(.02f)] public float fixedStepSeconds = .25f;
        [Min(1)] public float dayLengthRealSeconds = 1800;
        [Min(0)] public float weatherTimeScale = 1;
        public bool weatherFollowsClockScale;
        [Min(.25f)] public float maxCatchUpSeconds = 8;
        [Range(0, 24)] public float startingTimeHours = 8;
        [Range(0, 24)] public float dawnHours = 5.5f;
        [Range(0, 24)] public float sunriseHours = 6.5f;
        [Range(0, 24)] public float sunsetHours = 18.5f;
        [Range(0, 24)] public float duskHours = 19.5f;
        [Header("Celestial response")]
        public AnimationCurve daylightIntensityCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public AnimationCurve sunElevationCurve = AnimationCurve.Linear(0, -18, 1, 58);
        public Gradient daylightColorGradient = DefaultDaylightColorGradient();
        [Min(0)] public float rainFrequency = .35f;
        [Min(0)] public float stormLikelihood = .08f;
        [Min(0)] public float dryingTendency = .5f;
        [Min(0)] public float temperatureC = 18;
        [Range(0, 1)] public float atmosphericMoisture = .6f;
        [Min(0)] public float minimumPersistenceSeconds = 180;
        [Min(0)] public float maximumPersistenceSeconds = 1200;
        [Min(0)] public float wetnessAccumulationPerSecond = .09f;
        [Min(0)] public float wetnessDrainagePerSecond = .025f;
        [Min(0)] public float standingWaterDrainagePerSecond = .035f;
        [Min(0)] public float sunlightDryingPerSecond = .018f;
        [Min(0)] public float windDryingPerSecond = .002f;
        [Range(0, 1)] public float surfaceWetnessThreshold = .4f;
        [Min(0)] public float lightningQuietSecondsMin = 8;
        [Min(0)] public float lightningQuietSecondsMax = 45;
        [Min(1)] public int maximumLightningEvents = 8;
        [Min(0)] public float soundSpeedMetersPerSecond = 343;
        public WeatherTransitionRule[] transitions = System.Array.Empty<WeatherTransitionRule>();

        public void Sanitize()
        {
            fixedStepSeconds = Mathf.Clamp(finite(fixedStepSeconds), .02f, 10);
            dayLengthRealSeconds = Mathf.Clamp(finite(dayLengthRealSeconds), 1, 604800);
            weatherTimeScale = Mathf.Clamp(finite(weatherTimeScale), 0, 1000);
            maxCatchUpSeconds = Mathf.Clamp(finite(maxCatchUpSeconds), .25f, 120);
            startingTimeHours = Mathf.Repeat(finite(startingTimeHours), 24);
            dawnHours = Mathf.Repeat(finite(dawnHours), 24); sunriseHours = Mathf.Repeat(finite(sunriseHours), 24);
            sunsetHours = Mathf.Repeat(finite(sunsetHours), 24); duskHours = Mathf.Repeat(finite(duskHours), 24);
            minimumPersistenceSeconds = Mathf.Clamp(finite(minimumPersistenceSeconds), 0, 604800);
            maximumPersistenceSeconds = Mathf.Clamp(finite(maximumPersistenceSeconds), minimumPersistenceSeconds, 604800);
            rainFrequency = Mathf.Max(0, finite(rainFrequency)); stormLikelihood = Mathf.Max(0, finite(stormLikelihood));
            dryingTendency = Mathf.Max(0, finite(dryingTendency)); atmosphericMoisture = Mathf.Clamp01(finite(atmosphericMoisture));
            wetnessAccumulationPerSecond = Mathf.Clamp(finite(wetnessAccumulationPerSecond), 0, 10);
            wetnessDrainagePerSecond = Mathf.Clamp(finite(wetnessDrainagePerSecond), 0, 10);
            standingWaterDrainagePerSecond = Mathf.Clamp(finite(standingWaterDrainagePerSecond), 0, 10);
            sunlightDryingPerSecond = Mathf.Clamp(finite(sunlightDryingPerSecond), 0, 10);
            windDryingPerSecond = Mathf.Clamp(finite(windDryingPerSecond), 0, 10);
            surfaceWetnessThreshold = Mathf.Clamp01(finite(surfaceWetnessThreshold));
            lightningQuietSecondsMin = Mathf.Clamp(finite(lightningQuietSecondsMin), 0, 86400);
            lightningQuietSecondsMax = Mathf.Clamp(finite(lightningQuietSecondsMax), lightningQuietSecondsMin, 86400);
            maximumLightningEvents = Mathf.Clamp(maximumLightningEvents, 1, 32);
            soundSpeedMetersPerSecond = Mathf.Clamp(finite(soundSpeedMetersPerSecond), 1, 1000);
            if (daylightIntensityCurve == null) daylightIntensityCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
            if (sunElevationCurve == null) sunElevationCurve = AnimationCurve.Linear(0, -18, 1, 58);
            if (daylightColorGradient == null) daylightColorGradient = DefaultDaylightColorGradient();
        }

        public bool Validate(out string failure)
        {
            if (!Finite(fixedStepSeconds) || fixedStepSeconds < .02f || !Finite(dayLengthRealSeconds) || dayLengthRealSeconds < 1
                || !Finite(weatherTimeScale) || weatherTimeScale < 0 || !Finite(maxCatchUpSeconds) || maxCatchUpSeconds < .25f
                || !Finite(startingTimeHours) || startingTimeHours < 0 || startingTimeHours > 24
                || !Finite(dawnHours) || dawnHours < 0 || dawnHours > 24
                || !Finite(sunriseHours) || sunriseHours < 0 || sunriseHours > 24
                || !Finite(sunsetHours) || sunsetHours < 0 || sunsetHours > 24
                || !Finite(duskHours) || duskHours < 0 || duskHours > 24
                || !Finite(rainFrequency) || rainFrequency < 0 || !Finite(stormLikelihood) || stormLikelihood < 0
                || !Finite(dryingTendency) || dryingTendency < 0 || !Finite(temperatureC) || temperatureC < -80 || temperatureC > 80
                || !Finite(atmosphericMoisture) || atmosphericMoisture < 0 || atmosphericMoisture > 1
                || !Finite(minimumPersistenceSeconds) || minimumPersistenceSeconds < 0
                || !Finite(maximumPersistenceSeconds) || maximumPersistenceSeconds < minimumPersistenceSeconds || maximumPersistenceSeconds > 604800
                || !Finite(wetnessAccumulationPerSecond) || wetnessAccumulationPerSecond < 0
                || !Finite(wetnessDrainagePerSecond) || wetnessDrainagePerSecond < 0
                || !Finite(standingWaterDrainagePerSecond) || standingWaterDrainagePerSecond < 0
                || !Finite(sunlightDryingPerSecond) || sunlightDryingPerSecond < 0
                || !Finite(windDryingPerSecond) || windDryingPerSecond < 0
                || !Finite(surfaceWetnessThreshold) || surfaceWetnessThreshold < 0 || surfaceWetnessThreshold > 1
                || !Finite(lightningQuietSecondsMin) || lightningQuietSecondsMin < 0
                || !Finite(lightningQuietSecondsMax) || lightningQuietSecondsMax < lightningQuietSecondsMin
                || maximumLightningEvents < 1 || !Finite(soundSpeedMetersPerSecond) || soundSpeedMetersPerSecond <= 0)
            { failure = "Weather climate contains an out-of-range or non-finite value."; return false; }
            if (sunriseHours == sunsetHours) { failure = "Sunrise and sunset must be distinct."; return false; }
            if (!(dawnHours < sunriseHours && sunriseHours < sunsetHours && sunsetHours < duskHours))
            { failure = "Weather day boundaries must be ordered dawn < sunrise < sunset < dusk."; return false; }
            if (transitions != null)
            {
                if (transitions.Length > 256) { failure = "Climate transition rules exceed the bound of 256."; return false; }
                for (int i = 0; i < transitions.Length; i++)
                    if (transitions[i] == null || !Finite(transitions[i].weight) || transitions[i].weight < 0
                        || !Finite(transitions[i].cooldownSeconds) || transitions[i].cooldownSeconds < 0
                        || string.IsNullOrWhiteSpace(transitions[i].fromPresetId)
                        || string.IsNullOrWhiteSpace(transitions[i].toPresetId))
                    { failure = "Climate profile contains an invalid transition rule."; return false; }
            }
            failure = string.Empty; return true;
        }

        public static WeatherClimateProfile CreateRuntimeDefaults()
        {
            var climate = CreateInstance<WeatherClimateProfile>();
            climate.hideFlags = HideFlags.HideAndDontSave;
            climate.transitions = DefaultTransitions();
            return climate;
        }

        public static WeatherTransitionRule[] DefaultTransitions()
        {
            return new[]
            {
                Rule("clear", "partly-cloudy", 5), Rule("clear", "cloudy", 2), Rule("partly-cloudy", "clear", 4),
                Rule("partly-cloudy", "cloudy", 4), Rule("cloudy", "partly-cloudy", 2), Rule("cloudy", "overcast", 4),
                Rule("cloudy", "mist", 1), Rule("overcast", "cloudy", 2), Rule("overcast", "drizzle", 4),
                Rule("overcast", "fog", 1), Rule("drizzle", "overcast", 3), Rule("drizzle", "light-rain", 4),
                Rule("light-rain", "drizzle", 2), Rule("light-rain", "rain", 4), Rule("rain", "light-rain", 2),
                Rule("rain", "heavy-rain", 2), Rule("rain", "overcast", 1), Rule("heavy-rain", "rain", 4),
                Rule("heavy-rain", "storm-rain", 1), Rule("storm-rain", "heavy-rain", 4), Rule("storm-rain", "thunderstorm", 1),
                Rule("thunderstorm", "storm-rain", 3), Rule("thunderstorm", "rain", 2), Rule("thunderstorm", "overcast", 1),
                Rule("mist", "cloudy", 3), Rule("mist", "fog", 2), Rule("fog", "mist", 2), Rule("fog", "overcast", 1),
                Rule("heavy-fog", "fog", 2), Rule("windy", "partly-cloudy", 3), Rule("windy", "strong-wind", 1),
                Rule("strong-wind", "windy", 3), Rule("strong-wind", "storm-rain", 1)
            };
        }

        static WeatherTransitionRule Rule(string from, string to, float weight) => new WeatherTransitionRule { fromPresetId = from, toPresetId = to, weight = weight };
        static Gradient DefaultDaylightColorGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(.18f, .24f, .42f), 0),
                    new GradientColorKey(new Color(1f, .42f, .2f), .12f),
                    new GradientColorKey(Color.white, .5f),
                    new GradientColorKey(new Color(1f, .32f, .16f), .88f),
                    new GradientColorKey(new Color(.18f, .24f, .42f), 1)
                },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            return gradient;
        }
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static float finite(float v) => Finite(v) ? v : 0;
    }
}
