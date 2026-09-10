using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace NfsMwRemaster.Driving.Editor
{
    public static class RaceRouteCommands
    {
        public static void Edit(RaceRouteDefinition source,string name,Action action)
        {
            if(source==null||EditorApplication.isPlayingOrWillChangePlaymode)throw new ArgumentException("Choose a route outside Play mode.");
            Workspace.RacingEditGuard.Require(source);
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.RegisterCompleteObjectUndo(source,name);
            try{action();EditorUtility.SetDirty(source);Undo.FlushUndoRecordObjects();Undo.CollapseUndoOperations(group);}
            catch{Undo.RevertAllDownToGroup(group);throw;}
        }
        public static RaceRouteDefinition Create(string path)
        {var source=ScriptableObject.CreateInstance<RaceRouteDefinition>();AssetDatabase.CreateAsset(source,AssetDatabase.GenerateUniqueAssetPath(path));return source;}
        public static RaceRouteDefinition Duplicate(RaceRouteDefinition source,string path)
        {
            var copy=UnityEngine.Object.Instantiate(source);copy.id=Guid.NewGuid().ToString("N");copy.published=null;copy.racingLine=null;
            foreach(var leg in copy.legs){leg.id=Guid.NewGuid().ToString("N");foreach(var route in leg.paths){route.id=Guid.NewGuid().ToString("N");foreach(var span in route.spans)span.id=Guid.NewGuid().ToString("N");}}
            AssetDatabase.CreateAsset(copy,AssetDatabase.GenerateUniqueAssetPath(path));return copy;
        }
        public static RacingRouteSpan[] Suggest(RaceRouteDefinition source,string startId,float start,string finishId,float finish,RaceRouteSuggestion metric,Func<float,bool> cancel=null)
        {
            if(source?.network==null)throw new ArgumentException("Choose a road publication.");
            var first=RaceRouteCompiler.Lane(source,startId);var last=RaceRouteCompiler.Lane(source,finishId);
            if(first==null||last==null||start<0||start>=first.Length||finish<=0||finish>last.Length)throw new ArgumentException("Choose valid start/finish lane anchors.");
            if(!RaceRouteCompiler.Allowed(source,first,out var restriction))throw new ArgumentException(restriction);
            if(first==last&&finish>start)return new[]{new RacingRouteSpan {laneId=startId,startMetres=start,endMetres=finish}};
            var distances=new Dictionary<RoadId,float>();var previous=new Dictionary<RoadId,RoadId>();var open=new HashSet<RoadId>();var byId=source.network.Lanes.ToDictionary(l=>l.Id);var closed=new HashSet<RoadId>();
            float Cost(RoadBakedLane l)=>l.Length/(metric==RaceRouteSuggestion.TravelTime?Mathf.Max(1,l.Speed):1);
            foreach(var next in first.Successors)if(byId.TryGetValue(next,out var initial)&&Vector3.Distance(first.Sample(first.Length).position,initial.Sample(0).position)<2&&Vector3.Angle(first.Sample(first.Length).forward,initial.Sample(0).forward)<=source.maximumTurnDegrees){distances[next]=Cost(byId[next]);previous[next]=first.Id;open.Add(next);}
            int visits=0;bool found=false;
            while(open.Count>0)
            {
                if(++visits>10000)throw new ArgumentException("Routing visit budget exceeded.");
                if(cancel!=null&&cancel((float)visits/Math.Max(1,byId.Count)))throw new OperationCanceledException();
                var id=open.OrderBy(n=>distances[n]).ThenBy(n=>n.ToString(),StringComparer.Ordinal).First();open.Remove(id);if(!closed.Add(id))continue;
                var lane=byId[id];if(!RaceRouteCompiler.Allowed(source,lane,out _))continue;
                if(id==last.Id){found=true;break;}
                foreach(var next in lane.Successors)
                {
                    if(!byId.TryGetValue(next,out var successor)||closed.Contains(next)||Vector3.Distance(lane.Sample(lane.Length).position,successor.Sample(0).position)>=2||Vector3.Angle(lane.Sample(lane.Length).forward,successor.Sample(0).forward)>source.maximumTurnDegrees)continue;
                    float distance=distances[id]+Cost(successor);
                    if(!distances.TryGetValue(next,out var old)||distance<old){distances[next]=distance;previous[next]=id;open.Add(next);}
                }
            }
            if(!found)throw new ArgumentException("No connected route satisfies these restrictions. Add explicit via sectors or fix upstream road connectivity.");
            var reverse=new List<RoadId>{last.Id};var cursor=last.Id;
            do{cursor=previous[cursor];reverse.Add(cursor);if(reverse.Count>10001)throw new ArgumentException("Route predecessor cycle.");}while(cursor!=first.Id);
            reverse.Reverse();var result=reverse.Select(id=>new RacingRouteSpan {laneId=id.ToString()}).ToArray();result[0].startMetres=start;result[result.Length-1].endMetres=finish;return result;
        }
        public static RaceRoutePublication Publish(RaceRouteDefinition source,RaceRoutePlan reviewed,string path)
        {
            if(reviewed==null||!reviewed.Valid||reviewed.fingerprint!=RaceRouteCompiler.Fingerprint(source))throw new ArgumentException("Route is invalid or changed since review. Validate again.");
            if(!path.StartsWith("Assets/",StringComparison.Ordinal)||!path.EndsWith(".asset",StringComparison.Ordinal))throw new ArgumentException("Publish under Assets.");
            var fresh=RaceRouteCompiler.Build(source);if(!fresh.Valid)throw new ArgumentException(string.Join("\n",fresh.issues));
            var publication=ScriptableObject.CreateInstance<RaceRoutePublication>();path=AssetDatabase.GenerateUniqueAssetPath(path);
            try{publication.Initialize(source.id,reviewed.fingerprint,source.network,fresh.legs,fresh.grid.ToArray(),fresh.gridRotations.ToArray(),new Vector3(source.vehicleWidth,source.vehicleHeight,source.vehicleLength));AssetDatabase.CreateAsset(publication,path);AssetDatabase.SaveAssetIfDirty(publication);Edit(source,"Publish race route",()=>source.published=publication);return publication;}
            catch{if(AssetDatabase.GetAssetPath(publication)==path)AssetDatabase.DeleteAsset(path);else UnityEngine.Object.DestroyImmediate(publication);throw;}
        }
        public static FreeRoamEventDefinition PlaceEvent(RaceRouteDefinition source,RaceRoutePlan plan)
        {
            if(source==null||source.published==null)throw new ArgumentException("Publish the current route first.");
            if(source.published.Fingerprint!=RaceRouteCompiler.Fingerprint(source))throw new ArgumentException("Publication is stale after source/dependency changes. Validate and publish again.");
            if(plan==null||!plan.Valid||plan.fingerprint!=source.published.Fingerprint)throw new ArgumentException("Validate the published route before placement: "+(plan==null?"no plan":string.Join("; ",plan.issues)));
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
            var go=new GameObject(source.displayName+" — race entry");
            var definition=go.AddComponent<FreeRoamEventDefinition>();definition.Configure(source.id,source.displayName,source.policy,source.published.Checkpoints,source.laps,source.timeLimit,0,source.targetSpeedKph);definition.BindRoute(source.published);
            var first=source.legs[0].paths[0].spans[0];var pose=RaceRouteCompiler.Lane(source,first.laneId).Sample(first.startMetres);
            go.transform.SetPositionAndRotation(plan.grid[0],Quaternion.LookRotation(pose.forward,pose.up));Undo.RegisterCreatedObjectUndo(go,"Place race event");Undo.CollapseUndoOperations(group);EditorUtility.SetDirty(definition);EditorSceneManager.MarkSceneDirty(go.scene);return definition;
        }
        public static RacingRouteSpan[] LineSpans(RaceRouteDefinition source, int[] branches = null)
        {
            var spans = new List<RacingRouteSpan>();
            for (int lap = 0; lap < source.laps; lap++)
                for (int i = 0; i < source.legs.Length; i++)
                    foreach (var span in source.legs[i].paths[branches == null ? 0 : branches[i]].spans)
                    {
                        var copy = JsonUtility.FromJson<RacingRouteSpan>(JsonUtility.ToJson(span));
                        using var hash = System.Security.Cryptography.SHA256.Create();
                        copy.id = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(span.id + ".lap." + lap))).Replace("-", "").Substring(0,32).ToLowerInvariant();
                        spans.Add(copy);
                    }
            // The driver must continue beyond the finish plane before its follower stops.
            var last = spans.Last(); var lane = RaceRouteCompiler.Lane(source, last.laneId);
            float end = RaceRouteCompiler.End(last, lane);
            if (end + source.finishRunoff <= lane.Length) last.endMetres = end + source.finishRunoff;
            else if (source.policy == FreeRoamEventKind.Circuit)
            {
                var first = spans[0]; var firstLane = RaceRouteCompiler.Lane(source, first.laneId);
                if (first.startMetres + source.finishRunoff > firstLane.Length) throw new ArgumentException("AI finish continuation exceeds the first lane. Author a longer runoff corridor.");
                spans.Add(new RacingRouteSpan { laneId = first.laneId, startMetres = first.startMetres, endMetres = first.startMetres + source.finishRunoff });
            }
            return spans.ToArray();
        }
        public static RacingLineRoute ExportLineRoute(RaceRouteDefinition source,int[] branches,string path)
        {
            var plan=RaceRouteCompiler.Build(source);if(!plan.Valid)throw new ArgumentException("Validate the route before exporting a line corridor.");
            var route=ScriptableObject.CreateInstance<RacingLineRoute>();route.network=source.network;route.closed=false;
            route.spans=LineSpans(source, branches);
            AssetDatabase.CreateAsset(route,AssetDatabase.GenerateUniqueAssetPath(path));return route;
        }
        public static List<RaceRouteIssue> CheckLoadedGrid(RaceRouteDefinition source,RaceRoutePlan plan)
        {
            var issues=new List<RaceRouteIssue>();var span=source.legs[0].paths[0].spans[0];var lane=RaceRouteCompiler.Lane(source,span.laneId);var pose=lane.Sample(span.startMetres);
            foreach(var position in plan.grid)
                if(Physics.CheckBox(position+pose.up*(source.vehicleHeight/2+.15f),new Vector3(source.vehicleWidth/2,source.vehicleHeight/2,source.vehicleLength/2),Quaternion.LookRotation(pose.forward,pose.up),~0,QueryTriggerInteraction.Ignore))
                    issues.Add(new RaceRouteIssue("ROUTE_GRID_OBSTACLE",source.id,"Loaded-scene collision overlaps a proposed grid slot."));
            return issues;
        }
    }
}
