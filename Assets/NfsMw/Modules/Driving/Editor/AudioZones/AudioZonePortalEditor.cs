#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    [CustomEditor(typeof(AudioZonePortal))]
    public sealed class AudioZonePortalEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("A portal is an authored acoustic connection, not a road or navigation link. WorldControlled is treated as open until a gameplay adapter supplies a closed state.", MessageType.Info);
            var iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.name == "m_Script") continue;
                if (iterator.name == "stableId") using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(iterator); else EditorGUILayout.PropertyField(iterator, true);
            }
            serializedObject.ApplyModifiedProperties();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Audio Zone Editor")) AudioZoneEditorWindow.Open((AudioZonePortal)target);
                if (GUILayout.Button("Validate")) AudioZoneEditorWindow.Open((AudioZonePortal)target).RunValidation();
            }
        }

        private void OnSceneGUI()
        {
            var portal = (AudioZonePortal)target;
            Vector3 from = portal.SourceZone != null ? portal.SourceZone.WorldBounds.center : portal.transform.position;
            Vector3 to = portal.TargetZone != null ? portal.TargetZone.WorldBounds.center : portal.transform.position;
            Color old = Handles.color;
            Handles.color = new Color(0.95f, 0.78f, 0.2f, 0.95f);
            Handles.DrawDottedLine(from, to, 4);
            Handles.SphereHandleCap(0, portal.transform.position, Quaternion.identity, HandleUtility.GetHandleSize(portal.transform.position) * 0.12f, EventType.Repaint);
            Handles.Label(portal.transform.position, portal.name + "\n" + portal.State + " · " + portal.Transmission.ToString("P0"));
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(portal.transform.position, portal.transform.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(portal.transform, "Move audio zone portal");
                portal.transform.position = moved;
                EditorUtility.SetDirty(portal.transform);
            }
            Handles.color = old;
        }
    }
}
#endif
