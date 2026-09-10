using System;
using System.Collections.Generic;
using System.Linq;
using NfsMwRemaster.Driving;
using NfsMwRemaster.Driving.Editor.WorldValidation;
using UnityEditor;

namespace NfsMwRemaster.Driving.Editor.Missions
{
    /// <summary>
    /// Adapts the mission editor's authoritative semantic validator to the
    /// shared dashboard. The dashboard owns presentation; this module owns
    /// mission graph meaning and keeps node IDs as affected identities.
    /// </summary>
    public sealed class WorldValidationMissionAuthoringRule : WorldValidationRuleBase
    {
        public WorldValidationMissionAuthoringRule()
            : base("missions.graph.authoring", "Mission graph authoring diagnostics", "Driving/Missions",
                WorldValidationCategory.EventsMissionsCareer, WorldValidationCost.Standard,
                Enum.GetValues(typeof(WorldValidationScopeKind)).Cast<WorldValidationScopeKind>(),
                new[] { "MissionDefinitionAsset", "MissionGraphEditorModel", "stable objective IDs" },
                new[] { "missions.career.compile" }) { }

        public override void Evaluate(WorldValidationContext context, WorldValidationResultSink results)
        {
            int count = 0;
            foreach (MissionDefinitionAsset asset in context.FindAssets<MissionDefinitionAsset>().Distinct())
            {
                context.ThrowIfCancellationRequested();
                count++;
                List<MissionGraphDiagnostic> diagnostics;
                try
                {
                    MissionGraph graph = asset.Compile();
                    if (graph == null) throw new InvalidOperationException("Mission compiler returned no graph.");
                    diagnostics = MissionGraphEditorModel.Validate(graph.Definition());
                }
                catch (Exception exception)
                {
                    var result = WorldValidationResult.Failed(Descriptor, "MISSION_GRAPH_AUTHORING_EXCEPTION",
                        "Mission authoring validation failed", ExceptionMessage(exception), WorldValidationSeverity.Blocker);
                    result.SetTarget(asset);
                    result.AddEvidence(WorldValidationEvidenceKind.Text, "Authoritative compiler exception", exception.ToString(),
                        asset: AssetDatabase.GetAssetPath(asset));
                    results.Add(result);
                    continue;
                }

                bool actionable = false;
                bool emitted = false;
                foreach (MissionGraphDiagnostic diagnostic in (diagnostics ?? new List<MissionGraphDiagnostic>())
                             .Where(value => value != null)
                             .OrderBy(value => value.code, StringComparer.Ordinal)
                             .ThenBy(value => value.nodeId, StringComparer.Ordinal)
                             .ThenBy(value => value.property, StringComparer.Ordinal))
                {
                    context.ThrowIfCancellationRequested();
                    if (diagnostic.severity == MissionGraphDiagnosticSeverity.Info && !context.Request.includeInfo) continue;

                    WorldValidationStatus status;
                    WorldValidationSeverity severity;
                    string title;
                    switch (diagnostic.severity)
                    {
                        case MissionGraphDiagnosticSeverity.Error:
                            status = WorldValidationStatus.Failed;
                            severity = WorldValidationSeverity.Blocker;
                            title = "Mission graph error";
                            actionable = true;
                            break;
                        case MissionGraphDiagnosticSeverity.Warning:
                            status = WorldValidationStatus.Warning;
                            severity = WorldValidationSeverity.Warning;
                            title = "Mission graph review";
                            actionable = true;
                            break;
                        default:
                            status = WorldValidationStatus.Passed;
                            severity = WorldValidationSeverity.Info;
                            title = "Mission graph information";
                            break;
                    }

                    var result = WorldValidationResult.Create(Descriptor, status, severity,
                        string.IsNullOrEmpty(diagnostic.code) ? "MISSION_AUTHORING" : diagnostic.code,
                        title, diagnostic.message);
                    result.SetTarget(asset);
                    result.location = string.IsNullOrEmpty(diagnostic.property)
                        ? diagnostic.nodeId
                        : diagnostic.nodeId + "." + diagnostic.property;
                    if (!string.IsNullOrEmpty(diagnostic.nodeId)) result.AddAffectedId(diagnostic.nodeId);
                    result.AddEvidence(WorldValidationEvidenceKind.Text, "Mission editor diagnostic", diagnostic.message,
                        diagnostic.nodeId, AssetDatabase.GetAssetPath(asset), string.Empty, result.globalObjectId);
                    results.Add(result);
                    emitted = true;
                }

                if (!actionable && !emitted)
                    PassAsset(results, Descriptor, asset, "Mission graph has no diagnostics at the selected information level.");
            }

            if (count == 0)
                NotEvaluated(results, Descriptor, "MISSION_AUTHORING_NO_INPUT",
                    "No MissionDefinitionAsset inputs were included in this scope.");
        }
    }

    [InitializeOnLoad]
    internal static class WorldValidationMissionRegistration
    {
        static WorldValidationMissionRegistration()
        {
            WorldValidationRuleRegistry.Register(new WorldValidationMissionAuthoringRule());
        }
    }
}
