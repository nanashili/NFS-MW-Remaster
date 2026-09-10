using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System;
using System.IO;
using System.Linq;

internal class CommandScript : IRunCommand
{
    [Serializable] internal class MapReport { public string scene,asset; public int meshObjects,renderers,materials,missingMeshes,missingMaterials,terrains,nonBuildingObjects; public long triangles; public string[] shaders; public float[] boundsMin,boundsMax,rootPosition,rootScale; }
    public void Execute(ExecutionResult result)
    {
        const string assetPath="Assets/NfsMw/Content/World/Models/RockportBuildings/RockportBuildings.gltf";
        const string scenePath="Assets/NfsMw/Scenes/World/RockportBuildings.unity";
        AssetDatabase.ImportAsset(assetPath,ImportAssetOptions.ForceUpdate);
        var scene=SceneManager.GetSceneByPath(scenePath);
        if(!scene.isLoaded) scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Additive);
        var root=scene.GetRootGameObjects().Single(r=>r.name=="Rockport Buildings - Original Game Layout");
        var renderers=root.GetComponentsInChildren<MeshRenderer>(true);
        var filters=root.GetComponentsInChildren<MeshFilter>(true);
        var bounds=renderers[0].bounds;
        foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        int missingMeshes=filters.Count(f=>!f.sharedMesh);
        int missingMaterials=renderers.Sum(r=>r.sharedMaterials.Count(m=>!m||!m.shader||m.shader.name.Contains("InternalError")));
        long triangles=filters.Sum(f=>(long)f.sharedMesh.triangles.Length/3);
        var materials=renderers.SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
        var report=new MapReport { scene=scenePath, asset=assetPath, meshObjects=filters.Length, renderers=renderers.Length, triangles=triangles, materials=materials.Length, missingMeshes=missingMeshes, missingMaterials=missingMaterials, shaders=materials.Select(m=>m.shader.name).Distinct().ToArray(), boundsMin=new[]{bounds.min.x,bounds.min.y,bounds.min.z}, boundsMax=new[]{bounds.max.x,bounds.max.y,bounds.max.z}, rootPosition=new[]{root.transform.position.x,root.transform.position.y,root.transform.position.z}, rootScale=new[]{root.transform.localScale.x,root.transform.localScale.y,root.transform.localScale.z}, terrains=root.GetComponentsInChildren<Terrain>(true).Length, nonBuildingObjects=root.GetComponentsInChildren<Transform>(true).Count(t=>t.name.StartsWith("XT_")||t.name.StartsWith("PAN_")||t.name.StartsWith("SHD")) };
        if(missingMeshes!=0||missingMaterials!=0) throw new InvalidOperationException("Missing mesh/material in imported map.");
        EditorSceneManager.SaveScene(scene,scenePath);
        File.WriteAllText("Art/RockportBuildings/Source/unity-validation.json",JsonUtility.ToJson(report,true));
        Selection.activeGameObject=root;
        if(SceneView.lastActiveSceneView){var view=SceneView.lastActiveSceneView;view.sceneLighting=false;view.LookAt(bounds.center,Quaternion.Euler(65,-30,0),3500,true);view.Repaint();}
        result.Log(JsonUtility.ToJson(report));
    }
}
