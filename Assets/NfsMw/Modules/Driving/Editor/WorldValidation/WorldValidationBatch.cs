using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.WorldValidation
{
    /// <summary>
    /// Batch entry point. Example:
    /// Unity -batchmode -projectPath . -executeMethod NfsMwRemaster.Driving.Editor.WorldValidation.WorldValidationBatch.Run -worldValidationScope BuildContent -worldValidationOutput Reports/world-validation.json -quit
    /// </summary>
    public static class WorldValidationBatch
    {
        public static void Run()
        {
            try
            {
                WorldValidationRunRequest request = ParseRequest(Environment.GetCommandLineArgs());
                WorldValidationReport report = WorldValidationService.RunSynchronously(request);
                string jsonPath = Value(Environment.GetCommandLineArgs(), "-worldValidationOutput");
                string markdownPath = Value(Environment.GetCommandLineArgs(), "-worldValidationMarkdown");
                if (string.IsNullOrEmpty(jsonPath) && string.IsNullOrEmpty(markdownPath))
                {
                    string outputRoot = System.IO.Path.Combine("Library", "NfsMwRemaster", "WorldValidation", "batch-report.json");
                    jsonPath = outputRoot;
                }
                if (!WorldValidationReportSerializer.TryWrite(jsonPath, markdownPath, report, out string failure))
                {
                    Debug.LogError("World validation report export failed: " + failure);
                    Exit(2);
                    return;
                }
                Debug.Log("World validation completed: " + report.runStatus + " | blockers=" + report.results.Count(result => result != null && result.IsBlocking)
                    + " | notEvaluated=" + report.notEvaluated + " | unsupported=" + report.unsupported + " | partial=" + report.partial);
                Exit(ExitCode(report, request.exitPolicy));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Exit(2);
            }
        }

        public static WorldValidationRunRequest ParseRequest(IReadOnlyList<string> arguments)
        {
            var request = WorldValidationRunRequest.Default();
            string scope = Value(arguments, "-worldValidationScope");
            if (!string.IsNullOrEmpty(scope) && Enum.TryParse(scope, true, out WorldValidationScopeKind parsedScope)) request.scope = parsedScope;
            string categories = Value(arguments, "-worldValidationCategories");
            if (!string.IsNullOrEmpty(categories)) request.categories = ParseEnumList<WorldValidationCategory>(categories);
            string rules = Value(arguments, "-worldValidationRules");
            if (!string.IsNullOrEmpty(rules)) request.ruleIds = Split(rules);
            string scenes = Value(arguments, "-worldValidationScenes");
            if (!string.IsNullOrEmpty(scenes)) request.explicitScenePaths = Split(scenes);
            string districtId = Value(arguments, "-worldValidationDistrictId");
            if (!string.IsNullOrEmpty(districtId)) request.districtId = districtId.Trim();
            string cells = Value(arguments, "-worldValidationCells");
            if (!string.IsNullOrEmpty(cells)) request.cellIds = WorldValidationScopeDiscovery.ParseCellTokens(cells);
            string policy = Value(arguments, "-worldValidationPolicy");
            if (!string.IsNullOrEmpty(policy)) request.policyAssetPath = policy;
            string timeout = Value(arguments, "-worldValidationTimeout");
            if (float.TryParse(timeout, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds))
                request.timeoutSeconds = Mathf.Max(0, seconds);
            string exitPolicy = Value(arguments, "-worldValidationExitPolicy");
            if (!string.IsNullOrEmpty(exitPolicy) && Enum.TryParse(exitPolicy, true, out WorldValidationExitPolicy parsedExit)) request.exitPolicy = parsedExit;
            request.includeExpensive = Has(arguments, "-worldValidationExpensive");
            request.incremental = Has(arguments, "-worldValidationIncremental");
            request.includeInfo = !Has(arguments, "-worldValidationNoInfo");
            return request;
        }

        public static int ExitCode(WorldValidationReport report, WorldValidationExitPolicy policy)
        {
            if (report == null) return 2;
            bool hasRegistrationErrors = report.registrationErrors != null && report.registrationErrors.Length > 0;
            switch (policy)
            {
                case WorldValidationExitPolicy.NoErrors:
                    return hasRegistrationErrors || report.failed > 0 || report.executionErrors > 0 || report.timedOut > 0 ? 1 : 0;
                case WorldValidationExitPolicy.AllRequestedRulesEvaluated:
                    return report.notEvaluated > 0 || report.unsupported > 0 || report.cancelled > 0 || report.timedOut > 0
                        || report.executionErrors > 0 || hasRegistrationErrors ? 1 : 0;
                default:
                    return hasRegistrationErrors || report.HasBlockingFindings ? 1 : 0;
            }
        }

        private static string Value(IReadOnlyList<string> arguments, string key)
        {
            if (arguments == null) return string.Empty;
            for (int i = 0; i + 1 < arguments.Count; i++)
                if (string.Equals(arguments[i], key, StringComparison.Ordinal)) return arguments[i + 1];
            return string.Empty;
        }

        private static bool Has(IReadOnlyList<string> arguments, string key)
        {
            return arguments != null && arguments.Any(value => string.Equals(value, key, StringComparison.Ordinal));
        }

        private static string[] Split(string value)
        {
            return (value ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim()).Where(item => item.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        }

        private static T[] ParseEnumList<T>(string value) where T : struct
        {
            return Split(value).Where(item => Enum.TryParse(item, true, out T _))
                .Select(item => (T)Enum.Parse(typeof(T), item, true)).ToArray();
        }

        private static void Exit(int code)
        {
            if (Application.isBatchMode) EditorApplication.Exit(code);
            else Debug.Log("World validation exit code: " + code);
        }
    }
}
