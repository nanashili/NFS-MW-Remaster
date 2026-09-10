using System;
using System.Globalization;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Minimal zero-asset HUD for the prototype scene. Replace this with the
    /// game's UI layer without changing vehicle simulation code.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleTelemetryHud : MonoBehaviour
    {
        const float WeatherPanelWidth = 300f;
        const float WeatherPanelHeight = 238f;
        static readonly float[] DebugRates = { 0f, .25f, .5f, 1f, 2f, 4f, 8f };

        [SerializeField] private VehicleController vehicle;
        [SerializeField] private bool showPhysicsTelemetry = true;

        [NonSerialized] private GUIStyle titleStyle;
        [NonSerialized] private GUIStyle bodyStyle;
        [NonSerialized] private GUIStyle panelStyle;
        [NonSerialized] private GUIStyle debugHeaderStyle;
        [NonSerialized] private GUIStyle debugLineStyle;
        [NonSerialized] private GUIStyle debugCenterStyle;
        [NonSerialized] private GUIStyle debugButtonStyle;
        private DynamicWeatherWorld selectedWorld;
        private int selectedPresetIndex = -1;
        private bool presetSelectionChanged;
        private string observedTargetPreset = string.Empty;

        private void Awake()
        {
            if (vehicle == null)
            {
                vehicle = GetComponent<VehicleController>();
            }

        }

        private void OnGUI()
        {
            DynamicWeatherWorld weather = DynamicWeatherWorld.Active;
            if (weather != null)
            {
                DrawWeatherDebug(weather);
                return;
            }

            if (vehicle == null)
            {
                return;
            }

            BuildStyles();

            VehicleTelemetry telemetry = vehicle.Telemetry;
            GUILayout.BeginArea(new Rect(18f, 18f, 350f, showPhysicsTelemetry ? 220f : 120f));
            GUILayout.Label("MW STREET RACER", titleStyle);
            GUILayout.Label(
                string.Format("{0:000} km/h   GEAR {1}   {2:0000} RPM", telemetry.SpeedKph, telemetry.GearLabel, telemetry.EngineRpm),
                bodyStyle);
            GUILayout.Label("WASD / arrows  Drive     SPACE  Handbrake", bodyStyle);
            GUILayout.Label("SHIFT  Nitrous  |  C  Camera  |  R  Reset", bodyStyle);

            if (telemetry.NitrousActive)
            {
                GUILayout.Label(string.Format("NITROUS  {0:0.0}s", telemetry.NitrousSeconds), titleStyle);
            }

            if (showPhysicsTelemetry)
            {
                GUILayout.Label(
                    string.Format(
                        "Throttle {0:0.00}  Brake {1:0.00}  Slip {2:0.000}",
                        telemetry.Throttle,
                        telemetry.Brake,
                        telemetry.AverageSlip),
                    bodyStyle);
            }

            GUILayout.EndArea();
        }

        private void DrawWeatherDebug(DynamicWeatherWorld weather)
        {
            WeatherSimulation simulation = weather.Simulation;
            if (simulation == null)
            {
                return;
            }

            BuildStyles();
            SyncPresetSelection(weather);
            WeatherSnapshot snapshot = weather.Snapshot;
            WeatherPresetDefinition[] presets = weather.Catalog != null
                ? weather.Catalog.presets
                : null;
            WeatherPresetDefinition selected = SelectedPreset(presets);

            GUILayout.BeginArea(
                new Rect(12f, 12f, WeatherPanelWidth, WeatherPanelHeight),
                GUIContent.none,
                panelStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label(FormatTime(snapshot.timeOfDayHours) + "  " + snapshot.dayPhase.ToString().ToUpperInvariant(), debugHeaderStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(snapshot.paused ? "PAUSED" : snapshot.automaticWeather ? "AUTO" : "MANUAL", debugHeaderStyle);
            GUILayout.EndHorizontal();

            string currentName = PresetName(weather.Catalog, snapshot.currentPresetId);
            string targetName = PresetName(weather.Catalog, snapshot.targetPresetId);
            string weatherLine = string.Equals(snapshot.currentPresetId, snapshot.targetPresetId, StringComparison.Ordinal)
                ? currentName
                : currentName + "  >  " + targetName + "  " + Percent(snapshot.transitionProgress);
            GUILayout.Label(weatherLine, debugLineStyle);
            GUILayout.Label(
                "Rain " + Percent(snapshot.precipitationIntensity)
                + "   Clouds " + Percent(snapshot.cloudCover)
                + "   Fog " + Percent(snapshot.fogDensity),
                debugLineStyle);
            GUILayout.Label(
                "Wind " + snapshot.windSpeedMps.ToString("0.0", CultureInfo.InvariantCulture)
                + " m/s @ " + Mathf.RoundToInt(snapshot.windDirectionDegrees) + "°"
                + "   Vis " + (snapshot.visibilityMeters / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km",
                debugLineStyle);
            GUILayout.Label(
                "Temp " + snapshot.temperatureC.ToString("0.0", CultureInfo.InvariantCulture) + "°C"
                + "   Hum " + Percent(snapshot.humidity)
                + "   " + Mathf.RoundToInt(snapshot.pressureHpa) + " hPa",
                debugLineStyle);
            GUILayout.Label(
                "Wet " + Percent(snapshot.surfaceWetness)
                + "   Water " + Percent(snapshot.standingWater)
                + "   Storm " + Percent(snapshot.atmosphere.stormIntensity),
                debugLineStyle);

            GUILayout.Space(3f);
            GUILayout.BeginHorizontal();
            bool controlsWereEnabled = GUI.enabled;
            GUI.enabled = selected != null;
            if (GUILayout.Button("<", debugButtonStyle, GUILayout.Width(28f))) SelectPreset(presets, -1);
            GUILayout.Label(selected != null ? selected.displayName : "No weather presets", debugCenterStyle, GUILayout.Height(20f));
            if (GUILayout.Button(">", debugButtonStyle, GUILayout.Width(28f))) SelectPreset(presets, 1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Transition", debugButtonStyle)) ApplyPreset(weather, selected, false);
            if (GUILayout.Button("Instant", debugButtonStyle)) ApplyPreset(weather, selected, true);
            GUI.enabled = controlsWereEnabled;
            if (GUILayout.Button(snapshot.automaticWeather ? "Auto: ON" : "Auto: OFF", debugButtonStyle))
                weather.SetAutomaticWeather(!snapshot.automaticWeather);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("- 1 h", debugButtonStyle)) simulation.SkipTimeHours(-1f);
            if (GUILayout.Button("+ 1 h", debugButtonStyle)) simulation.SkipTimeHours(1f);
            if (GUILayout.Button("Noon", debugButtonStyle)) weather.SetTimeOfDayHours(12f);
            if (GUILayout.Button(snapshot.paused ? "Resume" : "Pause", debugButtonStyle)) weather.SetPaused(!snapshot.paused);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Clock", debugLineStyle, GUILayout.Width(38f));
            if (GUILayout.Button("-", debugButtonStyle, GUILayout.Width(24f))) weather.SetTimeScale(StepRate(simulation.TimeScale, -1));
            GUILayout.Label(FormatRate(simulation.TimeScale), debugCenterStyle, GUILayout.Width(34f));
            if (GUILayout.Button("+", debugButtonStyle, GUILayout.Width(24f))) weather.SetTimeScale(StepRate(simulation.TimeScale, 1));
            GUILayout.FlexibleSpace();
            GUILayout.Label("Weather", debugLineStyle, GUILayout.Width(52f));
            if (GUILayout.Button("-", debugButtonStyle, GUILayout.Width(24f))) weather.SetWeatherSpeed(StepRate(simulation.WeatherSpeed, -1));
            GUILayout.Label(FormatRate(simulation.WeatherSpeed), debugCenterStyle, GUILayout.Width(34f));
            if (GUILayout.Button("+", debugButtonStyle, GUILayout.Width(24f))) weather.SetWeatherSpeed(StepRate(simulation.WeatherSpeed, 1));
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private void SyncPresetSelection(DynamicWeatherWorld weather)
        {
            if (selectedWorld != weather)
            {
                selectedWorld = weather;
                selectedPresetIndex = -1;
                presetSelectionChanged = false;
                observedTargetPreset = string.Empty;
            }

            WeatherPresetDefinition[] presets = weather.Catalog != null ? weather.Catalog.presets : null;
            string target = weather.Snapshot.targetPresetId ?? string.Empty;
            if (selectedPresetIndex < 0 || presets == null || selectedPresetIndex >= presets.Length
                || !presetSelectionChanged && !string.Equals(target, observedTargetPreset, StringComparison.Ordinal))
            {
                selectedPresetIndex = FindPresetIndex(presets, target);
                if (selectedPresetIndex < 0) selectedPresetIndex = NextPresetIndex(presets, -1, 1);
                presetSelectionChanged = false;
            }
            observedTargetPreset = target;
        }

        private void SelectPreset(WeatherPresetDefinition[] presets, int direction)
        {
            int next = NextPresetIndex(presets, selectedPresetIndex, direction);
            if (next < 0) return;
            selectedPresetIndex = next;
            presetSelectionChanged = true;
        }

        private void ApplyPreset(DynamicWeatherWorld weather, WeatherPresetDefinition preset, bool immediate)
        {
            if (preset == null) return;
            if (!weather.TransitionToPreset(preset.id, immediate, out string failure))
            {
                Debug.LogWarning("Weather debug controls could not apply the preset: " + failure, this);
                return;
            }
            observedTargetPreset = preset.id;
            presetSelectionChanged = false;
        }

        private WeatherPresetDefinition SelectedPreset(WeatherPresetDefinition[] presets)
        {
            return presets != null && selectedPresetIndex >= 0 && selectedPresetIndex < presets.Length
                ? presets[selectedPresetIndex]
                : null;
        }

        private static int FindPresetIndex(WeatherPresetDefinition[] presets, string id)
        {
            if (presets == null) return -1;
            for (int i = 0; i < presets.Length; i++)
            {
                if (presets[i] != null && string.Equals(presets[i].id, id, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        private static int NextPresetIndex(WeatherPresetDefinition[] presets, int current, int direction)
        {
            if (presets == null || presets.Length == 0) return -1;
            int index = current;
            if (index < 0 || index >= presets.Length) index = direction > 0 ? -1 : 0;
            for (int i = 0; i < presets.Length; i++)
            {
                index = (index + direction + presets.Length) % presets.Length;
                if (presets[index] != null) return index;
            }
            return -1;
        }

        private static float StepRate(float current, int direction)
        {
            if (direction > 0)
            {
                for (int i = 0; i < DebugRates.Length; i++)
                    if (DebugRates[i] > current + .001f) return DebugRates[i];
                return DebugRates[DebugRates.Length - 1];
            }

            for (int i = DebugRates.Length - 1; i >= 0; i--)
                if (DebugRates[i] < current - .001f) return DebugRates[i];
            return DebugRates[0];
        }

        private static string PresetName(WeatherPresetCatalog catalog, string id)
        {
            WeatherPresetDefinition preset = catalog != null ? catalog.Find(id) : null;
            return preset != null && !string.IsNullOrWhiteSpace(preset.displayName) ? preset.displayName : id ?? "Unknown";
        }

        private static string Percent(float value) => Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";

        private static string FormatTime(float hours)
        {
            float wrapped = Mathf.Repeat(hours, 24f);
            int wholeHours = Mathf.FloorToInt(wrapped);
            int minutes = Mathf.FloorToInt((wrapped - wholeHours) * 60f);
            return wholeHours.ToString("00", CultureInfo.InvariantCulture) + ":" + minutes.ToString("00", CultureInfo.InvariantCulture);
        }

        private static string FormatRate(float value)
            => value.ToString("0.##", CultureInfo.InvariantCulture) + "x";

        private void BuildStyles()
        {
            if (titleStyle != null
                && bodyStyle != null
                && panelStyle != null
                && debugHeaderStyle != null
                && debugLineStyle != null
                && debugCenterStyle != null
                && debugButtonStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 19;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = Color.white;

            bodyStyle = new GUIStyle(GUI.skin.label);
            bodyStyle.fontSize = 13;
            bodyStyle.normal.textColor = new Color(0.92f, 0.95f, 1f);

            panelStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 7, 7)
            };

            debugHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                fixedHeight = 16f
            };
            debugHeaderStyle.normal.textColor = new Color(.82f, .91f, 1f);

            debugLineStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fixedHeight = 15f
            };
            debugLineStyle.normal.textColor = new Color(.91f, .94f, .98f);

            debugCenterStyle = new GUIStyle(debugLineStyle)
            {
                alignment = TextAnchor.MiddleCenter
            };

            debugButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 10,
                fixedHeight = 20f,
                padding = new RectOffset(5, 5, 1, 1)
            };
        }
    }
}
