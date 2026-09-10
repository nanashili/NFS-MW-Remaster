using System;
using System.Collections.Generic;
using System.Linq;
using NfsMwRemaster.Driving.Editor.WorldValidation;
using UnityEditor;

namespace NfsMwRemaster.Maps.Editor
{
    /// <summary>
    /// Adapts map baking and crossing audits without recreating map topology in
    /// the dashboard. MapBaker remains authoritative for source invariants;
    /// MapAudit owns the optional geometry-heavy crossing pass.
    /// </summary>
    public sealed class WorldValidationMapRule : WorldValidationRuleBase
    {
        public WorldValidationMapRule()
            : base("maps.publication", "Map source and publication integrity", "Maps",
                WorldValidationCategory.AudioLightingMaps, WorldValidationCost.Standard,
                Enum.GetValues(typeof(WorldValidationScopeKind)).Cast<WorldValidationScopeKind>(),
                new[] { "MapDefinition", "MapBaker", "MapPublication", "RoadNetworkAsset" },
                new[] { "world.identity.references", "roads.authority" }) { }

        public override void Evaluate(WorldValidationContext context, WorldValidationResultSink results)
        {
            int count = 0;
            foreach (MapDefinition definition in context.FindAssets<MapDefinition>().Distinct())
            {
                context.ThrowIfCancellationRequested();
                count++;
                bool valid = true;
                List<string> errors;
                try { errors = MapBaker.Validate(definition) ?? new List<string>(); }
                catch (Exception exception)
                {
                    Add(results, definition, WorldValidationStatus.ErrorRunning, WorldValidationSeverity.Error,
                        "MAP_VALIDATOR_EXCEPTION", "Map validator failed", ExceptionMessage(exception));
                    continue;
                }

                foreach (string error in errors.Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    Add(results, definition, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "MAP_SOURCE_INVALID", "Map source is invalid", error);
                    valid = false;
                }

                MapPublication publication = definition.publication;
                if (publication == null)
                {
                    Add(results, definition, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                        "MAP_UNPUBLISHED", "Map has no runtime publication",
                        "Runtime map consumers require an explicit immutable publication; a valid source definition alone is not sufficient.");
                    valid = false;
                }
                else
                {
                    try
                    {
                        string sourceFingerprint = MapBaker.Fingerprint(definition);
                        bool identityMatches = publication.Schema == MapPublication.CurrentSchema
                            && string.Equals(publication.Id, definition.id, StringComparison.Ordinal)
                            && string.Equals(publication.Fingerprint, sourceFingerprint, StringComparison.Ordinal)
                            && definition.roads != null
                            && string.Equals(publication.RoadNetworkId, definition.roads.NetworkId.ToString(), StringComparison.Ordinal)
                            && string.Equals(publication.RoadRevision, definition.roads.Fingerprint, StringComparison.Ordinal);
                        if (!identityMatches)
                        {
                            Add(results, definition, WorldValidationStatus.Failed, WorldValidationSeverity.Blocker,
                                "MAP_PUBLICATION_STALE", "Map publication is stale",
                                "Publication schema, identity, source fingerprint or authoritative road revision no longer matches the map source.");
                            valid = false;
                        }
                    }
                    catch (Exception exception)
                    {
                        Add(results, definition, WorldValidationStatus.ErrorRunning, WorldValidationSeverity.Error,
                            "MAP_PUBLICATION_CHECK_EXCEPTION", "Map publication check failed", ExceptionMessage(exception));
                        valid = false;
                    }

                    if (context.IncludeExpensive)
                    {
                        try
                        {
                            foreach (string crossing in MapAudit.Crossings(publication) ?? new List<string>())
                            {
                                context.ThrowIfCancellationRequested();
                                Add(results, definition, WorldValidationStatus.Warning, WorldValidationSeverity.Warning,
                                    "MAP_CROSSING_REVIEW", "Map geometry review", crossing);
                            }
                        }
                        catch (Exception exception)
                        {
                            Add(results, definition, WorldValidationStatus.ErrorRunning, WorldValidationSeverity.Error,
                                "MAP_CROSSING_EXCEPTION", "Map crossing audit failed", ExceptionMessage(exception));
                            valid = false;
                        }
                    }
                    else
                    {
                        NotEvaluated(results, Descriptor, "MAP_CROSSING_NOT_EVALUATED",
                            "The map crossing audit is Expensive and was omitted. Enable the Expensive option to inspect tile geometry.", definition);
                    }
                }

                if (valid && !context.IncludeExpensive)
                {
                    // The explicit NotEvaluated result above is retained as
                    // coverage evidence; this pass only describes the cheap
                    // source/publication checks.
                    PassAsset(results, Descriptor, definition, "Map source and immutable publication pass the standard checks; geometry crossing audit was omitted.");
                }
                else if (valid && context.IncludeExpensive)
                    PassAsset(results, Descriptor, definition, "Map source, publication and requested geometry audits pass.");
            }

            if (count == 0)
                NotEvaluated(results, Descriptor, "MAP_NO_INPUT", "No MapDefinition assets were included in this scope.");
        }

        private void Add(WorldValidationResultSink sink, MapDefinition definition,
            WorldValidationStatus status, WorldValidationSeverity severity, string code, string title, string message)
        {
            var result = WorldValidationResult.Create(Descriptor, status, severity, code, title, message);
            result.SetTarget(definition);
            result.AddEvidence(WorldValidationEvidenceKind.Asset, "Map source", AssetDatabase.GetAssetPath(definition));
            sink.Add(result);
        }
    }

    [InitializeOnLoad]
    internal static class WorldValidationMapRegistration
    {
        static WorldValidationMapRegistration()
        {
            WorldValidationRuleRegistry.Register(new WorldValidationMapRule());
        }
    }
}
