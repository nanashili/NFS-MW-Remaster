using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleCameraScenePlayTests
    {
        [UnityTest] public IEnumerator RigSettlesAtRestAndDoesNotChangePhysics()
        {
            var go = new GameObject("Camera test vehicle"); var body = go.AddComponent<Rigidbody>(); body.useGravity = false;
            var cameraObject = new GameObject("Camera under test"); cameraObject.AddComponent<Camera>();
            var rig = cameraObject.AddComponent<VehicleCameraRig>(); rig.SetTarget(go.transform, body);
            yield return new WaitForSeconds(.6f);
            Vector3 position = cameraObject.transform.position;
            for (int i = 0; i < 60; i++) { go.transform.position = Vector3.up * Mathf.Sin(i) * .005f; yield return null; }
            Assert.That(Vector3.Distance(position, cameraObject.transform.position), Is.LessThan(.001));
            Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero)); Assert.That(body.angularVelocity, Is.EqualTo(Vector3.zero));
            body.linearVelocity = Vector3.forward * 20;
            yield return new WaitForSeconds(.2f);
            Assert.That(body.linearVelocity.z, Is.EqualTo(20).Within(.001)); Assert.That(rig.Diagnostics.parked, Is.False);
            Object.Destroy(cameraObject); Object.Destroy(go); yield return null;
        }
        [UnityTest] public IEnumerator HdrpAdapterHonorsQualityUserSwitchAndRestoresMask()
        {
            int quality = QualitySettings.GetQualityLevel();
            var go = new GameObject("HDRP camera test"); go.AddComponent<Camera>();
            var hd = go.AddComponent<HDAdditionalCameraData>(); int mask = hd.volumeLayerMask;
            var rig = go.AddComponent<VehicleCameraRig>(); var adapter = go.AddComponent<VehicleCameraPostProcessing>();
            var vehicle = new GameObject("HDRP vehicle"); var body = vehicle.AddComponent<Rigidbody>(); body.useGravity = false; body.linearVelocity = Vector3.forward * 80;
            rig.SetTarget(vehicle.transform, body);
            try
            {
                QualitySettings.SetQualityLevel(4, false); yield return new WaitForSeconds(.4f);
                Assert.That(adapter.AppliedMotionBlur, Is.GreaterThan(.01f)); Assert.That(adapter.AppliedMotionBlur, Is.LessThanOrEqualTo(.4f));
                rig.MotionBlurEnabled = false; yield return null; yield return null; Assert.That(adapter.AppliedMotionBlur, Is.Zero);
                rig.MotionBlurEnabled = true; QualitySettings.SetQualityLevel(1, false); yield return null; yield return null; Assert.That(adapter.AppliedMotionBlur, Is.Zero);
                adapter.enabled = false; Assert.That(hd.volumeLayerMask.value, Is.EqualTo(mask));
            }
            finally { QualitySettings.SetQualityLevel(quality, false); Object.Destroy(go); Object.Destroy(vehicle); }
            yield return null;
        }
        [UnityTest] public IEnumerator IdenticalSpeedReplayRendersLegacyAndNewCamera()
        {
            string output = Environment.GetEnvironmentVariable("CAMERA_SCENE_EVIDENCE");
            if (string.IsNullOrEmpty(output)) Assert.Ignore("Set CAMERA_SCENE_EVIDENCE to render the fixed-speed comparison.");
            Directory.CreateDirectory(output); SceneManager.sceneLoaded += IsolateStorage;
            try { yield return SceneManager.LoadSceneAsync("Assets/NfsMw/Scenes/Showcase/WeatherDemo.unity"); }
            finally { SceneManager.sceneLoaded -= IsolateStorage; }
            var vehicle = Object.FindFirstObjectByType<VehicleController>(); var rig = vehicle.CameraRig; var camera = rig.GetComponent<Camera>();
            Assert.That(rig.Profile, Is.Not.Null); Assert.That(rig.GetComponent<VehicleCameraPostProcessing>(), Is.Not.Null);
            vehicle.GetComponent<VehicleInputAuthority>().SetNeutral();
            for (int i = 0; i < 20; i++) yield return new WaitForFixedUpdate();
            vehicle.enabled = false; vehicle.Body.isKinematic = true; rig.enabled = false;
            var adapter = rig.GetComponent<VehicleCameraPostProcessing>(); adapter.enabled = false;
            var secondObject = new GameObject("Legacy camera comparison"); var legacy = secondObject.AddComponent<Camera>(); legacy.CopyFrom(camera); legacy.enabled = false;
            var hd = secondObject.AddComponent<HDAdditionalCameraData>(); camera.GetComponent<HDAdditionalCameraData>().CopyTo(hd);
            hd.volumeLayerMask &= ~(1 << VehicleCameraPostProcessing.VolumeLayer);
            Vector3 origin = new Vector3(20000, 0, 20000); Quaternion heading = Quaternion.identity;
            var shader = Shader.Find("HDRP/Lit"); var asphalt = new Material(shader); asphalt.SetColor("_BaseColor", new Color(.12f, .13f, .14f));
            var white = new Material(shader); white.SetColor("_BaseColor", new Color(.8f, .8f, .7f));
            var stage = new GameObject("Repeatable camera comparison corridor");
            var sunObject = new GameObject("Comparison daylight"); sunObject.transform.SetParent(stage.transform);
            sunObject.transform.rotation = Quaternion.Euler(45, -30, 0);
            var sun = sunObject.AddComponent<Light>(); sun.type = LightType.Directional; sun.color = Color.white;
            sunObject.AddComponent<HDAdditionalLightData>(); sun.lightUnit = LightUnit.Lux; sun.intensity = 90000;
            var stageProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            var exposure = stageProfile.Add<Exposure>(); exposure.mode.Override(ExposureMode.Fixed); exposure.fixedExposure.Override(12);
            var volume = stage.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 10000; volume.sharedProfile = stageProfile;
            Box(stage.transform, origin + new Vector3(0, -.2f, 180), new Vector3(14, .4f, 500), asphalt);
            for (int z = -50; z < 430; z += 10)
            {
                Box(stage.transform, origin + new Vector3(0, .012f, z), new Vector3(.15f, .02f, 4), white);
                Box(stage.transform, origin + new Vector3(-7, 1, z), new Vector3(.25f, 2, .25f), white);
                Box(stage.transform, origin + new Vector3(7, 1, z), new Vector3(.25f, 2, .25f), white);
            }
            var profile = rig.Profile; var pipeline = new VehicleCameraPipeline();
            using (var csv = new StreamWriter(Path.Combine(output, "speed-comparison.csv")))
            {
                csv.WriteLine("speed_kph,legacy_fov,new_fov,legacy_distance_m,new_distance_m,lookahead_m,shake");
                foreach (float speed in new[] { 0f, 50, 80, 100, 150, 160, 200, 250, 320 })
                {
                    vehicle.transform.SetPositionAndRotation(origin + Vector3.up * .8f, heading);
                    pipeline.Reset(); var frame = new VehicleCameraFrame { position = vehicle.transform.position, rotation = heading, velocity = Vector3.forward * speed / 3.6f,
                        speedKph = speed, grounded = true, roadNormal = Vector3.up, roughness = .08f };
                    VehicleCameraPose pose = default;
                    for (int i = 0; i < 600; i++) pose = pipeline.Resolve(profile, frame, false, VehicleCameraEffects.All, 1f / 60);
                    Vector3 oldPosition = frame.position + new Vector3(0, 2.25f, -6.8f); Vector3 oldVelocity = Vector3.zero;
                    Quaternion oldRotation = Quaternion.LookRotation(frame.position + new Vector3(0, .85f, 3.2f) + frame.velocity * .1f - oldPosition);
                    // Settle both at the same actual trajectory, including legacy world-space follow latency.
                    for (int i = 0; i < 180; i++)
                    {
                        frame.position += frame.velocity / 60;
                        oldPosition = Vector3.SmoothDamp(oldPosition, frame.position + new Vector3(0, 2.25f, -6.8f), ref oldVelocity, .075f, Mathf.Infinity, 1f / 60);
                        oldRotation = Quaternion.Slerp(oldRotation, Quaternion.LookRotation(frame.position + new Vector3(0, .85f, 3.2f) + frame.velocity * .1f - oldPosition), .22f);
                        pose = pipeline.Resolve(profile, frame, false, VehicleCameraEffects.All, 1f / 60);
                    }
                    vehicle.transform.position = frame.position;
                    camera.transform.SetPositionAndRotation(pose.position, pose.rotation); camera.fieldOfView = pose.fieldOfView;
                    legacy.transform.SetPositionAndRotation(oldPosition, oldRotation); legacy.fieldOfView = Mathf.Lerp(64, 78, Mathf.Clamp01(speed / 230));
                    yield return null;
                    Capture(legacy, Path.Combine(output, $"{speed:000}-legacy.png")); Capture(camera, Path.Combine(output, $"{speed:000}-new.png"));
                    csv.WriteLine(FormattableString.Invariant($"{speed},{legacy.fieldOfView},{pose.fieldOfView},{Vector3.Distance(oldPosition, frame.position)},{pose.debug.distance},{pose.debug.lookAhead},{pose.debug.shakeStrength}"));
                    if (speed != 80 && speed != 160 && speed != 250) continue;
                    string clip = Path.Combine(output, $"{speed:000}-sequence"); Directory.CreateDirectory(clip);
                    for (int i = 0; i < 48; i++)
                    {
                        frame.position += frame.velocity / 24; vehicle.transform.position = frame.position;
                        oldPosition = Vector3.SmoothDamp(oldPosition, frame.position + new Vector3(0, 2.25f, -6.8f), ref oldVelocity, .075f, Mathf.Infinity, 1f / 24);
                        oldRotation = Quaternion.Slerp(oldRotation, Quaternion.LookRotation(frame.position + new Vector3(0, .85f, 3.2f) + frame.velocity * .1f - oldPosition), 1 - Mathf.Pow(.78f, 60f / 24));
                        pose = pipeline.Resolve(profile, frame, false, VehicleCameraEffects.All, 1f / 24);
                        camera.transform.SetPositionAndRotation(pose.position, pose.rotation); camera.fieldOfView = pose.fieldOfView;
                        legacy.transform.SetPositionAndRotation(oldPosition, oldRotation);
                        yield return null;
                        Capture(legacy, Path.Combine(clip, $"legacy-{i:000}.png")); Capture(camera, Path.Combine(clip, $"new-{i:000}.png"));
                    }
                }
            }
            Object.Destroy(stage); Object.Destroy(exposure); Object.Destroy(stageProfile); Object.Destroy(asphalt); Object.Destroy(white); Object.Destroy(secondObject);
        }
        private static void Box(Transform parent, Vector3 position, Vector3 scale, Material material)
        { var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.transform.SetParent(parent); go.transform.position = position; go.transform.localScale = scale; go.GetComponent<Renderer>().sharedMaterial = material; }
        private static void IsolateStorage(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects()) foreach (var profile in root.GetComponentsInChildren<CareerProfileSystem>(true))
            { profile.ConfigureAutomaticPersistence(false, false, false); var storage = profile.GetComponent<JsonCareerProfileStorage>(); if (storage) storage.SetDirectory(Path.Combine(Path.GetTempPath(), "camera-test-" + Guid.NewGuid().ToString("N"))); }
        }
        private static void Capture(Camera camera, string path)
        {
            var rt = new RenderTexture(960, 540, 24); rt.Create(); var previous = camera.targetTexture; var active = RenderTexture.active; float aspect = camera.aspect;
            var image = new Texture2D(960, 540, TextureFormat.RGB24, false);
            try { camera.targetTexture = rt; camera.aspect = 16f / 9; camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
            finally { camera.targetTexture = previous; camera.aspect = aspect; RenderTexture.active = active; rt.Release(); Object.Destroy(rt); Object.Destroy(image); }
        }
    }
}
