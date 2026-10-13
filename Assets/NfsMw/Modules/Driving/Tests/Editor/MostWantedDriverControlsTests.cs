using NUnit.Framework;
using UnityEngine;
using System.Globalization;
using System.IO;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class MostWantedDriverControlsTests
    {
        VehicleTuning tuning;
        GameObject root;
        VehiclePowertrain engine;

        [SetUp]
        public void SetUp()
        {
            tuning = VehicleTuning.CreateStreetRacer();
            tuning.transmissionMode = VehicleTransmissionMode.Manual;
            root = new GameObject("Source driver controls");
            engine = root.AddComponent<VehiclePowertrain>();
            engine.Configure(tuning, System.Array.Empty<VehicleWheel>());
        }

        [TearDown]
        public void TearDown() { Object.DestroyImmediate(root); Object.DestroyImmediate(tuning); }

        [Test]
        public void MatchesRecoveredCppAcross1536DriverInputCombinations()
        {
            string path = Path.Combine(Application.dataPath, "NfsMw/Modules/Driving/Tests/Editor/driver-controls-reference.csv");
            string[] rows = File.ReadAllLines(path);
            Assert.That(rows.Length, Is.EqualTo(1537));
            for (int i = 1; i < rows.Length; i++)
            {
                string[] values = rows[i].Split(',');
                float Read(int n) => float.Parse(values[n], CultureInfo.InvariantCulture);
                var input = new VehicleInputState { Throttle = Read(2), Brake = Read(3), Handbrake = Read(4) != 0f };
                int gear = MostWantedDriverControls.AutoReverseGear((int)Read(0), Read(1), input.Throttle, input.Brake);
                var result = MostWantedDriverControls.Pedals(input, gear, Read(1));
                Assert.That(gear, Is.EqualTo((int)Read(5)), rows[i]);
                Assert.That(result.Throttle, Is.EqualTo(Read(6)).Within(.000001f), rows[i]);
                Assert.That(result.Brake, Is.EqualTo(Read(7)).Within(.000001f), rows[i]);
            }
        }

        [Test]
        public void MatchesRecoveredCppAcross84EngineBrakingSamples()
        {
            tuning.engine.idleRpm = 1000f;
            tuning.engine.redlineRpm = 7000f;
            tuning.mostWanted.torqueTableMaximumRpm = 9000f;
            float[][] tables = { new[] { .25f }, new[] { 1.2f }, new[] { .1f, .5f, .9f }, new[] { -.5f, 1.5f } };
            string path = Path.Combine(Application.dataPath, "NfsMw/Modules/Driving/Tests/Editor/engine-braking-reference.csv");
            string[] rows = File.ReadAllLines(path);
            Assert.That(rows.Length, Is.EqualTo(85));
            for (int i = 1; i < rows.Length; i++)
            {
                string[] values = rows[i].Split(',');
                float Read(int n) => float.Parse(values[n], CultureInfo.InvariantCulture);
                tuning.mostWanted.engineBraking = tables[(int)Read(0)];
                Assert.That(MostWantedVehicleMath.EngineBrakingTorque(tuning, Read(1), Read(2), 0, false, 0f),
                    Is.EqualTo(Read(3)).Within(.0001f), rows[i]);
            }
        }

        // PInput.cpp uses signed speed and strict comparisons, independently of transmission mode.
        [TestCase(1, 2.499f, 0f, .01f, -1)]
        [TestCase(1, 2.5f, 0f, 1f, 1)]
        [TestCase(1, -20f, 0f, 1f, -1)]
        [TestCase(0, 0f, 0f, 1f, -1)]
        [TestCase(1, 0f, .001f, 1f, 1)]
        [TestCase(-1, -5f, 1f, 0f, -1)]
        [TestCase(-1, -4.999f, 1f, 0f, 1)]
        [TestCase(-1, 0f, 0f, 1f, -1)]
        [TestCase(-1, 0f, 0f, 0f, 1)]
        [TestCase(-1, 0f, .1f, .9f, 1)]
        public void AutoReverseMatchesSourceBoundaries(int gear, float speed, float gas, float brake, int expected)
            => Assert.That(MostWantedDriverControls.AutoReverseGear(gear, speed, gas, brake), Is.EqualTo(expected));

        [TestCase(1, -4f, 1f)]
        [TestCase(0, 0f, 1f)]
        [TestCase(1, 4f, .625f)]
        [TestCase(1, 7.999f, .25009375f)]
        [TestCase(1, 8f, 0f)]
        [TestCase(-1, 0f, 1f)]
        [TestCase(-1, -5f, .625f)]
        [TestCase(-1, -10f, .25f)]
        [TestCase(-1, -20f, .25f)]
        public void CoastingBrakeUsesSourceForwardAndReverseCurves(int gear, float speed, float expected)
            => Assert.That(MostWantedDriverControls.Pedals(VehicleInputState.Neutral, gear, speed).Brake,
                Is.EqualTo(expected).Within(.000001f));

        [TestCase(.099f, 0f)]
        [TestCase(.1f, .1f)]
        [TestCase(.5f, .5f)]
        [TestCase(.9f, .9f)]
        [TestCase(.901f, 1f)]
        public void PedalDeadZoneSnapsOnlyOutsideEndpoints(float value, float expected)
            => Assert.That(MostWantedDriverControls.SnapPedal(value, .1f), Is.EqualTo(expected));

        [Test]
        public void ReverseSwapsBothPedalsAndHandbrakeOverridesEvenFullServiceBrake()
        {
            var input = new VehicleInputState { Throttle = .7f, Brake = .3f };
            var result = MostWantedDriverControls.Pedals(input, -1, -8f);
            Assert.That(result.Throttle, Is.EqualTo(.3f));
            Assert.That(result.Brake, Is.EqualTo(.7f));
            input.Handbrake = true;
            Assert.That(MostWantedDriverControls.Pedals(input, -1, -8f).Brake, Is.Zero);
            Assert.That(MostWantedDriverControls.Pedals(new VehicleInputState { Brake = 1f, Handbrake = true }, 1, 20f).Brake, Is.Zero);
        }

        [TestCase(VehicleTransmissionMode.Manual)]
        [TestCase(VehicleTransmissionMode.Automatic)]
        public void ProductionPedalsShiftBeforeRemappingAndSimulationRetainsReverse(VehicleTransmissionMode mode)
        {
            tuning.transmissionMode = mode;
            var result = engine.ApplyDriverPedals(new VehicleInputState { Brake = .6f }, 2f);
            Assert.That(engine.Gear, Is.EqualTo(-1));
            Assert.That(result.Throttle, Is.EqualTo(.6f));
            Assert.That(result.Brake, Is.Zero);
            Assert.That(engine.Simulate(.02f, 2f, result.Throttle, false).Gear, Is.EqualTo(-1));
            result = engine.ApplyDriverPedals(VehicleInputState.Neutral, -4f);
            Assert.That(engine.Gear, Is.EqualTo(1));
            Assert.That(result.Brake, Is.EqualTo(1f));
        }

        [Test]
        public void ManualShiftEventsReachNeutralAndAreNotBlockedByShiftTimer()
        {
            engine.SetManualRequests(false, true);
            Assert.That(engine.Gear, Is.Zero);
            engine.SetManualRequests(true, false);
            engine.SetManualRequests(true, false);
            Assert.That(engine.Gear, Is.EqualTo(2));
            engine.ApplyDriverPedals(new VehicleInputState { Brake = 1f }, 0f);
            engine.SetManualRequests(true, false);
            Assert.That(engine.Gear, Is.EqualTo(-1));
        }

        [Test]
        public void AutomaticSportShiftAcceptsForwardEventButRejectsNeutralAndBusyShifts()
        {
            tuning.transmissionMode = VehicleTransmissionMode.Automatic;
            engine.SetManualRequests(false, true);
            Assert.That(engine.Gear, Is.EqualTo(1));
            engine.SetManualRequests(true, false);
            Assert.That(engine.Gear, Is.EqualTo(2));
            engine.SetManualRequests(true, false);
            Assert.That(engine.Gear, Is.EqualTo(2));
        }

        [TestCase(1, true, 10f, -100f)]
        [TestCase(1, true, -10f, 100f)]
        [TestCase(-1, true, -10f, -100f)]
        [TestCase(-1, true, 10f, 100f)]
        [TestCase(0, true, -10f, -100f)]
        [TestCase(1, false, -10f, -100f)]
        [TestCase(1, true, 0f, -100f)]
        public void EngineBrakingOpposesAxleOnlyWithEngagedClutchAndSelectedGear(int gear, bool engaged, float axle, float expected)
        {
            tuning.mostWanted.engineBraking = new[] { .25f };
            Assert.That(MostWantedVehicleMath.EngineBrakingTorque(tuning, 400f, 3000f, gear, engaged, axle), Is.EqualTo(expected));
        }

        [Test]
        public void SportShiftRejectsUpshiftWhenTransmissionHasDownshiftPotential()
        {
            engine.SetManualRequests(true, false);
            for (int i = 0; i < 40; i++) engine.Simulate(.02f, 0f, 0f, false);
            tuning.transmissionMode = VehicleTransmissionMode.Automatic;
            engine.SetManualRequests(true, false);
            Assert.That(engine.Gear, Is.EqualTo(2));
            engine.SetManualRequests(false, true);
            Assert.That(engine.Gear, Is.EqualTo(1));
        }

        [Test]
        public void ShiftTimerScalesWithSelectedRatioAndQuarterForDownshift()
        {
            tuning.engine.shiftDuration = .2f;
            tuning.engine.gearRatios = new[] { 3f, 2f };
            engine.SetManualRequests(true, false);
            engine.Simulate(.39f, 0f, 0f, false);
            Assert.That(engine.IsShifting, Is.True);
            engine.Simulate(.02f, 0f, 0f, false);
            Assert.That(engine.IsShifting, Is.False);
            engine.SetManualRequests(false, true);
            engine.Simulate(.14f, 0f, 0f, false);
            Assert.That(engine.IsShifting, Is.True);
            engine.Simulate(.02f, 0f, 0f, false);
            Assert.That(engine.IsShifting, Is.False);
        }

        [Test]
        public void ProductionEngineUsesCorrectedBrakingTorqueAfterClutchEngages()
        {
            var body = root.AddComponent<Rigidbody>();
            var wheelObject = new GameObject("Driven wheel");
            wheelObject.transform.SetParent(root.transform);
            var wheel = wheelObject.AddComponent<VehicleWheel>();
            wheel.Setup(VehicleAxle.Rear, false, true, true, null);
            wheel.Configure(body, tuning);
            tuning.engine.forcedInduction = false;
            tuning.mostWanted.engineBraking = new[] { .25f };
            engine.Configure(tuning, new[] { wheel });
            engine.SetManualRequests(true, false);
            wheel.SetInitialForwardSpeed(-10f);
            VehiclePowertrainOutput output = default;
            for (int i = 0; i < 20; i++) output = engine.Simulate(.02f, -10f, 0f, false);
            Assert.That(output.EngineTorque, Is.GreaterThan(0f), "Engaged forward gear must oppose backwards axle rotation.");
            wheel.SetInitialForwardSpeed(10f);
            output = engine.Simulate(.02f, 10f, 0f, false);
            Assert.That(output.EngineTorque, Is.LessThan(0f));
            output = engine.Simulate(.02f, 10f, 1f, false);
            Assert.That(output.EngineTorque, Is.GreaterThan(0f), "Full throttle removes the braking contribution.");
        }

        [Test]
        public void BrakingUsesMaxRpmDomainRedlineClampAndSingleSampleException()
        {
            tuning.engine.idleRpm = 1000f;
            tuning.engine.redlineRpm = 7000f;
            tuning.mostWanted.torqueTableMaximumRpm = 9000f;
            tuning.mostWanted.engineBraking = new[] { .1f, .5f, .9f };
            Assert.That(MostWantedVehicleMath.EngineBrakingFraction(tuning, 0f), Is.EqualTo(.1f));
            Assert.That(MostWantedVehicleMath.EngineBrakingFraction(tuning, 3000f), Is.EqualTo(.3f).Within(.000001f));
            Assert.That(MostWantedVehicleMath.EngineBrakingFraction(tuning, 20000f), Is.EqualTo(.7f).Within(.000001f));
            tuning.mostWanted.engineBraking = new[] { 1.2f };
            Assert.That(MostWantedVehicleMath.EngineBrakingTorque(tuning, 100f, 3000f, 1, true, 10f), Is.EqualTo(-120f).Within(.00001f));
            tuning.mostWanted.engineBraking = new[] { 1.2f, 1.2f };
            Assert.That(MostWantedVehicleMath.EngineBrakingFraction(tuning, 3000f), Is.EqualTo(1f));
        }
    }
}
