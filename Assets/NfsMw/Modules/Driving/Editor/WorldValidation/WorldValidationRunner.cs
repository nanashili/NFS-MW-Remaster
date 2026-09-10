using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.Rendering;

namespace NfsMwRemaster.Driving.Editor.WorldValidation
{
    /// <summary>A cancellable editor-thread validation run. One rule is scheduled per editor tick.</summary>
    public sealed class WorldValidationRunSession
    {
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly WorldValidationContext context;
        private readonly WorldValidationPolicy policy;
        private readonly List<IWorldValidationRule> rules;
        private readonly List<WorldValidationResult> results = new List<WorldValidationResult>();
        private int nextRule;
        private bool completed;
        private bool finalized;

        internal WorldValidationRunSession(WorldValidationRunRequest request)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Snapshot = WorldValidationScopeDiscovery.Capture(request);
            policy = WorldValidationPolicy.Find(request.policyAssetPath);
            context = new WorldValidationContext(request, Snapshot, policy, cancellation.Token);
            Report = new WorldValidationReport
            {
                runId = Guid.NewGuid().ToString("N"),
                startedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                pipeline = GraphicsSettingsName(),
                requestedScope = request.scope,
                scope = Snapshot.ToRecord(),
                registrationErrors = WorldValidationRuleRegistry.RegistrationErrors.ToArray(),
                omittedCategories = Enum.GetValues(typeof(WorldValidationCategory)).Cast<WorldValidationCategory>()
                    .Where(category => !request.Includes(category))
                    .Select(category => category.ToString())
                    .ToArray(),
                runStatus = WorldValidationStatus.Warning,
                partial = Snapshot.IsPartial
            };
            rules = BuildRulePlan(request);
            // Rule planning can add unsupported/omitted rule evidence. Rebuild
            // the immutable scope fingerprint after planning so the persisted
            // scope describes both inspected inputs and deliberate omissions.
            Snapshot.FinalizeSnapshot();
            Report.scope = Snapshot.ToRecord();
            Report.partial = Snapshot.IsPartial;
        }

        public WorldValidationRunRequest Request { get; }
        public WorldValidationScopeSnapshot Snapshot { get; }
        public WorldValidationReport Report { get; private set; }
        public bool IsComplete => completed;
        public float Progress => rules.Count == 0 ? (completed ? 1 : 0) : Mathf.Clamp01((float)nextRule / rules.Count);
        public int EvaluatedRuleCount => Math.Min(nextRule, rules.Count);
        public int TotalRuleCount => rules.Count;

        public void Cancel()
        {
            if (!completed) cancellation.Cancel();
        }

        /// <summary>Advances one rule. Call from EditorApplication.update or a batch loop.</summary>
        public bool Step()
        {
            if (completed) return true;

            if (cancellation.IsCancellationRequested || context.IsTimedOut)
            {
                CompleteRemainder(cancellation.IsCancellationRequested
                    ? WorldValidationStatus.Cancelled
                    : WorldValidationStatus.TimedOut);
                FinalizeReport();
                return true;
            }

            if (nextRule >= rules.Count)
            {
                FinalizeReport();
                return true;
            }

            IWorldValidationRule rule = rules[nextRule++];
            WorldValidationResultSink sink = new WorldValidationResultSink(context, rule.Descriptor);
            try
            {
                context.ThrowIfCancellationRequested();
                rule.Evaluate(context, sink);
                if (sink.Count == 0)
                {
                    sink.Add(WorldValidationResult.NotEvaluated(rule.Descriptor, "RULE_NO_RESULT",
                        "The provider returned no result for this scope; no clean conclusion was inferred."));
                }
                results.AddRange(sink.Values.Select(value => value.Clone()));
            }
            catch (OperationCanceledException)
            {
                results.Add(CreateControlResult(rule.Descriptor, WorldValidationStatus.Cancelled,
                    "RULE_CANCELLED", "Validation was cancelled before this rule completed."));
                CompleteRemainder(WorldValidationStatus.Cancelled);
            }
            catch (TimeoutException exception)
            {
                var timedOut = CreateControlResult(rule.Descriptor, WorldValidationStatus.TimedOut,
                    "RULE_TIMEOUT", "Validation exceeded the configured time budget: " + exception.Message);
                results.Add(timedOut);
                CompleteRemainder(WorldValidationStatus.TimedOut);
            }
            catch (Exception exception)
            {
                var failure = CreateControlResult(rule.Descriptor, WorldValidationStatus.ErrorRunning,
                    "RULE_EXCEPTION", "The validator threw " + exception.GetType().Name + ": " + exception.Message);
                failure.AddEvidence(WorldValidationEvidenceKind.Text, "Exception", exception.ToString());
                results.Add(failure);
            }

            if (!completed && nextRule >= rules.Count) FinalizeReport();
            return completed;
        }

        private List<IWorldValidationRule> BuildRulePlan(WorldValidationRunRequest request)
        {
            var selected = new Dictionary<string, IWorldValidationRule>(StringComparer.Ordinal);
            foreach (IWorldValidationRule rule in WorldValidationRuleRegistry.All)
            {
                if (rule == null || rule.Descriptor == null
                    || !request.Includes(rule.Descriptor.category)
                    || !request.IncludesRule(rule.Descriptor.id)) continue;

                if (!rule.Descriptor.Supports(request.scope))
                {
                    results.Add(WorldValidationResult.Unsupported(rule.Descriptor, "RULE_SCOPE_UNSUPPORTED",
                        "Rule scope unsupported", "The " + rule.Descriptor.displayName + " provider does not support " + request.scope + "."));
                    Snapshot.AddOmittedRule(rule.Descriptor.id + " (scope unsupported)");
                    continue;
                }

                if (!request.includeExpensive && rule.Descriptor.expectedCost == WorldValidationCost.Expensive)
                {
                    results.Add(WorldValidationResult.NotEvaluated(rule.Descriptor, "RULE_EXPENSIVE_OMITTED",
                        "The rule is marked Expensive and was omitted. Enable the Expensive option to run it."));
                    Snapshot.AddOmittedRule(rule.Descriptor.id + " (expensive disabled)");
                    continue;
                }

                selected[rule.Descriptor.id] = rule;
            }

            foreach (string requestedRule in request.ruleIds ?? Array.Empty<string>())
                if (!WorldValidationRuleRegistry.TryGet(requestedRule, out _))
                    ReportRegistrationError("Requested validation rule is not registered: " + requestedRule + ".");

            var ordered = new List<IWorldValidationRule>();
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);

            foreach (IWorldValidationRule rule in selected.Values.OrderBy(value => value.Descriptor.id, StringComparer.Ordinal))
                Visit(rule);

            // Visit adds dependencies before dependants. Do not sort this list
            // after the traversal: doing so can run a generated-data consumer
            // before the source rule it explicitly depends on.
            return ordered;

            void Visit(IWorldValidationRule rule)
            {
                string id = rule.Descriptor.id;
                if (visited.Contains(id)) return;
                if (!visiting.Add(id))
                {
                    ReportRegistrationError("Validation dependency cycle includes " + id + ".");
                    return;
                }
                foreach (string dependency in rule.Descriptor.dependencies ?? Array.Empty<string>())
                {
                    if (selected.TryGetValue(dependency, out IWorldValidationRule dependencyRule)) Visit(dependencyRule);
                    else if (WorldValidationRuleRegistry.TryGet(dependency, out _))
                        ReportRegistrationError("Rule " + id + " requires " + dependency + ", but that dependency is outside the requested rule/category set.");
                    else ReportRegistrationError("Rule " + id + " declares missing dependency " + dependency + ".");
                }
                visiting.Remove(id);
                visited.Add(id);
                if (!ordered.Contains(rule)) ordered.Add(rule);
            }
        }

        private void ReportRegistrationError(string message)
        {
            if (Report == null) return;
            var values = new List<string>(Report.registrationErrors ?? Array.Empty<string>()) { message };
            Report.registrationErrors = values.Distinct(StringComparer.Ordinal).ToArray();
        }

        private void CompleteRemainder(WorldValidationStatus status)
        {
            while (nextRule < rules.Count)
            {
                IWorldValidationRule rule = rules[nextRule++];
                results.Add(CreateControlResult(rule.Descriptor, status,
                    status == WorldValidationStatus.Cancelled ? "RULE_CANCELLED" : "RULE_TIMEOUT",
                    status == WorldValidationStatus.Cancelled
                        ? "Validation was cancelled before this rule started."
                        : "Validation timed out before this rule started."));
            }
        }

        private static WorldValidationResult CreateControlResult(WorldValidationRuleDescriptor descriptor,
            WorldValidationStatus status, string code, string message)
        {
            return WorldValidationResult.Create(descriptor, status, WorldValidationSeverity.Error, code,
                status == WorldValidationStatus.Cancelled ? "Cancelled" : "Timed out", message);
        }

        private void FinalizeReport()
        {
            if (finalized) return;
            finalized = true;
            completed = true;
            Report.results = results.ToArray();
            Report.scope = Snapshot.ToRecord();
            Report.partial |= Snapshot.IsPartial || results.Any(result => result.status == WorldValidationStatus.Cancelled
                || result.status == WorldValidationStatus.TimedOut);

            if (!Snapshot.IsCurrent())
            {
                Report.stale = true;
                Report.partial = true;
                foreach (WorldValidationResult result in Report.results)
                {
                    result.stale = true;
                    result.message += " Source changed during validation; rerun this scope before acting on it.";
                }
            }

            if (policy != null) policy.Apply(Report);
            else Report.NormalizeAndSummarize();
            Report.scope = Snapshot.ToRecord();
            Report.partial |= Snapshot.IsPartial;
            Report.finishedUtc = DateTime.UtcNow.ToString("O");

            if (!WorldValidationDependencyCache.TryUpdate(out string cacheFailure))
                Report.registrationErrors = (Report.registrationErrors ?? Array.Empty<string>())
                    .Concat(new[] { "Dependency index update failed: " + cacheFailure }).Distinct(StringComparer.Ordinal).ToArray();
            WorldValidationHistoryStore.TrySave(Report, out string historyFailure);
            if (!string.IsNullOrEmpty(historyFailure))
                Report.registrationErrors = (Report.registrationErrors ?? Array.Empty<string>())
                    .Concat(new[] { "History export failed: " + historyFailure }).Distinct(StringComparer.Ordinal).ToArray();

            // Preserve hard failures even when coverage is partial. Partial is
            // an additional truthfulness flag, not a reason to downgrade a
            // blocking finding or a runner/registration failure to Warning.
            if ((Report.registrationErrors != null && Report.registrationErrors.Length > 0) || Report.HasBlockingFindings)
                Report.runStatus = WorldValidationStatus.Failed;
            else if (Report.partial || Report.notEvaluated > 0 || Report.unsupported > 0 || Report.cancelled > 0 || Report.timedOut > 0)
                Report.runStatus = WorldValidationStatus.Warning;
            else
                Report.runStatus = WorldValidationStatus.Passed;
        }

        private static string GraphicsSettingsName()
        {
            try { return GraphicsSettings.currentRenderPipeline == null ? "Built-in" : GraphicsSettings.currentRenderPipeline.GetType().Name; }
            catch { return "Unknown"; }
        }
    }

    [InitializeOnLoad]
    public static class WorldValidationService
    {
        private static WorldValidationRunSession active;
        private static WorldValidationReport lastReport;

        static WorldValidationService()
        {
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += CancelForReload;
            EditorApplication.playModeStateChanged += HandlePlayMode;
            lastReport = WorldValidationHistoryStore.LoadLatest();
        }

        public static WorldValidationRunSession Active => active;
        public static WorldValidationReport LastReport => lastReport;
        public static bool IsRunning => active != null && !active.IsComplete;
        public static event Action<WorldValidationReport> ReportChanged;

        public static WorldValidationRunSession Start(WorldValidationRunRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            active?.Cancel();
            active = new WorldValidationRunSession(request);
            RaiseChanged(null);
            return active;
        }

        public static void Cancel()
        {
            active?.Cancel();
        }

        public static WorldValidationReport RunSynchronously(WorldValidationRunRequest request)
        {
            var session = new WorldValidationRunSession(request ?? throw new ArgumentNullException(nameof(request)));
            while (!session.IsComplete) session.Step();
            lastReport = session.Report;
            return lastReport;
        }

        private static void Tick()
        {
            if (active == null) return;
            try
            {
                if (active.Step())
                {
                    lastReport = active.Report;
                    WorldValidationRunSession finished = active;
                    active = null;
                    RaiseChanged(finished.Report);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                active.Cancel();
            }
        }

        private static void CancelForReload()
        {
            active?.Cancel();
            active = null;
        }

        private static void HandlePlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
                Cancel();
        }

        private static void RaiseChanged(WorldValidationReport report)
        {
            try { ReportChanged?.Invoke(report ?? lastReport); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
    }

    internal static class WorldValidationHistoryStore
    {
        private static string RootPath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "NfsMwRemaster", "WorldValidation", "history");

        public static bool TrySave(WorldValidationReport report, out string failure)
        {
            failure = string.Empty;
            try
            {
                Directory.CreateDirectory(RootPath);
                string name = (string.IsNullOrEmpty(report.finishedUtc) ? DateTime.UtcNow : DateTime.Parse(report.finishedUtc).ToUniversalTime())
                    .ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture) + "_" + report.runId + ".json";
                File.WriteAllText(Path.Combine(RootPath, name), WorldValidationReportSerializer.ToJson(report));
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
        }

        public static WorldValidationReport LoadLatest()
        {
            try
            {
                if (!Directory.Exists(RootPath)) return null;
                string path = Directory.GetFiles(RootPath, "*.json")
                    .OrderByDescending(value => value, StringComparer.Ordinal).FirstOrDefault();
                if (string.IsNullOrEmpty(path)) return null;
                return JsonUtility.FromJson<WorldValidationReport>(File.ReadAllText(path));
            }
            catch { return null; }
        }

        public static IReadOnlyList<string> ListPaths()
        {
            try
            {
                if (!Directory.Exists(RootPath)) return Array.Empty<string>();
                return Directory.GetFiles(RootPath, "*.json").OrderByDescending(value => value, StringComparer.Ordinal).ToArray();
            }
            catch { return Array.Empty<string>(); }
        }

        public static WorldValidationReport Load(string path)
        {
            try { return string.IsNullOrEmpty(path) ? null : JsonUtility.FromJson<WorldValidationReport>(File.ReadAllText(path)); }
            catch { return null; }
        }
    }
}
