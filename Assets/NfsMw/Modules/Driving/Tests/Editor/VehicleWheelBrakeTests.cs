using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleWheelBrakeTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void PositiveAngularVelocityStopsAtZeroWithoutReversal()
        {
            float result = VehicleMath.IntegrateBrakedWheel(
                4f,
                0f,
                20f,
                2f,
                0.5f);

            Assert.That(result, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(result, Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void NegativeAngularVelocityStopsAtZeroWithoutReversal()
        {
            float result = VehicleMath.IntegrateBrakedWheel(
                -4f,
                0f,
                20f,
                2f,
                0.5f);

            Assert.That(result, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(result, Is.LessThanOrEqualTo(0f));
        }

        [Test]
        public void StandstillHoldsWhenExternalTorqueIsBelowBrakeTorque()
        {
            float result = VehicleMath.IntegrateBrakedWheel(
                0f,
                1f,
                2f,
                1f,
                0.5f);

            Assert.That(result, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void ExternalDriveOvercomesBrakeAndKeepsItsSign()
        {
            float forward = VehicleMath.IntegrateBrakedWheel(
                0f,
                10f,
                3f,
                1f,
                0.1f);
            float reverse = VehicleMath.IntegrateBrakedWheel(
                0f,
                -10f,
                3f,
                1f,
                0.1f);

            Assert.That(forward, Is.EqualTo(0.7f).Within(Tolerance));
            Assert.That(forward, Is.GreaterThan(0f));
            Assert.That(reverse, Is.EqualTo(-0.7f).Within(Tolerance));
            Assert.That(reverse, Is.LessThan(0f));
        }

        [Test]
        public void NoBrakeRecoversFreeMomentum()
        {
            float result = VehicleMath.IntegrateBrakedWheel(
                3f,
                -4f,
                0f,
                2f,
                0.5f);

            Assert.That(result, Is.EqualTo(2f).Within(Tolerance));
        }

        [Test]
        public void BrakeCannotIncreaseAbsoluteVelocityBeyondFreeMomentum()
        {
            float positive = VehicleMath.IntegrateBrakedWheel(
                1f,
                1f,
                1f,
                1f,
                0.5f);
            float negative = VehicleMath.IntegrateBrakedWheel(
                -1f,
                -1f,
                1f,
                1f,
                0.5f);

            Assert.That(Mathf.Abs(positive), Is.LessThanOrEqualTo(1.5f + Tolerance));
            Assert.That(Mathf.Abs(negative), Is.LessThanOrEqualTo(1.5f + Tolerance));
        }
    }
}
