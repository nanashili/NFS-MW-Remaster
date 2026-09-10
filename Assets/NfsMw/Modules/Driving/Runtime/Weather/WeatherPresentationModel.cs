using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Derived particle controls for one presentation frame. These values never feed
    /// back into the deterministic weather simulation.
    /// </summary>
    public struct WeatherPrecipitationPresentation
    {
        public float intensity;
        public float middleEmission;
        public float nearEmission;
        public float farEmission;
        public float impactEmission;
        public float surfaceMistEmission;
        public Vector3 horizontalVelocity;
        public bool collisionEnabled;

        public bool IsEmitting => middleEmission > .001f || nearEmission > .001f || farEmission > .001f;
    }

    /// <summary>Derived shader controls for wet surfaces.</summary>
    public struct WeatherSurfacePresentation
    {
        public float wetness;
        public float standingWater;
        public float rippleStrength;
        public float rainSpotStrength;
    }

    /// <summary>
    /// Rendering-agnostic cloud controls derived from one weather snapshot. The HDRP
    /// adapter translates these values to its Volume component without feeding them
    /// back into the deterministic simulation.
    /// </summary>
    public struct WeatherCloudPresentation
    {
        public bool enabled;
        public bool qualityMode;
        public bool shadows;
        public float coverage;
        public float lowCoverage;
        public float midCoverage;
        public float highCoverage;
        public float convectiveCoverage;
        public float densityMultiplier;
        public float shapeFactor;
        public float shapeScale;
        public float erosionFactor;
        public float erosionScale;
        public float bottomAltitude;
        public float altitudeRange;
        public float powderEffectIntensity;
        public float multiScattering;
        public float ambientLightProbeDimmer;
        public float sunLightDimmer;
        public float windSpeedKph;
        public float windOrientationDegrees;
        public float altitudeDistortion;
        public float temporalAccumulation;
        public float shadowDistance;
        public float shadowOpacity;
        public float opticalDepth;
        public float sunTransmission;
        public float cloudShadowStrength;
        public float stormIntensity;
        public float precipitationPotential;
        public float cloudEvolution;
        public float cloudMapSpeedMultiplier;
        public float shapeSpeedMultiplier;
        public float erosionSpeedMultiplier;
        public float verticalShapeWindSpeed;
        public float verticalErosionWindSpeed;
        public Vector3 shapeOffset;
        public int primarySteps;
        public int lightSteps;
        public int shadowResolution;
    }

    /// <summary>
    /// The pure presentation policy shared by the particle and surface adapters.
    /// Keeping it free of scene objects makes quality and shelter behaviour testable.
    /// </summary>
    public static class WeatherPresentationModel
    {
        // Cloud layers sit much closer to the player than a real weather system's
        // horizon-scale field, so one-to-one atmospheric wind looks accelerated.
        // Keep the simulation wind physical and compress only HDRP's visual drift.
        const float VisualCloudWindScale = .15f;
        const float MaximumVisualCloudWindKph = 12f;

        public static WeatherPrecipitationPresentation Precipitation(
            WeatherSnapshot snapshot,
            WeatherPresentationQuality quality,
            bool sheltered)
        {
            float rain = snapshot.precipitationType == WeatherPrecipitationType.None
                ? 0
                : Mathf.Clamp01(snapshot.precipitationIntensity);
            if (snapshot.precipitationType == WeatherPrecipitationType.Drizzle) rain *= .72f;
            if (snapshot.atmosphere.precipitationPotential > 0)
            {
                // Keep rain onset behind cloud support during a long transition;
                // legacy snapshots with no derived atmosphere retain their authored
                // precipitation intent.
                float support = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.08f, .42f, snapshot.atmosphere.precipitationPotential));
                rain *= support;
            }

            float density = QualityDensity(quality);
            float nearAvailability = quality == WeatherPresentationQuality.VeryLow ? .45f : 1;
            float farAvailability = quality == WeatherPresentationQuality.VeryLow ? 0
                : quality == WeatherPresentationQuality.Low ? .28f
                : quality == WeatherPresentationQuality.Medium ? .58f
                : quality == WeatherPresentationQuality.High ? .82f : 1;
            float impactAvailability = quality == WeatherPresentationQuality.VeryLow ? 0
                : quality == WeatherPresentationQuality.Low ? .2f
                : quality == WeatherPresentationQuality.Medium ? .5f
                : quality == WeatherPresentationQuality.High ? .78f : 1;
            float mistAvailability = quality == WeatherPresentationQuality.VeryLow ? 0
                : quality == WeatherPresentationQuality.Low ? .25f
                : quality == WeatherPresentationQuality.Medium ? .55f
                : quality == WeatherPresentationQuality.High ? .8f : 1;

            Vector3 direction = Quaternion.Euler(0, snapshot.windDirectionDegrees, 0) * Vector3.forward;
            float exposedRain = sheltered ? 0 : rain;
            return new WeatherPrecipitationPresentation
            {
                intensity = rain,
                middleEmission = exposedRain * density,
                nearEmission = exposedRain * density * nearAvailability * Mathf.Lerp(.62f, 1, rain),
                farEmission = exposedRain * density * farAvailability * Mathf.Lerp(.55f, 1, rain),
                impactEmission = exposedRain * impactAvailability
                    * Mathf.Clamp01(.2f + snapshot.surfaceWetness * .5f + snapshot.standingWater * .65f),
                surfaceMistEmission = exposedRain * mistAvailability
                    * Mathf.Clamp01(.12f + snapshot.surfaceWetness * .35f + snapshot.standingWater * .9f),
                horizontalVelocity = direction * Mathf.Max(0, snapshot.windSpeedMps),
                collisionEnabled = !sheltered
                    && rain > .025f
                    && quality >= WeatherPresentationQuality.High
            };
        }

        public static WeatherSurfacePresentation Surface(
            WeatherSnapshot snapshot,
            WeatherPresentationQuality quality)
        {
            float wetness = Mathf.Clamp01(snapshot.surfaceWetness);
            float standingWater = Mathf.Clamp01(snapshot.standingWater);
            float rain = snapshot.precipitationType == WeatherPrecipitationType.None
                ? 0
                : Mathf.Clamp01(snapshot.precipitationIntensity);
            float detail = quality == WeatherPresentationQuality.VeryLow ? 0
                : quality == WeatherPresentationQuality.Low ? .25f
                : quality == WeatherPresentationQuality.Medium ? .55f
                : quality == WeatherPresentationQuality.High ? .8f : 1;
            return new WeatherSurfacePresentation
            {
                wetness = wetness,
                standingWater = standingWater,
                rippleStrength = rain * detail * Mathf.Clamp01(standingWater * 1.25f + wetness * .2f),
                rainSpotStrength = rain * detail * Mathf.Clamp01(.2f + wetness * .8f)
            };
        }

        public static WeatherCloudPresentation Clouds(
            WeatherSnapshot snapshot,
            WeatherPresentationQuality quality)
        {
            WeatherAtmosphericState atmosphere = snapshot.atmosphere;
            float coverage = Mathf.Clamp01(atmosphere.cloudCoverage > 0 ? atmosphere.cloudCoverage : snapshot.cloudCover);
            float overcast = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.35f, .9f, coverage));
            float convective = atmosphere.stormIntensity > 0
                ? Mathf.Clamp01(atmosphere.stormIntensity)
                : Mathf.Clamp01(snapshot.electricalActivity * .95f + snapshot.precipitationIntensity * .35f + snapshot.gustStrength * .2f);
            bool ultra = quality == WeatherPresentationQuality.Ultra;
            float cloudDensity = Mathf.Clamp01(atmosphere.cloudDensity > 0
                ? atmosphere.cloudDensity
                : Mathf.Lerp(.12f, .55f, coverage));
            float transmission = atmosphere.sunTransmission > 0
                ? atmosphere.sunTransmission
                : Mathf.Exp(-cloudDensity * 1.5f);
            float shadowStrength = atmosphere.cloudShadowStrength > 0
                ? atmosphere.cloudShadowStrength
                : Mathf.Clamp01((1 - transmission) * coverage);
            float evolution = Mathf.Repeat(atmosphere.cloudEvolution, 1);
            // A denser, more humid field generally lowers its condensation base.
            // Keep this legacy-snapshot fallback monotonic with the derived model.
            float layerBase = atmosphere.cloudBaseAltitudeMeters > 0 ? atmosphere.cloudBaseAltitudeMeters : Mathf.Lerp(2800, 1200, overcast);
            float layerTop = atmosphere.cloudTopAltitudeMeters > layerBase
                ? atmosphere.cloudTopAltitudeMeters
                : layerBase + Mathf.Lerp(1000, 4500, convective);
            float windSpeedMps = atmosphere.cloudWindSpeedMps > 0 ? atmosphere.cloudWindSpeedMps : snapshot.windSpeedMps;
            float windDirection = atmosphere.cloudWindSpeedMps > 0
                ? atmosphere.cloudWindDirectionDegrees
                : snapshot.windDirectionDegrees;

            // Shape and erosion are independent from coverage: broad overcast uses a
            // connected field, while instability restores sculpted vertical forms.
            float baseShape = Mathf.Lerp(.9f, .52f, coverage);
            float baseErosion = Mathf.Lerp(.9f, .5f, overcast);
            return new WeatherCloudPresentation
            {
                enabled = quality >= WeatherPresentationQuality.High,
                qualityMode = ultra,
                shadows = ultra && shadowStrength > .12f,
                coverage = coverage,
                lowCoverage = atmosphere.lowClouds.coverage > 0 ? atmosphere.lowClouds.coverage : Mathf.Clamp01(coverage * 1.05f),
                midCoverage = atmosphere.midClouds.coverage > 0 ? atmosphere.midClouds.coverage : Mathf.Clamp01(coverage * .7f),
                highCoverage = atmosphere.highClouds.coverage > 0 ? atmosphere.highClouds.coverage : Mathf.Clamp01(coverage * .35f),
                convectiveCoverage = atmosphere.convectiveClouds.coverage > 0 ? atmosphere.convectiveClouds.coverage : Mathf.Clamp01(convective * .8f),
                densityMultiplier = Mathf.Clamp01(Mathf.Lerp(.12f, .72f, cloudDensity)),
                shapeFactor = Mathf.Lerp(baseShape, .84f, convective * .72f),
                shapeScale = Mathf.Lerp(7, 3.5f, convective),
                erosionFactor = Mathf.Lerp(baseErosion, .8f, convective * .65f),
                erosionScale = Mathf.Lerp(120, 64, convective),
                bottomAltitude = layerBase,
                altitudeRange = Mathf.Max(250, layerTop - layerBase),
                powderEffectIntensity = Mathf.Lerp(.16f, .42f, cloudDensity),
                multiScattering = Mathf.Lerp(.62f, .38f, convective),
                ambientLightProbeDimmer = Mathf.Clamp01(Mathf.Lerp(1, .42f, 1 - transmission)),
                sunLightDimmer = Mathf.Clamp01(Mathf.Lerp(.98f, .28f, 1 - transmission)),
                windSpeedKph = Mathf.Clamp(
                    windSpeedMps * 3.6f * VisualCloudWindScale,
                    0,
                    MaximumVisualCloudWindKph),
                // Weather uses +Z as zero degrees; HDRP's cloud animation is based on
                // an orientation around +X and applies the negated direction vector.
                windOrientationDegrees = Mathf.Repeat(270 - windDirection, 360),
                altitudeDistortion = Mathf.Lerp(.08f, .45f, Mathf.Clamp01(atmosphere.instability + snapshot.gustStrength * .35f)),
                temporalAccumulation = ultra ? .96f : .92f,
                shadowDistance = ultra ? 4000 : 2500,
                shadowOpacity = Mathf.Lerp(.04f, .86f, shadowStrength),
                primarySteps = ultra ? 96 : 48,
                lightSteps = ultra ? 8 : 4,
                shadowResolution = ultra ? 256 : 128,
                opticalDepth = atmosphere.cloudOpticalDepth,
                sunTransmission = transmission,
                cloudShadowStrength = shadowStrength,
                stormIntensity = atmosphere.stormIntensity,
                precipitationPotential = atmosphere.precipitationPotential,
                cloudEvolution = evolution,
                cloudMapSpeedMultiplier = Mathf.Lerp(.08f, .28f, Mathf.Clamp01(windSpeedMps / 25f)),
                shapeSpeedMultiplier = Mathf.Lerp(.18f, .42f, Mathf.Clamp01(windSpeedMps / 25f)),
                erosionSpeedMultiplier = Mathf.Lerp(.04f, .14f, Mathf.Clamp01(atmosphere.instability)),
                verticalShapeWindSpeed = Mathf.Lerp(0, .3f, convective),
                verticalErosionWindSpeed = Mathf.Lerp(0, .18f, convective),
                // HDRP already integrates global wind over time. A second horizontal
                // shape offset doubled the drift and jumped whenever evolution wrapped.
                shapeOffset = Vector3.up * (convective * 2f)
            };
        }

        public static float QualityDensity(WeatherPresentationQuality quality)
        {
            switch (quality)
            {
                case WeatherPresentationQuality.VeryLow: return .22f;
                case WeatherPresentationQuality.Low: return .4f;
                case WeatherPresentationQuality.Medium: return .62f;
                case WeatherPresentationQuality.High: return .82f;
                default: return 1;
            }
        }
    }
}
