using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.UIElements;
namespace NfsMwRemaster.Driving.Editor
{
    public sealed class GrimePainterWindow : EditorWindow
    {
        [SerializeField] GrimeCanvas canvas;
        [SerializeField] GrimeBrush brush;
        [SerializeField] GrimeReceiver receiver;
        [SerializeField] RoadNetworkAsset network;
        [SerializeField] int tab, layerIndex, laneIndex, seed=123;
        [SerializeField] float width=1, rotation, start, end=10, lateral;
        [SerializeField] bool erase;
        Vector2 scroll; int scatterCount=50; float scatterRadius=5; GrimeStroke[] scatterPreview; string message="Choose a canvas, brush and approved receiver.";
        readonly Dictionary<Renderer,bool> hiddenOutput=new Dictionary<Renderer,bool>();
        GrimeBuild preview; GrimeStroke rangePreview; List<string> remap;
        readonly HashSet<string> selected=new HashSet<string>();
        void OnLostFocus(){ GrimePainterTool.Discard(); }
        public static GrimePainterWindow Active {get;private set;}
        public GrimeCanvas Canvas=>canvas; public GrimeBrush Brush=>brush; public GrimeReceiver Receiver=>receiver;
        public float Width {get=>width;set=>width=Mathf.Clamp(value,.05f,20);} public float Rotation {get=>rotation;set=>rotation=value;}
        public bool Erase=>erase;
        public GrimeLayer Layer=>canvas && canvas.layers.Count>0?canvas.layers[Mathf.Clamp(layerIndex,0,canvas.layers.Count-1)]:null;
        [MenuItem("Tools/NFS MW/Surface Dressing/Grime Painter")]
        public static void Open() { var window=GetWindow<GrimePainterWindow>("Grime Painter");var source=Selection.activeGameObject?Selection.activeGameObject.GetComponent<GrimeCanvas>():null;if(source&&window.canvas!=source){window.Cleanup();window.canvas=source;}window.Show(); }
        public void CreateGUI()
        {
            var title=new Label("SURFACE DRESSING"); title.style.fontSize=19; title.style.marginLeft=10; title.style.marginTop=10; rootVisualElement.Add(title);
            var note=new Label("Static art • approved receivers • reversible source"); note.style.marginLeft=10; rootVisualElement.Add(note);
            var content=new IMGUIContainer(Draw);content.style.flexGrow=1;rootVisualElement.Add(content);
        }
        void OnEnable() {minSize=new Vector2(580,640);Active=this; SceneView.duringSceneGui+=Scene; Undo.undoRedoPerformed+=Invalidate; EditorApplication.playModeStateChanged+=Play; AssemblyReloadEvents.beforeAssemblyReload+=Cleanup;Undo.postprocessModifications+=Modified;}
        void OnDisable() {Cleanup(); SceneView.duringSceneGui-=Scene;Undo.undoRedoPerformed-=Invalidate;EditorApplication.playModeStateChanged-=Play;AssemblyReloadEvents.beforeAssemblyReload-=Cleanup;Undo.postprocessModifications-=Modified;if(Active==this)Active=null;}
        UndoPropertyModification[] Modified(UndoPropertyModification[] changes){ClearPreview();rangePreview=null;remap=null;return changes;}
        void Play(PlayModeStateChange state)=>Cleanup();
        void Cleanup() { GrimePainterTool.Discard(); ClearPreview(); rangePreview=null; scatterPreview=null; remap=null; }
        void ClearPreview() {foreach(var pair in hiddenOutput)if(pair.Key)pair.Key.forceRenderingOff=pair.Value;hiddenOutput.Clear();if(preview==null)return;preview.Dispose();preview=null;SceneView.RepaintAll();}
        void Invalidate() {Cleanup();Repaint();}
        public void Status(string value) {message=value;Repaint();}
        public GrimeStroke NewStroke() => new GrimeStroke{brush=brush,brushRevision=GrimeGeometry.BrushRevision(brush),layerId=Layer.id,width=width,rotation=rotation,seed=seed};
        void Run(Action action) { try {action();} catch(Exception ex) {message=ex.Message;} finally {EditorUtility.ClearProgressBar();Repaint();} }
        void Button(string label,Action action) {if(GUILayout.Button(label))Run(action);}
        void Draw()
        {
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUI.BeginChangeCheck(); var next=(GrimeCanvas)EditorGUILayout.ObjectField("Canvas",canvas,typeof(GrimeCanvas),true);
                if(EditorGUI.EndChangeCheck()) {Cleanup();canvas=next;selected.Clear();}
                using(new EditorGUILayout.HorizontalScope()) {Button("New canvas",()=>canvas=GrimeCommands.Create());Button("Use selected",()=>canvas=Selection.activeGameObject?.GetComponent<GrimeCanvas>());}
                tab=GUILayout.Toolbar(tab,new[]{"Paint","Layers","Masks","Roads","Inspect","Publish"});
                scroll=EditorGUILayout.BeginScrollView(scroll,GUILayout.MinHeight(300));
                if(canvas)
                {
                    if(tab==0) Paint(); if(tab==1) Layers(); if(tab==2) Masks(); if(tab==3) Roads(); if(tab==4) Inspect(); if(tab==5) Publish();
                }
                else EditorGUILayout.HelpBox("Create or choose a surface dressing canvas. Source stays in the scene; baked output is a separate asset.",MessageType.Info);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.HelpBox(message,MessageType.None);
            }
        }
        void Paint()
        {
            EditorGUI.BeginChangeCheck(); brush=(GrimeBrush)EditorGUILayout.ObjectField("Brush",brush,typeof(GrimeBrush),false); receiver=(GrimeReceiver)EditorGUILayout.ObjectField("Exact receiver",receiver,typeof(GrimeReceiver),true);
            if(EditorGUI.EndChangeCheck())Cleanup();
            using(new EditorGUILayout.HorizontalScope()) {Button("Register selected meshes",()=> { foreach(var go in Selection.gameObjects)receiver=GrimeCommands.Register(go); });Button("Create brush presets",()=>GrimeDemo.CreateBrushes());}
            if(brush) { using(new EditorGUILayout.HorizontalScope()) {Button("Inspect brush",()=>Selection.activeObject=brush);Button("Use brush size",()=>width=brush.width);} }
            width=EditorGUILayout.Slider("Width (m)",width,.05f,20);rotation=EditorGUILayout.Slider("Rotation",rotation,-180,180);seed=EditorGUILayout.IntField("Seed",seed);
            if(canvas.layers.Count>0)layerIndex=EditorGUILayout.Popup("Layer",Mathf.Clamp(layerIndex,0,canvas.layers.Count-1),canvas.layers.Select(l=>l.name+(l.locked?" [locked]":"")).ToArray());
            erase=EditorGUILayout.Toggle("Erase whole strokes",erase);
            Button("Activate Scene brush",()=> {if(!brush||!receiver||Layer==null||Layer.locked||!Layer.enabled)throw new InvalidOperationException("Choose a brush, receiver and enabled unlocked layer.");ToolManager.SetActiveTool<GrimePainterTool>();});
            EditorGUILayout.HelpBox("Drag LMB to paint; click to stamp. [ / ] size, Shift + wheel rotation, Escape discards. Eraser removes entire touched strokes. A stroke stays on its chosen receiver. Release LMB to commit one Undo operation.",MessageType.Info);
            Button("Validate / preview canvas",Preview);
            Button("Clear preview",ClearPreview);
            EditorGUILayout.LabelField("Seeded region stamps",EditorStyles.boldLabel);
            scatterCount=EditorGUILayout.IntSlider("Candidate count",scatterCount,1,1000);scatterRadius=EditorGUILayout.Slider("Region radius (m)",scatterRadius,.5f,30);
            Button("Preview seeded stamps at Scene pivot",()=>
            {
                if(!brush||!receiver||Layer==null||Layer.locked)throw new InvalidOperationException("Choose brush, receiver and unlocked layer.");
                var pivot=SceneView.lastActiveSceneView?SceneView.lastActiveSceneView.pivot:receiver.transform.position;var candidates=new List<GrimeStroke>();
                for(int i=0;i<scatterCount;i++){float angle=GrimeGeometry.Noise(seed,"region",i,0)*Mathf.PI*2;float radius=Mathf.Sqrt(GrimeGeometry.Noise(seed,"region",i,1))*scatterRadius;var p=pivot+receiver.transform.right*Mathf.Cos(angle)*radius+receiver.transform.forward*Mathf.Sin(angle)*radius;var n=receiver.transform.up;
                    if(receiver.surface.Raycast(new Ray(p+n*brush.projectionDepth,-n),out var hit,brush.projectionDepth*2)){var stroke=NewStroke();stroke.id=Hash128.Compute(canvas.id+"/region/"+seed+"/"+i).ToString();stroke.samples.Add(GrimeGeometry.Capture(receiver,hit,receiver.transform.forward));candidates.Add(stroke);}}
                scatterPreview=candidates.ToArray();message=$"{scatterPreview.Length} of {scatterCount} seeded candidates hit the explicit receiver. Masks are applied when validating the canvas.";SceneView.RepaintAll();
            });
            using(new EditorGUI.DisabledScope(scatterPreview==null))Button("Commit region preview",()=>{if(scatterPreview.Any(s=>canvas.strokes.Any(old=>old.id==s.id)))throw new InvalidOperationException("This seed region is already present; choose a different seed.");GrimeCommands.Edit(canvas,"Add procedural dressing region",()=>canvas.strokes.AddRange(scatterPreview));scatterPreview=null;});
            EditorGUILayout.LabelField("Receiver filters",EditorStyles.boldLabel);
            var so=new SerializedObject(canvas);so.Update(); foreach(var key in new[]{"physicsLayers","renderingLayers","requiredCategory","requiredMaterial","maximumSlope"})EditorGUILayout.PropertyField(so.FindProperty(key)); if(so.ApplyModifiedProperties())ClearPreview();
            if(receiver)EditorGUILayout.HelpBox(GrimeGeometry.ReceiverAllowed(canvas,receiver,out var why)?"Receiver approved. Footprint depth and normal tests run during projection.":why,MessageType.Info);
        }
        void Layers()
        {
            Button("Add layer",()=>GrimeCommands.Edit(canvas,"Add grime layer",()=>canvas.layers.Add(new GrimeLayer{name="Layer "+(canvas.layers.Count+1)})));
            for(int i=0;i<canvas.layers.Count;i++)
            {
                int index=i;var l=canvas.layers[i];
                using(new EditorGUILayout.HorizontalScope())
                {
                    bool enabled=GUILayout.Toggle(l.enabled,"",GUILayout.Width(20));string name=EditorGUILayout.TextField(l.name);bool locked=GUILayout.Toggle(l.locked,"Lock",GUILayout.Width(45));
                    if(enabled!=l.enabled||name!=l.name||locked!=l.locked)GrimeCommands.Edit(canvas,"Edit grime layer",()=>{l.enabled=enabled;l.name=name;l.locked=locked;});
                    Button("Solo",()=>GrimeCommands.Edit(canvas,"Solo grime layer",()=>{foreach(var other in canvas.layers)other.enabled=other==l;}));
                    if(index>0)Button("↑",()=>GrimeCommands.Edit(canvas,"Reorder grime layer",()=>{canvas.layers.RemoveAt(index);canvas.layers.Insert(index-1,l);}));
                }
                float opacity=EditorGUILayout.Slider("Opacity",l.opacity,0,1);if(opacity!=l.opacity)GrimeCommands.Edit(canvas,"Layer opacity",()=>l.opacity=opacity);
            }
            Button("Show all layers",()=>GrimeCommands.Edit(canvas,"Show layers",()=>{foreach(var l in canvas.layers)l.enabled=true;}));
            EditorGUILayout.Space();EditorGUILayout.LabelField("Stroke history / selection",EditorStyles.boldLabel);
            foreach(var s in canvas.strokes.ToArray())
            {
                using(new EditorGUILayout.HorizontalScope())
                {
                    bool pick=GUILayout.Toggle(selected.Contains(s.id),(s.brush?s.brush.label:"Missing brush")+" • "+s.samples.Count+" anchors • "+s.id.Substring(0,Math.Min(8,s.id.Length)));if(pick)selected.Add(s.id);else selected.Remove(s.id);
                    bool enabled=GUILayout.Toggle(s.enabled,"On",GUILayout.Width(35));if(enabled!=s.enabled&&!Locked(s))GrimeCommands.Edit(canvas,"Toggle stroke",()=>s.enabled=enabled);
                }
            }
            Button("Duplicate selected with fresh IDs",()=>GrimeCommands.Edit(canvas,"Duplicate dressing strokes",()=>{foreach(var s in canvas.strokes.Where(s=>selected.Contains(s.id)&&!Locked(s)).ToArray()){var copy=JsonUtility.FromJson<GrimeStroke>(JsonUtility.ToJson(s));copy.id=Guid.NewGuid().ToString("N");canvas.strokes.Add(copy);}}));
            Button("Erase selected",()=>GrimeCommands.Edit(canvas,"Erase dressing strokes",()=>canvas.strokes.RemoveAll(s=>selected.Contains(s.id)&&!Locked(s))));
            Button("Accept current brush revisions (selected)",()=>GrimeCommands.Edit(canvas,"Accept brush revisions",()=>{foreach(var s in canvas.strokes.Where(s=>selected.Contains(s.id)&&!Locked(s)))s.brushRevision=GrimeGeometry.BrushRevision(s.brush);}));
            Button("Pin selected anchors at stored world positions",()=>GrimeCommands.Edit(canvas,"Pin dressing in world",()=>{foreach(var s in canvas.strokes.Where(s=>selected.Contains(s.id)&&!Locked(s)))foreach(var a in s.samples)a.kind=GrimeAnchorKind.World;}));
        }
        bool Locked(GrimeStroke s)=>canvas.layers.FirstOrDefault(l=>l.id==s.layerId)?.locked??true;
        void Masks()
        {
            Button("Add protection at Scene pivot",()=>GrimeCommands.Edit(canvas,"Add protection mask",()=>canvas.masks.Add(new GrimeMask{origin=SceneView.lastActiveSceneView?SceneView.lastActiveSceneView.pivot:Vector3.zero})));
            Button("Protect selected activity entrance",()=>
            {
                var activity=Selection.activeGameObject?.GetComponent<EventPlacementSource>();if(!activity||!EventPlacementCompiler.Resolve(activity.anchor,out var pose,out var error))throw new InvalidOperationException("Select a resolvable activity source.");
                var size=activity.triggerSize;GrimeCommands.Edit(canvas,"Protect activity entrance",()=>canvas.masks.Add(new GrimeMask{name="Activity entrance "+activity.name,origin=pose.position+pose.rotation*activity.interactionOffset,euler=pose.rotation.eulerAngles,depth=Mathf.Max(2,size.y),polygon=new List<Vector2>{new Vector2(-size.x/2,-size.z/2),new Vector2(size.x/2,-size.z/2),new Vector2(size.x/2,size.z/2),new Vector2(-size.x/2,size.z/2)}}));
            });
            EditorGUILayout.HelpBox("Inclusions intersect; exclusions win. Use convex inclusion polygons. Exclusions conservatively protect their oriented bounding rectangles, including thin markings. Masks are appearance protection, not legal road markings. Scene handles edit mask origins and vertices.",MessageType.Info);
            var so=new SerializedObject(canvas);so.Update();EditorGUILayout.PropertyField(so.FindProperty("masks"),true);if(so.ApplyModifiedProperties())ClearPreview();
        }
        void Roads()
        {
            EditorGUI.BeginChangeCheck();network=(RoadNetworkAsset)EditorGUILayout.ObjectField("Authoritative network",network,typeof(RoadNetworkAsset),false);
            if(network&&network.Lanes.Count>0)laneIndex=EditorGUILayout.Popup("Exact lane",Mathf.Clamp(laneIndex,0,network.Lanes.Count-1),network.Lanes.Select(l=>l.Id.ToString()).ToArray());
            start=EditorGUILayout.FloatField("Start station",start);end=EditorGUILayout.FloatField("End station",end);lateral=EditorGUILayout.FloatField("Lateral offset (+left)",lateral);
            if(EditorGUI.EndChangeCheck()){rangePreview=null;remap=null;}
            EditorGUILayout.HelpBox("Uses the Paint tab's brush, receiver and seed. Lateral offsets author lane-center, shoulder or seam wear. Procedural art does not imply simulated traffic history.",MessageType.Info);
            Button("Preview station range",()=> {rangePreview=GrimeCommands.RoadRange(canvas,brush,receiver,network,network.Lanes[laneIndex].Id.ToString(),start,end,lateral,seed);if(Layer!=null&&!Layer.locked)rangePreview.layerId=Layer.id;message=$"{rangePreview.samples.Count} road anchors. Cyan line previews the range; commit then validate footprint coverage.";SceneView.RepaintAll();});
            using(new EditorGUI.DisabledScope(rangePreview==null))Button("Commit previewed range",()=>{GrimeCommands.Add(canvas,rangePreview);rangePreview=null;});
            Button("Preview exact-ID remapping",()=>{remap=GrimeCommands.PreviewRemap(canvas,network);message=string.Join("\n",remap.Take(12));SceneView.RepaintAll();});
            using(new EditorGUI.DisabledScope(remap==null))Button("Accept reviewed road revision",()=>{GrimeCommands.Remap(canvas,network);remap=null;ClearPreview();});
        }
        void Inspect()
        {
            EditorGUILayout.HelpBox("Backend: HDRP conforming mesh decals. No renderer feature required. Opaque static MeshRenderer only. Unsupported: transparent receivers, skinned/moving geometry, texture atlases, tablet pressure, semantic grip changes. Normal channel shades the decal surface; it does not modify the underlying material normal.",MessageType.Info);
            Button("Rebuild diagnostics and preview",Preview);
            if(preview!=null)
            {
                EditorGUILayout.LabelField($"{preview.stamps} stamps / {preview.rejected} rejected / {preview.chunks.Count} mesh draws");
                EditorGUILayout.LabelField($"{preview.vertices:N0} vertices • {preview.milliseconds:F1} ms CPU build");
                EditorGUILayout.LabelField($"{preview.chunks.Select(c=>c.material).Distinct().Count()} shared materials • ~{preview.vertices*64/1048576f:F2} MiB vertex storage");
                var textures=canvas.strokes.Where(s=>s.brush).SelectMany(s=>new[]{s.brush.colorOpacity,s.brush.normalMap}).Where(t=>t).Distinct();long bytes=textures.Sum(t=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t));EditorGUILayout.LabelField($"Referenced textures: {bytes/1048576f:F2} MiB loaded • 0 projectors");
                var camera=SceneView.lastActiveSceneView?SceneView.lastActiveSceneView.camera:null;if(camera){var planes=GeometryUtility.CalculateFrustumPlanes(camera);EditorGUILayout.LabelField($"Scene frustum: {preview.chunks.Count(c=>GeometryUtility.TestPlanesAABB(planes,c.mesh.bounds))} chunk bounds visible");}
                foreach(var d in preview.diagnostics)EditorGUILayout.HelpBox(d,preview.valid?MessageType.Warning:MessageType.Error);
            }
            EditorGUILayout.HelpBox("Draws and memory are geometry estimates, not measured GPU cost. Transparent overlap depends on camera and resolution. Profile target builds in Unity Profiler; distance fade does not eliminate submission cost.",MessageType.Info);
            var so=new SerializedObject(canvas);so.Update();foreach(var key in new[]{"chunkSize","maximumStamps","maximumDraws"})EditorGUILayout.PropertyField(so.FindProperty(key));if(so.ApplyModifiedProperties())ClearPreview();
            Button("Frame dressing",()=>{Selection.activeGameObject=canvas.gameObject;SceneView.lastActiveSceneView?.FrameSelected();});
            Button("Grazing view",()=>{var view=SceneView.lastActiveSceneView;if(view)view.rotation=Quaternion.Euler(8,25,0);});
        }
        void Publish()
        {
            EditorGUILayout.HelpBox("Bake creates a new owned asset with shared meshes/materials. Previous assets remain recoverable; scene replacement supports Undo. Detach preserves hand-edited art. Save the scene before portable export so receiver references have stable scene IDs.",MessageType.Info);
            Button("Bake to new asset…",()=>{string path=EditorUtility.SaveFilePanelInProject("Bake grime","Grime_"+canvas.id.Substring(0,8),"asset","Choose a new file");if(path.Length>0){GrimeCommands.Bake(canvas,path);ClearPreview();message="Published mesh dressing. Source strokes retained.";}});
            Button("Assign fresh IDs to copied canvas",()=>GrimeCommands.FreshIdentity(canvas));
            Button("Detach previous generated art",()=>GrimeCommands.Edit(canvas,"Detach generated dressing",()=>{canvas.generatedRoot=null;canvas.published=null;}));
            Button("Export source library…",()=>{var path=EditorUtility.SaveFilePanel("Export dressing","","Dressing","json");if(path.Length>0)System.IO.File.WriteAllText(path,GrimeTransfer.Export(canvas));});
            Button("Import source library…",()=>{var path=EditorUtility.OpenFilePanel("Import dressing","","json");if(path.Length>0){Cleanup();canvas=GrimeTransfer.Import(System.IO.File.ReadAllText(path));}});
            Button("Build synthetic demonstration",()=>GrimeDemo.Build());
        }
        void Preview()
        {
            ClearPreview();preview=GrimeCompiler.Build(canvas,p=>EditorUtility.DisplayCancelableProgressBar("Preview grime","Projecting",p));if(preview.valid&&canvas.generatedRoot)foreach(var renderer in canvas.generatedRoot.GetComponentsInChildren<Renderer>(true)){hiddenOutput[renderer]=renderer.forceRenderingOff;renderer.forceRenderingOff=true;}message=preview.valid?$"Preview ready: {preview.stamps} stamps; {preview.rejected} rejected.":string.Join("\n",preview.diagnostics);SceneView.RepaintAll();
        }
        void Scene(SceneView view)
        {
            if(!canvas||EditorApplication.isPlayingOrWillChangePlaymode){ClearPreview();return;}
            if(preview!=null&&preview.valid&&Event.current.type==EventType.Repaint)foreach(var chunk in preview.chunks){chunk.material.SetPass(0);Graphics.DrawMeshNow(chunk.mesh,Matrix4x4.identity);}
            if(scatterPreview!=null){Handles.color=Color.cyan;foreach(var s in scatterPreview)Handles.DrawWireDisc(s.samples[0].position,s.samples[0].normal,s.width/2);}
            if(rangePreview!=null){Handles.color=Color.cyan;Handles.DrawAAPolyLine(rangePreview.samples.Select(a=>a.position).ToArray());}
            if(remap!=null&&network)foreach(var s in canvas.strokes)foreach(var a in s.samples.Where(a=>a.kind==GrimeAnchorKind.Road)){var lane=network.Lanes.FirstOrDefault(l=>l.Id.ToString()==a.laneId);if(lane!=null){var point=lane.Sample(a.station);Handles.color=Color.yellow;Handles.DrawLine(a.position,point.position+point.left*a.lateral);}}
            if(tab!=2)return;
            foreach(var mask in canvas.masks)
            {
                var rotation=Quaternion.Euler(mask.euler);Handles.color=mask.inclusion?Color.cyan:Color.red;
                EditorGUI.BeginChangeCheck();var origin=Handles.PositionHandle(mask.origin,rotation);if(EditorGUI.EndChangeCheck())GrimeCommands.Edit(canvas,"Move grime mask",()=>mask.origin=origin);
                for(int i=0;i<mask.polygon.Count;i++)
                {
                    var p=mask.polygon[i];var world=mask.origin+rotation*new Vector3(p.x,0,p.y);var next=mask.polygon[(i+1)%mask.polygon.Count];Handles.DrawLine(world,mask.origin+rotation*new Vector3(next.x,0,next.y));
                    EditorGUI.BeginChangeCheck();var moved=Handles.FreeMoveHandle(world,HandleUtility.GetHandleSize(world)*.06f,Vector3.zero,Handles.DotHandleCap);
                    if(EditorGUI.EndChangeCheck()){int index=i;var local=Quaternion.Inverse(rotation)*(moved-mask.origin);GrimeCommands.Edit(canvas,"Edit grime mask",()=>mask.polygon[index]=new Vector2(local.x,local.z));}
                }
            }
        }
    }
}
