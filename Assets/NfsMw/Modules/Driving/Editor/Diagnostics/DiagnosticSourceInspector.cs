using System;
using UnityEditor;
using UnityEngine;
namespace NfsMwRemaster.Diagnostics.Editor
{
    [CustomEditor(typeof(DiagnosticSource)),CanEditMultipleObjects]
    public sealed class DiagnosticSourceInspector:UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            using(new EditorGUI.DisabledScope(Application.isPlaying))DrawDefaultInspector();
            EditorGUILayout.HelpBox("Bind an authoritative owner. Changes are authored outside Play Mode. Use a child GameObject for each additional owner; the binding does not change gameplay state.",MessageType.Info);
            if(!Application.isPlaying && GUILayout.Button("Give binding a fresh authored identity"))
            {
                Undo.RecordObjects(targets,"New diagnostic identities");
                foreach(var value in targets){var serialized=new SerializedObject(value);serialized.FindProperty("providerId").stringValue=Guid.NewGuid().ToString("N");serialized.ApplyModifiedProperties();PrefabUtility.RecordPrefabInstancePropertyModifications(value);}
            }
            if(target is DiagnosticSource source && !DiagnosticSetup.Supported(source.Source))EditorGUILayout.HelpBox("Missing or unsupported owner. No valid-looking metric values will be fabricated.",MessageType.Warning);
        }
    }
}
