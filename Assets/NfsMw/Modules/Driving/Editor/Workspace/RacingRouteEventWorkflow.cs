using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.Workspace
{
    public static class RacingRouteEventWorkflow
    {
        public static string Unavailable(RaceRouteDefinition route)
        {
            if(!route)return "Choose a race route.";
            if(EditorApplication.isPlayingOrWillChangePlaymode)return "Return to Edit mode to create a placement.";
            if(!route.published||route.published.Fingerprint!=RaceRouteCompiler.Fingerprint(route))return "Publish the current validated route revision first.";
            if(!route.network||route.legs?.FirstOrDefault()?.paths?.FirstOrDefault()?.spans?.FirstOrDefault()==null)return "The route has no start lane.";
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(!scene.IsValid()||!scene.isLoaded)return "Load the intended placement scene first.";
            if(!string.IsNullOrEmpty(scene.path)&&!AssetDatabase.IsOpenForEdit(scene.path))return "The active scene is read-only.";
            if(UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage()!=null)return "Return to the main stage before placing a world event.";
            return "";
        }
        // Source path is supplied explicitly by the user. No existing asset is overwritten.
        public static EventPlacementSource Create(RaceRouteDefinition route,string definitionPath)
        {
            string reason=Unavailable(route);if(reason.Length>0)throw new InvalidOperationException(reason);
            if(string.IsNullOrEmpty(definitionPath)||!definitionPath.StartsWith("Assets/",StringComparison.Ordinal)||!definitionPath.EndsWith(".asset",StringComparison.Ordinal)||System.IO.File.Exists(definitionPath))
                throw new ArgumentException("Choose an unused definition path under Assets.");
            var span=route.legs[0].paths[0].spans[0];var lane=RaceRouteCompiler.Lane(route,span.laneId);
            if(lane==null)throw new InvalidOperationException("The exact start lane no longer exists. Revalidate the route.");
            var pose=lane.Sample(span.startMetres);
            var definition=ScriptableObject.CreateInstance<WorldActivityDefinition>();
            definition.displayName=route.displayName;definition.race=route;
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Create route event placement");
            bool created=false;
            try
            {
                AssetDatabase.CreateAsset(definition,definitionPath);created=true;
                var source=EventPlacementCommands.Create(definition,pose.position);
                Undo.RecordObject(source,"Bind exact route access lane");
                source.access=new ActivityAnchor{kind=ActivityAnchorKind.Lane,network=route.network,laneId=span.laneId,station=span.startMetres,sourceRevision=route.network.Fingerprint};
                source.anchor.worldEuler=Quaternion.LookRotation(pose.forward,pose.up).eulerAngles;
                EventPlacementCommands.Changed(source);AssetDatabase.SaveAssetIfDirty(definition);
                Undo.CollapseUndoOperations(group);return source;
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                if(created)AssetDatabase.DeleteAsset(definitionPath);else UnityEngine.Object.DestroyImmediate(definition);
                throw;
            }
        }
    }
}
