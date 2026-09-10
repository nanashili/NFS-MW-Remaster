using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using NfsMwRemaster.Driving;
using NfsMwRemaster.Driving.Editor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class CareerGraphVisualizerTests
    {
        private readonly List<CareerDefinitionAsset> createdAssets = new List<CareerDefinitionAsset>();
        private readonly List<CareerGraphLayoutAsset> createdLayouts = new List<CareerGraphLayoutAsset>();

        [TearDown]
        public void TearDown()
        {
            foreach (CareerDefinitionAsset asset in createdAssets)
            {
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }

            foreach (CareerGraphLayoutAsset layout in createdLayouts)
            {
                if (layout != null) UnityEngine.Object.DestroyImmediate(layout);
            }
        }

        [Test]
        public void ProjectionPreservesLogicalEdgesAndTypedGroups()
        {
            CareerDefinitionAsset asset = CreateAsset(Definition());
            CareerGraphProjection projection = CareerGraphVisualizerModel.Build(asset);

            Assert.That(projection.IsCompiled, Is.True, projection.CompileError);
            Assert.That(projection.FindContent("event.start"), Is.Not.Null);
            Assert.That(projection.FindNode(CareerGraphProjection.GroupKey("group.events")), Is.Not.Null);
            Assert.That(projection.Edges.Any(edge => edge.Relation == CareerGraphRelationKind.GroupMember), Is.True);
            Assert.That(projection.Edges.Any(edge => edge.Relation == CareerGraphRelationKind.RequiresAny), Is.True);
            Assert.That(projection.Edges.Any(edge => edge.Relation == CareerGraphRelationKind.RequiresNot && edge.Negative), Is.True);
            Assert.That(projection.RequirementsByNodeKey.ContainsKey(CareerGraphProjection.ContentKey("event.gated")), Is.True);
        }

        [Test]
        public void MissingTypedReferenceRemainsVisibleAsValidationNode()
        {
            CareerDefinitionAsset asset = CreateAsset(new CareerGraphDefinition
            {
                id = "career.visualizer.invalid",
                version = 1,
                content = new[]
                {
                    Content("event.gated", CareerContentKind.Event, All(Fact(CareerFactKind.EventWon, "event.missing")))
                },
                groups = Array.Empty<CareerEventGroupDefinition>()
            });

            CareerGraphProjection projection = CareerGraphVisualizerModel.Build(asset);
            CareerGraphNodeModel missing = projection.FindNode(
                CareerGraphProjection.MissingKey(CareerFactKind.EventWon, "event.missing"));

            Assert.That(missing, Is.Not.Null);
            Assert.That(missing.Missing, Is.True);
            Assert.That(projection.Findings.Any(finding => finding.Code == "CAREER_MISSING_REFERENCE"), Is.True);
            Assert.That(projection.IsCompiled, Is.False);
        }

        [Test]
        public void SandboxChangesFactsWithoutMutatingCareerSource()
        {
            CareerDefinitionAsset asset = CreateAsset(Definition());
            CareerGraphProjection projection = CareerGraphVisualizerModel.Build(asset);
            CareerGraphSandbox sandbox = new CareerGraphSandbox(projection.Compiled);
            CareerGraphNodeModel eventNode = projection.FindContent("event.start");
            string sourceBefore = JsonUtility.ToJson(asset.Definition());

            Assert.That(sandbox.CompleteNode(eventNode, out string failure), Is.True, failure);
            Assert.That(sandbox.Facts.Read(CareerFactKind.EventWon, "event.start"), Is.EqualTo(1));
            Assert.That(sandbox.Profile.freeRoam.completedEventIds, Does.Contain("event.start"));
            Assert.That(JsonUtility.ToJson(asset.Definition()), Is.EqualTo(sourceBefore));

            Assert.That(sandbox.SetFact(CareerFactKind.Bounty, string.Empty, 5000, out failure), Is.True, failure);
            Assert.That(sandbox.Facts.Read(CareerFactKind.Bounty, string.Empty), Is.EqualTo(5000));
            Assert.That(JsonUtility.ToJson(asset.Definition()), Is.EqualTo(sourceBefore));
        }

        [Test]
        public void ComparisonDetectsChangedRequirements()
        {
            CareerDefinitionAsset leftAsset = CreateAsset(Definition());
            CareerGraphDefinition changed = Definition();
            changed.content[1].requirement = All(Fact(CareerFactKind.Bounty, string.Empty, 9000));
            CareerDefinitionAsset rightAsset = CreateAsset(changed);

            CareerGraphComparison comparison = CareerGraphVisualizerModel.Compare(
                CareerGraphVisualizerModel.Build(leftAsset),
                CareerGraphVisualizerModel.Build(rightAsset));

            Assert.That(comparison.ChangedRequirements, Does.Contain("event.gated"));
            Assert.That(comparison.LeftFingerprint, Is.Not.EqualTo(comparison.RightFingerprint));
        }

        [Test]
        public void LayoutSidecarEnsuresStableNodesAndTiers()
        {
            CareerGraphLayoutAsset layout = ScriptableObject.CreateInstance<CareerGraphLayoutAsset>();
            createdLayouts.Add(layout);

            CareerGraphNodeLayout node = layout.EnsureNode("content:event.start", new Vector2(10f, 20f));
            CareerGraphGroupLayout group = layout.EnsureGroup(
                "tier:tier.1",
                "tier.1",
                new Rect(0f, 0f, 300f, 200f),
                new[] { node.key });

            Assert.That(layout.EnsureNode(node.key, Vector2.zero), Is.SameAs(node));
            Assert.That(layout.EnsureGroup(group.id, "changed title", new Rect()), Is.SameAs(group));
            Assert.That(group.title, Is.EqualTo("changed title"));
            Assert.That(group.nodeKeys, Is.EqualTo(new[] { node.key }));
            Assert.That(layout.FindGroup("tier:tier.1"), Is.SameAs(group));

            layout.Nodes.Add(new CareerGraphNodeLayout { key = node.key });
            layout.Groups.Add(new CareerGraphGroupLayout { id = group.id });
            layout.Normalize();
            Assert.That(layout.Nodes.Count, Is.EqualTo(1));
            Assert.That(layout.Groups.Count, Is.EqualTo(1));
        }

        [Test]
        public void ReportsCarryCareerIdentityAndSchema()
        {
            CareerDefinitionAsset asset = CreateAsset(Definition());
            CareerGraphProjection projection = CareerGraphVisualizerModel.Build(asset);
            string markdown = CareerGraphVisualizerCommands.BuildMarkdown(projection, null, string.Empty, string.Empty, null);
            string json = CareerGraphVisualizerCommands.BuildJson(projection, null, string.Empty, string.Empty, null);

            Assert.That(markdown, Does.Contain("career.visualizer.test"));
            Assert.That(markdown, Does.Contain("Source fingerprint"));
            Assert.That(json, Does.Contain("\"schema\": 1"));
            Assert.That(json, Does.Contain("career.visualizer.test"));
        }

        private CareerDefinitionAsset CreateAsset(CareerGraphDefinition definition)
        {
            CareerDefinitionAsset asset = ScriptableObject.CreateInstance<CareerDefinitionAsset>();
            createdAssets.Add(asset);
            SetDefinition(asset, definition);
            return asset;
        }

        private static void SetDefinition(CareerDefinitionAsset asset, CareerGraphDefinition definition)
        {
            try
            {
                asset.Configure(definition);
            }
            catch (ArgumentException)
            {
                // Invalid fixtures are deliberately injected through the same
                // private serialized field the asset persists. The visualizer
                // must display their missing references before compile failure.
                FieldInfo field = typeof(CareerDefinitionAsset).GetField(
                    "definition",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null);
                field.SetValue(asset, definition);
            }
        }

        private static CareerGraphDefinition Definition()
        {
            return new CareerGraphDefinition
            {
                id = "career.visualizer.test",
                version = 3,
                content = new[]
                {
                    Content("event.start", CareerContentKind.Event, All()),
                    Content("event.gated", CareerContentKind.Event, All(
                        Any(
                            Fact(CareerFactKind.EventWon, "event.start"),
                            Not(Fact(CareerFactKind.Bounty, string.Empty, 1000))))),
                    Content("milestone.group", CareerContentKind.Milestone,
                        All(Fact(CareerFactKind.EventGroupWins, "group.events", 1)))
                },
                groups = new[]
                {
                    new CareerEventGroupDefinition
                    {
                        id = "group.events",
                        eventIds = new[] { "event.start", "event.gated" }
                    }
                }
            };
        }

        private static CareerContentDefinition Content(
            string id,
            CareerContentKind kind,
            CareerRequirementDefinition requirement)
        {
            return new CareerContentDefinition
            {
                id = id,
                name = id,
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

        private static CareerRequirementDefinition Not(CareerRequirementDefinition child)
        {
            return new CareerRequirementDefinition
            {
                kind = CareerRequirementKind.Not,
                children = new[] { child }
            };
        }

        private static CareerRequirementDefinition Fact(
            CareerFactKind fact,
            string subject,
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
    }
}
