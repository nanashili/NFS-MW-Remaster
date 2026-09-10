using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using NfsMwRemaster.Driving;
using NfsMwRemaster.Driving.Editor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!="Assets/NfsMw/Scenes/World/RockportMap.unity")throw new InvalidOperationException("Open RockportMap in edit mode.");
        foreach(string dependency in AssetDatabase.GetDependencies(scene.path,true)){
            if(dependency.StartsWith("Assets/NfsMw/Content/World/Models/RockportTrees/")||dependency.StartsWith("Assets/NfsMw/Content/World/Maps/Rockport/Trees/Prefabs/")||dependency.StartsWith("Assets/NfsMw/Content/World/Maps/Rockport/Trees/Materials/"))throw new IOException("Saved scene still depends on source tree asset: "+dependency);
        }
        int prefabs=0,renderedTrees=0;var meshes=new HashSet<UnityEngine.Mesh>();var materials=new HashSet<Material>();
        foreach(string name in File.ReadAllLines("Assets/NfsMw/Content/World/Maps/Rockport/Trees/prototypes.tsv")){
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(RockportReplacementTrees.Folder+"Prefabs/"+name+".prefab");if(!prefab)throw new IOException("Missing replacement prefab "+name);
            var lods=prefab.GetComponent<LODGroup>().GetLODs();if(lods.Length!=3||lods[0].renderers.Length==0)throw new IOException("Missing generated LODs");
            int previous=int.MaxValue;
            foreach(var lod in lods){int triangleCount=0;
                foreach(var renderer in lod.renderers){
                    var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
                    if(!mesh||!AssetDatabase.GetAssetPath(mesh).StartsWith(RockportReplacementTrees.Folder+"Meshes/"))throw new IOException("Renderer uses a source mesh");
                    triangleCount+=mesh.triangles.Length/3;meshes.Add(mesh);
                    foreach(var material in renderer.sharedMaterials){
                        if(!material||!material.shader||material.shader.name!="HDRP/Lit"||!material.enableInstancing||!AssetDatabase.GetAssetPath(material).StartsWith(RockportReplacementTrees.Folder+"Materials/"))throw new IOException("Renderer uses source or invalid material");materials.Add(material);
                    }
                }
                if(triangleCount>=previous)throw new IOException("LOD does not reduce geometry "+name);previous=triangleCount;
            }
            renderedTrees+=lods[0].renderers.Length;prefabs++;
        }
        foreach(var material in materials)foreach(string property in material.GetTexturePropertyNames()){
            var texture=material.GetTexture(property);if(!texture)continue;string path=AssetDatabase.GetAssetPath(texture);
            if(path.Length>0&&!path.StartsWith(RockportReplacementTrees.Folder+"Textures/"))throw new IOException("Replacement material uses a source texture: "+path);
        }
        int physical=0;
        foreach(var root in scene.GetRootGameObjects())if(root.name=="Rockport Breakable Trees")foreach(var prop in root.GetComponentsInChildren<DestructibleProp>(true)){
            if(!prop.GetComponent<Rigidbody>().isKinematic||prop.IsBroken)throw new IOException("Broken physical tree persisted");
            var visual=new SerializedObject(prop).FindProperty("intactVisual").objectReferenceValue as GameObject;
            if(!visual||visual.transform.parent!=prop.transform||!PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(visual).StartsWith(RockportReplacementTrees.Folder+"Prefabs/"))throw new IOException("Breakable tree visual not replaced");physical++;
        }
        if(prefabs!=2004||physical!=481||renderedTrees!=31439)throw new IOException("Replacement content count mismatch");
        string json="{\"status\":\"PASS\",\"replacementPrefabs\":"+prefabs+",\"independentSharedMeshes\":"+meshes.Count+",\"independentMaterials\":"+materials.Count+",\"treesAcrossPrototypeLibrary\":"+renderedTrees+",\"physicalTreeVisuals\":"+physical+",\"lodLevels\":3,\"eachLodReducesGeometry\":true,\"originalTreeSceneDependencies\":0}";
        File.WriteAllText("Art/RockportTrees/Source/replacement-tree-dependencies.json",json);result.Log(json);
    }
}
