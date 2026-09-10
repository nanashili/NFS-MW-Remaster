using System;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    [CustomEditor(typeof(WorldActivityDefinition)), CanEditMultipleObjects]
    public sealed class WorldActivityDefinitionInspector : UnityEditor.Editor
    {
        private static readonly string[] Adapters = { "race", "service", "mission", "collectible", "challenge-area", "encounter-portal" };
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("Reusable content definition. Scene placements own entrance, icon and trigger geometry. Career and mission owners retain progress and rewards.", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("category"));
            var adapter = serializedObject.FindProperty("adapter"); int index = Array.IndexOf(Adapters, adapter.stringValue);
            EditorGUI.BeginChangeCheck(); int next = EditorGUILayout.Popup("Adapter", index, Adapters);
            if (EditorGUI.EndChangeCheck() && next >= 0) adapter.stringValue = Adapters[next];
            if (next < 0) EditorGUILayout.PropertyField(adapter);
            if (Array.IndexOf(Adapters, adapter.stringValue) >= 3 || index < 0) EditorGUILayout.HelpBox("Runtime owner adapter is unavailable. Authoring is supported; publication is blocked.", MessageType.Warning);
            if (adapter.stringValue == "race") EditorGUILayout.PropertyField(serializedObject.FindProperty("race"));
            if (adapter.stringValue == "mission") EditorGUILayout.PropertyField(serializedObject.FindProperty("mission"));
            if (adapter.stringValue == "service")
            { EditorGUILayout.PropertyField(serializedObject.FindProperty("serviceKind")); EditorGUILayout.PropertyField(serializedObject.FindProperty("storefront")); }
            foreach (string field in new[] { "localizationKey", "markerColor", "completionScope", "uniqueDefinition", "hideWhenLocked", "availabilityJson", "notes" }) EditorGUILayout.PropertyField(serializedObject.FindProperty(field), true);
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.SelectableLabel(((WorldActivityDefinition)target).id, GUILayout.Height(18));
            if (GUILayout.Button("Assign new definition identity (for a new copy)"))
                foreach (WorldActivityDefinition definition in targets) { Undo.RecordObject(definition, "Assign activity definition identity"); definition.id = Guid.NewGuid().ToString("N"); EditorUtility.SetDirty(definition); }
        }
    }
}
