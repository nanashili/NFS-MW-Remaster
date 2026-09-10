#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// One-way migration from the recovered 13-part Rockport world to the
    /// terrain-aligned cells authored in RockportMap. A source backup and JSON
    /// reports are written outside Assets before the entry scene is changed.
    /// </summary>
    public static class RockportStreamingMigration
    {
        internal const string EntryScenePath = "Assets/NfsMw/Scenes/World/RockportMap.unity";
        internal const string LegacyEntryScenePath = "Assets/NfsMw/Scenes/Tests/FreeRoam.unity";
        internal const string LegacyCellFolder = "Assets/NfsMw/Scenes/Tests/Rockport";
        internal const string LegacyModelFolder = "Assets/NfsMw/Content/World/Models/Map";
        internal const string WeatherCircuitSource = LegacyModelFolder + "/Demo";
        internal const string WeatherCircuitDestination = "Assets/NfsMw/Content/World/Models/WeatherDemoCircuit";
        internal const string LegacyCityExample = "Assets/NfsMw/Modules/Driving/Examples/RockportCityBuilder.unity";
        internal const string CellFolder = "Assets/NfsMw/Scenes/World/Streaming";
        internal const string ReportFolder = "Artifacts/RockportStreaming";
        internal const string RoadSourcePath = "Assets/NfsMw/Content/World/Maps/Rockport/Navigation/rockport-road-network.json";
        internal const string RoadPublicationPath = "Assets/NfsMw/Content/World/Maps/Rockport/Navigation/RockportRoadNetwork.asset";

        private const string BuildingsRootName = "Rockport Buildings - Original Game Layout";
        private const string RoadsRootName = "Rockport Roads - Paved and Unpaved Routes";
        private const string OriginalGroundRootName = "Rockport Original Ground - Exact Source Meshes";
        private const string TerrainRootName = "Rockport Generated Terrain - 1m Heightmap";
        private const string TerrainTreeRootName = "Rockport Trees - Terrain Openings";
        private const string BreakableTreeRootName = "Rockport Breakable Trees";
        private const string OceanRootName = "Rockport Ocean - Original Coastline";
        private const string GameplayRootName = "Free Roam District";
        private const string StreamerName = "Rockport World Streaming";
        private const string RoadSurfaceName = "Rockport Road Colliders";
        private const string AsphaltProfilePath = "Assets/NfsMw/Modules/Driving/Data/Sensory/AsphaltDry.asset";
        private const string TrafficProfilePath = "Assets/NfsMw/Modules/Driving/Data/Traffic/NFS2015_TRAFFIC_FIDELITY.asset";
        private const float LoadDistance = 384f;
        private const float UnloadDistance = 768f;
        private const int MaximumLoadedCells = 6;
        private const float LookAheadSeconds = 2.5f;
        private const float MaximumLookAhead = 300f;
        private const float CleanupDelay = 8f;

        private static readonly Regex TerrainName = new Regex(
            @"height_x(?<x>\d+)_z(?<z>\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        [Serializable]
        private sealed class InventoryReport
        {
            public string scenePath;
            public string generatedUtc;
            public int rootCount;
            public int dependencyCount;
            public int legacyDependencyCount;
            public string[] legacyDependencies;
            public RootReport[] roots;
        }

        [Serializable]
        private sealed class RootReport
        {
            public string name;
            public bool active;
            public int directChildren;
            public int transforms;
            public int renderers;
            public int terrains;
            public int colliders;
            public int monoBehaviours;
            public int missingScripts;
            public bool hasBounds;
            public Vector3 boundsCenter;
            public Vector3 boundsSize;
        }

        [Serializable]
        private sealed class MigrationReport
        {
            public string generatedUtc;
            public string entryScene;
            public string sourceBackup;
            public int cellCount;
            public int movedBuildings;
            public int movedRoads;
            public int movedTerrainTiles;
            public int movedTreeTerrains;
            public int movedBreakableTrees;
            public int rendererTextureCount;
            public int mipStreamingEnabled;
            public int mipStreamingAlreadyEnabled;
            public int mipStreamingExcluded;
            public int retainedSceneCount;
            public bool legacyEntryRemoved;
            public bool legacyCellsRemoved;
            public bool legacyModelsRemoved;
            public string[] cellScenes;
            public string[] mipStreamingTextures;
            public string[] mipStreamingExclusions;
        }

        [Serializable]
        private sealed class ValidationReport
        {
            public string generatedUtc;
            public string entryScene;
            public int cellCount;
            public int enabledBuildScenes;
            public int missingScripts;
            public int unavailableCellPaths;
            public int legacyDependencyCount;
            public int streamedRendererTextures;
            public int publishedRoadLanes;
            public int roadSurfaceCells;
            public int roadSurfaceColliders;
            public string[] legacyDependencies;
            public string[] errors;
        }

        private sealed class Cell
        {
            public int x;
            public int z;
            public string path;
            public Bounds bounds;
            public Scene scene;
            public GameObject root;
            public DestructionWorld destruction;
            public Transform roadSurface;
        }

        [Serializable]
        private sealed class SourceRoadNetwork
        {
            public int schemaVersion;
            public string sourceSha256;
            public int carpVersion;
            public string coordinateTransform;
            public string fingerprint;
            public SourceRoadNode[] nodes;
            public SourceRoadSegment[] segments;
        }

        [Serializable]
        private sealed class SourceRoadNode
        {
            public int id;
            public Vector3 position;
            public int degree;
            public int[] segments;
        }

        [Serializable]
        private sealed class SourceRoadSegment
        {
            public int id;
            public int nodeA;
            public int nodeB;
            public float arcLength;
            public float chordLength;
            public int flags;
        }

        [MenuItem("NFS MW Remaster/Rockport Streaming/Inventory New Map")]
        public static void InventoryNewMap() => Inventory(EntryScenePath, "inventory.json", "ROCKPORT_STREAMING_INVENTORY_PASS");

        [MenuItem("NFS MW Remaster/Rockport Streaming/Inventory Legacy Gameplay")]
        public static void InventoryLegacyGameplay() => Inventory(LegacyEntryScenePath,
            "legacy-gameplay-inventory.json", "ROCKPORT_LEGACY_GAMEPLAY_INVENTORY_PASS");

        [MenuItem("NFS MW Remaster/Rockport Streaming/Build New Free Roam")]
        public static void BuildOrRefresh()
        {
            RequireEditMode();
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(ReportFolder);

            string backup = Path.Combine(ReportFolder, "RockportMap.pre-streaming.unity");
            if (!File.Exists(backup)) File.Copy(EntryScenePath, backup, false);

            Scene entry = EditorSceneManager.OpenScene(EntryScenePath, OpenSceneMode.Single);
            RockportWorldStreamer existing = FindInScene<RockportWorldStreamer>(entry);
            if (FindRoot(entry, TerrainRootName) == null && existing != null)
            {
                ResumeFinalization(entry, existing);
                return;
            }

            if (AssetDatabase.IsValidFolder(CellFolder) && !AssetDatabase.DeleteAsset(CellFolder))
                throw new InvalidOperationException("Could not replace the generated Rockport streaming cell folder.");
            EnsureAssetFolder(CellFolder);

            var report = new MigrationReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                entryScene = EntryScenePath,
                sourceBackup = backup
            };
            var rendererTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<Cell> cells = null;
            bool legacyRemovalStarted = false;

            try
            {
                cells = CreateCells(entry);
                report.cellCount = cells.Count;
                report.movedTerrainTiles = MoveTerrain(entry, TerrainRootName, cells, false);
                report.movedTreeTerrains = MoveTerrain(entry, TerrainTreeRootName, cells, true);
                report.movedBuildings = MoveSpatialChildren(entry, BuildingsRootName, cells, false);
                report.movedRoads = MoveSpatialChildren(entry, RoadsRootName, cells, true);
                report.movedBreakableTrees = MoveBreakableTrees(entry, cells);

                GameObject originalGround = FindRoot(entry, OriginalGroundRootName);
                if (originalGround != null) UnityEngine.Object.DestroyImmediate(originalGround);

                MoveLegacyGameplay(entry);
                RockportWorldChunkDefinition[] definitions = cells
                    .OrderBy(cell => cell.z).ThenBy(cell => cell.x)
                    .Select((cell, index) => new RockportWorldChunkDefinition(index, cell.path, cell.bounds))
                    .ToArray();
                ConfigureStreamer(entry, definitions);
                ConfigureRockportRoads(entry);

                foreach (Cell cell in cells)
                {
                    CollectRendererTexturePaths(cell.root, rendererTextures);
                    EditorSceneManager.MarkSceneDirty(cell.scene);
                    if (!EditorSceneManager.SaveScene(cell.scene, cell.path))
                        throw new InvalidOperationException("Could not save Rockport cell " + cell.path + ".");
                }
                GameObject ocean = FindRoot(entry, OceanRootName);
                if (ocean != null) CollectRendererTexturePaths(ocean, rendererTextures);

                EditorSceneManager.MarkSceneDirty(entry);
                if (!EditorSceneManager.SaveScene(entry, EntryScenePath))
                    throw new InvalidOperationException("Could not save the Rockport entry scene.");
                foreach (Cell cell in cells)
                    if (cell.scene.IsValid() && cell.scene.isLoaded) EditorSceneManager.CloseScene(cell.scene, true);

                UpdateGameFlowSettings();
                UpdateBuildScenes(cells);
                ApplyMipStreaming(rendererTextures, report);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                ValidateBeforeLegacyRemoval(cells.Select(cell => cell.path));
                ValidateGeneratedState(false);
                PreserveWeatherCircuit();
                legacyRemovalStarted = true;
                RemoveLegacyAssets();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                report.cellScenes = cells.Select(cell => cell.path).OrderBy(path => path).ToArray();
                report.retainedSceneCount = EditorBuildSettings.scenes.Count(scene => scene.enabled);
                report.legacyEntryRemoved = AssetDatabase.LoadMainAssetAtPath(LegacyEntryScenePath) == null;
                report.legacyCellsRemoved = !AssetDatabase.IsValidFolder(LegacyCellFolder);
                report.legacyModelsRemoved = !AssetDatabase.IsValidFolder(LegacyModelFolder);
                ValidateGeneratedState(true);
                WriteJson("migration-report.json", report);
                Debug.Log("ROCKPORT_STREAMING_BUILD_PASS cells=" + report.cellCount
                    + " buildings=" + report.movedBuildings + " roads=" + report.movedRoads
                    + " mipTextures=" + report.rendererTextureCount);
            }
            catch
            {
                if (legacyRemovalStarted) throw;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                File.Copy(backup, EntryScenePath, true);
                if (AssetDatabase.IsValidFolder(CellFolder)) AssetDatabase.DeleteAsset(CellFolder);
                AssetDatabase.Refresh();
                throw;
            }
        }

        [MenuItem("NFS MW Remaster/Rockport Streaming/Validate New Free Roam")]
        public static void Validate() => ValidateGeneratedState(true);

        private static void ValidateGeneratedState(bool requireLegacyRemoval)
        {
            RequireEditMode();
            AssetDatabase.Refresh();
            var errors = new List<string>();
            var legacyDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int missingScripts = 0;
            int unavailablePaths = 0;
            int roadSurfaceCells = 0;
            int roadSurfaceColliders = 0;
            var rendererTexturePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Scene entry = EditorSceneManager.OpenScene(EntryScenePath, OpenSceneMode.Single);
            RockportWorldStreamer streamer = FindInScene<RockportWorldStreamer>(entry);
            if (streamer == null) errors.Add("Entry scene has no RockportWorldStreamer.");
            RockportWorldChunkDefinition[] definitions = streamer == null
                ? Array.Empty<RockportWorldChunkDefinition>()
                : new SerializedObject(streamer).FindProperty("chunks").ToChunkDefinitions();
            if (streamer != null)
            {
                var serialized = new SerializedObject(streamer);
                float load = serialized.FindProperty("loadDistance").floatValue;
                float unload = serialized.FindProperty("unloadDistance").floatValue;
                int residentLimit = serialized.FindProperty("maximumLoadedChunks").intValue;
                if (load <= 0f || unload <= load)
                    errors.Add("Streaming radii are invalid; unload distance must exceed the positive load distance.");
                if (residentLimit <= 0) errors.Add("Streaming resident-cell limit must be positive.");
            }
            if (definitions.Length != 39) errors.Add("Expected 39 cell definitions, found " + definitions.Length + ".");
            if (FindInScene<FreeRoamSession>(entry) == null) errors.Add("Entry scene has no FreeRoamSession.");
            if (FindRoot(entry, OceanRootName) == null) errors.Add("Entry scene lost the global Rockport ocean.");
            RoadNetwork roadNetwork = FindInScene<RoadNetwork>(entry);
            int publishedRoadLanes = roadNetwork != null && roadNetwork.UsesBakedData && roadNetwork.Publication != null
                ? roadNetwork.Publication.Lanes.Count : 0;
            if (publishedRoadLanes != 13076)
                errors.Add("Expected the recovered 13,076-lane Rockport road publication, found " + publishedRoadLanes + ".");
            missingScripts += CountMissingScripts(entry);
            CollectLegacyDependencies(EntryScenePath, legacyDependencies);

            foreach (RockportWorldChunkDefinition definition in definitions)
            {
                if (definition == null || AssetDatabase.LoadAssetAtPath<SceneAsset>(definition.scenePath) == null)
                {
                    unavailablePaths++;
                    continue;
                }
                Scene cell = EditorSceneManager.OpenScene(definition.scenePath, OpenSceneMode.Additive);
                try
                {
                    Terrain[] terrains = cell.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<Terrain>(true)).ToArray();
                    if (terrains.Length == 0) errors.Add(definition.scenePath + " has no terrain tile.");
                    DestructibleProp[] props = cell.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<DestructibleProp>(true)).ToArray();
                    foreach (DestructibleProp prop in props)
                    {
                        UnityEngine.Object world = new SerializedObject(prop).FindProperty("world").objectReferenceValue;
                        Component component = world as Component;
                        if (component == null || component.gameObject.scene != cell)
                            errors.Add(definition.scenePath + " has a destructible with no cell-local world.");
                    }
                    missingScripts += CountMissingScripts(cell);
                    foreach (GameObject root in cell.GetRootGameObjects())
                    {
                        CollectRendererTexturePaths(root, rendererTexturePaths);
                        Transform roadSurface = root.transform.Find(RoadSurfaceName);
                        if (roadSurface == null || roadSurface.GetComponent<VehicleSurface>() == null) continue;
                        roadSurfaceCells++;
                        roadSurfaceColliders += roadSurface.GetComponentsInChildren<MeshCollider>(true).Length;
                    }
                }
                finally { EditorSceneManager.CloseScene(cell, true); }
                CollectLegacyDependencies(definition.scenePath, legacyDependencies);
            }

            GameObject ocean = FindRoot(entry, OceanRootName);
            if (ocean != null) CollectRendererTexturePaths(ocean, rendererTexturePaths);
            int streamedTextures = 0;
            foreach (string path in rendererTexturePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (!IsMipStreamingEligible(importer)) continue;
                if (!importer.streamingMipmaps) errors.Add("Eligible renderer texture is not mip-streamed: " + path);
                else streamedTextures++;
            }

            string[] enabled = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (!enabled.Contains(EntryScenePath)) errors.Add("Entry scene is not enabled in Build Settings.");
            foreach (RockportWorldChunkDefinition definition in definitions)
                if (definition != null && !enabled.Contains(definition.scenePath))
                    errors.Add(definition.scenePath + " is not enabled in Build Settings.");
            if (enabled.Any(IsLegacyAsset)) errors.Add("Build Settings still contains a legacy Rockport scene.");
            if (requireLegacyRemoval && AssetDatabase.IsValidFolder(LegacyModelFolder)) errors.Add("Legacy Rockport model folder still exists.");
            if (requireLegacyRemoval && AssetDatabase.IsValidFolder(LegacyCellFolder)) errors.Add("Legacy Rockport cell folder still exists.");
            if (requireLegacyRemoval && AssetDatabase.LoadMainAssetAtPath(LegacyEntryScenePath) != null) errors.Add("Legacy free-roam scene still exists.");
            if (legacyDependencies.Count > 0) errors.Add("Retained streaming scenes still depend on legacy assets.");
            if (missingScripts > 0) errors.Add("Streaming scenes contain " + missingScripts + " missing scripts.");
            if (unavailablePaths > 0) errors.Add(unavailablePaths + " configured cell scenes are unavailable.");
            if (roadSurfaceCells != 39) errors.Add("Expected one Rockport road-surface owner in every cell, found " + roadSurfaceCells + ".");
            if (roadSurfaceColliders != 11764) errors.Add("Expected 11,764 streamed Rockport road colliders, found " + roadSurfaceColliders + ".");

            var report = new ValidationReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O"), entryScene = EntryScenePath,
                cellCount = definitions.Length, enabledBuildScenes = enabled.Length,
                missingScripts = missingScripts, unavailableCellPaths = unavailablePaths,
                legacyDependencyCount = legacyDependencies.Count, streamedRendererTextures = streamedTextures,
                publishedRoadLanes = publishedRoadLanes, roadSurfaceCells = roadSurfaceCells,
                roadSurfaceColliders = roadSurfaceColliders,
                legacyDependencies = legacyDependencies.OrderBy(path => path).ToArray(), errors = errors.ToArray()
            };
            WriteJson("validation-report.json", report);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            Debug.Log("ROCKPORT_STREAMING_VALIDATION_PASS cells=" + definitions.Length
                + " missingScripts=0 legacyDependencies=0 streamedTextures=" + streamedTextures
                + " roadLanes=" + publishedRoadLanes + " roadColliders=" + roadSurfaceColliders);
        }

        private static List<Cell> CreateCells(Scene entry)
        {
            GameObject terrainRoot = RequireRoot(entry, TerrainRootName);
            var cells = new List<Cell>();
            foreach (Transform child in DirectChildren(terrainRoot.transform))
            {
                Terrain terrain = child.GetComponent<Terrain>();
                Match match = TerrainName.Match(child.name);
                if (terrain == null || !match.Success) continue;
                int x = int.Parse(match.Groups["x"].Value);
                int z = int.Parse(match.Groups["z"].Value);
                Bounds bounds = TerrainBounds(terrain);
                string path = CellFolder + "/Cell_x" + x.ToString("00") + "_z" + z.ToString("00") + ".unity";
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var root = new GameObject("Rockport Cell x" + x.ToString("00") + " z" + z.ToString("00"));
                SceneManager.MoveGameObjectToScene(root, scene);
                if (!EditorSceneManager.SaveScene(scene, path))
                    throw new InvalidOperationException("Could not create Rockport cell " + path + ".");
                cells.Add(new Cell { x = x, z = z, path = path, bounds = bounds, scene = scene, root = root });
            }
            if (cells.Count != 39) throw new InvalidOperationException("Expected 39 authored terrain cells, found " + cells.Count + ".");
            if (cells.Select(cell => cell.path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != cells.Count)
                throw new InvalidOperationException("Terrain coordinates contain duplicate streaming cells.");
            return cells;
        }

        private static int MoveTerrain(Scene entry, string rootName, List<Cell> cells, bool matchSuffix)
        {
            GameObject source = RequireRoot(entry, rootName);
            PrepareSourceForPartition(source);
            int moved = 0;
            foreach (Transform child in DirectChildren(source.transform))
            {
                Match match = TerrainName.Match(child.name);
                if (!match.Success && matchSuffix)
                {
                    int at = child.name.IndexOf("height_x", StringComparison.Ordinal);
                    if (at >= 0) match = TerrainName.Match(child.name.Substring(at));
                }
                if (!match.Success) throw new InvalidOperationException("Cannot map terrain object " + child.name + " to a cell.");
                int x = int.Parse(match.Groups["x"].Value);
                int z = int.Parse(match.Groups["z"].Value);
                Cell cell = cells.SingleOrDefault(item => item.x == x && item.z == z);
                if (cell == null) throw new InvalidOperationException("Terrain object " + child.name + " has no authored cell.");
                MoveToCell(child.gameObject, cell);
                moved++;
            }
            UnityEngine.Object.DestroyImmediate(source);
            return moved;
        }

        private static int MoveSpatialChildren(Scene entry, string rootName, List<Cell> cells, bool roadSurface)
        {
            GameObject source = RequireRoot(entry, rootName);
            PrepareSourceForPartition(source);
            int moved = 0;
            foreach (Transform child in DirectChildren(source.transform))
            {
                Bounds bounds;
                Vector3 point = TryGetBounds(child.gameObject, out bounds) ? bounds.center : child.position;
                Cell cell = FindCell(cells, point);
                MoveToCell(child.gameObject, cell);
                if (roadSurface) child.SetParent(EnsureRoadSurface(cell), true);
                moved++;
            }
            UnityEngine.Object.DestroyImmediate(source);
            return moved;
        }

        private static Transform EnsureRoadSurface(Cell cell)
        {
            if (cell.roadSurface != null) return cell.roadSurface;
            Transform existing = cell.root.transform.Find(RoadSurfaceName);
            if (existing == null)
            {
                var owner = new GameObject(RoadSurfaceName);
                MoveToCell(owner, cell);
                existing = owner.transform;
            }
            VehicleSurface surface = existing.GetComponent<VehicleSurface>()
                ?? existing.gameObject.AddComponent<VehicleSurface>();
            SensorySurfaceProfile profile = AssetDatabase.LoadAssetAtPath<SensorySurfaceProfile>(AsphaltProfilePath);
            if (profile == null) throw new InvalidOperationException("Rockport asphalt profile is missing: " + AsphaltProfilePath);
            surface.SetProfile(profile);
            EditorUtility.SetDirty(surface);
            cell.roadSurface = existing;
            return existing;
        }

        private static int MoveBreakableTrees(Scene entry, List<Cell> cells)
        {
            GameObject source = RequireRoot(entry, BreakableTreeRootName);
            PrepareSourceForPartition(source);
            int moved = 0;
            foreach (Transform child in DirectChildren(source.transform))
            {
                Bounds bounds;
                Vector3 point = TryGetBounds(child.gameObject, out bounds) ? bounds.center : child.position;
                Cell cell = FindCell(cells, point);
                if (cell.destruction == null)
                {
                    var owner = new GameObject("Cell Destruction World");
                    MoveToCell(owner, cell);
                    cell.destruction = owner.AddComponent<DestructionWorld>();
                }
                MoveToCell(child.gameObject, cell);
                foreach (DestructibleProp prop in child.GetComponentsInChildren<DestructibleProp>(true))
                {
                    var serialized = new SerializedObject(prop);
                    serialized.FindProperty("world").objectReferenceValue = cell.destruction;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                foreach (OceanBuoyantBody buoyant in child.GetComponentsInChildren<OceanBuoyantBody>(true))
                    buoyant.SetOcean(null);
                moved++;
            }
            UnityEngine.Object.DestroyImmediate(source);
            return moved;
        }

        private static void PrepareSourceForPartition(GameObject source)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(source)) return;
            GameObject outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(source);
            if (outermost != source)
                throw new InvalidOperationException("Cannot partition nested prefab root " + source.name + ".");
            PrefabUtility.UnpackPrefabInstance(source, PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
        }

        private static void MoveLegacyGameplay(Scene entry)
        {
            Scene legacy = EditorSceneManager.OpenScene(LegacyEntryScenePath, OpenSceneMode.Additive);
            try
            {
                foreach (RockportWorldStreamer streamer in legacy.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<RockportWorldStreamer>(true)).ToArray())
                    UnityEngine.Object.DestroyImmediate(streamer.gameObject);
                foreach (GameObject root in legacy.GetRootGameObjects()) SceneManager.MoveGameObjectToScene(root, entry);
            }
            finally { if (legacy.IsValid() && legacy.isLoaded) EditorSceneManager.CloseScene(legacy, true); }
        }

        private static void ConfigureStreamer(Scene entry, RockportWorldChunkDefinition[] definitions)
        {
            GameObject gameplay = RequireRoot(entry, GameplayRootName);
            foreach (RockportWorldStreamer old in gameplay.GetComponentsInChildren<RockportWorldStreamer>(true))
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            VehicleController vehicle = gameplay.GetComponentInChildren<VehicleController>(true);
            if (vehicle == null) throw new InvalidOperationException("Free-roam gameplay has no player vehicle.");
            var root = new GameObject(StreamerName);
            root.transform.SetParent(gameplay.transform, false);
            RockportWorldStreamer streamer = root.AddComponent<RockportWorldStreamer>();
            streamer.Configure(vehicle.transform, definitions, LoadDistance, UnloadDistance,
                MaximumLoadedCells, LookAheadSeconds, MaximumLookAhead, CleanupDelay);
            EditorUtility.SetDirty(streamer);
        }

        private static void ConfigureRockportRoads(Scene entry)
        {
            RoadNetwork roads = FindInScene<RoadNetwork>(entry);
            if (roads == null) throw new InvalidOperationException("Free-roam gameplay has no RoadNetwork.");
            RoadNetworkAsset publication = PublishRockportRoadNetwork();
            RoadNetwork.ValidatePublication(publication);
            roads.ConfigureBaked(publication);
            EditorUtility.SetDirty(roads);

            VehicleController player = FindInScene<VehicleController>(entry);
            if (player == null) throw new InvalidOperationException("Free-roam gameplay has no player vehicle.");
            RoadBakedLane spawnLane = publication.Lanes[Mathf.Min(2921 * 2, publication.Lanes.Count - 1)];
            RoadLaneSample spawn = spawnLane.Sample(spawnLane.Length * 0.5f);
            Quaternion spawnRotation = Quaternion.LookRotation(spawn.forward, spawn.up);
            player.transform.SetPositionAndRotation(spawn.position + spawn.up * 0.8f, spawnRotation);
            Rigidbody playerBody = player.GetComponent<Rigidbody>();
            if (playerBody != null)
            {
                playerBody.position = player.transform.position;
                playerBody.rotation = spawnRotation;
                EditorUtility.SetDirty(playerBody);
            }
            GameObject playerStart = entry.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(item => item.name == "Player Start")?.gameObject;
            if (playerStart != null) playerStart.transform.SetPositionAndRotation(player.transform.position, spawnRotation);

            PlaceFreeRoamContent(entry, roads, publication, spawnLane);
            ConfigureTraffic(entry, roads, publication, spawnLane);
        }

        private static RoadNetworkAsset PublishRockportRoadNetwork()
        {
            TextAsset sourceAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(RoadSourcePath);
            if (sourceAsset == null) throw new InvalidOperationException("Recovered Rockport road source is missing: " + RoadSourcePath);
            SourceRoadNetwork source = JsonUtility.FromJson<SourceRoadNetwork>(sourceAsset.text);
            if (source == null || source.schemaVersion != 1 || source.carpVersion != 122
                || source.nodes == null || source.nodes.Length != 4385 || source.segments == null || source.segments.Length != 6538
                || string.IsNullOrEmpty(source.fingerprint) || string.IsNullOrEmpty(source.sourceSha256))
                throw new InvalidOperationException("Recovered Rockport road source failed its publication contract.");

            string publicationFingerprint = source.fingerprint + "|bidirectional-offset-v1";
            RoadNetworkAsset existing = AssetDatabase.LoadAssetAtPath<RoadNetworkAsset>(RoadPublicationPath);
            if (existing != null && existing.SchemaVersion == RoadNetworkAsset.CurrentSchema
                && existing.Fingerprint == publicationFingerprint && existing.Lanes.Count == source.segments.Length * 2)
                return existing;
            if (existing != null && !AssetDatabase.DeleteAsset(RoadPublicationPath))
                throw new InvalidOperationException("Could not replace the Rockport road publication.");

            SensorySurfaceProfile asphalt = AssetDatabase.LoadAssetAtPath<SensorySurfaceProfile>(AsphaltProfilePath);
            if (asphalt == null) throw new InvalidOperationException("Rockport asphalt profile is missing: " + AsphaltProfilePath);
            RoadId[] laneIds = new RoadId[source.segments.Length * 2];
            for (int i = 0; i < laneIds.Length; i++) laneIds[i] = StableRoadId(publicationFingerprint + ":lane:" + i);
            var lanes = new RoadBakedLane[laneIds.Length];
            for (int segmentIndex = 0; segmentIndex < source.segments.Length; segmentIndex++)
            {
                SourceRoadSegment segment = source.segments[segmentIndex];
                if (segment.id != segmentIndex || segment.nodeA < 0 || segment.nodeA >= source.nodes.Length
                    || segment.nodeB < 0 || segment.nodeB >= source.nodes.Length || segment.nodeA == segment.nodeB)
                    throw new InvalidOperationException("Recovered Rockport segment " + segmentIndex + " has invalid endpoints.");
                RoadId roadId = StableRoadId(publicationFingerprint + ":segment:" + segmentIndex);
                lanes[segmentIndex * 2] = BuildLane(source, segmentIndex, false, laneIds, roadId, asphalt);
                lanes[segmentIndex * 2 + 1] = BuildLane(source, segmentIndex, true, laneIds, roadId, asphalt);
            }
            var publication = ScriptableObject.CreateInstance<RoadNetworkAsset>();
            publication.Initialize(StableRoadId(publicationFingerprint + ":network"), publicationFingerprint,
                lanes, Array.Empty<RoadBakedChunk>(), lanes.Length);
            AssetDatabase.CreateAsset(publication, RoadPublicationPath);
            EditorUtility.SetDirty(publication);
            AssetDatabase.SaveAssets();
            return publication;
        }

        private static RoadBakedLane BuildLane(SourceRoadNetwork source, int segmentIndex, bool reverse,
            RoadId[] laneIds, RoadId roadId, SensorySurfaceProfile asphalt)
        {
            SourceRoadSegment segment = source.segments[segmentIndex];
            int from = reverse ? segment.nodeB : segment.nodeA;
            int to = reverse ? segment.nodeA : segment.nodeB;
            Vector3 start = source.nodes[from].position;
            Vector3 end = source.nodes[to].position;
            Vector3 forward = (end - start).normalized;
            Vector3 left = Vector3.Cross(forward, Vector3.up).normalized;
            if (left.sqrMagnitude < 0.5f) left = Vector3.left;
            Vector3 up = Vector3.Cross(left, forward).normalized;
            float offset = Mathf.Min(1.65f, Vector3.Distance(start, end) * 0.12f);
            Vector3 rightOffset = -left * offset;
            Vector3[] points =
            {
                start,
                Vector3.Lerp(start, end, 0.2f) + rightOffset,
                Vector3.Lerp(start, end, 0.8f) + rightOffset,
                end
            };
            var samples = new RoadLaneSample[points.Length];
            float distance = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if (i > 0) distance += Vector3.Distance(points[i - 1], points[i]);
                Vector3 tangent = i + 1 < points.Length ? (points[i + 1] - points[i]).normalized
                    : (points[i] - points[i - 1]).normalized;
                Vector3 sampleLeft = Vector3.Cross(tangent, Vector3.up).normalized;
                if (sampleLeft.sqrMagnitude < 0.5f) sampleLeft = left;
                Vector3 sampleUp = Vector3.Cross(sampleLeft, tangent).normalized;
                samples[i] = new RoadLaneSample
                {
                    station = distance,
                    distance = distance,
                    width = 3.4f,
                    position = points[i],
                    forward = tangent,
                    left = sampleLeft,
                    up = sampleUp
                };
            }

            List<RoadId> successorIds = new List<RoadId>();
            foreach (int adjacent in source.nodes[to].segments ?? Array.Empty<int>())
            {
                if (adjacent < 0 || adjacent >= source.segments.Length || adjacent == segmentIndex) continue;
                SourceRoadSegment next = source.segments[adjacent];
                int directed = next.nodeA == to ? adjacent * 2 : next.nodeB == to ? adjacent * 2 + 1 : -1;
                if (directed >= 0) successorIds.Add(laneIds[directed]);
            }
            if (successorIds.Count == 0) successorIds.Add(laneIds[segmentIndex * 2 + (reverse ? 0 : 1)]);
            successorIds.Sort((a, b) => string.CompareOrdinal(a.ToString(), b.ToString()));
            return new RoadBakedLane(laneIds[segmentIndex * 2 + (reverse ? 1 : 0)], roadId,
                RoadClass.Arterial, 20f, samples, successorIds.ToArray(), asphalt, segmentIndex * 2 + (reverse ? 1 : 0));
        }

        private static RoadId StableRoadId(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var builder = new StringBuilder(32);
                for (int i = 0; i < 16; i++) builder.Append(digest[i].ToString("x2"));
                return JsonUtility.FromJson<RoadId>("{\"value\":\"" + builder + "\"}");
            }
        }

        private static void PlaceFreeRoamContent(Scene entry, RoadNetwork roads, RoadNetworkAsset publication,
            RoadBakedLane spawnLane)
        {
            RoadBakedLane[] candidates = publication.Lanes.Where(lane => lane.Length >= 35f).ToArray();
            if (candidates.Length < 64) throw new InvalidOperationException("Rockport road publication has too few placement lanes.");
            WorldLocation[] locations = entry.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<WorldLocation>(true))
                .OrderBy(location => location.Kind).ThenBy(location => location.Id, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < locations.Length; i++)
            {
                RoadBakedLane lane = candidates[(i * 977 + 311) % candidates.Length];
                RoadLaneSample sample = lane.Sample(lane.Length * 0.5f);
                locations[i].transform.SetPositionAndRotation(sample.position + sample.up * 0.15f,
                    Quaternion.LookRotation(sample.forward, sample.up));
                EditorUtility.SetDirty(locations[i]);
            }

            FreeRoamEventDefinition[] events = entry.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<FreeRoamEventDefinition>(true))
                .OrderBy(definition => definition.Id, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < events.Length; i++)
            {
                RoadBakedLane first = candidates[(i * 1499 + 727) % candidates.Length];
                RoadBakedLane last = candidates[(i * 2267 + 1777) % candidates.Length];
                var route = new List<Vector3>();
                if (!roads.TryRoute(first.Sample(first.Length * 0.5f).position, last.Sample(last.Length * 0.5f).position, route)
                    || route.Count < 3)
                    throw new InvalidOperationException("Could not route Rockport free-roam event " + events[i].Id + ".");
                Vector3[] checkpoints = DownsampleRoute(route, 12);
                events[i].BindRoute(null);
                events[i].Configure(events[i].Id, events[i].DisplayName, events[i].Kind, checkpoints,
                    events[i].Laps, events[i].TimeLimit, events[i].Reward, events[i].TargetSpeedKph);
                Vector3 direction = checkpoints.Length > 1 ? checkpoints[1] - checkpoints[0] : first.Sample(0).forward;
                events[i].transform.SetPositionAndRotation(checkpoints[0], Quaternion.LookRotation(direction.normalized, Vector3.up));
                EditorUtility.SetDirty(events[i]);
            }

            EnsureAmbientResidents(entry);
            AmbientPedestrian[] residents = entry.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<AmbientPedestrian>(true)).OrderBy(item => item.name, StringComparer.Ordinal).ToArray();
            if (residents.Length != 32)
                throw new InvalidOperationException("Expected 32 Rockport sidewalk residents, found " + residents.Length + ".");
            for (int i = 0; i < residents.Length; i++)
            {
                RoadBakedLane lane = candidates[(i * 173 + 41) % candidates.Length];
                RoadLaneSample a = lane.Sample(lane.Length * 0.2f);
                RoadLaneSample b = lane.Sample(lane.Length * 0.8f);
                Vector3 sidewalkA = a.position + a.left * 5f + a.up * 0.1f;
                Vector3 sidewalkB = b.position + b.left * 5f + b.up * 0.1f;
                residents[i].Configure(new[] { sidewalkA, sidewalkB }, 2015 + i * 7919);
                residents[i].transform.SetPositionAndRotation(sidewalkA,
                    Quaternion.LookRotation((sidewalkB - sidewalkA).normalized, Vector3.up));
                EditorUtility.SetDirty(residents[i]);
            }
        }

        private static void EnsureAmbientResidents(Scene entry)
        {
            AmbientPedestrian[] existing = entry.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<AmbientPedestrian>(true)).ToArray();
            if (existing.Length == 32) return;
            if (existing.Length != 0)
                throw new InvalidOperationException("Cannot complete a partial ambient-resident set of " + existing.Length + ".");
            GameObject gameplay = RequireRoot(entry, GameplayRootName);
            var root = new GameObject("Rockport Sidewalk Residents");
            root.transform.SetParent(gameplay.transform, false);
            Material[] materials =
            {
                AssetDatabase.LoadAssetAtPath<Material>("Assets/NfsMw/Modules/Driving/Data/Materials/FreeRoamBlue.mat"),
                AssetDatabase.LoadAssetAtPath<Material>("Assets/NfsMw/Modules/Driving/Data/Materials/FreeRoamYellow.mat"),
                AssetDatabase.LoadAssetAtPath<Material>("Assets/NfsMw/Modules/Driving/Data/Materials/FreeRoamRed.mat")
            };
            if (materials.Any(material => material == null))
                throw new InvalidOperationException("Free-roam resident materials are missing.");
            for (int i = 0; i < 32; i++)
            {
                GameObject resident = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                resident.name = "Ambient Resident " + (i + 1).ToString("00");
                resident.transform.SetParent(root.transform, false);
                resident.transform.localScale = new Vector3(0.42f, 0.85f, 0.42f);
                Collider collider = resident.GetComponent<Collider>();
                if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
                resident.GetComponent<Renderer>().sharedMaterial = materials[i % materials.Length];
                resident.AddComponent<AmbientPedestrian>();
            }
        }

        private static Vector3[] DownsampleRoute(List<Vector3> route, int maximum)
        {
            if (route.Count <= maximum) return route.ToArray();
            var result = new Vector3[maximum];
            for (int i = 0; i < maximum; i++) result[i] = route[Mathf.RoundToInt(i * (route.Count - 1f) / (maximum - 1f))];
            return result;
        }

        private static void ConfigureTraffic(Scene entry, RoadNetwork roads, RoadNetworkAsset publication,
            RoadBakedLane spawnLane)
        {
            FreeRoamTraffic traffic = FindInScene<FreeRoamTraffic>(entry);
            VehicleController player = FindInScene<VehicleController>(entry);
            if (traffic == null || player == null) throw new InvalidOperationException("Free-roam traffic bindings are missing.");
            TrafficWorldDirector director = traffic.GetComponent<TrafficWorldDirector>()
                ?? traffic.gameObject.AddComponent<TrafficWorldDirector>();
            RoadTrafficSignals signals = roads.GetComponent<RoadTrafficSignals>();
            TrafficWorldProfile profile = AssetDatabase.LoadAssetAtPath<TrafficWorldProfile>(TrafficProfilePath);
            if (profile == null) throw new InvalidOperationException("Traffic profile is missing: " + TrafficProfilePath);
            director.Configure(roads, signals, traffic.Civilians, player, traffic.Police, profile);

            if (traffic.Civilians.Length < 6 || traffic.Civilians.Take(6).Any(motor => motor == null || motor.VehicleProfile == null))
                throw new InvalidOperationException("Rockport starting traffic requires six configured physical vehicle rigs.");
            RockportWorldStreamer streamer = FindInScene<RockportWorldStreamer>(entry);
            Vector3 spawnPoint = spawnLane.Sample(spawnLane.Length * 0.5f).position;
            RockportWorldChunkDefinition spawnCell = streamer?.Chunks
                .OrderBy(definition => HorizontalDistance(new Bounds(definition.boundsCenter, definition.boundsSize), spawnPoint))
                .FirstOrDefault();
            if (spawnCell == null) throw new InvalidOperationException("Rockport streaming manifest has no player-start cell.");
            Scene placementScene = SceneManager.GetSceneByPath(spawnCell.scenePath);
            bool closePlacementScene = !placementScene.IsValid() || !placementScene.isLoaded;
            if (closePlacementScene) placementScene = EditorSceneManager.OpenScene(spawnCell.scenePath, OpenSceneMode.Additive);

            try
            {
                Physics.SyncTransforms();
                var candidates = new List<int>();
                Bounds placementBounds = new Bounds(spawnCell.boundsCenter, spawnCell.boundsSize);
                for (int i = 0; i < publication.Lanes.Count; i++)
                {
                    RoadBakedLane lane = publication.Lanes[i];
                    RoadLaneSample midpoint = lane.Sample(lane.Length * 0.5f);
                    if (lane.Length >= 35f && HorizontalDistance(placementBounds, midpoint.position) <= 0.01f
                        && Vector3.Distance(midpoint.position, spawnPoint) < 300f)
                        candidates.Add(i);
                }
                var initial = new List<TrafficInitialTrip>();
                var occupied = new List<Vector3>();
                int[] route = new int[roads.Lanes.Count];
                for (int candidate = 0; candidate < candidates.Count && initial.Count < 6; candidate++)
                {
                    int origin = candidates[candidate];
                    RoadBakedLane originLane = publication.Lanes[origin];
                    RoadLaneSample originSample = originLane.Sample(originLane.Length * 0.5f);
                    if (occupied.Any(point => Vector3.Distance(point, originSample.position) < 35f
                        || Vector3.Distance(point, originLane.Samples[0].position) < 10f)
                        || !InitialTrafficPlacementClear(traffic.Civilians[initial.Count], originSample)) continue;
                    for (int offset = candidates.Count - 1; offset >= 0; offset--)
                    {
                        int destination = candidates[offset];
                        if (destination == origin || !roads.Lanes.TryRoute(origin, destination, 2015 + origin, route, out int count)
                            || count < 3) continue;
                        initial.Add(new TrafficInitialTrip
                        {
                            originLaneId = roads.Lanes[origin].Id,
                            destinationLaneId = roads.Lanes[destination].Id,
                            seed = 2015 + origin * 7919,
                            along = roads.Lanes[origin].Length * 0.5f
                        });
                        occupied.Add(originSample.position);
                        break;
                    }
                }
                if (initial.Count != 6) throw new InvalidOperationException("Could not author six collision-free Rockport starting traffic trips.");
                director.ConfigureInitialTrips(initial.ToArray());
                EditorUtility.SetDirty(director);
            }
            finally
            {
                if (closePlacementScene && placementScene.IsValid() && placementScene.isLoaded)
                    EditorSceneManager.CloseScene(placementScene, true);
            }
        }

        private static bool InitialTrafficPlacementClear(RoadVehicleMotor motor, RoadLaneSample sample)
        {
            TrafficVehicleProfile profile = motor.VehicleProfile;
            Vector3 center = sample.position + Vector3.up * 0.65f + profile.colliderCenter;
            return !Physics.CheckBox(center, profile.colliderSize * 0.5f,
                Quaternion.LookRotation(sample.forward, sample.up), ~0, QueryTriggerInteraction.Ignore);
        }

        private static float HorizontalDistance(Bounds bounds, Vector3 point)
        {
            Vector3 closest = bounds.ClosestPoint(new Vector3(point.x, bounds.center.y, point.z));
            return new Vector2(closest.x - point.x, closest.z - point.z).magnitude;
        }

        private static void ApplyMipStreaming(HashSet<string> texturePaths, MigrationReport report)
        {
            var enabled = new List<string>();
            var exclusions = new List<string>();
            int changed = 0;
            int already = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in texturePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (!IsMipStreamingEligible(importer))
                    {
                        exclusions.Add(path);
                        continue;
                    }
                    enabled.Add(path);
                    if (importer.streamingMipmaps) { already++; continue; }
                    importer.streamingMipmaps = true;
                    importer.streamingMipmapsPriority = 0;
                    importer.SaveAndReimport();
                    changed++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            report.rendererTextureCount = texturePaths.Count;
            report.mipStreamingEnabled = changed;
            report.mipStreamingAlreadyEnabled = already;
            report.mipStreamingExcluded = exclusions.Count;
            report.mipStreamingTextures = enabled.ToArray();
            report.mipStreamingExclusions = exclusions.ToArray();
        }

        private static bool IsMipStreamingEligible(TextureImporter importer)
            => importer != null && importer.mipmapEnabled
                && importer.textureShape == TextureImporterShape.Texture2D
                && importer.textureType != TextureImporterType.Sprite;

        private static void UpdateGameFlowSettings()
        {
            const string path = "Assets/NfsMw/Content/Frontend/UI/Data/GameFlowSettings.asset";
            GameFlowSettings settings = AssetDatabase.LoadAssetAtPath<GameFlowSettings>(path);
            if (settings == null) throw new InvalidOperationException("Game flow settings are missing: " + path);
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("worldScenePath").stringValue = EntryScenePath;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
        }

        private static void UpdateBuildScenes(IEnumerable<Cell> cells)
            => UpdateBuildScenes(cells.Select(cell => cell.path));

        private static void UpdateBuildScenes(IEnumerable<string> cellPaths)
        {
            var retained = EditorBuildSettings.scenes
                .Where(scene => !IsLegacyAsset(scene.path) && scene.path != EntryScenePath && !IsUnder(scene.path, CellFolder))
                .ToList();
            retained.Add(new EditorBuildSettingsScene(EntryScenePath, true));
            retained.AddRange(cellPaths.OrderBy(path => path).Select(path => new EditorBuildSettingsScene(path, true)));
            EditorBuildSettings.scenes = retained.ToArray();
        }

        private static void ResumeFinalization(Scene entry, RockportWorldStreamer streamer)
        {
            RockportWorldChunkDefinition[] definitions = new SerializedObject(streamer)
                .FindProperty("chunks").ToChunkDefinitions();
            if (definitions.Length != 39)
                throw new InvalidOperationException("Cannot resume Rockport migration with " + definitions.Length + " cells.");

            var textures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int buildings = 0, roads = 0, terrainTiles = 0, treeTerrains = 0, breakableTrees = 0;
            foreach (RockportWorldChunkDefinition definition in definitions)
            {
                Scene cell = EditorSceneManager.OpenScene(definition.scenePath, OpenSceneMode.Additive);
                try
                {
                    foreach (GameObject root in cell.GetRootGameObjects())
                    {
                        var descriptor = new Cell { path = definition.scenePath, scene = cell, root = root };
                        Transform roadSurface = EnsureRoadSurface(descriptor);
                        foreach (Transform child in DirectChildren(root.transform))
                        {
                            if (child == roadSurface || child.GetComponent<Terrain>() != null
                                || child.GetComponent<DestructionWorld>() != null
                                || child.GetComponentInChildren<DestructibleProp>(true) != null
                                || child.GetComponentInChildren<MeshCollider>(true) == null) continue;
                            child.SetParent(roadSurface, true);
                        }
                        CollectRendererTexturePaths(root, textures);
                        foreach (Transform child in DirectChildren(root.transform))
                        {
                            if (child.GetComponent<Terrain>() != null)
                            {
                                if (child.name.StartsWith("height_x", StringComparison.OrdinalIgnoreCase)) terrainTiles++;
                                else treeTerrains++;
                            }
                            else if (child.GetComponentInChildren<DestructibleProp>(true) != null) breakableTrees++;
                            else if (child == roadSurface) roads += roadSurface.GetComponentsInChildren<MeshCollider>(true).Length;
                            else if (child.GetComponent<DestructionWorld>() == null) buildings++;
                        }
                        EditorSceneManager.MarkSceneDirty(cell);
                    }
                }
                finally
                {
                    if (cell.IsValid() && cell.isLoaded)
                    {
                        if (!EditorSceneManager.SaveScene(cell, definition.scenePath))
                            throw new InvalidOperationException("Could not save Rockport cell " + definition.scenePath + ".");
                        EditorSceneManager.CloseScene(cell, true);
                    }
                }
            }
            GameObject ocean = FindRoot(entry, OceanRootName);
            if (ocean != null) CollectRendererTexturePaths(ocean, textures);

            var report = new MigrationReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O"), entryScene = EntryScenePath,
                sourceBackup = Path.Combine(ReportFolder, "RockportMap.pre-streaming.unity"),
                cellCount = definitions.Length,
                movedBuildings = buildings, movedRoads = roads,
                movedTerrainTiles = terrainTiles, movedTreeTerrains = treeTerrains,
                movedBreakableTrees = breakableTrees,
                cellScenes = definitions.Select(definition => definition.scenePath).OrderBy(path => path).ToArray()
            };
            ConfigureRockportRoads(entry);
            EditorSceneManager.MarkSceneDirty(entry);
            if (!EditorSceneManager.SaveScene(entry, EntryScenePath))
                throw new InvalidOperationException("Could not save the Rockport entry scene.");
            UpdateGameFlowSettings();
            UpdateBuildScenes(report.cellScenes);
            ApplyMipStreaming(textures, report);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateBeforeLegacyRemoval(report.cellScenes);
            ValidateGeneratedState(false);
            PreserveWeatherCircuit();
            RemoveLegacyAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            report.retainedSceneCount = EditorBuildSettings.scenes.Count(scene => scene.enabled);
            report.legacyEntryRemoved = AssetDatabase.LoadMainAssetAtPath(LegacyEntryScenePath) == null;
            report.legacyCellsRemoved = !AssetDatabase.IsValidFolder(LegacyCellFolder);
            report.legacyModelsRemoved = !AssetDatabase.IsValidFolder(LegacyModelFolder);
            ValidateGeneratedState(true);
            WriteJson("migration-report.json", report);
            Debug.Log("ROCKPORT_STREAMING_BUILD_PASS resumed=true cells=" + report.cellCount
                + " mipTextures=" + report.rendererTextureCount);
        }

        private static void ValidateBeforeLegacyRemoval(IEnumerable<string> cellPaths)
        {
            var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectLegacyDependencies(EntryScenePath, dependencies);
            foreach (string path in cellPaths) CollectLegacyDependencies(path, dependencies);
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                if (!IsLegacyAsset(scene.path)) CollectLegacyDependencies(scene.path, dependencies);
            dependencies.RemoveWhere(path => IsUnder(path, WeatherCircuitSource));
            if (dependencies.Count > 0)
                throw new InvalidOperationException("Retained scenes still depend on legacy Rockport assets:\n"
                    + string.Join("\n", dependencies.OrderBy(path => path)));
        }

        private static void PreserveWeatherCircuit()
        {
            if (AssetDatabase.IsValidFolder(WeatherCircuitSource))
            {
                if (AssetDatabase.IsValidFolder(WeatherCircuitDestination))
                    throw new InvalidOperationException("Weather demo circuit destination already exists: " + WeatherCircuitDestination);
                string error = AssetDatabase.MoveAsset(WeatherCircuitSource, WeatherCircuitDestination);
                if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            }

            // The glTF importer caches resolved dependency paths in its metadata.
            // Refresh after the folder move so the retained weather demo no longer
            // names the deleted legacy map folder.
            const string retainedGltf = WeatherCircuitDestination + "/scene.gltf";
            if (AssetDatabase.LoadMainAssetAtPath(retainedGltf) != null)
                AssetDatabase.ImportAsset(retainedGltf, ImportAssetOptions.ForceUpdate);
        }

        private static void RemoveLegacyAssets()
        {
            foreach (string path in new[] { LegacyEntryScenePath, LegacyCellFolder, LegacyCityExample, LegacyModelFolder })
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) == null && !AssetDatabase.IsValidFolder(path)) continue;
                if (!AssetDatabase.DeleteAsset(path)) throw new InvalidOperationException("Could not remove legacy asset: " + path);
            }
        }

        private static void Inventory(string scenePath, string reportName, string passMarker)
        {
            RequireEditMode();
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            GameObject[] roots = scene.GetRootGameObjects();
            string[] dependencies = AssetDatabase.GetDependencies(scenePath, true);
            string[] legacy = dependencies.Where(IsLegacyAsset).OrderBy(path => path).ToArray();
            var report = new InventoryReport
            {
                scenePath = scenePath, generatedUtc = DateTime.UtcNow.ToString("O"),
                rootCount = roots.Length, dependencyCount = dependencies.Length,
                legacyDependencyCount = legacy.Length, legacyDependencies = legacy,
                roots = roots.Select(InventoryRoot).ToArray()
            };
            WriteJson(reportName, report);
            Debug.Log(passMarker + " roots=" + report.rootCount + " dependencies=" + report.dependencyCount
                + " legacyDependencies=" + report.legacyDependencyCount);
        }

        private static RootReport InventoryRoot(GameObject root)
        {
            Bounds bounds;
            bool hasBounds = TryGetBounds(root, out bounds);
            return new RootReport
            {
                name = root.name, active = root.activeSelf, directChildren = root.transform.childCount,
                transforms = root.GetComponentsInChildren<Transform>(true).Length,
                renderers = root.GetComponentsInChildren<Renderer>(true).Length,
                terrains = root.GetComponentsInChildren<Terrain>(true).Length,
                colliders = root.GetComponentsInChildren<Collider>(true).Length,
                monoBehaviours = root.GetComponentsInChildren<MonoBehaviour>(true).Length,
                missingScripts = CountMissingScripts(root.scene), hasBounds = hasBounds,
                boundsCenter = hasBounds ? bounds.center : Vector3.zero,
                boundsSize = hasBounds ? bounds.size : Vector3.zero
            };
        }

        private static Cell FindCell(List<Cell> cells, Vector3 point)
        {
            Cell contained = cells.FirstOrDefault(cell => point.x >= cell.bounds.min.x && point.x <= cell.bounds.max.x
                && point.z >= cell.bounds.min.z && point.z <= cell.bounds.max.z);
            if (contained != null) return contained;
            Cell nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (Cell cell in cells)
            {
                Vector3 closest = cell.bounds.ClosestPoint(point);
                float distance = new Vector2(closest.x - point.x, closest.z - point.z).sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = cell;
            }
            if (nearest == null) throw new InvalidOperationException("No Rockport cell is available for " + point + ".");
            return nearest;
        }

        private static void MoveToCell(GameObject gameObject, Cell cell)
        {
            gameObject.transform.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(gameObject, cell.scene);
            gameObject.transform.SetParent(cell.root.transform, true);
        }

        private static Bounds TerrainBounds(Terrain terrain)
        {
            Vector3 size = Vector3.Scale(terrain.terrainData.size, terrain.transform.lossyScale);
            return new Bounds(terrain.transform.position + size * 0.5f, size);
        }

        private static void CollectRendererTexturePaths(GameObject root, HashSet<string> paths)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    foreach (string property in material.GetTexturePropertyNames())
                    {
                        Texture texture = material.GetTexture(property);
                        string path = texture == null ? string.Empty : AssetDatabase.GetAssetPath(texture);
                        if (!string.IsNullOrEmpty(path)) paths.Add(path);
                    }
                }
        }

        private static void CollectLegacyDependencies(string assetPath, HashSet<string> output)
        {
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.LoadMainAssetAtPath(assetPath) == null) return;
            foreach (string dependency in AssetDatabase.GetDependencies(assetPath, true))
                if (IsLegacyAsset(dependency)) output.Add(dependency);
        }

        private static bool TryGetBounds(GameObject root, out Bounds combined)
        {
            bool found = false;
            combined = default;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!found) { combined = renderer.bounds; found = true; }
                else combined.Encapsulate(renderer.bounds);
            }
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (!found) { combined = collider.bounds; found = true; }
                else combined.Encapsulate(collider.bounds);
            }
            foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
            {
                Bounds bounds = TerrainBounds(terrain);
                if (!found) { combined = bounds; found = true; }
                else combined.Encapsulate(bounds);
            }
            return found;
        }

        private static int CountMissingScripts(Scene scene)
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                    count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
            return count;
        }

        private static T FindInScene<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).FirstOrDefault();

        private static GameObject FindRoot(Scene scene, string name) => scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == name);

        private static GameObject RequireRoot(Scene scene, string name) => FindRoot(scene, name)
            ?? throw new InvalidOperationException("Required Rockport root is missing: " + name);

        private static Transform[] DirectChildren(Transform parent) => Enumerable.Range(0, parent.childCount)
            .Select(parent.GetChild).ToArray();

        private static bool IsLegacyAsset(string path) => string.Equals(path, LegacyEntryScenePath, StringComparison.OrdinalIgnoreCase)
            || IsUnder(path, LegacyCellFolder) || IsUnder(path, LegacyModelFolder);

        private static bool IsUnder(string path, string folder) => string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);

        private static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before changing Rockport streaming.");
        }

        private static void EnsureAssetFolder(string path)
        {
            string[] segments = path.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }

        private static void WriteJson(string fileName, object report)
        {
            Directory.CreateDirectory(ReportFolder);
            File.WriteAllText(Path.Combine(ReportFolder, fileName), JsonUtility.ToJson(report, true) + Environment.NewLine);
        }

        private static RockportWorldChunkDefinition[] ToChunkDefinitions(this SerializedProperty property)
        {
            var definitions = new RockportWorldChunkDefinition[property.arraySize];
            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty item = property.GetArrayElementAtIndex(i);
                definitions[i] = new RockportWorldChunkDefinition
                {
                    sceneNumber = item.FindPropertyRelative("sceneNumber").intValue,
                    scenePath = item.FindPropertyRelative("scenePath").stringValue,
                    boundsCenter = item.FindPropertyRelative("boundsCenter").vector3Value,
                    boundsSize = item.FindPropertyRelative("boundsSize").vector3Value
                };
            }
            return definitions;
        }
    }
}
#endif
