using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    public sealed class CityDiff
    {
        public readonly List<string> create=new List<string>(),update=new List<string>(),delete=new List<string>(),preserve=new List<string>();
        public override string ToString() => $"Create {create.Count}  ·  Update {update.Count}  ·  Delete {delete.Count}  ·  Preserve {preserve.Count}";
    }
    public static class CityCommands
    {
        public static void Edit(CityDistrict district,string name,Action action)
        {
            if(district==null || EditorApplication.isPlayingOrWillChangePlaymode) throw new ArgumentException("Choose a district outside Play mode.");
            Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup(); Undo.RegisterCompleteObjectUndo(district,name);
            try { action(); Changed(district); Undo.CollapseUndoOperations(group); }
            catch { Undo.RevertAllDownToGroup(group); throw; }
        }
        internal static void Changed(Component source)
        { EditorUtility.SetDirty(source); PrefabUtility.RecordPrefabInstancePropertyModifications(source); EditorSceneManager.MarkSceneDirty(source.gameObject.scene); SceneView.RepaintAll(); }
        public static CityDistrict Create(CityStyle style,Vector3 origin,Scene scene)
        {
            if(!scene.IsValid()||!scene.isLoaded||EditorApplication.isPlayingOrWillChangePlaymode) throw new ArgumentException("Create a district in a loaded editing scene.");
            var go=new GameObject("City District"); SceneManager.MoveGameObjectToScene(go,scene); go.transform.position=origin;
            var d=go.AddComponent<CityDistrict>(); d.id=Guid.NewGuid().ToString("N"); d.style=style;
            Undo.RegisterCreatedObjectUndo(go,"Create city district"); Changed(d); return d;
        }
        public static CityBlock AddBlock(CityDistrict d,CityPolygon polygon,string label="Block")
        {
            CityGeometry.Validate(polygon); if(!CityGeometry.Fits(d.boundary,polygon)) throw new ArgumentException("Block must fit inside the district.");
            var b=new CityBlock { id=Guid.NewGuid().ToString("N"),label=label,polygon=polygon.Copy() };
            Edit(d,"Approve city block",()=>d.blocks.Add(b)); return b;
        }
        public static CityParcel AddParcel(CityDistrict d,CityBlock block,CityPolygon polygon)
        {
            CityGeometry.Validate(polygon); if(block==null || !d.blocks.Contains(block)||!CityGeometry.Fits(block.polygon,polygon)) throw new ArgumentException("Parcel must fit an approved block.");
            var p=new CityParcel { id=Guid.NewGuid().ToString("N"),blockId=block.id,label="Parcel "+(d.parcels.Count+1),polygon=polygon.Copy(),
                use=d.style==null?CityLandUse.Industrial:d.style.defaultUse,setback=d.style==null?3:d.style.setback,
                kit=d.style==null?null:d.style.kit,coverage=d.style==null?0.6f:d.style.coverage,floors=d.style==null?2:d.style.minimumFloors };
            Edit(d,"Approve city parcel",()=>d.parcels.Add(p)); return p;
        }
        public static void SplitParcel(CityDistrict d,CityParcel parcel,Vector2 a,Vector2 b)
        {
            if(parcel.locked) throw new ArgumentException("Unlock the parcel before changing its boundary.");
            if(d.overrides.Any(o=>o.key.StartsWith(parcel.id+"/",StringComparison.Ordinal))) throw new ArgumentException("Remap/release generator overrides before splitting their parcel anchor.");
            var cuts=CityGeometry.Cut(parcel.polygon,a,b);
            if(d.style!=null && cuts.Any(p=>CityGeometry.Area(p)<d.style.minimumParcelArea)) throw new ArgumentException("Cut violates the style's minimum parcel area.");
            Edit(d,"Split city parcel",()=>
            {
                d.parcels.Remove(parcel);
                for(int i=0;i<cuts.Count;i++)
                {
                    var copy=JsonUtility.FromJson<CityParcel>(JsonUtility.ToJson(parcel)); copy.id=Guid.NewGuid().ToString("N");
                    copy.label=parcel.label+" / "+(i+1); copy.polygon=cuts[i]; copy.entrance.enabled=false; d.parcels.Add(copy);
                }
            });
        }
        public static void MergeParcels(CityDistrict d,CityParcel a,CityParcel b)
        {
            if(a==b||a.locked||b.locked||a.blockId!=b.blockId) throw new ArgumentException("Choose two unlocked parcels in the same block.");
            if(d.overrides.Any(o=>o.key.StartsWith(a.id+"/",StringComparison.Ordinal)||o.key.StartsWith(b.id+"/",StringComparison.Ordinal))) throw new ArgumentException("Remap/release overrides before merging parcel anchors.");
            var union=CityGeometry.Merge(a.polygon,b.polygon); if(union.Count!=1) throw new ArgumentException("Parcels must share a boundary.");
            Edit(d,"Merge city parcels",()=> { d.parcels.Remove(b); a.polygon=union[0]; a.entrance.enabled=false; });
        }
        public static CityDistrict Duplicate(CityDistrict source,Vector3 offset)
        {
            var go=new GameObject(source.name+" Copy"); SceneManager.MoveGameObjectToScene(go,source.gameObject.scene); go.transform.position=source.transform.position+offset;
            var d=go.AddComponent<CityDistrict>(); EditorUtility.CopySerialized(source,d);
            d.id=Guid.NewGuid().ToString("N"); d.publication=null; d.overrides.Clear();
            var blockMap=new Dictionary<string,string>(); foreach(var b in d.blocks) { string old=b.id;b.id=Guid.NewGuid().ToString("N");blockMap.Add(old,b.id); }
            foreach(var p in d.parcels) { p.id=Guid.NewGuid().ToString("N");p.blockId=blockMap[p.blockId];p.entrance.enabled=false; }
            foreach(var r in d.reservations) r.id=Guid.NewGuid().ToString("N");
            Undo.RegisterCreatedObjectUndo(go,"Duplicate city district"); Changed(d); return d;
        }
        public static Dictionary<string,CityGeneratedInstance> Existing(CityDistrict d)
        {
            var result=new Dictionary<string,CityGeneratedInstance>(StringComparer.Ordinal);
            foreach(var item in d.GetComponentsInChildren<CityGeneratedInstance>(true))
            {
                if(item.districtId!=d.id) continue;
                if(!result.TryAdd(item.key,item)) throw new ArgumentException("CITY_OUTPUT_ID: duplicate generated key "+item.key);
            }
            return result;
        }
        public static CityDiff Diff(CityDistrict d,CityPlan plan)
        {
            var result=new CityDiff(); var existing=Existing(d); var next=new HashSet<string>();
            foreach(var item in plan.instances)
            {
                next.Add(item.key); var state=d.overrides.FirstOrDefault(o=>o.key==item.key);
                if(state!=null && state.state!=CityOwnership.Generated) result.preserve.Add(item.key);
                else if(!existing.TryGetValue(item.key,out var previous)) result.create.Add(item.key);
                else if(previous.signature==item.signature) result.preserve.Add(item.key);
                else result.update.Add(item.key);
            }
            foreach(var old in existing) if(!next.Contains(old.Key))
            {
                if(d.overrides.Any(o=>o.key==old.Key && o.state!=CityOwnership.Generated)) result.preserve.Add(old.Key);
                else result.delete.Add(old.Key);
            }
            return result;
        }
        public static void SetOwnership(CityDistrict d,CityGeneratedInstance item,CityOwnership state)
        {
            if(item==null||item.districtId!=d.id) throw new ArgumentException("Select an instance belonging to this district.");
            Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup(); Undo.RegisterCompleteObjectUndo(d,"Set city ownership");
            Undo.RecordObject(item,"Set city ownership");
            var record=d.overrides.FirstOrDefault(o=>o.key==item.key);
            if(record!=null) d.overrides.Remove(record);
            if(state!=CityOwnership.Generated) d.overrides.Add(new CityOverride { key=item.key,state=state,
                position=item.transform.position-d.transform.position,euler=item.transform.eulerAngles,scale=item.transform.lossyScale });
            item.state=state;
            if(state==CityOwnership.Detached)
            {
                Undo.SetTransformParent(item.transform,d.transform.parent,"Detach city instance");
                Undo.DestroyObjectImmediate(item);
            }
            else { Changed(item); }
            Changed(d); Undo.CollapseUndoOperations(group);
        }
        public static CityPublication Commit(CityDistrict d,CityPlan plan,string assetPath,Func<float,bool> cancel=null)
        {
            if(plan==null||!plan.Valid) throw new ArgumentException("CITY_VALIDATION: resolve errors before committing.");
            if(EditorApplication.isPlayingOrWillChangePlaymode || PrefabUtility.IsPartOfPrefabAsset(d)) throw new ArgumentException("Commit in an editing scene.");
            if(plan.fingerprint!=CityPlanning.Fingerprint(d)) throw new ArgumentException("CITY_STALE: inputs changed after preview. Generate and review again.");
            if(!assetPath.StartsWith("Assets/",StringComparison.Ordinal)||!assetPath.EndsWith(".asset",StringComparison.Ordinal)) throw new ArgumentException("Choose an asset path under Assets.");
            var existing=Existing(d); var diff=Diff(d,plan);
            foreach(var state in d.overrides)
                if(state.state!=CityOwnership.Detached && !existing.ContainsKey(state.key))
                    throw new ArgumentException("CITY_MISSING_PIN: restore or release missing override "+state.key);
            foreach(var item in existing.Values)
            {
                if(item.state==CityOwnership.Generated && (Vector3.Distance(item.transform.localPosition,item.plannedPosition)>0.001f || Quaternion.Angle(item.transform.localRotation,Quaternion.Euler(item.plannedEuler))>0.01f || Vector3.Distance(item.transform.localScale,item.plannedScale)>0.001f))
                    throw new ArgumentException("CITY_MANUAL_EDIT: capture an override/pin or reset the moved object before regeneration: "+item.key);
                if((diff.update.Contains(item.key)||diff.delete.Contains(item.key)) && item.transform.childCount>0 && item.GetComponent<CityPrefabOutput>()==null)
                    throw new ArgumentException("CITY_MANUAL_CHILD: detach manually added children before replacing "+item.key);
                if(diff.update.Contains(item.key)||diff.delete.Contains(item.key))
                {
                    bool Owned(Component c)=>c is Transform||c is MeshFilter||c is MeshRenderer||c is Collider||c is CityGeneratedInstance||c is CityPrefabOutput;
                    if(item.GetComponent<CityPrefabOutput>()==null && item.GetComponents<Component>().Any(c=>c==null||!Owned(c)))
                        throw new ArgumentException("CITY_MANUAL_COMPONENT: detach custom behavior before replacing "+item.key);
                    if(item.GetComponent<CityPrefabOutput>()!=null && (PrefabUtility.GetAddedGameObjects(item.gameObject).Count>0 || PrefabUtility.GetAddedComponents(item.gameObject).Any(c=>!Owned(c.instanceComponent))))
                        throw new ArgumentException("CITY_MANUAL_PREFAB: pin or detach custom prefab additions before replacing "+item.key);
                }
            }
            string path=AssetDatabase.GenerateUniqueAssetPath(assetPath); GameObject staging=null; CityPublication publication=null;
            var staged=new Dictionary<string,GameObject>(); int group=-1;
            try
            {
                staging=new GameObject("City transaction — temporary"); staging.SetActive(false);
                SceneManager.MoveGameObjectToScene(staging,d.gameObject.scene); staging.transform.position=d.transform.position;
                int index=0;
                foreach(var item in plan.instances)
                {
                    if(cancel!=null&&cancel((float)index++/Math.Max(1,plan.instances.Count))) throw new OperationCanceledException();
                    if(!diff.create.Contains(item.key)&&!diff.update.Contains(item.key)) continue;
                    staged.Add(item.key,Instantiate(item,staging.transform,d,false));
                }
                if(plan.fingerprint!=CityPlanning.Fingerprint(d)) throw new ArgumentException("CITY_STALE: source changed during staging.");
                publication=ScriptableObject.CreateInstance<CityPublication>(); publication.Initialize(d.id,plan.fingerprint,d.roads==null?"":d.roads.Fingerprint,d.transform.position,plan.locations.ToArray());
                AssetDatabase.CreateAsset(publication,path); AssetDatabase.SaveAssetIfDirty(publication);
                Undo.IncrementCurrentGroup(); group=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Commit city district");
                var locations=d.GetComponent<CityLocations>(); if(locations==null) locations=Undo.AddComponent<CityLocations>(d.gameObject);
                foreach(var key in diff.delete.Concat(diff.update)) if(existing.TryGetValue(key,out var old)) Undo.DestroyObjectImmediate(old.gameObject);
                foreach(var obj in staged.Values)
                {
                    obj.transform.SetParent(d.transform,false); obj.SetActive(true); Undo.RegisterCreatedObjectUndo(obj,"Create city output");
                }
                // Register created objects before recording serialized publication changes: creation flushes Undo recording.
                Undo.RecordObject(d,"Publish city district");Undo.RecordObject(locations,"Publish city locations");
                locations.Configure(publication);d.publication=publication; Changed(d); Changed(locations); Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
                return publication;
            }
            catch
            {
                if(group>=0) Undo.RevertAllDownToGroup(group);
                if(publication!=null && AssetDatabase.GetAssetPath(publication)==path) AssetDatabase.DeleteAsset(path);
                throw;
            }
            finally { if(staging!=null) UnityEngine.Object.DestroyImmediate(staging); }
        }
        internal static GameObject Instantiate(CityPlannedInstance item,Transform parent,CityDistrict district,bool preview)
        {
            GameObject go;
            if(item.prefab!=null && !preview)
            { go=(GameObject)PrefabUtility.InstantiatePrefab(item.prefab,parent); go.AddComponent<CityPrefabOutput>(); }
            else
            {
                // Preview renders safe bounds for prefabs without executing arbitrary prefab scripts.
                go=GameObject.CreatePrimitive(item.prefab!=null?PrimitiveType.Cube:item.primitive); go.transform.SetParent(parent,false);
                var renderer=go.GetComponent<MeshRenderer>(); renderer.sharedMaterial=item.material;
                if(preview || !item.collision) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            }
            go.name=(preview?"Preview · ":"")+item.label;
            go.transform.localPosition=item.position; go.transform.localRotation=Quaternion.Euler(item.euler); go.transform.localScale=item.size;
            if(preview && item.prefab!=null) {go.transform.localScale=item.boundsSize;go.transform.localPosition+=Quaternion.Euler(item.euler)*item.boundsOffset;}
            if(preview) go.hideFlags=HideFlags.HideAndDontSave;
            else
            {
                var marker=go.AddComponent<CityGeneratedInstance>(); marker.districtId=district.id; marker.parcelId=item.parcelId; marker.key=item.key;
                marker.signature=item.signature; marker.plannedPosition=item.position; marker.plannedEuler=item.euler; marker.plannedScale=item.size;
                PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            }
            return go;
        }
    }
}
