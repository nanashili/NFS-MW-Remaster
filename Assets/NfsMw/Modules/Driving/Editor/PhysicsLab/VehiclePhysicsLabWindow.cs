using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// UI-only workbench for the shared vehicle simulation. The numerical model
    /// lives in VehiclePhysicsLabRunner and VehiclePhysicsLabAnalysis so the
    /// editor can be closed, reloaded and tested without moving gameplay state.
    /// </summary>
    public sealed class VehiclePhysicsLabWindow : EditorWindow
    {
        private static readonly VehiclePhysicsLabTuningParameter[] Parameters =
            (VehiclePhysicsLabTuningParameter[])Enum.GetValues(typeof(VehiclePhysicsLabTuningParameter));

        [SerializeField] private VehiclePhysicsLabDefinition definition;
        [SerializeField] private VehiclePhysicsLabSuite suite;
        [SerializeField] private VehiclePhysicsLabSweepDefinition sweep;
        [SerializeField] private int view;
        [SerializeField] private int selectedReportIndex;
        [SerializeField] private int comparisonBaselineIndex;
        [SerializeField] private int comparisonCandidateIndex;
        [SerializeField] private bool showEffectiveConfiguration = true;
        [SerializeField] private bool drawTrack = true;
        [SerializeField] private bool drawTrajectory = true;
        [SerializeField] private bool drawForces;
        [SerializeField] private bool drawContacts;

        [NonSerialized] private readonly List<VehiclePhysicsLabRunReport> history = new List<VehiclePhysicsLabRunReport>();
        [NonSerialized] private VehiclePhysicsLabRunner runner;
        [NonSerialized] private VehiclePhysicsLabSweepRunner sweepRunner;
        [NonSerialized] private VehiclePhysicsLabDefinition activeRunDefinition;
        [NonSerialized] private bool runningSuite;
        [NonSerialized] private int suiteExperimentIndex;
        [NonSerialized] private int suiteRepetition;
        [NonSerialized] private int suiteCompleted;
        [NonSerialized] private int suiteFailures;
        [NonSerialized] private string message = "Select a Vehicle Physics Lab experiment or create the example.";
        [NonSerialized] private MessageType messageType = MessageType.Info;
        [NonSerialized] private string effectiveFingerprint;
        [NonSerialized] private string effectiveError;
        [NonSerialized] private Dictionary<VehiclePhysicsLabTuningParameter, float> effectiveValues;
        [NonSerialized] private IMGUIContainer inspectorContainer;
        [NonSerialized] private IMGUIContainer workbenchContainer;
        [NonSerialized] private HelpBox status;
        [NonSerialized] private ObjectField definitionField;
        [NonSerialized] private ObjectField suiteField;
        [NonSerialized] private ObjectField sweepField;
        [NonSerialized] private Vector2 inspectorScroll;
        [NonSerialized] private Vector2 workbenchScroll;
        [NonSerialized] private int telemetryCursor = -1;

        public static bool SceneDrawTrack { get; set; } = true;
        public static bool SceneDrawTrajectory { get; set; } = true;
        public static bool SceneDrawForces { get; set; }
        public static bool SceneDrawContacts { get; set; }
        public VehiclePhysicsLabDefinition Definition => definition;
        public IReadOnlyList<VehiclePhysicsLabRunReport> Reports => history;
        private bool Busy => runner != null || sweepRunner != null;

        [MenuItem("NFS MW Remaster/Driving/Vehicle Physics Lab")]
        public static VehiclePhysicsLabWindow Open()
        {
            VehiclePhysicsLabWindow window = GetWindow<VehiclePhysicsLabWindow>("Vehicle Physics Lab");
            window.minSize = new Vector2(1120f, 680f);
            return window;
        }

        [OnOpenAsset]
        private static bool OpenAsset(EntityId instanceId, int line)
        {
            UnityEngine.Object value = EditorUtility.EntityIdToObject(instanceId);
            VehiclePhysicsLabDefinition experiment = value as VehiclePhysicsLabDefinition;
            if (experiment != null)
            {
                Open().SelectDefinition(experiment);
                return true;
            }

            VehiclePhysicsLabSuite selectedSuite = value as VehiclePhysicsLabSuite;
            if (selectedSuite != null)
            {
                Open().SelectSuite(selectedSuite);
                return true;
            }

            VehiclePhysicsLabSweepDefinition selectedSweep = value as VehiclePhysicsLabSweepDefinition;
            if (selectedSweep != null)
            {
                Open().SelectSweep(selectedSweep);
                return true;
            }

            VehiclePhysicsLabTrack selectedTrack = value as VehiclePhysicsLabTrack;
            VehiclePhysicsLabDefinition referencingExperiment = VehiclePhysicsLabEditorAssetSearch.FindReferencingExperiment(selectedTrack);
            if (referencingExperiment == null) return false;
            Open().SelectDefinition(referencingExperiment);
            return true;
        }

        private void OnEnable()
        {
            minSize = new Vector2(1120f, 680f);
            EditorApplication.update += UpdateRunner;
            Selection.selectionChanged += Repaint;
            Undo.undoRedoPerformed += OnUndoRedo;
            SceneView.duringSceneGui += DrawScene;
            AssemblyReloadEvents.beforeAssemblyReload += CancelForReload;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            StopRunner(false);
            EditorApplication.update -= UpdateRunner;
            Selection.selectionChanged -= Repaint;
            Undo.undoRedoPerformed -= OnUndoRedo;
            SceneView.duringSceneGui -= DrawScene;
            AssemblyReloadEvents.beforeAssemblyReload -= CancelForReload;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        public void SelectDefinition(VehiclePhysicsLabDefinition value)
        {
            StopRunner(false);
            definition = value;
            telemetryCursor = -1;
            InvalidateEffectiveCache();
            SetMessage(value == null ? "Select a Vehicle Physics Lab experiment or create the example." : "Ready.", MessageType.Info);
            if (definitionField != null) definitionField.SetValueWithoutNotify(definition);
            Repaint();
            SceneView.RepaintAll();
        }

        public void SelectSuite(VehiclePhysicsLabSuite value)
        {
            StopRunner(false);
            suite = value;
            view = 7;
            if (suiteField != null) suiteField.SetValueWithoutNotify(suite);
            SetMessage(value == null ? "No regression suite selected." : "Suite selected: " + value.name, MessageType.Info);
            Repaint();
        }

        public void SelectSweep(VehiclePhysicsLabSweepDefinition value)
        {
            StopRunner(false);
            sweep = value;
            view = 7;
            if (sweepField != null) sweepField.SetValueWithoutNotify(sweep);
            SetMessage(value == null ? "No parameter sweep selected." : "Sweep selected: " + value.name, MessageType.Info);
            Repaint();
        }

        private void OnUndoRedo()
        {
            StopRunner(false);
            InvalidateEffectiveCache();
            Repaint();
            SceneView.RepaintAll();
        }

        private void CancelForReload()
        {
            StopRunner(false);
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode
                || state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
            {
                StopRunner(false);
                InvalidateEffectiveCache();
            }
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.paddingLeft = 8f;
            rootVisualElement.style.paddingRight = 8f;
            rootVisualElement.style.paddingTop = 6f;
            rootVisualElement.style.paddingBottom = 6f;

            Toolbar toolbar = new Toolbar();
            toolbar.Add(new Label("VEHICLE PHYSICS LAB")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 8f }
            });

            definitionField = new ObjectField("Experiment")
            {
                objectType = typeof(VehiclePhysicsLabDefinition),
                allowSceneObjects = false,
                value = definition
            };
            definitionField.style.flexGrow = 1f;
            definitionField.style.minWidth = 220f;
            definitionField.RegisterValueChangedCallback(change => SelectDefinition(change.newValue as VehiclePhysicsLabDefinition));
            toolbar.Add(definitionField);

            suiteField = new ObjectField("Suite")
            {
                objectType = typeof(VehiclePhysicsLabSuite),
                allowSceneObjects = false,
                value = suite
            };
            suiteField.style.minWidth = 170f;
            suiteField.RegisterValueChangedCallback(change =>
            {
                suite = change.newValue as VehiclePhysicsLabSuite;
                SetMessage(suite == null ? "No regression suite selected." : "Suite selected: " + suite.name, MessageType.Info);
            });
            toolbar.Add(suiteField);

            sweepField = new ObjectField("Sweep")
            {
                objectType = typeof(VehiclePhysicsLabSweepDefinition),
                allowSceneObjects = false,
                value = sweep
            };
            sweepField.style.minWidth = 170f;
            sweepField.RegisterValueChangedCallback(change =>
            {
                sweep = change.newValue as VehiclePhysicsLabSweepDefinition;
                SetMessage(sweep == null ? "No parameter sweep selected." : "Sweep selected: " + sweep.name, MessageType.Info);
            });
            toolbar.Add(sweepField);

            toolbar.Add(new ToolbarButton(() => Safe(CreateNewExperiment)) { text = "New Experiment" });
            toolbar.Add(new ToolbarButton(() => Safe(VehiclePhysicsLabEditorOperations.CreateDemo)) { text = "Create Demo" });
            toolbar.Add(new ToolbarButton(() => Safe(() => BeginRun(definition))) { text = "Run" });
            toolbar.Add(new ToolbarButton(() => Safe(BeginSweep)) { text = "Run Sweep" });
            toolbar.Add(new ToolbarButton(() => Safe(CancelRun)) { text = "Cancel" });
            toolbar.Add(new ToolbarButton(() => Safe(ExportSelectedJson)) { text = "JSON" });
            toolbar.Add(new ToolbarButton(() => Safe(ExportSelectedCsv)) { text = "CSV" });

            rootVisualElement.Add(toolbar);

            TwoPaneSplitView split = new TwoPaneSplitView(0, 365f, TwoPaneSplitViewOrientation.Horizontal)
            {
                style = { flexGrow = 1f }
            };
            inspectorContainer = new IMGUIContainer(DrawInspector) { style = { flexGrow = 1f } };
            workbenchContainer = new IMGUIContainer(DrawWorkbench) { style = { flexGrow = 1f } };
            split.Add(inspectorContainer);
            split.Add(workbenchContainer);
            rootVisualElement.Add(split);

            status = new HelpBox(message, ToHelpBoxType(messageType));
            status.style.marginTop = 5f;
            rootVisualElement.Add(status);
            RefreshStatus();
        }

        private void DrawInspector()
        {
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("AUTHORING & DEPENDENCIES", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The lab resolves a disposable effective tune from the canonical vehicle setup, then applies scenario overrides to a runtime copy. Temporary launch, damage and assist state never writes to the player garage or career save.",
                MessageType.Info);

            if (definition == null)
            {
                EditorGUILayout.HelpBox("Choose an experiment asset, create the demo, or press New Experiment.", MessageType.Info);
                if (GUILayout.Button("Create demo assets")) Safe(VehiclePhysicsLabEditorOperations.CreateDemo);
                EditorGUILayout.EndScrollView();
                return;
            }

            SerializedObject serialized = new SerializedObject(definition);
            serialized.Update();
            EditorGUI.BeginChangeCheck();
            DrawProperty(serialized, "displayName");
            DrawProperty(serialized, "vehicle");
            DrawProperty(serialized, "track");
            DrawProperty(serialized, "publishToCapability");
            DrawProperty(serialized, "experiment");
            DrawProperty(serialized, "fixedStep");
            DrawProperty(serialized, "warmupSeconds");
            DrawProperty(serialized, "startingSpeedKph");
            DrawProperty(serialized, "targetSpeedKph");
            DrawProperty(serialized, "brakingStartSpeedKph");
            DrawProperty(serialized, "seed");

            EditorGUILayout.Space(4f);
            DrawSection(serialized, "input", "INPUT SCHEDULE — generated, recorded or live");
            DrawSection(serialized, "evaluation", "EVALUATION — thresholds and safety expectations");
            DrawSection(serialized, "capture", "CAPTURE — bounded raw telemetry");
            DrawSection(serialized, "safety", "SAFETY — fixture and time budgets");
            DrawSection(serialized, "temporaryOverrides", "TEMPORARY OVERRIDES — runtime copy only");
            DrawSection(serialized, "referenceEvidence", "REFERENCE EVIDENCE — provenance, not reconstructed telemetry");
            bool changed = EditorGUI.EndChangeCheck();
            if (changed)
            {
                serialized.ApplyModifiedProperties();
                StopRunner(false);
                InvalidateEffectiveCache();
                SceneView.RepaintAll();
            }
            else
            {
                serialized.ApplyModifiedProperties();
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(Busy))
                {
                    if (GUILayout.Button("Validate")) Safe(ValidateDefinition);
                    if (GUILayout.Button("Open Asset")) AssetDatabase.OpenAsset(definition);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(Busy || EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button("Apply Overrides to Vehicle…")) Safe(ApplyOverridesToSource);
                    if (GUILayout.Button("Save Experiment"))
                    {
                        AssetDatabase.SaveAssetIfDirty(definition);
                        SetMessage("Saved experiment asset.", MessageType.Info);
                    }
                }
            }

            showEffectiveConfiguration = EditorGUILayout.Foldout(showEffectiveConfiguration, "EFFECTIVE CONFIGURATION", true);
            if (showEffectiveConfiguration) DrawEffectiveConfiguration();
            EditorGUILayout.EndScrollView();
        }

        private void DrawWorkbench()
        {
            workbenchScroll = EditorGUILayout.BeginScrollView(workbenchScroll);
            EditorGUILayout.Space(6f);
            view = GUILayout.Toolbar(view, new[]
            {
                "Vehicle", "Configuration", "Test Track", "Experiments",
                "Telemetry", "Comparison", "Calibration", "Regression"
            });
            EditorGUILayout.Space(6f);

            switch (view)
            {
                case 0: DrawVehicleView(); break;
                case 1: DrawConfigurationView(); break;
                case 2: DrawTrackView(); break;
                case 3: DrawExperimentsView(); break;
                case 4: DrawTelemetryView(); break;
                case 5: DrawComparisonView(); break;
                case 6: DrawCalibrationView(); break;
                case 7: DrawRegressionView(); break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawVehicleView()
        {
            EditorGUILayout.LabelField("SHARED VEHICLE CONTRACT", EditorStyles.boldLabel);
            if (definition == null)
            {
                EditorGUILayout.HelpBox("No experiment selected.", MessageType.Info);
                return;
            }

            if (!TryValidate(out string failure)) EditorGUILayout.HelpBox(failure, MessageType.Error);
            else EditorGUILayout.HelpBox("Vehicle setup is valid for the isolated fixture. The runner will clone this setup and clear its upgrade list before applying the resolved effective tuning.", MessageType.Info);

            string vehicleFingerprint = definition.vehicle == null ? "<unavailable>" : RacingLineSnapshot.VehicleFingerprintOf(definition.vehicle);
            EditorGUILayout.SelectableLabel("Vehicle fingerprint\n" + vehicleFingerprint, GUILayout.Height(35f));
            EditorGUILayout.LabelField("Simulation revision", VehicleController.SimulationRevision);
            EditorGUILayout.LabelField("Unity / platform", Application.unityVersion + " / " + Application.platform);
            if (definition.vehicle != null)
            {
                EditorGUILayout.LabelField("Physics prefab", definition.vehicle.physicsPrefab == null ? "Synthetic four-wheel fixture" : definition.vehicle.physicsPrefab.name);
                EditorGUILayout.LabelField("Dimensions", FormatVector(definition.vehicle.dimensions) + " m");
                EditorGUILayout.LabelField("Wheelbase / track", definition.vehicle.wheelbase.ToString("F2") + " / " + definition.vehicle.trackWidth.ToString("F2") + " m");
                EditorGUILayout.LabelField("Upgrades in source setup", definition.vehicle.upgrades == null ? "<missing>" : definition.vehicle.upgrades.Length.ToString());
                if (GUILayout.Button("Select vehicle setup")) Selection.activeObject = definition.vehicle;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("AVAILABLE CHANNELS", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Measured", "pose, velocity, acceleration, speed, yaw, lateral acceleration, slip angle, raw/final input, wheel contact/load/slip/force, RPM, gear, drivetrain torque, assists, nitrous, collisions");
            EditorGUILayout.LabelField("Unavailable", "presentation FOV/shake/audio, mechanical damage, original NFS input trace and any channel not exposed by the shared vehicle");
            EditorGUILayout.HelpBox("Unavailable is explicit. The lab never turns a third-person reference video into invented steering, tire-force or world-space telemetry.", MessageType.Info);
        }

        private void DrawConfigurationView()
        {
            EditorGUILayout.LabelField("RESOLUTION ORDER", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Vehicle tuning → performance upgrade build → lab scenario override. The table is read-only until the explicit Apply Overrides to Vehicle command is used.", MessageType.Info);
            DrawEffectiveConfiguration();
            if (definition != null && definition.temporaryOverrides != null && definition.temporaryOverrides.Length > 0)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("OVERRIDE DIFF", EditorStyles.boldLabel);
                EnsureEffectiveCache();
                for (int i = 0; i < definition.temporaryOverrides.Length; i++)
                {
                    VehiclePhysicsLabTuningOverride item = definition.temporaryOverrides[i];
                    if (!item.enabled || !effectiveValues.ContainsKey(item.parameter)) continue;
                    float finalValue = effectiveValues[item.parameter];
                    EditorGUILayout.LabelField(item.parameter.ToString(), "scenario " + item.value.ToString("R") + " → effective " + finalValue.ToString("R"));
                }
            }
        }

        private void DrawTrackView()
        {
            EditorGUILayout.LabelField("DISPOSABLE TEST TRACK", EditorStyles.boldLabel);
            if (definition == null || definition.track == null)
            {
                EditorGUILayout.HelpBox("Assign a Vehicle Physics Lab Track in the inspector.", MessageType.Error);
                return;
            }

            VehiclePhysicsLabTrack track = definition.track;
            if (!track.IsValid(out string failure)) EditorGUILayout.HelpBox(failure, MessageType.Error);
            EditorGUILayout.LabelField("Track", track.displayName + " · " + track.kind);
            EditorGUILayout.LabelField("Length / width", track.length.ToString("F1") + " / " + track.width.ToString("F1") + " m");
            EditorGUILayout.LabelField("Surface", track.surfaceId + " · grip ×" + track.surfaceGrip.ToString("F2") + " · rolling ×" + track.surfaceRollingResistance.ToString("F2"));

            VehiclePhysicsLabTrackSample start = track.Sample(0f);
            VehiclePhysicsLabTrackSample middle = track.Sample(track.length * 0.5f);
            VehiclePhysicsLabTrackSample end = track.Sample(track.length);
            EditorGUILayout.LabelField("Start frame", FormatVector(start.position) + " forward " + FormatVector(start.forward));
            EditorGUILayout.LabelField("Middle frame", FormatVector(middle.position) + " forward " + FormatVector(middle.forward));
            EditorGUILayout.LabelField("End frame", FormatVector(end.position) + " forward " + FormatVector(end.forward));
            EditorGUILayout.HelpBox("The runner creates actual colliders in a preview physics scene. The scene anchor is authoring-only and is never used as a second road system.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select track asset")) Selection.activeObject = track;
                if (GUILayout.Button("Create scene anchor")) Safe(() => CreateTrackAnchorInScene(track));
                if (GUILayout.Button("Frame track")) FrameTrack(track);
            }
            DrawTrackMiniChart(track);
        }

        private void DrawExperimentsView()
        {
            EditorGUILayout.LabelField("EXPERIMENT CONTROL", EditorStyles.boldLabel);
            if (definition == null)
            {
                EditorGUILayout.HelpBox("No experiment selected.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Case", definition.displayName + " · " + definition.experiment);
            EditorGUILayout.LabelField("Input", definition.input == null ? "<invalid>" : definition.input.mode.ToString());
            EditorGUILayout.LabelField("Manual order", "control/input → VehicleController.StepSimulation → preview PhysicsScene.Simulate → telemetry capture");
            EditorGUILayout.LabelField("Warmup", definition.warmupSeconds.ToString("F2") + " s · measurement starts after warmup");
            DrawRunControls();
            if (definition.input != null && definition.input.mode == VehiclePhysicsLabInputMode.Live)
            {
                EditorGUILayout.HelpBox("Live editor controls: W throttle, S brake, A/D steering, Space handbrake, Left/Right Shift nitrous. Live input is useful for qualitative inspection; use recorded/generated input for reproducible regression.", MessageType.Info);
            }
            if (runner != null)
            {
                EditorGUI.ProgressBar(GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true)), runner.Progress,
                    "Running " + runner.LiveSamples.Count + " captured samples · " + runner.Report.elapsedSeconds.ToString("F2") + " s");
            }
            DrawRecentReports(5);
        }

        private void DrawTelemetryView()
        {
            EditorGUILayout.LabelField("RAW PHYSICS TELEMETRY", EditorStyles.boldLabel);
            VehiclePhysicsLabRunReport report = SelectedReport;
            VehiclePhysicsLabSample[] samples = runner != null && runner.LiveSamples.Count > 0
                ? runner.LiveSamples.ToArray()
                : report == null ? Array.Empty<VehiclePhysicsLabSample>() : report.samples;
            if (report == null && runner == null)
            {
                EditorGUILayout.HelpBox("Run an experiment or import a report JSON. Charts preserve the report's measurement clock; no arbitrary time shift is applied.", MessageType.Info);
                DrawRecentReports(8);
                return;
            }

            if (report != null) DrawReportHeader(report);
            if (samples.Length == 0)
            {
                EditorGUILayout.HelpBox("No samples are currently available. Check capture settings or the failure diagnostics.", MessageType.Warning);
                return;
            }

            telemetryCursor = Mathf.Clamp(telemetryCursor < 0 ? samples.Length - 1 : telemetryCursor, 0, samples.Length - 1);
            telemetryCursor = EditorGUILayout.IntSlider("Cursor", telemetryCursor, 0, samples.Length - 1);
            VehiclePhysicsLabSample selected = samples[telemetryCursor];
            EditorGUILayout.LabelField("Sample", selected.tick + " · t=" + selected.measurementTime.ToString("F3") + " s · speed=" + selected.speedKph.ToString("F1") + " km/h");
            EditorGUILayout.LabelField("Input raw / final", FormatInput(selected.rawInput) + " → " + FormatInput(selected.finalInput));
            EditorGUILayout.LabelField("Handling / assist", selected.handling + " · yaw assist " + selected.assistYawTorque.ToString("F1") + " N m");
            EditorGUILayout.LabelField("Wheels", selected.groundedWheels + " grounded · average longitudinal slip " + selected.averageLongitudinalSlip.ToString("F3"));

            PlotSamples("Speed — km/h", samples, s => s.speedKph);
            PlotSamples("Acceleration — m/s²", samples, s => s.acceleration.magnitude, s => s.lateralAccelerationMps2);
            PlotSamples("Yaw / slip — rad/s and degrees", samples, s => s.yawRateRadPerSec, s => s.slipAngleValid ? s.slipAngleDegrees : 0f);
            PlotInputs(samples);
            PlotSamples("Wheel longitudinal force — N", samples, s => AverageWheel(s, wheel => wheel.longitudinalForce));
            PlotSamples("Wheel load / surface grip", samples, s => AverageWheel(s, wheel => wheel.normalLoad), s => AverageWheel(s, wheel => wheel.surfaceGrip));
            EditorGUILayout.HelpBox("Acceleration is a finite difference of post-physics velocity samples. Slip angle is invalid below the stable speed threshold and is shown as zero in the chart; the raw validity flag remains in JSON/CSV.", MessageType.Info);
        }

        private void DrawComparisonView()
        {
            EditorGUILayout.LabelField("TIME-ALIGNED COMPARISON", EditorStyles.boldLabel);
            if (history.Count < 2)
            {
                EditorGUILayout.HelpBox("Run or import at least two reports. Comparisons align the candidate to the baseline's measurement timestamps and never hide latency with an arbitrary shift.", MessageType.Info);
                DrawRecentReports(8);
                return;
            }

            string[] names = history.Select(ReportLabel).ToArray();
            comparisonBaselineIndex = EditorGUILayout.Popup("Baseline", Mathf.Clamp(comparisonBaselineIndex, 0, history.Count - 1), names);
            comparisonCandidateIndex = EditorGUILayout.Popup("Candidate", Mathf.Clamp(comparisonCandidateIndex, 0, history.Count - 1), names);
            VehiclePhysicsLabRunReport baseline = history[comparisonBaselineIndex];
            VehiclePhysicsLabRunReport candidate = history[comparisonCandidateIndex];
            if (ReferenceEquals(baseline, candidate))
            {
                EditorGUILayout.HelpBox("Choose two different reports.", MessageType.Warning);
                return;
            }

            DrawReportHeader(baseline, "Baseline");
            DrawReportHeader(candidate, "Candidate");
            DrawMetricComparison(baseline, candidate);
            VehiclePhysicsLabComparisonSample[] comparison = VehiclePhysicsLabAnalysis.CompareByTime(baseline.samples, candidate.samples);
            PlotComparison("Speed delta — candidate minus baseline (km/h)", comparison, c => c.speedDeltaKph);
            PlotComparison("Lateral acceleration delta — m/s²", comparison, c => c.lateralAccelerationDelta);
            PlotComparison("Yaw-rate delta — rad/s", comparison, c => c.yawRateDelta);
            PlotComparison("Slip-angle delta — degrees", comparison, c => c.slipAngleDeltaDegrees);
            if (comparison.Length == 0) EditorGUILayout.HelpBox("The reports have no overlapping measurement timestamps; no fabricated alignment was created.", MessageType.Warning);
        }

        private void DrawCalibrationView()
        {
            EditorGUILayout.LabelField("CAPABILITY PROFILE BAKE", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Baking is an explicit derived-data operation. It requires a measured report, records the run and fingerprints, and leaves unsupported regions as existing assumptions instead of extrapolating them as measured.", MessageType.Info);
            VehiclePhysicsLabRunReport report = SelectedReport;
            if (definition == null)
            {
                EditorGUILayout.HelpBox("No experiment selected.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Publish target", definition.publishToCapability == null ? "<none>" : definition.publishToCapability.name);
            if (report != null) DrawReportHeader(report);
            using (new EditorGUI.DisabledScope(report == null || definition.publishToCapability == null || Busy))
            {
                if (GUILayout.Button("Bake selected report to capability profile")) Safe(() => BakeCapability(report));
            }
            if (definition.publishToCapability != null)
            {
                RacingCapabilityProfile profile = definition.publishToCapability;
                EditorGUILayout.LabelField("Profile vehicle fingerprint", string.IsNullOrEmpty(profile.vehicleFingerprint) ? "<none>" : profile.vehicleFingerprint);
                EditorGUILayout.LabelField("Measured channels", "longitudinal=" + profile.longitudinalMeasured + " · lateral=" + profile.lateralMeasured);
                EditorGUILayout.LabelField("Physics Lab run", string.IsNullOrEmpty(profile.physicsLabRunId) ? "<none>" : profile.physicsLabRunId);
                if (!string.IsNullOrEmpty(profile.physicsLabEvidence)) EditorGUILayout.HelpBox(profile.physicsLabEvidence, MessageType.Info);
                if (GUILayout.Button("Select capability profile")) Selection.activeObject = profile;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("REFERENCE EVIDENCE", EditorStyles.boldLabel);
            if (definition.referenceEvidence == null || definition.referenceEvidence.Length == 0)
                EditorGUILayout.HelpBox("No reference source metadata is attached. Add a video, still capture or legitimate telemetry file to record provenance and uncertainty.", MessageType.Info);
            else
                for (int i = 0; i < definition.referenceEvidence.Length; i++)
                {
                    VehiclePhysicsLabReferenceEvidence evidence = definition.referenceEvidence[i];
                    if (evidence == null) continue;
                    EditorGUILayout.LabelField((i + 1) + ". " + evidence.sourcePath, evidence.frameRate.ToString("F2") + " fps · timing ±" + evidence.timingUncertaintySeconds.ToString("F3") + " s");
                    if (!string.IsNullOrEmpty(evidence.annotations)) EditorGUILayout.HelpBox(evidence.annotations, MessageType.None);
                }
            if (GUILayout.Button("Attach reference evidence metadata…")) Safe(AddReferenceEvidence);
        }

        private void DrawRegressionView()
        {
            EditorGUILayout.LabelField("REGRESSION SUITE", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Each suite entry creates a fresh isolated rig. A timeout or cancellation is incomplete and cannot be reported as a pass. Suite execution does not use or mutate gameplay saves, wallet state or garage selection.", MessageType.Info);
            if (suite == null)
            {
                EditorGUILayout.HelpBox("Assign a Vehicle Physics Lab Suite in the toolbar, or create one below.", MessageType.Info);
                if (GUILayout.Button("New Suite")) Safe(CreateNewSuite);
            }
            else
            {
                if (!suite.IsValid(out string failure)) EditorGUILayout.HelpBox(failure, MessageType.Error);
                EditorGUILayout.LabelField("Suite", suite.displayName);
                EditorGUILayout.LabelField("Experiments / repetitions", (suite.experiments == null ? 0 : suite.experiments.Length) + " / " + suite.repetitions);
                if (suite.experiments != null)
                    for (int i = 0; i < suite.experiments.Length; i++)
                        EditorGUILayout.LabelField((i + 1) + ".", suite.experiments[i] == null ? "<missing>" : suite.experiments[i].displayName);
                using (new EditorGUI.DisabledScope(Busy || !suite.IsValid(out _)))
                {
                    if (GUILayout.Button("Run suite")) Safe(BeginSuite);
                }
                if (runningSuite)
                {
                    EditorGUILayout.LabelField("Suite progress", suiteCompleted + " completed · " + suiteFailures + " failed · next " + (suiteExperimentIndex + 1) + "/" + suite.experiments.Length + " repetition " + (suiteRepetition + 1) + "/" + suite.repetitions);
                    if (GUILayout.Button("Cancel suite")) Safe(CancelRun);
                }
                if (GUILayout.Button("Select suite asset")) Selection.activeObject = suite;
            }
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("PARAMETER SWEEP", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Sweeps vary approved tuning parameters over bounded ranges. Every trial gets a fresh isolated rig; results remain candidates until a designer explicitly applies or publishes them.", MessageType.Info);
            if (sweep == null)
            {
                EditorGUILayout.HelpBox("Assign a Vehicle Physics Lab Sweep in the toolbar, or create one below.", MessageType.Info);
                if (GUILayout.Button("New Sweep")) Safe(CreateNewSweep);
            }
            else
            {
                if (!sweep.IsValid(out string sweepFailure)) EditorGUILayout.HelpBox(sweepFailure, MessageType.Error);
                EditorGUILayout.LabelField("Sweep", sweep.displayName);
                EditorGUILayout.LabelField("Trials / budget", sweep.TrialCount + " / " + sweep.maximumTrials);
                if (sweep.axes != null)
                    for (int i = 0; i < sweep.axes.Length; i++)
                    {
                        VehiclePhysicsLabSweepAxis axis = sweep.axes[i];
                        if (axis == null || !axis.enabled) continue;
                        EditorGUILayout.LabelField((i + 1) + ". " + axis.parameter, axis.minimum.ToString("R") + " → " + axis.maximum.ToString("R") + " · " + axis.steps + " steps");
                    }
                using (new EditorGUI.DisabledScope(Busy || !sweep.IsValid(out _)))
                {
                    if (GUILayout.Button("Run parameter sweep")) Safe(BeginSweep);
                }
                if (sweepRunner != null)
                {
                    EditorGUILayout.LabelField("Sweep progress", sweepRunner.CompletedCount + " / " + sweepRunner.TrialCount + " completed · " + sweepRunner.Progress.ToString("P0"));
                    EditorGUILayout.LabelField("Current trial", sweepRunner.CurrentLabel);
                    if (GUILayout.Button("Cancel sweep")) Safe(CancelRun);
                }
                if (GUILayout.Button("Select sweep asset")) Selection.activeObject = sweep;
            }
            EditorGUILayout.Space(8f);
            DrawRecentReports(16);
        }

        private void DrawRunControls()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(definition == null || Busy || EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button("Run isolated experiment")) Safe(() => BeginRun(definition));
                }
                using (new EditorGUI.DisabledScope(!Busy))
                {
                    if (GUILayout.Button("Cancel and retain partial report")) Safe(CancelRun);
                }
            }
            if (runner != null)
            {
                EditorGUILayout.HelpBox("The preview rig is manually stepped. No FixedUpdate or default-scene simulation is used by this run.", MessageType.Info);
                if (GUILayout.Button("Advance one editor work slice")) Safe(() => runner.Advance(1d));
            }
            if (sweepRunner != null)
            {
                EditorGUILayout.HelpBox("The sweep is executing trials sequentially. The current trial is manually stepped and will be disposed before the next trial starts.", MessageType.Info);
                if (GUILayout.Button("Advance sweep one editor work slice")) Safe(() => sweepRunner.Advance(1d));
            }
        }

        private void DrawReportHeader(VehiclePhysicsLabRunReport report, string prefix = "Selected report")
        {
            if (report == null) return;
            MessageType type = report.Passed ? MessageType.Info : report.status == VehiclePhysicsLabResultStatus.Running ? MessageType.Warning : MessageType.Error;
            EditorGUILayout.HelpBox(prefix + ": " + report.status + " · complete=" + report.complete + " · target=" + report.targetReached
                + " · samples=" + (report.samples == null ? 0 : report.samples.Length) + " · " + report.failureCode, type);
            EditorGUILayout.SelectableLabel("Run " + report.runId + "\nDefinition " + report.definitionFingerprint + "\nVehicle " + report.vehicleFingerprint, GUILayout.Height(65f));
            if (!string.IsNullOrEmpty(report.failureMessage)) EditorGUILayout.HelpBox(report.failureMessage, MessageType.Error);
            if (report.metrics != null)
                EditorGUILayout.LabelField("Measured duration", VehiclePhysicsLabAnalysis.FindMetric(report.metrics, "duration").value.ToString("F3") + " s");
        }

        private void DrawRecentReports(int max)
        {
            if (history.Count == 0) return;
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("RECENT REPORTS", EditorStyles.boldLabel);
            for (int i = 0; i < Mathf.Min(max, history.Count); i++)
            {
                VehiclePhysicsLabRunReport report = history[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(ReportLabel(report), GUILayout.Width(285f))) selectedReportIndex = i;
                    GUILayout.Label(report.status + " · " + (report.samples == null ? 0 : report.samples.Length) + " samples");
                    if (GUILayout.Button("JSON", GUILayout.Width(48f))) Safe(() => ExportJson(report));
                    if (GUILayout.Button("CSV", GUILayout.Width(42f))) Safe(() => ExportCsv(report));
                }
            }
        }

        private void DrawMetricComparison(VehiclePhysicsLabRunReport baseline, VehiclePhysicsLabRunReport candidate)
        {
            VehiclePhysicsLabMetric[] baselineMetrics = baseline.metrics ?? Array.Empty<VehiclePhysicsLabMetric>();
            VehiclePhysicsLabMetric[] candidateMetrics = candidate.metrics ?? Array.Empty<VehiclePhysicsLabMetric>();
            var keys = new List<string>();
            foreach (VehiclePhysicsLabMetric metric in baselineMetrics) if (!keys.Contains(metric.key)) keys.Add(metric.key);
            foreach (VehiclePhysicsLabMetric metric in candidateMetrics) if (!keys.Contains(metric.key)) keys.Add(metric.key);
            EditorGUILayout.LabelField("METRIC DELTAS", EditorStyles.boldLabel);
            foreach (string key in keys)
            {
                VehiclePhysicsLabMetric a = VehiclePhysicsLabAnalysis.FindMetric(baselineMetrics, key);
                VehiclePhysicsLabMetric b = VehiclePhysicsLabAnalysis.FindMetric(candidateMetrics, key);
                if (!a.available || !b.available)
                {
                    EditorGUILayout.LabelField(key, "unavailable in baseline or candidate");
                    continue;
                }
                EditorGUILayout.LabelField(key, a.value.ToString("F3") + " → " + b.value.ToString("F3") + " (Δ " + (b.value - a.value).ToString("F3") + " " + b.unit + ")");
            }
        }

        private void DrawEffectiveConfiguration()
        {
            if (definition == null || definition.vehicle == null)
            {
                EditorGUILayout.HelpBox("Effective configuration unavailable until a valid vehicle setup is assigned.", MessageType.Warning);
                return;
            }
            EnsureEffectiveCache();
            if (!string.IsNullOrEmpty(effectiveError))
            {
                EditorGUILayout.HelpBox(effectiveError, MessageType.Error);
                return;
            }
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Final value", "Source / legal range", EditorStyles.miniBoldLabel);
            foreach (VehiclePhysicsLabTuningParameter parameter in Parameters)
            {
                if (!effectiveValues.TryGetValue(parameter, out float value)) continue;
                VehiclePhysicsLabTuningOverrides.TryGetRange(parameter, out float minimum, out float maximum);
                EditorGUILayout.LabelField(parameter.ToString(), value.ToString("R") + " · " + ConfigurationSource(parameter)
                    + " · [" + minimum.ToString("R") + ", " + maximum.ToString("R") + "]");
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.LabelField("Resolution fingerprint", effectiveFingerprint ?? "<none>");
        }

        private void EnsureEffectiveCache()
        {
            if (definition == null) return;
            string fingerprint;
            try { fingerprint = VehiclePhysicsLabEditorOperations.Fingerprint(definition); }
            catch (Exception exception) { effectiveError = exception.Message; effectiveFingerprint = string.Empty; return; }
            if (fingerprint == effectiveFingerprint && effectiveValues != null) return;
            effectiveFingerprint = fingerprint;
            effectiveValues = new Dictionary<VehiclePhysicsLabTuningParameter, float>();
            effectiveError = string.Empty;
            try
            {
                VehicleTuning tuning = VehiclePhysicsLabEditorOperations.CreateEffectiveTuning(definition, out _);
                try
                {
                    foreach (VehiclePhysicsLabTuningParameter parameter in Parameters)
                        effectiveValues[parameter] = VehiclePhysicsLabTuningOverrides.Read(tuning, parameter);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(tuning);
                }
            }
            catch (Exception exception)
            {
                effectiveValues.Clear();
                effectiveError = exception.Message;
            }
        }

        private void ApplyOverridesToSource()
        {
            if (definition == null || definition.vehicle == null || definition.vehicle.tuning == null)
                throw new ArgumentException("Assign a vehicle setup with a tuning asset first.");
            VehiclePhysicsLabTuningOverride[] overrides = definition.temporaryOverrides ?? Array.Empty<VehiclePhysicsLabTuningOverride>();
            if (!overrides.Any(value => value.enabled)) throw new ArgumentException("There are no enabled scenario overrides to apply.");
            var invalid = new List<string>();
            for (int i = 0; i < overrides.Length; i++)
            {
                if (!overrides[i].enabled) continue;
                if (!VehiclePhysicsLabTuningOverrides.TryGetRange(overrides[i].parameter, out float minimum, out float maximum)
                    || overrides[i].value < minimum || overrides[i].value > maximum || float.IsNaN(overrides[i].value) || float.IsInfinity(overrides[i].value))
                    invalid.Add(overrides[i].parameter + "=" + overrides[i].value.ToString("R"));
            }
            if (invalid.Count > 0) throw new ArgumentException("Invalid override values: " + string.Join(", ", invalid));
            bool hasUpgrades = definition.vehicle.upgrades != null && definition.vehicle.upgrades.Length > 0;
            string warning = hasUpgrades
                ? "This applies scenario values to the base tuning asset before the setup's performance upgrades. The effective experiment may still resolve upgrade values afterwards. Continue?"
                : "This changes the canonical tuning asset and saves it. The operation is Undoable. Continue?";
            if (!EditorUtility.DisplayDialog("Apply Physics Lab Overrides", warning, "Apply", "Cancel")) return;

            VehicleTuning source = definition.vehicle.tuning;
            Undo.RecordObject(source, "Apply Vehicle Physics Lab overrides");
            if (!VehiclePhysicsLabTuningOverrides.Apply(source, overrides, out string[] diagnostics))
                throw new ArgumentException(string.Join("; ", diagnostics));
            RacingLineSnapshot.ValidateTuning(source);
            EditorUtility.SetDirty(source);
            AssetDatabase.SaveAssetIfDirty(source);
            InvalidateEffectiveCache();
            SetMessage("Applied " + overrides.Count(value => value.enabled) + " override(s) to " + source.name + ". Use Undo to restore the source asset.", MessageType.Info);
        }

        private void BeginRun(VehiclePhysicsLabDefinition target)
        {
            if (target == null) throw new ArgumentException("Choose a Vehicle Physics Lab experiment first.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before running an isolated lab experiment.");
            StopRunner(false);
            runningSuite = false;
            activeRunDefinition = target;
            runner = new VehiclePhysicsLabRunner(target, VehiclePhysicsLabLiveInput.ReadKeyboard);
            SetMessage("Running " + target.displayName + " in an isolated preview physics scene…", MessageType.Info);
        }

        private void BeginSuite()
        {
            if (suite == null) throw new ArgumentException("Choose a valid suite.");
            if (!suite.IsValid(out string failure)) throw new ArgumentException(failure);
            StopRunner(false);
            runningSuite = true;
            suiteExperimentIndex = 0;
            suiteRepetition = 0;
            suiteCompleted = 0;
            suiteFailures = 0;
            StartNextSuiteRun();
        }

        private void BeginSweep()
        {
            if (sweep == null) throw new ArgumentException("Choose a valid parameter sweep first.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before running an isolated lab sweep.");
            if (!sweep.IsValid(out string failure)) throw new ArgumentException(failure);
            StopRunner(false);
            runningSuite = false;
            sweepRunner = new VehiclePhysicsLabSweepRunner(sweep, VehiclePhysicsLabLiveInput.ReadKeyboard);
            SetMessage("Running " + sweep.displayName + " · " + sweep.TrialCount + " isolated trial(s)…", MessageType.Info);
        }

        private void StartNextSuiteRun()
        {
            if (!runningSuite || suite == null || suiteExperimentIndex >= suite.experiments.Length)
            {
                runningSuite = false;
                SetMessage("Regression suite complete: " + suiteCompleted + " run(s), " + suiteFailures + " failure(s).", suiteFailures == 0 ? MessageType.Info : MessageType.Warning);
                return;
            }
            VehiclePhysicsLabDefinition next = suite.experiments[suiteExperimentIndex];
            activeRunDefinition = next;
            runner = new VehiclePhysicsLabRunner(next, VehiclePhysicsLabLiveInput.ReadKeyboard);
            SetMessage("Suite run " + (suiteCompleted + 1) + ": " + next.displayName + " · repetition " + (suiteRepetition + 1) + "/" + suite.repetitions, MessageType.Info);
        }

        private void UpdateRunner()
        {
            if (sweepRunner != null)
            {
                UpdateSweepRunner();
                return;
            }
            if (runner == null) return;
            try
            {
                runner.Advance(8d);
                if (!runner.IsDone) { Repaint(); SceneView.RepaintAll(); return; }
                VehiclePhysicsLabRunReport completed = runner.Report;
                runner.Dispose();
                runner = null;
                AddReport(completed);
                if (!runningSuite) SetMessage("Run finished: " + completed.status + (string.IsNullOrEmpty(completed.failureCode) ? string.Empty : " · " + completed.failureCode), completed.Passed ? MessageType.Info : MessageType.Warning);
                else
                {
                    suiteCompleted++;
                    if (!completed.Passed) suiteFailures++;
                    bool stop = !completed.Passed && suite.stopOnFailure;
                    if (stop)
                    {
                        runningSuite = false;
                        SetMessage("Suite stopped after failure: " + completed.failureCode, MessageType.Error);
                    }
                    else
                    {
                        suiteRepetition++;
                        if (suiteRepetition >= suite.repetitions)
                        {
                            suiteRepetition = 0;
                            suiteExperimentIndex++;
                        }
                        StartNextSuiteRun();
                    }
                }
                Repaint();
                SceneView.RepaintAll();
            }
            catch (Exception exception)
            {
                StopRunner(false);
                SetMessage(exception.Message, MessageType.Error);
                Debug.LogWarning("PHYSICS_LAB: " + exception);
            }
        }

        private void UpdateSweepRunner()
        {
            try
            {
                sweepRunner.Advance(8d);
                if (!sweepRunner.IsDone) { Repaint(); SceneView.RepaintAll(); return; }
                VehiclePhysicsLabRunReport[] completed = sweepRunner.Reports.ToArray();
                bool allPassed = completed.Length > 0 && completed.All(report => report != null && report.Passed);
                sweepRunner.Dispose();
                sweepRunner = null;
                for (int i = 0; i < completed.Length; i++) AddReport(completed[i]);
                SetMessage("Sweep finished: " + completed.Length + " trial(s) · " + (allPassed ? "all passed" : "review failures or incomplete trials"),
                    allPassed ? MessageType.Info : MessageType.Warning);
                Repaint();
                SceneView.RepaintAll();
            }
            catch (Exception exception)
            {
                StopRunner(false);
                SetMessage(exception.Message, MessageType.Error);
                Debug.LogWarning("PHYSICS_LAB_SWEEP: " + exception);
            }
        }

        private void CancelRun()
        {
            runningSuite = false;
            StopRunner(true);
            SetMessage("Run cancelled; partial report retained in the report list.", MessageType.Warning);
        }

        private void StopRunner(bool retainReport)
        {
            if (sweepRunner != null)
            {
                if (retainReport && !sweepRunner.IsDone) sweepRunner.Cancel();
                VehiclePhysicsLabRunReport[] partialSweep = sweepRunner.Reports.ToArray();
                sweepRunner.Dispose();
                sweepRunner = null;
                if (retainReport)
                    for (int i = 0; i < partialSweep.Length; i++) AddReport(partialSweep[i]);
            }
            if (runner != null)
            {
                if (!runner.IsDone) runner.Cancel();
                VehiclePhysicsLabRunReport partial = runner.Report;
                runner.Dispose();
                runner = null;
                activeRunDefinition = null;
                if (retainReport && partial != null) AddReport(partial);
            }
            SceneView.RepaintAll();
        }

        private void AddReport(VehiclePhysicsLabRunReport report)
        {
            if (report == null) return;
            history.Insert(0, report);
            while (history.Count > 16) history.RemoveAt(history.Count - 1);
            selectedReportIndex = 0;
            comparisonCandidateIndex = 0;
            comparisonBaselineIndex = Mathf.Min(1, history.Count - 1);
            telemetryCursor = -1;
        }

        private VehiclePhysicsLabRunReport SelectedReport
        {
            get
            {
                if (history.Count == 0) return null;
                selectedReportIndex = Mathf.Clamp(selectedReportIndex, 0, history.Count - 1);
                return history[selectedReportIndex];
            }
        }

        private void ValidateDefinition()
        {
            if (!TryValidate(out string failure)) throw new ArgumentException(failure);
            EnsureEffectiveCache();
            SetMessage("Experiment and canonical vehicle setup are valid. Effective fingerprint: " + effectiveFingerprint, MessageType.Info);
        }

        private bool TryValidate(out string failure)
        {
            if (definition == null)
            {
                failure = "Select an experiment.";
                return false;
            }
            return definition.IsValid(out failure);
        }

        private void BakeCapability(VehiclePhysicsLabRunReport report)
        {
            if (!VehiclePhysicsLabEditorOperations.TryBakeCapability(definition.publishToCapability, report, out string failure))
                throw new ArgumentException(failure);
            SetMessage("Capability profile baked from measured run " + report.runId + ". Racing Line Studio and AI consumers should revalidate this fingerprint.", MessageType.Info);
        }

        private void AddReferenceEvidence()
        {
            if (definition == null) throw new ArgumentException("Select an experiment first.");
            string path = EditorUtility.OpenFilePanel("Attach observational reference source", "", "");
            if (string.IsNullOrEmpty(path)) return;
            string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
            string normalized = path.Replace('\\', '/');
            if (normalized.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase)) normalized = normalized.Substring(projectRoot.Length + 1);
            var evidence = new List<VehiclePhysicsLabReferenceEvidence>(definition.referenceEvidence ?? Array.Empty<VehiclePhysicsLabReferenceEvidence>())
            {
                new VehiclePhysicsLabReferenceEvidence
                {
                    sourcePath = normalized,
                    provenance = "User-attached observational source",
                    vehicleSetup = definition.vehicle == null ? string.Empty : definition.vehicle.name,
                    timingUncertaintySeconds = 0.1f,
                    annotations = "Reference evidence is qualitative until frame rate, synchronization and measurement provenance are supplied. No steering or tire-force trace was reconstructed."
                }
            };
            Undo.RecordObject(definition, "Attach Physics Lab reference evidence");
            definition.referenceEvidence = evidence.ToArray();
            EditorUtility.SetDirty(definition);
            SetMessage("Attached reference provenance: " + normalized, MessageType.Info);
        }

        private void CreateNewExperiment()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before creating assets.");
            string path = EditorUtility.SaveFilePanelInProject("New Vehicle Physics Lab experiment", "PhysicsLabExperiment", "asset", "Choose the experiment asset location.");
            if (string.IsNullOrEmpty(path)) return;
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            string folder = Path.GetDirectoryName(path).Replace('\\', '/');
            VehiclePhysicsLabTrack track = Selection.activeObject as VehiclePhysicsLabTrack;
            if (track == null) track = FindFirstAsset<VehiclePhysicsLabTrack>();
            if (track == null)
            {
                track = ScriptableObject.CreateInstance<VehiclePhysicsLabTrack>();
                track.name = "PhysicsLabTrack";
                AssetDatabase.CreateAsset(track, AssetDatabase.GenerateUniqueAssetPath(folder + "/PhysicsLabTrack.asset"));
            }
            RacingVehicleSetup vehicle = Selection.activeObject as RacingVehicleSetup;
            if (vehicle == null) vehicle = FindFirstAsset<RacingVehicleSetup>();
            if (vehicle == null)
            {
                VehicleTuning tuning = VehicleTuning.CreateStreetRacer();
                tuning.name = "PhysicsLabTuning";
                AssetDatabase.CreateAsset(tuning, AssetDatabase.GenerateUniqueAssetPath(folder + "/PhysicsLabTuning.asset"));
                vehicle = ScriptableObject.CreateInstance<RacingVehicleSetup>();
                vehicle.name = "PhysicsLabVehicle";
                vehicle.tuning = tuning;
                AssetDatabase.CreateAsset(vehicle, AssetDatabase.GenerateUniqueAssetPath(folder + "/PhysicsLabVehicle.asset"));
            }
            VehiclePhysicsLabDefinition created = ScriptableObject.CreateInstance<VehiclePhysicsLabDefinition>();
            created.name = Path.GetFileNameWithoutExtension(path);
            created.displayName = created.name;
            created.track = track;
            created.vehicle = vehicle;
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            SelectDefinition(created);
            SetMessage("Created " + path + ". Assign a track/vehicle if the automatically resolved assets are not the intended ones.", MessageType.Info);
        }

        private void CreateNewSuite()
        {
            string path = EditorUtility.SaveFilePanelInProject("New Vehicle Physics Lab suite", "PhysicsLabSuite", "asset", "Choose the suite asset location.");
            if (string.IsNullOrEmpty(path)) return;
            VehiclePhysicsLabSuite created = CreateInstance<VehiclePhysicsLabSuite>();
            created.name = Path.GetFileNameWithoutExtension(path);
            created.experiments = definition == null ? Array.Empty<VehiclePhysicsLabDefinition>() : new[] { definition };
            AssetDatabase.CreateAsset(created, AssetDatabase.GenerateUniqueAssetPath(path));
            AssetDatabase.SaveAssets();
            suite = created;
            if (suiteField != null) suiteField.SetValueWithoutNotify(suite);
            SetMessage("Created suite asset. Add more experiments in its inspector.", MessageType.Info);
        }

        private void CreateNewSweep()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before creating assets.");
            string path = EditorUtility.SaveFilePanelInProject("New Vehicle Physics Lab sweep", "PhysicsLabSweep", "asset", "Choose the sweep asset location.");
            if (string.IsNullOrEmpty(path)) return;
            VehiclePhysicsLabDefinition baseExperiment = definition;
            if (baseExperiment == null) baseExperiment = FindFirstAsset<VehiclePhysicsLabDefinition>();
            if (baseExperiment == null) throw new ArgumentException("Create or select a valid experiment before creating a sweep.");
            VehiclePhysicsLabSweepDefinition created = CreateInstance<VehiclePhysicsLabSweepDefinition>();
            created.name = Path.GetFileNameWithoutExtension(path);
            created.displayName = created.name;
            created.baseExperiment = baseExperiment;
            created.axes = new[]
            {
                new VehiclePhysicsLabSweepAxis
                {
                    enabled = true,
                    parameter = VehiclePhysicsLabTuningParameter.Mass,
                    minimum = 1200f,
                    maximum = 1800f,
                    steps = 3
                }
            };
            AssetDatabase.CreateAsset(created, AssetDatabase.GenerateUniqueAssetPath(path));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            sweep = created;
            if (sweepField != null) sweepField.SetValueWithoutNotify(sweep);
            SetMessage("Created sweep asset. Review its bounds, then run it from Regression.", MessageType.Info);
        }

        private static void CreateTrackAnchorInScene(VehiclePhysicsLabTrack track)
        {
            if (track == null) throw new ArgumentNullException(nameof(track));
            GameObject anchorObject = new GameObject(track.name + " Anchor");
            Undo.RegisterCreatedObjectUndo(anchorObject, "Create Physics Lab track anchor");
            VehiclePhysicsLabTrackAnchor anchor = anchorObject.AddComponent<VehiclePhysicsLabTrackAnchor>();
            anchor.track = track;
            Selection.activeGameObject = anchorObject;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        private static void FrameTrack(VehiclePhysicsLabTrack track)
        {
            if (track == null) return;
            VehiclePhysicsLabTrackAnchor anchor = FindAnchor(track);
            Vector3 position = anchor == null ? track.Sample(track.length * 0.5f).position : anchor.transform.TransformPoint(track.Sample(track.length * 0.5f).position);
            SceneView view = SceneView.lastActiveSceneView ?? GetWindow<SceneView>();
            view.LookAt(position, Quaternion.LookRotation(Vector3.forward, Vector3.up), Mathf.Max(15f, track.length * 0.65f));
            view.Repaint();
        }

        private void DrawTrackMiniChart(VehiclePhysicsLabTrack track)
        {
            var values = new Vector2[32];
            for (int i = 0; i < values.Length; i++)
            {
                float distance = track.length * i / (values.Length - 1f);
                VehiclePhysicsLabTrackSample sample = track.Sample(distance);
                values[i] = new Vector2(distance, sample.position.x);
            }
            Plot("Track lateral profile — m", values);
        }

        private void PlotInputs(VehiclePhysicsLabSample[] samples)
        {
            PlotSamples("Controls — raw steering / final steering", samples, s => s.rawInput.Steering, s => s.finalInput.Steering);
            PlotSamples("Controls — throttle / brake", samples, s => s.finalInput.Throttle, s => s.finalInput.Brake);
        }

        private static void PlotSamples(string title, VehiclePhysicsLabSample[] samples, params Func<VehiclePhysicsLabSample, float>[] selectors)
        {
            if (samples == null || samples.Length == 0) return;
            var series = new Vector2[selectors.Length][];
            for (int j = 0; j < selectors.Length; j++)
            {
                series[j] = new Vector2[samples.Length];
                for (int i = 0; i < samples.Length; i++) series[j][i] = new Vector2(samples[i].measurementTime, selectors[j](samples[i]));
            }
            Plot(title, series);
        }

        private static void PlotComparison(string title, VehiclePhysicsLabComparisonSample[] samples, Func<VehiclePhysicsLabComparisonSample, float> selector)
        {
            if (samples == null || samples.Length == 0) return;
            var values = new Vector2[samples.Length];
            for (int i = 0; i < samples.Length; i++) values[i] = new Vector2(samples[i].time, selector(samples[i]));
            Plot(title, values);
        }

        private static void Plot(string title, params Vector2[][] series)
        {
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
            Rect rect = GUILayoutUtility.GetRect(240f, 128f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.075f, 0.085f, 0.095f));
            float xMin = 0f;
            float xMax = 1f;
            float yMin = 0f;
            float yMax = 1f;
            bool hasValues = false;
            foreach (Vector2[] values in series)
                if (values != null)
                    foreach (Vector2 point in values)
                    {
                        if (float.IsNaN(point.x) || float.IsInfinity(point.x) || float.IsNaN(point.y) || float.IsInfinity(point.y)) continue;
                        hasValues = true;
                        xMax = Mathf.Max(xMax, point.x);
                        yMin = Mathf.Min(yMin, point.y);
                        yMax = Mathf.Max(yMax, point.y);
                    }
            if (!hasValues || Event.current.type != EventType.Repaint)
            {
                GUI.Label(rect, "No finite samples", EditorStyles.centeredGreyMiniLabel);
                return;
            }
            if (Mathf.Abs(yMax - yMin) < 0.0001f) { yMin -= 1f; yMax += 1f; }
            Handles.BeginGUI();
            for (int j = 0; j < series.Length; j++)
            {
                Vector2[] values = series[j];
                if (values == null || values.Length < 2) continue;
                Handles.color = j == 0 ? new Color(0.88f, 0.93f, 0.98f) : j == 1 ? new Color(1f, 0.65f, 0.16f) : Color.cyan;
                int stride = Mathf.Max(1, values.Length / 900);
                Vector3 previous = PlotPoint(rect, values[0], xMin, xMax, yMin, yMax);
                for (int i = stride; i < values.Length; i += stride)
                {
                    Vector3 next = PlotPoint(rect, values[i], xMin, xMax, yMin, yMax);
                    Handles.DrawLine(previous, next);
                    previous = next;
                }
                Vector3 last = PlotPoint(rect, values[values.Length - 1], xMin, xMax, yMin, yMax);
                if (last != previous) Handles.DrawLine(previous, last);
            }
            Handles.EndGUI();
            GUI.Label(new Rect(rect.x + 5f, rect.y + 2f, rect.width - 10f, 17f), yMax.ToString("F2") + " / " + yMin.ToString("F2"), EditorStyles.miniLabel);
            GUI.Label(new Rect(rect.x + 5f, rect.yMax - 18f, rect.width - 10f, 17f), "0.00 s → " + xMax.ToString("F2") + " s", EditorStyles.miniLabel);
        }

        private static Vector3 PlotPoint(Rect rect, Vector2 point, float xMin, float xMax, float yMin, float yMax)
        {
            float x = Mathf.InverseLerp(xMin, xMax, point.x);
            float y = Mathf.InverseLerp(yMin, yMax, point.y);
            return new Vector3(rect.x + 5f + x * (rect.width - 10f), rect.yMax - 5f - y * (rect.height - 10f), 0f);
        }

        private void DrawScene(SceneView sceneView)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            VehiclePhysicsLabTrack track = definition == null ? null : definition.track;
            VehiclePhysicsLabTrackAnchor anchor = track == null ? null : FindAnchor(track);
            if (drawTrack && SceneDrawTrack && track != null)
                VehiclePhysicsLabSceneDrawing.DrawTrack(track, anchor == null ? null : anchor.transform, anchor == null || anchor.drawLabels);
            if (!drawTrajectory || !SceneDrawTrajectory) return;
            if (sweepRunner != null && sweepRunner.LiveSamples.Count > 1)
                VehiclePhysicsLabSceneDrawing.DrawReport(sweepRunner.LiveSamples, SceneDrawForces, SceneDrawContacts, new Color(1f, 0.4f, 0.2f));
            else if (runner != null && runner.LiveSamples.Count > 1)
                VehiclePhysicsLabSceneDrawing.DrawReport(runner.LiveSamples, SceneDrawForces, SceneDrawContacts, new Color(1f, 0.65f, 0.12f));
            else if (SelectedReport != null && SelectedReport.samples != null)
                VehiclePhysicsLabSceneDrawing.DrawReport(SelectedReport.samples, SceneDrawForces, SceneDrawContacts, new Color(0.2f, 0.8f, 1f));
        }

        private void ExportSelectedJson()
        {
            VehiclePhysicsLabRunReport report = SelectedReport;
            if (report == null) throw new ArgumentException("Run or import a report first.");
            ExportJson(report);
        }

        private void ExportSelectedCsv()
        {
            VehiclePhysicsLabRunReport report = SelectedReport;
            if (report == null) throw new ArgumentException("Run or import a report first.");
            ExportCsv(report);
        }

        private static void ExportJson(VehiclePhysicsLabRunReport report)
        {
            VehiclePhysicsLabEditorOperations.ValidateReport(report);
            string path = EditorUtility.SaveFilePanel("Export Physics Lab report", "", report.runId + ".json", "json");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("PHYSICS_LAB_REPORT_EXPORTED: " + path);
        }

        private static void ExportCsv(VehiclePhysicsLabRunReport report)
        {
            string path = EditorUtility.SaveFilePanel("Export Physics Lab telemetry", "", report.runId + ".csv", "csv");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, VehiclePhysicsLabEditorOperations.ReportCsv(report));
            Debug.Log("PHYSICS_LAB_CSV_EXPORTED: " + path);
        }

        private static VehiclePhysicsLabTrackAnchor FindAnchor(VehiclePhysicsLabTrack track)
        {
            if (track == null) return null;
            VehiclePhysicsLabTrackAnchor[] anchors = FindObjectsByType<VehiclePhysicsLabTrackAnchor>(FindObjectsInactive.Include);
            for (int i = 0; i < anchors.Length; i++) if (anchors[i] != null && anchors[i].track == track) return anchors[i];
            return null;
        }

        private static T FindFirstAsset<T>() where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            for (int i = 0; i < guids.Length; i++)
            {
                T value = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (value != null) return value;
            }
            return null;
        }

        private void DrawSection(SerializedObject serialized, string propertyName, string title)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null) return;
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(property, true);
        }

        private static void DrawProperty(SerializedObject serialized, string propertyName)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property != null) EditorGUILayout.PropertyField(property, true);
        }

        private string ConfigurationSource(VehiclePhysicsLabTuningParameter parameter)
        {
            if (definition != null && definition.temporaryOverrides != null)
                for (int i = 0; i < definition.temporaryOverrides.Length; i++)
                    if (definition.temporaryOverrides[i].enabled && definition.temporaryOverrides[i].parameter == parameter)
                        return "scenario override";
            if (definition != null && definition.vehicle != null && definition.vehicle.upgrades != null && definition.vehicle.upgrades.Length > 0)
                return "vehicle + upgrades";
            return "vehicle stock";
        }

        private static float AverageWheel(VehiclePhysicsLabSample sample, Func<VehiclePhysicsLabWheelSample, float> selector)
        {
            if (sample.wheels == null || sample.wheels.Length == 0) return 0f;
            float total = 0f;
            int count = 0;
            for (int i = 0; i < sample.wheels.Length; i++)
            {
                float value = selector(sample.wheels[i]);
                if (float.IsNaN(value) || float.IsInfinity(value)) continue;
                total += value;
                count++;
            }
            return count == 0 ? 0f : total / count;
        }

        private static string ReportLabel(VehiclePhysicsLabRunReport report)
        {
            return report == null ? "<null>" : report.experiment + " · " + report.status + " · " + report.runId.Substring(0, Mathf.Min(8, report.runId.Length));
        }

        private void InvalidateEffectiveCache()
        {
            effectiveFingerprint = null;
            effectiveError = null;
            effectiveValues = null;
        }

        private void RefreshStatus()
        {
            if (status == null) return;
            status.text = message;
            status.messageType = ToHelpBoxType(messageType);
        }

        private void SetMessage(string value, MessageType type)
        {
            message = value ?? string.Empty;
            messageType = type;
            RefreshStatus();
        }

        private void Safe(Action action)
        {
            try { action(); }
            catch (Exception exception)
            {
                StopRunner(false);
                SetMessage(exception.Message, MessageType.Error);
                Debug.LogWarning("PHYSICS_LAB: " + exception);
            }
        }

        private static HelpBoxMessageType ToHelpBoxType(MessageType type)
        {
            switch (type)
            {
                case MessageType.Error: return HelpBoxMessageType.Error;
                case MessageType.Warning: return HelpBoxMessageType.Warning;
                default: return HelpBoxMessageType.Info;
            }
        }

        private static string FormatInput(VehicleInputState input)
        {
            return "S " + input.Steering.ToString("F2") + " · T " + input.Throttle.ToString("F2") + " · B " + input.Brake.ToString("F2")
                + (input.Handbrake ? " · HB" : string.Empty) + (input.Nitrous ? " · NOS" : string.Empty);
        }

        private static string FormatVector(Vector3 value) => "(" + value.x.ToString("F2") + ", " + value.y.ToString("F2") + ", " + value.z.ToString("F2") + ")";
    }

    internal static class VehiclePhysicsLabSceneDrawing
    {
        public static void DrawTrack(VehiclePhysicsLabTrack track, Transform anchor, bool labels)
        {
            if (track == null || !track.IsValid(out _)) return;
            Matrix4x4 matrix = anchor == null ? Matrix4x4.identity : anchor.localToWorldMatrix;
            using (new Handles.DrawingScope(matrix))
            {
                Handles.color = new Color(0.15f, 0.65f, 1f, 0.8f);
                VehiclePhysicsLabTrackSample previous = track.Sample(0f);
                int count = Mathf.Clamp(Mathf.CeilToInt(track.length / 4f), 2, 256);
                for (int i = 1; i <= count; i++)
                {
                    VehiclePhysicsLabTrackSample next = track.Sample(track.length * i / count);
                    Handles.DrawLine(previous.position, next.position);
                    Handles.color = new Color(0.15f, 0.65f, 1f, 0.3f);
                    Handles.DrawLine(previous.position + previous.left * previous.width * 0.5f, next.position + next.left * next.width * 0.5f);
                    Handles.DrawLine(previous.position - previous.left * previous.width * 0.5f, next.position - next.left * next.width * 0.5f);
                    Handles.color = new Color(0.15f, 0.65f, 1f, 0.8f);
                    previous = next;
                }
                VehiclePhysicsLabTrackSample start = track.Sample(0f);
                VehiclePhysicsLabTrackSample end = track.Sample(track.length);
                Handles.color = Color.green;
                Handles.SphereHandleCap(0, start.position, Quaternion.identity, 0.6f, EventType.Repaint);
                Handles.color = Color.red;
                Handles.SphereHandleCap(0, end.position, Quaternion.identity, 0.6f, EventType.Repaint);
                if (labels)
                {
                    Handles.Label(start.position + Vector3.up, "START · " + track.displayName);
                    Handles.Label(end.position + Vector3.up, "END");
                }
            }
        }

        public static void DrawReport(IReadOnlyList<VehiclePhysicsLabSample> samples, bool drawForces, bool drawContacts, Color color)
        {
            if (samples == null || samples.Count < 2) return;
            Handles.color = color;
            int stride = Mathf.Max(1, samples.Count / 1000);
            for (int i = stride; i < samples.Count; i += stride)
                Handles.DrawAAPolyLine(3f, samples[i - stride].position, samples[i].position);
            if (drawForces)
                for (int i = 0; i < samples.Count; i += Mathf.Max(1, samples.Count / 160))
                {
                    VehiclePhysicsLabSample sample = samples[i];
                    Handles.color = Color.yellow;
                    Handles.DrawLine(sample.position, sample.position + sample.acceleration * 0.25f);
                    if (sample.wheels == null) continue;
                    for (int wheel = 0; wheel < sample.wheels.Length; wheel++)
                    {
                        VehiclePhysicsLabWheelSample value = sample.wheels[wheel];
                        if (!value.grounded) continue;
                        Handles.color = Color.magenta;
                        Handles.DrawLine(value.contactPoint, value.contactPoint + value.contactNormal * 0.5f);
                    }
                }
            if (drawContacts)
                for (int i = 0; i < samples.Count; i += Mathf.Max(1, samples.Count / 160))
                {
                    VehiclePhysicsLabSample sample = samples[i];
                    if (sample.wheels == null) continue;
                    for (int wheel = 0; wheel < sample.wheels.Length; wheel++)
                    {
                        VehiclePhysicsLabWheelSample value = sample.wheels[wheel];
                        if (!value.grounded) continue;
                        Handles.color = value.surfaceGrip < 0.8f ? Color.red : Color.cyan;
                        Handles.DrawWireDisc(value.contactPoint, value.contactNormal, 0.15f);
                    }
                }
        }
    }
}
