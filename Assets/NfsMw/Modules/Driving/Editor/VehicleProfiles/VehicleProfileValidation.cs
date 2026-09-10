using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public enum VehicleProfileSeverity { Warning, Error }

    public sealed class VehicleProfileIssue
    {
        public readonly string Code, Field, Message;
        public readonly VehicleProfileSeverity Severity;
        public readonly UnityEngine.Object Target;
        public VehicleProfileIssue(string code, string field, string message, VehicleProfileSeverity severity, UnityEngine.Object target)
        { Code = code; Field = field; Message = message; Severity = severity; Target = target; }
    }

    public sealed class VehicleProfileReport
    {
        public readonly List<VehicleProfileIssue> Issues = new List<VehicleProfileIssue>();
        public long Lod0Triangles, MaterialSlots, EstimatedDecodedAudioBytes;
        public int MaximumTextureDimension;
        public string[] Dependencies = Array.Empty<string>();
        public bool CanPublish => Issues.All(i => i.Severity != VehicleProfileSeverity.Error);
    }

    /// <summary>Explicit, read-only inspection. Never invoked by an IMGUI repaint.</summary>
    public static class VehicleProfileValidation
    {
        public static VehicleProfileReport Inspect(VehicleProfileDraft draft, IEnumerable<VehicleProfileDraft> otherDrafts)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (otherDrafts == null) throw new ArgumentNullException(nameof(otherDrafts));
            var report = new VehicleProfileReport();
            void Issue(string code, string field, string message, bool error = true, UnityEngine.Object target = null)
                => report.Issues.Add(new VehicleProfileIssue(code, field, message,
                    error ? VehicleProfileSeverity.Error : VehicleProfileSeverity.Warning, target != null ? target : draft));
            if (draft.Schema != VehicleProfileDraft.CurrentSchema) Issue("VP_SCHEMA", "schema", "Unsupported draft version; do not overwrite it.");
            if (!Guid.TryParseExact(draft.Id, "N", out _)) Issue("VP_ID", "id", "Invalid stable vehicle identity.");
            if (draft.catalog == null) Issue("VP_CATALOG", "catalog", "Assign an identity catalogue.");
            else foreach (string failure in draft.catalog.ValidateRecords()) Issue("VP_CATALOG_RECORD", "catalog", failure, true, draft.catalog);
            var model = draft.catalog != null ? draft.catalog.FindModel(draft.modelId) : null;
            var variant = model?.variants.Find(v => v != null && v.Id == draft.variantId);
            if (model == null || variant == null || variant.year != draft.modelYear || !model.years.Contains(draft.modelYear))
                Issue("VP_IDENTITY", "modelId", "Select a declared model, model year and variant.");
            if (model != null && draft.catalog.FindBrand(model.brandId)?.logo == null)
                Issue("VP_LOGO", "catalog", "No approved manufacturer logo; the catalogue shows labelled initials.", false, draft.catalog);
            foreach (var other in otherDrafts)
            {
                if (other == null || other == draft) continue;
                if (other.Id == draft.Id) Issue("VP_DUPLICATE_ID", "id", "Another draft has the same immutable ID. Do not duplicate draft assets in Finder.", true, other);
                if (other.catalog == draft.catalog && other.modelId == draft.modelId && other.modelYear == draft.modelYear && other.variantId == draft.variantId)
                    Issue("VP_DUPLICATE_IDENTITY", "variantId", "Another profile claims the same model/year/variant identity.", true, other);
            }
            if (!AssetDatabase.Contains(draft)) Issue("VP_UNSAVED", "", "Save the draft as an asset before publication.");
            if (draft.price < 0) Issue("VP_PRICE", "price", "Price cannot be negative.");
            if (draft.vehiclePrefab == null || !PrefabUtility.IsPartOfPrefabAsset(draft.vehiclePrefab) || draft.vehiclePrefab.transform.parent != null)
                Issue("VP_PREFAB", "vehiclePrefab", "Assign the root of an assembled prefab asset, not a scene object or mesh alone.");
            else InspectPrefab(draft, report, Issue);
            if (draft.tuning == null || !AssetDatabase.Contains(draft.tuning)) Issue("VP_TUNING", "tuning", "Assign a persistent authoritative tuning asset.");
            else try { RacingLineSnapshot.ValidateTuning(draft.tuning); }
                catch (ArgumentException e) { Issue("VP_TUNING_VALUE", "tuning", e.Message, true, draft.tuning); }
            if (draft.audio == null) Issue("VP_AUDIO_OPTIONAL", "audio", "No engine audio selected. Silent vehicle publication is permitted.", false);
            else if (!draft.audio.Validate(out string failure)) Issue("VP_AUDIO", "audio", failure, true, draft.audio);
            if (draft.storeCatalog == null || !AssetDatabase.Contains(draft.storeCatalog)) Issue("VP_STORE", "storeCatalog", "Assign the existing destination store catalogue.");
            else foreach (var product in draft.storeCatalog.ProductAssets)
            {
                if (product == draft.GeneratedProduct || product == null) continue;
                if (product is IVehicleStoreProduct listing && listing.ProductId == draft.Id ||
                    product is IVehicleCarStoreProduct car && car.VehicleDefinitionId == draft.Id)
                    Issue("VP_STORE_COLLISION", "storeCatalog", "An independently owned listing already uses this vehicle identity.", true, product);
            }
            if (draft.budget == null) Issue("VP_BUDGET", "budget", "Content budgets are missing.");
            else
            {
                var b = draft.budget;
                if (b.maximumLod0Triangles <= 0 || b.maximumMaterialSlots <= 0 || b.maximumTextureDimension <= 0 || b.maximumAudioMiB <= 0)
                    Issue("VP_BUDGET_VALUE", "budget", "All content budgets must be positive.");
                if (report.Lod0Triangles > b.maximumLod0Triangles) Issue("VP_TRIANGLES", "budget", "LOD0 triangle estimate exceeds the authored budget.", b.enforceAsErrors);
                if (report.MaterialSlots > b.maximumMaterialSlots) Issue("VP_MATERIALS", "budget", "LOD0 material slot count exceeds the authored budget.", b.enforceAsErrors);
                if (report.MaximumTextureDimension > b.maximumTextureDimension) Issue("VP_TEXTURES", "budget", "A referenced texture exceeds the maximum dimension.", b.enforceAsErrors);
                if (report.EstimatedDecodedAudioBytes > (long)b.maximumAudioMiB * 1024 * 1024)
                    Issue("VP_AUDIO_MEMORY", "budget", "Float PCM upper-bound estimate exceeds the audio budget; this is not measured resident memory.", b.enforceAsErrors);
            }
            return report;
        }

        private static void InspectPrefab(VehicleProfileDraft draft, VehicleProfileReport report,
            Action<string, string, string, bool, UnityEngine.Object> issue)
        {
            var root = draft.vehiclePrefab;
            var controller = root.GetComponent<VehicleController>();
            if (controller == null) issue("VP_CONTROLLER", "vehiclePrefab", "The prefab root needs the existing VehicleController.", true, root);
            else
            {
                if (controller.Tuning != draft.tuning) issue("VP_TUNING_BINDING", "tuning", "The prefab and draft must reference the same tuning asset. Edit the prefab deliberately.", true, root);
                if (!controller.HasLocalPhysicsBindings) issue("VP_PHYSICS_BINDING", "vehiclePrefab", "Physics bindings escape the chassis.", true, root);
                var wheels = controller.Wheels;
                if (wheels == null || wheels.Length != 4 || wheels.Any(w => w == null || !w.transform.IsChildOf(root.transform)) || wheels.Distinct().Count() != 4)
                    issue("VP_WHEELS", "vehiclePrefab", "Bind four distinct local vehicle wheels.", true, root);
            }
            if (draft.audio != null && !root.GetComponentsInChildren<VehicleAudio>(true).Any(p => p.Profile == draft.audio))
                issue("VP_AUDIO_BINDING", "audio", "Bind the selected profile on a VehicleAudio in this prefab.", true, root);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0)
                    issue("VP_MISSING_SCRIPT", "vehiclePrefab", "Prefab contains a missing script: " + t.name, true, root);
            var renderers = new HashSet<Renderer>(root.GetComponentsInChildren<Renderer>(true));
            if (renderers.Count == 0) issue("VP_RENDERERS", "vehiclePrefab", "The prefab has no renderers.", true, root);
            var lods = root.GetComponentsInChildren<LODGroup>(true);
            if (draft.budget != null && draft.budget.requireLods && !lods.Any(g => g.lodCount > 1))
                issue("VP_LODS", "vehiclePrefab", "No multi-level LOD group; configure distance reduction before traffic-scale use.", draft.budget.enforceAsErrors, root);
            // Count non-LOD renderers plus level zero, excluding renderers used exclusively by lower levels.
            var lower = new HashSet<Renderer>();
            var highest = new HashSet<Renderer>();
            foreach (var group in lods)
            {
                var levels = group.GetLODs();
                for (int i = 0; i < levels.Length; i++) foreach (var renderer in levels[i].renderers)
                    if (renderer != null) (i == 0 ? highest : lower).Add(renderer);
            }
            lower.ExceptWith(highest);
            renderers.ExceptWith(lower);
            foreach (var renderer in renderers)
            {
                report.MaterialSlots += renderer.sharedMaterials.Length;
                if (renderer.sharedMaterials.Any(m => m == null)) issue("VP_MATERIAL_NULL", "vehiclePrefab", "Renderer has an unassigned material: " + renderer.name, true, root);
                Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    if (mesh.GetTopology(sub) == MeshTopology.Triangles) report.Lod0Triangles += mesh.GetIndexCount(sub) / 3;
            }
            report.Dependencies = AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(root), true);
            foreach (string path in report.Dependencies)
            {
                if (path.Contains("/Resources/") || path.StartsWith("Assets/StreamingAssets/", StringComparison.Ordinal))
                    issue("VP_ALWAYS_INCLUDED", "vehiclePrefab", "Dependency is under an always-included content root: " + path, false, root);
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is Texture texture) report.MaximumTextureDimension = Math.Max(report.MaximumTextureDimension, Math.Max(texture.width, texture.height));
                    if (asset is AudioClip clip) report.EstimatedDecodedAudioBytes += (long)clip.samples * clip.channels * sizeof(float);
                }
            }
        }

        public static string Fingerprint(VehicleProfileDraft draft)
        {
            var text = new StringBuilder("vehicle-listing/1\n");
            text.AppendLine(draft.Id).AppendLine(draft.Schema.ToString()).AppendLine(draft.DisplayLabel)
                .AppendLine(draft.modelId).AppendLine(draft.variantId).AppendLine(draft.modelYear.ToString())
                .AppendLine(draft.price.ToString()).AppendLine(draft.availableInStore.ToString()).AppendLine(JsonUtility.ToJson(draft.budget));
            foreach (var asset in new UnityEngine.Object[] { draft.vehiclePrefab, draft.tuning, draft.audio, draft.customization, draft.performance, draft.thumbnail, draft.catalog })
            {
                string path = AssetDatabase.GetAssetPath(asset);
                if (asset != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long local))
                    text.AppendLine(guid + ":" + local + ":" + AssetDatabase.GetAssetDependencyHash(path));
            }
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
        }
    }
}
