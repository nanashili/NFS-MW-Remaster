using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class MostWantedPowertrainTests
    {
        [Test]
        public void SourceLoadedEngineInertiaUsesFlywheelEquation()
        {
            Assert.That(MostWantedVehicleMath.LoadedEngineInertia(12f), Is.EqualTo(.55f).Within(.000001f));
        }

        [Test]
        public void NeutralEngineRacerStillIntegratesFreeRev()
        {
            var tuning = VehicleTuning.CreateStreetRacer();
            tuning.transmissionMode = VehicleTransmissionMode.Manual;
            var root = new GameObject("Most Wanted powertrain golden");
            var powertrain = root.AddComponent<VehiclePowertrain>();
            powertrain.Configure(tuning, System.Array.Empty<VehicleWheel>());
            powertrain.SetManualRequests(false, false, true);
            var before = powertrain.EngineRpm;
            var output = powertrain.Simulate(.02f, 0f, 1f, false);
            Assert.That(output.Gear, Is.EqualTo(0));
            Assert.That(output.EngineRpm, Is.GreaterThan(before));
            Object.DestroyImmediate(tuning);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void SourceNosUsesStartAndContinueHysteresis()
        {
            var tuning = VehicleTuning.CreateStreetRacer();
            tuning.mostWanted.nitrousTorqueBoost = .5f;
            tuning.mostWanted.nitrousDisengageSeconds = 1f;
            tuning.engine.nitrousFuelSeconds = 2f;
            var root = new GameObject("Most Wanted NOS golden");
            var nitrous = root.AddComponent<VehicleNitrous>();
            nitrous.Configure(tuning);
            float mph = MostWantedVehicleMath.ReferenceMphToKph;
            Assert.That(nitrous.Tick(.02f, true, 9f * mph, 1), Is.False);
            Assert.That(nitrous.Tick(.02f, true, 11f * mph, 1), Is.True);
            Assert.That(nitrous.Tick(.02f, true, 6f * mph, 1), Is.True);
            Assert.That(nitrous.Tick(.02f, true, 4f * mph, 1), Is.False);
            Object.DestroyImmediate(tuning);
            Object.DestroyImmediate(root);
        }
    }
}
