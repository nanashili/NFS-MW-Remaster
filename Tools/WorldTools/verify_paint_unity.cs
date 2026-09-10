using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using System;
using System.IO;
internal class CommandScript : IRunCommand {
 public void Execute(ExecutionResult result){
  GameObject parent=null,source=null;foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects()){if(root.name=="Rockport Generated Terrain - 1m Heightmap")parent=root;if(root.name=="Rockport Original Ground - Exact Source Meshes")source=root;}
  if(!parent||!parent.activeSelf||!source||source.activeSelf)throw new IOException("Incorrect active terrain roots.");
  int tileCount=0;long samples=0;float maxError=0;var counts=new long[8];
  foreach(var terrain in parent.GetComponentsInChildren<Terrain>(true)){
   var data=terrain.terrainData;if(data.alphamapResolution!=512||data.terrainLayers.Length!=8||terrain.materialTemplate.shader.name!="HDRP/TerrainLit")throw new IOException("Invalid paint configuration "+terrain.name);
   foreach(var layer in data.terrainLayers){if(!layer.diffuseTexture)throw new IOException("Missing texture");float repeats=1024/layer.tileSize.x;if(Mathf.Abs(repeats-Mathf.Round(repeats))>.0001f)throw new IOException("Discontinuous texture repeat");}
   var labels=File.ReadAllBytes("Assets/NfsMw/Content/World/Maps/Rockport/PaintMaps/"+terrain.name+".paint");var weights=data.GetAlphamaps(0,0,512,512);
   for(int z=0;z<512;z++)for(int x=0;x<512;x++){
    int label=labels[z*512+x];if(label==255)label=0;counts[label]++;float sum=0;
    for(int i=0;i<8;i++){float expected=i==label?1:0;maxError=Mathf.Max(maxError,Mathf.Abs(weights[z,x,i]-expected));sum+=weights[z,x,i];}
    if(Mathf.Abs(sum-1)>.0001f)throw new IOException("Unnormalized weights");samples++;
   }tileCount++;
  }
  if(tileCount!=39||maxError>.0001f)throw new IOException("Paint verification failed");
  string json="{\"status\":\"PASS\",\"tiles\":"+tileCount+",\"alphamapSamples\":"+samples+",\"maxWeightError\":"+maxError+",\"layers\":8,\"nativeTerrainActive\":true,\"sourceGroundActive\":false}";
  File.WriteAllText("Art/RockportGround/Source/paint-unity-verification.json",json);result.Log(json);
 }
}
