using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving.Editor
{
    [Serializable]
    public sealed class RacingStudioVariant
    {
        public RacingLineFamily family;
        public RacingLineCandidate candidate;
        public RacingVerificationReport report;
    }

    public sealed class RacingLineStudioWindow : EditorWindow
    {
        [SerializeField] private RacingLineSource source;
        [SerializeField] private RacingLineFamily family;
        [SerializeField] private List<RacingStudioVariant> variants = new List<RacingStudioVariant>();
        [SerializeField] private float station, sectorStart, sectorEnd = 100;
        [SerializeField] private int panel, trialIndex;
        [SerializeField] private bool showCorridor = true, showVariants = true, showTelemetry = true;
        private RacingLineSnapshot snapshot;
        private RacingLinePlanner planner;
        private RacingLineRollout rollout;
        private RacingCapabilityCalibration calibration;
        private readonly Queue<RacingLineFamily> queue = new Queue<RacingLineFamily>();
        private IMGUIContainer inspector, workbench;
        private Label status;
        private ObjectField documentField;
        private Vector2 inspectorScroll, reportScroll;
        private string message = "Select a Studio Document or create the compound-corner example.";
        private string workFingerprint;
        private double nextCheck;
        private bool Busy => planner != null || rollout != null || calibration != null;
        private RacingStudioVariant Selected => variants.Find(v => v.family == family);
        public RacingLineSource Document => source;

        [MenuItem("NFS MW Remaster/Racing Lines/Racing Line Studio")]
        public static RacingLineStudioWindow Open() => GetWindow<RacingLineStudioWindow>("Racing Line Studio");
        public void SelectDocument(RacingLineSource value)
        {
            Cancel(); source = value; variants.Clear(); snapshot = null; station = 0;
            if (documentField != null) documentField.SetValueWithoutNotify(source);
            Refresh(); Repaint();
            if (source != null && source.published != null && snapshot != null && source.published.TryOpen(snapshot.Fingerprint, out _, out _))
            {
                var published = source.published.CopyTrajectory(); family = published.family;
                variants.Add(new RacingStudioVariant { family = family, candidate = published, report = source.published.Report });
            }
        }
        [OnOpenAsset]
        public static bool OnOpen(EntityId instanceId, int line)
        {
            if (!(EditorUtility.EntityIdToObject(instanceId) is RacingLineSource document)) return false;
            Open().SelectDocument(document); return true;
        }
        private void OnEnable()
        {
            minSize = new Vector2(1050, 620);
            EditorApplication.update += UpdateWork;
            Undo.undoRedoPerformed += OnUndo;
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
            EditorApplication.playModeStateChanged += OnPlayMode;
            SceneView.duringSceneGui += DrawScene;
            EditorSceneManager.sceneClosing += OnSceneClosing;
        }
        private void OnDisable()
        {
            Cancel(); EditorApplication.update -= UpdateWork; Undo.undoRedoPerformed -= OnUndo;
            AssemblyReloadEvents.beforeAssemblyReload -= Cancel; EditorApplication.playModeStateChanged -= OnPlayMode;
            SceneView.duringSceneGui -= DrawScene;
            EditorSceneManager.sceneClosing -= OnSceneClosing;
        }
        private void OnSceneClosing(Scene scene, bool removingScene) { if (!EditorSceneManager.IsPreviewScene(scene)) Cancel(); }
        private void OnUndo() { Cancel(); Refresh(); Repaint(); SceneView.RepaintAll(); }
        private void OnPlayMode(PlayModeStateChange state) { Cancel(); snapshot = null; }
        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var toolbar = new Toolbar(); rootVisualElement.Add(toolbar);
            toolbar.Add(new Label("RACING LINE STUDIO") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 12 } });
            documentField = new ObjectField { objectType = typeof(RacingLineSource), allowSceneObjects = false, value = source,
                style = { flexGrow = 1, minWidth = 180 } };
            documentField.RegisterValueChangedCallback(e => SelectDocument(e.newValue as RacingLineSource)); toolbar.Add(documentField);
            toolbar.Add(new ToolbarButton(() => Safe(CreateDocument)) { text = "New" });
            toolbar.Add(new ToolbarButton(() => Safe(DuplicateDocument)) { text = "Duplicate with new IDs" });
            toolbar.Add(new ToolbarButton(() => Safe(() => RacingLineStudioDemo.Create())) { text = "Create example" });
            toolbar.Add(new ToolbarButton(() => Safe(() => Application.OpenURL("file://" + System.IO.Path.GetFullPath("Assets/NfsMw/Modules/Driving/RACING_LINE_STUDIO.md")))) { text = "Guide" });
            var split = new TwoPaneSplitView(0, 340, TwoPaneSplitViewOrientation.Horizontal); split.style.flexGrow = 1; rootVisualElement.Add(split);
            inspector = new IMGUIContainer(DrawInspector); split.Add(inspector);
            workbench = new IMGUIContainer(DrawWorkbench); workbench.style.flexGrow = 1; split.Add(workbench);
            status = new Label(message) { style = { whiteSpace = WhiteSpace.Normal, paddingLeft = 8, paddingTop = 5, paddingBottom = 5 } };
            rootVisualElement.Add(status); Refresh();
        }
        private void DrawInspector()
        {
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            EditorGUILayout.Space(8); EditorGUILayout.LabelField("AUTHORING & DEPENDENCIES", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Metres, seconds and m/s. The lane's left/up frame is authoritative. Source edits are Undoable; generated candidates are disposable.", MessageType.Info);
            if (source == null) { EditorGUILayout.EndScrollView(); return; }
            using (new EditorGUI.DisabledScope(Busy || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                var serialized = new SerializedObject(source); serialized.Update();
                EditorGUI.BeginChangeCheck();
                foreach (string field in new[] { "route", "vehicle", "capability", "planner", "verification", "hints", "exclusions", "referenceNotes", "referenceCaptures" })
                    EditorGUILayout.PropertyField(serialized.FindProperty(field), true);
                if (EditorGUI.EndChangeCheck()) { serialized.ApplyModifiedProperties(); Refresh(); SceneView.RepaintAll(); }
                EditorGUILayout.PropertyField(serialized.FindProperty("published")); serialized.ApplyModifiedProperties();
                EditorGUILayout.Space();
                NestedInspector(source.route, "ROUTE ADAPTER — legal lane occurrences");
                NestedInspector(source.vehicle, "VEHICLE SETUP — shared physics");
                NestedInspector(source.capability, "CAPABILITY — assumptions / measurements");
                if (source.schema == 1 && GUILayout.Button("Migrate document schema 1 → 2")) Safe(() => { RacingLineEditorOperations.MigrateV1(source); Refresh(); });
                if (GUILayout.Button("Save authoring assets"))
                {
                    AssetDatabase.SaveAssetIfDirty(source);
                    if (source.route != null) AssetDatabase.SaveAssetIfDirty(source.route);
                    if (source.vehicle != null) AssetDatabase.SaveAssetIfDirty(source.vehicle);
                    if (source.capability != null) AssetDatabase.SaveAssetIfDirty(source.capability);
                    Notify("Saved the selected document and its selected authoring dependencies.");
                }
            }
            EditorGUILayout.EndScrollView();
        }
        private void NestedInspector(UnityEngine.Object target, string title)
        {
            if (target == null) return;
            var data = new SerializedObject(target); data.Update();
            var iterator = data.GetIterator();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck(); bool children = true;
            while (iterator.NextVisible(children))
            {
                children = false; if (iterator.name == "m_Script") continue;
                EditorGUILayout.PropertyField(iterator, true);
            }
            if (EditorGUI.EndChangeCheck()) { data.ApplyModifiedProperties(); Refresh(); }
        }
        private void DrawWorkbench()
        {
            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                family = (RacingLineFamily)EditorGUILayout.EnumPopup("Line family", family);
                showCorridor = GUILayout.Toggle(showCorridor, "Corridor", "Button");
                showVariants = GUILayout.Toggle(showVariants, "Variants", "Button");
                showTelemetry = GUILayout.Toggle(showTelemetry, "Measured path", "Button");
                if (GUILayout.Button("Frame in Scene")) FrameStation();
            }
            EditorGUI.BeginChangeCheck();
            station = EditorGUILayout.Slider("Route station (m)", station, 0, snapshot?.Length ?? 1);
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
            using (new EditorGUILayout.HorizontalScope())
            {
                sectorStart = EditorGUILayout.FloatField("Sector start (m)", sectorStart);
                sectorEnd = EditorGUILayout.FloatField("End (m)", sectorEnd);
            }
            using (new EditorGUI.DisabledScope(source == null || Busy || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Generate / refine")) Safe(() => Generate(false));
                    if (GUILayout.Button("Regenerate sector")) Safe(() => Generate(true));
                    if (GUILayout.Button("Generate all 7 families")) Safe(() =>
                    {
                        queue.Clear(); foreach (RacingLineFamily value in Enum.GetValues(typeof(RacingLineFamily))) queue.Enqueue(value);
                        family = queue.Dequeue(); Generate(false);
                    });
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    foreach (RacingHintKind kind in Enum.GetValues(typeof(RacingHintKind)))
                        if (GUILayout.Button("+ " + kind)) Safe(() => { RacingLineEditorOperations.AddHint(source, kind, station); Refresh(); });
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Measure & apply capability")) Safe(() =>
                    {
                        if (source.capability == null) throw new ArgumentException("Assign a capability asset to receive measurements.");
                        calibration = new RacingCapabilityCalibration(source.vehicle); Notify("Calibrating full throttle / braking on an isolated dry straight…");
                    });
                    if (GUILayout.Button("Verify robust entry batch")) Safe(() => Verify(false));
                    if (GUILayout.Button("Run inside + outside pair")) Safe(() => Verify(true));
                    if (GUILayout.Button("Publish verified revision")) Safe(Publish);
                }
            }
            if (Busy)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Cancel & dispose")) Cancel();
                    if (rollout != null)
                    {
                        rollout.Paused = GUILayout.Toggle(rollout.Paused, "Pause rollout", "Button");
                        if (GUILayout.Button("Single physics step")) Safe(() => rollout.Advance(4, true));
                        GUILayout.Label("Trial " + (rollout.TrialIndex + 1) + " · " + rollout.Elapsed.ToString("F1") + " s");
                    }
                }
            }
            panel = GUILayout.Toolbar(panel, new[] { "Trajectory & speed", "Physics telemetry", "Diagnostics", "Compare & export", "Reference review" });
            reportScroll = EditorGUILayout.BeginScrollView(reportScroll);
            if (panel == 4) { DrawReferences(); EditorGUILayout.EndScrollView(); return; }
            var selected = Selected;
            if (selected?.candidate == null) EditorGUILayout.HelpBox("Generate a line to inspect geometric estimates. Verification records actual shared-physics behavior; generation alone is not a pass.", MessageType.Info);
            else
            {
                var c = selected.candidate;
                EditorGUILayout.LabelField(c.family + " · " + c.state + " · " + c.samples.Length + " samples", EditorStyles.boldLabel);
                if (c.state == RacingLineState.Stale) EditorGUILayout.HelpBox("Dependency fingerprint changed. This retained attempt is for comparison only; regenerate before verification/publication.", MessageType.Warning);
                if (panel == 0)
                {
                    EditorGUILayout.LabelField("Estimated time " + c.estimatedSeconds.ToString("F2") + " s · numerical compute " + c.computeMilliseconds.ToString("F2") + " ms");
                    EditorGUILayout.LabelField("Geometric surrogate cost " + c.baselineCost.ToString("F3") + " → " + c.geometricCost.ToString("F3") + " · " + c.termination);
                    Plot("Speed envelope — m/s (white target, amber local ceiling)", c.samples.Select(s => new Vector2(s.station, s.targetSpeed)).ToArray(),
                        c.samples.Select(s => new Vector2(s.station, s.speedLimit)).ToArray());
                    Plot("Curvature — 1/m (white lateral, amber vertical)", c.samples.Select(s => new Vector2(s.station, s.curvature)).ToArray(),
                        c.samples.Select(s => new Vector2(s.station, s.verticalCurvature)).ToArray());
                    Plot("Planned body clearance — m", c.samples.Select(s => new Vector2(s.station, s.clearance)).ToArray());
                }
                else if (panel == 1) DrawTelemetry(selected.report);
                else if (panel == 2)
                {
                    foreach (var d in c.diagnostics)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button(d.station.ToString("F1") + " m", GUILayout.Width(65))) { station = d.station; FrameStation(); }
                            EditorGUILayout.HelpBox(d.code + ": " + d.message, d.severity == RacingDiagnosticSeverity.Error ? MessageType.Error : MessageType.Warning);
                        }
                    }
                    if (selected.report != null)
                        foreach (var t in selected.report.trials) EditorGUILayout.HelpBox("Trial " + t.trial + ": " + t.diagnosis, t.passed ? MessageType.Info : MessageType.Error);
                }
                else DrawComparison();
            }
            EditorGUILayout.EndScrollView();
        }
        private void DrawTelemetry(RacingVerificationReport report)
        {
            RacingTelemetrySample[] data;
            if (rollout != null) data = rollout.LiveTelemetry.Where(t => t.vehicleIndex == 0).ToArray();
            else if (report != null && report.trials.Length > 0)
            {
                trialIndex = EditorGUILayout.IntSlider("Trial", trialIndex, 0, report.trials.Length - 1);
                var t = report.trials[trialIndex]; data = t.telemetry.Where(s => s.vehicleIndex == 0).ToArray();
                EditorGUILayout.HelpBox(t.diagnosis, t.passed ? MessageType.Info : MessageType.Warning);
                EditorGUILayout.LabelField("Time " + t.elapsed.ToString("F2") + " s · lateral max " + t.maximumLateralError.ToString("F2")
                    + " m · RMS speed " + t.rmsSpeedError.ToString("F2") + " m/s · clearance " + t.minimumClearance.ToString("F2") + " m");
                EditorGUILayout.LabelField("Collisions " + t.collisions + " · boundary samples " + t.boundaryViolations + " · steering saturation " + t.saturationFraction.ToString("P1"));
                EditorGUILayout.LabelField("Entry " + t.entrySpeed.ToString("F2") + " m/s · offset " + t.entryOffset.ToString("F2")
                    + " m · grip ×" + t.gripScale.ToString("F3") + " · added delay " + t.reactionDelay.ToString("F3") + " s");
            }
            else { EditorGUILayout.HelpBox("Run verification to record actual physics telemetry.", MessageType.Info); return; }
            if (data.Length == 0) EditorGUILayout.HelpBox("The published asset keeps compact summaries. Load the matching report JSON in Compare & export for raw traces, or run verification again.", MessageType.Info);
            Plot("Speed — m/s (white target, amber actual)", Series(data, s => s.targetSpeed), Series(data, s => s.actualSpeed));
            Plot("Lateral tracking error — m", Series(data, s => s.lateralError));
            Plot("Body slip / heading error — degrees", Series(data, s => s.slipDegrees), Series(data, s => s.headingError));
            Plot("Input — steering / brake / throttle", Series(data, s => s.steering), Series(data, s => s.brake), Series(data, s => s.throttle));
            Plot("Shared assist yaw torque — Nm", Series(data, s => s.assistTorque));
            Plot("Handling phase — Grip 0, Initiating 1, Drifting 2, Recovering 3, Airborne 4", Series(data, s => (int)s.handling));
        }
        private static Vector2[] Series(RacingTelemetrySample[] samples, Func<RacingTelemetrySample, float> value)
            => samples.Select(s => new Vector2(s.time, value(s))).ToArray();
        private void DrawComparison()
        {
            foreach (var v in variants)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(v.family.ToString(), GUILayout.Width(120))) family = v.family;
                    GUILayout.Label(v.candidate.state + " · estimate " + v.candidate.estimatedSeconds.ToString("F2") + " s · "
                        + (v.report == null ? "unmeasured" : v.report.trials.Count(t => t.passed) + "/" + v.report.trials.Length + " measured passes"));
                }
            }
            EditorGUILayout.HelpBox("Inside/outside pairing records actual simultaneous occupancy. It does not prove universal traffic safety. Route-local exclusions do not include arbitrary scene obstacles.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Export candidate JSON")) Safe(() => Export(JsonUtility.ToJson(Selected.candidate, true), "trajectory", "json"));
                if (GUILayout.Button("Export report JSON")) Safe(() =>
                { if (Selected.report == null) throw new ArgumentException("Run verification first."); Export(JsonUtility.ToJson(Selected.report, true), "report", "json"); });
                if (GUILayout.Button("Export telemetry CSV")) Safe(() => Export(RacingLineEditorOperations.TelemetryCsv(Selected.report), "telemetry", "csv"));
                if (GUILayout.Button("Load report JSON")) Safe(() =>
                {
                    string path = EditorUtility.OpenFilePanel("Load matching verification report", "", "json");
                    if (string.IsNullOrEmpty(path)) return;
                    if (new System.IO.FileInfo(path).Length > 64 * 1024 * 1024) throw new ArgumentException("Report exceeds the 64 MB import budget.");
                    var report = JsonUtility.FromJson<RacingVerificationReport>(System.IO.File.ReadAllText(path));
                    RacingLineEditorOperations.ValidateImportedReport(report, Selected?.candidate);
                    Selected.report = report;
                });
            }
            if (snapshot != null) EditorGUILayout.SelectableLabel("Input SHA256: " + snapshot.Fingerprint + "\nVehicle SHA256: " + snapshot.VehicleFingerprint, GUILayout.Height(40));
        }
        private static void Plot(string title, params Vector2[][] series)
        {
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
            var rect = GUILayoutUtility.GetRect(200, 120, GUILayout.ExpandWidth(true)); EditorGUI.DrawRect(rect, new Color(0.075f, 0.085f, 0.095f));
            float xMax = 1, yMin = 0, yMax = 1;
            foreach (var values in series) foreach (var p in values) { xMax = Mathf.Max(xMax, p.x); yMin = Mathf.Min(yMin, p.y); yMax = Mathf.Max(yMax, p.y); }
            if (Event.current.type != EventType.Repaint) return;
            Handles.BeginGUI();
            for (int j = 0; j < series.Length; j++)
            {
                var values = series[j]; if (values.Length < 2) continue;
                Handles.color = j == 0 ? new Color(0.88f, 0.93f, 0.98f) : j == 1 ? new Color(1, 0.65f, 0.16f) : Color.cyan;
                int stride = Mathf.Max(1, values.Length / 900);
                Vector3 previous = Point(values[0]);
                for (int i = stride; i < values.Length; i += stride) { var next = Point(values[i]); Handles.DrawLine(previous, next); previous = next; }
            }
            Handles.EndGUI(); GUI.Label(new Rect(rect.x + 5, rect.y + 2, 240, 18), yMax.ToString("F2") + " / " + yMin.ToString("F2"), EditorStyles.miniLabel);
            Vector3 Point(Vector2 p) => new Vector3(rect.x + 5 + p.x / xMax * (rect.width - 10), rect.yMax - 5 - (p.y - yMin) / (yMax - yMin) * (rect.height - 10), 0);
        }
        private void Generate(bool sector)
        {
            snapshot = RacingLineEditorOperations.Capture(source); RacingLineEditorOperations.ValidateUniqueDocument(source);
            planner = new RacingLinePlanner(snapshot, family, sector ? Selected?.candidate ?? throw new ArgumentException("Generate a full candidate before sector regeneration.") : null, sectorStart, sectorEnd);
            workFingerprint = snapshot.Fingerprint; Notify("Generating " + family + " using a bounded numerical work slice…");
        }
        public void BeginGeneration(RacingLineFamily selectedFamily)
        {
            Cancel(); family = selectedFamily; Generate(false);
        }
        private void DrawReferences()
        {
            EditorGUILayout.HelpBox("Attach lawful still captures in Reference Captures. Speed and synchronization annotations are evidence with uncertainty, not reconstructed steering/force telemetry. These editor-only attachments do not alter physics feasibility.", MessageType.Info);
            if (source == null || source.referenceCaptures == null) return;
            foreach (var capture in source.referenceCaptures)
            {
                if (capture == null) continue;
                EditorGUILayout.LabelField(capture.gameBuild + " · " + capture.carAndSetup, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(capture.sourceAndPermission ?? "Source not documented", EditorStyles.wordWrappedLabel);
                if (capture.image != null)
                {
                    var rect = GUILayoutUtility.GetAspectRect((float)capture.image.width / capture.image.height, GUILayout.MaxHeight(320));
                    GUI.DrawTexture(rect, capture.image, ScaleMode.ScaleToFit);
                }
                EditorGUILayout.LabelField("Capture " + capture.captureSeconds.ToString("F2") + " s → route " + capture.routeStation.ToString("F1") + " m"
                    + (capture.stationIsInferred ? " (inferred mapping)" : " (documented mapping)"));
                if (capture.observedSpeedMps >= 0)
                    EditorGUILayout.LabelField("Visible speed " + capture.observedSpeedMps.ToString("F1") + " ± " + capture.speedUncertaintyMps.ToString("F1") + " m/s");
                EditorGUILayout.LabelField(capture.observation ?? "", EditorStyles.wordWrappedLabel);
                if (GUILayout.Button("Scrub to synchronization point")) { station = capture.routeStation; SceneView.RepaintAll(); }
                EditorGUILayout.Space(10);
            }
        }
        private void Verify(bool pair)
        {
            var current = Selected?.candidate ?? throw new ArgumentException("Generate the selected family first.");
            RacingLineEditorOperations.Capture(source);
            if (pair)
            {
                var inside = variants.Find(v => v.family == RacingLineFamily.Inside)?.candidate;
                var outside = variants.Find(v => v.family == RacingLineFamily.Outside)?.candidate;
                if (inside == null || outside == null) throw new ArgumentException("Generate Inside and Outside candidates first.");
                family = RacingLineFamily.Inside; current = inside; rollout = new RacingLineRollout(source, inside, outside);
            }
            else rollout = new RacingLineRollout(source, current);
            workFingerprint = current.fingerprint; Notify(pair ? "Measuring two-car occupancy in isolated shared physics…" : "Measuring deterministic baseline and seeded entry perturbations…");
        }
        private void Publish()
        {
            var v = Selected ?? throw new ArgumentException("Select a generated candidate.");
            string path = EditorUtility.SaveFilePanelInProject("Publish immutable verified line", source.name + "_" + family, "asset", "A unique revision is created; previous publications are retained.");
            if (string.IsNullOrEmpty(path)) return;
            var artifact = RacingLineEditorOperations.Publish(source, v.candidate, v.report, path);
            v.candidate.state = RacingLineState.Verified; EditorGUIUtility.PingObject(artifact); Notify("Published " + AssetDatabase.GetAssetPath(artifact) + ". Save the document reference when ready.");
        }
        private void UpdateWork()
        {
            if (EditorApplication.timeSinceStartup >= nextCheck)
            {
                nextCheck = EditorApplication.timeSinceStartup + 0.75;
                if (source != null && calibration == null)
                {
                    try
                    {
                        var current = RacingLineEditorOperations.Capture(source);
                        if (Busy && current.Fingerprint != workFingerprint) { Cancel(); Notify("Dependencies changed during work; discarded the stale attempt."); }
                        snapshot = current; MarkStale();
                    }
                    catch (ArgumentException e) { if (Busy) Cancel(); snapshot = null; Notify(e.Message); MarkStale(); }
                }
            }
            if (!Busy) return;
            Safe(() =>
            {
                if (planner != null)
                {
                    planner.Step();
                    if (planner.IsDone)
                    {
                        var result = planner.Result; planner.Dispose(); planner = null;
                        if (RacingLineEditorOperations.Capture(source).Fingerprint != result.fingerprint) throw new ArgumentException("Stale generation discarded.");
                        var v = variants.Find(v => v.family == result.family);
                        if (v == null) { v = new RacingStudioVariant { family = result.family }; variants.Add(v); }
                        v.candidate = result; v.report = null; Notify(result.family + ": " + result.state + " · " + result.computeMilliseconds.ToString("F2") + " ms numerical work.");
                        if (queue.Count > 0) { family = queue.Dequeue(); Generate(false); }
                    }
                }
                else if (rollout != null)
                {
                    rollout.Advance();
                    if (rollout.IsDone)
                    {
                        var result = rollout.Result; rollout.Dispose(); rollout = null;
                        if (RacingLineEditorOperations.Capture(source).Fingerprint != result.fingerprint) throw new ArgumentException("Stale rollout discarded.");
                        var completed = variants.Find(v => v.candidate.id == result.candidateId);
                        completed.report = result;
                        completed.candidate.state = !result.passed ? RacingLineState.Failed
                            : result.companionRun ? RacingLineState.Generated : RacingLineState.Verified;
                        Notify((result.passed ? "PASS" : "FAILED") + ": " + result.trials.Count(t => t.passed) + "/" + result.trials.Length + " trials. Inspect diagnostics and raw telemetry.");
                    }
                }
                else if (calibration != null)
                {
                    calibration.Advance();
                    if (calibration.IsDone)
                    {
                        if (RacingLineSnapshot.VehicleFingerprintOf(source.vehicle) != calibration.Fingerprint) throw new ArgumentException("Vehicle changed; discarded stale calibration.");
                        Undo.RecordObject(source.capability, "Apply measured longitudinal capability");
                        source.capability.points = calibration.Points; source.capability.vehicleFingerprint = calibration.Fingerprint;
                        source.capability.longitudinalMeasured = true; source.capability.lateralMeasured = false; source.capability.evidence = calibration.Evidence;
                        EditorUtility.SetDirty(source.capability); calibration.Dispose(); calibration = null; Refresh();
                        Notify("Applied measured straight-line capability. Lateral envelope remains labelled as an assumption; regenerate and verify candidates.");
                    }
                }
            });
            Repaint(); SceneView.RepaintAll();
        }
        public void Cancel()
        {
            planner?.Dispose(); planner = null; rollout?.Dispose(); rollout = null; calibration?.Dispose(); calibration = null; queue.Clear();
        }
        private void Refresh()
        {
            try { snapshot = source == null ? null : RacingLineEditorOperations.Capture(source); Notify(snapshot == null ? "Select or create a studio document." : "Ready · " + snapshot.Corridor.Length + " corridor samples · " + snapshot.Length.ToString("F1") + " m"); }
            catch (ArgumentException e) { snapshot = null; Notify(e.Message); }
            MarkStale(); inspector?.MarkDirtyRepaint(); workbench?.MarkDirtyRepaint();
        }
        private void MarkStale()
        { foreach (var v in variants) if (v.candidate != null && (snapshot == null || snapshot.Fingerprint != v.candidate.fingerprint)) v.candidate.state = RacingLineState.Stale; }
        private void Safe(Action action) { try { action(); } catch (Exception e) { Cancel(); Notify(e.Message); UnityEngine.Debug.LogWarning("RACING_LINE_STUDIO: " + e.Message); } }
        private void Notify(string value) { message = value; if (status != null) status.text = value; }
        private void CreateDocument()
        {
            string path = EditorUtility.SaveFilePanelInProject("New studio document", "RacingLine", "asset", "Assign route, vehicle and capability assets in the inspector.");
            if (string.IsNullOrEmpty(path)) return;
            var doc = CreateInstance<RacingLineSource>(); AssetDatabase.CreateAsset(doc, AssetDatabase.GenerateUniqueAssetPath(path)); SelectDocument(doc);
        }
        private void DuplicateDocument()
        {
            if (source == null) throw new ArgumentException("Select a source document first.");
            var copy = RacingLineEditorOperations.Duplicate(source);
            AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(AssetDatabase.GetAssetPath(source).Replace(".asset", " Copy.asset"))); SelectDocument(copy);
        }
        private void Export(string content, string name, string extension)
        {
            string path = EditorUtility.SaveFilePanel("Export " + name, "", source.name + "_" + family + "_" + name, extension);
            RacingLineEditorOperations.Export(path, content); if (!string.IsNullOrEmpty(path)) Notify("Exported " + path);
        }
        private RacingCorridorSample At(float along)
        {
            var samples = snapshot.Corridor; int i = 0; while (i + 1 < samples.Length - 1 && samples[i + 1].station < along) i++;
            var a = samples[i]; var b = samples[i + 1]; float t = Mathf.InverseLerp(a.station, b.station, along);
            a.position = Vector3.Lerp(a.position, b.position, t); a.forward = Vector3.Slerp(a.forward, b.forward, t).normalized;
            a.left = Vector3.Slerp(a.left, b.left, t).normalized; a.up = Vector3.Slerp(a.up, b.up, t).normalized;
            a.width = Mathf.Lerp(a.width, b.width, t); return a;
        }
        private void FrameStation()
        {
            if (snapshot == null) return;
            var view = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
            var c = At(station); view.LookAt(c.position, Quaternion.LookRotation(c.forward + Vector3.down * 1.5f), 30); view.Repaint();
        }
        private void DrawScene(SceneView view)
        {
            if (snapshot == null || source == null) return;
            using (new Handles.DrawingScope(Matrix4x4.identity))
            {
                var samples = snapshot.Corridor;
                if (showCorridor)
                    for (int i = 1; i < samples.Length; i++)
                    {
                        var a = samples[i - 1]; var b = samples[i]; Handles.color = new Color(0.4f, 0.55f, 0.7f, 0.7f);
                        Handles.DrawLine(a.position + a.left * a.width * 0.5f, b.position + b.left * b.width * 0.5f);
                        Handles.DrawLine(a.position - a.left * a.width * 0.5f, b.position - b.left * b.width * 0.5f);
                    }
                foreach (var v in variants)
                {
                    if (v.candidate == null || !showVariants && v.family != family) continue;
                    var path = v.candidate.samples;
                    for (int i = 1; i < path.Length; i++)
                    {
                        Handles.color = v.candidate.HasErrors ? new Color(1, 0.2f, 0.15f) : v.candidate.state == RacingLineState.Stale ? Color.gray
                            : v.family != family ? new Color(0.5f, 0.6f, 0.8f, 0.4f) : Color.Lerp(new Color(0.15f, 0.65f, 1), new Color(1, 0.6f, 0.1f), path[i].targetSpeed / snapshot.Settings.maximumSpeed);
                        Handles.DrawAAPolyLine(v.family == family ? 3 : 1, path[i - 1].position + path[i - 1].normal * 0.1f, path[i].position + path[i].normal * 0.1f);
                    }
                }
                var c = At(station); Handles.color = Color.white; Handles.DrawWireDisc(c.position, c.up, 0.7f);
                Handles.Label(c.position + c.up, station.ToString("F1") + " m · lane " + c.laneId.Substring(0, Math.Min(8, c.laneId.Length)));
                using (new Handles.DrawingScope(Matrix4x4.TRS(c.position + c.up * snapshot.Dimensions.y * 0.5f, Quaternion.LookRotation(c.forward, c.up), Vector3.one)))
                    Handles.DrawWireCube(Vector3.zero, snapshot.Dimensions);
                foreach (var hint in source.hints)
                {
                    var at = At(hint.station); var position = at.position + at.left * hint.lateral + at.up * 0.2f;
                    Handles.color = Mathf.Abs(hint.lateral) + snapshot.Dimensions.x * 0.5f > at.width * 0.5f ? Color.red : new Color(1, 0.72f, 0.1f);
                    Handles.Label(position + at.up, hint.kind + " · " + hint.station.ToString("F1"));
                    using (new EditorGUI.DisabledScope(Busy || EditorApplication.isPlayingOrWillChangePlaymode))
                    {
                        EditorGUI.BeginChangeCheck(); var moved = Handles.Slider(position, at.left, HandleUtility.GetHandleSize(position) * 0.12f, Handles.DotHandleCap, 0.1f);
                        if (EditorGUI.EndChangeCheck()) { RacingLineEditorOperations.MoveHint(source, hint.id, Vector3.Dot(moved - at.position, at.left)); Refresh(); }
                    }
                }
                foreach (var e in source.exclusions)
                {
                    Handles.color = new Color(1, 0.15f, 0.1f, 0.25f);
                    for (float s = e.start; s < e.end; s += snapshot.Settings.spacing)
                    {
                        var a = At(s); var b = At(Mathf.Min(e.end, s + snapshot.Settings.spacing));
                        Handles.DrawAAConvexPolygon(a.position + a.left * e.minimumLateral, a.position + a.left * e.maximumLateral,
                            b.position + b.left * e.maximumLateral, b.position + b.left * e.minimumLateral);
                    }
                }
                if (showTelemetry)
                {
                    var report = Selected?.report;
                    if (report != null && report.trials.Length > 0)
                    {
                        var points = report.trials[Mathf.Clamp(trialIndex, 0, report.trials.Length - 1)].telemetry;
                        for (int vehicle = 0; vehicle < 2; vehicle++)
                        {
                            Vector3 previous = default; bool found = false;
                            foreach (var point in points)
                            {
                                if (point.vehicleIndex != vehicle) continue;
                                Handles.color = vehicle == 0 ? Color.green : Color.cyan;
                                if (found) Handles.DrawLine(previous, point.position);
                                previous = point.position; found = true;
                            }
                        }
                    }
                }
            }
        }
    }
}
