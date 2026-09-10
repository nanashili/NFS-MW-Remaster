using System;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class TrafficSimulationTests
    {
        [Test]
        public void FreshCutInAppliesImmediateSafetyBrakeAndKeepsTelemetryFinite()
        {
            var driver = new TrafficDriver(new TrafficDriverProfile());
            float speed = 10;
            for (int i = 0; i < 50; i++) speed = driver.Step(0.02f, speed, 12, float.PositiveInfinity, 0, false);

            float next = driver.Step(0.02f, speed, 12, 2, 0, false);

            Assert.That(next, Is.LessThan(speed - 0.1f));
            Assert.That(driver.SafetyAcceleration, Is.LessThan(0));
            Assert.That(driver.Braking, Is.True);
            AssertFinite(next);
            AssertFinite(driver.DesiredAcceleration);
            AssertFinite(driver.SafetyAcceleration);
            AssertFinite(driver.FinalAcceleration);
            AssertFinite(driver.DesiredGap);
            Assert.That(float.IsNaN(driver.TimeToCollision), Is.False);
        }

        [Test]
        public void DriverSanitizesNonFiniteObservations()
        {
            var driver = new TrafficDriver(new TrafficDriverProfile());

            float next = driver.Step(0.02f, float.NaN, float.PositiveInfinity, float.NaN, float.PositiveInfinity, false);

            AssertFinite(next);
            AssertFinite(driver.DesiredAcceleration);
            AssertFinite(driver.SafetyAcceleration);
            AssertFinite(driver.FinalAcceleration);
            AssertFinite(driver.DesiredGap);
            Assert.That(float.IsNaN(driver.TimeToCollision), Is.False);
        }

        [Test]
        public void SimulationUsesBoundedFixedTimesteps()
        {
            var simulation = new TrafficSimulation(CreateStraightNetwork(), 1);
            var registry = new TrafficSpatialRegistry(1);
            var environment = new TestTrafficEnvironment();

            Assert.DoesNotThrow(() => simulation.Step(0, registry, environment));
            Assert.DoesNotThrow(() => simulation.Step(-0.02f, registry, environment));
            Assert.DoesNotThrow(() => simulation.Step(float.NaN, registry, environment));
            Assert.DoesNotThrow(() => simulation.Step(float.PositiveInfinity, registry, environment));
            Assert.That(simulation.Statistics.Elapsed, Is.EqualTo(0));

            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Step(0.1001f, registry, environment));
            Assert.That(simulation.Statistics.Elapsed, Is.EqualTo(0));
        }

        [Test]
        public void SeededIdentityAndRouteAreRepeatable()
        {
            const int seed = 271;
            var firstNetwork = CreateBranchNetwork();
            var secondNetwork = CreateBranchNetwork();
            int[] firstRoute = new int[8];
            int[] secondRoute = new int[8];

            Assert.That(firstNetwork.TryRoute(0, 3, seed, firstRoute, out int firstCount), Is.True);
            Assert.That(secondNetwork.TryRoute(0, 3, seed, secondRoute, out int secondCount), Is.True);
            Assert.That(firstCount, Is.EqualTo(secondCount));
            for (int i = 0; i < firstCount; i++) Assert.That(firstRoute[i], Is.EqualTo(secondRoute[i]));

            var firstSimulation = new TrafficSimulation(firstNetwork, 2);
            var secondSimulation = new TrafficSimulation(secondNetwork, 2);
            Assert.That(firstSimulation.TrySpawn(0, 3, seed, out int firstSlot), Is.True);
            Assert.That(secondSimulation.TrySpawn(0, 3, seed, out int secondSlot), Is.True);

            TrafficAgentState first = firstSimulation[firstSlot];
            TrafficAgentState second = secondSimulation[secondSlot];
            TrafficVehicleSnapshot firstSnapshot = firstSimulation.Snapshot(firstSlot);
            TrafficVehicleSnapshot secondSnapshot = secondSimulation.Snapshot(secondSlot);
            Assert.That(first.Id, Is.EqualTo(second.Id));
            Assert.That(first.Lane, Is.EqualTo(second.Lane));
            Assert.That(first.Destination, Is.EqualTo(second.Destination));
            Assert.That(firstSnapshot.AgentSlot, Is.EqualTo(firstSlot));
            Assert.That(secondSnapshot.AgentSlot, Is.EqualTo(secondSlot));
            Assert.That(firstSnapshot.Id, Is.EqualTo(first.Id));
            Assert.That(secondSnapshot.Id, Is.EqualTo(second.Id));
            Assert.That(first.Profile.archetype, Is.EqualTo(second.Profile.archetype));
            Assert.That(first.Profile.speedMultiplier, Is.EqualTo(second.Profile.speedMultiplier));
            Assert.That(first.Profile.lateralPreference, Is.EqualTo(second.Profile.lateralPreference));

            int secondAgentSlot = SpawnWhenEntranceClears(firstSimulation, 0, 3, seed + 1, new TestTrafficEnvironment());
            Assert.That(firstSimulation[secondAgentSlot].Id, Is.Not.EqualTo(first.Id));
        }

        [Test]
        public void PhysicalSynchronizationUpdatesSnapshotAndPreservesAgentSlot()
        {
            var simulation = new TrafficSimulation(CreateStraightNetwork(), 1);
            Assert.That(simulation.TrySpawn(0, 1, 271, out int slot), Is.True);

            Vector3 position = Vector3.forward * 40;
            Vector3 velocity = Vector3.forward * 6;
            simulation.SynchronizePhysical(slot, position, velocity, Vector3.forward, 2.2f, 0.95f, 6, 16);

            TrafficAgentState state = simulation[slot];
            TrafficVehicleSnapshot snapshot = simulation.Snapshot(slot);
            Assert.That(state.Position, Is.EqualTo(position));
            Assert.That(state.Speed, Is.EqualTo(6).Within(0.001f));
            Assert.That(state.HalfLength, Is.EqualTo(2.2f).Within(0.001f));
            Assert.That(state.HalfWidth, Is.EqualTo(0.95f).Within(0.001f));
            Assert.That(snapshot.AgentSlot, Is.EqualTo(slot));
            Assert.That(snapshot.Position, Is.EqualTo(position));
        }

        [Test]
        public void SynchronizePhysicalRejectsInvalidObservationsWithoutMutation()
        {
            var simulation = new TrafficSimulation(CreateStraightNetwork(), 1);
            Assert.That(simulation.TrySpawn(0, 1, 271, out int slot), Is.True);

            Vector3 position = Vector3.forward * 40;
            Vector3 velocity = Vector3.forward * 6;
            simulation.SynchronizePhysical(slot, position, velocity, Vector3.forward, 2.2f, 0.95f, 6, 16);
            TrafficVehicleSnapshot baseline = simulation.Snapshot(slot);
            float baselineSpeed = simulation[slot].Speed;

            Action[] invalidObservations = {
                () => simulation.SynchronizePhysical(slot, new Vector3(0, float.NaN, 40), velocity, Vector3.forward, 2.2f, 0.95f, 6, 16),
                () => simulation.SynchronizePhysical(slot, position, new Vector3(float.PositiveInfinity, 0, 6), Vector3.forward, 2.2f, 0.95f, 6, 16),
                () => simulation.SynchronizePhysical(slot, position, velocity, new Vector3(float.NaN, 0, 1), 2.2f, 0.95f, 6, 16),
                () => simulation.SynchronizePhysical(slot, position, velocity, Vector3.zero, 2.2f, 0.95f, 6, 16),
                () => simulation.SynchronizePhysical(slot, position, velocity, Vector3.forward, float.NaN, 0.95f, 6, 16),
                () => simulation.SynchronizePhysical(slot, position, velocity, Vector3.forward, 2.2f, 0, 6, 16),
                () => simulation.SynchronizePhysical(slot, position, velocity, Vector3.forward, 2.2f, 0.95f, float.NaN, 16),
                () => simulation.SynchronizePhysical(slot, position, velocity, Vector3.forward, 2.2f, 0.95f, 6, float.PositiveInfinity)
            };

            foreach (Action invalidObservation in invalidObservations)
            {
                Assert.Throws<ArgumentException>(() => invalidObservation());
                TrafficVehicleSnapshot current = simulation.Snapshot(slot);
                Assert.That(current.Position, Is.EqualTo(baseline.Position));
                Assert.That(current.Velocity, Is.EqualTo(baseline.Velocity));
                Assert.That(current.Forward, Is.EqualTo(baseline.Forward));
                Assert.That(current.HalfLength, Is.EqualTo(baseline.HalfLength).Within(0.001f));
                Assert.That(current.HalfWidth, Is.EqualTo(baseline.HalfWidth).Within(0.001f));
                Assert.That(simulation[slot].Speed, Is.EqualTo(baselineSpeed).Within(0.001f));
            }
        }

        [Test]
        public void LaneGraphRejectsUnknownSuccessorIds()
        {
            RoadLaneDefinition[] definitions = {
                Lane(10, Vector3.zero, Vector3.forward * 100, 99)
            };

            Assert.Throws<ArgumentException>(() => new RoadLaneNetwork(definitions));
        }

        [Test]
        public void LaneGraphRejectsDisconnectedSuccessorGeometry()
        {
            RoadLaneDefinition[] definitions = {
                Lane(10, Vector3.zero, Vector3.forward * 100, 20),
                Lane(20, Vector3.forward * 102, Vector3.forward * 202)
            };

            Assert.Throws<ArgumentException>(() => new RoadLaneNetwork(definitions));
        }

        [Test]
        public void LaneGraphAcceptsSameDirectionAdjacentSharedBoundary()
        {
            RoadLaneDefinition first = Lane(10, Vector3.zero, Vector3.forward * 100);
            RoadLaneDefinition second = Lane(20, Vector3.right * 3.5f, Vector3.right * 3.5f + Vector3.forward * 100);
            first.rightLane = second.id;
            second.leftLane = first.id;

            var network = new RoadLaneNetwork(new[] { first, second });

            Assert.That(network[0].Right, Is.EqualTo(1));
            Assert.That(network[1].Left, Is.EqualTo(0));
        }

        [Test]
        public void LaneGraphRejectsNonAdjacentNeighborGeometry()
        {
            RoadLaneDefinition first = Lane(10, Vector3.zero, Vector3.forward * 100);
            RoadLaneDefinition second = Lane(20, Vector3.right * 8, Vector3.right * 8 + Vector3.forward * 100);
            first.rightLane = second.id;
            second.leftLane = first.id;

            Assert.Throws<ArgumentException>(() => new RoadLaneNetwork(new[] { first, second }));
        }

        [Test]
        public void LaneGraphBakesPredecessorsAndConflictCorridors()
        {
            RoadLaneDefinition predecessor = Lane(10, Vector3.zero, Vector3.forward * 100, 20);
            RoadLaneDefinition successor = Lane(20, Vector3.forward * 100, Vector3.forward * 200);
            RoadLaneDefinition horizontal = Lane(30, new Vector3(-20, 0, 100), new Vector3(20, 0, 100));
            RoadLaneDefinition crossing = Lane(40, new Vector3(0, 0, 80), new Vector3(0, 0, 120));
            horizontal.intersection = 7;
            crossing.intersection = 7;

            var network = new RoadLaneNetwork(new[] { predecessor, successor, horizontal, crossing });

            Assert.That(network[1].Predecessors.Count, Is.EqualTo(1));
            Assert.That(network[1].Predecessors[0], Is.EqualTo(0));
            Assert.That(network.MovementsConflict(2, 3), Is.True);
            Assert.That(network.MovementsConflict(3, 2), Is.True);
            Assert.That(network[2].ConflictingMovements, Does.Contain(3));
            Assert.That(network[3].ConflictingMovements, Does.Contain(2));
        }

        [Test]
        public void RoutesRejectInvalidCompactLaneIds()
        {
            var network = CreateStraightNetwork();
            int[] route = new int[4];

            Assert.That(network.TryRoute(-1, 1, 0, route, out int count), Is.False);
            Assert.That(count, Is.Zero);
            Assert.That(network.TryRoute(0, network.Count, 0, route, out count), Is.False);
            Assert.That(count, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => network.SetClosed(network.Count, true));

            var simulation = new TrafficSimulation(network, 1);
            Assert.That(simulation.TrySpawn(-1, 1, 0, out _), Is.False);
            Assert.That(simulation.TrySpawn(0, network.Count, 0, out _), Is.False);
        }

        [Test]
        public void ClosedLaneIsExcludedFromAnAlternateRoute()
        {
            var network = CreateBranchNetwork();
            int[] route = new int[8];

            Assert.That(network.TryRoute(0, 3, 17, route, out int count), Is.True);
            Assert.That(count, Is.EqualTo(3));
            int openMiddle = route[1];
            Assert.That(new[] { 1, 2 }, Does.Contain(openMiddle));
            int alternateMiddle = openMiddle == 1 ? 2 : 1;

            int revision = network.Revision;
            network.SetClosed(openMiddle, true);

            Assert.That(network.IsClosed(openMiddle), Is.True);
            Assert.That(network.Revision, Is.EqualTo(revision + 1));
            Assert.That(network.TryRoute(0, 3, 17, route, out count), Is.True);
            Assert.That(count, Is.EqualTo(3));
            Assert.That(route[0], Is.EqualTo(0));
            Assert.That(route[1], Is.EqualTo(alternateMiddle));
            Assert.That(route[2], Is.EqualTo(3));
            Assert.That(network.TryRoute(0, openMiddle, 17, route, out count), Is.False);
            Assert.That(count, Is.Zero);
        }

        [Test]
        public void ClosedStartLaneIsExcludedFromRoute()
        {
            var network = CreateStraightNetwork();
            int[] route = new int[4];
            network.SetClosed(0, true);

            Assert.That(network.TryRoute(0, 1, 0, route, out int count), Is.False);
            Assert.That(count, Is.Zero);
        }

        [Test]
        public void ClosureLeasesOnlyReleaseTheirOwnBlockage()
        {
            var network = CreateStraightNetwork();
            using (IDisposable first = network.AcquireClosure(0, 30))
            using (IDisposable second = network.AcquireClosure(0, 70))
            {
                Assert.That(network.IsClosed(0), Is.True);
                Assert.That(network.DistanceToBlockage(0, 0), Is.EqualTo(30).Within(0.001f));

                first.Dispose();

                Assert.That(network.IsClosed(0), Is.True);
                Assert.That(network.DistanceToBlockage(0, 0), Is.EqualTo(70).Within(0.001f));

                second.Dispose();
                Assert.That(network.IsClosed(0), Is.False);
                Assert.That(network.DistanceToBlockage(0, 0), Is.EqualTo(float.PositiveInfinity));
            }
        }

        [Test]
        public void OccupiedEntranceRejectsSecondTrip()
        {
            var simulation = new TrafficSimulation(CreateStraightNetwork(), 2);
            Assert.That(simulation.TrySpawn(0, 1, 400, out int firstSlot), Is.True);

            Assert.That(simulation.TrySpawn(0, 1, 401, out int rejectedSlot), Is.False);
            Assert.That(rejectedSlot, Is.EqualTo(-1));
            Assert.That(simulation[firstSlot].Active, Is.True);
            Assert.That(simulation[1].Active, Is.False);
        }

        [Test]
        public void FastRearVehicleBlocksMandatoryLaneChange()
        {
            var driver = new TrafficDriverProfile();
            var follower = new TrafficDriverProfile();
            var observation = new TrafficLaneChangeObservation {
                Available = true,
                Mandatory = true,
                Speed = 12,
                DesiredSpeed = 14,
                CurrentAcceleration = 0,
                FrontGap = 30,
                FrontSpeed = 14,
                RearGap = 4,
                RearSpeed = 20,
                RearDesiredSpeed = 14,
                RearBeforeAcceleration = 0
            };

            TrafficLaneChangeDecision decision = TrafficLaneChangePlanner.Evaluate(observation, driver, follower);

            Assert.That(decision.Accepted, Is.False);
            Assert.That(decision.Reason, Is.EqualTo(TrafficLaneChangeReason.UnsafeRear));
            Assert.That(decision.RearTimeToCollision, Is.LessThan(3.5f));
        }

        [Test]
        public void FasterRearRacerDoesNotForceCivilianBraking()
        {
            var simulation = new TrafficSimulation(CreateStraightNetwork(), 1);
            Assert.That(simulation.TrySpawn(0, 1, 271, out int slot), Is.True);

            simulation.SynchronizePhysical(slot, Vector3.forward * 50, Vector3.forward * 5, Vector3.forward,
                2.05f, 0.9f, 4, 12);
            var registry = new TrafficSpatialRegistry(2);
            Assert.That(registry.Add(simulation.Snapshot(slot)), Is.True);
            Assert.That(registry.Add(Snapshot(900, TrafficActorKind.Racer, Vector3.forward * 40, Vector3.forward * 20,
                Vector3.forward)), Is.True);

            simulation.Step(0.02f, registry, new TestTrafficEnvironment());

            Assert.That(simulation[slot].Driver.Braking, Is.False);
            Assert.That(simulation[slot].Driver.FinalAcceleration, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void OccludedPoliceSirenDoesNotInduceCivilianYielding()
        {
            var simulation = new TrafficSimulation(CreateStraightNetwork(), 1);
            Assert.That(simulation.TrySpawn(0, 1, 271, out int slot), Is.True);

            simulation.SynchronizePhysical(slot, Vector3.forward * 50, Vector3.forward * 5, Vector3.forward,
                2.05f, 0.9f, 4, 12);
            var registry = new TrafficSpatialRegistry(2);
            Assert.That(registry.Add(simulation.Snapshot(slot)), Is.True);
            Assert.That(registry.Add(Snapshot(901, TrafficActorKind.Police, Vector3.forward * 40, Vector3.zero,
                Vector3.forward, true)), Is.True);
            var environment = new OccludedTrafficEnvironment();

            simulation.Step(0.02f, registry, environment);

            Assert.That(environment.PerceptionChecks, Is.GreaterThan(0));
            Assert.That(simulation[slot].Yielding, Is.False);
        }

        [Test]
        public void RedSignalBlocksSignalControlledIntersection()
        {
            TrafficIntersectionDecision decision = TrafficIntersectionRules.Evaluate(
                RoadEntryControl.Signal, RoadSignalAspect.Red, 12, 100, 0, 0, float.PositiveInfinity, true, false,
                new TrafficDriverProfile());

            Assert.That(decision, Is.EqualTo(TrafficIntersectionDecision.Signal));
        }

        [Test]
        public void YellowSignalStopsWhenThereIsEnoughStoppingDistance()
        {
            TrafficIntersectionDecision decision = TrafficIntersectionRules.Evaluate(
                RoadEntryControl.Signal, RoadSignalAspect.Yellow, 10, 30, 0, 0, float.PositiveInfinity, true, false,
                new TrafficDriverProfile());

            Assert.That(decision, Is.EqualTo(TrafficIntersectionDecision.YellowStop));
        }

        [Test]
        public void StopSignRequiresTheInitialDwell()
        {
            TrafficIntersectionDecision decision = TrafficIntersectionRules.Evaluate(
                RoadEntryControl.Stop, RoadSignalAspect.Green, 0, 0, 0, 0, float.PositiveInfinity, true, false,
                new TrafficDriverProfile());

            Assert.That(decision, Is.EqualTo(TrafficIntersectionDecision.StopSign));
        }

        [Test]
        public void StopSignReleasesAfterTheRequiredDwell()
        {
            TrafficIntersectionDecision decision = TrafficIntersectionRules.Evaluate(
                RoadEntryControl.Stop, RoadSignalAspect.Green, 0, 0, 2, 0, float.PositiveInfinity, true, false,
                new TrafficDriverProfile());

            Assert.That(decision, Is.EqualTo(TrafficIntersectionDecision.Proceed));
        }

        [Test]
        public void BlockedExitPreventsIntersectionEntry()
        {
            TrafficIntersectionDecision decision = TrafficIntersectionRules.Evaluate(
                RoadEntryControl.Signal, RoadSignalAspect.Green, 0, 20, 0, 0, float.PositiveInfinity, false, false,
                new TrafficDriverProfile());

            Assert.That(decision, Is.EqualTo(TrafficIntersectionDecision.ExitBlocked));
        }

        [Test]
        public void SpatialRegistryReportsCapacityAndResultOverflow()
        {
            var registry = new TrafficSpatialRegistry(2);
            Assert.That(registry.Add(Snapshot(1, Vector3.zero)), Is.True);
            Assert.That(registry.Add(Snapshot(2, Vector3.forward * 8)), Is.True);
            Assert.That(registry.Add(Snapshot(3, Vector3.forward * 16)), Is.False);
            Assert.That(registry.Count, Is.EqualTo(2));
            Assert.That(registry.Saturated, Is.True);

            int[] results = new int[2];
            int count = registry.Query(Vector3.zero, 32, results, out bool truncated);
            Assert.That(count, Is.EqualTo(2));
            Assert.That(truncated, Is.True);

            registry.BeginFrame();
            registry.Add(Snapshot(1, Vector3.zero));
            registry.Add(Snapshot(2, Vector3.forward * 8));
            int[] oneResult = new int[1];
            count = registry.Query(Vector3.zero, 32, oneResult, out truncated);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(truncated, Is.True);
        }

        [Test]
        public void InvalidRegistryObservationsAreRejectedAndSaturateQueries()
        {
            TrafficVehicleSnapshot invalidPosition = Snapshot(1, Vector3.zero);
            invalidPosition.Position = new Vector3(float.NaN, 0, 0);
            TrafficVehicleSnapshot invalidVelocity = Snapshot(2, Vector3.zero);
            invalidVelocity.Velocity = new Vector3(0, float.PositiveInfinity, 0);
            TrafficVehicleSnapshot invalidForward = Snapshot(3, Vector3.zero);
            invalidForward.Forward = new Vector3(float.NaN, 0, 1);
            TrafficVehicleSnapshot invalidLength = Snapshot(4, Vector3.zero);
            invalidLength.HalfLength = float.NaN;
            TrafficVehicleSnapshot invalidWidth = Snapshot(5, Vector3.zero);
            invalidWidth.HalfWidth = -1;
            TrafficVehicleSnapshot[] invalidObservations = {
                invalidPosition, invalidVelocity, invalidForward, invalidLength, invalidWidth
            };
            var registry = new TrafficSpatialRegistry(2);

            foreach (TrafficVehicleSnapshot invalidObservation in invalidObservations)
            {
                registry.BeginFrame();
                Assert.That(registry.Add(invalidObservation), Is.False);
                Assert.That(registry.Count, Is.Zero);
                Assert.That(registry.Saturated, Is.True);
            }

            registry.BeginFrame();
            Assert.That(registry.Add(invalidPosition), Is.False);
            Assert.That(registry.Add(Snapshot(6, Vector3.forward * 8)), Is.True);
            int count = registry.Query(Vector3.zero, 32, new int[2], out bool truncated);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(truncated, Is.True);
        }

        [Test]
        public void StraightLaneQueueNeverOverlapsMicroscopically()
        {
            var simulation = new TrafficSimulation(CreateStraightNetwork(), 3);
            var environment = new TestTrafficEnvironment();
            Assert.That(simulation.TrySpawn(0, 1, 400, out _), Is.True);

            AssertNoMicroscopicOverlap(simulation);
            int secondSlot = SpawnWhenEntranceClears(simulation, 0, 1, 401, environment);
            Assert.That(simulation[secondSlot].Active, Is.True);
            AssertNoMicroscopicOverlap(simulation);
            int thirdSlot = SpawnWhenEntranceClears(simulation, 0, 1, 402, environment);
            Assert.That(simulation[thirdSlot].Active, Is.True);
            AssertNoMicroscopicOverlap(simulation);
            for (int i = 0; i < 120; i++)
            {
                simulation.Step(0.02f, RegistryFor(simulation), environment);
                AssertNoMicroscopicOverlap(simulation);
            }
        }

        [Test]
        public void PhysicalIncursionAdvancesActualRouteWithoutInventingPermissionAndBlocksConflicts()
        {
            var north = Lane(20, Vector3.back * 10, Vector3.forward * 10, 30);
            var east = Lane(50, Vector3.left * 10, Vector3.right * 10, 60);
            north.intersection = east.intersection = 0;
            north.entryControl = east.entryControl = RoadEntryControl.ProtectedSignal;
            var roads = new RoadLaneNetwork(new[] {
                Lane(10, Vector3.back * 100, Vector3.back * 10, 20), north,
                Lane(30, Vector3.forward * 10, Vector3.forward * 100),
                Lane(40, Vector3.left * 100, Vector3.left * 10, 50), east,
                Lane(60, Vector3.right * 10, Vector3.right * 100)
            });
            var simulation = new TrafficSimulation(roads, 2);
            Assert.That(simulation.TrySpawn(0, 2, 11, out int pushed), Is.True);
            Assert.That(simulation.TrySpawn(3, 5, 12, out int waiting), Is.True);
            simulation.SynchronizePhysical(pushed, Vector3.zero, Vector3.forward, Vector3.forward, 2, 0.9f, 2, 9);
            simulation.SynchronizePhysical(waiting, Vector3.left * 15, Vector3.zero, Vector3.right, 2, 0.9f, 2, 9);
            simulation.Step(0.02f, RegistryFor(simulation), new TestTrafficEnvironment());

            Assert.That(simulation[pushed].Lane, Is.EqualTo(1));
            Assert.That(simulation[pushed].CommittedMovement, Is.EqualTo(-1));
            Assert.That(simulation[pushed].Along, Is.GreaterThanOrEqualTo(10));
            Assert.That(simulation.Statistics.PhysicalIncursions, Is.EqualTo(1));
            Assert.That(simulation[waiting].IntersectionDecision, Is.EqualTo(TrafficIntersectionDecision.Conflict));

            // Rear of an unreserved car still occupies the connector after its centre exits.
            simulation.SynchronizePhysical(pushed, Vector3.forward * 11, Vector3.forward, Vector3.forward, 2, 0.9f, 2, 9);
            simulation.Step(0.02f, RegistryFor(simulation), new TestTrafficEnvironment());
            Assert.That(simulation[waiting].IntersectionDecision, Is.EqualTo(TrafficIntersectionDecision.Conflict));
            simulation.SynchronizePhysical(pushed, Vector3.forward * 25, Vector3.forward, Vector3.forward, 2, 0.9f, 2, 9);
            simulation.Step(0.02f, RegistryFor(simulation), new TestTrafficEnvironment());
            simulation.Step(0.02f, RegistryFor(simulation), new TestTrafficEnvironment());
            Assert.That(simulation[waiting].IntersectionDecision, Is.EqualTo(TrafficIntersectionDecision.Proceed));
            Assert.That(simulation.Statistics.PhysicalIncursions, Is.EqualTo(1));
        }

        [Test]
        public void SideBySideVehicleBlocksMandatoryMerge()
        {
            var first = Lane(10, Vector3.zero, Vector3.forward * 200, 30);
            var ending = Lane(20, Vector3.right * 3.5f, Vector3.right * 3.5f + Vector3.forward * 200);
            first.rightLane = 20; ending.leftLane = 10;
            var roads = new RoadLaneNetwork(new[] { first, ending, Lane(30, Vector3.forward * 200, Vector3.forward * 400) });
            var simulation = new TrafficSimulation(roads, 2);
            Assert.That(simulation.TrySpawn(0, 2, 11, out int through), Is.True);
            simulation.SynchronizePhysical(through, Vector3.forward * 50, Vector3.zero, Vector3.forward, 2, 0.9f, 2, 9);
            Assert.That(simulation.TrySpawn(1, 2, 12, out int merge), Is.True);
            simulation.SynchronizePhysical(merge, Vector3.right * 3.5f + Vector3.forward * 50, Vector3.zero, Vector3.forward, 2, 0.9f, 2, 9);
            simulation.Step(0.02f, RegistryFor(simulation), new TestTrafficEnvironment());

            Assert.That(simulation[merge].LaneDecision.Accepted, Is.False);
            Assert.That(simulation[merge].LaneDecision.Reason, Is.EqualTo(TrafficLaneChangeReason.UnsafeRear));
        }

        private static RoadLaneNetwork CreateStraightNetwork()
        {
            return new RoadLaneNetwork(new[] {
                Lane(10, Vector3.zero, Vector3.forward * 200, 20),
                Lane(20, Vector3.forward * 200, Vector3.forward * 400)
            });
        }

        private static RoadLaneNetwork CreateBranchNetwork()
        {
            Vector3 junction = Vector3.forward * 100;
            Vector3 merge = Vector3.forward * 200;
            return new RoadLaneNetwork(new[] {
                Lane(100, Vector3.zero, junction, 200, 300),
                Lane(200, new[] { junction, junction + Vector3.right * 40, merge }, 400),
                Lane(300, new[] { junction, junction + Vector3.left * 40, merge }, 400),
                Lane(400, merge, Vector3.forward * 300)
            });
        }

        private static RoadLaneDefinition Lane(int id, Vector3 start, Vector3 end, params int[] successors)
        {
            return Lane(id, new[] { start, end }, successors);
        }

        private static RoadLaneDefinition Lane(int id, Vector3[] points, params int[] successors)
        {
            return new RoadLaneDefinition {
                id = id,
                width = 3.5f,
                speedMetersPerSecond = 13.9f,
                points = points,
                successors = successors ?? Array.Empty<int>()
            };
        }

        private static TrafficVehicleSnapshot Snapshot(int id, Vector3 position)
        {
            return Snapshot(id, TrafficActorKind.Civilian, position, Vector3.zero, Vector3.forward);
        }

        private static TrafficVehicleSnapshot Snapshot(int id, TrafficActorKind kind, Vector3 position, Vector3 velocity,
            Vector3 forward, bool siren = false)
        {
            return new TrafficVehicleSnapshot {
                Id = id,
                Kind = kind,
                Position = position,
                Forward = forward,
                Velocity = velocity,
                HalfLength = 2.05f,
                HalfWidth = 0.9f,
                Siren = siren
            };
        }

        private static TrafficSpatialRegistry RegistryFor(TrafficSimulation simulation)
        {
            var registry = new TrafficSpatialRegistry(simulation.Capacity);
            for (int i = 0; i < simulation.Capacity; i++)
                if (simulation[i].Active) registry.Add(simulation.Snapshot(i));
            return registry;
        }

        private static int SpawnWhenEntranceClears(TrafficSimulation simulation, int origin, int destination, int seed,
            ITrafficEnvironment environment)
        {
            for (int i = 0; i < 600; i++)
            {
                if (simulation.TrySpawn(origin, destination, seed, out int slot)) return slot;
                simulation.Step(0.02f, RegistryFor(simulation), environment);
            }

            Assert.Fail($"Entrance did not clear for seed {seed} within the bounded test horizon.");
            return -1;
        }

        private static void AssertNoMicroscopicOverlap(TrafficSimulation simulation)
        {
            for (int i = 0; i < simulation.Capacity; i++)
            {
                TrafficAgentState first = simulation[i];
                if (!first.Active) continue;
                for (int j = i + 1; j < simulation.Capacity; j++)
                {
                    TrafficAgentState second = simulation[j];
                    if (!second.Active) continue;
                    float minimum = first.HalfLength + second.HalfLength;
                    Assert.That(Vector3.Distance(first.Position, second.Position) + 0.001f,
                        Is.GreaterThanOrEqualTo(minimum), $"Traffic agents {first.Id} and {second.Id} overlap.");
                }
            }
        }

        private static void AssertFinite(float value)
        {
            Assert.That(float.IsNaN(value), Is.False);
            Assert.That(float.IsInfinity(value), Is.False);
        }

        private sealed class TestTrafficEnvironment : ITrafficEnvironment
        {
            public RoadSignalAspect Signal(int intersection, Vector3 approach, float simulationTime) => RoadSignalAspect.Green;
            public bool CanPerceive(TrafficVehicleSnapshot observer, TrafficVehicleSnapshot target) => true;
        }

        private sealed class OccludedTrafficEnvironment : ITrafficEnvironment
        {
            public int PerceptionChecks { get; private set; }
            public RoadSignalAspect Signal(int intersection, Vector3 approach, float simulationTime) => RoadSignalAspect.Green;
            public bool CanPerceive(TrafficVehicleSnapshot observer, TrafficVehicleSnapshot target)
            {
                PerceptionChecks++;
                return false;
            }
        }
    }
}
