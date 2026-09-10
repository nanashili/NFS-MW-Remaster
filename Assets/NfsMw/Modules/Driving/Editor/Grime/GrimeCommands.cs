using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace NfsMwRemaster.Driving.Editor
{
    public static class GrimeCommands
    {
        public static void Edit(GrimeCanvas c,string name,Action action)
        { Undo.RecordObject(c,name); action(); EditorUtility.SetDirty(c); PrefabUtility.RecordPrefabInstancePropertyModifications(c); EditorSceneManager.MarkSceneDirty(c.gameObject.scene); SceneView.RepaintAll(); }
        public static void FreshIdentity(GrimeCanvas c)
        {
            Edit(c,"Assign fresh dressing identity",()=>
            {
                var ids=c.layers.ToDictionary(l=>l.id,l=>Guid.NewGuid().ToString("N"));
                c.id=Guid.NewGuid().ToString("N");c.published=null;c.generatedRoot=null;
                foreach(var l in c.layers)l.id=ids[l.id];
                foreach(var s in c.strokes){s.id=Guid.NewGuid().ToString("N");s.layerId=ids[s.layerId];}
                foreach(var m in c.masks){m.id=Guid.NewGuid().ToString("N");if(!string.IsNullOrEmpty(m.layerId))m.layerId=ids[m.layerId];}
            });
        }
        public static GrimeCanvas Create()
        { var go=new GameObject("Surface Dressing"); Undo.RegisterCreatedObjectUndo(go,"Create dressing canvas"); var c=Undo.AddComponent<GrimeCanvas>(go); Selection.activeGameObject=go; return c; }
        public static GrimeReceiver Register(GameObject go)
        {
            var filter=go.GetComponent<MeshFilter>(); var renderer=go.GetComponent<MeshRenderer>();
            if(!filter || !filter.sharedMesh || !renderer) throw new InvalidOperationException("Select a static MeshFilter/MeshRenderer surface.");
            if(go.GetComponentInParent<Rigidbody>()) throw new InvalidOperationException("Dynamic bodies are unsupported.");
            var r=go.GetComponent<GrimeReceiver>(); if(r) return r;
            var collider=go.GetComponent<MeshCollider>(); if(!collider) { collider=Undo.AddComponent<MeshCollider>(go); collider.sharedMesh=filter.sharedMesh; }
            r=Undo.AddComponent<GrimeReceiver>(go); Undo.RecordObject(r,"Configure grime receiver"); r.surface=collider; r.surfaceRenderer=renderer; PrefabUtility.RecordPrefabInstancePropertyModifications(r); return r;
        }
        public static GrimeStroke RoadRange(GrimeCanvas c,GrimeBrush brush,GrimeReceiver receiver,RoadNetworkAsset network,string laneId,float start,float end,float lateral,int seed)
        {
            if(!network || !brush) throw new InvalidOperationException("Choose a road network and brush.");
            var lane=network.Lanes.FirstOrDefault(l=>l.Id.ToString()==laneId);
            if(lane==null || !float.IsFinite(start)||!float.IsFinite(end)||!float.IsFinite(lateral)||start<0||end<start||end>lane.Length) throw new InvalidOperationException("Select an exact lane and valid station range.");
            var layer=c.layers.FirstOrDefault(l=>l.enabled&&!l.locked); if(layer==null) throw new InvalidOperationException("No enabled unlocked layer.");
            var s=new GrimeStroke{layerId=layer.id,brush=brush,brushRevision=GrimeGeometry.BrushRevision(brush),width=brush.width,seed=seed};
            int count=Mathf.CeilToInt((end-start)/Mathf.Max(.1f,brush.width*.25f)); if(count>20000) throw new InvalidOperationException("Road range exceeds 20,000 source anchors.");
            for(int i=0;i<=count;i++)
            {
                float station=count==0?start:Mathf.Lerp(start,end,i/(float)count); var sample=lane.Sample(station);
                s.samples.Add(new GrimeAnchor{kind=GrimeAnchorKind.Road,receiver=receiver,receiverId=receiver?receiver.id:"",network=network,laneId=laneId,roadRevision=network.Fingerprint,station=station,lateral=lateral,position=sample.position+sample.left*lateral,normal=sample.up,tangent=sample.forward});
            }
            return s;
        }
        public static void Add(GrimeCanvas c,GrimeStroke stroke)
        {
            var layer=c.layers.FirstOrDefault(l=>l.id==stroke.layerId); if(layer==null||layer.locked) throw new InvalidOperationException("Target layer is locked or missing.");
            Edit(c,"Paint surface dressing",()=>c.strokes.Add(stroke));
        }
        public static string Signature(IEnumerable<GrimeChunk> chunks)
        {
            var text=new System.Text.StringBuilder();
            foreach(var c in chunks)
            {
                if(!c.mesh||!c.material) return "missing";
                text.Append(c.key).Append(EditorJsonUtility.ToJson(c.material));
                foreach(var p in c.mesh.vertices) text.Append(p.ToString("R"));
                foreach(var p in c.mesh.normals) text.Append(p.ToString("R"));
                foreach(var p in c.mesh.uv) text.Append(p.ToString("R"));
                foreach(var p in c.mesh.colors) text.Append(p.ToString("R"));
                foreach(var p in c.mesh.triangles) text.Append(p).Append(",");
            }
            return Hash128.Compute(text.ToString()).ToString();
        }
        public static bool OwnedOutputIntact(GrimeCanvas c)
        {
            if(!c.generatedRoot) return true;
            if(!c.published || c.published.Owner!=c.id || c.published.ArtifactSignature!=Signature(c.published.Chunks)) return false;
            var children=c.generatedRoot.GetComponentsInChildren<MeshFilter>(true); var chunks=c.published.Chunks;
            if(children.Length!=chunks.Length || c.generatedRoot.transform.childCount!=chunks.Length || c.generatedRoot.transform.parent!=c.transform || c.generatedRoot.transform.position!=Vector3.zero || c.generatedRoot.transform.rotation!=Quaternion.identity || c.generatedRoot.transform.lossyScale!=Vector3.one) return false;
            for(int i=0;i<children.Length;i++) if(children[i].sharedMesh!=chunks[i].mesh || children[i].transform.localPosition!=Vector3.zero || children[i].transform.localRotation!=Quaternion.identity || children[i].transform.localScale!=Vector3.one || children[i].GetComponent<MeshRenderer>()?.sharedMaterial!=chunks[i].material || !children[i].GetComponent<MeshRenderer>().enabled || children[i].GetComponent<MeshRenderer>().sharedMaterials.Length!=1 || children[i].GetComponent<MeshRenderer>().shadowCastingMode!=ShadowCastingMode.Off || children[i].GetComponents<Component>().Length!=3 || !children[i].gameObject.activeSelf) return false;
            return c.generatedRoot.GetComponents<Component>().Length==1;
        }
        static GrimeBuild BuildForBake(GrimeCanvas c)
        { try { return GrimeCompiler.Build(c,p=>EditorUtility.DisplayCancelableProgressBar("Bake dressing","Projecting approved receivers",p)); } finally { EditorUtility.ClearProgressBar(); } }
        public static GrimePublication Bake(GrimeCanvas c,string path,Action<int> failureInjection=null)
        {
            if(!path.StartsWith("Assets/",StringComparison.Ordinal)||!path.EndsWith(".asset",StringComparison.Ordinal)||AssetDatabase.LoadMainAssetAtPath(path)||System.IO.File.Exists(path)) throw new InvalidOperationException("Choose a new .asset path inside Assets; existing files are never overwritten.");
            if(!OwnedOutputIntact(c)) throw new InvalidOperationException("Generated art was modified. Detach previous output before rebuilding.");
            using(var build=BuildForBake(c))
            {
                EditorUtility.ClearProgressBar(); if(!build.valid) throw new InvalidOperationException(string.Join("\n",build.diagnostics));
                GrimePublication publication=null; GameObject root=null; bool assetCreated=false;
                try
                {
                    publication=ScriptableObject.CreateInstance<GrimePublication>(); AssetDatabase.CreateAsset(publication,path); assetCreated=true;
                    foreach(var chunk in build.chunks) { AssetDatabase.AddObjectToAsset(chunk.mesh,publication); if(!AssetDatabase.Contains(chunk.material)) AssetDatabase.AddObjectToAsset(chunk.material,publication); }
                    failureInjection?.Invoke(1);
                    publication.Initialize(c.id,build.fingerprint,Signature(build.chunks),build.chunks.ToArray()); EditorUtility.SetDirty(publication); AssetDatabase.SaveAssets();
                    failureInjection?.Invoke(2);
                    root=new GameObject("Generated Dressing "+c.id); root.SetActive(false); root.transform.SetParent(c.transform,true);
                    foreach(var chunk in publication.Chunks)
                    {
                        var go=new GameObject(chunk.key); go.transform.SetParent(root.transform,false); go.AddComponent<MeshFilter>().sharedMesh=chunk.mesh; var renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterial=chunk.material; renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=true;
                    }
                    failureInjection?.Invoke(3);
                    Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Publish surface dressing"); Undo.RegisterCreatedObjectUndo(root,"Publish surface dressing"); Undo.RecordObject(c,"Publish surface dressing");
                    if(c.generatedRoot) Undo.DestroyObjectImmediate(c.generatedRoot);
                    c.generatedRoot=root; c.published=publication; root.SetActive(true); EditorUtility.SetDirty(c); PrefabUtility.RecordPrefabInstancePropertyModifications(c); EditorSceneManager.MarkSceneDirty(c.gameObject.scene); Undo.CollapseUndoOperations(group);
                    return publication;
                }
                catch { if(root) UnityEngine.Object.DestroyImmediate(root); if(assetCreated) AssetDatabase.DeleteAsset(path); else if(publication) UnityEngine.Object.DestroyImmediate(publication); throw; }
                finally { EditorUtility.ClearProgressBar(); }
            }
        }
        public static List<string> PreviewRemap(GrimeCanvas c,RoadNetworkAsset network)
        {
            var changes=new List<string>(); if(!network) throw new InvalidOperationException("Choose the replacement network.");
            foreach(var s in c.strokes.Where(s=>s.enabled)) foreach(var a in s.samples.Where(a=>a.kind==GrimeAnchorKind.Road))
            {
                var lane=network.Lanes.FirstOrDefault(l=>l.Id.ToString()==a.laneId);
                if(lane==null||a.station>lane.Length) throw new InvalidOperationException("Exact lane/station missing. Re-author this range; no automatic nearest remap.");
                var sample=lane.Sample(a.station); changes.Add($"{s.id.Substring(0,8)} @ {a.station:F1}m: {(sample.position+sample.left*a.lateral-a.position).magnitude:F2}m displacement");
            }
            return changes;
        }
        public static void Remap(GrimeCanvas c,RoadNetworkAsset network)
        {
            PreviewRemap(c,network);
            Edit(c,"Accept road dressing remap",()=> { foreach(var s in c.strokes.Where(s=>s.enabled && !c.layers.First(l=>l.id==s.layerId).locked)) foreach(var a in s.samples.Where(a=>a.kind==GrimeAnchorKind.Road)) { a.network=network; a.roadRevision=network.Fingerprint; var sample=network.Lanes.First(l=>l.Id.ToString()==a.laneId).Sample(a.station); a.position=sample.position+sample.left*a.lateral; a.normal=sample.up; a.tangent=sample.forward; } });
        }
    }
}
