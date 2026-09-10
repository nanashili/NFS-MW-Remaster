using System;
using System.Collections.Generic;
using System.Linq;
using NfsMwRemaster.Driving.Editor.WorldValidation;
using UnityEditor;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Uses the AudioZone editor model as the sole owner of zone, portal and
    /// profile semantics. Its current API is intentionally open-scene scoped;
    /// unsupported scopes are reported rather than pretending to isolate a
    /// global FindObjectsByType query to an unloaded scene.
    /// </summary>
    public sealed class WorldValidationAudioZoneRule : WorldValidationRuleBase
    {
        public WorldValidationAudioZoneRule()
            : base("audio.zones.authoring", "Audio zones, portals and profile ownership", "Driving/AudioZones",
                WorldValidationCategory.AudioLightingMaps, WorldValidationCost.Standard,
                new[] { WorldValidationScopeKind.OpenScenes },
                new[] { "AudioZoneEditorModel", "AudioZone", "AudioZonePortal", "AudioZoneProfile" },
                null, false,
                "The owner validator inspects loaded scenes and project audio profiles; per-asset isolation is not exposed by its current editor API.") { }

        public override void Evaluate(WorldValidationContext context, WorldValidationResultSink results)
        {
            if (context.Scope.ScenePaths.Count == 0)
            {
                NotEvaluated(results, Descriptor, "AUDIO_ZONES_NO_SCENES",
                    "Audio zone validation requires at least one loaded, saved open scene.");
                return;
            }

            List<AudioZoneDiagnostic> diagnostics;
            try { diagnostics = AudioZoneEditorModel.ValidateAll() ?? new List<AudioZoneDiagnostic>(); }
            catch (Exception exception)
            {
                var failure = WorldValidationResult.Create(Descriptor, WorldValidationStatus.ErrorRunning,
                    WorldValidationSeverity.Error, "AUDIO_ZONES_EXCEPTION", "Audio zone validation failed", ExceptionMessage(exception));
                failure.AddEvidence(WorldValidationEvidenceKind.Text, "Owner validator exception", exception.ToString());
                results.Add(failure);
                return;
            }

            bool emitted = false;
            foreach (AudioZoneDiagnostic diagnostic in diagnostics.Where(value => value != null)
                         .OrderBy(value => value.code, StringComparer.Ordinal)
                         .ThenBy(value => value.message, StringComparer.Ordinal))
            {
                context.ThrowIfCancellationRequested();
                bool informational = diagnostic.severity == AudioZoneDiagnosticSeverity.Info;
                if (informational && !context.Request.includeInfo) continue;

                WorldValidationStatus status = diagnostic.severity == AudioZoneDiagnosticSeverity.Error
                    ? WorldValidationStatus.Failed
                    : informational ? WorldValidationStatus.Passed : WorldValidationStatus.Warning;
                WorldValidationSeverity severity = diagnostic.severity == AudioZoneDiagnosticSeverity.Error
                    ? WorldValidationSeverity.Blocker
                    : informational ? WorldValidationSeverity.Info : WorldValidationSeverity.Warning;
                var result = WorldValidationResult.Create(Descriptor, status, severity,
                    diagnostic.code, informational ? "Audio zone information" : diagnostic.severity == AudioZoneDiagnosticSeverity.Error
                        ? "Audio zone error" : "Audio zone review", diagnostic.message);
                if (diagnostic.target != null) result.SetTarget(diagnostic.target);
                else result.SetTarget(string.Empty, context.Scope.ScenePaths[0], string.Empty);
                result.location = diagnostic.property ?? string.Empty;
                result.AddEvidence(WorldValidationEvidenceKind.SceneObject, "Audio zone owner diagnostic", diagnostic.message,
                    string.Empty, result.assetPath, result.scenePath, result.globalObjectId);
                results.Add(result);
                emitted = true;
            }

            if (!emitted)
                PassScene(results, Descriptor, UnityEngine.SceneManagement.SceneManager.GetActiveScene(),
                    "Audio zone owner reports no diagnostics at the selected information level.");
        }
    }

    [InitializeOnLoad]
    internal static class WorldValidationAudioZoneRegistration
    {
        static WorldValidationAudioZoneRegistration()
        {
            WorldValidationRuleRegistry.Register(new WorldValidationAudioZoneRule());
        }
    }
}
