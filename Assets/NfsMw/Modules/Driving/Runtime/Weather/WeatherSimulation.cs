using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum WeatherPrecipitationType { None, Drizzle, Rain }

    public enum WeatherCondition
    {
        Clear, PartlyCloudy, Cloudy, Overcast, Drizzle, LightRain, Rain,
        HeavyRain, StormRain, Thunderstorm, Mist, Fog, HeavyFog, Windy, StrongWind
    }

    public enum WeatherDayPhase { Night, Dawn, Sunrise, Day, Sunset, Dusk }
    public enum WeatherPresentationQuality { VeryLow, Low, Medium, High, Ultra }

    /// <summary>
    /// A compact, value-type view of the environment. Values are simulation values;
    /// presentation quality is deliberately absent so render settings cannot change
    /// weather, wetness, visibility or the authoritative random stream.
    /// </summary>
    [Serializable]
    public struct WeatherSnapshot
    {
        public float simulationSeconds;
        public float timeOfDaySeconds;
        public float timeOfDayHours;
        public float temperatureC;
        public float pressureHpa;
        [Range(0, 1)] public float humidity;
        [Range(0, 1)] public float cloudCover;
        public WeatherPrecipitationType precipitationType;
        [Range(0, 1)] public float precipitationIntensity;
        public float windDirectionDegrees;
        public float windSpeedMps;
        [Range(0, 1)] public float gustStrength;
        public float visibilityMeters;
        [Range(0, 1)] public float fogDensity;
        [Range(0, 1)] public float electricalActivity;
        [Range(0, 1)] public float surfaceWetness;
        [Range(0, 1)] public float standingWater;
        [Range(0, 1)] public float daylight;
        [Range(0, 1)] public float moonlight;
        public float sunElevationDegrees;
        public Color sunColor;
        public string currentPresetId;
        public string targetPresetId;
        [Range(0, 1)] public float transitionProgress;
        public float remainingDurationSeconds;
        public WeatherCondition currentCondition;
        public WeatherDayPhase dayPhase;
        public bool automaticWeather;
        public bool paused;
        public WeatherAtmosphericState atmosphere;

        public float DewPointC => atmosphere.dewPointC;
        public float CloudOpticalDepth => atmosphere.cloudOpticalDepth;
        public float SunTransmission => atmosphere.sunTransmission;

        public bool IsRaining => precipitationType != WeatherPrecipitationType.None && precipitationIntensity > 0.001f;
        public bool IsStorm => electricalActivity > 0.01f || atmosphere.stormIntensity > .55f;
    }

    [Serializable]
    public sealed class WeatherPresetDefinition
    {
        public string id = "clear";
        public string displayName = "Clear";
        public WeatherCondition condition = WeatherCondition.Clear;
        public WeatherPrecipitationType precipitationType = WeatherPrecipitationType.None;
        [Range(0, 1)] public float precipitationIntensity;
        [Range(0, 1)] public float cloudCover;
        public float windDirectionDegrees = 270;
        [Min(0)] public float windSpeedMps = 2;
        [Range(0, 1)] public float gustStrength = .1f;
        [Min(0)] public float visibilityMeters = 20000;
        [Range(0, 1)] public float fogDensity;
        [Range(0, 1)] public float electricalActivity;
        public float temperatureC = 18;
        [Min(0)] public float pressureHpa = WeatherAtmosphericModel.DefaultPressureHpa;
        [Range(0, 1)] public float humidity = .55f;
        [Min(.01f)] public float transitionSeconds = 45;
        [Min(0)] public float minimumDurationSeconds = 180;
        [Min(0)] public float maximumDurationSeconds = 900;
        [Range(0, 1)] public float wetnessContribution;
        [Range(0, 1)] public float standingWaterContribution;

        public WeatherPresetDefinition Clone() => (WeatherPresetDefinition)MemberwiseClone();

        public void Sanitize()
        {
            if (string.IsNullOrWhiteSpace(id)) id = "clear";
            id = id.Trim();
            displayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim();
            precipitationIntensity = Mathf.Clamp01(Finite(precipitationIntensity));
            cloudCover = Mathf.Clamp01(Finite(cloudCover));
            windDirectionDegrees = Mathf.Repeat(Finite(windDirectionDegrees), 360);
            windSpeedMps = Mathf.Max(0, Finite(windSpeedMps));
            gustStrength = Mathf.Clamp01(Finite(gustStrength));
            visibilityMeters = Mathf.Max(1, Finite(visibilityMeters));
            fogDensity = Mathf.Clamp01(Finite(fogDensity));
            electricalActivity = Mathf.Clamp01(Finite(electricalActivity));
            temperatureC = Mathf.Clamp(Finite(temperatureC), -80, 80);
            if (pressureHpa <= 0 || float.IsNaN(pressureHpa) || float.IsInfinity(pressureHpa)) pressureHpa = WeatherAtmosphericModel.DefaultPressureHpa;
            pressureHpa = Mathf.Clamp(pressureHpa, 870, 1085);
            humidity = Mathf.Clamp01(Finite(humidity));
            transitionSeconds = Mathf.Clamp(Finite(transitionSeconds), .01f, 86400);
            minimumDurationSeconds = Mathf.Clamp(Finite(minimumDurationSeconds), 0, 604800);
            maximumDurationSeconds = Mathf.Clamp(Finite(maximumDurationSeconds), minimumDurationSeconds, 604800);
            wetnessContribution = Mathf.Clamp01(Finite(wetnessContribution));
            standingWaterContribution = Mathf.Clamp01(Finite(standingWaterContribution));
        }

        static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : value;
    }

    [Serializable]
    public sealed class WeatherTransitionRule
    {
        public string fromPresetId = "*";
        public string toPresetId = "clear";
        [Min(0)] public float weight = 1;
        [Min(0)] public float cooldownSeconds;
    }

    [Serializable]
    public sealed class WeatherLightningSaveData
    {
        public int sequence;
        public float scheduledSimulationSeconds;
        public float distanceMeters;
        public float thunderDelaySeconds;
        public float flashDurationSeconds;
    }

    /// <summary>Versioned primitive save payload. It is optional in old career profiles.</summary>
    [Serializable]
    public sealed class CareerWeatherData
    {
        public int schemaVersion = 1;
        public int seed;
        public uint simulationRandomState;
        public uint cosmeticRandomState;
        public uint lightningSequence;
        public float simulationSeconds;
        public float timeOfDaySeconds;
        public float fixedAccumulator;
        public float clockTimeScale = 1;
        public float weatherTimeScale = 1;
        public string currentPresetId = "clear";
        public string targetPresetId = "clear";
        public WeatherSnapshotValues currentValues = DefaultClearValues();
        public WeatherSnapshotValues transitionStartValues = DefaultClearValues();
        public WeatherSnapshotValues targetValues = DefaultClearValues();
        public float transitionElapsed;
        public float transitionDuration;
        public float settledRemaining;
        public float surfaceWetness;
        public float standingWater;
        public float nextLightningSimulationSeconds = -1;
        public bool automaticWeather = true;
        public bool paused;
        public int recentHistoryCount;
        public string[] recentHistory = Array.Empty<string>();
        // Optional in schema 1 so older career profiles still load with zero cooldowns.
        public float[] transitionCooldowns = Array.Empty<float>();
        public WeatherLightningSaveData[] pendingLightning = Array.Empty<WeatherLightningSaveData>();

        private static WeatherSnapshotValues DefaultClearValues() => new WeatherSnapshotValues
        {
            temperatureC = 22,
            humidity = .42f,
            cloudCover = .08f,
            precipitationType = WeatherPrecipitationType.None,
            precipitationIntensity = 0,
            windDirectionDegrees = 270,
            windSpeedMps = 2,
            gustStrength = .08f,
            visibilityMeters = 30000,
            fogDensity = 0,
            electricalActivity = 0,
            condition = WeatherCondition.Clear
            ,pressureHpa = WeatherAtmosphericModel.DefaultPressureHpa
        };
    }

    [Serializable]
    public struct WeatherSnapshotValues
    {
        public float temperatureC, pressureHpa, humidity, cloudCover, precipitationIntensity;
        public WeatherPrecipitationType precipitationType;
        public float windDirectionDegrees, windSpeedMps, gustStrength, visibilityMeters, fogDensity, electricalActivity;
        public WeatherCondition condition;

        public static WeatherSnapshotValues From(WeatherPresetDefinition p) => new WeatherSnapshotValues
        {
            temperatureC = p.temperatureC, pressureHpa = p.pressureHpa > 0 ? p.pressureHpa : WeatherAtmosphericModel.DefaultPressureHpa, humidity = p.humidity, cloudCover = p.cloudCover,
            precipitationIntensity = p.precipitationIntensity, precipitationType = p.precipitationType,
            windDirectionDegrees = p.windDirectionDegrees, windSpeedMps = p.windSpeedMps, gustStrength = p.gustStrength,
            visibilityMeters = p.visibilityMeters, fogDensity = p.fogDensity, electricalActivity = p.electricalActivity,
            condition = p.condition
        };

        public static WeatherSnapshotValues Lerp(WeatherSnapshotValues a, WeatherSnapshotValues b, float t)
        {
            t = Mathf.Clamp01(t);
            return new WeatherSnapshotValues
            {
                temperatureC = Mathf.Lerp(a.temperatureC, b.temperatureC, t), pressureHpa = Mathf.Lerp(a.pressureHpa, b.pressureHpa, t), humidity = Mathf.Lerp(a.humidity, b.humidity, t),
                cloudCover = Mathf.Lerp(a.cloudCover, b.cloudCover, t), precipitationIntensity = Mathf.Lerp(a.precipitationIntensity, b.precipitationIntensity, t),
                precipitationType = t < .5f ? a.precipitationType : b.precipitationType,
                windDirectionDegrees = LerpAngle(a.windDirectionDegrees, b.windDirectionDegrees, t),
                windSpeedMps = Mathf.Lerp(a.windSpeedMps, b.windSpeedMps, t), gustStrength = Mathf.Lerp(a.gustStrength, b.gustStrength, t),
                visibilityMeters = Mathf.Lerp(a.visibilityMeters, b.visibilityMeters, t), fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, t),
                electricalActivity = Mathf.Lerp(a.electricalActivity, b.electricalActivity, t), condition = t < .5f ? a.condition : b.condition
            };
        }

        static float LerpAngle(float a, float b, float t)
        {
            float d = Mathf.Repeat(b - a + 180, 360) - 180;
            return Mathf.Repeat(a + d * t, 360);
        }
    }

    public struct WeatherLightningEvent
    {
        public int sequence;
        public float scheduledSimulationSeconds;
        public float distanceMeters;
        public float thunderDelaySeconds;
        public float flashDurationSeconds;
    }

    /// <summary>
    /// Authoritative weather and clock state. It has no rendering, audio, scene-search,
    /// or vehicle dependencies, so it can be exercised in EditMode tests and replayed
    /// at a fixed tick independent of rendering frame rate.
    /// </summary>
    public sealed class WeatherSimulation
    {
        public const int SaveSchemaVersion = 1;
        readonly WeatherClimateProfile climate;
        readonly WeatherPresetDefinition[] presets;
        readonly WeatherLightningEvent[] lightning;
        readonly float[] transitionCooldowns;
        readonly string[] recentHistory = new string[8];
        WeatherSnapshotValues currentValues, transitionStartValues, targetValues;
        WeatherPresetDefinition currentPreset, targetPreset;
        WeatherSnapshot snapshot;
        int recentCount, lightningCount, seed;
        uint randomState, cosmeticRandomState, lightningSequence;
        float fixedAccumulator, simulationSeconds, timeOfDaySeconds;
        float transitionElapsed, transitionDuration, settledRemaining, nextLightningSimulationSeconds = -1;
        float surfaceWetness, standingWater;
        bool automaticWeather = true, paused;
        float clockTimeScale = 1, runtimeWeatherTimeScale;
        bool initialized;
        bool disposed;
        int lastSelectedTransitionRule = -1;

        public WeatherSnapshot Snapshot => snapshot;
        public int Seed => seed;
        public uint SimulationRandomState => randomState;
        public uint CosmeticRandomState => cosmeticRandomState;
        public float DroppedCatchUpSeconds { get; private set; }
        public bool LastAdvanceWasClamped { get; private set; }
        public string LastConfigurationError { get; private set; } = string.Empty;
        public bool AutomaticWeather => automaticWeather;
        public bool Paused => paused;
        public float TimeScale => clockTimeScale;
        public float WeatherSpeed => runtimeWeatherTimeScale;
        public int PendingLightningCount => lightningCount;
        public int RecentHistoryCount => recentCount;
        public WeatherClimateProfile RuntimeClimate => climate;
        public event Action<WeatherSnapshot> SnapshotChanged;
        public event Action<WeatherDayPhase> DayPhaseChanged;

        public WeatherSimulation(WeatherClimateProfile sourceClimate, WeatherPresetCatalog sourceCatalog, int requestedSeed = 0)
        {
            bool ownsSourceClimate = sourceClimate == null;
            if (ownsSourceClimate) sourceClimate = WeatherClimateProfile.CreateRuntimeDefaults();
            climate = UnityEngine.Object.Instantiate(sourceClimate);
            climate.hideFlags = HideFlags.HideAndDontSave;
            if (climate.transitions != null)
            {
                var sourceRules = climate.transitions;
                climate.transitions = new WeatherTransitionRule[sourceRules.Length];
                for (int i = 0; i < sourceRules.Length; i++)
                {
                    var rule = sourceRules[i];
                    climate.transitions[i] = rule == null ? null : new WeatherTransitionRule
                    {
                        fromPresetId = rule.fromPresetId, toPresetId = rule.toPresetId,
                        weight = rule.weight, cooldownSeconds = rule.cooldownSeconds
                    };
                }
            }
            climate.Sanitize();
            if (!sourceClimate.Validate(out string climateFailure)) LastConfigurationError = climateFailure;
            var authoredPresets = sourceCatalog != null ? sourceCatalog.presets : null;
            if (sourceCatalog != null && !sourceCatalog.Validate(out string catalogFailure))
                LastConfigurationError = AppendConfigurationError(LastConfigurationError, catalogFailure);
            if (authoredPresets == null || authoredPresets.Length == 0) authoredPresets = WeatherPresetCatalog.DefaultPresets();
            presets = new WeatherPresetDefinition[authoredPresets.Length];
            for (int i = 0; i < authoredPresets.Length; i++)
            {
                presets[i] = authoredPresets[i] == null ? new WeatherPresetDefinition() : authoredPresets[i].Clone();
                presets[i].Sanitize();
                for (int j = 0; j < i; j++) if (string.Equals(presets[j].id, presets[i].id, StringComparison.Ordinal))
                    LastConfigurationError = "Weather preset IDs must be unique: " + presets[i].id;
            }
            runtimeWeatherTimeScale = Mathf.Clamp(finite(climate.weatherTimeScale), 0, 1000);
            transitionCooldowns = new float[climate.transitions == null ? 0 : climate.transitions.Length];
            lightning = new WeatherLightningEvent[Mathf.Clamp(climate.maximumLightningEvents, 1, 32)];
            if (ownsSourceClimate)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(sourceClimate);
                else UnityEngine.Object.DestroyImmediate(sourceClimate);
            }
            Reset(requestedSeed);
        }

        static string AppendConfigurationError(string existing, string next)
            => string.IsNullOrEmpty(existing) ? next : existing + " " + next;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            initialized = false;
            if (climate)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(climate);
                else UnityEngine.Object.DestroyImmediate(climate);
            }
        }

        public void Reset(int requestedSeed = 0)
        {
            disposed = false;
            seed = requestedSeed != 0 ? PositiveSeed(requestedSeed) : climate.defaultSeed != 0 ? PositiveSeed(climate.defaultSeed) : NewSessionSeed();
            randomState = MixSeed((uint)seed); cosmeticRandomState = MixSeed((uint)seed ^ 0x9E3779B9u);
            simulationSeconds = 0; fixedAccumulator = 0; timeOfDaySeconds = Mathf.Repeat(climate.startingTimeHours * 3600, 86400);
            transitionElapsed = 0; transitionDuration = 0; settledRemaining = 0; surfaceWetness = 0; standingWater = 0;
            recentCount = 0; lightningCount = 0; lightningSequence = 0; nextLightningSimulationSeconds = -1; DroppedCatchUpSeconds = 0; LastAdvanceWasClamped = false;
            if (transitionCooldowns != null) Array.Clear(transitionCooldowns, 0, transitionCooldowns.Length);
            automaticWeather = true; paused = false; initialized = false;
            currentPreset = FindInitialPreset();
            targetPreset = currentPreset;
            currentValues = WeatherSnapshotValues.From(currentPreset); transitionStartValues = currentValues; targetValues = currentValues;
            initialized = true; settledRemaining = DurationFor(currentPreset); Remember(currentPreset.id); PublishSnapshot();
        }

        public void SetPaused(bool value) { paused = value; PublishSnapshot(); }
        /// <summary>Sets the world-clock rate. One means one authored day per dayLengthRealSeconds.</summary>
        public void SetTimeScale(float value) { clockTimeScale = Mathf.Clamp(finite(value), 0, 100); PublishSnapshot(); }
        public void SetWeatherSpeed(float value) { runtimeWeatherTimeScale = Mathf.Clamp(finite(value), 0, 1000); }
        public void SetAutomaticWeather(bool value)
        {
            automaticWeather = value;
            if (value && transitionElapsed >= transitionDuration) settledRemaining = Mathf.Min(settledRemaining, climate.minimumPersistenceSeconds);
            PublishSnapshot();
        }
        public bool SetTimeOfDayHours(float hours)
        {
            if (float.IsNaN(hours) || float.IsInfinity(hours)) return false;
            float oldTime = timeOfDaySeconds;
            timeOfDaySeconds = Mathf.Repeat(hours * 3600, 86400);
            EmitClockEvents(oldTime, timeOfDaySeconds, ForwardClockDelta(oldTime, timeOfDaySeconds));
            PublishSnapshot();
            return true;
        }
        public bool SkipTimeHours(float hours)
        {
            if (float.IsNaN(hours) || float.IsInfinity(hours)) return false;
            float requestedHours = Mathf.Clamp(hours, -7 * 24, 7 * 24);
            if (requestedHours >= 0)
            {
                float clockHours = requestedHours;
                if (clockHours <= 0) { PublishSnapshot(); return true; }
                if (clockTimeScale <= .0001f)
                {
                    float oldTime = timeOfDaySeconds;
                    timeOfDaySeconds = Mathf.Repeat(timeOfDaySeconds + clockHours * 3600, 86400);
                    EmitClockEvents(oldTime, timeOfDaySeconds, ForwardClockDelta(oldTime, timeOfDaySeconds));
                    PublishSnapshot();
                }
                else
                {
                    // Skip is expressed in authored world-clock hours. Convert back to real
                    // seconds so a clock scale of two still advances exactly one clock hour.
                    Advance(clockHours / 24f * climate.dayLengthRealSeconds / clockTimeScale);
                }
            }
            else
            {
                // Clock edits do not run the authoritative weather state backwards.
                timeOfDaySeconds = Mathf.Repeat(timeOfDaySeconds + requestedHours * 3600, 86400);
                PublishSnapshot();
            }
            return true;
        }

        public bool TransitionToPreset(string presetId, bool immediate, out string failure)
        {
            var preset = FindPreset(presetId);
            if (preset == null) { failure = "Unknown weather preset: " + presetId; return false; }
            automaticWeather = false;
            if (immediate)
            {
                currentPreset = targetPreset = preset; currentValues = transitionStartValues = targetValues = WeatherSnapshotValues.From(preset);
                transitionElapsed = transitionDuration = 0; settledRemaining = DurationFor(preset); Remember(preset.id);
                nextLightningSimulationSeconds = -1;
                if (preset.electricalActivity < .01f) ClearLightning();
            }
            else { lastSelectedTransitionRule = -1; BeginTransition(preset); }
            PublishSnapshot(); failure = string.Empty; return true;
        }

        public void Advance(float realSeconds)
        {
            LastAdvanceWasClamped = false;
            if (!initialized || paused || realSeconds <= 0 || float.IsNaN(realSeconds) || float.IsInfinity(realSeconds)) return;
            float safe = Mathf.Min(realSeconds, 86400 * 7);
            fixedAccumulator += safe;
            float step = Mathf.Max(.02f, climate.fixedStepSeconds);
            int maximumTicks = Mathf.Max(1, Mathf.CeilToInt(climate.maxCatchUpSeconds / step));
            int ticks = 0;
            while (fixedAccumulator >= step && ticks++ < maximumTicks) { fixedAccumulator -= step; Step(step); }
            if (fixedAccumulator >= step)
            {
                float dropped = fixedAccumulator - Mathf.Repeat(fixedAccumulator, step);
                fixedAccumulator = Mathf.Repeat(fixedAccumulator, step);
                DroppedCatchUpSeconds += dropped; LastAdvanceWasClamped = true;
                float weatherDelta = dropped * runtimeWeatherTimeScale * (climate.weatherFollowsClockScale ? clockTimeScale : 1);
                float oldTime = timeOfDaySeconds;
                float clockDelta = dropped * 86400 / Mathf.Max(1, climate.dayLengthRealSeconds) * clockTimeScale;
                timeOfDaySeconds = Mathf.Repeat(timeOfDaySeconds + clockDelta, 86400);
                EmitClockEvents(oldTime, timeOfDaySeconds, clockDelta);
                simulationSeconds += weatherDelta; UpdateWetness(weatherDelta); ClearLightning();
                PublishSnapshot();
            }
        }

        void Step(float realDelta)
        {
            float clockScale = clockTimeScale; // world clock is intentionally separate from weather speed.
            float oldTime = timeOfDaySeconds;
            timeOfDaySeconds = Mathf.Repeat(timeOfDaySeconds + realDelta * 86400 / Mathf.Max(1, climate.dayLengthRealSeconds) * clockScale, 86400);
            EmitClockEvents(oldTime, timeOfDaySeconds, realDelta * 86400 / Mathf.Max(1, climate.dayLengthRealSeconds) * clockScale);
            float weatherDelta = realDelta * runtimeWeatherTimeScale * (climate.weatherFollowsClockScale ? clockScale : 1);
            simulationSeconds += weatherDelta;
            if (weatherDelta > 0)
            {
                TickTransitionCooldowns(weatherDelta);
                AdvanceTransition(weatherDelta);
                UpdateWetness(weatherDelta);
                ScheduleLightning();
            }
            PublishSnapshot();
        }

        void AdvanceTransition(float delta)
        {
            if (transitionElapsed < transitionDuration)
            {
                transitionElapsed = Mathf.Min(transitionDuration, transitionElapsed + delta);
                currentValues = WeatherSnapshotValues.Lerp(transitionStartValues, targetValues, transitionDuration <= 0 ? 1 : transitionElapsed / transitionDuration);
                if (transitionElapsed >= transitionDuration)
                {
                    currentPreset = targetPreset; currentValues = targetValues; settledRemaining = DurationFor(currentPreset); Remember(currentPreset.id);
                }
                return;
            }
            if (!automaticWeather) return;
            settledRemaining -= delta;
            if (settledRemaining > 0) return;
            BeginTransition(ChooseNextPreset());
        }

        void BeginTransition(WeatherPresetDefinition preset)
        {
            if (preset == null) return;
            transitionStartValues = currentValues; targetPreset = preset; targetValues = WeatherSnapshotValues.From(preset);
            transitionElapsed = 0; transitionDuration = Mathf.Max(.01f, preset.transitionSeconds); settledRemaining = 0;
            if (lastSelectedTransitionRule >= 0 && lastSelectedTransitionRule < transitionCooldowns.Length)
            {
                var rule = climate.transitions[lastSelectedTransitionRule];
                transitionCooldowns[lastSelectedTransitionRule] = rule == null ? 0 : Mathf.Max(0, rule.cooldownSeconds);
            }
            lastSelectedTransitionRule = -1;
        }

        void TickTransitionCooldowns(float delta)
        {
            if (transitionCooldowns == null) return;
            for (int i = 0; i < transitionCooldowns.Length; i++) transitionCooldowns[i] = Mathf.Max(0, transitionCooldowns[i] - delta);
        }

        WeatherPresetDefinition ChooseNextPreset()
        {
            lastSelectedTransitionRule = -1;
            WeatherTransitionRule[] rules = climate.transitions;
            float total = 0;
            if (rules != null)
                for (int i = 0; i < rules.Length; i++)
                {
                    var rule = rules[i];
                    if (rule == null || rule.weight <= 0 || transitionCooldowns[i] > 0 || (!string.Equals(rule.fromPresetId, "*", StringComparison.Ordinal) && !string.Equals(rule.fromPresetId, currentPreset.id, StringComparison.Ordinal))) continue;
                    var candidate = FindPreset(rule.toPresetId);
                    if (candidate == null || string.Equals(candidate.id, currentPreset.id, StringComparison.Ordinal) || IsRecent(candidate.id)) continue;
                    total += AdjustedWeight(rule.weight, candidate);
                }
            if (total <= 0) return ChooseFallbackPreset();
            float pick = NextFloat() * total;
            if (rules != null)
                for (int i = 0; i < rules.Length; i++)
                {
                    var rule = rules[i];
                    if (rule == null || rule.weight <= 0 || transitionCooldowns[i] > 0 || (!string.Equals(rule.fromPresetId, "*", StringComparison.Ordinal) && !string.Equals(rule.fromPresetId, currentPreset.id, StringComparison.Ordinal))) continue;
                    var candidate = FindPreset(rule.toPresetId);
                    if (candidate == null || string.Equals(candidate.id, currentPreset.id, StringComparison.Ordinal) || IsRecent(candidate.id)) continue;
                    pick -= AdjustedWeight(rule.weight, candidate);
                    if (pick <= 0) { lastSelectedTransitionRule = i; return candidate; }
                }
            return ChooseFallbackPreset();
        }

        float AdjustedWeight(float baseWeight, WeatherPresetDefinition candidate)
        {
            float factor = 1;
            if (candidate.precipitationIntensity > .05f) factor *= Mathf.Lerp(.45f, 1.55f, Mathf.InverseLerp(0, 1, climate.rainFrequency));
            if (candidate.electricalActivity > .05f) factor *= Mathf.Lerp(.4f, 1.8f, Mathf.InverseLerp(0, 1, climate.stormLikelihood));
            if (candidate.precipitationIntensity > .05f) factor *= Mathf.Lerp(.75f, 1.25f, climate.atmosphericMoisture);
            if (candidate.precipitationIntensity < .05f) factor *= Mathf.Lerp(.7f, 1.3f, Mathf.InverseLerp(0, 2, climate.dryingTendency));
            return baseWeight * factor;
        }

        WeatherPresetDefinition ChooseFallbackPreset()
        {
            WeatherPresetDefinition recentCandidate = null;
            int recentRuleIndex = -1;
            if (climate.transitions != null)
                for (int i = 0; i < climate.transitions.Length; i++)
                {
                    var rule = climate.transitions[i];
                    if (rule == null || rule.weight <= 0 || transitionCooldowns[i] > 0
                        || (!string.Equals(rule.fromPresetId, "*", StringComparison.Ordinal) && !string.Equals(rule.fromPresetId, currentPreset.id, StringComparison.Ordinal))) continue;
                    var candidate = FindPreset(rule.toPresetId);
                    if (candidate == null || string.Equals(candidate.id, currentPreset.id, StringComparison.Ordinal)) continue;
                    if (!IsRecent(candidate.id)) { lastSelectedTransitionRule = i; return candidate; }
                    recentCandidate = candidate;
                    recentRuleIndex = i;
                }
            if (recentCandidate != null) { lastSelectedTransitionRule = recentRuleIndex; return recentCandidate; }
            return currentPreset;
        }

        void UpdateWetness(float delta)
        {
            float rain = Mathf.Clamp01(currentValues.precipitationIntensity);
            float exposure = rain * (0.35f + currentValues.humidity * .65f);
            float temperatureDrying = Mathf.InverseLerp(-10, 40, climate.temperatureC);
            float dry = climate.wetnessDrainagePerSecond * (1 - rain) * (1 - currentValues.humidity * .65f)
                + climate.wetnessDrainagePerSecond * temperatureDrying * .2f * (1 - rain)
                + climate.sunlightDryingPerSecond * snapshot.daylight * (1 - currentValues.humidity)
                + climate.windDryingPerSecond * currentValues.windSpeedMps * (1 - rain);
            surfaceWetness = Mathf.Clamp01(surfaceWetness + (exposure * climate.wetnessAccumulationPerSecond - dry) * delta);
            float standingTarget = Mathf.Clamp01(currentValues.precipitationIntensity * currentValues.humidity + (surfaceWetness - climate.surfaceWetnessThreshold) * 1.4f);
            float standingRate = standingTarget > standingWater ? climate.wetnessAccumulationPerSecond : climate.standingWaterDrainagePerSecond + dry;
            standingWater = Mathf.MoveTowards(standingWater, standingTarget, Mathf.Max(.001f, standingRate) * delta);
        }

        void ScheduleLightning()
        {
            float activity = Mathf.Clamp01(currentValues.electricalActivity);
            // Electrical activity is the authored trigger, but a strike is only
            // plausible once the derived atmosphere has enough deep convection.
            if (activity < .01f || snapshot.atmosphere.stormIntensity < .15f || snapshot.atmosphere.convectiveClouds.coverage < .1f)
            { nextLightningSimulationSeconds = -1; ClearLightning(); return; }
            if (nextLightningSimulationSeconds < 0)
                nextLightningSimulationSeconds = simulationSeconds + Mathf.Lerp(climate.lightningQuietSecondsMax, climate.lightningQuietSecondsMin, activity);
            if (simulationSeconds < nextLightningSimulationSeconds) return;
            if (lightningCount < lightning.Length)
            {
                float distance = Mathf.Lerp(80, 900, NextFloat());
                lightning[lightningCount++] = new WeatherLightningEvent
                {
                    sequence = (int)++lightningSequence, scheduledSimulationSeconds = simulationSeconds,
                    distanceMeters = distance, thunderDelaySeconds = distance / Mathf.Max(1, climate.soundSpeedMetersPerSecond),
                    flashDurationSeconds = Mathf.Lerp(.05f, .24f, NextFloat())
                };
            }
            nextLightningSimulationSeconds = simulationSeconds + Mathf.Lerp(climate.lightningQuietSecondsMax, climate.lightningQuietSecondsMin, activity) * Mathf.Lerp(.65f, 1.35f, NextFloat());
        }

        public bool TryDequeueLightning(out WeatherLightningEvent value)
        {
            if (lightningCount == 0) { value = default; return false; }
            value = lightning[0]; for (int i = 1; i < lightningCount; i++) lightning[i - 1] = lightning[i]; lightningCount--; return true;
        }

        void ClearLightning() { lightningCount = 0; }

        void EmitClockEvents(float oldTime, float newTime, float delta)
        {
            if (delta <= 0) return;
            if (CrossedForward(oldTime, newTime, climate.dawnHours * 3600, delta)) DayPhaseChanged?.Invoke(WeatherDayPhase.Dawn);
            if (CrossedForward(oldTime, newTime, climate.sunriseHours * 3600, delta)) DayPhaseChanged?.Invoke(WeatherDayPhase.Sunrise);
            // Sunrise/sunset retain their short named transition windows in the snapshot;
            // these epsilon boundaries emit the broader Day/Night phases without duplicate
            // events on every fixed tick.
            float phaseWindowSeconds = .05f / 15f * 3600f;
            if (CrossedForward(oldTime, newTime, Mathf.Repeat(climate.sunriseHours * 3600 + phaseWindowSeconds, 86400), delta)) DayPhaseChanged?.Invoke(WeatherDayPhase.Day);
            if (CrossedForward(oldTime, newTime, climate.sunsetHours * 3600, delta)) DayPhaseChanged?.Invoke(WeatherDayPhase.Sunset);
            if (CrossedForward(oldTime, newTime, climate.duskHours * 3600, delta)) DayPhaseChanged?.Invoke(WeatherDayPhase.Dusk);
            if (CrossedForward(oldTime, newTime, Mathf.Repeat(climate.duskHours * 3600 + phaseWindowSeconds, 86400), delta)) DayPhaseChanged?.Invoke(WeatherDayPhase.Night);
        }

        static float ForwardClockDelta(float oldTime, float newTime)
            => Mathf.Repeat(newTime - oldTime + 86400, 86400);

        static bool CrossedForward(float oldTime, float newTime, float boundary, float delta)
        {
            if (delta >= 86400) return true;
            return oldTime <= newTime ? oldTime < boundary && boundary <= newTime : oldTime < boundary || boundary <= newTime;
        }

        void PublishSnapshot()
        {
            float hours = timeOfDaySeconds / 3600;
            float rawDaylight = Daylight(hours, climate.sunriseHours, climate.sunsetHours);
            float daylight = climate.daylightIntensityCurve == null ? rawDaylight : Mathf.Clamp01(climate.daylightIntensityCurve.Evaluate(rawDaylight));
            float moon = Mathf.Clamp01(1 - daylight * .9f);
            WeatherDayPhase phase = Phase(hours, climate.dawnHours, climate.sunriseHours, climate.sunsetHours, climate.duskHours);
            float sunPhase = DayPhaseProgress(hours, climate.sunriseHours, climate.sunsetHours);
            snapshot = new WeatherSnapshot
            {
                simulationSeconds = simulationSeconds, timeOfDaySeconds = timeOfDaySeconds, timeOfDayHours = hours,
                temperatureC = currentValues.temperatureC, pressureHpa = currentValues.pressureHpa > 0 ? currentValues.pressureHpa : WeatherAtmosphericModel.DefaultPressureHpa, humidity = Mathf.Clamp01(currentValues.humidity), cloudCover = Mathf.Clamp01(currentValues.cloudCover),
                precipitationType = currentValues.precipitationType, precipitationIntensity = Mathf.Clamp01(currentValues.precipitationIntensity),
                windDirectionDegrees = Mathf.Repeat(currentValues.windDirectionDegrees, 360), windSpeedMps = Mathf.Max(0, currentValues.windSpeedMps),
                gustStrength = Mathf.Clamp01(currentValues.gustStrength), visibilityMeters = Mathf.Max(1, currentValues.visibilityMeters), fogDensity = Mathf.Clamp01(currentValues.fogDensity),
                electricalActivity = Mathf.Clamp01(currentValues.electricalActivity), surfaceWetness = surfaceWetness, standingWater = standingWater,
                daylight = daylight, moonlight = moon,
                sunElevationDegrees = climate.sunElevationCurve == null ? Mathf.Lerp(-18, 58, rawDaylight) : climate.sunElevationCurve.Evaluate(rawDaylight),
                sunColor = climate.daylightColorGradient == null ? Color.white : climate.daylightColorGradient.Evaluate(Mathf.Clamp01(sunPhase)),
                currentPresetId = currentPreset != null ? currentPreset.id : string.Empty, targetPresetId = targetPreset != null ? targetPreset.id : string.Empty,
                transitionProgress = transitionDuration <= 0 ? 1 : Mathf.Clamp01(transitionElapsed / transitionDuration),
                remainingDurationSeconds = Mathf.Max(0, transitionElapsed < transitionDuration ? transitionDuration - transitionElapsed : settledRemaining),
                currentCondition = currentValues.condition, dayPhase = phase, automaticWeather = automaticWeather, paused = paused,
                atmosphere = WeatherAtmosphericModel.Derive(currentValues, climate, simulationSeconds, seed)
            };
            SnapshotChanged?.Invoke(snapshot);
        }

        static WeatherDayPhase Phase(float hours, float dawn, float sunrise, float sunset, float dusk)
        {
            if (Near(hours, sunrise)) return WeatherDayPhase.Sunrise;
            if (Near(hours, sunset)) return WeatherDayPhase.Sunset;
            if (Between(hours, dawn, sunrise)) return WeatherDayPhase.Dawn;
            if (Between(hours, sunrise, sunset)) return WeatherDayPhase.Day;
            if (Between(hours, sunset, dusk)) return WeatherDayPhase.Dusk;
            return WeatherDayPhase.Night;
        }
        static bool Between(float value, float start, float end) => start <= end ? value >= start && value < end : value >= start || value < end;
        static bool Near(float value, float boundary) => Mathf.Abs(Mathf.DeltaAngle(value * 15, boundary * 15)) < .05f;
        static float Daylight(float hours, float sunrise, float sunset)
        {
            float length = Mathf.Repeat(sunset - sunrise + 24, 24); if (length < .01f) return .5f;
            float elapsed = Mathf.Repeat(hours - sunrise + 24, 24); if (elapsed >= length) return 0;
            return Mathf.Clamp01(Mathf.Sin(Mathf.PI * elapsed / length));
        }
        static float DayPhaseProgress(float hours, float sunrise, float sunset)
        {
            float length = Mathf.Repeat(sunset - sunrise + 24, 24);
            if (length < .01f) return 0;
            float elapsed = Mathf.Repeat(hours - sunrise + 24, 24);
            return elapsed >= length ? 0 : Mathf.Clamp01(elapsed / length);
        }

        float DurationFor(WeatherPresetDefinition preset)
        {
            float min = Mathf.Max(climate.minimumPersistenceSeconds, preset.minimumDurationSeconds);
            float max = Mathf.Max(min, Mathf.Min(climate.maximumPersistenceSeconds, preset.maximumDurationSeconds));
            return Mathf.Lerp(min, max, NextFloat());
        }
        void Remember(string id) { if (string.IsNullOrEmpty(id)) return; if (recentCount < recentHistory.Length) recentHistory[recentCount++] = id; else { for (int i = 1; i < recentHistory.Length; i++) recentHistory[i - 1] = recentHistory[i]; recentHistory[recentHistory.Length - 1] = id; } }
        bool IsRecent(string id) { for (int i = 0; i < recentCount; i++) if (string.Equals(recentHistory[i], id, StringComparison.Ordinal)) return true; return false; }
        WeatherPresetDefinition FindInitialPreset() => FindPreset(climate.initialPresetId) ?? (presets != null && presets.Length > 0 ? presets[0] : null) ?? new WeatherPresetDefinition();
        WeatherPresetDefinition FindPreset(string id)
        {
            if (presets == null || string.IsNullOrWhiteSpace(id)) return null;
            for (int i = 0; i < presets.Length; i++) if (presets[i] != null && string.Equals(presets[i].id, id, StringComparison.Ordinal)) return presets[i];
            return null;
        }
        float NextFloat() { randomState = randomState * 1664525u + 1013904223u; return (randomState & 0x00ffffffu) / 16777216f; }
        static uint MixSeed(uint value) { value ^= value >> 16; value *= 2246822519u; value ^= value >> 13; value *= 3266489917u; value ^= value >> 16; return value == 0 ? 1u : value; }
        static int NewSessionSeed()
        {
            uint entropy = unchecked((uint)DateTime.UtcNow.Ticks) ^ unchecked((uint)Environment.TickCount);
            return PositiveSeed(unchecked((int)MixSeed(entropy)));
        }
        static int PositiveSeed(int value) => value == int.MinValue ? int.MaxValue : Mathf.Abs(value);

        public float EvaluateLocalWetness(float exposure, float drainageMultiplier = 1)
        {
            exposure = Mathf.Clamp01(exposure); drainageMultiplier = Mathf.Max(.01f, drainageMultiplier);
            float shelteredFloor = surfaceWetness * .08f;
            float baseWetness = Mathf.Lerp(shelteredFloor, surfaceWetness, exposure) / drainageMultiplier;
            return Mathf.Clamp01(baseWetness + standingWater * exposure * .2f / drainageMultiplier);
        }

        public CareerWeatherData CaptureState()
        {
            var state = new CareerWeatherData
            {
                schemaVersion = SaveSchemaVersion, seed = seed, simulationRandomState = randomState, cosmeticRandomState = cosmeticRandomState, lightningSequence = lightningSequence,
                simulationSeconds = simulationSeconds, timeOfDaySeconds = timeOfDaySeconds, fixedAccumulator = fixedAccumulator,
                clockTimeScale = clockTimeScale, weatherTimeScale = runtimeWeatherTimeScale,
                currentPresetId = currentPreset != null ? currentPreset.id : string.Empty, targetPresetId = targetPreset != null ? targetPreset.id : string.Empty,
                currentValues = currentValues, transitionStartValues = transitionStartValues, targetValues = targetValues,
                transitionElapsed = transitionElapsed, transitionDuration = transitionDuration, settledRemaining = settledRemaining,
                surfaceWetness = surfaceWetness, standingWater = standingWater, nextLightningSimulationSeconds = nextLightningSimulationSeconds,
                automaticWeather = automaticWeather, paused = paused, recentHistoryCount = recentCount,
                recentHistory = new string[recentCount], transitionCooldowns = new float[transitionCooldowns.Length],
                pendingLightning = new WeatherLightningSaveData[lightningCount]
            };
            for (int i = 0; i < recentCount; i++) state.recentHistory[i] = recentHistory[i];
            for (int i = 0; i < transitionCooldowns.Length; i++) state.transitionCooldowns[i] = transitionCooldowns[i];
            for (int i = 0; i < lightningCount; i++) state.pendingLightning[i] = new WeatherLightningSaveData { sequence = lightning[i].sequence, scheduledSimulationSeconds = lightning[i].scheduledSimulationSeconds, distanceMeters = lightning[i].distanceMeters, thunderDelaySeconds = lightning[i].thunderDelaySeconds, flashDurationSeconds = lightning[i].flashDurationSeconds };
            return state;
        }

        public bool RestoreState(CareerWeatherData state, out string failure)
        {
            if (state == null || state.schemaVersion == 0) { Reset(seed); failure = string.Empty; return true; }
            if (state.schemaVersion != SaveSchemaVersion) { failure = "Unsupported weather save schema: " + state.schemaVersion; return false; }
            var current = FindPreset(state.currentPresetId); var target = FindPreset(state.targetPresetId);
            if (current == null || target == null) { failure = "Saved weather preset is missing from the active catalog."; return false; }
            seed = state.seed == 0 ? seed : PositiveSeed(state.seed); randomState = state.simulationRandomState == 0 ? MixSeed((uint)seed) : state.simulationRandomState; cosmeticRandomState = state.cosmeticRandomState == 0 ? MixSeed((uint)seed ^ 0x9E3779B9u) : state.cosmeticRandomState; lightningSequence = state.lightningSequence;
            simulationSeconds = Mathf.Max(0, finite(state.simulationSeconds)); timeOfDaySeconds = Mathf.Repeat(finite(state.timeOfDaySeconds), 86400); fixedAccumulator = Mathf.Clamp(finite(state.fixedAccumulator), 0, climate.fixedStepSeconds); clockTimeScale = Mathf.Clamp(finite(state.clockTimeScale), 0, 100); runtimeWeatherTimeScale = Mathf.Clamp(finite(state.weatherTimeScale), 0, 1000);
            currentPreset = current; targetPreset = target; currentValues = SanitizeValues(state.currentValues); transitionStartValues = SanitizeValues(state.transitionStartValues); targetValues = SanitizeValues(state.targetValues);
            transitionDuration = Mathf.Clamp(finite(state.transitionDuration), 0, 604800);
            transitionElapsed = Mathf.Clamp(finite(state.transitionElapsed), 0, transitionDuration);
            settledRemaining = Mathf.Clamp(finite(state.settledRemaining), 0, 604800);
            surfaceWetness = Mathf.Clamp01(finite(state.surfaceWetness)); standingWater = Mathf.Clamp01(finite(state.standingWater));
            nextLightningSimulationSeconds = finite(state.nextLightningSimulationSeconds);
            automaticWeather = state.automaticWeather; paused = state.paused; lightningCount = 0; recentCount = 0;
            Array.Clear(transitionCooldowns, 0, transitionCooldowns.Length);
            if (state.transitionCooldowns != null)
                for (int i = 0; i < state.transitionCooldowns.Length && i < transitionCooldowns.Length; i++)
                    transitionCooldowns[i] = Mathf.Clamp(finite(state.transitionCooldowns[i]), 0, 604800);
            if (state.recentHistory != null)
                for (int i = 0; i < state.recentHistory.Length && i < recentHistory.Length && i < Mathf.Max(0, state.recentHistoryCount); i++)
                    if (!string.IsNullOrWhiteSpace(state.recentHistory[i])) recentHistory[recentCount++] = state.recentHistory[i];
            if (recentCount == 0) Remember(current.id);
            if (currentValues.electricalActivity < .01f)
            {
                nextLightningSimulationSeconds = -1;
            }
            else if (nextLightningSimulationSeconds < simulationSeconds)
            {
                nextLightningSimulationSeconds = -1;
            }
            if (currentValues.electricalActivity >= .01f && state.pendingLightning != null)
                for (int i = 0; i < state.pendingLightning.Length && i < lightning.Length; i++)
                {
                    var saved = state.pendingLightning[i];
                    if (saved == null || !Finite(saved.scheduledSimulationSeconds) || saved.scheduledSimulationSeconds < simulationSeconds) continue;
                    lightning[lightningCount++] = new WeatherLightningEvent { sequence = saved.sequence, scheduledSimulationSeconds = saved.scheduledSimulationSeconds, distanceMeters = Mathf.Clamp(saved.distanceMeters, 1, 10000), thunderDelaySeconds = Mathf.Clamp(saved.thunderDelaySeconds, 0, 60), flashDurationSeconds = Mathf.Clamp(saved.flashDurationSeconds, .01f, 1) };
                }
            initialized = true; PublishSnapshot(); failure = string.Empty; return true;
        }

        /// <summary>Consumes only the cosmetic random stream; it cannot alter forecast outcomes.</summary>
        public float NextCosmeticRandom()
        {
            cosmeticRandomState = cosmeticRandomState * 1664525u + 1013904223u;
            return (cosmeticRandomState & 0x00ffffffu) / 16777216f;
        }

        static WeatherSnapshotValues SanitizeValues(WeatherSnapshotValues value)
        {
            value.temperatureC = Mathf.Clamp(finite(value.temperatureC), -80, 80); value.pressureHpa = Finite(value.pressureHpa) && value.pressureHpa > 0 ? Mathf.Clamp(value.pressureHpa, 870, 1085) : WeatherAtmosphericModel.DefaultPressureHpa; value.humidity = Mathf.Clamp01(finite(value.humidity)); value.cloudCover = Mathf.Clamp01(finite(value.cloudCover)); value.precipitationIntensity = Mathf.Clamp01(finite(value.precipitationIntensity)); value.precipitationType = (WeatherPrecipitationType)Mathf.Clamp((int)value.precipitationType, 0, 2); value.windDirectionDegrees = Mathf.Repeat(finite(value.windDirectionDegrees), 360); value.windSpeedMps = Mathf.Max(0, finite(value.windSpeedMps)); value.gustStrength = Mathf.Clamp01(finite(value.gustStrength)); value.visibilityMeters = Mathf.Max(1, finite(value.visibilityMeters)); value.fogDensity = Mathf.Clamp01(finite(value.fogDensity)); value.electricalActivity = Mathf.Clamp01(finite(value.electricalActivity)); value.condition = (WeatherCondition)Mathf.Clamp((int)value.condition, 0, 14); return value;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static float finite(float value) => Finite(value) ? value : 0;
    }
}
