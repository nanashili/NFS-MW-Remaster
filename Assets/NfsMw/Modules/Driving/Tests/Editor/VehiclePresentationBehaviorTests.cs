using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehiclePresentationBehaviorTests
    {
        [Test]
        public void BrakeStateEnablesLampAndEmissiveOutput()
        {
            GameObject root = new GameObject("presentation");
            try
            {
                GameObject lampObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                lampObject.transform.SetParent(root.transform, false);
                Light light = root.AddComponent<Light>();
                var bindings = root.AddComponent<VehiclePresentationBindings>();
                Renderer renderer = lampObject.GetComponent<Renderer>();
                bindings.useAutomaticLights = false;
                bindings.headlights.mode = VehicleLampMode.Low;
                bindings.tailLights.emissive = new[] { renderer };
                bindings.tailLights.lights = new[] { light };
                bindings.tailLights.intensity = .5f;
                bindings.brakeLights.emissive = bindings.tailLights.emissive;
                bindings.brakeLights.lights = new[] { light };
                bindings.brakeLights.intensity = 2f;
                var existing = new MaterialPropertyBlock(); existing.SetFloat("_Wetness", .37f); existing.SetColor("_BaseColor", Color.magenta); renderer.SetPropertyBlock(existing);
                var module = root.AddComponent<VehiclePresentationModule>();
                var context = new VehicleModuleContext(null, root.AddComponent<Rigidbody>(), null, null);
                module.Initialize(context);
                context.Telemetry = default; module.Present(context, .02f);
                var tail = new MaterialPropertyBlock(); renderer.GetPropertyBlock(tail);
                Assert.That(light.enabled, Is.True);
                Assert.That(light.intensity, Is.EqualTo(.5f).Within(.001f));
                Assert.That(tail.GetColor("_EmissiveColor").maxColorComponent, Is.GreaterThan(0));
                Assert.That(tail.GetFloat("_Wetness"), Is.EqualTo(.37f).Within(.001f));
                Assert.That(tail.GetColor("_BaseColor"), Is.EqualTo(Color.magenta));
                context.Telemetry = new VehicleTelemetry { Brake = 1f };
                module.Present(context, .02f);
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                Assert.That(light.enabled, Is.True);
                Assert.That(light.intensity, Is.EqualTo(2f).Within(.001f));
                Assert.That(block.GetColor("_EmissiveColor").maxColorComponent, Is.GreaterThan(1f));
                bindings.headlights.mode = VehicleLampMode.Off;
                context.Telemetry = default; module.Present(context, .02f);
                renderer.GetPropertyBlock(block);
                Assert.That(light.enabled, Is.False);
                Assert.That(block.GetColor("_EmissiveColor").maxColorComponent, Is.EqualTo(0));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void SteeringUsesAuthoredNeutralAndAxis()
        {
            GameObject root = new GameObject("presentation");
            try
            {
                Transform wheel = new GameObject("steering").transform; wheel.SetParent(root.transform, false);
                var bindings = root.AddComponent<VehiclePresentationBindings>();
                bindings.steeringWheel = wheel; bindings.steeringAxis = Vector3.up;
                bindings.steeringNeutral = Quaternion.Euler(10, 20, 30); bindings.steeringWheelDegrees = 450; bindings.steeringRatio = 1;
                var module = root.AddComponent<VehiclePresentationModule>();
                var body = root.AddComponent<Rigidbody>();
                var wheelObject = new GameObject("road-wheel"); wheelObject.transform.SetParent(root.transform, false);
                var roadWheel = wheelObject.AddComponent<VehicleWheel>(); roadWheel.Setup(VehicleAxle.Front, true, false, false, null); roadWheel.Configure(body, null);
                var context = new VehicleModuleContext(null, body, null, new[] { roadWheel });
                module.Initialize(context);
                roadWheel.Simulate(.02f, new VehicleWheelCommand { SteerAngle = 28f });
                module.Present(context, .02f);
                float firstFrameAngle = 28f * (1f - Mathf.Exp(-.02f * 14f));
                Quaternion firstFrame = bindings.steeringNeutral * Quaternion.AngleAxis(-firstFrameAngle, Vector3.up);
                Assert.That(Quaternion.Angle(wheel.localRotation, firstFrame), Is.LessThan(.01f));
                Assert.That(Quaternion.Angle(wheel.localRotation, bindings.steeringNeutral * Quaternion.AngleAxis(-28f, Vector3.up)), Is.GreaterThan(1f));
                for (int i = 0; i < 120; i++) module.Present(context, .02f);
                Quaternion settled = bindings.steeringNeutral * Quaternion.AngleAxis(-28f, Vector3.up);
                Assert.That(Quaternion.Angle(wheel.localRotation, settled), Is.LessThan(.01f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void CaliperFollowsSteeringAndSuspensionWithoutWheelSpin()
        {
            GameObject root = new GameObject("presentation");
            try
            {
                var body = root.AddComponent<Rigidbody>();
                var wheelObject = new GameObject("wheel"); wheelObject.transform.SetParent(root.transform, false);
                wheelObject.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
                var wheel = wheelObject.AddComponent<VehicleWheel>(); wheel.Setup(VehicleAxle.Front, true, false, false, null); wheel.Configure(body, null);
                var caliper = new GameObject("caliper").transform; caliper.SetParent(root.transform, false);
                var bindings = root.AddComponent<VehiclePresentationBindings>();
                bindings.wheels[0] = new VehicleWheelPresentationBinding { wheel = wheelObject.transform, caliper = caliper };
                var module = root.AddComponent<VehiclePresentationModule>();
                var context = new VehicleModuleContext(null, body, null, new[] { wheel }); module.Initialize(context);
                wheel.Simulate(.02f, new VehicleWheelCommand { SteerAngle = 25f });
                module.Present(context, .02f);
                Vector3 expectedPosition = wheel.transform.position - wheel.transform.up * wheel.SuspensionLength;
                Quaternion firstRotation = caliper.rotation;
                Assert.That(Vector3.Distance(caliper.position, expectedPosition), Is.LessThan(.001f));
                module.Present(context, .02f);
                Assert.That(Quaternion.Angle(caliper.rotation, firstRotation), Is.LessThan(.01f));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
