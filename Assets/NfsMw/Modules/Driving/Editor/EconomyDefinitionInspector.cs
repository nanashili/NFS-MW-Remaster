using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    [CustomEditor(typeof(EconomyDefinitionAsset))]
    public sealed class EconomyDefinitionInspector : UnityEditor.Editor
    {
        private string report;
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("Synthetic scenarios use the real settlement engine. They do not prove full career or vehicle-performance balance. No player saves are touched.", MessageType.Info);
            if (GUILayout.Button("Run configured economy scenarios"))
            {
                var asset = (EconomyDefinitionAsset)target;
                var text = new StringBuilder("Scenario,Attempts,Wins,Busts,Income,Spending,Fines,FinalCash,Seconds,BudgetExhausted\n");
                try
                {
                    var scenarios = asset.Scenarios();
                    if (scenarios.Length == 0) throw new ArgumentException("Author at least one scenario first.");
                    foreach (var scenario in scenarios)
                    {
                        var result = EconomySimulation.Run(asset.Definition(), scenario);
                        text.AppendLine($"{scenario.name.Replace(',', '_')},{result.Attempts},{result.Wins},{result.Busts},{result.Income},{result.Spending},{result.Fines},{result.FinalCash},{result.ElapsedSeconds},{result.AttemptBudgetExhausted}");
                        text.AppendLine("Wallet timeline: " + string.Join(" -> ", result.WalletTimeline));
                        text.AppendLine("Not acquired: " + string.Join(", ", result.UnaffordableOrLocked));
                    }
                    report = text.ToString();
                }
                catch (Exception exception) { report = "Simulation rejected: " + exception.Message; }
            }
            if (!string.IsNullOrEmpty(report)) EditorGUILayout.TextArea(report);
        }
    }
}
