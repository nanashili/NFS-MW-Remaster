using System;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    [CustomEditor(typeof(MissionDefinitionAsset))]
    public sealed class MissionDefinitionInspector : UnityEditor.Editor
    {
        private string report = "";
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (GUILayout.Button("Validate mission graph"))
            {
                try { var graph = ((MissionDefinitionAsset)target).Compile(); report = graph.Id + ": valid; " + graph.Definition().objectives.Length + " nodes"; }
                catch (Exception e) { report = e.Message; }
            }
            EditorGUILayout.HelpBox(report, MessageType.Info);
        }
    }
    public sealed class MissionGraphInspector : EditorWindow
    {
        private MissionHost host;
        private FreeRoamSession session;
        private MissionDefinitionAsset asset;
        private Vector2 scroll;
        [MenuItem("Tools/Driving/Mission Debugger")]
        public static void Open() => GetWindow<MissionGraphInspector>("Mission Graph");
        private void OnInspectorUpdate() { if (Application.isPlaying) Repaint(); }
        private void OnGUI()
        {
            host = (MissionHost)EditorGUILayout.ObjectField("Mission host", host, typeof(MissionHost), true);
            session = (FreeRoamSession)EditorGUILayout.ObjectField("Free-roam session", session, typeof(FreeRoamSession), true);
            asset = (MissionDefinitionAsset)EditorGUILayout.ObjectField("Definition (offline)", asset, typeof(MissionDefinitionAsset), false);
            var runtime = host != null ? host.Runtime : session?.EventProgress?.Runtime;
            MissionDefinition definition = null;
            try { definition = runtime != null ? runtime.Definition : asset?.Compile().Definition(); }
            catch (Exception e) { EditorGUILayout.HelpBox(e.Message, MessageType.Error); }
            if (definition == null) { EditorGUILayout.HelpBox("Select a running host/session or authored graph. Node rows show the dependency graph; conditions are evaluated on demand.", MessageType.Info); return; }
            EditorGUILayout.LabelField(definition.id + " v" + definition.version + " — " + (runtime == null ? "Offline" : runtime.State.ToString()), EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var node in definition.objectives)
            {
                var state = runtime?.Objective(node.id);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(node.id + " [" + (state?.state.ToString() ?? "Authored") + "]" + (node.optional ? " OPTIONAL" : ""), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(string.Join(" + ", node.dependencies) + " → " + node.id, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Parent: " + node.parent + "   Branch: " + node.branchGroup + "/" + node.branchChoice);
                EditorGUILayout.LabelField("Type: " + node.kind + "   Event: " + node.eventType + "   Target: " + node.target);
                if (state != null)
                {
                    EditorGUILayout.LabelField("Progress: " + state.progress + "/" + node.required + "   Time: " + state.elapsed.ToString("F2") + "/" + node.duration);
                    EditorGUILayout.LabelField("Activation\n" + runtime.Explain(node.activate), EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("Success\n" + runtime.Explain(node.success), EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("Failure\n" + runtime.Explain(node.failure), EditorStyles.wordWrappedLabel);
                }
                EditorGUILayout.EndVertical();
            }
            if (runtime != null) foreach (string line in runtime.Timeline) EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
        }
        [MenuItem("Assets/Create/Driving/Timed Destination Example")]
        private static void Example()
        {
            var definition = new MissionDefinition { id = "mission.delivery.destination", title = "Reach destination within 90 seconds",
                objectives = new[] { new MissionObjective { id = "destination", title = "Reach destination", eventType = "area.entered", target = "area.destination", marker = "area.destination", duration = 90 } },
                success = MissionCondition.Done("destination"), rewards = new[] { new MissionReward { id = "completion", cash = 1500 } } };
            var asset = CreateInstance<MissionDefinitionAsset>(); asset.Configure(definition);
            ProjectWindowUtil.CreateAsset(asset, "TimedDestination.asset");
        }
    }
}
