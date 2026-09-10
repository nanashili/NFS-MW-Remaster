using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class MostWantedDrivingTests
    {
        private readonly List<Object> owned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in owned.AsEnumerable().Reverse()) if (item != null) Object.DestroyImmediate(item);
            owned.Clear();
        }

        private VehicleTuning Reference()
        {
            var tuning = Own(VehicleTuning.CreateStreetRacer());
            tuning.simulationModel = VehicleSimulationModel.MostWantedReference;
            tuning.driveLayout = VehicleDriveLayout.Rwd;
            tuning.engine.idleRpm = 800; tuning.engine.redlineRpm = 8500;
            tuning.engine.maxTorqueNewtonMeters = 467f * 1.3558f;
            tuning.engine.gearRatios = new[] { 4.1f, 2.53f, 1.67f, 1.23f, 1f, .83f };
            tuning.engine.finalDrive = 3.4f;
            tuning.mostWanted = new MostWantedDrivingSettings {
                torqueTableMaximumRpm = 9500f,
                normalizedTorque = new[] { 170f, 251f, 340f, 428f, 467f, 452f, 411f, 375f, 350f }.Select(value => value / 467f).ToArray(),
                engineBraking = new[] { .7f, .8f, .9f }, gearEfficiency = Enumerable.Repeat(1f, 6).ToArray(),
                nitrousTorqueBoost = .75f, nitrousDisengageSeconds = 2f,
                nitrousRechargeMinimumKph = 50f * MostWantedVehicleMath.ReferenceMphToKph,
                nitrousRechargeMaximumKph = 100f * MostWantedVehicleMath.ReferenceMphToKph,
                nitrousRechargeMinimumSeconds = 50f, nitrousRechargeMaximumSeconds = 30f,
                linearDownforceCoefficient = 600f
            };
            tuning.engine.nitrousFuelSeconds = 2.5f;
            return tuning;
        }

        [TestCase(800f, 170f)]
        [TestCase(1887.5f, 251f)]
        [TestCase(5150f, 467f)]
        public void TorqueKnotsUseIdleToMaximumAxis(float rpm, float footPounds)
        {
            var tuning = Reference();
            Assert.That(VehicleMath.EvaluateEngineTorque(tuning, rpm), Is.EqualTo(footPounds * 1.3558f).Within(.001f));
        }

        [Test]
        public void TorqueClampsInputAtRedlineWithoutMovingLastKnotThere()
        {
            var tuning = Reference();
            float expected = (375f - 25f * 700f / 8700f) * 1.3558f;
            Assert.That(MostWantedVehicleMath.Torque(tuning, 8500), Is.EqualTo(expected).Within(.002f));
            Assert.That(MostWantedVehicleMath.Torque(tuning, 9500), Is.EqualTo(expected).Within(.002f));
            Assert.That(MostWantedVehicleMath.Torque(tuning, 0), Is.EqualTo(170f * 1.3558f).Within(.002f));
        }

        [Test]
        public void ReferenceDoesNotMultiplyLegacyEnvelopeOrAnimationCurve()
        {
            var tuning = Reference(); tuning.engine.torqueCurve = AnimationCurve.Constant(0, 1, 0);
            Assert.That(VehicleMath.EvaluateEngineTorque(tuning, 5150), Is.EqualTo(467f * 1.3558f).Within(.001f));
            tuning.simulationModel = VehicleSimulationModel.Authored;
            Assert.That(VehicleMath.EvaluateEngineTorque(tuning, 5150), Is.EqualTo(0));
        }

        [Test]
        public void AuthoredTorqueOverloadPreservesPreviousTwoCurveCalculation()
        {
            var tuning = Own(VehicleTuning.CreateStreetRacer()); var e = tuning.engine;
            foreach (float rpm in new[] { 900f, 2000f, 4200f, 6000f, 7200f })
            {
                float expected = VehicleMath.EvaluateEngineTorque(rpm, e.idleRpm, e.redlineRpm, e.peakTorqueRpm, e.maxTorqueNewtonMeters)
                    * e.torqueCurve.Evaluate(Mathf.InverseLerp(e.idleRpm, e.redlineRpm, rpm));
                Assert.That(VehicleMath.EvaluateEngineTorque(tuning, rpm), Is.EqualTo(expected));
            }
        }

        [Test]
        public void UnitConversionsMatchRecoveredConventions()
        {
            Assert.That(MostWantedVehicleMath.WheelRadius(19f, 255f, 40f), Is.EqualTo(.3433f).Within(.000001f));
            Assert.That(MostWantedVehicleMath.LoadedEngineInertia(10f), Is.EqualTo(.5f));
            Assert.That(650f * MostWantedVehicleMath.PoundsPerInchToNewtonsPerMeter, Is.EqualTo(113832.42f).Within(.02f));
        }

        [Test]
        public void NitrousMultipliesTorqueIncludingPartialThrottleEngineBrakeBlend()
        {
            var tuning = Reference();
            foreach (float throttle in new[] { .25f, .6f, 1f })
            {
                float normal = MostWantedVehicleMath.NetEngineTorque(tuning, 5150, throttle, 0f, false);
                float boosted = MostWantedVehicleMath.NetEngineTorque(tuning, 5150, throttle, 0f, true);
                Assert.That(boosted, Is.EqualTo(normal * 1.75f).Within(.001f));
            }
            Assert.That(MostWantedVehicleMath.NetEngineTorque(tuning, 5150, 0f, 0f, false), Is.LessThan(0));
        }

        [Test]
        public void InductionUsesRpmThresholdBoostFractionsAndSpoolState()
        {
            var tuning = Reference(); tuning.engine.forcedInduction = true;
            var m = tuning.mostWanted;
            m.inductionSpoolRpmFraction = .2f; m.inductionLowBoost = .1f; m.inductionHighBoost = .3f; m.inductionVacuum = -.05f;
            float threshold = 800f + (8500f - 800f) * .2f;
            Assert.That(MostWantedVehicleMath.InductionThresholdRpm(tuning), Is.EqualTo(threshold));
            Assert.That(MostWantedVehicleMath.InductionBoost(tuning, threshold, 1), Is.EqualTo(.1f).Within(.000001f));
            Assert.That(MostWantedVehicleMath.InductionBoost(tuning, 8500, .5f), Is.EqualTo(.15f).Within(.000001f));
            Assert.That(MostWantedVehicleMath.InductionBoost(tuning, (800 + threshold) / 2, 1), Is.EqualTo(-.025f).Within(.000001f));
            Assert.That(MostWantedVehicleMath.InductionBoost(tuning, 8500, 0), Is.EqualTo(0));
        }

        [Test]
        public void ForwardReverseAndNeutralUseTheirOwnEfficiencySlots()
        {
            var tuning = Reference(); tuning.mostWanted.gearEfficiency[0] = .9f; tuning.mostWanted.gearEfficiency[5] = .7f;
            tuning.mostWanted.reverseGearEfficiency = .8f;
            Assert.That(MostWantedVehicleMath.GearEfficiency(tuning, 1), Is.EqualTo(.9f));
            Assert.That(MostWantedVehicleMath.GearEfficiency(tuning, 6), Is.EqualTo(.7f));
            Assert.That(MostWantedVehicleMath.GearEfficiency(tuning, -1), Is.EqualTo(.8f));
            Assert.That(MostWantedVehicleMath.GearEfficiency(tuning, 0), Is.EqualTo(0));
        }

        [Test]
        public void ShiftDelayUsesNewRatioAndDownshiftQuarterDuration()
        {
            Assert.That(MostWantedVehicleMath.ShiftDuration(.25f, 2.53f, false), Is.EqualTo(.6325f).Within(.000001f));
            Assert.That(MostWantedVehicleMath.ShiftDuration(.25f, 4.1f, true), Is.EqualTo(.25625f).Within(.000001f));
            var tuning = Reference();
            for (int gear = 1; gear < 6; gear++)
                Assert.That(MostWantedVehicleMath.UpshiftRpm(tuning, gear), Is.InRange(4650f, 8500f));
        }

        [Test]
        public void InvalidReferenceSamplesFailBeforeSimulation()
        {
            var tuning = Reference(); tuning.mostWanted.normalizedTorque[0] = float.NaN;
            Assert.Throws<ArgumentException>(() => RacingLineSnapshot.ValidateTuning(tuning));
            tuning.mostWanted.normalizedTorque[0] = .5f; tuning.mostWanted.torqueTableMaximumRpm = 8000;
            Assert.Throws<ArgumentException>(() => RacingLineSnapshot.ValidateTuning(tuning));
            tuning.mostWanted.torqueTableMaximumRpm = 9500; tuning.mostWanted.gearEfficiency = new float[1];
            Assert.Throws<ArgumentException>(() => RacingLineSnapshot.ValidateTuning(tuning));
        }

        [Test]
        public void NitrousRequiresForwardGearAndHasStartContinueSpeedHysteresis()
        {
            var tuning = Reference(); var nos = Own(new GameObject("NOS test")).AddComponent<VehicleNitrous>(); nos.Configure(tuning);
            float mph = MostWantedVehicleMath.ReferenceMphToKph;
            Assert.That(nos.Tick(.02f, true, 9f * mph, 1), Is.False);
            Assert.That(nos.Tick(.02f, true, 11f * mph, -1), Is.False);
            Assert.That(nos.Tick(.02f, true, 11f * mph, 0), Is.False);
            Assert.That(nos.Tick(.02f, true, 11f * mph, 1), Is.True);
            Assert.That(nos.Tick(.02f, true, 6f * mph, 1), Is.True);
            Assert.That(nos.Tick(.02f, true, 4f * mph, 1), Is.False);
        }

        [Test]
        public void NitrousRechargeUsesFullTankSecondsAndHonorsPermission()
        {
            var tuning = Reference(); var nos = Own(new GameObject("NOS recharge test")).AddComponent<VehicleNitrous>(); nos.Configure(tuning);
            for (int i = 0; i < 130; i++) nos.Tick(.02f, true, 120f, 1);
            for (int i = 0; i < 110; i++) nos.Tick(.02f, false, 0f, 1);
            Assert.That(nos.NormalizedFuel, Is.EqualTo(0f).Within(.0001f));
            nos.RechargeAllowed = false; nos.Tick(1f, false, 200f, 1);
            Assert.That(nos.NormalizedFuel, Is.EqualTo(0f).Within(.0001f));
            nos.RechargeAllowed = true; nos.Tick(1f, false, 200f, 1);
            Assert.That(nos.NormalizedFuel, Is.EqualTo(1f / 30f).Within(.0001f));
            nos.ResetForSpawn(); Assert.That(nos.NormalizedFuel, Is.EqualTo(1)); Assert.That(nos.IsActive, Is.False);
        }

        [Test]
        public void SteeringUsesMetersPerSecondAndReverseUsesLowSpeedEnd()
        {
            Assert.That(MostWantedSteeringModel.BaseRangeDegrees(0), Is.EqualTo(40f));
            Assert.That(MostWantedSteeringModel.BaseRangeDegrees(160f / 9f), Is.EqualTo(20f).Within(.00001f));
            Assert.That(MostWantedSteeringModel.BaseRangeDegrees(-20f), Is.EqualTo(40f));
            Assert.That(MostWantedSteeringModel.BaseRangeDegrees(160f), Is.EqualTo(2.9f));
            Assert.That(MostWantedSteeringModel.BaseRangeDegrees(100f / 3.6f), Is.GreaterThan(MostWantedSteeringModel.BaseRangeDegrees(100f)));
        }

        [Test]
        public void SteeringIsSymmetricRateLimitedAndResettable()
        {
            var settings = Reference().mostWanted;
            var right = new MostWantedSteeringModel(); var left = new MostWantedSteeringModel();
            float initial = right.Step(settings, new VehicleInputState { Steering = 1, Throttle = 1 }, 0, 0, .02f);
            Assert.That(initial, Is.GreaterThan(0).And.LessThan(40f));
            right.Reset();
            for (int i = 0; i < 100; i++)
            {
                float a = right.Step(settings, new VehicleInputState { Steering = .8f, Throttle = 1 }, 40f, 0, .02f);
                float b = left.Step(settings, new VehicleInputState { Steering = -.8f, Throttle = 1 }, 40f, 0, .02f);
                Assert.That(a, Is.EqualTo(-b).Within(.00001f));
                Assert.That(Mathf.Abs(a), Is.LessThanOrEqualTo(45));
            }
            right.Reset();
            Assert.That(right.Step(settings, new VehicleInputState { Steering = 1, Throttle = 1 }, 0, 0, .02f), Is.EqualTo(initial));
        }

        [Test]
        public void HeldPartialSteeringDoesNotRetriggerFullInputSpeedEveryTick()
        {
            var settings = Reference().mostWanted;
            var steering = new MostWantedSteeringModel();
            var input = new VehicleInputState { Steering = .4f, Throttle = 1f };
            const float dt = .005f;
            float first = steering.Step(settings, input, 0f, 0f, dt);
            float second = steering.Step(settings, input, 0f, 0f, dt);
            float firstDelta = Mathf.Abs(first);
            float secondDelta = Mathf.Abs(second - first);
            Assert.That(firstDelta, Is.GreaterThan(0f));
            Assert.That(secondDelta, Is.LessThan(firstDelta * .8f),
                "A held partial steer must decay from the initial input-speed boost instead of being treated as a new stick movement each tick.");
        }

        [Test]
        public void AckermannUsesRecoveredRationalApproximation()
        {
            float expected = 2.73f * (30f * Mathf.Deg2Rad) / (1.608f * 30f * Mathf.Deg2Rad + 2.73f) * Mathf.Rad2Deg;
            Assert.That(MostWantedVehicleMath.OutsideSteerDegrees(30, 2.73f, 1.608f), Is.EqualTo(expected).Within(.00001f));
            Assert.That(MostWantedVehicleMath.OutsideSteerDegrees(-30, 2.73f, 1.608f), Is.EqualTo(-expected).Within(.00001f));
            Assert.That(expected, Is.LessThan(30));
        }

        [Test]
        public void DownforceIsLinearAndAirborneAttitudeDependent()
        {
            Assert.That(MostWantedVehicleMath.ReferenceDownforce(20, 600, 1, 1, 4), Is.EqualTo(12000));
            Assert.That(MostWantedVehicleMath.ReferenceDownforce(40, 600, 1, 1, 4), Is.EqualTo(24000));
            Assert.That(MostWantedVehicleMath.ReferenceDownforce(20, 600, 1, 1, 0), Is.EqualTo(9600));
            Assert.That(MostWantedVehicleMath.ReferenceDownforce(20, 600, 1, -1, 0), Is.EqualTo(0));
            Assert.That(MostWantedVehicleMath.ReferenceDownforce(20, 600, -1, 1, 4), Is.EqualTo(4800));
        }

        [Test]
        public void EngineOffInvalidTimeAndNeutralCannotProduceWheelTorque()
        {
            var tuning = Reference(); tuning.transmissionMode = VehicleTransmissionMode.Manual;
            var engine = Own(new GameObject("Reference engine test")).AddComponent<VehiclePowertrain>();
            engine.Configure(tuning, Array.Empty<VehicleWheel>());
            Assert.That(engine.Simulate(float.NaN, 0, 1, false, false).WheelTorque, Is.EqualTo(0));
            engine.SetIgnition(false);
            Assert.That(engine.Simulate(.02f, 0, 1, false, true).WheelTorque, Is.EqualTo(0));
            engine.SetIgnition(true); engine.SetManualRequests(false, false, true);
            var result = engine.Simulate(.02f, 0, .5f, false, true);
            Assert.That(result.Gear, Is.EqualTo(0)); Assert.That(result.WheelTorque, Is.EqualTo(0));
            Assert.That(result.EngineRpm, Is.GreaterThan(tuning.engine.idleRpm));
        }

        [Test]
        public void AutomaticDirectionChangeWaitsForDrivenWheelSpinAndDoesNotPowerOldGear()
        {
            var tuning = Reference();
            tuning.transmissionMode = VehicleTransmissionMode.Automatic;
            var wheel = Own(new GameObject("Reference direction wheel")).AddComponent<VehicleWheel>();
            wheel.Setup(VehicleAxle.Rear, false, true, true, null);
            var engine = Own(new GameObject("Reference direction engine")).AddComponent<VehiclePowertrain>();
            engine.Configure(tuning, new[] { wheel });

            wheel.SetInitialForwardSpeed(10f);
            var blockedReverse = engine.Simulate(.02f, 0f, 1f, true, false);
            Assert.That(blockedReverse.Gear, Is.EqualTo(1));
            Assert.That(blockedReverse.EngineTorque, Is.LessThanOrEqualTo(0f),
                "A pending reverse request must not keep driving first gear while the wheel is still spinning forward.");

            wheel.SetInitialForwardSpeed(0f);
            Assert.That(engine.Simulate(.02f, 0f, 1f, true, false).Gear, Is.EqualTo(-1));

            wheel.SetInitialForwardSpeed(-10f);
            var blockedForward = engine.Simulate(.02f, 0f, 1f, false, false);
            Assert.That(blockedForward.Gear, Is.EqualTo(-1));
            Assert.That(blockedForward.EngineTorque, Is.LessThanOrEqualTo(0f),
                "A pending forward request must not power reverse while the wheel is still spinning backward.");
        }

        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
    }
}
