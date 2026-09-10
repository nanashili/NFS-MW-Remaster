using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using System;
using System.IO;
internal class CommandScript : IRunCommand {
 public void Execute(ExecutionResult result){
  Terrain source=null;foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Terrain>(true))if(t.name=="height_x02_z01")source=t;if(!source)throw new IOException("No test terrain");
  var data=UnityEngine.Object.Instantiate(source.terrainData);GameObject obj=null;try{
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/NfsMw/Content/World/Maps/Rockport/Trees/Prefabs/Tree_0169_XT_EVRGRN_TREELINEL_1B_00.prefab");data.treePrototypes=new[]{new TreePrototype{prefab=prefab}};
   var world=new Vector3(-2283.142333984375f,262.359619140625f,-4482.36962890625f);var local=world-source.transform.position;var instance=new TreeInstance{prototypeIndex=0,position=new Vector3(local.x/data.size.x,local.y/data.size.y,local.z/data.size.z),rotation=0,widthScale=1,heightScale=1,color=Color.white,lightmapColor=Color.white};
   data.SetTreeInstances(new[]{instance},false);int setCount=data.treeInstanceCount;
   data.treeInstances=new[]{instance};int propertyCount=data.treeInstanceCount;
   data.SetTreeInstances(new TreeInstance[0],false);obj=Terrain.CreateTerrainGameObject(data);obj.transform.position=source.transform.position;obj.SetActive(false);var terrain=obj.GetComponent<Terrain>();terrain.AddTreeInstance(instance);int addCount=data.treeInstanceCount;terrain.Flush();int flushCount=data.treeInstanceCount;string after=flushCount>0?data.treeInstances[0].position.ToString("F8"):"absent";
   string report="SetTreeInstances="+setCount+" property="+propertyCount+" AddTreeInstance="+addCount+" afterFlush="+flushCount+" position="+after+" expected="+instance.position.ToString("F8");File.WriteAllText("Art/RockportTrees/Source/tree-hole-api-probe.txt",report);result.Log(report);
  }finally{if(obj)UnityEngine.Object.DestroyImmediate(obj);UnityEngine.Object.DestroyImmediate(data);}
 }
}
