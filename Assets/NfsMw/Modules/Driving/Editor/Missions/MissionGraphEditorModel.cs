using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public enum MissionGraphDiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    [Serializable]
    public sealed class MissionGraphDiagnostic
    {
        public MissionGraphDiagnosticSeverity severity;
        public string code = string.Empty;
        public string message = string.Empty;
        public string nodeId = string.Empty;
        public string property = string.Empty;

        public MissionGraphDiagnostic() { }

        public MissionGraphDiagnostic(MissionGraphDiagnosticSeverity severity, string code, string message,
            string nodeId = "", string property = "")
        {
            this.severity = severity;
            this.code = code ?? string.Empty;
            this.message = message ?? string.Empty;
            this.nodeId = nodeId ?? string.Empty;
            this.property = property ?? string.Empty;
        }
    }

    [Serializable]
    public sealed class MissionGraphSemanticEdge
    {
        public string sourceId = string.Empty;
        public string targetId = string.Empty;
        public MissionGraphPortKind kind;
        public string label = string.Empty;

        public string Key => MissionGraphEditorModel.EdgeKey(kind, sourceId, targetId, label);
    }

    /// <summary>
    /// Pure-ish authoring operations for the editor. It converts graph gestures
    /// into the authoritative MissionDefinition representation and validates the
    /// proposed result through MissionGraph before committing it.
    /// </summary>
    public static class MissionGraphEditorModel
    {
        public const int MaximumClipboardNodes = 256;
        public const int MaximumEditorGroups = 256;
        public const int MaximumEditorComments = 512;
        public const int MaximumEditorBookmarks = 128;

        public static MissionDefinition CloneDefinition(MissionDefinition definition)
        {
            if (definition == null) return null;
            return JsonUtility.FromJson<MissionDefinition>(JsonUtility.ToJson(definition));
        }

        public static MissionObjective CloneObjective(MissionObjective objective)
        {
            if (objective == null) return null;
            return JsonUtility.FromJson<MissionObjective>(JsonUtility.ToJson(objective));
        }

        public static MissionCondition CloneCondition(MissionCondition condition)
        {
            if (condition == null) return null;
            return JsonUtility.FromJson<MissionCondition>(JsonUtility.ToJson(condition));
        }

        public static MissionAction CloneAction(MissionAction action)
        {
            if (action == null) return null;
            return JsonUtility.FromJson<MissionAction>(JsonUtility.ToJson(action));
        }

        public static MissionDefinition CloneWithFreshIdentity(MissionDefinition definition, string suffix = "copy")
        {
            var copy = CloneDefinition(definition);
            if (copy == null) throw new ArgumentNullException(nameof(definition));
            var existing = new HashSet<string>(StringComparer.Ordinal);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            copy.id = NewId(copy.id + "." + suffix, existing);
            foreach (var node in copy.objectives ?? Array.Empty<MissionObjective>())
            {
                if (node == null) continue;
                string oldId = node.id;
                string next = NewId(oldId + "." + suffix, existing);
                map[oldId] = next;
                node.id = next;
            }
            foreach (var node in copy.objectives ?? Array.Empty<MissionObjective>())
            {
                if (node == null) continue;
                node.dependencies = RemapArray(node.dependencies, map);
                if (map.TryGetValue(node.parent, out var parent)) node.parent = parent;
                node.activate = RemapCondition(node.activate, map);
                node.success = RemapCondition(node.success, map);
                node.failure = RemapCondition(node.failure, map);
                foreach (var action in node.actions ?? Array.Empty<MissionAction>())
                    if (action != null) action.id = NewId(action.id + "." + suffix, existing);
                if (!string.IsNullOrEmpty(node.branchGroup)) node.branchGroup += "." + suffix;
            }
            copy.success = RemapCondition(copy.success, map);
            copy.failure = RemapCondition(copy.failure, map);
            foreach (var checkpoint in copy.checkpoints ?? Array.Empty<MissionCheckpoint>())
                if (checkpoint != null) checkpoint.when = RemapCondition(checkpoint.when, map);
            foreach (var reward in copy.rewards ?? Array.Empty<MissionReward>())
                if (reward != null) reward.when = RemapCondition(reward.when, map);
            return copy;
        }

        public static string NewId(string baseId, ISet<string> existing)
        {
            string seed = (baseId ?? string.Empty).Trim();
            if (seed.Length == 0) seed = "node";
            var chars = seed.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!(char.IsLetterOrDigit(chars[i]) || chars[i] == '.' || chars[i] == '_' || chars[i] == '-' || chars[i] == ':'))
                    chars[i] = '_';
            seed = new string(chars);
            if (seed.Length > 150) seed = seed.Substring(0, 150);
            string result = seed;
            int suffix = 2;
            while (existing != null && existing.Contains(result)) result = seed + "." + suffix++;
            existing?.Add(result);
            return result;
        }

        public static string EdgeKey(MissionGraphPortKind kind, string sourceId, string targetId, string label = "")
            => kind + "|" + (sourceId ?? string.Empty) + "|" + (targetId ?? string.Empty) + "|" + (label ?? string.Empty);

        public static MissionObjective FindNode(MissionDefinition definition, string id)
        {
            if (definition?.objectives == null || string.IsNullOrEmpty(id)) return null;
            return definition.objectives.FirstOrDefault(x => x != null && x.id == id);
        }

        public static List<MissionGraphSemanticEdge> EnumerateEdges(MissionDefinition definition)
        {
            var result = new List<MissionGraphSemanticEdge>();
            if (definition?.objectives == null) return result;
            foreach (var node in definition.objectives)
            {
                if (node == null) continue;
                foreach (var dependency in node.dependencies ?? Array.Empty<string>())
                    result.Add(new MissionGraphSemanticEdge { sourceId = dependency, targetId = node.id, kind = MissionGraphPortKind.Control, label = "dependency" });
                if (!string.IsNullOrEmpty(node.parent))
                    result.Add(new MissionGraphSemanticEdge { sourceId = node.parent, targetId = node.id, kind = MissionGraphPortKind.Data, label = "parent" });
                AddConditionEdges(result, node.id, "activate", node.activate);
                AddConditionEdges(result, node.id, "success", node.success);
                AddConditionEdges(result, node.id, "failure", node.failure);
            }
            return result;
        }

        private static void AddConditionEdges(List<MissionGraphSemanticEdge> output, string ownerId, string label, MissionCondition condition)
        {
            if (condition == null) return;
            if ((condition.kind == MissionConditionKind.ObjectiveIs || condition.kind == MissionConditionKind.ProgressAtLeast || condition.kind == MissionConditionKind.RemainingAtMost)
                && !string.IsNullOrEmpty(condition.key))
                output.Add(new MissionGraphSemanticEdge { sourceId = condition.key, targetId = ownerId, kind = MissionGraphPortKind.Condition, label = label + ":" + condition.kind });
            foreach (var child in condition.children ?? Array.Empty<MissionCondition>()) AddConditionEdges(output, ownerId, label, child);
        }

        public static bool TryAddControlLink(MissionDefinition definition, string sourceId, string targetId, out string failure)
        {
            failure = string.Empty;
            var candidate = CloneDefinition(definition);
            var target = FindNode(candidate, targetId);
            if (FindNode(candidate, sourceId) == null || target == null)
            { failure = "Both ports must belong to existing objective nodes."; return false; }
            if (sourceId == targetId)
            { failure = "A control port cannot connect to itself."; return false; }
            var dependencies = new List<string>(target.dependencies ?? Array.Empty<string>());
            if (dependencies.Contains(sourceId, StringComparer.Ordinal))
            { failure = "That dependency already exists."; return false; }
            dependencies.Add(sourceId); target.dependencies = dependencies.ToArray();
            try { _ = new MissionGraph(candidate); }
            catch (Exception exception) { failure = "Control connection rejected: " + exception.Message; return false; }
            var actual = FindNode(definition, targetId);
            actual.dependencies = new List<string>(actual.dependencies ?? Array.Empty<string>()) { sourceId }.ToArray();
            return true;
        }

        public static bool TryRemoveControlLink(MissionDefinition definition, string sourceId, string targetId)
        {
            var target = FindNode(definition, targetId);
            if (target == null || target.dependencies == null) return false;
            int before = target.dependencies.Length;
            target.dependencies = target.dependencies.Where(x => x != sourceId).ToArray();
            return before != target.dependencies.Length;
        }

        public static List<MissionGraphDiagnostic> Validate(MissionDefinition definition, MissionGraphLayoutAsset layout = null)
        {
            var diagnostics = new List<MissionGraphDiagnostic>();
            if (definition == null)
            {
                diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "MISSION_NULL", "No mission definition is selected."));
                return diagnostics;
            }
            if (string.IsNullOrWhiteSpace(definition.id)) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "MISSION_ID", "Mission ID is empty.", property: "id"));
            if (string.IsNullOrWhiteSpace(definition.title)) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Warning, "MISSION_TITLE", "Mission title is empty.", property: "title"));
            if (definition.version < 1) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "MISSION_VERSION", "Mission content version must be positive.", property: "version"));
            if (definition.success == null) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "MISSION_SUCCESS", "Mission success policy is not configured.", property: "success"));
            if (definition.objectives == null || definition.objectives.Length == 0)
                diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "NO_OBJECTIVES", "A mission needs at least one objective.", property: "objectives"));
            if (definition.objectives != null && definition.objectives.Length > 2048)
                diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "OBJECTIVE_BUDGET", "The runtime limit is 2,048 objectives.", property: "objectives"));

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var actionIds = new HashSet<string>(StringComparer.Ordinal);
            var branchChoices = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var node in definition.objectives ?? Array.Empty<MissionObjective>())
            {
                if (node == null) continue;
                if (string.IsNullOrWhiteSpace(node.id)) continue;
                if (!ids.Add(node.id)) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "DUPLICATE_NODE_ID", "Objective ID is duplicated: " + node.id, node.id, "id"));
            }
            foreach (var node in definition.objectives ?? Array.Empty<MissionObjective>())
            {
                if (node == null)
                {
                    diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "NULL_NODE", "Objective collection contains a null node."));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(node.id)) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "NODE_ID", "Objective ID is empty.", property: "id"));
                if (string.IsNullOrWhiteSpace(node.title)) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Warning, "NODE_TITLE", "Objective has no designer title.", node.id, "title"));
                if (string.IsNullOrEmpty(node.kind)) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "NODE_KIND", "Objective kind is empty.", node.id, "kind"));
                if (node.required <= 0 || double.IsNaN(node.required) || double.IsInfinity(node.required)) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "NODE_REQUIRED", "Required progress must be finite and positive.", node.id, "required"));
                if (node.duration < 0 || double.IsNaN(node.duration) || double.IsInfinity(node.duration)) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "NODE_DURATION", "Duration must be finite and non-negative.", node.id, "duration"));
                if ((node.kind == "event" || node.kind == "sequence") && string.IsNullOrWhiteSpace(node.eventType))
                    diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "EVENT_PORT", "Event and sequence objectives need an event type.", node.id, "eventType"));
                if (node.kind == "sequence" && (node.sequence == null || node.sequence.Length == 0))
                    diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "SEQUENCE_EMPTY", "Sequence objective needs at least one stable target token.", node.id, "sequence"));
                foreach (var dependency in node.dependencies ?? Array.Empty<string>())
                    if (string.IsNullOrEmpty(dependency)) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "DEPENDENCY_EMPTY", "Control dependency is empty.", node.id, "dependencies"));
                if (node.dependencies != null && node.dependencies.Length != node.dependencies.Distinct(StringComparer.Ordinal).Count())
                    diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "DEPENDENCY_DUPLICATE", "Control dependencies must be unique.", node.id, "dependencies"));
                if (node.activate == null)
                    diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Warning, "ACTIVATION_UNSET", "No activation condition means this objective can never become active in MissionRuntime.", node.id, "activate"));
                else if (IsAlwaysFalse(node.activate))
                    diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Warning, "UNREACHABLE_ACTIVATION", "Bounded analysis found an always-false activation condition; runtime reachability is not otherwise proven.", node.id, "activate"));
                ValidateCondition(diagnostics, node.id, "activate", node.activate, ids, 0);
                ValidateCondition(diagnostics, node.id, "success", node.success, ids, 0);
                ValidateCondition(diagnostics, node.id, "failure", node.failure, ids, 0);
                if (!string.IsNullOrEmpty(node.branchGroup))
                {
                    if (!branchChoices.TryGetValue(node.branchGroup, out var choices)) branchChoices.Add(node.branchGroup, choices = new HashSet<string>(StringComparer.Ordinal));
                    if (!choices.Add(node.branchChoice)) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Warning, "BRANCH_DUPLICATE", "Branch choice is duplicated in group " + node.branchGroup + ".", node.id, "branchChoice"));
                }
                if ((node.kind == "timer" || node.kind == "hold" || node.kind == "deadline") && node.duration <= 0)
                    diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "TIMER_DURATION", "Timer-like objectives need a positive duration.", node.id, "duration"));
                foreach (var action in node.actions ?? Array.Empty<MissionAction>())
                {
                    if (action == null) { diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "NULL_ACTION", "Objective contains a null action.", node.id, "actions")); continue; }
                    if (string.IsNullOrWhiteSpace(action.id)) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "ACTION_ID", "Action ID is empty.", node.id, "actions"));
                    else if (!actionIds.Add(action.id)) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "DUPLICATE_ACTION_ID", "Action ID is duplicated: " + action.id, node.id, "actions"));
                    if (string.IsNullOrWhiteSpace(action.kind)) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "ACTION_KIND", "Action kind is empty; a typed adapter must own the effect.", node.id, "actions"));
                    if (string.IsNullOrWhiteSpace(action.binding)) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Warning, "ACTION_BINDING", "Action has no stable world binding.", node.id, "actions"));
                }
            }
            foreach (var node in definition.objectives ?? Array.Empty<MissionObjective>())
            {
                if (node == null) continue;
                foreach (var dependency in node.dependencies ?? Array.Empty<string>())
                    if (!ids.Contains(dependency)) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "MISSING_DEPENDENCY", "Dependency references missing objective: " + dependency, node.id, "dependencies"));
                if (!string.IsNullOrEmpty(node.parent) && !ids.Contains(node.parent)) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "MISSING_PARENT", "Parent references missing objective: " + node.parent, node.id, "parent"));
            }
            foreach (var checkpoint in definition.checkpoints ?? Array.Empty<MissionCheckpoint>())
                if (checkpoint == null || string.IsNullOrWhiteSpace(checkpoint.id)) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "CHECKPOINT_ID", "Recovery checkpoints need stable IDs.", property: "checkpoints"));
            if ((definition.rewards ?? Array.Empty<MissionReward>()).Length == 0)
                diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Info, "NO_REWARDS", "No rewards are authored; settlement will produce a zero-reward result."));
            ValidateCondition(diagnostics, string.Empty, "mission.success", definition.success, ids, 0);
            ValidateCondition(diagnostics, string.Empty, "mission.failure", definition.failure, ids, 0);
            try
            {
                _ = new MissionGraph(definition);
                if (!diagnostics.Any(x => x.severity == MissionGraphDiagnosticSeverity.Error))
                    diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Info, "COMPILED", "MissionGraph compiled successfully; runtime content hash is valid."));
            }
            catch (Exception exception)
            {
                diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "COMPILE", exception.Message));
            }
            if (layout != null)
            {
                if (layout.Schema != MissionGraphLayoutAsset.CurrentSchema)
                    diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "LAYOUT_SCHEMA", "Editor layout schema is unsupported; migrate or recreate the layout asset."));
                var known = new HashSet<string>(ids, StringComparer.Ordinal);
                foreach (var entry in layout.Nodes ?? new List<MissionGraphNodeLayout>())
                    if (entry != null && !known.Contains(entry.id)) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Info, "STALE_LAYOUT", "Layout contains a node no longer present in the mission: " + entry.id, entry.id));
            }
            return diagnostics;
        }

        private static void ValidateCondition(List<MissionGraphDiagnostic> diagnostics, string owner, string property,
            MissionCondition condition, ISet<string> ids, int depth)
        {
            if (condition == null) return;
            if (depth > 32)
            { diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "CONDITION_DEPTH", "Condition tree exceeds depth 32.", owner, property)); return; }
            if (condition.children == null)
            { diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "CONDITION_CHILDREN", "Condition child collection is missing.", owner, property)); return; }
            if (condition.kind == MissionConditionKind.Any && condition.children.Length == 0)
                diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "ANY_EMPTY", "ANY requires at least one child.", owner, property));
            if (condition.kind == MissionConditionKind.Not && condition.children.Length != 1)
                diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "NOT_ARITY", "NOT requires exactly one child.", owner, property));
            if (condition.kind != MissionConditionKind.All && condition.kind != MissionConditionKind.Any && condition.kind != MissionConditionKind.Not && condition.children.Length > 0)
                diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "LEAF_CHILDREN", "Leaf conditions cannot have children.", owner, property));
            if ((condition.kind == MissionConditionKind.ObjectiveIs || condition.kind == MissionConditionKind.ProgressAtLeast || condition.kind == MissionConditionKind.RemainingAtMost)
                && !ids.Contains(condition.key)) diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "MISSING_CONDITION_NODE", "Condition references missing objective: " + condition.key, owner, property));
            if ((condition.kind == MissionConditionKind.FactAtLeast || condition.kind == MissionConditionKind.FactEquals) && string.IsNullOrWhiteSpace(condition.key))
                diagnostics.Add(new MissionGraphDiagnostic(MissionGraphDiagnosticSeverity.Error, "FACT_KEY", "Fact condition needs a stable key.", owner, property));
            foreach (var child in condition.children) ValidateCondition(diagnostics, owner, property, child, ids, depth + 1);
        }

        private static bool IsAlwaysFalse(MissionCondition condition)
        {
            if (condition == null) return false;
            if (condition.kind == MissionConditionKind.Any && (condition.children == null || condition.children.Length == 0)) return true;
            if (condition.kind == MissionConditionKind.Not && condition.children != null && condition.children.Length == 1 && IsAlwaysTrue(condition.children[0])) return true;
            return false;
        }

        private static bool IsAlwaysTrue(MissionCondition condition)
        {
            return condition != null && condition.kind == MissionConditionKind.All && (condition.children == null || condition.children.Length == 0);
        }

        private static string[] RemapArray(string[] values, IReadOnlyDictionary<string, string> map)
        {
            if (values == null) return Array.Empty<string>();
            var result = new string[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = map.TryGetValue(values[i], out var mapped) ? mapped : values[i];
            return result;
        }

        private static MissionCondition RemapCondition(MissionCondition condition, IReadOnlyDictionary<string, string> map)
        {
            if (condition == null) return null;
            var result = CloneCondition(condition);
            if ((result.kind == MissionConditionKind.ObjectiveIs || result.kind == MissionConditionKind.ProgressAtLeast || result.kind == MissionConditionKind.RemainingAtMost)
                && map.TryGetValue(result.key, out var mapped)) result.key = mapped;
            if (result.children != null)
                for (int i = 0; i < result.children.Length; i++) result.children[i] = RemapCondition(result.children[i], map);
            return result;
        }

        [Serializable]
        private sealed class ClipboardPayload
        {
            public int schema = 1;
            public MissionObjective[] objectives = Array.Empty<MissionObjective>();
            public MissionGraphNodeLayout[] layouts = Array.Empty<MissionGraphNodeLayout>();
        }

        public static class Clipboard
        {
            public static string Copy(MissionDefinition definition, MissionGraphLayoutAsset layout, IEnumerable<string> selection)
            {
                var ids = new HashSet<string>(selection ?? Array.Empty<string>(), StringComparer.Ordinal);
                var nodes = (definition?.objectives ?? Array.Empty<MissionObjective>()).Where(x => x != null && ids.Contains(x.id)).Select(CloneObjective).ToArray();
                if (nodes.Length == 0) return string.Empty;
                if (nodes.Length > MaximumClipboardNodes) throw new InvalidOperationException("Clipboard limit is 256 objective nodes.");
                var layouts = (layout?.Nodes ?? new List<MissionGraphNodeLayout>()).Where(x => x != null && ids.Contains(x.id)).Select(x => JsonUtility.FromJson<MissionGraphNodeLayout>(JsonUtility.ToJson(x))).ToArray();
                return JsonUtility.ToJson(new ClipboardPayload { objectives = nodes, layouts = layouts });
            }

            public static bool TryPaste(MissionDefinition definition, MissionGraphLayoutAsset layout, string payload,
                Vector2 offset, out string[] pastedIds, out string failure)
            {
                pastedIds = Array.Empty<string>(); failure = string.Empty;
                if (definition == null || string.IsNullOrEmpty(payload)) { failure = "Clipboard is empty."; return false; }
                ClipboardPayload source;
                try { source = JsonUtility.FromJson<ClipboardPayload>(payload); }
                catch (Exception exception) { failure = "Clipboard data is invalid: " + exception.Message; return false; }
                if (source == null || source.schema != 1 || source.objectives == null || source.objectives.Length == 0 || source.objectives.Length > MaximumClipboardNodes)
                { failure = "Clipboard payload is unsupported or exceeds the 256-node limit."; return false; }
                var candidate = CloneDefinition(definition);
                var existing = new HashSet<string>((candidate.objectives ?? Array.Empty<MissionObjective>()).Where(x => x != null).Select(x => x.id), StringComparer.Ordinal);
                var actionIds = new HashSet<string>((candidate.objectives ?? Array.Empty<MissionObjective>()).Where(x => x != null).SelectMany(x => x.actions ?? Array.Empty<MissionAction>()).Where(x => x != null).Select(x => x.id), StringComparer.Ordinal);
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                var branchMap = new Dictionary<string, string>(StringComparer.Ordinal);
                var copied = new List<MissionObjective>();
                foreach (var original in source.objectives)
                {
                    if (original == null) continue;
                    var clone = CloneObjective(original);
                    map[original.id] = NewId(original.id + ".copy", existing);
                    clone.id = map[original.id];
                    if (!string.IsNullOrEmpty(clone.branchGroup))
                    {
                        if (!branchMap.TryGetValue(clone.branchGroup, out var group)) branchMap.Add(clone.branchGroup, NewId(clone.branchGroup + ".copy", existing));
                        clone.branchGroup = group;
                    }
                    foreach (var action in clone.actions ?? Array.Empty<MissionAction>())
                        if (action != null) action.id = NewId(action.id + ".copy", actionIds);
                    copied.Add(clone);
                }
                foreach (var clone in copied)
                {
                    clone.dependencies = RemapArray(clone.dependencies, map);
                    if (map.TryGetValue(clone.parent, out var parent)) clone.parent = parent;
                    clone.activate = RemapCondition(clone.activate, map);
                    clone.success = RemapCondition(clone.success, map);
                    clone.failure = RemapCondition(clone.failure, map);
                }
                candidate.objectives = (candidate.objectives ?? Array.Empty<MissionObjective>()).Concat(copied).ToArray();
                try { _ = new MissionGraph(candidate); }
                catch (Exception exception) { failure = "Paste rejected: " + exception.Message; return false; }
                definition.objectives = candidate.objectives;
                pastedIds = copied.Select(x => x.id).ToArray();
                if (layout != null)
                {
                    var sourceLayouts = source.layouts ?? Array.Empty<MissionGraphNodeLayout>();
                    foreach (var id in pastedIds)
                    {
                        var originalId = map.First(x => x.Value == id).Key;
                        var originalLayout = sourceLayouts.FirstOrDefault(x => x != null && x.id == originalId);
                        var position = originalLayout == null ? Vector2.zero : originalLayout.position + offset;
                        layout.EnsureNode(id, position);
                        var targetLayout = layout.FindNode(id);
                        if (originalLayout != null)
                        {
                            targetLayout.position = position;
                            targetLayout.collapsed = originalLayout.collapsed;
                            targetLayout.pinned = originalLayout.pinned;
                        }
                    }
                }
                return true;
            }
        }
    }
}
