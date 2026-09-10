using System;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class RoadRuntimeTests
    {
        [Test]
        public void InvalidPublicationDoesNotPartiallyInitializeAnImmutableAsset()
        {
            var asset = ScriptableObject.CreateInstance<RoadNetworkAsset>();
            try
            {
                Assert.Throws<ArgumentException>(() => asset.Initialize(RoadId.New(), "invalid", new RoadBakedLane[] { null }, Array.Empty<RoadBakedChunk>()));
                Assert.That(asset.SchemaVersion, Is.Zero);
                asset.Initialize(RoadId.New(), "valid", new[] { Lane(RoadId.New(), Vector3.zero, Vector3.forward * 100) }, Array.Empty<RoadBakedChunk>());
                Assert.That(new RoadRuntimeNetwork(asset).Count, Is.EqualTo(1));
                Assert.Throws<InvalidOperationException>(() => asset.Initialize(RoadId.New(), "overwrite", Array.Empty<RoadBakedLane>(), Array.Empty<RoadBakedChunk>()));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void NarrowLaneGeometryIsPreservedWhileVehicleRoutesRequireClearance()
        {
            var id = RoadId.New(); var asset = ScriptableObject.CreateInstance<RoadNetworkAsset>(); var root = new GameObject("Narrow road test");
            try
            {
                asset.Initialize(RoadId.New(), "narrow lane fixture", new[]
                {
                    new RoadBakedLane(id, RoadId.New(), RoadClass.Local, 10, new[]
                    {
                        new RoadLaneSample { position = Vector3.zero, width = 1.5f, forward = Vector3.forward, left = Vector3.left, up = Vector3.up },
                        new RoadLaneSample { position = Vector3.forward * 100, distance = 100, station = 100, width = 1.5f, forward = Vector3.forward, left = Vector3.left, up = Vector3.up }
                    }, Array.Empty<RoadId>(), null)
                }, Array.Empty<RoadBakedChunk>());
                var network = root.AddComponent<RoadNetwork>(); network.ConfigureBaked(asset);
                Assert.That(network.Lanes[0].Width, Is.EqualTo(1.5f));
                var route = new int[2];
                Assert.That(network.Lanes.TryRoute(0, 0, 0, route, out _), Is.False);
                Assert.That(network.Lanes.TryRoute(0, 0, 0, route, out _, minimumWidth: 1.2f), Is.True);
                Assert.That(network.TryRoute(Vector3.zero, Vector3.forward * 50, new System.Collections.Generic.List<Vector3>()), Is.False);
                Assert.That(network.Runtime.Route(new RoadLaneAnchor(id, 0), new RoadLaneAnchor(id, 50), new RoadRouteBuffer(2)), Is.EqualTo(RoadRouteResult.Success));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void PublishedNetworkRoutesAndTrafficObserveTheSameDirectedLanesAndClosures()
        {
            var first = RoadId.New(); var second = RoadId.New();
            var asset = ScriptableObject.CreateInstance<RoadNetworkAsset>(); var root = new GameObject("Runtime network test");
            try
            {
                asset.Initialize(RoadId.New(), "integration fixture", new[]
                {
                    Lane(first, Vector3.zero, Vector3.forward * 100, second),
                    Lane(second, Vector3.forward * 100, Vector3.forward * 200)
                }, Array.Empty<RoadBakedChunk>());
                var network = root.AddComponent<RoadNetwork>(); network.ConfigureBaked(asset);
                var route = new System.Collections.Generic.List<Vector3>();
                Assert.That(network.TryRoute(Vector3.forward * 25, Vector3.forward * 175, route), Is.True);
                Assert.That(route[0].z, Is.EqualTo(25).Within(0.001)); Assert.That(route[route.Count - 1].z, Is.EqualTo(175).Within(0.001));
                Assert.That(network.TryRoute(Vector3.forward * 175, Vector3.forward * 25, route), Is.False);
                Assert.That(network.TryLaneIndex(second, out int closedLane), Is.True);
                using (network.Lanes.AcquireClosure(closedLane, 50))
                    Assert.That(network.TryRoute(Vector3.forward * 25, Vector3.forward * 175, route), Is.False);
                Assert.That(network.TryRoute(Vector3.forward * 25, Vector3.forward * 175, route), Is.True);
                Assert.Throws<InvalidOperationException>(() => network.ConfigureLanes(Array.Empty<RoadLaneDefinition>()));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void DirectedRoutesRespectPartialLanesLoopsDisconnectionsAndClosures()
        {
            var first = RoadId.New(); var second = RoadId.New(); var isolated = RoadId.New();
            var asset = ScriptableObject.CreateInstance<RoadNetworkAsset>();
            try
            {
                asset.Initialize(RoadId.New(), "test fixture", new[]
                {
                    Lane(first, new Vector3(0, 0, 0), new Vector3(0, 0, 100), second),
                    Lane(second, new Vector3(0, 0, 100), new Vector3(0, 0, 0), first),
                    Lane(isolated, new Vector3(10, 10, 0), new Vector3(10, 10, 100))
                }, Array.Empty<RoadBakedChunk>());
                var closed = new bool[3]; var network = new RoadRuntimeNetwork(asset, index => closed[index]);
                var scratch = new RoadRouteBuffer(8);
                Assert.That(network.Route(new RoadLaneAnchor(first, 20), new RoadLaneAnchor(first, 80), scratch), Is.EqualTo(RoadRouteResult.Success));
                Assert.That(scratch.Count, Is.EqualTo(1)); Assert.That(scratch.Distance, Is.EqualTo(60).Within(0.001));
                Assert.That(network.Route(new RoadLaneAnchor(first, 80), new RoadLaneAnchor(first, 20), scratch), Is.EqualTo(RoadRouteResult.Success));
                Assert.That(scratch.Count, Is.EqualTo(3)); Assert.That(scratch.Distance, Is.EqualTo(140).Within(0.001));
                Assert.That(scratch[1].LaneId, Is.EqualTo(second));
                Assert.That(network.Route(new RoadLaneAnchor(first, 20), new RoadLaneAnchor(isolated, 80), scratch), Is.EqualTo(RoadRouteResult.Disconnected));
                closed[1] = true;
                Assert.That(network.Route(new RoadLaneAnchor(first, 80), new RoadLaneAnchor(first, 20), scratch), Is.EqualTo(RoadRouteResult.Disconnected));
                Assert.That(scratch.Count, Is.Zero);
                Assert.That(network.TryNearest(new Vector3(10, 10.1f, 50), Vector3.forward, 1, out var anchor), Is.True);
                Assert.That(anchor.LaneId, Is.EqualTo(isolated)); Assert.That(anchor.Distance, Is.EqualTo(50).Within(0.001));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }
        private static RoadBakedLane Lane(RoadId id, Vector3 from, Vector3 to, params RoadId[] successors)
        {
            var direction = (to - from).normalized;
            return new RoadBakedLane(id, RoadId.New(), RoadClass.Local, 10, new[]
            {
                new RoadLaneSample { position = from, distance = 0, station = 0, width = 3.5f, forward = direction, left = Vector3.left, up = Vector3.up },
                new RoadLaneSample { position = to, distance = 100, station = 100, width = 3.5f, forward = direction, left = Vector3.left, up = Vector3.up }
            }, successors, null);
        }
    }
}
