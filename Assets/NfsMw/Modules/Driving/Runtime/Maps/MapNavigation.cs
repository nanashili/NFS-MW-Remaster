using System;
using System.Collections.Generic;
using NfsMwRemaster.Driving;
using UnityEngine;
namespace NfsMwRemaster.Maps
{
    public enum MapRouteState { None, Pending, Ready, Unavailable, Rerouting }
    public readonly struct MapDestination
    {
        public readonly string networkRevision;
        public readonly RoadLaneAnchor anchor;
        public readonly int level;
        public MapDestination(string revision,RoadLaneAnchor anchor,int level){networkRevision=revision;this.anchor=anchor;this.level=level;}
    }
    public sealed class MapRouteRequest
    {
        private readonly List<Vector3> points = new List<Vector3>();
        private Func<bool> authorized;
        public bool Visible => State==MapRouteState.Ready && (authorized==null||authorized());
        public long Generation { get; private set; }
        public MapRouteState State { get; private set; }
        public string Failure { get; private set; }
        public IReadOnlyList<Vector3> Points => points.AsReadOnly();
        public long Begin(bool reroute=false){authorized=null;Generation++;points.Clear();Failure="";State=reroute?MapRouteState.Rerouting:MapRouteState.Pending;return Generation;}
        public bool Publish(long generation,IReadOnlyList<Vector3> route,string failure="",Func<bool> authorization=null)
        {
            if(generation!=Generation || (State!=MapRouteState.Pending && State!=MapRouteState.Rerouting))return false;
            points.Clear(); Failure=failure;
            if(route==null || route.Count<2){State=MapRouteState.Unavailable;return true;}
            foreach(var point in route) if(!Finite(point)){State=MapRouteState.Unavailable;Failure="Non-finite route.";return true;}
            points.AddRange(route);authorized=authorization;State=MapRouteState.Ready;return true;
        }
        private static bool Finite(Vector3 v)=>new MapPoint(v.x,v.z).Finite&&!float.IsNaN(v.y)&&!float.IsInfinity(v.y);
        public void Clear(){authorized=null;Generation++;points.Clear();State=MapRouteState.None;Failure="";}
    }
    public static class MapNavigation
    {
        // Routes to the picked lane anchor, never to a planar nearest-road guess.
        public static void Route(RoadRuntimeNetwork owner,Vector3 sourcePosition,MapDestination destination,MapRouteRequest request,bool reroute=false,Func<RoadId,bool> canDisplayLane=null)
        {
            long generation=request.Begin(reroute);
            if(owner==null || owner.Asset.Fingerprint!=destination.networkRevision)
            {request.Publish(generation,null,"Road revision changed; select a destination again.");return;}
            if(!owner.TryNearest(sourcePosition,Vector3.zero,30,out var start))
            {request.Publish(generation,null,"Player has no accessible road anchor within 30m.");return;}
            var buffer=new RoadRouteBuffer(Math.Max(1,owner.Count));
            var status=owner.Route(start,destination.anchor,buffer,2);
            if(status!=RoadRouteResult.Success){request.Publish(generation,null,status.ToString());return;}
            if(canDisplayLane!=null)for(int i=0;i<buffer.Count;i++)if(!canDisplayLane(buffer[i].LaneId)){request.Publish(generation,null,"Route is unavailable under current map knowledge.");return;}
            var points=new List<Vector3>();
            for(int i=0;i<buffer.Count;i++)
            {
                var span=buffer[i];owner.TryIndex(span.LaneId,out int index);var lane=owner[index];
                Add(points,lane.Sample(span.Start).position);
                foreach(var p in lane.Samples)if(p.distance>span.Start&&p.distance<span.End)Add(points,p.position);
                Add(points,lane.Sample(span.End).position);
            }
            var routeLanes=new RoadId[buffer.Count];for(int i=0;i<buffer.Count;i++)routeLanes[i]=buffer[i].LaneId;
            request.Publish(generation,points,points.Count<2?"Already at destination.":"",()=>{if(canDisplayLane==null)return true;foreach(var lane in routeLanes)if(!canDisplayLane(lane))return false;return true;});
        }
        private static void Add(List<Vector3> points,Vector3 p){if(points.Count==0||(points[points.Count-1]-p).sqrMagnitude>1e-8f)points.Add(p);}
    }
}
