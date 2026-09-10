using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public static class EventPlacementCommands
    {
        public static EventPlacementSource Create(WorldActivityDefinition definition, Vector3 point)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Return to Edit mode before placing an event.");
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(!scene.IsValid()||!scene.isLoaded||(!string.IsNullOrEmpty(scene.path)&&!AssetDatabase.IsOpenForEdit(scene.path)))throw new InvalidOperationException("Load an editable placement scene.");
            if (definition == null) throw new ArgumentException("Choose an activity definition.");
            if (definition.uniqueDefinition && UnityEngine.Object.FindObjectsByType<EventPlacementSource>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(p => p.definition == definition))
                throw new InvalidOperationException("This unique definition already has a placement.");
            var go = new GameObject(definition.displayName + " placement");
            Undo.RegisterCreatedObjectUndo(go, "Place activity");
            var source = Undo.AddComponent<EventPlacementSource>(go);
            source.definition = definition; source.anchor.worldPosition = point; go.transform.position = point;
            Selection.activeGameObject = go; EditorSceneManager.MarkSceneDirty(go.scene); return source;
        }
        public static EventPlacementSource Duplicate(EventPlacementSource source)
        {
            Workspace.RacingEditGuard.Require(source);
            if (source.definition != null && source.definition.uniqueDefinition) throw new InvalidOperationException("Unique definitions cannot be duplicated. Create a different definition explicitly.");
            var result = Create(source.definition, source.anchor.worldPosition);
            EditorUtility.CopySerialized(source, result);
            result.id = Guid.NewGuid().ToString("N"); result.published = null;
            result.name = source.name + " copy"; result.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            return result;
        }
        public static void AcceptRevision(EventPlacementSource source, bool access)
        {
            Workspace.RacingEditGuard.Require(source);
            Undo.RecordObject(source, "Accept reviewed anchor revision");
            var a = access ? source.access : source.anchor;
            switch (a.kind)
            {
                case ActivityAnchorKind.Lane: a.sourceRevision = a.network != null ? a.network.Fingerprint : ""; break;
                case ActivityAnchorKind.CityEntrance: a.sourceRevision = a.city != null ? a.city.Fingerprint : ""; break;
                case ActivityAnchorKind.Socket:
                    a.sourceRevision = a.socket != null ? a.socket.revision : "";
                    a.socketId = a.socket != null ? a.socket.id : ""; break;
            }
            Changed(source);
        }
        public static void MoveWorld(EventPlacementSource source, Vector3 point)
        {
            Workspace.RacingEditGuard.Require(source);
            if (source.anchor.kind != ActivityAnchorKind.World) throw new InvalidOperationException("Move the bound station or socket; world dragging cannot remap an anchor.");
            Undo.RecordObjects(new UnityEngine.Object[] { source, source.transform }, "Move activity");
            source.anchor.worldPosition = point; source.transform.position = point; Changed(source);
        }
        public static void Changed(EventPlacementSource source)
        {
            EditorUtility.SetDirty(source); PrefabUtility.RecordPrefabInstancePropertyModifications(source);
            PrefabUtility.RecordPrefabInstancePropertyModifications(source.transform);
            EditorSceneManager.MarkSceneDirty(source.gameObject.scene); SceneView.RepaintAll();
        }
        public static EventPlacementPublication Publish(EventPlacementSource source, string path)
        {
            Workspace.RacingEditGuard.Require(source);
            var plan = EventPlacementCompiler.Build(source);
            if (!plan.Valid) throw new InvalidOperationException(string.Join("\n", plan.errors));
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".asset", StringComparison.Ordinal) || AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new ArgumentException("Choose an unused .asset path inside Assets.");
            var publication = ScriptableObject.CreateInstance<EventPlacementPublication>(); publication.Initialize(plan.record);
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Publish activity revision");
            try
            {
                AssetDatabase.CreateAsset(publication, path);
                Undo.RecordObject(source, "Bind publication"); source.published = publication;
                var instance = source.GetComponent<WorldActivityInstance>() ?? Undo.AddComponent<WorldActivityInstance>(source.gameObject);
                Undo.RecordObject(instance, "Bind runtime activity"); instance.Configure(publication);
                Undo.RecordObject(source.transform, "Place runtime entrance"); source.transform.SetPositionAndRotation(plan.record.interaction, plan.record.rotation);
                var oldRace = source.GetComponent<FreeRoamEventDefinition>();
                var oldService = source.GetComponent<WorldLocation>();
                if (plan.record.adapter != "race" && oldRace != null) Undo.DestroyObjectImmediate(oldRace);
                if (plan.record.adapter != "service" && oldService != null) Undo.DestroyObjectImmediate(oldService);
                if (plan.record.adapter == "race")
                {
                    var race = oldRace ?? Undo.AddComponent<FreeRoamEventDefinition>(source.gameObject);
                    Undo.RecordObject(race, "Configure race adapter");
                    race.Configure(plan.record.completionScope == ActivityCompletionScope.Definition ? plan.record.definitionId : source.id, plan.record.label, plan.record.raceKind, plan.record.route.Checkpoints, plan.record.laps, plan.record.timeLimit, 0, plan.record.targetSpeedKph);
                    race.BindRoute(plan.record.route); PrefabUtility.RecordPrefabInstancePropertyModifications(race);
                }
                else if (plan.record.adapter == "service")
                {
                    var service = oldService ?? Undo.AddComponent<WorldLocation>(source.gameObject);
                    Undo.RecordObject(service, "Configure service adapter");
                    service.Configure(source.id, plan.record.label, plan.record.serviceKind, plan.record.storefront);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(service);
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
                Changed(source); AssetDatabase.SaveAssets(); Undo.CollapseUndoOperations(group); return publication;
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                // Only the new file created by this transaction is removed; existing revisions are retained.
                if (AssetDatabase.LoadMainAssetAtPath(path) == publication) AssetDatabase.DeleteAsset(path);
                else UnityEngine.Object.DestroyImmediate(publication);
                throw;
            }
        }
    }
}
