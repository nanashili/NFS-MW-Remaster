using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using NfsMwRemaster.Driving.Editor;
using NfsMwRemaster.Driving.Editor.AudioAnalysis;
using NfsMwRemaster.Driving.Editor.Workspace;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class BlackBoxWorkspaceTests
    {
        [Serializable] private sealed class UiReport
        { public string scope, unity; public bool guiSamplesAvailable; public int passes; public double totalMilliseconds, maximumMilliseconds; }

        [Test] public void AuditionDisposalAndWorkspaceShutdownRestoreListenersAndLeases()
        {
            var root = new GameObject("BlackBox listener test"); var listener = root.AddComponent<AudioListener>();
            using var audition = new BlackBoxAudition(); var pcm = new float[4410];
            for (int i = 0; i < pcm.Length; i++) pcm[i] = Mathf.Sin(i * 0.05f) * 0.1f;
            int leases = RacingPreviewSessions.Count;
            try
            {
                audition.Play(pcm, 44100, 1, 0, pcm.Length, 0.2f, "Analysis lifecycle test");
                Assert.False(listener.enabled); Assert.AreEqual(leases + 1, RacingPreviewSessions.Count);
                audition.Stop(); Assert.True(listener.enabled); Assert.AreEqual(leases, RacingPreviewSessions.Count);
                audition.Play(pcm, 44100, 1, 0, pcm.Length, 0.2f, "Analysis lifecycle test");
                RacingPreviewSessions.StopAll(); Assert.True(listener.enabled); Assert.AreEqual(0, RacingPreviewSessions.Count);
                audition.Dispose(); audition.Stop(); Assert.True(listener.enabled);
            }
            finally { audition.Stop(); UnityEngine.Object.DestroyImmediate(root); }
        }

        [UnityTest] public IEnumerator AuditionProgressPausesResumesAndReleasesCompletedPlayback()
        {
            var root = new GameObject("BlackBox transport listener"); var listener = root.AddComponent<AudioListener>();
            using var audition = new BlackBoxAudition();
            var pcm = new float[176400];
            for (int i = 0; i < pcm.Length; i++) pcm[i] = Mathf.Sin(i * 0.04f) * 0.1f;
            const int start = 11025, end = 55125;
            int leases = RacingPreviewSessions.Count;
            try
            {
                audition.Toggle(pcm, 44100, 2, start, end, 0, "Transport test");
                Assert.AreEqual(44100, audition.FrameCount, "Progress counts source frames, not interleaved samples.");
                double deadline = EditorApplication.timeSinceStartup + 4;
                while (audition.Frame == 0 && EditorApplication.timeSinceStartup < deadline)
                { EditorApplication.QueuePlayerLoopUpdate(); yield return null; }
                Assert.Greater(audition.Frame, 0, "The actual AudioSource must advance before pause is tested.");
                Assert.True(audition.Active);
                audition.Toggle(pcm, 44100, 2, start, end, 0, "Transport test");
                int pausedFrame = audition.Frame;
                Assert.True(audition.Paused); Assert.False(audition.Active);
                double pausedUntil = EditorApplication.timeSinceStartup + 0.1;
                while (EditorApplication.timeSinceStartup < pausedUntil) yield return null;
                Assert.AreEqual(pausedFrame, audition.Frame); Assert.False(audition.Tick());
                Assert.False(listener.enabled); Assert.AreEqual(leases + 1, RacingPreviewSessions.Count);
                audition.Toggle(pcm, 44100, 2, start, end, 0, "Transport test");
                Assert.False(audition.Paused); Assert.True(audition.Active);
                Assert.GreaterOrEqual(audition.Frame, pausedFrame, "Resume must not restart the clip.");
                deadline = EditorApplication.timeSinceStartup + 4;
                while (audition.Active && EditorApplication.timeSinceStartup < deadline)
                { EditorApplication.QueuePlayerLoopUpdate(); yield return null; }
                Assert.False(audition.Active); Assert.True(audition.Tick());
                Assert.AreEqual(end - start, audition.Frame); Assert.True(listener.enabled);
                Assert.AreEqual(leases, RacingPreviewSessions.Count);
                audition.Toggle(pcm, 44100, 2, start, end, 0, "Replay test");
                Assert.True(audition.Active); Assert.Less(audition.Frame, audition.FrameCount);
                audition.Stop(); Assert.AreEqual(0, audition.Frame); Assert.AreEqual(0, audition.FrameCount);
                Assert.False(audition.Matches(pcm, start, end)); Assert.True(listener.enabled);
            }
            finally { audition.Stop(); UnityEngine.Object.DestroyImmediate(root); }
        }

        [UnityTest] public IEnumerator SharedWindowPreservesSessionAcrossViewsAndMeasuresAvailableGuiPasses()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Window verification requires a graphics device. Rerun without -nographics.");
            var session = ScriptableObject.CreateInstance<BlackBoxSession>();
            var vehicle = ScriptableObject.CreateInstance<VehicleProfileDraft>(); vehicle.audioAnalysis = session;
            string temporary = Path.Combine(Path.GetTempPath(), "black-box-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
            var pcm = new float[4410]; for (int i = 0; i < pcm.Length; i++) pcm[i] = Mathf.Sin(i * 0.06f) * 0.2f;
            using (var stream = File.Create(Path.Combine(temporary, "source.wav"))) BlackBoxWaveExport.Write(stream, pcm, 44100, 1, 0, pcm.Length);
            session.sources.Add(new BlackBoxSource { root = temporary, relativePath = "source.wav" });
            BlackBoxInspectorWindow window = null;
            try
            {
                BlackBoxInspectorWindow.Open(vehicle); window = EditorWindow.GetWindow<BlackBoxInspectorWindow>();
                Assert.AreSame(session, window.Session);
                double deadline = EditorApplication.timeSinceStartup + 15;
                while (window.IsBusy && EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.False(window.IsBusy, "Source inspection did not complete"); yield return null;
                using var gui = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "BlackBoxInspector.OnGUI", 2048, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                // Exercise four workflow steps plus both technical views
                // at both sides of the supported docked-window range.
                foreach (var size in new[] { new Vector2(700, 580), new Vector2(1500, 820) })
                {
                    window.position = new Rect(0, 0, size.x, size.y);
                    for (int view = 0; view < 6; view++)
                    {
                        window.ShowView(view); yield return null;
                        for (int i = 0; i < 6; i++)
                        {
                            window.SendEvent(new Event { type = EventType.Layout });
                            window.SendEvent(new Event { type = EventType.Repaint });
                        }
                        // The technical byte inspector consumes this when focus is clear;
                        // this guards the keyboard path without relying on a private control ID.
                        window.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.RightArrow });
                        Assert.AreEqual(view, window.ActiveView, "Repainting or keyboard input changed the selected workflow");
                        Assert.AreSame(session, window.Session, "View and size changes must preserve the authoring overlay");
                    }
                }
                BlackBoxInspectorWindow.Open(vehicle); Assert.AreSame(window, EditorWindow.GetWindow<BlackBoxInspectorWindow>());
                Assert.AreSame(session, window.Session);
                gui.Stop(); var report = new UiReport { unity = Application.unityVersion, scope = "Batch Edit Mode, four workflow steps plus two technical views at narrow and wide window sizes over a synthetic decoded WAV. Includes explicit Layout/Repaint dispatch, automatic passes and a keyboard event with active-view assertions. No human readability, loaded large-table or input-latency claim.", guiSamplesAvailable = gui.Valid && gui.Count > 0, passes = gui.Count };
                for (int i = 0; i < gui.Count; i++) { double ms = gui.GetSample(i).Value / 1000000.0; report.totalMilliseconds += ms; report.maximumMilliseconds = Math.Max(report.maximumMilliseconds, ms); }
                Directory.CreateDirectory("Library/BlackBoxAudio/Reports"); File.WriteAllText("Library/BlackBoxAudio/Reports/ui-performance.json", JsonUtility.ToJson(report, true));
                TestContext.WriteLine(JsonUtility.ToJson(report, true));
                // Optional private session prepared for this workspace; public tests need no proprietary fixtures.
                var supplied = AssetDatabase.LoadAssetAtPath<BlackBoxSession>("Assets/NfsMw/Modules/Driving/Editor/AudioAnalysis/LocalSessions/BMW M3 source inspection.asset");
                if (supplied != null)
                {
                    string originalRegions = string.Join("\n", supplied.regions.ConvertAll(region => JsonUtility.ToJson(region)));
                    supplied.ValidateSchema(); BlackBoxInspectorWindow.OpenSession(supplied); deadline = EditorApplication.timeSinceStartup + 15;
                    while (window.IsBusy && EditorApplication.timeSinceStartup < deadline) yield return null;
                    Assert.False(window.IsBusy); Assert.AreSame(supplied, window.Session); Assert.AreEqual(5, supplied.sources.Count);
                    Assert.AreEqual(originalRegions, string.Join("\n", supplied.regions.ConvertAll(region => JsonUtility.ToJson(region))), "Opening evidence must preserve the user's existing RPM regions.");
                }
                window.Close(); window = null; yield return null;
                Assert.AreSame(session, vehicle.audioAnalysis, "Window closure must preserve serialisable session ownership");
            }
            finally
            {
                if (window != null) window.Close();
                UnityEngine.Object.DestroyImmediate(vehicle); UnityEngine.Object.DestroyImmediate(session); Directory.Delete(temporary, true);
            }
        }
    }
}
