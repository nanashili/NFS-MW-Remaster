using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class SensoryFeedbackTests
    {
        [Test]
        public void RadioDropsExpiredWrongEncounterAndInvalidContext()
        {
            var queue = new PoliceRadioQueue(4);
            queue.Enqueue(new RadioRequest(RadioCueKind.Pursuit, 60, 1, 10), 0);
            Assert.False(queue.TryTake(1, 2, uint.MaxValue, 256, out _));
            queue.Enqueue(new RadioRequest(RadioCueKind.Pursuit, 60, 2, 10), 0);
            Assert.False(queue.TryTake(1, 2, 1u << (int)RadioCueKind.Cooldown, 256, out _));
            queue.Enqueue(new RadioRequest(RadioCueKind.Cooldown, 60, 2, 2), 1);
            Assert.False(queue.TryTake(3, 2, uint.MaxValue, 256, out _));
            Assert.AreEqual(0, queue.Count);
        }

        [Test]
        public void RadioDeduplicatesAndOnlyHigherPriorityMayInterrupt()
        {
            var queue = new PoliceRadioQueue(2);
            var request = new RadioRequest(RadioCueKind.Pursuit, 60, 1, 10);
            Assert.True(queue.Enqueue(request, 0)); Assert.False(queue.Enqueue(request, 0));
            Assert.False(queue.TryTake(1, 1, uint.MaxValue, 60, out _));
            queue.Enqueue(new RadioRequest(RadioCueKind.Spikes, 20, 1, 10), 0);
            Assert.True(queue.TryTake(1, 1, uint.MaxValue, 60, out var selected)); Assert.AreEqual(RadioCueKind.Spikes, selected.Kind);
            queue.MarkPlayed(RadioCueKind.Spikes, 1, 10);
            Assert.False(queue.Enqueue(new RadioRequest(RadioCueKind.Spikes, 20, 1, 20), 2));
        }

        [Test]
        public void RadioFullQueuePreservesImportantRequests()
        {
            var queue = new PoliceRadioQueue(1);
            queue.Enqueue(new RadioRequest(RadioCueKind.Spikes, 20, 1, 10), 0);
            Assert.False(queue.Enqueue(new RadioRequest(RadioCueKind.Pursuit, 60, 1, 10), 0));
            Assert.True(queue.Enqueue(new RadioRequest(RadioCueKind.Arrested, 10, 1, 10), 0));
            Assert.True(queue.TryTake(1, 1, uint.MaxValue, 256, out var selected)); Assert.AreEqual(RadioCueKind.Arrested, selected.Kind);
        }

        [Test]
        public void RecordingRingKeepsChronologicalTailAndSnapshotIsIndependent()
        {
            var ring = new FeedbackRecording(3);
            for (int i = 0; i < 5; i++) ring.Add(new VehicleFeedbackFrame { Sequence = i });
            var frames = ring.Snapshot(); Assert.AreEqual(3, frames.Length);
            Assert.AreEqual(2, frames[0].Sequence); Assert.AreEqual(4, frames[2].Sequence);
            frames[0].Sequence = 99; Assert.AreEqual(2, ring.Snapshot()[0].Sequence);
            ring.Clear(); Assert.AreEqual(0, ring.Count);
        }

        [Test]
        public void MaterialPairsAreSymmetricAndDuplicatesAreRejected()
        {
            var library = ScriptableObject.CreateInstance<ImpactMaterialLibrary>();
            try
            {
                library.pairs = new[] { new ImpactMaterialPair { a = SensorySurface.Metal, b = SensorySurface.Wood } };
                Assert.NotNull(library.Find(SensorySurface.Wood, SensorySurface.Metal));
                Assert.Null(library.Find(SensorySurface.Glass, SensorySurface.Metal));
                library.pairs = new[] { library.pairs[0], new ImpactMaterialPair { a = SensorySurface.Wood, b = SensorySurface.Metal } };
                Assert.False(library.Validate(out _));
            }
            finally { Object.DestroyImmediate(library); }
        }

        [Test]
        public void DestructionLatchesOnceAndPoolResetCreatesANewGeneration()
        {
            var latch = new DestructionLatch();
            Assert.False(latch.TryBreak(float.NaN, 0.2f)); Assert.False(latch.TryBreak(0.1f, 0.2f));
            Assert.True(latch.TryBreak(0.4f, 0.2f)); Assert.False(latch.TryBreak(1, 0.2f));
            latch.Reset(); Assert.AreEqual(1, latch.Generation); Assert.True(latch.TryBreak(1, 0.2f));
        }

        [Test]
        public void FeedbackSurfaceMigrationCanPreserveExistingPhysics()
        {
            var root = new GameObject(); var profile = ScriptableObject.CreateInstance<SensorySurfaceProfile>();
            try
            {
                var surface = root.AddComponent<VehicleSurface>(); surface.Configure("Custom road", 0.6f, 2.5f);
                profile.surface = SensorySurface.Gravel; profile.grip = 0.8f;
                surface.SetProfile(profile, false); Assert.AreEqual(0.6f, surface.GripMultiplier); Assert.AreEqual(2.5f, surface.RollingResistanceMultiplier);
                Assert.AreEqual(SensorySurface.Gravel, surface.Kind);
                surface.SetProfile(profile); Assert.AreEqual(0.8f, surface.GripMultiplier);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(profile); }
        }

        [Test]
        public void ReplayRejectsUnorderedOrNonFiniteFrames()
        {
            var root = new GameObject();
            try
            {
                var replay = root.AddComponent<FeedbackReplaySource>();
                Assert.Throws<System.ArgumentException>(() => replay.Play(new[] { new VehicleFeedbackFrame { Time = 1, Rotation = Quaternion.identity }, new VehicleFeedbackFrame { Time = 0, Rotation = Quaternion.identity } }));
                Assert.Throws<System.ArgumentException>(() => replay.Play(new[] { new VehicleFeedbackFrame { Time = 0, Rotation = Quaternion.identity }, new VehicleFeedbackFrame { Time = 1, Rotation = Quaternion.identity, Speed = float.NaN } }));
            }
            finally { Object.DestroyImmediate(root); }
        }
        [Test]
        public void ReplayAcceptsValidFramesAndStopClearsState()
        {
            var root = new GameObject();
            try
            {
                var replay = root.AddComponent<FeedbackReplaySource>();
                replay.Play(new[] { new VehicleFeedbackFrame { Time = 0, Rotation = Quaternion.identity }, new VehicleFeedbackFrame { Time = 1, Rotation = Quaternion.identity } });
                Assert.True(replay.Playing); replay.Stop(); Assert.False(replay.Playing); Assert.AreEqual(0, replay.Frame.Sequence);
            }
            finally { Object.DestroyImmediate(root); }
        }
        [Test]
        public void ReplayCannotBeAttachedToAGameplayPhysicsHierarchy()
        {
            var root = new GameObject(); var child = new GameObject(); child.transform.SetParent(root.transform);
            try
            {
                root.AddComponent<Rigidbody>(); var replay = child.AddComponent<FeedbackReplaySource>();
                Assert.Throws<System.InvalidOperationException>(() => replay.Play(new[] { new VehicleFeedbackFrame { Time = 0, Rotation = Quaternion.identity }, new VehicleFeedbackFrame { Time = 1, Rotation = Quaternion.identity } }));
            }
            finally { Object.DestroyImmediate(root); }
        }
        [Test]
        public void ReplayValidationRejectsInvalidRotationAndWheelContact()
        {
            var frame = new VehicleFeedbackFrame { Rotation = Quaternion.identity, WheelCount = 1 };
            Assert.True(FeedbackFrameValidation.Valid(frame));
            frame.Rotation = new Quaternion(float.MaxValue, 0, 0, 1); Assert.False(FeedbackFrameValidation.Valid(frame));
            frame.Rotation = Quaternion.identity; frame.Wheel0.Point.x = float.NaN; Assert.False(FeedbackFrameValidation.Valid(frame));
        }
        [Test]
        public void LoadedDrivenSlipIsSpinButAnUnloadedWheelIsSilent()
        {
            var model = new FeedbackNormalizer();
            var frame = new VehicleFeedbackFrame { WheelCount = 1, Speed = 2f, EngineRunning = true };
            frame.Wheel0 = new WheelFeedback { Grounded = true, Driven = true, Load = 3500, LongitudinalSlip = 1, LinearSpeed = 15 };
            for (int i = 0; i < 100; i++) frame = model.Step(frame, 0.02f);
            Assert.Greater(frame.Wheelspin, 0.9f);
            Assert.AreEqual(0, frame.BrakeLock);
            frame.Wheel0.Grounded = false;
            for (int i = 0; i < 100; i++) frame = model.Step(frame, 0.02f);
            Assert.Less(frame.Wheelspin, 0.01f);
        }

        [Test]
        public void ReverseWheelspinIsNotMisclassifiedAsBrakeLock()
        {
            var model = new FeedbackNormalizer();
            var frame = new VehicleFeedbackFrame { WheelCount = 1, Speed = 4, LocalVelocity = Vector3.back * 4 };
            frame.Wheel0 = new WheelFeedback { Grounded = true, Driven = true, Load = 3500, LongitudinalSlip = -1, LinearSpeed = -15 };
            for (int i = 0; i < 100; i++) frame = model.Step(frame, 0.02f);
            Assert.Greater(frame.Wheelspin, 0.9f);
            Assert.Less(frame.BrakeLock, 0.01f);
        }

        [Test]
        public void PauseAndNonFiniteInputsCannotPoisonFilters()
        {
            Assert.AreEqual(0.5f, SensoryMath.Envelope(0.5f, 1, 0, 0.1f, 0.3f));
            Assert.AreEqual(0, SensoryMath.Unit(float.NaN));
            Assert.AreEqual(0, SensoryMath.Unit(float.PositiveInfinity));
            Assert.AreEqual(0.5f, SensoryMath.Envelope(0.5f, 1, float.NaN, 0.1f, 0.3f));
            Assert.AreEqual(0, SensoryMath.Envelope(float.NaN, 1, 0, 0.1f, 0.3f));
        }

        [Test]
        public void LayerBlendIsContinuousEqualPowerAndLoadSensitive()
        {
            var regions = new[] { new EngineRpmRegion { rpm = 1000 }, new EngineRpmRegion { rpm = 5000 } };
            float a = EngineBlend.Weight(regions, 0, 3000, 0.5f, true);
            float b = EngineBlend.Weight(regions, 1, 3000, 0.5f, true);
            Assert.AreEqual(0.5f, a, 0.0001f);
            Assert.AreEqual(1f, 2f * (a * a + b * b), 0.0001f);
            Assert.AreEqual(0, EngineBlend.Weight(regions, 0, 1000, 0, true));
            Assert.AreEqual(1, EngineBlend.Weight(regions, 0, 1000, 0, false));
        }

        [Test]
        public void CollisionUsesNormalSpeedAndImpulseNotRoadSpeed()
        {
            Assert.AreEqual(0, ImpactClassifier.Severity(0, 0, 1500));
            Assert.Greater(ImpactClassifier.Severity(0, 15000, 1500), 0.4f);
            Assert.Greater(ImpactClassifier.Severity(12, 0, 1500), ImpactClassifier.Severity(2, 0, 1500));
        }

        [Test]
        public void VoiceStealingInvalidatesTheOldLease()
        {
            var budget = new FeedbackVoiceBudget(2);
            var low = budget.Acquire(100);
            var other = budget.Acquire(90);
            var important = budget.Acquire(10);
            Assert.False(budget.Owns(low));
            Assert.True(budget.Owns(other));
            Assert.True(budget.Owns(important));
            budget.Release(low);
            Assert.True(budget.Owns(important));
            Assert.False(budget.Acquire(110).IsValid);
        }

        [Test]
        public void MusicalBoundaryAlwaysIncludesSchedulingLead()
        {
            Assert.AreEqual(4, MusicTiming.NextBar(3.95, 0, 120, 4, 0.02), 0.00001);
            Assert.AreEqual(6, MusicTiming.NextBar(3.95, 0, 120, 4, 0.1), 0.00001);
            Assert.AreEqual(4, MusicTiming.NextBar(2, 0, 120, 4, 0.1), 0.00001);
        }

        [Test]
        public void ReturningAStolenVoiceTwiceNeverReleasesTheNewOwner()
        {
            var budget = new FeedbackVoiceBudget(1);
            var old = budget.Acquire(100);
            budget.Release(old);
            var current = budget.Acquire(100);
            budget.Release(old);
            budget.Release(old);
            Assert.True(budget.Owns(current));
            Assert.AreEqual(1, budget.Count);
            budget.Release(current);
            Assert.AreEqual(0, budget.Count);
        }

        [Test]
        public void LandingRequiresAPreviousAirborneSampleAndResetClearsIt()
        {
            var model = new FeedbackNormalizer();
            var frame = new VehicleFeedbackFrame { WheelCount = 1, LocalVelocity = Vector3.down * 8 };
            frame = model.Step(frame, 0.02f);
            frame.Wheel0 = new WheelFeedback { Grounded = true, Load = 4000 };
            frame.LocalVelocity = Vector3.zero;
            frame = model.Step(frame, 0.02f);
            Assert.Greater(frame.Landing, 0);
            model.Reset();
            frame = model.Step(frame, 0.02f);
            Assert.AreEqual(0, frame.Landing);
        }

        [TestCase(0)]
        [TestCase(9)]
        public void InvalidWheelIndexIsNotSilentlyAnActualWheel(int count)
        {
            var frame = new VehicleFeedbackFrame { WheelCount = count };
            Assert.Throws<System.ArgumentOutOfRangeException>(() => frame.GetWheel(8));
        }
    }
}
