using System;
using System.Collections.Generic;
using System.IO;
using NfsMwRemaster.Driving;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace NfsMwRemaster.Maps.Editor
{
    public static class MapDemo
    {
        public static RoadNetworkAsset Fixture()
        {
            var ids=new[]{RoadId.New(),RoadId.New(),RoadId.New(),RoadId.New(),RoadId.New(),RoadId.New()};
            var starts=new[]{new Vector3(-300,0,-200),new Vector3(300,0,-200),new Vector3(300,0,200),new Vector3(-300,0,200),new Vector3(-400,12,0),new Vector3(0,-8,-400)};
            var ends=new[]{starts[1],starts[2],starts[3],starts[0],new Vector3(400,12,0),new Vector3(0,-8,400)};
            var lanes=new RoadBakedLane[6];
            for(int i=0;i<6;i++)
            {
                var samples=new RoadLaneSample[65];float distance=0;Vector3 previous=starts[i];
                for(int j=0;j<samples.Length;j++)
                {float t=j/64f;var p=Vector3.Lerp(starts[i],ends[i],t);if(i==4)p.z+=Mathf.Sin(t*Mathf.PI*2)*35;distance+=Vector3.Distance(previous,p);var forward=(ends[i]-starts[i]).normalized;samples[j]=new RoadLaneSample{position=p,distance=distance,station=t,width=8,forward=forward,left=Vector3.Cross(forward,Vector3.up),up=Vector3.up};previous=p;}
                lanes[i]=new RoadBakedLane(ids[i],ids[i],i==4?RoadClass.Highway:RoadClass.Local,20,samples,i<4?new[]{ids[(i+1)%4]}:Array.Empty<RoadId>(),null);
            }
            var asset=ScriptableObject.CreateInstance<RoadNetworkAsset>();asset.Initialize(RoadId.New(),"synthetic-map-fixture-v1",lanes,Array.Empty<RoadBakedChunk>());return asset;
        }
        [MenuItem("Tools/NFS MW Remaster/Maps/Create bridge and tunnel sample")]
        public static void MenuCreate()=>Selection.activeObject=Create();
        public static MapDefinition Create()
        {
            var original=SceneManager.GetActiveScene();
            if(string.IsNullOrEmpty(original.path)&&original.rootCount>0)throw new InvalidOperationException("Save the current untitled scene before creating the sample.");
            string folder="Assets/NfsMw/Modules/Driving/Examples/MapStudio/"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            var roads=Fixture();AssetDatabase.CreateAsset(roads,folder+"/Roads.asset");var style=ScriptableObject.CreateInstance<MapStyle>();AssetDatabase.CreateAsset(style,folder+"/Style.asset");
            var definition=ScriptableObject.CreateInstance<MapDefinition>();definition.roads=roads;definition.style=style;definition.tileSize=128;
            var levels=new List<MapLaneLevel>();for(int i=0;i<roads.Lanes.Count;i++)levels.Add(new MapLaneLevel{lane=roads.Lanes[i].Id,level=i==4?1:i==5?-1:0,structure=i==4?MapRoadStructure.Bridge:i==5?MapRoadStructure.Tunnel:MapRoadStructure.Surface});definition.levels=levels.ToArray();
            AssetDatabase.CreateAsset(definition,folder+"/WorldMap.asset");EditorUtility.SetDirty(definition);AssetDatabase.SaveAssets();var result=MapBaker.Bake(definition);
            Scene scene=default;
            try
            {
                if(string.IsNullOrEmpty(original.path))EditorSceneManager.SaveScene(original,folder+"/EmptyWorkspace.unity");
                scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
                var root=new GameObject("Synthetic map fixture — stationary player anchor");var roadOwner=root.AddComponent<RoadNetwork>();roadOwner.ConfigureBaked(roads);
                var policy=root.AddComponent<FreeRoamMapKnowledge>();policy.publicRoadNetwork=true;
                var map=root.AddComponent<WorldMapController>();map.map=result.publication;map.style=style;map.roads=roadOwner;map.player=root.transform;map.knowledgeProvider=policy;root.transform.position=new Vector3(-280,0,-200);
                var camera=new GameObject("Map demo camera").AddComponent<Camera>(); NfsMwRemaster.Driving.Editor.Rendering.HdrpSceneDefaults.Camera(camera);camera.transform.position=new Vector3(0,100,-100);camera.transform.rotation=Quaternion.Euler(60,0,0);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.035f,.035f);
                EditorSceneManager.SaveScene(scene,folder+"/MapStudio.unity");AssetDatabase.SaveAssets();
                File.WriteAllText("/tmp/nfs-map-demo.txt",folder);Debug.Log($"Map sample: {folder}; bake {result.milliseconds:0.0} ms, {result.bytes} bytes, {result.segments} segments.");
            }
            finally{if(original.IsValid())SceneManager.SetActiveScene(original);if(scene.IsValid())EditorSceneManager.CloseScene(scene,true);}
            return definition;
        }
        public static void BatchCreate(){Create();}
    }
}
