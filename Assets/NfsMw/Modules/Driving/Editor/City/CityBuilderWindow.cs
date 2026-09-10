using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving.Editor
{
    public sealed class CityBuilderWindow : EditorWindow
    {
        private static readonly string[] Views={"Rockport Map","Districts","Blocks","Parcels","Structures","Dressing","Streaming","Preview","Validation"};
        [SerializeField] private CityDistrict district;
        [SerializeField] private CityStyle style;
        [SerializeField] private int view;
        [SerializeField] private string blockId,parcelId;
        [SerializeField] private bool handles=true,showCells,showAccess=true;
        [SerializeField] private Vector2 cutA,cutB=Vector2.up*100;
        [SerializeField] private Terrain terrain;
        private List<CityPolygon> candidates=new List<CityPolygon>();
        private CityPlan plan;
        private CityPreview preview;
        private CityTerrainPreview terrainPreview;
        private ScrollView body;
        private HelpBox status;
        private ObjectField ownerField;
        private bool refreshQueued;
        private CityParcel Parcel => district==null?null:district.parcels.FirstOrDefault(p=>p.id==parcelId);
        private CityBlock Block => district==null?null:district.blocks.FirstOrDefault(b=>b.id==blockId);
        [MenuItem("NFS MW Remaster/World/City Builder",false,0)]
        public static void Open() => GetWindow<CityBuilderWindow>("World / City Builder");
        [MenuItem("Window/NFS MW Remaster/World & City Builder")]
        public static void OpenFromWindow() => Open();
        private void OnEnable()
        {
            minSize=new Vector2(760,500);SceneView.duringSceneGui+=SceneGUI;Undo.undoRedoPerformed+=SourceChanged;
            EditorApplication.playModeStateChanged+=PlayChanged;EditorSceneManager.sceneClosing+=SceneClosing;
        }
        private void OnDisable()
        {
            SceneView.duringSceneGui-=SceneGUI;Undo.undoRedoPerformed-=SourceChanged;EditorApplication.playModeStateChanged-=PlayChanged;
            EditorSceneManager.sceneClosing-=SceneClosing;EditorApplication.delayCall-=Refresh;StopPreview();
        }
        private void SceneClosing(Scene scene,bool removing) { if(district==null || district.gameObject.scene==scene) StopPreview(); }
        private void PlayChanged(PlayModeStateChange state) { if(state==PlayModeStateChange.ExitingEditMode) StopPreview(); }
        private void SourceChanged() { StopPreview();plan=null;QueueRefresh();SceneView.RepaintAll(); }
        private void QueueRefresh() { if(refreshQueued) return; refreshQueued=true;EditorApplication.delayCall+=Refresh; }
        private void Refresh() { refreshQueued=false;if(body!=null) DrawView(); }
        public void CreateGUI()
        {
            rootVisualElement.Clear();rootVisualElement.style.backgroundColor=new Color(0.12f,0.13f,0.15f);
            var heading=new VisualElement();heading.style.paddingLeft=18;heading.style.paddingTop=14;heading.style.paddingBottom=12;
            var title=new Label("ROCKPORT  /  WORLD & CITY BUILDER");title.style.fontSize=20;title.style.unityFontStyleAndWeight=FontStyle.Bold;heading.Add(title);
            var sub=new Label("Assemble the authored city. Shape districts. Review every regeneration.");sub.style.color=new Color(0.67f,0.72f,0.76f);heading.Add(sub);rootVisualElement.Add(heading);
            var toolbar=new Toolbar();
            var owner=new ObjectField("District") { objectType=typeof(CityDistrict),allowSceneObjects=true,value=district };owner.style.minWidth=300;
            ownerField=owner;
            owner.RegisterValueChangedCallback(e=> {StopPreview();district=e.newValue as CityDistrict;plan=null;DrawView();});toolbar.Add(owner);
            toolbar.Add(new ToolbarButton(()=>Run(()=> { if(district==null) throw new ArgumentException("Choose a district.");Selection.activeGameObject=district.gameObject;SceneView.lastActiveSceneView?.FrameSelected(); })) { text="Frame" });
            var toggle=new ToolbarToggle { text="Scene handles",value=handles };toggle.RegisterValueChangedCallback(e=>{handles=e.newValue;SceneView.RepaintAll();});toolbar.Add(toggle);
            toolbar.Add(new ToolbarButton(()=> {view=7;DrawView();}) {text="Preview / Commit"});rootVisualElement.Add(toolbar);
            var split=new TwoPaneSplitView(0,160,TwoPaneSplitViewOrientation.Horizontal);split.style.flexGrow=1;
            var nav=new VisualElement();nav.style.paddingTop=10;nav.style.paddingLeft=8;nav.style.paddingRight=8;
            for(int i=0;i<Views.Length;i++)
            {
                int index=i;var button=new Button(()=> {view=index;DrawView();}) {text=Views[i]};button.name="city-view-"+i;
                button.style.height=34;button.style.unityTextAlign=TextAnchor.MiddleLeft;nav.Add(button);
            }
            var guide=new Button(()=>AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/NfsMw/Modules/Driving/CITY_BUILDER_GUIDE.md"))) {text="Usage guide"};nav.Add(guide);
            split.Add(nav);body=new ScrollView();body.style.paddingLeft=18;body.style.paddingRight=18;body.style.paddingTop=14;split.Add(body);rootVisualElement.Add(split);
            status=new HelpBox("Ready. Opening this workspace does not generate or modify scene content.",HelpBoxMessageType.Info);rootVisualElement.Add(status);DrawView();
        }
        private void Title(string text,string detail=null)
        {
            var title=new Label(text);title.style.fontSize=18;title.style.unityFontStyleAndWeight=FontStyle.Bold;title.style.marginBottom=8;body.Add(title);
            if(detail!=null) Info(detail);
        }
        private void Info(string text,HelpBoxMessageType type=HelpBoxMessageType.Info) => body.Add(new HelpBox(text,type));
        private void Button(string text,Action action)
        {var button=new Button(()=>Run(action)) {text=text};button.style.minHeight=28;button.style.marginTop=4;body.Add(button);}
        private void Run(Action action)
        {
            try { var previous=status.text; action();if(status.text==previous){status.text="Done. "+(plan==null?"":plan.instances.Count+" planned instances.");status.messageType=HelpBoxMessageType.Info;} }
            catch(OperationCanceledException) {status.text="Cancelled. Previously committed content is unchanged.";status.messageType=HelpBoxMessageType.Info;}
            catch(Exception e) {status.text=e.Message;status.messageType=HelpBoxMessageType.Error;}
            finally {EditorUtility.ClearProgressBar();QueueRefresh();}
        }
        private void DrawView()
        {
            ownerField?.SetValueWithoutNotify(district);
            body.Clear();view=Mathf.Clamp(view,0,Views.Length-1);
            if(view==0) { DrawMap();return; }
            if(view==1) { DrawDistricts();return; }
            if(district==null) {Title(Views[view]);Info("Select a district above, or create one in Districts.");return;}
            switch(view) {case 2:DrawBlocks();break;case 3:DrawParcels();break;case 4:DrawStructures();break;case 5:DrawDressing();break;case 6:DrawStreaming();break;case 7:DrawPreview();break;case 8:DrawValidation();break;}
        }
        private void DrawMap()
        {
            Title("Rockport free-roam map","RockportMap is the playable entry scene. The world is split into 39 terrain-aligned additive cells; the ocean, player, camera, traffic, missions, and services remain in the entry scene.");
            Button("Open Rockport free-roam scene",()=>EditorSceneManager.OpenScene(RockportStreamingMigration.EntryScenePath));
            Button("Build or refresh streaming cells",RockportStreamingMigration.BuildOrRefresh);
            Button("Validate streaming map",RockportStreamingMigration.Validate);
            Title("Local art editing","Select scene mesh groups in the Hierarchy. Exact mesh collision is intended for static environment meshes; each operation is undoable and limited to 2000 new colliders.");
            Button("Add exact collision to selected mesh groups",()=>CityMapLibrary.AddSelectedCollision(Selection.gameObjects,false));
            Button("Add box proxies to selected mesh groups",()=>CityMapLibrary.AddSelectedCollision(Selection.gameObjects,true));
            Button("Capture selected city object as a reusable kit",()=>CityPresets.CaptureSelection());
        }
        private void ObjectField(string label,UnityEngine.Object value,Type type,bool scene,Action<UnityEngine.Object> change)
        {
            var field=new ObjectField(label){value=value,objectType=type,allowSceneObjects=scene};field.RegisterValueChangedCallback(e=>change(e.newValue));body.Add(field);
        }
        private void DrawDistricts()
        {
            Title("Districts","Choose a composable style, or use the existing city parts as handcrafted surroundings. District source and generated output are separate.");
            ObjectField("New district style",style,typeof(CityStyle),false,o=>style=o as CityStyle);
            Button("Create district at Scene view pivot",()=> {district=CityCommands.Create(style,SceneView.lastActiveSceneView==null?Vector3.zero:SceneView.lastActiveSceneView.pivot,SceneManager.GetActiveScene());Selection.activeGameObject=district.gameObject;});
            Button("Create sample styles and industrial kit",()=> {CityPresets.Ensure();style=AssetDatabase.LoadAssetAtPath<CityStyle>(CityPresets.Folder+"/Industrial.asset");});
            foreach(var d in UnityEngine.Object.FindObjectsByType<CityDistrict>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                Button(d.name+" · "+d.gameObject.scene.name+" · "+d.parcels.Count+" parcels",()=> {district=d;Selection.activeGameObject=d.gameObject;});
            if(district==null)return;
            Title("Selected district");Properties(district,"style","worldSeed","seed","roads","surfaceLevelTolerance","cellSize","boundary");
            Button("Refresh road publication from selected Road Network",()=>
            {
                var network=Selection.activeGameObject?.GetComponentInParent<RoadNetworkAuthoring>();
                if(network==null||network.Baked==null||!RoadNetworkBake.IsCurrent(network))throw new ArgumentException("Select a currently baked Road Network in the Hierarchy.");
                CityCommands.Edit(district,"Refresh city road dependency",()=>district.roads=network.Baked);SourceChanged();
            });
            Button("Duplicate district source with fresh identities",()=> {district=CityCommands.Duplicate(district,new Vector3(220,0,0));Selection.activeGameObject=district.gameObject;});
            Button("Frame district",()=>SceneView.lastActiveSceneView?.Frame(new Bounds(district.transform.position,new Vector3(CityGeometry.Bounds(district.boundary).width,50,CityGeometry.Bounds(district.boundary).height)),false));
        }
        private void Properties(UnityEngine.Object owner,params string[] names)
        {
            var serialized=new SerializedObject(owner);
            foreach(var name in names)
            {
                var property=serialized.FindProperty(name);if(property==null)continue;
                var field=new PropertyField(property);field.Bind(serialized);field.RegisterValueChangeCallback(_=> {StopPreview();plan=null;SceneView.RepaintAll();});body.Add(field);
            }
        }
        private void DrawBlocks()
        {
            Title("Blocks","Decode surface-level bounded faces from published road-band footprints. Open layouts and district-border faces are not fabricated into blocks. Candidates remain separate until approved.");
            Button("Decode candidate blocks",()=> {candidates=CityGeometry.DecodeBlocks(district);if(candidates.Count==0)status.text="No enclosed surface faces. Open roads, cul-de-sacs and elevated crossings can legitimately produce no block.";});
            Button("Add hand-authored rectangular block",()=> {var b=CityGeometry.Bounds(district.boundary);var p=CityPolygon.Rectangle(b.center.x-b.width*0.3f,b.center.y-b.height*0.3f,b.width*0.6f,b.height*0.6f);blockId=CityCommands.AddBlock(district,p).id;});
            for(int i=0;i<candidates.Count;i++)
            {var candidate=candidates[i];Button("Approve candidate "+(i+1)+" · "+CityGeometry.Area(candidate).ToString("F0")+" m²",()=> {blockId=CityCommands.AddBlock(district,candidate).id;candidates.Remove(candidate);});}
            foreach(var block in district.blocks) Button(block.label+" · "+CityGeometry.Area(block.polygon).ToString("F0")+" m²"+(block.locked?" · locked":""),()=> {blockId=block.id;SceneView.RepaintAll();});
            if(Block==null)return;
            int index=district.blocks.IndexOf(Block);Properties(district,"blocks.Array.data["+index+"]");
            Button("Subdivide selected block into parcels",()=>
            {
                if(Block.locked)throw new ArgumentException("Unlock the block before subdivision.");
                if(district.parcels.Any(p=>p.blockId==Block.id))throw new ArgumentException("This block already owns parcels; edit or split those parcels explicitly.");
                if(district.style==null)throw new ArgumentException("Choose a district style.");
                var polygons=CityGeometry.Subdivide(Block.polygon,district.style.targetParcelArea,district.style.minimumParcelArea);
                Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();foreach(var p in polygons)CityCommands.AddParcel(district,Block,p);Undo.CollapseUndoOperations(group);view=3;
            });
            Button("Use selected block as one parcel",()=> {parcelId=CityCommands.AddParcel(district,Block,Block.polygon).id;view=3;});
        }
        private void ParcelPicker()
        {
            var choices=district.parcels.Select(p=>p.label+" · "+p.id.Substring(0,Math.Min(6,p.id.Length))).ToList();
            if(choices.Count==0){Info("Approve a block and create parcels first.");return;}
            int index=Math.Max(0,district.parcels.FindIndex(p=>p.id==parcelId));parcelId=district.parcels[index].id;
            var picker=new PopupField<string>("Parcel",choices,index);picker.RegisterValueChangedCallback(e=> {parcelId=district.parcels[picker.index].id;QueueRefresh();SceneView.RepaintAll();});body.Add(picker);
        }
        private void DrawParcels()
        {
            Title("Parcels and access","Edit the selected outline with Scene handles. A lane anchor must be explicit; a nearby freeway or bridge is not automatically an entrance.");ParcelPicker();if(Parcel==null)return;
            Properties(district,"parcels.Array.data["+district.parcels.IndexOf(Parcel)+"]");
            var a=new Vector2Field("Cut start (local metres)"){value=cutA};a.RegisterValueChangedCallback(e=>cutA=e.newValue);body.Add(a);
            var b=new Vector2Field("Cut end (local metres)"){value=cutB};b.RegisterValueChangedCallback(e=>cutB=e.newValue);body.Add(b);
            Button("Cut selected parcel",()=>CityCommands.SplitParcel(district,Parcel,cutA,cutB));
            foreach(var other in district.parcels.Where(p=>p!=Parcel&&p.blockId==Parcel.blockId))
                Button("Merge with "+other.label,()=>CityCommands.MergeParcels(district,Parcel,other));
            if(district.roads!=null)
            {
                var lanes=district.roads.Lanes.Select(l=>l.Id+" · "+l.Class+" · "+l.Length.ToString("F0")+" m").ToList();
                if(lanes.Count>0)
                {
                    var lanePicker=new PopupField<string>("Explicit entrance lane",lanes,Math.Max(0,district.roads.Lanes.ToList().FindIndex(l=>l.Id==Parcel.entrance.laneId)));
                    lanePicker.RegisterValueChangedCallback(e=>CityCommands.Edit(district,"Anchor parcel entrance",()=>Parcel.entrance.laneId=district.roads.Lanes[lanePicker.index].Id));body.Add(lanePicker);
                    Button("Assign shown lane to entrance",()=>CityCommands.Edit(district,"Anchor parcel entrance",()=>Parcel.entrance.laneId=district.roads.Lanes[lanePicker.index].Id));
                }
            }
            Button("Check police van envelope (2.5 m × 3.2 m)",()=> {var diagnostics=new List<CityDiagnostic>();bool valid=CityPlanning.CheckAccess(district,Parcel,diagnostics);status.text=valid?"Lane anchor, authored grade and dimensions pass. Review physical collision and turning manoeuvre in Play Mode.":string.Join("\n",diagnostics);});
            Button("Propose service road through Road Editor",()=>CityPresets.ProposeServiceRoad(district,Parcel));
        }
        private void DrawStructures()
        {
            Title("Structures and manual art direction","Use authored map objects/prefabs or a modular exterior kit. Pins preserve approved instances; detachment transfers ownership and prevents that generator key from respawning.");ParcelPicker();
            if(Parcel!=null)
            {
                ObjectField("Parcel kit",Parcel.kit,typeof(CityKit),false,o=>CityCommands.Edit(district,"Assign city kit",()=>Parcel.kit=o as CityKit));
                var kit=Parcel.kit!=null?Parcel.kit:district.style?.kit;
                if(kit!=null)Properties(kit,"exteriorPrefab","prefabDimensions","wall","roof","trim","glass","yard","bayWidth","floorHeight","minimumWidth","minimumDepth","maximumFloors","roofFamily","loadingDock","roofDressing","collision");
            }
            Button("Capture selected map object as kit",()=>CityPresets.CaptureSelection());
            var selected=Selection.gameObjects.Select(g=>g.GetComponent<CityGeneratedInstance>()).Where(i=>i!=null&&i.districtId==district.id).ToArray();
            Info(selected.Length+" generated instances selected. Select multiple in the Hierarchy to apply an ownership state together.");
            foreach(CityOwnership state in Enum.GetValues(typeof(CityOwnership)))
                Button(state==CityOwnership.Generated?"Release overrides / return to generated":"Set selected: "+state,()=>
                {Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();foreach(var item in selected)CityCommands.SetOwnership(district,item,state);Undo.CollapseUndoOperations(group);SourceChanged();});
            foreach(var record in district.overrides.ToArray())
            {Info(record.state+" · "+record.key);Button("Release record "+record.key,()=>{var existing=CityCommands.Existing(district);if(existing.TryGetValue(record.key,out var instance))CityCommands.SetOwnership(district,instance,CityOwnership.Generated);else CityCommands.Edit(district,"Release city override",()=>district.overrides.Remove(record));SourceChanged();});}
        }
        private void DrawDressing()
        {
            Title("Dressing, yards and grading","Parcel-owned dressing avoids generated structures, entrance envelopes and reservations. Road-owned signals, lamps and barriers remain owned by the Road Editor.");
            if(district.style!=null)Properties(district.style,"dressingSpacing","dressingPerParcel");
            ParcelPicker();if(Parcel!=null)
            {
                var kit=Parcel.kit!=null?Parcel.kit:district.style?.kit;if(kit!=null)Properties(kit,"dressing");
                Button("Reserve the selected parcel as a protected zone",()=>CityCommands.Edit(district,"Add city reservation",()=>district.reservations.Add(new CityReservation {id=Guid.NewGuid().ToString("N"),label=Parcel.label+" reservation",polygon=Parcel.polygon.Copy(),minimumHeight=Parcel.padHeight,maximumHeight=Parcel.padHeight+10})));
            }
            Properties(district,"reservations");ObjectField("Grading preview terrain",terrain,typeof(Terrain),true,o=>terrain=o as Terrain);
            Button("Preview parcel grading on a temporary terrain copy",()=> {terrainPreview?.Dispose();terrainPreview=new CityTerrainPreview(district,terrain,p=>EditorUtility.DisplayCancelableProgressBar("City grading preview","Original terrain remains unchanged",p));status.text=$"Preview cut {terrainPreview.CutCubicMetres:F0} m³ · fill {terrainPreview.FillCubicMetres:F0} m³";});
            Button("Discard grading preview",()=> {terrainPreview?.Dispose();terrainPreview=null;});
            Info("Grading is a visual estimate only. This project has no terrain-composition owner, so the tool does not write permanent heights or vegetation changes.");
        }
        private void DrawStreaming()
        {
            Title("Streaming cells and output budgets","The playable Rockport free-roam build uses additive section scenes and loads nearby chunks around the player. This authoring view reports generation cells and budgets; it does not replace the runtime RockportWorldStreamer.");
            Properties(district,"cellSize");if(district.style!=null)Properties(district.style,"maximumInstancesPerCell","maximumCollidersPerCell","maximumVerticesPerCell");
            var toggle=new Toggle("Draw cell grid"){value=showCells};toggle.RegisterValueChangedCallback(e=> {showCells=e.newValue;SceneView.RepaintAll();});body.Add(toggle);
            Button("Calculate generation and cell report",BuildPlan);
            if(plan!=null) foreach(var group in plan.instances.SelectMany(i=>i.cells.Select(c=>(cell:c,item:i))).GroupBy(p=>p.cell).OrderBy(g=>g.Key.x).ThenBy(g=>g.Key.y))
                Info($"Cell {group.Key.x}, {group.Key.y} · {group.Count()} instances · {group.Sum(g=>g.item.vertices):N0} estimated vertices · {group.Count(g=>g.item.collision)} collision instances");
            Button("Export source / generation report",()=>CityPresets.ExportReport(district,plan));
        }
        private void BuildPlan()
        {
            StopPreview();plan=CityPlanning.Build(district,p=>EditorUtility.DisplayCancelableProgressBar("Plan city district","Evaluating parcels and ownership",p));
            if(plan.Valid)preview=new CityPreview(district,plan);
        }
        private void StopPreview() {preview?.Dispose();preview=null;terrainPreview?.Dispose();terrainPreview=null;}
        private void DrawPreview()
        {
            Title("Preview, diff and commit","Transient previews have no collision. Existing output stays usable until a validated, current plan is committed. Cancellation discards only this transaction's staged work.");
            Button("Generate reversible preview",BuildPlan);Button("Cancel / close preview",StopPreview);
            if(plan==null)return;
            Info($"{plan.instances.Count:N0} instances · {plan.milliseconds:F1} ms planning · revision {plan.fingerprint?.Substring(0,Math.Min(12,plan.fingerprint.Length))}");
            foreach(var error in plan.diagnostics)Info(error.ToString(),error.severity==CitySeverity.Error?HelpBoxMessageType.Error:HelpBoxMessageType.Warning);
            var diff=CityCommands.Diff(district,plan);Title(diff.ToString());
            foreach(var group in new[]{("Create",diff.create),("Update",diff.update),("Delete",diff.delete),("Preserve",diff.preserve)})
            {var fold=new Foldout{text=group.Item1+" ("+group.Item2.Count+")",value=false};foreach(var key in group.Item2.Take(250))fold.Add(new Label(key));body.Add(fold);}
            Button("Commit this reviewed plan",()=>
            {
                if(!plan.Valid)throw new ArgumentException("Resolve validation errors first.");
                string path=EditorUtility.SaveFilePanelInProject("Publish city district",district.name+" City","asset","Choose an immutable publication revision.");if(string.IsNullOrEmpty(path))return;
                StopPreview();CityCommands.Commit(district,plan,path,p=>EditorUtility.DisplayCancelableProgressBar("Commit city district","Staging approved changes",p));
            });
        }
        private void DrawValidation()
        {
            Title("Validation and integration","Diagnostics distinguish invalid content from missing or unvalidated upstream capabilities. Select an owner to inspect its source.");
            Button("Run source, output and access validation",()=> {plan=CityPlanning.Build(district);plan.diagnostics.AddRange(CityBuildGuard.Validate(district));});
            Button("Export validation report",()=>CityPresets.ExportReport(district,plan));
            if(plan!=null)foreach(var diagnostic in plan.diagnostics)
            {Info(diagnostic.ToString()+"\nOwner: "+diagnostic.owner+"\n"+diagnostic.action,diagnostic.severity==CitySeverity.Error?HelpBoxMessageType.Error:HelpBoxMessageType.Warning);Button("Inspect owner",()=> {parcelId=diagnostic.owner;view=3;Selection.activeGameObject=district.gameObject;});}
            if(plan!=null&&plan.diagnostics.Count==0)Info("No diagnostics in the evaluated loaded district. This does not validate unloaded scenes.");
        }
        private void SceneGUI(SceneView scene)
        {
            if(district==null||EditorApplication.isPlayingOrWillChangePlaymode)return;
            void Ring(IReadOnlyList<Vector2> points,Color color,float height=0)
            {if(points==null||points.Count<2)return;Handles.color=color;var line=points.Select(p=>district.ToWorld(p,height)).Concat(new[]{district.ToWorld(points[0],height)}).ToArray();Handles.DrawAAPolyLine(3,line);}
            Ring(district.boundary.outline,new Color(1,0.65f,0.18f));
            foreach(var block in district.blocks)Ring(block.polygon.outline,block.id==blockId?Color.cyan:new Color(0.2f,0.5f,0.65f));
            foreach(var p in district.parcels){Ring(p.polygon.outline,p.id==parcelId?Color.yellow:new Color(0.4f,0.7f,0.45f),p.padHeight);foreach(var h in p.polygon.holes)Ring(h.points,Color.red,p.padHeight);}
            foreach(var candidate in candidates)Ring(candidate.outline,Color.magenta,0.2f);
            foreach(var reservation in district.reservations)Ring(reservation.polygon.outline,Color.red,reservation.minimumHeight);
            if(showAccess)foreach(var p in district.parcels.Where(p=>p.entrance.enabled))
            {Ring(CityPlanning.AccessEnvelope(p).outline,Color.green,p.entrance.localPosition.y);Handles.Label(district.transform.position+p.entrance.localPosition,p.label+" · "+p.entrance.width+"m × "+p.entrance.height+"m");}
            if(showCells)
            {
                var bounds=CityGeometry.Bounds(district.boundary);float size=Mathf.Max(10,district.cellSize);Handles.color=new Color(0.5f,0.6f,0.7f,0.4f);
                if(bounds.width/size<200 && bounds.height/size<200)
                {
                    for(float x=Mathf.Floor(bounds.xMin/size)*size;x<=bounds.xMax;x+=size)Handles.DrawLine(district.ToWorld(new Vector2(x,bounds.yMin)),district.ToWorld(new Vector2(x,bounds.yMax)));
                    for(float z=Mathf.Floor(bounds.yMin/size)*size;z<=bounds.yMax;z+=size)Handles.DrawLine(district.ToWorld(new Vector2(bounds.xMin,z)),district.ToWorld(new Vector2(bounds.xMax,z)));
                }
            }
            var polygon=view==3?Parcel?.polygon:view==2?Block?.polygon:view==1?district.boundary:null;
            bool locked=view==3?Parcel?.locked??true:view==2?Block?.locked??true:false;
            if(!handles||polygon==null||locked)return;
            for(int i=0;i<polygon.outline.Count;i++)
            {
                var before=district.ToWorld(polygon.outline[i],Parcel!=null&&view==3?Parcel.padHeight:0);
                EditorGUI.BeginChangeCheck();var after=Handles.PositionHandle(before,Quaternion.identity);
                if(EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(district,"Move city polygon vertex");polygon.outline[i]=district.ToLocal(after);CityCommands.Changed(district);StopPreview();plan=null;
                }
            }
        }
    }
}
