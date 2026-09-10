using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System;
using System.IO;
using System.Collections.Generic;
internal class CommandScript : IRunCommand {
    [Serializable] internal class Tile {public string name;public int xIndex,zIndex,originX,originZ,validSamples;}
    [Serializable] internal class Map {public int tileSize,tileResolution,minHeight,heightRange;public Tile[] tiles;}
    public void Execute(ExecutionResult result) {
        const string maps="Assets/NfsMw/Content/World/Maps/Rockport/";var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/NfsMw/Scenes/World/RockportMap.unity")throw new InvalidOperationException("Open the combined Rockport map first.");
        var map=JsonUtility.FromJson<Map>(File.ReadAllText(maps+"Heightmaps/metadata.json"));
        var tileRows=File.ReadAllLines(maps+"Heightmaps/tiles.tsv");var tileList=new List<Tile>();
        foreach(var row in tileRows){var columns=row.Split('\t');tileList.Add(new Tile{name=columns[0],xIndex=int.Parse(columns[1]),zIndex=int.Parse(columns[2]),originX=int.Parse(columns[3]),originZ=int.Parse(columns[4])});}map.tiles=tileList.ToArray();
        Directory.CreateDirectory(maps+"TerrainData");AssetDatabase.Refresh();
        var parent=GameObject.Find("Rockport Generated Terrain - 1m Heightmap");
        if(!parent){foreach(var root in scene.GetRootGameObjects())if(root.name=="Rockport Generated Terrain - 1m Heightmap")parent=root;}
        if(!parent){parent=new GameObject("Rockport Generated Terrain - 1m Heightmap");result.RegisterObjectCreation(parent);parent.SetActive(false);}
        var material=AssetDatabase.LoadAssetAtPath<Material>(maps+"HeightmapTerrain.mat");
        if(!material){var shader=Shader.Find("HDRP/TerrainLit");if(!shader)throw new InvalidOperationException("HDRP TerrainLit unavailable.");material=new Material(shader);AssetDatabase.CreateAsset(material,maps+"HeightmapTerrain.mat");}
        int created=0;var terrains=new Dictionary<string,Terrain>();
        foreach(var terrain in parent.GetComponentsInChildren<Terrain>(true))terrains.Add(terrain.name,terrain);
        foreach(var tile in map.tiles) {
            if(terrains.ContainsKey(tile.name))continue;
            string asset=maps+"TerrainData/"+tile.name+".asset";
            if(File.Exists(asset))throw new InvalidOperationException("Unattached existing terrain asset: "+asset);
            var bytes=File.ReadAllBytes(maps+"Heightmaps/"+tile.name+".raw");var mask=File.ReadAllBytes(maps+"Heightmaps/"+tile.name+".terraincoverage");int n=map.tileResolution;
            if(bytes.Length!=n*n*2||mask.Length!=n*n)throw new IOException(tile.name);
            var heights=new float[n,n];var holes=new bool[n-1,n-1];
            for(int z=0;z<n;z++)for(int x=0;x<n;x++){int k=z*n+x;heights[z,x]=(bytes[k*2]|bytes[k*2+1]<<8)/65535f;}
            for(int z=0;z<n-1;z++)for(int x=0;x<n-1;x++){int k=z*n+x;holes[z,x]=mask[k]!=0&&mask[k+1]!=0&&mask[k+n]!=0&&mask[k+n+1]!=0;}
            var data=new TerrainData();data.name=tile.name;data.heightmapResolution=n;data.size=new Vector3(map.tileSize,map.heightRange,map.tileSize);data.enableHolesTextureCompression=false;data.SetHeights(0,0,heights);data.SetHoles(0,0,holes);AssetDatabase.CreateAsset(data,asset);
            var obj=Terrain.CreateTerrainGameObject(data);obj.name=tile.name;obj.transform.SetParent(parent.transform,false);obj.transform.position=new Vector3(tile.originX,map.minHeight,tile.originZ);result.RegisterObjectCreation(obj);
            var terrain=obj.GetComponent<Terrain>();terrain.materialTemplate=material;terrain.heightmapPixelError=1;terrain.allowAutoConnect=true;terrain.groupingID=19805;terrains.Add(tile.name,terrain);created++;
            // Keep each relay command bounded and save a complete partial batch.
            if(created==6)break;
        }
        foreach(var tile in map.tiles)if(terrains.TryGetValue(tile.name,out var terrain)) {
            Terrain left=null,top=null,right=null,bottom=null;
            foreach(var other in map.tiles)if(terrains.TryGetValue(other.name,out var neighbor)){
                if(other.zIndex==tile.zIndex&&other.xIndex==tile.xIndex-1)left=neighbor;if(other.zIndex==tile.zIndex&&other.xIndex==tile.xIndex+1)right=neighbor;
                if(other.xIndex==tile.xIndex&&other.zIndex==tile.zIndex+1)top=neighbor;if(other.xIndex==tile.xIndex&&other.zIndex==tile.zIndex-1)bottom=neighbor;
            }terrain.SetNeighbors(left,top,right,bottom);
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        result.Log("Generated "+created+" tiles; total "+terrains.Count+" / "+map.tiles.Length+". Terrain group is disabled; original textured ground remains visible.");
    }
}
