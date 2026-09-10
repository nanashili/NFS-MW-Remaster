using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using NfsMwRemaster.Driving.Editor;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class MissionObjectiveGraphEditorTests
    {
        private static MissionObjective Event(string id, string eventType, params string[] dependencies)
        {
            return new MissionObjective
            {
                id = id,
                title = id,
                kind = "event",
                eventType = eventType,
                dependencies = dependencies ?? Array.Empty<string>(),
                sequence = Array.Empty<string>(),
                warnings = Array.Empty<double>(),
                actions = Array.Empty<MissionAction>(),
                activate = MissionCondition.All(),
                required = 1,
                clock = MissionClock.Mission
            };
        }

        private static MissionDefinition Definition(params MissionObjective[] objectives)
        {
            return new MissionDefinition
            {
                id = "mission.editor.test",
                title = "Editor test mission",
                version = 1,
                objectives = objectives,
                success = MissionCondition.Done(objectives[objectives.Length - 1].id),
                checkpoints = Array.Empty<MissionCheckpoint>(),
                rewards = Array.Empty<MissionReward>()
            };
        }

        [Test]
        public void ControlLinkRejectsCycleAndAcceptsValidDependency()
        {
            var first = Event("first", "event.first");
            var second = Event("second", "event.second", "first");
            var third = Event("third", "event.third");
            var definition = Definition(first, second, third);

            Assert.That(MissionGraphEditorModel.TryAddControlLink(definition, "second", "first", out _), Is.False);
            Assert.That(first.dependencies, Is.Empty);
            Assert.That(MissionGraphEditorModel.TryAddControlLink(definition, "second", "third", out var failure), Is.True, failure);
            Assert.That(third.dependencies, Does.Contain("second"));
            Assert.That(new MissionGraph(definition), Is.Not.Null);
        }

        [Test]
        public void ClipboardPasteRemapsInternalReferencesAndLayout()
        {
            var first = Event("first", "event.first");
            var second = Event("second", "event.second", "first");
            second.success = MissionCondition.Done("first");
            var definition = Definition(first, second);
            var layout = ScriptableObject.CreateInstance<MissionGraphLayoutAsset>();
            try
            {
                layout.EnsureNode("first", new Vector2(10, 20));
                layout.EnsureNode("second", new Vector2(300, 20));
                string payload = MissionGraphEditorModel.Clipboard.Copy(definition, layout, new[] { "first", "second" });
                var pastedLayout = ScriptableObject.CreateInstance<MissionGraphLayoutAsset>();
                try
                {
                    Assert.That(MissionGraphEditorModel.Clipboard.TryPaste(definition, pastedLayout, payload, new Vector2(50, 60), out var pasted, out var failure), Is.True, failure);
                    Assert.That(pasted, Has.Length.EqualTo(2));
                    var pastedFirst = definition.objectives.Single(x => x.title == "first" && pasted.Contains(x.id));
                    var pastedSecond = definition.objectives.Single(x => x.title == "second" && pasted.Contains(x.id));
                    Assert.That(pastedSecond.dependencies, Does.Contain(pastedFirst.id));
                    Assert.That(pastedSecond.success.key, Is.EqualTo(pastedFirst.id));
                    Assert.That(pastedLayout.FindNode(pastedFirst.id).position, Is.EqualTo(new Vector2(60, 80)));
                }
                finally { UnityEngine.Object.DestroyImmediate(pastedLayout); }
            }
            finally { UnityEngine.Object.DestroyImmediate(layout); }
        }

        [Test]
        public void ValidatorReportsMissingObjectiveReferenceWithoutThrowing()
        {
            var definition = Definition(Event("only", "event.only"));
            definition.success = MissionCondition.Done("missing");
            var diagnostics = MissionGraphEditorModel.Validate(definition);
            Assert.That(diagnostics.Any(x => x.code == "MISSING_CONDITION_NODE" && x.severity == MissionGraphDiagnosticSeverity.Error), Is.True);
            Assert.That(diagnostics.Any(x => x.code == "COMPILE"), Is.True);
        }

        [Test]
        public void DuplicateMissionRemapsMissionPoliciesAndActionIds()
        {
            var first = Event("first", "event.first");
            var second = Event("second", "event.second", "first");
            second.actions = new[] { new MissionAction { id = "activate.world", kind = "active", binding = "world.one" } };
            var definition = Definition(first, second);
            definition.checkpoints = new[] { new MissionCheckpoint { id = "stage.two", when = MissionCondition.Done("second") } };
            definition.rewards = new[] { new MissionReward { id = "finish", cash = 100, when = MissionCondition.Done("second") } };

            var copy = MissionGraphEditorModel.CloneWithFreshIdentity(definition);
            var copiedSecond = copy.objectives.Single(x => x.title == "second");
            Assert.That(copy.id, Is.Not.EqualTo(definition.id));
            Assert.That(copy.success.key, Is.EqualTo(copiedSecond.id));
            Assert.That(copy.checkpoints[0].when.key, Is.EqualTo(copiedSecond.id));
            Assert.That(copy.rewards[0].when.key, Is.EqualTo(copiedSecond.id));
            Assert.That(copiedSecond.actions[0].id, Is.Not.EqualTo(second.actions[0].id));
            Assert.That(new MissionGraph(copy), Is.Not.Null);
        }

        [Test]
        public void LayoutSidecarClampsViewportAndEnumeratesSemanticEdges()
        {
            var first = Event("first", "event.first");
            var second = Event("second", "event.second", "first");
            second.success = MissionCondition.Done("first");
            var definition = Definition(first, second);
            var layout = ScriptableObject.CreateInstance<MissionGraphLayoutAsset>();
            try
            {
                layout.Zoom = 99;
                Assert.That(layout.Zoom, Is.EqualTo(2.5f));
                var edges = MissionGraphEditorModel.EnumerateEdges(definition);
                Assert.That(edges.Any(x => x.kind == MissionGraphPortKind.Control && x.sourceId == "first" && x.targetId == "second"), Is.True);
                Assert.That(edges.Any(x => x.kind == MissionGraphPortKind.Condition && x.sourceId == "first" && x.targetId == "second"), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(layout); }
        }
    }
}
