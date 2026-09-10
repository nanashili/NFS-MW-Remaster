using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Mission authoring and debugging workbench. The canvas is deliberately a
    /// thin view: every semantic edit is committed to MissionDefinitionAsset and
    /// checked by MissionGraph before the asset changes.
    /// </summary>
    public sealed class MissionObjectiveGraphWindow : EditorWindow
    {
        private static readonly string[] Tabs = { "Graph", "Inspector", "Validation", "Variables", "Simulation", "Trace", "Publish" };
        private static readonly string[] BuiltInKinds = { "event", "sequence", "condition", "hold", "timer", "deadline" };
        private const float NodeWidth = 238f;
        private const float NodeHeight = 150f;
        private const float CollapsedNodeHeight = 34f;

        private MissionDefinitionAsset definitionAsset;
        private MissionGraphLayoutAsset layoutAsset;
        private MissionDefinition definition;
        private string sourceError = string.Empty;
        private string status = "Select a Mission Graph asset or create a demo.";
        private string rawJson = string.Empty;
        private string rawJsonAssetPath = string.Empty;
        private readonly List<MissionDefinitionAsset> catalog = new List<MissionDefinitionAsset>();
        private readonly HashSet<string> selectedNodes = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<MissionGraphDiagnostic> diagnostics = new List<MissionGraphDiagnostic>();
        private readonly List<string> traceFilter = new List<string>();
        private Vector2 catalogScroll;
        private Vector2 graphPan = new Vector2(40, 40);
        private float graphZoom = 1f;
        private int tab;
        private string catalogSearch = string.Empty;
        private string graphSearch = string.Empty;
        private string diagnosticSearch = string.Empty;
        private string connectSource;
        private string selectedEdgeKey;
        private string draggingNode;
        private bool draggingNodes;
        private bool panning;
        private Vector2 lastCanvasMouse;
        private Vector2 dragStartGraph;
        private readonly Dictionary<string, Vector2> dragStartPositions = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        private MissionObjective objectiveBuffer;
        private string objectiveBufferId = string.Empty;
        private MissionDefinition missionBuffer;
        private string missionBufferHash = string.Empty;
        private string clipboardPayload = string.Empty;
        private MissionRuntime simulation;
        private PreviewActions previewActions;
        private long simulationStep;
        private MissionHost attachedHost;
        private double simulationGameDelta = 1d;
        private double simulationRealDelta = 1d;
        private string eventId = "preview.1";
        private string eventType = "event.preview";
        private string eventTarget = string.Empty;
        private string eventFact = string.Empty;
        private double eventValue = 1d;
        private bool injectEvent = true;
        private bool showControlEdges = true;
        private bool showConditionEdges = true;
        private bool showParentEdges;
        private bool showMinimap = true;
        private bool showComments = true;
        private bool showGroups = true;
        private Vector2 inspectorScroll;
        private Vector2 variablesScroll;
        private Vector2 simulationScroll;
        private Vector2 traceScroll;
        private Vector2 publishScroll;
        private string variableSearch = string.Empty;
        private string simulationStatus = string.Empty;
        private bool showInfoDiagnostics = true;
        private bool showWarningDiagnostics = true;
        private bool showErrorDiagnostics = true;

        public MissionDefinitionAsset DefinitionAsset => definitionAsset;

        [MenuItem("NFS MW Remaster/Driving/Mission Objective Graph")]
        public static void Open()
        {
            var window = GetWindow<MissionObjectiveGraphWindow>();
            window.titleContent = new GUIContent("Mission Objective Graph");
            window.minSize = new Vector2(1120, 680);
            window.RefreshCatalog();
            window.Focus();
        }

        [OnOpenAsset(2)]
        private static bool OpenMissionAsset(EntityId instanceId, int line)
        {
            var asset = EditorUtility.EntityIdToObject(instanceId) as MissionDefinitionAsset;
            if (asset == null) return false;
            Open();
            GetWindow<MissionObjectiveGraphWindow>().SetDefinition(asset);
            return true;
        }

        public static void SelectDefinition(MissionDefinitionAsset asset)
        {
            Open();
            GetWindow<MissionObjectiveGraphWindow>().SetDefinition(asset);
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Mission Objective Graph");
            minSize = new Vector2(1120, 680);
            Selection.selectionChanged += SelectionChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.projectChanged += OnProjectChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += DisposeSimulation;
            RefreshCatalog();
            if (definitionAsset == null && Selection.activeObject is MissionDefinitionAsset selected) SetDefinition(selected);
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= SelectionChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= DisposeSimulation;
            DisposeSimulation();
            if (layoutAsset != null && !AssetDatabase.Contains(layoutAsset)) DestroyImmediate(layoutAsset);
        }

        private void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.Add(new IMGUIContainer(DrawWindow) { style = { flexGrow = 1 } });
        }

        private void SelectionChanged()
        {
            if (Selection.activeObject is MissionDefinitionAsset selected && selected != definitionAsset) SetDefinition(selected);
        }

        private void OnUndoRedo()
        {
            if (definitionAsset != null) ReloadFromAsset(true);
            Repaint();
        }

        private void OnProjectChanged()
        {
            RefreshCatalog();
            if (definitionAsset != null) ReloadFromAsset(true);
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode) DisposeSimulation();
            Repaint();
        }

        private void DrawWindow()
        {
            DrawHeader();
            EditorGUILayout.BeginHorizontal();
            DrawCatalogPanel();
            EditorGUILayout.BeginVertical();
            if (definitionAsset == null)
            {
                DrawWelcome();
            }
            else
            {
                DrawWorkspaceToolbar();
                tab = GUILayout.Toolbar(tab, Tabs);
                EditorGUILayout.Space(4);
                switch (tab)
                {
                    case 0: DrawGraphTab(); break;
                    case 1: DrawInspectorTab(); break;
                    case 2: DrawValidationTab(); break;
                    case 3: DrawVariablesTab(); break;
                    case 4: DrawSimulationTab(); break;
                    case 5: DrawTraceTab(); break;
                    case 6: DrawPublishTab(); break;
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.None);
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("MISSION / OBJECTIVE GRAPH", EditorStyles.boldLabel, GUILayout.Width(205));
                EditorGUI.BeginChangeCheck();
                var chosen = (MissionDefinitionAsset)EditorGUILayout.ObjectField(definitionAsset, typeof(MissionDefinitionAsset), false, GUILayout.Width(260));
                if (EditorGUI.EndChangeCheck()) SetDefinition(chosen);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Create Demo", EditorStyles.toolbarButton, GUILayout.Width(90))) CreateDemo();
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70))) { RefreshCatalog(); ReloadFromAsset(true); }
                if (GUILayout.Button("Save Assets", EditorStyles.toolbarButton, GUILayout.Width(80))) AssetDatabase.SaveAssets();
            }
        }

        private void DrawCatalogPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(270)))
            {
                EditorGUILayout.LabelField("Mission library", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                catalogSearch = EditorGUILayout.TextField("Search", catalogSearch);
                if (EditorGUI.EndChangeCheck()) Repaint();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("New")) CreateMissionAsset();
                    if (GUILayout.Button("Open JSON")) { tab = 6; rawJson = definitionAsset == null ? string.Empty : definitionAsset.Json; }
                }
                catalogScroll = EditorGUILayout.BeginScrollView(catalogScroll, GUILayout.ExpandHeight(true));
                foreach (var asset in catalog.Where(MatchesCatalog).OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase))
                {
                    bool active = asset == definitionAsset;
                    var label = asset.name;
                    try
                    {
                        var graph = asset.Compile();
                        label += "\n" + graph.Id + " · " + graph.Definition().objectives.Length + " nodes";
                    }
                    catch (Exception exception) { label += "\nInvalid: " + FirstLine(exception.Message); }
                    var old = GUI.backgroundColor;
                    if (active) GUI.backgroundColor = new Color(0.22f, 0.48f, 0.72f);
                    if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.MinHeight(38))) SetDefinition(asset);
                    GUI.backgroundColor = old;
                }
                EditorGUILayout.EndScrollView();
                if (definitionAsset != null)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
                    EditorGUILayout.SelectableLabel(AssetDatabase.GetAssetPath(definitionAsset), EditorStyles.miniLabel, GUILayout.Height(30));
                    if (GUILayout.Button("Select asset")) Selection.activeObject = definitionAsset;
                    if (GUILayout.Button("Create / select layout sidecar")) EnsurePersistentLayout();
                }
            }
        }

        private bool MatchesCatalog(MissionDefinitionAsset asset)
        {
            if (asset == null) return false;
            if (string.IsNullOrWhiteSpace(catalogSearch)) return true;
            string text = asset.name + " " + AssetDatabase.GetAssetPath(asset);
            try { text += " " + asset.Compile().Id + " " + asset.Compile().Definition().title; } catch { }
            return text.IndexOf(catalogSearch, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawWelcome()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Mission Objective Graph Editor", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("Author reusable sequences, branches, timers, pursuit goals, checkpoints and reward references against the existing MissionRuntime.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(10);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create production demo", GUILayout.Height(32))) CreateDemo();
                if (GUILayout.Button("Create mission asset", GUILayout.Height(32))) CreateMissionAsset();
            }
            EditorGUILayout.HelpBox("The graph is an editor view. Runtime semantics remain owned by MissionGraph and MissionRuntime; layout, comments, bookmarks and reroutes live in a separate editor-only sidecar.", MessageType.Info);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
        }

        private void DrawWorkspaceToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Add node", EditorStyles.toolbarDropDown, GUILayout.Width(78))) ShowNodeMenu(null);
                if (GUILayout.Button("Delete", EditorStyles.toolbarButton, GUILayout.Width(60))) DeleteSelected();
                if (GUILayout.Button("Copy", EditorStyles.toolbarButton, GUILayout.Width(55))) CopySelected();
                if (GUILayout.Button("Paste", EditorStyles.toolbarButton, GUILayout.Width(55))) PasteClipboard();
                if (GUILayout.Button("Connect", EditorStyles.toolbarButton, GUILayout.Width(65))) ConnectSelected();
                if (GUILayout.Button("Group", EditorStyles.toolbarButton, GUILayout.Width(55))) GroupSelected();
                if (GUILayout.Button("Comment", EditorStyles.toolbarButton, GUILayout.Width(65))) AddComment();
                if (GUILayout.Button("Bookmark", EditorStyles.toolbarButton, GUILayout.Width(68))) AddBookmark();
                if (GUILayout.Button("Frame", EditorStyles.toolbarButton, GUILayout.Width(52))) FrameSelection();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(68))) RefreshDiagnostics();
                if (GUILayout.Button("Simulate", EditorStyles.toolbarButton, GUILayout.Width(68))) tab = 4;
            }
        }

        private void DrawGraphTab()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                graphSearch = EditorGUILayout.TextField("Find node", graphSearch, GUI.skin.FindStyle("ToolbarSearchTextField"));
                showControlEdges = GUILayout.Toggle(showControlEdges, "Control", EditorStyles.toolbarButton);
                showConditionEdges = GUILayout.Toggle(showConditionEdges, "Conditions", EditorStyles.toolbarButton);
                showParentEdges = GUILayout.Toggle(showParentEdges, "Parent", EditorStyles.toolbarButton);
                showGroups = GUILayout.Toggle(showGroups, "Groups", EditorStyles.toolbarButton);
                showComments = GUILayout.Toggle(showComments, "Comments", EditorStyles.toolbarButton);
                showMinimap = GUILayout.Toggle(showMinimap, "Minimap", EditorStyles.toolbarButton);
            }
            if (!string.IsNullOrEmpty(connectSource)) EditorGUILayout.HelpBox("Connecting from " + connectSource + ". Click an objective input port or press Escape.", MessageType.Info);
            if (!string.IsNullOrEmpty(sourceError)) EditorGUILayout.HelpBox(sourceError + " Use Publish > Raw JSON to recover a structurally valid draft.", MessageType.Error);
            var canvas = GUILayoutUtility.GetRect(300, 480, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawGraphCanvas(canvas);
            EditorGUILayout.LabelField("Mouse: drag nodes · middle/Alt-drag pans · wheel zooms · click output then input connects control flow · right-click opens the palette. Keyboard: Delete, Cmd/Ctrl+C/V, F, arrows.", EditorStyles.miniLabel);
        }

        private void DrawGraphCanvas(Rect viewport)
        {
            if (definition == null) return;
            EnsureLayoutNodes();
            Event current = Event.current;
            Vector2 mouse = current.mousePosition - viewport.position;
            bool inside = viewport.Contains(current.mousePosition);
            GUI.BeginGroup(viewport);
            EditorGUI.DrawRect(new Rect(0, 0, viewport.width, viewport.height), new Color(0.055f, 0.065f, 0.075f));
            DrawGrid(viewport.size);
            Handles.BeginGUI();
            if (showGroups) DrawGroups();
            DrawEdges();
            if (showComments) DrawComments();
            DrawNodes();
            Handles.EndGUI();
            if (showMinimap) DrawMinimap(viewport.size);
            GUI.EndGroup();
            if (inside) HandleCanvasEvent(current, mouse);
        }

        private void DrawGrid(Vector2 size)
        {
            Handles.color = new Color(0.16f, 0.18f, 0.21f, 0.45f);
            float spacing = 40f * graphZoom;
            if (spacing < 12f) spacing = 12f;
            float offsetX = Mathf.Repeat(graphPan.x, spacing);
            float offsetY = Mathf.Repeat(graphPan.y, spacing);
            for (float x = offsetX; x < size.x; x += spacing) Handles.DrawLine(new Vector3(x, 0), new Vector3(x, size.y));
            for (float y = offsetY; y < size.y; y += spacing) Handles.DrawLine(new Vector3(0, y), new Vector3(size.x, y));
        }

        private void DrawNodes()
        {
            foreach (var node in definition.objectives ?? Array.Empty<MissionObjective>())
            {
                if (node == null || IsHiddenByCollapsedGroup(node.id)) continue;
                var rect = NodeRect(node.id);
                bool selected = selectedNodes.Contains(node.id);
                bool matched = string.IsNullOrWhiteSpace(graphSearch) || (node.id + " " + node.title + " " + node.kind + " " + node.eventType).IndexOf(graphSearch, StringComparison.OrdinalIgnoreCase) >= 0;
                var background = selected ? new Color(0.12f, 0.32f, 0.52f, 0.98f) : matched ? new Color(0.12f, 0.14f, 0.17f, 0.98f) : new Color(0.08f, 0.09f, 0.1f, 0.45f);
                EditorGUI.DrawRect(rect, background);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 28f * graphZoom), NodeColor(node.kind));
                GUI.Label(new Rect(rect.x + 8f * graphZoom, rect.y + 4f * graphZoom, rect.width - 16f * graphZoom, 21f * graphZoom), node.title.Length == 0 ? node.id : node.title, EditorStyles.boldLabel);
                if (LayoutFor(node.id)?.collapsed == true) continue;
                float line = 31f * graphZoom;
                GUI.Label(new Rect(rect.x + 8f * graphZoom, rect.y + line, rect.width - 16f * graphZoom, 18f * graphZoom), node.id, EditorStyles.miniLabel);
                line += 18f * graphZoom;
                GUI.Label(new Rect(rect.x + 8f * graphZoom, rect.y + line, rect.width - 16f * graphZoom, 18f * graphZoom), "Type: " + node.kind, EditorStyles.miniLabel);
                line += 18f * graphZoom;
                string eventText = string.IsNullOrEmpty(node.eventType) ? "No event input" : "Event: " + node.eventType;
                GUI.Label(new Rect(rect.x + 8f * graphZoom, rect.y + line, rect.width - 16f * graphZoom, 18f * graphZoom), eventText, EditorStyles.miniLabel);
                line += 18f * graphZoom;
                string progress = node.kind == "sequence" ? "Steps: " + (node.sequence?.Length ?? 0) : "Required: " + node.required.ToString("0.##");
                GUI.Label(new Rect(rect.x + 8f * graphZoom, rect.y + line, rect.width - 16f * graphZoom, 18f * graphZoom), progress, EditorStyles.miniLabel);
                line += 18f * graphZoom;
                GUI.Label(new Rect(rect.x + 8f * graphZoom, rect.y + line, rect.width - 16f * graphZoom, 18f * graphZoom), node.optional ? "Optional failure" : "Required objective", EditorStyles.miniLabel);
                DrawPort(rect, false, MissionGraphPortKind.Control, Color.white);
                DrawPort(rect, true, node.actions != null && node.actions.Length > 0 ? MissionGraphPortKind.Action : MissionGraphPortKind.Event, NodeColor(node.kind));
            }
        }

        private void DrawPort(Rect rect, bool output, MissionGraphPortKind kind, Color color)
        {
            float x = output ? rect.xMax - 7f * graphZoom : rect.x + 1f * graphZoom;
            float y = rect.y + (output ? rect.height * 0.5f : 20f * graphZoom);
            EditorGUI.DrawRect(new Rect(x - 4f * graphZoom, y - 4f * graphZoom, 8f * graphZoom, 8f * graphZoom), color);
        }

        private void DrawEdges()
        {
            foreach (var edge in MissionGraphEditorModel.EnumerateEdges(definition))
            {
                if (edge.kind == MissionGraphPortKind.Control && !showControlEdges || edge.kind == MissionGraphPortKind.Condition && !showConditionEdges || edge.kind == MissionGraphPortKind.Data && !showParentEdges) continue;
                if (!TryGetNodeCenter(edge.sourceId, out var source) || !TryGetNodeCenter(edge.targetId, out var target)) continue;
                var sourcePoint = new Vector2(NodeRect(edge.sourceId).xMax, source.y);
                var targetPoint = new Vector2(NodeRect(edge.targetId).x, target.y);
                var reroute = layoutAsset?.FindReroute(edge.Key);
                var points = new List<Vector2> { sourcePoint };
                if (reroute != null) points.AddRange(reroute.points.Select(GraphToCanvas));
                points.Add(targetPoint);
                var color = edge.Key == selectedEdgeKey ? Color.yellow : edge.kind == MissionGraphPortKind.Control ? new Color(0.25f, 0.75f, 1f) : edge.kind == MissionGraphPortKind.Condition ? new Color(0.95f, 0.55f, 0.25f) : new Color(0.55f, 0.65f, 0.7f);
                Handles.color = color;
                for (int i = 1; i < points.Count; i++)
                {
                    if (edge.kind == MissionGraphPortKind.Condition) Handles.DrawDottedLine(points[i - 1], points[i], 4f);
                    else Handles.DrawBezier(points[i - 1], points[i], points[i - 1] + Vector2.right * 60f * graphZoom, points[i] + Vector2.left * 60f * graphZoom, color, null, 2f * graphZoom);
                }
                if (edge.kind == MissionGraphPortKind.Control) DrawArrow(points[points.Count - 2], targetPoint, color);
            }
        }

        private void DrawArrow(Vector2 start, Vector2 end, Color color)
        {
            Vector2 direction = (end - start).normalized;
            if (direction.sqrMagnitude < 0.01f) return;
            Vector2 side = new Vector2(-direction.y, direction.x);
            Handles.color = color;
            Handles.DrawLine(end, end - direction * 10f * graphZoom + side * 5f * graphZoom, 2f * graphZoom);
            Handles.DrawLine(end, end - direction * 10f * graphZoom - side * 5f * graphZoom, 2f * graphZoom);
        }

        private void DrawGroups()
        {
            foreach (var group in layoutAsset?.Groups ?? new List<MissionGraphGroupLayout>())
            {
                if (group == null) continue;
                var rect = GraphRect(group.rect);
                EditorGUI.DrawRect(rect, group.color);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 23f * graphZoom), new Color(group.color.r, group.color.g, group.color.b, 0.9f));
                GUI.Label(new Rect(rect.x + 7f * graphZoom, rect.y + 3f * graphZoom, rect.width - 42f * graphZoom, 18f * graphZoom), group.title + (group.collapsed ? "  [collapsed]" : ""), EditorStyles.boldLabel);
                GUI.Label(new Rect(rect.xMax - 32f * graphZoom, rect.y + 3f * graphZoom, 24f * graphZoom, 18f * graphZoom), group.nodeIds.Count.ToString(), EditorStyles.miniLabel);
            }
        }

        private void DrawComments()
        {
            foreach (var comment in layoutAsset?.Comments ?? new List<MissionGraphCommentLayout>())
            {
                if (comment == null) continue;
                var rect = GraphRect(comment.rect);
                EditorGUI.DrawRect(rect, comment.color);
                GUI.Label(new Rect(rect.x + 7f * graphZoom, rect.y + 5f * graphZoom, rect.width - 14f * graphZoom, rect.height - 10f * graphZoom), comment.text, EditorStyles.wordWrappedLabel);
            }
        }

        private void DrawMinimap(Vector2 size)
        {
            var rect = new Rect(size.x - 190, 10, 180, 125);
            EditorGUI.DrawRect(rect, new Color(0.02f, 0.025f, 0.03f, 0.9f));
            if (definition.objectives == null || definition.objectives.Length == 0) return;
            var positions = definition.objectives.Where(x => x != null).Select(x => LayoutFor(x.id)?.position ?? Vector2.zero).ToArray();
            var bounds = new Rect(positions[0], Vector2.one);
            foreach (var position in positions) bounds = Encapsulate(bounds, position);
            bounds.xMin -= 100; bounds.xMax += 100; bounds.yMin -= 70; bounds.yMax += 70;
            foreach (var node in definition.objectives)
            {
                if (node == null) continue;
                var p = new Vector2(rect.x + 5 + (LayoutFor(node.id).position.x - bounds.xMin) / Mathf.Max(1, bounds.width) * (rect.width - 10), rect.y + 5 + (LayoutFor(node.id).position.y - bounds.yMin) / Mathf.Max(1, bounds.height) * (rect.height - 10));
                EditorGUI.DrawRect(new Rect(p.x - 2, p.y - 2, 4, 4), selectedNodes.Contains(node.id) ? Color.yellow : NodeColor(node.kind));
            }
            GUI.Label(new Rect(rect.x + 6, rect.y + 4, rect.width - 12, 18), "Overview", EditorStyles.miniBoldLabel);
        }

        private void HandleCanvasEvent(Event current, Vector2 mouse)
        {
            if (current.type == EventType.ScrollWheel)
            {
                float oldZoom = graphZoom;
                graphZoom = Mathf.Clamp(graphZoom * Mathf.Pow(1.1f, -current.delta.y / 3f), 0.25f, 2.5f);
                Vector2 graphPoint = (mouse - graphPan) / oldZoom;
                graphPan = mouse - graphPoint * graphZoom;
                SaveViewState(false);
                current.Use(); Repaint(); return;
            }
            if (current.type == EventType.KeyDown)
            {
                if (current.keyCode == KeyCode.Escape) { connectSource = null; draggingNode = null; panning = false; current.Use(); Repaint(); return; }
                bool command = current.control || current.command;
                if (command && current.keyCode == KeyCode.C)
                { CopySelected(); current.Use(); return; }
                if (command && current.keyCode == KeyCode.V)
                { PasteClipboard(); current.Use(); return; }
                if (current.keyCode == KeyCode.Delete || current.keyCode == KeyCode.Backspace)
                { DeleteSelected(); current.Use(); return; }
                if (current.keyCode == KeyCode.F)
                { FrameSelection(); current.Use(); return; }
                Vector2 nudge = current.shift ? Vector2.one * 10f : Vector2.one * 3f;
                if (current.keyCode == KeyCode.LeftArrow) { NudgeSelection(Vector2.left * nudge.x); current.Use(); return; }
                if (current.keyCode == KeyCode.RightArrow) { NudgeSelection(Vector2.right * nudge.x); current.Use(); return; }
                if (current.keyCode == KeyCode.UpArrow) { NudgeSelection(Vector2.up * nudge.y); current.Use(); return; }
                if (current.keyCode == KeyCode.DownArrow) { NudgeSelection(Vector2.down * nudge.y); current.Use(); return; }
            }
            if (current.type == EventType.MouseDown)
            {
                if (current.button == 1)
                { ShowNodeMenu(NodeAt(mouse)); current.Use(); return; }
                if (current.button == 2 || (current.button == 0 && current.alt))
                { panning = true; lastCanvasMouse = mouse; current.Use(); return; }
                if (current.button == 0)
                {
                    string node = NodeAt(mouse);
                    bool output = IsOutputPortHit(node, mouse);
                    bool input = IsInputPortHit(node, mouse);
                    if (output)
                    { connectSource = node; status = "Choose a target objective input port."; current.Use(); return; }
                    if (input && !string.IsNullOrEmpty(connectSource))
                    { Connect(connectSource, node); connectSource = null; current.Use(); return; }
                    if (!string.IsNullOrEmpty(node))
                    {
                        if (!current.shift) selectedNodes.Clear();
                        if (!selectedNodes.Add(node) && current.shift) selectedNodes.Remove(node);
                        selectedEdgeKey = null; PrepareObjectiveBuffer(node);
                        if (current.clickCount == 2) tab = 1;
                        draggingNode = node; draggingNodes = false; dragStartGraph = CanvasToGraph(mouse); dragStartPositions.Clear();
                        foreach (var id in selectedNodes) dragStartPositions[id] = LayoutFor(id).position;
                        current.Use(); Repaint(); return;
                    }
                    var edge = EdgeAt(mouse);
                    if (edge != null) { selectedEdgeKey = edge.Key; current.Use(); Repaint(); return; }
                    selectedNodes.Clear(); selectedEdgeKey = null; objectiveBuffer = null;
                    current.Use(); Repaint(); return;
                }
            }
            if (current.type == EventType.MouseDrag)
            {
                if (panning)
                { graphPan += mouse - lastCanvasMouse; lastCanvasMouse = mouse; SaveViewState(false); current.Use(); Repaint(); return; }
                if (!string.IsNullOrEmpty(draggingNode) && current.button == 0)
                {
                    EnsurePersistentLayout();
                    Vector2 delta = CanvasToGraph(mouse) - dragStartGraph;
                    if (!draggingNodes) { Undo.RecordObject(layoutAsset, "Move mission graph nodes"); draggingNodes = true; }
                    foreach (var pair in dragStartPositions) LayoutFor(pair.Key).position = pair.Value + delta;
                    EditorUtility.SetDirty(layoutAsset); current.Use(); Repaint(); return;
                }
            }
            if (current.type == EventType.MouseUp)
            {
                if (panning) panning = false;
                if (!string.IsNullOrEmpty(draggingNode)) { draggingNode = null; draggingNodes = false; dragStartPositions.Clear(); }
                current.Use(); return;
            }
        }

        private string NodeAt(Vector2 canvasPoint)
        {
            for (int i = definition.objectives.Length - 1; i >= 0; i--)
            {
                var node = definition.objectives[i];
                if (node != null && !IsHiddenByCollapsedGroup(node.id) && NodeRect(node.id).Contains(canvasPoint)) return node.id;
            }
            return string.Empty;
        }

        private MissionGraphSemanticEdge EdgeAt(Vector2 canvasPoint)
        {
            MissionGraphSemanticEdge best = null; float distance = 9f;
            foreach (var edge in MissionGraphEditorModel.EnumerateEdges(definition))
            {
                if (edge.kind == MissionGraphPortKind.Control && !showControlEdges || edge.kind == MissionGraphPortKind.Condition && !showConditionEdges || edge.kind == MissionGraphPortKind.Data && !showParentEdges) continue;
                if (!TryGetNodeCenter(edge.sourceId, out var source) || !TryGetNodeCenter(edge.targetId, out var target)) continue;
                var points = new List<Vector2> { new Vector2(NodeRect(edge.sourceId).xMax, source.y) };
                var reroute = layoutAsset?.FindReroute(edge.Key);
                if (reroute != null) points.AddRange(reroute.points.Select(GraphToCanvas));
                points.Add(new Vector2(NodeRect(edge.targetId).x, target.y));
                for (int i = 1; i < points.Count; i++)
                {
                    float d = DistanceToSegment(canvasPoint, points[i - 1], points[i]);
                    if (d < distance) { distance = d; best = edge; }
                }
            }
            return best;
        }

        private bool IsOutputPortHit(string node, Vector2 point)
        {
            if (string.IsNullOrEmpty(node)) return false;
            var rect = NodeRect(node);
            return new Rect(rect.xMax - 18f * graphZoom, rect.y + rect.height * .5f - 14f * graphZoom, 24f * graphZoom, 28f * graphZoom).Contains(point);
        }

        private bool IsInputPortHit(string node, Vector2 point)
        {
            if (string.IsNullOrEmpty(node)) return false;
            var rect = NodeRect(node);
            return new Rect(rect.x - 8f * graphZoom, rect.y + 8f * graphZoom, 24f * graphZoom, 26f * graphZoom).Contains(point);
        }

        private Rect NodeRect(string id)
        {
            var layout = LayoutFor(id);
            float height = layout != null && layout.collapsed ? CollapsedNodeHeight : NodeHeight;
            return new Rect(GraphToCanvas(layout.position), new Vector2(NodeWidth, height) * graphZoom);
        }

        private Rect GraphRect(Rect rect)
        {
            return new Rect(GraphToCanvas(rect.position), rect.size * graphZoom);
        }

        private Vector2 GraphToCanvas(Vector2 point) => graphPan + point * graphZoom;
        private Vector2 CanvasToGraph(Vector2 point) => (point - graphPan) / graphZoom;

        private MissionGraphNodeLayout LayoutFor(string id)
        {
            if (layoutAsset == null) return null;
            var existing = layoutAsset.FindNode(id);
            if (existing != null) return existing;
            int index = Array.FindIndex(definition.objectives, x => x != null && x.id == id);
            int column = Math.Max(0, index) % 4;
            int row = Math.Max(0, index) / 4;
            return layoutAsset.EnsureNode(id, new Vector2(60 + column * 290, 60 + row * 190));
        }

        private void EnsureLayoutNodes()
        {
            if (layoutAsset == null || definition?.objectives == null) return;
            for (int i = 0; i < definition.objectives.Length; i++)
            {
                var node = definition.objectives[i];
                if (node != null) LayoutFor(node.id);
            }
            layoutAsset.Normalize();
        }

        private bool IsHiddenByCollapsedGroup(string id)
        {
            return showGroups && (layoutAsset?.Groups ?? new List<MissionGraphGroupLayout>()).Any(x => x != null && x.collapsed && x.nodeIds.Contains(id));
        }

        private bool TryGetNodeCenter(string id, out Vector2 center)
        {
            center = default;
            if (string.IsNullOrEmpty(id) || MissionGraphEditorModel.FindNode(definition, id) == null) return false;
            var rect = NodeRect(id); center = new Vector2(rect.center.x, rect.y + 28f * graphZoom); return true;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 delta = b - a;
            float t = delta.sqrMagnitude < .0001f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, delta) / delta.sqrMagnitude);
            return Vector2.Distance(p, a + delta * t);
        }

        private static Rect Encapsulate(Rect rect, Vector2 point)
        {
            rect.xMin = Mathf.Min(rect.xMin, point.x); rect.xMax = Mathf.Max(rect.xMax, point.x);
            rect.yMin = Mathf.Min(rect.yMin, point.y); rect.yMax = Mathf.Max(rect.yMax, point.y);
            return rect;
        }

        private static Color NodeColor(string kind)
        {
            switch (kind)
            {
                case "sequence": return new Color(0.23f, 0.58f, 0.78f);
                case "condition": return new Color(0.64f, 0.38f, 0.75f);
                case "hold": return new Color(0.85f, 0.47f, 0.24f);
                case "timer":
                case "deadline": return new Color(0.76f, 0.28f, 0.25f);
                default: return new Color(0.22f, 0.55f, 0.36f);
            }
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            int index = text.IndexOf('\n'); return index < 0 ? text : text.Substring(0, index);
        }

        private void RefreshCatalog()
        {
            catalog.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:MissionDefinitionAsset"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<MissionDefinitionAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) catalog.Add(asset);
            }
        }

        private void SetDefinition(MissionDefinitionAsset asset)
        {
            if (asset == definitionAsset) { ReloadFromAsset(true); return; }
            DisposeSimulation();
            if (layoutAsset != null && !AssetDatabase.Contains(layoutAsset)) DestroyImmediate(layoutAsset);
            definitionAsset = asset;
            selectedNodes.Clear(); selectedEdgeKey = null; connectSource = null;
            objectiveBuffer = null; missionBuffer = null;
            layoutAsset = FindLayout(asset);
            if (layoutAsset == null)
            {
                layoutAsset = CreateInstance<MissionGraphLayoutAsset>();
                layoutAsset.hideFlags = HideFlags.HideAndDontSave;
            }
            ReloadFromAsset(false);
            Selection.activeObject = asset;
            Repaint();
        }

        private void ReloadFromAsset(bool preserveSelection)
        {
            if (definitionAsset == null) return;
            var preserved = preserveSelection ? new HashSet<string>(selectedNodes, StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
            sourceError = string.Empty;
            try
            {
                var graph = definitionAsset.Compile();
                definition = graph.Definition();
                rawJson = definitionAsset.Json;
                rawJsonAssetPath = AssetDatabase.GetAssetPath(definitionAsset);
                layoutAsset.DefinitionGuid = AssetDatabase.AssetPathToGUID(rawJsonAssetPath);
                layoutAsset.DefinitionId = graph.Id;
                layoutAsset.SourceContentHash = graph.ContentHash;
                graphPan = layoutAsset.Pan;
                graphZoom = layoutAsset.Zoom;
                EnsureLayoutNodes();
                diagnostics.Clear(); diagnostics.AddRange(MissionGraphEditorModel.Validate(definition, layoutAsset));
                status = "Loaded " + graph.Id + " · content hash " + graph.ContentHash.Substring(0, 12) + "…";
            }
            catch (Exception exception)
            {
                sourceError = exception.Message;
                try { definition = JsonUtility.FromJson<MissionDefinition>(definitionAsset.Json); }
                catch { definition = null; }
                rawJson = definitionAsset.Json;
                rawJsonAssetPath = AssetDatabase.GetAssetPath(definitionAsset);
                diagnostics.Clear(); diagnostics.AddRange(MissionGraphEditorModel.Validate(definition, layoutAsset));
                status = "Mission is a draft or invalid; resolve the diagnostics before runtime use.";
            }
            selectedNodes.Clear();
            foreach (var id in preserved) if (MissionGraphEditorModel.FindNode(definition, id) != null) selectedNodes.Add(id);
            if (selectedNodes.Count == 0) { objectiveBuffer = null; objectiveBufferId = string.Empty; }
            missionBuffer = null; missionBufferHash = string.Empty;
        }

        private MissionGraphLayoutAsset FindLayout(MissionDefinitionAsset asset)
        {
            if (asset == null) return null;
            string sourcePath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(sourcePath)) return null;
            string guid = AssetDatabase.AssetPathToGUID(sourcePath);
            foreach (string layoutGuid in AssetDatabase.FindAssets("t:MissionGraphLayoutAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(layoutGuid);
                var candidate = AssetDatabase.LoadAssetAtPath<MissionGraphLayoutAsset>(path);
                if (candidate != null && candidate.DefinitionGuid == guid) return candidate;
            }
            return null;
        }

        private void EnsurePersistentLayout()
        {
            if (definitionAsset == null || layoutAsset == null) return;
            if (AssetDatabase.Contains(layoutAsset)) { status = "Layout sidecar is already persistent."; return; }
            string sourcePath = AssetDatabase.GetAssetPath(definitionAsset);
            if (string.IsNullOrEmpty(sourcePath)) { status = "Save the mission asset before creating a layout sidecar."; return; }
            string folder = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/') ?? "Assets";
            string name = Path.GetFileNameWithoutExtension(sourcePath) + ".layout.asset";
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name);
            var transient = layoutAsset;
            layoutAsset = CreateInstance<MissionGraphLayoutAsset>();
            layoutAsset.hideFlags = HideFlags.None;
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(transient), layoutAsset);
            layoutAsset.DefinitionGuid = AssetDatabase.AssetPathToGUID(sourcePath);
            layoutAsset.DefinitionId = definition?.id ?? string.Empty;
            AssetDatabase.CreateAsset(layoutAsset, path);
            Undo.RegisterCreatedObjectUndo(layoutAsset, "Create mission graph layout");
            if (transient != null) DestroyImmediate(transient);
            AssetDatabase.SaveAssets();
            status = "Created layout sidecar: " + path;
            Repaint();
        }

        private void SaveViewState(bool persistIfAvailable)
        {
            if (layoutAsset == null) return;
            layoutAsset.Pan = graphPan; layoutAsset.Zoom = graphZoom;
            if (persistIfAvailable || AssetDatabase.Contains(layoutAsset)) EditorUtility.SetDirty(layoutAsset);
        }

        private void CommitDefinition(string label, MissionDefinition candidate)
        {
            if (definitionAsset == null || candidate == null) return;
            try
            {
                var graph = new MissionGraph(candidate);
                Undo.RecordObject(definitionAsset, label);
                definitionAsset.Configure(candidate);
                EditorUtility.SetDirty(definitionAsset);
                definition = graph.Definition();
                rawJson = definitionAsset.Json;
                if (layoutAsset != null)
                {
                    layoutAsset.DefinitionId = graph.Id;
                    layoutAsset.SourceContentHash = graph.ContentHash;
                    SaveViewState(false);
                }
                diagnostics.Clear(); diagnostics.AddRange(MissionGraphEditorModel.Validate(definition, layoutAsset));
                sourceError = string.Empty;
                status = label + " · content hash " + graph.ContentHash.Substring(0, 12) + "…";
                objectiveBuffer = null; missionBuffer = null;
                AssetDatabase.SaveAssets();
            }
            catch (Exception exception)
            {
                status = label + " rejected: " + exception.Message;
                diagnostics.Clear(); diagnostics.AddRange(MissionGraphEditorModel.Validate(candidate, layoutAsset));
            }
            Repaint();
        }

        private void CommitRawJson()
        {
            if (definitionAsset == null || string.IsNullOrWhiteSpace(rawJson)) return;
            try
            {
                JsonUtility.FromJson<MissionDefinition>(rawJson);
                Undo.RecordObject(definitionAsset, "Apply mission JSON draft");
                definitionAsset.SetAuthoringJson(rawJson);
                EditorUtility.SetDirty(definitionAsset);
                ReloadFromAsset(true);
                AssetDatabase.SaveAssets();
                status = string.IsNullOrEmpty(sourceError) ? "JSON draft applied and compiled." : "JSON draft applied; compile diagnostics remain.";
            }
            catch (Exception exception) { status = "JSON draft rejected: " + exception.Message; }
        }

        private void CreateMissionAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create mission definition", "Mission", "asset", "Choose an asset path.");
            if (string.IsNullOrEmpty(path)) return;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) { status = "An asset already exists at that path."; return; }
            var asset = CreateInstance<MissionDefinitionAsset>();
            var node = MakeNode("event", "objective.start");
            var draft = new MissionDefinition { id = "mission.new", title = "New mission", version = 1,
                objectives = new[] { node }, success = MissionCondition.Done(node.id), failure = null,
                checkpoints = Array.Empty<MissionCheckpoint>(), rewards = Array.Empty<MissionReward>() };
            try { asset.Configure(draft); AssetDatabase.CreateAsset(asset, path); AssetDatabase.SaveAssets(); RefreshCatalog(); SetDefinition(asset); status = "Created " + path; }
            catch (Exception exception) { DestroyImmediate(asset); status = "Could not create mission: " + exception.Message; }
        }

        private void CreateDemo()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create mission graph demonstration", "MissionObjectiveGraphDemo", "asset", "Creates a reusable branch/pursuit/checkpoint example.");
            if (string.IsNullOrEmpty(path)) return;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) { status = "An asset already exists at that path."; return; }
            var asset = CreateInstance<MissionDefinitionAsset>();
            try
            {
                var district = MakeNode("event", "district.reached");
                district.title = "Reach the district"; district.eventType = "area.entered"; district.target = "district.industrial";
                var routeA = MakeNode("event", "route.a"); routeA.title = "Take the tunnel route"; routeA.eventType = "route.completed"; routeA.target = "route.tunnel"; routeA.dependencies = new[] { district.id }; routeA.branchGroup = "route"; routeA.branchChoice = "tunnel";
                routeA.activate = MakeFactEquals("route.choice", 1);
                var routeB = MakeNode("event", "route.b"); routeB.title = "Take the bridge route"; routeB.eventType = "route.completed"; routeB.target = "route.bridge"; routeB.dependencies = new[] { district.id }; routeB.branchGroup = "route"; routeB.branchChoice = "bridge";
                routeB.activate = MakeFactEquals("route.choice", 2);
                var survive = MakeNode("hold", "pursuit.survive"); survive.title = "Survive the pursuit"; survive.dependencies = Array.Empty<string>(); survive.activate = MissionCondition.Any(MissionCondition.Done(routeA.id), MissionCondition.Done(routeB.id)); survive.success = MissionCondition.Fact("pursuit.active"); survive.failure = MissionCondition.Fact("pursuit.busted"); survive.duration = 20; survive.resetWhenFalse = true;
                survive.actions = new[] { new MissionAction { id = "pursuit.spawn", kind = "spawn", binding = "pursuit.units", value = "preview" } };
                var escape = MakeNode("event", "pursuit.escaped"); escape.title = "Escape the cops"; escape.eventType = "pursuit.escaped"; escape.dependencies = new[] { survive.id };
                var garage = MakeNode("event", "garage.return"); garage.title = "Return to the garage"; garage.eventType = "area.entered"; garage.target = "garage.safehouse"; garage.dependencies = new[] { escape.id };
                var damage = MakeNode("hold", "bonus.clean"); damage.title = "Optional: keep the car clean"; damage.eventType = string.Empty; damage.optional = true; damage.dependencies = new[] { district.id }; damage.success = MissionCondition.Not(MissionCondition.Fact("vehicle.damage")); damage.failure = MissionCondition.Fact("vehicle.damage"); damage.duration = 30;
                var definition = new MissionDefinition { id = "mission.demo.branching-pursuit", title = "Industrial Run — Branching Pursuit", version = 1,
                    objectives = new[] { district, routeA, routeB, survive, escape, garage, damage },
                    checkpoints = new[] { new MissionCheckpoint { id = "checkpoint.after-route", when = MissionCondition.Any(MissionCondition.Done(routeA.id), MissionCondition.Done(routeB.id)) } },
                    success = MissionCondition.Done(garage.id), failure = MissionCondition.Fact("pursuit.busted"), failureReason = "Busted",
                    rewards = new[] { new MissionReward { id = "completion", cash = 1500 }, new MissionReward { id = "clean-bonus", cash = 500, when = MissionCondition.Done(damage.id) } } };
                asset.Configure(definition);
                AssetDatabase.CreateAsset(asset, path); AssetDatabase.SaveAssets(); RefreshCatalog(); SetDefinition(asset);
                EnsurePersistentLayout();
                ArrangeDemo();
                status = "Created demo mission with branch, optional goal, pursuit, recovery checkpoint and settlement references.";
            }
            catch (Exception exception) { DestroyImmediate(asset); status = "Could not create demo: " + exception.Message; }
        }

        private static MissionObjective MakeNode(string kind, string id)
        {
            var node = new MissionObjective { id = id, title = id, kind = kind, clock = MissionClock.Mission,
                dependencies = Array.Empty<string>(), sequence = Array.Empty<string>(), warnings = Array.Empty<double>(),
                activate = MissionCondition.All(), required = 1, duration = 0, actions = Array.Empty<MissionAction>() };
            switch (kind)
            {
                case "event": node.eventType = "event." + id; break;
                case "sequence": node.eventType = "checkpoint.reached"; node.sequence = new[] { "checkpoint." + id }; break;
                case "condition": node.success = MissionCondition.Fact("condition." + id); break;
                case "hold": node.success = MissionCondition.Fact("condition." + id); node.duration = 5; node.resetWhenFalse = true; break;
                case "timer": node.duration = 5; break;
                case "deadline": node.duration = 30; break;
            }
            return node;
        }

        private void ArrangeDemo()
        {
            if (layoutAsset == null || definition?.objectives == null) return;
            EnsurePersistentLayout();
            Undo.RecordObject(layoutAsset, "Arrange mission demo graph");
            var positions = new Dictionary<string, Vector2>(StringComparer.Ordinal)
            {
                ["district.reached"] = new Vector2(60, 210), ["route.a"] = new Vector2(360, 90), ["route.b"] = new Vector2(360, 330),
                ["pursuit.survive"] = new Vector2(680, 210), ["pursuit.escaped"] = new Vector2(980, 210), ["garage.return"] = new Vector2(1280, 210), ["bonus.clean"] = new Vector2(360, 560)
            };
            foreach (var node in definition.objectives) if (node != null) LayoutFor(node.id).position = positions.TryGetValue(node.id, out var position) ? position : LayoutFor(node.id).position;
            GroupSelected();
            EditorUtility.SetDirty(layoutAsset);
            Repaint();
        }

        private void ShowNodeMenu(string nodeId)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Add/Event objective"), false, () => AddNode("event"));
            menu.AddItem(new GUIContent("Add/Ordered checkpoint sequence"), false, () => AddNode("sequence"));
            menu.AddItem(new GUIContent("Add/Condition gate"), false, () => AddNode("condition"));
            menu.AddItem(new GUIContent("Add/Hold condition"), false, () => AddNode("hold"));
            menu.AddItem(new GUIContent("Add/Timer success"), false, () => AddNode("timer"));
            menu.AddItem(new GUIContent("Add/Deadline failure"), false, () => AddNode("deadline"));
            menu.AddSeparator("");
            if (!string.IsNullOrEmpty(nodeId))
            {
                menu.AddItem(new GUIContent("Inspect node"), false, () => { selectedNodes.Clear(); selectedNodes.Add(nodeId); PrepareObjectiveBuffer(nodeId); tab = 1; Repaint(); });
                menu.AddItem(new GUIContent("Toggle collapse"), LayoutFor(nodeId)?.collapsed == true, () => ToggleNodeCollapse(nodeId));
                menu.AddItem(new GUIContent("Duplicate node"), false, () => { selectedNodes.Clear(); selectedNodes.Add(nodeId); CopySelected(); PasteClipboard(); });
                menu.AddItem(new GUIContent("Delete node"), false, DeleteSelected);
            }
            else
            {
                menu.AddItem(new GUIContent("Add/group selected"), selectedNodes.Count > 0, GroupSelected);
                menu.AddItem(new GUIContent("Add/comment"), false, AddComment);
                menu.AddItem(new GUIContent("Add/bookmark"), false, AddBookmark);
            }
            menu.ShowAsContext();
        }

        private void AddNode(string kind)
        {
            if (definitionAsset == null || definition == null) return;
            var candidate = MissionGraphEditorModel.CloneDefinition(definition);
            var existing = new HashSet<string>((candidate.objectives ?? Array.Empty<MissionObjective>()).Where(x => x != null).Select(x => x.id), StringComparer.Ordinal);
            string id = MissionGraphEditorModel.NewId("objective." + kind, existing);
            var node = MakeNode(kind, id);
            if (selectedNodes.Count == 1)
            {
                string selected = selectedNodes.First();
                node.dependencies = new[] { selected };
                node.activate = MissionCondition.All();
            }
            candidate.objectives = (candidate.objectives ?? Array.Empty<MissionObjective>()).Concat(new[] { node }).ToArray();
            if (candidate.success == null) candidate.success = MissionCondition.Done(id);
            CommitDefinition("Add " + kind + " objective", candidate);
            EnsurePersistentLayout();
            Vector2 position = selectedNodes.Count == 1 ? LayoutFor(selectedNodes.First()).position + new Vector2(290, 0) : CanvasToGraph(new Vector2(320, 230));
            Undo.RecordObject(layoutAsset, "Place mission graph node");
            LayoutFor(id).position = position;
            EditorUtility.SetDirty(layoutAsset);
            selectedNodes.Clear(); selectedNodes.Add(id); PrepareObjectiveBuffer(id); tab = 1;
            Repaint();
        }

        private void ToggleNodeCollapse(string id)
        {
            EnsurePersistentLayout();
            Undo.RecordObject(layoutAsset, "Collapse mission graph node");
            LayoutFor(id).collapsed = !LayoutFor(id).collapsed;
            EditorUtility.SetDirty(layoutAsset); Repaint();
        }

        private void ConnectSelected()
        {
            if (selectedNodes.Count != 2)
            {
                status = "Select exactly two objectives, or click an output port followed by an input port.";
                return;
            }
            var ids = selectedNodes.ToArray();
            Connect(ids[0], ids[1]);
        }

        private void Connect(string sourceId, string targetId)
        {
            if (string.IsNullOrEmpty(sourceId) || string.IsNullOrEmpty(targetId)) return;
            var candidate = MissionGraphEditorModel.CloneDefinition(definition);
            if (!MissionGraphEditorModel.TryAddControlLink(candidate, sourceId, targetId, out var failure))
            {
                status = failure; Repaint(); return;
            }
            CommitDefinition("Connect control-flow objectives", candidate);
            selectedNodes.Clear(); selectedNodes.Add(targetId);
            status = "Connected " + sourceId + " → " + targetId + ". Condition/data ports are read-only references; edit them in Inspector.";
        }

        private void CopySelected()
        {
            if (definition == null || selectedNodes.Count == 0) { status = "Select at least one objective to copy."; return; }
            try
            {
                clipboardPayload = MissionGraphEditorModel.Clipboard.Copy(definition, layoutAsset, selectedNodes);
                EditorGUIUtility.systemCopyBuffer = clipboardPayload;
                status = "Copied " + selectedNodes.Count + " objective node(s) with internal dependency remapping on paste.";
            }
            catch (Exception exception) { status = "Copy failed: " + exception.Message; }
        }

        private void PasteClipboard()
        {
            string payload = string.IsNullOrEmpty(clipboardPayload) ? EditorGUIUtility.systemCopyBuffer : clipboardPayload;
            if (string.IsNullOrEmpty(payload)) { status = "Clipboard is empty."; return; }
            var candidate = MissionGraphEditorModel.CloneDefinition(definition);
            var tempLayout = CloneLayout(layoutAsset);
            if (!MissionGraphEditorModel.Clipboard.TryPaste(candidate, tempLayout, payload, new Vector2(50, 50), out var pasted, out var failure))
            { status = failure; DestroyImmediate(tempLayout); Repaint(); return; }
            CommitDefinition("Paste mission graph nodes", candidate);
            EnsurePersistentLayout();
            Undo.RecordObject(layoutAsset, "Paste mission graph layout");
            CopyLayoutData(tempLayout, layoutAsset);
            EditorUtility.SetDirty(layoutAsset); DestroyImmediate(tempLayout);
            selectedNodes.Clear(); foreach (var id in pasted) selectedNodes.Add(id);
            PrepareObjectiveBuffer(selectedNodes.FirstOrDefault());
            status = "Pasted " + pasted.Length + " node(s) with fresh IDs.";
            Repaint();
        }

        private static MissionGraphLayoutAsset CloneLayout(MissionGraphLayoutAsset source)
        {
            var clone = CreateInstance<MissionGraphLayoutAsset>();
            clone.hideFlags = HideFlags.HideAndDontSave;
            if (source != null) EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), clone);
            return clone;
        }

        private static void CopyLayoutData(MissionGraphLayoutAsset source, MissionGraphLayoutAsset destination)
        {
            if (source == null || destination == null) return;
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), destination);
        }

        private void DeleteSelected()
        {
            if (definition == null || selectedNodes.Count == 0) return;
            if (definition.objectives.Length <= selectedNodes.Count)
            { status = "A mission must retain at least one objective; delete or repurpose the final node instead."; return; }
            var selected = new HashSet<string>(selectedNodes, StringComparer.Ordinal);
            var conditionEdges = MissionGraphEditorModel.EnumerateEdges(definition).Where(x => x.kind == MissionGraphPortKind.Condition && selected.Contains(x.sourceId)).ToArray();
            bool missionPolicyReference = ReferencesAny(definition.success, selected) || ReferencesAny(definition.failure, selected);
            if (conditionEdges.Length > 0 || missionPolicyReference)
            {
                status = "Deletion blocked: a condition or mission outcome still references the selected node(s). Remove those semantic references in Inspector first.";
                tab = 2; Repaint(); return;
            }
            if (!EditorUtility.DisplayDialog("Delete mission objectives", "Delete " + selected.Count + " objective node(s)? Incoming control dependencies will be removed; other references are intentionally not guessed.", "Delete", "Cancel")) return;
            var candidate = MissionGraphEditorModel.CloneDefinition(definition);
            candidate.objectives = candidate.objectives.Where(x => x == null || !selected.Contains(x.id)).ToArray();
            foreach (var node in candidate.objectives)
            {
                if (node == null) continue;
                node.dependencies = (node.dependencies ?? Array.Empty<string>()).Where(x => !selected.Contains(x)).ToArray();
                if (selected.Contains(node.parent)) node.parent = string.Empty;
            }
            CommitDefinition("Delete mission objectives", candidate);
            EnsurePersistentLayout();
            Undo.RecordObject(layoutAsset, "Delete mission graph layout");
            foreach (var id in selected) layoutAsset.RemoveNode(id);
            EditorUtility.SetDirty(layoutAsset); selectedNodes.Clear(); selectedEdgeKey = null;
            status = "Deleted selected objective(s); validation remains the source of truth for any unresolved external references.";
        }

        private static bool ReferencesAny(MissionCondition condition, ISet<string> ids)
        {
            if (condition == null) return false;
            if ((condition.kind == MissionConditionKind.ObjectiveIs || condition.kind == MissionConditionKind.ProgressAtLeast || condition.kind == MissionConditionKind.RemainingAtMost) && ids.Contains(condition.key)) return true;
            return (condition.children ?? Array.Empty<MissionCondition>()).Any(x => ReferencesAny(x, ids));
        }

        private void NudgeSelection(Vector2 delta)
        {
            if (selectedNodes.Count == 0) return;
            EnsurePersistentLayout();
            Undo.RecordObject(layoutAsset, "Nudge mission graph nodes");
            foreach (var id in selectedNodes) LayoutFor(id).position += delta;
            EditorUtility.SetDirty(layoutAsset); Repaint();
        }

        private void GroupSelected()
        {
            if (selectedNodes.Count == 0) { status = "Select nodes before creating a group."; return; }
            EnsurePersistentLayout();
            if (layoutAsset.Groups.Count >= MissionGraphEditorModel.MaximumEditorGroups) { status = "Editor group budget reached."; return; }
            var positions = selectedNodes.Select(x => LayoutFor(x).position).ToArray();
            var min = new Vector2(positions.Min(x => x.x), positions.Min(x => x.y));
            var max = new Vector2(positions.Max(x => x.x + NodeWidth), positions.Max(x => x.y + NodeHeight));
            var group = new MissionGraphGroupLayout { id = MissionGraphEditorModel.NewId("group", new HashSet<string>(layoutAsset.Groups.Where(x => x != null).Select(x => x.id), StringComparer.Ordinal)), title = "Stage group", rect = Rect.MinMaxRect(min.x - 30, min.y - 48, max.x + 30, max.y + 30), nodeIds = selectedNodes.ToList() };
            Undo.RecordObject(layoutAsset, "Group mission objectives"); layoutAsset.Groups.Add(group); EditorUtility.SetDirty(layoutAsset); status = "Created editor-only group; graph semantics are unchanged."; Repaint();
        }

        private void AddComment()
        {
            EnsurePersistentLayout();
            if (layoutAsset.Comments.Count >= MissionGraphEditorModel.MaximumEditorComments) { status = "Editor comment budget reached."; return; }
            Undo.RecordObject(layoutAsset, "Add mission graph comment");
            layoutAsset.Comments.Add(new MissionGraphCommentLayout { id = MissionGraphEditorModel.NewId("comment", new HashSet<string>(layoutAsset.Comments.Where(x => x != null).Select(x => x.id), StringComparer.Ordinal)), rect = new Rect(CanvasToGraph(new Vector2(220, 180)), new Vector2(300, 100)), text = "Describe the player intent, failure precedence, or streaming assumption here." });
            EditorUtility.SetDirty(layoutAsset); status = "Added editor-only comment."; Repaint();
        }

        private void AddBookmark()
        {
            EnsurePersistentLayout();
            if (layoutAsset.Bookmarks.Count >= MissionGraphEditorModel.MaximumEditorBookmarks) { status = "Editor bookmark budget reached."; return; }
            Undo.RecordObject(layoutAsset, "Add mission graph bookmark");
            layoutAsset.Bookmarks.Add(new MissionGraphBookmarkLayout { id = MissionGraphEditorModel.NewId("bookmark", new HashSet<string>(layoutAsset.Bookmarks.Where(x => x != null).Select(x => x.id), StringComparer.Ordinal)), title = "Bookmark " + (layoutAsset.Bookmarks.Count + 1), pan = graphPan, zoom = graphZoom, nodeIds = selectedNodes.ToList() });
            EditorUtility.SetDirty(layoutAsset); status = "Added bookmark for the current graph view."; Repaint();
        }

        private void FrameSelection()
        {
            var ids = selectedNodes.Count > 0 ? selectedNodes : new HashSet<string>((definition?.objectives ?? Array.Empty<MissionObjective>()).Where(x => x != null).Select(x => x.id), StringComparer.Ordinal);
            if (ids.Count == 0) return;
            var positions = ids.Select(x => LayoutFor(x).position).ToArray();
            var min = new Vector2(positions.Min(x => x.x), positions.Min(x => x.y));
            var max = new Vector2(positions.Max(x => x.x + NodeWidth), positions.Max(x => x.y + NodeHeight));
            var center = (min + max) * .5f;
            graphPan = new Vector2(420, 270) - center * graphZoom;
            SaveViewState(false); Repaint();
        }

        private void DrawBookmarks()
        {
            if (layoutAsset == null || layoutAsset.Bookmarks.Count == 0) return;
            EditorGUILayout.LabelField("Bookmarks", EditorStyles.boldLabel);
            foreach (var bookmark in layoutAsset.Bookmarks.ToArray())
            {
                if (bookmark == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(bookmark.title, EditorStyles.miniButton)) { graphPan = bookmark.pan; graphZoom = bookmark.zoom; selectedNodes.Clear(); foreach (var id in bookmark.nodeIds) if (MissionGraphEditorModel.FindNode(definition, id) != null) selectedNodes.Add(id); Repaint(); }
                    if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(24))) { Undo.RecordObject(layoutAsset, "Remove graph bookmark"); layoutAsset.Bookmarks.Remove(bookmark); EditorUtility.SetDirty(layoutAsset); }
                }
            }
        }

        private void AddRerouteToSelectedEdge()
        {
            var edge = MissionGraphEditorModel.EnumerateEdges(definition).FirstOrDefault(x => x.Key == selectedEdgeKey);
            if (edge == null) { status = "Select a control or condition edge first."; return; }
            EnsurePersistentLayout();
            Undo.RecordObject(layoutAsset, "Add mission graph reroute");
            var reroute = layoutAsset.FindReroute(edge.Key);
            if (reroute == null) { reroute = new MissionGraphRerouteLayout { edgeKey = edge.Key }; layoutAsset.Reroutes.Add(reroute); }
            if (!TryGetNodeCenter(edge.sourceId, out var source) || !TryGetNodeCenter(edge.targetId, out var target)) return;
            reroute.points.Add(CanvasToGraph((source + target) * .5f));
            EditorUtility.SetDirty(layoutAsset); status = "Added editor-only reroute point."; Repaint();
        }

        private void RemoveSelectedEdge()
        {
            var edge = MissionGraphEditorModel.EnumerateEdges(definition).FirstOrDefault(x => x.Key == selectedEdgeKey);
            if (edge == null) return;
            if (edge.kind != MissionGraphPortKind.Control) { status = "Condition/parent edges are derived from Inspector values and cannot be removed as visual-only links."; return; }
            var candidate = MissionGraphEditorModel.CloneDefinition(definition);
            if (MissionGraphEditorModel.TryRemoveControlLink(candidate, edge.sourceId, edge.targetId)) CommitDefinition("Remove control-flow connection", candidate);
            EnsurePersistentLayout(); Undo.RecordObject(layoutAsset, "Remove mission graph reroute"); layoutAsset.RemoveReroute(edge.Key); EditorUtility.SetDirty(layoutAsset); selectedEdgeKey = null;
        }

        private void DrawInspectorTab()
        {
            if (definition == null) return;
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            if (selectedNodes.Count == 1)
            {
                string id = selectedNodes.First();
                PrepareObjectiveBuffer(id);
                if (objectiveBuffer != null) DrawObjectiveInspector(objectiveBuffer);
                else EditorGUILayout.HelpBox("The selected objective no longer exists.", MessageType.Error);
            }
            else
            {
                if (selectedNodes.Count > 1) EditorGUILayout.HelpBox("Select one objective to edit its typed ports. With no selection, this panel edits mission-level success, failure, availability, checkpoints and rewards.", MessageType.Info);
                DrawMissionInspector();
            }
            EditorGUILayout.Space(8);
            DrawLayoutReview();
            EditorGUILayout.EndScrollView();
        }

        private void DrawObjectiveInspector(MissionObjective node)
        {
            EditorGUILayout.LabelField("Objective", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox("Stable IDs are runtime contract keys. Changing them changes save compatibility and all objective references, so use Raw JSON for deliberate migrations.", MessageType.Warning);
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("Stable ID", node.id);
            EditorGUI.EndDisabledGroup();
            node.title = EditorGUILayout.TextField("Title", node.title);
            int kind = Array.IndexOf(BuiltInKinds, node.kind);
            if (kind < 0) kind = 0;
            int nextKind = EditorGUILayout.Popup("Primitive", kind, BuiltInKinds);
            if (nextKind != kind)
            {
                node.kind = BuiltInKinds[nextKind];
                if (node.kind == "sequence" && (node.sequence == null || node.sequence.Length == 0)) node.sequence = new[] { "checkpoint." + node.id };
                if ((node.kind == "hold" || node.kind == "condition") && node.success == null) node.success = MissionCondition.Fact("condition." + node.id);
                if ((node.kind == "timer" || node.kind == "hold" || node.kind == "deadline") && node.duration <= 0) node.duration = 5;
                if (node.kind == "event" && string.IsNullOrEmpty(node.eventType)) node.eventType = "event." + node.id;
            }
            node.parent = DrawNodeIdPopup("Parent / hierarchy", node.parent, true);
            using (new EditorGUILayout.HorizontalScope())
            {
                node.optional = EditorGUILayout.ToggleLeft("Optional failure", node.optional, GUILayout.Width(120));
                node.uniqueTargets = EditorGUILayout.ToggleLeft("Unique targets", node.uniqueTargets, GUILayout.Width(115));
                node.accumulate = EditorGUILayout.ToggleLeft("Accumulate value", node.accumulate, GUILayout.Width(125));
                node.resetWhenFalse = EditorGUILayout.ToggleLeft("Reset hold", node.resetWhenFalse, GUILayout.Width(95));
            }
            EditorGUILayout.LabelField("Input port", EditorStyles.boldLabel);
            node.eventType = EditorGUILayout.TextField("Event type", node.eventType);
            node.target = EditorGUILayout.TextField("Target filter", node.target);
            node.marker = EditorGUILayout.TextField("World marker", node.marker);
            node.required = EditorGUILayout.DoubleField("Required progress", node.required);
            node.duration = EditorGUILayout.DoubleField("Duration seconds", node.duration);
            node.clock = (MissionClock)EditorGUILayout.EnumPopup("Clock", node.clock);
            node.failureReason = EditorGUILayout.TextField("Failure reason", node.failureReason);
            node.branchGroup = EditorGUILayout.TextField("Branch group", node.branchGroup);
            node.branchChoice = EditorGUILayout.TextField("Branch choice", node.branchChoice);
            DrawDependencies(node);
            DrawSequence(node);
            node.activate = DrawConditionEditor("Activation condition", node.activate ?? MissionCondition.All(), false);
            node.success = DrawConditionEditor("Success condition", node.success, true);
            node.failure = DrawConditionEditor("Failure condition", node.failure, true);
            DrawActionList(node);
            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply objective")) CommitNodeBuffer();
                if (GUILayout.Button("Revert")) { objectiveBuffer = null; PrepareObjectiveBuffer(node.id); status = "Reverted objective draft."; }
                if (GUILayout.Button("Delete")) DeleteSelected();
            }
            EditorGUILayout.HelpBox("Port semantics: output → input edits control dependencies. Orange condition links and gray parent links are derived references and are edited above, so every visible edge has a single source of truth.", MessageType.Info);
        }

        private void DrawMissionInspector()
        {
            PrepareMissionBuffer();
            if (missionBuffer == null) return;
            EditorGUILayout.LabelField("Mission contract", EditorStyles.largeLabel);
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("Stable ID", missionBuffer.id);
            EditorGUI.EndDisabledGroup();
            missionBuffer.title = EditorGUILayout.TextField("Title", missionBuffer.title);
            missionBuffer.version = EditorGUILayout.IntField("Content version", missionBuffer.version);
            missionBuffer.failureReason = EditorGUILayout.TextField("Mission failure reason", missionBuffer.failureReason);
            missionBuffer.availability = DrawCareerRequirementEditor("Career availability", missionBuffer.availability, true);
            missionBuffer.success = DrawConditionEditor("Mission success", missionBuffer.success, false);
            missionBuffer.failure = DrawConditionEditor("Mission failure", missionBuffer.failure, true);
            DrawCheckpointList(missionBuffer);
            DrawRewardList(missionBuffer);
            EditorGUILayout.Space(8);
            if (GUILayout.Button("Apply mission contract", GUILayout.Height(28))) CommitMissionBuffer();
            EditorGUILayout.HelpBox("Mission success/failure and reward conditions are evaluated by MissionRuntime. This editor does not add a parallel evaluator or silently infer gameplay outcomes.", MessageType.Info);
        }

        private void DrawDependencies(MissionObjective node)
        {
            EditorGUILayout.LabelField("Control dependencies", EditorStyles.boldLabel);
            var values = new List<string>(node.dependencies ?? Array.Empty<string>());
            for (int i = 0; i < values.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    values[i] = DrawNodeIdPopup("", values[i], false);
                    if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(24))) { values.RemoveAt(i); i--; }
                }
            }
            if (GUILayout.Button("Add dependency", EditorStyles.miniButton))
            {
                string candidate = (definition.objectives ?? Array.Empty<MissionObjective>()).Where(x => x != null && x.id != node.id && !values.Contains(x.id)).Select(x => x.id).FirstOrDefault();
                if (!string.IsNullOrEmpty(candidate)) values.Add(candidate);
                else status = "No unused objective is available for a dependency.";
            }
            node.dependencies = values.ToArray();
        }

        private void DrawSequence(MissionObjective node)
        {
            if (node.kind != "sequence") return;
            EditorGUILayout.LabelField("Ordered target tokens", EditorStyles.boldLabel);
            var values = new List<string>(node.sequence ?? Array.Empty<string>());
            for (int i = 0; i < values.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField((i + 1).ToString(), GUILayout.Width(22));
                    values[i] = EditorGUILayout.TextField(values[i]);
                    if (GUILayout.Button("↑", EditorStyles.miniButton, GUILayout.Width(22)) && i > 0) { var temp = values[i - 1]; values[i - 1] = values[i]; values[i] = temp; }
                    if (GUILayout.Button("↓", EditorStyles.miniButton, GUILayout.Width(22)) && i < values.Count - 1) { var temp = values[i + 1]; values[i + 1] = values[i]; values[i] = temp; }
                    if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(24))) { values.RemoveAt(i); i--; }
                }
            }
            if (GUILayout.Button("Add target token", EditorStyles.miniButton)) values.Add("checkpoint." + node.id + "." + (values.Count + 1));
            node.sequence = values.ToArray();
        }

        private void DrawActionList(MissionObjective node)
        {
            EditorGUILayout.LabelField("Typed world actions", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Actions are desired-state commands reconciled by IMissionActions. The graph authoring layer only stores the typed command; it never mutates a vehicle, police unit, UI panel or streaming system.", MessageType.None);
            var values = new List<MissionAction>(node.actions ?? Array.Empty<MissionAction>());
            for (int i = 0; i < values.Count; i++)
            {
                var action = values[i] ?? new MissionAction();
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        action.id = EditorGUILayout.TextField("ID", action.id);
                        if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(24))) { values.RemoveAt(i); i--; continue; }
                    }
                    action.kind = EditorGUILayout.TextField("Command kind", action.kind);
                    action.binding = EditorGUILayout.TextField("World binding", action.binding);
                    action.value = EditorGUILayout.TextField("Value / payload", action.value);
                    values[i] = action;
                }
            }
            if (GUILayout.Button("Add action", EditorStyles.miniButton))
            {
                var actionIds = new HashSet<string>(values.Where(x => x != null).Select(x => x.id), StringComparer.Ordinal);
                values.Add(new MissionAction { id = MissionGraphEditorModel.NewId(node.id + ".action", actionIds), kind = "spawn", binding = "world.binding", value = "" });
            }
            node.actions = values.ToArray();
        }

        private MissionCondition DrawConditionEditor(string label, MissionCondition condition, bool allowUnset)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                    if (allowUnset && condition != null && GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(48))) return null;
                    if (condition == null)
                    {
                        if (GUILayout.Button("Configure", EditorStyles.miniButton, GUILayout.Width(72))) condition = MissionCondition.All();
                        else return null;
                    }
                }
                if (condition == null) return null;
                condition.kind = (MissionConditionKind)EditorGUILayout.EnumPopup("Kind", condition.kind);
                if (condition.kind == MissionConditionKind.All || condition.kind == MissionConditionKind.Any || condition.kind == MissionConditionKind.Not)
                {
                    var children = new List<MissionCondition>(condition.children ?? Array.Empty<MissionCondition>());
                    for (int i = 0; i < children.Count; i++)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField("Child " + (i + 1), GUILayout.Width(58));
                            children[i] = DrawConditionEditor("", children[i], false);
                            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(24))) { children.RemoveAt(i); i--; }
                        }
                    }
                    if (GUILayout.Button("Add child", EditorStyles.miniButton)) children.Add(MissionCondition.All());
                    condition.children = children.ToArray();
                    if (condition.kind == MissionConditionKind.All) EditorGUILayout.HelpBox("ALL with zero children is true and is the runtime default activation condition.", MessageType.None);
                    if (condition.kind == MissionConditionKind.Any && children.Count == 0) EditorGUILayout.HelpBox("ANY needs at least one child.", MessageType.Warning);
                    if (condition.kind == MissionConditionKind.Not && children.Count != 1) EditorGUILayout.HelpBox("NOT needs exactly one child.", MessageType.Warning);
                }
                else if (condition.kind == MissionConditionKind.ObjectiveIs || condition.kind == MissionConditionKind.ProgressAtLeast || condition.kind == MissionConditionKind.RemainingAtMost)
                {
                    condition.key = DrawNodeIdPopup("Objective", condition.key, false);
                    if (condition.kind == MissionConditionKind.ObjectiveIs) condition.state = (ObjectiveState)EditorGUILayout.EnumPopup("State", condition.state);
                    else condition.value = EditorGUILayout.DoubleField(condition.kind == MissionConditionKind.RemainingAtMost ? "Seconds remaining" : "Progress threshold", condition.value);
                }
                else if (condition.kind == MissionConditionKind.FactAtLeast || condition.kind == MissionConditionKind.FactEquals)
                {
                    condition.key = EditorGUILayout.TextField("Fact key", condition.key);
                    condition.value = EditorGUILayout.DoubleField(condition.kind == MissionConditionKind.FactEquals ? "Exact value" : "Minimum value", condition.value);
                }
                else if (condition.kind == MissionConditionKind.Career)
                {
                    condition.career = DrawCareerRequirementEditor("Requirement", condition.career, false);
                }
            }
            return condition;
        }

        private CareerRequirementDefinition DrawCareerRequirementEditor(string label, CareerRequirementDefinition requirement, bool allowUnset)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                    if (allowUnset && requirement != null && GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(48))) return null;
                    if (requirement == null)
                    {
                        if (GUILayout.Button("Configure", EditorStyles.miniButton, GUILayout.Width(72))) requirement = NewCareerRequirement();
                        else return null;
                    }
                }
                if (requirement == null) return null;
                requirement.kind = (CareerRequirementKind)EditorGUILayout.EnumPopup("Kind", requirement.kind);
                requirement.description = EditorGUILayout.TextField("Description", requirement.description);
                if (requirement.kind == CareerRequirementKind.Fact)
                {
                    requirement.fact = (CareerFactKind)EditorGUILayout.EnumPopup("Career fact", requirement.fact);
                    bool keyed = requirement.fact >= CareerFactKind.EventCompleted;
                    requirement.subjectId = keyed ? EditorGUILayout.TextField("Subject ID", requirement.subjectId) : string.Empty;
                    requirement.required = EditorGUILayout.LongField("Required", requirement.required);
                    if (!keyed) EditorGUILayout.LabelField("Global fact", EditorStyles.miniLabel);
                }
                else
                {
                    var children = new List<CareerRequirementDefinition>(requirement.children ?? Array.Empty<CareerRequirementDefinition>());
                    for (int i = 0; i < children.Count; i++)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            children[i] = DrawCareerRequirementEditor("Child " + (i + 1), children[i], false);
                            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(24))) { children.RemoveAt(i); i--; }
                        }
                    }
                    if (GUILayout.Button("Add requirement child", EditorStyles.miniButton)) children.Add(NewCareerRequirement());
                    requirement.children = children.ToArray();
                    if (requirement.kind == CareerRequirementKind.Any && children.Count == 0) EditorGUILayout.HelpBox("ANY needs one child.", MessageType.Warning);
                    if (requirement.kind == CareerRequirementKind.Not && children.Count != 1) EditorGUILayout.HelpBox("NOT needs exactly one child.", MessageType.Warning);
                }
            }
            return requirement;
        }

        private static CareerRequirementDefinition NewCareerRequirement()
        {
            return new CareerRequirementDefinition { kind = CareerRequirementKind.Fact, fact = CareerFactKind.Reputation, required = 1, children = Array.Empty<CareerRequirementDefinition>() };
        }

        private static MissionCondition MakeFactEquals(string key, double value)
        {
            return new MissionCondition { kind = MissionConditionKind.FactEquals, key = key, value = value, children = Array.Empty<MissionCondition>() };
        }

        private string DrawNodeIdPopup(string label, string current, bool includeEmpty)
        {
            var values = new List<string>();
            if (includeEmpty) values.Add(string.Empty);
            foreach (var node in definition?.objectives ?? Array.Empty<MissionObjective>())
                if (node != null && !values.Contains(node.id)) values.Add(node.id);
            if (!string.IsNullOrEmpty(current) && !values.Contains(current)) values.Add(current);
            if (values.Count == 0) return current ?? string.Empty;
            int index = Math.Max(0, values.IndexOf(current));
            string result = values[EditorGUILayout.Popup(label, index, values.Select(x => string.IsNullOrEmpty(x) ? "(none)" : x).ToArray())];
            return result;
        }

        private void DrawCheckpointList(MissionDefinition value)
        {
            EditorGUILayout.LabelField("Recovery checkpoints", EditorStyles.boldLabel);
            var values = new List<MissionCheckpoint>(value.checkpoints ?? Array.Empty<MissionCheckpoint>());
            for (int i = 0; i < values.Count; i++)
            {
                var checkpoint = values[i] ?? new MissionCheckpoint();
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        checkpoint.id = EditorGUILayout.TextField("ID", checkpoint.id);
                        if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(24))) { values.RemoveAt(i); i--; continue; }
                    }
                    checkpoint.when = DrawConditionEditor("Save when", checkpoint.when ?? MissionCondition.All(), false);
                    values[i] = checkpoint;
                }
            }
            if (GUILayout.Button("Add recovery checkpoint", EditorStyles.miniButton))
            {
                var ids = new HashSet<string>(values.Where(x => x != null).Select(x => x.id), StringComparer.Ordinal);
                values.Add(new MissionCheckpoint { id = MissionGraphEditorModel.NewId("checkpoint.recovery", ids), when = MissionCondition.All() });
            }
            value.checkpoints = values.ToArray();
        }

        private void DrawRewardList(MissionDefinition value)
        {
            EditorGUILayout.LabelField("Rewards / settlement lines", EditorStyles.boldLabel);
            var values = new List<MissionReward>(value.rewards ?? Array.Empty<MissionReward>());
            for (int i = 0; i < values.Count; i++)
            {
                var reward = values[i] ?? new MissionReward();
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        reward.id = EditorGUILayout.TextField("ID", reward.id);
                        if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(24))) { values.RemoveAt(i); i--; continue; }
                    }
                    reward.cash = EditorGUILayout.IntField("Cash", reward.cash);
                    reward.reputation = EditorGUILayout.LongField("Reputation", reward.reputation);
                    reward.when = DrawConditionEditor("Award when", reward.when ?? MissionCondition.All(), false);
                    var grants = new List<EconomyGrant>(reward.grants ?? Array.Empty<EconomyGrant>());
                    for (int grantIndex = 0; grantIndex < grants.Count; grantIndex++)
                    {
                        var grant = grants[grantIndex] ?? new EconomyGrant();
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            grant.kind = (EconomyItemKind)EditorGUILayout.EnumPopup(grant.kind, GUILayout.Width(120));
                            grant.itemId = EditorGUILayout.TextField(grant.itemId);
                            grant.ownership = EditorGUILayout.ToggleLeft("Owned", grant.ownership, GUILayout.Width(65));
                            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(24))) { grants.RemoveAt(grantIndex); grantIndex--; continue; }
                            grants[grantIndex] = grant;
                        }
                    }
                    if (GUILayout.Button("Add grant", EditorStyles.miniButton)) grants.Add(new EconomyGrant { kind = EconomyItemKind.Event, itemId = "content.item" });
                    reward.grants = grants.ToArray();
                    values[i] = reward;
                }
            }
            if (GUILayout.Button("Add reward", EditorStyles.miniButton))
            {
                var ids = new HashSet<string>(values.Where(x => x != null).Select(x => x.id), StringComparer.Ordinal);
                values.Add(new MissionReward { id = MissionGraphEditorModel.NewId("reward", ids), when = MissionCondition.All(), grants = Array.Empty<EconomyGrant>() });
            }
            value.rewards = values.ToArray();
        }

        private void DrawLayoutReview()
        {
            EditorGUILayout.LabelField("Editor-only review metadata", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox("Groups, comments, bookmarks, reroutes and variable notes are stored in the .layout.asset sidecar. They never enter the mission JSON, runtime content hash, or career save.", MessageType.Info);
            if (layoutAsset == null) return;
            if (!AssetDatabase.Contains(layoutAsset) && GUILayout.Button("Create persistent layout sidecar")) EnsurePersistentLayout();
            DrawBookmarks();
            EditorGUILayout.LabelField("Groups", EditorStyles.boldLabel);
            foreach (var group in layoutAsset.Groups.ToArray())
            {
                if (group == null) continue;
                string title = EditorGUILayout.TextField(group.title);
                bool collapsed = EditorGUILayout.ToggleLeft("Collapsed", group.collapsed);
                Color color = EditorGUILayout.ColorField("Color", group.color);
                if (title != group.title || collapsed != group.collapsed || color != group.color)
                {
                    Undo.RecordObject(layoutAsset, "Edit mission graph group"); group.title = title; group.collapsed = collapsed; group.color = color; EditorUtility.SetDirty(layoutAsset);
                }
            }
            EditorGUILayout.LabelField("Comments", EditorStyles.boldLabel);
            foreach (var comment in layoutAsset.Comments.ToArray())
            {
                if (comment == null) continue;
                string text = EditorGUILayout.TextArea(comment.text, GUILayout.MinHeight(38));
                Color color = EditorGUILayout.ColorField("Color", comment.color);
                if (text != comment.text || color != comment.color)
                {
                    Undo.RecordObject(layoutAsset, "Edit mission graph comment"); comment.text = text; comment.color = color; EditorUtility.SetDirty(layoutAsset);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add group from selection")) GroupSelected();
                if (GUILayout.Button("Add comment")) AddComment();
                if (GUILayout.Button("Add bookmark")) AddBookmark();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add reroute to selected edge")) AddRerouteToSelectedEdge();
                if (GUILayout.Button("Remove selected edge")) RemoveSelectedEdge();
            }
        }

        private void PrepareObjectiveBuffer(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (objectiveBuffer != null && objectiveBufferId == id) return;
            var source = MissionGraphEditorModel.FindNode(definition, id);
            objectiveBuffer = MissionGraphEditorModel.CloneObjective(source);
            objectiveBufferId = id;
        }

        private void PrepareMissionBuffer()
        {
            if (definition == null) return;
            if (missionBuffer != null && missionBufferHash == rawJson) return;
            missionBuffer = MissionGraphEditorModel.CloneDefinition(definition);
            missionBufferHash = rawJson;
        }

        private void CommitNodeBuffer()
        {
            if (definition == null || objectiveBuffer == null) return;
            var candidate = MissionGraphEditorModel.CloneDefinition(definition);
            int index = Array.FindIndex(candidate.objectives ?? Array.Empty<MissionObjective>(), x => x != null && x.id == objectiveBuffer.id);
            if (index < 0) { status = "Objective no longer exists in the source definition."; return; }
            candidate.objectives[index] = MissionGraphEditorModel.CloneObjective(objectiveBuffer);
            CommitDefinition("Edit mission objective", candidate);
            selectedNodes.Clear(); selectedNodes.Add(objectiveBufferId);
        }

        private void CommitMissionBuffer()
        {
            if (missionBuffer == null) return;
            CommitDefinition("Edit mission contract", MissionGraphEditorModel.CloneDefinition(missionBuffer));
        }

        private void RefreshDiagnostics()
        {
            diagnostics.Clear();
            diagnostics.AddRange(MissionGraphEditorModel.Validate(definition, layoutAsset));
            int errors = diagnostics.Count(x => x.severity == MissionGraphDiagnosticSeverity.Error);
            status = errors == 0 ? "Validation passed: no errors." : "Validation found " + errors + " error(s).";
            Repaint();
        }

        private void DrawValidationTab()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh diagnostics", GUILayout.Width(140))) RefreshDiagnostics();
                showErrorDiagnostics = GUILayout.Toggle(showErrorDiagnostics, "Errors", EditorStyles.toolbarButton);
                showWarningDiagnostics = GUILayout.Toggle(showWarningDiagnostics, "Warnings", EditorStyles.toolbarButton);
                showInfoDiagnostics = GUILayout.Toggle(showInfoDiagnostics, "Info", EditorStyles.toolbarButton);
                diagnosticSearch = EditorGUILayout.TextField(diagnosticSearch, GUI.skin.FindStyle("ToolbarSearchTextField"));
            }
            int errors = diagnostics.Count(x => x.severity == MissionGraphDiagnosticSeverity.Error);
            int warnings = diagnostics.Count(x => x.severity == MissionGraphDiagnosticSeverity.Warning);
            int infos = diagnostics.Count(x => x.severity == MissionGraphDiagnosticSeverity.Info);
            EditorGUILayout.HelpBox("Errors: " + errors + " · Warnings: " + warnings + " · Info: " + infos + " · Runtime compilation is fail-closed.", errors > 0 ? MessageType.Error : warnings > 0 ? MessageType.Warning : MessageType.Info);
            if (!string.IsNullOrEmpty(sourceError)) EditorGUILayout.HelpBox("Source compile error: " + sourceError, MessageType.Error);
            EditorGUILayout.LabelField("Bounded authoring checks", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("The validator checks stable IDs, typed event/action ports, control dependencies, condition references, branch uniqueness, runtime budgets, recovery checkpoints and compilation. It does not claim dynamic reachability without gameplay facts.", EditorStyles.wordWrappedLabel);
            var scroll = EditorGUILayout.BeginScrollView(Vector2.zero, GUILayout.ExpandHeight(true));
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic == null || !DiagnosticVisible(diagnostic)) continue;
                MessageType type = diagnostic.severity == MissionGraphDiagnosticSeverity.Error ? MessageType.Error : diagnostic.severity == MissionGraphDiagnosticSeverity.Warning ? MessageType.Warning : MessageType.Info;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox("[" + diagnostic.code + "] " + diagnostic.message + (string.IsNullOrEmpty(diagnostic.property) ? string.Empty : " · " + diagnostic.property), type);
                    if (!string.IsNullOrEmpty(diagnostic.nodeId) && MissionGraphEditorModel.FindNode(definition, diagnostic.nodeId) != null && GUILayout.Button("Go", GUILayout.Width(38)))
                    {
                        selectedNodes.Clear(); selectedNodes.Add(diagnostic.nodeId); PrepareObjectiveBuffer(diagnostic.nodeId); tab = 1; FrameSelection();
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private bool DiagnosticVisible(MissionGraphDiagnostic diagnostic)
        {
            if (diagnostic.severity == MissionGraphDiagnosticSeverity.Error && !showErrorDiagnostics) return false;
            if (diagnostic.severity == MissionGraphDiagnosticSeverity.Warning && !showWarningDiagnostics) return false;
            if (diagnostic.severity == MissionGraphDiagnosticSeverity.Info && !showInfoDiagnostics) return false;
            if (string.IsNullOrWhiteSpace(diagnosticSearch)) return true;
            string text = diagnostic.code + " " + diagnostic.message + " " + diagnostic.nodeId + " " + diagnostic.property;
            return text.IndexOf(diagnosticSearch, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawVariablesTab()
        {
            EditorGUILayout.HelpBox("MissionRuntime facts, objective IDs, action bindings and event types are the authoritative channels. This panel discovers those channels and stores optional designer notes in the editor-only sidecar; it never creates a second variable database.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                variableSearch = EditorGUILayout.TextField("Search", variableSearch);
                if (GUILayout.Button("Add note", GUILayout.Width(80)))
                {
                    EnsurePersistentLayout();
                    var keys = new HashSet<string>(layoutAsset.Variables.Where(x => x != null).Select(x => x.key), StringComparer.Ordinal);
                    Undo.RecordObject(layoutAsset, "Add mission graph variable note");
                    layoutAsset.Variables.Add(new MissionGraphVariableNote { key = MissionGraphEditorModel.NewId("fact.new", keys), type = "number", ownership = "authoritative fact" });
                    EditorUtility.SetDirty(layoutAsset);
                }
            }
            var inferred = InferChannels();
            EditorGUILayout.LabelField("Discovered channels (" + inferred.Count + ")", EditorStyles.boldLabel);
            variablesScroll = EditorGUILayout.BeginScrollView(variablesScroll, GUILayout.ExpandHeight(true));
            foreach (var channel in inferred.Where(MatchesVariable).OrderBy(x => x.key, StringComparer.Ordinal))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(channel.key, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(channel.kind + " · " + string.Join(", ", channel.sources.Distinct(StringComparer.Ordinal)), EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Designer notes", EditorStyles.boldLabel);
            if (layoutAsset == null || layoutAsset.Variables.Count == 0) EditorGUILayout.LabelField("No notes yet. Add one to document ownership, units, streaming lifetime or producer/consumer assumptions.", EditorStyles.miniLabel);
            else
            {
                foreach (var note in layoutAsset.Variables.ToArray())
                {
                    if (note == null || (!string.IsNullOrWhiteSpace(variableSearch) && (note.key + " " + note.notes).IndexOf(variableSearch, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        string key = EditorGUILayout.TextField("Key", note.key);
                        string type = EditorGUILayout.TextField("Type", note.type);
                        string ownership = EditorGUILayout.TextField("Ownership", note.ownership);
                        string notes = EditorGUILayout.TextArea(note.notes, GUILayout.MinHeight(42));
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("Apply note"))
                            {
                                Undo.RecordObject(layoutAsset, "Edit mission graph variable note"); note.key = key; note.type = type; note.ownership = ownership; note.notes = notes; EditorUtility.SetDirty(layoutAsset);
                            }
                            if (GUILayout.Button("Remove note"))
                            {
                                Undo.RecordObject(layoutAsset, "Remove mission graph variable note"); layoutAsset.Variables.Remove(note); EditorUtility.SetDirty(layoutAsset);
                            }
                        }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private bool MatchesVariable(InferredChannel channel)
        {
            return string.IsNullOrWhiteSpace(variableSearch) || (channel.key + " " + channel.kind).IndexOf(variableSearch, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private sealed class InferredChannel
        {
            public string key;
            public string kind;
            public readonly List<string> sources = new List<string>();
        }

        private List<InferredChannel> InferChannels()
        {
            var channels = new Dictionary<string, InferredChannel>(StringComparer.Ordinal);
            void Add(string key, string kind, string source)
            {
                if (string.IsNullOrWhiteSpace(key)) return;
                if (!channels.TryGetValue(key, out var channel)) channels.Add(key, channel = new InferredChannel { key = key, kind = kind });
                if (!string.IsNullOrEmpty(source) && !channel.sources.Contains(source)) channel.sources.Add(source);
            }
            foreach (var node in definition?.objectives ?? Array.Empty<MissionObjective>())
            {
                if (node == null) continue;
                Add(node.id, "objective ID", node.id);
                Add(node.eventType, "event type", node.id);
                Add(node.target, "event target", node.id);
                Add(node.marker, "world marker", node.id);
                Add(node.branchGroup, "branch group", node.id);
                foreach (var action in node.actions ?? Array.Empty<MissionAction>()) if (action != null) { Add(action.binding, "action binding", action.id); Add(action.kind, "action kind", action.id); }
                CollectConditionChannels(node.activate, node.id, Add);
                CollectConditionChannels(node.success, node.id, Add);
                CollectConditionChannels(node.failure, node.id, Add);
            }
            CollectConditionChannels(definition?.success, "mission.success", Add);
            CollectConditionChannels(definition?.failure, "mission.failure", Add);
            foreach (var checkpoint in definition?.checkpoints ?? Array.Empty<MissionCheckpoint>()) if (checkpoint != null) { Add(checkpoint.id, "checkpoint ID", "mission.checkpoints"); CollectConditionChannels(checkpoint.when, checkpoint.id, Add); }
            foreach (var reward in definition?.rewards ?? Array.Empty<MissionReward>()) if (reward != null) { Add(reward.id, "reward ID", "mission.rewards"); foreach (var grant in reward.grants ?? Array.Empty<EconomyGrant>()) if (grant != null) Add(grant.itemId, "economy grant", reward.id); CollectConditionChannels(reward.when, reward.id, Add); }
            return channels.Values.ToList();
        }

        private static void CollectConditionChannels(MissionCondition condition, string source, Action<string, string, string> add)
        {
            if (condition == null) return;
            switch (condition.kind)
            {
                case MissionConditionKind.FactAtLeast:
                case MissionConditionKind.FactEquals: add(condition.key, "fact key", source); break;
                case MissionConditionKind.ObjectiveIs:
                case MissionConditionKind.ProgressAtLeast:
                case MissionConditionKind.RemainingAtMost: add(condition.key, "objective reference", source); break;
                case MissionConditionKind.Career: CollectCareerChannels(condition.career, source, add); break;
            }
            foreach (var child in condition.children ?? Array.Empty<MissionCondition>()) CollectConditionChannels(child, source, add);
        }

        private static void CollectCareerChannels(CareerRequirementDefinition requirement, string source, Action<string, string, string> add)
        {
            if (requirement == null) return;
            if (requirement.kind == CareerRequirementKind.Fact) add(requirement.fact + (string.IsNullOrEmpty(requirement.subjectId) ? string.Empty : ":" + requirement.subjectId), "career fact", source);
            foreach (var child in requirement.children ?? Array.Empty<CareerRequirementDefinition>()) CollectCareerChannels(child, source, add);
        }

        private MissionRuntime DisplayRuntime => attachedHost != null && attachedHost.Runtime != null ? attachedHost.Runtime : simulation;

        private void DrawSimulationTab()
        {
            EditorGUILayout.HelpBox("Preview runs the same MissionGraph and MissionRuntime used by the game with a dry-run IMissionActions adapter. It does not write career saves, settle rewards, spawn police, or mutate the world. Attach a live MissionHost to inspect play-mode state read-only.", MessageType.Info);
            attachedHost = (MissionHost)EditorGUILayout.ObjectField("Live MissionHost", attachedHost, typeof(MissionHost), true);
            var liveRuntime = attachedHost == null ? null : attachedHost.Runtime;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(liveRuntime != null || EditorApplication.isPlaying);
                if (GUILayout.Button("Start preview", GUILayout.Width(110))) StartPreview();
                if (GUILayout.Button("Reset preview", GUILayout.Width(105))) ResetPreview();
                EditorGUI.EndDisabledGroup();
                EditorGUI.BeginDisabledGroup(liveRuntime != null || simulation == null || EditorApplication.isPlaying);
                if (GUILayout.Button("Step", GUILayout.Width(65))) StepPreview();
                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button("Use selected input", GUILayout.Width(125))) UseSelectedInput();
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                simulationGameDelta = Math.Max(0, EditorGUILayout.DoubleField("Game Δ", simulationGameDelta));
                simulationRealDelta = Math.Max(0, EditorGUILayout.DoubleField("Real Δ", simulationRealDelta));
                injectEvent = EditorGUILayout.ToggleLeft("Inject event", injectEvent, GUILayout.Width(100));
            }
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                eventId = EditorGUILayout.TextField("Event ID", eventId);
                eventType = EditorGUILayout.TextField("Event type", eventType);
                eventTarget = EditorGUILayout.TextField("Target", eventTarget);
                eventFact = EditorGUILayout.TextField("Fact key", eventFact);
                eventValue = EditorGUILayout.DoubleField("Value", eventValue);
            }
            if (!string.IsNullOrEmpty(simulationStatus)) EditorGUILayout.HelpBox(simulationStatus, MessageType.None);
            var runtime = DisplayRuntime;
            if (runtime == null)
            {
                EditorGUILayout.HelpBox("No preview or live runtime is attached. Start a preview in edit mode or assign a MissionHost while the game is running.", MessageType.None);
                return;
            }
            simulationScroll = EditorGUILayout.BeginScrollView(simulationScroll, GUILayout.ExpandHeight(true));
            DrawRuntimeSummary(runtime);
            EditorGUILayout.EndScrollView();
        }

        private void DrawRuntimeSummary(MissionRuntime runtime)
        {
            EditorGUILayout.LabelField("Runtime state", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("Mission", runtime.Id);
            EditorGUILayout.LabelField("State", runtime.State.ToString());
            EditorGUILayout.LabelField("Elapsed", runtime.Elapsed.ToString("0.000") + " s");
            EditorGUILayout.LabelField("Subscriptions", runtime.SubscriptionCount.ToString());
            EditorGUILayout.LabelField("Events delivered", runtime.EventsDelivered.ToString());
            if (runtime.State == MissionState.Succeeded || runtime.State == MissionState.Failed || runtime.State == MissionState.Aborted)
            {
                var result = runtime.Result;
                if (result != null) EditorGUILayout.HelpBox("Result: " + result.outcome + " · cash " + result.cash + " · reputation " + result.reputation + (string.IsNullOrEmpty(result.failure) ? string.Empty : " · " + result.failure), result.outcome == MissionState.Succeeded ? MessageType.Info : MessageType.Warning);
            }
            EditorGUILayout.LabelField("Objective state", EditorStyles.boldLabel);
            foreach (var node in runtime.Definition.objectives ?? Array.Empty<MissionObjective>())
            {
                if (node == null) continue;
                var snapshot = runtime.Objective(node.id);
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(node.title + "  [" + node.id + "]", GUILayout.MinWidth(220));
                    EditorGUILayout.LabelField(snapshot.state.ToString(), GUILayout.Width(90));
                    EditorGUILayout.LabelField(snapshot.progress.ToString("0.##") + " / " + (node.kind == "sequence" ? node.sequence.Length.ToString() : node.required.ToString("0.##")), GUILayout.Width(105));
                }
            }
            if (previewActions != null) EditorGUILayout.LabelField("Dry-run desired actions: " + previewActions.DesiredSummary, EditorStyles.miniLabel);
        }

        private void StartPreview()
        {
            if (definition == null) return;
            DisposeSimulation();
            try
            {
                var graph = new MissionGraph(definition);
                previewActions = new PreviewActions();
                simulation = new MissionRuntime(graph, null, previewActions);
                simulationStep = 0;
                simulationStatus = "Preview started. Input steps are deterministic and do not touch the game world.";
            }
            catch (Exception exception)
            {
                simulation = null; previewActions = null; simulationStatus = "Preview could not start: " + exception.Message;
            }
            Repaint();
        }

        private void ResetPreview()
        {
            DisposeSimulation();
            simulationStatus = "Preview reset.";
            Repaint();
        }

        private void StepPreview()
        {
            if (simulation == null) { StartPreview(); if (simulation == null) return; }
            try
            {
                var events = injectEvent ? new[] { new MissionEvent(eventId, eventType, eventTarget, eventValue, eventFact) } : Array.Empty<MissionEvent>();
                simulation.Step(++simulationStep, Math.Max(0, simulationGameDelta), Math.Max(0, simulationRealDelta), events);
                simulationStatus = "Advanced preview to step " + simulationStep + ".";
                if (eventId == "preview." + simulationStep) eventId = "preview." + (simulationStep + 1);
            }
            catch (Exception exception) { simulationStatus = "Preview step failed: " + exception.Message; }
            Repaint();
        }

        private void UseSelectedInput()
        {
            var node = selectedNodes.Count == 1 ? MissionGraphEditorModel.FindNode(definition, selectedNodes.First()) : null;
            if (node == null) { status = "Select one event or sequence objective first."; return; }
            eventType = node.eventType;
            eventTarget = node.target;
            eventId = "preview." + (simulationStep + 1);
            status = "Preview input copied from " + node.id + ".";
            Repaint();
        }

        private void DrawTraceTab()
        {
            var runtime = DisplayRuntime;
            if (runtime == null)
            {
                EditorGUILayout.HelpBox("Start a preview or attach a live MissionHost to inspect the deterministic event trace.", MessageType.None);
                return;
            }
            using (new EditorGUILayout.HorizontalScope()) traceFilterText = EditorGUILayout.TextField("Filter", traceFilterText);
            EditorGUILayout.HelpBox("Trace is diagnostic evidence from the selected runtime. It is capped by MissionRuntime and is not a save format.", MessageType.Info);
            traceScroll = EditorGUILayout.BeginScrollView(traceScroll, GUILayout.ExpandHeight(true));
            DrawRuntimeSummary(runtime);
            EditorGUILayout.LabelField("Timeline", EditorStyles.largeLabel);
            foreach (var line in runtime.Timeline)
                if (string.IsNullOrWhiteSpace(traceFilterText) || line.IndexOf(traceFilterText, StringComparison.OrdinalIgnoreCase) >= 0)
                    EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Condition explanations", EditorStyles.largeLabel);
            foreach (var node in runtime.Definition.objectives ?? Array.Empty<MissionObjective>())
            {
                if (node == null) continue;
                if (node.activate != null) DrawRuntimeCondition(runtime, node.id + " / activation", node.activate);
                if (node.success != null) DrawRuntimeCondition(runtime, node.id + " / success", node.success);
                if (node.failure != null) DrawRuntimeCondition(runtime, node.id + " / failure", node.failure);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawRuntimeCondition(MissionRuntime runtime, string label, MissionCondition condition)
        {
            try { EditorGUILayout.LabelField(label + "\n" + runtime.Explain(condition), EditorStyles.wordWrappedMiniLabel); }
            catch (Exception exception) { EditorGUILayout.LabelField(label + " · explanation failed: " + exception.Message, EditorStyles.wordWrappedMiniLabel); }
        }

        private string traceFilterText = string.Empty;

        private sealed class PreviewActions : IMissionActions
        {
            private readonly List<string> desired = new List<string>();
            public string DesiredSummary => desired.Count == 0 ? "none" : string.Join(", ", desired);

            public void Reconcile(string claimId, int attempt, IReadOnlyList<MissionAction> actions)
            {
                desired.Clear();
                foreach (var action in actions ?? Array.Empty<MissionAction>()) if (action != null) desired.Add(action.id + " (" + action.kind + ")");
            }
        }

        private void DisposeSimulation()
        {
            if (simulation == null) { previewActions = null; return; }
            try { simulation.Dispose(); } catch (Exception exception) { simulationStatus = "Preview dispose failed: " + exception.Message; }
            simulation = null; previewActions = null; simulationStep = 0;
        }

        private void DrawPublishTab()
        {
            publishScroll = EditorGUILayout.BeginScrollView(publishScroll);
            EditorGUILayout.LabelField("Publish / source control", EditorStyles.largeLabel);
            if (definitionAsset == null) { EditorGUILayout.HelpBox("Select a mission asset first.", MessageType.None); EditorGUILayout.EndScrollView(); return; }
            try
            {
                var graph = definitionAsset.Compile();
                EditorGUILayout.HelpBox("Compiles for runtime · ID " + graph.Id + " · version " + graph.Version + " · content hash " + graph.ContentHash, MessageType.Info);
            }
            catch (Exception exception) { EditorGUILayout.HelpBox("Runtime compilation is blocked: " + exception.Message, MessageType.Error); }
            EditorGUILayout.LabelField("Raw JSON draft", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Use this escape hatch for deliberate stable-ID migrations or bulk review. Applying JSON preserves the draft even when MissionGraph rejects it, so the Validation tab can explain what must be repaired before play.", MessageType.Warning);
            rawJson = EditorGUILayout.TextArea(rawJson, GUILayout.MinHeight(300));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply JSON draft", GUILayout.Height(26))) CommitRawJson();
                if (GUILayout.Button("Reload source", GUILayout.Height(26))) ReloadFromAsset(true);
                if (GUILayout.Button("Copy JSON", GUILayout.Height(26))) { EditorGUIUtility.systemCopyBuffer = rawJson; status = "Copied mission JSON to the system clipboard."; }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate and save", GUILayout.Height(26))) ValidateAndSave();
                if (GUILayout.Button("Create layout sidecar", GUILayout.Height(26))) EnsurePersistentLayout();
                if (GUILayout.Button("Duplicate with fresh IDs", GUILayout.Height(26))) DuplicateMission();
            }
            if (GUILayout.Button("Select source asset")) Selection.activeObject = definitionAsset;
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Legacy compatibility", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("The older MissionGraphInspector can still inspect JSON through the normal Inspector menu. This workbench is the forward authoring path and shares the same MissionDefinitionAsset contract.", EditorStyles.wordWrappedLabel);
            if (GUILayout.Button("Open legacy inspector context")) { Selection.activeObject = definitionAsset; status = "Selected the source asset. Use the Inspector if you need the legacy JSON debugger."; }
            EditorGUILayout.EndScrollView();
        }

        private void ValidateAndSave()
        {
            RefreshDiagnostics();
            if (diagnostics.Any(x => x.severity == MissionGraphDiagnosticSeverity.Error))
            {
                tab = 2; status = "Save blocked: resolve validation errors first."; return;
            }
            AssetDatabase.SaveAssets();
            status = "Mission and available editor metadata saved.";
        }

        private void DuplicateMission()
        {
            if (definition == null) return;
            string path = EditorUtility.SaveFilePanelInProject("Duplicate mission definition", definitionAsset.name + ".copy", "asset", "Choose a new asset path.");
            if (string.IsNullOrEmpty(path)) return;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) { status = "An asset already exists at that path."; return; }
            try
            {
                var copy = MissionGraphEditorModel.CloneWithFreshIdentity(definition, "copy");
                var asset = CreateInstance<MissionDefinitionAsset>();
                asset.Configure(copy);
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
                RefreshCatalog(); SetDefinition(asset);
                status = "Duplicated mission with remapped objective, action, branch and condition IDs.";
            }
            catch (Exception exception) { status = "Duplicate rejected: " + exception.Message; }
        }

        private static string FirstLineOrEmpty(Exception exception)
        {
            return exception == null ? string.Empty : FirstLine(exception.Message);
        }
    }
}
