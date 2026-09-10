using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class FreeRoamTests
    {
        [Test]
        public void NavigationFollowsConnectedRoadsAroundABlock()
        {
            IRoadNetwork graph = new RoadGraph(new[]
            {
                new RoadNode { position = Vector3.zero, exits = new[] { 1 } },
                new RoadNode { position = new Vector3(0, 0, 100), exits = new[] { 0, 2 } },
                new RoadNode { position = new Vector3(100, 0, 100), exits = new[] { 1, 3 } },
                new RoadNode { position = new Vector3(100, 0, 0), exits = new[] { 2 } }
            });
            var route = new List<Vector3>();
            Assert.That(graph.TryRoute(Vector3.zero, new Vector3(100, 0, 0), route), Is.True);
            Assert.That(route, Does.Contain(new Vector3(0, 0, 100)));
            Assert.That(route, Does.Contain(new Vector3(100, 0, 100)));
            Assert.That(route[route.Count - 1], Is.EqualTo(new Vector3(100, 0, 0)));
        }

        [Test]
        public void DisconnectedRoadsDoNotInventADiagonalShortcut()
        {
            var graph = new RoadGraph(new[]
            {
                new RoadNode { position = Vector3.zero, exits = new[] { 1 } },
                new RoadNode { position = Vector3.forward * 100, exits = new[] { 0 } },
                new RoadNode { position = Vector3.right * 100, exits = new[] { 3 } },
                new RoadNode { position = new Vector3(100, 0, 100), exits = new[] { 2 } }
            });
            var route = new List<Vector3> { Vector3.one };
            Assert.That(graph.TryRoute(Vector3.zero, Vector3.right * 100, route), Is.False);
            Assert.That(route, Is.Empty);
            Assert.That(graph.NearestRoadPoint(new Vector3(4, 1, 50)), Is.EqualTo(new Vector3(0, 0, 50)));
        }

        [Test]
        public void SameRoadRouteDoesNotDetourViaIntersections()
        {
            var graph = new RoadGraph(new[] {
                new RoadNode { position = Vector3.zero, exits = new[] { 1 } },
                new RoadNode { position = Vector3.forward * 100, exits = new[] { 0 } } });
            var route = new List<Vector3>();
            Assert.That(graph.TryRoute(new Vector3(3, 0, 30), new Vector3(4, 0, 70), route), Is.True);
            Assert.That(route.Count, Is.EqualTo(2));
            Assert.That(Vector3.Distance(route[0], new Vector3(0, 0, 30)), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(route[1], new Vector3(0, 0, 70)), Is.LessThan(0.001f));
        }

        [Test]
        public void EventRequiresOrderedCheckpointsAndEveryLap()
        {
            var first = new Vector3(0, 0, 100); var second = new Vector3(100, 0, 100);
            var progress = new MissionCourse(new[] { first, second }, 2, 100);
            progress.Advance(1, second, second, 90);
            Assert.That(progress.CheckpointsPassed, Is.Zero);
            progress.Advance(1, first, first, 90); progress.Advance(1, second, second, 90);
            Assert.That(progress.Lap, Is.EqualTo(2));
            Assert.That(progress.Outcome, Is.EqualTo(MissionState.Active));
            progress.Advance(1, first, first, 90); progress.Advance(1, second, second, 90);
            Assert.That(progress.Outcome, Is.EqualTo(MissionState.Succeeded));
        }

        [Test]
        public void HighSpeedCrossingCountsAndRewardCanOnlyBeClaimedOnce()
        {
            var progress = new MissionCourse(new[] { Vector3.forward * 50 }, 1, 10, 100, cash: 1500);
            progress.Advance(1, Vector3.zero, Vector3.forward * 100, 120);
            Assert.That(progress.Outcome, Is.EqualTo(MissionState.Succeeded));
            Assert.That(progress.Runtime.Result.cash, Is.EqualTo(1500));
            progress.Advance(1, Vector3.zero, Vector3.forward * 100, 120);
            Assert.That(progress.CheckpointsPassed, Is.EqualTo(1));
            Assert.That(progress.Runtime.Result.cash, Is.EqualTo(1500));
        }

        [TestCase(11, 120)]
        [TestCase(1, 90)]
        public void MissedTimeOrSpeedTargetCannotAwardCash(float elapsed, float speed)
        {
            var progress = new MissionCourse(new[] { Vector3.forward * 50 }, 1, 10, 100);
            progress.Advance(elapsed, Vector3.zero, Vector3.forward * 100, speed);
            Assert.That(progress.Outcome, Is.EqualTo(MissionState.Failed));
            Assert.That(progress.Runtime.Result.cash, Is.Zero);
        }

        [Test]
        public void OldProfilesNormalizeAndNewDiscoveryDataRoundTrips()
        {
            var profile = JsonUtility.FromJson<CareerProfileData>("{\"saveVersion\":1,\"profileId\":\"test\",\"activeVehicleId\":\"car\"}");
            Assert.That(profile.Validate("test", out _), Is.True);
            Assert.That(profile.freeRoam.completedEventIds, Is.Not.Null);
            profile.freeRoam.completedEventIds.AddRange(new[] { " sprint ", "sprint", "" });
            profile.freeRoam.discoveredLocationIds.Add("garage");
            profile.Normalize();
            var restored = JsonUtility.FromJson<CareerProfileData>(JsonUtility.ToJson(profile));
            Assert.That(restored.freeRoam.completedEventIds, Is.EqualTo(new[] { "sprint" }));
            Assert.That(restored.freeRoam.discoveredLocationIds, Is.EqualTo(new[] { "garage" }));
        }

        [Test]
        public void TrafficLightsNeverReleasePerpendicularApproachesTogether()
        {
            var root = new GameObject("Signals Test");
            try
            {
                var signals = root.AddComponent<RoadTrafficSignals>();
                bool clearance = false; bool northGreen = false; bool eastGreen = false;
                for (float time = 0; time < 44; time += 0.25f)
                {
                    bool north = signals.IsGreen(Vector3.zero, Vector3.forward, time);
                    bool east = signals.IsGreen(Vector3.zero, Vector3.right, time);
                    Assert.That(north && east, Is.False);
                    clearance |= !north && !east; northGreen |= north; eastGreen |= east;
                }
                Assert.That(clearance && northGreen && eastGreen, Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void WallsBlockPoliceSightButObserverAndPlayerDoNot()
        {
            var observer = new GameObject("Observer"); var target = new GameObject("Target"); var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var sight = observer.AddComponent<RoadPolicePerception>();
                observer.AddComponent<BoxCollider>(); target.AddComponent<BoxCollider>();
                target.transform.position = Vector3.forward * 20;
                wall.transform.position = Vector3.forward * 10; wall.transform.localScale = new Vector3(5, 5, 1);
                Physics.SyncTransforms();
                Assert.That(sight.ClearLine(Vector3.zero, target.transform.position, observer.transform, target.transform, 30), Is.False);
                wall.SetActive(false); Physics.SyncTransforms();
                Assert.That(sight.ClearLine(Vector3.zero, target.transform.position, observer.transform, target.transform, 30), Is.True);
                Assert.That(sight.ClearLine(Vector3.zero, target.transform.position, observer.transform, target.transform, 10), Is.False);
            }
            finally { Object.DestroyImmediate(observer); Object.DestroyImmediate(target); Object.DestroyImmediate(wall); }
        }
    }
}
