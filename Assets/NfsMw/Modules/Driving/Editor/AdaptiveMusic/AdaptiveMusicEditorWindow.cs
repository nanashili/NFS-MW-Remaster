#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Authoring, preview and diagnostics workspace for SensoryMusicProfile.
    /// The window owns no gameplay state; preview objects are destroyed on close,
    /// reload, scene close and play-mode changes.
    /// </summary>
    public sealed class AdaptiveMusicEditorWindow : EditorWindow
    {
        private static readonly string[] Tabs = { "Library", "Graph", "Timeline", "Mixer", "Transport", "Validation", "Publish" };
        private static readonly AdaptiveMusicContext[] Contexts =
        {
            AdaptiveMusicContext.Frontend, AdaptiveMusicContext.FreeRoam, AdaptiveMusicContext.Race,
            AdaptiveMusicContext.Pursuit, AdaptiveMusicContext.Cooldown, AdaptiveMusicContext.Escape,
            AdaptiveMusicContext.Results, AdaptiveMusicContext.Failure, AdaptiveMusicContext.Pause
        };

        private readonly List<SensoryMusicProfile> profiles = new List<SensoryMusicProfile>();
        private readonly List<AdaptiveMusicDiagnostic> diagnostics = new List<AdaptiveMusicDiagnostic>();
        private readonly List<AdaptiveMusicTraceEntry> trace = new List<AdaptiveMusicTraceEntry>();
        private readonly List<AdaptiveMusicStemSnapshot> stemDiagnostics = new List<AdaptiveMusicStemSnapshot>();
        private readonly HashSet<string> graphUndoNodes = new HashSet<string>(StringComparer.Ordinal);
        private AdaptiveMusicSoundtrackScan soundtrackScan;
        private AdaptiveMusicPreviewSession preview;
        private AdaptiveMusicGraphLayout layout;
        private SensoryMusicProfile profile;
        private string selectedSectionId = string.Empty;
        private string selectedTransitionId = string.Empty;
        private string selectedStingerId = string.Empty;
        private string search = string.Empty;
        private string diagnosticSearch = string.Empty;
        private string message = "Select a SensoryMusicProfile or build the synthetic fixture.";
        private Vector2 catalogScroll;
        private Vector2 graphScroll;
        private Vector2 inspectorScroll;
        private Vector2 validationScroll;
        private Vector2 traceScroll;
        private int tab;
        private int severityFilter = -1;
        private float previewIntensity = 0.55f;
        private bool showGrid = true;
        private bool showTrace = true;
        private string soloStemId = string.Empty;

        private MusicSection SelectedSection => profile != null ? profile.FindSection(selectedSectionId) : null;
        private MusicTransitionRule SelectedTransition => profile != null ? profile.FindTransition(selectedTransitionId) : null;
        private MusicStinger SelectedStinger => profile != null ? profile.FindStinger(selectedStingerId) : null;

        [MenuItem("NFS MW Remaster/Sensory/Adaptive Music/Adaptive Music Editor")]
        public static void OpenFromMenu() => Open();

        public static AdaptiveMusicEditorWindow Open()
        {
            var window = GetWindow<AdaptiveMusicEditorWindow>();
            window.titleContent = new GUIContent("Adaptive Music Editor");
            window.minSize = new Vector2(1120, 680);
            window.RefreshCatalog();
            return window;
        }

        public static AdaptiveMusicEditorWindow Open(SensoryMusicProfile value)
        {
            var window = Open();
            window.SelectProfile(value);
            return window;
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.hierarchyChanged += RefreshCatalog;
            Selection.selectionChanged += SyncSelection;
            Undo.undoRedoPerformed += OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            RefreshCatalog();
        }

        private void OnDisable()
        {
            StopPreview();
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.hierarchyChanged -= RefreshCatalog;
            Selection.selectionChanged -= SyncSelection;
            Undo.undoRedoPerformed -= OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload -= StopPreview;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.paddingLeft = 8;
            rootVisualElement.style.paddingRight = 8;
            rootVisualElement.style.paddingTop = 6;
            rootVisualElement.style.paddingBottom = 6;

            var toolbar = new Toolbar();
            toolbar.Add(new Label("ADAPTIVE MUSIC EDITOR") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 12 } });
            toolbar.Add(new ToolbarButton(() => Safe(RefreshCatalog)) { text = "Refresh" });
            toolbar.Add(new ToolbarButton(() => Safe(RunValidation)) { text = "Validate" });
            toolbar.Add(new ToolbarButton(() => Safe(BuildProjectSoundtrack)) { text = "Project soundtrack" });
            toolbar.Add(new ToolbarButton(() => Safe(MigrateSelectedLegacy)) { text = "Migrate legacy" });
            toolbar.Add(new ToolbarButton(() => Safe(AdaptiveMusicDemoBuilder.Build)) { text = "Build test scene" });
            toolbar.Add(new ToolbarButton(OpenGuide) { text = "Guide" });
            rootVisualElement.Add(toolbar);
            rootVisualElement.Add(new IMGUIContainer(DrawContent) { style = { flexGrow = 1 } });
        }

        public void RunValidation()
        {
            diagnostics.Clear(); diagnostics.AddRange(AdaptiveMusicEditorModel.ValidateAll());
            tab = 5;
            int errors = Count(AdaptiveMusicDiagnosticSeverity.Error);
            message = errors == 0 ? "Validation completed without errors." : "Validation found " + errors + " error(s). Publish is blocked until they are resolved.";
            Repaint();
        }

        private void DrawContent()
        {
            tab = GUILayout.Toolbar(tab, Tabs);
            switch (tab)
            {
                case 0: DrawLibrary(); break;
                case 1: DrawGraph(); break;
                case 2: DrawTimeline(); break;
                case 3: DrawMixer(); break;
                case 4: DrawTransport(); break;
                case 5: DrawValidation(); break;
                default: DrawPublish(); break;
            }
            EditorGUILayout.HelpBox(message, MessageType.Info);
        }

        private void DrawLibrary()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("New profile", EditorStyles.toolbarButton, GUILayout.Width(90))) Safe(CreateProfile);
                if (GUILayout.Button("Fixture profile", EditorStyles.toolbarButton, GUILayout.Width(100))) Safe(BuildFixture);
                if (GUILayout.Button("Project soundtrack", EditorStyles.toolbarButton, GUILayout.Width(125))) Safe(BuildProjectSoundtrack);
                GUILayout.FlexibleSpace();
                if (profile != null) EditorGUILayout.LabelField(profile.profileId, EditorStyles.miniLabel, GUILayout.Width(260));
            }
            DrawProjectSoundtrackPanel();
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawProfileCatalog();
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll, GUILayout.ExpandHeight(true));
                    if (profile == null)
                    {
                        EditorGUILayout.HelpBox("Choose a profile. The profile is the runtime contract; graph positions are stored separately in a layout asset.", MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.LabelField("PROFILE CONTRACT", EditorStyles.boldLabel);
                        EditorGUILayout.HelpBox("Sections, cues, transitions, stingers and playback policy are serialized runtime data. Changes are Undoable and are not published until the Publish tab passes validation.", MessageType.Info);
                        DrawProfileProperties(profile);
                        EditorGUILayout.Space(8);
                        EditorGUILayout.LabelField("EDITOR ASSETS", EditorStyles.boldLabel);
                        EditorGUILayout.ObjectField("Profile", profile, typeof(SensoryMusicProfile), false);
                        EditorGUILayout.ObjectField("Graph layout", layout, typeof(AdaptiveMusicGraphLayout), false);
                        if (!profile.HasArrangement && GUILayout.Button("Migrate legacy single arrangement")) Safe(MigrateSelectedLegacy);
                        if (GUILayout.Button("Select profile asset")) { Selection.activeObject = profile; EditorGUIUtility.PingObject(profile); }
                    }
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void DrawProfileCatalog()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(330)))
            {
                EditorGUILayout.LabelField("MUSIC LIBRARY", EditorStyles.boldLabel);
                search = EditorGUILayout.TextField("Search", search);
                catalogScroll = EditorGUILayout.BeginScrollView(catalogScroll, GUILayout.ExpandHeight(true));
                for (int i = 0; i < profiles.Count; i++)
                {
                    var candidate = profiles[i];
                    if (candidate == null || !Matches(candidate.name, candidate.profileId, candidate.sourceRevision)) continue;
                    bool active = candidate == profile;
                    using (new EditorGUILayout.HorizontalScope(active ? "SelectionRect" : GUIStyle.none))
                    {
                        if (GUILayout.Button(candidate.name, EditorStyles.label)) SelectProfile(candidate);
                        GUILayout.Label(candidate.HasArrangement ? candidate.sections.Length + " sections" : "Legacy", EditorStyles.miniLabel, GUILayout.Width(80));
                    }
                }
                if (profiles.Count == 0) EditorGUILayout.HelpBox("No SensoryMusicProfile assets found.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.LabelField(profiles.Count + " profile(s) indexed", EditorStyles.miniLabel);
            }
        }

        private void DrawProfileProperties(SensoryMusicProfile value)
        {
            var serialized = new SerializedObject(value);
            serialized.Update();
            EditorGUI.BeginChangeCheck();
            DrawProperty(serialized, "profileId");
            DrawProperty(serialized, "schemaVersion");
            DrawProperty(serialized, "sourceRevision");
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("LEGACY COMPATIBILITY", EditorStyles.boldLabel);
            DrawProperty(serialized, "bpm"); DrawProperty(serialized, "beatsPerBar"); DrawProperty(serialized, "bars");
            DrawProperty(serialized, "stems"); DrawProperty(serialized, "escapedStinger");
            DrawProperty(serialized, "arrestedStinger"); DrawProperty(serialized, "finePaidStinger");
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("VERSIONED ARRANGEMENT", EditorStyles.boldLabel);
            DrawProperty(serialized, "cues"); DrawProperty(serialized, "sections");
            DrawProperty(serialized, "transitions"); DrawProperty(serialized, "stingers");
            DrawProperty(serialized, "intensity"); DrawProperty(serialized, "playback");
            if (EditorGUI.EndChangeCheck())
            {
                serialized.ApplyModifiedProperties();
                layout = AdaptiveMusicEditorModel.GetOrCreateLayout(value);
                message = "Profile changes applied. Run Validate before publishing.";
                Repaint();
            }
            else serialized.ApplyModifiedProperties();
        }

        private void DrawGraph()
        {
            if (!EnsureProfileForWorkspace()) return;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Auto layout", EditorStyles.toolbarButton, GUILayout.Width(85))) Safe(AutoLayout);
                if (GUILayout.Button("New section", EditorStyles.toolbarButton, GUILayout.Width(90))) Safe(AddSection);
                if (GUILayout.Button("New transition", EditorStyles.toolbarButton, GUILayout.Width(105))) Safe(AddTransition);
                showGrid = GUILayout.Toggle(showGrid, "Grid", EditorStyles.toolbarButton, GUILayout.Width(55));
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("Layout: " + (layout != null ? layout.name : "not created"), EditorStyles.miniLabel, GUILayout.Width(240));
            }
            if (profile.sections == null || profile.sections.Length == 0)
            {
                EditorGUILayout.HelpBox("Add a section or migrate the legacy arrangement before using the graph.", MessageType.Info);
                return;
            }
            graphScroll = EditorGUILayout.BeginScrollView(graphScroll, false, true, GUILayout.ExpandHeight(true));
            Rect canvas = GUILayoutUtility.GetRect(1200, 820, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUI.Box(canvas, GUIContent.none);
            GUI.BeginGroup(canvas);
            if (showGrid) DrawGraphGrid(canvas.width, canvas.height);
            var nodeRects = new Dictionary<string, Rect>(StringComparer.Ordinal);
            for (int i = 0; i < profile.sections.Length; i++)
            {
                var section = profile.sections[i];
                if (section == null || string.IsNullOrWhiteSpace(section.stableId)) continue;
                Vector2 fallback = new Vector2(24 + (i % 4) * 250, 30 + (i / 4) * 160);
                Vector2 position = layout != null ? layout.GetPosition(section.stableId, fallback) : fallback;
                if (!nodeRects.ContainsKey(section.stableId))
                    nodeRects.Add(section.stableId, new Rect(position, new Vector2(220, 118)));
            }
            Handles.BeginGUI();
            foreach (var transition in profile.transitions ?? Array.Empty<MusicTransitionRule>())
            {
                if (transition == null || string.IsNullOrEmpty(transition.toSectionId)) continue;
                Rect to;
                if (!nodeRects.TryGetValue(transition.toSectionId, out to)) continue;
                Rect from;
                if (!nodeRects.TryGetValue(transition.fromSectionId, out from))
                {
                    if (!nodeRects.TryGetValue(SelectedSection != null ? SelectedSection.stableId : transition.toSectionId, out from)) from = to;
                }
                Vector3 start = new Vector3(from.xMax, from.center.y, 0);
                Vector3 end = new Vector3(to.xMin, to.center.y, 0);
                Color color = transition.eligibleContexts == AdaptiveMusicContext.All ? new Color(0.75f, 0.75f, 0.75f) : new Color(0.9f, 0.55f, 0.12f);
                Handles.DrawBezier(start, end, start + Vector3.right * 70, end + Vector3.left * 70, color, null, transition.priority > 15 ? 3f : 1.5f);
            }
            Handles.EndGUI();
            for (int i = 0; i < profile.sections.Length; i++)
            {
                var section = profile.sections[i];
                if (section == null || !nodeRects.TryGetValue(section.stableId, out var node)) continue;
                Rect moved = GUI.Window(1400 + i, node, _ => DrawGraphNode(section), section.displayName ?? section.stableId);
                if (moved.position != node.position && layout != null)
                {
                    if (Event.current.type == EventType.MouseDrag && graphUndoNodes.Add(section.stableId)) Undo.RecordObject(layout, "Move adaptive music graph node");
                    layout.SetPosition(section.stableId, moved.position, false);
                    EditorUtility.SetDirty(layout);
                }
            }
            if (Event.current.type == EventType.MouseUp) graphUndoNodes.Clear();
            GUI.EndGroup();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.HelpBox("Edges are authoring hints for the runtime arbitration graph. The runtime still owns context priority, readiness, quantization and cancellation.", MessageType.Info);
        }

        private void DrawGraphNode(MusicSection section)
        {
            GUILayout.Label(section.stableId, EditorStyles.miniLabel);
            GUILayout.Label(section.kind + " · " + section.eligibleContexts, EditorStyles.miniLabel);
            GUILayout.Label(section.bpm.ToString("0.#") + " BPM · " + section.beatsPerBar + "/4 · " + section.bars + " bars", EditorStyles.miniLabel);
            GUILayout.Label((section.fullMix ? "Full mix · " : string.Empty) + (section.stems == null ? 0 : section.stems.Length) + " stem(s) · " + section.LoopDurationSeconds().ToString("0.00") + "s", EditorStyles.miniLabel);
            if (GUILayout.Button("Inspect", GUILayout.Height(20))) SelectSection(section);
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void DrawTimeline()
        {
            if (!EnsureProfileForWorkspace()) return;
            DrawSectionToolbar("ARRANGEMENT TIMELINE");
            var section = SelectedSection;
            if (section == null) { EditorGUILayout.HelpBox("Select a section from the graph or choose one above.", MessageType.Info); return; }
            EditorGUILayout.HelpBox(section.fullMix
                ? "This is a supplied full-mix track. It is intentionally authored as one base layer and transitions at its section boundary; it is not presented as a fabricated set of synchronized stems."
                : "The grid is a musical timing view. Scrubbing is intentionally not implemented: editor audition starts at a DSP boundary and follows the same section metadata the runtime schedules.", MessageType.Info);
            DrawNestedProperty(profile, "sections", Array.IndexOf(profile.sections, section), "Selected section data");
            EditorGUILayout.Space(8);
            Rect timeline = GUILayoutUtility.GetRect(0, 126, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(timeline, new Color(0.08f, 0.09f, 0.11f));
            int beats = Mathf.Max(1, section.beatsPerBar * section.bars);
            for (int beat = 0; beat <= beats; beat++)
            {
                float x = timeline.x + timeline.width * beat / (float)beats;
                EditorGUI.DrawRect(new Rect(x, timeline.y, beat % section.beatsPerBar == 0 ? 2 : 1, timeline.height), beat % section.beatsPerBar == 0 ? new Color(0.9f, 0.55f, 0.12f, 0.7f) : new Color(1, 1, 1, 0.12f));
                if (beat < beats) GUI.Label(new Rect(x + 3, timeline.y + 4, 56, 18), beat % section.beatsPerBar == 0 ? "BAR " + (beat / section.beatsPerBar + 1) : beat.ToString(), EditorStyles.miniLabel);
            }
            foreach (var marker in section.markers ?? Array.Empty<MusicMarker>())
            {
                if (marker == null || marker.beat < 0 || marker.beat >= beats) continue;
                float x = timeline.x + timeline.width * marker.beat / beats;
                EditorGUI.DrawRect(new Rect(x, timeline.y + 22, 2, timeline.height - 22), new Color(0.25f, 0.9f, 1f, 0.8f));
                GUI.Label(new Rect(x + 3, timeline.y + 22, 130, 18), marker.displayName + " @ " + marker.beat.ToString("0.#"), EditorStyles.miniLabel);
            }
            GUI.Label(new Rect(timeline.x + 8, timeline.yMax - 24, 300, 18), section.LoopDurationSeconds().ToString("0.00") + " seconds loop", EditorStyles.miniLabel);
            DrawStemRows(section);
        }

        private void DrawMixer()
        {
            if (!EnsureProfileForWorkspace()) return;
            DrawSectionToolbar("STEM MIXER");
            var section = SelectedSection;
            if (section == null) { EditorGUILayout.HelpBox("Select a section first.", MessageType.Info); return; }
            EditorGUILayout.HelpBox("Stem gain and intensity curves are authoring data. The runtime sends the resulting target gain through SensoryAudioWorld; this tool never writes mixer parameters or user preferences.", MessageType.Info);
            var stems = section.stems ?? Array.Empty<MusicStem>();
            for (int i = 0; i < stems.Length; i++)
            {
                var stem = stems[i];
                if (stem == null) continue;
                using (new EditorGUILayout.VerticalScope("HelpBox"))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(stem.displayName + "  [" + stem.role + "]", EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        bool solo = soloStemId == stem.stableId;
                        if (GUILayout.Button(solo ? "Unsolo" : "Solo", EditorStyles.miniButton, GUILayout.Width(58))) soloStemId = solo ? string.Empty : stem.stableId;
                        EditorGUILayout.LabelField(stem.clip == null ? "MISSING CLIP" : stem.clip.name, EditorStyles.miniLabel, GUILayout.Width(180));
                    }
                    var serialized = new SerializedObject(profile); serialized.Update();
                    var sectionProperty = serialized.FindProperty("sections").GetArrayElementAtIndex(Array.IndexOf(profile.sections, section));
                    var stemProperty = sectionProperty.FindPropertyRelative("stems").GetArrayElementAtIndex(i);
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(stemProperty.FindPropertyRelative("stableId"), new GUIContent("Stable ID"));
                    EditorGUILayout.PropertyField(stemProperty.FindPropertyRelative("role"));
                    EditorGUILayout.PropertyField(stemProperty.FindPropertyRelative("clip"));
                    EditorGUILayout.PropertyField(stemProperty.FindPropertyRelative("gain"));
                    EditorGUILayout.PropertyField(stemProperty.FindPropertyRelative("intensityGain"));
                    EditorGUILayout.PropertyField(stemProperty.FindPropertyRelative("entryOffsetBeats"));
                    EditorGUILayout.PropertyField(stemProperty.FindPropertyRelative("alwaysOn"));
                    EditorGUILayout.PropertyField(stemProperty.FindPropertyRelative("critical"));
                    EditorGUILayout.PropertyField(stemProperty.FindPropertyRelative("allowPhaseCompensation"));
                    EditorGUILayout.PropertyField(stemProperty.FindPropertyRelative("loadPolicy"));
                    if (EditorGUI.EndChangeCheck()) { serialized.ApplyModifiedProperties(); message = "Stem mixer changes applied."; }
                    else serialized.ApplyModifiedProperties();
                }
            }
            if (stems.Length == 0) EditorGUILayout.HelpBox("This section has no stems. Add them in the selected section inspector.", MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preview selected section")) Safe(() => PreviewSection(section));
                if (GUILayout.Button("Stop preview")) StopPreview();
                if (!string.IsNullOrEmpty(soloStemId)) EditorGUILayout.LabelField("Solo: " + soloStemId, EditorStyles.miniLabel);
            }
        }

        private void DrawTransport()
        {
            if (!EnsureProfileForWorkspace()) return;
            EditorGUILayout.LabelField("TRANSPORT & SIMULATION", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Use the buttons to inject a semantic gameplay snapshot into the live director while playing the fixture. The injection is a test adapter; it does not modify GameFlow, heat or pursuit authorities.", MessageType.Info);
            previewIntensity = EditorGUILayout.Slider("Preview intensity", previewIntensity, 0, 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < Contexts.Length; i++)
                {
                    var context = Contexts[i];
                    if (GUILayout.Button(context.ToString(), GUILayout.Width(76))) SetSimulationContext(context);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preview current section")) Safe(() => PreviewSection(SelectedSection));
                if (GUILayout.Button("Stop all preview")) StopPreview();
                if (GUILayout.Button("Clear live snapshot")) ClearLiveSnapshot();
            }
            EditorGUILayout.Space(8);
            var director = FindRuntimeDirector();
            if (director == null)
            {
                EditorGUILayout.HelpBox("No AdaptiveMusic director is loaded. Enter Play mode with AdaptiveMusicTest.unity, or use editor preview.", MessageType.Info);
            }
            else
            {
                var transport = director.Transport;
                EditorGUILayout.LabelField("LIVE RUNTIME", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("State", transport.running ? transport.paused ? "Paused" : "Running" : "Stopped");
                EditorGUILayout.LabelField("Context", transport.context.ToString());
                EditorGUILayout.LabelField("Active section", transport.activeSectionId);
                EditorGUILayout.LabelField("Pending", string.IsNullOrEmpty(transport.pendingSectionId) ? "—" : transport.pendingSectionId + " @ " + transport.scheduledAt.ToString("0.000"));
                if (!string.IsNullOrEmpty(transport.bridgeSectionId))
                    EditorGUILayout.LabelField("Bridge", transport.bridgeSectionId + " → " + transport.pendingFinalSectionId);
                EditorGUILayout.LabelField("Clock", "DSP " + transport.dspTime.ToString("0.000") + " · beat " + transport.beat.ToString("0.00") + " · bar " + transport.bar);
                EditorGUILayout.LabelField("Stems", transport.activeStemCount + " active · " + transport.scheduledStemCount + " scheduled · " + transport.droppedStemCount + " dropped");
                if (!string.IsNullOrEmpty(transport.lastError)) EditorGUILayout.HelpBox(transport.lastError, MessageType.Warning);
                if (GUILayout.Button("Refresh runtime diagnostics")) RefreshRuntimeDiagnostics(director);
                DrawRuntimeStemDiagnostics();
                if (showTrace) DrawRuntimeTrace();
            }
            DrawStingerInspector();
        }

        private void DrawStingerInspector()
        {
            EditorGUILayout.Space(8); EditorGUILayout.LabelField("STINGER LIBRARY", EditorStyles.boldLabel);
            var stingers = profile.stingers ?? Array.Empty<MusicStinger>();
            string[] names = new string[stingers.Length + 1]; names[0] = "— select —";
            for (int i = 0; i < stingers.Length; i++) names[i + 1] = stingers[i] == null ? "<null>" : stingers[i].displayName + " [" + stingers[i].stableId + "]";
            int current = 0; for (int i = 0; i < stingers.Length; i++) if (stingers[i] == SelectedStinger) current = i + 1;
            int next = EditorGUILayout.Popup("Selected stinger", current, names);
            if (next != current) selectedStingerId = next == 0 || stingers[next - 1] == null ? string.Empty : stingers[next - 1].stableId;
            if (SelectedStinger != null)
            {
                EditorGUILayout.LabelField("Eligibility", SelectedStinger.eligibleContexts.ToString());
                EditorGUILayout.LabelField("Quantization", SelectedStinger.quantization.ToString());
                EditorGUILayout.LabelField("Cooldown", SelectedStinger.cooldownSeconds.ToString("0.##") + "s · repeat suppression " + SelectedStinger.repetitionSuppressionSeconds.ToString("0.##") + "s");
                if (GUILayout.Button("Preview stinger")) Safe(() => PreviewStinger(SelectedStinger));
                var director = FindRuntimeDirector();
                using (new EditorGUI.DisabledScope(director == null || !Application.isPlaying))
                {
                    if (GUILayout.Button(Application.isPlaying ? "Queue stinger on live director" : "Queue stinger (Play mode only)"))
                        director.RequestStinger(SelectedStinger.stableId, "adaptive-music-editor");
                }
            }
        }

        private void DrawValidation()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Run validation", EditorStyles.toolbarButton, GUILayout.Width(100))) RunValidation();
                string[] filters = { "All", "Info", "Warning", "Error" };
                int filter = severityFilter + 1; int next = EditorGUILayout.Popup(filter, filters, GUILayout.Width(90)); severityFilter = next - 1;
                diagnosticSearch = EditorGUILayout.TextField(diagnosticSearch, GUILayout.ExpandWidth(true));
                GUILayout.Label(Count(AdaptiveMusicDiagnosticSeverity.Error) + " errors", EditorStyles.miniLabel, GUILayout.Width(70));
            }
            if (diagnostics.Count == 0) RunValidation();
            EditorGUILayout.HelpBox("Validation is deterministic and read-only. Publish refuses errors; warnings remain visible so asset readiness and streaming decisions are explicit.", MessageType.Info);
            validationScroll = EditorGUILayout.BeginScrollView(validationScroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < diagnostics.Count; i++)
            {
                var diagnostic = diagnostics[i];
                if (diagnostic == null || severityFilter >= 0 && (int)diagnostic.severity != severityFilter || !MatchesDiagnostic(diagnostic.code, diagnostic.message, diagnostic.target != null ? diagnostic.target.name : string.Empty)) continue;
                MessageType type = diagnostic.severity == AdaptiveMusicDiagnosticSeverity.Error ? MessageType.Error : diagnostic.severity == AdaptiveMusicDiagnosticSeverity.Warning ? MessageType.Warning : MessageType.Info;
                using (new EditorGUILayout.VerticalScope("HelpBox"))
                {
                    EditorGUILayout.HelpBox(diagnostic.message, type);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(diagnostic.code, EditorStyles.miniLabel, GUILayout.Width(170));
                        if (diagnostic.target != null && GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(60))) { Selection.activeObject = diagnostic.target; EditorGUIUtility.PingObject(diagnostic.target); }
                        if (diagnostic.code == "PROFILE_MIGRATION" && GUILayout.Button("Migrate", EditorStyles.miniButton, GUILayout.Width(65))) Safe(MigrateSelectedLegacy);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawPublish()
        {
            if (!EnsureProfileForWorkspace()) return;
            EditorGUILayout.LabelField("PUBLISH & MIGRATION", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Publish computes a deterministic source revision and saves the profile/layout only when there are no validation errors. There is no destructive bake step, so the last valid asset remains intact after a failed validation.", MessageType.Info);
            if (GUILayout.Button("Validate before publish")) RunValidation();
            int errors = Count(AdaptiveMusicDiagnosticSeverity.Error);
            EditorGUILayout.LabelField("Selected profile", profile.name);
            EditorGUILayout.LabelField("Schema", profile.schemaVersion + " / " + SensoryMusicProfile.CurrentSchemaVersion);
            EditorGUILayout.LabelField("Current source revision", profile.sourceRevision);
            EditorGUILayout.LabelField("Computed source revision", AdaptiveMusicEditorModel.ComputeSourceRevision(profile));
            EditorGUILayout.LabelField("Validation errors", errors.ToString());
            using (new EditorGUI.DisabledScope(errors > 0))
            {
                if (GUILayout.Button("Publish arrangement")) Safe(Publish);
            }
            if (GUILayout.Button("Open authoring guide")) OpenGuide();
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("RUNTIME OWNERSHIP", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Transport", "AdaptiveMusic (one owner per runtime context)");
            EditorGUILayout.LabelField("Voice/mixer admission", "SensoryAudioWorld");
            EditorGUILayout.LabelField("Gameplay authority", "GameFlow / pursuit / race adapters");
            EditorGUILayout.LabelField("Editor preview", "Disposable AudioSources; no gameplay or user mix writes");
        }

        private void DrawSectionToolbar(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            var sections = profile.sections ?? Array.Empty<MusicSection>();
            string[] names = new string[sections.Length + 1]; names[0] = "— select section —";
            int current = 0;
            for (int i = 0; i < sections.Length; i++)
            {
                names[i + 1] = sections[i] == null ? "<null>" : sections[i].displayName + " [" + sections[i].stableId + "]";
                if (sections[i] != null && sections[i].stableId == selectedSectionId) current = i + 1;
            }
            int next = EditorGUILayout.Popup("Section", current, names);
            if (next != current) selectedSectionId = next == 0 || sections[next - 1] == null ? string.Empty : sections[next - 1].stableId;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Preview", EditorStyles.toolbarButton, GUILayout.Width(70))) Safe(() => PreviewSection(SelectedSection));
                if (GUILayout.Button("Stop", EditorStyles.toolbarButton, GUILayout.Width(60))) StopPreview();
                if (SelectedSection != null) EditorGUILayout.LabelField(SelectedSection.LoopDurationSeconds().ToString("0.00") + "s loop", EditorStyles.miniLabel);
            }
        }

        private void DrawNestedProperty(SensoryMusicProfile value, string arrayName, int index, string title)
        {
            if (value == null || index < 0) return;
            var serialized = new SerializedObject(value); serialized.Update();
            var array = serialized.FindProperty(arrayName);
            if (array == null || index >= array.arraySize) return;
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck(); EditorGUILayout.PropertyField(array.GetArrayElementAtIndex(index), true);
            if (EditorGUI.EndChangeCheck()) { serialized.ApplyModifiedProperties(); message = "Arrangement data applied."; }
            else serialized.ApplyModifiedProperties();
        }

        private void DrawStemRows(MusicSection section)
        {
            EditorGUILayout.LabelField("STEM LANES", EditorStyles.boldLabel);
            foreach (var stem in section.stems ?? Array.Empty<MusicStem>())
            {
                if (stem == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(stem.displayName + " · " + stem.role, GUILayout.Width(190));
                    Rect bar = GUILayoutUtility.GetRect(180, 18, GUILayout.ExpandWidth(true));
                    float curve = stem.intensityGain == null ? 1 : stem.intensityGain.Evaluate(previewIntensity);
                    EditorGUI.ProgressBar(bar, Mathf.Clamp01(stem.gain * curve), stem.clip == null ? "Missing clip" : stem.clip.name);
                    EditorGUILayout.LabelField(stem.gain.ToString("0.00"), EditorStyles.miniLabel, GUILayout.Width(42));
                }
            }
        }

        private void DrawRuntimeStemDiagnostics()
        {
            if (stemDiagnostics.Count == 0) return;
            EditorGUILayout.LabelField("STEM DIAGNOSTICS", EditorStyles.boldLabel);
            for (int i = 0; i < stemDiagnostics.Count; i++)
            {
                var stem = stemDiagnostics[i];
                EditorGUILayout.LabelField(stem.sectionId + "/" + stem.displayName,
                    (stem.playing ? "Playing" : stem.scheduled ? "Scheduled" : "Released") + " · " + stem.assetState + " · gain " + stem.currentGain.ToString("0.00"));
            }
        }

        private void DrawRuntimeTrace()
        {
            EditorGUILayout.Space(6); showTrace = EditorGUILayout.Foldout(showTrace, "TRACE (bounded)", true);
            if (!showTrace) return;
            traceScroll = EditorGUILayout.BeginScrollView(traceScroll, GUILayout.Height(190));
            for (int i = trace.Count - 1; i >= 0; i--)
            {
                var item = trace[i];
                EditorGUILayout.LabelField(item.dspTime.ToString("0.000") + "  " + item.eventName + "  " + item.fromSectionId + " → " + item.toSectionId + "  " + item.detail, EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        private void SelectProfile(SensoryMusicProfile value)
        {
            profile = value;
            layout = AdaptiveMusicEditorModel.GetOrCreateLayout(profile);
            selectedSectionId = profile != null && profile.sections != null && profile.sections.Length > 0 && profile.sections[0] != null ? profile.sections[0].stableId : string.Empty;
            selectedTransitionId = string.Empty; selectedStingerId = string.Empty; soloStemId = string.Empty;
            Repaint();
        }

        private void SelectSection(MusicSection value)
        {
            if (value == null) return;
            selectedSectionId = value.stableId; tab = 2; Repaint();
        }

        private void RefreshCatalog()
        {
            profiles.Clear(); profiles.AddRange(AdaptiveMusicEditorModel.FindProfiles());
            soundtrackScan = AdaptiveMusicProjectSoundtrack.Scan();
            if (profile == null || !profiles.Contains(profile))
                SelectProfile(profiles.Count > 0 ? profiles[0] : null);
            else layout = AdaptiveMusicEditorModel.GetOrCreateLayout(profile);
            Repaint();
        }

        private void SyncSelection()
        {
            if (Selection.activeObject is SensoryMusicProfile selected) SelectProfile(selected);
        }

        private void OnUndoRedo() { RefreshCatalog(); SceneView.RepaintAll(); }
        private void OnEditorUpdate() { if (preview != null && preview.IsPlaying) Repaint(); }
        private void OnPlayModeStateChanged(PlayModeStateChange state) { if (state != PlayModeStateChange.EnteredPlayMode) StopPreview(); Repaint(); }

        private bool EnsureProfileForWorkspace()
        {
            if (profile != null) return true;
            EditorGUILayout.HelpBox("Select a SensoryMusicProfile in Library first.", MessageType.Info);
            return false;
        }

        private void CreateProfile()
        {
            SelectProfile(AdaptiveMusicEditorModel.CreateProfile());
            message = "Created an empty versioned profile. Add sections, cues, clips and transitions, then validate.";
        }

        private void BuildFixture()
        {
            SelectProfile(AdaptiveMusicEditorModel.BuildSyntheticFixture());
            message = "Built the deterministic original adaptive music fixture. Use Build test scene for play-mode controls.";
        }

        private void BuildProjectSoundtrack()
        {
            soundtrackScan = AdaptiveMusicProjectSoundtrack.Scan();
            if (!soundtrackScan.HasPlayableTracks)
            {
                message = soundtrackScan.BuildFailureMessage();
                tab = 0;
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<SensoryMusicProfile>(AdaptiveMusicProjectSoundtrack.SoundtrackProfilePath) != null
                && !EditorUtility.DisplayDialog("Update soundtrack profile?",
                    "This updates the generated arrangement at " + AdaptiveMusicProjectSoundtrack.SoundtrackProfilePath + ". Source audio is not changed.",
                    "Update", "Cancel")) return;
            var imported = AdaptiveMusicProjectSoundtrack.BuildProfile(soundtrackScan, true);
            SelectProfile(imported);
            message = "Built a full-mix soundtrack profile from " + soundtrackScan.playableTracks.Count + " AudioClip(s). Edit the generated cues and transition rules, then publish.";
        }

        private void DrawProjectSoundtrackPanel()
        {
            if (soundtrackScan == null) soundtrackScan = AdaptiveMusicProjectSoundtrack.Scan();
            EditorGUILayout.LabelField("PROJECT SOUNDTRACK", EditorStyles.boldLabel);
            if (soundtrackScan.SourceCount == 0)
            {
                EditorGUILayout.HelpBox("No source files were found under " + AdaptiveMusicProjectSoundtrack.MusicRoot + ".", MessageType.Info);
                return;
            }
            EditorGUILayout.LabelField("Sources", soundtrackScan.SourceCount + " · playable AudioClips " + soundtrackScan.playableTracks.Count
                + " · unsupported " + soundtrackScan.unsupportedSources.Count + " · waiting for import " + soundtrackScan.unimportedSupportedSources.Count);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (soundtrackScan.HasPlayableTracks && GUILayout.Button("Build profile from playable clips")) Safe(BuildProjectSoundtrack);
                if (GUILayout.Button("Reveal source folder", GUILayout.Width(135)))
                    EditorUtility.RevealInFinder(Path.Combine(Application.dataPath, "Audio", "Music"));
            }
            if (soundtrackScan.HasUnsupportedSources)
            {
                EditorGUILayout.HelpBox("Unsupported source formats are kept intact but cannot be assigned to AudioClip fields. Convert the listed sources to a Unity-supported format, keep the converted files under the same folder, then press Refresh.", MessageType.Warning);
                int limit = Mathf.Min(4, soundtrackScan.unsupportedSources.Count);
                for (int i = 0; i < limit; i++) EditorGUILayout.LabelField("Unsupported", soundtrackScan.unsupportedSources[i], EditorStyles.miniLabel);
                if (soundtrackScan.unsupportedSources.Count > limit)
                    EditorGUILayout.LabelField("…", (soundtrackScan.unsupportedSources.Count - limit) + " more unsupported source(s)", EditorStyles.miniLabel);
            }
            if (soundtrackScan.unimportedSupportedSources.Count > 0)
            {
                EditorGUILayout.HelpBox("Some supported-extension files are present but are not AudioClip assets yet. Wait for Unity to finish importing or reimport them.", MessageType.Info);
            }
            int trackLimit = Mathf.Min(6, soundtrackScan.playableTracks.Count);
            for (int i = 0; i < trackLimit; i++)
            {
                var track = soundtrackScan.playableTracks[i];
                EditorGUILayout.LabelField(track.folder + " / " + track.displayName, track.clip.length.ToString("0.0") + " s", EditorStyles.miniLabel);
            }
            if (soundtrackScan.playableTracks.Count > trackLimit)
                EditorGUILayout.LabelField("…", (soundtrackScan.playableTracks.Count - trackLimit) + " more playable soundtrack clip(s)", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);
        }

        private void MigrateSelectedLegacy()
        {
            if (profile == null) return;
            if (profile.HasArrangement) { message = "Selected profile already has a versioned arrangement."; return; }
            AdaptiveMusicEditorModel.MigrateLegacy(profile);
            layout = AdaptiveMusicEditorModel.GetOrCreateLayout(profile);
            RefreshCatalog();
            message = "Migrated legacy stems and outcome clips into versioned sections, cues, transitions and stingers.";
        }

        private void AutoLayout()
        {
            layout = AdaptiveMusicEditorModel.GetOrCreateLayout(profile);
            if (layout == null) return;
            if (profile.sections == null) { message = "Profile has no sections to lay out."; return; }
            Undo.RecordObject(layout, "Auto layout adaptive music graph");
            for (int i = 0; i < profile.sections.Length; i++)
                if (profile.sections[i] != null) layout.SetPosition(profile.sections[i].stableId, new Vector2(24 + (i % 4) * 250, 30 + (i / 4) * 160));
            EditorUtility.SetDirty(layout); AssetDatabase.SaveAssetIfDirty(layout); message = "Graph layout updated.";
        }

        private void AddSection()
        {
            Undo.RecordObject(profile, "Add adaptive music section");
            var list = new List<MusicSection>(profile.sections ?? Array.Empty<MusicSection>());
            string id = UniqueId("section");
            list.Add(new MusicSection { stableId = id, displayName = "New Section", eligibleContexts = AdaptiveMusicContext.FreeRoam,
                bpm = profile.bpm, beatsPerBar = profile.beatsPerBar, bars = profile.bars, harmonicFamily = "default",
                gridEnabled = true, stems = Array.Empty<MusicStem>() });
            profile.sections = list.ToArray(); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
            layout = AdaptiveMusicEditorModel.GetOrCreateLayout(profile); selectedSectionId = id; message = "Added " + id + ". Add a cue and at least one ready stem before publishing.";
        }

        private void AddTransition()
        {
            if (profile.sections == null || profile.sections.Length == 0) { message = "Add a section first."; return; }
            Undo.RecordObject(profile, "Add adaptive music transition");
            var list = new List<MusicTransitionRule>(profile.transitions ?? Array.Empty<MusicTransitionRule>());
            MusicSection firstSection = null;
            for (int i = 0; i < profile.sections.Length; i++)
                if (profile.sections[i] != null && !string.IsNullOrWhiteSpace(profile.sections[i].stableId)) { firstSection = profile.sections[i]; break; }
            if (firstSection == null) { message = "Add a valid section ID first."; return; }
            string to = SelectedSection != null ? SelectedSection.stableId : firstSection.stableId;
            string id = UniqueId("transition");
            list.Add(new MusicTransitionRule { stableId = id, displayName = "Any → " + to, fromSectionId = "*", toSectionId = to,
                eligibleContexts = SelectedSection != null ? SelectedSection.eligibleContexts : AdaptiveMusicContext.All,
                priority = 10, quantization = MusicQuantization.Bar, minimumDwellSeconds = 0.4f });
            profile.transitions = list.ToArray(); selectedTransitionId = id; EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
            message = "Added transition " + id + ".";
        }

        private string UniqueId(string prefix)
        {
            int index = 1;
            while (true)
            {
                string candidate = prefix + "." + index++;
                bool used = false;
                foreach (var section in profile.sections ?? Array.Empty<MusicSection>()) if (section != null && section.stableId == candidate) used = true;
                foreach (var transition in profile.transitions ?? Array.Empty<MusicTransitionRule>()) if (transition != null && transition.stableId == candidate) used = true;
                if (!used) return candidate;
            }
        }

        private void PreviewSection(MusicSection section)
        {
            if (section == null) { message = "Select a section before previewing."; return; }
            if (preview == null) preview = new AdaptiveMusicPreviewSession();
            if (!preview.StartSection(profile, section, previewIntensity)) message = preview.Error;
            else message = "Auditioning " + section.displayName + " on an editor-only DSP schedule.";
        }

        private void PreviewStinger(MusicStinger stinger)
        {
            if (stinger == null) return;
            if (preview == null) preview = new AdaptiveMusicPreviewSession();
            if (!preview.StartStinger(stinger)) message = preview.Error;
            else message = "Auditioning stinger " + stinger.displayName + ".";
        }

        private void StopPreview()
        {
            if (preview == null) return;
            preview.Dispose(); preview = null; message = "Editor preview stopped and its sources were destroyed.";
        }

        private void SetSimulationContext(AdaptiveMusicContext context)
        {
            float intensity = context == AdaptiveMusicContext.Pursuit ? 0.82f : context == AdaptiveMusicContext.Race ? 0.55f : context == AdaptiveMusicContext.Escape ? 0.45f : previewIntensity;
            var director = FindRuntimeDirector();
            if (director != null && Application.isPlaying)
            {
                director.SetGameplaySnapshot(MusicGameplaySnapshot.ForContext(context, intensity, "adaptive-music-editor"));
                message = "Injected " + context + " into the live director.";
            }
            else
            {
                string sectionId = profile.DefaultSectionFor(context);
                PreviewSection(profile.FindSection(sectionId));
            }
        }

        private void ClearLiveSnapshot()
        {
            var director = FindRuntimeDirector();
            if (director != null && Application.isPlaying) { director.ClearGameplaySnapshot(); message = "Live director returned to runtime gameplay adapters."; }
        }

        private void RefreshRuntimeDiagnostics(AdaptiveMusic director)
        {
            trace.Clear(); stemDiagnostics.Clear();
            director.CopyTrace(trace); director.CopyStemDiagnostics(stemDiagnostics); Repaint();
        }

        private AdaptiveMusic FindRuntimeDirector() => UnityEngine.Object.FindAnyObjectByType<AdaptiveMusic>(FindObjectsInactive.Include);

        private void Publish()
        {
            diagnostics.Clear(); diagnostics.AddRange(AdaptiveMusicEditorModel.ValidateProfile(profile));
            if (Count(AdaptiveMusicDiagnosticSeverity.Error) > 0) { message = "Publish blocked by validation errors."; tab = 5; return; }
            Undo.RecordObject(profile, "Publish adaptive music arrangement");
            profile.sourceRevision = AdaptiveMusicEditorModel.ComputeSourceRevision(profile);
            EditorUtility.SetDirty(profile);
            layout = AdaptiveMusicEditorModel.GetOrCreateLayout(profile);
            if (layout != null) AssetDatabase.SaveAssetIfDirty(layout);
            AssetDatabase.SaveAssetIfDirty(profile); AssetDatabase.SaveAssets();
            message = "Published source revision " + profile.sourceRevision + ". Runtime consumes the profile on the next configure/scene load.";
        }

        private void DrawGraphGrid(float width, float height)
        {
            for (float x = 0; x < width; x += 40) EditorGUI.DrawRect(new Rect(x, 0, 1, height), new Color(1, 1, 1, 0.035f));
            for (float y = 0; y < height; y += 40) EditorGUI.DrawRect(new Rect(0, y, width, 1), new Color(1, 1, 1, 0.035f));
        }

        private void DrawProperty(SerializedObject serialized, string name)
        {
            var property = serialized.FindProperty(name);
            if (property != null) EditorGUILayout.PropertyField(property, true);
        }

        private int Count(AdaptiveMusicDiagnosticSeverity severity)
        {
            int count = 0; for (int i = 0; i < diagnostics.Count; i++) if (diagnostics[i] != null && diagnostics[i].severity == severity) count++; return count;
        }

        private bool Matches(string a, string b, string c)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            return Contains(a, search) || Contains(b, search) || Contains(c, search);
        }

        private bool MatchesDiagnostic(string code, string text, string target)
        {
            if (string.IsNullOrWhiteSpace(diagnosticSearch)) return true;
            return Contains(code, diagnosticSearch) || Contains(text, diagnosticSearch) || Contains(target, diagnosticSearch);
        }

        private static bool Contains(string value, string query) => !string.IsNullOrEmpty(value) && value.IndexOf(query ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;

        private void Safe(Action action)
        {
            try { action?.Invoke(); }
            catch (Exception exception) { message = exception.Message; Debug.LogException(exception); }
        }

        private void OpenGuide()
        {
            string path = Path.GetFullPath("Assets/NfsMw/Modules/Driving/ADAPTIVE_MUSIC_EDITOR.md");
            if (File.Exists(path)) Application.OpenURL("file://" + path);
            else message = "Guide will be available at " + path;
        }
    }
}
#endif
