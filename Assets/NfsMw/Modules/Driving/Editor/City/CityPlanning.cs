using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public sealed class CityPlannedInstance
    {
        public string key, parcelId, label, signature;
        public Vector3 position, euler, size;
        public Vector3 boundsSize, boundsOffset;
        public PrimitiveType primitive = PrimitiveType.Cube;
        public GameObject prefab;
        public Material material;
        public bool collision;
        public int vertices = 24;
        public CityPolygon footprint;
        public Vector2Int[] cells;
    }
    public sealed class CityPlan
    {
        public string fingerprint;
        public List<CityPlannedInstance> instances = new List<CityPlannedInstance>();
        public List<CityDiagnostic> diagnostics = new List<CityDiagnostic>();
        public List<CityLocationRecord> locations = new List<CityLocationRecord>();
        public double milliseconds;
        public bool Valid => diagnostics.All(d=>d.severity!=CitySeverity.Error);
    }
    public static class CityPlanning
    {
        public const string GeneratorVersion = "City/1";
        public static string Hash(string value)
        { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant(); }
        public static string Fingerprint(CityDistrict district)
        {
            // Source publication is excluded: committing does not change the input revision.
            var data=new StringBuilder(GeneratorVersion).Append(Application.unityVersion).Append(district.id)
                .Append(JsonUtility.ToJson(district.boundary)).Append(district.worldSeed).Append('/').Append(district.seed)
                .Append(JsonUtility.ToJson(new Position { value=district.transform.position }))
                .Append(district.cellSize.ToString("R",System.Globalization.CultureInfo.InvariantCulture))
                .Append(district.surfaceLevelTolerance.ToString("R",System.Globalization.CultureInfo.InvariantCulture))
                .Append(district.roads==null?"none":district.roads.Fingerprint);
            void Asset(UnityEngine.Object asset)
            {
                if(asset==null) { data.Append("missing"); return; }
                var path=AssetDatabase.GetAssetPath(asset);
                data.Append(AssetDatabase.AssetPathToGUID(path)).Append(AssetDatabase.GetAssetDependencyHash(path)).Append(EditorJsonUtility.ToJson(asset));
            }
            Asset(district.style);
            foreach(var p in district.parcels.OrderBy(p=>p.id,StringComparer.Ordinal)) { data.Append(JsonUtility.ToJson(p)); Asset(p.kit!=null?p.kit:district.style==null?null:district.style.kit); }
            foreach(var b in district.blocks.OrderBy(b=>b.id,StringComparer.Ordinal)) data.Append(JsonUtility.ToJson(b));
            foreach(var r in district.reservations.OrderBy(r=>r.id,StringComparer.Ordinal)) data.Append(JsonUtility.ToJson(r));
            foreach(var o in district.overrides.OrderBy(o=>o.key,StringComparer.Ordinal)) data.Append(JsonUtility.ToJson(o));
            return Hash(data.ToString());
        }
        [Serializable] private sealed class Position { public Vector3 value; }

        public static CityPlan Build(CityDistrict district,Func<float,bool> cancel = null)
        {
            var watch=System.Diagnostics.Stopwatch.StartNew(); var plan=new CityPlan();
            ValidateSource(district,plan.diagnostics);
            if(!plan.Valid) return plan;
            plan.fingerprint=Fingerprint(district);
            var roads=CityGeometry.Polygons(CityGeometry.RoadFootprints(district));
            int parcelIndex=0;
            foreach(var parcel in district.parcels.OrderBy(p=>p.id,StringComparer.Ordinal))
            {
                if(cancel!=null && cancel((float)parcelIndex++/Math.Max(1,district.parcels.Count))) throw new OperationCanceledException("City generation cancelled; committed output is unchanged.");
                var centroid=CityGeometry.Bounds(parcel.polygon).center;
                bool access=CheckAccess(district,parcel,plan.diagnostics);
                plan.locations.Add(new CityLocationRecord { id=parcel.id,label=parcel.label,districtId=district.id,blockId=parcel.blockId,
                    use=parcel.use,footprint=parcel.polygon.Copy(),localPosition=new Vector3(centroid.x,parcel.padHeight,centroid.y),
                    entranceLane=parcel.entrance.laneId,accessValidated=access,cells=CityGeometry.Cells(parcel.polygon,district.cellSize) });
                if(!parcel.generate) continue;
                var kit=parcel.kit!=null?parcel.kit:district.style.kit;
                if(!ValidateKit(kit,parcel,plan.diagnostics)) continue;
                int start=plan.instances.Count;
                try
                {
                    var envelopes=CityGeometry.Inset(parcel.polygon,parcel.setback);
                    if(envelopes.Count==0) throw new ArgumentException("Setback consumes the entire parcel.");
                    var envelope=envelopes.OrderByDescending(CityGeometry.Area).First();
                    if(parcel.use==CityLandUse.Park || parcel.use==CityLandUse.ServiceYard) AddYard(plan,parcel,kit,envelope);
                    else if(parcel.use==CityLandUse.Parking) AddParking(plan,parcel,kit,envelope);
                    else AddStructure(plan,parcel,kit,envelope);
                    AddDressing(plan,district,parcel,kit,envelope);
                    foreach(var item in plan.instances.Skip(start))
                    {
                        if(roads.Any(r=>CityGeometry.Overlaps(r,item.footprint))) throw new ArgumentException("Generated structure/dressing intrudes into a published surface-road footprint.");
                        if(district.reservations.Any(r=>item.position.y+item.boundsOffset.y+item.boundsSize.y/2>r.minimumHeight && item.position.y+item.boundsOffset.y-item.boundsSize.y/2<r.maximumHeight && CityGeometry.Overlaps(r.polygon,item.footprint)))
                            throw new ArgumentException("Generated content overlaps a protected reservation.");
                        if(parcel.entrance.enabled && item.collision && CityGeometry.Overlaps(AccessEnvelope(parcel),item.footprint))
                            throw new ArgumentException("Generated collision blocks the reserved entrance approach. Move the entrance or change the parcel envelope.");
                        item.cells=CityGeometry.Cells(item.footprint,district.cellSize);
                        item.signature=Hash(item.key+JsonUtility.ToJson(new ItemSignature { position=item.position,euler=item.euler,size=item.size,primitive=(int)item.primitive,collision=item.collision })
                            +AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(kit))+EditorJsonUtility.ToJson(kit));
                    }
                }
                catch(ArgumentException exception)
                {
                    plan.instances.RemoveRange(start,plan.instances.Count-start);
                    plan.diagnostics.Add(new CityDiagnostic("CITY_ENVELOPE",parcel.id,parcel.label+": "+exception.Message));
                }
                if(plan.instances.Count>25000) throw new ArgumentException("CITY_BUDGET: preview is limited to 25000 instances; split the district.");
            }
            var keys=new HashSet<string>(plan.instances.Select(i=>i.key));
            foreach(var state in district.overrides)
                if(state.state!=CityOwnership.Detached && !keys.Contains(state.key)) plan.diagnostics.Add(new CityDiagnostic("CITY_ANCHOR",state.key,"Override owner or generator slot no longer exists. Remap or release the override explicitly."));
            if(district.overrides.Count>0)
            {
                var existing=CityCommands.Existing(district);
                foreach(var state in district.overrides.Where(o=>o.state!=CityOwnership.Detached))
                {
                    if(!existing.TryGetValue(state.key,out var instance)||instance.GetComponentInChildren<Renderer>()==null)continue;
                    var bounds=CityMapLibrary.RenderBounds(instance.gameObject);var low=bounds.min-district.transform.position;var high=bounds.max-district.transform.position;
                    var footprint=CityPolygon.Rectangle(low.x,low.z,bounds.size.x,bounds.size.z);
                    if(low.y<district.surfaceLevelTolerance&&high.y>-district.surfaceLevelTolerance&&roads.Any(r=>CityGeometry.Overlaps(r,footprint)) || district.reservations.Any(r=>low.y<r.maximumHeight&&high.y>r.minimumHeight&&CityGeometry.Overlaps(r.polygon,footprint)))
                        plan.diagnostics.Add(new CityDiagnostic("CITY_OVERRIDE_CLEARANCE",state.key,"Preserved object bounds overlap a protected road or reservation. Adjust the object and recapture its override."));
                }
            }
            foreach(var cell in plan.instances.SelectMany(i=>i.cells.Select(c=>(cell:c,item:i))).GroupBy(pair=>pair.cell))
            {
                int colliders=cell.Count(p=>p.item.collision),vertices=cell.Sum(p=>p.item.vertices);
                if(cell.Count()>district.style.maximumInstancesPerCell || colliders>district.style.maximumCollidersPerCell || vertices>district.style.maximumVerticesPerCell)
                    plan.diagnostics.Add(new CityDiagnostic("CITY_CELL_BUDGET",district.id,"Cell "+cell.Key+": "+cell.Count()+" instances, "+colliders+" colliders, "+vertices+" estimated vertices."));
            }
            plan.milliseconds=watch.Elapsed.TotalMilliseconds; return plan;
        }
        [Serializable] private sealed class ItemSignature { public Vector3 position,euler,size; public int primitive; public bool collision; }
        public static void ValidateSource(CityDistrict d,List<CityDiagnostic> output)
        {
            if(d==null) { output.Add(new CityDiagnostic("CITY_SOURCE","","Select a district.")); return; }
            void Error(string rule,string owner,string message) => output.Add(new CityDiagnostic(rule,owner,message));
            if(d.schemaVersion!=CityDistrict.CurrentSchema) Error("CITY_SCHEMA",d.id,"Unsupported district schema; explicit migration is required.");
            if(d.transform.lossyScale!=Vector3.one || Quaternion.Angle(d.transform.rotation,Quaternion.identity)>0.001f) Error("CITY_TRANSFORM",d.id,"Districts require identity rotation and unit scale through the hierarchy.");
            if(d.style==null) Error("CITY_STYLE",d.id,"Assign a district style.");
            else if(d.style.schemaVersion!=1 || d.style.minimumParcelArea<1 || !float.IsFinite(d.style.minimumParcelArea) || d.style.targetParcelArea<d.style.minimumParcelArea || !float.IsFinite(d.style.targetParcelArea)
                || d.style.dressingSpacing<1 || !float.IsFinite(d.style.dressingSpacing) || d.style.dressingPerParcel<0 || d.style.dressingPerParcel>1000)
                Error("CITY_STYLE",d.id,"Style area/dressing constraints or schema are invalid.");
            if(d.cellSize<10 || !float.IsFinite(d.cellSize) || d.surfaceLevelTolerance<=0 || !float.IsFinite(d.surfaceLevelTolerance)) Error("CITY_SCALE",d.id,"Cell size/level tolerance must be finite and positive.");
            var ids=new HashSet<string>();
            void Identity(string id) { if(!Guid.TryParseExact(id,"N",out _) || !ids.Add(id)) Error("CITY_ID",id,"Missing/duplicate stable identity. Use the explicit city duplicate/create commands."); }
            void Polygon(string owner,CityPolygon polygon) { try { CityGeometry.Validate(polygon); } catch(ArgumentException e) { Error("CITY_POLYGON",owner,e.Message); } }
            Identity(d.id); Polygon(d.id,d.boundary);
            if(d.blocks.Any(b=>b==null)||d.parcels.Any(p=>p==null||p.entrance==null)||d.reservations.Any(r=>r==null)||d.overrides.Any(o=>o==null))
            {Error("CITY_SOURCE",d.id,"Remove null source entries and restore missing entrance records.");return;}
            if(d.overrides.Any(o=>string.IsNullOrEmpty(o.key))||d.overrides.Select(o=>o.key).Distinct().Count()!=d.overrides.Count)
                Error("CITY_OVERRIDE_ID",d.id,"Override keys must be present and unique.");
            foreach(var b in d.blocks) { Identity(b.id); Polygon(b.id,b.polygon); }
            foreach(var p in d.parcels)
            {
                Identity(p.id); Polygon(p.id,p.polygon);
                if(!float.IsFinite(p.padHeight)||!float.IsFinite(p.setback)||p.setback<0||!float.IsFinite(p.coverage)||p.coverage<=0||p.coverage>1||p.floors<1||p.floors>60) Error("CITY_PARCEL",p.id,"Invalid height, setback, coverage or floor count.");
                if(!d.blocks.Any(b=>b.id==p.blockId)) Error("CITY_BLOCK_REFERENCE",p.id,"Parcel references a missing block; remap it explicitly.");
            }
            foreach(var r in d.reservations) { Identity(r.id); Polygon(r.id,r.polygon); }
            if(output.Any(di=>di.severity==CitySeverity.Error)) return;
            foreach(var b in d.blocks) if(!CityGeometry.Fits(d.boundary,b.polygon)) Error("CITY_BOUNDARY",b.id,"Block leaves the district boundary.");
            for(int i=0;i<d.parcels.Count;i++)
            {
                var p=d.parcels[i];
                if(!CityGeometry.Fits(d.blocks.First(b=>b.id==p.blockId).polygon,p.polygon)) Error("CITY_PARCEL_BOUNDARY",p.id,"Parcel leaves its approved block.");
                for(int j=0;j<i;j++) if(Mathf.Abs(p.padHeight-d.parcels[j].padHeight)<2 && CityGeometry.Overlaps(p.polygon,d.parcels[j].polygon)) Error("CITY_PARCEL_OVERLAP",p.id,"Parcel overlaps "+d.parcels[j].label);
            }
            foreach(var source in UnityEngine.Object.FindObjectsByType<RoadNetworkAuthoring>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(d.roads!=null && source.Id==d.roads.NetworkId && (source.Baked!=d.roads || !RoadNetworkBake.IsCurrent(source)))
                    Error("CITY_ROAD_REVISION",d.id,"Upstream road source changed. Bake roads, explicitly refresh this district's publication, then review a new city preview.");
            if(d.roads==null) output.Add(new CityDiagnostic("CITY_ROADS",d.id,"No road publication: manual composition is available, but vehicle access is unvalidated.",CitySeverity.Warning));
        }
        private static bool ValidateKit(CityKit kit,CityParcel p,List<CityDiagnostic> errors)
        {
            if(kit==null || kit.schemaVersion!=1 || kit.wall==null || kit.roof==null || kit.trim==null || kit.glass==null || kit.yard==null
                || !float.IsFinite(kit.bayWidth)||kit.bayWidth<1 || !float.IsFinite(kit.floorHeight)||kit.floorHeight<1 || !float.IsFinite(kit.minimumWidth)||!float.IsFinite(kit.minimumDepth)||kit.minimumWidth<1||kit.minimumDepth<1||p.floors>kit.maximumFloors)
            { errors.Add(new CityDiagnostic("CITY_KIT",p.id,"Assign a valid kit with wall, roof, trim, glass and yard materials and supported dimensions/floor count.")); return false; }
            if(kit.exteriorPrefab!=null && !PrefabUtility.IsPartOfPrefabAsset(kit.exteriorPrefab))
            { errors.Add(new CityDiagnostic("CITY_PREFAB",p.id,"Kit exterior must reference a prefab asset.")); return false; }
            if(kit.exteriorPrefab!=null && kit.exteriorPrefab.GetComponentInChildren<LODGroup>()==null)
                errors.Add(new CityDiagnostic("CITY_LOD",p.id,"Exterior prefab has no LODGroup; review distant rendering cost.",CitySeverity.Warning));
            return true;
        }
        private static Rect Fit(CityPolygon envelope,float coverage,float minWidth,float minDepth)
        {
            var bounds=CityGeometry.Bounds(envelope);
            float scale=Mathf.Sqrt(coverage);
            for(int attempt=0;attempt<12;attempt++,scale*=0.85f)
            {
                float w=bounds.width*scale,h=bounds.height*scale;
                if(w<minWidth||h<minDepth) break;
                for(int x=0;x<3;x++) for(int y=0;y<3;y++)
                {
                    var centre=bounds.center+new Vector2((x-1)*(bounds.width-w)*0.45f,(y-1)*(bounds.height-h)*0.45f);
                    var rect=new Rect(centre.x-w/2,centre.y-h/2,w,h);
                    if(CityGeometry.Fits(envelope,CityPolygon.Rectangle(rect.x,rect.y,w,h))) return rect;
                }
            }
            throw new ArgumentException("No footprint meets minimum kit dimensions, holes and setbacks.");
        }
        private static CityPlannedInstance Box(CityPlan plan,CityParcel p,string key,Vector3 pos,Vector3 size,Material mat,bool collision=false)
        {
            var item=new CityPlannedInstance { key=p.id+"/"+key,parcelId=p.id,label=key,position=pos,size=size,boundsSize=size,material=mat,collision=collision,
                footprint=CityPolygon.Rectangle(pos.x-size.x/2,pos.z-size.z/2,size.x,size.z) };
            plan.instances.Add(item); return item;
        }
        private static void AddStructure(CityPlan plan,CityParcel p,CityKit kit,CityPolygon envelope)
        {
            var rect=Fit(envelope,p.coverage,kit.minimumWidth,kit.minimumDepth); var c=rect.center;
            float height=p.floors*kit.floorHeight;
            if(kit.exteriorPrefab!=null)
            {
                if(kit.prefabDimensions.x<=0||kit.prefabDimensions.y<=0||kit.prefabDimensions.z<=0) throw new ArgumentException("Prefab dimensions must be positive.");
                var item=Box(plan,p,"exterior",new Vector3(c.x,p.padHeight,c.y),new Vector3(rect.width,height,rect.height),kit.wall,kit.collision);
                item.prefab=kit.exteriorPrefab;
                item.boundsOffset=Vector3.up*height/2;
                item.size=new Vector3(rect.width/kit.prefabDimensions.x,height/kit.prefabDimensions.y,rect.height/kit.prefabDimensions.z);
                item.vertices=kit.exteriorPrefab.GetComponentsInChildren<MeshFilter>(true).Sum(m=>m.sharedMesh==null?0:m.sharedMesh.vertexCount); return;
            }
            Box(plan,p,"structure",new Vector3(c.x,p.padHeight+height/2,c.y),new Vector3(rect.width,height,rect.height),kit.wall,kit.collision);
            Box(plan,p,"roof",new Vector3(c.x,p.padHeight+height+0.2f,c.y),new Vector3(rect.width+0.2f,0.4f,rect.height+0.2f),kit.roof);
            int bays=Mathf.Clamp(Mathf.FloorToInt(rect.width/kit.bayWidth),1,64);
            for(int floor=0;floor<p.floors;floor++) for(int bay=0;bay<bays;bay++)
            {
                float x=rect.x+(bay+0.5f)*rect.width/bays,y=p.padHeight+(floor+0.55f)*kit.floorHeight;
                Box(plan,p,"facade/"+floor+"/"+bay,new Vector3(x,y,rect.yMin-0.04f),new Vector3(rect.width/bays*0.6f,kit.floorHeight*0.38f,0.08f),kit.glass);
                Box(plan,p,"rear/"+floor+"/"+bay,new Vector3(x,y,rect.yMax+0.04f),new Vector3(rect.width/bays*0.6f,kit.floorHeight*0.38f,0.08f),kit.glass);
            }
            if(kit.loadingDock && p.use==CityLandUse.Industrial)
            {
                Box(plan,p,"dock",new Vector3(c.x,p.padHeight+0.5f,rect.yMin-1.1f),new Vector3(Mathf.Min(rect.width*0.45f,8),1,2),kit.trim,kit.collision);
                Box(plan,p,"loading-door",new Vector3(c.x,p.padHeight+2.5f,rect.yMin-0.1f),new Vector3(3.5f,3.5f,0.15f),kit.trim);
            }
            if(kit.roofDressing) Box(plan,p,"rooftop-plant",new Vector3(c.x,p.padHeight+height+0.9f,c.y),new Vector3(3,1.2f,2),kit.trim);
            if(kit.roofFamily!=CityRoof.Flat)
            {
                int strips=kit.roofFamily==CityRoof.Sawtooth?4:2;
                for(int i=0;i<strips;i++)
                {
                    var roof=Box(plan,p,"roof-slope/"+i,new Vector3(c.x,p.padHeight+height+0.65f,rect.y+(i+0.5f)*rect.height/strips),new Vector3(rect.width,0.2f,rect.height/strips),kit.roof);
                    roof.euler=new Vector3(kit.roofFamily==CityRoof.Pitched?(i==0?-8:8):8,0,0);
                }
            }
        }
        private static void AddYard(CityPlan plan,CityParcel p,CityKit kit,CityPolygon envelope)
        {
            var r=Fit(envelope,0.85f,4,4);
            Box(plan,p,"yard-pad",new Vector3(r.center.x,p.padHeight-0.04f,r.center.y),new Vector3(r.width,0.08f,r.height),kit.yard);
            if(p.use==CityLandUse.ServiceYard)
                for(int i=0;i<3;i++) Box(plan,p,"container/"+i,new Vector3(r.x+3+i*3,p.padHeight+1.3f,r.y+4),new Vector3(2.4f,2.6f,6),kit.wall,kit.collision);
        }
        private static void AddParking(CityPlan plan,CityParcel p,CityKit kit,CityPolygon envelope)
        {
            var r=Fit(envelope,0.9f,12,16);
            Box(plan,p,"parking-pad",new Vector3(r.center.x,p.padHeight-0.04f,r.center.y),new Vector3(r.width,0.08f,r.height),kit.yard);
            int count=Mathf.FloorToInt((r.width-4)/2.6f);
            for(int i=0;i<=count;i++) for(int side=0;side<2;side++)
                Box(plan,p,"stall-line/"+side+"/"+i,new Vector3(r.x+2+i*2.6f,p.padHeight+0.015f,side==0?r.y+3:r.yMax-3),new Vector3(0.08f,0.03f,5),kit.trim);
            // Aisle remains clear; no active or permanently occupied traffic agents are generated.
        }
        private static void AddDressing(CityPlan plan,CityDistrict d,CityParcel p,CityKit kit,CityPolygon envelope)
        {
            uint seed=Convert.ToUInt32(Hash(d.worldSeed+"/"+d.seed+"/"+p.blockId+"/"+p.id+"/"+p.seed).Substring(0,8),16);
            var random=new System.Random(unchecked((int)seed)); var b=CityGeometry.Bounds(envelope);
            var positions=new List<Vector2>(); var occupied=plan.instances.Where(i=>i.parcelId==p.id && i.collision).Select(i=>i.footprint).ToArray();
            int desired=d.style.dressingPerParcel;
            for(int attempt=0;attempt<desired*30 && positions.Count<desired;attempt++)
            {
                var pos=new Vector2(Mathf.Lerp(b.xMin,b.xMax,(float)random.NextDouble()),Mathf.Lerp(b.yMin,b.yMax,(float)random.NextDouble()));
                if(!CityGeometry.Contains(envelope,pos)||positions.Any(q=>Vector2.Distance(pos,q)<d.style.dressingSpacing)) continue;
                var valid=kit.dressing.Where(a=>a!=null && a.prefab!=null && a.weight>0 && float.IsFinite(a.weight)).ToArray();
                CityDressingAsset asset=null;
                if(valid.Length>0)
                {
                    double pick=random.NextDouble()*valid.Sum(a=>a.weight);
                    foreach(var candidate in valid) { pick-=candidate.weight; if(pick<=0) { asset=candidate; break; } }
                    asset=asset??valid[valid.Length-1];
                }
                float radius=asset==null?1:asset.clearanceRadius;
                if(!float.IsFinite(radius)||radius<=0) throw new ArgumentException("Dressing clearance radius must be finite and positive.");
                var footprint=CityPolygon.Rectangle(pos.x-radius,pos.y-radius,radius*2,radius*2);
                if(!CityGeometry.Fits(envelope,footprint)||occupied.Any(o=>CityGeometry.Overlaps(o,footprint))||p.entrance.enabled && CityGeometry.Overlaps(AccessEnvelope(p),footprint)) continue;
                int slot=positions.Count; positions.Add(pos);
                if(asset==null)
                {
                    if(p.use!=CityLandUse.Park && p.use!=CityLandUse.Residential) continue;
                    var trunk=Box(plan,p,"tree-trunk/"+slot,new Vector3(pos.x,p.padHeight+1.5f,pos.y),new Vector3(0.4f,3,0.4f),kit.wall,true);
                    var crown=Box(plan,p,"tree-crown/"+slot,new Vector3(pos.x,p.padHeight+3.6f,pos.y),new Vector3(2,3,2),kit.roof);
                    crown.primitive=PrimitiveType.Sphere; crown.vertices=515;
                }
                else
                {
                    float scale=Mathf.Lerp(asset.scaleRange.x,asset.scaleRange.y,(float)random.NextDouble());
                    if(!float.IsFinite(scale)||scale<=0 || !PrefabUtility.IsPartOfPrefabAsset(asset.prefab)) throw new ArgumentException("Dressing requires prefab assets and positive finite scales.");
                    var item=Box(plan,p,"dressing/"+slot,new Vector3(pos.x,p.padHeight,pos.y),Vector3.one*scale,kit.trim);
                    item.prefab=asset.prefab; item.euler=new Vector3(0,(float)random.NextDouble()*360,0); item.footprint=footprint;
                    var bounds=CityMapLibrary.RenderBounds(asset.prefab);item.boundsSize=bounds.size*scale;item.boundsOffset=bounds.center*scale;
                    item.vertices=asset.prefab.GetComponentsInChildren<MeshFilter>(true).Sum(m=>m.sharedMesh==null?0:m.sharedMesh.vertexCount);
                }
            }
        }
        public static CityPolygon AccessEnvelope(CityParcel parcel)
        {
            var e=parcel.entrance;
            return CityPolygon.Rectangle(e.localPosition.x-e.width/2,e.localPosition.z-e.approachLength/2,e.width,e.approachLength);
        }
        public static bool CheckAccess(CityDistrict d,CityParcel p,List<CityDiagnostic> diagnostics,float vehicleWidth=2.5f,float vehicleHeight=3.2f)
        {
            var e=p.entrance; if(!e.enabled) { diagnostics.Add(new CityDiagnostic("CITY_ACCESS_UNVALIDATED",p.id,p.label+": no entrance authored.",CitySeverity.Warning)); return false; }
            string error=null;
            var lane=d.roads==null?null:d.roads.Lanes.FirstOrDefault(l=>l.Id==e.laneId);
            if(!e.approvedPublicAccess) error="Explicit public-road access approval is missing.";
            else if(lane==null) error="Referenced lane is missing; remap explicitly.";
            else if(!float.IsFinite(e.laneDistance)||e.laneDistance<0||e.laneDistance>lane.Length) error="Lane anchor distance is out of range.";
            else if(!float.IsFinite(e.width)||!float.IsFinite(e.height)||e.width<vehicleWidth+0.5f||e.height<vehicleHeight+0.3f) error="Entrance width/height cannot clear the selected vehicle plus margins.";
            else
            {
                var anchor=lane.Sample(e.laneDistance).position-d.transform.position;
                float horizontal=Vector2.Distance(new Vector2(anchor.x,anchor.z),new Vector2(e.localPosition.x,e.localPosition.z));
                float rise=Mathf.Abs(anchor.y-e.localPosition.y);
                if(horizontal>e.approachLength+lane.Sample(e.laneDistance).width/2) error="Entrance is beyond its approved approach envelope. Create a service-road proposal and connect it in the Road Editor.";
                else if(Mathf.Atan2(rise,Mathf.Max(horizontal,0.01f))*Mathf.Rad2Deg>e.maximumSlopeDegrees) error="Entrance approach exceeds the authored grade limit.";
                else if(!CityGeometry.Contains(p.polygon,new Vector2(e.localPosition.x,e.localPosition.z))) error="Entrance destination is outside its parcel.";
            }
            if(error!=null) diagnostics.Add(new CityDiagnostic("CITY_ACCESS",p.id,p.label+": "+error));
            return error==null;
        }
    }
}
