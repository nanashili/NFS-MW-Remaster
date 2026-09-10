using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using NfsMwRemaster.Driving;
internal class CommandScript : IRunCommand {
 public void Execute(ExecutionResult result){
  const string folder="Assets/NfsMw/Content/World/Maps/Rockport/Trees/";var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/NfsMw/Scenes/World/RockportMap.unity")throw new IOException("Wrong scene");GameObject root=null;foreach(var candidate in scene.GetRootGameObjects())if(candidate.name=="Rockport Generated Terrain - 1m Heightmap")root=candidate;if(!root||!root.activeSelf)throw new IOException("Inactive native terrain");
  GameObject openingRoot=null;foreach(var candidate in scene.GetRootGameObjects())if(candidate.name=="Rockport Trees - Terrain Openings")openingRoot=candidate;
  var supports=new Dictionary<string,Terrain>();if(openingRoot){if(!openingRoot.activeSelf)throw new IOException("Hidden opening trees");foreach(var support in openingRoot.GetComponentsInChildren<Terrain>(true)){if(support.drawHeightmap||support.GetComponent<TerrainCollider>()||!support.drawTreesAndFoliage||!support.gameObject.activeInHierarchy)throw new IOException("Invalid opening terrain");supports.Add(support.name,support);}}
  var names=File.ReadAllLines(folder+"prototypes.tsv");var lines=File.ReadAllLines(folder+"placements.tsv");var used=new HashSet<string>();int count=0,tileCount=0,openingTreeCount=0,supportCount=0;float maxError=0;
  foreach(var terrain in root.GetComponentsInChildren<Terrain>(true)){
   if(!terrain.drawTreesAndFoliage)throw new IOException("Hidden terrain trees");var data=terrain.terrainData;var prototypes=data.treePrototypes;var instances=data.treeInstances;var expected=new List<TreeInstance>();var positions=new List<Vector3>();var byName=new Dictionary<string,List<int>>();
   foreach(var line in lines){var cols=line.Split('\t');if(cols[0]!=terrain.name)continue;int index=int.Parse(cols[1]);string name=names[index];if(name.Contains("XO_CT_CAMPUSSMACKTREE"))continue;if(!byName.TryGetValue(name,out var list)){list=new List<int>();byName.Add(name,list);}list.Add(expected.Count);
    positions.Add(new Vector3(float.Parse(cols[2],CultureInfo.InvariantCulture),float.Parse(cols[3],CultureInfo.InvariantCulture),float.Parse(cols[4],CultureInfo.InvariantCulture)));
    expected.Add(new TreeInstance{rotation=float.Parse(cols[5],CultureInfo.InvariantCulture),widthScale=float.Parse(cols[6],CultureInfo.InvariantCulture),heightScale=float.Parse(cols[7],CultureInfo.InvariantCulture)});
   }
   var matched=new bool[expected.Count];int matchedCount=0;var sources=new List<Terrain>{terrain};if(supports.TryGetValue("TreesOverOpenings_"+terrain.name,out var opening)){sources.Add(opening);supportCount++;openingTreeCount+=opening.terrainData.treeInstanceCount;}
   foreach(var treeTerrain in sources)foreach(var instance in treeTerrain.terrainData.treeInstances){var prefab=treeTerrain.terrainData.treePrototypes[instance.prototypeIndex].prefab;string path=AssetDatabase.GetAssetPath(prefab);if(!path.StartsWith(folder+"Replacement/Prefabs/"))throw new IOException("Source tree prefab is still active: "+path);
    var actual=treeTerrain.transform.position+Vector3.Scale(instance.position,treeTerrain.terrainData.size);int match=-1;float distance=0;
    if(byName.TryGetValue(prefab.name,out var candidates))foreach(int candidate in candidates){if(matched[candidate])continue;var wanted=expected[candidate];float error=Vector3.Distance(actual,positions[candidate]);
     if(error<=.001f&&Mathf.Abs(instance.rotation-wanted.rotation)<=.000001f&&Mathf.Abs(instance.widthScale-wanted.widthScale)<=.000001f&&Mathf.Abs(instance.heightScale-wanted.heightScale)<=.000001f){match=candidate;distance=error;break;}
    }
    if(match<0)throw new IOException("Unmatched native tree "+terrain.name+" "+prefab.name+" at "+actual.ToString("F6"));matched[match]=true;matchedCount++;maxError=Mathf.Max(maxError,distance);used.Add(prefab.name);
    if(!prefab.GetComponent<LODGroup>()||prefab.GetComponent<LODGroup>().lodCount!=3)throw new IOException("Missing replacement LOD levels");foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>(true))if(!filter.sharedMesh||!AssetDatabase.GetAssetPath(filter.sharedMesh).StartsWith(folder+"Replacement/Meshes/"))throw new IOException("Source or missing tree mesh");
    foreach(var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))foreach(var mat in renderer.sharedMaterials)if(!mat||!mat.shader||mat.shader.name.Contains("InternalError")||!mat.enableInstancing||!AssetDatabase.GetAssetPath(mat).StartsWith(folder+"Replacement/Materials/"))throw new IOException("Invalid tree material");count++;
   }
   if(matchedCount!=expected.Count){
    var holes=data.GetHoles(0,0,data.holesResolution,data.holesResolution);var details=new List<string>();
    foreach(var entry in byName)foreach(int index in entry.Value)if(!matched[index]&&details.Count<20){var pos=positions[index];var local=pos-terrain.transform.position;int x=Mathf.Clamp((int)(local.x/data.size.x*data.holesResolution),0,data.holesResolution-1),z=Mathf.Clamp((int)(local.z/data.size.z*data.holesResolution),0,data.holesResolution-1);details.Add(entry.Key+" at "+pos.ToString("F6")+" ground="+holes[z,x]);}
    File.WriteAllText("Art/RockportTrees/Source/missing-tree-diagnostic.txt",terrain.name+" actual="+instances.Length+" matched="+matchedCount+" expected="+expected.Count+" prototypes="+prototypes.Length+"\n"+string.Join("\n",details));throw new IOException("Missing terrain trees on "+terrain.name);
   }tileCount++;
  }
  GameObject breakableRoot=null;foreach(var candidate in scene.GetRootGameObjects())if(candidate.name=="Rockport Breakable Trees")breakableRoot=candidate;
  if(!breakableRoot||!breakableRoot.activeSelf||breakableRoot.transform.childCount!=481)throw new IOException("Breakable tree root missing or incorrect");
  var physical=new Dictionary<string,DestructibleProp>();foreach(var prop in breakableRoot.GetComponentsInChildren<DestructibleProp>(true))physical.Add(prop.StableId,prop);
  int breakableCount=0;
  foreach(var line in File.ReadAllLines(folder+"breakable-placements.tsv")){
   var cols=line.Split('\t');string id="rockport.tree."+cols[0];if(!physical.TryGetValue(id,out var prop))throw new IOException("Missing source breakable "+id);
   var wanted=new Vector3(float.Parse(cols[3],CultureInfo.InvariantCulture),float.Parse(cols[4],CultureInfo.InvariantCulture),float.Parse(cols[5],CultureInfo.InvariantCulture));
   float width=float.Parse(cols[7],CultureInfo.InvariantCulture),height=float.Parse(cols[8],CultureInfo.InvariantCulture);float error=Vector3.Distance(prop.transform.position,wanted);maxError=Mathf.Max(maxError,error);
   if(error>.001f||Quaternion.Angle(prop.transform.rotation,Quaternion.Euler(0,float.Parse(cols[6],CultureInfo.InvariantCulture)*Mathf.Rad2Deg,0))>.05f||Vector3.Distance(prop.transform.localScale,new Vector3(width,height,width))>.000001f)throw new IOException("Moved source breakable "+id);
   var body=prop.GetComponent<Rigidbody>();var trunk=prop.GetComponent<CapsuleCollider>();var buoyancy=prop.GetComponent<OceanBuoyantBody>();
   if(!prop.enabled||!prop.gameObject.activeInHierarchy||prop.IsBroken||!body||!body.isKinematic||!trunk||!trunk.enabled||trunk.isTrigger||!buoyancy||!buoyancy.enabled||!new SerializedObject(buoyancy).FindProperty("ocean").objectReferenceValue)throw new IOException("Invalid physical tree "+id);
   if(prop.transform.childCount!=1||PrefabUtility.GetCorrespondingObjectFromSource(prop.transform.GetChild(0).gameObject).name!=cols[2])throw new IOException("Breakable source visual changed "+id);
   used.Add(cols[2]);physical.Remove(id);breakableCount++;
  }
  if(count!=8946||breakableCount!=481||count+breakableCount!=lines.Length||physical.Count!=0||used.Count!=names.Length||tileCount!=39||supportCount!=supports.Count||maxError>.001f)throw new IOException("Tree validation failed");
  string json="{\"status\":\"PASS\",\"nativeTreeInstances\":"+count+",\"physicalBreakableTrees\":"+breakableCount+",\"totalAuthoredPlacements\":"+(count+breakableCount)+",\"usedPrototypes\":"+used.Count+",\"terrainTiles\":"+tileCount+",\"invisibleOpeningTerrains\":"+supportCount+",\"treesOverOpenings\":"+openingTreeCount+",\"openingGroundAndCollisionDisabled\":true,\"maxPositionErrorMetres\":"+maxError.ToString(CultureInfo.InvariantCulture)+",\"sourceElevationPreserved\":true,\"matchingPolicy\":\"Unordered multiset of prototype and world transform; physical trees matched by source stable ID; each authored instance matched once\"}";File.WriteAllText("Art/RockportTrees/Source/unity-tree-verification.json",json);EditorSceneManager.SaveScene(scene);result.Log(json);
 }
}
