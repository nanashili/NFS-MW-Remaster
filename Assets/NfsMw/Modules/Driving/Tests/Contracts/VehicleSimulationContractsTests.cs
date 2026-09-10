using NUnit.Framework;

namespace NfsMwRemaster.Driving.Contracts.Tests
{
    public sealed class VehicleSimulationContractsTests
    {
        [Test]
        public void NeutralInputHasNoDriverIntent()
        {
            VehicleInputState input = VehicleInputState.Neutral;

            Assert.That(input.Steering, Is.EqualTo(0f));
            Assert.That(input.Throttle, Is.EqualTo(0f));
            Assert.That(input.Brake, Is.EqualTo(0f));
            Assert.That(input.Handbrake, Is.False);
            Assert.That(input.Nitrous, Is.False);
        }

        [Test]
        public void ClampedInputSanitizesRangesAndNonFiniteValues()
        {
            VehicleInputState input = new VehicleInputState
            {
                Steering = float.PositiveInfinity,
                Throttle = 2f,
                Brake = -1f,
                Handbrake = true,
                Nitrous = true
            };

            VehicleInputState result = input.Clamped();

            Assert.That(result.Steering, Is.EqualTo(0f));
            Assert.That(result.Throttle, Is.EqualTo(1f));
            Assert.That(result.Brake, Is.EqualTo(0f));
            Assert.That(result.Handbrake, Is.True);
            Assert.That(result.Nitrous, Is.True);
        }

        [Test]
        public void TelemetryGearLabelUsesStableContractValues()
        {
            Assert.That(new VehicleTelemetry { Gear = -1 }.GearLabel, Is.EqualTo("R"));
            Assert.That(new VehicleTelemetry { Gear = 0 }.GearLabel, Is.EqualTo("N"));
            Assert.That(new VehicleTelemetry { Gear = 3 }.GearLabel, Is.EqualTo("3"));
        }
    }
}
