using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.UIElements;
using NfsMwRemaster.Driving.Editor.Workspace;

namespace NfsMwRemaster.Driving.Editor
{
    public sealed class EventPlacementWindow : RacingFocusedWindow
    {
        protected override string ModuleId => "event-placement";
        public static WorldActivityDefinition Brush => EventPlacementView.Brush;
        [MenuItem("Tools/NFS MW Remaster/Event Placement Studio")]
        public static void Open() => GetWindow<EventPlacementWindow>("Event Placement");
    }
    public sealed class EventPlacementView : RacingModuleView, IRacingViewState
    {
        private string search = "", status = "Choose a definition, then place it in the Scene view.";
        private WorldActivityDefinition palette;
        private int tab;
        private Vector2 scroll;
        private readonly List<WorldActivityDefinition> definitions = new List<WorldActivityDefinition>();
        private ActivityPlan plan;
        private EventPlacementSource inspected;
        private CareerFactKind previewKind;
        private string previewSubject = "";
        private long previewValue;
        private readonly ActivityPreviewFacts facts = new ActivityPreviewFacts();
        private Vector3 batchOffset;
        private RoadNetworkAsset scatterNetwork;
        private int scatterLane, scatterCount = 5;
        private float scatterStart, scatterSpacing = 50;
        private readonly List<Vector3> candidates = new List<Vector3>();
        private bool scatterPreview;
        private string scatterRevision;
        private double lastValidationMilliseconds;
        private RacingVehicleSetup previewVehicle;
        public static WorldActivityDefinition Brush { get; private set; }
        private static EventPlacementView brushOwner;
        private RacingPreviewLease brushLease;
        private void ActivateBrush()
        {
            if(!palette)throw new InvalidOperationException("Choose an activity definition first.");
            if(brushOwner==this)StopBrush();
            brushLease=RacingPreviewSessions.Acquire("scene-placement-brush","Event Placement",StopBrush);
            try{ToolManager.SetActiveTool<ActivityPlacementTool>();brushOwner=this;Brush=palette;}
            catch{brushLease.Dispose();brushLease=null;throw;}
            status="Click a collision surface to place. Escape cancels the brush.";
        }
        private void StopBrush()
        {
            if(brushOwner==this){Brush=null;brushOwner=null;}
            brushLease?.Dispose();brushLease=null;
        }
        public static void StopActiveBrush()=>brushOwner?.StopBrush();

        private bool pinned, disposed;
        [Serializable] private sealed class ViewState{public string search;public int tab;public Vector2 scroll;}
        public string CaptureViewState()=>JsonUtility.ToJson(new ViewState{search=search,tab=tab,scroll=scroll});
        public void RestoreViewState(string json){if(string.IsNullOrEmpty(json))return;var state=JsonUtility.FromJson<ViewState>(json);search=state.search??"";tab=Mathf.Clamp(state.tab,0,6);scroll=state.scroll;}
        public EventPlacementView()
        {
            CreateGUI(); RefreshCatalog(); SelectionChanged();
            Undo.undoRedoPerformed += Invalidate;
            EditorApplication.projectChanged += RefreshCatalog;
            EditorApplication.hierarchyChanged += Invalidate;
            SceneView.duringSceneGui += DrawScatter;
            EditorApplication.playModeStateChanged += PlayChanged;
        }
        public override void Dispose()
        {
            if(disposed)return;disposed=true;
            Undo.undoRedoPerformed -= Invalidate;
            EditorApplication.projectChanged -= RefreshCatalog; EditorApplication.hierarchyChanged -= Invalidate;
            SceneView.duringSceneGui -= DrawScatter; EditorApplication.playModeStateChanged -= PlayChanged;
            if(brushOwner==this){StopBrush();if(ToolManager.activeToolType==typeof(ActivityPlacementTool))ToolManager.RestorePreviousTool();}candidates.Clear();
        }
        public override void SetContext(RacingEditingContext context)
        {
            pinned=context.Pinned;
            var target=context.ResolveDocument();
            if(target==inspected&&inspected!=null)return;
            inspected=target as EventPlacementSource;
            if(target is WorldActivityDefinition definition){palette=definition;tab=0;}
            else if(inspected!=null)tab=string.IsNullOrEmpty(context.Document?.elementId)?1:3;
            Invalidate();
        }
        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.Add(new IMGUIContainer(Draw) { style = { flexGrow = 1 } });
        }
        private void PlayChanged(PlayModeStateChange state) { StopBrush(); candidates.Clear(); scatterPreview = false; Invalidate(); }
        private void SelectionChanged() { if(pinned)return;inspected = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<EventPlacementSource>() : null; Invalidate(); }
        private void Invalidate() { plan = null; Repaint(); }
        private void RefreshCatalog()
        {
            definitions.Clear(); definitions.AddRange(AssetDatabase.FindAssets("t:WorldActivityDefinition").Select(g => AssetDatabase.LoadAssetAtPath<WorldActivityDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(a => a != null).OrderBy(a => a.category).ThenBy(a => a.displayName));
            Invalidate();
        }
        private void Run(Action action)
        {
            try { action(); } catch (Exception e) { status = e.Message; Debug.LogWarning("Event Placement: " + e.Message); }
            Repaint();
        }
        private void Draw()
        {
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                tab = EditorGUILayout.Popup("View",tab, new[] { "Catalog", "Placement", "Availability", "Access", "Map", "Batch", "Publish" });
                if(inspected!=null)
                {
                    if(inspected.definition?.race!=null&&GUILayout.Button("Open Race Route"))Navigate?.Invoke(RacingDocumentLink.For("race-routes",inspected.definition.race));
                    if(GUILayout.Button("Open Validation"))Navigate?.Invoke(RacingDocumentLink.For("validation",inspected));
                }
                scroll = EditorGUILayout.BeginScrollView(scroll);
                if (tab == 0) Catalog();
                else if (tab == 5) Batch();
                else if (inspected == null) EditorGUILayout.HelpBox("Select an EventPlacementSource in the scene or create one from Catalog.", MessageType.Info);
                else switch (tab)
                {
                    case 1: Properties("definition", "anchor", "interactionOffset", "triggerSize", "approachAngle", "maximumSpeed", "dwellSeconds", "notes"); break;
                    case 2: Availability(); break;
                    case 3: Access(); break;
                    case 4: Properties("iconOffset", "cinematicOffset", "district", "level", "streamingCell"); Map(); break;
                    case 6: Publication(); break;
                }
                EditorGUILayout.EndScrollView();
                EditorGUILayout.HelpBox(status, MessageType.None);
            }
        }
        private void Catalog()
        {
            search = EditorGUILayout.TextField("Search name/category/adapter", search);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("New definition")) Run(() =>
                {
                    string path = EditorUtility.SaveFilePanelInProject("Activity definition", "Activity", "asset", "Save reusable definition");
                    if (string.IsNullOrEmpty(path)) return;
                    if (AssetDatabase.LoadMainAssetAtPath(path) != null) throw new InvalidOperationException("Choose a new path.");
                    var definition = ScriptableObject.CreateInstance<WorldActivityDefinition>(); AssetDatabase.CreateAsset(definition, path); AssetDatabase.SaveAssets(); palette = definition; Selection.activeObject = definition;
                });
                if (GUILayout.Button("Import placement…")) Run(() =>
                { string path = EditorUtility.OpenFilePanel("Import activity package", "", "json"); if (path.Length > 0) EventPlacementTransfer.Import(File.ReadAllText(path)); });
            }
            foreach (var definition in definitions.Where(d => (d.displayName + " " + d.category + " " + d.adapter).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    if (GUILayout.Toggle(palette == definition, definition.category + " / " + definition.displayName, "Button")) palette = definition;
                    GUILayout.Label(definition.adapter, GUILayout.Width(70));
                    if (GUILayout.Button("Inspect", GUILayout.Width(60))) Selection.activeObject = definition;
                }
            palette = (WorldActivityDefinition)EditorGUILayout.ObjectField("Brush definition", palette, typeof(WorldActivityDefinition), false);
            using (new EditorGUI.DisabledScope(palette == null))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Place at Scene pivot")) Run(() => EventPlacementCommands.Create(palette, SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero));
                if (GUILayout.Button("Surface placement brush")) Run(ActivateBrush);
            }
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Loaded scene catalog", EditorStyles.boldLabel);
            foreach (var source in UnityEngine.Object.FindObjectsByType<EventPlacementSource>(FindObjectsInactive.Include, FindObjectsSortMode.None).OrderBy(s => s.name))
                if ((source.name + " " + source.id).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 && GUILayout.Button(source.name + " · " + (source.published != null ? "published" : "draft")))
                { Selection.activeGameObject = source.gameObject; SceneView.lastActiveSceneView?.FrameSelected(); tab = 1; }
        }
        private void Properties(params string[] names)
        {
            EditorGUILayout.ObjectField("Placement", inspected, typeof(EventPlacementSource), true);
            EditorGUILayout.SelectableLabel(inspected.id, EditorStyles.miniLabel, GUILayout.Height(18));
            var selected = pinned ? new UnityEngine.Object[]{inspected} : Selection.gameObjects.Select(g => g.GetComponent<EventPlacementSource>()).Where(p => p != null).Cast<UnityEngine.Object>().ToArray();
            var serialized = new SerializedObject(selected.Length > 0 ? selected : new UnityEngine.Object[] { inspected }); serialized.Update();
            var unavailable=serialized.targetObjects.Select(Workspace.RacingEditGuard.Reason).FirstOrDefault(r=>r.Length>0);
            using(new EditorGUI.DisabledScope(!string.IsNullOrEmpty(unavailable)))
            {
                foreach (string name in names) EditorGUILayout.PropertyField(serialized.FindProperty(name), true);
                if (serialized.ApplyModifiedProperties()) { foreach (EventPlacementSource p in serialized.targetObjects) EventPlacementCommands.Changed(p); Invalidate(); }
            }
            if(!string.IsNullOrEmpty(unavailable))EditorGUILayout.HelpBox(unavailable,MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Frame entrance")) SceneView.lastActiveSceneView?.Frame(new Bounds(inspected.transform.position,Vector3.one*10));
                if (GUILayout.Button("Edit Scene handles")) { StopActiveBrush();Selection.activeGameObject=inspected.gameObject;ToolManager.SetActiveTool<ActivityPlacementTool>(); }
                if (GUILayout.Button("Duplicate with fresh ID")) Run(() => EventPlacementCommands.Duplicate(inspected));
            }
        }
        private void Access()
        {
            Properties("access", "stagingOffset", "vehicleSize", "maximumAccessLength", "maximumGrade", "obstructionMask", "serviceExitOffsets");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Accept reviewed anchor revision")) Run(() => EventPlacementCommands.AcceptRevision(inspected, false));
                if (GUILayout.Button("Accept reviewed access revision")) Run(() => EventPlacementCommands.AcceptRevision(inspected, true));
            }
            if (inspected.access.network != null)
            {
                var lanes = inspected.access.network.Lanes; int index = -1;
                for (int i = 0; i < lanes.Count; i++) if (lanes[i].Id.ToString() == inspected.access.laneId) index = i;
                int next = EditorGUILayout.Popup("Explicit access lane", index, lanes.Select(l => l.Id + " · " + l.Class + " · " + l.Length.ToString("F0") + "m").ToArray());
                if (next != index && next >= 0) { Undo.RecordObject(inspected, "Bind access lane"); inspected.access.laneId = lanes[next].Id.ToString(); EventPlacementCommands.AcceptRevision(inspected, true); }
            }
            EditorGUILayout.HelpBox("Load road and entrance collision geometry before validation. Access is a conservative straight vehicle corridor; bends require an authored lane/entrance with a suitable direct approach.", MessageType.Info);
            ValidateButton();
        }
        private void Availability()
        {
            if (inspected.definition == null) return;
            EditorGUILayout.ObjectField("Shared definition", inspected.definition, typeof(WorldActivityDefinition), false);
            EditorGUILayout.HelpBox("Synthetic facts only. This preview never loads a profile, starts a mission, saves, or settles rewards.", MessageType.Info);
            var serialized = new SerializedObject(inspected.definition); serialized.Update();
            foreach (string property in new[] { "availabilityJson", "hideWhenLocked", "completionScope", "uniqueDefinition" }) EditorGUILayout.PropertyField(serialized.FindProperty(property), true);
            serialized.ApplyModifiedProperties();
            previewKind = (CareerFactKind)EditorGUILayout.EnumPopup("Fact", previewKind); previewSubject = EditorGUILayout.TextField("Subject ID", previewSubject); previewValue = EditorGUILayout.LongField("Synthetic value", previewValue);
            if (GUILayout.Button("Apply synthetic fact")) facts.Set(previewKind, previewSubject, previewValue);
            try { DrawExplanation(CareerRequirement.Compile(inspected.definition.availability).Explain(facts), 0); }
            catch (Exception e) { EditorGUILayout.HelpBox(e.Message, MessageType.Error); }
        }
        private static void DrawExplanation(CareerRequirementExplanation explanation, int depth)
        {
            EditorGUI.indentLevel = depth; EditorGUILayout.LabelField((explanation.Satisfied ? "✓ " : "× ") + explanation.Description, explanation.Current + " / " + explanation.Required);
            foreach (var child in explanation.Children) DrawExplanation(child, depth + 1);
            EditorGUI.indentLevel = 0;
        }
        private void Map()
        {
            var diagnostic = EventPlacementCompiler.Build(inspected, false); if (diagnostic.record == null) return;
            var r = diagnostic.record;
            EditorGUILayout.Vector3Field("GPS destination (access)", r.access); EditorGUILayout.Vector3Field("Interaction", r.interaction); EditorGUILayout.Vector3Field("Map icon", r.icon);
            EditorGUILayout.HelpBox("Markers register when their runtime instance loads and unregister on unload. Streaming cell is metadata for your scene loader; it does not claim the player discovered this activity.", MessageType.Info);
            Rect rect = GUILayoutUtility.GetRect(200, 210, GUILayout.ExpandWidth(true)); EditorGUI.DrawRect(rect, new Color(.09f, .12f, .14f));
            float span = Mathf.Max(20, Vector3.Distance(r.access, r.icon) * 2);
            Vector2 Point(Vector3 p) => rect.center + new Vector2(p.x - r.interaction.x, r.interaction.z - p.z) / span * 180;
            foreach (var entry in new[] { (r.access, "GPS", Color.cyan), (r.interaction, "Entrance", Color.green), (r.icon, "Icon", Color.yellow), (r.staging, "Staging", Color.magenta) })
            { Vector2 point = Point(entry.Item1); EditorGUI.DrawRect(new Rect(point - Vector2.one * 3, Vector2.one * 6), entry.Item3); GUI.Label(new Rect(point + Vector2.one * 5, new Vector2(90, 20)), entry.Item2); }
        }
        private void ValidateButton()
        {
            if (GUILayout.Button("Validate loaded geometry and dependencies")) Run(() =>
            { var timer = System.Diagnostics.Stopwatch.StartNew(); plan = EventPlacementCompiler.Build(inspected); lastValidationMilliseconds = timer.Elapsed.TotalMilliseconds; status = plan.Valid ? "Validation passed; publication is available." : plan.errors.Count + " blocking diagnostics."; });
            if (plan == null) return;
            EditorGUILayout.LabelField("Last validation", lastValidationMilliseconds.ToString("F2") + " ms (this scene)");
            foreach (var error in plan.errors) EditorGUILayout.HelpBox(error, MessageType.Error);
            foreach (var warning in plan.warnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
        }
        private void Publication()
        {
            previewVehicle = (RacingVehicleSetup)EditorGUILayout.ObjectField("Isolated test vehicle", previewVehicle, typeof(RacingVehicleSetup), false);
            if (GUILayout.Button("Run isolated service owner test")) Run(() => status = ActivityOwnerPreview.Run(inspected, previewVehicle));
            ValidateButton(); EditorGUILayout.ObjectField("Pinned publication", inspected.published, typeof(EventPlacementPublication), false);
            if (plan != null && inspected.published != null) EditorGUILayout.LabelField("Revision", plan.record != null && plan.record.fingerprint == inspected.published.Fingerprint ? "Matches source" : "Source differs — republish after validation");
            if (GUILayout.Button("Validate and publish new revision…")) Run(() =>
            { string path = EditorUtility.SaveFilePanelInProject("Publish activity", inspected.name + "Published", "asset", "Choose a new immutable revision path"); if (path.Length > 0) { EventPlacementCommands.Publish(inspected, path); status = "Published. The prior asset is retained for rollback; Undo restores the scene binding."; } });
            if (GUILayout.Button("Create marker catalog from loaded scene publications")) Run(() =>
            {
                var go = new GameObject("Activity map catalog — keep in bootstrap scene"); Undo.RegisterCreatedObjectUndo(go, "Create activity catalog");
                var catalog = Undo.AddComponent<ActivityMapCatalog>(go);
                catalog.Configure(UnityEngine.Object.FindObjectsByType<EventPlacementSource>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(p => p.published != null).Select(p => p.published).Distinct().ToArray());
                status = "Catalog created. Keep it in a persistent scene to retain markers while placement cells unload.";
            });
            if (GUILayout.Button("Export portable placement…")) Run(() =>
            { string path = EditorUtility.SaveFilePanel("Export activity", "", inspected.name, "json"); if (path.Length > 0) File.WriteAllText(path, EventPlacementTransfer.Export(inspected)); });
            EditorGUILayout.HelpBox("Publication revalidates immediately. Runtime adapters use the pinned record. Asset revisions remain on disk after Undo so references and rollback history are not destroyed.", MessageType.Info);
        }
        private void Batch()
        {
            EditorGUILayout.HelpBox("Batch operations use Unity's selected placements, not the pinned document. Scatter creates new placements from the chosen definition.",MessageType.Info);
            EditorGUILayout.LabelField("Move selected world anchors", EditorStyles.boldLabel); batchOffset = EditorGUILayout.Vector3Field("Offset", batchOffset);
            if (GUILayout.Button("Apply offset (one Undo group)")) Run(() =>
            {
                var sources = Selection.gameObjects.Select(g => g.GetComponent<EventPlacementSource>()).Where(p => p != null).ToArray();
                if (sources.Any(p => p.anchor.kind != ActivityAnchorKind.World)) throw new InvalidOperationException("Selection contains bound anchors; edit their stations explicitly.");
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); foreach (var source in sources) EventPlacementCommands.MoveWorld(source, source.anchor.worldPosition + batchOffset); Undo.CollapseUndoOperations(group);
            });
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Scatter preview along an approved lane interval", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            scatterNetwork = (RoadNetworkAsset)EditorGUILayout.ObjectField("Road publication", scatterNetwork, typeof(RoadNetworkAsset), false);
            palette = (WorldActivityDefinition)EditorGUILayout.ObjectField("Definition", palette, typeof(WorldActivityDefinition), false);
            if (scatterNetwork != null) scatterLane = EditorGUILayout.Popup("Lane", scatterLane, scatterNetwork.Lanes.Select(l => l.Id.ToString()).ToArray());
            scatterStart = EditorGUILayout.FloatField("Start station", scatterStart); scatterSpacing = EditorGUILayout.FloatField("Spacing (m)", scatterSpacing); scatterCount = EditorGUILayout.IntSlider("Count", scatterCount, 1, 100);
            if (EditorGUI.EndChangeCheck()) { candidates.Clear(); scatterPreview = false; }
            if (GUILayout.Button("Preview candidates")) Run(() =>
            {
                candidates.Clear(); scatterPreview = false;
                if (palette == null || palette.uniqueDefinition || scatterNetwork == null || scatterLane < 0 || scatterLane >= scatterNetwork.Lanes.Count || !float.IsFinite(scatterSpacing) || scatterSpacing < 15 || scatterStart < 0) throw new InvalidOperationException("Choose a reusable definition, lane, nonnegative station and spacing ≥ 15m.");
                var lane = scatterNetwork.Lanes[scatterLane];
                if (scatterStart + (scatterCount - 1) * scatterSpacing > lane.Length) throw new InvalidOperationException("Approved interval extends beyond this lane.");
                for (int i = 0; i < scatterCount; i++) candidates.Add(lane.Sample(scatterStart + i * scatterSpacing).position);
                scatterRevision = scatterNetwork.Fingerprint; scatterPreview = true; SceneView.RepaintAll();
            });
            using (new EditorGUI.DisabledScope(!scatterPreview)) if (GUILayout.Button("Create previewed drafts")) Run(() =>
            {
                if (scatterNetwork.Fingerprint != scatterRevision) throw new InvalidOperationException("Road changed; preview again.");
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                try
                {
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var source = EventPlacementCommands.Create(palette, candidates[i]);
                        source.anchor = new ActivityAnchor { kind = ActivityAnchorKind.Lane, network = scatterNetwork, laneId = scatterNetwork.Lanes[scatterLane].Id.ToString(), station = scatterStart + i * scatterSpacing, sourceRevision = scatterRevision };
                        source.access = JsonUtility.FromJson<ActivityAnchor>(JsonUtility.ToJson(source.anchor));
                        var candidate = EventPlacementCompiler.Build(source);
                        if (!candidate.Valid) throw new InvalidOperationException("Scatter rolled back: " + string.Join("; ", candidate.errors));
                    }
                    Undo.CollapseUndoOperations(group); candidates.Clear(); scatterPreview = false;
                }
                catch { Undo.RevertAllDownToGroup(group); throw; }
            });
            EditorGUILayout.HelpBox("Candidates are scene previews until committed. Each draft is validated; any failure rolls back the batch. Existing placements are preserved.", MessageType.Info);
        }
        private void DrawScatter(SceneView view)
        { Handles.color = Color.yellow; foreach (var point in candidates) Handles.DrawWireDisc(point, Vector3.up, 4); }
    }
}
