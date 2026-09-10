using NUnit.Framework;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleHandlingModelTests
    {
        [Test]
        public void BrakeEdgeInitiatesAndTorqueIsBounded()
        {
            var model = new VehicleHandlingModel();
            var settings = new VehicleHandlingSettings { maximumYawTorque = 100f };
            float torque = model.Step(settings, new VehicleInputState { Steering = 1f, Brake = 0.5f },
                20f, 0f, 0f, true, true, 0.02f);
            Assert.AreEqual(VehicleHandlingMode.Initiating, model.Mode);
            Assert.That(torque, Is.GreaterThan(0f).And.LessThanOrEqualTo(100f));
        }

        [Test]
        public void AirborneCannotApplyTorqueOrBufferBrakeEdge()
        {
            var model = new VehicleHandlingModel();
            var settings = new VehicleHandlingSettings();
            var input = new VehicleInputState { Steering = 1f, Brake = 1f };
            Assert.AreEqual(0f, model.Step(settings, input, 20f, 0f, 0f, false, true, 0.02f));
            Assert.AreEqual(VehicleHandlingMode.Airborne, model.Mode);
            model.Step(settings, input, 20f, 0f, 0f, true, true, 0.02f);
            Assert.AreEqual(VehicleHandlingMode.Grip, model.Mode);
        }

        [Test]
        public void HeldBrakeDoesNotRepeatedlyInitiate()
        {
            var model = new VehicleHandlingModel();
            var settings = new VehicleHandlingSettings();
            var input = new VehicleInputState { Steering = 1f, Brake = 1f };
            for (int i = 0; i < 150; i++) model.Step(settings, input, 20f, 0f, 0f, true, true, 0.02f);
            Assert.AreEqual(VehicleHandlingMode.Grip, model.Mode);
            model.Step(settings, VehicleInputState.Neutral, 20f, 0f, 0f, true, true, 0.02f);
            model.Step(settings, input, 20f, 0f, 0f, true, true, 0.02f);
            Assert.AreEqual(VehicleHandlingMode.Initiating, model.Mode);
        }

        [TestCase(-20f, 1f)]
        [TestCase(2f, 1f)]
        [TestCase(20f, 0f)]
        public void ReverseLowSpeedAndGripTuneRejectDrift(float speed, float bias)
        {
            var model = new VehicleHandlingModel();
            model.Step(new VehicleHandlingSettings { driftBias = bias },
                new VehicleInputState { Steering = 1f, Brake = 1f }, speed, 0f, 0f, true, true, 0.02f);
            Assert.AreEqual(VehicleHandlingMode.Grip, model.Mode);
        }

        [Test]
        public void InvalidInputIsNeutralAndInvalidObservationFailsSafe()
        {
            var input = new VehicleInputState { Steering = float.NaN, Brake = float.PositiveInfinity };
            Assert.AreEqual(0f, input.Clamped().Steering);
            Assert.AreEqual(0f, input.Clamped().Brake);
            var model = new VehicleHandlingModel();
            Assert.AreEqual(0f, model.Step(new VehicleHandlingSettings(), input, float.NaN, 0f, 0f, true, true, 0.02f));
        }
    }
}
