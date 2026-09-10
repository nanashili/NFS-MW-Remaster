using System;
using System.Collections.Generic;

namespace NfsMwRemaster.Driving
{
    public enum CareerContentKind { Event, Milestone, Rival, Vehicle, Upgrade, District, Story, Challenge, Shop, Safehouse, Customization }
    public enum CareerAvailability { Undiscovered, Teased, Locked, Available, Completed }

    [Serializable]
    public sealed class CareerContentDefinition
    {
        public string id = string.Empty;
        public string name = string.Empty;
        public CareerContentKind kind;
        public bool tease;
        public CareerRequirementDefinition requirement = new CareerRequirementDefinition { kind = CareerRequirementKind.All };
    }

    [Serializable]
    public sealed class CareerEventGroupDefinition
    {
        public string id = string.Empty;
        public string[] eventIds = Array.Empty<string>();
    }

    /// <summary>Content DTO; compile creates an immutable snapshot and validates typed references.</summary>
    [Serializable]
    public sealed class CareerGraphDefinition
    {
        public string id = string.Empty;
        public int version = 1;
        public CareerContentDefinition[] content = Array.Empty<CareerContentDefinition>();
        public CareerEventGroupDefinition[] groups = Array.Empty<CareerEventGroupDefinition>();
    }

    public sealed class CareerContent
    {
        public string Id { get; }
        public string Name { get; }
        public CareerContentKind Kind { get; }
        public bool Tease { get; }
        public CareerRequirement Requirement { get; }
        internal CareerContent(CareerContentDefinition definition)
        {
            Id = definition.id; Name = definition.name; Kind = definition.kind;
            Tease = definition.tease; Requirement = CareerRequirement.Compile(definition.requirement);
        }
    }

    /// <summary>
    /// Immutable catalog and reverse dependency index. Consumers query the same
    /// requirements; gameplay events can invalidate only affected content.
    /// Cyclic dependency warnings are conservative, not a reachability proof.
    /// </summary>
    public sealed class CareerGraph
    {
        private readonly Dictionary<string, CareerContent> content = new Dictionary<string, CareerContent>(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<string>> groups = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        private readonly Dictionary<(CareerFactKind, string), IReadOnlyList<string>> dependents = new Dictionary<(CareerFactKind, string), IReadOnlyList<string>>();
        private static readonly IReadOnlyList<string> Empty = Array.AsReadOnly(Array.Empty<string>());
        public string Id { get; }
        public int Version { get; }
        public IReadOnlyList<CareerContent> Content { get; }

        public CareerGraph(CareerGraphDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            ValidateId(definition.id);
            if (definition.version < 1) throw new ArgumentException("Career content version must be positive.");
            if (definition.content == null || definition.groups == null) throw new ArgumentException("Null career collections.");
            if (definition.content.Length > 10000 || definition.groups.Length > 10000) throw new ArgumentException("Career exceeds content budget.");
            Id = definition.id; Version = definition.version;
            var all = new List<CareerContent>();
            foreach (var source in definition.content)
            {
                if (source == null) throw new ArgumentException("Null career content.");
                ValidateId(source.id);
                if (string.IsNullOrWhiteSpace(source.name)) throw new ArgumentException("Content requires a display name: " + source.id);
                if (!Enum.IsDefined(typeof(CareerContentKind), source.kind)) throw new ArgumentException("Unknown content kind.");
                if (content.ContainsKey(source.id)) throw new ArgumentException("Duplicate career ID: " + source.id);
                var entry = new CareerContent(source);
                content.Add(entry.Id, entry); all.Add(entry);
            }
            foreach (var group in definition.groups)
            {
                if (group == null) throw new ArgumentException("Null event group.");
                ValidateId(group.id);
                if (content.ContainsKey(group.id) || groups.ContainsKey(group.id)) throw new ArgumentException("Duplicate career ID: " + group.id);
                if (group.eventIds == null || group.eventIds.Length == 0) throw new ArgumentException("Empty event group: " + group.id);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in group.eventIds)
                {
                    ValidateId(id);
                    if (!seen.Add(id)) throw new ArgumentException("Duplicate event in group: " + id);
                    RequireKind(id, CareerContentKind.Event);
                }
                groups.Add(group.id, Array.AsReadOnly((string[])group.eventIds.Clone()));
            }
            var reverse = new Dictionary<(CareerFactKind, string), List<string>>();
            foreach (var entry in all)
            {
                entry.Requirement.VisitDependencies((fact, subject, negated) =>
                {
                    ValidateReference(fact, subject);
                    var key = (fact, subject);
                    if (!reverse.TryGetValue(key, out var ids)) reverse.Add(key, ids = new List<string>());
                    if (!ids.Contains(entry.Id)) ids.Add(entry.Id);
                });
            }
            foreach (var pair in reverse) dependents.Add(pair.Key, pair.Value.AsReadOnly());
            Content = all.AsReadOnly();
        }

        public CareerContent Get(string id)
        {
            if (id == null || !content.TryGetValue(id, out var entry)) throw new ArgumentException("Unknown career ID: " + id);
            return entry;
        }

        public IReadOnlyList<string> GetEventGroup(string id)
        {
            if (id == null || !groups.TryGetValue(id, out var members)) throw new ArgumentException("Unknown event group: " + id);
            return members;
        }

        public IReadOnlyList<string> AffectedBy(CareerFactKind fact, string subjectId = "")
            => dependents.TryGetValue((fact, subjectId ?? string.Empty), out var ids) ? ids : Empty;

        public CareerAvailability Availability(string id, ICareerFacts facts, bool discovered, bool completed)
        {
            var entry = Get(id);
            if (completed) return CareerAvailability.Completed;
            if (!discovered) return entry.Tease ? CareerAvailability.Teased : CareerAvailability.Undiscovered;
            return entry.Requirement.Evaluate(facts) ? CareerAvailability.Available : CareerAvailability.Locked;
        }

        private void ValidateReference(CareerFactKind fact, string subject)
        {
            switch (fact)
            {
                case CareerFactKind.EventGroupWins: GetEventGroup(subject); break;
                case CareerFactKind.EventCompleted:
                case CareerFactKind.EventWon: RequireKind(subject, CareerContentKind.Event); break;
                case CareerFactKind.MilestoneCompleted: RequireKind(subject, CareerContentKind.Milestone); break;
                case CareerFactKind.RivalDefeated: RequireKind(subject, CareerContentKind.Rival); break;
                case CareerFactKind.VehicleUnlocked:
                case CareerFactKind.VehicleOwned: RequireKind(subject, CareerContentKind.Vehicle); break;
                case CareerFactKind.UpgradeUnlocked: RequireKind(subject, CareerContentKind.Upgrade); break;
                case CareerFactKind.DistrictUnlocked: RequireKind(subject, CareerContentKind.District); break;
                case CareerFactKind.StoryState: RequireKind(subject, CareerContentKind.Story); break;
                case CareerFactKind.ChallengeCompleted: RequireKind(subject, CareerContentKind.Challenge); break;
            }
        }

        private void RequireKind(string id, CareerContentKind kind)
        {
            if (Get(id).Kind != kind) throw new ArgumentException("Wrong content type for " + id + "; expected " + kind);
        }

        private static void ValidateId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 160 || id != id.Trim()) throw new ArgumentException("Invalid stable career ID.");
            foreach (char character in id)
                if (!(character >= 'a' && character <= 'z') && !(character >= '0' && character <= '9')
                    && character != '.' && character != '_' && character != '-') throw new ArgumentException("Invalid career ID: " + id);
        }
    }
}
