using System;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleCameraCollisionTests
    {
        private GameObject target, wall, roof;
        private CameraCollisionModule collision;
        private readonly VehicleCameraProfile.Collision settings = new VehicleCameraProfile.Collision();
        [SetUp] public void Setup()
        {
            target = GameObject.CreatePrimitive(PrimitiveType.Cube); target.transform.position = new Vector3(0, 1, 0);
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(0, 2, -4); wall.transform.localScale = new Vector3(10, 6, .3f);
            roof = GameObject.CreatePrimitive(PrimitiveType.Cube); roof.transform.position = new Vector3(0, 3, 0); roof.transform.localScale = new Vector3(10, .3f, 20);
            Physics.SyncTransforms(); collision = new CameraCollisionModule(); collision.Bind(target.transform);
        }
        [TearDown] public void Cleanup() { collision.Dispose(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(wall); UnityEngine.Object.DestroyImmediate(roof); }
        private Vector3 Resolve(Vector3 desired) => collision.Resolve(target.transform.position, desired, settings, .1f, 82, 16f / 9, .02f);
        [Test] public void WallBlocksDynamicPullbackAndOwnBodyIsIgnored()
        {
            var pose = Resolve(new Vector3(0, 2, -8)); Assert.That(pose.z, Is.GreaterThan(-3.6f));
            Assert.That(pose.z, Is.LessThan(-2));
        }
        [Test] public void TunnelRoofAndInitialOverlapAreRecovered()
        {
            var pose = Resolve(new Vector3(0, 3, -2)); Assert.That(pose.y, Is.LessThan(2.6f));
            collision.Reset(); target.transform.position = new Vector3(0, 3, -2); Physics.SyncTransforms();
            pose = Resolve(target.transform.position); Assert.That(Mathf.Abs(pose.y - 3), Is.GreaterThan(.4f));
        }
        [Test] public void CollisionQueriesHaveZeroSteadyStateAllocation()
        {
            for (int i = 0; i < 30; i++) Resolve(new Vector3(0, 2, -8));
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) Resolve(new Vector3(0, 2, -8));
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - start, Is.Zero);
        }
        [Test] public void CollisionReturnsSmoothlyAfterObstructionClears()
        {
            Vector3 blocked = Resolve(new Vector3(0, 2, -8)); wall.SetActive(false); Physics.SyncTransforms();
            var next = Resolve(new Vector3(0, 2, -8)); Assert.That(next.z, Is.LessThan(blocked.z)); Assert.That(next.z, Is.GreaterThan(-7));
        }
    }
}
