using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class CareerProgressionTests
    {
        private sealed class Facts : ICareerFacts
        {
            public long Value;
            public long Read(CareerFactKind kind, string subjectId) => Value;
        }

        private static CareerRequirementDefinition Fact(long threshold = 10)
            => new CareerRequirementDefinition { fact = CareerFactKind.Bounty, required = threshold };

        [TestCase(9, false)]
        [TestCase(10, true)]
        [TestCase(11, true)]
        public void ThresholdAndExplanationAgree(long value, bool expected)
        {
            var requirement = CareerRequirement.Compile(Fact());
            var facts = new Facts { Value = value };
            Assert.That(requirement.Evaluate(facts), Is.EqualTo(expected));
            var explanation = requirement.Explain(facts);
            Assert.That(explanation.Satisfied, Is.EqualTo(expected));
            Assert.That(explanation.Current, Is.EqualTo(value));
            Assert.That(explanation.Required, Is.EqualTo(10));
        }

        [Test]
        public void NestedAllAnyNotRetainsLogicalExplanation()
        {
            var requirement = CareerRequirement.Compile(new CareerRequirementDefinition
            {
                kind = CareerRequirementKind.All,
                children = new[] { Fact(1), new CareerRequirementDefinition
                {
                    kind = CareerRequirementKind.Any,
                    children = new[] { Fact(100), new CareerRequirementDefinition
                    { kind = CareerRequirementKind.Not, children = new[] { Fact(20) } } }
                } }
            });
            var facts = new Facts { Value = 10 };
            Assert.That(requirement.Evaluate(facts), Is.True);
            Assert.That(requirement.Explain(facts).Children[1].Kind, Is.EqualTo(CareerRequirementKind.Any));
            Assert.That(requirement.Explain(facts).Satisfied, Is.True);
        }

        [TestCase(0, false)]
        [TestCase(7, true)]
        [TestCase(8, true)]
        [TestCase(9, false)]
        public void RankUsesSmallerIsBetterAndRejectsMissingRank(long value, bool expected)
        {
            var requirement = CareerRequirement.Compile(new CareerRequirementDefinition
            { fact = CareerFactKind.BlacklistRank, required = 8 });
            Assert.That(requirement.Evaluate(new Facts { Value = value }), Is.EqualTo(expected));
        }

        [Test]
        public void CompilationCopiesAuthoringData()
        {
            var source = Fact();
            var compiled = CareerRequirement.Compile(source);
            source.required = 100;
            Assert.That(compiled.Evaluate(new Facts { Value = 10 }), Is.True);
        }

        [Test]
        public void EmptyAllIsUnconditionalButEmptyAnyIsRejected()
        {
            Assert.That(CareerRequirement.Compile(new CareerRequirementDefinition
            { kind = CareerRequirementKind.All }).Evaluate(new Facts()), Is.True);
            Assert.Throws<ArgumentException>(() => CareerRequirement.Compile(new CareerRequirementDefinition
            { kind = CareerRequirementKind.Any }));
        }

        [Test]
        public void MalformedAndCyclicExpressionsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => CareerRequirement.Compile(Fact(-1)));
            Assert.Throws<ArgumentException>(() => CareerRequirement.Compile(new CareerRequirementDefinition
            { kind = CareerRequirementKind.Not }));
            Assert.Throws<ArgumentException>(() => CareerRequirement.Compile(new CareerRequirementDefinition
            { fact = CareerFactKind.EventWon }));
            var cycle = new CareerRequirementDefinition { kind = CareerRequirementKind.All };
            cycle.children = new[] { cycle };
            Assert.Throws<ArgumentException>(() => CareerRequirement.Compile(cycle));
        }

        [Test]
        public void DependenciesPreserveNegation()
        {
            var requirement = CareerRequirement.Compile(new CareerRequirementDefinition
            { kind = CareerRequirementKind.Not, children = new[] { Fact() } });
            var signs = new List<bool>();
            requirement.VisitDependencies((fact, id, negative) => signs.Add(negative));
            Assert.That(signs, Is.EqualTo(new[] { true }));
        }

        private static CareerGraphDefinition Catalog()
            => new CareerGraphDefinition
            {
                id = "career.test",
                content = new[]
                {
                    new CareerContentDefinition { id = "event.a", name = "A" },
                    new CareerContentDefinition { id = "event.b", name = "B", tease = true,
                        requirement = new CareerRequirementDefinition { fact = CareerFactKind.EventWon, subjectId = "event.a" } }
                }
            };

        [Test]
        public void CatalogExplainsLocksAndIndexesDownstreamChanges()
        {
            var graph = new CareerGraph(Catalog());
            Assert.That(graph.AffectedBy(CareerFactKind.EventWon, "event.a"), Is.EqualTo(new[] { "event.b" }));
            var facts = new Facts();
            Assert.That(graph.Availability("event.b", facts, false, false), Is.EqualTo(CareerAvailability.Teased));
            Assert.That(graph.Availability("event.b", facts, true, false), Is.EqualTo(CareerAvailability.Locked));
            facts.Value = 1;
            Assert.That(graph.Availability("event.b", facts, true, false), Is.EqualTo(CareerAvailability.Available));
            facts.Value = 0;
            Assert.That(graph.Availability("event.b", facts, true, true), Is.EqualTo(CareerAvailability.Completed));
        }

        [Test]
        public void CatalogRejectsDuplicateMissingAndWrongTypeReferences()
        {
            var source = Catalog();
            source.content[1].id = "event.a";
            Assert.Throws<ArgumentException>(() => new CareerGraph(source));
            source = Catalog(); source.content[1].requirement.subjectId = "event.missing";
            Assert.Throws<ArgumentException>(() => new CareerGraph(source));
            source = Catalog(); source.content[0].kind = CareerContentKind.Vehicle;
            Assert.Throws<ArgumentException>(() => new CareerGraph(source));
        }

        [Test]
        public void EventGroupsAreDistinctTypedAndImmutable()
        {
            var source = Catalog();
            source.groups = new[] { new CareerEventGroupDefinition { id = "group.a", eventIds = new[] { "event.a" } } };
            var graph = new CareerGraph(source);
            source.groups[0].eventIds[0] = "event.b";
            Assert.That(graph.GetEventGroup("group.a")[0], Is.EqualTo("event.a"));
            source.groups[0].eventIds = new[] { "event.a", "event.a" };
            Assert.Throws<ArgumentException>(() => new CareerGraph(source));
        }

        [Test]
        public void UnknownContentFailsClosed()
        {
            var graph = new CareerGraph(Catalog());
            Assert.Throws<ArgumentException>(() => graph.Availability("event.missing", new Facts(), true, false));
        }
    }
}
