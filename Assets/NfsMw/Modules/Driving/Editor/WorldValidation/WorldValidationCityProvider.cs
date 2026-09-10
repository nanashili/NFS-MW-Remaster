using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NfsMwRemaster.Driving.Editor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor.WorldValidation
{
    /// <summary>
    /// Adapts the city owner's build guard to the shared validation contract.
    /// CityPlanning and CityBuildGuard remain authoritative; this provider only
    /// maps their structured diagnostics into dashboard results and applies the
    /// requested district/cell presentation scope.
    /// </summary>
    public sealed class WorldValidationCityRule : WorldValidationRuleBase
    {
        private static readonly Regex CellDiagnosticPattern = new Regex(
            @"Cell\s*\(\s*(-?\d+)\s*,\s*(-?\d+)\s*\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public WorldValidationCityRule()
            : base("world.city.publication", "City district publication and cell integrity", "World/City",
                WorldValidationCategory.WorldArt, WorldValidationCost.Standard,
                new[]
                {
                    WorldValidationScopeKind.SelectedObjects,
                    WorldValidationScopeKind.DistrictCell,
                    WorldValidationScopeKind.OpenScenes,
                    WorldValidationScopeKind.ExplicitScenes,
                    WorldValidationScopeKind.BuildContent,
                    WorldValidationScopeKind.Project
                },
                new[]
                {
                    "CityDistrict",
                    "CityPlanning",
                    "CityBuildGuard",
                    "CityPublication",
                    "CityGeneratedInstance"
                }) { }

        public override void Evaluate(WorldValidationContext context, WorldValidationResultSink results)
        {
            int districts = 0;
            context.InspectScenes(scene =>
            {
                context.ThrowIfCancellationRequested();
                CityDistrict[] values = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<CityDistrict>(true))
                    .Where(value => value != null)
                    .OrderBy(value => value.id ?? string.Empty, StringComparer.Ordinal)
                    .ThenBy(value => value.name, StringComparer.Ordinal)
                    .ToArray();

                foreach (CityDistrict district in values)
                {
                    context.ThrowIfCancellationRequested();
                    if (!context.IncludesDistrict(district)) continue;
                    districts++;
                    ValidateDistrict(context, results, district, scene);
                }
            });

            if (districts == 0)
            {
                string target = context.DistrictIds.Count > 0
                    ? " No matching district ID was found in the inspected scenes."
                    : " Select a CityDistrict, provide a district ID, or inspect a scene containing city content.";
                NotEvaluated(results, Descriptor, "CITY_NO_DISTRICT", "No CityDistrict was included in this scope." + target);
            }
        }

        private void ValidateDistrict(WorldValidationContext context, WorldValidationResultSink results,
            CityDistrict district, Scene scene)
        {
            Dictionary<string, CityGeneratedInstance> existing = null;
            try
            {
                existing = CityCommands.Existing(district);
                List<CityDiagnostic> diagnostics = CityBuildGuard.Validate(district) ?? new List<CityDiagnostic>();
                int emitted = 0;
                foreach (CityDiagnostic diagnostic in diagnostics
                             .Where(value => value != null)
                             .OrderBy(value => value.rule ?? string.Empty, StringComparer.Ordinal)
                             .ThenBy(value => value.owner ?? string.Empty, StringComparer.Ordinal)
                             .ThenBy(value => value.message ?? string.Empty, StringComparer.Ordinal))
                {
                    context.ThrowIfCancellationRequested();
                    if (!IsRelevantToCells(context, district, diagnostic, existing)) continue;
                    AddDiagnostic(context, results, district, scene, diagnostic, existing);
                    emitted++;
                }

                if (emitted == 0)
                {
                    string message = context.IsDistrictCellScope && context.CellIds.Count > 0
                        ? "The selected city cells have no applicable publication or generated-content diagnostics. District-level source checks remain included; diagnostics outside the selected cells were filtered from this result."
                        : "City source, publication, generated ownership and cell-budget checks pass through the authoritative CityBuildGuard.";
                    PassAsset(results, Descriptor, district, message);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (TimeoutException) { throw; }
            catch (Exception exception)
            {
                var result = WorldValidationResult.Create(
                    Descriptor,
                    WorldValidationStatus.ErrorRunning,
                    WorldValidationSeverity.Error,
                    "CITY_VALIDATOR_EXCEPTION",
                    "City validator failed",
                    ExceptionMessage(exception));
                result.SetTarget(district);
                result.location = district != null ? district.id ?? string.Empty : string.Empty;
                result.AddEvidence(WorldValidationEvidenceKind.Text, "Exception", exception.ToString());
                results.Add(result);
            }
        }

        private void AddDiagnostic(WorldValidationContext context, WorldValidationResultSink results,
            CityDistrict district, Scene scene, CityDiagnostic diagnostic,
            IReadOnlyDictionary<string, CityGeneratedInstance> existing)
        {
            WorldValidationStatus status = ToStatus(diagnostic.severity);
            WorldValidationSeverity severity = ToSeverity(diagnostic.severity);
            string code = string.IsNullOrWhiteSpace(diagnostic.rule) ? "CITY_ISSUE" : diagnostic.rule;
            string title = diagnostic.severity == CitySeverity.Warning ? "City publication review" : "City publication issue";
            string message = diagnostic.message ?? "The city owner reported an issue without a message.";
            if (!string.IsNullOrWhiteSpace(diagnostic.action)) message += "\nAction: " + diagnostic.action;

            var result = WorldValidationResult.Create(Descriptor, status, severity, code, title, message);
            UnityEngine.Object target = ResolveOwner(district, diagnostic.owner, existing);
            if (target != null) result.SetTarget(target);
            else result.SetTarget(string.Empty, scene.path, string.Empty);
            result.location = diagnostic.owner ?? string.Empty;
            if (!TrySetDiagnosticPosition(result, district, diagnostic, target))
            {
                if (target == null && district != null) result.SetTarget(district);
            }
            result.AddAffectedId(diagnostic.owner);
            result.AddEvidence(WorldValidationEvidenceKind.Text, "City owner rule", code,
                diagnostic.owner ?? string.Empty, scene: scene.path);
            if (!string.IsNullOrWhiteSpace(diagnostic.action))
                result.AddEvidence(WorldValidationEvidenceKind.Text, "Owner action", diagnostic.action,
                    diagnostic.owner ?? string.Empty, scene: scene.path);
            if (context.IsDistrictCellScope)
                result.AddEvidence(WorldValidationEvidenceKind.Metric, "Validation scope",
                    context.CellIds.Count == 0 ? "district" : "selected city cells",
                    diagnostic.owner ?? string.Empty, scene: scene.path);
            results.Add(result);
        }

        private static UnityEngine.Object ResolveOwner(CityDistrict district, string owner,
            IReadOnlyDictionary<string, CityGeneratedInstance> existing)
        {
            string value = (owner ?? string.Empty).Trim();
            if (existing != null && existing.TryGetValue(value, out CityGeneratedInstance instance) && instance != null)
                return instance;
            return district;
        }

        private static bool IsRelevantToCells(WorldValidationContext context, CityDistrict district,
            CityDiagnostic diagnostic, IReadOnlyDictionary<string, CityGeneratedInstance> existing)
        {
            if (!context.IsDistrictCellScope || context.CellIds.Count == 0) return true;

            if (TryDecodeCell(diagnostic.message, out Vector2Int diagnosticCell))
                return context.IncludesCell(district, diagnosticCell);

            if (existing != null && existing.TryGetValue((diagnostic.owner ?? string.Empty).Trim(), out CityGeneratedInstance instance)
                && instance != null)
            {
                return TryGetInstanceCell(district, instance, out Vector2Int instanceCell)
                    ? context.IncludesCell(district, instanceCell)
                    : true;
            }

            if (TryFindOwnerPolygon(district, diagnostic.owner, out CityPolygon polygon, out _))
            {
                try
                {
                    return CityGeometry.Cells(polygon, district.cellSize)
                        .Any(cell => context.IncludesCell(district, cell));
                }
                catch (Exception)
                {
                    // A malformed polygon is itself a district-level source
                    // error. Keep it visible even when a cell slice is asked.
                    return true;
                }
            }

            // District-wide publication/schema/ownership diagnostics have no
            // cell polygon. They remain visible in every cell slice because
            // hiding them would make a broken district look healthy.
            return true;
        }

        private static bool TrySetDiagnosticPosition(WorldValidationResult result, CityDistrict district,
            CityDiagnostic diagnostic, UnityEngine.Object target)
        {
            if (result == null || district == null) return false;
            if (diagnostic.localPosition != Vector3.zero && Finite(diagnostic.localPosition))
            {
                result.worldPosition = district.transform.TransformPoint(diagnostic.localPosition);
                result.hasWorldPosition = true;
                return true;
            }

            if (TryDecodeCell(diagnostic.message, out Vector2Int cell)
                && float.IsFinite(district.cellSize) && district.cellSize >= 10)
            {
                result.worldPosition = district.transform.TransformPoint(new Vector3(
                    (cell.x + 0.5f) * district.cellSize,
                    0,
                    (cell.y + 0.5f) * district.cellSize));
                result.hasWorldPosition = true;
                return true;
            }

            if (target is Component component)
            {
                result.worldPosition = component.transform.position;
                result.hasWorldPosition = true;
                return true;
            }

            if (TryFindOwnerPolygon(district, diagnostic.owner, out CityPolygon polygon, out float height))
            {
                try
                {
                    Vector2 center = CityGeometry.Bounds(polygon).center;
                    result.worldPosition = district.transform.TransformPoint(new Vector3(center.x, height, center.y));
                    result.hasWorldPosition = true;
                    return true;
                }
                catch (Exception) { }
            }

            result.worldPosition = district.transform.position;
            result.hasWorldPosition = true;
            return true;
        }

        private static bool TryGetInstanceCell(CityDistrict district, CityGeneratedInstance instance, out Vector2Int cell)
        {
            cell = default;
            if (district == null || instance == null || string.IsNullOrWhiteSpace(district.id)
                || !float.IsFinite(district.cellSize) || district.cellSize < 10) return false;
            Vector3 local = district.transform.InverseTransformPoint(instance.transform.position);
            if (!Finite(local)) return false;
            cell = new Vector2Int(Mathf.FloorToInt(local.x / district.cellSize), Mathf.FloorToInt(local.z / district.cellSize));
            return true;
        }

        private static bool TryDecodeCell(string message, out Vector2Int cell)
        {
            cell = default;
            Match match = CellDiagnosticPattern.Match(message ?? string.Empty);
            if (!match.Success
                || !int.TryParse(match.Groups[1].Value, out int x)
                || !int.TryParse(match.Groups[2].Value, out int z)) return false;
            cell = new Vector2Int(x, z);
            return true;
        }

        private static bool TryFindOwnerPolygon(CityDistrict district, string owner, out CityPolygon polygon, out float height)
        {
            polygon = null;
            height = 0;
            if (district == null || string.IsNullOrWhiteSpace(owner)) return false;
            string value = owner.Trim();
            CityParcel parcel = (district.parcels ?? new List<CityParcel>())
                .FirstOrDefault(candidate => candidate != null
                    && (string.Equals(candidate.id, value, StringComparison.Ordinal)
                        || value.StartsWith((candidate.id ?? string.Empty) + "/", StringComparison.Ordinal)));
            if (parcel != null)
            {
                polygon = parcel.polygon;
                height = parcel.padHeight;
                return polygon != null;
            }

            CityBlock block = (district.blocks ?? new List<CityBlock>())
                .FirstOrDefault(candidate => candidate != null && string.Equals(candidate.id, value, StringComparison.Ordinal));
            if (block != null)
            {
                polygon = block.polygon;
                return polygon != null;
            }

            CityReservation reservation = (district.reservations ?? new List<CityReservation>())
                .FirstOrDefault(candidate => candidate != null && string.Equals(candidate.id, value, StringComparison.Ordinal));
            if (reservation != null)
            {
                polygon = reservation.polygon;
                return polygon != null;
            }
            return false;
        }

        private static WorldValidationStatus ToStatus(CitySeverity severity)
        {
            return severity == CitySeverity.Error ? WorldValidationStatus.Failed
                : severity == CitySeverity.Warning ? WorldValidationStatus.Warning
                : WorldValidationStatus.Passed;
        }

        private static WorldValidationSeverity ToSeverity(CitySeverity severity)
        {
            return severity == CitySeverity.Error ? WorldValidationSeverity.Blocker
                : severity == CitySeverity.Warning ? WorldValidationSeverity.Warning
                : WorldValidationSeverity.Info;
        }
    }
}
