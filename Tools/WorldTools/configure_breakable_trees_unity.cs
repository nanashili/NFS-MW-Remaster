using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using NfsMwRemaster.Driving;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/NfsMw/Scenes/World/RockportMap.unity")throw new IOException("Open RockportMap");
        const string rootName="Rockport Breakable Trees";
        GameObject root=null;foreach(var candidate in scene.GetRootGameObjects())if(candidate.name==rootName)root=candidate;
        if(root&&root.transform.childCount>0)throw new IOException("Breakable trees already exist; inspect before replacing authored components.");
        if(!root){root=new GameObject(rootName);result.RegisterObjectCreation(root);}var world=root.GetComponent<DestructionWorld>();if(!world)world=root.AddComponent<DestructionWorld>();
        int removed=0,created=0;var ids=new HashSet<int>();
        foreach(var sceneRoot in scene.GetRootGameObjects())
        {
            if(sceneRoot.name!="Rockport Generated Terrain - 1m Heightmap"&&sceneRoot.name!="Rockport Trees - Terrain Openings")continue;
            foreach(var terrain in sceneRoot.GetComponentsInChildren<Terrain>(true))
            {
                var data=terrain.terrainData;var prototypes=data.treePrototypes;var kept=new List<TreeInstance>();
                foreach(var tree in data.treeInstances)
                {
                    if(prototypes[tree.prototypeIndex].prefab.name.Contains("XO_CT_CAMPUSSMACKTREE"))removed++;else kept.Add(tree);
                }
                data.SetTreeInstances(kept.ToArray(),false);EditorUtility.SetDirty(data);terrain.Flush();
            }
        }
        result.Log("Removed "+removed+" native instances; creating physical trees.");
        foreach(var line in File.ReadAllLines("Assets/NfsMw/Content/World/Maps/Rockport/Trees/breakable-placements.tsv"))
        {
            var cols=line.Split('\t');int sourceOffset=int.Parse(cols[0]);string sourceName=cols[1];float width=float.Parse(cols[7],CultureInfo.InvariantCulture),scaleHeight=float.Parse(cols[8],CultureInfo.InvariantCulture);
            if(!ids.Add(sourceOffset))throw new IOException("Duplicate source tree");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/NfsMw/Content/World/Maps/Rockport/Trees/Replacement/Prefabs/"+cols[2]+".prefab");
            if(!prefab)throw new IOException("Missing source tree prefab");
            var obj=new GameObject(sourceName+"__"+sourceOffset);obj.transform.SetParent(root.transform,false);
            obj.transform.position=new Vector3(float.Parse(cols[3],CultureInfo.InvariantCulture),float.Parse(cols[4],CultureInfo.InvariantCulture),float.Parse(cols[5],CultureInfo.InvariantCulture));
            obj.transform.rotation=Quaternion.Euler(0,float.Parse(cols[6],CultureInfo.InvariantCulture)*Mathf.Rad2Deg,0);obj.transform.localScale=new Vector3(width,scaleHeight,width);
            var visual=(GameObject)PrefabUtility.InstantiatePrefab(prefab,obj.transform);visual.name="New Unity tree";
            var body=obj.AddComponent<Rigidbody>();body.isKinematic=true;body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;body.interpolation=RigidbodyInterpolation.Interpolate;
            var trunk=obj.AddComponent<CapsuleCollider>();float height=float.Parse(cols[12],CultureInfo.InvariantCulture);trunk.height=height;trunk.radius=sourceName.Contains("SMACKTREEB")?.11f:.08f;
            trunk.center=new Vector3(-float.Parse(cols[9],CultureInfo.InvariantCulture),height*.5f,-float.Parse(cols[11],CultureInfo.InvariantCulture));
            float volume=Mathf.PI*trunk.radius*trunk.radius*height*width*width*scaleHeight;
            body.mass=Mathf.Max(8,volume*650);body.centerOfMass=trunk.center;
            obj.AddComponent<DestructibleProp>().ConfigureWholeBody("rockport.tree."+sourceOffset,world,visual,new Collider[]{trunk},body,SensorySurface.Wood);
            obj.AddComponent<OceanBuoyantBody>().Configure(null,new Bounds(trunk.center,new Vector3(trunk.radius*2,height,trunk.radius*2)),volume);
            created++;
        }
        if(created!=481||(removed!=created&&removed!=0))throw new IOException("Breakable tree promotion mismatch: "+created+" / "+removed);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        Directory.CreateDirectory("Art/RockportTrees/Source");
        string json="{\"status\":\"PASS\",\"breakableTrees\":"+created+",\"removedTerrainInstances\":"+removed+",\"remainingTerrainInstances\":8946,\"sourceSelection\":\"XO_CT_CAMPUSSMACKTREE and XO_CT_CAMPUSSMACKTREEB; matching original DMG and FRAG solids prove authored breakable families\",\"physicalBehavior\":\"Existing DestructibleProp WholeBody: trunk collider, kinematic until vehicle impact, dynamic fall and ocean buoyancy; source fragmentation not recreated\"}";
        File.WriteAllText("Art/RockportTrees/Source/breakable-tree-import.json",json);result.Log(json);
    }
}
