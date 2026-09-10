using UnityEditor;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    [CustomEditor(typeof(VehicleSensoryProfile))]
    public sealed class BlackBoxProfileInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (UnityEngine.GUILayout.Button("Black Box Audio · Inspect source / Debug playback")) BlackBoxInspectorWindow.Open(null, (VehicleSensoryProfile)target);
            DrawDefaultInspector();
        }
    }

    [CustomEditor(typeof(BlackBoxSession))]
    public sealed class BlackBoxSessionInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (UnityEngine.GUILayout.Button("Open Black Box source inspection")) BlackBoxInspectorWindow.OpenSession((BlackBoxSession)target);
            DrawDefaultInspector();
        }
    }
}
