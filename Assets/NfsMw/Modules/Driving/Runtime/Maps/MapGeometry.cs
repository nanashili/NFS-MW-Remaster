using System;
using System.Collections.Generic;
using UnityEngine;
namespace NfsMwRemaster.Maps
{
    public static class MapGeometry
    {
        public static double DistanceSquared(MapPoint p, MapPoint a, MapPoint b, out double t)
        { var d = b - a; t = d.Square < 1e-20 ? 0 : Math.Max(0, Math.Min(1, ((p.x-a.x)*d.x+(p.y-a.y)*d.y)/d.Square)); return (p-a-d*t).Square; }
        // Iterative RDP runs on a complete lane, before clipping; endpoints are always retained.
        public static List<int> Simplify(IReadOnlyList<MapPoint> points, double tolerance, ISet<int> protectedPoints = null)
        {
            if (points == null || points.Count < 2 || tolerance < 0 || double.IsNaN(tolerance) || double.IsInfinity(tolerance)) throw new ArgumentException("Invalid simplification input.");
            var keep = new bool[points.Count]; keep[0] = keep[points.Count-1] = true;
            var stack = new Stack<Vector2Int>(); int start = 0;
            for (int i = 1; i < points.Count; i++) if (i == points.Count-1 || protectedPoints?.Contains(i) == true)
            { keep[i] = true; stack.Push(new Vector2Int(start,i)); start = i; }
            while (stack.Count > 0)
            {
                var span = stack.Pop(); double best = tolerance*tolerance; int at = -1;
                for (int i = span.x+1; i < span.y; i++)
                { double distance = DistanceSquared(points[i],points[span.x],points[span.y],out _); if (distance > best) { best=distance; at=i; } }
                if (at < 0) continue; keep[at]=true; stack.Push(new Vector2Int(span.x,at)); stack.Push(new Vector2Int(at,span.y));
            }
            var result = new List<int>(); for (int i=0;i<keep.Length;i++) if (keep[i]) result.Add(i); return result;
        }
        public static bool Clip(MapPoint a, MapPoint b, double left, double bottom, double right, double top, out double enter, out double exit)
        {
            enter=0; exit=1; var d=b-a;
            return Cut(-d.x,a.x-left,ref enter,ref exit) && Cut(d.x,right-a.x,ref enter,ref exit)
                && Cut(-d.y,a.y-bottom,ref enter,ref exit) && Cut(d.y,top-a.y,ref enter,ref exit) && exit-enter > 1e-12;
        }
        private static bool Cut(double p,double q,ref double a,ref double b)
        { if (Math.Abs(p)<1e-20) return q>=0; double t=q/p; if(p<0) { if(t>b)return false; a=Math.Max(a,t); } else { if(t<a)return false;b=Math.Min(b,t); } return true; }
        public static MapSegment Slice(MapSegment s,double t,double u) => new MapSegment
        { lane=s.lane,road=s.road,level=s.level,detail=s.detail,structure=s.structure,roadClass=s.roadClass,surface=s.surface,
            a=s.a+(s.b-s.a)*t,b=s.a+(s.b-s.a)*u,start=Mathf.Lerp(s.start,s.end,(float)t),end=Mathf.Lerp(s.start,s.end,(float)u),
            heightA=Mathf.Lerp(s.heightA,s.heightB,(float)t),heightB=Mathf.Lerp(s.heightA,s.heightB,(float)u),width=s.width };
    }
}
