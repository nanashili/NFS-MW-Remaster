using System;
using System.Collections.Generic;

namespace NfsMwRemaster.Driving
{
    /// <summary>Compiled private definition; the public authoring object is never retained.</summary>
    public sealed class MissionGraph
    {
        internal readonly MissionDefinition Data;
        internal readonly Dictionary<string, MissionObjective> Nodes = new Dictionary<string, MissionObjective>(StringComparer.Ordinal);
        internal readonly Dictionary<MissionCondition, CareerRequirement> Requirements = new Dictionary<MissionCondition, CareerRequirement>();
        internal readonly Dictionary<string, IMissionPrimitive> Primitives;
        internal readonly CareerRequirement Availability;
        public string Id => Data.id;
        public int Version => Data.version;
        public string ContentHash { get; }
        public MissionDefinition Definition() => MissionData.Copy(Data);

        public MissionGraph(MissionDefinition definition, IDictionary<string, IMissionPrimitive> primitives = null)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            // Validate before copying: cyclic input trees must not overflow the serializer.
            int budget = 0;
            CheckTree(definition.success, new HashSet<MissionCondition>(), 0, ref budget);
            CheckTree(definition.failure, new HashSet<MissionCondition>(), 0, ref budget);
            if (definition.objectives == null || definition.objectives.Length == 0 || definition.objectives.Length > 2048) throw new ArgumentException("Mission needs 1–2048 objectives.");
            foreach (var node in definition.objectives)
            {
                if (node == null) throw new ArgumentException("Null objective.");
                CheckTree(node.activate, new HashSet<MissionCondition>(), 0, ref budget);
                CheckTree(node.success, new HashSet<MissionCondition>(), 0, ref budget);
                CheckTree(node.failure, new HashSet<MissionCondition>(), 0, ref budget);
            }
            if (definition.rewards == null || definition.checkpoints == null) throw new ArgumentException("Missing mission collections.");
            foreach (var reward in definition.rewards) { if (reward == null) throw new ArgumentException("Null reward."); CheckTree(reward.when, new HashSet<MissionCondition>(), 0, ref budget); }
            foreach (var checkpoint in definition.checkpoints) { if (checkpoint == null) throw new ArgumentException("Null checkpoint."); CheckTree(checkpoint.when, new HashSet<MissionCondition>(), 0, ref budget); }
            Data = MissionData.Copy(definition);
            Primitives = primitives == null ? new Dictionary<string, IMissionPrimitive>() : new Dictionary<string, IMissionPrimitive>(primitives);
            MissionData.Id(Data.id);
            if (Data.version < 1 || Data.success == null) throw new ArgumentException(Data.id + ": version and explicit success condition required.");
            Availability = Data.availability == null ? null : CareerRequirement.Compile(Data.availability);
            Array.Sort(Data.objectives, (a, b) => string.CompareOrdinal(a.id, b.id));
            var actions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in Data.objectives)
            {
                MissionData.Id(node.id);
                if (Nodes.ContainsKey(node.id)) throw Error(node.id, "duplicate objective ID");
                Nodes.Add(node.id, node);
                if (!MissionData.Finite(node.required) || node.required <= 0 || !MissionData.Finite(node.duration) || node.duration < 0
                    || !Enum.IsDefined(typeof(MissionClock), node.clock) || node.dependencies == null || node.sequence == null || node.actions == null || node.warnings == null)
                    throw Error(node.id, "invalid bounds/collections");
                if (node.kind != "event" && node.kind != "sequence" && node.kind != "condition" && node.kind != "hold" && node.kind != "timer" && node.kind != "deadline")
                { if (!Primitives.TryGetValue(node.kind, out var custom)) throw Error(node.id, "unregistered primitive " + node.kind); custom.Validate(MissionData.Copy(node)); }
                if (node.kind == "event" || node.kind == "sequence" || Primitives.ContainsKey(node.kind)) MissionData.Id(node.eventType);
                if (node.kind == "sequence" && (node.sequence.Length == 0 || node.sequence.Length > 100000)) throw Error(node.id, "sequence must have 1–100000 checkpoints");
                foreach (string target in node.sequence) MissionData.Id(target);
                if ((node.kind == "hold" || node.kind == "condition") && node.success == null) throw Error(node.id, "predicate required");
                if ((node.kind == "timer" || node.kind == "hold" || node.kind == "deadline") && node.duration <= 0) throw Error(node.id, "positive duration required");
                if (!string.IsNullOrEmpty(node.branchGroup)) { MissionData.Id(node.branchGroup); MissionData.Id(node.branchChoice); }
                else if (!string.IsNullOrEmpty(node.branchChoice)) throw Error(node.id, "branch choice without group");
                foreach (double warning in node.warnings) if (!MissionData.Finite(warning) || warning < 0 || warning > node.duration) throw Error(node.id, "invalid timer warning");
                foreach (var action in node.actions)
                {
                    if (action == null) throw Error(node.id, "null action");
                    MissionData.Id(action.id); MissionData.Id(action.kind); MissionData.Id(action.binding);
                    if (!actions.Add(action.id)) throw Error(node.id, "duplicate action " + action.id);
                }
            }
            foreach (var node in Data.objectives)
            {
                var seen = new HashSet<string>();
                foreach (string dependency in node.dependencies) if (!Nodes.ContainsKey(dependency) || !seen.Add(dependency)) throw Error(node.id, "missing/duplicate dependency " + dependency);
                if (!string.IsNullOrEmpty(node.parent) && !Nodes.ContainsKey(node.parent)) throw Error(node.id, "missing parent " + node.parent);
                ValidateCondition(node.activate); ValidateCondition(node.success); ValidateCondition(node.failure);
                Visit(node.id, new HashSet<string>(), new HashSet<string>(), false);
                Visit(node.id, new HashSet<string>(), new HashSet<string>(), true);
            }
            ValidateCondition(Data.success); ValidateCondition(Data.failure);
            var ids = new HashSet<string>();
            foreach (var checkpoint in Data.checkpoints) { MissionData.Id(checkpoint.id); if (!ids.Add(checkpoint.id)) throw Error(checkpoint.id, "duplicate checkpoint"); ValidateCondition(checkpoint.when); }
            ids.Clear();
            foreach (var reward in Data.rewards)
            {
                MissionData.Id(reward.id);
                if (!ids.Add(reward.id) || reward.cash < 0 || reward.reputation < 0 || reward.grants == null) throw Error(reward.id, "invalid reward");
                foreach (var grant in reward.grants)
                { if (grant == null || !Enum.IsDefined(typeof(EconomyItemKind), grant.kind)) throw Error(reward.id, "invalid grant"); MissionData.Id(grant.itemId); }
                ValidateCondition(reward.when);
            }
            // Canonicalize unordered authoring collections; sequence order remains meaningful.
            foreach (var node in Data.objectives)
            {
                Array.Sort(node.dependencies, StringComparer.Ordinal);
                Array.Sort(node.actions, (a, b) => string.CompareOrdinal(a.id, b.id));
                Array.Sort(node.warnings);
            }
            Array.Sort(Data.rewards, (a, b) => string.CompareOrdinal(a.id, b.id));
            Array.Sort(Data.checkpoints, (a, b) => string.CompareOrdinal(a.id, b.id));
            using var hash = System.Security.Cryptography.SHA256.Create();
            ContentHash = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(Data)))).Replace("-", "");
        }
        private ArgumentException Error(string node, string detail) => new ArgumentException(Data.id + " / " + node + ": " + detail);
        private void Visit(string id, HashSet<string> path, HashSet<string> complete, bool parents)
        {
            if (!Nodes.ContainsKey(id)) throw Error(id, "missing graph prerequisite");
            if (complete.Contains(id)) return;
            if (!path.Add(id)) throw Error(id, parents ? "hierarchy cycle" : "dependency cycle");
            if (path.Count > 128) throw Error(id, "graph depth exceeds 128");
            var node = Nodes[id];
            if (parents) { if (!string.IsNullOrEmpty(node.parent)) Visit(node.parent, path, complete, true); }
            else
            {
                foreach (var dependency in node.dependencies) Visit(dependency, path, complete, false);
                VisitActivation(node.activate, path, complete);
            }
            path.Remove(id); complete.Add(id);
        }
        private void VisitActivation(MissionCondition condition, HashSet<string> path, HashSet<string> complete)
        {
            if (condition == null) return;
            if (condition.kind == MissionConditionKind.ObjectiveIs || condition.kind == MissionConditionKind.ProgressAtLeast)
                Visit(condition.key, path, complete, false);
            foreach (var child in condition.children) VisitActivation(child, path, complete);
        }
        private static void CheckTree(MissionCondition condition, HashSet<MissionCondition> path, int depth, ref int budget)
        {
            if (condition == null) return;
            if (depth > 32 || ++budget > 16384 || !path.Add(condition)) throw new ArgumentException("Mission condition cycle or complexity budget exceeded.");
            if (condition.children == null) throw new ArgumentException("Condition children missing.");
            foreach (var child in condition.children) { if (child == null) throw new ArgumentException("Null condition child."); CheckTree(child, path, depth + 1, ref budget); }
            path.Remove(condition);
        }
        private void ValidateCondition(MissionCondition c)
        {
            if (c == null) return;
            if (!Enum.IsDefined(typeof(MissionConditionKind), c.kind) || !MissionData.Finite(c.value)) throw Error(c.key, "invalid predicate");
            if (c.kind == MissionConditionKind.Not && c.children.Length != 1 || c.kind == MissionConditionKind.Any && c.children.Length == 0) throw Error(c.key, "invalid logical arity");
            if (c.kind > MissionConditionKind.Not && c.children.Length != 0) throw Error(c.key, "predicate cannot contain children");
            if (c.kind == MissionConditionKind.ObjectiveIs || c.kind == MissionConditionKind.ProgressAtLeast || c.kind == MissionConditionKind.RemainingAtMost)
            { if (!Nodes.ContainsKey(c.key)) throw Error(c.key, "condition references missing objective"); }
            if (c.kind == MissionConditionKind.ObjectiveIs && !Enum.IsDefined(typeof(ObjectiveState), c.state)) throw Error(c.key, "invalid objective state");
            if (c.kind == MissionConditionKind.FactAtLeast || c.kind == MissionConditionKind.FactEquals) MissionData.Id(c.key);
            if (c.kind == MissionConditionKind.Career) Requirements.Add(c, CareerRequirement.Compile(c.career));
            foreach (var child in c.children) ValidateCondition(child);
        }
    }
}
