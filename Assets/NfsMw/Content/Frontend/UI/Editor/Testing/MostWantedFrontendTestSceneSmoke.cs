#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>Batch-only Play Mode traversal of the generated frontend scene and its production UI.</summary>
    [InitializeOnLoad]
    public static class MostWantedFrontendTestSceneSmoke
    {
        private const string RunningKey = "NFS.Frontend.SceneSmoke.Running";
        private const string OutputKey = "NFS.Frontend.SceneSmoke.Output";
        private const string PrefsKey = "NFS.Frontend.SceneSmoke.Prefs";
        private static readonly string[] PreferenceKeys =
        { MostWantedFrontendPlayerPrefsStore.FrontendKey, MostWantedFrontendPlayerPrefsStore.SensoryKey };
        private static readonly List<string> errors = new List<string>();
        private static readonly List<string> editorInfrastructureIssues = new List<string>();
        private static readonly List<string> passedPages = new List<string>();
        private static double deadline;
        private static int index, settleFrame;
        private static bool requested, finishing;
        private static string directory;
        private static RenderTexture capture;
        private static PanelSettings capturePanel;
        private static RenderTexture previousTarget;

        static MostWantedFrontendTestSceneSmoke()
        {
            if (SessionState.GetBool(RunningKey, false)) Subscribe();
        }

        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".mw-frontend-validation-source"))
                throw new InvalidOperationException("Run this smoke only in the owned, isolated frontend validation project.");
            directory = Environment.GetEnvironmentVariable("MW_FRONTEND_SCENE_SMOKE_EVIDENCE");
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("Set MW_FRONTEND_SCENE_SMOKE_EVIDENCE.");
            Directory.CreateDirectory(directory);
            SessionState.SetString(OutputKey, directory);
            var snapshot = new PreferenceSnapshot();
            for (int i = 0; i < PreferenceKeys.Length; i++)
            {
                snapshot.exists[i] = PlayerPrefs.HasKey(PreferenceKeys[i]);
                snapshot.values[i] = PlayerPrefs.GetString(PreferenceKeys[i], string.Empty);
            }
            SessionState.SetString(PrefsKey, JsonUtility.ToJson(snapshot));
            try
            {
                MostWantedFrontendTestSceneBuilder.BuildBatch();
                EditorSceneManager.OpenScene(MostWantedFrontendTestSceneBuilder.GalleryPath);
                SessionState.SetBool(RunningKey, true);
                Subscribe();
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                WriteResult(false, exception.Message);
                EditorApplication.Exit(1);
            }
        }

        private static void Subscribe()
        {
            directory = SessionState.GetString(OutputKey, string.Empty);
            deadline = EditorApplication.timeSinceStartup + 180;
            index = 0; settleFrame = -1; requested = false; finishing = false;
            errors.Clear(); editorInfrastructureIssues.Clear(); passedPages.Clear();
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
            Application.logMessageReceived -= Log; Application.logMessageReceived += Log;
        }

        private static void Log(string message, string stack, LogType type)
        {
            if (!finishing && (type == LogType.Exception || type == LogType.Error || type == LogType.Assert))
            {
                // The installed Editor's QuickSearch startup can fail independently of any game
                // code. Preserve that exact infrastructure exception in the report, rather than
                // treating it as either a successful editor check or a frontend runtime failure.
                if (stack.Contains("UnityEditor.Search.SearchDatabase")
                    && stack.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup")
                    && !stack.Contains("Assets/NfsMw/Modules/Driving/"))
                    editorInfrastructureIssues.Add(message + "\n" + stack);
                else errors.Add(message + "\n" + stack);
            }
        }

        private static void Poll()
        {
            if (finishing) return;
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Scene smoke timed out at page index " + index);
                Require(errors.Count == 0, errors.Count == 0 ? string.Empty : errors[0]);
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                var driver = UnityEngine.Object.FindAnyObjectByType<MostWantedFrontendTestDriver>();
                if (driver == null || driver.ApplicationRuntime == null) return;
                Require(!driver.Status.StartsWith("Frontend test failed:", StringComparison.Ordinal), driver.Status);
                var page = MostWantedFrontendTestDriver.Pages[index];
                if (!requested)
                {
                    if (!driver.IsReady) return;
                    driver.RequestPage(page); requested = true;
                    return;
                }
                if (!driver.IsReady) return;
                var runtime = driver.ApplicationRuntime;
                var screen = runtime.GetComponent<GameFlowScreen>();
                Require(screen != null && screen.IsVisible, "Frontend document is missing or hidden: " + page);
                Require(screen.Navigation.Current.Page == page, "Wrong page: expected " + page + ", got " + screen.CurrentPage);
                if (settleFrame < 0)
                {
                    settleFrame = Time.frameCount;
                    if (ShouldCapture(page) && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                    {
                        RenderShowroomForCapture(runtime.GetComponent<MostWantedShowroom>());
                        BeginCapture(runtime.GetComponent<UIDocument>());
                    }
                    return;
                }
                if (Time.frameCount < settleFrame + 3)
                {
                    if (capture != null) RenderShowroomForCapture(runtime.GetComponent<MostWantedShowroom>());
                    EditorApplication.QueuePlayerLoopUpdate(); return;
                }
                ValidatePage(page, runtime, screen);
                if (capture != null) CaptureShowroom(runtime.GetComponent<MostWantedShowroom>(), page);
                if (capture != null) CompleteCapture(page);
                passedPages.Add(page.ToString());
                Debug.Log("FRONTEND_PAGE_SMOKE_PASS " + page);
                index++;
                if (index == MostWantedFrontendTestDriver.Pages.Length)
                {
                    VerifyPreferenceStorage();
                    Finish(true, string.Empty);
                    return;
                }
                requested = false; settleFrame = -1;
            }
            catch (Exception exception) { Finish(false, exception.Message); }
        }

        private static void ValidatePage(MostWantedFrontendPage page, GameFlowRuntime runtime, GameFlowScreen screen)
        {
            Require(screen.Root.Q("mw-content") != null && screen.Root.Q("mw-command-bar") != null,
                "Frontend content or command bar missing: " + page);
            Require(!runtime.AllowsWorldInput, "The UI fixture leaked driving input: " + page);
            Require(Time.timeScale == 0 && AudioListener.pause, "The UI fixture did not suspend gameplay: " + page);
            Require(MapInputFocus.Captured, "The frontend did not own its input: " + page);
            var showroom = runtime.GetComponent<MostWantedShowroom>();
            Require(showroom != null && showroom.HasPublishedVehicle, "Renderer-only car publication missing.");
            if (MostWantedFrontendNavigation.IsSettings(page))
                Require(runtime.Preferences.IsEditing && screen.Root.Q<Label>("preferences-status") != null,
                    "Preferences did not open a transaction: " + page);
            if (page == MostWantedFrontendPage.Controls)
                Require(screen.Root.Q<Button>("pref-binding-Throttle-0") != null, "Controls table is missing the primary accelerator binding.");
            if (page == MostWantedFrontendPage.Paint)
                Require(screen.Root.Q<Button>("paint-79") != null && runtime.WorldSession.Cash == 100000,
                    "Paint swatches or isolated wallet are missing.");
            if (page == MostWantedFrontendPage.Music)
                Require(screen.Root.Q<Button>("music-section-0") != null, "Diagnostic music catalog is missing.");
            if (MostWantedFrontendTestDriver.NeedsWorld(page))
            {
                var profile = runtime.WorldSession.GetComponent<CareerProfileSystem>();
                Require(profile.Storage is MostWantedFrontendTestStorage, "A fixture used persistent career storage.");
                Require(runtime.WorldSession.Events.Count == 17 && runtime.WorldSession.Locations.Count == 9,
                    "Fixture world content was not restored.");
            }
        }

        private static bool ShouldCapture(MostWantedFrontendPage page) => page == MostWantedFrontendPage.Safehouse
            || page == MostWantedFrontendPage.Audio || page == MostWantedFrontendPage.Controls || page == MostWantedFrontendPage.Paint;

        private static void BeginCapture(UIDocument document)
        {
            capturePanel = document.panelSettings;
            previousTarget = capturePanel.targetTexture;
            capture = new RenderTexture(1536, 992, 0, RenderTextureFormat.ARGB32) { name = "Frontend smoke capture" };
            capture.Create();
            capturePanel.targetTexture = capture;
        }

        private static void RenderShowroomForCapture(MostWantedShowroom showroom)
        {
            // Batch Editor sessions do not necessarily paint a Game View. Request the offscreen
            // HDRP camera explicitly for evidence; normal game rendering remains camera-driven.
            var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = showroom.Target };
            Require(UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(showroom.PreviewCamera, request),
                "The active render pipeline does not support the showroom capture request.");
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(showroom.PreviewCamera, request);
        }

        private static void CompleteCapture(MostWantedFrontendPage page)
        {
            RenderTexture active = RenderTexture.active;
            var pixels = new Texture2D(capture.width, capture.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = capture;
                pixels.ReadPixels(new Rect(0, 0, capture.width, capture.height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(directory, page + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = active;
                UnityEngine.Object.Destroy(pixels);
                ReleaseCapture();
            }
        }

        private static void CaptureShowroom(MostWantedShowroom showroom, MostWantedFrontendPage page)
        {
            var target = showroom.Target;
            RenderTexture active = RenderTexture.active;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(directory, page + "-showroom.png"), pixels.EncodeToPNG());
                Debug.Log("FRONTEND_SHOWROOM_STATE cameraEnabled=" + showroom.PreviewCamera.enabled
                    + " stageActive=" + showroom.Stage.activeInHierarchy + " position=" + showroom.PreviewCamera.transform.position
                    + " rendererCount=" + showroom.Stage.GetComponentsInChildren<Renderer>().Length);
            }
            finally { RenderTexture.active = active; UnityEngine.Object.Destroy(pixels); }
        }

        private static void ReleaseCapture()
        {
            if (capturePanel != null) capturePanel.targetTexture = previousTarget;
            if (capture != null) { capture.Release(); UnityEngine.Object.Destroy(capture); }
            capture = null; capturePanel = null; previousTarget = null;
        }

        private static void VerifyPreferenceStorage()
        {
            var snapshot = JsonUtility.FromJson<PreferenceSnapshot>(SessionState.GetString(PrefsKey, string.Empty));
            for (int i = 0; i < PreferenceKeys.Length; i++)
                Require(PlayerPrefs.HasKey(PreferenceKeys[i]) == snapshot.exists[i]
                    && PlayerPrefs.GetString(PreferenceKeys[i], string.Empty) == snapshot.values[i],
                    "The test scene changed persistent preferences: " + PreferenceKeys[i]);
        }

        private static void Finish(bool success, string failure)
        {
            if (finishing) return;
            finishing = true;
            ReleaseCapture();
            WriteResult(success, failure);
            SessionState.SetBool(RunningKey, false);
            EditorApplication.update -= Poll; Application.logMessageReceived -= Log;
            if (success) Debug.Log("FRONTEND_SCENE_SMOKE_PASSED pages=" + passedPages.Count);
            else Debug.LogError("FRONTEND_SCENE_SMOKE_FAILED " + failure);
            EditorApplication.Exit(success ? 0 : 1);
        }

        private static void WriteResult(bool success, string failure)
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            File.WriteAllText(Path.Combine(directory, "scene-smoke.json"), JsonUtility.ToJson(new Report
            {
                succeeded = success, failure = failure, pages = passedPages.ToArray(),
                errors = errors.ToArray(), graphicsDevice = SystemInfo.graphicsDeviceType.ToString(),
                editorInfrastructureIssues = editorInfrastructureIssues.ToArray(),
                unityVersion = Application.unityVersion
            }, true));
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        [Serializable] private sealed class PreferenceSnapshot { public bool[] exists = new bool[2]; public string[] values = new string[2]; }
        [Serializable] private sealed class Report
        {
            public bool succeeded;
            public string failure, unityVersion, graphicsDevice;
            public string scope = "Frontend runtime traversal; unrelated QuickSearch startup exceptions are recorded separately.";
            public string[] pages, errors, editorInfrastructureIssues;
        }
    }
}
#endif
