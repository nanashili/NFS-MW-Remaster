using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System;
using System.IO;
using System.Linq;
internal class CommandScript : IRunCommand {
    [Serializable] internal class LayerReport { public string name;public int meshes,materials,missingMeshes,missingMaterials;public long triangles;public float[] min,max; }
    [Serializable] internal class GroundReport { public string scene;public LayerReport[] layers; }
    public void Execute(ExecutionResult result) {
        const string path="Assets/NfsMw/Scenes/World/RockportMap.unity";
        var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/NfsMw/Scenes/World/RockportBuildings.unity"||scene.isDirty||File.Exists(path))throw new InvalidOperationException("Inspect map scene state before creating the combined map.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var roads=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/NfsMw/Content/World/Models/RockportRoads/RockportRoads.gltf");
        var ground=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/NfsMw/Content/World/Models/RockportTerrainSource/RockportTerrainSource.gltf");
        if(!roads||!ground)throw new InvalidOperationException("Ground glTF imports are unavailable.");
        if(!EditorSceneManager.SaveScene(scene,path,true))throw new IOException("Could not save the map scene copy.");
        scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
        var rr=(GameObject)PrefabUtility.InstantiatePrefab(roads,scene);rr.name="Rockport Roads - Paved and Unpaved Routes";result.RegisterObjectCreation(rr);
        var gr=(GameObject)PrefabUtility.InstantiatePrefab(ground,scene);gr.name="Rockport Original Ground - Exact Source Meshes";result.RegisterObjectCreation(gr);
        var reports=scene.GetRootGameObjects().Select(root=>{
            var renderers=root.GetComponentsInChildren<MeshRenderer>(true);var meshes=root.GetComponentsInChildren<MeshFilter>(true);var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
            return new LayerReport{name=root.name,meshes=meshes.Length,materials=renderers.SelectMany(r=>r.sharedMaterials).Distinct().Count(),missingMeshes=meshes.Count(f=>!f.sharedMesh),missingMaterials=renderers.Sum(r=>r.sharedMaterials.Count(m=>!m||!m.shader||m.shader.name.Contains("InternalError"))),triangles=meshes.Sum(f=>(long)f.sharedMesh.triangles.Length/3),min=new[]{bounds.min.x,bounds.min.y,bounds.min.z},max=new[]{bounds.max.x,bounds.max.y,bounds.max.z}};
        }).ToArray();
        if(reports.Any(r=>r.missingMeshes!=0||r.missingMaterials!=0))throw new InvalidOperationException("Missing imported ground data.");
        var json="{\"scene\":\""+path+"\",\"layers\":["+string.Join(",",reports.Select(r=>JsonUtility.ToJson(r)))+"]}";File.WriteAllText("Art/RockportGround/Source/unity-ground-validation.json",json);
        EditorSceneManager.SaveScene(scene);Selection.activeGameObject=rr;
        if(SceneView.lastActiveSceneView){SceneView.lastActiveSceneView.sceneLighting=false;SceneView.lastActiveSceneView.LookAt(new Vector3(-1500,100,-1700),Quaternion.Euler(65,-30,0),4000,true);SceneView.lastActiveSceneView.Repaint();}
        result.Log(json);
    }
}
