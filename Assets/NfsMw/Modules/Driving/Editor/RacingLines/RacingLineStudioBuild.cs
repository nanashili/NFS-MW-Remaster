using System;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace NfsMwRemaster.Driving.Editor
{
    public static class RacingLineStudioBuild
    {
        // CLI-only qualification helper. Uses an explicit scene list, never changes the game's build settings.
        public static void BuildMacExample()
        {
            if (!UnityEngine.Application.isBatchMode) throw new InvalidOperationException("Run this build helper in a disposable batch project copy.");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { RacingLineStudioDemo.ScenePath },
                locationPathName = "Temp/RacingLineStudio-Mac.app", target = BuildTarget.StandaloneOSX, options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("RACING_LINE_MAC_BUILD_FAILED: " + report.summary.result + ", errors=" + report.summary.totalErrors);
            UnityEngine.Debug.Log("RACING_LINE_MAC_BUILD_PASSED: " + report.summary.totalTime + "; bytes=" + report.summary.totalSize);
        }
    }
}
