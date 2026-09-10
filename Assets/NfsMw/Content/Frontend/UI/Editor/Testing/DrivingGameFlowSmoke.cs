#if UNITY_EDITOR
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving.Editor
{
    [InitializeOnLoad]
    public static class DrivingGameFlowSmoke
    {
        private const string Key = "Driving.GameFlowSmoke.Running";
        private static GameFlowRuntime runtime;
        private static Task<FlowResult> operation;
        private static string directory, alias;
        private static int stage, checkpoint, cash;
        private static double deadline;
        private static float pausedCountdown, stageTime;
        private static MissionCourse oldAttempt;

        static DrivingGameFlowSmoke() { if (SessionState.GetBool(Key, false)) Subscribe(); }

        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run game-flow smoke in an isolated Unity batch project.");
            DrivingGameFlowBuilder.Build();
            EditorSceneManager.OpenScene(DrivingGameFlowBuilder.BootScenePath);
            SessionState.SetBool(Key, true); Subscribe(); EditorApplication.isPlaying = true;
        }

        private static void Subscribe()
        {
            stage = 0; runtime = null; operation = null;
            alias = "flow_smoke_" + Guid.NewGuid().ToString("N");
            directory = System.IO.Path.Combine(Application.temporaryCachePath, alias);
            deadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
            SceneManager.sceneLoaded -= ConfigureWorld; SceneManager.sceneLoaded += ConfigureWorld;
        }

        private static void ConfigureWorld(Scene scene, LoadSceneMode mode)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (JsonCareerProfileStorage storage in root.GetComponentsInChildren<JsonCareerProfileStorage>(true)) storage.SetDirectory(directory);
                foreach (FreeRoamTraffic traffic in root.GetComponentsInChildren<FreeRoamTraffic>(true)) traffic.enabled = false;
            }
        }

        private static void Poll()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Game-flow smoke timed out at stage " + stage);
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                runtime = GameFlowRuntime.Instance;
                if (runtime == null || runtime.Flow == null) return;
                GameFlow flow = runtime.Flow;
                switch (stage)
                {
                    case 0:
                        if (flow.State != GameFlowState.MainMenu) return;
                        Require(Time.timeScale == 0 && AudioListener.pause, "Menu must suspend simulation/audio.");
                        Require(runtime.GetComponent<GameFlowScreen>().Root.Q<Button>("resume-career")?.enabledSelf == true, "Main-menu action is missing or permanently disabled.");
                        operation = runtime.EnterCareerAsync(alias, true); Next(); break;
                    case 1:
                        if (!Done()) return;
                        Require(operation.Result.Succeeded, operation.Result.Message);
                        Require(flow.State == GameFlowState.FreeRoam && runtime.WorldSession.CanDrive, "Career did not release driving.");
                        Require(UnityEngine.Object.FindObjectsByType<FreeRoamSession>(FindObjectsSortMode.None).Length == 1, "Duplicate player/session.");
                        Time.timeScale = 0.65f;
                        Require(flow.Execute(GameFlowCommand.Pause).Succeeded, "Pause rejected.");
                        Require(Time.timeScale == 0 && AudioListener.pause, "Pause did not suspend world/audio.");
                        Require(flow.Execute(GameFlowCommand.Resume).Succeeded && Mathf.Approximately(Time.timeScale, 0.65f), "Resume lost previous time scale.");
                        Time.timeScale = 1;
                        FreeRoamEventDefinition race = FindRace(); MovePlayer(race.transform.position, race.transform.rotation);
                        cash = runtime.WorldSession.Cash;
                        Require(runtime.WorldSession.TryStartEvent(race, out string failure), failure);
                        flow.Refresh(); Require(flow.State == GameFlowState.EventLoading && !runtime.WorldSession.CanDrive, "Preparation did not lock input.");
                        Next(); break;
                    case 2:
                        if (flow.State != GameFlowState.RaceActive) return;
                        Require(runtime.WorldSession.Countdown > 0, "Race skipped countdown.");
                        flow.Execute(GameFlowCommand.Pause); pausedCountdown = runtime.WorldSession.Countdown;
                        oldAttempt = runtime.WorldSession.EventProgress; Next(); break;
                    case 3:
                        if (Time.unscaledTime - stageTime < 1) return;
                        Require(runtime.WorldSession.Countdown == pausedCountdown, "Countdown advanced while paused.");
                        Require(flow.Execute(GameFlowCommand.Restart).Succeeded, "Paused race failed restart.");
                        Require(oldAttempt.Outcome == MissionState.Aborted && runtime.WorldSession.EventProgress != oldAttempt, "Restart reused previous attempt.");
                        Require(flow.State == GameFlowState.EventLoading, "Restart skipped preparation.");
                        checkpoint = -1; Next(); break;
                    case 4:
                        if (flow.State == GameFlowState.EventLoading || runtime.WorldSession.Countdown > 0) return;
                        Require(runtime.WorldSession.CanDrive, "Countdown did not release input."); Next(); break;
                    case 5:
                        if (flow.State != GameFlowState.Results)
                        {
                            var progress = runtime.WorldSession.EventProgress;
                            if (checkpoint == progress.CheckpointsPassed) return;
                            checkpoint = progress.CheckpointsPassed;
                            MovePlayer(progress.NextCheckpoint, Quaternion.identity); return;
                        }
                        Require(runtime.WorldSession.Cash == cash + FindRace().Reward, "Race payout mismatch.");
                        Require(runtime.WorldSession.LastRaceResult?.CashAwarded == FindRace().Reward, "Missing immutable result receipt.");
                        Require(Time.timeScale == 0, "Results did not freeze simulation.");
                        Require(runtime.GetComponent<GameFlowScreen>().Root.Q<Button>("continue") != null, "Results view missing.");
                        Require(flow.Execute(GameFlowCommand.Continue).Succeeded, "Results continuation rejected.");
                        Require(runtime.WorldSession.Cash == cash + FindRace().Reward, "Continuation duplicated payout.");
                        cash = runtime.WorldSession.Cash; operation = flow.ReturnToMenuAsync(); Next(); break;
                    case 6:
                        if (!Done()) return;
                        Require(operation.Result.Succeeded, operation.Result.Message);
                        Require(flow.State == GameFlowState.MainMenu && flow.Session == null, "World lease survived return to menu.");
                        Require(!SceneManager.GetSceneByPath(runtime.Settings.WorldScenePath).isLoaded, "World scene was not unloaded.");
                        operation = runtime.EnterCareerAsync(alias, false); Next(); break;
                    case 7:
                        if (!Done()) return;
                        Require(operation.Result.Succeeded, operation.Result.Message);
                        Require(runtime.WorldSession.Cash == cash, "Career did not survive scene unload/reload.");
                        operation = flow.ReturnToMenuAsync(); Next(); break;
                    case 8:
                        if (!Done()) return;
                        Require(operation.Result.Succeeded, operation.Result.Message);
                        operation = runtime.EnterCareerAsync(alias, true); Next(); break;
                    case 9:
                        if (!Done()) return;
                        Require(!operation.Result.Succeeded && flow.State == GameFlowState.MainMenu, "Existing alias was overwritten.");
                        Require(!SceneManager.GetSceneByPath(runtime.Settings.WorldScenePath).isLoaded, "Rejected alias leaked a scene.");
                        operation = runtime.EnterCareerAsync(alias, false); runtime.CancelLoading(); Next(); break;
                    case 10:
                        if (!Done()) return;
                        Require(!operation.Result.Succeeded && flow.State == GameFlowState.MainMenu, "Cancellation did not restore menu.");
                        Require(!SceneManager.GetSceneByPath(runtime.Settings.WorldScenePath).isLoaded, "Cancelled load leaked a scene.");
                        Debug.Log("Game flow smoke passed: boot/menu UI, exclusive alias creation, scene load, pause/time/audio restore, event preparation/countdown, restart, results/reward, save/unload/resume, duplicate alias rollback and Unity load cancellation.");
                        Finish(0); break;
                }
            }
            catch (Exception exception) { Debug.LogException(exception); Finish(1); }
        }

        private static bool Done() { if (operation == null || !operation.IsCompleted) return false; if (operation.IsFaulted) throw operation.Exception; return true; }
        private static FreeRoamEventDefinition FindRace()
        { foreach (var race in runtime.WorldSession.Events) if (race.Kind == FreeRoamEventKind.Drag) return race; throw new InvalidOperationException("Drag event missing."); }
        private static void MovePlayer(Vector3 position, Quaternion rotation)
        {
            var body = runtime.WorldSession.GetComponent<VehicleController>().Body;
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            body.position = position + Vector3.up * 0.8f; body.rotation = rotation;
            body.transform.SetPositionAndRotation(body.position, rotation); Physics.SyncTransforms();
        }
        private static void Next() { stage++; stageTime = Time.unscaledTime; }
        private static void Require(bool condition, string failure) { if (!condition) throw new InvalidOperationException(failure); }
        private static void Finish(int code)
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Poll; SceneManager.sceneLoaded -= ConfigureWorld;
            EditorApplication.Exit(code);
        }
    }
}
#endif
