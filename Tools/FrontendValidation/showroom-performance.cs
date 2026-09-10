// Run through Unity_RunCommand with Boot in Play mode. The report is written under
// Tools/FrontendValidation/Evidence/showroom-performance-latest.txt. Run with a
// focused Game view and leave the editor undisturbed until FINISHED is reported.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using NfsMwRemaster.Driving;

public class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!EditorApplication.isPlaying || GameFlowRuntime.Instance == null)
            throw new InvalidOperationException("Start Boot in Play mode first.");
        var runtime = GameFlowRuntime.Instance;
        var screen = runtime.GetComponent<GameFlowScreen>();
        var showroom = runtime.GetComponent<MostWantedShowroom>();
        string report = Path.GetFullPath("Tools/FrontendValidation/Evidence/showroom-performance-latest.txt");
        File.WriteAllText(report, "Started " + DateTime.UtcNow.ToString("O") + " Unity=" + Application.unityVersion
            + " GPU=" + SystemInfo.graphicsDeviceName + " quality=" + QualitySettings.GetQualityLevel() + "\n");
        var samples = new List<double>();
        int phase = -2, lastFrame = -1, visibleRenders = 0;
        double began = EditorApplication.timeSinceStartup, nextPage = 0;
        bool passed = true, alternate = false;
        Vector3 initialPosition = Vector3.zero;
        bool moved = false;
        void Submit(string name)
        {
            var button = screen.Root.Q<Button>(name);
            if (button == null) throw new InvalidOperationException("Missing " + name);
            button.Focus();
            using (var ev = NavigationSubmitEvent.GetPooled()) button.SendEvent(ev);
        }
        void Check(bool condition, string message)
        {
            passed &= condition;
            File.AppendAllText(report, (condition ? "PASS " : "FAIL ") + message + "\n");
        }
        EditorApplication.CallbackFunction tick = null;
        tick = () =>
        {
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play mode stopped.");
                if (Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                double now = EditorApplication.timeSinceStartup, elapsed = now - began;
                if (phase < 0)
                {
                    if (elapsed < 1.5) return;
                    if (phase == -2 && screen.CurrentPage == "Title") Submit("title-continue");
                    else if (phase == -1)
                    {
                        if (screen.CurrentPage == "AliasPrompt") Submit("alias-no");
                        screen.OpenPage(MostWantedFrontendPage.Audio);
                    }
                    phase++; began = now; return;
                }
                if (phase == 1 && now >= nextPage)
                {
                    screen.OpenPage(alternate ? MostWantedFrontendPage.Audio : MostWantedFrontendPage.Options);
                    alternate = !alternate; nextPage = now + 1.25;
                }
                if (elapsed > 2)
                {
                    samples.Add(Time.unscaledDeltaTime * 1000);
                    if (showroom.PreviewCamera.enabled) visibleRenders++;
                    moved |= (showroom.PreviewCamera.transform.position - initialPosition).sqrMagnitude > .01f;
                }
                if (elapsed < 8) return;
                samples.Sort();
                double mean = samples.Average(), fps = 1000 / mean, p95 = samples[(int)((samples.Count - 1) * .95)];
                string name = phase == 0 ? "settled-audio" : phase == 1 ? "camera-transitions" : "paint-restored";
                File.AppendAllText(report, string.Format(CultureInfo.InvariantCulture,
                    "{0} page={1} flow={2} frames={3} fps={4:F1} mean_ms={5:F2} p95_ms={6:F2} rendering_frames={7} target={8}x{9}\n",
                    name, screen.CurrentPage, runtime.Flow.State, samples.Count, fps, mean, p95,
                    visibleRenders, showroom.Target.width, showroom.Target.height));
                Check(fps >= 60, name + " averages at least 60 FPS");
                Check(p95 <= 20, name + " keeps 95 percent of frame times within 20 ms");
                if (phase != 1) Check(visibleRenders == 0, name + " reuses the settled image despite repeated Show calls");
                else Check(moved && visibleRenders > 0, "page transitions move and render the camera");
                if (phase == 0)
                {
                    initialPosition = showroom.PreviewCamera.transform.position;
                    nextPage = now;
                }
                if (phase == 1)
                {
                    showroom.SetPaintPreview(Color.red);
                    Check(showroom.PreviewCamera.enabled, "paint invalidates the cached image immediately");
                    showroom.ClearPaintPreview();
                    screen.OpenPage(MostWantedFrontendPage.Audio);
                }
                if (++phase == 3)
                {
                    File.AppendAllText(report, "FINISHED " + (passed ? "PASS" : "FAIL") + "\n");
                    EditorApplication.update -= tick;
                    return;
                }
                samples.Clear(); visibleRenders = 0; began = now;
            }
            catch (Exception error)
            {
                EditorApplication.update -= tick;
                File.AppendAllText(report, "FAILED " + error + "\n");
            }
        };
        EditorApplication.update += tick;
    }
}
