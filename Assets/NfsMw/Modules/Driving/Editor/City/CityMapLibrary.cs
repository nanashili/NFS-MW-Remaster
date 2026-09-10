using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    public sealed class CityMapSource
    {
        public int number, meshes, nodes, materials;
        public string path, guid;
        public long bufferBytes;
        public readonly List<string> missing=new List<string>();
        public GameObject Prefab => AssetDatabase.LoadAssetAtPath<GameObject>(path);
        public string Label => "Scene "+number.ToString("00")+" · "+meshes.ToString("N0")+" meshes";
    }
    public static class CityMapLibrary
    {
        [Serializable] private sealed class Gltf
        { public Buffer[] buffers; public Image[] images; public Named[] meshes,nodes,materials; }
        [Serializable] private sealed class Buffer { public string uri;public long byteLength; }
        [Serializable] private sealed class Image { public string uri; }
        [Serializable] private sealed class Named { public string name; }
        public static List<CityMapSource> Scan(string folder)
        {
            var output=new List<CityMapSource>(); if(!Directory.Exists(folder)) return output;
            foreach(var path in Directory.GetFiles(folder,"*.gltf",SearchOption.TopDirectoryOnly))
            {
                var match=Regex.Match(Path.GetFileNameWithoutExtension(path),@"^scene(?:\s+(\d+))?$",RegexOptions.IgnoreCase);
                if(!match.Success) continue;
                int number=match.Groups[1].Success?int.Parse(match.Groups[1].Value):0;
                var data=JsonUtility.FromJson<Gltf>(File.ReadAllText(path));
                var part=new CityMapSource { number=number,path=path.Replace('\\','/'),guid=AssetDatabase.AssetPathToGUID(path),meshes=data.meshes?.Length??0,nodes=data.nodes?.Length??0,materials=data.materials?.Length??0 };
                void Dependency(string uri,long minimumBytes=0)
                {
                    if(string.IsNullOrEmpty(uri)||uri.StartsWith("data:",StringComparison.Ordinal)) return;
                    var dependency=Path.GetFullPath(Path.Combine(folder,Uri.UnescapeDataString(uri)));
                    if(!dependency.StartsWith(Path.GetFullPath(folder)+Path.DirectorySeparatorChar,StringComparison.Ordinal)) { part.missing.Add("External path outside Map: "+uri); return; }
                    if(!File.Exists(dependency)) part.missing.Add(uri);
                    else if(new FileInfo(dependency).Length<minimumBytes) part.missing.Add("Truncated buffer: "+uri);
                }
                if(data.buffers!=null) foreach(var buffer in data.buffers) { part.bufferBytes+=buffer.byteLength;Dependency(buffer.uri,buffer.byteLength); }
                if(data.images!=null) foreach(var image in data.images) Dependency(image.uri);
                output.Add(part);
            }
            return output.OrderBy(p=>p.number).ToList();
        }
        public static GameObject Assemble(IReadOnlyList<CityMapSource> parts,Scene scene,GameObject parent=null,Func<float,bool> cancel=null)
        {
            if(parts==null||parts.Count==0) throw new ArgumentException("Select at least one map section.");
            if(!scene.IsValid()||!scene.isLoaded||EditorApplication.isPlayingOrWillChangePlaymode) throw new ArgumentException("Assemble into an editing scene.");
            foreach(var p in parts) if(p.missing.Count>0 || p.Prefab==null) throw new ArgumentException(p.Label+": repair dependencies and finish glTF import first.");
            if(parts.Select(p=>p.guid).Distinct().Count()!=parts.Count) throw new ArgumentException("Choose each part once.");
            Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Assemble Rockport city parts");
            try
            {
                if(parent==null) { parent=new GameObject("Rockport — authored city parts");SceneManager.MoveGameObjectToScene(parent,scene);Undo.RegisterCreatedObjectUndo(parent,"Create Rockport assembly"); }
                if(parent.scene!=scene) throw new ArgumentException("Assembly parent must belong to the destination scene.");
                var present=new HashSet<string>(parent.GetComponentsInChildren<CityMapPart>(true).Select(p=>p.sourceGuid));
                int index=0;
                foreach(var part in parts.OrderBy(p=>p.number))
                {
                    if(cancel!=null&&cancel((float)index++/Math.Max(1,parts.Count))) throw new OperationCanceledException();
                    if(present.Contains(part.guid)) continue;
                    var prefab=part.Prefab;
                    var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent.transform);
                    Undo.RegisterCreatedObjectUndo(instance,"Add Rockport scene part");
                    // Number controls hierarchy order only. Authored glTF node transforms determine spatial assembly.
                    instance.name="Scene "+part.number.ToString("00")+" — Rockport";
                    var marker=Undo.AddComponent<CityMapPart>(instance);
                    marker.sceneNumber=part.number; marker.sourceGuid=part.guid; marker.sourceRevision=AssetDatabase.GetAssetDependencyHash(part.path).ToString();
                    marker.importedPosition=instance.transform.localPosition;marker.importedRotation=instance.transform.localRotation;marker.importedScale=instance.transform.localScale;
                    var following=parent.GetComponentsInChildren<CityMapPart>(true).Where(p=>p.transform.parent==parent.transform&&p.sceneNumber>part.number).OrderBy(p=>p.sceneNumber).FirstOrDefault();
                    if(following!=null)instance.transform.SetSiblingIndex(following.transform.GetSiblingIndex());
                    PrefabUtility.RecordPrefabInstancePropertyModifications(instance); EditorUtility.SetDirty(marker);
                }
                EditorSceneManager.MarkSceneDirty(scene);Undo.CollapseUndoOperations(group);return parent;
            }
            catch { Undo.RevertAllDownToGroup(group);throw; }
        }
        public static void ResetPlacement(CityMapPart part)
        {
            Undo.RecordObject(part.transform,"Restore imported map placement");
            part.transform.localPosition=part.importedPosition;part.transform.localRotation=part.importedRotation;part.transform.localScale=part.importedScale;
            CityCommands.Changed(part.transform);
        }
        public static Bounds RenderBounds(GameObject root)
        {
            var renderers=root.GetComponentsInChildren<Renderer>(true); if(renderers.Length==0) throw new ArgumentException("No renderers found.");
            var bounds=renderers[0].bounds;foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);return bounds;
        }
        public static void AddSelectedCollision(GameObject[] selection,bool boundsOnly)
        {
            var filters=selection.SelectMany(go=>go.GetComponentsInChildren<MeshFilter>(true)).Distinct().Where(f=>f.sharedMesh!=null&&f.GetComponent<Collider>()==null).ToArray();
            if(filters.Length>2000) throw new ArgumentException("Select a smaller mesh group (maximum 2000 colliders per operation). Use the part hierarchy search.");
            if(!boundsOnly && filters.Any(f=>f.GetComponentInParent<Rigidbody>()!=null && !f.GetComponentInParent<Rigidbody>().isKinematic))
                throw new ArgumentException("Non-convex map collision requires static or kinematic geometry.");
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
            try
            {
                foreach(var filter in filters)
                {
                    if(boundsOnly) { var collider=Undo.AddComponent<BoxCollider>(filter.gameObject);collider.center=filter.sharedMesh.bounds.center;collider.size=filter.sharedMesh.bounds.size; }
                    else { var collider=Undo.AddComponent<MeshCollider>(filter.gameObject);collider.sharedMesh=filter.sharedMesh;collider.convex=false; }
                    CityCommands.Changed(filter);
                }
                Undo.CollapseUndoOperations(group);
            }
            catch { Undo.RevertAllDownToGroup(group);throw; }
        }
    }
}
