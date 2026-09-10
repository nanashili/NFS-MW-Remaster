using System;
using System.Collections.Generic;
using System.Linq;
using NfsMwRemaster.Driving.Editor.WorldValidation;
using UnityEditor;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>Adapts per-profile adaptive-music diagnostics to world validation.</summary>
    public sealed class WorldValidationAdaptiveMusicRule : WorldValidationRuleBase
    {
        public WorldValidationAdaptiveMusicRule()
            : base("audio.music.arrangement", "Adaptive music arrangement integrity", "Driving/AdaptiveMusic",
                WorldValidationCategory.AudioLightingMaps, WorldValidationCost.Standard,
                Enum.GetValues(typeof(WorldValidationScopeKind)).Cast<WorldValidationScopeKind>(),
                new[] { "SensoryMusicProfile", "AdaptiveMusicEditorModel", "audio clip readiness" },
                null, false,
                "Static arrangement and clip-readiness checks. Runtime mixer latency and platform decoder behavior need an audio-capable scenario.") { }

        public override void Evaluate(WorldValidationContext context, WorldValidationResultSink results)
        {
            int count = 0;
            foreach (SensoryMusicProfile profile in context.FindAssets<SensoryMusicProfile>().Distinct())
            {
                context.ThrowIfCancellationRequested();
                count++;
                List<AdaptiveMusicDiagnostic> diagnostics;
                try { diagnostics = AdaptiveMusicEditorModel.ValidateProfile(profile) ?? new List<AdaptiveMusicDiagnostic>(); }
                catch (Exception exception)
                {
                    var failure = WorldValidationResult.Create(Descriptor, WorldValidationStatus.ErrorRunning,
                        WorldValidationSeverity.Error, "MUSIC_AUDIT_EXCEPTION", "Adaptive music validation failed", ExceptionMessage(exception));
                    failure.SetTarget(profile);
                    failure.AddEvidence(WorldValidationEvidenceKind.Text, "Owner validator exception", exception.ToString(),
                        asset: AssetDatabase.GetAssetPath(profile));
                    results.Add(failure);
                    continue;
                }

                bool emitted = false;
                foreach (AdaptiveMusicDiagnostic diagnostic in diagnostics.Where(value => value != null)
                             .OrderBy(value => value.code, StringComparer.Ordinal)
                             .ThenBy(value => value.message, StringComparer.Ordinal))
                {
                    context.ThrowIfCancellationRequested();
                    bool informational = diagnostic.severity == AdaptiveMusicDiagnosticSeverity.Info;
                    if (informational && !context.Request.includeInfo) continue;
                    WorldValidationStatus status = diagnostic.severity == AdaptiveMusicDiagnosticSeverity.Error
                        ? WorldValidationStatus.Failed
                        : informational ? WorldValidationStatus.Passed : WorldValidationStatus.Warning;
                    WorldValidationSeverity severity = diagnostic.severity == AdaptiveMusicDiagnosticSeverity.Error
                        ? WorldValidationSeverity.Blocker
                        : informational ? WorldValidationSeverity.Info : WorldValidationSeverity.Warning;
                    var result = WorldValidationResult.Create(Descriptor, status, severity, diagnostic.code,
                        informational ? "Adaptive music information" : diagnostic.severity == AdaptiveMusicDiagnosticSeverity.Error
                            ? "Adaptive music error" : "Adaptive music review", diagnostic.message);
                    result.SetTarget(diagnostic.target != null ? diagnostic.target : profile);
                    result.location = diagnostic.property ?? string.Empty;
                    result.AddEvidence(WorldValidationEvidenceKind.Asset, "Adaptive music owner diagnostic", diagnostic.message,
                        string.Empty, AssetDatabase.GetAssetPath(profile), string.Empty, result.globalObjectId);
                    results.Add(result);
                    emitted = true;
                }

                if (!emitted)
                    PassAsset(results, Descriptor, profile, "Adaptive music profile has no diagnostics at the selected information level.");
            }

            if (count == 0)
                NotEvaluated(results, Descriptor, "MUSIC_NO_INPUT", "No SensoryMusicProfile assets were included in this scope.");
        }
    }

    [InitializeOnLoad]
    internal static class WorldValidationAdaptiveMusicRegistration
    {
        static WorldValidationAdaptiveMusicRegistration()
        {
            WorldValidationRuleRegistry.Register(new WorldValidationAdaptiveMusicRule());
        }
    }
}
