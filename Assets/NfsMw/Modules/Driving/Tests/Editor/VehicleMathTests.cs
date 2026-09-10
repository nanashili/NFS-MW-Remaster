using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleMathTests
    {
        [Test]
        public void EngineTorquePeaksNearConfiguredPeakRpm()
        {
            float low = VehicleMath.EvaluateEngineTorque(1500f, 900f, 7200f, 4200f, 390f);
            float peak = VehicleMath.EvaluateEngineTorque(4200f, 900f, 7200f, 4200f, 390f);
            float redline = VehicleMath.EvaluateEngineTorque(7200f, 900f, 7200f, 4200f, 390f);

            Assert.That(peak, Is.GreaterThan(low));
            Assert.That(peak, Is.GreaterThan(redline));
            Assert.That(peak, Is.EqualTo(390f).Within(0.01f));
        }

        [Test]
        public void TireForceChangesSignAndFallsAfterPeak()
        {
            float positivePeak = VehicleMath.EvaluateTireForce(0.105f, 0.105f, 1f, 0.78f);
            float positiveSlide = VehicleMath.EvaluateTireForce(0.50f, 0.105f, 1f, 0.78f);
            float negativePeak = VehicleMath.EvaluateTireForce(-0.105f, 0.105f, 1f, 0.78f);

            Assert.That(positivePeak, Is.EqualTo(1f).Within(0.001f));
            Assert.That(positiveSlide, Is.LessThan(positivePeak));
            Assert.That(negativePeak, Is.EqualTo(-positivePeak).Within(0.001f));
        }

        [Test]
        public void FrictionCircleNeverExceedsEitherAxisLimit()
        {
            Vector2 limited = VehicleMath.ApplyFrictionCircle(new Vector2(10f, 10f), 4f, 3f);

            Assert.That(Mathf.Abs(limited.x), Is.LessThanOrEqualTo(4f + 0.001f));
            Assert.That(Mathf.Abs(limited.y), Is.LessThanOrEqualTo(3f + 0.001f));
            Assert.That((limited.x * limited.x) / 16f + (limited.y * limited.y) / 9f, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void SteeringSensitivityReducesAtSpeed()
        {
            VehicleTuning.ControlSettings settings = new VehicleTuning.ControlSettings
            {
                steeringExponent = 1f,
                highSpeedSteerScale = 0.4f,
                highSpeedSteerKph = 180f
            };

            float lowSpeed = VehicleMath.ShapeSteering(1f, 0f, settings);
            float highSpeed = VehicleMath.ShapeSteering(1f, 180f, settings);

            Assert.That(lowSpeed, Is.EqualTo(1f).Within(0.001f));
            Assert.That(highSpeed, Is.EqualTo(0.4f).Within(0.001f));
        }

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
