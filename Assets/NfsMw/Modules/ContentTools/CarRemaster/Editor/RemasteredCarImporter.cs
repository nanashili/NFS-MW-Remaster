using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.CarRemaster.Editor
{
    public static class RemasteredCarImporter
    {
        [Serializable] private sealed class Entry
        {
            public string model, prefab;
            public int[] triangles;
            public int renderers, materials;
            public Vector3 size;
        }
        [Serializable] private sealed class Report
        {
            public string generatedUtc;
            public List<Entry> cars = new List<Entry>();
            public List<string> errors = new List<string>();
        }

        [MenuItem("Tools/NFS MW/Cars/Build Remastered Prefabs")]
        public static void Build()
        {
            var report = new Report { generatedUtc = DateTime.UtcNow.ToString("O") };
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var paths = Directory.GetFiles("Assets/NfsMw/Content/Vehicles", "Remastered.glb", SearchOption.AllDirectories).OrderBy(p => p).ToArray();
            if (paths.Length == 0) throw new InvalidOperationException("No remastered GLB models found.");
            foreach (var path in paths)
            {
                GameObject instance = null;
                try
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (model == null) throw new InvalidOperationException("GLB import did not produce a GameObject.");
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    instance.name = Path.GetFileName(Path.GetDirectoryName(path)) + " Remastered";
                    var lods = new LOD[3];
                    var counts = new int[3];
                    for (var i = 0; i < 3; i++)
                    {
                        var index = i;
                        var group = instance.GetComponentsInChildren<Transform>(true).Single(t => t.name == "LOD" + index || t.name.StartsWith("LOD" + index + ".", StringComparison.Ordinal));
                        group.gameObject.SetActive(true);
                        var renderers = group.GetComponentsInChildren<Renderer>(true);
                        if (renderers.Length == 0) throw new InvalidOperationException("Empty LOD " + i);
                        foreach (var renderer in renderers)
                        {
                            renderer.enabled = true;
                            if (renderer.sharedMaterials.Any(m => m == null || m.shader == null)) throw new InvalidOperationException("Missing material or shader: " + renderer.name);
                        }
                        counts[i] = group.GetComponentsInChildren<MeshFilter>(true).Sum(f => f.sharedMesh.triangles.Length / 3);
                        lods[i] = new LOD(new[] { .45f, .16f, .035f }[i], renderers);
                    }
                    if (!(counts[0] > counts[1] && counts[1] > counts[2])) throw new InvalidOperationException("LOD triangle counts do not decrease.");
                    var lodGroup = instance.AddComponent<LODGroup>();
                    lodGroup.SetLODs(lods);
                    lodGroup.RecalculateBounds();
                    var bounds = lods[0].renderers[0].bounds;
                    foreach (var renderer in lods[0].renderers) bounds.Encapsulate(renderer.bounds);
                    if (!float.IsFinite(bounds.size.x) || bounds.size.y < .3f || bounds.size.z < 1f ||
                        bounds.size.x > 4.5f || bounds.size.y > 5f || bounds.size.z > 15f)
                        throw new InvalidOperationException("Invalid vehicle bounds: " + bounds);
                    var prefabPath = Path.ChangeExtension(path, ".prefab");
                    PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                    report.cars.Add(new Entry { model = path, prefab = prefabPath, triangles = counts, size = bounds.size,
                        renderers = lods[0].renderers.Length, materials = lods[0].renderers.SelectMany(r => r.sharedMaterials).Distinct().Count() });
                }
                catch (Exception error) { report.errors.Add(path + ": " + error.Message); }
                finally { if (instance != null) UnityEngine.Object.DestroyImmediate(instance); }
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Art/Cars");
            File.WriteAllText("Art/Cars/unity-import-report.json", JsonUtility.ToJson(report, true));
            Debug.Log("Remastered cars: " + report.cars.Count + " prefabs validated; " + report.errors.Count + " failures.");
            if (report.errors.Count > 0) throw new InvalidOperationException(string.Join("\n", report.errors));
        }

        public static void BuildBatch()
        {
            Build();
            if (!File.Exists("Assets/NfsMw/Scenes/Showcase/RemasteredCarShowcase.unity")) CreateShowcase();
        }

        public static void AwaitExportsBatch()
        {
            var deadline = EditorApplication.timeSinceStartup + 900;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (!File.Exists("Art/Cars/exports-ready.json") && EditorApplication.timeSinceStartup < deadline) return;
                EditorApplication.update -= tick;
                try
                {
                    if (!File.Exists("Art/Cars/exports-ready.json")) throw new TimeoutException("Car exports did not finish.");
                    BuildBatch();
                    EditorApplication.Exit(0);
                }
                catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
            };
            EditorApplication.update += tick;
        }

        [MenuItem("Tools/NFS MW/Cars/Create Remastered Showcase")]
        public static void CreateShowcase()
        {
            const string path = "Assets/NfsMw/Scenes/Showcase/RemasteredCarShowcase.unity";
            if (File.Exists(path)) throw new InvalidOperationException("Showcase already exists; open the saved scene.");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var paths = Directory.GetFiles("Assets/NfsMw/Content/Vehicles", "Remastered.prefab", SearchOption.AllDirectories).OrderBy(p => p).ToArray();
                for (var i = 0; i < paths.Length; i++)
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                    instance.transform.position = new Vector3(i % 10 * 5, 0, i / 10 * 13);
                }
                var light = new GameObject("Showcase Sun").AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 3;
                light.transform.rotation = Quaternion.Euler(45, -35, 0);
                var camera = new GameObject("Showcase Camera").AddComponent<Camera>();
                camera.transform.position = new Vector3(25, 65, -45);
                camera.transform.LookAt(new Vector3(25, 0, 38));
                camera.farClipPlane = 300;
                EditorSceneManager.SaveScene(scene, path);
            }
            finally
            {
                if (!Application.isBatchMode)
                {
                    if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
