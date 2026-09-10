#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>Checks actual paused-listener output and captures the real UITK announcement.</summary>
    [InitializeOnLoad]
    public static class MostWantedMusicPlaybackSmoke
    {
        private const string Running = "NFS.MusicSmoke.Running", Output = "NFS.MusicSmoke.Output";
        private static readonly float[] captureAges = { .14f, .55f, 1.2f, 4.95f, 5.5f, 6.15f };
        private static readonly List<string> checks = new List<string>();
        private static readonly float[] samples = new float[1024];
        private static double deadline, firstSampleAt, initialDsp, initialPosition, titleStarted;
        private static int initialSample, captureIndex;
        private static bool finishing, advanced, navigated;
        private static AudioSource source;
        private static RenderTexture target;
        private static MostWantedMusicOverlay overlay;
        private static string directory;
        private static float peakRms;
        private static int locationStage;
        private static double stageStarted;

        static MostWantedMusicPlaybackSmoke() { if (SessionState.GetBool(Running, false)) Subscribe(); }

        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".mw-frontend-validation-source"))
                throw new InvalidOperationException("Use an owned frontend validation project.");
            directory = Environment.GetEnvironmentVariable("MW_MUSIC_SMOKE_EVIDENCE");
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("Set MW_MUSIC_SMOKE_EVIDENCE.");
            Directory.CreateDirectory(directory);
            SessionState.SetString(Output, directory);
            string scene = Environment.GetEnvironmentVariable("MW_MUSIC_SMOKE_SCENE");
            EditorSceneManager.OpenScene(string.IsNullOrEmpty(scene) ? DrivingGameFlowBuilder.BootScenePath : scene);
            // This is an isolated test project; no scene rebuild or production asset edits.
            SessionState.SetBool(Running, true);
            Subscribe();
            EditorApplication.isPlaying = true;
        }

        private static void Subscribe()
        {
            directory = SessionState.GetString(Output, string.Empty);
            deadline = EditorApplication.timeSinceStartup + 90;
            finishing = advanced = navigated = false; firstSampleAt = 0; titleStarted = -1; captureIndex = 0; peakRms = 0;
            checks.Clear(); source = null; overlay = null;
            locationStage = 0; stageStarted = 0;
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (finishing) return;
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Timed out waiting for music: " + Diagnostic());
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                var application = GameFlowRuntime.Instance;
                if (application?.Flow == null) return;
                if (locationStage != 0) { PollLocation(application); return; }
                if (application.Flow.State == GameFlowState.Boot)
                {
                    if (titleStarted < 0) titleStarted = Time.unscaledTimeAsDouble;
                    Require(AdaptiveMusic.Instance == null || string.IsNullOrEmpty(AdaptiveMusic.Instance.ActiveSectionId), "Title screen started a music section.");
                    var playing = application.GetComponentsInChildren<AudioSource>().Where(x => x.clip != null && x.isPlaying).ToArray();
                    Require(playing.Length == 0, "Title screen produced audio: " + string.Join(", ", playing.Select(x =>
                        x.name + "/" + x.clip?.name + " bypass=" + x.ignoreListenerPause + " gain=" + x.volume)));
                    Require(AdaptiveMusic.Instance?.AudioWorld.ActiveVoices == 0, "Title screen admitted music voices.");
                    if (Time.unscaledTimeAsDouble - titleStarted < 1.5) return;
                    checks.Add("Title screen remained silent for 1.5 seconds before opening the main menu.");
                    application.Flow.CompleteBoot();
                    deadline = EditorApplication.timeSinceStartup + 25;
                }
                var director = AdaptiveMusic.Instance;
                if (director == null || !director.HasStarted || string.IsNullOrEmpty(director.ActiveSectionId)) return;
                Require(Time.timeScale == 0 && AudioListener.pause, "The frontend must keep gameplay and its audio paused.");
                Require(UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(x => x.isActiveAndEnabled) == 1,
                    "Expected exactly one active audio listener.");
                if (source == null)
                {
                    source = director.AudioWorld.GetComponentsInChildren<AudioSource>().FirstOrDefault(x => x.clip != null && x.loop && x.ignoreListenerPause);
                    if (source == null || !source.isPlaying) return;
                    overlay = UnityEngine.Object.FindAnyObjectByType<MostWantedMusicOverlay>();
                    Require(overlay != null, "The music announcement is missing.");
                    // Rounded UITK masks need a stencil buffer, including in captures.
                    target = new RenderTexture(1536, 992, 24, RenderTextureFormat.ARGB32);
                    target.Create();
                    var capturePanel = overlay.GetComponent<UIDocument>().panelSettings;
                    capturePanel.targetTexture = target;
                    capturePanel.clearColor = true; capturePanel.colorClearValue = Color.clear;
                    firstSampleAt = Time.unscaledTimeAsDouble;
                    initialDsp = AudioSettings.dspTime; initialSample = source.timeSamples;
                    initialPosition = director.Transport.cuePositionSeconds;
                    checks.Add("Startup used the saved scene, published soundtrack and a single active listener.");
                }
                AudioListener.GetOutputData(samples, 0);
                double sum = 0; foreach (float sample in samples) sum += sample * sample;
                peakRms = Mathf.Max(peakRms, (float)Math.Sqrt(sum / samples.Length));
                if (!advanced && Time.unscaledTimeAsDouble - firstSampleAt > 1.5)
                {
                    Require(source.isPlaying && source.timeSamples > initialSample + 1000, "Music samples did not advance.");
                    Require(director.Transport.cuePositionSeconds > initialPosition + 1, "Music transport did not advance.");
                    Require(Math.Abs(AudioSettings.dspTime - initialDsp) < .05, "The paused DSP clock unexpectedly advanced.");
                    Require(peakRms > .00001f, "The audio listener output remained silent.");
                    advanced = true;
                    checks.Add("Music samples and transport advance while the DSP clock and gameplay remain paused.");
                    checks.Add("Nonzero listener output measured; peak RMS=" + peakRms.ToString("G6"));
                }
                float age = overlay.Card.AnimationAge;
                if (!navigated && age > 2)
                {
                    var screen = application.GetComponent<GameFlowScreen>();
                    screen.OpenPage(MostWantedFrontendPage.Credits);
                    navigated = true;
                    checks.Add("Opened Credits during the same track announcement.");
                }
                if (captureIndex < captureAges.Length && age >= captureAges[captureIndex])
                {
                    Capture("card-" + captureIndex + "-age-" + age.ToString("F2") + ".png");
                    if (captureIndex == 2)
                    {
                        Require(overlay.Card.PanelReveal > .99f, "Announcement did not expand.");
                        Require(!string.IsNullOrEmpty(overlay.Card.Q<Label>("ea-trax-title").text), "Track title is missing.");
                        Require(overlay.Card.Q<Label>("ea-trax-album").text == "Megadef", "Reference album metadata is missing.");
                    }
                    captureIndex++;
                }
                if (age > 6.3f)
                {
                    Require(advanced && navigated && captureIndex == captureAges.Length, "Playback or animation checks did not finish.");
                    Require(overlay.Card.style.display.value == DisplayStyle.None, "Announcement failed to disappear.");
                    checks.Add("Announcement expands, survives page navigation, retracts and disappears without stopping music.");
                    var driver = UnityEngine.Object.FindAnyObjectByType<MostWantedFrontendTestDriver>();
                    if (driver == null) Finish(true, string.Empty);
                    else
                    {
                        deadline = EditorApplication.timeSinceStartup + 60;
                        locationStage = 1; source = null;
                        driver.RequestPage(MostWantedFrontendPage.Safehouse);
                    }
                }
            }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }

        private static void PollLocation(GameFlowRuntime application)
        {
            bool safehouse = locationStage == 1;
            var driver = UnityEngine.Object.FindAnyObjectByType<MostWantedFrontendTestDriver>();
            Require(driver != null && !driver.Status.StartsWith("Frontend test failed:"), driver?.Status);
            if (!driver.IsReady || application.AllowsFrontendMusic != safehouse) return;
            var director = AdaptiveMusic.Instance;
            if (director == null || string.IsNullOrEmpty(director.ActiveSectionId)) return;
            if (source == null)
            {
                source = director.AudioWorld.GetComponentsInChildren<AudioSource>().FirstOrDefault(x =>
                    x.clip != null && x.loop && x.ignoreListenerPause == safehouse && x.isPlaying);
                if (source == null) return;
                stageStarted = Time.unscaledTimeAsDouble; initialSample = source.timeSamples; initialDsp = AudioSettings.dspTime;
            }
            if (Time.unscaledTimeAsDouble - stageStarted < 1.2) return;
            Require(source.isPlaying && source.timeSamples > initialSample + 1000, "Music stopped on the " + (safehouse ? "safehouse" : "free roam") + " boundary.");
            Require(AudioListener.pause == safehouse && (Time.timeScale == 0) == safehouse, "Incorrect simulation/audio pause state.");
            Require(Math.Abs(AudioSettings.dspTime - initialDsp) < .05 == safehouse, "Incorrect transport clock after changing location.");
            Require(UnityEngine.Object.FindObjectsByType<AudioListener>().Count(x => x.isActiveAndEnabled) == 1, "Duplicate active listeners after loading a world.");
            // The same menu track continues into a safehouse without repeating an
            // expired announcement. Driving changes the presentation and announces it.
            if (!safehouse) Require(overlay.Card.PanelReveal > .99f, "Driving announcement did not expand.");
            Capture(safehouse ? "safehouse-card.png" : "free-roam-card.png");
            checks.Add((safehouse ? "Safehouse" : "Free roam") + " music advances with one listener and the appropriate pause state.");
            if (!safehouse) { Finish(true, string.Empty); return; }
            Require(application.Flow.Execute(GameFlowCommand.Continue).Succeeded, "Could not leave the test safehouse.");
            locationStage = 2; source = null;
        }

        private static void Capture(string name)
        {
            var previous = RenderTexture.active;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(directory, name), pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(pixels); }
        }

        private static string Diagnostic()
        {
            var director = AdaptiveMusic.Instance;
            var trace = new List<AdaptiveMusicTraceEntry>(); director?.CopyTrace(trace);
            return "flow=" + GameFlowRuntime.Instance?.Flow?.State + " started=" + director?.HasStarted
                + " enabled=" + director?.isActiveAndEnabled + " unscaled=" + Time.unscaledTimeAsDouble
                + " dsp=" + AudioSettings.dspTime + " error=" + director?.LastError
                + " transport=" + JsonUtility.ToJson(director?.Transport)
                + " trace=" + string.Join("\n", trace.Select(x => JsonUtility.ToJson(x)));
        }

        private static void Finish(bool passed, string failure)
        {
            finishing = true; SessionState.SetBool(Running, false); EditorApplication.update -= Poll;
            File.WriteAllText(Path.Combine(directory, "result.json"), JsonUtility.ToJson(new Result
            { passed = passed, failure = failure, checks = checks.ToArray(), peakRms = peakRms }, true));
            if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
            Debug.Log("MUSIC_PLAYBACK_SMOKE " + (passed ? "PASS" : "FAIL " + failure));
            EditorApplication.Exit(passed ? 0 : 1);
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        [Serializable] private sealed class Result { public bool passed; public string failure; public string[] checks; public float peakRms; }
    }
}
#endif
