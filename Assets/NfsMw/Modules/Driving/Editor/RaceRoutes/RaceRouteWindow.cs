using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using NfsMwRemaster.Driving.Editor.Workspace;
namespace NfsMwRemaster.Driving.Editor
{
    public sealed class RaceRouteWindow : RacingFocusedWindow
    {
        [SerializeField] private RaceRouteDefinition source;
        protected override void OnEnable(){base.OnEnable();if(source){OpenDocument(source);source=null;}}
        protected override string ModuleId => "race-routes";
        public static RaceRouteView Active => RaceRouteView.Active;
        [MenuItem("NFS MW Remaster/Racing/Race Route Editor",false,0)] public static void Open()=>GetWindow<RaceRouteWindow>("Race Routes");
        [MenuItem("Window/NFS MW Remaster/Race Route Editor")] public static void OpenWorkspace()=>Open();
    }
    public sealed class RaceRouteView:RacingModuleView, IRacingViewState
    {
        public static RaceRouteView Active {get;private set;}
        private static readonly string[] Views={"Routes","Network map","Traversal","Gates & branches","Start grid","Event policy","Dependencies","Test race","Publish","Reports"};
        [SerializeField] private RaceRouteDefinition source;
        [SerializeField] private int view,legIndex,pathIndex;
        [SerializeField] private string startLane,finishLane,search="";
        [SerializeField] private float start=30,finish=70,mapHeight,heightTolerance=10000;
        [SerializeField] private RaceRouteSuggestion metric;
        [SerializeField] private bool showGates=true,showCorridor=true;
        public bool PickFinish {get;set;}
        public RaceRouteDefinition Source=>source;
        private ScrollView body;private HelpBox status;private ObjectField owner;private DropdownField section;
        private RaceRoutePlan plan;private RaceRouteTestPreview preview;
        private readonly List<RaceRouteDefinition> catalog=new List<RaceRouteDefinition>();
        private bool disposed;
        [Serializable] private sealed class ViewState
        {
            public int view, legIndex, pathIndex;
            public string startLane, finishLane, search;
            public float start, finish, mapHeight, heightTolerance;
            public bool showGates, showCorridor;
        }
        public string CaptureViewState()=>JsonUtility.ToJson(new ViewState{view=view,legIndex=legIndex,pathIndex=pathIndex,startLane=startLane,finishLane=finishLane,search=search,start=start,finish=finish,mapHeight=mapHeight,heightTolerance=heightTolerance,showGates=showGates,showCorridor=showCorridor});
        public void RestoreViewState(string json){if(string.IsNullOrEmpty(json))return;var state=JsonUtility.FromJson<ViewState>(json);view=Mathf.Clamp(state.view,0,Views.Length-1);legIndex=state.legIndex;pathIndex=state.pathIndex;startLane=state.startLane;finishLane=state.finishLane;search=state.search??"";start=state.start;finish=state.finish;mapHeight=state.mapHeight;heightTolerance=state.heightTolerance;showGates=state.showGates;showCorridor=state.showCorridor;Draw();}
        private RacingPreviewLease previewLease;
        public RaceRouteView(){CreateGUI();Active=this;Root.RegisterCallback<FocusInEvent>(_=>Active=this);SceneView.duringSceneGui+=DrawScene;Undo.undoRedoPerformed+=Changed;AssemblyReloadEvents.beforeAssemblyReload+=Stop;EditorApplication.playModeStateChanged+=Play;EditorSceneManager.sceneClosing+=SceneClosing;EditorApplication.update+=Tick;}
        public override void Dispose(){if(disposed)return;disposed=true;SceneView.duringSceneGui-=DrawScene;Undo.undoRedoPerformed-=Changed;AssemblyReloadEvents.beforeAssemblyReload-=Stop;EditorApplication.playModeStateChanged-=Play;EditorSceneManager.sceneClosing-=SceneClosing;EditorApplication.update-=Tick;Stop();if(Active==this){if(ToolManager.activeToolType==typeof(RaceRoutePickTool))ToolManager.RestorePreviousTool();Active=null;}}
        public override void SetContext(RacingEditingContext context){var next=context.ResolveDocument() as RaceRouteDefinition;if(source!=next){source=next;Changed();}if(!string.IsNullOrEmpty(context.Document?.elementId)){view=3;Draw();Focus(context.Document.elementId);}}
        private void Play(PlayModeStateChange state){if(state==PlayModeStateChange.ExitingEditMode)Stop();}
        private void SceneClosing(UnityEngine.SceneManagement.Scene scene,bool removing){if(preview==null||scene!=preview.PreviewScene)Stop();}
        private void Tick(){if(preview!=null){try{preview.Advance();if(preview.Done){status.text=preview.Summary;Repaint();}}catch(Exception e){Stop();status.text=e.Message;status.messageType=HelpBoxMessageType.Error;}}}
        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var toolbar=new Toolbar();toolbar.style.flexWrap=Wrap.Wrap;toolbar.style.height=StyleKeyword.Auto;owner=new ObjectField("Route"){objectType=typeof(RaceRouteDefinition),allowSceneObjects=false,value=source};owner.style.flexGrow=1;
            owner.RegisterValueChangedCallback(e=>{source=e.newValue as RaceRouteDefinition;Changed();Navigate?.Invoke(RacingDocumentLink.For("race-routes",source));});toolbar.Add(owner);
            toolbar.Add(new ToolbarButton(()=>Run(()=>{PickFinish=false;ToolManager.SetActiveTool<RaceRoutePickTool>();})){text="Pick start"});
            toolbar.Add(new ToolbarButton(()=>Run(()=>{PickFinish=true;ToolManager.SetActiveTool<RaceRoutePickTool>();})){text="Pick finish"});
            toolbar.Add(new ToolbarButton(Validate){text="Validate"});rootVisualElement.Add(toolbar);
            section=new DropdownField("View",Views.ToList(),view){name="race-route-section"};section.RegisterValueChangedCallback(e=>{view=Array.IndexOf(Views,e.newValue);Draw();});rootVisualElement.Add(section);
            body=new ScrollView();body.style.flexGrow=1;body.style.paddingLeft=8;body.style.paddingRight=8;body.style.paddingTop=8;rootVisualElement.Add(body);
            status=new HelpBox("Choose a route or create one. Road topology remains owned by the Road Editor.",HelpBoxMessageType.Info);rootVisualElement.Add(status);Draw();
        }
        private void Changed(){Stop();plan=null;Draw();SceneView.RepaintAll();}
        public void Stop(){var old=preview;preview=null;try{old?.Dispose();}finally{previewLease?.Dispose();previewLease=null;}}
        private void Run(Action action){var before=source;try{action();}catch(OperationCanceledException){status.text="Cancelled; previous publication retained.";}catch(Exception e){status.text=e.Message;status.messageType=HelpBoxMessageType.Error;}finally{EditorUtility.ClearProgressBar();if(!disposed){Draw();if(before!=source)Navigate?.Invoke(RacingDocumentLink.For("race-routes",source));}SceneView.RepaintAll();}}
        private void Info(string text,bool error=false)=>body.Add(new HelpBox(text,error?HelpBoxMessageType.Error:HelpBoxMessageType.Info));
        private void Button(string label,Action action){var b=new Button(()=>Run(action)){text=label};b.style.minHeight=29;b.style.marginTop=4;body.Add(b);}
        private void Fields(params string[] names)
        {
            var serialized=new SerializedObject(source);
            foreach(string name in names){var property=serialized.FindProperty(name);if(property==null)continue;var field=new PropertyField(property);field.Bind(serialized);field.RegisterValueChangeCallback(_=>{Stop();plan=null;SceneView.RepaintAll();});var reason=RacingEditGuard.Reason(source);field.SetEnabled(reason.Length==0);field.tooltip=reason;body.Add(field);}
        }
        public void Validate()=>Run(()=>{Stop();plan=RaceRouteCompiler.Build(source,p=>EditorUtility.DisplayCancelableProgressBar("Validate race route","Resolve lane occurrences and physical gates",p));status.text=plan.Valid?"Validated geometry. Review dependencies and publication.":string.Join("\n",plan.issues.Where(i=>i.error).Take(3));status.messageType=plan.Valid?HelpBoxMessageType.Info:HelpBoxMessageType.Error;});
        private void Draw()
        {
            if(body==null)return;owner?.SetValueWithoutNotify(source);body.Clear();view=Mathf.Clamp(view,0,Views.Length-1);
            section?.SetValueWithoutNotify(Views[view]);
            var title=new Label(Views[view]);title.style.fontSize=18;title.style.unityFontStyleAndWeight=FontStyle.Bold;body.Add(title);
            if(view==0){DrawCatalog();return;}if(source==null){Info("Create or choose a route first.");return;}
            switch(view)
            {
                case 1: DrawNetwork();break;
                case 2: DrawTraversal();break;
                case 3: DrawGates();break;
                case 4: Fields("gridCount","vehicleWidth","vehicleLength","vehicleHeight","gridGap","sideBySideGrid","finishRunoff");Button("Validate grid dimensions",Validate);Button("Check loaded-scene obstacles",()=>{RequirePlan();plan.issues.AddRange(RaceRouteCommands.CheckLoadedGrid(source,plan));});Info("Graph validation measures available lane width and length. Collision checks cover loaded colliders only. Runtime uses the existing safe-start owner.");break;
                case 5: Fields("displayName","localizationKey","tags","policy","laps","timeLimit","targetSpeedKph","notes");Info("Sprint, Circuit, Drag and Speedtrap use existing MissionRuntime policies. Drag currently means ordered traversal; a staging/shifting rule set is not supplied. Drift scoring and spontaneous challenge approval need their authoritative policy adapters. Placement starts with zero reward; Career/settlement remain the owners.");break;
                case 6: DrawDependencies();break;
                case 7: DrawTest();break;
                case 8: DrawPublish();break;
                case 9: DrawReport();break;
            }
            if(plan!=null&&view!=9)Info($"{plan.length:F0} m main route · {plan.minimumWidth:F1} m minimum width · {plan.maximumGrade:F1}° maximum sampled grade · {plan.milliseconds:F1} ms compilation");
        }
        private void DrawCatalog()
        {
            Button("Create route asset",()=>{var path=EditorUtility.SaveFilePanelInProject("Create race route","RaceRoute","asset","Choose route source location.");if(path.Length>0){source=RaceRouteCommands.Create(path);view=1;}});
            Button("Refresh route library",()=>{catalog.Clear();foreach(var guid in AssetDatabase.FindAssets("t:RaceRouteDefinition"))catalog.Add(AssetDatabase.LoadAssetAtPath<RaceRouteDefinition>(AssetDatabase.GUIDToAssetPath(guid)));});
            var items=catalog.Where(r=>r!=null&&(r.displayName+" "+r.tags+" "+r.policy).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0).ToList();
            var list=new ListView(items,28,()=>new Label(),(v,i)=>((Label)v).text=items[i].displayName+" · "+items[i].policy);list.style.height=240;list.selectionChanged+=v=>{source=v.OfType<RaceRouteDefinition>().FirstOrDefault();Changed();Navigate?.Invoke(RacingDocumentLink.For("race-routes",source));};
            var filter=new ToolbarSearchField{value=search};filter.RegisterValueChangedCallback(e=>{search=e.newValue;items.Clear();items.AddRange(catalog.Where(r=>r!=null&&(r.displayName+" "+r.tags+" "+r.policy).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0));list.Rebuild();});body.Add(filter);body.Add(list);
            if(source!=null){Fields("network");Button("Duplicate with new owned IDs",()=>{var path=EditorUtility.SaveFilePanelInProject("Duplicate route",source.name+" Copy","asset","Choose location.");if(path.Length>0){source=RaceRouteCommands.Duplicate(source,path);Changed();}});}
            Button("Create geometry-validated graybox sample",()=>{source=RaceRouteDemo.Create();view=1;});
        }
        private void LanePicker(string label,string value,Action<string> set)
        {
            var lanes=source.network?.Lanes;if(lanes==null)return;var options=lanes.Select(l=>l.Id.ToString()).ToList();if(options.Count==0)return;
            if(!options.Contains(value)){value=options[0];set(value);}
            var field=new PopupField<string>(label,options,Mathf.Max(0,options.IndexOf(value)));field.RegisterValueChangedCallback(e=>set(e.newValue));body.Add(field);
        }
        private void Number(string label,float value,Action<float> set){var field=new FloatField(label){value=value};field.RegisterValueChangedCallback(e=>set(e.newValue));body.Add(field);}
        private void DrawNetwork()
        {
            Fields("network","minimumWidth","maximumGradeDegrees","maximumTurnDegrees","requiredSurface","excludedRoadClasses");
            LanePicker("Start lane",startLane,v=>startLane=v);Number("Start station (m)",start,v=>start=v);LanePicker("Finish lane",finishLane,v=>finishLane=v);Number("Finish station (m)",finish,v=>finish=v);
            var mode=new EnumField("Suggestion cost",metric);mode.RegisterValueChangedCallback(e=>metric=(RaceRouteSuggestion)e.newValue);body.Add(mode);
            Button("Suggest connected sector",()=>{var spans=RaceRouteCommands.Suggest(source,startLane,start,finishLane,finish,metric,p=>EditorUtility.DisplayCancelableProgressBar("Route search","Following published successor edges",p));RaceRouteCommands.Edit(source,"Append suggested sector",()=>source.legs=source.legs.Concat(new[]{new RaceRouteLeg {label="Sector "+(source.legs.Length+1),paths=new[]{new RaceRoutePath {spans=spans}}}}).ToArray());plan=null;view=2;});
            Info("Add sectors through preferred districts to constrain the route. Suggestions minimize the selected cost; target length/scenery quality are designer judgments, not guaranteed optimization results. Click the map to select the active start/finish anchor.");
            Number("Map height layer (m)",mapHeight,v=>mapHeight=v);Number("Layer tolerance (m)",heightTolerance,v=>heightTolerance=Mathf.Max(.1f,v));
            body.Add(new IMGUIContainer(MapGUI){style={height=340}});
        }
        private void MapGUI()
        {
            var rect=GUILayoutUtility.GetRect(300,330,GUILayout.ExpandWidth(true));EditorGUI.DrawRect(rect,new Color(.09f,.11f,.13f));if(source.network==null)return;
            var lanes=source.network.Lanes.Where(l=>l.Samples.Any(s=>Mathf.Abs(s.position.y-mapHeight)<=heightTolerance)).ToArray();if(lanes.Length==0)return;
            var bounds=new Bounds(lanes[0].Samples[0].position,Vector3.zero);foreach(var l in lanes)foreach(var p in l.Samples)bounds.Encapsulate(p.position);
            Vector2 Project(Vector3 p)=>new Vector2(rect.x+15+(p.x-bounds.min.x)/Mathf.Max(1,bounds.size.x)*(rect.width-30),rect.yMax-15-(p.z-bounds.min.z)/Mathf.Max(1,bounds.size.z)*(rect.height-30));
            Handles.BeginGUI();float best=15;RoadBakedLane picked=null;float station=0;
            foreach(var lane in lanes){Handles.color=lane.Id.ToString()==startLane?Color.green:lane.Id.ToString()==finishLane?Color.red:new Color(.35f,.65f,.75f);for(int i=1;i<lane.Samples.Count;i++)Handles.DrawLine(Project(lane.Samples[i-1].position),Project(lane.Samples[i].position));foreach(var p in lane.Samples){float d=Vector2.Distance(Project(p.position),Event.current.mousePosition);if(d<best){best=d;picked=lane;station=p.distance;}}}Handles.EndGUI();
            if(Event.current.type==EventType.MouseDown&&Event.current.button==0&&rect.Contains(Event.current.mousePosition)&&picked!=null){SetAnchor(picked,station);Event.current.Use();}
        }
        public void SetAnchor(RoadBakedLane lane,float station){if(PickFinish){finishLane=lane.Id.ToString();finish=station;}else{startLane=lane.Id.ToString();start=station;}status.text=(PickFinish?"Finish":"Start")+" anchor: "+lane.Id+" @ "+station.ToString("F1")+" m";Repaint();}
        private void DrawTraversal()
        {
            Info("Each span is a directed lane occurrence. Repeated lanes keep separate IDs. Reorder sectors deliberately; validation checks every seam.");Fields("legs");
            Button("Add empty sector",()=>RaceRouteCommands.Edit(source,"Add sector",()=>source.legs=source.legs.Concat(new[]{new RaceRouteLeg()}).ToArray()));
            Button("Reverse sector order for editing",()=>RaceRouteCommands.Edit(source,"Reverse sector order",()=>Array.Reverse(source.legs)));Info("Reordering does not reverse one-way lanes; remap each occurrence explicitly before publishing.");Button("Validate connectivity",Validate);
        }
        private void DrawGates()
        {
            Info("Gates are generated per lane occurrence and sector spacing, with lane orientation, real height and corridor width. Alternatives share explicit start/rejoin anchors. All internal gates on the chosen path are required; the sector end advances MissionRuntime once.");Fields("legs");
            var a=new Toggle("Show corridor"){value=showCorridor};a.RegisterValueChangedCallback(e=>{showCorridor=e.newValue;SceneView.RepaintAll();});body.Add(a);var b=new Toggle("Show gates"){value=showGates};b.RegisterValueChangedCallback(e=>{showGates=e.newValue;SceneView.RepaintAll();});body.Add(b);
            var branch=new IntegerField("Path index"){value=pathIndex};branch.RegisterValueChangedCallback(e=>{pathIndex=e.newValue;SceneView.RepaintAll();});body.Add(branch);
            Info("Scene arrows move the selected path endpoints along their lane; release to commit one Undo action. Measured test traces use blue-to-red speed colors (0–150 km/h).");
            var index=new IntegerField("Sector index"){value=legIndex};index.RegisterValueChangedCallback(e=>legIndex=e.newValue);body.Add(index);
            Button("Duplicate main path as editable alternative",()=>RaceRouteCommands.Edit(source,"Add alternative",()=>{var leg=source.legs[legIndex];var copy=JsonUtility.FromJson<RaceRoutePath>(JsonUtility.ToJson(leg.paths[0]));copy.id=Guid.NewGuid().ToString("N");copy.label="Alternative";foreach(var span in copy.spans)span.id=Guid.NewGuid().ToString("N");leg.paths=leg.paths.Concat(new[]{copy}).ToArray();}));
            Info("Ranking is completed sectors plus normalized distance through the selected branch. Physical distance and legal progress are separate. Gate crossing is directional and swept in 3D.");Button("Build gate preview",Validate);
        }
        private void DrawDependencies()
        {
            Fields("vehicle","racingLine");Info("Roads supply topology and surfaces. MissionRuntime supplies progress and outcomes. FreeRoamSession supplies race lifecycle and safe start checks. Racing Line Studio supplies the production vehicle/AI rollout. Traffic and police configuration are not modified by this tool.");
            Button("Export main traversal to Racing Line Studio",()=>{var path=EditorUtility.SaveFilePanelInProject("Export line route",source.name+" LineRoute","asset","Choose adapter location.");if(path.Length>0)Selection.activeObject=RaceRouteCommands.ExportLineRoute(source,null,path);});
            Button("Inspect linked line source",()=>Selection.activeObject=source.racingLine);
            Button("Find loaded pursuit/traffic components",()=>{var components=UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(c=>c!=null&&(c.GetType().Name.StartsWith("Police",StringComparison.Ordinal)||c.GetType().Name.StartsWith("Traffic",StringComparison.Ordinal))).Take(250);Selection.objects=components.Cast<UnityEngine.Object>().ToArray();status.text=Selection.objects.Length+" loaded components selected for read-only inspection.";});
        }
        private void DrawTest()
        {
            Info("Without a current verified Racing Line Studio source, the available test is a geometry replay. A vehicle rollout uses the production Racing Line rig in an isolated physics scene and sends crossings to MissionRuntime with zero rewards. It does not load a career/profile.");
            Button("Replay gate geometry through MissionRuntime",()=>{RequirePlan();status.text=RaceRouteTestPreview.Replay(source,plan);});
            Button("Run isolated vehicle / mission test",()=>{RequirePlan();Stop();previewLease=RacingPreviewSessions.Acquire("vehicle-simulation","Race Routes",Stop);try{preview=new RaceRouteTestPreview(source);}catch{Stop();throw;}status.text="Running isolated production vehicle rollout…";});
            Button("Cancel test / release preview world",Stop);
            if(preview!=null){Info(preview.Summary);Button("Export recorded test",()=>Export(preview.ReportJson));}
        }
        private void DrawPublish()
        {
            Info(source.published==null?"Unpublished":source.published.Fingerprint==RaceRouteCompiler.Fingerprint(source)?"Published revision matches current route dependencies.":"Dirty: relevant source data changed since publication.");Button("Validate and review",Validate);
            Button("Publish immutable revision",()=>{RequirePlan();var path=EditorUtility.SaveFilePanelInProject("Publish route",source.name+" Published","asset","Previous revisions remain available for Undo.");if(path.Length>0)RaceRouteCommands.Publish(source,plan,path);});
            var context=new RacingEditingContext{Document=RacingDocumentLink.For("race-routes",source)};
            var command=new RacingCommand("route.create-event","Create Event at Start Lane","Creates a new activity definition asset and a scene placement.","Undo removes the scene placement. The new definition asset remains.",c=>RacingRouteEventWorkflow.Unavailable(c.ResolveDocument() as RaceRouteDefinition),c=>{var route=(RaceRouteDefinition)c.ResolveDocument();var path=EditorUtility.SaveFilePanelInProject("Create route activity definition",route.name+" Activity","asset","The definition asset survives scene Undo. Delete it explicitly if no longer used.");if(string.IsNullOrEmpty(path))return;var placed=RacingRouteEventWorkflow.Create(route,path);Navigate?.Invoke(RacingDocumentLink.For("event-placement",placed,"access"));});
            var reason=command.UnavailableReason(context);
            var placement=new Button(()=>Run(()=>command.Execute(context))){text=command.Label,tooltip=reason.Length>0?reason:command.Effects+" "+command.Recovery};placement.SetEnabled(reason.Length==0);body.Add(placement);
            if(reason.Length>0)Info(reason);
            Button("Open Validation",()=>Navigate?.Invoke(RacingDocumentLink.For("validation",source)));
            Info("Creates a reusable activity definition and an unpublished scene placement bound to this exact start lane. Review access, clearance and interaction in Event Placement before publishing. Scene creation supports Undo; the definition asset remains. Career, rewards and discovery catalogs are not changed.");
        }
        private void DrawReport()
        {
            Button("Compile diagnostics",Validate);if(plan==null)return;
            Info($"Main path: {plan.length:F1} m; gain: {plan.elevationGain:F1} m; posted-speed estimate: {plan.estimatedSeconds:F1} s.");
            foreach(var issue in plan.issues){Info(issue.ToString(),issue.error);Button("Focus "+issue.owner,()=>Focus(issue.owner));}
            Button("Export validation JSON",()=>Export(JsonUtility.ToJson(new Report {routeId=source.id,fingerprint=plan.fingerprint,unity=Application.unityVersion,issues=plan.issues.ToArray(),length=plan.length,compileMilliseconds=plan.milliseconds},true)));
        }
        [Serializable] private sealed class Report{public string routeId,fingerprint,unity;public RaceRouteIssue[] issues;public float length;public double compileMilliseconds;}
        private void Export(string text){var path=EditorUtility.SaveFilePanel("Export race report","",source.name+" Report","json");if(path.Length>0)File.WriteAllText(path,text);}
        private void RequirePlan(){if(plan==null||!plan.Valid||plan.fingerprint!=RaceRouteCompiler.Fingerprint(source))throw new ArgumentException("Validate the current route and resolve errors first.");}
        private void Focus(string id)
        {var span=source.legs.SelectMany(l=>l.paths).SelectMany(p=>p.spans).FirstOrDefault(s=>s.id==id);if(span==null)return;var lane=RaceRouteCompiler.Lane(source,span.laneId);if(lane!=null)SceneView.lastActiveSceneView?.Frame(new Bounds(lane.Sample(span.startMetres).position,Vector3.one*30),false);}
        private string draggingSpan;
        private float draggedStation;
        private bool draggingEnd;
        private void DrawEndpointHandles()
        {
            if(view!=3||source.legs==null||legIndex<0||legIndex>=source.legs.Length)return;
            var leg=source.legs[legIndex];
            if(leg?.paths==null||pathIndex<0||pathIndex>=leg.paths.Length)return;
            var path=leg.paths[pathIndex];if(path?.spans==null||path.spans.Length==0)return;
            foreach(bool endpoint in new[]{false,true})
            {
                var span=endpoint?path.spans.Last():path.spans[0];if(span==null)continue;
                var lane=RaceRouteCompiler.Lane(source,span.laneId);if(lane==null)continue;
                float station=draggingSpan==span.id&&draggingEnd==endpoint?draggedStation:endpoint?RaceRouteCompiler.End(span,lane):span.startMetres;
                var pose=lane.Sample(station);Handles.color=endpoint?Color.red:Color.green;
                EditorGUI.BeginChangeCheck();
                var position=Handles.Slider(pose.position,pose.forward,HandleUtility.GetHandleSize(pose.position)*.8f,Handles.ArrowHandleCap,0);
                if(EditorGUI.EndChangeCheck())
                {
                    draggingSpan=span.id;draggingEnd=endpoint;
                    draggedStation=Mathf.Clamp(lane.Project(position,out _),endpoint?span.startMetres+.5f:0,endpoint?lane.Length:RaceRouteCompiler.End(span,lane)-.5f);
                }
                if(draggingSpan==span.id&&draggingEnd==endpoint&&GUIUtility.hotControl==0)
                {
                    float value=draggedStation;draggingSpan=null;
                    RaceRouteCommands.Edit(source,"Move route anchor",()=>{if(endpoint)span.endMetres=value;else span.startMetres=value;});
                    plan=null;Stop();
                }
            }
        }
        private void DrawScene(SceneView scene)
        {
            if(Active!=this||source==null||source.network==null)return;
            if(showCorridor)foreach(var leg in (source.legs??Array.Empty<RaceRouteLeg>()).Where(l=>l?.paths!=null))foreach(var path in leg.paths.Where(p=>p?.spans!=null))foreach(var span in path.spans.Where(s=>s!=null))
            {
                var lane=RaceRouteCompiler.Lane(source,span.laneId);if(lane==null)continue;Handles.color=path==leg.paths[0]?Color.cyan:Color.magenta;var last=lane.Sample(span.startMetres);
                for(float d=span.startMetres+5;d<=RaceRouteCompiler.End(span,lane);d+=5){var p=lane.Sample(d);Handles.DrawLine(last.position,p.position,2);Handles.DrawLine(last.position+last.left*last.width/2,p.position+p.left*p.width/2);Handles.DrawLine(last.position-last.left*last.width/2,p.position-p.left*p.width/2);last=p;}
            }
            preview?.DrawSpeedHeatmap();
            DrawEndpointHandles();
            if(plan==null)return;if(showGates)foreach(var leg in plan.legs)foreach(var path in leg.paths)foreach(var gate in path.gates){Handles.color=Color.yellow;using(new Handles.DrawingScope(Matrix4x4.TRS(gate.position,Quaternion.LookRotation(gate.forward,gate.up),Vector3.one)))Handles.DrawWireCube(Vector3.zero,new Vector3(gate.width,gate.height,.3f));}
            Handles.color=Color.green;foreach(var position in plan.grid)Handles.DrawWireCube(position+Vector3.up*source.vehicleHeight/2,new Vector3(source.vehicleWidth,source.vehicleHeight,source.vehicleLength));
        }
    }
    [EditorTool("Pick race route anchors")]
    public sealed class RaceRoutePickTool:EditorTool
    {
        public override void OnToolGUI(EditorWindow window)
        {
            var editor=RaceRouteWindow.Active;if(!(window is SceneView))return;var evt=Event.current;
            if(evt.type==EventType.KeyDown&&evt.keyCode==KeyCode.Escape){ToolManager.RestorePreviousTool();evt.Use();return;}
            if(editor?.Source?.network==null){ToolManager.RestorePreviousTool();return;}if(evt.alt)return;
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));RoadBakedLane chosen=null;float station=0,best=16;
            foreach(var lane in editor.Source.network.Lanes)foreach(var sample in lane.Samples){float distance=Vector2.Distance(HandleUtility.WorldToGUIPoint(sample.position),evt.mousePosition);if(distance<best){best=distance;station=sample.distance;chosen=lane;}}
            if(chosen==null)return;var pose=chosen.Sample(station);Handles.color=Color.green;Handles.SphereHandleCap(0,pose.position,Quaternion.identity,HandleUtility.GetHandleSize(pose.position)*.2f,EventType.Repaint);
            if(evt.type==EventType.MouseDown&&evt.button==0){editor.SetAnchor(chosen,station);evt.Use();}
        }
    }
    [UnityEditor.Overlays.Overlay(typeof(SceneView),"Race route authoring")]
    public sealed class RaceRouteOverlay:UnityEditor.Overlays.Overlay
    {
        public override VisualElement CreatePanelContent(){var panel=new VisualElement();panel.Add(new Button(RaceRouteWindow.Open){text="Open Route Editor"});panel.Add(new Button(()=>RaceRouteWindow.Active?.Validate()){text="Validate route"});panel.Add(new Button(()=>{if(RaceRouteWindow.Active!=null)ToolManager.SetActiveTool<RaceRoutePickTool>();}){text="Pick lane anchor"});panel.Add(new Button(()=>RaceRouteWindow.Active?.Stop()){text="Stop test"});return panel;}
    }
}
