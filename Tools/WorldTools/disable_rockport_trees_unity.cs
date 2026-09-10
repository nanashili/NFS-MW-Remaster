using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using NfsMwRemaster.Driving;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before editing tree placements.");
        const string folder = "Assets/NfsMw/Scenes/World/Streaming";
        const string reportFolder = "Art/RockportTrees/Disabled-20260910";
        var paths = Directory.GetFiles(folder, "*.unity").OrderBy(path => path, StringComparer.Ordinal).ToList();
        paths.Insert(0, "Assets/NfsMw/Scenes/World/RockportMap.unity");
        if (paths.Count != 40) throw new InvalidOperationException("Expected the entry scene and 39 streaming cells.");
        // Loaded unsaved scenes must remain untouched; other cells can still be opened additively.
        foreach (string path in paths)
        {
            Scene existing = SceneManager.GetSceneByPath(path);
            if (existing.IsValid() && existing.isLoaded && existing.isDirty)
                throw new InvalidOperationException("Unsaved scene: " + path);
        }
        Directory.CreateDirectory(reportFolder);
        Directory.CreateDirectory(reportFolder + "/SceneBackups");
        int terrains = 0, supports = 0, terrainColliders = 0, placements = 0, physicalTrees = 0, changedScenes = 0;
        int activeTreeRenderers = 0, activeTreeColliders = 0;
        foreach (string path in paths)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            bool changed = false;
            try
            {
                string backup = reportFolder + "/SceneBackups/" + Path.GetFileName(path);
                if (!File.Exists(backup)) File.Copy(path, backup, false);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
                    {
                        if (!terrain.terrainData) continue;
                        terrains++;
                        placements += terrain.terrainData.treeInstanceCount;
                        if (terrain.drawTreesAndFoliage)
                        {
                            result.RegisterObjectModification(terrain);
                            terrain.drawTreesAndFoliage = false;
                            changed = true;
                        }
                        // These terrains only support tree placements over terrain holes.
                        if (!terrain.drawHeightmap && terrain.name.StartsWith("TreesOverOpenings_", StringComparison.Ordinal))
                        {
                            supports++;
                            if (terrain.gameObject.activeSelf)
                            {
                                result.RegisterObjectModification(terrain.gameObject);
                                terrain.gameObject.SetActive(false);
                                changed = true;
                            }
                        }
                        if (terrain.TryGetComponent<TerrainCollider>(out var collider))
                        {
                            terrainColliders++;
                            var serialized = new SerializedObject(collider);
                            var treeCollisions = serialized.FindProperty("m_EnableTreeColliders");
                            if (treeCollisions == null) throw new InvalidOperationException("TerrainCollider tree collision setting is missing.");
                            if (treeCollisions.boolValue)
                            {
                                result.RegisterObjectModification(collider);
                                treeCollisions.boolValue = false;
                                serialized.ApplyModifiedProperties();
                                changed = true;
                            }
                        }
                    }
                    foreach (DestructibleProp tree in root.GetComponentsInChildren<DestructibleProp>(true))
                    {
                        if (tree.StableId == null || !tree.StableId.StartsWith("rockport.tree.", StringComparison.Ordinal)) continue;
                        physicalTrees++;
                        if (tree.gameObject.activeSelf)
                        {
                            result.RegisterObjectModification(tree.gameObject);
                            tree.gameObject.SetActive(false);
                            changed = true;
                        }
                        foreach (Renderer renderer in tree.GetComponentsInChildren<Renderer>(true))
                            if (renderer.enabled && renderer.gameObject.activeInHierarchy) activeTreeRenderers++;
                        foreach (Collider collider in tree.GetComponentsInChildren<Collider>(true))
                            if (collider.enabled && collider.gameObject.activeInHierarchy) activeTreeColliders++;
                    }
                }
                if (changed)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + path);
                    changedScenes++;
                }
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }
        if (terrains != 70 || supports != 31 || terrainColliders != 39 || placements != 8946 || physicalTrees != 481
            || activeTreeRenderers != 0 || activeTreeColliders != 0)
            throw new InvalidOperationException("Tree inventory or disabled-state verification mismatch.");
        string json = "{\"status\":\"PASS\",\"scenesChecked\":" + paths.Count + ",\"scenesChanged\":" + changedScenes
            + ",\"terrainPlacementsRetainedButHidden\":" + placements + ",\"terrainTreeDrawingDisabled\":" + terrains
            + ",\"treeSupportObjectsInactive\":" + supports + ",\"terrainTreeCollisionsDisabled\":" + terrainColliders
            + ",\"physicalTreesInactive\":" + physicalTrees + ",\"activePhysicalTreeRenderers\":" + activeTreeRenderers
            + ",\"activePhysicalTreeColliders\":" + activeTreeColliders + ",\"sourceAssetsPreserved\":true}";
        File.WriteAllText(reportFolder + "/removal-report.json", json + "\n");
        result.Log(json);
    }
}
