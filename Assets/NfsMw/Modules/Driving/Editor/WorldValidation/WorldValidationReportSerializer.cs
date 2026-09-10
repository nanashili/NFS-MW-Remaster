using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.WorldValidation
{
    public static class WorldValidationReportSerializer
    {
        public static string ToJson(WorldValidationReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            report.NormalizeAndSummarize();
            return JsonUtility.ToJson(report, true);
        }

        public static string ToMarkdown(WorldValidationReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            report.NormalizeAndSummarize();
            var text = new StringBuilder();
            text.AppendLine("# World Validation Report");
            text.AppendLine();
            text.AppendLine("- Run: `" + report.runId + "`");
            text.AppendLine("- Scope: `" + report.requestedScope + "`");
            text.AppendLine("- Status: **" + report.runStatus + "**");
            text.AppendLine("- Started: `" + report.startedUtc + "`");
            text.AppendLine("- Finished: `" + report.finishedUtc + "`");
            text.AppendLine("- Unity: `" + report.unityVersion + "`");
            text.AppendLine();
            text.AppendLine("## Summary");
            text.AppendLine();
            text.AppendLine("| Passed | Failed | Warnings | Not evaluated | Unsupported | Cancelled | Timed out | Execution errors | Suppressed | Existing debt |");
            text.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            text.AppendLine("| " + report.passed + " | " + report.failed + " | " + report.warnings + " | " + report.notEvaluated + " | "
                + report.unsupported + " | " + report.cancelled + " | " + report.timedOut + " | " + report.executionErrors + " | "
                + report.suppressed + " | " + report.baselineDebt + " |");
            text.AppendLine();
            text.AppendLine("Partial: **" + report.partial + "** · Stale: **" + report.stale + "**");
            text.AppendLine();
            text.AppendLine("## Coverage");
            text.AppendLine();
            text.AppendLine("| Category | Evaluated | Not evaluated | Unsupported | Blocking | Reason |");
            text.AppendLine("| --- | ---: | ---: | ---: | ---: | --- |");
            foreach (WorldValidationCoverage coverage in report.coverage ?? Array.Empty<WorldValidationCoverage>())
                text.AppendLine("| " + coverage.category + " | " + coverage.evaluated + " | " + coverage.notEvaluated + " | "
                    + coverage.unsupported + " | " + coverage.errors + " | " + Escape(coverage.reason) + " |");
            text.AppendLine();

            if (report.scope != null)
            {
                text.AppendLine("## Scope evidence");
                text.AppendLine();
                text.AppendLine("- Scenes scanned: " + (report.scope.scannedScenes?.Length ?? 0));
                text.AppendLine("- Assets scanned: " + (report.scope.scannedAssets?.Length ?? 0));
                text.AppendLine("- Assets omitted by incremental scope: " + (report.scope.omittedAssets?.Length ?? 0));
                text.AppendLine("- Selected objects: " + (report.scope.selectedObjects?.Length ?? 0));
                text.AppendLine("- Districts: " + (report.scope.districtIds?.Length ?? 0));
                text.AppendLine("- Cells: " + (report.scope.cellIds?.Length ?? 0));
                if (report.scope.districtIds != null && report.scope.districtIds.Length > 0)
                {
                    text.AppendLine("- District IDs:");
                    foreach (string value in report.scope.districtIds.OrderBy(value => value, StringComparer.Ordinal)) text.AppendLine("  - " + Escape(value));
                }
                if (report.scope.cellIds != null && report.scope.cellIds.Length > 0)
                {
                    text.AppendLine("- Cell IDs:");
                    foreach (string value in report.scope.cellIds.OrderBy(value => value, StringComparer.Ordinal)) text.AppendLine("  - " + Escape(value));
                }
                if (report.scope.unavailable != null && report.scope.unavailable.Length > 0)
                {
                    text.AppendLine("- Unavailable/omitted:");
                    foreach (string value in report.scope.unavailable.OrderBy(value => value, StringComparer.Ordinal)) text.AppendLine("  - " + Escape(value));
                }
                if (report.scope.omittedAssets != null && report.scope.omittedAssets.Length > 0)
                {
                    text.AppendLine("- Incremental assets not inspected:");
                    foreach (string value in report.scope.omittedAssets.OrderBy(value => value, StringComparer.Ordinal)) text.AppendLine("  - " + Escape(value));
                }
                if (report.scope.omittedRules != null && report.scope.omittedRules.Length > 0)
                {
                    text.AppendLine("- Rules omitted or unsupported:");
                    foreach (string value in report.scope.omittedRules.OrderBy(value => value, StringComparer.Ordinal)) text.AppendLine("  - " + Escape(value));
                }
            }

            text.AppendLine();
            text.AppendLine("## Diagnostics");
            text.AppendLine();
            foreach (WorldValidationResult result in report.results ?? Array.Empty<WorldValidationResult>())
            {
                text.AppendLine("### [" + result.status + "] " + Escape(result.title) + " — " + Escape(result.ruleId));
                text.AppendLine();
                text.AppendLine("- Code: `" + Escape(result.diagnosticCode) + "`");
                text.AppendLine("- Severity: `" + result.severity + "`");
                text.AppendLine("- Owner: `" + Escape(result.ownerModule) + "`");
                text.AppendLine("- Target: `" + Escape(result.DisplayTarget) + "`");
                text.AppendLine("- Source revision: `" + Escape(result.sourceRevision) + "`");
                if (result.baselineState != WorldValidationBaselineState.None) text.AppendLine("- Baseline: `" + result.baselineState + "`");
                if (result.heuristic) text.AppendLine("- Evidence class: `heuristic/static estimate`");
                if (result.empirical) text.AppendLine("- Evidence class: `empirical scenario`");
                text.AppendLine();
                text.AppendLine(Escape(result.message));
                foreach (WorldValidationEvidence evidence in result.evidence ?? Array.Empty<WorldValidationEvidence>())
                    text.AppendLine("\n> " + evidence.kind + " — " + Escape(evidence.label) + ": " + Escape(evidence.value));
                text.AppendLine();
            }
            if (report.registrationErrors != null && report.registrationErrors.Length > 0)
            {
                text.AppendLine("## Registration and export errors");
                text.AppendLine();
                foreach (string error in report.registrationErrors) text.AppendLine("- " + Escape(error));
            }
            return text.ToString();
        }

        public static bool TryWrite(string jsonPath, string markdownPath, WorldValidationReport report, out string failure)
        {
            failure = string.Empty;
            try
            {
                if (string.IsNullOrEmpty(jsonPath) && string.IsNullOrEmpty(markdownPath))
                    throw new ArgumentException("At least one report output path is required.");
                if (!string.IsNullOrEmpty(jsonPath)) WriteFile(jsonPath, ToJson(report));
                if (!string.IsNullOrEmpty(markdownPath)) WriteFile(markdownPath, ToMarkdown(report));
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
        }

        private static void WriteFile(string path, string content)
        {
            string full = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(full, content, new UTF8Encoding(false));
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ");
        }
    }
}
