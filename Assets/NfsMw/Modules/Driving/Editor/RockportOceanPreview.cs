using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    [InitializeOnLoad]
    public static class RockportOceanPreview
    {
        private const string Key = "RockportOceanPreview.Active";
        private static Camera camera;
        private static RenderTexture target;
        private static int stage, firstFrame;
        private static double deadline;
        static RockportOceanPreview() { if (SessionState.GetBool(Key, false)) EditorApplication.delayCall += Resume; }
        public static void Run()
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().path != "Assets/NfsMw/Scenes/World/RockportMap.unity") throw new InvalidOperationException("Open RockportMap in edit mode.");
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true; Resume();
        }
        private static void Resume()
        {
            stage = 0; deadline = EditorApplication.timeSinceStartup + 240;
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
        }
        private static void Capture(string name)
        {
            var previous = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            File.WriteAllBytes("Art/RockportOcean/" + name + ".png", texture.EncodeToPNG());
            RenderTexture.active = previous; UnityEngine.Object.Destroy(texture);
        }
        private static void Poll()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Ocean preview " + stage);
                if (!EditorApplication.isPlaying || Time.time < .1f) return;
                if (stage == 0)
                {
                    camera = new GameObject("TEMP ocean preview camera").AddComponent<Camera>();
                    camera.nearClipPlane = .1f; camera.farClipPlane = 20000; camera.fieldOfView = 60;
                    var hd = camera.gameObject.AddComponent<HDAdditionalCameraData>(); hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
                    hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
                    var sun = new GameObject("TEMP ocean preview sun").AddComponent<Light>(); sun.type = LightType.Directional;
                    sun.intensity = 100000; sun.transform.rotation = Quaternion.Euler(35, -35, 0); sun.gameObject.AddComponent<HDAdditionalLightData>();
                    var volume = new GameObject("TEMP ocean preview atmosphere").AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 100000;
                    var profile = ScriptableObject.CreateInstance<VolumeProfile>(); volume.sharedProfile = profile;
                    var exposure = profile.Add<Exposure>(true); exposure.mode.Override(ExposureMode.Fixed); exposure.fixedExposure.Override(12);
                    profile.Add<VisualEnvironment>(true).skyType.Override((int)SkyType.Gradient);
                    var sky = profile.Add<GradientSky>(true); sky.skyIntensityMode.Override(SkyIntensityMode.Multiplier); sky.multiplier.Override(10000);
                    sky.top.Override(new Color(.12f, .3f, .55f)); sky.middle.Override(new Color(.55f, .68f, .77f)); sky.bottom.Override(new Color(.2f, .26f, .3f));
                    target = new RenderTexture(1400, 1000, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                    camera.transform.position = new Vector3(-6000, 4, -14); camera.transform.LookAt(new Vector3(-6000, 1, 15));
                    firstFrame = Time.frameCount; stage = 1; return;
                }
                if (Time.frameCount - firstFrame < 80) return;
                if (stage == 1)
                {
                    Capture("unity-ocean-waves"); camera.transform.position = new Vector3(-4400, 90, -2500); camera.transform.LookAt(new Vector3(-2400, 30, -2800));
                    firstFrame = Time.frameCount; stage = 2; return;
                }
                if (stage == 2)
                {
                    Capture("unity-ocean-coast"); camera.orthographic = true; camera.orthographicSize = 4700;
                    camera.transform.position = new Vector3(-3400, 9500, 900); camera.transform.LookAt(new Vector3(-3000, 0, -2100));
                    firstFrame = Time.frameCount; stage = 3; return;
                }
                Capture("unity-ocean-overview"); Finish(); Debug.Log("ROCKPORT_OCEAN_PREVIEW_PASS animated HDRP captures saved.");
            }
            catch (Exception exception) { Finish(); Debug.LogException(exception); }
        }
        private static void Finish()
        {
            EditorApplication.update -= Poll; SessionState.SetBool(Key, false);
            if (camera) camera.targetTexture = null;
            if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            EditorApplication.isPlaying = false;
        }
    }
}
