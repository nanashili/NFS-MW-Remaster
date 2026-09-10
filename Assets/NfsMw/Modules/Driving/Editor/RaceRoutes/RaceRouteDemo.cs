using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace NfsMwRemaster.Driving.Editor
{
    public static class RaceRouteDemo
    {
        public static RaceRouteDefinition Create()
        {
            const string folder="Assets/NfsMw/Modules/Driving/Examples/RaceRoutes";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            var laneId=RoadId.New();var roadId=RoadId.New();var network=ScriptableObject.CreateInstance<RoadNetworkAsset>();
            var samples=new RoadLaneSample[31];for(int i=0;i<samples.Length;i++)samples[i]=new RoadLaneSample {distance=i*10,station=i*10,width=8,position=Vector3.forward*i*10,forward=Vector3.forward,left=Vector3.left,up=Vector3.up};
            var mesh=new Mesh {name="Synthetic road surface",vertices=new[]{new Vector3(-4,0,0),new Vector3(4,0,0),new Vector3(-4,0,300),new Vector3(4,0,300)},triangles=new[]{0,2,1,1,2,3}};
            mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,AssetDatabase.GenerateUniqueAssetPath(folder+"/GrayboxSurface.asset"));
            network.Initialize(RoadId.New(),"synthetic-route-fixture-1",new[]{new RoadBakedLane(laneId,roadId,default,20,samples,Array.Empty<RoadId>(),null)},new[]{new RoadBakedChunk(roadId,laneId,mesh,Vector3.zero,null,null,true)});
            AssetDatabase.CreateAsset(network,AssetDatabase.GenerateUniqueAssetPath(folder+"/GrayboxRoads.asset"));
            var source=RaceRouteCommands.Create(folder+"/GrayboxSprint.asset");source.displayName="Synthetic graybox sprint";source.network=network;source.gridCount=2;
            source.legs=new[]{new RaceRouteLeg {label="Straight sector",paths=new[]{new RaceRoutePath {spans=new[]{new RacingRouteSpan {laneId=laneId.ToString(),startMetres=30,endMetres=270}}}}}};
            var plan=RaceRouteCompiler.Build(source);if(!plan.Valid)throw new InvalidOperationException(string.Join("\n",plan.issues));RaceRouteCommands.Publish(source,plan,folder+"/GrayboxPublished.asset");AssetDatabase.SaveAssets();return source;
        }
        private static void ValidateVehicle(RaceRouteDefinition source)
        {
            const string folder="Assets/NfsMw/Modules/Driving/Examples/RaceRoutes";
            void Save(UnityEngine.Object asset,string file)=>AssetDatabase.CreateAsset(asset,AssetDatabase.GenerateUniqueAssetPath(folder+"/"+file+".asset"));
            var setup=ScriptableObject.CreateInstance<RacingVehicleSetup>();setup.tuning=VehicleTuning.CreateStreetRacer();setup.tuning.handling.brakeToDrift=false;
            Save(setup.tuning,"SprintTune");Save(setup,"SprintVehicle");source.vehicle=setup;
            var line=ScriptableObject.CreateInstance<RacingLineSource>();line.vehicle=setup;
            line.route=RaceRouteCommands.ExportLineRoute(source,null,folder+"/SprintLineRoute.asset");
            line.capability=ScriptableObject.CreateInstance<RacingCapabilityProfile>();Save(line.capability,"SprintCapability");
            line.planner.maximumSpeed=10;line.planner.exitSpeed=8;line.verification.trials=3;Save(line,"SprintLine");
            using var planner=new RacingLinePlanner(RacingLineSnapshot.Capture(line),RacingLineFamily.Grip);
            while(!planner.IsDone)planner.Step(8);
            using(var rollout=new RacingLineRollout(line,planner.Result))
            {
                while(!rollout.IsDone)rollout.Advance(8);
                if(!rollout.Result.passed)throw new InvalidOperationException("Route sample vehicle qualification failed: "+JsonUtility.ToJson(rollout.Result));
                RacingLineEditorOperations.Publish(line,planner.Result,rollout.Result,folder+"/SprintVerifiedLine.asset");
            }
            source.racingLine=line;RaceRouteCommands.Publish(source,RaceRouteCompiler.Build(source),folder+"/SprintVehiclePublished.asset");EditorUtility.SetDirty(source);AssetDatabase.SaveAssets();
            Directory.CreateDirectory("RaceRouteValidation");
            using(var preview=new RaceRouteTestPreview(source))
            {
                while(!preview.Done)preview.Advance();
                File.WriteAllText("RaceRouteValidation/vehicle.json",preview.ReportJson);
                if(!preview.AllTrialsSucceeded)throw new InvalidOperationException("Route sample mission did not succeed: "+preview.Summary);
            }
        }
        public static void Measure()
        {
            if(!Application.isBatchMode)throw new InvalidOperationException("Measure in an isolated project.");
            var sample=AssetDatabase.LoadAssetAtPath<RaceRouteDefinition>("Assets/NfsMw/Modules/Driving/Examples/RaceRoutes/GrayboxSprint.asset");
            var copy=UnityEngine.Object.Instantiate(sample);var records=new List<Measurement>();
            try
            {
                string lane=copy.legs[0].paths[0].spans[0].laneId;
                foreach(int sectors in new[]{1,10,100,200})
                {
                    copy.legs=Enumerable.Range(0,sectors).Select(i=>new RaceRouteLeg {paths=new[]{new RaceRoutePath {spans=new[]{new RacingRouteSpan {laneId=lane,startMetres=40+230f*i/sectors,endMetres=40+230f*(i+1)/sectors}}}}}).ToArray();
                    for(int repeat=0;repeat<21;repeat++)
                    {
                        var plan=RaceRouteCompiler.Build(copy);if(!plan.Valid)throw new InvalidOperationException(string.Join("; ",plan.issues));
                        records.Add(new Measurement {sectors=sectors,gates=plan.legs.Sum(l=>l.paths[0].gates.Length),warm=repeat>0,milliseconds=plan.milliseconds});
                    }
                }
                Directory.CreateDirectory("RaceRouteValidation");
                File.WriteAllText("RaceRouteValidation/benchmark.json",JsonUtility.ToJson(new Measurements {unity=Application.unityVersion,hardware=SystemInfo.processorType,os=SystemInfo.operatingSystem,records=records.ToArray()},true));
                using(var preview=new RaceRouteTestPreview(sample)){while(!preview.Done)preview.Advance();if(!preview.AllTrialsSucceeded)throw new InvalidOperationException(preview.Summary);File.WriteAllText("RaceRouteValidation/vehicle.json",preview.ReportJson);}
                var line=AssetDatabase.LoadAssetAtPath<RacingLineSource>(RacingLineStudioDemo.GripPath);
                File.WriteAllText("RaceRouteValidation/existing-line.txt","Stored: "+line.published.Fingerprint+"\nCurrent: "+RacingLineSnapshot.Capture(line).Fingerprint+"\n");
            }
            finally{UnityEngine.Object.DestroyImmediate(copy);}
        }
        [Serializable] private struct Measurement {public int sectors,gates;public bool warm;public double milliseconds;}
        [Serializable] private sealed class Measurements {public string unity,hardware,os;public Measurement[] records;}
        public static void BuildScene()
        {
            if(!Application.isBatchMode)throw new InvalidOperationException("Build the fixture scene in an isolated batch project.");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var source=Create();
            var road=GameObject.CreatePrimitive(PrimitiveType.Cube);road.name="Synthetic road collision";road.transform.position=new Vector3(0,-.25f,150);road.transform.localScale=new Vector3(8,.5f,300);
            var camera=new GameObject("Overview").AddComponent<Camera>(); NfsMwRemaster.Driving.Editor.Rendering.HdrpSceneDefaults.Camera(camera);camera.transform.position=new Vector3(100,160,100);camera.transform.LookAt(new Vector3(0,0,150));camera.farClipPlane=1000;
            var sun=new GameObject("Sun").AddComponent<Light>();Rendering.HdrpSceneDefaults.Sun(sun);sun.transform.eulerAngles=new Vector3(45,30,0);
            RaceRouteCommands.PlaceEvent(source,RaceRouteCompiler.Build(source));EditorSceneManager.SaveScene(scene,"Assets/NfsMw/Modules/Driving/Examples/RaceRoutes/RouteAuthoring.unity");
            ValidateVehicle(source);
            var entry=UnityEngine.Object.FindObjectsByType<FreeRoamEventDefinition>(FindObjectsSortMode.None).First(e=>e.Id==source.id);entry.BindRoute(source.published);EditorUtility.SetDirty(entry);EditorSceneManager.SaveScene(scene);
            Directory.CreateDirectory("RaceRouteValidation");File.WriteAllText("RaceRouteValidation/demo.txt",RaceRouteTestPreview.Replay(source,RaceRouteCompiler.Build(source)));
        }
    }
}
