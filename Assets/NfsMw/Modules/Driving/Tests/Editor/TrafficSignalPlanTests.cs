using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class TrafficSignalPlanTests
    {
        private GameObject root;
        private RoadTrafficSignals signals;
        private readonly List<TrafficSignalPlan> plans = new List<TrafficSignalPlan>();

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Traffic signal plan tests");
            signals = root.AddComponent<RoadTrafficSignals>();
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            for (int i = 0; i < plans.Count; i++)
                if (plans[i] != null) Object.DestroyImmediate(plans[i]);
            plans.Clear();
        }

        [Test]
        public void NoAuthoredPlanKeepsLegacyFallbackBoundary()
        {
            signals.ConfigurePlans(CreateNetwork(), null);

            Assert.That(signals.TryMovementAspect(7, 101, 0, out RoadSignalAspect aspect), Is.False);
            Assert.That(aspect, Is.EqualTo(RoadSignalAspect.Red));
        }

        [Test]
        public void ConflictingGreensAreRejectedAndIntersectionFailsClosed()
        {
            RoadLaneNetwork network = new RoadLaneNetwork(new[]
            {
                Lane(101, 7, new Vector3(-10, 0, 0), new Vector3(10, 0, 0)),
                Lane(102, 7, new Vector3(0, 0, -10), new Vector3(0, 0, 10))
            });
            TrafficSignalPlan plan = CreatePlan(7, 0, Phase(5, 1, 1, 101, 102));

            Assert.That(plan.Validate(network, out string error), Is.False, error);

            signals.ConfigurePlans(network, new[] { plan });
            Assert.That(signals.TryMovementAspect(7, 101, 0, out RoadSignalAspect aspect), Is.True);
            Assert.That(aspect, Is.EqualTo(RoadSignalAspect.Red));
        }

        [Test]
        public void UnknownMovementIsRejectedDuringPlanValidation()
        {
            RoadLaneNetwork network = CreateNetwork();
            TrafficSignalPlan plan = CreatePlan(7, 0, Phase(5, 1, 1, 999));

            Assert.That(plan.Validate(network, out string error), Is.False, error);

            signals.ConfigurePlans(network, new[] { plan });
            Assert.That(signals.TryMovementAspect(7, 101, 0, out RoadSignalAspect aspect), Is.True);
            Assert.That(aspect, Is.EqualTo(RoadSignalAspect.Red));
        }

        [Test]
        public void MovementBearingPhaseRequiresPositiveProtectedIntervals()
        {
            RoadLaneNetwork network = CreateNetwork();

            AssertInvalid(network, Phase(0, 1, 1, 101));
            AssertInvalid(network, Phase(1, 0, 1, 101));
            AssertInvalid(network, Phase(1, 1, 0, 101));
        }

        [Test]
        public void EmptyAllRedPhaseMayHaveZeroGreenAndYellowWithPositiveDuration()
        {
            RoadLaneNetwork network = CreateNetwork();
            TrafficSignalPlan plan = CreatePlan(7, 0,
                Phase(0, 0, 2),
                Phase(1, 1, 1, 101));

            Assert.That(plan.Validate(network, out string error), Is.True, error);
        }

        [Test]
        public void ProtectedPhaseReportsGreenYellowAndAllRedInOrder()
        {
            RoadLaneNetwork network = CreateNetwork(RoadEntryControl.ProtectedSignal);
            TrafficSignalPlan plan = CreatePlan(7, 0,
                Phase(2, 1, 2, 101),
                Phase(1, 1, 1, 102));
            signals.ConfigurePlans(network, new[] { plan });

            AssertAspect(101, 0, RoadSignalAspect.Green);
            AssertAspect(101, 2, RoadSignalAspect.Yellow);
            AssertAspect(101, 3, RoadSignalAspect.Red);
            AssertAspect(102, 5, RoadSignalAspect.Green);
            AssertAspect(102, 6, RoadSignalAspect.Yellow);
            AssertAspect(102, 7, RoadSignalAspect.Red);
            AssertAspect(101, 8, RoadSignalAspect.Green);
        }

        [Test]
        public void UnlistedMovementIsRedDuringAnotherMovementGreen()
        {
            RoadLaneNetwork network = CreateNetwork(RoadEntryControl.ProtectedSignal);
            TrafficSignalPlan plan = CreatePlan(7, 0, Phase(4, 1, 2, 101));
            signals.ConfigurePlans(network, new[] { plan });

            AssertAspect(101, 0, RoadSignalAspect.Green);
            AssertAspect(102, 0, RoadSignalAspect.Red);
            AssertAspect(102, 4, RoadSignalAspect.Red);
        }

        [Test]
        public void NonFiniteQueryTimeFailsClosed()
        {
            RoadLaneNetwork network = CreateNetwork();
            TrafficSignalPlan plan = CreatePlan(7, 0, Phase(4, 1, 2, 101));
            signals.ConfigurePlans(network, new[] { plan });

            Assert.That(signals.TryMovementAspect(7, 101, float.NaN, out RoadSignalAspect nanAspect), Is.True);
            Assert.That(nanAspect, Is.EqualTo(RoadSignalAspect.Red));
            Assert.That(signals.TryMovementAspect(7, 101, float.PositiveInfinity, out RoadSignalAspect infinityAspect), Is.True);
            Assert.That(infinityAspect, Is.EqualTo(RoadSignalAspect.Red));
        }

        private void AssertAspect(int movementLaneId, float time, RoadSignalAspect expected)
        {
            Assert.That(signals.TryMovementAspect(7, movementLaneId, time, out RoadSignalAspect actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        private TrafficSignalPlan CreatePlan(int intersection, float offset, params TrafficSignalPhase[] phases)
        {
            TrafficSignalPlan plan = ScriptableObject.CreateInstance<TrafficSignalPlan>();
            plan.Configure(intersection, offset, phases);
            plans.Add(plan);
            return plan;
        }

        private static void AssertInvalid(RoadLaneNetwork network, TrafficSignalPhase phase)
        {
            TrafficSignalPlan plan = ScriptableObject.CreateInstance<TrafficSignalPlan>();
            plan.Configure(7, 0, new[] { phase });
            Assert.That(plan.Validate(network, out string error), Is.False, error);
            Object.DestroyImmediate(plan);
        }

        private static TrafficSignalPhase Phase(float green, float yellow, float allRed, params int[] movementLaneIds) =>
            new TrafficSignalPhase
            {
                greenSeconds = green,
                yellowSeconds = yellow,
                allRedSeconds = allRed,
                allowedMovementLaneIds = movementLaneIds
            };

        private static RoadLaneNetwork CreateNetwork(RoadEntryControl entryControl = RoadEntryControl.Signal)
        {
            return new RoadLaneNetwork(new[]
            {
                Lane(101, 7, Vector3.zero, Vector3.forward * 30, entryControl),
                Lane(102, 7, Vector3.right * 3.5f, Vector3.right * 3.5f + Vector3.forward * 30, entryControl)
            });
        }

        private static RoadLaneDefinition Lane(int id, int intersection, Vector3 start, Vector3 end,
            RoadEntryControl entryControl = RoadEntryControl.Signal)
        {
            return new RoadLaneDefinition
            {
                id = id,
                intersection = intersection,
                entryControl = entryControl,
                width = 3.5f,
                speedMetersPerSecond = 10,
                points = new[] { start, end }
            };
        }
    }
}
