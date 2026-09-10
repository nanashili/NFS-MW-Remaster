using System;
using System.Collections.Generic;
using System.Linq;
using NfsMwRemaster.Driving;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Maps.Editor
{
    public sealed class MapStudioWindow : EditorWindow
    {
        [SerializeField] private MapDefinition definition;
        [SerializeField] private int page,level,aspect;
        [SerializeField] private bool filterLevel,grid=true,sceneLinks=true;
        [SerializeField] private MapViewport viewport=new MapViewport();
        private static readonly string[] Pages={"Map","Sources","Levels","Style","Coordinates","Tiles","Markers","Validation","Delivery"};
        private Vector2 scroll;
        private MapTileCache cache;
        private MapPublication cached;
        private readonly MapDrawing drawing=new MapDrawing();
        private List<MapDestination> picked=new List<MapDestination>();
        private string status="",search="";
        private MapBakeResult lastBake;
        private List<string> audit=new List<string>();
        private UnityEditor.Editor styleEditor;
        private readonly EditorKnowledge knowledge=new EditorKnowledge();
        private sealed class EditorKnowledge:IMapKnowledge
        { public bool RoadVisible(RoadId id)=>true;public bool MarkerVisible(string id,string category)=>true;public bool ActivityVisible(ActivityMapMarker m)=>true;public string Localize(string key,string fallback)=>fallback; }
        [MenuItem("Tools/NFS MW Remaster/Minimap & World Map Studio")]
        public static void Open()=>GetWindow<MapStudioWindow>("Map Studio");
        private void OnEnable(){SceneView.duringSceneGui+=SceneGUI;Undo.undoRedoPerformed+=Invalidate;EditorApplication.playModeStateChanged+=PlayChanged;}
        private void OnDisable(){SceneView.duringSceneGui-=SceneGUI;Undo.undoRedoPerformed-=Invalidate;EditorApplication.playModeStateChanged-=PlayChanged;cache?.Dispose();if(styleEditor!=null)DestroyImmediate(styleEditor);}
        private void PlayChanged(PlayModeStateChange state)=>Invalidate();
        private void Invalidate(){cache?.Dispose();cache=null;cached=null;Repaint();SceneView.RepaintAll();}
        private void OnGUI()
        {
            minSize=new Vector2(760,500);
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            var next=(MapDefinition)EditorGUILayout.ObjectField(definition,typeof(MapDefinition),false,GUILayout.MinWidth(220));
            if(next!=definition){definition=next;Invalidate();Fit();}
            if(GUILayout.Button("New",EditorStyles.toolbarButton))CreateDefinition();
            using(new EditorGUI.DisabledScope(definition==null))if(GUILayout.Button("Bake revision",EditorStyles.toolbarButton))Bake();
            if(GUILayout.Button("Sample",EditorStyles.toolbarButton)){try{definition=MapDemo.Create();Invalidate();Fit();}catch(Exception e){status=e.Message;}}
            EditorGUILayout.EndHorizontal();
            page=GUILayout.Toolbar(page,Pages);if(!string.IsNullOrEmpty(status))EditorGUILayout.HelpBox(status,MessageType.Info);
            if(definition==null){EditorGUILayout.HelpBox("Choose a map definition, create one, or build the isolated bridge/tunnel sample. Road publications are required; imported city meshes alone do not contain lane topology.",MessageType.Info);return;}
            if(definition.publication!=null && definition.publication.Fingerprint!=MapBaker.Fingerprint(definition))EditorGUILayout.HelpBox("Source changed. Preview shows the previous valid map revision. Bake to update it.",MessageType.Warning);
            if(page==0){DrawMap();return;}
            scroll=EditorGUILayout.BeginScrollView(scroll);
            try
            {
                switch(page)
                {
                    case 1:Sources();break;case 2:Levels();break;case 3:Style();break;case 4:Coordinates();break;case 5:Tiles();break;case 6:Markers();break;case 7:Validation();break;case 8:Delivery();break;
                }
            }
            finally{EditorGUILayout.EndScrollView();}
        }
        private void CreateDefinition()
        {
            string path=EditorUtility.SaveFilePanelInProject("Create map definition","WorldMap","asset","Choose source asset location.");if(string.IsNullOrEmpty(path))return;
            var asset=CreateInstance<MapDefinition>();AssetDatabase.CreateAsset(asset,path);Undo.RegisterCreatedObjectUndo(asset,"Create map definition");definition=asset;Selection.activeObject=asset;Invalidate();
        }
        private void Bake()
        {
            try{lastBake=MapBaker.Bake(definition,p=>EditorUtility.DisplayCancelableProgressBar("Generate semantic map","Building and staging vector tiles",p));status=$"Published {lastBake.written} changed / {lastBake.reused} reused tiles · {lastBake.segments:N0} segments (3 LODs) · {lastBake.bytes:N0} JSON bytes · {lastBake.milliseconds:0} ms";Invalidate();}
            catch(OperationCanceledException){status="Cancelled. Previous publication retained.";}
            catch(Exception e){status=e.Message;}
            finally{EditorUtility.ClearProgressBar();}
        }
        private void Sources()
        {
            EditorGUILayout.LabelField("AUTHORITATIVE SOURCES",EditorStyles.boldLabel);
            var serialized=new SerializedObject(definition);serialized.Update();
            EditorGUILayout.PropertyField(serialized.FindProperty("roads"));EditorGUILayout.PropertyField(serialized.FindProperty("districts"),true);
            EditorGUILayout.PropertyField(serialized.FindProperty("tileSize"));EditorGUILayout.PropertyField(serialized.FindProperty("simplifyMeters"));serialized.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("No visual city scenes need to be loaded. Only the listed publications are covered. District labels remain separate and require gameplay visibility authorization. World footprints and fog raster layers are not generated by this version.",MessageType.Info);
            if(definition.roads!=null){EditorGUILayout.SelectableLabel("Network "+definition.roads.NetworkId+"\nRoad revision "+definition.roads.Fingerprint,GUILayout.Height(40));EditorGUILayout.LabelField("Directed lanes",definition.roads.Lanes.Count.ToString());if(GUILayout.Button("Inspect road publication"))Selection.activeObject=definition.roads;}
            if(GUILayout.Button("Duplicate definition with fresh identity"))
            {
                var copy=Instantiate(definition);copy.id=Guid.NewGuid().ToString("N");copy.publication=null;copy.name=definition.name+" Copy";
                string path=AssetDatabase.GenerateUniqueAssetPath(AssetDatabase.GetAssetPath(definition));AssetDatabase.CreateAsset(copy,path);Undo.RegisterCreatedObjectUndo(copy,"Duplicate map definition");definition=copy;Invalidate();
            }
        }
        private void Levels()
        {
            EditorGUILayout.HelpBox("Levels are explicit cartographic annotations on existing lane IDs. Elevation remains from source samples. Use distinct levels for stacked roads; each lane is directed. Level changes within one lane require splitting that lane in the road authoring tool.",MessageType.Info);
            if(definition.roads==null)return;
            if(GUILayout.Button("Assign surface level 0 to unclassified lanes"))
            {
                Undo.RecordObject(definition,"Classify map lanes");var list=definition.levels.Where(x=>x!=null).ToList();
                foreach(var lane in definition.roads.Lanes)if(!list.Exists(x=>x.lane==lane.Id))list.Add(new MapLaneLevel{lane=lane.Id});definition.levels=list.ToArray();EditorUtility.SetDirty(definition);
            }
            search=EditorGUILayout.TextField("Lane ID filter",search);
            foreach(var lane in definition.levels)
            {
                if(lane==null||!lane.lane.ToString().Contains(search))continue;
                EditorGUILayout.BeginHorizontal();EditorGUILayout.SelectableLabel(lane.lane.ToString(),GUILayout.Width(265),GUILayout.Height(20));
                EditorGUI.BeginChangeCheck();int next=EditorGUILayout.IntField(lane.level,GUILayout.Width(65));var structure=(MapRoadStructure)EditorGUILayout.EnumPopup(lane.structure);
                if(EditorGUI.EndChangeCheck()){Undo.RecordObject(definition,"Change map level");lane.level=next;lane.structure=structure;EditorUtility.SetDirty(definition);}
                if(GUILayout.Button("Frame",GUILayout.Width(60)))FrameLane(lane.lane,0);EditorGUILayout.EndHorizontal();
            }
        }
        private void Style()
        {
            var serialized=new SerializedObject(definition);serialized.Update();EditorGUILayout.PropertyField(serialized.FindProperty("style"));serialized.ApplyModifiedProperties();
            if(definition.style==null)
            {if(GUILayout.Button("Create original MW-inspired style")){string path=EditorUtility.SaveFilePanelInProject("Map style","MapStyle","asset","");if(!string.IsNullOrEmpty(path)){var style=CreateInstance<MapStyle>();AssetDatabase.CreateAsset(style,path);Undo.RecordObject(definition,"Assign map style");definition.style=style;EditorUtility.SetDirty(definition);}}return;}
            UnityEditor.Editor.CreateCachedEditor(definition.style,null,ref styleEditor);styleEditor.OnInspectorGUI();
            EditorGUILayout.HelpBox("Style changes update presentation immediately and do not invalidate semantic tiles. Assign a licensed font with your required glyph coverage; this tool does not ship commercial game artwork.",MessageType.Info);
        }
        private void Coordinates()
        {
            var serialized=new SerializedObject(definition);serialized.Update();EditorGUILayout.PropertyField(serialized.FindProperty("frame"),true);serialized.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("Logical source world is X/Z in meters, north +Z. Map space is east/north. UI Y is down. Runtime worldShift is the accumulated logical origin removed from scene transforms. Map headings use the same basis, including mirroring. Updating worldShift does not require rebaking.",MessageType.Info);
            if(SceneView.lastActiveSceneView!=null)
            {
                Vector3 point=SceneView.lastActiveSceneView.pivot;var mapped=definition.frame.Source(point);var restored=definition.frame.ToShifted(mapped,point.y,default);
                EditorGUILayout.LabelField("Scene pivot",point.ToString("F3"));EditorGUILayout.LabelField("Map",$"{mapped.x:R}, {mapped.y:R}");EditorGUILayout.LabelField("Round-trip error",Vector3.Distance(point,restored).ToString("R")+" meters");
            }
            sceneLinks=EditorGUILayout.Toggle("Scene overlay",sceneLinks);
        }
        private void Tiles()
        {
            var publication=definition.publication;if(publication==null){EditorGUILayout.HelpBox("Bake to inspect tiles.",MessageType.Info);return;}
            EditorGUILayout.SelectableLabel("Map "+publication.Id+"\nBake "+publication.Fingerprint+"\nFrame "+publication.FrameRevision,GUILayout.Height(62));
            EditorGUILayout.LabelField("Index",publication.Tiles.Count+" tiles");
            EditorGUILayout.LabelField("Cache",cache==null?"Not loaded":$"{cache.Count} tiles / {cache.Bytes:N0} estimated managed bytes / {cache.Loads} loads / {cache.Evictions} evictions / {cache.Missing} unavailable");
            foreach(var tile in publication.Tiles)
            {EditorGUILayout.BeginHorizontal();EditorGUILayout.LabelField($"({tile.x}, {tile.y}) · {tile.segments:N0} segments · {tile.bytes:N0} bytes");if(GUILayout.Button("Focus",GUILayout.Width(65))){viewport.center=new MapPoint((tile.x+.5)*publication.TileSize,(tile.y+.5)*publication.TileSize);viewport.pixelsPerUnit=1;page=0;}EditorGUILayout.EndHorizontal();}
            if(GUILayout.Button("Reset tile cache / retry missing"))Invalidate();
        }
        private void Markers()
        {
            EditorGUILayout.HelpBox("EDITOR KNOWLEDGE — these records bypass player visibility for authoring inspection. Runtime requires an IMapKnowledge provider. New activity publications preserve validated access lane/station/revision. Older publications need republishing before marker GPS is available. No nearest-lane replacement is made.",MessageType.Warning);
            foreach(var marker in ActivityMapRegistry.Markers){var m=marker.Snapshot;EditorGUILayout.LabelField(m.label+" · "+m.adapter,EditorStyles.boldLabel);EditorGUILayout.SelectableLabel(m.id+" · level "+m.level+" · "+(marker.Loaded?"Loaded":"Catalog only"),GUILayout.Height(20));if(GUILayout.Button("Focus marker")){viewport.center=definition.frame.Source(m.icon);page=0;}}
            if(definition.publication!=null)foreach(var landmark in definition.publication.Landmarks)EditorGUILayout.LabelField(landmark.label,$"{landmark.district} / access {(landmark.accessible?"validated":"unvalidated")}");
        }
        private void Validation()
        {
            var errors=MapBaker.Validate(definition);foreach(string error in errors)EditorGUILayout.HelpBox(error,MessageType.Error);
            if(errors.Count==0)EditorGUILayout.HelpBox("Source schema, lane references, explicit levels, district road revisions and frame are valid.",MessageType.Info);
            if(definition.style==null)EditorGUILayout.HelpBox("Assign a style before preview/runtime use.",MessageType.Warning);
            if(definition.publication!=null)
            {
                if(GUILayout.Button("Verify every tile hash, frame and segment count"))
                {using(var check=new MapTileCache(definition.publication)){foreach(var tile in definition.publication.Tiles)check.Get(tile);status=$"Tile integrity: {check.Loads} loaded, {check.Missing} missing/corrupt/over budget.";}}
            }
            if(definition.publication!=null&&GUILayout.Button("Audit stacked crossings")){audit=MapAudit.Crossings(definition.publication);status=audit.Count==0?"No stacked crossings sharing one level found within the audit budget.":audit.Count+" crossing diagnostics.";}
            foreach(var issue in audit)EditorGUILayout.HelpBox(issue,MessageType.Warning);
            EditorGUILayout.HelpBox("Manual acceptance still required: inspect close parallel roads at supported zoom, bridge/tunnel annotations, font coverage, controller focus and target-device rendering. The imported MW city needs authored road publications before this generator can cover it.",MessageType.Warning);
        }
        private void Delivery()
        {
            EditorGUILayout.HelpBox("Attach the runtime map to a selected player with a published RoadNetwork. The command adds WorldMapController and a FreeRoamMapKnowledge adapter. Approve public road visibility explicitly in its Inspector. Missing policy hides map content. WorldMapController uses M/View, Esc/B and captures shared player input.",MessageType.Info);
            using(new EditorGUI.DisabledScope(Selection.activeGameObject==null||definition.publication==null||definition.style==null))
                if(GUILayout.Button("Attach map to selected player"))
                {
                    var go=Selection.activeGameObject;var controller=go.GetComponent<WorldMapController>()??Undo.AddComponent<WorldMapController>(go);
                    var policy=go.GetComponent<FreeRoamMapKnowledge>()??Undo.AddComponent<FreeRoamMapKnowledge>(go);
                    Undo.RecordObjects(new UnityEngine.Object[]{controller,policy},"Configure world map");controller.map=definition.publication;controller.style=definition.style;controller.player=go.transform;controller.knowledgeProvider=policy;
                    policy.session=go.GetComponent<FreeRoamSession>();controller.roads=UnityEngine.Object.FindAnyObjectByType<RoadNetwork>();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(controller);PrefabUtility.RecordPrefabInstancePropertyModifications(policy);
                    EditorUtility.SetDirty(controller);EditorUtility.SetDirty(policy);status="Attached. Verify the road owner and approve the visibility policy in the Inspector.";
                }
            EditorGUILayout.LabelField("Publication recovery",EditorStyles.boldLabel);EditorGUILayout.LabelField("Undo changes the definition's publication pointer. Previous generated folders remain intact.\nDo not delete tile folders referenced by any retained publication. Each bake stages a new folder; cancellation removes only that staging folder.",EditorStyles.wordWrappedLabel);
            using(new EditorGUI.DisabledScope(definition.publication==null||definition.style==null))
            if(GUILayout.Button("Export editor road map as SVG")){string path=EditorUtility.SaveFilePanel("Export editor map", "", "WorldMap.svg", "svg");if(!string.IsNullOrEmpty(path)){try{MapExport.Svg(definition.publication,definition.style,path,filterLevel?(int?)level:null);status="Exported "+path;}catch(Exception e){status=e.Message;}}}
            if(GUILayout.Button("Open usage guide"))Application.OpenURL("file://"+System.IO.Path.GetFullPath("MAP_STUDIO_GUIDE.md"));
        }
        private void Fit()
        {if(definition?.publication==null)return;var map=definition.publication;viewport.center=(map.Minimum+map.Maximum)*.5;viewport.pixelsPerUnit=Mathf.Clamp((float)(Math.Min(position.width-70,position.height-180)/Math.Max(1,Math.Max(map.Maximum.x-map.Minimum.x,map.Maximum.y-map.Minimum.y))),.01f,20);}
        private void DrawMap()
        {
            var map=definition.publication;if(map==null||definition.style==null){EditorGUILayout.HelpBox("Assign a style and bake a valid source to preview.",MessageType.Info);return;}
            if(cached!=map||cache==null){cache?.Dispose();cache=new MapTileCache(map);cached=map;}
            EditorGUILayout.BeginHorizontal();if(GUILayout.Button("Fit"))Fit();grid=GUILayout.Toggle(grid,"Tiles");filterLevel=GUILayout.Toggle(filterLevel,"Level");level=EditorGUILayout.IntField(level,GUILayout.Width(50));
            aspect=EditorGUILayout.Popup(aspect,new[]{"Window","16:9","4:3","21:9","Minimap"},GUILayout.Width(100));EditorGUILayout.EndHorizontal();
            var available=GUILayoutUtility.GetRect(100,100,GUILayout.ExpandWidth(true),GUILayout.ExpandHeight(true));var rect=available;
            if(aspect>0){float ratio=aspect==1?16/9f:aspect==2?4/3f:aspect==3?21/9f:1;rect.width=Mathf.Min(available.width,available.height*ratio);rect.height=rect.width/ratio;}
            drawing.Draw(rect,map,cache,definition.style,viewport,filterLevel?(int?)level:null,knowledge,grid);
            float margin=definition.style.safeMargin;Handles.BeginGUI();Handles.color=Color.yellow;Handles.DrawWireCube(new Vector3(rect.center.x,rect.center.y),new Vector3(Mathf.Max(1,rect.width-margin*2),Mathf.Max(1,rect.height-margin*2)));Handles.EndGUI();
            GUI.Label(new Rect(rect.x+12,rect.y+10,rect.width-24,24),$"EDITOR · N ↑ · {drawing.DrawnSegments:N0} segments · {drawing.VisibleTiles} visible tiles");
            if(drawing.OverviewMode)GUI.Label(new Rect(rect.x+12,rect.y+36,rect.width-24,24),"CITY OVERVIEW · zoom in for exact source selection");
            if(drawing.BudgetExceeded)GUI.Label(new Rect(rect.x+12,rect.y+36,rect.width-24,24),"Viewport exceeds cache capacity. Zoom in; missing regions cannot be selected.");
            var e=Event.current;if(rect.Contains(e.mousePosition))
            {
                if(e.type==EventType.ScrollWheel){viewport.Zoom(Mathf.Exp(-e.delta.y*.1f),e.mousePosition,rect);e.Use();Repaint();}
                if(e.type==EventType.MouseDrag&&(e.button==1||e.button==2)){viewport.Pan(e.delta,rect);e.Use();Repaint();}
                if(e.type==EventType.MouseDown&&e.button==0&&drawing.OverviewMode){viewport.center=viewport.FromUI(e.mousePosition,rect);viewport.Zoom(2,rect.center,rect);e.Use();Repaint();}
                if(e.type==EventType.MouseDown&&e.button==0){picked=drawing.Pick(e.mousePosition,rect,viewport,map);e.Use();Repaint();}
            }
            foreach(var candidate in picked.Take(6))if(GUILayout.Button($"Lane {candidate.anchor.LaneId} · level {candidate.level} · distance {candidate.anchor.Distance:0.0}m · frame source"))FrameLane(candidate.anchor.LaneId,candidate.anchor.Distance);
        }
        private void FrameLane(RoadId laneId,float distance)
        {
            if(definition?.roads==null)return;var lane=definition.roads.Lanes.FirstOrDefault(l=>l.Id==laneId);if(lane==null)return;
            var point=lane.Sample(distance).position;Selection.activeObject=definition.roads;if(SceneView.lastActiveSceneView!=null)SceneView.lastActiveSceneView.LookAt(point,SceneView.lastActiveSceneView.rotation,35);
        }
        private void SceneGUI(SceneView scene)
        {
            if(!sceneLinks||definition?.publication==null)return;
            var frame=definition.publication.Frame;var origin=frame.ToShifted(new MapPoint(0,0),frame.origin.y,default);
            using(new Handles.DrawingScope(Color.cyan)){Handles.Label(origin,"MAP ORIGIN · "+definition.name);Handles.DrawLine(origin,frame.ToShifted(new MapPoint(25,0),frame.origin.y,default));Handles.DrawLine(origin,frame.ToShifted(new MapPoint(0,25),frame.origin.y,default));}
            foreach(var candidate in picked){var lane=definition.roads.Lanes.FirstOrDefault(l=>l.Id==candidate.anchor.LaneId);if(lane==null)continue;var p=lane.Sample(candidate.anchor.Distance).position;Handles.color=Color.yellow;Handles.DrawWireDisc(p,Vector3.up,4);Handles.Label(p,"Level "+candidate.level+" · "+candidate.anchor.LaneId);}
        }
    }
}
