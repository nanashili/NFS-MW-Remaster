using System;
using System.Collections.Generic;
using System.Linq;
namespace NfsMwRemaster.Maps.Editor
{
    public static class MapAudit
    {
        public static List<string> Crossings(MapPublication publication)
        {
            var diagnostics=new List<string>();var reported=new HashSet<string>(StringComparer.Ordinal);
            using(var cache=new MapTileCache(publication))foreach(var tile in publication.Tiles)
            {
                var data=cache.Get(tile);if(data==null){diagnostics.Add($"Tile {tile.x},{tile.y} is missing, corrupt or exceeds the memory budget.");continue;}
                var segments=data.segments.Where(s=>s.detail==0).ToArray();
                if(segments.Length>2000){diagnostics.Add($"Tile {tile.x},{tile.y} exceeds pairwise crossing-audit budget; inspect its {segments.Length} segments manually.");continue;}
                for(int i=0;i<segments.Length;i++)for(int j=i+1;j<segments.Length;j++)
                {
                    var a=segments[i];var b=segments[j];if(a.lane==b.lane)continue;
                    var r=a.b-a.a;var s=b.b-b.a;double denominator=Cross(r,s);if(Math.Abs(denominator)<1e-9)continue;
                    double t=Cross(b.a-a.a,s)/denominator,u=Cross(b.a-a.a,r)/denominator;
                    if(t<0||t>1||u<0||u>1)continue;
                    double ay=a.heightA+(a.heightB-a.heightA)*t,by=b.heightA+(b.heightB-b.heightA)*u;
                    if(Math.Abs(ay-by)<2||a.level!=b.level)continue;
                    string pair=a.lane+" / "+b.lane;if(reported.Add(pair))diagnostics.Add($"Stacked crossing shares level {a.level}: {pair}; elevation gap {Math.Abs(ay-by):0.0}m. Review lane level annotations.");
                }
            }
            return diagnostics;
        }
        private static double Cross(MapPoint a,MapPoint b)=>a.x*b.y-a.y*b.x;
    }
}
