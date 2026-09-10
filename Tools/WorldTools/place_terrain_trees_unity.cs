using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
internal class CommandScript : IRunCommand {
 public void Execute(ExecutionResult result){
  const string folder="Assets/NfsMw/Content/World/Maps/Rockport/Trees/";const string progress="Art/RockportTrees/Source/placement-next.txt";var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/NfsMw/Scenes/World/RockportMap.unity")throw new IOException("Open RockportMap first");
  GameObject root=null;foreach(var candidate in scene.GetRootGameObjects())if(candidate.name=="Rockport Generated Terrain - 1m Heightmap")root=candidate;if(!root||!root.activeSelf)throw new IOException("Generated terrain must be active");
  var names=File.ReadAllLines(folder+"prototypes.tsv");var tileRows=File.ReadAllLines("Assets/NfsMw/Content/World/Maps/Rockport/Heightmaps/tiles.tsv");var placements=File.ReadAllLines(folder+"placements.tsv");var terrains=root.GetComponentsInChildren<Terrain>(true);int start=File.Exists(progress)?int.Parse(File.ReadAllText(progress)):0,end=Math.Min(start+6,tileRows.Length),added=0;
  for(int t=start;t<end;t++){
   var name=tileRows[t].Split('\t')[0];Terrain terrain=null;foreach(var candidate in terrains)if(candidate.name==name)terrain=candidate;if(!terrain)throw new IOException(name);
   var data=terrain.terrainData;var prototypes=new List<TreePrototype>();var instances=new List<TreeInstance>();var oldPrototypes=data.treePrototypes;var oldIndices=new Dictionary<int,int>();
   for(int i=0;i<oldPrototypes.Length;i++)if(!AssetDatabase.GetAssetPath(oldPrototypes[i].prefab).StartsWith(folder+"Prefabs/")&&!AssetDatabase.GetAssetPath(oldPrototypes[i].prefab).StartsWith(folder+"Replacement/Prefabs/")){oldIndices.Add(i,prototypes.Count);prototypes.Add(oldPrototypes[i]);}
   foreach(var old in data.treeInstances)if(oldIndices.TryGetValue(old.prototypeIndex,out int id)){var kept=old;kept.prototypeIndex=id;instances.Add(kept);}
   var localIndices=new Dictionary<int,int>();var generated=new List<TreeInstance>();
   foreach(var line in placements){var cols=line.Split('\t');if(cols[0]!=name)continue;int sourceIndex=int.Parse(cols[1]);if(names[sourceIndex].Contains("XO_CT_CAMPUSSMACKTREE"))continue;
    if(!localIndices.TryGetValue(sourceIndex,out int localIndex)){var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(folder+"Replacement/Prefabs/"+names[sourceIndex]+".prefab");if(!prefab||!prefab.GetComponent<LODGroup>())throw new IOException("Missing replacement tree prefab "+sourceIndex);localIndex=prototypes.Count;prototypes.Add(new TreePrototype{prefab=prefab,bendFactor=0});localIndices.Add(sourceIndex,localIndex);}
    var world=new Vector3(float.Parse(cols[2],CultureInfo.InvariantCulture),float.Parse(cols[3],CultureInfo.InvariantCulture),float.Parse(cols[4],CultureInfo.InvariantCulture));var local=world-terrain.transform.position;var size=data.size;var position=new Vector3(local.x/size.x,local.y/size.y,local.z/size.z);
    if(position.x<0||position.x>1||position.y<0||position.y>1||position.z<0||position.z>1)throw new IOException("Tree outside terrain "+cols[8]);
    var tree=new TreeInstance{position=position,prototypeIndex=localIndex,rotation=float.Parse(cols[5],CultureInfo.InvariantCulture),widthScale=float.Parse(cols[6],CultureInfo.InvariantCulture),heightScale=float.Parse(cols[7],CultureInfo.InvariantCulture),color=Color.white,lightmapColor=Color.white};instances.Add(tree);generated.Add(tree);added++;
   }
   data.treePrototypes=prototypes.ToArray();data.SetTreeInstances(instances.ToArray(),false);
   // Unity removes instances over holes. Keep their authored elevations on a
   // separate native Terrain whose ground rendering and collision are absent.
   var retained=data.treeInstances;var used=new bool[retained.Length];var missing=new List<TreeInstance>();
   foreach(var wanted in generated){int match=-1;for(int j=0;j<retained.Length;j++){var actual=retained[j];if(!used[j]&&actual.prototypeIndex==wanted.prototypeIndex&&Vector3.Distance(actual.position,wanted.position)<.0000001f&&Mathf.Abs(actual.rotation-wanted.rotation)<.000001f&&Mathf.Abs(actual.widthScale-wanted.widthScale)<.000001f&&Mathf.Abs(actual.heightScale-wanted.heightScale)<.000001f){match=j;break;}}if(match>=0)used[match]=true;else missing.Add(wanted);}
   GameObject supportRoot=null;foreach(var candidate in scene.GetRootGameObjects())if(candidate.name=="Rockport Trees - Terrain Openings")supportRoot=candidate;
   Terrain support=null;if(supportRoot)foreach(var candidate in supportRoot.GetComponentsInChildren<Terrain>(true))if(candidate.name=="TreesOverOpenings_"+name)support=candidate;
   if(missing.Count>0&&!support){
    if(!supportRoot){supportRoot=new GameObject("Rockport Trees - Terrain Openings");result.RegisterObjectCreation(supportRoot);}
    string directory=folder+"OpeningTerrainData/";Directory.CreateDirectory(directory);string asset=directory+name+".asset";var supportData=AssetDatabase.LoadAssetAtPath<TerrainData>(asset);
    if(!supportData){supportData=new TerrainData();supportData.name="TreesOverOpenings_"+name;supportData.heightmapResolution=data.heightmapResolution;supportData.size=data.size;supportData.SetHeights(0,0,data.GetHeights(0,0,data.heightmapResolution,data.heightmapResolution));AssetDatabase.CreateAsset(supportData,asset);}
    var obj=new GameObject("TreesOverOpenings_"+name);obj.transform.SetParent(supportRoot.transform,false);obj.transform.position=terrain.transform.position;support=obj.AddComponent<Terrain>();support.terrainData=supportData;support.drawHeightmap=false;support.drawTreesAndFoliage=true;support.allowAutoConnect=false;support.groupingID=19806;support.materialTemplate=terrain.materialTemplate;support.treeDistance=12000;support.treeBillboardDistance=12000;support.treeMaximumFullLODCount=20000;result.RegisterObjectCreation(obj);
   }
   if(support){support.terrainData.treePrototypes=prototypes.ToArray();support.terrainData.SetTreeInstances(missing.ToArray(),false);if(support.terrainData.treeInstanceCount!=missing.Count)throw new IOException("Opening support dropped trees");support.Flush();EditorUtility.SetDirty(support.terrainData);EditorUtility.SetDirty(support);}
   terrain.drawTreesAndFoliage=true;terrain.treeDistance=12000;terrain.treeBillboardDistance=12000;terrain.treeMaximumFullLODCount=20000;terrain.Flush();EditorUtility.SetDirty(data);EditorUtility.SetDirty(terrain);
  }
  AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);File.WriteAllText(progress,end.ToString());result.Log("Added "+added+" authored tree/group instances; tiles "+end+" / "+tileRows.Length+" complete.");
 }
}
