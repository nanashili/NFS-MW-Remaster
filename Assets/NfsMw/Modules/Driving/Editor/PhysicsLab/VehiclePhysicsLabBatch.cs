using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    [Serializable]
    internal sealed class VehiclePhysicsLabBatchReport
    {
        public int schema = 1;
        public string command;
        public string unityVersion;
        public string platform;
        public string outputPath;
        public string status;
        public VehiclePhysicsLabRunReport[] reports = Array.Empty<VehiclePhysicsLabRunReport>();
    }

    [Serializable]
    internal sealed class VehiclePhysicsLabBatchError
    {
        public string status = "Invalid";
        public string error;
    }

    /// <summary>
    /// Bounded command-line entry point. It is deliberately separate from the
    /// Unity Test Framework runner: this executes authored lab scenarios and
    /// emits their raw physics reports, while EditMode tests verify contracts.
    /// </summary>
    [InitializeOnLoad]
    public static class VehiclePhysicsLabBatch
    {
        private static bool started;

        static VehiclePhysicsLabBatch()
        {
            if (Application.isBatchMode && HasArgument("-physicsLabBatch"))
                EditorApplication.delayCall += RunCommandLine;
        }

        public static void RunCommandLine()
        {
            if (started) return;
            started = true;
            if (!Application.isBatchMode)
            {
                Debug.LogWarning("PHYSICS_LAB_BATCH: use Unity -batchmode or run the EditMode tests from the Test Runner.");
                return;
            }

            int exitCode = 1;
            try
            {
                List<VehiclePhysicsLabRunReport> reports = Execute(out string outputPath);
                var envelope = new VehiclePhysicsLabBatchReport
                {
                    command = string.Join(" ", Environment.GetCommandLineArgs()),
                    unityVersion = Application.unityVersion,
                    platform = Application.platform.ToString(),
                    outputPath = outputPath,
                    reports = reports.ToArray()
                };
                envelope.status = reports.All(report => report != null && report.Passed)
                    ? "Passed"
                    : reports.Any(report => report != null && report.status == VehiclePhysicsLabResultStatus.Incomplete)
                        ? "Incomplete"
                        : "Failed";
                WriteOutputs(envelope, outputPath);
                exitCode = envelope.status == "Passed" ? 0 : envelope.status == "Incomplete" ? 2 : 1;
            }
            catch (Exception exception)
            {
                Debug.LogError("PHYSICS_LAB_BATCH: " + exception);
                string fallback = GetOption("-physicsLabOutput");
                if (!string.IsNullOrEmpty(fallback))
                {
                    try
                    {
                        string jsonPath = ResolveJsonPath(fallback, "physics-lab-batch-error");
                        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath));
                        File.WriteAllText(jsonPath, JsonUtility.ToJson(new VehiclePhysicsLabBatchError
                        {
                            error = exception.ToString()
                        }, true));
                    }
                    catch (Exception writeException)
                    {
                        Debug.LogError("PHYSICS_LAB_BATCH_OUTPUT: " + writeException.Message);
                    }
                }
            }
            finally
            {
                EditorApplication.Exit(exitCode);
            }
        }

        private static List<VehiclePhysicsLabRunReport> Execute(out string outputPath)
        {
            string outputArgument = GetRequiredOption("-physicsLabOutput");
            outputPath = ResolveJsonPath(outputArgument, "physics-lab-batch");
            float timeoutSeconds = ParseFloat(GetOption("-physicsLabTimeoutSeconds", "300"), 1f, 3600f, "-physicsLabTimeoutSeconds");
            int[] seeds = ParseSeeds(GetOption("-physicsLabSeeds"));

            VehiclePhysicsLabSuite suite = LoadAsset<VehiclePhysicsLabSuite>(GetOption("-physicsLabSuite"));
            VehiclePhysicsLabDefinition single = LoadAsset<VehiclePhysicsLabDefinition>(GetOption("-physicsLabDefinition"));
            if (suite == null && single == null)
                throw new ArgumentException("Specify exactly one valid -physicsLabDefinition or -physicsLabSuite asset path/GUID.");
            if (suite != null && single != null)
                throw new ArgumentException("Specify only one of -physicsLabDefinition or -physicsLabSuite.");
            if (suite != null && !suite.IsValid(out string suiteFailure)) throw new ArgumentException(suiteFailure);
            if (single != null && !single.IsValid(out string singleFailure)) throw new ArgumentException(singleFailure);

            var reports = new List<VehiclePhysicsLabRunReport>();
            if (suite != null)
            {
                for (int experiment = 0; experiment < suite.experiments.Length; experiment++)
                    for (int repetition = 0; repetition < suite.repetitions; repetition++)
                    {
                        if (!RunScenario(suite.experiments[experiment], seeds, timeoutSeconds, reports))
                            if (suite.stopOnFailure) return reports;
                    }
            }
            else
            {
                RunScenario(single, seeds, timeoutSeconds, reports);
            }
            return reports;
        }

        private static bool RunScenario(
            VehiclePhysicsLabDefinition source,
            int[] seeds,
            float timeoutSeconds,
            List<VehiclePhysicsLabRunReport> reports)
        {
            int[] selectedSeeds = seeds.Length == 0 ? new[] { source.seed } : seeds;
            for (int i = 0; i < selectedSeeds.Length; i++)
            {
                VehiclePhysicsLabDefinition scenario = source;
                bool transient = selectedSeeds[i] != source.seed;
                if (transient)
                {
                    scenario = UnityEngine.Object.Instantiate(source);
                    scenario.hideFlags = HideFlags.HideAndDontSave;
                    scenario.seed = selectedSeeds[i];
                }
                VehiclePhysicsLabRunReport report = RunOne(scenario, timeoutSeconds);
                if (transient) UnityEngine.Object.DestroyImmediate(scenario);
                reports.Add(report);
                if (report == null || !report.Passed) return false;
            }
            return true;
        }

        private static VehiclePhysicsLabRunReport RunOne(VehiclePhysicsLabDefinition definition, float timeoutSeconds)
        {
            VehiclePhysicsLabRunner runner = null;
            try
            {
                runner = new VehiclePhysicsLabRunner(definition, () => VehicleInputState.Neutral);
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (!runner.IsDone)
                {
                    runner.Advance(25d);
                    if (timer.Elapsed.TotalSeconds > timeoutSeconds)
                    {
                        runner.MarkIncomplete("BATCH_TIMEOUT", "The command-line wall-clock timeout elapsed; the partial report is incomplete.");
                        break;
                    }
                }
                return runner.Report;
            }
            catch (Exception exception)
            {
                return new VehiclePhysicsLabRunReport
                {
                    definitionId = definition == null ? string.Empty : definition.id,
                    experiment = definition == null ? string.Empty : definition.experiment.ToString(),
                    inputMode = definition == null || definition.input == null ? string.Empty : definition.input.mode.ToString(),
                    engineVersion = Application.unityVersion,
                    platform = Application.platform.ToString(),
                    status = VehiclePhysicsLabResultStatus.Invalid,
                    complete = false,
                    failureCode = "BATCH_EXCEPTION",
                    failureMessage = exception.Message,
                    diagnostics = new[] { exception.ToString() },
                    metrics = Array.Empty<VehiclePhysicsLabMetric>(),
                    samples = Array.Empty<VehiclePhysicsLabSample>(),
                    collisions = Array.Empty<VehiclePhysicsLabCollisionEvent>()
                };
            }
            finally
            {
                runner?.Dispose();
            }
        }

        private static void WriteOutputs(VehiclePhysicsLabBatchReport envelope, string outputPath)
        {
            string directory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("The output path has no directory.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(outputPath, JsonUtility.ToJson(envelope, true));

            string csvPath = Path.ChangeExtension(outputPath, ".csv");
            var csv = new StringBuilder();
            for (int i = 0; i < envelope.reports.Length; i++)
            {
                VehiclePhysicsLabRunReport report = envelope.reports[i];
                if (report == null) continue;
                csv.AppendLine("# run=" + report.runId + ",status=" + report.status + ",experiment=" + report.experiment);
                csv.Append(VehiclePhysicsLabEditorOperations.ReportCsv(report));
            }
            File.WriteAllText(csvPath, csv.ToString());
            Debug.Log("PHYSICS_LAB_BATCH_OUTPUT: " + outputPath + " and " + csvPath + " · " + envelope.status);
        }

        private static int[] ParseSeeds(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Array.Empty<int>();
            string[] tokens = value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 128) throw new ArgumentException("-physicsLabSeeds accepts at most 128 seeds.");
            var result = new int[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
                if (!int.TryParse(tokens[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result[i]))
                    throw new ArgumentException("Invalid seed: " + tokens[i]);
            return result;
        }

        private static float ParseFloat(string value, float minimum, float maximum, string option)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
                || float.IsNaN(result) || float.IsInfinity(result) || result < minimum || result > maximum)
                throw new ArgumentException(option + " must be a finite value in [" + minimum + ", " + maximum + "].");
            return result;
        }

        private static T LoadAsset<T>(string value) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string path = value;
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) path = AssetDatabase.GUIDToAssetPath(value);
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Could not resolve asset path or GUID: " + value);
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new ArgumentException("Asset is not a " + typeof(T).Name + ": " + path);
            return asset;
        }

        private static string ResolveJsonPath(string value, string defaultName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("-physicsLabOutput is required and must be explicit.");
            string path = value;
            if (Directory.Exists(path) || string.IsNullOrEmpty(Path.GetExtension(path)))
                path = Path.Combine(path, defaultName + ".json");
            return Path.GetFullPath(path);
        }

        private static bool HasArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            return args.Any(value => string.Equals(value, name, StringComparison.Ordinal));
        }

        private static string GetOption(string name, string fallback = null)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.Ordinal)) return args[i + 1];
            return fallback;
        }

        private static string GetRequiredOption(string name)
        {
            string value = GetOption(name);
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(name + " is required.");
            return value;
        }
    }
}
