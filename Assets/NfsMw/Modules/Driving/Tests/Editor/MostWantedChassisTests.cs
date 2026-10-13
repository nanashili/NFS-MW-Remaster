using NUnit.Framework;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class MostWantedChassisTests
    {
        [Test]
        public void SteeringRangeUsesRecoveredSpeedTable()
        {
            Assert.That(MostWantedChassis.BaseRangeDegrees(0f), Is.EqualTo(40f).Within(.0001f));
            Assert.That(MostWantedChassis.BaseRangeDegrees(160f / 9f), Is.EqualTo(20f).Within(.0001f));
            Assert.That(MostWantedChassis.BaseRangeDegrees(160f), Is.EqualTo(2.9f).Within(.0001f));
        }

        [Test]
        public void AckermannKeepsInsideWheelAtRequestedAngle()
        {
            MostWantedChassis.Ackermann(30f, 1.3f, -1.3f, 1.5f, out float left, out float right);
            Assert.That(right, Is.EqualTo(30f).Within(.0001f));
            Assert.That(left, Is.LessThan(right));
        }

        [Test]
        public void YawFrictionBoostIsCappedAtSourceBonus()
        {
            Assert.That(MostWantedChassis.YawFrictionBoost(0f, 1f, 30f, 1f, 0f), Is.EqualTo(1f).Within(.0001f));
            Assert.That(MostWantedChassis.YawFrictionBoost(20f * UnityEngine.Mathf.Deg2Rad, 0f, 30f, 1f, 0f), Is.EqualTo(1f + 20f * UnityEngine.Mathf.Deg2Rad).Within(.0001f));
        }

        [Test]
        public void ChassisGripAndTractionUseSourceSpeedTables()
        {
            Assert.That(MostWantedChassis.ComputeMaxSlip(0f, 1), Is.EqualTo(.5f).Within(.0001f));
            Assert.That(MostWantedChassis.ComputeMaxSlip(0f, -1), Is.EqualTo(71f).Within(.0001f));
            Assert.That(MostWantedChassis.ComputeLateralGripScale(0f, false), Is.EqualTo(.9996f).Within(.001f));
            Assert.That(MostWantedChassis.ComputeTractionScale(0f, 1, false), Is.EqualTo(.9999f).Within(.001f));
            Assert.That(MostWantedChassis.ComputeLateralGripScale(0f, true), Is.EqualTo(3f).Within(.0001f));
        }
    }
}
