using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleMathTests
    {
        [Test]
        public void InputStateClampsDeviceValues()
        {
            VehicleInputState state = new VehicleInputState
            {
                Steering = 2f,
                Throttle = -1f,
                Brake = 4f
            }.Clamped();

            Assert.That(state.Steering, Is.EqualTo(1f));
            Assert.That(state.Throttle, Is.EqualTo(0f));
            Assert.That(state.Brake, Is.EqualTo(1f));
        }
    }
}
