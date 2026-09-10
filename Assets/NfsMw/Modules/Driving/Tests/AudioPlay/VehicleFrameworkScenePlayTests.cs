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
    public sealed class VehicleFrameworkScenePlayTests
    {
        private sealed class Driver : IVehicleInputSource
        {
            public VehicleInputState state;
            public VehicleInputState Current => state;
            public bool ConsumeResetRequest() => false;
            public bool ConsumeCameraToggleRequest() => false;
        }

        [UnityTest]
        public IEnumerator GeneratedScenesDriveThroughExistingGameplayAndCockpitCamera()
        {
            string evidence = Environment.GetEnvironmentVariable("VEHICLE_FRAMEWORK_SCENE_EVIDENCE");
            if (string.IsNullOrEmpty(evidence)) Assert.Ignore("Generate framework examples, then set VEHICLE_FRAMEWORK_SCENE_EVIDENCE to an output folder.");
            Directory.CreateDirectory(evidence);
            foreach (string id in new[] { "hatch-fwd", "coupe-rwd" })
            {
                string scene = "Assets/NfsMw/Modules/Driving/Examples/VehicleFramework/" + id + "/Driving.unity";
                Assert.That(Application.CanStreamedLevelBeLoaded(scene), Is.True, scene);
                yield return SceneManager.LoadSceneAsync(scene);
                var vehicle = Object.FindFirstObjectByType<VehicleController>(); Assert.That(vehicle, Is.Not.Null);
                var configuration = vehicle.GetComponent<VehicleConfiguration>(); Assert.That(configuration.Definition.vehicleId, Is.EqualTo(id));
                var audio = vehicle.GetComponent<VehicleAudio>(); Assert.That(audio, Is.Not.Null); Assert.That(audio.Profile, Is.Not.Null);
                var profile = vehicle.GetComponent<CareerProfileSystem>(); profile.ConfigureAutomaticPersistence(false, false, false);
                var authority = vehicle.GetComponent<VehicleInputAuthority>(); authority.SetNeutral();
                var driver = new Driver { state = new VehicleInputState { Throttle = 1 } };
                Assert.That(authority.TryAcquire(this, driver), Is.True);
                for (int tick = 0; tick < 80; tick++) yield return new WaitForFixedUpdate();
                Assert.That(vehicle.Telemetry.SpeedKph, Is.GreaterThan(5), id + " drives using the regular fixed-step loop.");
                var bindings = vehicle.GetComponent<VehiclePresentationBindings>();
                Assert.That(bindings.headlights.lights[0].enabled, Is.True);
                driver.state = new VehicleInputState { Throttle = .4f, Steering = .5f };
                for (int tick = 0; tick < 15; tick++) yield return new WaitForFixedUpdate();
                Assert.That(Quaternion.Angle(bindings.steeringWheel.localRotation, bindings.steeringNeutral), Is.GreaterThan(2));
                var rig = vehicle.CameraRig; Assert.That(rig, Is.Not.Null); if (!rig.IsHoodView) rig.ToggleCameraMode();
                yield return null; yield return null;
                Assert.That(Vector3.Distance(rig.transform.position, bindings.cockpitCameraAnchor.position), Is.LessThan(.5f));
                var mirrors = vehicle.GetComponent<VehicleMirrorRenderer>();
                yield return new WaitForSecondsRealtime(.25f);
                for (int index = 0; index < mirrors.Mirrors.Length; index++)
                {
                    var displayProperties = new MaterialPropertyBlock(); mirrors.Mirrors[index].surfaces[0].GetPropertyBlock(displayProperties);
                    Assert.That(displayProperties.GetTexture("_UnlitColorMap"), Is.SameAs(mirrors.GetCaptureTexture(index)), "Mirror " + index + " refreshes through its normal presentation loop.");
                }
                var captured = mirrors.GetCaptureTexture(2); var previousMirror = RenderTexture.active;
                var mirrorRead = new Texture2D(captured.width, captured.height, TextureFormat.RGB24, false);
                try
                {
                    RenderTexture.active = captured; mirrorRead.ReadPixels(new Rect(0, 0, captured.width, captured.height), 0, 0); mirrorRead.Apply();
                    float energy = 0; foreach (var pixel in mirrorRead.GetPixels()) energy += pixel.grayscale;
                    Assert.That(energy / (captured.width * captured.height), Is.GreaterThan(.005f), "The real scene mirror must contain visible scenery.");
                    File.WriteAllBytes(Path.Combine(evidence, id + "-mirror.png"), mirrorRead.EncodeToPNG());
                }
                finally { RenderTexture.active = previousMirror; Object.DestroyImmediate(mirrorRead); }
                var camera = rig.GetComponent<Camera>(); var output = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32); output.Create();
                float previousAspect = camera.aspect; camera.aspect = (float)output.width / output.height;
                var previousCameraTarget = camera.targetTexture; camera.targetTexture = output;
                yield return null; yield return null; yield return null;
                var previous = RenderTexture.active; Texture2D read = null;
                try
                {
                    RenderTexture.active = output; read = new Texture2D(960, 540, TextureFormat.RGB24, false); read.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); read.Apply();
                    File.WriteAllBytes(Path.Combine(evidence, id + "-cockpit.png"), read.EncodeToPNG());
                    File.WriteAllText(Path.Combine(evidence, id + "-telemetry.json"), JsonUtility.ToJson(vehicle.Telemetry, true));
                    var surface = mirrors.Mirrors[2].surfaces[0]; var properties = new MaterialPropertyBlock(); surface.GetPropertyBlock(properties);
                    var indexed = new MaterialPropertyBlock(); surface.GetPropertyBlock(indexed, 0);
                    var point = camera.WorldToViewportPoint(surface.bounds.center);
                    var pixel = read.GetPixel(Mathf.Clamp((int)(point.x * read.width), 0, read.width - 1), Mathf.Clamp((int)(point.y * read.height), 0, read.height - 1));
                    // A single point can legitimately hit a dark car body or the road. Sample inside the actual quad.
                    var bounds = surface.GetComponent<MeshFilter>().sharedMesh.bounds; float surfaceEnergy = 0;
                    for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
                    {
                        var local = bounds.center + new Vector3(x * bounds.size.x * .3f, y * bounds.size.y * .3f, 0);
                        var samplePoint = camera.WorldToViewportPoint(surface.transform.TransformPoint(local));
                        Assert.That(samplePoint.z, Is.GreaterThan(0)); Assert.That(samplePoint.x, Is.InRange(0f, 1f)); Assert.That(samplePoint.y, Is.InRange(0f, 1f));
                        surfaceEnergy += read.GetPixel((int)(samplePoint.x * (read.width - 1)), (int)(samplePoint.y * (read.height - 1))).grayscale;
                    }
                    surfaceEnergy /= 9;
                    File.WriteAllText(Path.Combine(evidence, id + "-mirror-binding.txt"), "material=" + surface.sharedMaterial.name + " shader=" + surface.sharedMaterial.shader.name + " unlit=" + surface.sharedMaterial.GetColor("_UnlitColor") + " texture=" + properties.GetTexture("_UnlitColorMap") + " indexedEmpty=" + indexed.isEmpty + " projected=" + point + " centerPixel=" + pixel + " surfaceMean=" + surfaceEnergy);
                    Assert.That(surfaceEnergy, Is.GreaterThan(.005f), "The mirror must display its captured scenery on the actual cockpit surface.");
                }
                finally { camera.targetTexture = previousCameraTarget; camera.aspect = previousAspect; RenderTexture.active = previous; if (read) Object.DestroyImmediate(read); output.Release(); Object.DestroyImmediate(output); authority.Release(this); }
                VerifyMovingAuthoredVehicleBehind(vehicle, configuration.Definition, mirrors, evidence, id);
            }
        }

        private static void VerifyMovingAuthoredVehicleBehind(VehicleController player, VehicleDefinition definition, VehicleMirrorRenderer mirrors, string evidence, string id)
        {
            // Use the shipped vehicle geometry, with a temporary green body marker to isolate it from road and sky.
            var rear = Object.Instantiate(definition.prefab);
            var marker = new Material(Shader.Find("HDRP/Unlit")); marker.SetColor("_UnlitColor", Color.green * 4);
            try
            {
                rear.enabled = false; rear.GetComponent<Rigidbody>().isKinematic = true;
                rear.GetComponent<VehiclePresentationBindings>().role = VehiclePresentationRole.Traffic;
                rear.GetComponent<VehicleMirrorRenderer>().enabled = false;
                rear.GetComponent<VehicleAudio>().enabled = false;
                foreach (var renderer in rear.GetComponentsInChildren<MeshRenderer>()) if (renderer.name == "Body") renderer.sharedMaterial = marker;
                rear.transform.SetPositionAndRotation(player.transform.TransformPoint(new Vector3(-2, 0, -8)), player.transform.rotation);
                Assert.That(mirrors.CaptureNow(2), Is.True);
                float first = ReadRearVehicle(mirrors.GetCaptureTexture(2), Path.Combine(evidence, id + "-rear-vehicle-left.png"));
                rear.transform.position = player.transform.TransformPoint(new Vector3(2, 0, -8));
                Assert.That(mirrors.CaptureNow(2), Is.True);
                float second = ReadRearVehicle(mirrors.GetCaptureTexture(2), Path.Combine(evidence, id + "-rear-vehicle-right.png"));
                Assert.That(second, Is.LessThan(first - .15f), "The actual authored vehicle changes position in the rear camera.");
                File.WriteAllText(Path.Combine(evidence, id + "-rear-vehicle.json"), JsonUtility.ToJson(new RearVehicleEvidence { definitionId = definition.vehicleId, firstX = first, secondX = second }, true));
            }
            finally { Object.DestroyImmediate(rear.gameObject); Object.DestroyImmediate(marker); }
        }
        [Serializable] private sealed class RearVehicleEvidence { public string definitionId; public float firstX, secondX; }
        private static float ReadRearVehicle(RenderTexture texture, string path)
        {
            var previous = RenderTexture.active; var read = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = texture; read.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); read.Apply();
                File.WriteAllBytes(path, read.EncodeToPNG()); float sum = 0, weightedX = 0; var pixels = read.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                { float weight = Mathf.Max(0, pixels[i].g - Mathf.Max(pixels[i].r, pixels[i].b) - .2f); sum += weight; weightedX += (i % texture.width + .5f) * weight; }
                Assert.That(sum, Is.GreaterThan(2f), "The marked body mesh of the rear vehicle must be visible.");
                return weightedX / sum / texture.width - .5f;
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(read); }
        }

    }
}
