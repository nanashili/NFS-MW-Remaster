using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor.WorldValidation
{
    /// <summary>World content areas represented by the dashboard.</summary>
    public enum WorldValidationCategory
    {
        Identity,
        Roads,
        TrafficPolice,
        RaceVehicle,
        EventsMissionsCareer,
        WorldArt,
        AudioLightingMaps,
        RuntimeScenario
    }

    /// <summary>Impact of a finding. Status describes whether the check actually ran.</summary>
    public enum WorldValidationSeverity
    {
        Info,
        Warning,
        Error,
        Blocker
    }

    public enum WorldValidationStatus
    {
        Passed,
        Failed,
        Warning,
        NotEvaluated,
        Unsupported,
        Cancelled,
        TimedOut,
        ErrorRunning,
        Suppressed
    }

    public enum WorldValidationScopeKind
    {
        SelectedObjects,
        DistrictCell,
        OpenScenes,
        ExplicitScenes,
        ChangedAssets,
        BuildContent,
        Project
    }

    public enum WorldValidationCost
    {
        Cheap,
        Standard,
        Expensive
    }

    public enum WorldValidationEvidenceKind
    {
        Text,
        Asset,
        SceneObject,
        Geometry,
        Dependency,
        Metric
    }

    public enum WorldValidationBaselineState
    {
        None,
        New,
        ExistingDebt,
        Resolved
    }

    public enum WorldValidationExitPolicy
    {
        NoBlockers,
        NoErrors,
        AllRequestedRulesEvaluated
    }

    [Serializable]
    public sealed class WorldValidationRuleDescriptor
    {
        public string id = string.Empty;
        public int version = 1;
        public string displayName = string.Empty;
        public string ownerModule = string.Empty;
        public WorldValidationCategory category;
        public WorldValidationScopeKind[] supportedScopes = Array.Empty<WorldValidationScopeKind>();
        public string[] requiredInputs = Array.Empty<string>();
        public WorldValidationCost expectedCost;
        public bool requiresMainThread = true;
        public bool supportsFixes;
        public string[] dependencies = Array.Empty<string>();
        public string runtimeRequirements = string.Empty;

        public bool Supports(WorldValidationScopeKind scope)
        {
            return supportedScopes != null && supportedScopes.Contains(scope);
        }

        public WorldValidationRuleDescriptor Clone()
        {
            return new WorldValidationRuleDescriptor
            {
                id = id,
                version = version,
                displayName = displayName,
                ownerModule = ownerModule,
                category = category,
                supportedScopes = (supportedScopes ?? Array.Empty<WorldValidationScopeKind>()).ToArray(),
                requiredInputs = (requiredInputs ?? Array.Empty<string>()).ToArray(),
                expectedCost = expectedCost,
                requiresMainThread = requiresMainThread,
                supportsFixes = supportsFixes,
                dependencies = (dependencies ?? Array.Empty<string>()).ToArray(),
                runtimeRequirements = runtimeRequirements
            };
        }
    }

    [Serializable]
    public sealed class WorldValidationEvidence
    {
        public WorldValidationEvidenceKind kind;
        public string label = string.Empty;
        public string value = string.Empty;
        public string assetPath = string.Empty;
        public string scenePath = string.Empty;
        public string globalObjectId = string.Empty;
        public string affectedId = string.Empty;
    }

    [Serializable]
    public sealed class WorldValidationResult
    {
        public string ruleId = string.Empty;
        public int ruleVersion;
        public string ruleName = string.Empty;
        public WorldValidationCategory category;
        public WorldValidationStatus status;
        public WorldValidationSeverity severity;
        public string diagnosticCode = string.Empty;
        public string title = string.Empty;
        public string message = string.Empty;
        public string ownerModule = string.Empty;
        public string assetPath = string.Empty;
        public string scenePath = string.Empty;
        public string globalObjectId = string.Empty;
        public string sourceRevision = string.Empty;
        public string location = string.Empty;
        public Vector3 worldPosition;
        public bool hasWorldPosition;
        public string[] affectedIds = Array.Empty<string>();
        public WorldValidationEvidence[] evidence = Array.Empty<WorldValidationEvidence>();
        public string originalStatus = string.Empty;
        public WorldValidationBaselineState baselineState;
        public bool heuristic;
        public bool empirical;
        public bool stale;
        public bool canNavigate;
        public bool canFix;
        public string fixId = string.Empty;
        public string fixRisk = string.Empty;
        public long createdUtcTicks;

        public string Key
        {
            get
            {
                string target = affectedIds != null && affectedIds.Length > 0
                    ? string.Join(",", affectedIds)
                    : globalObjectId + "|" + assetPath + "|" + scenePath;
                return ruleId + "|" + diagnosticCode + "|" + target;
            }
        }

        public bool IsBlocking
        {
            get
            {
                if (status == WorldValidationStatus.Suppressed) return false;
                if (status == WorldValidationStatus.ErrorRunning || status == WorldValidationStatus.TimedOut) return true;
                return status == WorldValidationStatus.Failed
                    && (severity == WorldValidationSeverity.Error || severity == WorldValidationSeverity.Blocker);
            }
        }

        public string DisplayTarget
        {
            get
            {
                if (!string.IsNullOrEmpty(scenePath)) return scenePath;
                if (!string.IsNullOrEmpty(assetPath)) return assetPath;
                return string.IsNullOrEmpty(globalObjectId) ? "Project" : globalObjectId;
            }
        }

        public static WorldValidationResult Create(
            WorldValidationRuleDescriptor descriptor,
            WorldValidationStatus resultStatus,
            WorldValidationSeverity resultSeverity,
            string code,
            string resultTitle,
            string resultMessage)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return new WorldValidationResult
            {
                ruleId = descriptor.id ?? string.Empty,
                ruleVersion = descriptor.version,
                ruleName = descriptor.displayName ?? string.Empty,
                category = descriptor.category,
                status = resultStatus,
                severity = resultSeverity,
                diagnosticCode = code ?? string.Empty,
                title = resultTitle ?? descriptor.displayName ?? string.Empty,
                message = resultMessage ?? string.Empty,
                ownerModule = descriptor.ownerModule ?? string.Empty,
                createdUtcTicks = DateTime.UtcNow.Ticks,
                affectedIds = Array.Empty<string>(),
                evidence = Array.Empty<WorldValidationEvidence>()
            };
        }

        public static WorldValidationResult Passed(WorldValidationRuleDescriptor descriptor, string message = "No issues found in the inspected scope.")
        {
            return Create(descriptor, WorldValidationStatus.Passed, WorldValidationSeverity.Info,
                "PASS", "Passed", message);
        }

        public static WorldValidationResult NotEvaluated(WorldValidationRuleDescriptor descriptor, string code, string message)
        {
            return Create(descriptor, WorldValidationStatus.NotEvaluated, WorldValidationSeverity.Warning,
                code, "Not evaluated", message);
        }

        public static WorldValidationResult Unsupported(WorldValidationRuleDescriptor descriptor, string code, string message)
        {
            return Create(descriptor, WorldValidationStatus.Unsupported, WorldValidationSeverity.Warning,
                code, "Unsupported", message);
        }

        public static WorldValidationResult Unsupported(WorldValidationRuleDescriptor descriptor, string code,
            string resultTitle, string message)
        {
            return Create(descriptor, WorldValidationStatus.Unsupported, WorldValidationSeverity.Warning,
                code, resultTitle, message);
        }

        public static WorldValidationResult Failed(WorldValidationRuleDescriptor descriptor, string code, string title, string message,
            WorldValidationSeverity resultSeverity = WorldValidationSeverity.Error)
        {
            return Create(descriptor, WorldValidationStatus.Failed, resultSeverity, code, title, message);
        }

        public static WorldValidationResult Warning(WorldValidationRuleDescriptor descriptor, string code, string title, string message)
        {
            return Create(descriptor, WorldValidationStatus.Warning, WorldValidationSeverity.Warning, code, title, message);
        }

        public void SetTarget(UnityEngine.Object target)
        {
            if (target == null) return;
            try
            {
                string path = AssetDatabase.GetAssetPath(target);
                if (!string.IsNullOrEmpty(path)) assetPath = path;
                if (target is Component component && component.gameObject.scene.IsValid())
                {
                    scenePath = component.gameObject.scene.path;
                    worldPosition = component.transform.position;
                    hasWorldPosition = true;
                }
                if (target is GameObject gameObject && gameObject.scene.IsValid())
                {
                    scenePath = gameObject.scene.path;
                    worldPosition = gameObject.transform.position;
                    hasWorldPosition = true;
                }
                GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(target);
                globalObjectId = id.ToString();
                canNavigate = !string.IsNullOrEmpty(globalObjectId) || !string.IsNullOrEmpty(assetPath) || !string.IsNullOrEmpty(scenePath);
            }
            catch (Exception)
            {
                canNavigate = !string.IsNullOrEmpty(assetPath) || !string.IsNullOrEmpty(scenePath);
            }
        }

        public void SetTarget(string asset, string scene, string globalId)
        {
            assetPath = asset ?? string.Empty;
            scenePath = scene ?? string.Empty;
            globalObjectId = globalId ?? string.Empty;
            canNavigate = !string.IsNullOrEmpty(assetPath) || !string.IsNullOrEmpty(scenePath) || !string.IsNullOrEmpty(globalObjectId);
        }

        public void AddAffectedId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            var values = new HashSet<string>(affectedIds ?? Array.Empty<string>(), StringComparer.Ordinal) { id };
            affectedIds = values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        public void AddEvidence(WorldValidationEvidenceKind kind, string label, string value,
            string affectedId = "", string asset = "", string scene = "", string globalId = "")
        {
            var list = new List<WorldValidationEvidence>(evidence ?? Array.Empty<WorldValidationEvidence>())
            {
                new WorldValidationEvidence
                {
                    kind = kind,
                    label = label ?? string.Empty,
                    value = value ?? string.Empty,
                    affectedId = affectedId ?? string.Empty,
                    assetPath = asset ?? string.Empty,
                    scenePath = scene ?? string.Empty,
                    globalObjectId = globalId ?? string.Empty
                }
            };
            evidence = list.ToArray();
        }

        public WorldValidationResult Clone()
        {
            return new WorldValidationResult
            {
                ruleId = ruleId,
                ruleVersion = ruleVersion,
                ruleName = ruleName,
                category = category,
                status = status,
                severity = severity,
                diagnosticCode = diagnosticCode,
                title = title,
                message = message,
                ownerModule = ownerModule,
                assetPath = assetPath,
                scenePath = scenePath,
                globalObjectId = globalObjectId,
                sourceRevision = sourceRevision,
                location = location,
                worldPosition = worldPosition,
                hasWorldPosition = hasWorldPosition,
                affectedIds = (affectedIds ?? Array.Empty<string>()).ToArray(),
                evidence = (evidence ?? Array.Empty<WorldValidationEvidence>()).Select(CloneEvidence).ToArray(),
                originalStatus = originalStatus,
                baselineState = baselineState,
                heuristic = heuristic,
                empirical = empirical,
                stale = stale,
                canNavigate = canNavigate,
                canFix = canFix,
                fixId = fixId,
                fixRisk = fixRisk,
                createdUtcTicks = createdUtcTicks
            };
        }

        private static WorldValidationEvidence CloneEvidence(WorldValidationEvidence source)
        {
            return new WorldValidationEvidence
            {
                kind = source.kind,
                label = source.label,
                value = source.value,
                assetPath = source.assetPath,
                scenePath = source.scenePath,
                globalObjectId = source.globalObjectId,
                affectedId = source.affectedId
            };
        }
    }

    [Serializable]
    public sealed class WorldValidationScopeRecord
    {
        public WorldValidationScopeKind requested;
        public string[] scannedScenes = Array.Empty<string>();
        public string[] scannedAssets = Array.Empty<string>();
        public string[] omittedAssets = Array.Empty<string>();
        public string[] selectedObjects = Array.Empty<string>();
        public string[] districtIds = Array.Empty<string>();
        public string[] cellIds = Array.Empty<string>();
        public string[] unavailable = Array.Empty<string>();
        public string[] omittedRules = Array.Empty<string>();
        public string fingerprint = string.Empty;
        public bool partial;
    }

    [Serializable]
    public sealed class WorldValidationCoverage
    {
        public WorldValidationCategory category;
        public int evaluated;
        public int notEvaluated;
        public int unsupported;
        public int errors;
        public string reason = string.Empty;
    }

    [Serializable]
    public sealed class WorldValidationReport
    {
        public const int CurrentSchema = 1;
        public int schema = CurrentSchema;
        public string runId = string.Empty;
        public string startedUtc = string.Empty;
        public string finishedUtc = string.Empty;
        public string unityVersion = string.Empty;
        public string pipeline = string.Empty;
        public WorldValidationScopeKind requestedScope;
        public WorldValidationScopeRecord scope = new WorldValidationScopeRecord();
        public WorldValidationResult[] results = Array.Empty<WorldValidationResult>();
        public WorldValidationCoverage[] coverage = Array.Empty<WorldValidationCoverage>();
        public string[] omittedCategories = Array.Empty<string>();
        public string[] registrationErrors = Array.Empty<string>();
        public int passed;
        public int failed;
        public int warnings;
        public int notEvaluated;
        public int unsupported;
        public int cancelled;
        public int timedOut;
        public int executionErrors;
        public int suppressed;
        public int baselineDebt;
        public string[] resolvedBaselineKeys = Array.Empty<string>();
        public bool partial;
        public bool stale;
        public WorldValidationStatus runStatus;

        public bool HasBlockingFindings => (results ?? Array.Empty<WorldValidationResult>()).Any(result => result != null && result.IsBlocking);

        public void NormalizeAndSummarize()
        {
            var ordered = (results ?? Array.Empty<WorldValidationResult>())
                .Where(result => result != null)
                .OrderBy(result => result.category)
                .ThenByDescending(result => result.severity)
                .ThenBy(result => result.status)
                .ThenBy(result => result.ruleId, StringComparer.Ordinal)
                .ThenBy(result => result.assetPath, StringComparer.Ordinal)
                .ThenBy(result => result.scenePath, StringComparer.Ordinal)
                .ThenBy(result => result.diagnosticCode, StringComparer.Ordinal)
                .ThenBy(result => result.message, StringComparer.Ordinal)
                .ToArray();
            results = ordered;
            passed = failed = warnings = notEvaluated = unsupported = cancelled = timedOut = executionErrors = suppressed = baselineDebt = 0;
            foreach (var result in results)
            {
                switch (result.status)
                {
                    case WorldValidationStatus.Passed: passed++; break;
                    case WorldValidationStatus.Failed: failed++; break;
                    case WorldValidationStatus.Warning: warnings++; break;
                    case WorldValidationStatus.NotEvaluated: notEvaluated++; break;
                    case WorldValidationStatus.Unsupported: unsupported++; break;
                    case WorldValidationStatus.Cancelled: cancelled++; break;
                    case WorldValidationStatus.TimedOut: timedOut++; break;
                    case WorldValidationStatus.ErrorRunning: executionErrors++; break;
                    case WorldValidationStatus.Suppressed: suppressed++; break;
                }
                if (result.baselineState == WorldValidationBaselineState.ExistingDebt) baselineDebt++;
                if (result.stale) stale = true;
            }
            BuildCoverage();
        }

        private void BuildCoverage()
        {
            var values = new List<WorldValidationCoverage>();
            foreach (WorldValidationCategory category in Enum.GetValues(typeof(WorldValidationCategory)))
            {
                var categoryResults = results.Where(result => result.category == category).ToArray();
                var entry = new WorldValidationCoverage
                {
                    category = category,
                    evaluated = categoryResults.Count(result => result.status == WorldValidationStatus.Passed
                        || result.status == WorldValidationStatus.Failed
                        || result.status == WorldValidationStatus.Warning
                        || result.status == WorldValidationStatus.Suppressed),
                    notEvaluated = categoryResults.Count(result => result.status == WorldValidationStatus.NotEvaluated),
                    unsupported = categoryResults.Count(result => result.status == WorldValidationStatus.Unsupported),
                    errors = categoryResults.Count(result => result.IsBlocking),
                    reason = categoryResults.Length == 0 ? "Category was not selected or no provider registered." : string.Empty
                };
                values.Add(entry);
            }
            coverage = values.ToArray();
        }
    }

    [Serializable]
    public sealed class WorldValidationRunRequest
    {
        public WorldValidationScopeKind scope = WorldValidationScopeKind.OpenScenes;
        public string[] explicitScenePaths = Array.Empty<string>();
        public string districtId = string.Empty;
        public string[] cellIds = Array.Empty<string>();
        public WorldValidationCategory[] categories = (WorldValidationCategory[])Enum.GetValues(typeof(WorldValidationCategory));
        public string[] ruleIds = Array.Empty<string>();
        public bool incremental;
        public bool includeExpensive;
        public bool includeInfo = true;
        public float timeoutSeconds;
        public WorldValidationExitPolicy exitPolicy = WorldValidationExitPolicy.NoBlockers;
        public string policyAssetPath = string.Empty;

        public static WorldValidationRunRequest Default()
        {
            return new WorldValidationRunRequest
            {
                scope = WorldValidationScopeKind.OpenScenes,
                categories = (WorldValidationCategory[])Enum.GetValues(typeof(WorldValidationCategory)),
                explicitScenePaths = Array.Empty<string>(),
                districtId = string.Empty,
                cellIds = Array.Empty<string>(),
                ruleIds = Array.Empty<string>(),
                incremental = false,
                includeExpensive = false,
                includeInfo = true,
                timeoutSeconds = 0,
                exitPolicy = WorldValidationExitPolicy.NoBlockers
            };
        }

        public bool Includes(WorldValidationCategory category)
        {
            return categories == null || categories.Length == 0 || categories.Contains(category);
        }

        public bool IncludesRule(string id)
        {
            return ruleIds == null || ruleIds.Length == 0 || ruleIds.Contains(id, StringComparer.Ordinal);
        }
    }

    /// <summary>Read-only snapshot of a run's discovered content. Unity objects are intentionally not persisted.</summary>
    public sealed class WorldValidationScopeSnapshot
    {
        private readonly List<string> scenePaths = new List<string>();
        private readonly List<string> assetPaths = new List<string>();
        private readonly List<string> omittedAssetPaths = new List<string>();
        private readonly List<string> selectedObjectIds = new List<string>();
        private readonly List<string> districtIds = new List<string>();
        private readonly List<string> cellIds = new List<string>();
        private readonly List<string> unavailable = new List<string>();
        private readonly List<string> omittedRules = new List<string>();
        private readonly Dictionary<string, string> sourceRevisions = new Dictionary<string, string>(StringComparer.Ordinal);

        public WorldValidationScopeKind Requested { get; }
        public IReadOnlyList<string> ScenePaths => scenePaths;
        public IReadOnlyList<string> AssetPaths => assetPaths;
        public IReadOnlyList<string> OmittedAssetPaths => omittedAssetPaths;
        public IReadOnlyList<string> SelectedObjectIds => selectedObjectIds;
        public IReadOnlyList<string> DistrictIds => districtIds;
        public IReadOnlyList<string> CellIds => cellIds;
        public IReadOnlyList<string> Unavailable => unavailable;
        public string Fingerprint { get; private set; } = string.Empty;
        public bool IsPartial => unavailable.Count > 0 || omittedAssetPaths.Count > 0 || omittedRules.Count > 0;

        internal WorldValidationScopeSnapshot(WorldValidationScopeKind requested)
        {
            Requested = requested;
        }

        internal void AddScene(string path)
        {
            if (!string.IsNullOrEmpty(path) && !scenePaths.Contains(path, StringComparer.Ordinal)) scenePaths.Add(path);
        }

        internal void AddAsset(string path)
        {
            if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal)
                && !assetPaths.Contains(path, StringComparer.Ordinal)) assetPaths.Add(path);
        }

        internal void AddSelectedObject(string id)
        {
            if (!string.IsNullOrEmpty(id) && !selectedObjectIds.Contains(id, StringComparer.Ordinal)) selectedObjectIds.Add(id);
        }

        internal void AddDistrict(string id)
        {
            if (!string.IsNullOrEmpty(id) && !districtIds.Contains(id, StringComparer.Ordinal)) districtIds.Add(id);
        }

        internal void AddCell(string districtId, Vector2Int cell)
        {
            if (string.IsNullOrEmpty(districtId)) return;
            string value = WorldValidationContext.CellKey(districtId, cell);
            if (!cellIds.Contains(value, StringComparer.Ordinal)) cellIds.Add(value);
        }

        internal void AddUnavailable(string value)
        {
            if (!string.IsNullOrEmpty(value) && !unavailable.Contains(value, StringComparer.Ordinal)) unavailable.Add(value);
        }

        internal void AddOmittedRule(string value)
        {
            if (!string.IsNullOrEmpty(value) && !omittedRules.Contains(value, StringComparer.Ordinal)) omittedRules.Add(value);
        }

        internal void SetRevision(string path, string revision)
        {
            if (!string.IsNullOrEmpty(path)) sourceRevisions[path] = revision ?? string.Empty;
        }

        /// <summary>
        /// Retains only inputs that the dependency index says are changed or
        /// depend on changed inputs. The omitted list is persisted so an
        /// incremental run remains explainable instead of looking like a full
        /// scan with fewer results.
        /// </summary>
        internal void RetainChanged(IReadOnlyCollection<string> changedPaths)
        {
            var changed = new HashSet<string>(changedPaths ?? Array.Empty<string>(), StringComparer.Ordinal);
            foreach (string path in assetPaths.ToArray())
            {
                if (changed.Contains(path)) continue;
                assetPaths.Remove(path);
                if (!omittedAssetPaths.Contains(path, StringComparer.Ordinal)) omittedAssetPaths.Add(path);
            }

            foreach (string path in scenePaths.ToArray())
            {
                if (changed.Contains(path)) continue;
                scenePaths.Remove(path);
                if (!omittedAssetPaths.Contains(path, StringComparer.Ordinal)) omittedAssetPaths.Add(path);
            }

            foreach (string path in sourceRevisions.Keys.Where(path => !assetPaths.Contains(path, StringComparer.Ordinal)).ToArray())
                sourceRevisions.Remove(path);

            if (changed.Count == 0 || (assetPaths.Count == 0 && scenePaths.Count == 0))
                AddUnavailable("Incremental scan found no changed inputs in the requested scope; no domain rule was run against omitted content.");
        }

        public string RevisionFor(string path)
        {
            return path != null && sourceRevisions.TryGetValue(path, out string value) ? value : string.Empty;
        }

        internal void FinalizeSnapshot()
        {
            scenePaths.Sort(StringComparer.Ordinal);
            assetPaths.Sort(StringComparer.Ordinal);
            omittedAssetPaths.Sort(StringComparer.Ordinal);
            selectedObjectIds.Sort(StringComparer.Ordinal);
            districtIds.Sort(StringComparer.Ordinal);
            cellIds.Sort(StringComparer.Ordinal);
            unavailable.Sort(StringComparer.Ordinal);
            omittedRules.Sort(StringComparer.Ordinal);
            string value = Requested + "|" + string.Join(";", scenePaths) + "|" + string.Join(";", assetPaths)
                + "|" + string.Join(";", omittedAssetPaths) + "|" + string.Join(";", omittedRules)
                + "|" + string.Join(";", selectedObjectIds) + "|" + string.Join(";", districtIds)
                + "|" + string.Join(";", cellIds) + "|" + string.Join(";", sourceRevisions.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value));
            Fingerprint = Hash128.Compute(value).ToString();
        }

        internal WorldValidationScopeRecord ToRecord()
        {
            return new WorldValidationScopeRecord
            {
                requested = Requested,
                scannedScenes = scenePaths.ToArray(),
                scannedAssets = assetPaths.ToArray(),
                omittedAssets = omittedAssetPaths.ToArray(),
                selectedObjects = selectedObjectIds.ToArray(),
                districtIds = districtIds.ToArray(),
                cellIds = cellIds.ToArray(),
                unavailable = unavailable.ToArray(),
                omittedRules = omittedRules.ToArray(),
                fingerprint = Fingerprint,
                partial = IsPartial
            };
        }

        internal bool IsCurrent()
        {
            foreach (var pair in sourceRevisions)
            {
                string current = WorldValidationFingerprint.AssetRevision(pair.Key);
                if (!string.Equals(current, pair.Value, StringComparison.Ordinal)) return false;
            }
            return true;
        }
    }

    public interface IWorldValidationRule
    {
        WorldValidationRuleDescriptor Descriptor { get; }
        void Evaluate(WorldValidationContext context, WorldValidationResultSink results);
    }

    public interface IWorldValidationFixProvider
    {
        string Id { get; }
        bool CanHandle(WorldValidationResult result);
        WorldValidationFixPreview Preview(WorldValidationResult result);
        bool TryApply(WorldValidationResult result, out string failure);
    }

    [Serializable]
    public sealed class WorldValidationFixPreview
    {
        public string fixId = string.Empty;
        public string title = string.Empty;
        public string rationale = string.Empty;
        public string risk = string.Empty;
        public string rollback = string.Empty;
        public string[] affectedIds = Array.Empty<string>();
        public bool mutatesContent;
    }

    /// <summary>Context passed to providers. All Unity API access stays on the editor thread.</summary>
    public sealed class WorldValidationContext
    {
        private readonly CancellationToken cancellation;
        private readonly DateTime deadlineUtc;

        internal WorldValidationContext(WorldValidationRunRequest request, WorldValidationScopeSnapshot snapshot,
            WorldValidationPolicy policy, CancellationToken cancellation)
        {
            Request = request;
            Scope = snapshot;
            Policy = policy;
            this.cancellation = cancellation;
            deadlineUtc = request.timeoutSeconds > 0
                ? DateTime.UtcNow.AddSeconds(request.timeoutSeconds)
                : DateTime.MaxValue;
        }

        public WorldValidationRunRequest Request { get; }
        public WorldValidationScopeSnapshot Scope { get; }
        public WorldValidationPolicy Policy { get; }
        public IReadOnlyList<string> DistrictIds => Scope.DistrictIds;
        public IReadOnlyList<string> CellIds => Scope.CellIds;
        public bool IsDistrictCellScope => Request.scope == WorldValidationScopeKind.DistrictCell;
        public bool IncludeExpensive => Request.includeExpensive;
        public bool IsCancellationRequested => cancellation.IsCancellationRequested;
        public bool IsTimedOut => DateTime.UtcNow >= deadlineUtc;

        public void ThrowIfCancellationRequested()
        {
            cancellation.ThrowIfCancellationRequested();
            if (IsTimedOut) throw new TimeoutException("World validation time budget expired.");
        }

        public IEnumerable<T> FindAssets<T>() where T : UnityEngine.Object
        {
            foreach (string path in Scope.AssetPaths)
            {
                ThrowIfCancellationRequested();
                UnityEngine.Object[] values;
                try { values = AssetDatabase.LoadAllAssetsAtPath(path); }
                catch (Exception exception)
                {
                    Scope.AddUnavailable(path + " — " + exception.Message);
                    continue;
                }
                foreach (UnityEngine.Object value in values ?? Array.Empty<UnityEngine.Object>())
                    if (value is T typed) yield return typed;
            }
        }

        public void InspectScenes(Action<Scene> inspect)
        {
            if (inspect == null) throw new ArgumentNullException(nameof(inspect));
            Scene active = SceneManager.GetActiveScene();
            foreach (string path in Scope.ScenePaths)
            {
                ThrowIfCancellationRequested();
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = false;
                try
                {
                    if (!scene.IsValid() || !scene.isLoaded)
                    {
                        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                        {
                            Scope.AddUnavailable(path + " — scene asset is unavailable");
                            continue;
                        }
                        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                        opened = true;
                    }
                    inspect(scene);
                }
                catch (OperationCanceledException) { throw; }
                catch (TimeoutException) { throw; }
                catch (Exception exception)
                {
                    Scope.AddUnavailable(path + " — scene inspection failed: " + exception.Message);
                }
                finally
                {
                    if (opened && scene.IsValid() && scene.isLoaded)
                    {
                        try { EditorSceneManager.CloseScene(scene, true); }
                        catch (Exception exception) { Scope.AddUnavailable(path + " — could not close temporary scene: " + exception.Message); }
                    }
                    if (active.IsValid() && active.isLoaded && SceneManager.GetActiveScene() != active)
                    {
                        try { SceneManager.SetActiveScene(active); } catch (Exception) { }
                    }
                }
            }
        }

        public bool IncludesDistrict(CityDistrict district)
        {
            if (district == null) return false;
            if (!IsDistrictCellScope || DistrictIds.Count == 0) return true;
            return DistrictIds.Contains(district.id ?? string.Empty, StringComparer.Ordinal);
        }

        public bool IncludesCell(CityDistrict district, Vector2Int cell)
        {
            if (district == null || !IncludesDistrict(district)) return false;
            if (!IsDistrictCellScope || CellIds.Count == 0) return true;
            return CellIds.Contains(CellKey(district.id, cell), StringComparer.Ordinal);
        }

        public static string CellKey(string districtId, Vector2Int cell)
        {
            return (districtId ?? string.Empty) + "|" + cell.x + "," + cell.y;
        }

        public string AssetRevision(string path)
        {
            return WorldValidationFingerprint.AssetRevision(path);
        }
    }

    public sealed class WorldValidationResultSink
    {
        private readonly WorldValidationContext context;
        private readonly WorldValidationRuleDescriptor descriptor;
        private readonly List<WorldValidationResult> values = new List<WorldValidationResult>();

        internal WorldValidationResultSink(WorldValidationContext context, WorldValidationRuleDescriptor descriptor)
        {
            this.context = context;
            this.descriptor = descriptor;
        }

        public int Count => values.Count;
        internal IReadOnlyList<WorldValidationResult> Values => values;

        public void Add(WorldValidationResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            result.ruleId = string.IsNullOrEmpty(result.ruleId) ? descriptor.id : result.ruleId;
            result.ruleVersion = result.ruleVersion == 0 ? descriptor.version : result.ruleVersion;
            result.ruleName = string.IsNullOrEmpty(result.ruleName) ? descriptor.displayName : result.ruleName;
            result.category = descriptor.category;
            result.ownerModule = string.IsNullOrEmpty(result.ownerModule) ? descriptor.ownerModule : result.ownerModule;
            if (string.IsNullOrEmpty(result.sourceRevision))
                result.sourceRevision = context.Scope.RevisionFor(!string.IsNullOrEmpty(result.assetPath) ? result.assetPath : result.scenePath);
            if (!result.canNavigate)
                result.canNavigate = !string.IsNullOrEmpty(result.assetPath) || !string.IsNullOrEmpty(result.scenePath) || !string.IsNullOrEmpty(result.globalObjectId);
            if (result.createdUtcTicks == 0) result.createdUtcTicks = DateTime.UtcNow.Ticks;
            values.Add(result);
        }

        public void AddTarget(WorldValidationResult result, UnityEngine.Object target)
        {
            result.SetTarget(target);
            Add(result);
        }

        public void AddAffected(WorldValidationResult result, string affectedId, string evidence = "")
        {
            result.AddAffectedId(affectedId);
            if (!string.IsNullOrEmpty(evidence)) result.AddEvidence(WorldValidationEvidenceKind.Text, "Affected ID", evidence, affectedId);
            Add(result);
        }
    }
}
