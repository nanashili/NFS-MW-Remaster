using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class VehicleMirrorRendererPlayTests
    {
        [UnityTest]
        public IEnumerator RainWipersAndWindowsReturnToAuthoredPoseWhenVehicleIsPooled()
        {
            var root = new GameObject("presentation lifecycle");
            var weatherRoot = new GameObject("presentation test weather");
            try
            {
                var weather = weatherRoot.AddComponent<DynamicWeatherWorld>();
                Assert.That(weather.TransitionToPreset("rain", true, out var failure), Is.True, failure);
                weather.SetPaused(true);
                var wiper = Node(root, "wiper").transform; var neutral = Quaternion.Euler(12, 23, 34); wiper.localRotation = neutral;
                var window = Node(root, "side window").transform; var position = new Vector3(.2f, .3f, .4f); window.localPosition = position;
                var light = Node(root, "headlight").AddComponent<Light>();
                var bindings = root.AddComponent<VehiclePresentationBindings>(); bindings.wiperPivots = new[] { wiper }; bindings.sideWindowPivots = new[] { window }; bindings.sideWindowOpen = 1;
                bindings.headlights.mode = VehicleLampMode.Low; bindings.headlights.lights = new[] { light };
                var module = root.AddComponent<VehiclePresentationModule>(); var body = root.AddComponent<Rigidbody>(); body.isKinematic = true;
                var context = new VehicleModuleContext(null, body, null, null); module.Initialize(context);
                yield return null;
                module.Present(context, .2f);
                Assert.That(Quaternion.Angle(wiper.localRotation, neutral), Is.GreaterThan(1));
                Assert.That(Vector3.Distance(window.localPosition, position), Is.GreaterThan(.1f)); Assert.That(light.enabled, Is.True);
                root.SetActive(false); yield return null;
                Assert.That(Quaternion.Angle(wiper.localRotation, neutral), Is.LessThan(.01f)); Assert.That(window.localPosition, Is.EqualTo(position)); Assert.That(light.enabled, Is.False);
                root.SetActive(true); module.Initialize(context); module.Present(context, .2f); Assert.That(light.enabled, Is.True);
                Assert.That(weather.TransitionToPreset("clear", true, out failure), Is.True, failure); module.Present(context, .2f);
                Assert.That(Quaternion.Angle(wiper.localRotation, neutral), Is.LessThan(.01f), "Wipers park when precipitation stops.");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(weatherRoot); }
        }

        [Serializable] private sealed class Evidence
        {
            public string device, unity, scope;
            public float firstEnergy, firstX, secondX, displayedX, flippedX;
            public double requestAndReadbackMs, gpuFrameMs;
            public double mirrorGpuMs;
            public int mirrorGpuSamples;
            public bool supportsGpuRecorder;
            public int resolution;
        }
        [UnityTest]
        public IEnumerator MovingRearTargetRendersFlippedOnMirrorSurfaceAndClipsFront()
        {
            if (!SystemInfo.supportsRenderTextures || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Requires a real render device.");
            Assert.That(HdrpQualityRuntime.CurrentPreset.planarReflections, Is.True, "Use a mirror-enabled quality preset.");
            var root = new GameObject("mirror gpu test");
            var camera = Node(root, "observer").AddComponent<Camera>(); camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); ConfigureCamera(camera);
            var view = Node(root, "mirror view").transform; view.SetPositionAndRotation(new Vector3(0, 0, 1), Quaternion.Euler(0, 180, 0));
            var surface = GameObject.CreatePrimitive(PrimitiveType.Quad); surface.transform.SetParent(root.transform); surface.layer = 29;
            surface.transform.SetPositionAndRotation(new Vector3(0, 0, 1.1f), Quaternion.identity);
            var shader = Shader.Find("HDRP/Unlit"); Assert.That(shader, Is.Not.Null);
            var mirrorMaterial = new Material(shader); mirrorMaterial.SetColor("_UnlitColor", Color.white); surface.GetComponent<Renderer>().sharedMaterial = mirrorMaterial;
            var target = GameObject.CreatePrimitive(PrimitiveType.Quad); target.transform.SetParent(root.transform); target.layer = 29;
            target.transform.SetPositionAndRotation(new Vector3(-1, 0, -4), Quaternion.Euler(0, 180, 0)); target.transform.localScale = Vector3.one;
            var targetMaterial = new Material(shader); targetMaterial.SetColor("_UnlitColor", Color.red * 4); target.GetComponent<Renderer>().sharedMaterial = targetMaterial;
            var binding = new VehicleMirrorBinding { id = "test", view = view, surfaces = new[] { surface.GetComponent<Renderer>() }, resolution = 128, farClip = 20, refreshSeconds = .05f, cullingLayer = 29 };
            var mirrors = root.AddComponent<VehicleMirrorRenderer>();
            typeof(VehicleMirrorRenderer).GetField("textureProperty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(mirrors, "_UnlitColorMap");
            mirrors.Configure(camera, new[] { binding });
            var output = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32); output.Create();
            camera.targetTexture = output;
            var request = new RenderPipeline.StandardRequest { destination = output };
            try
            {
                yield return null;
                Assert.That(mirrors.CaptureNow(0), Is.True);
                var first = Read(mirrors.GetCaptureTexture(0), "mirror-first.png"); Assert.That(first.energy, Is.GreaterThan(.002f), "rear target is visible");
                target.transform.position = new Vector3(1, 0, -4); Assert.That(mirrors.CaptureNow(0), Is.True);
                var second = Read(mirrors.GetCaptureTexture(0), "mirror-second.png");
                Assert.That(second.x, Is.LessThan(first.x - .15f), "rearward camera right is vehicle left");
                Assert.That(RenderPipeline.SupportsRenderRequest(camera, request), Is.True);
                RenderPipeline.SubmitRenderRequest(camera, request); var displayed = Read(output, "mirror-display.png");
                Assert.That(displayed.energy, Is.GreaterThan(.001f), "observer sees the texture on the actual mirror surface");
                binding.horizontalFlip = true; mirrors.CaptureNow(0); RenderPipeline.SubmitRenderRequest(camera, request); var flipped = Read(output, "mirror-flipped.png");
                Assert.That(displayed.x * flipped.x, Is.LessThan(-.001f), "displayed target crosses centre after horizontal flip");
                Assert.That(Mathf.Abs(displayed.x + flipped.x), Is.LessThan(.04f), "displayed UV reflection is symmetric");
                target.transform.position = new Vector3(0, 0, 4); mirrors.CaptureNow(0);
                Assert.That(Read(mirrors.GetCaptureTexture(0)).energy, Is.LessThan(first.energy * .1f), "front target is absent from rear view");
                target.transform.position = new Vector3(0, 0, -30); mirrors.CaptureNow(0);
                Assert.That(Read(mirrors.GetCaptureTexture(0)).energy, Is.LessThan(first.energy * .1f), "far plane clips remote target");
                target.transform.position = new Vector3(20, 0, -4); mirrors.CaptureNow(0);
                Assert.That(Read(mirrors.GetCaptureTexture(0)).energy, Is.LessThan(first.energy * .1f), "FOV excludes side target");
                target.transform.position = new Vector3(-1, 0, -4); target.layer = 28; mirrors.CaptureNow(0);
                Assert.That(Read(mirrors.GetCaptureTexture(0)).energy, Is.LessThan(first.energy * .1f), "layer mask excludes target"); target.layer = 29;
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < 8; i++) { mirrors.CaptureNow(0); Read(mirrors.GetCaptureTexture(0)); }
                double requestMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency / 8;
                var gpuSample = UnityEngine.Profiling.CustomSampler.Create("VehicleFramework.MirrorCapture", true);
                var gpuRecorder = gpuSample.GetRecorder(); gpuRecorder.enabled = true;
                double gpuTotal = 0; int gpuSamples = 0;
                try
                {
                    for (int frame = 0; frame < 20; frame++)
                    {
                        gpuSample.Begin(); mirrors.CaptureNow(0); gpuSample.End(); yield return null;
                        if (frame >= 5 && gpuRecorder.gpuSampleBlockCount > 0 && gpuRecorder.gpuElapsedNanoseconds > 0)
                        { gpuTotal += gpuRecorder.gpuElapsedNanoseconds / 1000000.0 / gpuRecorder.gpuSampleBlockCount; gpuSamples++; }
                    }
                }
                finally { gpuRecorder.enabled = false; }
                FrameTimingManager.CaptureFrameTimings(); yield return null; yield return null;
                var timings = new FrameTiming[1]; uint timingCount = FrameTimingManager.GetLatestTimings(1, timings);
                string evidencePath = Environment.GetEnvironmentVariable("VEHICLE_MIRROR_EVIDENCE");
                if (!string.IsNullOrWhiteSpace(evidencePath)) File.WriteAllText(evidencePath, JsonUtility.ToJson(new Evidence { device = SystemInfo.graphicsDeviceName, unity = Application.unityVersion, resolution = 128, firstEnergy = first.energy, firstX = first.x, secondX = second.x, displayedX = displayed.x, flippedX = flipped.x, requestAndReadbackMs = requestMs, gpuFrameMs = timingCount > 0 ? timings[0].gpuFrameTime : 0, mirrorGpuMs = gpuSamples > 0 ? gpuTotal / gpuSamples : 0, mirrorGpuSamples = gpuSamples, supportsGpuRecorder = SystemInfo.supportsGpuRecorder, scope = "HDRP mirror StandardRequest plus synchronous GPU readback, 8 samples. Separate GPU CustomSampler spans only capture requests; FrameTimingManager is whole-frame. Zero timing/count means unavailable." }, true));
                mirrors.enabled = false; Assert.That(mirrors.GetCaptureTexture(0), Is.Null); yield return null;
                mirrors.enabled = true; yield return null; Assert.That(mirrors.GetCaptureTexture(0), Is.Not.Null);
                var presentation = root.AddComponent<VehiclePresentationBindings>(); presentation.role = VehiclePresentationRole.Traffic;
                mirrors.Configure(camera, new[] { binding }); Assert.That(mirrors.GetCaptureTexture(0), Is.Null, "traffic allocates no mirror targets"); Assert.That(mirrors.CaptureNow(0), Is.False);
                presentation.role = VehiclePresentationRole.Player; yield return null; Assert.That(mirrors.GetCaptureTexture(0), Is.Not.Null, "Promoting traffic to player restores the mirror budget.");
            }
            finally { camera.targetTexture = null; output.Release(); Object.DestroyImmediate(output); Object.DestroyImmediate(root); Object.DestroyImmediate(mirrorMaterial); Object.DestroyImmediate(targetMaterial); }
        }
        private static GameObject Node(GameObject root, string name) { var node = new GameObject(name); node.transform.SetParent(root.transform); return node; }
        private static void ConfigureCamera(Camera camera)
        {
            camera.enabled = false; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.cullingMask = 1 << 29; camera.aspect = 1;
            var hd = camera.gameObject.AddComponent<HDAdditionalCameraData>(); hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color; hd.backgroundColorHDR = Color.black;
        }
        private static (float energy, float x) Read(RenderTexture texture, string evidenceName = null)
        {
            Assert.That(texture, Is.Not.Null); var previous = RenderTexture.active; Texture2D read = null;
            try
            {
                RenderTexture.active = texture; read = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false); read.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); read.Apply();
                string evidencePath = Environment.GetEnvironmentVariable("VEHICLE_MIRROR_EVIDENCE");
                if (evidenceName != null && !string.IsNullOrWhiteSpace(evidencePath)) File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(evidencePath), evidenceName), read.EncodeToPNG());
                var pixels = read.GetPixels(); float sum = 0, x = 0;
                // Track the red marker through exposure changes, excluding neutral sky/ground.
                for (int i = 0; i < pixels.Length; i++) { float weight = Mathf.Max(0, pixels[i].r - Mathf.Max(pixels[i].g, pixels[i].b)); sum += weight; x += (i % texture.width + .5f) * weight; }
                return (sum / pixels.Length, sum > .001f ? x / sum / texture.width - .5f : 0);
            }
            finally { RenderTexture.active = previous; if (read) Object.DestroyImmediate(read); }
        }
    }
}
