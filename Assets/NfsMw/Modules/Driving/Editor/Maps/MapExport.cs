using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
namespace NfsMwRemaster.Maps.Editor
{
    public static class MapExport
    {
        // Explicit editor/debug export. No player knowledge or localized labels are included.
        public static void Svg(MapPublication map,MapStyle style,string path,int? level=null)
        {
            if(map==null||style==null)throw new ArgumentException("Map and style are required.");
            double margin=20,minX=map.Minimum.x-margin,minY=-map.Maximum.y-margin;
            double width=map.Maximum.x-map.Minimum.x+margin*2,height=map.Maximum.y-map.Minimum.y+margin*2;
            var s=new StringBuilder();s.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"").Append(N(minX)).Append(' ').Append(N(minY)).Append(' ').Append(N(width)).Append(' ').Append(N(height)).Append("\">\n<title>Editor semantic road map — ").Append(map.Fingerprint).Append("</title>\n");
            s.Append("<rect x=\"").Append(N(minX)).Append("\" y=\"").Append(N(minY)).Append("\" width=\"").Append(N(width)).Append("\" height=\"").Append(N(height)).Append("\" fill=\"#").Append(ColorUtility.ToHtmlStringRGB(style.background)).Append("\"/>\n");
            using(var cache=new MapTileCache(map))
            foreach(var tile in map.Tiles)
            {
                var data=cache.Get(tile);if(data==null)throw new InvalidOperationException("Cannot export missing or corrupt tile: "+tile.resource);
                foreach(var segment in data.segments)
                {
                    if(segment.detail!=1||level.HasValue&&segment.level!=level.Value)continue;
                    var color=segment.structure==MapRoadStructure.Bridge?style.bridge:segment.structure==MapRoadStructure.Tunnel?style.tunnel:style.road;
                    s.Append("<path data-lane=\"").Append(segment.lane).Append("\" data-level=\"").Append(segment.level).Append("\" d=\"M ").Append(N(segment.a.x)).Append(' ').Append(N(-segment.a.y)).Append(" L ").Append(N(segment.b.x)).Append(' ').Append(N(-segment.b.y)).Append("\" fill=\"none\" stroke=\"#").Append(ColorUtility.ToHtmlStringRGB(color)).Append("\" stroke-width=\"").Append(N(segment.width*map.Frame.unitsPerMeter)).Append("\"");
                    if(segment.structure==MapRoadStructure.Tunnel)s.Append(" stroke-dasharray=\"8 6\"");s.Append("/>\n");
                }
            }
            s.Append("</svg>");string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temporary,s.ToString(),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        private static string N(double number)=>number.ToString("0.######",CultureInfo.InvariantCulture);
    }
}
