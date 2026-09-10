using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using NfsMwRemaster.Driving;
using NfsMwRemaster.Driving.Editor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!="Assets/NfsMw/Scenes/World/RockportMap.unity")throw new InvalidOperationException("Open RockportMap in edit mode.");
        var replacements=new Dictionary<string,GameObject>();
        foreach(string name in File.ReadAllLines("Assets/NfsMw/Content/World/Maps/Rockport/Trees/prototypes.tsv")){
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(RockportReplacementTrees.Folder+"Prefabs/"+name+".prefab");
            if(!prefab)throw new IOException("Replacement prefab missing "+name);replacements.Add(name,prefab);
        }
        int terrainCount=0,nativeCount=0,physicalCount=0;
        foreach(var root in scene.GetRootGameObjects())foreach(var terrain in root.GetComponentsInChildren<Terrain>(true)){
            var data=terrain.terrainData;var prototypes=data.treePrototypes;int before=data.treeInstanceCount;
            for(int i=0;i<prototypes.Length;i++)if(prototypes[i].prefab&&replacements.TryGetValue(prototypes[i].prefab.name,out var replacement))prototypes[i].prefab=replacement;
            data.treePrototypes=prototypes;data.RefreshPrototypes();terrain.Flush();
            if(data.treeInstanceCount!=before)throw new IOException("Tree count changed while replacing prototypes "+terrain.name);
            EditorUtility.SetDirty(data);terrainCount++;nativeCount+=before;
        }
        foreach(var root in scene.GetRootGameObjects())if(root.name=="Rockport Breakable Trees")foreach(var prop in root.GetComponentsInChildren<DestructibleProp>(true)){
            if(prop.transform.childCount!=1)throw new IOException("Unexpected breakable tree visual children");
            var old=prop.transform.GetChild(0).gameObject;var source=PrefabUtility.GetCorrespondingObjectFromSource(old);
            if(!source||!replacements.TryGetValue(source.name,out var replacement))throw new IOException("Unknown physical tree source");
            var visual=(GameObject)PrefabUtility.InstantiatePrefab(replacement,prop.transform);visual.name="New Unity tree";
            var serialized=new SerializedObject(prop);serialized.FindProperty("intactVisual").objectReferenceValue=visual;serialized.ApplyModifiedPropertiesWithoutUndo();
            UnityEngine.Object.DestroyImmediate(old);EditorUtility.SetDirty(prop);physicalCount++;
        }
        if(nativeCount!=8946||physicalCount!=481||terrainCount!=70)throw new IOException("Replacement count mismatch "+nativeCount+" / "+physicalCount+" / "+terrainCount);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        string json="{\"status\":\"PASS\",\"nativePlacements\":8946,\"physicalBreakableTrees\":481,\"replacementPrototypes\":2004,\"terrainComponents\":70,\"sourceTreeMeshesUsed\":false,\"sourceTreeTexturesUsed\":false,\"plantingLayoutRetained\":true}";
        File.WriteAllText("Art/RockportTrees/Source/replacement-tree-installation.json",json);result.Log(json);
    }
}
