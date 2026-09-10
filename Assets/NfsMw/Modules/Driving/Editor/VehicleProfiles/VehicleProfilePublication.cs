using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Publishes a listing for an already assembled prefab, not a model-to-vehicle compiler.
    /// Planning is read-only. Apply rejects stale plans and modified generated listings.
    /// Registration is explicit and never acquires ownership or touches a player save.
    /// </summary>
    public static class VehicleProfilePublication
    {
        public sealed class Plan
        {
            public readonly VehicleProfileDraft Draft;
            public readonly string OutputPath, InputFingerprint, Description;
            internal readonly string DraftHash, StoreHash, OutputHash;
            public readonly bool Unchanged;
            internal Plan(VehicleProfileDraft draft, string path, string fingerprint, bool unchanged)
            {
                Draft = draft; OutputPath = path; InputFingerprint = fingerprint; Unchanged = unchanged;
                DraftHash = HashFile(AssetDatabase.GetAssetPath(draft));
                StoreHash = HashFile(AssetDatabase.GetAssetPath(draft.storeCatalog));
                OutputHash = HashFile(path);
                Description = unchanged ? "No changes. No assets will be written."
                    : (draft.GeneratedProduct == null ? "Create " : "Update ") + path +
                      "\nReference the existing prefab, thumbnail, tuning and audio; copy no source assets." +
                      "\nRegister this listing in " + AssetDatabase.GetAssetPath(draft.storeCatalog) +
                      "\nRecord publication on " + AssetDatabase.GetAssetPath(draft) +
                      "\nNo purchases, save edits, scene changes or source-prefab edits.";
            }
        }

        [Serializable] private sealed class RecoveryJournal
        {
            public string operation, state;
            public string[] paths, originalBase64;
            public bool[] existed;
        }

        private static bool writing;

        public static Plan Prepare(VehicleProfileDraft draft, string newOutputPath)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Publish in a settled Edit Mode session.");
            if (HasPendingRecovery()) throw new InvalidOperationException("An interrupted publication needs review. Open the recovery folder before publishing again.");
            var drafts = AssetDatabase.FindAssets("t:VehicleProfileDraft").Select(g => AssetDatabase.LoadAssetAtPath<VehicleProfileDraft>(AssetDatabase.GUIDToAssetPath(g)));
            var report = VehicleProfileValidation.Inspect(draft, drafts);
            if (!report.CanPublish) throw new InvalidOperationException(string.Join("\n", report.Issues.Where(i => i.Severity == VehicleProfileSeverity.Error).Select(i => i.Code + ": " + i.Message)));
            if (EditorUtility.IsDirty(draft) || EditorUtility.IsDirty(draft.catalog) || EditorUtility.IsDirty(draft.storeCatalog))
                throw new InvalidOperationException("Save the draft, identity catalogue and store catalogue before planning publication.");
            foreach (string dependency in report.Dependencies)
                // Import-generated/package objects can be internally dirty without unsaved authoring edits.
                // Restrict this gate to Unity-native, editable content; importer state is fingerprinted separately.
                if (dependency.StartsWith("Assets/", StringComparison.Ordinal) &&
                    new[] { ".asset", ".mat", ".prefab" }.Contains(Path.GetExtension(dependency)) &&
                    AssetDatabase.LoadAllAssetsAtPath(dependency).Any(EditorUtility.IsDirty))
                    throw new InvalidOperationException("Save the dirty prefab dependency first: " + dependency);
            string path = draft.GeneratedProduct != null ? AssetDatabase.GetAssetPath(draft.GeneratedProduct) : ValidateOutputPath(newOutputPath);
            if (draft.GeneratedProduct != null) ValidateOutputPath(path, true);
            if (draft.GeneratedProduct == null && (File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) != null))
                throw new InvalidOperationException("The selected output already exists; it is not owned by this draft.");
            string fingerprint = VehicleProfileValidation.Fingerprint(draft);
            if (draft.GeneratedProduct != null && (EditorUtility.IsDirty(draft.GeneratedProduct) ||
                draft.GeneratedOutputHash != HashFile(path)))
                throw new InvalidOperationException("The generated listing was modified outside the Studio. Preserve and reconcile those edits before updating.");
            bool registered = draft.storeCatalog.ProductAssets.Contains(draft.GeneratedProduct);
            return new Plan(draft, path, fingerprint, draft.GeneratedProduct != null && registered && fingerprint == draft.GeneratedFingerprint);
        }

        public static AssetVehicleCarStoreProduct Apply(Plan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (writing) throw new InvalidOperationException("A vehicle publication is already in progress.");
            var current = Prepare(plan.Draft, plan.OutputPath);
            if (current.InputFingerprint != plan.InputFingerprint || current.DraftHash != plan.DraftHash ||
                current.StoreHash != plan.StoreHash || current.OutputHash != plan.OutputHash || current.OutputPath != plan.OutputPath)
                throw new InvalidOperationException("The plan is stale. Preview the changes again.");
            if (plan.Unchanged) return plan.Draft.GeneratedProduct;
            string[] paths = { plan.OutputPath, AssetDatabase.GetAssetPath(plan.Draft.storeCatalog), AssetDatabase.GetAssetPath(plan.Draft) };
            foreach (string path in paths)
                if (File.Exists(path) && (new FileInfo(path).IsReadOnly || !AssetDatabase.IsOpenForEdit(path)))
                    throw new IOException("Publication target is read-only: " + path);
            var journal = new RecoveryJournal
            {
                operation = Guid.NewGuid().ToString("N"), state = "Prepared",
                paths = paths, existed = paths.Select(File.Exists).ToArray(),
                originalBase64 = paths.Select(p => File.Exists(p) ? Convert.ToBase64String(File.ReadAllBytes(p)) : "").ToArray()
            };
            Directory.CreateDirectory(RecoveryDirectory);
            string journalPath = Path.Combine(RecoveryDirectory, journal.operation + ".json");
            // Backups are durable before the first write. They are outside Assets and cannot ship.
            File.WriteAllText(journalPath, JsonUtility.ToJson(journal, true), Encoding.UTF8);
            writing = true;
            try
            {
                var draft = plan.Draft;
                var product = draft.GeneratedProduct;
                if (product == null)
                {
                    product = ScriptableObject.CreateInstance<AssetVehicleCarStoreProduct>();
                    try { AssetDatabase.CreateAsset(product, plan.OutputPath); }
                    catch { if (!AssetDatabase.Contains(product)) UnityEngine.Object.DestroyImmediate(product); throw; }
                }
                product.ConfigureMetadata(draft.Id, draft.DisplayLabel, draft.Id, draft.price, draft.availableInStore);
                product.ConfigurePresentation(draft.vehiclePrefab, draft.thumbnail);
                EditorUtility.SetDirty(product);
                AssetDatabase.SaveAssetIfDirty(product);
                if (!draft.storeCatalog.ProductAssets.Contains(product))
                {
                    draft.storeCatalog.SetProducts(draft.storeCatalog.ProductAssets.Concat(new ScriptableObject[] { product }).ToArray());
                    EditorUtility.SetDirty(draft.storeCatalog);
                    AssetDatabase.SaveAssetIfDirty(draft.storeCatalog);
                }
                draft.RecordPublication(product, plan.InputFingerprint, HashFile(plan.OutputPath));
                EditorUtility.SetDirty(draft);
                AssetDatabase.SaveAssetIfDirty(draft);
                journal.state = "Committed";
                File.WriteAllText(journalPath, JsonUtility.ToJson(journal, true), Encoding.UTF8);
                return product;
            }
            catch (Exception failure)
            {
                // Do not guess whether external edits happened during a failed save/import callback.
                // Keep the last valid bytes for explicit recovery rather than overwriting newer work.
                throw new IOException("Publication did not finish. Further writes are blocked. Recovery journal: " + journalPath, failure);
            }
            finally { writing = false; }
        }

        public static string ValidateOutputPath(string path) => ValidateOutputPath(path, false);
        public static string ValidatePrefabOutputPath(string path) => ValidateOutputPath(path, false, ".prefab");

        private static string ValidateOutputPath(string path, bool ownedExisting, string extension = ".asset")
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains('\\'))
                throw new ArgumentException("Choose a project path beneath Assets.");
            if (!path.EndsWith(extension, StringComparison.Ordinal)) throw new ArgumentException("Expected output extension: " + extension);
            foreach (string part in path.Split('/')) ValidateSegment(part);
            if (path.Length > 240) throw new ArgumentException("The output path exceeds the Studio's cross-platform length budget.");
            if (path.Split('/').Any(p => p.Equals("Resources", StringComparison.OrdinalIgnoreCase) || p.Equals("StreamingAssets", StringComparison.OrdinalIgnoreCase) || p.Equals("Editor", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Runtime listings cannot be generated into Editor, Resources or StreamingAssets.");
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) throw new ArgumentException("Choose an existing output folder; no unrelated folders are created implicitly.");
            for (var directory = new DirectoryInfo(parent); directory != null; directory = directory.Parent)
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Publication through a symbolic-link directory is not supported.");
                if (directory.FullName == Path.GetFullPath("Assets")) break;
            }
            foreach (string sibling in Directory.EnumerateFileSystemEntries(parent))
                if (!(ownedExisting && Path.GetFileName(sibling) == Path.GetFileName(path)) &&
                    string.Equals(Path.GetFileName(sibling).Normalize(), Path.GetFileName(path).Normalize(), StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("A case-insensitive or Unicode-equivalent destination already exists.");
            return path;
        }

        public static void ValidateSegment(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == ".." || segment.Length > 120 ||
                segment != segment.Normalize(NormalizationForm.FormC) || segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal) ||
                segment.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)))
                throw new ArgumentException("Invalid cross-platform folder or file name: " + segment);
            string stem = segment.Split('.')[0].ToUpperInvariant();
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem))
                throw new ArgumentException("Windows reserved name: " + segment);
        }

        public static string RecoveryDirectory => Path.GetFullPath("Library/VehicleProfiles/PublicationRecovery");
        public static bool HasPendingRecovery() => Directory.Exists(RecoveryDirectory) && Directory.EnumerateFiles(RecoveryDirectory, "*.json")
            .Any(p => JsonUtility.FromJson<RecoveryJournal>(File.ReadAllText(p)).state != "Committed");

        private static string HashFile(string path)
        {
            if (!File.Exists(path)) return "";
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
        }
    }
}
