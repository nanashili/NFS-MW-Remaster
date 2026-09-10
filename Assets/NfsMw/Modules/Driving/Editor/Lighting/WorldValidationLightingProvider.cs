using System;
using System.Collections.Generic;
using System.Linq;
using NfsMwRemaster.Driving.Editor.WorldValidation;
using NfsMwRemaster.Lighting;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Lighting.Editor
{
    /// <summary>Adapts the lighting owner's static audit to scene-scoped validation.</summary>
    public sealed class WorldValidationLightingRule : WorldValidationRuleBase
    {
        public WorldValidationLightingRule()
            : base("lighting.atmosphere.audit", "Atmosphere, reflection and fixture audit", "Lighting",
                WorldValidationCategory.AudioLightingMaps, WorldValidationCost.Standard,
                new[] { WorldValidationScopeKind.SelectedObjects, WorldValidationScopeKind.OpenScenes,
                    WorldValidationScopeKind.ExplicitScenes, WorldValidationScopeKind.BuildContent },
                new[] { "AtmosphereController", "LightingAudit", "approved reflection revision" },
                new[] { "world.identity.references" }, false,
                "Static HDRP and authored-bake checks. Render-capture quality and APV scenarios need a graphics-capable scenario provider.") { }

        public override void Evaluate(WorldValidationContext context, WorldValidationResultSink results)
        {
            int scenes = 0;
            int owners = 0;
            context.InspectScenes(scene =>
            {
                context.ThrowIfCancellationRequested();
                scenes++;
                AtmosphereController[] controllers = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<AtmosphereController>(true))
                    .Where(value => value != null)
                    .OrderBy(value => value.id, StringComparer.Ordinal)
                    .ToArray();
                if (controllers.Length == 0)
                {
                    NotEvaluated(results, Descriptor, "LIGHTING_SCENE_NO_OWNER",
                        "The scene contains no AtmosphereController owner; no lighting audit was inferred.");
                    return;
                }

                foreach (AtmosphereController owner in controllers)
                {
                    context.ThrowIfCancellationRequested();
                    owners++;
                    List<string> issues;
                    try { issues = LightingAudit.Validate(owner, owner.transform.position) ?? new List<string>(); }
                    catch (Exception exception)
                    {
                        Add(results, owner, WorldValidationStatus.ErrorRunning, WorldValidationSeverity.Error,
                            "LIGHTING_AUDIT_EXCEPTION", "Lighting audit failed", ExceptionMessage(exception));
                        continue;
                    }

                    bool actionable = false;
                    bool emitted = false;
                    foreach (string issue in issues.Where(value => !string.IsNullOrWhiteSpace(value)))
                    {
                        context.ThrowIfCancellationRequested();
                        Parse(issue, out WorldValidationStatus status, out WorldValidationSeverity severity,
                            out string code, out string title, out bool informational);
                        if (informational && !context.Request.includeInfo) continue;
                        if (status != WorldValidationStatus.Passed) actionable = true;
                        Add(results, owner, status, severity, code, title, issue);
                        emitted = true;
                    }

                    if (!actionable && !emitted)
                        PassAsset(results, Descriptor, owner, "Lighting owner has no diagnostics at the selected information level.");
                }
            });

            if (scenes == 0)
                NotEvaluated(results, Descriptor, "LIGHTING_NO_SCENES", "This rule needs a scene scope; no scenes were available.");
            else if (owners == 0)
                NotEvaluated(results, Descriptor, "LIGHTING_NO_OWNER", "No AtmosphereController owner was available in the inspected scenes.");
        }

        private static void Parse(string issue, out WorldValidationStatus status, out WorldValidationSeverity severity,
            out string code, out string title, out bool informational)
        {
            string value = (issue ?? string.Empty).Trim();
            int separator = value.IndexOf(':');
            string prefix = separator > 0 ? value.Substring(0, separator).Trim().ToUpperInvariant() : string.Empty;
            informational = prefix == "INFO";
            if (prefix == "ERROR")
            {
                status = WorldValidationStatus.Failed;
                severity = WorldValidationSeverity.Blocker;
                code = "LIGHTING_ERROR";
                title = "Lighting configuration error";
            }
            else if (prefix == "REVIEW")
            {
                status = WorldValidationStatus.Warning;
                severity = WorldValidationSeverity.Warning;
                code = "LIGHTING_REVIEW";
                title = "Lighting configuration review";
            }
            else
            {
                status = WorldValidationStatus.Passed;
                severity = WorldValidationSeverity.Info;
                code = "LIGHTING_INFO";
                title = "Lighting audit information";
            }
        }

        private void Add(WorldValidationResultSink sink, AtmosphereController owner,
            WorldValidationStatus status, WorldValidationSeverity severity, string code, string title, string message)
        {
            var result = WorldValidationResult.Create(Descriptor, status, severity, code, title, message);
            result.SetTarget(owner);
            result.location = owner.id ?? string.Empty;
            result.AddAffectedId(owner.id);
            result.AddEvidence(WorldValidationEvidenceKind.SceneObject, "Lighting owner", owner.name,
                owner.id, string.Empty, owner.gameObject.scene.path, result.globalObjectId);
            sink.Add(result);
        }
    }

    [InitializeOnLoad]
    internal static class WorldValidationLightingRegistration
    {
        static WorldValidationLightingRegistration()
        {
            WorldValidationRuleRegistry.Register(new WorldValidationLightingRule());
        }
    }
}
