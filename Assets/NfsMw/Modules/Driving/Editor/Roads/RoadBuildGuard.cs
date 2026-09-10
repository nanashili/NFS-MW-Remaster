using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    [InitializeOnLoad]
    public sealed class RoadBuildGuard : IProcessSceneWithReport
    {
        public int callbackOrder => 100;
        static RoadBuildGuard()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.ExitingEditMode || Application.isBatchMode) return;
                try { for (int i = 0; i < SceneManager.sceneCount; i++) ValidateScene(SceneManager.GetSceneAt(i)); }
                catch (BuildFailedException exception) { EditorApplication.isPlaying = false; Debug.LogError(exception.Message); }
            };
        }
        public static void ValidateScene(Scene scene)
        {
            if (!scene.isLoaded) return;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var road in root.GetComponentsInChildren<RoadAuthoring>(true))
                {
                    var owner = road.GetComponentInParent<RoadNetworkAuthoring>();
                    if (owner == null || !owner.Roads.Contains(road)) throw new BuildFailedException("ROAD_UNPUBLISHED: assign " + road.name + " to a network and bake it.");
                }
                foreach (var source in root.GetComponentsInChildren<RoadNetworkAuthoring>(true))
                {
                    if (!RoadNetworkBake.IsCurrent(source)) throw new BuildFailedException("ROAD_STALE: rebake " + source.name + " in " + scene.name + ".");
                    var service = source.GetComponent<RoadNetwork>();
                    if (service == null || !service.UsesBakedData || service.Publication != source.Baked)
                        throw new BuildFailedException("ROAD_AUTHORITY: the runtime network must use the same publication as generated geometry.");
                    var generated = source.GetComponentsInChildren<RoadGeneratedNetwork>(true)
                        .Where(value => value.GetComponentInParent<RoadNetworkAuthoring>() == source).ToArray();
                    if (generated.Length != 1 || generated[0].Asset != source.Baked || !generated[0].gameObject.activeSelf)
                        throw new BuildFailedException("ROAD_GEOMETRY: published geometry is missing, duplicated, outdated or disabled in " + source.name + ".");
                    var chunks = generated[0].GetComponentsInChildren<RoadGeneratedChunk>(true);
                    if (chunks.Length != source.Baked.Chunks.Count) throw new BuildFailedException("ROAD_GEOMETRY: generated chunks have been removed; rebake the network.");
                    var seen = new bool[chunks.Length];
                    for (int i = 0; i < chunks.Length; i++)
                    {
                        var chunk = chunks[i];
                        if (chunk.Asset != source.Baked || chunk.Index < 0 || chunk.Index >= source.Baked.Chunks.Count) throw new BuildFailedException("ROAD_GEOMETRY: invalid generated ownership.");
                        if (seen[chunk.Index]) throw new BuildFailedException("ROAD_GEOMETRY: duplicate generated chunk index.");
                        seen[chunk.Index] = true;
                        var expected = source.Baked.Chunks[chunk.Index]; var filter = chunk.GetComponent<MeshFilter>(); var collider = chunk.GetComponent<MeshCollider>();
                        var renderer = chunk.GetComponent<MeshRenderer>(); var surface = chunk.GetComponent<VehicleSurface>();
                        if (chunk.transform.parent != generated[0].transform || !chunk.gameObject.activeSelf
                            || (chunk.transform.position - expected.Origin).sqrMagnitude > 0.00000001f
                            || Quaternion.Angle(chunk.transform.rotation, Quaternion.identity) > 0.001f
                            || (chunk.transform.lossyScale - Vector3.one).sqrMagnitude > 0.00000001f)
                            throw new BuildFailedException("ROAD_GEOMETRY: generated chunk placement has changed; rebake the network.");
                        if (expected.Mesh == null || filter == null || filter.sharedMesh != expected.Mesh || renderer == null || !renderer.enabled
                            || renderer.sharedMaterials.Length != 1 || renderer.sharedMaterial != expected.Material
                            || expected.Collision && (collider == null || !collider.enabled || collider.convex || collider.isTrigger
                                || collider.sharedMesh != expected.Mesh || surface == null || surface.Profile != expected.Surface)
                            || !expected.Collision && collider != null)
                            throw new BuildFailedException("ROAD_GEOMETRY: generated meshes or collision have been edited; rebake the network.");
                    }
                }
            }
        }
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return;
            ValidateScene(scene);
            // Unity invokes this on the build's scene copy. Authored scene assets are never changed here.
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var road in root.GetComponentsInChildren<RoadAuthoring>(true))
                { var spline = road.Reference; Object.DestroyImmediate(road); Object.DestroyImmediate(spline); }
                foreach (var source in root.GetComponentsInChildren<RoadNetworkAuthoring>(true)) Object.DestroyImmediate(source);
            }
        }
    }
}
