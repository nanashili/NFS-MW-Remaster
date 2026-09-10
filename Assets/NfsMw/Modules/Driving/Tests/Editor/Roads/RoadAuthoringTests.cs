using NUnit.Framework;
using UnityEngine;
using NfsMwRemaster.Driving.Editor;
using UnityEditor;
using UnityEngine.Splines;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class RoadAuthoringTests
    {
        [Test]
        public void ConnectMatchingEndsCreatesOnlyDirectedContinuationsOnTheSameLevel()
        {
            var profile = RoadProfile.CreateTwoLane(); RoadNetworkAuthoring network = null;
            try
            {
                var first = RoadAuthoringCommands.Create(profile, new[] { Vector3.zero, Vector3.forward * 100 });
                var second = RoadAuthoringCommands.Create(profile, new[] { Vector3.forward * 100, Vector3.forward * 200 });
                var bridge = RoadAuthoringCommands.Create(profile, new[] { new Vector3(0, 10, 100), new Vector3(0, 10, 200) });
                network = RoadAuthoringCommands.CreateNetwork(new[] { first, second, bridge });
                Assert.That(RoadAuthoringCommands.ConnectMatchingEnds(network), Is.EqualTo(2));
                Assert.That(network.connections, Has.Exactly(1).Matches<RoadLaneConnection>(link => link.from == first.Bands[1].id && link.to == second.Bands[1].id));
                Assert.That(network.connections, Has.Exactly(1).Matches<RoadLaneConnection>(link => link.from == second.Bands[0].id && link.to == first.Bands[0].id));
                Assert.That(RoadAuthoringCommands.ConnectMatchingEnds(network), Is.Zero);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Assert.That(network.connections, Is.Empty);
            }
            finally
            {
                if (network != null) Object.DestroyImmediate(network.gameObject);
                Object.DestroyImmediate(profile); Undo.ClearAll();
            }
        }

        [Test]
        public void AReferenceCuspIsRejectedBeforeItCanFoldTheRoadSurface()
        {
            var profile = RoadProfile.CreateTwoLane(); RoadAuthoring road = null;
            try
            {
                road = RoadAuthoringCommands.Create(profile, new[] { Vector3.zero, Vector3.right * 50 });
                road.Reference.Spline = new Spline(new[]
                {
                    new BezierKnot(Vector3.zero, Vector3.zero, Vector3.right * 100),
                    new BezierKnot(Vector3.right * 50, Vector3.zero, Vector3.zero)
                });
                var error = Assert.Throws<System.ArgumentException>(() => RoadGeometry.Evaluate(road));
                StringAssert.Contains("ROAD_CUSP", error.Message);
            }
            finally { if (road != null) Object.DestroyImmediate(road.gameObject); Object.DestroyImmediate(profile); }
        }

        [Test]
        public void BankedChunksShareTheSameSurfaceNormalsAtTheirBoundary()
        {
            var profile = RoadProfile.CreateTwoLane(); RoadAuthoring road = null;
            try
            {
                road = RoadAuthoringCommands.Create(profile, new[] { Vector3.zero, Vector3.forward * 200 });
                road.bankRadians = AnimationCurve.Linear(0, 0, 200, 0.4f);
                using (var build = RoadMeshBuilder.Build(road))
                {
                    var first = build.Chunks[0].Mesh.normals; var second = build.Chunks[2].Mesh.normals;
                    Assert.That(Vector3.Distance(first[first.Length - 2], second[0]), Is.LessThan(0.00001f));
                    Assert.That(Vector3.Distance(first[first.Length - 1], second[1]), Is.LessThan(0.00001f));
                }
            }
            finally { if (road != null) Object.DestroyImmediate(road.gameObject); Object.DestroyImmediate(profile); }
        }

        [Test]
        public void InsertingAKnotPreservesCurveIdentityAndCanBeUndone()
        {
            var profile = RoadProfile.CreateTwoLane();
            RoadAuthoring road = null;
            try
            {
                road = RoadAuthoringCommands.Create(profile, new[] { Vector3.zero, Vector3.forward * 100 });
                road.Reference.Spline = new Spline(new[]
                {
                    new BezierKnot(Vector3.zero, Vector3.zero, Vector3.right * 40),
                    new BezierKnot(Vector3.forward * 100, Vector3.right * 40, Vector3.zero)
                });
                var before = RoadGeometry.Evaluate(road);
                var id = road.Id; var laneId = road.Bands[0].id;
                RoadAuthoringCommands.InsertKnot(road, 0, 0.5f);
                var after = RoadGeometry.Evaluate(road);
                Assert.That(road.Reference.Spline.Count, Is.EqualTo(3));
                Assert.That(after.Length, Is.EqualTo(before.Length).Within(0.02));
                for (float s = 0; s < before.Length; s += 2)
                    Assert.That(Vector3.Distance(before.Sample(s).Position, after.Sample(s).Position), Is.LessThan(0.02));
                Assert.That(road.Id, Is.EqualTo(id)); Assert.That(road.Bands[0].id, Is.EqualTo(laneId));
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Assert.That(road.Reference.Spline.Count, Is.EqualTo(2));
                Assert.That(road.Id, Is.EqualTo(id));
            }
            finally
            {
                if (road != null) Object.DestroyImmediate(road.gameObject);
                Object.DestroyImmediate(profile); Undo.ClearAll();
            }
        }

        [Test]
        public void FlatRoadBuildsSevenMetreSurfaceWithUpwardFacesAndContinuousChunks()
        {
            var profile = RoadProfile.CreateTwoLane();
            RoadAuthoring road = null;
            try
            {
                road = RoadAuthoringCommands.Create(profile, new[] { Vector3.zero, Vector3.forward * 200 });
                using (var build = RoadMeshBuilder.Build(road))
                {
                    Assert.That(build.Chunks.Count, Is.EqualTo(4));
                    foreach (var chunk in build.Chunks)
                    {
                        Assert.That(chunk.Mesh.bounds.size.x, Is.EqualTo(3.5f).Within(0.001));
                        Assert.That(chunk.Mesh.bounds.size.z, Is.EqualTo(100).Within(0.001));
                        foreach (var normal in chunk.Mesh.normals) Assert.That(normal.y, Is.GreaterThan(0.999f));
                    }
                    Assert.That(build.Chunks[0].Mesh.bounds.min.x, Is.EqualTo(-3.5f).Within(0.001));
                    Assert.That(build.Chunks[1].Mesh.bounds.max.x, Is.EqualTo(3.5f).Within(0.001));
                    Assert.That(build.Chunks[0].Origin.z + build.Chunks[0].Mesh.bounds.max.z,
                        Is.EqualTo(build.Chunks[2].Origin.z + build.Chunks[2].Mesh.bounds.min.z).Within(0.001));
                }
            }
            finally
            {
                if (road != null) Object.DestroyImmediate(road.gameObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void SlopingRoadSeparatesHorizontalStationFromTravelDistance()
        {
            var profile = RoadProfile.CreateTwoLane();
            RoadAuthoring road = null;
            try
            {
                road = RoadAuthoringCommands.Create(profile, new[] { Vector3.zero, new Vector3(0, 30, 40) });
                var geometry = RoadGeometry.Evaluate(road);
                Assert.That(geometry.Length, Is.EqualTo(40).Within(0.001));
                Assert.That(geometry.TravelLength, Is.EqualTo(50).Within(0.001));
                Assert.That(Vector3.Distance(geometry.Sample(20).Position, new Vector3(0, 15, 20)), Is.LessThan(0.001));
            }
            finally
            {
                if (road != null) Object.DestroyImmediate(road.gameObject);
                Object.DestroyImmediate(profile);
            }
        }
    }
}
