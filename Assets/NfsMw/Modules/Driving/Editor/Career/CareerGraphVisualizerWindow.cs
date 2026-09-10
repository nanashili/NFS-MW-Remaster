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
    /// Dockable career authoring, validation and progression-inspection workbench.
    /// The window is an editor projection: career semantics remain owned by
    /// CareerDefinitionAsset/CareerGraph, economy settlement remains owned by
    /// EconomySimulation, and all canvas state is stored in a sidecar asset.
    /// </summary>
    public sealed class CareerGraphVisualizerWindow : EditorWindow
    {
        private delegate bool LongSandboxCommand(long value, out string failure);
        private delegate bool FactSandboxCommand(CareerFactKind fact, string subject, long value, out string failure);
        private delegate bool NodeSandboxCommand(CareerGraphNodeModel node, out string failure);

        private static readonly string[] Tabs =
        {
            "Graph", "Inspector", "Validation", "Sandbox", "Simulation", "Impact"
        };

        private const float NodeWidth = 224f;
        private const float NodeHeight = 92f;
        private const float GridSize = 48f;
        private const int MaxDrawNodes = 800;

        [SerializeField] private CareerDefinitionAsset definitionAsset;
        [SerializeField] private EconomyDefinitionAsset economyAsset;
        [SerializeField] private CareerDefinitionAsset comparisonAsset;
        [SerializeField] private int activeTab;
        [SerializeField] private CareerGraphViewMode viewMode;
        [SerializeField] private CareerFactKind overrideFact;
        [SerializeField] private long overrideValue = 1;
        [SerializeField] private string overrideSubject = string.Empty;
        [SerializeField] private int simulationScenarioIndex;
        [SerializeField] private bool showExternal = true;
        [SerializeField] private bool showMissing = true;
        [SerializeField] private bool showFacts = true;
        [SerializeField] private bool showGroups = true;
        [SerializeField] private bool showComments = true;
        [SerializeField] private string tierFilter = "All";

        [NonSerialized] private CareerGraphProjection projection;
        [NonSerialized] private CareerGraphLayoutAsset layoutAsset;
        [NonSerialized] private CareerGraphSandbox sandbox;
        [NonSerialized] private CareerGraphComparison comparison;
        [NonSerialized] private EconomySimulationReport simulationReport;
        [NonSerialized] private readonly List<CareerDefinitionAsset> catalog = new List<CareerDefinitionAsset>();
        [NonSerialized] private string selectedKey = string.Empty;
        [NonSerialized] private string catalogSearch = string.Empty;
        [NonSerialized] private string nodeSearch = string.Empty;
        [NonSerialized] private string findingSearch = string.Empty;
        [NonSerialized] private string status = "Select a CareerDefinitionAsset or create the demo career.";
        [NonSerialized] private MessageType statusType = MessageType.Info;
        [NonSerialized] private string simulationStatus = string.Empty;
        [NonSerialized] private Vector2 catalogScroll;
        [NonSerialized] private Vector2 inspectorScroll;
        [NonSerialized] private Vector2 validationScroll;
        [NonSerialized] private Vector2 sandboxScroll;
        [NonSerialized] private Vector2 simulationScroll;
        [NonSerialized] private Vector2 impactScroll;
        [NonSerialized] private Vector2 graphToolsScroll;
        [NonSerialized] private bool draggingNode;
        [NonSerialized] private string draggingNodeKey = string.Empty;
        [NonSerialized] private Vector2 dragOffset;
        [NonSerialized] private bool draggingComment;
        [NonSerialized] private string draggingCommentId = string.Empty;
        [NonSerialized] private Vector2 commentDragOffset;
        [NonSerialized] private string selectedCommentId = string.Empty;
        [NonSerialized] private bool panning;
        [NonSerialized] private Vector2 lastCanvasMouse;
        [NonSerialized] private Vector2 graphCanvasSize;
        [NonSerialized] private int selectedExternalIndex = -1;

        public CareerDefinitionAsset DefinitionAsset => definitionAsset;
        public CareerGraphProjection Projection => projection;
        public CareerGraphLayoutAsset LayoutAsset => layoutAsset;

        [MenuItem("NFS MW Remaster/Driving/Career Graph Visualizer")]
        public static CareerGraphVisualizerWindow Open()
        {
            CareerGraphVisualizerWindow window = GetWindow<CareerGraphVisualizerWindow>();
            window.titleContent = new GUIContent("Career Graph Visualizer");
            window.minSize = new Vector2(1180f, 720f);
            window.RefreshCatalog();
            window.Focus();
            return window;
        }

        public static void SelectDefinition(CareerDefinitionAsset asset)
        {
            CareerGraphVisualizerWindow window = Open();
            window.SetDefinition(asset);
        }

        public static void SelectActiveDefinition()
        {
            CareerDefinitionAsset selected = Selection.activeObject as CareerDefinitionAsset;
            if (selected == null)
            {
                Open().SetStatus("Select a CareerDefinitionAsset in the Project window first.", MessageType.Warning);
                return;
            }

            SelectDefinition(selected);
        }

        [OnOpenAsset(2)]
        private static bool OpenCareerAsset(EntityId instanceId, int line)
        {
            CareerDefinitionAsset asset = EditorUtility.EntityIdToObject(instanceId) as CareerDefinitionAsset;
            if (asset == null)
            {
                return false;
            }

            SelectDefinition(asset);
            return true;
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Career Graph Visualizer");
            minSize = new Vector2(1180f, 720f);
            Selection.selectionChanged += OnSelectionChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.projectChanged += OnProjectChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            RefreshCatalog();

            if (definitionAsset != null)
            {
                SetDefinition(definitionAsset);
            }
            else if (Selection.activeObject is CareerDefinitionAsset selected)
            {
                SetDefinition(selected);
            }
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            DisposeTransientLayout();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.Add(new IMGUIContainer(DrawWindow) { style = { flexGrow = 1f } });
        }

        private void OnSelectionChanged()
        {
            CareerDefinitionAsset selected = Selection.activeObject as CareerDefinitionAsset;
            if (selected != null && selected != definitionAsset)
            {
                SetDefinition(selected);
            }
        }

        private void OnUndoRedo()
        {
            if (definitionAsset != null)
            {
                RefreshProjection(true);
            }
            Repaint();
        }

        private void OnProjectChanged()
        {
            RefreshCatalog();
            if (definitionAsset != null)
            {
                RefreshProjection(true);
            }
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode
                || state == PlayModeStateChange.EnteredPlayMode
                || state == PlayModeStateChange.ExitingPlayMode
                || state == PlayModeStateChange.EnteredEditMode)
            {
                simulationReport = null;
                simulationStatus = "Simulation cleared because play-mode state changed.";
            }
            Repaint();
        }

        private void OnBeforeAssemblyReload()
        {
            DisposeTransientLayout();
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
                activeTab = GUILayout.Toolbar(activeTab, Tabs);
                EditorGUILayout.Space(4f);
                switch (activeTab)
                {
                    case 0: DrawGraphTab(); break;
                    case 1: DrawInspectorTab(); break;
                    case 2: DrawValidationTab(); break;
                    case 3: DrawSandboxTab(); break;
                    case 4: DrawSimulationTab(); break;
                    case 5: DrawImpactTab(); break;
                    default: activeTab = 0; DrawGraphTab(); break;
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(status))
            {
                EditorGUILayout.HelpBox(status, statusType);
            }
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("CAREER GRAPH VISUALIZER", EditorStyles.boldLabel, GUILayout.Width(200f));
                EditorGUI.BeginChangeCheck();
                CareerDefinitionAsset selected = (CareerDefinitionAsset)EditorGUILayout.ObjectField(
                    definitionAsset,
                    typeof(CareerDefinitionAsset),
                    false,
                    GUILayout.Width(300f));
                if (EditorGUI.EndChangeCheck())
                {
                    SetDefinition(selected);
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Create Demo", EditorStyles.toolbarButton, GUILayout.Width(88f)))
                {
                    CreateDemo();
                }
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(66f)))
                {
                    RefreshCatalog();
                    RefreshProjection(true);
                }
                if (GUILayout.Button("Save Layout", EditorStyles.toolbarButton, GUILayout.Width(82f)))
                {
                    SaveLayout();
                }
                if (GUILayout.Button("Export", EditorStyles.toolbarButton, GUILayout.Width(58f)))
                {
                    ShowExportMenu();
                }
            }
        }

        private void DrawCatalogPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(270f)))
            {
                EditorGUILayout.LabelField("Career library", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                catalogSearch = EditorGUILayout.TextField("Search", catalogSearch);
                if (EditorGUI.EndChangeCheck()) Repaint();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Refresh")) RefreshCatalog();
                    if (GUILayout.Button("New")) CreateCareerAsset();
                }

                EditorGUILayout.LabelField(catalog.Count + " career asset" + (catalog.Count == 1 ? string.Empty : "s"), EditorStyles.miniLabel);
                catalogScroll = EditorGUILayout.BeginScrollView(catalogScroll, GUILayout.ExpandHeight(true));
                foreach (CareerDefinitionAsset asset in catalog.Where(MatchesCatalog).OrderBy(item => item.name, StringComparer.OrdinalIgnoreCase))
                {
                    bool active = asset == definitionAsset;
                    string label = asset.name;
                    try
                    {
                        CareerGraphDefinition source = asset.Definition();
                        label += "\n" + source.id + " · " + (source.content == null ? 0 : source.content.Length) + " nodes";
                    }
                    catch (Exception exception)
                    {
                        label += "\nInvalid: " + FirstLine(exception.Message);
                    }

                    Color oldColor = GUI.backgroundColor;
                    if (active) GUI.backgroundColor = new Color(0.22f, 0.48f, 0.72f);
                    if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.MinHeight(40f))) SetDefinition(asset);
                    GUI.backgroundColor = oldColor;
                }
                EditorGUILayout.EndScrollView();

                if (definitionAsset != null)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
                    EditorGUILayout.SelectableLabel(AssetDatabase.GetAssetPath(definitionAsset), EditorStyles.miniLabel, GUILayout.Height(28f));
                    if (GUILayout.Button("Select source asset"))
                    {
                        Selection.activeObject = definitionAsset;
                        EditorGUIUtility.PingObject(definitionAsset);
                    }
                    if (GUILayout.Button("Create / select layout sidecar")) EnsurePersistentLayout();
                    EditorGUILayout.LabelField(
                        layoutAsset != null && AssetDatabase.Contains(layoutAsset) ? "Layout: saved sidecar" : "Layout: transient",
                        EditorStyles.miniLabel);
                    if (projection != null)
                    {
                        EditorGUILayout.LabelField("Fingerprint", EditorStyles.miniLabel);
                        EditorGUILayout.SelectableLabel(projection.SourceFingerprint, EditorStyles.miniLabel, GUILayout.Height(28f));
                    }
                }
            }
        }

        private void DrawWelcome()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("Career Graph Visualizer", EditorStyles.largeLabel);
                EditorGUILayout.LabelField(
                    "Author, inspect and validate the progression graph behind the existing CareerGraph compiler. Requirements, typed references, external activity links, economy matches, sandbox facts, simulations and reports are all visible from one dockable workbench.",
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(10f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Create production demo", GUILayout.Height(34f))) CreateDemo();
                    if (GUILayout.Button("Create career asset", GUILayout.Height(34f))) CreateCareerAsset();
                    if (GUILayout.Button("Open guide", GUILayout.Height(34f))) OpenGuide();
                }
                EditorGUILayout.HelpBox(
                    "The graph canvas is editor-only. Saving positions, comments and bookmarks never changes career semantics or save data. The sandbox is synthetic and isolated; use the existing runtime settlement system for economy simulation.",
                    MessageType.Info);
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawWorkspaceToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                viewMode = (CareerGraphViewMode)EditorGUILayout.EnumPopup(viewMode, EditorStyles.toolbarPopup, GUILayout.Width(155f));
                nodeSearch = GUILayout.TextField(nodeSearch, EditorStyles.toolbarSearchField, GUILayout.MinWidth(160f));
                string[] tiers = TierOptions();
                int tierIndex = Array.IndexOf(tiers, tierFilter);
                tierIndex = EditorGUILayout.Popup(
                    tierIndex < 0 ? 0 : tierIndex,
                    tiers,
                    EditorStyles.toolbarPopup,
                    GUILayout.Width(118f));
                tierFilter = tiers[Mathf.Clamp(tierIndex, 0, tiers.Length - 1)];
                if (GUILayout.Button("Auto Layout", EditorStyles.toolbarButton, GUILayout.Width(82f))) ApplyAutoLayout();
                if (GUILayout.Button("Fit", EditorStyles.toolbarButton, GUILayout.Width(42f))) FitGraph();
                showExternal = GUILayout.Toggle(showExternal, "External", EditorStyles.toolbarButton, GUILayout.Width(70f));
                showMissing = GUILayout.Toggle(showMissing, "Missing", EditorStyles.toolbarButton, GUILayout.Width(66f));
                showFacts = GUILayout.Toggle(showFacts, "Facts", EditorStyles.toolbarButton, GUILayout.Width(54f));
                showGroups = GUILayout.Toggle(showGroups, "Tiers", EditorStyles.toolbarButton, GUILayout.Width(54f));
                showComments = GUILayout.Toggle(showComments, "Notes", EditorStyles.toolbarButton, GUILayout.Width(54f));
                if (GUILayout.Button("Bookmark", EditorStyles.toolbarButton, GUILayout.Width(68f))) AddBookmark();
                if (GUILayout.Button("Comment", EditorStyles.toolbarButton, GUILayout.Width(62f))) AddComment();
                GUILayout.FlexibleSpace();
                GUILayout.Label(projection == null ? string.Empty : projection.Nodes.Count + " nodes / " + projection.Edges.Count + " edges", EditorStyles.miniLabel);
            }
        }

        private void DrawGraphTab()
        {
            if (projection == null)
            {
                EditorGUILayout.HelpBox("No career projection is available.", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
                {
                    Rect canvas = GUILayoutUtility.GetRect(100f, 100f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                    graphCanvasSize = canvas.size;
                    DrawGraphCanvas(canvas);
                    EditorGUILayout.LabelField(
                        "Middle-drag pans · wheel zooms · drag nodes to place · right-click a node for actions. " +
                        (VisibleNodes().Count > MaxDrawNodes ? "Canvas is capped at " + MaxDrawNodes + " nodes; use search or dependency view to focus." : string.Empty),
                        EditorStyles.miniLabel);
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(315f), GUILayout.ExpandHeight(true)))
                {
                    graphToolsScroll = EditorGUILayout.BeginScrollView(graphToolsScroll);
                    DrawSelectedNodePanel();
                    EditorGUILayout.Space(8f);
                    DrawLayoutTools();
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void DrawGraphCanvas(Rect canvas)
        {
            if (layoutAsset == null)
            {
                return;
            }

            EditorGUI.DrawRect(canvas, new Color(0.055f, 0.065f, 0.075f, 1f));
            Event current = Event.current;
            Vector2 localMouse = current.mousePosition - canvas.position;
            List<CareerGraphNodeModel> visibleNodes = VisibleNodes();
            var visibleKeys = new HashSet<string>(visibleNodes.Select(node => node.Key), StringComparer.Ordinal);

            GUI.BeginGroup(canvas);
            DrawGrid(canvas.size, layoutAsset.Pan, layoutAsset.Zoom);
            if (showGroups)
            {
                DrawGraphGroups(visibleKeys);
            }
            if (showComments)
            {
                DrawGraphComments();
            }
            Handles.BeginGUI();
            foreach (CareerGraphEdgeModel edge in projection.Edges)
            {
                if (!visibleKeys.Contains(edge.FromKey) || !visibleKeys.Contains(edge.ToKey)) continue;
                CareerGraphNodeModel from = projection.FindNode(edge.FromKey);
                CareerGraphNodeModel to = projection.FindNode(edge.ToKey);
                if (from == null || to == null) continue;
                DrawEdge(NodeRect(from), NodeRect(to), edge);
            }
            Handles.EndGUI();

            foreach (CareerGraphNodeModel node in visibleNodes)
            {
                DrawNode(node, NodeRect(node));
            }
            GUI.EndGroup();

            if (!canvas.Contains(current.mousePosition))
            {
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 0)
            {
                CareerGraphNodeModel hit = HitNode(visibleNodes, localMouse);
                if (hit != null)
                {
                    SelectNode(hit.Key);
                    draggingNode = true;
                    draggingNodeKey = hit.Key;
                    dragOffset = hit.Position - (localMouse - layoutAsset.Pan) / layoutAsset.Zoom;
                    if (AssetDatabase.Contains(layoutAsset)) Undo.RecordObject(layoutAsset, "Move career graph node");
                    current.Use();
                    Repaint();
                    return;
                }

                CareerGraphCommentLayout comment = HitComment(localMouse);
                if (comment != null && showComments)
                {
                    selectedCommentId = comment.id;
                    draggingComment = true;
                    draggingCommentId = comment.id;
                    commentDragOffset = comment.rect.position - GraphPoint(localMouse);
                    UndoIfPersistent("Move career graph comment");
                    current.Use();
                    Repaint();
                    return;
                }
            }

            if (current.type == EventType.MouseDrag && current.button == 0 && draggingComment)
            {
                CareerGraphCommentLayout comment = layoutAsset.Comments.FirstOrDefault(item =>
                    item != null && string.Equals(item.id, draggingCommentId, StringComparison.Ordinal));
                if (comment != null)
                {
                    comment.rect.position = GraphPoint(localMouse) + commentDragOffset;
                    EditorUtility.SetDirty(layoutAsset);
                    current.Use();
                    Repaint();
                }
                return;
            }

            if (current.type == EventType.MouseDrag && current.button == 0 && draggingNode)
            {
                CareerGraphNodeModel node = projection.FindNode(draggingNodeKey);
                CareerGraphNodeLayout nodeLayout = layoutAsset.FindNode(draggingNodeKey);
                if (node != null && nodeLayout != null)
                {
                    Vector2 position = (localMouse - layoutAsset.Pan) / layoutAsset.Zoom + dragOffset;
                    node.Position = position;
                    nodeLayout.position = position;
                    EditorUtility.SetDirty(layoutAsset);
                    current.Use();
                    Repaint();
                }
                return;
            }

            if (current.type == EventType.MouseUp && current.button == 0 && draggingComment)
            {
                draggingComment = false;
                draggingCommentId = string.Empty;
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp && current.button == 0 && draggingNode)
            {
                draggingNode = false;
                draggingNodeKey = string.Empty;
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 2)
            {
                panning = true;
                lastCanvasMouse = localMouse;
                UndoIfPersistent("Pan career graph");
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDrag && current.button == 2 && panning)
            {
                layoutAsset.Pan += localMouse - lastCanvasMouse;
                lastCanvasMouse = localMouse;
                EditorUtility.SetDirty(layoutAsset);
                current.Use();
                Repaint();
                return;
            }

            if (current.type == EventType.MouseUp && current.button == 2 && panning)
            {
                panning = false;
                current.Use();
                return;
            }

            if (current.type == EventType.ScrollWheel)
            {
                float oldZoom = layoutAsset.Zoom;
                float newZoom = Mathf.Clamp(oldZoom * (current.delta.y < 0f ? 1.1f : 0.9f), 0.25f, 2.5f);
                Vector2 graphPoint = (localMouse - layoutAsset.Pan) / oldZoom;
                UndoIfPersistent("Zoom career graph");
                layoutAsset.Zoom = newZoom;
                layoutAsset.Pan = localMouse - graphPoint * newZoom;
                EditorUtility.SetDirty(layoutAsset);
                current.Use();
                Repaint();
                return;
            }

            if (current.type == EventType.ContextClick)
            {
                CareerGraphNodeModel hit = HitNode(visibleNodes, localMouse);
                if (hit != null)
                {
                    SelectNode(hit.Key);
                    ShowNodeMenu(hit);
                    current.Use();
                }
            }
        }

        private void DrawGrid(Vector2 size, Vector2 pan, float zoom)
        {
            Handles.BeginGUI();
            float spacing = GridSize * zoom;
            if (spacing < 12f) spacing = 12f;
            Handles.color = new Color(1f, 1f, 1f, 0.055f);
            float offsetX = Mathf.Repeat(pan.x, spacing);
            float offsetY = Mathf.Repeat(pan.y, spacing);
            for (float x = offsetX; x < size.x; x += spacing) Handles.DrawLine(new Vector3(x, 0f), new Vector3(x, size.y));
            for (float y = offsetY; y < size.y; y += spacing) Handles.DrawLine(new Vector3(0f, y), new Vector3(size.x, y));
            Handles.EndGUI();
        }

        private void DrawGraphGroups(HashSet<string> visibleKeys)
        {
            if (layoutAsset.Groups == null)
            {
                return;
            }

            foreach (CareerGraphGroupLayout group in layoutAsset.Groups)
            {
                if (group == null || !MatchesTier(group.title))
                {
                    continue;
                }

                bool hasVisibleNode = group.nodeKeys != null && group.nodeKeys.Any(visibleKeys.Contains);
                if (!group.collapsed && !hasVisibleNode)
                {
                    continue;
                }

                Rect rect = GraphRect(GroupBounds(group));
                EditorGUI.DrawRect(rect, group.color);
                Handles.BeginGUI();
                Handles.color = new Color(group.color.r + 0.16f, group.color.g + 0.16f, group.color.b + 0.16f, 0.65f);
                Handles.DrawAAPolyLine(2f,
                    new Vector3(rect.x, rect.y),
                    new Vector3(rect.xMax, rect.y),
                    new Vector3(rect.xMax, rect.yMax),
                    new Vector3(rect.x, rect.yMax),
                    new Vector3(rect.x, rect.y));
                Handles.EndGUI();

                string label = group.title + (group.collapsed ? "  ·  collapsed" : string.Empty);
                GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 20f), label, GroupLabelStyle());
            }
        }

        private void DrawGraphComments()
        {
            if (layoutAsset.Comments == null)
            {
                return;
            }

            foreach (CareerGraphCommentLayout comment in layoutAsset.Comments)
            {
                if (comment == null)
                {
                    continue;
                }

                Rect rect = GraphRect(comment.rect);
                EditorGUI.DrawRect(rect, comment.color);
                GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
                Color old = GUI.color;
                GUI.color = string.Equals(selectedCommentId, comment.id, StringComparison.Ordinal)
                    ? Color.white
                    : new Color(1f, 1f, 1f, 0.78f);
                GUI.Label(
                    new Rect(rect.x + 8f, rect.y + 8f, Mathf.Max(1f, rect.width - 16f), Mathf.Max(1f, rect.height - 16f)),
                    comment.text ?? string.Empty,
                    CommentLabelStyle());
                GUI.color = old;
            }
        }

        private CareerGraphCommentLayout HitComment(Vector2 localMouse)
        {
            if (!showComments || layoutAsset == null || layoutAsset.Comments == null)
            {
                return null;
            }

            for (int i = layoutAsset.Comments.Count - 1; i >= 0; i--)
            {
                CareerGraphCommentLayout comment = layoutAsset.Comments[i];
                if (comment != null && GraphRect(comment.rect).Contains(localMouse))
                {
                    return comment;
                }
            }
            return null;
        }

        private Rect GroupBounds(CareerGraphGroupLayout group)
        {
            if (group == null || projection == null || group.nodeKeys == null)
            {
                return group == null ? new Rect(40f, 40f, 640f, 240f) : group.rect;
            }

            List<CareerGraphNodeModel> nodes = group.nodeKeys
                .Select(projection.FindNode)
                .Where(node => node != null)
                .ToList();
            if (nodes.Count == 0)
            {
                return group.rect;
            }

            const float padding = 28f;
            float minX = nodes.Min(node => node.Position.x);
            float minY = nodes.Min(node => node.Position.y);
            float maxX = nodes.Max(node => node.Position.x + NodeWidth);
            float maxY = nodes.Max(node => node.Position.y + NodeHeight);
            return new Rect(
                minX - padding,
                minY - padding - 18f,
                Mathf.Max(260f, maxX - minX + padding * 2f),
                Mathf.Max(150f, maxY - minY + padding * 2f + 18f));
        }

        private Rect GraphRect(Rect graphRect)
        {
            return new Rect(
                layoutAsset.Pan.x + graphRect.x * layoutAsset.Zoom,
                layoutAsset.Pan.y + graphRect.y * layoutAsset.Zoom,
                Mathf.Max(1f, graphRect.width * layoutAsset.Zoom),
                Mathf.Max(1f, graphRect.height * layoutAsset.Zoom));
        }

        private Vector2 GraphPoint(Vector2 localMouse)
        {
            return (localMouse - layoutAsset.Pan) / layoutAsset.Zoom;
        }

        private GUIStyle GroupLabelStyle()
        {
            return new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.88f, 0.94f, 1f) },
                fontSize = Mathf.Clamp(Mathf.RoundToInt(11f * layoutAsset.Zoom), 9, 13)
            };
        }

        private GUIStyle CommentLabelStyle()
        {
            return new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                normal = { textColor = new Color(1f, 0.94f, 0.72f) },
                fontSize = Mathf.Clamp(Mathf.RoundToInt(11f * layoutAsset.Zoom), 9, 13)
            };
        }

        private void DrawEdge(Rect from, Rect to, CareerGraphEdgeModel edge)
        {
            Vector2 start = new Vector2(from.xMax, from.center.y);
            Vector2 end = new Vector2(to.xMin, to.center.y);
            if (to.center.x < from.center.x)
            {
                start = new Vector2(from.xMin, from.center.y);
                end = new Vector2(to.xMax, to.center.y);
            }

            Color color = EdgeColor(edge);
            Handles.color = color;
            if (edge.Negative || edge.Relation == CareerGraphRelationKind.PriceReference)
            {
                DrawDashedLine(start, end, color);
            }
            else
            {
                Handles.DrawAAPolyLine(2f, start, end);
            }

            Vector2 direction = (end - start).normalized;
            if (direction.sqrMagnitude > 0.01f)
            {
                Vector2 tip = end;
                Vector2 side = new Vector2(-direction.y, direction.x);
                Handles.DrawAAPolyLine(2f, tip, tip - direction * 8f + side * 4f);
                Handles.DrawAAPolyLine(2f, tip, tip - direction * 8f - side * 4f);
            }

            Vector2 labelPosition = Vector2.Lerp(start, end, 0.5f);
            GUI.Label(new Rect(labelPosition.x - 40f, labelPosition.y - 22f, 80f, 18f), edge.Relation.ToString(), EdgeLabelStyle());
        }

        private static void DrawDashedLine(Vector2 start, Vector2 end, Color color)
        {
            float distance = Vector2.Distance(start, end);
            if (distance < 1f) return;
            Vector2 direction = (end - start) / distance;
            const float dash = 8f;
            const float gap = 5f;
            for (float offset = 0f; offset < distance; offset += dash + gap)
            {
                float length = Mathf.Min(dash, distance - offset);
                Handles.color = color;
                Handles.DrawAAPolyLine(2f, start + direction * offset, start + direction * (offset + length));
            }
        }

        private void DrawNode(CareerGraphNodeModel node, Rect rect)
        {
            Color fill = NodeColor(node);
            EditorGUI.DrawRect(rect, fill);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 5f), NodeAccent(node));
            Handles.color = string.Equals(node.Key, selectedKey, StringComparison.Ordinal)
                ? Color.white
                : new Color(1f, 1f, 1f, 0.2f);
            Handles.DrawAAPolyLine(2f,
                new Vector3(rect.x, rect.y),
                new Vector3(rect.xMax, rect.y),
                new Vector3(rect.xMax, rect.yMax),
                new Vector3(rect.x, rect.yMax),
                new Vector3(rect.x, rect.y));

            GUIStyle title = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(12f * layoutAsset.Zoom), 9, 14)
            };
            GUIStyle detail = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.82f, 0.86f, 0.9f) },
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(10f * layoutAsset.Zoom), 8, 11)
            };
            float padding = 8f * layoutAsset.Zoom;
            GUI.Label(new Rect(rect.x + padding, rect.y + 9f * layoutAsset.Zoom, rect.width - padding * 2f, rect.height * 0.52f), node.Label, title);
            GUI.Label(new Rect(rect.x + padding, rect.y + rect.height * 0.61f, rect.width - padding * 2f, rect.height * 0.31f),
                node.DisplayKind + (node.Missing ? " · missing" : node.Teased ? " · teased" : string.Empty), detail);
        }

        private GUIStyle EdgeLabelStyle()
        {
            return new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.72f, 0.8f, 0.87f) },
                fontSize = 8
            };
        }

        private Rect NodeRect(CareerGraphNodeModel node)
        {
            Vector2 position = layoutAsset.Pan + node.Position * layoutAsset.Zoom;
            return new Rect(position.x, position.y, NodeWidth * layoutAsset.Zoom, NodeHeight * layoutAsset.Zoom);
        }

        private CareerGraphNodeModel HitNode(IReadOnlyList<CareerGraphNodeModel> nodes, Vector2 localMouse)
        {
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                Rect rect = NodeRect(nodes[i]);
                if (rect.Contains(localMouse)) return nodes[i];
            }
            return null;
        }

        private void DrawSelectedNodePanel()
        {
            CareerGraphNodeModel node = projection.FindNode(selectedKey);
            if (node == null)
            {
                EditorGUILayout.LabelField("No node selected", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Select a node on the canvas or from the Inspector tab.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(node.Label, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(node.DisplayKind + "  ·  " + node.TierLabel, EditorStyles.miniLabel);
            EditorGUILayout.SelectableLabel(node.Key, EditorStyles.miniLabel, GUILayout.Height(28f));
            if (node.Missing)
            {
                EditorGUILayout.HelpBox("This node is a projected missing reference. It is intentionally visible so broken progression links are not silently dropped.", MessageType.Error);
            }
            if (!string.IsNullOrEmpty(node.SourcePath)) EditorGUILayout.SelectableLabel(node.SourcePath, EditorStyles.miniLabel, GUILayout.Height(28f));

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Inspector")) activeTab = 1;
                if (GUILayout.Button("Validation")) activeTab = 2;
                if (GUILayout.Button("Pin / unpin")) TogglePinned(node);
            }
            EditorGUILayout.Space(4f);
            DrawNodeRequirementSummary(node, false);
            DrawDependencySummary(node);
            DrawExternalSummary(node);
        }

        private void DrawLayoutTools()
        {
            EditorGUILayout.LabelField("Canvas layout", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Tiers, bookmarks and notes are editor-only sidecar metadata. They never enter the career source fingerprint or runtime save data.",
                MessageType.Info);

            if (layoutAsset == null)
            {
                EditorGUILayout.HelpBox("No layout sidecar is available.", MessageType.Warning);
                return;
            }

            if (GUILayout.Button("Save layout sidecar")) SaveLayout();

            EditorGUILayout.LabelField("Tiers", EditorStyles.boldLabel);
            if (layoutAsset.Groups == null || layoutAsset.Groups.Count == 0)
            {
                EditorGUILayout.LabelField("No generated tier groups.", EditorStyles.miniLabel);
            }
            else
            {
                foreach (CareerGraphGroupLayout group in layoutAsset.Groups.ToArray())
                {
                    if (group == null || !MatchesTier(group.title))
                    {
                        continue;
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUI.BeginChangeCheck();
                        bool collapsed = EditorGUILayout.Toggle(group.collapsed, GUILayout.Width(18f));
                        if (EditorGUI.EndChangeCheck())
                        {
                            UndoIfPersistent("Toggle career graph tier");
                            group.collapsed = collapsed;
                            EditorUtility.SetDirty(layoutAsset);
                            Repaint();
                        }

                        GUILayout.Label(group.title + "  (" + (group.nodeKeys?.Count ?? 0) + ")", EditorStyles.miniLabel);
                        if (GUILayout.Button("Focus", GUILayout.Width(48f))) FocusGroup(group);
                    }
                }
            }

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Bookmarks", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Add", GUILayout.Width(48f))) AddBookmark();
            }
            if (layoutAsset.Bookmarks == null || layoutAsset.Bookmarks.Count == 0)
            {
                EditorGUILayout.LabelField("No bookmarks yet.", EditorStyles.miniLabel);
            }
            else
            {
                CareerGraphBookmarkLayout remove = null;
                foreach (CareerGraphBookmarkLayout bookmark in layoutAsset.Bookmarks)
                {
                    if (bookmark == null) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUI.BeginChangeCheck();
                        string title = EditorGUILayout.TextField(bookmark.title);
                        if (EditorGUI.EndChangeCheck())
                        {
                            UndoIfPersistent("Rename career graph bookmark");
                            bookmark.title = string.IsNullOrWhiteSpace(title) ? "Bookmark" : title;
                            EditorUtility.SetDirty(layoutAsset);
                        }
                        if (GUILayout.Button("Go", GUILayout.Width(32f))) ApplyBookmark(bookmark);
                        if (GUILayout.Button("X", GUILayout.Width(24f))) remove = bookmark;
                    }
                }

                if (remove != null)
                {
                    DeleteBookmark(remove);
                }
            }

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Notes", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Add", GUILayout.Width(48f))) AddComment();
            }
            if (layoutAsset.Comments == null || layoutAsset.Comments.Count == 0)
            {
                EditorGUILayout.LabelField("No notes yet. Click a note on the canvas to drag it.", EditorStyles.miniLabel);
            }
            else
            {
                CareerGraphCommentLayout remove = null;
                foreach (CareerGraphCommentLayout comment in layoutAsset.Comments)
                {
                    if (comment == null) continue;
                    bool selected = string.Equals(selectedCommentId, comment.id, StringComparison.Ordinal);
                    using (new EditorGUILayout.VerticalScope(selected ? EditorStyles.helpBox : GUIStyle.none))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(string.IsNullOrEmpty(comment.id) ? "Note" : comment.id, EditorStyles.miniLabel);
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("Focus", GUILayout.Width(48f)))
                            {
                                selectedCommentId = comment.id;
                                FocusLayoutRect(comment.rect);
                            }
                            if (GUILayout.Button("X", GUILayout.Width(24f))) remove = comment;
                        }

                        EditorGUI.BeginChangeCheck();
                        string note = EditorGUILayout.TextArea(comment.text ?? string.Empty, GUILayout.MinHeight(42f));
                        Color color = EditorGUILayout.ColorField("Color", comment.color);
                        if (EditorGUI.EndChangeCheck())
                        {
                            UndoIfPersistent("Edit career graph note");
                            comment.text = note;
                            comment.color = color;
                            EditorUtility.SetDirty(layoutAsset);
                        }
                    }
                }

                if (remove != null)
                {
                    DeleteComment(remove);
                }
            }
        }

        private void DrawInspectorTab()
        {
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            CareerGraphNodeModel node = projection.FindNode(selectedKey);
            if (node == null)
            {
                EditorGUILayout.HelpBox("Select a graph node first.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Node inspector", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Select source", GUILayout.Width(100f))) SelectSource(node);
            }
            EditorGUILayout.LabelField("Stable key", node.Key);
            EditorGUILayout.LabelField("Projected kind", node.DisplayKind);
            EditorGUILayout.LabelField("Tier", node.TierLabel);
            EditorGUILayout.LabelField("Source", string.IsNullOrEmpty(node.SourcePath) ? "Not persisted" : node.SourcePath);
            EditorGUILayout.Space(6f);

            if (node.IsContent)
            {
                DrawContentAuthoring(node);
            }
            else if (node.ProjectedKind == CareerGraphProjectedNodeKind.EventGroup && node.GroupIndex >= 0)
            {
                DrawGroupAuthoring(node);
            }
            else
            {
                DrawNodeRequirementSummary(node, true);
                DrawExternalDetails(node);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawContentAuthoring(CareerGraphNodeModel node)
        {
            CareerContentDefinition content = projection.Source.content != null && node.ContentIndex < projection.Source.content.Length
                ? projection.Source.content[node.ContentIndex]
                : null;
            if (content == null)
            {
                EditorGUILayout.HelpBox("The source entry is missing; use the validation tab to inspect the authoring error.", MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Authoritative career source", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("These fields edit the CareerDefinitionAsset through SerializedObject. Changes affect runtime compilation after the asset is saved.", MessageType.Info);
            SerializedObject serialized = new SerializedObject(definitionAsset);
            serialized.Update();
            SerializedProperty definition = serialized.FindProperty("definition");
            SerializedProperty contents = definition == null ? null : definition.FindPropertyRelative("content");
            SerializedProperty element = contents == null || node.ContentIndex < 0 || node.ContentIndex >= contents.arraySize
                ? null
                : contents.GetArrayElementAtIndex(node.ContentIndex);
            if (element == null)
            {
                EditorGUILayout.HelpBox("Could not locate the serialized content entry.", MessageType.Error);
                return;
            }

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(element.FindPropertyRelative("id"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("name"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("kind"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("tease"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("requirement"), true);
            if (EditorGUI.EndChangeCheck())
            {
                if (serialized.ApplyModifiedProperties())
                {
                    EditorUtility.SetDirty(definitionAsset);
                    RefreshProjection(true);
                    SetStatus("Career source edited. Recompile validation before saving the asset.", MessageType.Info);
                    return;
                }
            }
            else
            {
                serialized.ApplyModifiedProperties();
            }

            EditorGUILayout.Space(8f);
            DrawNodeRequirementSummary(node, true);
            DrawEconomyMatches(node.Id);
            DrawExternalReferences(node.Id);
        }

        private void DrawGroupAuthoring(CareerGraphNodeModel node)
        {
            EditorGUILayout.LabelField("Event-group authoring", EditorStyles.boldLabel);
            SerializedObject serialized = new SerializedObject(definitionAsset);
            serialized.Update();
            SerializedProperty definition = serialized.FindProperty("definition");
            SerializedProperty groups = definition == null ? null : definition.FindPropertyRelative("groups");
            SerializedProperty group = groups == null || node.GroupIndex < 0 || node.GroupIndex >= groups.arraySize
                ? null
                : groups.GetArrayElementAtIndex(node.GroupIndex);
            if (group == null)
            {
                EditorGUILayout.HelpBox("Could not locate the serialized event group.", MessageType.Error);
                return;
            }
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(group.FindPropertyRelative("id"));
            EditorGUILayout.PropertyField(group.FindPropertyRelative("eventIds"), true);
            if (EditorGUI.EndChangeCheck() && serialized.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(definitionAsset);
                RefreshProjection(true);
                SetStatus("Event group edited; typed group membership will be revalidated.", MessageType.Info);
                return;
            }
            serialized.ApplyModifiedProperties();
            DrawDependencySummary(node);
        }

        private void DrawValidationTab()
        {
            int errors = projection.Findings.Count(finding => finding.Severity == CareerGraphFindingSeverity.Error);
            int warnings = projection.Findings.Count(finding => finding.Severity == CareerGraphFindingSeverity.Warning);
            int unsupported = projection.Findings.Count(finding => finding.Severity == CareerGraphFindingSeverity.Unsupported);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Validation and static analysis", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label("Errors " + errors + "  Warnings " + warnings + "  Unsupported " + unsupported, EditorStyles.miniLabel);
            }
            EditorGUILayout.HelpBox(
                projection.IsCompiled
                    ? "CareerGraph compiled successfully. Static analysis is advisory and preserves conservative assumptions around NOT and external runtime ownership."
                    : "CareerGraph compilation failed. The projection remains available so the invalid source can be located and repaired.",
                projection.IsCompiled ? MessageType.Info : MessageType.Error);

            using (new EditorGUILayout.HorizontalScope())
            {
                findingSearch = EditorGUILayout.TextField("Filter", findingSearch);
                if (GUILayout.Button("Refresh", GUILayout.Width(70f))) RefreshProjection(true);
                if (GUILayout.Button("First error", GUILayout.Width(82f))) SelectFirstFinding(CareerGraphFindingSeverity.Error);
            }
            validationScroll = EditorGUILayout.BeginScrollView(validationScroll);
            IEnumerable<CareerGraphFinding> findings = projection.Findings.Where(MatchesFinding);
            foreach (CareerGraphFinding finding in findings)
            {
                DrawFinding(finding);
            }
            if (!findings.Any()) EditorGUILayout.HelpBox("No findings match the filter.", MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void DrawFinding(CareerGraphFinding finding)
        {
            Color old = GUI.color;
            GUI.color = FindingColor(finding.Severity);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUI.color = FindingColor(finding.Severity);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(finding.Severity + "  " + finding.Code, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (!string.IsNullOrEmpty(finding.NodeKey) && GUILayout.Button("Select", GUILayout.Width(58f))) SelectNode(finding.NodeKey);
                }
                GUI.color = Color.white;
                EditorGUILayout.LabelField(finding.Title, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(finding.Message, EditorStyles.wordWrappedLabel);
                if (finding.AssumptionDependent) EditorGUILayout.LabelField("Assumption-dependent: confirm against authored runtime behavior.", EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(finding.SourcePath)) EditorGUILayout.SelectableLabel(finding.SourcePath, EditorStyles.miniLabel, GUILayout.Height(24f));
            }
            GUI.color = old;
        }

        private void DrawSandboxTab()
        {
            sandboxScroll = EditorGUILayout.BeginScrollView(sandboxScroll);
            EditorGUILayout.LabelField("Synthetic career sandbox", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This sandbox is an isolated CareerProfileData copy for requirement explanations. It has no storage adapter, no live profile connection and no effect on save data. It exposes only the facts represented by the current runtime profile contract.",
                MessageType.Warning);
            if (sandbox == null)
            {
                EditorGUILayout.HelpBox("Sandbox is unavailable until the career definition compiles.", MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }

            CareerProfileData profile = sandbox.Profile;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Profile values", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    long cash = EditorGUILayout.LongField("Cash", profile.wallet.balance);
                    if (GUILayout.Button("Set", GUILayout.Width(48f))) ApplySandbox(sandbox.SetCash, cash);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    long reputation = EditorGUILayout.LongField("Reputation", profile.economy.reputation);
                    if (GUILayout.Button("Set", GUILayout.Width(48f))) ApplySandbox(sandbox.SetReputation, reputation);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    long bounty = EditorGUILayout.LongField("Bounty", profile.bounty.totalBounty);
                    if (GUILayout.Button("Set", GUILayout.Width(48f))) ApplySandbox(sandbox.SetBounty, bounty);
                }
                if (GUILayout.Button("Reset synthetic profile"))
                {
                    sandbox.Reset();
                    SetStatus("Synthetic sandbox reset; no saved profile was changed.", MessageType.Info);
                }
                EditorGUILayout.LabelField("Revision", sandbox.Revision.ToString());
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Arbitrary fact override", EditorStyles.boldLabel);
                overrideFact = (CareerFactKind)EditorGUILayout.EnumPopup("Fact", overrideFact);
                overrideSubject = EditorGUILayout.TextField("Subject ID", overrideSubject);
                overrideValue = EditorGUILayout.LongField("Value", overrideValue);
                if (GUILayout.Button("Set fact override"))
                {
                    ApplySandbox(sandbox.SetFact, overrideFact, overrideSubject, overrideValue);
                }
                EditorGUILayout.LabelField("Overrides", sandbox.Overrides.Count.ToString());
                foreach (KeyValuePair<string, long> pair in sandbox.Overrides.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    EditorGUILayout.LabelField(pair.Key, pair.Value.ToString(), EditorStyles.miniLabel);
                }
            }

            CareerGraphNodeModel selected = projection.FindNode(selectedKey);
            if (selected != null && selected.IsContent)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Selected node commands", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(selected.Label + "  ·  " + selected.Id, EditorStyles.miniLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Complete content")) ApplySandbox(sandbox.CompleteNode, selected);
                        if (GUILayout.Button("Grant ownership")) ApplySandbox(sandbox.GrantOwnership, selected);
                    }
                    DrawNodeRequirementSummary(selected, true);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawSimulationTab()
        {
            simulationScroll = EditorGUILayout.BeginScrollView(simulationScroll);
            EditorGUILayout.LabelField("Economy settlement simulation", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This uses the existing bounded EconomySimulation and EconomySettlement owners. It is useful for cash/pursuit/economy pacing, but it does not simulate race performance or invent career rewards that are not present in the runtime contract.",
                MessageType.Info);
            EditorGUI.BeginChangeCheck();
            economyAsset = (EconomyDefinitionAsset)EditorGUILayout.ObjectField("Economy catalog", economyAsset, typeof(EconomyDefinitionAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                simulationReport = null;
                simulationStatus = string.Empty;
            }

            if (economyAsset == null)
            {
                if (GUILayout.Button("Use first economy catalog")) economyAsset = FindEconomyAssets().FirstOrDefault();
                EditorGUILayout.HelpBox("Assign an EconomyDefinitionAsset with at least one simulation scenario.", MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            EconomySimulationScenario[] scenarios;
            try
            {
                scenarios = economyAsset.Scenarios() ?? Array.Empty<EconomySimulationScenario>();
            }
            catch (Exception exception)
            {
                scenarios = Array.Empty<EconomySimulationScenario>();
                simulationStatus = exception.Message;
            }

            if (scenarios.Length == 0)
            {
                EditorGUILayout.HelpBox("The selected economy catalog has no bounded simulation scenarios.", MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            simulationScenarioIndex = Mathf.Clamp(simulationScenarioIndex, 0, scenarios.Length - 1);
            string[] scenarioNames = scenarios.Select(scenario => string.IsNullOrEmpty(scenario.name) ? "Scenario" : scenario.name).ToArray();
            simulationScenarioIndex = EditorGUILayout.Popup("Scenario", simulationScenarioIndex, scenarioNames);
            EconomySimulationScenario selectedScenario = scenarios[simulationScenarioIndex];
            DrawScenarioSummary(selectedScenario);
            if (GUILayout.Button("Run bounded settlement simulation", GUILayout.Height(28f))) RunSimulation(selectedScenario);
            if (!string.IsNullOrEmpty(simulationStatus)) EditorGUILayout.HelpBox(simulationStatus, MessageType.None);
            if (simulationReport != null) DrawSimulationReport(simulationReport);
            EditorGUILayout.EndScrollView();
        }

        private void DrawScenarioSummary(EconomySimulationScenario scenario)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Scenario bounds", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Events", string.Join(", ", scenario.eventIds ?? Array.Empty<string>()));
                EditorGUILayout.LabelField("Desired vehicles", string.Join(", ", scenario.desiredVehicleIds ?? Array.Empty<string>()));
                EditorGUILayout.LabelField("Starting cash", scenario.startingCash.ToString());
                EditorGUILayout.LabelField("Attempts per event", scenario.attemptsPerEvent.ToString());
                EditorGUILayout.LabelField("Race / pursuit duration", scenario.raceDurationSeconds + "s / " + scenario.pursuitDurationSeconds + "s");
                EditorGUILayout.LabelField("Seed", scenario.seed.ToString());
            }
        }

        private void DrawSimulationReport(EconomySimulationReport report)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Attempts / wins", report.Attempts + " / " + report.Wins);
                EditorGUILayout.LabelField("Pursuits escaped / busted", report.Escapes + " / " + report.Busts);
                EditorGUILayout.LabelField("Income / spending / fines", report.Income + " / " + report.Spending + " / " + report.Fines);
                EditorGUILayout.LabelField("Final cash / elapsed", report.FinalCash + " / " + report.ElapsedSeconds + "s");
                EditorGUILayout.LabelField("Wallet samples", report.WalletTimeline.Count.ToString());
                if (report.AttemptBudgetExhausted) EditorGUILayout.HelpBox("At least one event exhausted its attempt budget.", MessageType.Warning);
                if (report.UnaffordableOrLocked.Count > 0) EditorGUILayout.LabelField("Unaffordable or locked", string.Join(", ", report.UnaffordableOrLocked));
            }
        }

        private void DrawImpactTab()
        {
            impactScroll = EditorGUILayout.BeginScrollView(impactScroll);
            EditorGUILayout.LabelField("Impact, comparison and ownership map", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Impact is derived from the projected graph. It distinguishes authored dependencies from external references and keeps unsupported reward/effect surfaces explicit.",
                MessageType.Info);

            CareerGraphNodeModel selected = projection.FindNode(selectedKey);
            if (selected != null)
            {
                DrawDependencySummary(selected);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                comparisonAsset = (CareerDefinitionAsset)EditorGUILayout.ObjectField("Compare career", comparisonAsset, typeof(CareerDefinitionAsset), false);
                if (GUILayout.Button("Compare current source"))
                {
                    comparison = comparisonAsset == null
                        ? null
                        : CareerGraphVisualizerModel.Compare(projection, CareerGraphVisualizerModel.Build(comparisonAsset));
                    SetStatus(comparison == null ? "Choose a second CareerDefinitionAsset first." : "Comparison built from both source fingerprints.", MessageType.Info);
                }
                DrawComparison(comparison);
            }

            EditorGUILayout.LabelField("External references", EditorStyles.boldLabel);
            for (int i = 0; i < projection.ExternalReferences.Count; i++)
            {
                CareerGraphExternalReference reference = projection.ExternalReferences[i];
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(reference.Kind + " · " + (string.IsNullOrEmpty(reference.Id) ? reference.Label : reference.Id), GUILayout.Width(280f));
                    GUILayout.Label(reference.Status, EditorStyles.miniLabel);
                    if (reference.Asset != null && GUILayout.Button("Ping", GUILayout.Width(48f)))
                    {
                        selectedExternalIndex = i;
                        Selection.activeObject = reference.Asset;
                        EditorGUIUtility.PingObject(reference.Asset);
                    }
                }
            }
            if (projection.ExternalReferences.Count == 0) EditorGUILayout.HelpBox("No external activity, mission or economy matches were found.", MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void DrawComparison(CareerGraphComparison value)
        {
            if (value == null)
            {
                EditorGUILayout.LabelField("No comparison loaded.", EditorStyles.miniLabel);
                return;
            }
            EditorGUILayout.LabelField("Current fingerprint", value.LeftFingerprint, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Compared fingerprint", value.RightFingerprint, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Added", value.Added.Count + (value.Added.Count == 0 ? string.Empty : " · " + string.Join(", ", value.Added)));
            EditorGUILayout.LabelField("Removed", value.Removed.Count + (value.Removed.Count == 0 ? string.Empty : " · " + string.Join(", ", value.Removed)));
            EditorGUILayout.LabelField("Changed requirements", value.ChangedRequirements.Count + (value.ChangedRequirements.Count == 0 ? string.Empty : " · " + string.Join(", ", value.ChangedRequirements)));
            EditorGUILayout.LabelField("Changed external links", value.ChangedExternalLinks.Count.ToString());
        }

        private void DrawNodeRequirementSummary(CareerGraphNodeModel node, bool expanded)
        {
            CareerGraphRequirementModel tree;
            if (!projection.RequirementsByNodeKey.TryGetValue(node.Key, out tree))
            {
                EditorGUILayout.LabelField("Requirement", "No authored requirement tree");
                return;
            }

            EditorGUILayout.LabelField("Requirement", EditorStyles.boldLabel);
            if (!expanded)
            {
                EditorGUILayout.LabelField(tree.Summary, EditorStyles.wordWrappedLabel);
                return;
            }
            CareerRequirementExplanation explanation = null;
            if (projection.IsCompiled && sandbox != null && node.IsContent)
            {
                try
                {
                    explanation = projection.Compiled.Get(node.Id).Requirement.Explain(sandbox.Facts);
                }
                catch (Exception exception)
                {
                    EditorGUILayout.HelpBox("Requirement explanation failed: " + exception.Message, MessageType.Warning);
                }
            }
            DrawRequirementTree(tree, explanation, 0);
        }

        private void DrawRequirementTree(CareerGraphRequirementModel requirement, CareerRequirementExplanation explanation, int depth)
        {
            if (requirement == null) return;
            string indent = new string(' ', Math.Min(depth, 8) * 2);
            string line = indent + requirement.Summary;
            if (requirement.Kind == CareerRequirementKind.Fact && explanation != null)
            {
                CareerFactKind fact = requirement.Fact;
                bool known = sandbox == null || sandbox.Facts.IsKnown(fact, requirement.SubjectId);
                line += "  · current " + explanation.Current + " / " + explanation.Required;
                line += known ? (explanation.Satisfied ? "  ✓" : "  ✗") : "  ? unknown";
            }
            EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            for (int i = 0; i < requirement.Children.Count; i++)
            {
                CareerRequirementExplanation childExplanation = explanation != null && i < explanation.Children.Count
                    ? explanation.Children[i]
                    : null;
                DrawRequirementTree(requirement.Children[i], childExplanation, depth + 1);
            }
        }

        private void DrawDependencySummary(CareerGraphNodeModel node)
        {
            List<CareerGraphEdgeModel> incoming = projection.Incoming(node.Key).ToList();
            List<CareerGraphEdgeModel> outgoing = projection.Outgoing(node.Key).ToList();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Dependencies", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Prerequisites / incoming", incoming.Count.ToString());
                foreach (CareerGraphEdgeModel edge in incoming.Take(8)) EditorGUILayout.LabelField("← " + NodeLabel(edge.FromKey) + "  (" + edge.Relation + ")", EditorStyles.miniLabel);
                if (incoming.Count > 8) EditorGUILayout.LabelField("… " + (incoming.Count - 8) + " more", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Dependents / outgoing", outgoing.Count.ToString());
                foreach (CareerGraphEdgeModel edge in outgoing.Take(8)) EditorGUILayout.LabelField("→ " + NodeLabel(edge.ToKey) + "  (" + edge.Relation + ")", EditorStyles.miniLabel);
                if (outgoing.Count > 8) EditorGUILayout.LabelField("… " + (outgoing.Count - 8) + " more", EditorStyles.miniLabel);
            }
        }

        private void DrawExternalSummary(CareerGraphNodeModel node)
        {
            if (!node.IsContent) return;
            int references = projection.ExternalReferences.Count(reference => string.Equals(reference.Id, node.Id, StringComparison.Ordinal));
            int economy = projection.EconomyMatches.Count(match => string.Equals(match.ItemId, node.Id, StringComparison.Ordinal));
            if (references == 0 && economy == 0) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("External ownership", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Matched definitions", references.ToString());
                EditorGUILayout.LabelField("Economy catalog matches", economy.ToString());
                if (economy > 0) DrawEconomyMatches(node.Id);
            }
        }

        private void DrawExternalDetails(CareerGraphNodeModel node)
        {
            foreach (CareerGraphExternalReference reference in projection.ExternalReferences.Where(item =>
                         string.Equals(item.Id, node.Id, StringComparison.Ordinal)))
            {
                EditorGUILayout.LabelField(reference.Kind, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(reference.Status, EditorStyles.wordWrappedLabel);
                if (!string.IsNullOrEmpty(reference.AssetPath)) EditorGUILayout.SelectableLabel(reference.AssetPath, EditorStyles.miniLabel, GUILayout.Height(26f));
            }
            DrawEconomyMatches(node.Id);
        }

        private void DrawEconomyMatches(string id)
        {
            foreach (CareerGraphEconomyMatch match in projection.EconomyMatches.Where(item => string.Equals(item.ItemId, id, StringComparison.Ordinal)))
            {
                EditorGUILayout.LabelField("Economy match · " + match.Kind, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Price", match.Price.ToString());
                EditorGUILayout.LabelField("Initially unlocked", match.InitiallyUnlocked ? "yes" : "no");
                EditorGUILayout.LabelField("Catalog", match.AssetPath, EditorStyles.miniLabel);
            }
        }

        private void DrawExternalReferences(string id)
        {
            foreach (CareerGraphExternalReference reference in projection.ExternalReferences.Where(item => string.Equals(item.Id, id, StringComparison.Ordinal)))
            {
                EditorGUILayout.LabelField(reference.Kind + " · " + reference.Status, EditorStyles.miniLabel);
            }
        }

        private void CreateDemo()
        {
            if (!CareerGraphVisualizerCommands.CreateDemoCareer(out CareerDefinitionAsset asset, out string failure))
            {
                SetStatus("Demo creation failed: " + failure, MessageType.Error);
                return;
            }
            RefreshCatalog();
            SetDefinition(asset);
            SetStatus("Created a typed career demo at " + AssetDatabase.GetAssetPath(asset) + ".", MessageType.Info);
        }

        private void CreateCareerAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create career definition",
                "CareerDefinition",
                "asset",
                "Choose a project path for the new CareerDefinitionAsset.");
            if (string.IsNullOrEmpty(path)) return;
            CareerDefinitionAsset asset = CreateInstance<CareerDefinitionAsset>();
            try
            {
                asset.Configure(new CareerGraphDefinition
                {
                    id = "career.new",
                    version = 1,
                    content = new[]
                    {
                        new CareerContentDefinition
                        {
                            id = "event.new.start",
                            name = "New event",
                            kind = CareerContentKind.Event,
                            requirement = new CareerRequirementDefinition { kind = CareerRequirementKind.All }
                        }
                    },
                    groups = Array.Empty<CareerEventGroupDefinition>()
                });
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                RefreshCatalog();
                SetDefinition(asset);
                Selection.activeObject = asset;
            }
            catch (Exception exception)
            {
                DestroyImmediate(asset);
                SetStatus("Career asset creation failed: " + exception.Message, MessageType.Error);
            }
        }

        private void SaveLayout()
        {
            if (projection == null || layoutAsset == null)
            {
                SetStatus("Select a career before saving a layout.", MessageType.Warning);
                return;
            }
            if (!CareerGraphVisualizerCommands.SaveLayout(projection, layoutAsset, out string failure))
            {
                SetStatus(failure, MessageType.Warning);
                return;
            }
            SetStatus("Saved editor-only layout sidecar. Career source was not changed.", MessageType.Info);
        }

        private void EnsurePersistentLayout()
        {
            SaveLayout();
            if (layoutAsset != null && AssetDatabase.Contains(layoutAsset))
            {
                layoutAsset = AssetDatabase.LoadAssetAtPath<CareerGraphLayoutAsset>(CareerGraphVisualizerCommands.LayoutPath(definitionAsset));
                CareerGraphVisualizerCommands.SyncLayout(projection, layoutAsset);
            }
        }

        private void ApplyAutoLayout()
        {
            if (projection == null || layoutAsset == null) return;
            CareerGraphVisualizerCommands.ApplyAutoLayout(projection, layoutAsset);
            SetStatus("Auto-layout applied to unpinned nodes. Save the layout to persist it.", MessageType.Info);
            Repaint();
        }

        private void AddBookmark()
        {
            if (layoutAsset == null)
            {
                return;
            }

            UndoIfPersistent("Add career graph bookmark");
            int number = layoutAsset.Bookmarks.Count + 1;
            string id;
            do
            {
                id = "bookmark:" + number++;
            }
            while (layoutAsset.Bookmarks.Any(bookmark => bookmark != null && bookmark.id == id));

            layoutAsset.Bookmarks.Add(new CareerGraphBookmarkLayout
            {
                id = id,
                title = "Bookmark " + (number - 1),
                pan = layoutAsset.Pan,
                zoom = layoutAsset.Zoom,
                nodeKeys = string.IsNullOrEmpty(selectedKey)
                    ? new List<string>()
                    : new List<string> { selectedKey }
            });
            EditorUtility.SetDirty(layoutAsset);
            SetStatus("Added an editor-only graph bookmark. Save the layout to persist it.", MessageType.Info);
            Repaint();
        }

        private void ApplyBookmark(CareerGraphBookmarkLayout bookmark)
        {
            if (bookmark == null || layoutAsset == null)
            {
                return;
            }

            UndoIfPersistent("Apply career graph bookmark");
            layoutAsset.Pan = bookmark.pan;
            layoutAsset.Zoom = bookmark.zoom;
            string nodeKey = bookmark.nodeKeys == null
                ? string.Empty
                : projection == null
                    ? string.Empty
                    : bookmark.nodeKeys.FirstOrDefault(key => projection.FindNode(key) != null);
            if (!string.IsNullOrEmpty(nodeKey))
            {
                selectedKey = nodeKey;
            }
            EditorUtility.SetDirty(layoutAsset);
            Repaint();
        }

        private void DeleteBookmark(CareerGraphBookmarkLayout bookmark)
        {
            if (bookmark == null || layoutAsset == null)
            {
                return;
            }

            UndoIfPersistent("Delete career graph bookmark");
            layoutAsset.Bookmarks.Remove(bookmark);
            EditorUtility.SetDirty(layoutAsset);
            Repaint();
        }

        private void AddComment()
        {
            if (layoutAsset == null)
            {
                return;
            }

            UndoIfPersistent("Add career graph note");
            CareerGraphNodeModel selected = projection == null ? null : projection.FindNode(selectedKey);
            Vector2 position = selected == null
                ? new Vector2(120f, 120f)
                : selected.Position + new Vector2(24f, NodeHeight + 24f);
            var comment = new CareerGraphCommentLayout
            {
                id = "comment:" + Guid.NewGuid().ToString("N"),
                text = "New progression note",
                rect = new Rect(position.x, position.y, 300f, 110f)
            };
            layoutAsset.Comments.Add(comment);
            selectedCommentId = comment.id;
            EditorUtility.SetDirty(layoutAsset);
            SetStatus("Added an editor-only note. Edit it in the layout panel and save the sidecar to persist it.", MessageType.Info);
            Repaint();
        }

        private void DeleteComment(CareerGraphCommentLayout comment)
        {
            if (comment == null || layoutAsset == null)
            {
                return;
            }

            UndoIfPersistent("Delete career graph note");
            layoutAsset.Comments.Remove(comment);
            if (string.Equals(selectedCommentId, comment.id, StringComparison.Ordinal))
            {
                selectedCommentId = string.Empty;
            }
            EditorUtility.SetDirty(layoutAsset);
            Repaint();
        }

        private void FocusGroup(CareerGraphGroupLayout group)
        {
            if (group == null)
            {
                return;
            }

            FocusLayoutRect(GroupBounds(group));
        }

        private void FocusLayoutRect(Rect graphRect)
        {
            if (layoutAsset == null)
            {
                return;
            }

            UndoIfPersistent("Focus career graph layout");
            float width = Mathf.Max(1f, graphCanvasSize.x - 80f);
            float height = Mathf.Max(1f, graphCanvasSize.y - 80f);
            float desiredZoom = Mathf.Min(
                width / Mathf.Max(1f, graphRect.width),
                height / Mathf.Max(1f, graphRect.height));
            layoutAsset.Zoom = Mathf.Clamp(desiredZoom, 0.25f, 1.6f);
            layoutAsset.Pan = new Vector2(
                40f + (width - graphRect.width * layoutAsset.Zoom) * 0.5f - graphRect.x * layoutAsset.Zoom,
                40f + (height - graphRect.height * layoutAsset.Zoom) * 0.5f - graphRect.y * layoutAsset.Zoom);
            EditorUtility.SetDirty(layoutAsset);
            Repaint();
        }

        private void FitGraph()
        {
            if (layoutAsset == null) return;
            List<CareerGraphNodeModel> nodes = VisibleNodes();
            if (nodes.Count == 0) return;
            float minX = nodes.Min(node => node.Position.x);
            float maxX = nodes.Max(node => node.Position.x + NodeWidth);
            float minY = nodes.Min(node => node.Position.y);
            float maxY = nodes.Max(node => node.Position.y + NodeHeight);
            float width = Mathf.Max(1f, graphCanvasSize.x - 80f);
            float height = Mathf.Max(1f, graphCanvasSize.y - 80f);
            layoutAsset.Zoom = Mathf.Clamp(Mathf.Min(width / Mathf.Max(1f, maxX - minX), height / Mathf.Max(1f, maxY - minY)), 0.25f, 1.6f);
            layoutAsset.Pan = new Vector2(
                40f + (width - (maxX - minX) * layoutAsset.Zoom) * 0.5f - minX * layoutAsset.Zoom,
                40f + (height - (maxY - minY) * layoutAsset.Zoom) * 0.5f - minY * layoutAsset.Zoom);
            EditorUtility.SetDirty(layoutAsset);
            Repaint();
        }

        private void ShowExportMenu()
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Export Markdown report"), false, ExportMarkdown);
            menu.AddItem(new GUIContent("Export JSON report"), false, ExportJson);
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Open visualizer guide"), false, OpenGuide);
            menu.ShowAsContext();
        }

        private void ExportMarkdown()
        {
            Export("md", "CareerGraphReport", path => CareerGraphVisualizerCommands.BuildMarkdown(projection, sandbox, selectedKey, SimulationSummary(), comparison));
        }

        private void ExportJson()
        {
            Export("json", "CareerGraphReport", path => CareerGraphVisualizerCommands.BuildJson(projection, sandbox, selectedKey, SimulationSummary(), comparison));
        }

        private void Export(string extension, string defaultName, Func<string, string> builder)
        {
            if (projection == null)
            {
                SetStatus("Select a career before exporting.", MessageType.Warning);
                return;
            }
            string path = EditorUtility.SaveFilePanelInProject("Export career graph report", defaultName, extension, "Choose a project path for the report.");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                File.WriteAllText(Path.GetFullPath(path), builder(path));
                AssetDatabase.Refresh();
                SetStatus("Exported report to " + path + ".", MessageType.Info);
            }
            catch (Exception exception)
            {
                SetStatus("Report export failed: " + exception.Message, MessageType.Error);
            }
        }

        private string SimulationSummary()
        {
            if (simulationReport == null) return simulationStatus;
            return "Attempts=" + simulationReport.Attempts
                + ", Wins=" + simulationReport.Wins
                + ", Escapes=" + simulationReport.Escapes
                + ", Busts=" + simulationReport.Busts
                + ", Income=" + simulationReport.Income
                + ", Spending=" + simulationReport.Spending
                + ", Fines=" + simulationReport.Fines
                + ", FinalCash=" + simulationReport.FinalCash;
        }

        private void RunSimulation(EconomySimulationScenario scenario)
        {
            try
            {
                simulationReport = EconomySimulation.Run(economyAsset.Definition(), scenario);
                simulationStatus = "Simulation completed against the existing settlement engine.";
                SetStatus(simulationStatus, MessageType.Info);
            }
            catch (Exception exception)
            {
                simulationReport = null;
                simulationStatus = "Simulation failed: " + exception.Message;
                SetStatus(simulationStatus, MessageType.Error);
            }
        }

        private void ShowNodeMenu(CareerGraphNodeModel node)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Select node"), true, () => SelectNode(node.Key));
            menu.AddItem(new GUIContent(node.IsContent ? "Select career source" : "Select external source"), false, () => SelectSource(node));
            CareerGraphNodeLayout layout = layoutAsset.FindNode(node.Key);
            bool pinned = layout != null && layout.pinned;
            menu.AddItem(new GUIContent(pinned ? "Unpin node" : "Pin node"), pinned, () => TogglePinned(node));
            menu.AddItem(new GUIContent("Focus dependencies"), false, () => { viewMode = CareerGraphViewMode.ForwardDependencies; Repaint(); });
            menu.AddItem(new GUIContent("Focus dependents"), false, () => { viewMode = CareerGraphViewMode.ReverseDependencies; Repaint(); });
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Add bookmark here"), false, () => AddBookmark());
            menu.AddItem(new GUIContent("Add note here"), false, () => AddComment());
            menu.ShowAsContext();
        }

        private void TogglePinned(CareerGraphNodeModel node)
        {
            if (layoutAsset == null) return;
            CareerGraphNodeLayout layout = layoutAsset.EnsureNode(node.Key, node.DefaultPosition);
            UndoIfPersistent("Toggle career graph pin");
            layout.pinned = !layout.pinned;
            EditorUtility.SetDirty(layoutAsset);
            Repaint();
        }

        private void SelectNode(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            selectedKey = key;
            CareerGraphNodeModel node = projection == null ? null : projection.FindNode(key);
            if (node != null) Selection.activeObject = definitionAsset;
            Repaint();
        }

        private void SelectSource(CareerGraphNodeModel node)
        {
            if (node == null) return;
            UnityEngine.Object source = null;
            if (node.IsContent) source = definitionAsset;
            else source = projection.ExternalReferences.FirstOrDefault(reference => string.Equals(reference.Id, node.Id, StringComparison.Ordinal))?.Asset;
            if (source != null)
            {
                Selection.activeObject = source;
                EditorGUIUtility.PingObject(source);
                SetStatus("Selected source object for " + node.Label + ".", MessageType.Info);
            }
            else
            {
                SetStatus("No persisted source object is attached to this projected node.", MessageType.Warning);
            }
        }

        private List<CareerGraphNodeModel> VisibleNodes()
        {
            if (projection == null) return new List<CareerGraphNodeModel>();
            IEnumerable<CareerGraphNodeModel> query = projection.Nodes;
            if (!showExternal) query = query.Where(node => !node.IsExternal);
            if (!showMissing) query = query.Where(node => !node.Missing);
            if (!showFacts) query = query.Where(node => node.ProjectedKind != CareerGraphProjectedNodeKind.Fact);
            if (!string.IsNullOrEmpty(tierFilter) && tierFilter != "All")
            {
                query = query.Where(node => string.Equals(node.TierLabel, tierFilter, StringComparison.Ordinal));
            }
            query = query.Where(node => !IsHiddenByCollapsedTier(node));
            if (!string.IsNullOrWhiteSpace(nodeSearch)) query = query.Where(MatchesNode);
            if (viewMode != CareerGraphViewMode.Overview && !string.IsNullOrEmpty(selectedKey))
            {
                var related = new HashSet<string>(StringComparer.Ordinal) { selectedKey };
                var frontier = new Queue<string>();
                frontier.Enqueue(selectedKey);
                while (frontier.Count > 0)
                {
                    string key = frontier.Dequeue();
                    IEnumerable<CareerGraphEdgeModel> edges = viewMode == CareerGraphViewMode.ForwardDependencies
                        ? projection.Outgoing(key)
                        : projection.Incoming(key);
                    foreach (CareerGraphEdgeModel edge in edges)
                    {
                        string next = viewMode == CareerGraphViewMode.ForwardDependencies ? edge.ToKey : edge.FromKey;
                        if (related.Add(next)) frontier.Enqueue(next);
                    }
                }
                query = query.Where(node => related.Contains(node.Key));
            }
            return query.OrderBy(node => node.Position.y).ThenBy(node => node.Position.x).Take(MaxDrawNodes + 1).ToList();
        }

        private bool IsHiddenByCollapsedTier(CareerGraphNodeModel node)
        {
            if (node == null || layoutAsset == null || layoutAsset.Groups == null)
            {
                return false;
            }

            return layoutAsset.Groups.Any(group => group != null
                && group.collapsed
                && group.nodeKeys != null
                && group.nodeKeys.Contains(node.Key));
        }

        private string[] TierOptions()
        {
            var options = new List<string> { "All" };
            if (projection != null)
            {
                options.AddRange(projection.Nodes
                    .Select(node => node.TierLabel)
                    .Where(label => !string.IsNullOrEmpty(label))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(label => label, StringComparer.Ordinal));
            }
            return options.ToArray();
        }

        private bool MatchesTier(string tier)
        {
            return string.IsNullOrEmpty(tierFilter)
                || tierFilter == "All"
                || string.Equals(tierFilter, tier, StringComparison.Ordinal);
        }

        private bool MatchesNode(CareerGraphNodeModel node)
        {
            string text = node.Key + " " + node.Id + " " + node.Label + " " + node.DisplayKind + " " + node.TierLabel;
            return text.IndexOf(nodeSearch, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool MatchesFinding(CareerGraphFinding finding)
        {
            if (string.IsNullOrWhiteSpace(findingSearch)) return true;
            string text = finding.Code + " " + finding.Title + " " + finding.Message + " " + finding.SourceId;
            return text.IndexOf(findingSearch, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool MatchesCatalog(CareerDefinitionAsset asset)
        {
            if (asset == null || string.IsNullOrWhiteSpace(catalogSearch)) return asset != null;
            string text = asset.name + " " + AssetDatabase.GetAssetPath(asset);
            try { text += " " + asset.Definition().id; } catch { }
            return text.IndexOf(catalogSearch, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RefreshCatalog()
        {
            catalog.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:CareerDefinitionAsset"))
            {
                CareerDefinitionAsset asset = AssetDatabase.LoadAssetAtPath<CareerDefinitionAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) catalog.Add(asset);
            }
            Repaint();
        }

        private void SetDefinition(CareerDefinitionAsset asset)
        {
            if (asset == definitionAsset && projection != null)
            {
                return;
            }
            DisposeTransientLayout();
            definitionAsset = asset;
            comparison = null;
            simulationReport = null;
            selectedKey = string.Empty;
            if (definitionAsset == null)
            {
                projection = null;
                sandbox = null;
                SetStatus("Select a CareerDefinitionAsset or create the demo career.", MessageType.Info);
                Repaint();
                return;
            }
            projection = CareerGraphVisualizerModel.Build(definitionAsset);
            layoutAsset = CareerGraphVisualizerCommands.LoadLayout(projection, out _);
            sandbox = projection.IsCompiled ? new CareerGraphSandbox(projection.Compiled) : null;
            SelectFirstNode();
            SetStatus("Projection loaded from the authoritative career source.", projection.IsCompiled ? MessageType.Info : MessageType.Error);
            Repaint();
        }

        private void RefreshProjection(bool preserveLayout)
        {
            if (definitionAsset == null)
            {
                projection = null;
                sandbox = null;
                return;
            }
            string previousKey = selectedKey;
            projection = CareerGraphVisualizerModel.Build(definitionAsset);
            if (layoutAsset == null || !preserveLayout)
            {
                DisposeTransientLayout();
                layoutAsset = CareerGraphVisualizerCommands.LoadLayout(projection, out _);
            }
            else
            {
                CareerGraphVisualizerCommands.SyncLayout(projection, layoutAsset);
            }
            sandbox = projection.IsCompiled ? new CareerGraphSandbox(projection.Compiled) : null;
            selectedKey = projection.FindNode(previousKey) != null ? previousKey : string.Empty;
            if (string.IsNullOrEmpty(selectedKey)) SelectFirstNode();
            Repaint();
        }

        private void SelectFirstNode()
        {
            if (projection == null) return;
            CareerGraphNodeModel first = projection.Nodes.FirstOrDefault(node => node.IsContent)
                ?? projection.Nodes.FirstOrDefault();
            selectedKey = first == null ? string.Empty : first.Key;
        }

        private void DisposeTransientLayout()
        {
            if (layoutAsset != null && !AssetDatabase.Contains(layoutAsset)) DestroyImmediate(layoutAsset);
            layoutAsset = null;
        }

        private void EnsureSelectedNodeForAsset()
        {
            if (projection != null && projection.FindNode(selectedKey) == null) SelectFirstNode();
        }

        private void SelectFirstFinding(CareerGraphFindingSeverity severity)
        {
            CareerGraphFinding finding = projection.Findings.FirstOrDefault(item => item.Severity == severity);
            if (finding == null)
            {
                SetStatus("No " + severity + " finding exists.", MessageType.Info);
                return;
            }
            if (!string.IsNullOrEmpty(finding.NodeKey)) SelectNode(finding.NodeKey);
            activeTab = 2;
        }

        private void ApplySandbox(LongSandboxCommand action, long value)
        {
            if (action == null) return;
            if (!action(value, out string failure)) SetStatus(failure, MessageType.Warning);
            else SetStatus("Synthetic sandbox changed; no save data was touched.", MessageType.Info);
            Repaint();
        }

        private void ApplySandbox(FactSandboxCommand action, CareerFactKind fact, string subject, long value)
        {
            if (action == null) return;
            if (!action(fact, subject, value, out string failure)) SetStatus(failure, MessageType.Warning);
            else SetStatus("Synthetic fact override applied.", MessageType.Info);
            Repaint();
        }

        private void ApplySandbox(NodeSandboxCommand action, CareerGraphNodeModel node)
        {
            if (action == null) return;
            if (!action(node, out string failure)) SetStatus(failure, MessageType.Warning);
            else SetStatus("Synthetic sandbox command applied; no save data was touched.", MessageType.Info);
            Repaint();
        }

        private void UndoIfPersistent(string label)
        {
            if (layoutAsset != null && AssetDatabase.Contains(layoutAsset)) Undo.RecordObject(layoutAsset, label);
        }

        private void SetStatus(string message, MessageType type)
        {
            status = message ?? string.Empty;
            statusType = type;
            Repaint();
        }

        private static string NodeLabel(string key)
        {
            return string.IsNullOrEmpty(key) ? "<unknown>" : key;
        }

        private static string FirstLine(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            int index = value.IndexOf('\n');
            return index < 0 ? value : value.Substring(0, index);
        }

        private static Color NodeColor(CareerGraphNodeModel node)
        {
            if (node.Missing) return new Color(0.42f, 0.12f, 0.1f, 0.95f);
            if (node.ProjectedKind == CareerGraphProjectedNodeKind.Fact) return new Color(0.12f, 0.27f, 0.34f, 0.95f);
            if (node.IsExternal) return new Color(0.24f, 0.2f, 0.34f, 0.95f);
            switch (node.ContentKind)
            {
                case CareerContentKind.Event: return new Color(0.12f, 0.32f, 0.25f, 0.95f);
                case CareerContentKind.Rival: return new Color(0.42f, 0.22f, 0.12f, 0.95f);
                case CareerContentKind.Vehicle: return new Color(0.25f, 0.28f, 0.14f, 0.95f);
                case CareerContentKind.Upgrade: return new Color(0.2f, 0.22f, 0.34f, 0.95f);
                default: return new Color(0.2f, 0.22f, 0.25f, 0.95f);
            }
        }

        private static Color NodeAccent(CareerGraphNodeModel node)
        {
            if (node.Missing) return new Color(0.95f, 0.22f, 0.18f);
            if (node.IsExternal) return new Color(0.75f, 0.5f, 1f);
            if (node.ProjectedKind == CareerGraphProjectedNodeKind.Fact) return new Color(0.2f, 0.75f, 0.85f);
            return node.Teased ? new Color(0.95f, 0.7f, 0.18f) : new Color(0.35f, 0.8f, 0.45f);
        }

        private static Color EdgeColor(CareerGraphEdgeModel edge)
        {
            if (edge.Negative || edge.Relation == CareerGraphRelationKind.RequiresNot) return new Color(0.95f, 0.35f, 0.28f, 0.85f);
            if (edge.Relation == CareerGraphRelationKind.PriceReference) return new Color(0.85f, 0.65f, 0.28f, 0.75f);
            if (edge.Relation == CareerGraphRelationKind.AuthoringReference) return new Color(0.7f, 0.5f, 1f, 0.75f);
            if (edge.Relation == CareerGraphRelationKind.GroupMember) return new Color(0.35f, 0.75f, 0.85f, 0.75f);
            return new Color(0.55f, 0.85f, 0.62f, 0.85f);
        }

        private static Color FindingColor(CareerGraphFindingSeverity severity)
        {
            switch (severity)
            {
                case CareerGraphFindingSeverity.Error: return new Color(1f, 0.36f, 0.32f);
                case CareerGraphFindingSeverity.Warning: return new Color(1f, 0.72f, 0.28f);
                case CareerGraphFindingSeverity.Unsupported: return new Color(0.75f, 0.58f, 1f);
                default: return new Color(0.45f, 0.82f, 0.9f);
            }
        }

        private static List<EconomyDefinitionAsset> FindEconomyAssets()
        {
            return AssetDatabase.FindAssets("t:EconomyDefinitionAsset")
                .Select(guid => AssetDatabase.LoadAssetAtPath<EconomyDefinitionAsset>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(asset => asset != null)
                .ToList();
        }

        private static void OpenGuide()
        {
            string path = Path.GetFullPath("Assets/NfsMw/Modules/Driving/CAREER_GRAPH_VISUALIZER.md");
            if (File.Exists(path)) Application.OpenURL(new Uri(path).AbsoluteUri);
            else Debug.LogWarning("Career graph visualizer guide is missing: " + path);
        }
    }
}
