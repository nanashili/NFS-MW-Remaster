using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace NfsMwRemaster.Driving.Editor
{
    public sealed class RaceRouteTestPreview:IDisposable
    {
        private readonly RaceRouteDefinition source;private readonly string fingerprint;private readonly RacingLineRollout rollout;
        private MissionCourse course;private int trial=-1;private bool disposed;private float trialElapsed;
        private readonly List<Sample> samples=new List<Sample>();private readonly List<string> outcomes=new List<string>();
        public UnityEngine.SceneManagement.Scene PreviewScene=>rollout.PreviewScene;
        public bool AllTrialsSucceeded=>rollout.IsDone&&rollout.Result?.passed==true&&course?.Outcome==MissionState.Succeeded&&outcomes.All(o=>o==MissionState.Succeeded.ToString());
        public bool Done=>disposed||rollout.IsDone;
        public string Summary=>"Trial "+(trial+1)+" · "+(course?.Outcome.ToString()??"Preparing")+" · "+(course?.CheckpointsPassed??0)+" sectors · "+samples.Count+" samples. Zero-reward isolated context.";
        public string ReportJson=>JsonUtility.ToJson(new Report {route=source.id,revision=fingerprint,unity=Application.unityVersion,hardware=SystemInfo.processorType,vehicle=source.racingLine.vehicle.name,vehicleRevision=RacingLineSnapshot.VehicleFingerprintOf(source.racingLine.vehicle),roadRevision=source.network.Fingerprint,lineRevision=source.racingLine.published.Fingerprint,controllerRevision=RacingLineTracker.Revision,seed=source.racingLine.planner.seed,utc=DateTime.UtcNow.ToString("O"),samples=samples.ToArray(),outcomes=outcomes.Concat(course==null?Array.Empty<string>():new[]{course.Outcome.ToString()}).ToArray(),fixedStep=source.racingLine.vehicle.fixedStep},true);
        public RaceRouteTestPreview(RaceRouteDefinition source)
        {
            this.source=source;fingerprint=RaceRouteCompiler.Fingerprint(source);
            if(source.published==null||source.published.Fingerprint!=fingerprint)throw new ArgumentException("Publish the current route before a vehicle test.");
            var line=source.racingLine;if(line?.published==null)throw new ArgumentException("Geometry-only preview: link a current verified Racing Line Studio publication first.");
            if(source.vehicle!=null&&source.vehicle!=line.vehicle)throw new ArgumentException("The linked line uses a different vehicle setup. Select its setup or regenerate the line.");
            var expected=RaceRouteCommands.LineSpans(source);
            if(line.route==null||line.route.closed||line.route.network!=source.network||line.route.spans.Length!=expected.Length||line.route.spans.Where((s,i)=>s.laneId!=expected[i].laneId||s.startMetres!=expected[i].startMetres||s.endMetres!=expected[i].endMetres).Any())throw new ArgumentException("Linked line does not match the exact main traversal. Export its adapter and regenerate it.");
            rollout=new RacingLineRollout(line,line.published.CopyTrajectory());rollout.VehicleStepped+=Step;
        }
        private void Step(int index,int vehicle,float dt,Vector3 previous,Vector3 current,float speed)
        {
            if(vehicle!=0)return;
            if(index!=trial){if(course!=null){outcomes.Add(course.Outcome.ToString());course.Dispose();}trial=index;trialElapsed=0;course=Course(source,source.published);}
            trialElapsed+=dt;course.Advance(dt,previous,current,speed);
            if(samples.Count<50000)samples.Add(new Sample {trial=index,time=trialElapsed,position=current,speed=speed,progress=course.CheckpointsPassed});
        }
        public void DrawSpeedHeatmap()
        {
            for (int i=1;i<samples.Count;i+=5)
            {
                if(samples[i].trial!=samples[i-1].trial)continue;
                UnityEditor.Handles.color=Color.Lerp(Color.blue,Color.red,Mathf.Clamp01(samples[i].speed/150));
                UnityEditor.Handles.DrawLine(samples[i-1].position,samples[i].position,3);
            }
        }
        public void Advance(){if(disposed)return;if(RaceRouteCompiler.Fingerprint(source)!=fingerprint)throw new InvalidOperationException("Route changed; stop the stale test.");if(!rollout.IsDone)rollout.Advance();}
        private static MissionCourse Course(RaceRouteDefinition s,RaceRoutePublication p)=>new MissionCourse(p.Checkpoints,s.laps,s.timeLimit,s.policy==FreeRoamEventKind.Speedtrap?s.targetSpeedKph:0,"route.preview."+s.id,0,publishedRoute:p);
        public static string Replay(RaceRouteDefinition source,RaceRoutePlan plan)
        {
            var publication=ScriptableObject.CreateInstance<RaceRoutePublication>();
            try
            {
                publication.Initialize(source.id,plan.fingerprint,source.network,plan.legs);using var course=Course(source,publication);
                for(int lap=0;lap<source.laps;lap++)foreach(var leg in plan.legs)foreach(var gate in leg.paths[0].gates)course.Advance(.02f,gate.position-gate.forward,gate.position+gate.forward,source.targetSpeedKph);
                if(course.CheckpointsPassed!=course.TotalCheckpoints)throw new InvalidOperationException("Geometry replay did not complete all ordered sectors.");
                return "Geometry-only replay: "+course.Outcome+", "+course.CheckpointsPassed+" sector passages; no vehicles, traffic, police, rewards or persistence were simulated.";
            }
            finally{UnityEngine.Object.DestroyImmediate(publication);}
        }
        public void Dispose(){if(disposed)return;disposed=true;rollout.VehicleStepped-=Step;rollout.Dispose();course?.Dispose();}
        [Serializable] private struct Sample{public int trial,progress;public float time,speed;public Vector3 position;}
        [Serializable] private sealed class Report{public string route,revision,unity,hardware,vehicle,vehicleRevision,roadRevision,lineRevision,controllerRevision,utc;public int seed;public float fixedStep;public Sample[] samples;public string[] outcomes;}
    }
}
