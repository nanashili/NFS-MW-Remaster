using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    [CustomEditor(typeof(RoadNetworkAuthoring))]
    public sealed class RoadNetworkAuthoringInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update(); DrawPropertiesExcluding(serializedObject, "m_Script", "connections"); serializedObject.ApplyModifiedProperties();
            var source = (RoadNetworkAuthoring)target;
            bool current = RoadNetworkBake.IsCurrent(source);
            EditorGUILayout.HelpBox(current ? "Bake is current." : "Bake required before Play mode or a player build.", current ? MessageType.Info : MessageType.Warning);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Connect matching lane ends"))
                    try { RoadAuthoringCommands.ConnectMatchingEnds(source); }
                    catch (ArgumentException exception) { Debug.LogWarning(exception.Message, source); }
                for (int i = 0; source.connections != null && i < source.connections.Length; i++)
                {
                    var link = source.connections[i];
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(link == null ? "Invalid connection" : Label(source, link.from) + " → " + Label(source, link.to));
                    if (GUILayout.Button("Remove", GUILayout.Width(65)))
                    {
                        Undo.RecordObject(source, "Remove road connection");
                        var links = new List<RoadLaneConnection>(source.connections); links.RemoveAt(i);
                        source.connections = links.ToArray(); RoadAuthoringCommands.Changed(source);
                    }
                    EditorGUILayout.EndHorizontal();
                }
                if (GUILayout.Button("Bake network"))
                    try { Bake(source); }
                    catch (ArgumentException exception) { Debug.LogWarning(exception.Message, source); }
            }
            if (source.Baked != null && GUILayout.Button("Select baked asset")) Selection.activeObject = source.Baked;
        }
        private static string Label(RoadNetworkAuthoring network, RoadId id)
        {
            foreach (var road in network.Roads)
                if (road != null && road.Bands != null && road.Profile != null && road.Profile.bands != null)
                    for (int i = 0; i < road.Bands.Length && i < road.Profile.bands.Length; i++)
                        if (road.Bands[i] != null && road.Bands[i].id == id && road.Profile.bands[i] != null) return road.name + " / " + road.Profile.bands[i].label;
            return "Missing lane " + id;
        }
        internal static void Bake(RoadNetworkAuthoring source)
        {
            string path = source.Baked == null ? EditorUtility.SaveFilePanelInProject("Bake road network", "RoadNetwork", "asset", "Choose where to save this road network.") : AssetDatabase.GetAssetPath(source.Baked);
            if (!string.IsNullOrEmpty(path)) RoadNetworkBake.Publish(source, path);
        }
    }

    [CustomEditor(typeof(RoadNetworkAsset))]
    public sealed class RoadNetworkAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Published geometry and lanes are generated together. Edit the source roads and bake a new revision.", MessageType.Info);
            using (new EditorGUI.DisabledScope(true)) DrawDefaultInspector();
        }
    }
}
