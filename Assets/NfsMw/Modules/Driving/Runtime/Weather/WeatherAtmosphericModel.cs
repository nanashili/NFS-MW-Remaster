using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Cloud families used by the presentation layer, derived from continuous conditions.</summary>
    public enum WeatherCloudFamily
    {
        Clear,
        Cirrus,
        Cirrostratus,
        Cirrocumulus,
        Altocumulus,
        Altostratus,
        Nimbostratus,
        Stratocumulus,
        Stratus,
        Cumulus,
        ToweringCumulus,
        Cumulonimbus
    }

    [Serializable]
    public struct WeatherCloudLayer
    {
        [Range(0, 1)] public float coverage;
        [Range(0, 1)] public float density;
        [Range(0, 1)] public float opticalDepth;
        public float baseAltitudeMeters;
        public float topAltitudeMeters;
        public float thicknessMeters;
        public float windSpeedMps;
        public float windDirectionDegrees;

        public bool Present => coverage > .001f && density > .001f;
    }

    /// <summary>
    /// Derived atmospheric values. These are recomputed from the authoritative
    /// weather snapshot and simulation time; they are deliberately not persisted.
    /// </summary>
    [Serializable]
    public struct WeatherAtmosphericState
    {
        public float pressureHpa;
        public float dewPointC;
        public float lclAltitudeMeters;
        [Range(0, 1)] public float humidityDeficit;
        [Range(0, 1)] public float instability;
        [Range(0, 1)] public float stormIntensity;
        [Range(0, 1)] public float precipitationPotential;
        [Range(0, 1)] public float precipitationRate;
        [Range(0, 1)] public float cloudCoverage;
        [Range(0, 1)] public float cloudDensity;
        [Range(0, 1)] public float cloudOpticalDepth;
        [Range(0, 1)] public float sunTransmission;
        [Range(0, 1)] public float cloudShadowStrength;
        [Range(0, 1)] public float hazeDensity;
        public float cloudBaseAltitudeMeters;
        public float cloudTopAltitudeMeters;
        public float cloudThicknessMeters;
        public float cloudEvolution;
        public float cloudWindSpeedMps;
        public float cloudWindDirectionDegrees;
        public WeatherCloudFamily primaryFamily;
        public WeatherCloudLayer lowClouds;
        public WeatherCloudLayer midClouds;
        public WeatherCloudLayer highClouds;
        public WeatherCloudLayer convectiveClouds;

        public bool HasDeepConvection => convectiveClouds.Present && instability > .45f;
    }

    /// <summary>
    /// Small, bounded approximations of observed cloud relationships. It does not
    /// attempt fluid dynamics: moisture and lift form layers, optical depth drives
    /// transmission, and wind supplies a coherent translation/evolution signal.
    /// </summary>
    public static class WeatherAtmosphericModel
    {
        public const float DefaultPressureHpa = 1013.25f;

        public static WeatherAtmosphericState Derive(
            WeatherSnapshotValues values,
            WeatherClimateProfile climate,
            float simulationSeconds,
            int seed)
        {
            float temperature = Mathf.Clamp(values.temperatureC, -80, 60);
            float humidity = Mathf.Clamp01(values.humidity);
            float cloudCover = Mathf.Clamp01(values.cloudCover);
            float rain = Mathf.Clamp01(values.precipitationIntensity);
            float gust = Mathf.Clamp01(values.gustStrength);
            float electrical = Mathf.Clamp01(values.electricalActivity);
            float windSpeed = Mathf.Max(0, values.windSpeedMps);
            float moisture = Mathf.Clamp01(Mathf.Lerp(humidity, climate == null ? humidity : climate.atmosphericMoisture, .25f));
            float dewPoint = DewPointC(temperature, humidity);
            float lcl = Mathf.Clamp(125f * Mathf.Max(0, temperature - dewPoint), 150, 4000);
            float humidityDeficit = Mathf.Clamp01(1 - humidity);

            // Instability is a bounded gameplay proxy for moisture plus lift and
            // shear. Thunder is only reachable when lift and deep moisture coexist.
            float lift = Mathf.Clamp01(cloudCover * .25f + rain * .35f + gust * .15f + electrical * .55f);
            float instability = Mathf.Clamp01(
                moisture * .18f + rain * .28f + gust * .18f + electrical * .34f + lift * .18f);
            float storm = Mathf.Clamp01(Mathf.Max(
                electrical * .95f,
                moisture * (rain * .62f + instability * .52f + lift * .18f)));

            float lowCoverage = Mathf.Clamp01(cloudCover * (.42f + moisture * .48f) + rain * .28f + storm * .08f);
            float midCoverage = Mathf.Clamp01(cloudCover * (.18f + moisture * .48f) + rain * .42f + storm * .22f);
            float highCoverage = Mathf.Clamp01(cloudCover * .1f + storm * .38f + moisture * .05f);
            float convectiveCoverage = Mathf.Clamp01(
                (cloudCover * .22f + rain * .58f + instability * .58f) * (.4f + moisture * .6f));

            float lowDensity = Mathf.Clamp01(.12f + moisture * .24f + cloudCover * .44f + rain * .22f);
            float midDensity = Mathf.Clamp01(.08f + moisture * .22f + cloudCover * .3f + rain * .36f + storm * .12f);
            float highDensity = Mathf.Clamp01(.06f + cloudCover * .16f + storm * .18f);
            float convectiveDensity = Mathf.Clamp01(.16f + moisture * .25f + instability * .32f + storm * .42f);

            float upperWind = windSpeed * (1.1f + instability * .18f);
            float upperDirection = Mathf.Repeat(values.windDirectionDegrees + 8f + storm * 12f, 360);
            float highWind = windSpeed * (1.22f + instability * .2f);
            float highDirection = Mathf.Repeat(values.windDirectionDegrees + 16f + storm * 18f, 360);
            float convectiveWind = windSpeed * (1.04f + gust * .12f);

            WeatherCloudLayer low = Layer(lowCoverage, lowDensity, lcl, lcl + Mathf.Lerp(650, 1800, moisture), windSpeed, values.windDirectionDegrees);
            WeatherCloudLayer mid = Layer(midCoverage, midDensity, Mathf.Max(1700, lcl + 550),
                Mathf.Max(2600, lcl + 550) + Mathf.Lerp(1100, 2800, storm), upperWind, upperDirection);
            WeatherCloudLayer high = Layer(highCoverage, highDensity, 6000, 6000 + Mathf.Lerp(2500, 5000, storm), highWind, highDirection);
            WeatherCloudLayer convective = Layer(convectiveCoverage, convectiveDensity, Mathf.Max(700, lcl * .82f),
                Mathf.Max(2500, lcl * .82f + Mathf.Lerp(2200, 8500, Mathf.Clamp01(instability + storm * .35f))),
                convectiveWind, values.windDirectionDegrees);

            float opticalDepth = Mathf.Clamp01(
                low.opticalDepth + mid.opticalDepth + high.opticalDepth + convective.opticalDepth);
            float evolutionRate = .0008f + windSpeed * .000045f + instability * .00035f;
            float phase = Mathf.Repeat(simulationSeconds * evolutionRate + (seed & 2047) * .00037f, 1);
            float wave = .5f + .5f * Mathf.Sin(phase * Mathf.PI * 2);
            float coverageVariation = Mathf.Lerp(.035f, .2f, 1 - cloudCover);
            float fieldFactor = Mathf.Clamp(1 + (wave - .5f) * 2 * coverageVariation, .75f, 1.25f);
            // Beer-Lambert-like extinction: thin veils retain most direct light,
            // while a saturated deep storm can remove nearly all direct sun.
            float sunTransmission = Mathf.Clamp01(Mathf.Exp(-opticalDepth * fieldFactor * 2.6f));
            float shadowStrength = Mathf.Clamp01((1 - sunTransmission) * (.5f + cloudCover * .5f));
            float precipitationPotential = Mathf.Clamp01(
                moisture * (lowCoverage * .32f + midCoverage * .48f + convectiveCoverage * .9f)
                    * (.35f + instability * .65f) + rain * .35f);
            float precipitationRate = Mathf.Clamp01(rain * (.55f + precipitationPotential * .45f));
            float baseAltitude = Mathf.Min(low.Present ? low.baseAltitudeMeters : 100000,
                convective.Present ? convective.baseAltitudeMeters : 100000);
            if (baseAltitude >= 100000) baseAltitude = Mathf.Min(mid.baseAltitudeMeters, high.baseAltitudeMeters);
            float topAltitude = Mathf.Max(low.topAltitudeMeters, Mathf.Max(mid.topAltitudeMeters,
                Mathf.Max(high.topAltitudeMeters, convective.Present ? convective.topAltitudeMeters : 0)));
            float pressure = values.pressureHpa > 0 ? values.pressureHpa : DefaultPressureHpa;
            pressure = Mathf.Clamp(pressure, 870, 1085);

            return new WeatherAtmosphericState
            {
                pressureHpa = pressure,
                dewPointC = dewPoint,
                lclAltitudeMeters = lcl,
                humidityDeficit = humidityDeficit,
                instability = instability,
                stormIntensity = storm,
                precipitationPotential = precipitationPotential,
                precipitationRate = precipitationRate,
                cloudCoverage = cloudCover,
                cloudDensity = Mathf.Clamp01(opticalDepth / 1.35f),
                cloudOpticalDepth = opticalDepth,
                sunTransmission = sunTransmission,
                cloudShadowStrength = shadowStrength,
                hazeDensity = Mathf.Clamp01(Mathf.Max(values.fogDensity, humidity * .12f + rain * .28f + storm * .16f)),
                cloudBaseAltitudeMeters = baseAltitude,
                cloudTopAltitudeMeters = topAltitude,
                cloudThicknessMeters = Mathf.Max(0, topAltitude - baseAltitude),
                cloudEvolution = phase,
                cloudWindSpeedMps = windSpeed,
                cloudWindDirectionDegrees = Mathf.Repeat(values.windDirectionDegrees, 360),
                primaryFamily = Family(cloudCover, rain, storm, lowCoverage, highCoverage, convectiveCoverage),
                lowClouds = low,
                midClouds = mid,
                highClouds = high,
                convectiveClouds = convective
            };
        }

        public static float DewPointC(float temperatureC, float relativeHumidity)
        {
            float temperature = Mathf.Clamp(temperatureC, -80, 60);
            float humidity = Mathf.Clamp(relativeHumidity, .01f, 1);
            const float a = 17.625f;
            const float b = 243.04f;
            float gamma = Mathf.Log(humidity) + a * temperature / (b + temperature);
            return Mathf.Clamp(b * gamma / (a - gamma), -80, 60);
        }

        static WeatherCloudLayer Layer(float coverage, float density, float bottom, float top, float windSpeed, float direction)
        {
            float depth = Mathf.Max(0, top - bottom);
            float optical = Mathf.Clamp01(coverage * density * Mathf.Clamp01(depth / 2500f));
            return new WeatherCloudLayer
            {
                coverage = coverage,
                density = density,
                opticalDepth = optical,
                baseAltitudeMeters = bottom,
                topAltitudeMeters = top,
                thicknessMeters = depth,
                windSpeedMps = Mathf.Max(0, windSpeed),
                windDirectionDegrees = Mathf.Repeat(direction, 360)
            };
        }

        static WeatherCloudFamily Family(float cover, float rain, float storm, float low, float high, float convective)
        {
            if (cover <= .08f) return WeatherCloudFamily.Clear;
            if (storm > .68f && convective > .45f) return WeatherCloudFamily.Cumulonimbus;
            if (convective > .48f) return WeatherCloudFamily.ToweringCumulus;
            if (rain > .18f && low > .5f && cover > .7f) return WeatherCloudFamily.Nimbostratus;
            if (low > .72f && rain < .08f) return WeatherCloudFamily.Stratus;
            if (low > .48f && high < .25f) return WeatherCloudFamily.Stratocumulus;
            if (high > .45f && low < .25f) return WeatherCloudFamily.Cirrostratus;
            if (cover < .25f) return WeatherCloudFamily.Cirrus;
            if (rain > .05f) return WeatherCloudFamily.Altostratus;
            if (cover < .5f) return WeatherCloudFamily.Cumulus;
            return WeatherCloudFamily.Altocumulus;
        }
    }
}
