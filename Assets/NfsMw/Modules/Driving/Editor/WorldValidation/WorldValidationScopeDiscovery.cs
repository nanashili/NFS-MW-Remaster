using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor.WorldValidation
{
    /// <summary>
    /// Converts a user scope into a deterministic, read-only set of scene and
    /// asset paths. It never saves or dirties a scene while discovering it.
    /// </summary>
    public static class WorldValidationScopeDiscovery
    {
        public static WorldValidationScopeSnapshot Capture(WorldValidationRunRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var snapshot = new WorldValidationScopeSnapshot(request.scope);

            switch (request.scope)
            {
                case WorldValidationScopeKind.SelectedObjects:
                    CaptureSelection(snapshot);
                    break;
                case WorldValidationScopeKind.DistrictCell:
                    CaptureDistrictCell(snapshot, request);
                    break;
                case WorldValidationScopeKind.OpenScenes:
                    CaptureOpenScenes(snapshot);
                    break;
                case WorldValidationScopeKind.ExplicitScenes:
                    foreach (string path in request.explicitScenePaths ?? Array.Empty<string>()) AddScene(snapshot, path);
                    if (snapshot.ScenePaths.Count == 0) snapshot.AddUnavailable("Explicit Scenes scope contains no scene paths.");
                    break;
                case WorldValidationScopeKind.ChangedAssets:
                    CaptureChangedAssets(snapshot);
                    break;
                case WorldValidationScopeKind.BuildContent:
                    CaptureBuildContent(snapshot);
                    break;
                case WorldValidationScopeKind.Project:
                    CaptureProject(snapshot);
                    break;
                default:
                    snapshot.AddUnavailable("Unknown validation scope.");
                    break;
            }

            if (request.scope == WorldValidationScopeKind.OpenScenes && snapshot.ScenePaths.Count == 0)
                snapshot.AddUnavailable("No loaded scenes were available to inspect.");

            if (request.incremental && request.scope != WorldValidationScopeKind.ChangedAssets)
                ApplyIncrementalFilter(snapshot);

            FinalizeRevisions(snapshot);
            snapshot.FinalizeSnapshot();
            return snapshot;
        }

        private static void ApplyIncrementalFilter(WorldValidationScopeSnapshot snapshot)
        {
            if (!WorldValidationDependencyCache.HasIndex)
            {
                snapshot.AddUnavailable("Incremental validation requested, but no dependency index exists. The requested scope was retained for a conservative full scan; run Project or Build Content once to seed incremental invalidation.");
                return;
            }

            snapshot.RetainChanged(WorldValidationDependencyCache.GetChangedPaths());
        }

        private static void CaptureSelection(WorldValidationScopeSnapshot snapshot)
        {
            UnityEngine.Object[] selected = Selection.objects ?? Array.Empty<UnityEngine.Object>();
            foreach (UnityEngine.Object target in selected)
            {
                if (target == null) continue;
                string globalId = SafeGlobalId(target);
                snapshot.AddSelectedObject(globalId);

                string assetPath = AssetDatabase.GetAssetPath(target);
                if (!string.IsNullOrEmpty(assetPath)) AddAssetAndDependencies(snapshot, assetPath);

                if (target is Component component && component.gameObject.scene.IsValid())
                    AddScene(snapshot, component.gameObject.scene.path);
                else if (target is GameObject gameObject && gameObject.scene.IsValid())
                    AddScene(snapshot, gameObject.scene.path);
            }
            if (selected.Length == 0) snapshot.AddUnavailable("Nothing is selected. Select an asset or scene object first.");
        }

        /// <summary>
        /// Captures a city-focused scope without instantiating or saving any
        /// generated content. A district selection targets the whole authored
        /// district; a generated-instance selection narrows the run to that
        /// instance's authored cell. Explicit cell tokens can further narrow
        /// either selection.
        /// </summary>
        private static void CaptureDistrictCell(WorldValidationScopeSnapshot snapshot, WorldValidationRunRequest request)
        {
            var selectedDistricts = new List<CityDistrict>();
            UnityEngine.Object[] selected = Selection.objects ?? Array.Empty<UnityEngine.Object>();
            foreach (UnityEngine.Object target in selected)
            {
                if (target == null) continue;
                snapshot.AddSelectedObject(SafeGlobalId(target));

                CityDistrict district = ResolveDistrict(target);
                if (district == null) continue;
                if (!selectedDistricts.Contains(district)) selectedDistricts.Add(district);
                AddDistrictSource(snapshot, district);

                CityGeneratedInstance instance = ResolveGeneratedInstance(target);
                if (instance != null) AddCellAtPosition(snapshot, district, instance.transform.position);
            }

            string requestedDistrictId = (request.districtId ?? string.Empty).Trim();
            if (requestedDistrictId.Length > 0) snapshot.AddDistrict(requestedDistrictId);

            string[] explicitScenes = request.explicitScenePaths ?? Array.Empty<string>();
            foreach (string path in explicitScenes) AddScene(snapshot, path);

            // With no object/id anchor, DistrictCell remains useful as an
            // authored-scene query. Open scenes are already resident, so this
            // does not cause an implicit scene load during discovery.
            if (selectedDistricts.Count == 0 && requestedDistrictId.Length == 0 && explicitScenes.Length == 0)
                CaptureOpenScenes(snapshot);

            string defaultDistrictId = requestedDistrictId;
            if (defaultDistrictId.Length == 0)
            {
                string[] selectedIds = snapshot.DistrictIds.Distinct(StringComparer.Ordinal).ToArray();
                if (selectedIds.Length == 1) defaultDistrictId = selectedIds[0];
            }

            foreach (string token in request.cellIds ?? Array.Empty<string>())
            {
                if (!TryParseCellToken(token, defaultDistrictId, out string districtId, out Vector2Int cell, out string failure))
                {
                    snapshot.AddUnavailable(failure);
                    continue;
                }
                snapshot.AddDistrict(districtId);
                snapshot.AddCell(districtId, cell);
            }

            // Resolve requested IDs in resident scenes so the report records
            // their actual scene ownership. Unloaded explicit scenes remain in
            // the snapshot and are resolved by the provider when safely opened
            // for inspection.
            var requestedIds = snapshot.DistrictIds
                .Where(value => !string.IsNullOrEmpty(value))
                .ToHashSet(StringComparer.Ordinal);
            var foundIds = new HashSet<string>(StringComparer.Ordinal);
            if (requestedIds.Count > 0)
            {
                foreach (CityDistrict district in FindLoadedDistricts(requestedIds))
                {
                    foundIds.Add(district.id);
                    AddDistrictSource(snapshot, district);
                }
            }

            if (requestedIds.Count > 0 && explicitScenes.Length == 0)
            {
                foreach (string id in requestedIds.Where(id => !foundIds.Contains(id)).OrderBy(id => id, StringComparer.Ordinal))
                    snapshot.AddUnavailable("City district '" + id + "' was not found in the loaded scenes.");
            }

            if (snapshot.ScenePaths.Count == 0)
            {
                if (selected.Length == 0 && requestedIds.Count == 0 && explicitScenes.Length == 0)
                    snapshot.AddUnavailable("DistrictCell scope requires a CityDistrict/CityGeneratedInstance selection, a district ID, an explicit scene, or a loaded scene containing city content.");
                else if (requestedIds.Count > 0 && explicitScenes.Length > 0)
                    snapshot.AddUnavailable("DistrictCell scope has no available explicit scene containing the requested district yet.");
            }
        }

        private static IEnumerable<CityDistrict> FindLoadedDistricts(ISet<string> districtIds)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                foreach (CityDistrict district in scene.GetRootGameObjects()
                             .SelectMany(root => root.GetComponentsInChildren<CityDistrict>(true))
                             .Where(value => value != null && districtIds.Contains(value.id ?? string.Empty))
                             .OrderBy(value => value.id, StringComparer.Ordinal))
                    yield return district;
            }
        }

        private static CityDistrict ResolveDistrict(UnityEngine.Object target)
        {
            if (target is CityDistrict district) return district;
            if (target is CityGeneratedInstance generated) return generated.GetComponentInParent<CityDistrict>();
            if (target is Component component) return component.GetComponentInParent<CityDistrict>();
            if (target is GameObject gameObject) return gameObject.GetComponentInParent<CityDistrict>();
            return null;
        }

        private static CityGeneratedInstance ResolveGeneratedInstance(UnityEngine.Object target)
        {
            if (target is CityGeneratedInstance generated) return generated;
            if (target is Component component) return component.GetComponentInParent<CityGeneratedInstance>();
            if (target is GameObject gameObject) return gameObject.GetComponentInParent<CityGeneratedInstance>();
            return null;
        }

        private static void AddDistrictSource(WorldValidationScopeSnapshot snapshot, CityDistrict district)
        {
            if (district == null) return;
            if (string.IsNullOrWhiteSpace(district.id)) snapshot.AddUnavailable(district.name + " — district has no stable ID.");
            else snapshot.AddDistrict(district.id);

            Scene scene = district.gameObject.scene;
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                snapshot.AddUnavailable(district.name + " — district scene is unsaved or unavailable.");
                return;
            }
            AddScene(snapshot, scene.path);
            if (scene.isDirty) snapshot.AddUnavailable(scene.path + " — scene has unsaved changes; asset revision cannot represent them.");
        }

        private static void AddCellAtPosition(WorldValidationScopeSnapshot snapshot, CityDistrict district, Vector3 worldPosition)
        {
            if (district == null || string.IsNullOrWhiteSpace(district.id)) return;
            if (!float.IsFinite(district.cellSize) || district.cellSize < 10)
            {
                snapshot.AddUnavailable(district.name + " — cell coordinates cannot be derived until cell size is finite and at least 10 m.");
                return;
            }
            Vector3 local = district.transform.InverseTransformPoint(worldPosition);
            if (!float.IsFinite(local.x) || !float.IsFinite(local.z))
            {
                snapshot.AddUnavailable(district.name + " — selected generated instance has a non-finite position.");
                return;
            }
            snapshot.AddCell(district.id, new Vector2Int(
                Mathf.FloorToInt(local.x / district.cellSize),
                Mathf.FloorToInt(local.z / district.cellSize)));
        }

        /// <summary>Splits semicolon/newline separated x,z or district|x,z tokens.</summary>
        public static string[] ParseCellTokens(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        public static bool TryParseCellToken(string token, string defaultDistrictId,
            out string districtId, out Vector2Int cell, out string failure)
        {
            districtId = string.Empty;
            cell = default;
            failure = string.Empty;
            string value = (token ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                failure = "An empty city cell token was ignored.";
                return false;
            }

            int separator = value.LastIndexOf('|');
            string coordinates = separator >= 0 ? value.Substring(separator + 1).Trim() : value;
            districtId = separator >= 0 ? value.Substring(0, separator).Trim() : (defaultDistrictId ?? string.Empty).Trim();
            if (districtId.Length == 0)
            {
                failure = "Cell token '" + value + "' has no district ID. Select a single district or use districtId|x,z.";
                return false;
            }

            string[] parts = coordinates.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2
                || !int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int z))
            {
                failure = "Cell token '" + value + "' is invalid. Use x,z or districtId|x,z.";
                return false;
            }
            cell = new Vector2Int(x, z);
            return true;
        }

        private static void CaptureOpenScenes(WorldValidationScopeSnapshot snapshot)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                if (string.IsNullOrEmpty(scene.path))
                {
                    snapshot.AddUnavailable("Loaded scene '" + scene.name + "' has not been saved and has no inspectable asset path.");
                    continue;
                }
                AddScene(snapshot, scene.path);
                if (scene.isDirty) snapshot.AddUnavailable(scene.path + " — scene has unsaved changes; asset revision cannot represent them.");
            }
        }

        private static void CaptureChangedAssets(WorldValidationScopeSnapshot snapshot)
        {
            if (!WorldValidationDependencyCache.HasIndex)
            {
                snapshot.AddUnavailable("Changed Assets requires an existing dependency index. Run Project or Build Content once to seed it.");
                return;
            }

            IReadOnlyList<string> changed = WorldValidationDependencyCache.GetChangedPaths();
            foreach (string path in changed) AddAssetAndDependencies(snapshot, path);
            if (changed.Count == 0) snapshot.AddUnavailable("The dependency index has no changed assets since the last index update.");
        }

        private static void CaptureBuildContent(WorldValidationScopeSnapshot snapshot)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>();
            foreach (EditorBuildSettingsScene setting in scenes)
            {
                if (setting == null || !setting.enabled) continue;
                AddScene(snapshot, setting.path);
            }
            if (snapshot.ScenePaths.Count == 0) snapshot.AddUnavailable("Build Content contains no enabled scene entries.");
        }

        private static void CaptureProject(WorldValidationScopeSnapshot snapshot)
        {
            foreach (string path in AssetDatabase.GetAllAssetPaths()
                         .Where(value => !string.IsNullOrEmpty(value) && value.StartsWith("Assets/", StringComparison.Ordinal)
                             && !AssetDatabase.IsValidFolder(value))
                         .OrderBy(value => value, StringComparer.Ordinal))
                snapshot.AddAsset(path);
            if (snapshot.AssetPaths.Count == 0) snapshot.AddUnavailable("The AssetDatabase contains no Assets/ content.");

            // Project scope includes scene content even if no other asset references the scene.
            foreach (string scenePath in snapshot.AssetPaths.Where(IsScenePath).ToArray()) snapshot.AddScene(scenePath);
        }

        private static void AddScene(WorldValidationScopeSnapshot snapshot, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                snapshot.AddUnavailable("An empty scene path was requested.");
                return;
            }
            path = path.Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !IsScenePath(path))
            {
                snapshot.AddUnavailable(path + " — not an Assets/ scene path.");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
            {
                snapshot.AddUnavailable(path + " — scene asset is missing or unavailable.");
                return;
            }
            snapshot.AddScene(path);
            AddAssetAndDependencies(snapshot, path);
        }

        private static void AddAssetAndDependencies(WorldValidationScopeSnapshot snapshot, string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)) return;
            snapshot.AddAsset(path);
            try
            {
                foreach (string dependency in AssetDatabase.GetDependencies(path, true)) snapshot.AddAsset(dependency);
            }
            catch (Exception exception)
            {
                snapshot.AddUnavailable(path + " — dependency discovery failed: " + exception.Message);
            }
        }

        private static void FinalizeRevisions(WorldValidationScopeSnapshot snapshot)
        {
            foreach (string path in snapshot.AssetPaths)
            {
                string revision = WorldValidationFingerprint.AssetRevision(path);
                if (string.IsNullOrEmpty(revision)) snapshot.AddUnavailable(path + " — source revision is unavailable.");
                snapshot.SetRevision(path, revision);
            }
        }

        private static bool IsScenePath(string path)
        {
            return path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeGlobalId(UnityEngine.Object target)
        {
            try { return GlobalObjectId.GetGlobalObjectIdSlow(target).ToString(); }
            catch { return string.Empty; }
        }
    }
}
