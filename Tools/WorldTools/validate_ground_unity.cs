using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
internal class CommandScript : IRunCommand {
    [Serializable] internal class LayerReport {public string name;public int meshes,materials,missingMeshes,missingMaterials;public long triangles;public float[] min,max;}
    [Serializable] internal class TerrainReport {public int tiles,resolution,missingNeighbors;public long heightSamplesChecked,naturalCells,holeCells;public float maxUnityHeightErrorMetres;public bool terrainGroupActive;public string holesPolicy;}
    public void Execute(ExecutionResult result) {
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/NfsMw/Scenes/World/RockportMap.unity")throw new InvalidOperationException("Unexpected scene.");
        const string maps="Assets/NfsMw/Content/World/Maps/Rockport/";
        var roots=scene.GetRootGameObjects();var reports=new List<string>();
        foreach(var root in roots){var meshes=root.GetComponentsInChildren<MeshFilter>(true);if(meshes.Length==0)continue;var renderers=root.GetComponentsInChildren<MeshRenderer>(true);var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
            var report=new LayerReport{name=root.name,meshes=meshes.Length,materials=renderers.SelectMany(r=>r.sharedMaterials).Distinct().Count(),missingMeshes=meshes.Count(f=>!f.sharedMesh),missingMaterials=renderers.Sum(r=>r.sharedMaterials.Count(m=>!m||!m.shader||m.shader.name.Contains("InternalError"))),triangles=meshes.Sum(f=>(long)f.sharedMesh.triangles.Length/3),min=new[]{bounds.min.x,bounds.min.y,bounds.min.z},max=new[]{bounds.max.x,bounds.max.y,bounds.max.z}};
            if(report.missingMeshes!=0||report.missingMaterials!=0)throw new InvalidOperationException("Missing geometry or material.");reports.Add(JsonUtility.ToJson(report));
        }
        File.WriteAllText("Art/RockportGround/Source/unity-ground-validation.json","{\"scene\":\""+scene.path+"\",\"layers\":["+string.Join(",",reports)+"]}");
        var parent=roots.Single(r=>r.name=="Rockport Generated Terrain - 1m Heightmap");var terrains=parent.GetComponentsInChildren<Terrain>(true);var byName=terrains.ToDictionary(t=>t.name);var rows=File.ReadAllLines(maps+"Heightmaps/tiles.tsv");
        var output=new TerrainReport{tiles=terrains.Length,resolution=1025,terrainGroupActive=parent.activeSelf,holesPolicy="Only four covered natural-ground corners produce a Terrain cell. Original road meshes fill road-only areas."};
        if(terrains.Length!=rows.Length)throw new InvalidOperationException("Incomplete terrain tiles.");
        foreach(var row in rows){var fields=row.Split('\t');var terrain=byName[fields[0]];var data=terrain.terrainData;int n=data.heightmapResolution;
            if(n!=1025||data.size!=new Vector3(1024,489,1024)||terrain.transform.position!=new Vector3(int.Parse(fields[3]),-31,int.Parse(fields[4])))throw new InvalidOperationException("Terrain transform or dimensions differ from heightmap metadata.");
            var raw=File.ReadAllBytes(maps+"Heightmaps/"+terrain.name+".raw");var mask=File.ReadAllBytes(maps+"Heightmaps/"+terrain.name+".terraincoverage");var heights=data.GetHeights(0,0,n,n);var holes=new bool[n-1,n-1];
            for(int z=0;z<n;z++)for(int x=0;x<n;x++){int k=z*n+x;float expected=(raw[k*2]|raw[k*2+1]<<8)/65535f;output.maxUnityHeightErrorMetres=Mathf.Max(output.maxUnityHeightErrorMetres,Mathf.Abs(heights[z,x]-expected)*data.size.y);output.heightSamplesChecked++;}
            for(int z=0;z<n-1;z++)for(int x=0;x<n-1;x++){int k=z*n+x;holes[z,x]=mask[k]!=0&&mask[k+1]!=0&&mask[k+n]!=0&&mask[k+n+1]!=0;if(holes[z,x])output.naturalCells++;else output.holeCells++;}
            data.SetHoles(0,0,holes);var actual=data.GetHoles(0,0,n-1,n-1);
            for(int z=0;z<n-1;z++)for(int x=0;x<n-1;x++)if(holes[z,x]!=actual[z,x])throw new InvalidOperationException("Terrain hole mask mismatch.");
            EditorUtility.SetDirty(data);
            int tx=int.Parse(fields[1]),tz=int.Parse(fields[2]);
            foreach(var other in rows){var f=other.Split('\t');int xx=int.Parse(f[1]),zz=int.Parse(f[2]);if(xx==tx-1&&zz==tz&&terrain.leftNeighbor!=byName[f[0]])output.missingNeighbors++;if(xx==tx+1&&zz==tz&&terrain.rightNeighbor!=byName[f[0]])output.missingNeighbors++;if(xx==tx&&zz==tz+1&&terrain.topNeighbor!=byName[f[0]])output.missingNeighbors++;if(xx==tx&&zz==tz-1&&terrain.bottomNeighbor!=byName[f[0]])output.missingNeighbors++;}
        }
        if(output.maxUnityHeightErrorMetres>.02f||output.missingNeighbors!=0)throw new InvalidOperationException("Terrain height precision or adjacency failed.");
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);File.WriteAllText("Art/RockportGround/Source/unity-terrain-validation.json",JsonUtility.ToJson(output,true));result.Log(JsonUtility.ToJson(output));
    }
}
