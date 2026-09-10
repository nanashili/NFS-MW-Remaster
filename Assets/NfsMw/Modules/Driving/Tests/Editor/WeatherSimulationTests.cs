using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using NfsMwRemaster.Driving;
using NfsMwRemaster.Driving.Editor.Weather;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class WeatherSimulationTests
    {
        WeatherClimateProfile climate;
        WeatherPresetCatalog catalog;
        readonly List<WeatherSimulation> simulations = new List<WeatherSimulation>();

        [SetUp]
        public void SetUp()
        {
            climate = WeatherClimateProfile.CreateRuntimeDefaults();
            climate.fixedStepSeconds = .25f;
            climate.dayLengthRealSeconds = 120;
            climate.minimumPersistenceSeconds = 1;
            climate.maximumPersistenceSeconds = 30;
            climate.lightningQuietSecondsMin = .5f;
            climate.lightningQuietSecondsMax = 1.5f;
            catalog = WeatherPresetCatalog.CreateRuntimeDefaults();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < simulations.Count; i++) simulations[i].Dispose();
            simulations.Clear();
            if (climate) UnityEngine.Object.DestroyImmediate(climate);
            if (catalog) UnityEngine.Object.DestroyImmediate(catalog);
        }

        [Test]
        public void FixedTicksAreStableAcrossRenderFrameRates()
        {
            WeatherSimulation thirty = NewSimulation(1234);
            WeatherSimulation sixty = NewSimulation(1234);
            WeatherSimulation oneFortyFour = NewSimulation(1234);
            for (int i = 0; i < 30 * 20; i++) thirty.Advance(1f / 30f);
            for (int i = 0; i < 60 * 20; i++) sixty.Advance(1f / 60f);
            for (int i = 0; i < 144 * 20; i++) oneFortyFour.Advance(1f / 144f);

            Assert.That(thirty.SimulationRandomState, Is.EqualTo(sixty.SimulationRandomState));
            Assert.That(sixty.SimulationRandomState, Is.EqualTo(oneFortyFour.SimulationRandomState));
            Assert.That(thirty.Snapshot.timeOfDaySeconds, Is.EqualTo(sixty.Snapshot.timeOfDaySeconds).Within(.01f));
            Assert.That(sixty.Snapshot.surfaceWetness, Is.EqualTo(oneFortyFour.Snapshot.surfaceWetness).Within(.01f));
            Assert.That(thirty.Snapshot.currentPresetId, Is.EqualTo(oneFortyFour.Snapshot.currentPresetId));
        }

        [Test]
        public void CosmeticRandomAndPresentationQualityCannotChangeForecast()
        {
            WeatherSimulation baseline = NewSimulation(77);
            WeatherSimulation cosmetic = NewSimulation(77);
            for (int i = 0; i < 240; i++)
            {
                baseline.Advance(.25f);
                cosmetic.NextCosmeticRandom();
                cosmetic.NextCosmeticRandom();
                cosmetic.Advance(.25f);
            }

            Assert.That(cosmetic.SimulationRandomState, Is.EqualTo(baseline.SimulationRandomState));
            Assert.That(cosmetic.Snapshot.currentPresetId, Is.EqualTo(baseline.Snapshot.currentPresetId));
            Assert.That(cosmetic.Snapshot.surfaceWetness, Is.EqualTo(baseline.Snapshot.surfaceWetness).Within(.0001f));
        }

        [Test]
        public void PauseAndIndependentClockWeatherRatesAreExplicit()
        {
            WeatherSimulation simulation = NewSimulation(9);
            simulation.SetPaused(true);
            simulation.Advance(10);
            Assert.That(simulation.Snapshot.simulationSeconds, Is.EqualTo(0).Within(.0001f));
            simulation.SetPaused(false);
            simulation.SetTimeScale(2);
            simulation.SetWeatherSpeed(1);
            simulation.Advance(1);
            Assert.That(simulation.Snapshot.simulationSeconds, Is.EqualTo(1).Within(.01f));
            Assert.That(simulation.Snapshot.timeOfDaySeconds, Is.GreaterThan(0));
        }

        [Test]
        public void InterruptedTransitionRoundTripsAndMissingPresetFailsClosed()
        {
            WeatherSimulation source = NewSimulation(42);
            Assert.That(source.TransitionToPreset("heavy-rain", false, out string startFailure), Is.True, startFailure);
            source.Advance(7);
            CareerWeatherData saved = source.CaptureState();
            WeatherSimulation restored = NewSimulation(999);
            Assert.That(restored.RestoreState(saved, out string restoreFailure), Is.True, restoreFailure);
            source.Advance(20);
            restored.Advance(20);
            Assert.That(restored.Snapshot.currentPresetId, Is.EqualTo(source.Snapshot.currentPresetId));
            Assert.That(restored.Snapshot.transitionProgress, Is.EqualTo(source.Snapshot.transitionProgress).Within(.0001f));
            Assert.That(restored.Snapshot.surfaceWetness, Is.EqualTo(source.Snapshot.surfaceWetness).Within(.0001f));

            saved.currentPresetId = "removed-preset";
            Assert.That(restored.RestoreState(saved, out string failure), Is.False);
            StringAssert.Contains("missing", failure.ToLowerInvariant());
        }

        [Test]
        public void WetnessPersistsDrainsAndRespectsLocalExposure()
        {
            WeatherSimulation simulation = NewSimulation(4);
            Assert.That(simulation.TransitionToPreset("heavy-rain", true, out _), Is.True);
            simulation.Advance(8);
            float exposed = simulation.EvaluateLocalWetness(1);
            float sheltered = simulation.EvaluateLocalWetness(0);
            float wellDrained = simulation.EvaluateLocalWetness(1, 2);
            Assert.That(exposed, Is.GreaterThan(sheltered));
            Assert.That(wellDrained, Is.LessThan(exposed));
            Assert.That(exposed, Is.InRange(0, 1));
            Assert.That(simulation.TransitionToPreset("clear", true, out _), Is.True);
            simulation.Advance(1);
            Assert.That(simulation.Snapshot.surfaceWetness, Is.GreaterThan(0));
            simulation.Advance(300);
            Assert.That(simulation.Snapshot.surfaceWetness, Is.LessThan(exposed));
            Assert.That(simulation.Snapshot.standingWater, Is.InRange(0, 1));
        }

        [Test]
        public void LightningRequiresElectricalActivityAndQueueIsBounded()
        {
            WeatherSimulation rain = NewSimulation(5);
            Assert.That(rain.TransitionToPreset("storm-rain", true, out _), Is.True);
            rain.Advance(20);
            Assert.That(rain.PendingLightningCount, Is.EqualTo(0));

            WeatherSimulation thunder = NewSimulation(5);
            Assert.That(thunder.TransitionToPreset("thunderstorm", true, out _), Is.True);
            thunder.Advance(2);
            Assert.That(thunder.PendingLightningCount, Is.GreaterThan(0));
            Assert.That(thunder.PendingLightningCount, Is.LessThanOrEqualTo(8));
        }

        [Test]
        public void ClockEventsCrossCustomBoundariesAndCatchUpIsBounded()
        {
            climate.dawnHours = 4;
            climate.sunriseHours = 5;
            climate.sunsetHours = 19;
            climate.duskHours = 20;
            WeatherSimulation simulation = NewSimulation(1);
            int events = 0;
            simulation.DayPhaseChanged += _ => events++;
            Assert.That(simulation.SetTimeOfDayHours(3.5f), Is.True);
            Assert.That(simulation.SetTimeOfDayHours(20.5f), Is.True);
            Assert.That(events, Is.EqualTo(9));

            simulation.Advance(1000);
            Assert.That(simulation.LastAdvanceWasClamped, Is.True);
            Assert.That(simulation.DroppedCatchUpSeconds, Is.GreaterThan(0));
            Assert.That(simulation.Snapshot.surfaceWetness, Is.InRange(0, 1));
        }

        [Test]
        public void WeatherPayloadPassesCareerSaveValidation()
        {
            WeatherSimulation simulation = NewSimulation(11);
            simulation.TransitionToPreset("thunderstorm", true, out _);
            simulation.Advance(3);
            CareerProfileData profile = CareerProfileData.Create("weather-test", "Test");
            profile.weather = simulation.CaptureState();
            string json = JsonUtility.ToJson(profile);
            Assert.That(CareerSaveCodec.Validate("weather-test", json), Is.EqualTo(CareerProfileData.CurrentVersion));
        }

        [Test]
        public void SkipTimeUsesAuthoredClockHoursAtAnyClockScale()
        {
            WeatherSimulation simulation = NewSimulation(13);
            simulation.SetTimeOfDayHours(8);
            simulation.SetTimeScale(2);
            Assert.That(simulation.SkipTimeHours(1), Is.True);
            Assert.That(simulation.Snapshot.timeOfDayHours, Is.EqualTo(9).Within(.01f));
            float simulationSeconds = simulation.Snapshot.simulationSeconds;
            Assert.That(simulation.SkipTimeHours(-2), Is.True);
            Assert.That(simulation.Snapshot.timeOfDayHours, Is.EqualTo(7).Within(.01f));
            Assert.That(simulation.Snapshot.simulationSeconds, Is.EqualTo(simulationSeconds).Within(.01f));
        }

        [Test]
        public void ImmediateNonElectricalWeatherClearsQueuedLightning()
        {
            WeatherSimulation simulation = NewSimulation(14);
            Assert.That(simulation.TransitionToPreset("thunderstorm", true, out _), Is.True);
            simulation.Advance(2);
            Assert.That(simulation.PendingLightningCount, Is.GreaterThan(0));
            Assert.That(simulation.TransitionToPreset("storm-rain", true, out _), Is.True);
            Assert.That(simulation.PendingLightningCount, Is.EqualTo(0));
            simulation.Advance(20);
            Assert.That(simulation.PendingLightningCount, Is.EqualTo(0));
        }

        [Test]
        public void InvalidClimateIsReportedWithoutMutatingAuthoredAsset()
        {
            climate.fixedStepSeconds = 0;
            Assert.That(climate.Validate(out string failure), Is.False);
            StringAssert.Contains("out-of-range", failure);
            Assert.That(climate.fixedStepSeconds, Is.EqualTo(0));
            WeatherSimulation simulation = NewSimulation(15);
            StringAssert.Contains("out-of-range", simulation.LastConfigurationError);
        }

        [Test]
        public void RestoreDropsPendingLightningForNonElectricalState()
        {
            WeatherSimulation source = NewSimulation(16);
            source.TransitionToPreset("thunderstorm", true, out _);
            source.Advance(2);
            CareerWeatherData state = source.CaptureState();
            state.currentValues.electricalActivity = 0;
            WeatherSimulation restored = NewSimulation(17);
            Assert.That(restored.RestoreState(state, out string failure), Is.True, failure);
            Assert.That(restored.PendingLightningCount, Is.EqualTo(0));
        }

        [Test]
        public void RestoreDropsElapsedLightningEntries()
        {
            WeatherSimulation source = NewSimulation(17);
            source.TransitionToPreset("thunderstorm", true, out _);
            source.Advance(2);
            CareerWeatherData state = source.CaptureState();
            Assert.That(state.pendingLightning.Length, Is.GreaterThan(0));
            state.pendingLightning[0].scheduledSimulationSeconds = 0;
            WeatherSimulation restored = NewSimulation(18);
            Assert.That(restored.RestoreState(state, out string failure), Is.True, failure);
            Assert.That(restored.PendingLightningCount, Is.LessThan(state.pendingLightning.Length));
        }

        [Test]
        public void TransitionRuleCooldownPreventsImmediateOscillation()
        {
            catalog.Find("clear").minimumDurationSeconds = 0;
            catalog.Find("clear").maximumDurationSeconds = 0;
            catalog.Find("clear").transitionSeconds = .01f;
            catalog.Find("partly-cloudy").minimumDurationSeconds = 0;
            catalog.Find("partly-cloudy").maximumDurationSeconds = 0;
            catalog.Find("partly-cloudy").transitionSeconds = .01f;
            climate.minimumPersistenceSeconds = 0;
            climate.maximumPersistenceSeconds = 0;
            climate.transitions = new[]
            {
                new WeatherTransitionRule { fromPresetId = "clear", toPresetId = "partly-cloudy", weight = 1, cooldownSeconds = 1000 },
                new WeatherTransitionRule { fromPresetId = "partly-cloudy", toPresetId = "clear", weight = 1 }
            };
            WeatherSimulation simulation = NewSimulation(18);
            simulation.Advance(2);
            Assert.That(simulation.Snapshot.currentPresetId, Is.EqualTo("clear"));
            simulation.Advance(1);
            Assert.That(simulation.Snapshot.currentPresetId, Is.EqualTo("clear"));
        }

        [Test]
        public void SaveLoadPreservesTransitionCooldowns()
        {
            climate.minimumPersistenceSeconds = 0;
            climate.maximumPersistenceSeconds = 0;
            catalog.Find("clear").minimumDurationSeconds = 0;
            catalog.Find("clear").maximumDurationSeconds = 0;
            catalog.Find("clear").transitionSeconds = .01f;
            climate.transitions = new[]
            {
                new WeatherTransitionRule { fromPresetId = "clear", toPresetId = "partly-cloudy", weight = 1 },
                new WeatherTransitionRule { fromPresetId = "clear", toPresetId = "cloudy", weight = 1 }
            };
            WeatherSimulation source = NewSimulation(19);
            CareerWeatherData state = source.CaptureState();
            state.settledRemaining = 0;
            state.transitionCooldowns = new float[climate.transitions.Length];
            for (int i = 0; i < state.transitionCooldowns.Length; i++) state.transitionCooldowns[i] = 1000;
            WeatherSimulation restored = NewSimulation(20);
            Assert.That(restored.RestoreState(state, out string failure), Is.True, failure);
            restored.Advance(1);
            Assert.That(restored.Snapshot.currentPresetId, Is.EqualTo("clear"));
            Assert.That(restored.Snapshot.targetPresetId, Is.EqualTo("clear"));
        }

        [Test]
        public void DynamicWorldCleansUpRuntimeFallbackAssets()
        {
            int climatesBefore = Resources.FindObjectsOfTypeAll<WeatherClimateProfile>().Length;
            int catalogsBefore = Resources.FindObjectsOfTypeAll<WeatherPresetCatalog>().Length;
            GameObject root = new GameObject("weather-lifecycle-test");
            root.AddComponent<DynamicWeatherWorld>();
            UnityEngine.Object.DestroyImmediate(root);
            Assert.That(Resources.FindObjectsOfTypeAll<WeatherClimateProfile>().Length, Is.EqualTo(climatesBefore));
            Assert.That(Resources.FindObjectsOfTypeAll<WeatherPresetCatalog>().Length, Is.EqualTo(catalogsBefore));
        }

        [Test]
        public void PrecipitationPresentationSeparatesQualityShelterAndWeatherIntent()
        {
            var snapshot = new WeatherSnapshot
            {
                precipitationType = WeatherPrecipitationType.Rain,
                precipitationIntensity = 1,
                windDirectionDegrees = 90,
                windSpeedMps = 12,
                surfaceWetness = .7f,
                standingWater = .5f
            };

            WeatherPrecipitationPresentation veryLow = WeatherPresentationModel.Precipitation(
                snapshot,
                WeatherPresentationQuality.VeryLow,
                false);
            WeatherPrecipitationPresentation ultra = WeatherPresentationModel.Precipitation(
                snapshot,
                WeatherPresentationQuality.Ultra,
                false);
            WeatherPrecipitationPresentation sheltered = WeatherPresentationModel.Precipitation(
                snapshot,
                WeatherPresentationQuality.Ultra,
                true);

            Assert.That(ultra.middleEmission, Is.GreaterThan(veryLow.middleEmission));
            Assert.That(veryLow.farEmission, Is.EqualTo(0));
            Assert.That(veryLow.collisionEnabled, Is.False);
            Assert.That(ultra.collisionEnabled, Is.True);
            Assert.That(ultra.impactEmission, Is.GreaterThan(0));
            Assert.That(ultra.horizontalVelocity.magnitude, Is.EqualTo(12).Within(.001f));
            Assert.That(sheltered.IsEmitting, Is.False);
            Assert.That(sheltered.impactEmission, Is.EqualTo(0));
            Assert.That(sheltered.intensity, Is.EqualTo(1));

            snapshot.precipitationType = WeatherPrecipitationType.None;
            Assert.That(
                WeatherPresentationModel.Precipitation(snapshot, WeatherPresentationQuality.Ultra, false).IsEmitting,
                Is.False);
        }

        [Test]
        public void SurfacePresentationPreservesWetnessAndPuddlesAcrossQualityTiers()
        {
            var snapshot = new WeatherSnapshot
            {
                precipitationType = WeatherPrecipitationType.Rain,
                precipitationIntensity = .8f,
                surfaceWetness = .72f,
                standingWater = .43f
            };

            WeatherSurfacePresentation low = WeatherPresentationModel.Surface(
                snapshot,
                WeatherPresentationQuality.Low);
            WeatherSurfacePresentation ultra = WeatherPresentationModel.Surface(
                snapshot,
                WeatherPresentationQuality.Ultra);

            Assert.That(low.wetness, Is.EqualTo(snapshot.surfaceWetness));
            Assert.That(low.standingWater, Is.EqualTo(snapshot.standingWater));
            Assert.That(ultra.wetness, Is.EqualTo(low.wetness));
            Assert.That(ultra.standingWater, Is.EqualTo(low.standingWater));
            Assert.That(ultra.rippleStrength, Is.GreaterThan(low.rippleStrength));
            Assert.That(ultra.rainSpotStrength, Is.GreaterThan(low.rainSpotStrength));
        }

        [Test]
        public void VolumetricCloudPolicyUsesOnlySupportedPresentationTiers()
        {
            var snapshot = new WeatherSnapshot
            {
                cloudCover = .74f,
                precipitationType = WeatherPrecipitationType.Rain,
                precipitationIntensity = .65f,
                gustStrength = .4f,
                electricalActivity = .2f
            };

            Assert.That(WeatherPresentationModel.Clouds(snapshot, WeatherPresentationQuality.VeryLow).enabled, Is.False);
            Assert.That(WeatherPresentationModel.Clouds(snapshot, WeatherPresentationQuality.Low).enabled, Is.False);
            Assert.That(WeatherPresentationModel.Clouds(snapshot, WeatherPresentationQuality.Medium).enabled, Is.False);

            WeatherCloudPresentation high = WeatherPresentationModel.Clouds(snapshot, WeatherPresentationQuality.High);
            WeatherCloudPresentation ultra = WeatherPresentationModel.Clouds(snapshot, WeatherPresentationQuality.Ultra);
            Assert.That(high.enabled, Is.True);
            Assert.That(high.qualityMode, Is.False);
            Assert.That(high.shadows, Is.False);
            Assert.That(high.primarySteps, Is.EqualTo(48));
            Assert.That(high.shadowResolution, Is.EqualTo(128));
            Assert.That(ultra.enabled, Is.True);
            Assert.That(ultra.qualityMode, Is.True);
            Assert.That(ultra.shadows, Is.True);
            Assert.That(ultra.primarySteps, Is.EqualTo(96));
            Assert.That(ultra.shadowResolution, Is.EqualTo(256));
        }

        [Test]
        public void VolumetricCloudWeatherMappingIsContinuousAndPreservesWind()
        {
            var lighter = new WeatherSnapshot
            {
                cloudCover = .45f,
                precipitationType = WeatherPrecipitationType.Rain,
                precipitationIntensity = .35f,
                gustStrength = .2f,
                electricalActivity = .1f,
                windSpeedMps = 10,
                windDirectionDegrees = 90
            };
            WeatherSnapshot heavier = lighter;
            heavier.cloudCover = .55f;
            heavier.precipitationIntensity = .45f;
            heavier.gustStrength = .3f;
            heavier.electricalActivity = .2f;

            WeatherCloudPresentation a = WeatherPresentationModel.Clouds(lighter, WeatherPresentationQuality.High);
            WeatherCloudPresentation b = WeatherPresentationModel.Clouds(heavier, WeatherPresentationQuality.High);
            Assert.That(b.densityMultiplier, Is.GreaterThan(a.densityMultiplier));
            Assert.That(Mathf.Abs(b.densityMultiplier - a.densityMultiplier), Is.LessThan(.1f));
            Assert.That(b.bottomAltitude, Is.LessThan(a.bottomAltitude));
            Assert.That(b.sunLightDimmer, Is.LessThan(a.sunLightDimmer));
            Assert.That(a.windSpeedKph, Is.EqualTo(5.4f).Within(.001f));
            Assert.That(a.windOrientationDegrees, Is.EqualTo(180).Within(.001f));
        }

        [Test]
        public void VolumetricCloudMotionRemainsDeliberateAtStormWindSpeeds()
        {
            var thunderstorm = new WeatherSnapshot
            {
                cloudCover = 1,
                windSpeedMps = 18,
                windDirectionDegrees = 210,
                gustStrength = .72f,
                electricalActivity = .9f,
                precipitationType = WeatherPrecipitationType.Rain,
                precipitationIntensity = .9f
            };

            WeatherCloudPresentation clouds = WeatherPresentationModel.Clouds(
                thunderstorm,
                WeatherPresentationQuality.Ultra);

            Assert.That(clouds.windSpeedKph, Is.LessThanOrEqualTo(12));
            Assert.That(clouds.cloudMapSpeedMultiplier, Is.LessThanOrEqualTo(.35f));
            Assert.That(clouds.shapeSpeedMultiplier, Is.LessThanOrEqualTo(.55f));
            Assert.That(clouds.erosionSpeedMultiplier, Is.LessThanOrEqualTo(.18f));
            Assert.That(clouds.verticalShapeWindSpeed, Is.LessThanOrEqualTo(.35f));
            Assert.That(clouds.verticalErosionWindSpeed, Is.LessThanOrEqualTo(.2f));
            Assert.That(new Vector2(clouds.shapeOffset.x, clouds.shapeOffset.z).magnitude, Is.Zero.Within(.001f));
        }

        [Test]
        public void AtmosphericModelDerivesDewPointAndLclFromMoisture()
        {
            WeatherSnapshotValues values = new WeatherSnapshotValues
            {
                temperatureC = 20,
                pressureHpa = WeatherAtmosphericModel.DefaultPressureHpa,
                humidity = .5f,
                cloudCover = .4f,
                windSpeedMps = 3,
                windDirectionDegrees = 90
            };
            WeatherAtmosphericState atmosphere = WeatherAtmosphericModel.Derive(values, climate, 0, 7);

            Assert.That(atmosphere.dewPointC, Is.EqualTo(9.26f).Within(.2f));
            Assert.That(atmosphere.lclAltitudeMeters, Is.EqualTo(1342).Within(35));
            Assert.That(atmosphere.pressureHpa, Is.EqualTo(WeatherAtmosphericModel.DefaultPressureHpa).Within(.001f));
            Assert.That(atmosphere.humidityDeficit, Is.EqualTo(.5f).Within(.001f));
        }

        [Test]
        public void AtmosphericModelSeparatesCoverageFromOpticalDepth()
        {
            WeatherSnapshotValues thin = new WeatherSnapshotValues
            {
                temperatureC = 18, humidity = .58f, cloudCover = .9f,
                pressureHpa = WeatherAtmosphericModel.DefaultPressureHpa,
                windSpeedMps = 5, windDirectionDegrees = 270
            };
            WeatherAtmosphericState state = WeatherAtmosphericModel.Derive(thin, climate, 0, 8);

            Assert.That(state.cloudCoverage, Is.EqualTo(.9f).Within(.001f));
            Assert.That(state.cloudOpticalDepth, Is.GreaterThan(0));
            Assert.That(state.cloudOpticalDepth, Is.LessThan(1));
            Assert.That(state.cloudCoverage, Is.Not.EqualTo(state.cloudOpticalDepth));
            Assert.That(state.sunTransmission, Is.InRange(0, 1));
            Assert.That(state.lowClouds.coverage, Is.Not.EqualTo(state.midClouds.coverage));
        }

        [Test]
        public void AtmosphericModelBuildsDeepStormsAndLinksRainToCloudSupport()
        {
            WeatherSnapshotValues clear = new WeatherSnapshotValues
            {
                temperatureC = 22, pressureHpa = WeatherAtmosphericModel.DefaultPressureHpa,
                humidity = .42f, cloudCover = .08f, windSpeedMps = 2, windDirectionDegrees = 270
            };
            WeatherSnapshotValues storm = clear;
            storm.temperatureC = 24;
            storm.humidity = .99f;
            storm.cloudCover = 1;
            storm.precipitationType = WeatherPrecipitationType.Rain;
            storm.precipitationIntensity = .95f;
            storm.windSpeedMps = 18;
            storm.gustStrength = .72f;
            storm.electricalActivity = .9f;

            WeatherAtmosphericState fair = WeatherAtmosphericModel.Derive(clear, climate, 0, 9);
            WeatherAtmosphericState severe = WeatherAtmosphericModel.Derive(storm, climate, 0, 9);
            Assert.That(severe.convectiveClouds.topAltitudeMeters, Is.GreaterThan(fair.cloudTopAltitudeMeters));
            Assert.That(severe.stormIntensity, Is.GreaterThan(fair.stormIntensity));
            Assert.That(severe.precipitationPotential, Is.GreaterThan(fair.precipitationPotential));
            Assert.That(severe.cloudOpticalDepth, Is.GreaterThan(fair.cloudOpticalDepth));
            Assert.That(severe.sunTransmission, Is.LessThan(fair.sunTransmission));
            Assert.That(severe.primaryFamily, Is.EqualTo(WeatherCloudFamily.Cumulonimbus));
        }

        [Test]
        public void AtmosphericCloudEvolutionUsesWindWithoutResettingTheField()
        {
            WeatherSnapshotValues values = new WeatherSnapshotValues
            {
                temperatureC = 19, pressureHpa = WeatherAtmosphericModel.DefaultPressureHpa,
                humidity = .76f, cloudCover = .65f, windSpeedMps = 12, windDirectionDegrees = 45,
                precipitationType = WeatherPrecipitationType.Drizzle, precipitationIntensity = .2f
            };
            WeatherAtmosphericState first = WeatherAtmosphericModel.Derive(values, climate, 0, 10);
            WeatherAtmosphericState later = WeatherAtmosphericModel.Derive(values, climate, 120, 10);

            Assert.That(first.cloudWindSpeedMps, Is.EqualTo(12).Within(.001f));
            Assert.That(first.cloudWindDirectionDegrees, Is.EqualTo(45).Within(.001f));
            Assert.That(later.cloudWindSpeedMps, Is.EqualTo(first.cloudWindSpeedMps).Within(.001f));
            Assert.That(later.cloudEvolution, Is.Not.EqualTo(first.cloudEvolution));
            Assert.That(later.lowClouds.windSpeedMps, Is.EqualTo(first.lowClouds.windSpeedMps).Within(.001f));
        }

        [Test]
        public void DecodedWeatherPresentationSourcesRemainBound()
        {
            Assert.That(
                WeatherPresentationSourceRegistry.Validate(out string failure),
                Is.True,
                failure);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                WeatherPresentationSourceRegistry.RainPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<LocalRain>(), Is.Not.Null);
            Assert.That(prefab.GetComponentInChildren<ParticleSystem>(true), Is.Not.Null);
        }

        WeatherSimulation NewSimulation(int seed)
        {
            var simulation = new WeatherSimulation(climate, catalog, seed);
            simulations.Add(simulation);
            return simulation;
        }
    }
}
