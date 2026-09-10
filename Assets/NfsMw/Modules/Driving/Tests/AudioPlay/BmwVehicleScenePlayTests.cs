using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class BmwVehicleScenePlayTests
    {
        private sealed class Driver : IVehicleInputSource
        {
            public VehicleInputState state;
            public VehicleInputState Current => state;
            public bool ConsumeResetRequest() => false;
            public bool ConsumeCameraToggleRequest() => false;
        }
        [UnityTest]
        public IEnumerator CurrentWeatherSceneDrivesImportedWheelsAndRendersMirrors()
        {
            string output = Environment.GetEnvironmentVariable("BMW_SCENE_EVIDENCE");
            if (string.IsNullOrEmpty(output)) Assert.Ignore("Set BMW_SCENE_EVIDENCE to capture this authored scene.");
            Directory.CreateDirectory(output);
            SceneManager.sceneLoaded += IsolateStorage;
            try { yield return SceneManager.LoadSceneAsync("Assets/NfsMw/Scenes/Showcase/WeatherDemo.unity"); }
            finally { SceneManager.sceneLoaded -= IsolateStorage; }
            var vehicle = Object.FindFirstObjectByType<VehicleController>(); Assert.That(vehicle.GetComponent<VehicleConfiguration>(), Is.Not.Null);
            var bindings = vehicle.GetComponent<VehiclePresentationBindings>(); var authority = vehicle.GetComponent<VehicleInputAuthority>(); authority.SetNeutral(); var driver = new Driver(); Assert.That(authority.TryAcquire(this, driver), Is.True);
            for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
            for (int i = 0; i < 4; i++)
                Assert.That(Vector3.Distance(vehicle.transform.Find("BMW Wheel " + i).localScale, Vector3.one), Is.LessThan(.0001f), "Factory startup must preserve the imported wheel scale.");
            var audio = vehicle.GetComponent<VehicleAudio>();
            Assert.That(Object.FindObjectsByType<VehicleAudio>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(1), "The gameplay scene must not contain a second vehicle audition player.");
            Assert.That(audio.Source, Is.TypeOf<VehicleFeedbackSampler>());
            Assert.That(audio.Banks, Is.Not.Null);
            Assert.That(audio.ActiveLayers, Is.GreaterThan(0));
            Assert.That(audio.ControlTicks, Is.GreaterThan(0));
            foreach (var root in new[] { vehicle.gameObject, vehicle.CameraRig.gameObject })
                foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                    Assert.That(component, Is.Not.Null, "Scene migration left a missing script on " + root.name);
            var rig = vehicle.CameraRig; var camera = rig.GetComponent<Camera>();
            Capture(camera, Path.Combine(output, "chase.png"));
            if (!rig.IsHoodView) rig.ToggleCameraMode();
            for (int i = 0; i < 12; i++) yield return null;
            Assert.That(Vector3.Distance(camera.transform.position, bindings.cockpitCameraAnchor.position), Is.LessThan(.2f));
            var mirrors = vehicle.GetComponent<VehicleMirrorRenderer>();
            yield return new WaitForSecondsRealtime(.25f);
            for (int i = 0; i < 2; i++)
            {
                var texture = mirrors.GetCaptureTexture(i); Assert.That(texture, Is.Not.Null);
                var block = new MaterialPropertyBlock(); mirrors.Mirrors[i].surfaces[0].GetPropertyBlock(block);
                Assert.That(block.GetTexture("_UnlitColorMap"), Is.SameAs(texture)); SaveTexture(texture, Path.Combine(output, "mirror-" + i + ".png"));
            }
            Capture(camera, Path.Combine(output, "driver.png"));
            rig.ToggleCameraMode(); driver.state = new VehicleInputState { Throttle = .7f, Steering = .15f };
            Quaternion before = bindings.wheels[0].caliper.rotation;
            for (int i = 0; i < 100; i++) yield return new WaitForFixedUpdate();
            Assert.That(vehicle.Telemetry.SpeedKph, Is.GreaterThan(5)); Assert.That(Mathf.Abs(vehicle.Wheels[0].SteerAngle), Is.GreaterThan(.1f));
            for (int i = 0; i < 4; i++)
                Assert.That(Vector3.Distance(bindings.wheels[i].caliper.position, vehicle.Wheels[i].transform.position - vehicle.Wheels[i].transform.up * vehicle.Wheels[i].SuspensionLength), Is.LessThan(.05f));
            Assert.That(bindings.headlights.lights[0].enabled, Is.True);
            Capture(camera, Path.Combine(output, "driving.png"));
            File.WriteAllText(Path.Combine(output, "result.txt"), "WeatherDemo regular FixedUpdate: " + vehicle.Telemetry.SpeedKph + " km/h; front steering " + vehicle.Wheels[0].SteerAngle + "; both mirror displays refreshed; four calipers follow suspension.");
            authority.Release(this);
        }
        private static void IsolateStorage(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects()) foreach (var profile in root.GetComponentsInChildren<CareerProfileSystem>(true))
            { profile.ConfigureAutomaticPersistence(false, false, false); var storage = profile.GetComponent<JsonCareerProfileStorage>(); if (storage) storage.SetDirectory(Path.Combine(Path.GetTempPath(), "bmw-scene-test-" + Guid.NewGuid().ToString("N"))); }
        }
        private static void Capture(Camera camera, string path)
        {
            var target = new RenderTexture(960, 540, 24); target.Create(); var previous = camera.targetTexture; float aspect = camera.aspect;
            try { camera.targetTexture = target; camera.aspect = 960f / 540; camera.Render(); SaveTexture(target, path); }
            finally { camera.targetTexture = previous; camera.aspect = aspect; target.Release(); Object.Destroy(target); }
        }
        private static void SaveTexture(RenderTexture target, string path)
        {
            var previous = RenderTexture.active; var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); float sum = 0; foreach (var p in image.GetPixels()) sum += p.grayscale; Assert.That(sum / (target.width * target.height), Is.GreaterThan(.001f), "Rendered image contains visible scene content."); }
            finally { RenderTexture.active = previous; Object.Destroy(image); }
        }
    }
}
