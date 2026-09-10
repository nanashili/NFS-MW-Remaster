#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    [CustomEditor(typeof(AudioZoneProfile))]
    public sealed class AudioZoneProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("Profiles describe acoustic intent in dB, Hz, seconds and normalized weights. They do not own user volume, music strategy or police sensing.", MessageType.Info);
            var iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.name == "m_Script") continue;
                if (iterator.name == "stableId" || iterator.name == "schema")
                    using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(iterator, true);
                else EditorGUILayout.PropertyField(iterator, true);
            }
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate profile"))
                {
                    if (((AudioZoneProfile)target).Validate(out string failure)) EditorUtility.DisplayDialog("Audio Zone Profile", "Profile is valid.", "OK");
                    else EditorUtility.DisplayDialog("Audio Zone Profile", failure, "OK");
                }
                if (GUILayout.Button("Open zone editor")) AudioZoneEditorWindow.Open((AudioZoneProfile)target);
            }
        }
    }
}
#endif
