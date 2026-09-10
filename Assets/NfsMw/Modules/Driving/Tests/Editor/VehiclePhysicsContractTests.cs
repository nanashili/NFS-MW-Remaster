using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehiclePhysicsContractTests
    {
        [Test]
        public void LegacyDriveLayoutKeepsWheelFlagsAuthoritative()
        {
            VehicleTuning tuning = VehicleTuning.CreateStreetRacer();
            try { Assert.AreEqual(VehicleDriveLayout.LegacyBindings, tuning.driveLayout); }
            finally { Object.DestroyImmediate(tuning); }
        }

        [Test]
        public void StabilityStrengthChangesAssistTorque()
        {
            var model = new VehicleHandlingModel();
            var settings = new VehicleHandlingSettings { yawGain = 1000f, maximumYawTorque = 10000f };
            float low = model.Step(settings, new VehicleInputState { Steering = 1f }, 20f, 0f, 0f,
                true, true, 0.2f, 0.02f);
            model.Reset();
            float high = model.Step(settings, new VehicleInputState { Steering = 1f }, 20f, 0f, 0f,
                true, true, 0.8f, 0.02f);
            Assert.That(Mathf.Abs(high), Is.GreaterThan(Mathf.Abs(low)));
        }

        [Test]
        public void ExplicitDriveLayoutRoutesTorqueToConfiguredAxle()
        {
            var root = new GameObject("powertrain test");
            var powertrain = root.AddComponent<VehiclePowertrain>();
            var front = new GameObject("front").AddComponent<VehicleWheel>();
            var rear = new GameObject("rear").AddComponent<VehicleWheel>();
            front.transform.SetParent(root.transform); rear.transform.SetParent(root.transform);
            front.Setup(VehicleAxle.Front, false, false, false, null);
            rear.Setup(VehicleAxle.Rear, false, false, false, null);
            var tuning = VehicleTuning.CreateStreetRacer(); tuning.driveLayout = VehicleDriveLayout.Fwd;
            try
            {
                powertrain.Configure(tuning, new[] { front, rear });
                Assert.That(powertrain.GetDriveTorqueShare(front, new[] { front, rear }), Is.EqualTo(1f));
                Assert.That(powertrain.GetDriveTorqueShare(rear, new[] { front, rear }), Is.EqualTo(0f));
            }
            finally { Object.DestroyImmediate(tuning); Object.DestroyImmediate(root); }
        }

        [Test]
        public void WetnessChangesGripContinuously()
        {
            var surface = new GameObject("surface").AddComponent<VehicleSurface>();
            try
            {
                surface.Configure("road", 1f);
                surface.SetWeatherWetness(0f); float dry = surface.GripMultiplier;
                surface.SetWeatherWetness(0.5f); float damp = surface.GripMultiplier;
                surface.SetWeatherWetness(1f); float wet = surface.GripMultiplier;
                Assert.That(dry, Is.GreaterThan(damp));
                Assert.That(damp, Is.GreaterThan(wet));
            }
            finally { Object.DestroyImmediate(surface.gameObject); }
        }
    }
}
