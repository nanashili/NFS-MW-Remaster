using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
namespace NfsMwRemaster.Driving.Editor
{
    public sealed class RaceRoutePlan
    {
        public string fingerprint;public RaceRouteBakedLeg[] legs=Array.Empty<RaceRouteBakedLeg>();
        public readonly List<RaceRouteIssue> issues=new List<RaceRouteIssue>();
        public readonly List<Vector3> grid=new List<Vector3>();
        public readonly List<Quaternion> gridRotations=new List<Quaternion>();
        public float length,elevationGain,maximumGrade,minimumWidth=float.PositiveInfinity,estimatedSeconds;
        public double milliseconds;public bool Valid=>issues.All(i=>!i.error);
    }
    public static class RaceRouteCompiler
    {
        public static RoadBakedLane Lane(RaceRouteDefinition source,string id)=>source.network?.Lanes.FirstOrDefault(l=>l.Id.ToString()==id);
        public static float End(RacingRouteSpan span,RoadBakedLane lane)=>span.endMetres<0?lane.Length:span.endMetres;
        public static string Fingerprint(RaceRouteDefinition source)
        {
            var b=new StringBuilder("RaceRoute/1|");void Add(object v)=>b.Append(Convert.ToString(v,CultureInfo.InvariantCulture)).Append('|');
            Add(source.network==null?"":source.network.NetworkId.ToString());Add(source.schema);Add(source.id);Add(source.displayName);Add(source.localizationKey);Add(source.tags);Add(source.policy);Add(source.laps);Add(source.timeLimit);Add(source.targetSpeedKph);
            if(source.vehicle!=null)Add(JsonUtility.ToJson(source.vehicle.dimensions));Add(source.minimumWidth);Add(source.maximumGradeDegrees);Add(source.maximumTurnDegrees);Add(source.gridCount);Add(source.vehicleWidth);Add(source.vehicleLength);Add(source.vehicleHeight);Add(source.gridGap);Add(source.sideBySideGrid);Add(source.finishRunoff);
            Add(AssetDatabase.GetAssetPath(source.requiredSurface));Add(string.Join(",",source.excludedRoadClasses??Array.Empty<RoadClass>()));
            foreach(var leg in source.legs??Array.Empty<RaceRouteLeg>())Add(JsonUtility.ToJson(leg));
            foreach(var id in (source.legs??Array.Empty<RaceRouteLeg>()).Where(l=>l?.paths!=null).SelectMany(l=>l.paths).Where(p=>p?.spans!=null).SelectMany(p=>p.spans).Where(s=>s!=null).Select(s=>s.laneId).Distinct().OrderBy(s=>s,StringComparer.Ordinal))
            {
                Add(id);var lane=Lane(source,id);if(lane==null){Add("MISSING");continue;}
                Add(lane.RoadId);Add(lane.Class);Add(lane.Speed);Add(AssetDatabase.GetAssetPath(lane.Surface));
                foreach(var sample in lane.Samples)Add(JsonUtility.ToJson(sample));foreach(var next in lane.Successors)Add(next);
            }
            using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(b.ToString()))).Replace("-","");
        }
        public static RaceRoutePlan Build(RaceRouteDefinition source,Func<float,bool> cancel=null)
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();var plan=new RaceRoutePlan();
            void Error(string rule,string owner,string message)=>plan.issues.Add(new RaceRouteIssue(rule,owner,message));
            if(source==null||source.schema!=1||source.network==null||source.network.SchemaVersion!=RoadNetworkAsset.CurrentSchema)
            {Error("ROUTE_SOURCE","","Choose a supported route and published road network.");return plan;}
            if(source.legs==null||source.legs.Length==0||source.legs.Length>256){Error("ROUTE_LEGS",source.id,"Use 1–256 ordered sectors.");return plan;}
            if(!Enum.IsDefined(typeof(FreeRoamEventKind),source.policy)||source.policy==FreeRoamEventKind.Pursuit){Error("ROUTE_POLICY",source.id,"Pursuit uses its existing mission policy, not ordered race traversal.");return plan;}
            if(!float.IsFinite(source.targetSpeedKph)||source.targetSpeedKph<0)Error("ROUTE_POLICY",source.id,"Speed target must be finite and nonnegative.");
            if(source.laps<1||source.laps>100||source.policy!=FreeRoamEventKind.Circuit&&source.laps!=1||!float.IsFinite(source.timeLimit)||source.timeLimit<=0)
                Error("ROUTE_POLICY",source.id,"Use a positive time limit and one lap except for Circuit (1–100).");
            foreach(float v in new[]{source.minimumWidth,source.maximumGradeDegrees,source.maximumTurnDegrees,source.vehicleWidth,source.vehicleLength,source.vehicleHeight,source.gridGap})if(!float.IsFinite(v)||v<=0)Error("ROUTE_CONSTRAINT",source.id,"Dimensions and constraints must be finite and positive.");
            if(!float.IsFinite(source.finishRunoff)||source.finishRunoff<0||source.gridCount<1||source.gridCount>64)Error("ROUTE_GRID",source.id,"Use 1–64 entrants and a finite nonnegative runoff.");
            if(source.vehicle!=null&&(!RacingLineSnapshot.Finite(source.vehicle.dimensions)||source.vehicle.dimensions.x>source.vehicleWidth||source.vehicle.dimensions.y>source.vehicleHeight||source.vehicle.dimensions.z>source.vehicleLength))Error("ROUTE_ENTRANT",source.id,"The linked vehicle exceeds the declared largest entrant envelope.");
            var ids=new HashSet<string>();void Identity(string id){if(!Guid.TryParseExact(id,"N",out _)||!ids.Add(id))Error("ROUTE_ID",id,"Missing or duplicate identity; duplicate with the route command.");}
            Identity(source.id);var baked=new List<RaceRouteBakedLeg>();int budget=0;
            for(int i=0;i<source.legs.Length;i++)
            {
                if(cancel!=null&&cancel((float)i/source.legs.Length))throw new OperationCanceledException();
                var leg=source.legs[i];if(leg?.paths==null||leg.paths.Length==0||leg.paths.Length>8){Error("ROUTE_PATHS",source.id,"Each sector needs 1–8 paths.");continue;}
                Identity(leg.id);if(!float.IsFinite(leg.gateSpacing)||leg.gateSpacing<1||!float.IsFinite(leg.gateHeight)||leg.gateHeight<source.vehicleHeight){Error("ROUTE_GATE",leg.id,"Gate spacing must be at least 1 m and height must fit the vehicle.");continue;}
                var paths=new List<RaceRouteBakedPath>();
                foreach(var path in leg.paths)
                {
                    if(path?.spans==null||path.spans.Length==0||path.spans.Length>1024){Error("ROUTE_SPANS",leg.id,"Path needs 1–1024 lane occurrences.");continue;}
                    Identity(path.id);var gates=new List<RaceRouteGate>();float length=0;RoadBakedLane previous=null;RacingRouteSpan previousSpan=null;
                    foreach(var span in path.spans)
                    {
                        if(span==null){Error("ROUTE_SPAN",path.id,"Remove null occurrences.");continue;}Identity(span.id);
                        var lane=Lane(source,span.laneId);if(lane==null){Error("ROUTE_UNRESOLVED",span.id,"Lane "+span.laneId+" is missing. Explicitly remap it; no nearest-lane substitution is performed.");continue;}
                        float end=End(span,lane);
                        if(span.endMetres<0&&span.endMetres!=-1||!float.IsFinite(span.startMetres)||!float.IsFinite(end)||span.startMetres<0||end>lane.Length||end-span.startMetres<.5f){Error("ROUTE_RANGE",span.id,"Occurrence must travel forward at least 0.5 m within the lane.");continue;}
                        if(previous!=null&&!Connected(previous,previousSpan,lane,span))Error("ROUTE_CONNECTION",span.id,"No authored successor/contiguous occurrence connects this path.");
                        if(!Allowed(source,lane,out var reason))Error("ROUTE_RESTRICTION",span.id,reason);
                        if(previous!=null&&Vector3.Angle(previous.Sample(End(previousSpan,previous)).forward,lane.Sample(span.startMetres).forward)>source.maximumTurnDegrees)Error("ROUTE_TURN",span.id,"Turn exceeds the configured angle.");
                        float start=span.startMetres;var last=lane.Sample(start);int count=Mathf.CeilToInt((end-start)/leg.gateSpacing);
                        for(int s=0;s<=count;s++)
                        {
                            if(++budget>20000)throw new ArgumentException("Route gate budget exceeded; increase spacing or split routes.");
                            float station=s==0?Mathf.Min(start+.25f,end):Mathf.Lerp(start,end,(float)s/count);var sample=lane.Sample(station);
                            gates.Add(new RaceRouteGate{id=span.id+"/"+s,laneId=span.laneId,position=sample.position,forward=sample.forward,up=sample.up,width=sample.width,height=leg.gateHeight,distance=length+station-start});
                            plan.minimumWidth=Mathf.Min(plan.minimumWidth,sample.width);plan.maximumGrade=Mathf.Max(plan.maximumGrade,Mathf.Abs(Mathf.Asin(Mathf.Clamp(sample.forward.y,-1,1))*Mathf.Rad2Deg));
                            if(path==leg.paths[0])plan.elevationGain+=Mathf.Max(0,sample.position.y-last.position.y);last=sample;
                        }
                        length+=end-start;if(path==leg.paths[0])plan.estimatedSeconds+=(end-start)/Mathf.Max(1,lane.Speed);
                        previous=lane;previousSpan=span;
                    }
                    paths.Add(new RaceRouteBakedPath{id=path.id,length=length,gates=gates.ToArray()});
                }
                if(paths.Count>0)
                {
                    var main=leg.paths[0];foreach(var other in leg.paths.Skip(1))
                        if(other?.spans?.Length>0&&main?.spans?.Length>0 && (other.spans[0].laneId!=main.spans[0].laneId||other.spans.Last().laneId!=main.spans.Last().laneId||Mathf.Abs(other.spans[0].startMetres-main.spans[0].startMetres)>.001f||Mathf.Abs(other.spans.Last().endMetres-main.spans.Last().endMetres)>.001f))
                            Error("ROUTE_REJOIN",other.id,"Alternatives must share the same first and last road-relative anchors.");
                    plan.length+=paths[0].length;baked.Add(new RaceRouteBakedLeg{id=leg.id,paths=paths.ToArray()});
                }
            }
            plan.legs=baked.ToArray();
            if(plan.Valid)
            {
                for(int i=1;i<source.legs.Length;i++)if(!ConnectLegs(source,source.legs[i-1],source.legs[i]))Error("ROUTE_SEAM",source.legs[i].id,"Sector boundary is not connected.");
                if(source.policy==FreeRoamEventKind.Circuit&&!ConnectLegs(source,source.legs.Last(),source.legs[0]))Error("ROUTE_LOOP",source.id,"Circuit seam does not close through authored connectivity.");
                if(source.policy==FreeRoamEventKind.Circuit&&plan.length<50)Error("ROUTE_LOOP",source.id,"Circuit is shorter than 50 m.");
                Grid(source,plan);
                var finish=source.legs.Last().paths[0].spans.Last();var finishLane=Lane(source,finish.laneId);
                if(source.policy!=FreeRoamEventKind.Circuit&&finishLane.Length-End(finish,finishLane)<source.finishRunoff)Error("ROUTE_RUNOFF",finish.id,"Finish needs more same-lane runoff; move its end anchor back.");
            }
            plan.issues.Add(new RaceRouteIssue("ROUTE_ESTIMATE",source.id,"Time estimate uses posted lane speeds; this is not a measured lap time.",false));
            plan.issues.Add(new RaceRouteIssue("ROUTE_AI",source.id,source.racingLine==null?"No Racing Line Studio source is linked; AI readiness is unverified.":"Linked Racing Line Studio source must be regenerated and verified for this exact traversal.",false));
            if(plan.Valid)plan.fingerprint=Fingerprint(source);plan.milliseconds=watch.Elapsed.TotalMilliseconds;return plan;
        }
        private static bool ConnectLegs(RaceRouteDefinition s,RaceRouteLeg a,RaceRouteLeg b)
        {var first=b.paths[0].spans[0];var last=a.paths[0].spans.Last();return Connected(Lane(s,last.laneId),last,Lane(s,first.laneId),first);}
        public static bool Connected(RoadBakedLane a,RacingRouteSpan sa,RoadBakedLane b,RacingRouteSpan sb)
        {
            if(a==null||b==null)return false;
            if(a.Id==b.Id&&Mathf.Abs(End(sa,a)-sb.startMetres)<.01f)return true;
            return Mathf.Abs(End(sa,a)-a.Length)<.01f&&sb.startMetres<.01f&&a.Successors.Contains(b.Id)&&Vector3.Distance(a.Sample(a.Length).position,b.Sample(0).position)<2;
        }
        public static bool Allowed(RaceRouteDefinition source,RoadBakedLane lane,out string reason)
        {
            reason="";
            if((source.excludedRoadClasses??Array.Empty<RoadClass>()).Contains(lane.Class))reason="Road class is excluded.";
            else if(source.requiredSurface!=null&&source.requiredSurface!=lane.Surface)reason="Road surface does not match.";
            else if(lane.Samples.Any(s=>s.width<Mathf.Max(source.minimumWidth,source.vehicleWidth)))reason="Lane is narrower than the configured minimum.";
            else if(lane.Samples.Any(s=>Mathf.Abs(Mathf.Asin(Mathf.Clamp(s.forward.y,-1,1))*Mathf.Rad2Deg)>source.maximumGradeDegrees))reason="Lane grade exceeds the configured maximum.";
            return reason.Length==0;
        }
        private static void Grid(RaceRouteDefinition source,RaceRoutePlan plan)
        {
            var first=source.legs[0].paths[0].spans[0];var lane=Lane(source,first.laneId);int columns=source.sideBySideGrid?2:1;
            for(int i=0;i<source.gridCount;i++)
            {
                float station=first.startMetres-source.vehicleLength/2-source.gridGap-(i/columns)*(source.vehicleLength+source.gridGap);
                if(station<0){plan.issues.Add(new RaceRouteIssue("ROUTE_GRID",first.id,"Move the start anchor forward to leave room for all grid rows."));break;}
                var pose=lane.Sample(station);float needed=columns*source.vehicleWidth+(columns-1)*source.gridGap;
                if(pose.width<needed){plan.issues.Add(new RaceRouteIssue("ROUTE_GRID",first.id,"The largest entrants do not fit the selected grid columns."));break;}
                plan.gridRotations.Add(Quaternion.LookRotation(pose.forward,pose.up));
                plan.grid.Add(pose.position+pose.left*(columns==1?0:(i%2==0?-.5f:.5f)*(source.vehicleWidth+source.gridGap)));
            }
        }
    }
}
