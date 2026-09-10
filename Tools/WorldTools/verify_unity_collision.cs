using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System;
using System.IO;
internal class CommandScript : IRunCommand {
    [Serializable] internal class Report {public int meshColliders,roadRaycasts,groundRaycasts,raycastMisses;public float maxHitDistanceErrorMetres;public string[] misses;}
    public void Execute(ExecutionResult result) {
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/NfsMw/Scenes/World/RockportMap.unity")throw new InvalidOperationException("Unexpected scene.");Physics.SyncTransforms();var report=new Report();var misses=new System.Collections.Generic.List<string>();
        foreach(var root in scene.GetRootGameObjects()){
            bool road=root.name=="Rockport Roads - Paved and Unpaved Routes",ground=root.name=="Rockport Original Ground - Exact Source Meshes";if(!road&&!ground)continue;int tested=0;
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true)){
                var collider=filter.GetComponent<MeshCollider>();if(!collider||collider.sharedMesh!=filter.sharedMesh||collider.convex||!collider.enabled)throw new InvalidOperationException("Missing source MeshCollider: "+filter.name);report.meshColliders++;
                if(tested>=250)continue;var vertices=filter.sharedMesh.vertices;var indices=filter.sharedMesh.triangles;
                for(int i=0;i<indices.Length;i+=3){var a=filter.transform.TransformPoint(vertices[indices[i]]);var b=filter.transform.TransformPoint(vertices[indices[i+1]]);var c=filter.transform.TransformPoint(vertices[indices[i+2]]);var normal=Vector3.Cross(b-a,c-a);if(normal.sqrMagnitude<.01f||normal.normalized.y<.5f)continue;
                    var p=(a+b+c)/3;tested++;if(road)report.roadRaycasts++;else report.groundRaycasts++;
                    if(!collider.Raycast(new Ray(p+Vector3.up*.25f,Vector3.down),out var hit,.5f)){report.raycastMisses++;misses.Add(filter.name);}else report.maxHitDistanceErrorMetres=Mathf.Max(report.maxHitDistanceErrorMetres,Mathf.Abs(hit.distance-.25f));break;
                }
            }
        }
        report.misses=misses.ToArray();File.WriteAllText("Art/RockportGround/Source/unity-collision-validation.json",JsonUtility.ToJson(report,true));
        if(report.meshColliders!=15098||report.roadRaycasts!=250||report.groundRaycasts!=250||report.raycastMisses!=0)throw new InvalidOperationException("Ground collision validation failed: "+JsonUtility.ToJson(report));
        EditorSceneManager.SaveScene(scene);result.Log(JsonUtility.ToJson(report));
    }
}
