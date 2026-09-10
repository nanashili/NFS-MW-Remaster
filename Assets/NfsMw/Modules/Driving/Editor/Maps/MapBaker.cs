using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NfsMwRemaster.Driving;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Maps.Editor
{
    public sealed class MapBakeResult
    { public MapPublication publication; public int written,reused,segments; public long bytes; public double milliseconds; }
    public static class MapBaker
    {
        public static string Fingerprint(MapDefinition definition)
        {
            if(definition==null)return "";
            if(definition.levels==null||definition.districts==null||definition.frame==null)return "invalid-source";
            var b=new StringBuilder("map-v1.1|").Append(definition.id).Append('|').Append(JsonUtility.ToJson(definition.frame))
                .Append('|').Append(definition.tileSize.ToString("R",CultureInfo.InvariantCulture)).Append('|').Append(definition.simplifyMeters.ToString("R",CultureInfo.InvariantCulture));
            if(definition.roads!=null)b.Append('|').Append(definition.roads.NetworkId).Append('|').Append(definition.roads.Fingerprint).Append('|').Append(AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(definition.roads)));
            foreach(var level in definition.levels.OrderBy(x=>x?.lane.ToString(),StringComparer.Ordinal))b.Append('|').Append(JsonUtility.ToJson(level));
            foreach(var city in definition.districts.Where(x=>x!=null).OrderBy(x=>x.DistrictId,StringComparer.Ordinal))b.Append('|').Append(city.DistrictId).Append('|').Append(city.Fingerprint);
            return Hash128.Compute(b.ToString()).ToString();
        }
        public static List<string> Validate(MapDefinition definition)
        {
            var errors=new List<string>();
            if(definition==null){errors.Add("Choose a map definition.");return errors;}
            if(definition.schema!=1 || !Guid.TryParseExact(definition.id,"N",out _))errors.Add("Unsupported schema or invalid map identity.");
            if(definition.levels==null||definition.districts==null){errors.Add("Missing source arrays.");return errors;}
            if(definition.style!=null&&definition.style.schema!=1)errors.Add("Unsupported map style schema.");
            foreach(string guid in AssetDatabase.FindAssets("t:MapDefinition"))
            {var other=AssetDatabase.LoadAssetAtPath<MapDefinition>(AssetDatabase.GUIDToAssetPath(guid));if(other!=null&&other!=definition&&other.id==definition.id)errors.Add("Duplicate map identity: "+AssetDatabase.GetAssetPath(other)+". Use the fresh-identity duplicate command.");}
            try{definition.frame.Validate();}catch(Exception e){errors.Add(e.Message);}
            if(!float.IsFinite(definition.tileSize)||definition.tileSize<16 || !float.IsFinite(definition.simplifyMeters)||definition.simplifyMeters<0)errors.Add("Tile size must be finite and >=16; tolerance must be finite and >=0.");
            if(definition.roads==null){errors.Add("A published authoritative lane network is required; legacy graph/pixels cannot supply stable lane anchors.");return errors;}
            try{new RoadRuntimeNetwork(definition.roads);}catch(Exception e){errors.Add(e.Message);return errors;}
            var ids=new HashSet<RoadId>();var known=new HashSet<RoadId>(definition.roads.Lanes.Select(l=>l.Id));
            foreach(var level in definition.levels)
            {if(level==null||!known.Contains(level.lane)||!ids.Add(level.lane))errors.Add("Missing, duplicate, or obsolete lane level annotation.");}
            foreach(var lane in definition.roads.Lanes)if(!ids.Contains(lane.Id))errors.Add("Unclassified lane level: "+lane.Id);
            var districts=new HashSet<string>();
            foreach(var city in definition.districts)
            {if(city==null||city.SchemaVersion!=1)errors.Add("Missing or unsupported district publication.");else if(!districts.Add(city.DistrictId))errors.Add("Duplicate district: "+city.DistrictId);else if(city.RoadFingerprint!=definition.roads.Fingerprint)errors.Add("District uses a different road revision: "+city.DistrictId);}
            return errors;
        }
        public static MapBakeResult Bake(MapDefinition definition,Func<float,bool> cancel=null)
        {
            var errors=Validate(definition);if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
            string revision=Fingerprint(definition),frameRevision=Hash128.Compute(JsonUtility.ToJson(definition.frame)+"|"+definition.tileSize.ToString("R",CultureInfo.InvariantCulture)).ToString();
            var timer=System.Diagnostics.Stopwatch.StartNew();
            var frame=definition.frame.Copy(); double tileSize=definition.tileSize;
            var levels=definition.levels.ToDictionary(x=>x.lane);
            var tiles=new SortedDictionary<string,List<MapSegment>>(StringComparer.Ordinal);
            var overviewSegments=new List<MapSegment>();
            var tileCoordinates=new Dictionary<string,Vector2Int>();
            var minimum=new MapPoint(double.PositiveInfinity,double.PositiveInfinity);var maximum=new MapPoint(double.NegativeInfinity,double.NegativeInfinity);
            var lanes=definition.roads.Lanes.OrderBy(l=>l.Id.ToString(),StringComparer.Ordinal).ToArray();
            int completed=0;
            foreach(var lane in lanes)
            {
                if(cancel?.Invoke((float)completed++/Math.Max(1,lanes.Length))==true)throw new OperationCanceledException();
                var points=lane.Samples.Select(p=>frame.Source(p.position)).ToArray();
                foreach(var point in points){minimum.x=Math.Min(minimum.x,point.x);minimum.y=Math.Min(minimum.y,point.y);maximum.x=Math.Max(maximum.x,point.x);maximum.y=Math.Max(maximum.y,point.y);}
                // Preserve elevation bends, sampled width changes and sharp corners at every LOD.
                var protect=new HashSet<int>();
                for(int i=1;i<points.Length-1;i++)if(Mathf.Abs(lane.Samples[i-1].width-lane.Samples[i].width)>.01f
                    || Mathf.Abs(lane.Samples[i+1].position.y-2*lane.Samples[i].position.y+lane.Samples[i-1].position.y)>.02f
                    || Vector3.Angle(lane.Samples[i-1].forward,lane.Samples[i+1].forward)>12)protect.Add(i);
                var level=levels[lane.Id];
                for(int detail=0;detail<3;detail++)
                {
                    double tolerance=detail==0?0:Math.Min(definition.simplifyMeters*(detail==1?1:4),lane.Samples.Min(s=>s.width)*.1f)*frame.unitsPerMeter;
                    var simplified=detail==0?Enumerable.Range(0,points.Length).ToList():MapGeometry.Simplify(points,tolerance,protect);
                    for(int j=1;j<simplified.Count;j++)
                    {
                        int a=simplified[j-1],b=simplified[j];var first=lane.Samples[a];var last=lane.Samples[b];
                        var segment=new MapSegment{lane=lane.Id,road=lane.RoadId,detail=detail,level=level.level,structure=level.structure,roadClass=lane.Class,
                            surface=lane.Surface!=null?lane.Surface.name:"Unspecified",a=points[a],b=points[b],start=first.distance,end=last.distance,
                            heightA=first.position.y,heightB=last.position.y,width=Mathf.Max(first.width,last.width)};
                        if(detail==2)overviewSegments.Add(segment);
                        double dx0=Math.Floor(Math.Min(segment.a.x,segment.b.x)/tileSize),dx1=Math.Floor(Math.Max(segment.a.x,segment.b.x)/tileSize);
                        double dy0=Math.Floor(Math.Min(segment.a.y,segment.b.y)/tileSize),dy1=Math.Floor(Math.Max(segment.a.y,segment.b.y)/tileSize);
                        if(dx0<int.MinValue||dx1>=int.MaxValue-1||dy0<int.MinValue||dy1>=int.MaxValue-1||(dx1-dx0+1)*(dy1-dy0+1)>65536)
                            throw new InvalidOperationException("Lane segment exceeds tile budget; adjust map basis/tile size.");
                        for(int x=(int)dx0;x<=dx1;x++)for(int y=(int)dy0;y<=dy1;y++)
                        {
                            if(!MapGeometry.Clip(segment.a,segment.b,x*tileSize,y*tileSize,(x+1d)*tileSize,(y+1d)*tileSize,out double enter,out double exit))continue;
                            string key=x.ToString(CultureInfo.InvariantCulture)+"_"+y.ToString(CultureInfo.InvariantCulture);
                            if(!tiles.TryGetValue(key,out var list)){list=new List<MapSegment>();tiles.Add(key,list);tileCoordinates.Add(key,new Vector2Int(x,y));}
                            list.Add(MapGeometry.Slice(segment,enter,exit));
                        }
                    }
                }
            }
            if(tiles.Count==0)throw new InvalidOperationException("Source has no map geometry.");
            var landmarks=new List<MapLandmark>();
            foreach(var city in definition.districts.OrderBy(c=>c.DistrictId,StringComparer.Ordinal))foreach(var location in city.Locations.OrderBy(l=>l.id,StringComparer.Ordinal))
                landmarks.Add(new MapLandmark{id=location.id,district=city.DistrictId,label=location.label,point=frame.Source(city.Origin+location.localPosition),accessLane=location.entranceLane,accessible=location.accessValidated});
            string parent="Assets/NfsMw/Modules/Driving/Generated/Maps";Directory.CreateDirectory(parent);
            string folder=parent+"/Bake_"+Guid.NewGuid().ToString("N");string resourceRoot="Maps/"+definition.id+"/"+Path.GetFileName(folder);
            string output=folder+"/Resources/"+resourceRoot;Directory.CreateDirectory(output);AssetDatabase.Refresh();
            var result=new MapBakeResult();MapPublication publication=null;var previous=definition.publication;
            try
            {
                var index=new List<MapTileEntry>();var old=new Dictionary<string,MapTileEntry>();
                if(definition.publication!=null&&definition.publication.FrameRevision==frameRevision)
                    foreach(var t in definition.publication.Tiles)old[t.x+"_"+t.y]=t;
                int count=0;
                foreach(var tile in tiles)
                {
                    if(cancel?.Invoke((float)count++/tiles.Count)==true)throw new OperationCanceledException();
                    var data=new MapTileData{frameRevision=frameRevision,segments=tile.Value.OrderBy(s=>s.level).ThenBy(s=>s.detail).ThenBy(s=>s.lane.ToString(),StringComparer.Ordinal).ThenBy(s=>s.start).ToArray()};string json=JsonUtility.ToJson(data);
                    string hash=Hash128.Compute(json).ToString();var xy=tileCoordinates[tile.Key];string resource=resourceRoot+"/"+tile.Key;
                    if(old.TryGetValue(tile.Key,out var prior)&&prior.fingerprint==hash&&ResourceExists(prior.resource,hash)){resource=prior.resource;result.reused++;}
                    else{File.WriteAllText(output+"/"+tile.Key+".json",json,new UTF8Encoding(false));result.written++;}
                    int bytes=Encoding.UTF8.GetByteCount(json);result.bytes+=bytes;result.segments+=data.segments.Length;
                    index.Add(new MapTileEntry{x=xy.x,y=xy.y,resource=resource,fingerprint=hash,bytes=bytes,segments=data.segments.Length});
                }
                var overviewData=new MapTileData{frameRevision=frameRevision,segments=overviewSegments.OrderBy(s=>s.level).ThenBy(s=>s.lane.ToString(),StringComparer.Ordinal).ThenBy(s=>s.start).ToArray()};
                string overviewJson=JsonUtility.ToJson(overviewData),overviewHash=Hash128.Compute(overviewJson).ToString();
                var overview=new MapTileEntry{resource=resourceRoot+"/Overview",fingerprint=overviewHash,bytes=Encoding.UTF8.GetByteCount(overviewJson),segments=overviewData.segments.Length};
                if(previous!=null&&previous.FrameRevision==frameRevision&&previous.Overview.fingerprint==overviewHash&&ResourceExists(previous.Overview.resource,overviewHash))overview.resource=previous.Overview.resource;
                else File.WriteAllText(output+"/Overview.json",overviewJson,new UTF8Encoding(false));
                AssetDatabase.Refresh();
                if(Fingerprint(definition)!=revision)throw new InvalidOperationException("Source changed during generation; staged bake rejected.");
                publication=ScriptableObject.CreateInstance<MapPublication>();
                publication.Initialize(definition.id,revision,frameRevision,definition.roads,frame,definition.tileSize,index.ToArray(),landmarks.ToArray(),minimum,maximum,lanes.Select(l=>new MapLaneInfo{lane=l.Id,length=l.Length,level=levels[l.Id].level}).ToArray(),overview);
                AssetDatabase.CreateAsset(publication,folder+"/Map.asset");AssetDatabase.SaveAssets();
                Undo.RecordObject(definition,"Publish map revision");definition.publication=publication;EditorUtility.SetDirty(definition);AssetDatabase.SaveAssets();
                result.publication=publication;result.milliseconds=timer.Elapsed.TotalMilliseconds;return result;
            }
            catch{definition.publication=previous;EditorUtility.SetDirty(definition);if(publication!=null&&!AssetDatabase.Contains(publication))UnityEngine.Object.DestroyImmediate(publication);AssetDatabase.DeleteAsset(folder);throw;}
        }
        private static bool ResourceExists(string resource,string hash)
        {var asset=Resources.Load<TextAsset>(resource);if(asset==null)return false;try{return Hash128.Compute(asset.text).ToString()==hash;}finally{Resources.UnloadAsset(asset);}}
    }
}
