using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public static class RenderingBenchmarkBuild
    {
        [Serializable]
        private sealed class BuildResultReport
        {
            public string result;
            public int errors;
            public int warnings;
            public ulong bytes;
            public double seconds;
        }

        public static void BuildDevelopmentPlayer()
        {
            string output = Argument("-render-build-output");
            if (string.IsNullOrWhiteSpace(output))
                throw new BuildFailedException("Missing -render-build-output <path>.");

            string reportPath = Argument("-render-build-report");
            string outputDirectory = Path.GetDirectoryName(Path.GetFullPath(output));
            if (!string.IsNullOrEmpty(outputDirectory)) Directory.CreateDirectory(outputDirectory);

            var scenePaths = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                if (scene.enabled) scenePaths.Add(scene.path);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenePaths.ToArray(),
                locationPathName = Path.GetFullPath(output),
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development
            });

            var result = new BuildResultReport
            {
                result = report.summary.result.ToString(),
                errors = report.summary.totalErrors,
                warnings = report.summary.totalWarnings,
                bytes = report.summary.totalSize,
                seconds = report.summary.totalTime.TotalSeconds
            };
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                string fullReportPath = Path.GetFullPath(reportPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullReportPath));
                File.WriteAllText(fullReportPath, JsonUtility.ToJson(result, true));
            }

            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Rendering benchmark player build failed: " + report.summary.result);
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
