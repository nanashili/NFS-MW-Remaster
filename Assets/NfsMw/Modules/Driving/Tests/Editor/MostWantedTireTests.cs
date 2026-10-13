using NUnit.Framework;
using UnityEngine;
using NfsMwRemaster.Driving;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class MostWantedTireTests
    {
        static MostWantedTireSpecification Spec(float dynamic = 1f, float stat = 1f)
        {
            return new MostWantedTireSpecification(
                new[] { 1f, 1f }, new[] { dynamic, dynamic }, new[] { stat, stat },
                10f, 5f, surfaceLateralGrip: 1f, surfaceDriveGrip: 1f, surfaceRollingResistance: 1f);
        }

        [Test]
        public void ConstructorClampsRadiusAndUsesWheelAxleIndex()
        {
            var tire = new MostWantedTire(0f, 3, Spec());
            Assert.That(tire.Radius, Is.EqualTo(.1f));
            Assert.That(tire.WheelIndex, Is.EqualTo(3));
            Assert.That(tire.AxleIndex, Is.EqualTo(1));
            Assert.That(tire.IsSteeringWheel, Is.False);
        }

        [Test]
        public void BeginFrameClearsTorquesForcesAndRestoresFrameScales()
        {
            var tire = new MostWantedTire(.3f, 0, Spec());
            tire.ApplyDriveTorque(12f); tire.ApplyBrakeTorque(-3f); tire.SetDriftFriction(.4f);
            tire.BeginFrame(.25f, 1.2f, .8f, .1f);
            Assert.That(tire.TotalTorque, Is.Zero);
            Assert.That(tire.LateralForce, Is.Zero);
            Assert.That(tire.LongitudinalForce, Is.Zero);
            Assert.That(tire.ToleratedSlip, Is.EqualTo(.25f));
            tire.UpdateLoaded(0, 10, 10, 4000, .02f);
            Assert.That(tire.Traction, Is.GreaterThan(0));
        }

        [Test]
        public void UnloadedWheelBrakesAndStopsWithoutGroundForce()
        {
            var tire = new MostWantedTire(.3f, 0, Spec());
            tire.AngularVelocity = .1f; tire.SetBrake(1f); tire.BeginFrame(.5f, 1f, 1f, 0);
            tire.UpdateFree(.02f);
            Assert.That(tire.Load, Is.Zero);
            Assert.That(tire.LateralForce, Is.Zero);
            Assert.That(tire.LongitudinalForce, Is.Zero);
            Assert.That(tire.AngularVelocity, Is.EqualTo(0f));
            Assert.That(tire.BrakeLocked, Is.True);
        }

        [Test]
        public void MatchSpeedResetsBrakeAndSlipState()
        {
            var tire = new MostWantedTire(.25f, 1, Spec());
            tire.SetBrake(1); tire.SetEBrake(1); tire.AngularVelocity = -8; tire.BeginFrame(.4f, 1, 1, 0);
            tire.UpdateLoaded(0, 10, 10, 3000, .02f);
            tire.MatchSpeed(7f);
            Assert.That(tire.AngularVelocity, Is.EqualTo(28f));
            Assert.That(tire.RoadSpeed, Is.EqualTo(7f));
            Assert.That(tire.Slip, Is.Zero);
            Assert.That(tire.Brake, Is.Zero); Assert.That(tire.EBrake, Is.Zero);
            Assert.That(tire.BrakeLocked, Is.False);
        }

        [Test]
        public void LoadedStaticCorneringForceIncludesInitialForwardAccelerationCorrection()
        {
            var tire = new MostWantedTire(.3f, 0, Spec());
            tire.BeginFrame(.5f, 1f, 1f, 0f);
            float force = tire.UpdateLoaded(1f, 10f, 10f, 4000f, .02f);
            // Source order first adds the initial forward-acceleration correction
            // (angular acceleration is zero, road speed was zero), then limits the
            // combined vector. This makes the lateral component 1543.3596 N rather
            // than the standalone 4000 N static-grip cap.
            Assert.That(force, Is.EqualTo(-1543.3596f).Within(.001f));
            Assert.That(tire.Traction, Is.LessThan(1f));
            Assert.That(tire.SlipAngle, Is.EqualTo(Mathf.Atan2(1f, 10f) / (Mathf.PI * 2f)).Within(1e-6f));
        }

        [Test]
        public void BrakeLockFactorOnlyChangesLockThreshold()
        {
            var normal = new MostWantedTire(.3f, 0, new MostWantedTireSpecification(new[] { 1f, 1f }, new[] { 1f, 1f }, new[] { 1f, 1f }, 10f, 0f, brakeLockFactor: 1f));
            var reduced = new MostWantedTire(.3f, 0, new MostWantedTireSpecification(new[] { 1f, 1f }, new[] { 1f, 1f }, new[] { 1f, 1f }, 10f, 0f, brakeLockFactor: .1f));
            normal.SetBrake(1); reduced.SetBrake(1); normal.AngularVelocity = reduced.AngularVelocity = .1f;
            normal.BeginFrame(.5f, 1, 1, 0); reduced.BeginFrame(.5f, 1, 1, 0);
            normal.UpdateFree(.02f); reduced.UpdateFree(.02f);
            Assert.That(normal.BrakeLocked, Is.True);
            Assert.That(reduced.BrakeLocked, Is.False);
            // The source lock factor affects admission to the lock state; it does
            // not rewrite the configured service torque value.
            Assert.That(reduced.BrakeTorque, Is.EqualTo(-40f).Within(1e-6f));
        }

        [Test]
        public void DifferentialLockedOrMissingTractionUsesBiasExactly()
        {
            var differential = new MostWantedDifferential { Bias = .3f, Factor = .9f, AngularVelocity = new Vector2(2, 8), HasTraction = new Vector2(1, 1) };
            differential.CalcSplit(true);
            Assert.That(differential.TorqueSplit.x, Is.EqualTo(.3f));
            Assert.That(differential.TorqueSplit.y, Is.EqualTo(.7f));
            differential.HasTraction = new Vector2(0, 1); differential.CalcSplit(false);
            Assert.That(differential.TorqueSplit, Is.EqualTo(new Vector2(.3f, .7f)));
        }

        [Test]
        public void DifferentialOpenSplitMatchesRecoveredFormula()
        {
            var differential = new MostWantedDifferential { Bias = .25f, Factor = .5f, AngularVelocity = new Vector2(4, 8), HasTraction = Vector2.one };
            differential.CalcSplit(false);
            // av0=3, av1=2, combined=5: .125+.2 and .375+.3.
            Assert.That(differential.TorqueSplit.x, Is.EqualTo(.325f).Within(1e-6f));
            Assert.That(differential.TorqueSplit.y, Is.EqualTo(.675f).Within(1e-6f));
        }
    }
}
