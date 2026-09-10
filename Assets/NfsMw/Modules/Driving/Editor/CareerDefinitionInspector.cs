using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    [CustomEditor(typeof(CareerDefinitionAsset))]
    public sealed class CareerDefinitionInspector : UnityEditor.Editor
    {
        private string report;
        private MessageType severity;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (GUILayout.Button("Validate and inspect dependencies"))
            {
                try
                {
                    var graph = ((CareerDefinitionAsset)target).Compile();
                    var text = new System.Text.StringBuilder();
                    text.AppendLine(graph.Id + " v" + graph.Version + ": " + graph.Content.Count + " valid typed nodes.");
                    foreach (var node in graph.Content)
                    {
                        text.AppendLine(node.Id + " (" + node.Kind + ")");
                        node.Requirement.VisitDependencies((fact, id, negated) =>
                            text.AppendLine("  <- " + (negated ? "NOT " : "") + fact + " " + id));
                    }
                    text.AppendLine("This validates structure and references, not full career reachability or balance.");
                    report = text.ToString(); severity = MessageType.Info;
                }
                catch (ArgumentException exception) { report = exception.Message; severity = MessageType.Error; }
            }
            if (!string.IsNullOrEmpty(report)) EditorGUILayout.HelpBox(report, severity);
        }
    }

    /// <summary>Invalid authored career assets must not silently ship.</summary>
    public sealed class CareerDefinitionBuildValidation : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:CareerDefinitionAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<CareerDefinitionAsset>(path);
                try { asset.Compile(); }
                catch (ArgumentException exception)
                { throw new BuildFailedException(path + ": " + exception.Message); }
            }
        }
    }
}
