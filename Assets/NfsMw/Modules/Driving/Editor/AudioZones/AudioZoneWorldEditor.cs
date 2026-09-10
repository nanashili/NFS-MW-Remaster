#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Inspector for the runtime acoustic owner. The inspector exposes the
    /// serialized policy and budgets, while the window remains the authoring
    /// surface for zones, profiles, portals and traversal evidence.
    /// </summary>
    [CustomEditor(typeof(AudioZoneWorld))]
    public sealed class AudioZoneWorldEditor : UnityEditor.Editor
    {
        private SerializedProperty audioWorld;
        private SerializedProperty listenerPolicy;
        private SerializedProperty explicitListener;
        private SerializedProperty listenerVehicle;
        private SerializedProperty fallbackProfile;
        private SerializedProperty sampleInterval;
        private SerializedProperty maxActiveZones;
        private SerializedProperty maxAmbienceLayers;
        private SerializedProperty manageNativeReverb;
        private SerializedProperty nativeReverbZone;
        private SerializedProperty useExplicitZoneList;
        private SerializedProperty authoredZones;

        private AudioZoneWorld World => (AudioZoneWorld)target;

        private void OnEnable()
        {
            audioWorld = serializedObject.FindProperty("audioWorld");
            listenerPolicy = serializedObject.FindProperty("listenerPolicy");
            explicitListener = serializedObject.FindProperty("explicitListener");
            listenerVehicle = serializedObject.FindProperty("listenerVehicle");
            fallbackProfile = serializedObject.FindProperty("fallbackProfile");
            sampleInterval = serializedObject.FindProperty("sampleInterval");
            maxActiveZones = serializedObject.FindProperty("maxActiveZones");
            maxAmbienceLayers = serializedObject.FindProperty("maxAmbienceLayers");
            manageNativeReverb = serializedObject.FindProperty("manageNativeReverb");
            nativeReverbZone = serializedObject.FindProperty("nativeReverbZone");
            useExplicitZoneList = serializedObject.FindProperty("useExplicitZoneList");
            authoredZones = serializedObject.FindProperty("authoredZones");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox(
                "AudioZoneWorld is the single runtime owner for listener membership, environmental mix arbitration and authored ambience. It does not replace music, police sensing, user preferences or save ownership.",
                MessageType.Info);

            EditorGUILayout.LabelField("Runtime references", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(audioWorld, new GUIContent("Sensory audio world"));
            EditorGUILayout.PropertyField(listenerPolicy);
            AudioZoneListenerPolicy policy = (AudioZoneListenerPolicy)listenerPolicy.enumValueIndex;
            if (policy == AudioZoneListenerPolicy.ExplicitTransform)
                EditorGUILayout.PropertyField(explicitListener, new GUIContent("Explicit listener"));
            if (policy == AudioZoneListenerPolicy.ListenerVehicle || policy == AudioZoneListenerPolicy.VehicleThenListener)
                EditorGUILayout.PropertyField(listenerVehicle, new GUIContent("Listener vehicle"));
            EditorGUILayout.PropertyField(fallbackProfile);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Budgets and sampling", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(sampleInterval, new GUIContent("Sample interval (s)"));
            EditorGUILayout.PropertyField(maxActiveZones, new GUIContent("Max active zones"));
            EditorGUILayout.PropertyField(maxAmbienceLayers, new GUIContent("Max ambience layers"));
            EditorGUILayout.HelpBox("The active-zone and ambience limits are hard runtime admission budgets. A deterministic priority/weight/ID order decides which overlapping content survives truncation.", MessageType.None);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Zone discovery", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(useExplicitZoneList, new GUIContent("Use explicit zone list"));
            if (useExplicitZoneList.boolValue)
                EditorGUILayout.PropertyField(authoredZones, new GUIContent("Authored zones"), true);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Backend approximation", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(manageNativeReverb, new GUIContent("Manage native reverb zone"));
            if (manageNativeReverb.boolValue) EditorGUILayout.PropertyField(nativeReverbZone);

            if (serializedObject.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(World);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(World.gameObject.scene);
                World.RefreshNow();
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Audio Zone Editor")) AudioZoneEditorWindow.Open();
                if (GUILayout.Button("Validate")) AudioZoneEditorWindow.Open().RunValidation();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh catalog")) World.RefreshNow();
                if (GUILayout.Button("Evaluate now")) World.EvaluateNow();
            }

            if (Application.isPlaying)
            {
                var snapshot = World.RuntimeSnapshot;
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Runtime diagnostic", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Listener", World.ListenerPolicy + " · " + snapshot.listenerPosition.ToString("F2"));
                EditorGUILayout.LabelField("Primary", string.IsNullOrEmpty(snapshot.primaryZoneId) ? "neutral" : snapshot.primaryZoneId);
                EditorGUILayout.LabelField("Active influences", (snapshot.influences == null ? 0 : snapshot.influences.Length).ToString());
                EditorGUILayout.LabelField("Sample", snapshot.sample.ToString());
            }
        }
    }
}
#endif
