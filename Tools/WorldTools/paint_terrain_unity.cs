using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System;
using System.IO;
using System.Globalization;
internal class CommandScript : IRunCommand {
 public void Execute(ExecutionResult result){
  const string maps="Assets/NfsMw/Content/World/Maps/Rockport/";const string progress="Art/RockportGround/Source/paint-next.txt";
  var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/NfsMw/Scenes/World/RockportMap.unity")throw new InvalidOperationException("Open RockportMap.");
  GameObject parent=null,source=null;foreach(var root in scene.GetRootGameObjects()){if(root.name=="Rockport Generated Terrain - 1m Heightmap")parent=root;if(root.name=="Rockport Original Ground - Exact Source Meshes")source=root;}
  if(!parent||!source)throw new InvalidOperationException("Missing ground roots.");
  Directory.CreateDirectory(maps+"TerrainLayers");AssetDatabase.Refresh();var rows=File.ReadAllLines(maps+"PaintMaps/layers.tsv");var layers=new TerrainLayer[rows.Length];
  for(int i=0;i<rows.Length;i++){
   var cols=rows[i].Split('\t');var path=maps+"TerrainLayers/"+cols[0].Replace(" ","")+".terrainlayer";
   var layer=AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);if(!layer){layer=new TerrainLayer();layer.name=cols[0];AssetDatabase.CreateAsset(layer,path);}
   layer.diffuseTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(cols[1]);if(!layer.diffuseTexture)throw new IOException("Missing "+cols[1]);
   float size=float.Parse(cols[2],CultureInfo.InvariantCulture);layer.tileSize=new Vector2(size,size);layer.tileOffset=Vector2.zero;layer.metallic=0;layer.smoothness=0;layer.diffuseRemapMin=new Vector4(0,0,0,1);layer.diffuseRemapMax=Vector4.one;EditorUtility.SetDirty(layer);layers[i]=layer;
  }
  var tiles=File.ReadAllLines(maps+"Heightmaps/tiles.tsv");int start=File.Exists(progress)?int.Parse(File.ReadAllText(progress)):0,end=Math.Min(tiles.Length,start+6);
  var terrains=parent.GetComponentsInChildren<Terrain>(true);
  for(int t=start;t<end;t++){
   string name=tiles[t].Split('\t')[0];Terrain terrain=null;foreach(var candidate in terrains)if(candidate.name==name)terrain=candidate;if(!terrain)throw new IOException(name);
   var data=terrain.terrainData;var priorHeights=data.GetHeights(0,0,data.heightmapResolution,data.heightmapResolution);var priorHoles=data.GetHoles(0,0,data.holesResolution,data.holesResolution);
   var labels=File.ReadAllBytes(maps+"PaintMaps/"+name+".paint");if(labels.Length!=512*512)throw new IOException("Invalid paint map "+name);
   data.terrainLayers=layers;data.alphamapResolution=512;var weights=new float[512,512,layers.Length];for(int z=0;z<512;z++)for(int x=0;x<512;x++){int label=labels[z*512+x];if(label==255)label=0;if(label>=layers.Length)throw new IOException("Invalid material ID");weights[z,x,label]=1;}
   data.SetAlphamaps(0,0,weights);terrain.basemapDistance=20000;data.baseMapResolution=1024;data.SetBaseMapDirty();terrain.Flush();EditorUtility.SetDirty(data);EditorUtility.SetDirty(terrain);
   var afterHeights=data.GetHeights(0,0,data.heightmapResolution,data.heightmapResolution);var afterHoles=data.GetHoles(0,0,data.holesResolution,data.holesResolution);
   for(int z=0;z<data.heightmapResolution;z++)for(int x=0;x<data.heightmapResolution;x++)if(priorHeights[z,x]!=afterHeights[z,x])throw new IOException("Height changed "+name);
   for(int z=0;z<data.holesResolution;z++)for(int x=0;x<data.holesResolution;x++)if(priorHoles[z,x]!=afterHoles[z,x])throw new IOException("Holes changed "+name);
  }
  if(end==tiles.Length){source.SetActive(false);parent.SetActive(true);}
  AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);File.WriteAllText(progress,end.ToString());
  result.Log("Painted tiles "+start+" through "+(end-1)+"; total "+end+" / "+tiles.Length+". Heights and holes unchanged.");
 }
}
