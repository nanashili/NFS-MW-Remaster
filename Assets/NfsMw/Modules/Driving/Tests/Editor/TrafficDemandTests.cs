using System;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class TrafficDemandTests
    {
        [Test]
        public void DemandConservesGeneratedRequestsAcrossAdmissionAndSuppression()
        {
            var reservoir = new TrafficDemandReservoir(new[] { 3600f, 1800f }, 2015, 2);

            reservoir.Advance(60, new[] { 100f, 100f });
            AssertConservation(reservoir);
            Assert.That(reservoir.Generated, Is.GreaterThan(0));
            Assert.That(reservoir.Pending, Is.EqualTo(4));
            Assert.That(reservoir.SuppressedAtCapacity, Is.GreaterThan(0));

            while (reservoir.TryPeek(0, out _)) reservoir.CommitAdmission(0);
            while (reservoir.TryPeek(1, out _)) reservoir.CommitAdmission(1);

            Assert.That(reservoir.Pending, Is.Zero);
            Assert.That(reservoir.Admitted, Is.EqualTo(4));
            AssertConservation(reservoir);
        }

        [Test]
        public void DemandOverflowIsCountedExplicitly()
        {
            var reservoir = new TrafficDemandReservoir(new[] { 3600f }, 17, 2);

            reservoir.Advance(60, new[] { 100f });

            Assert.That(reservoir.Generated, Is.GreaterThan(reservoir.Pending));
            Assert.That(reservoir.PendingForFlow(0), Is.EqualTo(2));
            Assert.That(reservoir.SuppressedAtCapacity, Is.GreaterThan(0));
            Assert.That(reservoir.TryPeek(0, out _), Is.True);
            AssertConservation(reservoir);
        }

        [Test]
        public void QueuedSeedRemainsStableWhileAdmissionIsDeferred()
        {
            var reservoir = new TrafficDemandReservoir(new[] { 3600f }, 23, 1);

            reservoir.Advance(60, new[] { 100f });
            Assert.That(reservoir.TryPeek(0, out int queuedSeed), Is.True);
            long suppressedBeforeDeferral = reservoir.SuppressedAtCapacity;

            reservoir.Advance(60, new[] { 100f });

            Assert.That(reservoir.TryPeek(0, out int deferredSeed), Is.True);
            Assert.That(deferredSeed, Is.EqualTo(queuedSeed));
            Assert.That(reservoir.SuppressedAtCapacity, Is.GreaterThan(suppressedBeforeDeferral));
            AssertConservation(reservoir);

            reservoir.CommitAdmission(0);
            Assert.That(reservoir.Pending, Is.Zero);
            Assert.That(reservoir.Admitted, Is.EqualTo(1));
            AssertConservation(reservoir);
        }

        [Test]
        public void SameDemandSeedAndRatesProduceTheSameRequestStream()
        {
            var left = new TrafficDemandReservoir(new[] { 3600f, 1800f }, 91, 64);
            var right = new TrafficDemandReservoir(new[] { 3600f, 1800f }, 91, 64);

            left.Advance(10, new[] { 2f, 1f });
            right.Advance(10, new[] { 2f, 1f });
            AssertSameReservoirState(left, right);

            left.Advance(7.5f, new[] { 0.5f, 3f });
            right.Advance(7.5f, new[] { 0.5f, 3f });
            AssertSameReservoirState(left, right);

            for (int flow = 0; flow < left.FlowCount; flow++)
            {
                while (left.TryPeek(flow, out int leftSeed))
                {
                    Assert.That(right.TryPeek(flow, out int rightSeed), Is.True);
                    Assert.That(rightSeed, Is.EqualTo(leftSeed));
                    left.CommitAdmission(flow);
                    right.CommitAdmission(flow);
                }
            }

            AssertSameReservoirState(left, right);
        }

        [Test]
        public void PausedDemandRateProducesNoArrivals()
        {
            var reservoir = new TrafficDemandReservoir(new[] { 3600f }, 101, 8);

            reservoir.Advance(60, new[] { 0f });
            reservoir.Advance(60, new[] { 0f });

            Assert.That(reservoir.Generated, Is.Zero);
            Assert.That(reservoir.Admitted, Is.Zero);
            Assert.That(reservoir.Pending, Is.Zero);
            Assert.That(reservoir.SuppressedAtCapacity, Is.Zero);
            AssertConservation(reservoir);
        }

        [Test]
        public void DemandAndPopulationRejectInvalidInputs()
        {
            Assert.Throws<ArgumentException>(() => new TrafficDemandReservoir(null, 1));
            Assert.Throws<ArgumentException>(() => new TrafficDemandReservoir(new[] { float.NaN }, 1));
            Assert.Throws<ArgumentException>(() => new TrafficDemandReservoir(new[] { -1f }, 1));
            Assert.Throws<ArgumentException>(() => new TrafficDemandReservoir(new[] { 3600.1f }, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TrafficDemandReservoir(new[] { 1f }, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TrafficDemandReservoir(new[] { 1f }, 1, 257));

            var reservoir = new TrafficDemandReservoir(new[] { 1f }, 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => reservoir.Advance(float.NaN, new[] { 1f }));
            Assert.Throws<ArgumentOutOfRangeException>(() => reservoir.Advance(-0.01f, new[] { 1f }));
            Assert.Throws<ArgumentOutOfRangeException>(() => reservoir.Advance(60.01f, new[] { 1f }));
            Assert.Throws<ArgumentException>(() => reservoir.Advance(1, null));
            Assert.Throws<ArgumentException>(() => reservoir.Advance(1, Array.Empty<float>()));
            Assert.Throws<ArgumentException>(() => reservoir.Advance(1, new[] { float.NaN }));
            Assert.Throws<InvalidOperationException>(() => reservoir.CommitAdmission(0));

            TrafficDriverPopulationProfile population = ScriptableObject.CreateInstance<TrafficDriverPopulationProfile>();
            try
            {
                Assert.Throws<ArgumentNullException>(() => population.SampleInto(1, null));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(population);
            }
        }

        [Test]
        public void WorldProfileAppliesDistrictHourAndCategoryMix()
        {
            var network = new RoadLaneNetwork(new[] {
                new RoadLaneDefinition {
                    id = 10,
                    district = 7,
                    roadClass = RoadClass.Arterial,
                    points = new[] { Vector3.zero, Vector3.forward * 100 }
                }
            });
            TrafficWorldProfile profile = ScriptableObject.CreateInstance<TrafficWorldProfile>();
            try
            {
                float[] hourly = new float[24];
                for (int i = 0; i < hourly.Length; i++) hourly[i] = 1;
                hourly[6] = 0.5f;
                profile.demandScale = 2;
                profile.roadClassDemand = new[] { 1f, 1.5f, 1f, 1f, 1f };
                profile.startingHour = 6;
                profile.gameHoursPerRealHour = 1;
                profile.districts = new[] {
                    new TrafficDistrictDemand {
                        district = 7,
                        multiplier = 2,
                        hourlyMultipliers = hourly,
                        vehicleWeights = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f }
                    }
                };

                Assert.That(profile.DemandMultiplier(network[0], 0), Is.EqualTo(3).Within(0.001f));
                Assert.That(profile.DemandMultiplier(network[0], 3600), Is.EqualTo(6).Within(0.001f));
                Assert.That(profile.VehicleCategory(7, 2015), Is.EqualTo(TrafficVehicleCategory.Commercial));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void PopulationSamplingDoesNotMutateSharedMedian()
        {
            TrafficDriverPopulationProfile population = ScriptableObject.CreateInstance<TrafficDriverPopulationProfile>();
            var median = new TrafficDriverProfile();
            median.ApplyTemperament(0.5f, 0.12f);
            population.distributions = new[] {
                new TrafficDriverDistribution { weight = 1, correlatedVariation = 0.2f, median = median }
            };
            var before = new TrafficDriverProfile();
            before.CopyFrom(median);
            var destination = new TrafficDriverProfile();
            try
            {
                for (int seed = 0; seed < 128; seed++) population.SampleInto(seed, destination);

                AssertProfilesEqual(before, median);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(population);
            }
        }

        [Test]
        public void AdjacentSeedsProduceVariedDriverArchetypes()
        {
            var seen = new bool[4];
            for (int seed = 2015; seed < 2047; seed++)
                seen[(int)TrafficDriverProfile.FromSeed(seed).archetype] = true;

            int distinct = 0;
            for (int i = 0; i < seen.Length; i++) if (seen[i]) distinct++;
            Assert.That(distinct, Is.GreaterThan(1));
        }

        [Test]
        public void RepeatedSamplingAndAdvanceAreAllocationFreeAfterWarmup()
        {
            TrafficDriverPopulationProfile population = CreatePopulationProfile();
            try
            {
                var destination = new TrafficDriverProfile();
                var seeded = new TrafficDriverProfile();
                var reservoir = new TrafficDemandReservoir(new[] { 3600f }, 2015, 256);
                float[] multipliers = { 1 };
                for (int i = 0; i < 64; i++)
                {
                    population.SampleInto(i, destination);
                    seeded.ApplySeed(i);
                    reservoir.Advance(0.02f, multipliers);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 512; i++)
                {
                    population.SampleInto(i + 64, destination);
                    seeded.ApplySeed(i + 64);
                    reservoir.Advance(0.02f, multipliers);
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(allocated, Is.EqualTo(0), "Repeated demand/profile operations allocated managed memory.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(population);
            }
        }

        private static TrafficDriverPopulationProfile CreatePopulationProfile()
        {
            var population = ScriptableObject.CreateInstance<TrafficDriverPopulationProfile>();
            population.distributions = new[] {
                new TrafficDriverDistribution { weight = 1, correlatedVariation = 0.08f, median = new TrafficDriverProfile() }
            };
            return population;
        }

        private static void AssertConservation(TrafficDemandReservoir reservoir)
        {
            Assert.That(reservoir.Generated,
                Is.EqualTo(reservoir.Admitted + reservoir.Pending + reservoir.SuppressedAtCapacity));
        }

        private static void AssertSameReservoirState(TrafficDemandReservoir left, TrafficDemandReservoir right)
        {
            Assert.That(right.FlowCount, Is.EqualTo(left.FlowCount));
            Assert.That(right.Generated, Is.EqualTo(left.Generated));
            Assert.That(right.Admitted, Is.EqualTo(left.Admitted));
            Assert.That(right.Pending, Is.EqualTo(left.Pending));
            Assert.That(right.SuppressedAtCapacity, Is.EqualTo(left.SuppressedAtCapacity));
            Assert.That(right.ProcessingBacklogRows, Is.EqualTo(left.ProcessingBacklogRows));
            for (int flow = 0; flow < left.FlowCount; flow++)
            {
                Assert.That(right.PendingForFlow(flow), Is.EqualTo(left.PendingForFlow(flow)));
                bool leftHas = left.TryPeek(flow, out int leftSeed);
                bool rightHas = right.TryPeek(flow, out int rightSeed);
                Assert.That(rightHas, Is.EqualTo(leftHas));
                if (leftHas) Assert.That(rightSeed, Is.EqualTo(leftSeed));
            }
        }

        private static void AssertProfilesEqual(TrafficDriverProfile expected, TrafficDriverProfile actual)
        {
            Assert.That(actual.archetype, Is.EqualTo(expected.archetype));
            Assert.That(actual.speedMultiplier, Is.EqualTo(expected.speedMultiplier).Within(0.000001f));
            Assert.That(actual.followingSeconds, Is.EqualTo(expected.followingSeconds).Within(0.000001f));
            Assert.That(actual.acceleration, Is.EqualTo(expected.acceleration).Within(0.000001f));
            Assert.That(actual.comfortableBraking, Is.EqualTo(expected.comfortableBraking).Within(0.000001f));
            Assert.That(actual.standstillGap, Is.EqualTo(expected.standstillGap).Within(0.000001f));
            Assert.That(actual.jerk, Is.EqualTo(expected.jerk).Within(0.000001f));
            Assert.That(actual.reactionSeconds, Is.EqualTo(expected.reactionSeconds).Within(0.000001f));
            Assert.That(actual.emergencyBraking, Is.EqualTo(expected.emergencyBraking).Within(0.000001f));
            Assert.That(actual.politeness, Is.EqualTo(expected.politeness).Within(0.000001f));
            Assert.That(actual.intersectionGap, Is.EqualTo(expected.intersectionGap).Within(0.000001f));
            Assert.That(actual.patienceSeconds, Is.EqualTo(expected.patienceSeconds).Within(0.000001f));
            Assert.That(actual.lateralPreference, Is.EqualTo(expected.lateralPreference).Within(0.000001f));
            Assert.That(actual.actuatorSeconds, Is.EqualTo(expected.actuatorSeconds).Within(0.000001f));
        }
    }
}
