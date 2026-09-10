using System;
using System.Collections.Generic;

namespace NfsMwRemaster.Driving
{
    public enum CareerFactKind
    {
        Reputation, Bounty, BlacklistRank, RaceWins, MilestoneCount,
        EventCompleted, EventWon, EventGroupWins, MilestoneCompleted,
        RivalDefeated, VehicleUnlocked, VehicleOwned, UpgradeUnlocked,
        DistrictUnlocked, StoryState, ChallengeCompleted
    }

    public enum CareerRequirementKind { Fact, All, Any, Not }

    /// <summary>Authoring DTO. Compile before querying; runtime never retains this mutable tree.</summary>
    [Serializable]
    public sealed class CareerRequirementDefinition
    {
        public CareerRequirementKind kind;
        public CareerFactKind fact;
        public string subjectId = string.Empty;
        public string description = string.Empty;
        public long required = 1;
        public CareerRequirementDefinition[] children = Array.Empty<CareerRequirementDefinition>();
    }

    /// <summary>
    /// Read-only seam shared by live career snapshots and offline simulations.
    /// Rank uses smaller-is-better comparison; other facts use greater-or-equal.
    /// Boolean facts return zero or one. Group counts must count distinct wins.
    /// </summary>
    public interface ICareerFacts
    {
        long Read(CareerFactKind kind, string subjectId);
    }

    public sealed class CareerRequirementExplanation
    {
        public CareerRequirementKind Kind { get; }
        public string Description { get; }
        public long Current { get; }
        public long Required { get; }
        public bool Satisfied { get; }
        public IReadOnlyList<CareerRequirementExplanation> Children { get; }

        internal CareerRequirementExplanation(CareerRequirementKind kind, string description,
            long current, long required, bool satisfied, CareerRequirementExplanation[] children)
        {
            Kind = kind; Description = description; Current = current;
            Required = required; Satisfied = satisfied;
            Children = Array.AsReadOnly(children);
        }
    }

    /// <summary>
    /// Immutable bounded expression. Fast evaluation allocates nothing; explanation
    /// trees are built on demand for UI/debugging, not every frame.
    /// </summary>
    public sealed class CareerRequirement
    {
        private readonly CareerRequirementKind kind;
        private readonly CareerFactKind fact;
        private readonly string subjectId;
        private readonly string description;
        private readonly long required;
        private readonly CareerRequirement[] children;

        private CareerRequirement(CareerRequirementDefinition source, CareerRequirement[] compiled)
        {
            kind = source.kind; fact = source.fact; subjectId = source.subjectId ?? string.Empty;
            description = string.IsNullOrWhiteSpace(source.description)
                ? (kind == CareerRequirementKind.Fact ? fact + (subjectId.Length == 0 ? "" : ": " + subjectId) : kind.ToString())
                : source.description;
            required = source.required; children = compiled;
        }

        public static CareerRequirement Compile(CareerRequirementDefinition definition)
        {
            int count = 0;
            return CompileNode(definition, new HashSet<CareerRequirementDefinition>(), 0, ref count);
        }

        public bool Evaluate(ICareerFacts facts)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            switch (kind)
            {
                case CareerRequirementKind.Fact:
                    long value = facts.Read(fact, subjectId);
                    return fact == CareerFactKind.BlacklistRank ? value >= 1 && value <= required : value >= required;
                case CareerRequirementKind.All:
                    for (int i = 0; i < children.Length; i++) if (!children[i].Evaluate(facts)) return false;
                    return true;
                case CareerRequirementKind.Any:
                    for (int i = 0; i < children.Length; i++) if (children[i].Evaluate(facts)) return true;
                    return false;
                case CareerRequirementKind.Not: return !children[0].Evaluate(facts);
                default: throw new InvalidOperationException("Invalid compiled requirement.");
            }
        }

        public CareerRequirementExplanation Explain(ICareerFacts facts)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            var explanations = new CareerRequirementExplanation[children.Length];
            bool result = kind == CareerRequirementKind.All;
            for (int i = 0; i < children.Length; i++)
            {
                explanations[i] = children[i].Explain(facts);
                if (kind == CareerRequirementKind.All) result &= explanations[i].Satisfied;
                else if (kind == CareerRequirementKind.Any) result |= explanations[i].Satisfied;
                else result = !explanations[i].Satisfied;
            }
            long current = 0;
            if (kind == CareerRequirementKind.Fact)
            {
                current = facts.Read(fact, subjectId);
                result = fact == CareerFactKind.BlacklistRank ? current >= 1 && current <= required : current >= required;
            }
            return new CareerRequirementExplanation(kind, description, current, required, result, explanations);
        }

        public void VisitDependencies(Action<CareerFactKind, string, bool> visitor)
        {
            if (visitor == null) throw new ArgumentNullException(nameof(visitor));
            Visit(visitor, false);
        }

        private void Visit(Action<CareerFactKind, string, bool> visitor, bool negated)
        {
            if (kind == CareerRequirementKind.Fact) visitor(fact, subjectId, negated);
            else for (int i = 0; i < children.Length; i++)
                children[i].Visit(visitor, kind == CareerRequirementKind.Not ? !negated : negated);
        }

        private static CareerRequirement CompileNode(CareerRequirementDefinition node,
            HashSet<CareerRequirementDefinition> ancestors, int depth, ref int count)
        {
            if (node == null) throw new ArgumentException("Requirement cannot be null.");
            if (depth > 32 || ++count > 4096) throw new ArgumentException("Requirement exceeds depth/node budget.");
            if (!ancestors.Add(node)) throw new ArgumentException("Requirement tree contains a reference cycle.");
            if (!Enum.IsDefined(typeof(CareerRequirementKind), node.kind)) throw new ArgumentException("Unknown requirement kind.");
            var definitions = node.children ?? Array.Empty<CareerRequirementDefinition>();
            if (node.kind == CareerRequirementKind.Fact)
            {
                if (!Enum.IsDefined(typeof(CareerFactKind), node.fact)) throw new ArgumentException("Unknown career fact.");
                if (definitions.Length != 0) throw new ArgumentException("Fact must not have children.");
                if (node.required < 0) throw new ArgumentException("Negative requirement threshold.");
                bool keyed = node.fact >= CareerFactKind.EventCompleted;
                if (keyed && string.IsNullOrWhiteSpace(node.subjectId)) throw new ArgumentException("Fact requires a stable subject ID.");
                if (!keyed && !string.IsNullOrEmpty(node.subjectId)) throw new ArgumentException("Global fact must not have a subject ID.");
                if (keyed && node.subjectId != node.subjectId.Trim()) throw new ArgumentException("Subject ID contains surrounding whitespace.");
                if (keyed && node.fact != CareerFactKind.EventGroupWins && node.required != 1)
                    throw new ArgumentException("Boolean fact threshold must be one.");
                if (node.fact == CareerFactKind.BlacklistRank && (node.required < 1 || node.required > 16))
                    throw new ArgumentException("Blacklist rank must be between 1 and 16.");
            }
            else
            {
                if (node.kind == CareerRequirementKind.Any && definitions.Length == 0)
                    throw new ArgumentException("ANY requires a child.");
                if (node.kind == CareerRequirementKind.Not && definitions.Length != 1)
                    throw new ArgumentException("NOT requires exactly one child.");
            }
            var compiled = new CareerRequirement[definitions.Length];
            for (int i = 0; i < definitions.Length; i++) compiled[i] = CompileNode(definitions[i], ancestors, depth + 1, ref count);
            ancestors.Remove(node);
            return new CareerRequirement(node, compiled);
        }
    }
}
