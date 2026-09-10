using System;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleCameraPipelineTests
    {
        private VehicleCameraProfile profile;
        private VehicleCameraPipeline pipeline;
        [SetUp] public void Setup() { profile = ScriptableObject.CreateInstance<VehicleCameraProfile>(); pipeline = new VehicleCameraPipeline(); }
        [TearDown] public void Cleanup() => UnityEngine.Object.DestroyImmediate(profile);
        private static VehicleCameraFrame Frame(float speed) => new VehicleCameraFrame { rotation = Quaternion.identity, cockpitRotation = Quaternion.identity,
            cockpitPosition = new Vector3(0, 1.2f, .5f), hasCockpit = true, velocity = Vector3.forward * speed / 3.6f, speedKph = speed, grounded = true, roadNormal = Vector3.up, roughness = .08f };
        private VehicleCameraPose Settle(VehicleCameraFrame frame, bool cockpit = false, VehicleCameraEffects mask = VehicleCameraEffects.All, int count = 600)
        {
            VehicleCameraPose pose = default;
            for (int i = 0; i < count; i++) pose = pipeline.Resolve(profile, frame, cockpit, mask, 1f / 60);
            return pose;
        }
        [TestCase(0)] [TestCase(50)] [TestCase(100)] [TestCase(150)] [TestCase(200)] [TestCase(250)] [TestCase(320)]
        public void SpeedStepsHaveCurveFovAndBoundedDistance(float speed)
        {
            var pose = Settle(Frame(speed));
            Assert.That(pose.fieldOfView, Is.EqualTo(profile.chase.speed.speedToFov.Evaluate(speed)).Within(.01f));
            Assert.That(pose.debug.distance, Is.InRange(5.3f, 6.6f));
            Assert.That(pose.debug.shakeStrength, Is.InRange(0, 1));
            TestContext.WriteLine($"{speed:F0} km/h: new FOV={pose.fieldOfView:F2}, legacy FOV={Mathf.Lerp(64, 78, Mathf.Clamp01(speed / 230)):F2}, distance={pose.debug.distance:F2}, shake={pose.debug.shakeStrength:F3}, lookAhead={pose.debug.lookAhead:F2}");
        }
        [Test] public void ParkedSuspensionJitterCannotShakeCamera()
        {
            Settle(Frame(0)); var first = Settle(Frame(0));
            var jitter = Frame(.1f); jitter.localAcceleration = new Vector3(3, 5, 8); jitter.suspensionRate = 3; jitter.yawRate = .3f;
            for (int i = 0; i < 600; i++)
            {
                jitter.position = new Vector3(0, Mathf.Sin(i) * .008f, 0); jitter.rotation = Quaternion.Euler(Mathf.Sin(i) * .2f, 0, 0);
                jitter.cockpitPosition.y = 1.2f + jitter.position.y;
                var next = pipeline.Resolve(profile, jitter, false, VehicleCameraEffects.All, 1f / 60);
                Assert.That(Vector3.Distance(first.position, next.position), Is.LessThan(.00001f));
                Assert.That(Quaternion.Angle(first.rotation, next.rotation), Is.LessThan(.001f));
                Assert.That(next.debug.shakeStrength, Is.Zero); Assert.That(next.debug.accelerationFov, Is.Zero);
            }
        }
        [Test] public void FullStopSettlesAndMovingAgainReleasesParkLock()
        {
            var moving = Frame(220); moving.localAcceleration.z = -10; Settle(moving);
            var stopped = Settle(Frame(0)); Assert.That(stopped.debug.parked, Is.True); Assert.That(stopped.fieldOfView, Is.EqualTo(65).Within(.01));
            var start = Frame(5); start.position.z = .2f; start.localAcceleration.z = 7;
            var resumed = pipeline.Resolve(profile, start, false, VehicleCameraEffects.All, .02f);
            Assert.That(resumed.debug.parked, Is.False); Assert.That(resumed.debug.accelerationFov, Is.GreaterThan(0));
        }
        [Test] public void ActualAccelerationProducesKickAndBrakingCompresses()
        {
            var steady = Settle(Frame(160)); var f = Frame(160); f.localAcceleration.z = 9;
            var accelerating = Settle(f); Assert.That(accelerating.fieldOfView - steady.fieldOfView, Is.GreaterThan(3));
            Assert.That(accelerating.position.z, Is.LessThan(steady.position.z - .35f));
            f.localAcceleration.z = -10; var braking = Settle(f);
            Assert.That(braking.fieldOfView, Is.LessThan(steady.fieldOfView)); Assert.That(braking.position.z, Is.GreaterThan(steady.position.z + .4f));
        }
        [Test] public void SpeedAndNitrousTransitionsAreGradualAndRecover()
        {
            var baseline = Settle(Frame(160)); var f = Frame(160); f.nitrous = true;
            var first = pipeline.Resolve(profile, f, false, VehicleCameraEffects.All, 1f / 60);
            Assert.That(first.fieldOfView - baseline.fieldOfView, Is.InRange(.01f, 2f));
            var active = Settle(f); Assert.That(active.fieldOfView, Is.GreaterThan(baseline.fieldOfView + 6));
            Assert.That(active.position.z, Is.LessThan(baseline.position.z - .4f));
            f.nitrous = false; var recovered = Settle(f); Assert.That(recovered.fieldOfView, Is.EqualTo(baseline.fieldOfView).Within(.01));
        }
        [Test] public void DriftUsesTravelAngleAndSpeedAndSteeringUsesYaw()
        {
            var baseline = Settle(Frame(160)); var f = Frame(160); f.slipAngle = 30; f.velocity = Quaternion.Euler(0, 30, 0) * f.velocity; f.yawRate = 30;
            var drift = Settle(f); Assert.That(drift.debug.driftBlend, Is.GreaterThan(.9f));
            Assert.That(Mathf.Abs(drift.position.x), Is.GreaterThan(.5f)); Assert.That(drift.debug.driftFov, Is.GreaterThan(1));
            f = Frame(10); f.slipAngle = 40; Assert.That(Settle(f).debug.driftBlend, Is.LessThan(.001));
            f = Frame(160); f.yawRate = 30; var corner = Settle(f); Assert.That(Mathf.Abs(corner.position.x - baseline.position.x), Is.GreaterThan(.1f));
        }
        [Test] public void LandingScalesWithImpactAndRejectsContactFlicker()
        {
            float Land(float down, int frames)
            {
                pipeline.Reset(); var f = Frame(100); Settle(f); f.grounded = false; f.velocity.y = -down;
                Settle(f, count: frames); f.grounded = true; f.velocity.y = 0;
                return pipeline.Resolve(profile, f, false, VehicleCameraEffects.All, 1f / 60).debug.landingStrength;
            }
            Assert.That(Land(10, 1), Is.Zero); Assert.That(Land(1, 30), Is.Zero);
            float small = Land(4, 30), hard = Land(12, 30);
            Assert.That(hard, Is.GreaterThan(small * 2));
        }
        [Test] public void CockpitMotionIsBoundedAndAirborneRotationDamped()
        {
            var f = Frame(300); f.nitrous = true; f.localAcceleration = new Vector3(15, 0, 15); f.slipAngle = 30; f.yawRate = 40;
            var pose = Settle(f, true); Assert.That(Vector3.Distance(pose.position, f.cockpitPosition), Is.LessThanOrEqualTo(.121));
            Assert.That(pose.fieldOfView, Is.LessThanOrEqualTo(82));
            f.grounded = false; f.cockpitRotation = Quaternion.Euler(55, 0, 40);
            pose = Settle(f, true); Assert.That(Quaternion.Angle(pose.rotation, Quaternion.identity), Is.LessThan(15));
        }
        [Test] public void DisabledEffectsReturnToStableBaseAndPauseFreezes()
        {
            var f = Frame(250); f.nitrous = true; f.localAcceleration.z = 10; f.slipAngle = 30; Settle(f);
            var pose = Settle(f, mask: VehicleCameraEffects.None);
            Assert.That(pose.fieldOfView, Is.EqualTo(65).Within(.01)); Assert.That(pose.debug.shakeStrength, Is.Zero);
            Assert.That(pose.debug.motionBlur, Is.Zero); Assert.That(pose.debug.lookAhead, Is.LessThan(.001));
            f.position += Vector3.one * 10; var paused = pipeline.Resolve(profile, f, false, VehicleCameraEffects.All, 0);
            Assert.That(paused.position, Is.EqualTo(pose.position));
        }
        [Test] public void SteadyStatePipelineAllocatesZeroBytes()
        {
            var f = Frame(250); f.nitrous = true; f.localAcceleration.z = 9; Settle(f);
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) { f.position.z += .1f; pipeline.Resolve(profile, f, false, VehicleCameraEffects.All, 1f / 60); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            Assert.That(allocated, Is.Zero);
        }
        [Test] public void SameVelocityHasDistinct80_160_250FramingWithoutExtraFollowDelay()
        {
            float previous = 0;
            foreach (float speed in new[] { 80f, 160f, 250f })
            {
                pipeline.Reset(); var f = Frame(speed); var pose = Settle(f);
                Assert.That(pose.fieldOfView, Is.GreaterThan(previous + 4)); previous = pose.fieldOfView;
                float initial = f.position.z - pose.position.z;
                for (int i = 0; i < 180; i++) { f.position += f.velocity / 60; pose = pipeline.Resolve(profile, f, false, VehicleCameraEffects.All, 1f / 60); }
                Assert.That(f.position.z - pose.position.z, Is.EqualTo(initial).Within(.03));
            }
        }
        [Test] public void FovAxisConversionRespectsAspect()
        {
            profile.fovAxis = VehicleCameraFovAxis.Horizontal;
            var pose = Settle(Frame(0)); Assert.That(pose.fieldOfView, Is.EqualTo(Camera.HorizontalToVerticalFieldOfView(65, 16f / 9)).Within(.01));
        }
        [Test] public void LandingUsesFinalDescentSpeedRatherThanEarlierFallPeak()
        {
            var f = Frame(100); Settle(f); f.grounded = false; f.velocity.y = -20; Settle(f, count: 20);
            f.velocity.y = -2; Settle(f, count: 10); f.grounded = true; f.velocity.y = 0;
            var pose = pipeline.Resolve(profile, f, false, VehicleCameraEffects.All, 1f / 60);
            Assert.That(pose.debug.landingStrength, Is.LessThan(.1f));
        }
        [Test] public void ResetDiscardsNitrousAndLandingHistory()
        {
            var f = Frame(250); f.nitrous = true; Settle(f); pipeline.Reset();
            var pose = pipeline.Resolve(profile, Frame(0), false, VehicleCameraEffects.All, 1f / 60);
            Assert.That(pose.fieldOfView, Is.EqualTo(65).Within(.001));
            Assert.That(pose.debug.nitrousFov, Is.Zero); Assert.That(pose.debug.landingStrength, Is.Zero);
        }
        [Test] public void RoughSurfacesAndSuspensionAddMotionButStayQuietAtRest()
        {
            float Motion(float roughness, float suspension)
            {
                pipeline.Reset(); var f = Frame(160); f.roughness = roughness; f.suspensionRate = suspension;
                Settle(f, mask: VehicleCameraEffects.RoadMotion);
                float min = float.MaxValue, max = float.MinValue;
                for (int i = 0; i < 120; i++)
                {
                    var pose = pipeline.Resolve(profile, f, false, VehicleCameraEffects.RoadMotion, 1f / 60);
                    min = Mathf.Min(min, pose.position.y); max = Mathf.Max(max, pose.position.y);
                }
                return max - min;
            }
            float smooth = Motion(.08f, 0), rough = Motion(.68f, 0);
            Assert.That(rough, Is.GreaterThan(smooth * 3));
            var f = Frame(160); f.suspensionRate = 1; var compressing = Settle(f, mask: VehicleCameraEffects.RoadMotion);
            f.suspensionRate = 0; var relaxed = Settle(f, mask: VehicleCameraEffects.RoadMotion);
            Assert.That(relaxed.position.y - compressing.position.y, Is.GreaterThan(.04f));
        }
        [Test] public void FollowAndFovRemainConsistentAcrossRenderRates()
        {
            VehicleCameraPose Run(int hz)
            {
                pipeline.Reset(); var f = Frame(160); VehicleCameraPose pose = default;
                for (int i = 0; i < hz * 5; i++)
                { f.position += f.velocity / hz; pose = pipeline.Resolve(profile, f, false, VehicleCameraEffects.All & ~VehicleCameraEffects.Shake & ~VehicleCameraEffects.RoadMotion, 1f / hz); }
                return pose;
            }
            var slow = Run(30); var fast = Run(120);
            Assert.That(slow.fieldOfView, Is.EqualTo(fast.fieldOfView).Within(.01));
            Assert.That(Vector3.Distance(slow.position, fast.position), Is.LessThan(.02));
            Assert.That(Quaternion.Angle(slow.rotation, fast.rotation), Is.LessThan(.1));
        }
    }
}
