using System;
using System.Collections.Generic;
using UnityEngine;
namespace NfsMwRemaster.Maps
{
    public sealed class MapTileCache : IDisposable
    {
        private sealed class Entry { public MapTileData data; public int bytes; public long used; }
        private readonly Dictionary<string,Entry> loaded = new Dictionary<string,Entry>(StringComparer.Ordinal);
        private readonly HashSet<string> failed = new HashSet<string>(StringComparer.Ordinal);
        private readonly Func<MapTileEntry,string> read;
        private readonly string frameRevision;
        private long clock;
        public int ByteBudget { get; }
        public int TileBudget { get; }
        public int Bytes { get; private set; }
        public int Loads { get; private set; }
        public int Evictions { get; private set; }
        public int Missing => failed.Count;
        public int Count => loaded.Count;
        public MapTileCache(MapPublication map, int tileBudget = 128, int byteBudget = 16*1024*1024, Func<MapTileEntry,string> reader = null)
        {
            if (map == null || map.Schema != MapPublication.CurrentSchema || tileBudget < 1 || byteBudget < 1) throw new ArgumentException("Invalid map cache configuration.");
            frameRevision=map.FrameRevision; TileBudget=tileBudget; ByteBudget=byteBudget; read=reader??ReadResource;
        }
        private static string ReadResource(MapTileEntry tile)
        {
            var asset=Resources.Load<TextAsset>(tile.resource); if(asset==null)return null;
            try { return asset.text; } finally { Resources.UnloadAsset(asset); }
        }
        public MapTileData Get(MapTileEntry tile)
        {
            if(loaded.TryGetValue(tile.resource,out var value)){value.used=++clock;return value.data;}
            if(failed.Contains(tile.resource))return null;
            // Estimated managed cost includes JSON UTF16 plus conservative per-segment storage.
            long estimate=(long)tile.bytes*2+(long)tile.segments*256;
            if(estimate>ByteBudget || tile.bytes<0 || tile.segments<0){failed.Add(tile.resource);return null;}
            int cost=(int)estimate;
            try
            {
                string json=read(tile); if(json==null || Hash128.Compute(json).ToString()!=tile.fingerprint){failed.Add(tile.resource);return null;}
                var data=JsonUtility.FromJson<MapTileData>(json);
                if(data==null || data.schema!=1 || data.frameRevision!=frameRevision || data.segments==null || data.segments.Length!=tile.segments)
                {failed.Add(tile.resource);return null;}
                while(loaded.Count>=TileBudget || Bytes+cost>ByteBudget)
                {
                    string oldest=null;long age=long.MaxValue;
                    foreach(var item in loaded)if(item.Value.used<age){age=item.Value.used;oldest=item.Key;}
                    if(oldest==null)break;Bytes-=loaded[oldest].bytes;loaded.Remove(oldest);Evictions++;
                }
                loaded.Add(tile.resource,new Entry{data=data,bytes=cost,used=++clock});Bytes+=cost;Loads++;return data;
            }
            catch(Exception e) when(e is ArgumentException || e is FormatException || e is InvalidOperationException)
            {failed.Add(tile.resource);return null;}
        }
        public void Dispose(){loaded.Clear();failed.Clear();Bytes=0;}
    }
}
