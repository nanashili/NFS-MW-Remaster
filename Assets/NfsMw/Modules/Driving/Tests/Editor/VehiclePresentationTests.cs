using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using NfsMwRemaster.Driving;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehiclePresentationTests
    {
        [Test]
        public void CapabilityOffWithNullOptionalBindingsIsSafe()
        {
            var root = new GameObject("presentation-capability-test");
            try { var b = root.AddComponent<VehiclePresentationBindings>(); b.windscreen = null; b.wiperPivots = null; b.sideWindowPivots = null; var m = root.AddComponent<VehiclePresentationModule>(); var x = new VehicleModuleContext(null, root.AddComponent<Rigidbody>(), null, null); m.Initialize(x); SetPrivate(m, "capabilities", VehicleCapabilities.None); m.Present(x, .02f); Assert.That(m.Bindings, Is.SameAs(b)); }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void PlayerAndTrafficLightsRespectRoleWhileEmissionRemainsVisible()
        {
            var root = new GameObject("presentation-role-test");
            try { var r = GameObject.CreatePrimitive(PrimitiveType.Quad).GetComponent<Renderer>(); r.transform.SetParent(root.transform, false); var l = root.AddComponent<Light>(); var b = root.AddComponent<VehiclePresentationBindings>(); b.role = VehiclePresentationRole.Traffic; b.headlights.mode = VehicleLampMode.Low; b.headlights.lights = new[] { l }; b.headlights.emissive = new[] { r }; var m = root.AddComponent<VehiclePresentationModule>(); var x = new VehicleModuleContext(null, root.AddComponent<Rigidbody>(), null, null); m.Initialize(x); m.Present(x, .02f); var p = new MaterialPropertyBlock(); r.GetPropertyBlock(p); Assert.That(l.enabled, Is.False); Assert.That(p.GetColor("_EmissiveColor").maxColorComponent, Is.GreaterThan(0)); }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ReplacementSharedLampRestoresStockAndCombinesTailBrake()
        {
            var root = new GameObject("presentation-replacement-test"); var replacementRoot = new GameObject("replacement");
            try { var s = root.AddComponent<VehiclePresentationBindings>(); var sr = GameObject.CreatePrimitive(PrimitiveType.Quad).GetComponent<Renderer>(); sr.transform.SetParent(root.transform, false); s.tailLights.emissive = s.brakeLights.emissive = new[] { sr }; s.tailLights.lights = s.brakeLights.lights = new[] { root.AddComponent<Light>() }; s.headlights.mode = VehicleLampMode.Low; var n = replacementRoot.AddComponent<VehiclePresentationBindings>(); var nr = GameObject.CreatePrimitive(PrimitiveType.Quad).GetComponent<Renderer>(); nr.transform.SetParent(replacementRoot.transform, false); n.tailLights.emissive = n.brakeLights.emissive = new[] { nr }; n.tailLights.lights = n.brakeLights.lights = new[] { replacementRoot.AddComponent<Light>() }; n.headlights.mode = VehicleLampMode.Low; var m = root.AddComponent<VehiclePresentationModule>(); var x = new VehicleModuleContext(null, root.AddComponent<Rigidbody>(), null, null); m.Initialize(x); m.SetReplacementBindings(new[] { n }); x.Telemetry = new VehicleTelemetry { Brake = 1f }; m.Present(x, .02f); Assert.That(replacementRoot.GetComponentInChildren<Light>().enabled, Is.True); var p = new MaterialPropertyBlock(); sr.GetPropertyBlock(p); Assert.That(p.GetColor("_EmissiveColor").maxColorComponent, Is.EqualTo(0)); m.SetReplacementBindings(null); m.Present(x, .02f); Assert.That(root.GetComponent<Light>().enabled, Is.True); Assert.That(replacementRoot.GetComponent<Light>().enabled, Is.False); sr.GetPropertyBlock(p); Assert.That(p.GetColor("_EmissiveColor").maxColorComponent, Is.GreaterThan(0)); }
            finally { Object.DestroyImmediate(replacementRoot); Object.DestroyImmediate(root); }
        }

        [Test]
        public void QuaternionWiperAndWindowNeutralsSurviveReconfigureAndReset()
        {
            var root = new GameObject("presentation-neutral-test");
            try { var w = new GameObject("wiper").transform; w.SetParent(root.transform, false); w.localRotation = Quaternion.Euler(12, 23, 34); var q = new GameObject("window").transform; q.SetParent(root.transform, false); q.localPosition = new Vector3(.2f, .3f, .4f); var b = root.AddComponent<VehiclePresentationBindings>(); b.wiperPivots = new[] { w }; b.sideWindowPivots = new[] { q }; b.sideWindowOpen = 1; var m = root.AddComponent<VehiclePresentationModule>(); var x = new VehicleModuleContext(null, root.AddComponent<Rigidbody>(), null, null); m.Initialize(x); m.Present(x, .02f); m.Initialize(x); m.SetReplacementBindings(null); m.ResetPresentation(); Assert.That(Quaternion.Angle(w.localRotation, Quaternion.Euler(12, 23, 34)), Is.LessThan(.01f)); Assert.That(q.localPosition, Is.EqualTo(new Vector3(.2f, .3f, .4f))); }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void GaugeUsesBaseRotationAndAuthoredRangeThroughModule()
        {
            var root = new GameObject("gauge-test");
            try { var n = new GameObject("needle").transform; n.SetParent(root.transform, false); var baseRotation = Quaternion.Euler(5, 10, 15); n.localRotation = baseRotation; var b = root.AddComponent<VehiclePresentationBindings>(); b.speedometer = new VehicleGaugeBinding { needle = n, localAxis = Vector3.forward, minimumAngle = -90, maximumAngle = 90, minimumValue = 0, maximumValue = 100 }; var m = root.AddComponent<VehiclePresentationModule>(); var x = new VehicleModuleContext(null, root.AddComponent<Rigidbody>(), null, null); m.Initialize(x); x.Telemetry = new VehicleTelemetry { SpeedKph = 100 }; m.Present(x, .02f); Assert.That(Quaternion.Angle(n.localRotation, baseRotation * Quaternion.AngleAxis(90, Vector3.forward)), Is.LessThan(.01f)); }
            finally { Object.DestroyImmediate(root); }
        }

        private static void SetPrivate(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
