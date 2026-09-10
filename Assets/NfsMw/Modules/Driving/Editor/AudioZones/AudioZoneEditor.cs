#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    [CustomEditor(typeof(AudioZone))]
    public sealed class AudioZoneEditor : UnityEditor.Editor
    {
        private SerializedProperty stableId;
        private SerializedProperty profile;
        private SerializedProperty shape;
        private SerializedProperty center;
        private SerializedProperty size;
        private SerializedProperty radius;
        private SerializedProperty height;
        private SerializedProperty convexMesh;
        private SerializedProperty convexCollider;
        private SerializedProperty enabledForRuntime;
        private SerializedProperty allowOutsideBlend;
        private SerializedProperty authoringNotes;

        private AudioZone Zone => (AudioZone)target;

        private void OnEnable()
        {
            stableId = serializedObject.FindProperty("stableId");
            profile = serializedObject.FindProperty("profile");
            shape = serializedObject.FindProperty("shape");
            center = serializedObject.FindProperty("center");
            size = serializedObject.FindProperty("size");
            radius = serializedObject.FindProperty("radius");
            height = serializedObject.FindProperty("height");
            convexMesh = serializedObject.FindProperty("convexMesh");
            convexCollider = serializedObject.FindProperty("convexCollider");
            enabledForRuntime = serializedObject.FindProperty("enabledForRuntime");
            allowOutsideBlend = serializedObject.FindProperty("allowOutsideBlend");
            authoringNotes = serializedObject.FindProperty("authoringNotes");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("Authored acoustic volume. Membership, mix arbitration and ambience are owned by AudioZoneWorld; this component only stores source geometry and profile intent.", MessageType.Info);
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(stableId, new GUIContent("Stable ID"));
            EditorGUILayout.PropertyField(profile);
            EditorGUILayout.PropertyField(shape);
            EditorGUILayout.PropertyField(center);
            switch ((AudioZoneShape)shape.enumValueIndex)
            {
                case AudioZoneShape.Box: EditorGUILayout.PropertyField(size, new GUIContent("Core size (m)")); break;
                case AudioZoneShape.Sphere: EditorGUILayout.PropertyField(radius, new GUIContent("Core radius (m)")); break;
                case AudioZoneShape.Capsule:
                    EditorGUILayout.PropertyField(radius, new GUIContent("Core radius (m)"));
                    EditorGUILayout.PropertyField(height, new GUIContent("Core height (m)"));
                    break;
                case AudioZoneShape.Convex:
                    EditorGUILayout.PropertyField(convexMesh);
                    EditorGUILayout.PropertyField(convexCollider);
                    break;
            }
            EditorGUILayout.PropertyField(enabledForRuntime, new GUIContent("Enabled for runtime"));
            EditorGUILayout.PropertyField(allowOutsideBlend, new GUIContent("Blend outside core"));
            EditorGUILayout.PropertyField(authoringNotes);
            if (serializedObject.ApplyModifiedProperties()) SceneView.RepaintAll();

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Audio Zone Editor")) AudioZoneEditorWindow.Open(Zone);
                if (GUILayout.Button("Validate")) AudioZoneEditorWindow.Open(Zone).RunValidation();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Focus volume")) Focus();
                if (GUILayout.Button("Create portal from selection")) CreatePortalToSelection();
            }
            if (Zone.Profile == null) EditorGUILayout.HelpBox("Assign a profile before runtime evaluation. Use the Audio Zone Editor to create one with stable IDs and defaults.", MessageType.Warning);
        }

        private void OnSceneGUI()
        {
            if (Zone == null) return;
            DrawVolume(Zone, true);
            Vector3 worldCenter = Zone.transform.TransformPoint(Zone.Center);
            float handleSize = HandleUtility.GetHandleSize(worldCenter);
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(worldCenter, Zone.transform.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(Zone, "Move audio zone core");
                Zone.SetCenter(Zone.transform.InverseTransformPoint(moved));
                EditorUtility.SetDirty(Zone);
            }
            switch (Zone.Shape)
            {
                case AudioZoneShape.Box:
                    EditorGUI.BeginChangeCheck();
                    Vector3 newSize = Handles.ScaleHandle(Zone.Size, worldCenter, Zone.transform.rotation, handleSize);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(Zone, "Resize audio zone box");
                        Zone.SetSize(newSize);
                        EditorUtility.SetDirty(Zone);
                    }
                    break;
                case AudioZoneShape.Sphere:
                    EditorGUI.BeginChangeCheck();
                    float newRadius = Handles.RadiusHandle(Zone.transform.rotation, worldCenter, Zone.Radius);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(Zone, "Resize audio zone sphere");
                        Zone.SetRadius(newRadius);
                        EditorUtility.SetDirty(Zone);
                    }
                    break;
                case AudioZoneShape.Capsule:
                    EditorGUI.BeginChangeCheck();
                    float capsuleRadius = Handles.RadiusHandle(Zone.transform.rotation, worldCenter, Zone.Radius);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(Zone, "Resize audio zone capsule radius");
                        Zone.SetRadius(capsuleRadius);
                        EditorUtility.SetDirty(Zone);
                    }
                    Vector3 top = worldCenter + Zone.transform.up * Zone.Height * 0.5f;
                    EditorGUI.BeginChangeCheck();
                    float capsuleHeight = Handles.ScaleValueHandle(Zone.Height, top, Zone.transform.rotation,
                        handleSize, Handles.CubeHandleCap, 0.25f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(Zone, "Resize audio zone capsule height");
                        Zone.SetHeight(capsuleHeight);
                        EditorUtility.SetDirty(Zone);
                    }
                    break;
            }
            SceneView.RepaintAll();
        }

        internal static void DrawVolume(AudioZone zone, bool selected)
        {
            Color color = CategoryColor(zone.Profile != null ? zone.Profile.Category : AudioZoneCategory.Custom);
            color.a = selected ? 0.95f : 0.45f;
            Color previous = Handles.color;
            Matrix4x4 previousMatrix = Handles.matrix;
            Handles.color = color;
            Handles.matrix = zone.transform.localToWorldMatrix;
            Vector3 localCenter = zone.Center;
            switch (zone.Shape)
            {
                case AudioZoneShape.Sphere:
                    Handles.DrawWireDisc(localCenter, Vector3.up, zone.Radius);
                    Handles.DrawWireDisc(localCenter, Vector3.right, zone.Radius);
                    Handles.DrawWireDisc(localCenter, Vector3.forward, zone.Radius);
                    break;
                case AudioZoneShape.Capsule:
                    float half = Mathf.Max(0, zone.Height * 0.5f - zone.Radius);
                    Handles.DrawWireDisc(localCenter + Vector3.up * half, Vector3.up, zone.Radius);
                    Handles.DrawWireDisc(localCenter - Vector3.up * half, Vector3.up, zone.Radius);
                    Handles.DrawLine(localCenter + Vector3.up * half + Vector3.right * zone.Radius, localCenter - Vector3.up * half + Vector3.right * zone.Radius);
                    Handles.DrawLine(localCenter + Vector3.up * half - Vector3.right * zone.Radius, localCenter - Vector3.up * half - Vector3.right * zone.Radius);
                    Handles.DrawLine(localCenter + Vector3.up * half + Vector3.forward * zone.Radius, localCenter - Vector3.up * half + Vector3.forward * zone.Radius);
                    Handles.DrawLine(localCenter + Vector3.up * half - Vector3.forward * zone.Radius, localCenter - Vector3.up * half - Vector3.forward * zone.Radius);
                    break;
                default: Handles.DrawWireCube(localCenter, zone.Size); break;
            }
            Handles.matrix = previousMatrix;
            var blend = zone.BlendBounds;
            Handles.color = new Color(color.r, color.g, color.b, color.a * 0.4f);
            Handles.DrawWireCube(blend.center, blend.size);
            Handles.color = previous;
            string label = zone.name + "\n" + (zone.Profile != null ? zone.Profile.DisplayName : "Missing profile") + "\n" + zone.Shape;
            Handles.Label(zone.WorldBounds.center, label);
        }

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Pickable)]
        private static void DrawGizmo(AudioZone zone, GizmoType gizmoType) => DrawVolume(zone, false);

        private void Focus()
        {
            Selection.activeGameObject = Zone.gameObject;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
        }

        private void CreatePortalToSelection()
        {
            var other = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<AudioZone>() : null;
            if (other == null || other == Zone) { EditorUtility.DisplayDialog("Audio Zone Portal", "Select a different AudioZone after selecting this zone.", "OK"); return; }
            AudioZoneEditorModel.CreatePortal(Zone, other, (Zone.WorldBounds.center + other.WorldBounds.center) * 0.5f);
        }

        private static Color CategoryColor(AudioZoneCategory category)
        {
            switch (category)
            {
                case AudioZoneCategory.Tunnel: return new Color(0.2f, 0.65f, 1f);
                case AudioZoneCategory.Underpass: return new Color(0.25f, 0.9f, 0.85f);
                case AudioZoneCategory.Garage: return new Color(1f, 0.55f, 0.15f);
                case AudioZoneCategory.IndustrialInterior: return new Color(1f, 0.25f, 0.2f);
                case AudioZoneCategory.ParkingStructure: return new Color(0.85f, 0.35f, 1f);
                case AudioZoneCategory.EnclosedAlley: return new Color(0.95f, 0.8f, 0.2f);
                default: return new Color(0.35f, 1f, 0.45f);
            }
        }
    }
}
#endif
