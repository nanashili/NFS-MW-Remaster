using System;
using System.Collections.Generic;
using NfsMwRemaster.Driving;
using UnityEngine;
namespace NfsMwRemaster.Maps
{
    public sealed class MapMarker
    {
        public string id, label, category, icon;
        public MapPoint position;
        public int level, priority;
        public bool critical;
        public Color color;
        public MapDestination? destination;
        internal ActivityMapMarker activitySource;
        internal Func<bool> live;
        public bool Authorized(IMapKnowledge knowledge)=>knowledge!=null&&(live==null||live())&&(activitySource!=null?knowledge.ActivityVisible(activitySource):knowledge.MarkerVisible(id,category));
    }
    public interface IMapKnowledge
    {
        bool RoadVisible(RoadId lane);
        bool MarkerVisible(string id,string category);
        bool ActivityVisible(ActivityMapMarker activity);
        string Localize(string key,string fallback);
    }
    public sealed class MapMarkerRegistry
    {
        private sealed class Registration : IDisposable
        {
            private readonly MapMarkerRegistry owner; private readonly string id; private readonly object token;
            public Registration(MapMarkerRegistry owner,string id,object token){this.owner=owner;this.id=id;this.token=token;}
            public void Dispose(){if(owner.tokens.TryGetValue(id,out var current)&&ReferenceEquals(current,token)){owner.tokens.Remove(id);owner.values.Remove(id);owner.Version++;}}
        }
        private readonly Dictionary<string,MapMarker> values=new Dictionary<string,MapMarker>(StringComparer.Ordinal);
        private readonly Dictionary<string,object> tokens=new Dictionary<string,object>(StringComparer.Ordinal);
        public int Version { get; private set; }
        public IDisposable Register(MapMarker marker)
        {
            if(marker==null||string.IsNullOrWhiteSpace(marker.id)||!marker.position.Finite)throw new ArgumentException("Marker needs stable identity and finite coordinates.");
            // A stream reload supersedes the old lifetime; disposing the old token cannot remove the new marker.
            string id=marker.id;object token=new object();tokens[id]=token;values[id]=Copy(marker);values[id].live=()=>tokens.TryGetValue(id,out var current)&&ReferenceEquals(current,token);Version++;return new Registration(this,id,token);
        }
        public List<MapMarker> Visible(IMapKnowledge policy)
        {
            var result=new List<MapMarker>();if(policy==null)return result;
            foreach(var m in values.Values)if(policy.MarkerVisible(m.id,m.category))result.Add(Copy(m));
            result.Sort((a,b)=>string.CompareOrdinal(a.id,b.id));return result;
        }
        private static MapMarker Copy(MapMarker m)=>new MapMarker{id=m.id,label=m.label,category=m.category,icon=m.icon,position=m.position,level=m.level,
            priority=m.priority,critical=m.critical,color=m.color,destination=m.destination,activitySource=m.activitySource,live=m.live};
    }
    public sealed class MapMarkerCluster
    { public MapPoint position; public readonly List<MapMarker> members=new List<MapMarker>(); }
    public static class MapMarkers
    {
        public static List<MapMarkerCluster> Cluster(IEnumerable<MapMarker> source,double cellSize,string selected)
        {
            if(cellSize<=0 || double.IsNaN(cellSize)||double.IsInfinity(cellSize))throw new ArgumentException("Invalid cluster size.");
            var result=new List<MapMarkerCluster>();var cells=new SortedDictionary<string,MapMarkerCluster>(StringComparer.Ordinal);
            foreach(var m in source)
            {
                if(m.critical||m.id==selected){var single=new MapMarkerCluster{position=m.position};single.members.Add(m);result.Add(single);continue;}
                // World anchored buckets do not change when the viewport pans.
                string key=m.level+":"+Math.Floor(m.position.x/cellSize)+":"+Math.Floor(m.position.y/cellSize);
                if(!cells.TryGetValue(key,out var cluster)){cluster=new MapMarkerCluster();cells.Add(key,cluster);}cluster.members.Add(m);
            }
            foreach(var cluster in cells.Values){cluster.members.Sort((a,b)=>string.CompareOrdinal(a.id,b.id));cluster.position=cluster.members[0].position;result.Add(cluster);}
            return result;
        }
        public static List<MapMarker> Activities(MapPublication map,IMapKnowledge knowledge)
        {
            var result=new List<MapMarker>();if(knowledge==null||map==null)return result;var frame=map.Frame;
            foreach(var source in ActivityMapRegistry.Markers)
            {
                if(!knowledge.ActivityVisible(source))continue;var m=source.Snapshot;
                MapDestination? destination=null;
                if(m.accessLane.IsValid&&m.accessNetworkId==map.RoadNetworkId&&m.accessRoadRevision==map.RoadRevision
                    &&map.TryLane(m.accessLane,out var access)&&float.IsFinite(m.accessDistance)&&m.accessDistance>=0&&m.accessDistance<=access.length)
                    destination=new MapDestination(map.RoadRevision,new RoadLaneAnchor(m.accessLane,m.accessDistance),access.level);
                result.Add(new MapMarker{activitySource=source,destination=destination,id=m.id,label=knowledge.Localize(m.id,m.label),category=m.adapter,position=frame.Source(m.icon),color=m.color,
                    level=destination?.level??(int.TryParse(m.level,out int level)?level:0),icon=m.adapter});
            }
            return result;
        }
    }
}
