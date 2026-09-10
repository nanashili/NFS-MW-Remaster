using System;
using System.Collections.Generic;
using NfsMwRemaster.Driving;
using UnityEngine;
using UnityEngine.InputSystem;
namespace NfsMwRemaster.Maps
{
    [DefaultExecutionOrder(-2000)]
    public sealed class WorldMapController : MonoBehaviour
    {
        public MapPublication map;
        public MapStyle style;
        public Transform player;
        public RoadNetwork roads;
        public MonoBehaviour knowledgeProvider;
        public LogicalOrigin worldShift;
        public bool rotateMinimap=true;
        [Range(.1f,3)] public float minimapZoom=.8f;
        [Range(0,80)] public float lookAhead=15;
        [Range(0,5)] public float headingSpeedThreshold=.5f;
        public bool filterLevel;
        public int level;
        public bool IsOpen { get; private set; }
        public string Failure { get; private set; }
        public MapRouteRequest Navigation { get; }=new MapRouteRequest();
        public MapMarkerRegistry DynamicMarkers { get; }=new MapMarkerRegistry();
        public MapRouteOverlays Overlays { get; }=new MapRouteOverlays();
        public MapViewport View { get; }=new MapViewport();
        private readonly MapDrawing drawing=new MapDrawing();
        private readonly MapViewport minimap=new MapViewport();
        private MapTileCache cache;
        private MapPublication cachedMap;
        private MapFrame frame;
        private readonly List<MapMarker> markers=new List<MapMarker>();
        private List<MapDestination> candidates=new List<MapDestination>();
        private MapDestination? destination;
        private float nextMarkers,nextRoute,heading;
        private MapPoint lastPlayer;
        private bool hasPlayerPosition;
        private string selected,category="All";
        private int selectedIndex,candidateIndex;
        private Vector2 listScroll;
        private Rect interactionRect;
        private IMapKnowledge Knowledge=>knowledgeProvider as IMapKnowledge;
        private void OnEnable(){if(map!=null&&style!=null)MapInputFocus.Register(this);}
        private void OnDisable(){IsOpen=false;MapInputFocus.Unregister(this);cache?.Dispose();cache=null;cachedMap=null;Navigation.Clear();}
        public void SetOpen(bool value)
        {
            if(value&&(map==null||style==null))return;
            IsOpen=value;if(value)MapInputFocus.Acquire(this);else MapInputFocus.Release(this);
        }
        public void FocusPlayer(){if(player!=null&&frame!=null)View.center=frame.Shifted(player.position,worldShift);}
        private void EnsureMap()
        {
            if(cachedMap==map)return;cache?.Dispose();cache=null;cachedMap=map;Navigation.Clear();destination=null;hasPlayerPosition=false;
            if(map==null)return;
            try{cache=new MapTileCache(map);frame=map.Frame;Failure="";}catch(Exception e){Failure=e.Message;cache?.Dispose();cache=null;SetOpen(false);MapInputFocus.Unregister(this);return;}
            View.center=(map.Minimum+map.Maximum)*.5;
            View.pixelsPerUnit=.5f;MapInputFocus.Register(this);
        }
        private void Update()
        {
            if(map==null||style==null){SetOpen(false);MapInputFocus.Unregister(this);return;}EnsureMap();if(cache==null)return;
            if(GameFlowRuntime.Instance!=null&&!GameFlowRuntime.Instance.AllowsWorldInput){SetOpen(false);return;}
            var key=Keyboard.current;var pad=Gamepad.current;
            if(key?.mKey.wasPressedThisFrame==true||pad?.selectButton.wasPressedThisFrame==true)SetOpen(!IsOpen);
            if(IsOpen&&(key?.escapeKey.wasPressedThisFrame==true||pad?.buttonEast.wasPressedThisFrame==true)){SetOpen(false);return;}
            if(IsOpen)
            {
                Vector2 pan=pad?.leftStick.ReadValue()??Vector2.zero;
                if(key!=null)pan+=new Vector2((key.rightArrowKey.isPressed?1:0)-(key.leftArrowKey.isPressed?1:0),(key.upArrowKey.isPressed?1:0)-(key.downArrowKey.isPressed?1:0));
                View.Pan(new Vector2(-pan.x,pan.y)*Time.unscaledDeltaTime*350,interactionRect);
                float zoom=(pad?.rightTrigger.ReadValue()??0)-(pad?.leftTrigger.ReadValue()??0);
                if(Mathf.Abs(zoom)>.1f)View.Zoom(Mathf.Exp(zoom*Time.unscaledDeltaTime),interactionRect.center,interactionRect);
                if(key?.fKey.wasPressedThisFrame==true||pad?.rightStickButton.wasPressedThisFrame==true)FocusPlayer();
                int step=(pad?.dpad.down.wasPressedThisFrame==true?1:0)-(pad?.dpad.up.wasPressedThisFrame==true?1:0);
                if(key?.tabKey.wasPressedThisFrame==true)step=1;
                if(candidates.Count>0&&step!=0)candidateIndex=(candidateIndex+step+candidates.Count)%candidates.Count;
                var visible=FilteredMarkers();if(candidates.Count==0&&visible.Count>0&&step!=0){selectedIndex=(selectedIndex+step+visible.Count)%visible.Count;selected=visible[selectedIndex].id;View.center=visible[selectedIndex].position;}
                if(key?.enterKey.wasPressedThisFrame==true||pad?.buttonSouth.wasPressedThisFrame==true)Confirm();
            }
            if(player!=null)
            {
                var p=frame.Shifted(player.position,worldShift);double speed=hasPlayerPosition?Math.Sqrt((p-lastPlayer).Square)/frame.unitsPerMeter/Math.Max(.001,Time.unscaledDeltaTime):0;
                if(!hasPlayerPosition||speed>headingSpeedThreshold)heading=frame.Heading(player.forward);
                lastPlayer=p;hasPlayerPosition=true;minimap.center=p;minimap.rotation=rotateMinimap?heading:0;minimap.pixelsPerUnit=minimapZoom;
                minimap.anchor=new Vector2(.5f,.65f);float angle=heading*Mathf.Deg2Rad;
                minimap.center+=new MapPoint(Math.Sin(angle),Math.Cos(angle))*lookAhead*frame.unitsPerMeter;
            }
            if(Time.unscaledTime>=nextMarkers)
            {
                nextMarkers=Time.unscaledTime+.25f;markers.Clear();markers.AddRange(MapMarkers.Activities(map,Knowledge));markers.AddRange(DynamicMarkers.Visible(Knowledge));
                if(Knowledge!=null)foreach(var location in map.Landmarks)if(Knowledge.MarkerVisible(location.id,"Landmark"))markers.Add(new MapMarker{id=location.id,label=Knowledge.Localize(location.id,location.label),category="Landmark",position=location.point,color=style.marker});
                markers.Sort((a,b)=>a.priority!=b.priority?b.priority.CompareTo(a.priority):string.CompareOrdinal(a.id,b.id));
                if(selected!=null&&!markers.Exists(m=>m.id==selected))selected=null;
            }
            if(destination.HasValue&&Time.unscaledTime>=nextRoute){nextRoute=Time.unscaledTime+1;RequestRoute(destination.Value,true);}
        }
        private void Confirm()
        {
            var marker=FilteredMarkers().Find(m=>m.id==selected);
            if(marker?.destination!=null){RequestRoute(marker.destination.Value);return;}
            if(candidates.Count>0)RequestRoute(candidates[Mathf.Clamp(candidateIndex,0,candidates.Count-1)]);
        }
        private void RequestRoute(MapDestination target,bool reroute=false)
        {
            destination=target;if(player==null||roads==null||!roads.UsesBakedData||roads.Publication.NetworkId.ToString()!=map.RoadNetworkId){Navigation.Publish(Navigation.Begin(),null,"Assign player and published road owner.");return;}
            var source=new Vector3((float)(player.position.x+worldShift.x),(float)(player.position.y+worldShift.y),(float)(player.position.z+worldShift.z));
            MapNavigation.Route(roads.Runtime,source,target,Navigation,reroute,lane=>Knowledge?.RoadVisible(lane)==true);
        }
        private List<MapMarker> FilteredMarkers()=>markers.FindAll(m=>m.Authorized(Knowledge)&&(category=="All"||m.category==category)&&(!filterLevel||m.level==level));
        private void OnGUI()
        {
            if(map==null||style==null||cache==null)return;
            if(GameFlowRuntime.Instance!=null&&!GameFlowRuntime.Instance.AllowsWorldInput)return;
            var safe=Screen.safeArea;safe.y=Screen.height-safe.yMax;
            float margin=style.safeMargin;
            var rect=IsOpen?new Rect(safe.x+margin,safe.y+margin,Mathf.Max(80,safe.width-300-margin*2),Mathf.Max(80,safe.height-margin*2)):
                new Rect(safe.xMax-280-margin,safe.y+margin,280,240);
            interactionRect=rect;var view=IsOpen?View:minimap;
            drawing.Draw(rect,map,cache,style,view,filterLevel?(int?)level:null,Knowledge);
            if(Navigation.Visible)MapDrawing.Route(rect,view,frame,Navigation.Points,style.route,style.routePixels);
            Overlays.VisitVisible(Knowledge,map.RoadRevision,strip=>MapDrawing.Route(rect,view,frame,strip.points,strip.color,style.routePixels,strip.dashed));
            GUI.BeginGroup(rect);
            try
            {
                var local=new Rect(0,0,rect.width,rect.height);var label=new GUIStyle(GUI.skin.label){font=style.font,fontSize=style.fontSize,normal={textColor=style.text}};
                var occupied=new List<Rect>();double clusterSize=Math.Pow(2,Math.Floor(Math.Log(40/Math.Max(.01,view.pixelsPerUnit),2)));
                foreach(var cluster in MapMarkers.Cluster(FilteredMarkers(),clusterSize,selected))
                {
                    var p=view.ToUI(cluster.position,local);if(!local.Contains(p))continue;var first=cluster.members[0];MapDrawing.Box(new Rect(p.x-4,p.y-4,8,8),first.color);
                    string text=cluster.members.Count>1?cluster.members.Count+" locations":first.label;var r=new Rect(p.x+7,p.y-10,Mathf.Min(220,label.CalcSize(new GUIContent(text)).x),24);
                    if(first.critical||first.id==selected||!occupied.Exists(o=>o.Overlaps(r))){GUI.Label(r,text,label);occupied.Add(r);}
                }
                if(player!=null){var p=view.ToUI(frame.Shifted(player.position,worldShift),local);float angle=(heading-view.rotation)*Mathf.Deg2Rad;var forward=new Vector2(Mathf.Sin(angle),-Mathf.Cos(angle));var side=new Vector2(-forward.y,forward.x);MapDrawing.Line(p+forward*10,p-forward*7+side*6,style.player,3);MapDrawing.Line(p+forward*10,p-forward*7-side*6,style.player,3);}
                GUI.Label(new Rect(8,5,260,24),rotateMinimap&&!IsOpen?"HEADING UP  ·  M / View":"NORTH UP  ·  M / View",label);
                if(Knowledge==null)GUI.Label(new Rect(8,32,rect.width-16,50),"No gameplay visibility provider. Map content is hidden.",label);
                if(drawing.BudgetExceeded||cache.Missing>0)GUI.Label(new Rect(8,rect.height-48,rect.width-16,44),"Map detail unavailable / viewport budget exceeded. Zoom in or retry.",label);
            }
            finally{GUI.EndGroup();}
            if(!IsOpen)return;
            var e=Event.current;
            if(rect.Contains(e.mousePosition))
            {
                if(e.type==EventType.ScrollWheel){View.Zoom(Mathf.Exp(-e.delta.y*.1f),e.mousePosition,rect);e.Use();}
                else if(e.type==EventType.MouseDrag&&e.button==1){View.Pan(e.delta,rect);e.Use();}
                else if(e.type==EventType.MouseDown&&e.button==0&&drawing.OverviewMode){View.center=View.FromUI(e.mousePosition,rect);View.Zoom(2,rect.center,rect);e.Use();}
                else if(e.type==EventType.MouseDown&&e.button==0)
                {
                    MapMarker hit=null;float best=196;
                    foreach(var marker in FilteredMarkers()){float distance=(View.ToUI(marker.position,rect)-e.mousePosition).sqrMagnitude;if(distance<best){best=distance;hit=marker;}}
                    if(hit!=null){selected=hit.id;candidates.Clear();}
                    else{candidates=drawing.Pick(e.mousePosition,rect,View,map);candidateIndex=0;selected=null;}e.Use();
                }
            }
            GUILayout.BeginArea(new Rect(rect.xMax+12,rect.y,270,rect.height),GUI.skin.box);
            try
            {
                GUILayout.Label("ROCKPORT / WORLD MAP");GUILayout.Label("Right drag / arrows / stick: pan\nWheel / triggers: zoom · F: player\nTab / D-pad: markers · Enter / A: confirm");
                if(GUILayout.Button("Focus player"))FocusPlayer();
                filterLevel=GUILayout.Toggle(filterLevel,"Filter road level");if(filterLevel){GUILayout.BeginHorizontal();if(GUILayout.Button("−"))level--;GUILayout.Label("Level "+level);if(GUILayout.Button("+"))level++;GUILayout.EndHorizontal();}
                if(GUILayout.Button("Category: "+category)){var categories=new List<string>{"All"};foreach(var m in markers)if(!categories.Contains(m.category))categories.Add(m.category);category=categories[(categories.IndexOf(category)+1)%categories.Count];}
                if(drawing.OverviewMode)GUILayout.Label("City overview. Zoom in for exact road selection; marker destinations remain available.");
                GUILayout.Label(Navigation.State+" "+Navigation.Failure);
                if(GUILayout.Button("Clear destination")){destination=null;Navigation.Clear();candidates.Clear();}
                foreach(var c in candidates)if(GUILayout.Button((candidates.IndexOf(c)==candidateIndex?"▶ ":"")+"Lane "+c.anchor.LaneId.ToString().Substring(0,8)+" · level "+c.level)) {candidates=new List<MapDestination>{c};RequestRoute(c);break;}
                listScroll=GUILayout.BeginScrollView(listScroll);
                foreach(var marker in FilteredMarkers())if(GUILayout.Button((selected==marker.id?"▶ ":"")+marker.label)){selected=marker.id;candidates.Clear();View.center=marker.position;}
                GUILayout.EndScrollView();
                if(selected!=null&&markers.Find(m=>m.id==selected)?.destination==null)GUILayout.Label("This marker has no published stable access anchor. Pick an explicit road lane to navigate.");
                if(GUILayout.Button("Back / Esc / B"))SetOpen(false);
            }
            finally{GUILayout.EndArea();}
        }
    }
}
