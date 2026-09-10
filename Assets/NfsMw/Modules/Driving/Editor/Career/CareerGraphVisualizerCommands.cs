using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NfsMwRemaster.Driving;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public static class CareerGraphVisualizerCommands
    {
        private const float NodeWidth = 224f;
        private const float NodeHeight = 92f;
        private const float GroupPadding = 28f;

        public static string LayoutPath(CareerDefinitionAsset asset)
        {
            string sourcePath = asset == null ? string.Empty : AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(sourcePath))
            {
                return string.Empty;
            }

            string directory = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/') ?? "Assets";
            string file = Path.GetFileNameWithoutExtension(sourcePath);
            return directory + "/" + file + ".CareerGraphLayout.asset";
        }

        public static CareerGraphLayoutAsset LoadLayout(
            CareerGraphProjection projection,
            out bool persistent)
        {
            persistent = false;
            if (projection == null || projection.Asset == null)
            {
                return null;
            }

            string path = LayoutPath(projection.Asset);
            CareerGraphLayoutAsset layout = string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<CareerGraphLayoutAsset>(path);
            if (layout != null)
            {
                persistent = true;
                layout.Normalize();
                SyncLayout(projection, layout);
                return layout;
            }

            layout = ScriptableObject.CreateInstance<CareerGraphLayoutAsset>();
            layout.hideFlags = HideFlags.HideAndDontSave;
            layout.DefinitionGuid = projection.AssetGuid;
            layout.DefinitionId = projection.Source == null ? string.Empty : projection.Source.id;
            layout.SourceFingerprint = projection.SourceFingerprint;
            SyncLayout(projection, layout);
            return layout;
        }

        public static bool SaveLayout(
            CareerGraphProjection projection,
            CareerGraphLayoutAsset layout,
            out string failure)
        {
            failure = string.Empty;
            if (projection == null || projection.Asset == null || layout == null)
            {
                failure = "A career projection and layout are required.";
                return false;
            }

            string path = LayoutPath(projection.Asset);
            if (string.IsNullOrEmpty(path))
            {
                failure = "The career definition must be saved in the Unity project before its layout can be saved.";
                return false;
            }

            layout.Normalize();
            layout.DefinitionGuid = projection.AssetGuid;
            layout.DefinitionId = projection.Source == null ? string.Empty : projection.Source.id;
            layout.SourceFingerprint = projection.SourceFingerprint;
            if (!AssetDatabase.Contains(layout))
            {
                string uniquePath = AssetDatabase.GenerateUniqueAssetPath(path);
                AssetDatabase.CreateAsset(layout, uniquePath);
                Undo.RegisterCreatedObjectUndo(layout, "Create career graph layout");
            }
            else
            {
                Undo.RecordObject(layout, "Save career graph layout");
            }

            layout.hideFlags = HideFlags.None;
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssetIfDirty(layout);
            failure = string.Empty;
            return true;
        }

        public static void SyncLayout(CareerGraphProjection projection, CareerGraphLayoutAsset layout)
        {
            if (projection == null || layout == null)
            {
                return;
            }

            foreach (CareerGraphNodeModel node in projection.Nodes)
            {
                CareerGraphNodeLayout nodeLayout = layout.EnsureNode(node.Key, node.DefaultPosition);
                node.Position = nodeLayout.position;
            }

            var groupedNodes = projection.Nodes
                .GroupBy(node => node.TierLabel ?? "Unscoped", StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToArray();
            var activeGroupIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (IGrouping<string, CareerGraphNodeModel> grouped in groupedNodes)
            {
                string tier = string.IsNullOrEmpty(grouped.Key) ? "Unscoped" : grouped.Key;
                string groupId = TierGroupId(tier);
                CareerGraphNodeModel[] nodes = grouped.ToArray();
                Rect defaultRect = GroupBounds(nodes);
                CareerGraphGroupLayout group = layout.EnsureGroup(
                    groupId,
                    tier,
                    defaultRect,
                    nodes.Select(node => node.Key));
                if (group == null)
                {
                    continue;
                }

                group.title = tier;
                group.nodeKeys.Clear();
                group.nodeKeys.AddRange(nodes.Select(node => node.Key));
                group.rect = defaultRect;
                activeGroupIds.Add(groupId);
            }

            // Tier groups are generated metadata. Remove only stale generated
            // groups; preserve any future user-authored group records.
            layout.Groups.RemoveAll(group => group != null
                && group.id.StartsWith("tier:", StringComparison.Ordinal)
                && !activeGroupIds.Contains(group.id));
            layout.Normalize();
        }

        public static void ApplyAutoLayout(
            CareerGraphProjection projection,
            CareerGraphLayoutAsset layout)
        {
            if (projection == null || layout == null)
            {
                return;
            }

            Undo.RecordObject(layout, "Auto-layout career graph");
            var groups = projection.Nodes
                .GroupBy(node => node.TierLabel ?? string.Empty, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToArray();
            for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                CareerGraphNodeModel[] nodes = groups[groupIndex]
                    .OrderBy(node => node.ProjectedKind)
                    .ThenBy(node => node.Id, StringComparer.Ordinal)
                    .ToArray();
                for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
                {
                    CareerGraphNodeLayout nodeLayout = layout.EnsureNode(nodes[nodeIndex].Key, nodes[nodeIndex].DefaultPosition);
                    if (nodeLayout.pinned)
                    {
                        continue;
                    }

                    int column = nodeIndex % 4;
                    int row = nodeIndex / 4;
                    nodeLayout.position = new Vector2(
                        80f + column * 270f,
                        70f + groupIndex * 240f + row * 180f);
                    nodes[nodeIndex].Position = nodeLayout.position;
                }
            }
            SyncLayout(projection, layout);
            EditorUtility.SetDirty(layout);
        }

        private static string TierGroupId(string tier)
        {
            return "tier:" + (tier ?? "Unscoped");
        }

        private static Rect GroupBounds(IReadOnlyList<CareerGraphNodeModel> nodes)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return new Rect(40f, 40f, 640f, 240f);
            }

            float minX = nodes.Min(node => node.Position.x);
            float minY = nodes.Min(node => node.Position.y);
            float maxX = nodes.Max(node => node.Position.x + NodeWidth);
            float maxY = nodes.Max(node => node.Position.y + NodeHeight);
            return new Rect(
                minX - GroupPadding,
                minY - GroupPadding - 18f,
                Mathf.Max(260f, maxX - minX + GroupPadding * 2f),
                Mathf.Max(150f, maxY - minY + GroupPadding * 2f + 18f));
        }

        public static bool CreateDemoCareer(out CareerDefinitionAsset asset, out string failure)
        {
            asset = null;
            failure = string.Empty;
            try
            {
                EnsureFolder("Assets/NfsMw/Modules/Driving/Examples");
                EnsureFolder("Assets/NfsMw/Modules/Driving/Examples/Career");
                string path = AssetDatabase.GenerateUniqueAssetPath(
                    "Assets/NfsMw/Modules/Driving/Examples/Career/CareerGraphDemo.asset");
                asset = ScriptableObject.CreateInstance<CareerDefinitionAsset>();
                asset.Configure(DemoDefinition());
                AssetDatabase.CreateAsset(asset, path);
                Undo.RegisterCreatedObjectUndo(asset, "Create career graph demo");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
                return true;
            }
            catch (Exception exception)
            {
                if (asset != null && !AssetDatabase.Contains(asset))
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }
                failure = exception.Message;
                return false;
            }
        }

        public static string BuildMarkdown(
            CareerGraphProjection projection,
            CareerGraphSandbox sandbox,
            string selectedKey,
            string simulationSummary,
            CareerGraphComparison comparison)
        {
            var text = new StringBuilder();
            text.AppendLine("# Career Graph Visualizer Report");
            text.AppendLine();
            text.AppendLine("- Asset: `" + (projection?.AssetPath ?? string.Empty) + "`");
            text.AppendLine("- Career ID: `" + (projection?.Source?.id ?? string.Empty) + "`");
            text.AppendLine("- Source fingerprint: `" + (projection?.SourceFingerprint ?? string.Empty) + "`");
            text.AppendLine("- Compile status: " + (projection != null && projection.IsCompiled ? "Passed" : "Failed"));
            if (!string.IsNullOrEmpty(projection?.CompileError)) text.AppendLine("- Compile error: " + projection.CompileError);
            text.AppendLine();
            text.AppendLine("## Coverage");
            text.AppendLine();
            text.AppendLine("- Nodes: " + (projection?.Nodes.Count ?? 0));
            text.AppendLine("- Edges: " + (projection?.Edges.Count ?? 0));
            text.AppendLine("- Findings: " + (projection?.Findings.Count ?? 0));
            text.AppendLine("- External references: " + (projection?.ExternalReferences.Count ?? 0));
            text.AppendLine("- Selected node: `" + (selectedKey ?? string.Empty) + "`");
            text.AppendLine();
            text.AppendLine("## Findings");
            text.AppendLine();
            if (projection == null || projection.Findings.Count == 0)
            {
                text.AppendLine("No findings.");
            }
            else
            {
                foreach (CareerGraphFinding finding in projection.Findings)
                {
                    text.AppendLine("- **" + finding.Severity + "** `" + finding.Code + "` — "
                        + finding.Title + ": " + finding.Message);
                }
            }
            text.AppendLine();
            text.AppendLine("## Projected nodes");
            text.AppendLine();
            if (projection != null)
            {
                foreach (CareerGraphNodeModel node in projection.Nodes.OrderBy(node => node.Key, StringComparer.Ordinal))
                {
                    text.AppendLine("- `" + node.Key + "` — " + node.Label + " (" + node.DisplayKind + ")");
                }
            }
            text.AppendLine();
            text.AppendLine("## Sandbox");
            text.AppendLine();
            if (sandbox == null)
            {
                text.AppendLine("Not created. Sandbox state is synthetic and never persisted.");
            }
            else
            {
                CareerProfileData profile = sandbox.Profile;
                text.AppendLine("Synthetic revision: " + sandbox.Revision);
                text.AppendLine("Cash: " + profile.wallet.balance);
                text.AppendLine("Reputation: " + profile.economy.reputation);
                text.AppendLine("Bounty: " + profile.bounty.totalBounty);
                text.AppendLine("Completed events: " + string.Join(", ", profile.freeRoam.completedEventIds));
                text.AppendLine("Overrides: " + sandbox.Overrides.Count);
            }
            if (!string.IsNullOrWhiteSpace(simulationSummary))
            {
                text.AppendLine();
                text.AppendLine("## Economy simulation");
                text.AppendLine();
                text.AppendLine(simulationSummary);
            }
            AppendComparison(text, comparison);
            text.AppendLine();
            text.AppendLine("## Ownership and limitations");
            text.AppendLine();
            text.AppendLine("The report projects CareerDefinitionAsset requirements and matched external definitions. It does not grant rewards, mutate wallet/save state, move mission markers or replace settlement semantics.");
            return text.ToString();
        }

        public static string BuildJson(
            CareerGraphProjection projection,
            CareerGraphSandbox sandbox,
            string selectedKey,
            string simulationSummary,
            CareerGraphComparison comparison)
        {
            var payload = new CareerGraphReportPayload
            {
                schema = 1,
                assetPath = projection?.AssetPath ?? string.Empty,
                careerId = projection?.Source?.id ?? string.Empty,
                sourceFingerprint = projection?.SourceFingerprint ?? string.Empty,
                compileError = projection?.CompileError ?? string.Empty,
                selectedNode = selectedKey ?? string.Empty,
                nodes = projection == null ? Array.Empty<CareerGraphReportNode>() : projection.Nodes.Select(ToReport).ToArray(),
                edges = projection == null ? Array.Empty<CareerGraphReportEdge>() : projection.Edges.Select(ToReport).ToArray(),
                findings = projection == null ? Array.Empty<CareerGraphReportFinding>() : projection.Findings.Select(ToReport).ToArray(),
                externalReferences = projection == null ? Array.Empty<CareerGraphReportExternal>() : projection.ExternalReferences.Select(ToReport).ToArray(),
                sandbox = sandbox == null ? null : new CareerGraphReportSandbox
                {
                    revision = sandbox.Revision,
                    cash = sandbox.Profile.wallet.balance,
                    reputation = sandbox.Profile.economy.reputation,
                    bounty = sandbox.Profile.bounty.totalBounty,
                    completedEvents = sandbox.Profile.freeRoam.completedEventIds.ToArray(),
                    overrideCount = sandbox.Overrides.Count
                },
                simulationSummary = simulationSummary ?? string.Empty,
                comparison = comparison == null ? null : new CareerGraphReportComparison
                {
                    leftFingerprint = comparison.LeftFingerprint,
                    rightFingerprint = comparison.RightFingerprint,
                    added = comparison.Added.ToArray(),
                    removed = comparison.Removed.ToArray(),
                    changedRequirements = comparison.ChangedRequirements.ToArray(),
                    changedExternalLinks = comparison.ChangedExternalLinks.ToArray()
                }
            };
            return JsonUtility.ToJson(payload, true);
        }

        private static void AppendComparison(StringBuilder text, CareerGraphComparison comparison)
        {
            if (comparison == null)
            {
                return;
            }

            text.AppendLine();
            text.AppendLine("## Revision comparison");
            text.AppendLine();
            text.AppendLine("- Added: " + string.Join(", ", comparison.Added));
            text.AppendLine("- Removed: " + string.Join(", ", comparison.Removed));
            text.AppendLine("- Changed requirements: " + string.Join(", ", comparison.ChangedRequirements));
            text.AppendLine("- Changed external links: " + comparison.ChangedExternalLinks.Count);
        }

        private static CareerGraphReportNode ToReport(CareerGraphNodeModel node)
        {
            return new CareerGraphReportNode
            {
                key = node.Key,
                id = node.Id,
                label = node.Label,
                kind = node.DisplayKind,
                projectedKind = node.ProjectedKind.ToString(),
                missing = node.Missing,
                tier = node.TierLabel,
                sourcePath = node.SourcePath,
                position = node.Position
            };
        }

        private static CareerGraphReportEdge ToReport(CareerGraphEdgeModel edge)
        {
            return new CareerGraphReportEdge
            {
                key = edge.Key,
                from = edge.FromKey,
                to = edge.ToKey,
                relation = edge.Relation.ToString(),
                label = edge.Label,
                path = edge.RequirementPath,
                negative = edge.Negative
            };
        }

        private static CareerGraphReportFinding ToReport(CareerGraphFinding finding)
        {
            return new CareerGraphReportFinding
            {
                code = finding.Code,
                severity = finding.Severity.ToString(),
                title = finding.Title,
                message = finding.Message,
                sourceId = finding.SourceId,
                sourcePath = finding.SourcePath,
                nodeKey = finding.NodeKey,
                assumptionDependent = finding.AssumptionDependent
            };
        }

        private static CareerGraphReportExternal ToReport(CareerGraphExternalReference reference)
        {
            return new CareerGraphReportExternal
            {
                kind = reference.Kind,
                id = reference.Id,
                label = reference.Label,
                assetPath = reference.AssetPath,
                status = reference.Status
            };
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static CareerGraphDefinition DemoDefinition()
        {
            return new CareerGraphDefinition
            {
                id = "career.visualizer.demo",
                version = 1,
                content = new[]
                {
                    Content("event.tier1.sprint", "Rosewood Sprint", CareerContentKind.Event, All()),
                    Content("event.tier1.circuit", "Campus Circuit", CareerContentKind.Event,
                        All(Fact(CareerFactKind.EventWon, "event.tier1.sprint"))),
                    Content("event.tier1.pursuit", "Pursuit Milestone", CareerContentKind.Event,
                        All(Fact(CareerFactKind.Bounty, string.Empty, 5000))),
                    Content("milestone.tier1.bounty", "Build a reputation", CareerContentKind.Milestone,
                        All(Fact(CareerFactKind.Bounty, string.Empty, 5000))),
                    Content("rival.tier1.razor", "Razor challenge", CareerContentKind.Rival,
                        All(Any(Fact(CareerFactKind.EventGroupWins, "group.tier1", 2),
                            Fact(CareerFactKind.MilestoneCompleted, "milestone.tier1.bounty")))),
                    Content("vehicle.tier1.m3", "M3 GTR reward path", CareerContentKind.Vehicle,
                        All(Fact(CareerFactKind.RivalDefeated, "rival.tier1.razor"))),
                    Content("upgrade.tier1.junkman", "Junkman package", CareerContentKind.Upgrade,
                        All(Fact(CareerFactKind.RivalDefeated, "rival.tier1.razor"))),
                    Content("district.tier1.rosewood", "Rosewood access", CareerContentKind.District,
                        All(Fact(CareerFactKind.RivalDefeated, "rival.tier1.razor"))),
                    Content("challenge.tier1.clean", "Clean escape challenge", CareerContentKind.Challenge,
                        All(Fact(CareerFactKind.EventWon, "event.tier1.circuit")))
                },
                groups = new[]
                {
                    new CareerEventGroupDefinition
                    {
                        id = "group.tier1",
                        eventIds = new[] { "event.tier1.sprint", "event.tier1.circuit" }
                    }
                }
            };
        }

        private static CareerContentDefinition Content(
            string id,
            string name,
            CareerContentKind kind,
            CareerRequirementDefinition requirement)
        {
            return new CareerContentDefinition
            {
                id = id,
                name = name,
                kind = kind,
                requirement = requirement
            };
        }

        private static CareerRequirementDefinition All(params CareerRequirementDefinition[] children)
        {
            return new CareerRequirementDefinition
            {
                kind = CareerRequirementKind.All,
                children = children ?? Array.Empty<CareerRequirementDefinition>()
            };
        }

        private static CareerRequirementDefinition Any(params CareerRequirementDefinition[] children)
        {
            return new CareerRequirementDefinition
            {
                kind = CareerRequirementKind.Any,
                children = children ?? Array.Empty<CareerRequirementDefinition>()
            };
        }

        private static CareerRequirementDefinition Fact(
            CareerFactKind fact,
            string subject = "",
            long required = 1)
        {
            return new CareerRequirementDefinition
            {
                kind = CareerRequirementKind.Fact,
                fact = fact,
                subjectId = subject ?? string.Empty,
                required = required
            };
        }

        [Serializable]
        private sealed class CareerGraphReportPayload
        {
            public int schema;
            public string assetPath, careerId, sourceFingerprint, compileError, selectedNode, simulationSummary;
            public CareerGraphReportNode[] nodes = Array.Empty<CareerGraphReportNode>();
            public CareerGraphReportEdge[] edges = Array.Empty<CareerGraphReportEdge>();
            public CareerGraphReportFinding[] findings = Array.Empty<CareerGraphReportFinding>();
            public CareerGraphReportExternal[] externalReferences = Array.Empty<CareerGraphReportExternal>();
            public CareerGraphReportSandbox sandbox;
            public CareerGraphReportComparison comparison;
        }

        [Serializable] private sealed class CareerGraphReportNode { public string key, id, label, kind, projectedKind, tier, sourcePath; public bool missing; public Vector2 position; }
        [Serializable] private sealed class CareerGraphReportEdge { public string key, from, to, relation, label, path; public bool negative; }
        [Serializable] private sealed class CareerGraphReportFinding { public string code, severity, title, message, sourceId, sourcePath, nodeKey; public bool assumptionDependent; }
        [Serializable] private sealed class CareerGraphReportExternal { public string kind, id, label, assetPath, status; }
        [Serializable] private sealed class CareerGraphReportSandbox { public int revision, cash, bounty, overrideCount; public long reputation; public string[] completedEvents = Array.Empty<string>(); }
        [Serializable] private sealed class CareerGraphReportComparison { public string leftFingerprint, rightFingerprint; public string[] added, removed, changedRequirements, changedExternalLinks; }
    }
}
