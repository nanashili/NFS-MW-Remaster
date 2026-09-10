using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public static class CityGeometry
    {
        public const double Epsilon = 0.001;
        private static double Cross(Vector2 a, Vector2 b) => (double)a.x*b.y-(double)a.y*b.x;
        public static double SignedArea(IReadOnlyList<Vector2> points)
        {
            double area = 0; for (int i=0;i<points.Count;i++) area += Cross(points[i],points[(i+1)%points.Count]);
            return area/2;
        }
        public static double Area(CityPolygon polygon) => Math.Abs(SignedArea(polygon.outline))-polygon.holes.Sum(h=>Math.Abs(SignedArea(h.points)));
        public static Rect Bounds(CityPolygon polygon)
        {
            var min=polygon.outline[0]; var max=min;
            foreach(var p in polygon.outline) { min=Vector2.Min(min,p); max=Vector2.Max(max,p); }
            return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
        }
        public static bool Contains(IReadOnlyList<Vector2> ring, Vector2 p)
        {
            bool inside=false;
            for(int i=0,j=ring.Count-1;i<ring.Count;j=i++)
            {
                var a=ring[i]; var b=ring[j];
                if (Distance(p,a,b)<Epsilon) return true;
                if((a.y>p.y)!=(b.y>p.y) && p.x<(double)(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x) inside=!inside;
            }
            return inside;
        }
        public static bool Contains(CityPolygon polygon,Vector2 p) => Contains(polygon.outline,p) && !polygon.holes.Any(h=>Contains(h.points,p));
        public static float Distance(Vector2 p,Vector2 a,Vector2 b)
        { var d=b-a; return Vector2.Distance(p,a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(d.sqrMagnitude,1e-12f))); }
        private static bool Intersects(Vector2 a,Vector2 b,Vector2 c,Vector2 d)
        {
            double abC=Cross(b-a,c-a),abD=Cross(b-a,d-a),cdA=Cross(d-c,a-c),cdB=Cross(d-c,b-c);
            if(abC*abD<0 && cdA*cdB<0) return true;
            return Distance(a,c,d)<Epsilon || Distance(b,c,d)<Epsilon || Distance(c,a,b)<Epsilon || Distance(d,a,b)<Epsilon;
        }
        public static void Validate(CityPolygon polygon)
        {
            if(polygon==null || polygon.outline==null || polygon.holes==null) throw new ArgumentException("CITY_POLYGON: missing rings.");
            var rings=new List<IReadOnlyList<Vector2>> { polygon.outline };
            foreach(var h in polygon.holes) { if(h==null) throw new ArgumentException("CITY_POLYGON: missing hole."); rings.Add(h.points); }
            foreach(var ring in rings)
            {
                if(ring==null || ring.Count<3 || ring.Count>4096) throw new ArgumentException("CITY_POLYGON: use 3–4096 vertices per ring.");
                foreach(var p in ring) if(float.IsNaN(p.x)||float.IsNaN(p.y)||Math.Abs(p.x)>1000000||Math.Abs(p.y)>1000000)
                    throw new ArgumentException("CITY_COORDINATE: use finite district-local coordinates within 1000 km.");
                if(Math.Abs(SignedArea(ring))<0.01) throw new ArgumentException("CITY_POLYGON: ring has no usable area.");
                for(int i=0;i<ring.Count;i++)
                {
                    if(Vector2.Distance(ring[i],ring[(i+1)%ring.Count])<0.002f) throw new ArgumentException("CITY_EDGE: coincident vertices at edge "+i);
                    for(int j=i+1;j<ring.Count;j++)
                    {
                        if(j==i+1 || i==0 && j==ring.Count-1) continue;
                        if(Intersects(ring[i],ring[(i+1)%ring.Count],ring[j],ring[(j+1)%ring.Count])) throw new ArgumentException("CITY_SELF_INTERSECTION: edges "+i+" and "+j);
                    }
                }
            }
            for(int h=1;h<rings.Count;h++)
            {
                if(!Contains(rings[0],rings[h][0])) throw new ArgumentException("CITY_HOLE: hole lies outside the outline.");
                for(int k=0;k<h;k++)
                {
                    for(int i=0;i<rings[h].Count;i++) for(int j=0;j<rings[k].Count;j++)
                        if(Intersects(rings[h][i],rings[h][(i+1)%rings[h].Count],rings[k][j],rings[k][(j+1)%rings[k].Count]))
                            throw new ArgumentException("CITY_HOLE: ring boundaries touch or cross.");
                    if(k>0 && (Contains(rings[k],rings[h][0]) || Contains(rings[h],rings[k][0]))) throw new ArgumentException("CITY_HOLE: overlapping/nested holes.");
                }
            }
        }
        internal static PathsD Paths(CityPolygon polygon)
        {
            var result=new PathsD();
            PathD Ring(List<Vector2> points,bool positive)
            {
                var path=new PathD(points.Select(p=>new PointD(p.x,p.y)));
                if(Clipper.IsPositive(path)!=positive) path.Reverse(); return path;
            }
            result.Add(Ring(polygon.outline,true)); foreach(var h in polygon.holes) result.Add(Ring(h.points,false)); return result;
        }
        internal static List<CityPolygon> Polygons(PathsD paths)
        {
            var tree=new PolyTreeD(); Clipper.BooleanOp(ClipType.Union,paths,null,tree,FillRule.NonZero,3);
            var result=new List<CityPolygon>();
            void Visit(PolyPathD node)
            {
                if(node.Polygon!=null && !node.IsHole)
                {
                    var polygon=new CityPolygon { outline=node.Polygon.Select(p=>new Vector2((float)p.x,(float)p.y)).ToList() };
                    foreach(PolyPathD child in node) if(child.IsHole) polygon.holes.Add(new CityRing { points=child.Polygon.Select(p=>new Vector2((float)p.x,(float)p.y)).ToList() });
                    result.Add(polygon);
                }
                foreach(PolyPathD child in node) Visit(child);
            }
            Visit(tree); return result.OrderBy(p=>Bounds(p).xMin).ThenBy(p=>Bounds(p).yMin).ToList();
        }
        public static List<CityPolygon> Inset(CityPolygon polygon,float metres)
        {
            Validate(polygon); if(float.IsNaN(metres)||float.IsInfinity(metres)||metres<0) throw new ArgumentException("CITY_SETBACK: use a finite non-negative distance.");
            return Polygons(Clipper.InflatePaths(Paths(polygon),-metres,JoinType.Miter,EndType.Polygon,2,3));
        }
        public static bool Fits(CityPolygon container,CityPolygon subject) =>
            Math.Abs(Clipper.Area(Clipper.Difference(Paths(subject),Paths(container),FillRule.NonZero,3)))<0.002;
        public static bool Overlaps(CityPolygon a,CityPolygon b) =>
            Math.Abs(Clipper.Area(Clipper.Intersect(Paths(a),Paths(b),FillRule.NonZero,3)))>0.002;
        public static List<CityPolygon> Merge(CityPolygon a,CityPolygon b)
        { Validate(a); Validate(b); return Polygons(Clipper.Union(Paths(a),Paths(b),FillRule.NonZero,3)); }
        public static List<CityPolygon> Cut(CityPolygon polygon,Vector2 a,Vector2 b)
        {
            Validate(polygon); if((b-a).sqrMagnitude<0.01) throw new ArgumentException("CITY_CUT: choose distinct cut points.");
            var direction=(b-a).normalized; var normal=new Vector2(-direction.y,direction.x);
            float extent=(Bounds(polygon).size.magnitude+Vector2.Distance(Bounds(polygon).center,a))*4+10;
            var half=new CityPolygon { outline=new List<Vector2>{a-direction*extent,a+direction*extent,a+direction*extent+normal*extent,a-direction*extent+normal*extent} };
            var result=Polygons(Clipper.Intersect(Paths(polygon),Paths(half),FillRule.NonZero,3));
            result.AddRange(Polygons(Clipper.Difference(Paths(polygon),Paths(half),FillRule.NonZero,3)));
            if(result.Count<2) throw new ArgumentException("CITY_CUT: line does not split the polygon."); return result;
        }
        public static List<CityPolygon> Subdivide(CityPolygon polygon,float targetArea,float minimumArea)
        {
            Validate(polygon); if(!float.IsFinite(targetArea)||!float.IsFinite(minimumArea)||minimumArea<1||targetArea<minimumArea) throw new ArgumentException("CITY_SUBDIVISION: invalid area constraints.");
            var pending=new Queue<CityPolygon>(); var output=new List<CityPolygon>(); pending.Enqueue(polygon.Copy());
            while(pending.Count>0)
            {
                if(output.Count+pending.Count>2048) throw new ArgumentException("CITY_BUDGET: subdivision exceeds 2048 parcels.");
                var p=pending.Dequeue(); if(Area(p)<=targetArea*1.2) { output.Add(p); continue; }
                var bounds=Bounds(p); var centre=bounds.center;
                var cut=Cut(p,centre,centre+(bounds.width>=bounds.height?Vector2.up:Vector2.right));
                if(cut.Any(child=>Area(child)<minimumArea)) { output.Add(p); continue; }
                foreach(var child in cut) pending.Enqueue(child);
            }
            return output;
        }
        internal static PathsD RoadFootprints(CityDistrict district)
        {
            var triangles=new PathsD(); if(district.roads==null) return triangles;
            int count=0;
            foreach(var chunk in district.roads.Chunks)
            {
                if(chunk.Mesh==null || !chunk.Mesh.isReadable) throw new ArgumentException("CITY_ROAD_MESH: missing/unreadable published road mesh.");
                var vertices=chunk.Mesh.vertices; var indices=chunk.Mesh.triangles;
                for(int i=0;i<indices.Length;i+=3)
                {
                    if(++count>200000) throw new ArgumentException("CITY_BUDGET: select a smaller road publication (200000 triangle limit).");
                    var a=vertices[indices[i]]+chunk.Origin-district.transform.position;
                    var b=vertices[indices[i+1]]+chunk.Origin-district.transform.position;
                    var c=vertices[indices[i+2]]+chunk.Origin-district.transform.position;
                    if(Mathf.Min(a.y,b.y,c.y)>district.surfaceLevelTolerance || Mathf.Max(a.y,b.y,c.y)<-district.surfaceLevelTolerance) continue;
                    var path=new PathD{new PointD(a.x,a.z),new PointD(b.x,b.z),new PointD(c.x,c.z)};
                    if(Math.Abs(Clipper.Area(path))<0.00001) continue;
                    if(!Clipper.IsPositive(path)) path.Reverse(); triangles.Add(path);
                }
            }
            return Clipper.Union(triangles,null,FillRule.NonZero,3);
        }
        public static List<CityPolygon> DecodeBlocks(CityDistrict district)
        {
            Validate(district.boundary); if(district.roads==null) throw new ArgumentException("CITY_ROADS: choose a published road network.");
            var result=Polygons(Clipper.Difference(Paths(district.boundary),RoadFootprints(district),FillRule.NonZero,3));
            return result.Where(p=>Area(p)>=10 && !p.outline.Any(v=>district.boundary.outline.Where((a,i)=>Distance(v,a,district.boundary.outline[(i+1)%district.boundary.outline.Count])<0.003).Any())).ToList();
        }
        public static Vector2Int[] Cells(CityPolygon polygon,float size)
        {
            if(!float.IsFinite(size)||size<10) throw new ArgumentException("CITY_CELL: minimum cell size is 10 m.");
            var b=Bounds(polygon); int x0=Mathf.FloorToInt(b.xMin/size),x1=Mathf.FloorToInt(b.xMax/size),z0=Mathf.FloorToInt(b.yMin/size),z1=Mathf.FloorToInt(b.yMax/size);
            if((long)(x1-x0+1)*(z1-z0+1)>4096) throw new ArgumentException("CITY_CELL: footprint spans more than 4096 cells.");
            var cells=new List<Vector2Int>(); for(int x=x0;x<=x1;x++) for(int z=z0;z<=z1;z++)
                if(Overlaps(polygon,CityPolygon.Rectangle(x*size,z*size,size,size))) cells.Add(new Vector2Int(x,z));
            return cells.ToArray();
        }
    }
}
