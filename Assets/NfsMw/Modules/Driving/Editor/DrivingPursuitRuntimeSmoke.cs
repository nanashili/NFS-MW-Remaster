#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Short CLI-friendly play-mode integration probe for the pursuit scene.
    /// It exercises the public seams rather than inspecting private state.
    /// </summary>
    public static class DrivingPursuitRuntimeSmoke
    {
        private const string PursuitTestScenePath =
            "Assets/NfsMw/Scenes/Tests/PursuitTest.unity";

        private static double startedAt;
        private static bool pursuitStarted;

        public static void Run()
        {
            EditorSceneManager.OpenScene(PursuitTestScenePath);
            pursuitStarted = false;
            startedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            EditorApplication.isPlaying = true;
        }

        private static void Poll()
        {
            if (!EditorApplication.isPlaying)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode
                    || EditorApplication.timeSinceStartup - startedAt < 1.0)
                {
                    return;
                }

                Fail("Unity did not enter play mode for the pursuit smoke test.");
                return;
            }

            VehiclePursuitDirector director =
                Object.FindAnyObjectByType<VehiclePursuitDirector>();
            VehicleBountySystem bounty =
                Object.FindAnyObjectByType<VehicleBountySystem>();
            if (director == null || bounty == null)
            {
                Fail("Pursuit scene is missing its director or bounty system.");
                return;
            }

            if (!pursuitStarted)
            {
                if (!director.TryStartPursuit(out string failure))
                {
                    Fail("Could not start pursuit: " + failure);
                    return;
                }

                pursuitStarted = true;
                return;
            }

            if (EditorApplication.timeSinceStartup - startedAt < 1.5)
            {
                return;
            }

            if (!director.IsActive || director.ActiveUnitCount < 2)
            {
                Fail(
                    "Heat 1 response did not stay active with two units. "
                    + director.Status);
                return;
            }

            if (!director.TrySetHeatLevel(5, out string heatFailure))
            {
                Fail("Could not escalate heat: " + heatFailure);
                return;
            }

            if (director.ActiveUnitCount < 6)
            {
                Fail(
                    "Heat 5 did not activate all six configured units; active count was "
                    + director.ActiveUnitCount);
                return;
            }

            if (!director.TryRecordBountyEvent(
                VehicleBountyEventKind.PropertyDamage,
                1,
                out string bountyFailure))
            {
                Fail("Could not write a bounty fact: " + bountyFailure);
                return;
            }

            if (!bounty.PursuitActive || bounty.CurrentPursuitBounty <= 0)
            {
                Fail("Pursuit bounty did not accumulate during the smoke test.");
                return;
            }

            Debug.Log(
                "Pursuit runtime smoke passed: phase="
                + director.Phase
                + ", heat="
                + director.HeatLevel
                + ", activeUnits="
                + director.ActiveUnitCount
                + ", bounty=$"
                + bounty.CurrentPursuitBounty);
            EditorApplication.update -= Poll;
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(0);
        }

        private static void Fail(string message)
        {
            Debug.LogError(message);
            EditorApplication.update -= Poll;
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(1);
        }
    }
}
#endif
