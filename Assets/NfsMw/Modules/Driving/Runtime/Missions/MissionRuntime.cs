using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace NfsMwRemaster.Driving
{
    /// <summary>Single-threaded deterministic orchestration. Submit a whole step, never callbacks into Evaluate.</summary>
    public sealed class MissionRuntime : IDisposable
    {
        private readonly MissionGraph graph;
        private readonly ICareerFacts career;
        private readonly IMissionActions actions;
        private MissionSnapshot snapshot;
        private readonly Dictionary<string, MissionObjectiveSnapshot> nodes = new Dictionary<string, MissionObjectiveSnapshot>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> facts = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> branches = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<MissionObjective>> subscriptions = new Dictionary<string, List<MissionObjective>>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private readonly List<MissionEvent> ordered = new List<MissionEvent>();
        private readonly List<MissionAction> desired = new List<MissionAction>();
        private readonly Queue<string> log = new Queue<string>();
        private readonly HashSet<string> consumed = new HashSet<string>(StringComparer.Ordinal);
        private bool evaluating, disposed;
        private string actionSignature;
        public MissionState State => snapshot.state;
        public string Id => graph.Id;
        public double Elapsed => snapshot.elapsed;
        public int SubscriptionCount { get; private set; }
        public long EventsDelivered { get; private set; }
        public IReadOnlyCollection<string> Timeline => log.ToArray();
        public MissionResult Result => MissionData.Copy(snapshot.result);
        public MissionDefinition Definition => graph.Definition();

        public MissionRuntime(MissionGraph graph, ICareerFacts career = null, IMissionActions actions = null,
            MissionSnapshot restore = null, IMissionMigration migration = null)
        {
            this.graph = graph ?? throw new ArgumentNullException(nameof(graph)); this.career = career; this.actions = actions;
            if (restore == null)
            {
                if (graph.Availability != null && (career == null || !graph.Availability.Evaluate(career))) throw new InvalidOperationException("Mission is unavailable: " + graph.Id);
                snapshot = new MissionSnapshot { missionId = graph.Id, contentVersion = graph.Version, definitionHash = graph.ContentHash, claimId = Guid.NewGuid().ToString("N") };
                foreach (var definition in graph.Data.objectives) snapshot.objectives.Add(new MissionObjectiveSnapshot { id = definition.id });
            }
            else
            {
                snapshot = MissionData.Copy(restore);
                if (snapshot.contentVersion != graph.Version || snapshot.definitionHash != graph.ContentHash)
                {
                    if (migration == null) throw new ArgumentException("Mission content version requires an explicit migration: " + graph.Id);
                    snapshot = migration.Migrate(snapshot, graph.Version);
                    snapshot.definitionHash = graph.ContentHash;
                }
            }
            ValidateSnapshot(snapshot, graph);
            if (!snapshot.hasResult) snapshot.result = null;
            Index();
            if (restore == null) Resolve();
            Rebuild();
            Reconcile();
            Record(restore == null ? "Mission started" : "Mission restored (no completion replay)");
        }

        public MissionSnapshot Capture()
        {
            Sync(); return MissionData.Copy(snapshot);
        }
        public MissionObjectiveSnapshot Objective(string id) => nodes.TryGetValue(id, out var node) ? MissionData.Copy(node) : throw new ArgumentException("Unknown objective: " + id);
        public double Progress(string id) => nodes[id].progress;
        public double ObjectiveElapsed(string id) => nodes[id].elapsed;
        public ObjectiveState ObjectiveStatus(string id) => nodes[id].state;
        public double Fact(string key) => facts.TryGetValue(key, out double value) ? value : 0;
        public bool Evaluate(MissionCondition condition)
        {
            if (condition == null) return false;
            switch (condition.kind)
            {
                case MissionConditionKind.All: foreach (var c in condition.children) if (!Evaluate(c)) return false; return true;
                case MissionConditionKind.Any: foreach (var c in condition.children) if (Evaluate(c)) return true; return false;
                case MissionConditionKind.Not: return !Evaluate(condition.children[0]);
                case MissionConditionKind.FactAtLeast: return Fact(condition.key) >= condition.value;
                case MissionConditionKind.FactEquals: return Fact(condition.key) == condition.value;
                case MissionConditionKind.ObjectiveIs: return nodes[condition.key].state == condition.state;
                case MissionConditionKind.ProgressAtLeast: return nodes[condition.key].progress >= condition.value;
                case MissionConditionKind.RemainingAtMost: return Math.Max(0, graph.Nodes[condition.key].duration - nodes[condition.key].elapsed) <= condition.value;
                case MissionConditionKind.Career:
                    if (career == null) return false;
                    return (graph.Requirements.TryGetValue(condition, out var requirement) ? requirement : CareerRequirement.Compile(condition.career)).Evaluate(career);
                default: throw new ArgumentException("Unknown condition.");
            }
        }
        public string Explain(MissionCondition condition)
        {
            var text = new StringBuilder(); Explain(condition, text, 0); return text.ToString();
        }
        private void Explain(MissionCondition c, StringBuilder text, int depth)
        {
            if (c == null) { text.AppendLine("Not configured"); return; }
            text.Append(' ', depth * 2).Append(c.kind).Append(' ').Append(c.key).Append(" -> ").Append(Evaluate(c)).AppendLine();
            foreach (var child in c.children) Explain(child, text, depth + 1);
        }

        public void Step(long step, double gameSeconds, double realSeconds, IReadOnlyList<MissionEvent> events)
        {
            if (disposed) throw new ObjectDisposedException(nameof(MissionRuntime));
            if (evaluating) throw new InvalidOperationException("Reentrant mission step.");
            if (!MissionData.Finite(gameSeconds) || !MissionData.Finite(realSeconds) || gameSeconds < 0 || realSeconds < 0 || step <= snapshot.step)
                throw new ArgumentException("Mission step must advance monotonically with finite nonnegative clocks.");
            if (events == null || events.Count > 8192) throw new ArgumentException("Mission event batch exceeds 8192.");
            if (State != MissionState.Active && State != MissionState.Suspended) return;
            ordered.Clear();
            foreach (var input in events)
            {
                MissionData.Id(input.id); MissionData.Id(input.type);
                if (!MissionData.Finite(input.value)) throw new ArgumentException("Non-finite gameplay event.");
                if (!string.IsNullOrEmpty(input.fact)) MissionData.Id(input.fact);
                if (!string.IsNullOrEmpty(input.target)) MissionData.Id(input.target);
                ordered.Add(input);
            }
            ordered.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            for (int i = 1; i < ordered.Count; i++)
                if (ordered[i - 1].id == ordered[i].id && !ordered[i - 1].Equals(ordered[i])) throw new ArgumentException("Conflicting duplicate event ID: " + ordered[i].id);
            evaluating = true;
            try
            {
                snapshot.step = step;
                if (State == MissionState.Active)
                {
                    snapshot.elapsed += gameSeconds;
                    string last = null;
                    foreach (var input in ordered)
                    {
                        if (input.id == last) continue; last = input.id;
                        if (!string.IsNullOrEmpty(input.fact))
                        {
                            if (!facts.ContainsKey(input.fact) && facts.Count >= 4096) throw new InvalidOperationException("Mission fact budget exceeded.");
                            facts[input.fact] = input.value;
                        }
                        if (!subscriptions.TryGetValue(input.type, out var listeners)) continue;
                        if (consumed.Contains(input.id)) continue;
                        bool accepted = false;
                        foreach (var definition in listeners)
                        {
                            if (!string.IsNullOrEmpty(definition.target) && definition.target != input.target) continue;
                            var node = nodes[definition.id];
                            string identity = definition.uniqueTargets ? input.target : input.id;
                            if (string.IsNullOrEmpty(identity)) throw new ArgumentException("Unique-target objective requires target identity.");
                            if (seen[node.id].Contains(identity)) continue;
                            if (seen[node.id].Count >= 100000) throw new InvalidOperationException("Objective deduplication budget exceeded: " + node.id);
                            if (definition.kind == "sequence" && (node.progress >= definition.sequence.Length || definition.sequence[(int)node.progress] != input.target)) continue;
                            seen[node.id].Add(identity); node.seen.Add(identity); EventsDelivered++;
                            accepted = true;
                            if (definition.kind == "sequence") node.progress++;
                            else if (definition.kind == "event") node.progress = Math.Min(definition.required, node.progress + (definition.accumulate ? Math.Max(0, input.value) : 1));
                            else node.progress = graph.Primitives[definition.kind].Apply(MissionData.Copy(definition), node.progress, input);
                            if (!MissionData.Finite(node.progress) || node.progress < 0) throw new InvalidOperationException("Primitive produced invalid progress.");
                            Record(input.type + " -> " + node.id + " " + node.progress);
                        }
                        if (accepted)
                        {
                            if (consumed.Count >= 100000) throw new InvalidOperationException("Mission event identity budget exceeded.");
                            consumed.Add(input.id); snapshot.consumedEvents.Add(input.id);
                        }
                    }
                }
                foreach (var definition in graph.Data.objectives)
                {
                    var node = nodes[definition.id]; if (node.state != ObjectiveState.Active) continue;
                    double delta = definition.clock == MissionClock.Real ? realSeconds : State == MissionState.Suspended ? 0 : gameSeconds;
                    if (definition.kind == "hold")
                    {
                        if (State == MissionState.Active && Evaluate(definition.success)) node.elapsed += delta;
                        else if (definition.resetWhenFalse && State == MissionState.Active) node.elapsed = 0;
                        node.progress = node.elapsed;
                    }
                    else node.elapsed += delta;
                    foreach (double threshold in definition.warnings)
                        if (definition.duration - node.elapsed <= threshold && !node.warned.Contains(threshold))
                        { node.warned.Add(threshold); Record("Timer warning " + node.id + " " + threshold); }
                }
                Resolve(); Rebuild(); Reconcile();
                if (State == MissionState.Active)
                    foreach (var checkpoint in graph.Data.checkpoints)
                        if (!snapshot.checkpoints.Contains(checkpoint.id) && Evaluate(checkpoint.when))
                        {
                            snapshot.checkpoints.Add(checkpoint.id); snapshot.checkpointId = checkpoint.id;
                            Sync(); var copy = MissionData.Copy(snapshot); copy.checkpointJson = "";
                            snapshot.checkpointJson = JsonConvert.SerializeObject(copy); Record("Checkpoint " + checkpoint.id);
                        }
            }
            catch
            {
                // Never continue a partially evaluated transaction or allow its reward to settle.
                Finish(MissionState.Failed, "RuntimeFault"); Rebuild();
                try { Reconcile(); } catch { }
                throw;
            }
            finally { evaluating = false; }
        }

        private void Resolve()
        {
            if (State != MissionState.Active && State != MissionState.Suspended) return;
            // Failure wins across the entire simulation-step batch, independent of callback order.
            string failure = Evaluate(graph.Data.failure) ? graph.Data.failureReason : null;
            foreach (var definition in graph.Data.objectives)
            {
                var node = nodes[definition.id]; if (node.state != ObjectiveState.Active) continue;
                bool expired = definition.duration > 0 && definition.kind != "hold" && definition.kind != "timer" && node.elapsed >= definition.duration;
                if (Evaluate(definition.failure) || expired)
                {
                    node.state = ObjectiveState.Failed; node.failure = expired ? "TimerExpired" : definition.failureReason;
                    if (!definition.optional && failure == null) failure = node.failure;
                }
            }
            if (failure != null) { Finish(MissionState.Failed, failure); return; }
            if (State == MissionState.Suspended) return;
            foreach (var definition in graph.Data.objectives)
            {
                var node = nodes[definition.id]; if (node.state != ObjectiveState.Active) continue;
                bool done = definition.kind == "deadline" ? false : definition.kind == "condition" ? Evaluate(definition.success)
                    : definition.kind == "timer" || definition.kind == "hold" ? node.elapsed >= definition.duration
                    : node.progress >= (definition.kind == "sequence" ? definition.sequence.Length : definition.required);
                if (done && (definition.kind == "condition" || definition.kind == "hold" || definition.success == null || Evaluate(definition.success)))
                { node.state = ObjectiveState.Succeeded; node.completedAt = snapshot.elapsed; Record("Succeeded " + node.id); }
            }
            if (Evaluate(graph.Data.failure)) { Finish(MissionState.Failed, graph.Data.failureReason); return; }
            if (Evaluate(graph.Data.success)) { Finish(MissionState.Succeeded, ""); return; }
            foreach (var definition in graph.Data.objectives)
            {
                var node = nodes[definition.id]; if (node.state != ObjectiveState.Inactive) continue;
                if (!string.IsNullOrEmpty(definition.branchGroup) && branches.TryGetValue(definition.branchGroup, out var chosen) && chosen != definition.branchChoice)
                { node.state = ObjectiveState.Skipped; continue; }
                bool ready = true;
                foreach (string dependency in definition.dependencies) if (nodes[dependency].state != ObjectiveState.Succeeded) { ready = false; break; }
                if (!ready || !Evaluate(definition.activate)) continue;
                if (!string.IsNullOrEmpty(definition.branchGroup)) branches[definition.branchGroup] = definition.branchChoice;
                node.state = ObjectiveState.Active; Record("Activated " + node.id);
            }
        }
        private void Finish(MissionState state, string failure)
        {
            snapshot.state = state;
            var result = new MissionResult { missionId = Id, claimId = snapshot.claimId, outcome = state, failure = failure, duration = snapshot.elapsed, attempts = snapshot.attempt };
            foreach (var definition in graph.Data.objectives)
            {
                var node = nodes[definition.id];
                if (node.state == ObjectiveState.Succeeded) result.completed.Add(node.id);
                if (node.state == ObjectiveState.Failed) result.failed.Add(node.id);
                if (node.state == ObjectiveState.Active || node.state == ObjectiveState.Inactive) node.state = ObjectiveState.Cancelled;
            }
            if (state == MissionState.Succeeded)
                foreach (var reward in graph.Data.rewards) if (Evaluate(reward.when))
                {
                    result.cash = checked(result.cash + reward.cash); result.reputation = checked(result.reputation + reward.reputation);
                    foreach (var grant in reward.grants) result.grants.Add(MissionData.Copy(grant));
                    result.bonuses.Add(reward.id);
                }
            snapshot.result = result; snapshot.hasResult = true; Record("Mission " + state + " " + failure);
        }
        public void Suspend(bool suspended)
        {
            Guard();
            if (State != (suspended ? MissionState.Active : MissionState.Suspended)) throw new InvalidOperationException("Invalid suspension transition.");
            snapshot.state = suspended ? MissionState.Suspended : MissionState.Active; Rebuild();
        }
        public void Abort()
        {
            Guard(); if (State != MissionState.Active && State != MissionState.Suspended) return;
            Finish(MissionState.Aborted, "Aborted"); Rebuild(); Reconcile();
        }
        public void RetryCheckpoint()
        {
            Guard();
            if (State != MissionState.Failed || string.IsNullOrEmpty(snapshot.checkpointJson)) throw new InvalidOperationException("No failed checkpoint attempt to retry.");
            var restored = JsonConvert.DeserializeObject<MissionSnapshot>(snapshot.checkpointJson);
            ValidateSnapshot(restored, graph);
            if (restored.claimId != snapshot.claimId || restored.state != MissionState.Active) throw new ArgumentException("Invalid checkpoint ownership.");
            restored.checkpointJson = snapshot.checkpointJson; restored.attempt = checked(snapshot.attempt + 1); restored.step = snapshot.step;
            snapshot = restored; Index(); Rebuild(); Reconcile(); Record("Checkpoint retry " + snapshot.attempt);
        }
        private void Guard() { if (disposed || evaluating) throw new InvalidOperationException("Mission is disposed or evaluating."); }
        public void Dispose()
        {
            if (disposed) return;
            Guard(); desired.Clear(); actions?.Reconcile(snapshot.claimId, snapshot.attempt, desired);
            subscriptions.Clear(); SubscriptionCount = 0; disposed = true;
        }
        private void Index()
        {
            nodes.Clear(); facts.Clear(); branches.Clear(); seen.Clear(); consumed.Clear();
            foreach (string id in snapshot.consumedEvents) consumed.Add(id);
            foreach (var node in snapshot.objectives) { nodes.Add(node.id, node); seen.Add(node.id, new HashSet<string>(node.seen, StringComparer.Ordinal)); }
            foreach (var fact in snapshot.facts) facts.Add(fact.key, fact.value);
            foreach (var branch in snapshot.branches) branches.Add(branch.group, branch.choice);
        }
        private void Sync()
        {
            snapshot.facts.Clear(); foreach (var fact in facts) snapshot.facts.Add(new MissionValue { key = fact.Key, value = fact.Value });
            snapshot.branches.Clear(); foreach (var branch in branches) snapshot.branches.Add(new MissionBranch { group = branch.Key, choice = branch.Value });
        }
        private void Rebuild()
        {
            subscriptions.Clear(); SubscriptionCount = 0;
            if (State != MissionState.Active) return;
            foreach (var definition in graph.Data.objectives)
                if (nodes[definition.id].state == ObjectiveState.Active && !string.IsNullOrEmpty(definition.eventType))
                {
                    if (!subscriptions.TryGetValue(definition.eventType, out var list)) subscriptions.Add(definition.eventType, list = new List<MissionObjective>());
                    list.Add(definition); SubscriptionCount++;
                }
        }
        private void Reconcile()
        {
            if (actions == null)
            {
                foreach (var definition in graph.Data.objectives)
                    if (nodes[definition.id].state == ObjectiveState.Active && definition.actions.Length > 0)
                        throw new InvalidOperationException("Mission requires a world-action adapter.");
                return;
            }
            desired.Clear();
            if (State == MissionState.Active || State == MissionState.Suspended)
                foreach (var definition in graph.Data.objectives) if (nodes[definition.id].state == ObjectiveState.Active) desired.AddRange(definition.actions);
            string signature = snapshot.attempt + ":" + string.Join(",", desired.ConvertAll(x => x.id));
            if (signature == actionSignature) return;
            actions.Reconcile(snapshot.claimId, snapshot.attempt, MissionData.Copy(desired).AsReadOnly());
            actionSignature = signature;
        }
        private void Record(string message) { if (log.Count == 256) log.Dequeue(); log.Enqueue(snapshot.step + ": " + message); }

        public static void ValidateSnapshot(MissionSnapshot value, MissionGraph graph = null)
        {
            if (value == null || value.schema != 1 || value.contentVersion < 1 || value.attempt < 1 || value.step < 0 || !Enum.IsDefined(typeof(MissionState), value.state)
                || !MissionData.Finite(value.elapsed) || value.elapsed < 0 || value.objectives == null || value.objectives.Count > 2048 || value.facts == null || value.facts.Count > 4096
                || value.branches == null || value.checkpoints == null || value.consumedEvents == null || value.consumedEvents.Count > 100000 || value.checkpointJson == null || value.checkpointJson.Length > 65536) throw new ArgumentException("Invalid mission snapshot.");
            MissionData.Id(value.missionId); MissionData.Id(value.claimId);
            if (value.definitionHash == null || value.definitionHash.Length != 64) throw new ArgumentException("Missing mission definition fingerprint.");
            if (new HashSet<string>(value.consumedEvents).Count != value.consumedEvents.Count) throw new ArgumentException("Duplicate mission event identities.");
            foreach (string id in value.consumedEvents) MissionData.Id(id);
            if (graph != null && (value.missionId != graph.Id || value.contentVersion != graph.Version || value.definitionHash != graph.ContentHash || value.objectives.Count != graph.Nodes.Count)) throw new ArgumentException("Mission snapshot does not match definition.");
            var unique = new HashSet<string>();
            foreach (var node in value.objectives)
            {
                if (node == null || !unique.Add(node.id) || !Enum.IsDefined(typeof(ObjectiveState), node.state) || !MissionData.Finite(node.progress) || node.progress < 0
                    || !MissionData.Finite(node.elapsed) || node.elapsed < 0 || !MissionData.Finite(node.completedAt) || node.completedAt < 0 || node.seen == null || node.seen.Count > 100000 || node.warned == null)
                    throw new ArgumentException("Invalid objective snapshot.");
                MissionData.Id(node.id);
                if (new HashSet<string>(node.seen).Count != node.seen.Count) throw new ArgumentException("Duplicate objective event evidence.");
                foreach (string id in node.seen) MissionData.Id(id);
                foreach (double warning in node.warned) if (!MissionData.Finite(warning) || warning < 0) throw new ArgumentException("Invalid timer warning evidence.");
                if (graph != null && !graph.Nodes.ContainsKey(node.id)) throw new ArgumentException("Unknown restored objective: " + node.id);
                if (graph != null)
                {
                    var definition = graph.Nodes[node.id];
                    if (definition.kind == "sequence" && (node.progress != Math.Floor(node.progress) || node.progress > definition.sequence.Length)
                        || definition.kind == "event" && node.progress > definition.required) throw new ArgumentException("Invalid restored objective progress: " + node.id);
                }
            }
            unique.Clear(); foreach (var fact in value.facts) { if (fact == null || !unique.Add(fact.key) || !MissionData.Finite(fact.value)) throw new ArgumentException("Invalid mission fact."); MissionData.Id(fact.key); }
            unique.Clear(); foreach (var branch in value.branches) { if (branch == null || !unique.Add(branch.group)) throw new ArgumentException("Invalid branch."); MissionData.Id(branch.group); MissionData.Id(branch.choice); }
            bool terminal = value.state == MissionState.Succeeded || value.state == MissionState.Failed || value.state == MissionState.Aborted;
            if (terminal != value.hasResult || value.hasResult && value.result == null) throw new ArgumentException("Mission result/state mismatch.");
            if (value.hasResult && (value.result.missionId != value.missionId || value.result.claimId != value.claimId || value.result.outcome != value.state || value.result.cash < 0)) throw new ArgumentException("Invalid frozen mission result.");
            if (value.hasResult && (!MissionData.Finite(value.result.duration) || value.result.duration != value.elapsed || value.result.attempts != value.attempt
                || value.result.completed == null || value.result.failed == null || value.result.bonuses == null || value.result.grants == null || value.result.reputation < 0
                || value.state != MissionState.Succeeded && value.result.cash != 0)) throw new ArgumentException("Invalid result evidence.");
            if (!string.IsNullOrEmpty(value.checkpointJson))
            {
                MissionSnapshot checkpoint;
                try { checkpoint = CareerSaveCodec.Parse(value.checkpointJson).ToObject<MissionSnapshot>(); }
                catch (Exception exception) { throw new ArgumentException("Malformed checkpoint snapshot.", exception); }
                if (checkpoint == null || !string.IsNullOrEmpty(checkpoint.checkpointJson) || checkpoint.claimId != value.claimId || checkpoint.missionId != value.missionId || checkpoint.state != MissionState.Active)
                    throw new ArgumentException("Invalid recovery checkpoint ownership/state.");
                ValidateSnapshot(checkpoint, graph);
            }
        }
    }
}
