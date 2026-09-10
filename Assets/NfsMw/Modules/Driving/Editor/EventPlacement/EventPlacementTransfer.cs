using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public static class EventPlacementTransfer
    {
        [Serializable] private sealed class Reference { public string property, guid, sceneId; public long localId; }
        [Serializable] private sealed class Package { public int schema = 1; public string data; public List<Reference> references = new List<Reference>(); }
        public static string Export(EventPlacementSource source)
        {
            var temporary = new GameObject("Activity export") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var copy = temporary.AddComponent<EventPlacementSource>(); EditorUtility.CopySerialized(source, copy); copy.published = null;
                var package = new Package(); var serialized = new SerializedObject(copy); var iterator = serialized.GetIterator();
                while (iterator.Next(true))
                {
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference || iterator.propertyPath.StartsWith("m_", StringComparison.Ordinal) || iterator.objectReferenceValue == null) continue;
                    var reference = new Reference { property = iterator.propertyPath };
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(iterator.objectReferenceValue, out string guid, out long localId))
                    { reference.guid = guid; reference.localId = localId; }
                    else reference.sceneId = GlobalObjectId.GetGlobalObjectIdSlow(iterator.objectReferenceValue).ToString();
                    package.references.Add(reference); iterator.objectReferenceValue = null;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo(); package.data = JsonUtility.ToJson(copy).Replace("{\"instanceID\":0}", "null");
                return JsonUtility.ToJson(package, true);
            }
            finally { UnityEngine.Object.DestroyImmediate(temporary); }
        }
        public static EventPlacementSource Import(string json)
        {
            var package = JsonUtility.FromJson<Package>(json);
            if (package == null || package.schema != 1 || string.IsNullOrEmpty(package.data) || package.references == null) throw new ArgumentException("Unsupported activity package schema.");
            if (package.data.Contains("instanceID")) throw new ArgumentException("Portable packages cannot contain transient Unity instance IDs.");
            var go = new GameObject("Imported activity");
            try
            {
                var source = go.AddComponent<EventPlacementSource>(); JsonUtility.FromJsonOverwrite(package.data, source);
                if (source.schema != 1) throw new ArgumentException("Unsupported placement schema.");
                var serialized = new SerializedObject(source);
                foreach (var reference in package.references)
                {
                    var property = serialized.FindProperty(reference.property);
                    if (property == null || property.propertyType != SerializedPropertyType.ObjectReference || reference.property.StartsWith("m_", StringComparison.Ordinal)) throw new ArgumentException("Invalid asset reference path.");
                    UnityEngine.Object target = null;
                    if (!string.IsNullOrEmpty(reference.guid))
                        target = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(reference.guid)).FirstOrDefault(asset => AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId) && localId == reference.localId);
                    else if (GlobalObjectId.TryParse(reference.sceneId, out var id)) target = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
                    if (target == null) throw new ArgumentException("Unresolved reference: " + reference.property);
                    property.objectReferenceValue = target;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo(); source.id = Guid.NewGuid().ToString("N"); source.published = null;
                if (source.definition == null) throw new ArgumentException("Missing definition.");
                if (source.definition.uniqueDefinition && UnityEngine.Object.FindObjectsByType<EventPlacementSource>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(p => p != source && p.definition == source.definition)) throw new InvalidOperationException("Unique definition already placed.");
                go.name = source.definition.displayName + " imported";
                if (EventPlacementCompiler.Resolve(source.anchor, out var pose, out _)) go.transform.SetPositionAndRotation(pose.position, pose.rotation);
                Undo.RegisterCreatedObjectUndo(go, "Import activity with fresh identity"); Selection.activeGameObject = go; return source;
            }
            catch { UnityEngine.Object.DestroyImmediate(go); throw; }
        }
    }
}
