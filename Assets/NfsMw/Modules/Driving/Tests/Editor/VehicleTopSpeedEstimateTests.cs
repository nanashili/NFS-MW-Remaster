using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleTopSpeedEstimateTests
    {
        private VehicleTuning tuning;
        [SetUp] public void SetUp() => tuning = VehicleTuning.CreateStreetRacer();
        [TearDown] public void TearDown() => Object.DestroyImmediate(tuning);

        [Test] public void EstimateIgnoresConfiguredTargetWithoutGovernor()
        {
            tuning.chassis.maxSpeedKph = 120f;
            float first = VehicleTopSpeedEstimator.Estimate(tuning).estimatedKph;
            tuning.chassis.maxSpeedKph = 300f;
            Assert.That(VehicleTopSpeedEstimator.Estimate(tuning).estimatedKph, Is.EqualTo(first).Within(.01f));
        }

        [Test] public void GovernorClampsEstimateAndGearingCeilingIsReported()
        {
            tuning.speedGovernor = true; tuning.chassis.maxSpeedKph = 140f;
            var result = VehicleTopSpeedEstimator.Estimate(tuning);
            Assert.That(result.estimatedKph, Is.LessThanOrEqualTo(140.01f));
            Assert.That(result.gearingCeilingKph, Is.GreaterThan(0f));
        }

        [Test] public void MorePowerRaisesEstimateAndMoreDragLowersIt()
        {
            tuning.engine.gearRatios[tuning.engine.gearRatios.Length - 1] = .60f;
            float baseline = VehicleTopSpeedEstimator.Estimate(tuning).estimatedKph;
            tuning.engine.maxTorqueNewtonMeters *= 1.35f;
            float power = VehicleTopSpeedEstimator.Estimate(tuning).estimatedKph;
            tuning.engine.maxTorqueNewtonMeters /= 1.35f;
            tuning.aero.dragCoefficient *= 2f;
            float drag = VehicleTopSpeedEstimator.Estimate(tuning).estimatedKph;
            Assert.That(power, Is.GreaterThan(baseline));
            Assert.That(drag, Is.LessThan(baseline));
        }

        [Test] public void GearingCeilingUsesSmallestEffectiveRatio()
        {
            tuning.engine.gearRatios = new[] { 3.10f, 0.50f, 0.80f };
            tuning.engine.finalDrive = 3f;
            var result = VehicleTopSpeedEstimator.Estimate(tuning);
            float expected = tuning.engine.redlineRpm / (0.50f * 3f)
                * (2f * Mathf.PI * tuning.tires.wheelRadius) * 0.06f;
            Assert.That(result.gearingCeilingKph, Is.EqualTo(expected).Within(.001f));
        }

        [Test] public void ClutchEngagementReducesAvailableTopSpeed()
        {
            float engaged = VehicleTopSpeedEstimator.Estimate(tuning).estimatedKph;
            tuning.engine.clutchEngagement = 0f;
            Assert.That(VehicleTopSpeedEstimator.Estimate(tuning).estimatedKph, Is.LessThan(engaged));
        }

        [Test] public void EstimateDoesNotMutateTuning()
        {
            string before = JsonUtility.ToJson(tuning);
            VehicleTopSpeedEstimator.Estimate(tuning);
            Assert.That(JsonUtility.ToJson(tuning), Is.EqualTo(before));
        }
    }
}
