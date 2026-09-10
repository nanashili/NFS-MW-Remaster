using System;
using System.Collections.Generic;
using NfsMwRemaster.Driving;
using UnityEngine;
namespace NfsMwRemaster.Maps
{
    public sealed class MapDrawing
    {
        private readonly List<MapSegment> picking=new List<MapSegment>();
        private readonly List<MapTileEntry> visible=new List<MapTileEntry>();
        public int VisibleTiles { get; private set; }
        public int DrawnSegments { get; private set; }
        public bool BudgetExceeded { get; private set; }
        public bool OverviewMode { get; private set; }
        public void Draw(Rect rect,MapPublication map,MapTileCache cache,MapStyle style,MapViewport view,int? level,IMapKnowledge policy,bool grid=false)
        {
            if(map==null||style==null)return;
            Box(rect,style.background);picking.Clear();visible.Clear();DrawnSegments=0;
            var a=view.FromUI(rect.min,rect);var b=view.FromUI(new Vector2(rect.xMax,rect.yMin),rect);
            var c=view.FromUI(rect.max,rect);var d=view.FromUI(new Vector2(rect.xMin,rect.yMax),rect);
            double minX=Math.Min(Math.Min(a.x,b.x),Math.Min(c.x,d.x)),maxX=Math.Max(Math.Max(a.x,b.x),Math.Max(c.x,d.x));
            double minY=Math.Min(Math.Min(a.y,b.y),Math.Min(c.y,d.y)),maxY=Math.Max(Math.Max(a.y,b.y),Math.Max(c.y,d.y));
            foreach(var tile in map.Tiles)if(tile.x*map.TileSize<=maxX&&(tile.x+1d)*map.TileSize>=minX&&tile.y*map.TileSize<=maxY&&(tile.y+1d)*map.TileSize>=minY)visible.Add(tile);
            VisibleTiles=visible.Count;OverviewMode=visible.Count>cache.TileBudget;BudgetExceeded=false;
            if(OverviewMode){visible.Clear();if(!string.IsNullOrEmpty(map.Overview.resource))visible.Add(map.Overview);else BudgetExceeded=true;}
            visible.Sort((x,y)=>((new MapPoint((x.x+.5)*map.TileSize,(x.y+.5)*map.TileSize)-view.center).Square).CompareTo((new MapPoint((y.x+.5)*map.TileSize,(y.y+.5)*map.TileSize)-view.center).Square));
            double unitsPerMeter=map.Frame.unitsPerMeter;
            int detail=OverviewMode?2:view.pixelsPerUnit>.7f?0:view.pixelsPerUnit>.15f?1:2;
            GUI.BeginGroup(rect);
            try
            {
                var local=new Rect(0,0,rect.width,rect.height);
                for(int i=0;i<Math.Min(visible.Count,cache.TileBudget);i++)
                {
                    var tile=visible[i];var data=cache.Get(tile);
                    if(data==null){BudgetExceeded=true;TileOutline(tile,map.TileSize,local,view,style.missing);continue;}
                    if(grid&&!OverviewMode)TileOutline(tile,map.TileSize,local,view,style.tileGrid);
                    // Each tile has segments sorted by level and stable lane id at publication time.
                    foreach(var segment in data.segments)
                    {
                        if(level.HasValue&&segment.level!=level.Value||policy?.RoadVisible(segment.lane)!=true)continue;
                        if(segment.detail==0&&!OverviewMode)picking.Add(segment);
                        if(segment.detail!=detail)continue;
                        var p=view.ToUI(segment.a,local);var q=view.ToUI(segment.b,local);
                        float width=Mathf.Max(style.minimumRoadPixels,(float)(segment.width*unitsPerMeter)*view.pixelsPerUnit);
                        width=Mathf.Min(width,40);var color=style.highContrast?Color.white:segment.structure==MapRoadStructure.Tunnel?style.tunnel:segment.structure==MapRoadStructure.Bridge?style.bridge:segment.roadClass==RoadClass.Highway?style.highway:style.road;
                        Line(p,q,style.outline,width+style.outlinePixels*2);
                        if(segment.structure==MapRoadStructure.Tunnel)Dashed(p,q,color,width);else Line(p,q,color,width);
                        DrawnSegments++;
                    }
                }
            }
            finally{GUI.EndGroup();}
        }
        public List<MapDestination> Pick(Vector2 point,Rect rect,MapViewport view,MapPublication map,float radius=12)
        {
            var result=new List<MapDestination>();var seen=new HashSet<RoadId>();var choices=new List<(MapSegment segment,double distance,double t)>();
            var p=view.FromUI(point,rect);
            foreach(var segment in picking)
            {double distance=MapGeometry.DistanceSquared(p,segment.a,segment.b,out double t);if(distance*view.pixelsPerUnit*view.pixelsPerUnit<=radius*radius)choices.Add((segment,distance,t));}
            choices.Sort((a,b)=>a.distance!=b.distance?a.distance.CompareTo(b.distance):string.CompareOrdinal(a.segment.lane.ToString(),b.segment.lane.ToString()));
            foreach(var choice in choices)if(seen.Add(choice.segment.lane))result.Add(new MapDestination(map.RoadRevision,new RoadLaneAnchor(choice.segment.lane,Mathf.Lerp(choice.segment.start,choice.segment.end,(float)choice.t)),choice.segment.level));
            return result;
        }
        private static void TileOutline(MapTileEntry tile,float size,Rect rect,MapViewport view,Color color)
        {
            var a=view.ToUI(new MapPoint(tile.x*(double)size,tile.y*(double)size),rect);var b=view.ToUI(new MapPoint((tile.x+1d)*size,tile.y*(double)size),rect);
            var c=view.ToUI(new MapPoint((tile.x+1d)*size,(tile.y+1d)*size),rect);var d=view.ToUI(new MapPoint(tile.x*(double)size,(tile.y+1d)*size),rect);
            Line(a,b,color,1);Line(b,c,color,1);Line(c,d,color,1);Line(d,a,color,1);
        }
        public static void Box(Rect rect,Color color){var before=GUI.color;GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=before;}
        public static void Line(Vector2 a,Vector2 b,Color color,float width)
        {
            if(Event.current.type!=EventType.Repaint)return;
            var matrix=GUI.matrix;var old=GUI.color;Vector2 delta=b-a;
            try{GUI.color=color;GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,a);GUI.DrawTexture(new Rect(a.x,a.y-width*.5f,delta.magnitude,width),Texture2D.whiteTexture);}
            finally{GUI.matrix=matrix;GUI.color=old;}
        }
        public static void Dashed(Vector2 a,Vector2 b,Color color,float width)
        {var d=b-a;float length=d.magnitude;if(length<.01f)return;for(float s=0;s<length;s+=12)Line(a+d*(s/length),a+d*(Mathf.Min(s+7,length)/length),color,width);}
        public static void Route(Rect rect,MapViewport view,MapFrame frame,IReadOnlyList<Vector3> route,Color color,float width,bool dashed=false)
        {GUI.BeginGroup(rect);try{var local=new Rect(0,0,rect.width,rect.height);for(int i=1;i<route.Count;i++){var a=view.ToUI(frame.Source(route[i-1]),local);var b=view.ToUI(frame.Source(route[i]),local);if(dashed)Dashed(a,b,color,width);else Line(a,b,color,width); }}finally{GUI.EndGroup();}}
    }
}
