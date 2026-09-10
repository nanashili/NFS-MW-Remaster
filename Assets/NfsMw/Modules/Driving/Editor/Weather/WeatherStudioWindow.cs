using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using NfsMwRemaster.Driving.Editor.Workspace;

namespace NfsMwRemaster.Driving.Editor.Weather
{
    /// <summary>
    /// The weather inspector is an IMGUI panel so it can be hosted by the existing
    /// Racing Tools workspace as well as opened as a focused window. Runtime edits
    /// are transient; authored references are changed only through SerializedObject
    /// and Unity Undo in Edit mode.
    /// </summary>
    public sealed class WeatherStudioView : RacingModuleView
    {
        DynamicWeatherWorld world;
        Vector2 scroll;
        string status = "Select a Dynamic Weather World.";

        public WeatherStudioView()
        {
            Root.Add(new IMGUIContainer(Draw) { style = { flexGrow = 1 } });
            EditorApplication.update += Tick;
        }

        public override void SetContext(RacingEditingContext context)
        {
            world = context?.ResolveDocument() as DynamicWeatherWorld;
            if (!world && Application.isPlaying) world = DynamicWeatherWorld.Active;
        }

        public override void Dispose() => EditorApplication.update -= Tick;

        void Tick() { if (Application.isPlaying) Repaint(); }

        void Draw()
        {
            WeatherStudioGui.Draw(ref world, ref scroll, ref status);
        }
    }

    public sealed class WeatherStudioWindow : EditorWindow
    {
        [SerializeField] DynamicWeatherWorld world;
        Vector2 scroll;
        string status = "Select a Dynamic Weather World.";

        [MenuItem("Tools/NFS MW/Weather/Weather Studio")]
        public static void Open() => GetWindow<WeatherStudioWindow>("Weather Studio").Show();

        public static void OpenFocused(DynamicWeatherWorld target)
        {
            var window = GetWindow<WeatherStudioWindow>("Weather Studio");
            window.world = target;
            window.Show();
            window.Focus();
        }

        void OnEnable() => EditorApplication.update += RepaintWhilePlaying;
        void OnDisable() => EditorApplication.update -= RepaintWhilePlaying;
        void RepaintWhilePlaying() { if (Application.isPlaying) Repaint(); }

        public void CreateGUI()
        {
            rootVisualElement.Add(new Label("WEATHER & TIME") { style = { fontSize = 21, marginLeft = 12, marginTop = 10 } });
            rootVisualElement.Add(new Label("Deterministic climate • bounded presentation • day/night controls") { style = { marginLeft = 12 } });
            rootVisualElement.Add(new IMGUIContainer(Draw) { style = { flexGrow = 1 } });
        }

        void Draw() => WeatherStudioGui.Draw(ref world, ref scroll, ref status);
    }

    static class WeatherStudioGui
    {
        public static void Draw(ref DynamicWeatherWorld world, ref Vector2 scroll, ref string status)
        {
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUI.BeginChangeCheck();
                var next = (DynamicWeatherWorld)EditorGUILayout.ObjectField("Scene owner", world, typeof(DynamicWeatherWorld), true);
                if (EditorGUI.EndChangeCheck()) world = next;

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Use selected"))
                        world = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<DynamicWeatherWorld>() : null;
                    if (GUILayout.Button("Use active runtime")) world = DynamicWeatherWorld.Active;
                }

                DrawPresentationSources(ref status);

                if (!world)
                {
                    EditorGUILayout.HelpBox("Choose a Dynamic Weather World component in the scene. The simulation is created when that component is enabled.", MessageType.Info);
                    return;
                }

                scroll = EditorGUILayout.BeginScrollView(scroll);
                DrawAuthoring(world);
                if (Application.isPlaying) DrawRuntime(world, ref status);
                else EditorGUILayout.HelpBox("Enter Play mode to inspect the live snapshot and use debug controls. Authoring fields above are safe Undo-backed scene edits.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.HelpBox(status, MessageType.None);
            }
        }

        static void DrawAuthoring(DynamicWeatherWorld world)
        {
            EditorGUILayout.LabelField("Authoring", EditorStyles.boldLabel);
            var serialized = new SerializedObject(world);
            serialized.Update();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(serialized.FindProperty("climate"));
                EditorGUILayout.PropertyField(serialized.FindProperty("catalog"));
                EditorGUILayout.PropertyField(serialized.FindProperty("seed"));
                EditorGUILayout.PropertyField(serialized.FindProperty("startPaused"));
                EditorGUILayout.PropertyField(serialized.FindProperty("useUnscaledTime"));
                EditorGUILayout.PropertyField(serialized.FindProperty("primaryCamera"));
                EditorGUILayout.PropertyField(serialized.FindProperty("rain"));
                EditorGUILayout.PropertyField(serialized.FindProperty("roadWetness"));
                EditorGUILayout.PropertyField(serialized.FindProperty("audioWorld"));
                EditorGUILayout.PropertyField(serialized.FindProperty("rainAmbienceClip"));
                EditorGUILayout.PropertyField(serialized.FindProperty("windAmbienceClip"));
                EditorGUILayout.PropertyField(serialized.FindProperty("rainAmbienceGain"));
                EditorGUILayout.PropertyField(serialized.FindProperty("windAmbienceGain"));
                EditorGUILayout.PropertyField(serialized.FindProperty("thunderClip"));
                EditorGUILayout.PropertyField(serialized.FindProperty("lightningLight"));
                EditorGUILayout.PropertyField(serialized.FindProperty("reduceLightningFlashes"));
                EditorGUILayout.PropertyField(serialized.FindProperty("presentationQuality"));
                if (EditorGUI.EndChangeCheck())
                {
                    serialized.ApplyModifiedProperties();
                    EditorUtility.SetDirty(world);
                }
            }
        }

        static void DrawPresentationSources(ref string status)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Decoded presentation sources", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(WeatherPresentationSourceRegistry.Describe(), MessageType.None);
            if (GUILayout.Button("Validate decoded rain / wind / puddle sources"))
            {
                if (WeatherPresentationSourceRegistry.Validate(out string failure))
                    status = "Decoded presentation sources are valid and bound to the existing rain, surface, puddle, and wind assets.";
                else
                    status = failure;
            }
        }

        static void DrawRuntime(DynamicWeatherWorld world, ref string status)
        {
            WeatherSimulation simulation = world.Simulation;
            if (simulation == null)
            {
                EditorGUILayout.HelpBox("The weather simulation is not initialized.", MessageType.Warning);
                return;
            }

            WeatherSnapshot snapshot = world.Snapshot;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live snapshot", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Time", FormatTime(snapshot.timeOfDayHours) + "  /  " + snapshot.dayPhase);
            EditorGUILayout.LabelField("Weather", snapshot.currentPresetId + " → " + snapshot.targetPresetId + "  (" + (snapshot.transitionProgress * 100).ToString("F0") + "%)");
            EditorGUILayout.LabelField("Duration remaining", snapshot.remainingDurationSeconds.ToString("F1") + " s");
            EditorGUILayout.LabelField("Rain / clouds", snapshot.precipitationIntensity.ToString("F2") + " / " + snapshot.cloudCover.ToString("F2"));
            EditorGUILayout.LabelField("Wind / visibility", snapshot.windSpeedMps.ToString("F1") + " m/s @ " + snapshot.windDirectionDegrees.ToString("F0") + "° / " + snapshot.visibilityMeters.ToString("F0") + " m");
            EditorGUILayout.LabelField("Wetness / standing water", snapshot.surfaceWetness.ToString("F2") + " / " + snapshot.standingWater.ToString("F2"));
            WeatherAtmosphericState atmosphere = snapshot.atmosphere;
            EditorGUILayout.LabelField("Dew point / pressure", atmosphere.dewPointC.ToString("F1") + " °C / " + atmosphere.pressureHpa.ToString("F1") + " hPa");
            EditorGUILayout.LabelField("Cloud layers", atmosphere.lowClouds.coverage.ToString("F2") + " / " + atmosphere.midClouds.coverage.ToString("F2") + " / " + atmosphere.highClouds.coverage.ToString("F2"));
            EditorGUILayout.LabelField("Cloud base / top", atmosphere.cloudBaseAltitudeMeters.ToString("F0") + " / " + atmosphere.cloudTopAltitudeMeters.ToString("F0") + " m");
            EditorGUILayout.LabelField("Optical depth / sun transmission", atmosphere.cloudOpticalDepth.ToString("F2") + " / " + atmosphere.sunTransmission.ToString("F2"));
            EditorGUILayout.LabelField("Precipitation potential / rate", atmosphere.precipitationPotential.ToString("F2") + " / " + atmosphere.precipitationRate.ToString("F2"));
            EditorGUILayout.LabelField("Haze density", atmosphere.hazeDensity.ToString("F2"));
            EditorGUILayout.LabelField("Instability / storm", atmosphere.instability.ToString("F2") + " / " + atmosphere.stormIntensity.ToString("F2") + " (" + atmosphere.primaryFamily + ")");
            EditorGUILayout.LabelField("Seed / quality", simulation.Seed + " / " + world.PresentationQuality);

            EditorGUI.BeginChangeCheck();
            bool paused = EditorGUILayout.Toggle("Paused", snapshot.paused);
            if (EditorGUI.EndChangeCheck()) world.SetPaused(paused);

            EditorGUI.BeginChangeCheck();
            float timeScale = EditorGUILayout.Slider("Clock time scale", simulation.TimeScale, 0, 8);
            if (EditorGUI.EndChangeCheck()) world.SetTimeScale(timeScale);
            EditorGUI.BeginChangeCheck();
            float weatherSpeed = EditorGUILayout.Slider("Weather speed", simulation.WeatherSpeed, 0, 8);
            if (EditorGUI.EndChangeCheck()) world.SetWeatherSpeed(weatherSpeed);
            EditorGUI.BeginChangeCheck();
            var quality = (WeatherPresentationQuality)EditorGUILayout.EnumPopup("Presentation quality", world.PresentationQuality);
            if (EditorGUI.EndChangeCheck()) world.SetPresentationQuality(quality);

            EditorGUI.BeginChangeCheck();
            bool automatic = EditorGUILayout.Toggle("Automatic weather", snapshot.automaticWeather);
            if (EditorGUI.EndChangeCheck()) world.SetAutomaticWeather(automatic);

            float explicitTime = EditorGUILayout.Slider("Explicit time (hours)", snapshot.timeOfDayHours, 0, 24);
            if (GUILayout.Button("Apply explicit time")) world.SetTimeOfDayHours(explicitTime);
            if (GUILayout.Button("Advance 1 simulated hour")) simulation.SkipTimeHours(1);

            string[] ids = world.Catalog?.presets == null
                ? Array.Empty<string>()
                : world.Catalog.presets.Where(p => p != null && !string.IsNullOrWhiteSpace(p.id)).Select(p => p.id).ToArray();
            if (ids.Length > 0)
            {
                int selected = Mathf.Max(0, Array.IndexOf(ids, snapshot.targetPresetId));
                selected = EditorGUILayout.Popup("Debug preset", selected, ids);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Transition"))
                    {
                        if (!world.TransitionToPreset(ids[selected], false, out string failure)) status = failure;
                        else status = "Transition started; automatic weather is suspended.";
                    }
                    if (GUILayout.Button("Apply immediately"))
                    {
                        if (!world.TransitionToPreset(ids[selected], true, out string failure)) status = failure;
                        else status = "Preset applied immediately; automatic weather is suspended.";
                    }
                }
            }
            if (GUILayout.Button("Return to automatic progression")) { world.SetAutomaticWeather(true); status = "Automatic progression resumed."; }

            EditorGUILayout.LabelField("Bounded resources", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Pending lightning", world.PendingLightningCount.ToString());
            EditorGUILayout.LabelField("Dropped catch-up", world.DroppedCatchUpSeconds.ToString("F2") + " s");
            EditorGUILayout.LabelField("Local shelter volumes", WeatherShelterVolume.ActiveCount.ToString());
            EditorGUILayout.HelpBox("Simulation values and gameplay inputs are shared across quality tiers. Quality changes only presentation density, distance, and optional flashes.", MessageType.Info);
        }

        static string FormatTime(float hours)
        {
            int wholeHours = Mathf.FloorToInt(Mathf.Repeat(hours, 24));
            int minutes = Mathf.FloorToInt((Mathf.Repeat(hours, 24) - wholeHours) * 60);
            return wholeHours.ToString("00") + ":" + minutes.ToString("00");
        }
    }
}
