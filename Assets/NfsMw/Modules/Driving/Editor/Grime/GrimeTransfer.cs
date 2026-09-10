using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public static class GrimeTransfer
    {
        [Serializable] private sealed class Reference { public string property, guid, sceneId; public long localId; }
        [Serializable] private sealed class Package { public int schema = 1; public string data; public List<Reference> references = new List<Reference>(); }
        public static string Export(GrimeCanvas source)
        {
            var temporary = new GameObject("Grime export") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var copy = temporary.AddComponent<GrimeCanvas>(); EditorUtility.CopySerialized(source, copy); copy.published = null; copy.generatedRoot = null;
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
        public static GrimeCanvas Import(string json)
        {
            var package = JsonUtility.FromJson<Package>(json);
            if (package == null || package.schema != 1 || string.IsNullOrEmpty(package.data) || package.references == null) throw new ArgumentException("Unsupported grime package schema.");
            if (package.data.Contains("instanceID")) throw new ArgumentException("Portable packages cannot contain transient Unity instance IDs.");
            var go = new GameObject("Imported dressing");
            try
            {
                var source = go.AddComponent<GrimeCanvas>(); JsonUtility.FromJsonOverwrite(package.data, source);
                if (source.schema != 1) throw new ArgumentException("Unsupported canvas schema.");
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
                source.generatedRoot = null;
                var layerIds = source.layers.ToDictionary(l=>l.id,l=>Guid.NewGuid().ToString("N"));
                foreach(var layer in source.layers) layer.id=layerIds[layer.id];
                foreach(var stroke in source.strokes) { stroke.id=Guid.NewGuid().ToString("N"); stroke.layerId=layerIds[stroke.layerId]; }
                foreach(var mask in source.masks) { mask.id=Guid.NewGuid().ToString("N"); if(!string.IsNullOrEmpty(mask.layerId)) mask.layerId=layerIds[mask.layerId]; }
                Undo.RegisterCreatedObjectUndo(go, "Import dressing with fresh identity"); Selection.activeGameObject = go; return source;
            }
            catch { UnityEngine.Object.DestroyImmediate(go); throw; }
        }
    }
}
