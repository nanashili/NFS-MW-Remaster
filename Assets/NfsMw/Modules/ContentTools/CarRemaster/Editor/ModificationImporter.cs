using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NfsMwRemaster.CarRemaster.Editor
{
    [Serializable] public sealed class ModificationLod { public string node, sourceSolid; public int triangles; }
    [Serializable] public sealed class ModificationEntry
    {
        public string id, node, category, variant, archive, kit, compatibility, prefab;
        public string[] sourceSolids;
        public ModificationLod[] lods;
        public string PrefabPath => string.IsNullOrWhiteSpace(prefab)
            ? ModificationImporter.PartsRoot + "/" + ModificationImporter.FolderForCategory(category) + "/" + archive + "/" + id + ".prefab"
            : prefab;
    }
    [Serializable] public sealed class ModificationCatalog
    {
        public string archive, asset, sourceSha256, sourceCatalog, layout;
        public int sourceSolidCount;
        public ModificationEntry[] entries;
    }
    public static class ModificationImporter
    {
        public const string CustomizationRoot = "Assets/NfsMw/Content/Customization";
        public const string SourcesRoot = CustomizationRoot + "/Sources";
        public const string PartsRoot = CustomizationRoot + "/Parts";
        static readonly Dictionary<string, string> CategoryFolders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Brake"] = "Brake", ["Body kit"] = "BodyKit", ["Damage state"] = "DamageState",
            ["Decal mesh"] = "DecalMesh", ["Glass"] = "Glass", ["Hood"] = "Hood",
            ["Interior"] = "Interior", ["Light"] = "Light", ["Mirror"] = "Mirror",
            ["Other source part"] = "OtherSourcePart", ["Plate"] = "Plate", ["Rim"] = "Rim",
            ["Roof scoop"] = "RoofScoop", ["Spoiler"] = "Spoiler", ["Tyre"] = "Tyre",
            ["Wheel and tyre"] = "WheelAndTyre"
        };
        public static string FolderForCategory(string category)
        {
            if (CategoryFolders.TryGetValue(category ?? string.Empty, out var folder)) return folder;
            throw new InvalidDataException("Unknown modification category: " + category);
        }
        [Serializable] sealed class Validated
        {
            public string id, archive, prefab, category;
            public int[] triangles;
            public Vector3 size;
        }
        [Serializable] sealed class Report
        {
            public string generatedUtc;
            public List<Validated> parts = new List<Validated>();
            public List<string> errors = new List<string>();
        }
        public static IEnumerable<ModificationCatalog> Catalogs() => Directory.Exists(SourcesRoot)
            ? Directory.GetFiles(SourcesRoot, "catalog.json", SearchOption.AllDirectories).OrderBy(p => p)
                .Select(p => JsonUtility.FromJson<ModificationCatalog>(File.ReadAllText(p)))
            : Enumerable.Empty<ModificationCatalog>();

        public static void BuildBatch()
        {
            Build();
            VinylLibraryWindow.ValidateSamples();
        }

        [MenuItem("Tools/NFS MW/Modifications/Build and Validate Prefabs")]
        public static void Build()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var report = new Report { generatedUtc = DateTime.UtcNow.ToString("O") };
            BuildCatalogs(report, Catalogs());
        }

        public static void RepairFailedBatch()
        {
            var report = JsonUtility.FromJson<Report>(File.ReadAllText("Art/Cars/Modifications/unity-import-report.json"));
            var valid = new HashSet<string>(report.parts.Select(p => p.archive + "/" + p.id));
            var catalogs = Catalogs().Where(c => c.entries.Any(e => !valid.Contains(c.archive + "/" + e.id))).ToArray();
            var archives = new HashSet<string>(catalogs.Select(c => c.archive));
            report.parts.RemoveAll(p => archives.Contains(p.archive));
            report.errors.Clear();
            report.generatedUtc = DateTime.UtcNow.ToString("O");
            foreach (var catalog in catalogs)
                AssetDatabase.ImportAsset(catalog.asset, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            BuildCatalogs(report, catalogs);
            VinylLibraryWindow.ValidateSamples();
        }

        static void BuildCatalogs(Report report, IEnumerable<ModificationCatalog> catalogs)
        {
            foreach (var catalog in catalogs)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(catalog.asset);
                if (model == null) { report.errors.Add(catalog.archive + ": missing imported GLB"); continue; }
                var transforms = model.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
                if (!catalog.asset.StartsWith(SourcesRoot + "/", StringComparison.Ordinal) || catalog.asset.Contains(".."))
                    throw new InvalidDataException(catalog.archive + ": catalog points outside the customization source root");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.StartAssetEditing();
                try
                {
                foreach (var entry in catalog.entries)
                {
                    GameObject instance = null;
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(entry.PrefabPath));
                        if (!transforms.TryGetValue(entry.node, out var source))
                        {
                            // glTFast can promote a lone root to the imported main object;
                            // Unity then names that object after the asset file.
                            if (catalog.entries.Length == 1 && entry.lods.All(l => transforms.ContainsKey(l.node))) source = model.transform;
                            else throw new InvalidOperationException("Missing part node " + entry.node);
                        }
                        instance = UnityEngine.Object.Instantiate(source.gameObject);
                        instance.name = entry.id;
                        instance.SetActive(true);
                        var lods = new LOD[3];
                        var counts = new int[3];
                        for (int i = 0; i < 3; i++)
                        {
                            var nodeName = entry.lods[i].node;
                            var group = instance.GetComponentsInChildren<Transform>(true).Single(t => t.name == nodeName);
                            group.gameObject.SetActive(true);
                            var renderers = group.GetComponentsInChildren<Renderer>(true);
                            if (renderers.Length == 0) throw new InvalidOperationException("Empty LOD " + i);
                            foreach (var renderer in renderers)
                            {
                                renderer.enabled = true;
                                if (renderer.sharedMaterials.Any(m => m == null || m.shader == null)) throw new InvalidOperationException("Missing material or shader");
                            }
                            counts[i] = group.GetComponentsInChildren<MeshFilter>(true).Sum(f => f.sharedMesh.triangles.Length / 3);
                            if (counts[i] != entry.lods[i].triangles) throw new InvalidOperationException("Triangle mismatch in LOD " + i);
                            lods[i] = new LOD(new[] { .35f, .12f, .025f }[i], renderers);
                        }
                        var lodGroup = instance.AddComponent<LODGroup>();
                        lodGroup.SetLODs(lods); lodGroup.RecalculateBounds();
                        var bounds = lods[0].renderers[0].bounds;
                        foreach (var renderer in lods[0].renderers) bounds.Encapsulate(renderer.bounds);
                        var size = bounds.size;
                        if (!float.IsFinite(size.x) || !float.IsFinite(size.y) || !float.IsFinite(size.z) || size.sqrMagnitude == 0 || size.magnitude > 60)
                            throw new InvalidOperationException("Invalid part bounds " + bounds);
                        PrefabUtility.SaveAsPrefabAsset(instance, entry.PrefabPath, out var success);
                        if (!success) throw new InvalidOperationException("Prefab save failed");
                        report.parts.Add(new Validated { id = entry.id, archive = catalog.archive, category = entry.category,
                            prefab = entry.PrefabPath, triangles = counts, size = size });
                    }
                    catch (Exception error) { report.errors.Add(catalog.archive + "/" + entry.id + ": " + error.Message); }
                    finally { if (instance != null) UnityEngine.Object.DestroyImmediate(instance); }
                }
                }
                finally { AssetDatabase.StopAssetEditing(); }
                foreach (var entry in catalog.entries)
                {
                    var saved = AssetDatabase.LoadAssetAtPath<GameObject>(entry.PrefabPath);
                    var savedLods = saved != null ? saved.GetComponent<LODGroup>() : null;
                    if (savedLods == null || savedLods.lodCount != 3 || savedLods.GetLODs().Any(l => l.renderers.Length == 0 || l.renderers.Any(r => r == null)))
                        report.errors.Add(catalog.archive + "/" + entry.id + ": saved prefab did not retain its LOD renderers");
                }
                Debug.Log("Modification library imported: " + catalog.archive + " (" + catalog.entries.Length + " parts)");
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Art/Cars/Modifications/unity-import-report.json", JsonUtility.ToJson(report, true));
            Debug.Log("Modifications: " + report.parts.Count + " prefabs validated; " + report.errors.Count + " errors.");
            if (report.parts.Count == 0 || report.errors.Count > 0) throw new InvalidOperationException(string.Join("\n", report.errors));
        }
    }

    public sealed class ModificationLibraryWindow : EditorWindow
    {
        ModificationEntry[] entries = Array.Empty<ModificationEntry>();
        string search = "", category = "All";
        string[] categories = new[] { "All" };
        Vector2 scroll;
        [MenuItem("Tools/NFS MW/Modifications/Browse Parts")]
        public static void Open() => GetWindow<ModificationLibraryWindow>("MW Modifications");
        void OnEnable() => Reload();
        void Reload()
        {
            entries = ModificationImporter.Catalogs().SelectMany(c => c.entries).ToArray();
            categories = new[] { "All" }.Concat(entries.Select(e => e.category).Distinct().OrderBy(c => c)).ToArray();
            if (!categories.Contains(category)) category = "All";
        }
        void OnGUI()
        {
            EditorGUILayout.LabelField("Most Wanted modification library", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Car parts retain their source car origin. Shared parts need mount and size fitting. These are visual assets; installing one does not alter vehicle handling.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                search = EditorGUILayout.TextField("Search car or part", search);
                if (GUILayout.Button("Refresh", GUILayout.Width(70))) Reload();
            }
            category = categories[EditorGUILayout.Popup("Category", Array.IndexOf(categories, category), categories)];
            var matches = entries.Where(e => (category == "All" || e.category == category) &&
                (e.archive + " " + e.id + " " + e.category).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            EditorGUILayout.LabelField(matches.Length + " matches — showing first 100. Narrow the search to see more.");
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var entry in matches.Take(100))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(entry.id, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(entry.category + " · " + entry.compatibility, EditorStyles.wordWrappedLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Select prefab"))
                        {
                            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(entry.PrefabPath);
                            EditorGUIUtility.PingObject(Selection.activeObject);
                        }
                        if (GUILayout.Button("Add to scene"))
                        {
                            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.PrefabPath);
                            if (prefab != null)
                            {
                                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                                Undo.RegisterCreatedObjectUndo(instance, "Add modification part");
                                Selection.activeGameObject = instance;
                                SceneView.lastActiveSceneView?.FrameSelected();
                            }
                        }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
