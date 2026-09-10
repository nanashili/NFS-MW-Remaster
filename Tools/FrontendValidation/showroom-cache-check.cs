// Run through Unity_RunCommand after showroom-performance.cs. Uses the live
// renderer and restores the quality level; it never writes frontend preferences.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using NfsMwRemaster.Driving;

public class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var runtime = GameFlowRuntime.Instance;
        if (!EditorApplication.isPlaying || runtime == null) throw new InvalidOperationException("Start Boot first.");
        var screen = runtime.GetComponent<GameFlowScreen>();
        var showroom = runtime.GetComponent<MostWantedShowroom>();
        string directory = Path.GetFullPath("Tools/FrontendValidation/Evidence/showroom-performance-20260910");
        Directory.CreateDirectory(directory);
        string report = Path.Combine(directory, "cache-check.txt");
        File.WriteAllText(report, "Started " + DateTime.UtcNow.ToString("O") + "\n");
        int quality = QualitySettings.GetQualityLevel(), renders = 0, checkpoint = 0;
        bool passed = true;
        Color32[] original = null, painted = null;
        void Rendered(ScriptableRenderContext context, Camera camera)
        { if (camera == showroom.PreviewCamera) renders++; }
        RenderPipelineManager.endCameraRendering += Rendered;
        void Check(bool condition, string message)
        {
            passed &= condition;
            File.AppendAllText(report, (condition ? "PASS " : "FAIL ") + message + "\n");
        }
        Color32[] ReadImage()
        {
            var previous = RenderTexture.active;
            var small = RenderTexture.GetTemporary(192, 124, 0, RenderTextureFormat.ARGB32);
            var copy = new Texture2D(192, 124, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(showroom.Target, small);
                RenderTexture.active = small;
                copy.ReadPixels(new Rect(0, 0, 192, 124), 0, 0);
                return copy.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(small);
                UnityEngine.Object.Destroy(copy);
            }
        }
        double Difference(Color32[] a, Color32[] b)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++)
                sum += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
            return sum / (a.Length * 3);
        }
        void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(directory, name + ".png"));
        var steps = new List<Action>
        {
            () => screen.OpenPage(MostWantedFrontendPage.Garage),
            () => { original = ReadImage(); checkpoint = renders; },
            () => {
                Check(renders == checkpoint, "settled showroom submits no camera renders while Show is polled");
                Check(showroom.Target.width == 1536 && showroom.Target.height == 992, "settled image is full resolution");
                showroom.SetPaintPreview(Color.red);
            },
            () => {
                Check(renders > checkpoint, "paint change renders a fresh image");
                painted = ReadImage();
                Check(Difference(original, painted) > 1, "paint changes the rendered pixels");
                checkpoint = renders; showroom.ClearPaintPreview();
            },
            () => {
                Check(renders > checkpoint, "cancelled paint renders again");
                Check(Difference(original, ReadImage()) < Difference(original, painted), "cancel restores the original appearance");
                showroom.Show(false, MostWantedFrontendPage.Garage);
                Check(!showroom.PreviewCamera.enabled && !showroom.Stage.activeSelf, "hidden showroom stops rendering");
                showroom.Show(true, MostWantedFrontendPage.Garage);
                checkpoint = renders;
            },
            () => {
                Check(renders > checkpoint, "returning to the same page refreshes its image");
                checkpoint = renders;
                QualitySettings.SetQualityLevel(quality == 4 ? 3 : 4, true);
            },
            () => {
                Check(renders > checkpoint, "quality change invalidates the cached image");
                checkpoint = renders; QualitySettings.SetQualityLevel(quality, true);
            },
            () => {
                Check(renders > checkpoint, "restoring quality renders again");
                checkpoint = renders; showroom.Target.Release();
            },
            () => {
                Check(showroom.Target.IsCreated() && renders > checkpoint, "lost render target is recreated and redrawn");
                Capture("garage"); screen.OpenPage(MostWantedFrontendPage.Options);
            },
            () => { Capture("options"); screen.OpenPage(MostWantedFrontendPage.Audio); },
            () => { Capture("audio"); screen.OpenPage(MostWantedFrontendPage.Credits); },
            () => { Capture("credits"); screen.OpenPage(MostWantedFrontendPage.MainMenu); }
        };
        int step = 0;
        double next = EditorApplication.timeSinceStartup;
        EditorApplication.CallbackFunction tick = null;
        void Cleanup()
        {
            EditorApplication.update -= tick;
            RenderPipelineManager.endCameraRendering -= Rendered;
            if (QualitySettings.GetQualityLevel() != quality) QualitySettings.SetQualityLevel(quality, true);
        }
        tick = () =>
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 2;
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play mode stopped.");
                steps[step++]();
                if (step == steps.Count)
                {
                    Cleanup();
                    File.AppendAllText(report, "FINISHED " + (passed ? "PASS" : "FAIL") + "\n");
                }
            }
            catch (Exception error) { Cleanup(); File.AppendAllText(report, "FAILED " + error + "\n"); }
        };
        EditorApplication.update += tick;
    }
}
