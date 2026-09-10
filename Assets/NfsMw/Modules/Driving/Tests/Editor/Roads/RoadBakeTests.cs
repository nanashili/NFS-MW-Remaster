using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using NfsMwRemaster.Driving.Editor;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class RoadBakeTests
    {
        [Test]
        public void RebakeRetainsLaneIdentityAndUndoRestoresTheEntirePublication()
        {
            string folder = "Assets/RoadRevisionTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            RoadNetworkAuthoring source = null;
            try
            {
                var profile = RoadProfile.CreateTwoLane();
                var material = new Material(Shader.Find("HDRP/Lit"));
                var surface = ScriptableObject.CreateInstance<SensorySurfaceProfile>();
                AssetDatabase.CreateAsset(material, folder + "/Road.mat");
                AssetDatabase.CreateAsset(surface, folder + "/Surface.asset");
                foreach (var band in profile.bands) { band.material = material; band.surface = surface; }
                AssetDatabase.CreateAsset(profile, folder + "/Profile.asset");
                var road = RoadAuthoringCommands.Create(profile, new[] { Vector3.zero, Vector3.forward * 100 });
                source = RoadAuthoringCommands.CreateNetwork(new[] { road });
                var first = RoadNetworkBake.Publish(source, folder + "/Bake.asset");
                var ids = first.Lanes.ToDictionary(lane => lane.Id, lane => lane.CompatibilityId);
                road.Bands[0].overrideWidth = true;
                // A key near the end must remain valid geometry in every consumer, including the traffic adapter.
                road.Bands[0].width = new AnimationCurve(new Keyframe(0, 3.5f), new Keyframe(99.999f, 3.5f), new Keyframe(100, 3.5f));
                road.bankRadians = AnimationCurve.Linear(0, 0, 100, 0.2f);
                var second = RoadNetworkBake.Publish(source, folder + "/Bake.asset");
                Assert.That(second, Is.Not.SameAs(first));
                foreach (var lane in second.Lanes) Assert.That(lane.CompatibilityId, Is.EqualTo(ids[lane.Id]));
                foreach (var lane in second.Lanes) foreach (var sample in lane.Samples)
                {
                    Assert.That(Mathf.Abs(Vector3.Dot(sample.forward, sample.up)), Is.LessThan(0.00001f), "Lane frames must follow the changing banked lane surface.");
                    Assert.That(Mathf.Abs(Vector3.Dot(sample.forward, sample.left)), Is.LessThan(0.00001f));
                }
                Assert.That(AssetDatabase.Contains(first), Is.True);
                Assert.That(source.GetComponent<RoadNetwork>().Publication, Is.SameAs(second));
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Assert.That(source.Baked, Is.SameAs(first));
                Assert.That(source.GetComponent<RoadNetwork>().Publication, Is.SameAs(first));
                Assert.That(source.GetComponentsInChildren<RoadGeneratedNetwork>().Single().Asset, Is.SameAs(first));
                Undo.PerformRedo();
                Assert.That(source.Baked, Is.SameAs(second));
                Assert.That(source.GetComponent<RoadNetwork>().Publication, Is.SameAs(second));
                road.Bands[0].width = AnimationCurve.Constant(0, 100, -1);
                Assert.Throws<ArgumentException>(() => RoadNetworkBake.Publish(source, folder + "/Rejected.asset"));
                Assert.That(source.Baked, Is.SameAs(second));
                Assert.That(source.GetComponentsInChildren<RoadGeneratedNetwork>().Single().Asset, Is.SameAs(second));
                Assert.That(AssetDatabase.LoadAssetAtPath<RoadNetworkAsset>(folder + "/Rejected.asset"), Is.Null);
            }
            finally
            {
                if (source != null) UnityEngine.Object.DestroyImmediate(source.gameObject);
                AssetDatabase.DeleteAsset(folder); Undo.ClearAll();
            }
        }

        [Test]
        public void PublishedRoadSurvivesSceneReloadWithTheSameIdentityAndCollision()
        {
            if (!Application.isBatchMode) Assert.Ignore("Scene reload validation runs in an isolated batch project so it cannot discard an open authoring scene.");
            string folder = "Assets/RoadBakeTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                var profile = RoadProfile.CreateTwoLane();
                var material = new Material(Shader.Find("HDRP/Lit"));
                var surface = ScriptableObject.CreateInstance<SensorySurfaceProfile>();
                AssetDatabase.CreateAsset(material, folder + "/Road.mat");
                AssetDatabase.CreateAsset(surface, folder + "/Surface.asset");
                foreach (var band in profile.bands) { band.material = material; band.surface = surface; }
                AssetDatabase.CreateAsset(profile, folder + "/Profile.asset");
                var road = RoadAuthoringCommands.Create(profile, new[] { Vector3.zero, Vector3.forward * 100 });
                SceneManager.MoveGameObjectToScene(road.gameObject, scene);
                var source = RoadAuthoringCommands.CreateNetwork(new[] { road });
                var asset = RoadNetworkBake.Publish(source, folder + "/Bake.asset");
                var id = road.Id; var laneId = road.Bands[0].id;
                Assert.That(asset.Lanes.Count, Is.EqualTo(2));
                Assert.That(asset.Lanes.Any(lane => lane.Id == laneId), Is.True);
                Assert.That(source.GetComponentsInChildren<MeshCollider>().Length, Is.EqualTo(2));
                Assert.That(RoadNetworkBake.IsCurrent(source), Is.True);
                Assert.That(EditorSceneManager.SaveScene(scene, folder + "/Road.unity"), Is.True);
                scene = EditorSceneManager.OpenScene(folder + "/Road.unity", OpenSceneMode.Single);
                source = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RoadNetworkAuthoring>()).Single();
                Assert.That(source.Roads[0].Id, Is.EqualTo(id));
                Assert.That(source.Roads[0].Bands[0].id, Is.EqualTo(laneId));
                Assert.That(RoadNetworkBake.IsCurrent(source), Is.True);
                Assert.That(source.GetComponentsInChildren<MeshCollider>().All(collider => collider.sharedMesh != null), Is.True);
                Assert.DoesNotThrow(() => RoadBuildGuard.ValidateScene(scene));
                var chunk = source.GetComponentInChildren<RoadGeneratedChunk>();
                chunk.transform.position += Vector3.right;
                Assert.Throws<UnityEditor.Build.BuildFailedException>(() => RoadBuildGuard.ValidateScene(scene), "Moving generated collision must invalidate a build.");
                chunk.transform.position -= Vector3.right;
                var collider = chunk.GetComponent<MeshCollider>();
                collider.enabled = false;
                Assert.Throws<UnityEditor.Build.BuildFailedException>(() => RoadBuildGuard.ValidateScene(scene), "Disabled collision cannot replace the published driving surface.");
                collider.enabled = true;
                chunk.GetComponent<VehicleSurface>().SetProfile(null);
                Assert.Throws<UnityEditor.Build.BuildFailedException>(() => RoadBuildGuard.ValidateScene(scene), "Collision must retain its published sensory surface.");
                chunk.GetComponent<VehicleSurface>().SetProfile(source.Baked.Chunks[chunk.Index].Surface);
                Assert.DoesNotThrow(() => RoadBuildGuard.ValidateScene(scene));
                source.Roads[0].transform.position += Vector3.up;
                Assert.That(RoadNetworkBake.IsCurrent(source), Is.False);
                Assert.Throws<UnityEditor.Build.BuildFailedException>(() => RoadBuildGuard.ValidateScene(scene));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(folder); Undo.ClearAll();
            }
        }
    }
}
