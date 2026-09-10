using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class TrafficAmbientTests
    {
        [Test]
        public void RedSignalIsVisibleEarlyAndDoesNotTrapCommittedVehicles()
        {
            var root = new GameObject("Signals test");
            try
            {
                var signals = root.AddComponent<RoadTrafficSignals>();
                var graph = new RoadGraph(new[] {
                    new RoadNode { position = Vector3.zero, exits = new[] { 1, 2, 3 } },
                    new RoadNode { position = Vector3.forward * 100, exits = new[] { 0 } },
                    new RoadNode { position = Vector3.back * 100, exits = new[] { 0 } },
                    new RoadNode { position = Vector3.right * 100, exits = new[] { 0 } } });
                Vector3 approach = signals.IsGreen(Vector3.zero, Vector3.forward, Time.time) ? Vector3.right : Vector3.forward;
                Assert.That(signals.StopLineDistance(-approach * 60, approach, graph), Is.InRange(49, 51));
                Assert.That(signals.StopLineDistance(-approach * 7, approach, graph), Is.EqualTo(float.PositiveInfinity));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void DriverIdentityIsRepeatableAndVaried()
        {
            Assert.That(TrafficDriverProfile.FromSeed(41).followingSeconds,
                Is.EqualTo(TrafficDriverProfile.FromSeed(41).followingSeconds));
            Assert.That(TrafficDriverProfile.FromSeed(41).followingSeconds,
                Is.Not.EqualTo(TrafficDriverProfile.FromSeed(42).followingSeconds));
        }

        [Test]
        public void FreeRoadAccelerationIsSmoothAndConvergesToDesiredSpeed()
        {
            var driver = new TrafficDriver(new TrafficDriverProfile());
            float speed = 0;
            for (int i = 0; i < 1500; i++)
            {
                float next = driver.Step(0.02f, speed, 12, float.PositiveInfinity, 0, false);
                Assert.That(next - speed, Is.LessThanOrEqualTo(0.041f)); speed = next;
            }
            Assert.That(speed, Is.InRange(11.8f, 12.1f));
        }

        [Test]
        public void StationaryQueueIsApproachedWithoutBumperContact()
        {
            var driver = new TrafficDriver(new TrafficDriverProfile());
            float speed = 12, gap = 45;
            for (int i = 0; i < 1200; i++)
            {
                speed = driver.Step(0.02f, speed, 12, gap, 0, false);
                gap -= speed * 0.02f;
                Assert.That(gap, Is.GreaterThan(0.5f));
            }
            Assert.That(speed, Is.LessThan(0.2f));
            Assert.That(gap, Is.InRange(1.5f, 3.5f));
            Assert.That(driver.State, Is.EqualTo(TrafficDriverState.Waiting));
        }

        [Test]
        public void StopCommandActuallyStopsInsteadOfCreeping()
        {
            var driver = new TrafficDriver(new TrafficDriverProfile());
            float speed = 5;
            for (int i = 0; i < 500; i++) speed = driver.Step(0.02f, speed, 0, float.PositiveInfinity, 0, false);
            Assert.That(speed, Is.Zero);
        }

        [Test]
        public void LeadVehicleDepartureReleasesQueueWithoutTeleporting()
        {
            var driver = new TrafficDriver(new TrafficDriverProfile());
            float speed = 0, gap = 2;
            for (int i = 0; i < 400; i++)
            {
                speed = driver.Step(0.02f, speed, 12, gap, 6, false);
                gap += (6 - speed) * 0.02f;
                Assert.That(gap, Is.GreaterThan(1));
            }
            Assert.That(speed, Is.GreaterThan(5));
        }

        [Test]
        public void SuddenCutInOverridesComfortBraking()
        {
            var driver = new TrafficDriver(new TrafficDriverProfile());
            float next = driver.Step(0.02f, 12, 12, 2, 0, false);
            Assert.That(next, Is.LessThan(11.8f)); Assert.That(driver.Braking, Is.True);
        }

        [Test]
        public void EmergencyYieldAndImpactRecoveryExpire()
        {
            var driver = new TrafficDriver(new TrafficDriverProfile());
            float speed = 10;
            for (int i = 0; i < 500; i++) speed = driver.Step(0.02f, speed, 12, float.PositiveInfinity, 0, true);
            Assert.That(driver.State, Is.EqualTo(TrafficDriverState.Yielding)); Assert.That(speed, Is.LessThan(3.5f));
            driver.NotifyCollision(8);
            driver.Step(0.02f, speed, 12, float.PositiveInfinity, 0, false);
            Assert.That(driver.State, Is.EqualTo(TrafficDriverState.Recovering));
            for (int i = 0; i < 400; i++) speed = driver.Step(0.02f, speed, 12, float.PositiveInfinity, 0, false);
            Assert.That(driver.State, Is.EqualTo(TrafficDriverState.Cruising)); Assert.That(speed, Is.GreaterThan(3));
        }

        [Test]
        public void PoolResetClearsCollisionMemory()
        {
            var driver = new TrafficDriver(new TrafficDriverProfile());
            driver.NotifyCollision(20); driver.Reset();
            driver.Step(0.02f, 0, 12, float.PositiveInfinity, 0, false);
            Assert.That(driver.State, Is.EqualTo(TrafficDriverState.Cruising));
        }

        [Test]
        public void PedestrianDangerInterruptsIdleThenRecoversBeforeWalking()
        {
            var routine = new AmbientRoutine(); routine.Pause(10);
            routine.Advance(0.2f, true);
            Assert.That(routine.Activity, Is.EqualTo(AmbientActivity.Fleeing));
            routine.Pause(10); routine.Advance(2, false);
            Assert.That(routine.Activity, Is.EqualTo(AmbientActivity.Fleeing));
            routine.Advance(2.1f, false);
            Assert.That(routine.Activity, Is.EqualTo(AmbientActivity.CatchingBreath));
            routine.Advance(3.1f, false);
            Assert.That(routine.Activity, Is.EqualTo(AmbientActivity.Walking));
        }

        [Test]
        public void PausedWorldDoesNotAdvanceAmbientActivities()
        {
            var routine = new AmbientRoutine(); routine.Pause(2);
            routine.Advance(0, true);
            Assert.That(routine.Activity, Is.EqualTo(AmbientActivity.LookingAround));
        }
    }
}
