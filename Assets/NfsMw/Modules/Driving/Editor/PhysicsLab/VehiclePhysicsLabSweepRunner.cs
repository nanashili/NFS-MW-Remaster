using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Sequential, bounded sweep executor. Every trial owns a cloned experiment
    /// and a fresh VehiclePhysicsLabRunner, so controller, wheel and PhysX state
    /// cannot leak between candidates. The source sweep and base experiment are
    /// never mutated.
    /// </summary>
    public sealed class VehiclePhysicsLabSweepRunner : IDisposable
    {
        private readonly VehiclePhysicsLabSweepDefinition definition;
        private readonly Func<VehicleInputState> liveInput;
        private readonly List<VehiclePhysicsLabRunReport> reports = new List<VehiclePhysicsLabRunReport>();
        private readonly string sweepFingerprint;
        private VehiclePhysicsLabDefinition scenario;
        private VehiclePhysicsLabRunner runner;
        private int trialIndex;
        private bool stopped;
        private bool disposed;

        public VehiclePhysicsLabSweepRunner(
            VehiclePhysicsLabSweepDefinition sweep,
            Func<VehicleInputState> liveInputProvider = null)
        {
            definition = sweep ?? throw new ArgumentNullException(nameof(sweep));
            liveInput = liveInputProvider;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("PHYSICS_LAB_PLAY_MODE: run sweeps outside Play mode.");
            if (!definition.IsValid(out string failure)) throw new ArgumentException(failure);
            sweepFingerprint = ComputeFingerprint(definition);
            StartTrial();
        }

        public IReadOnlyList<VehiclePhysicsLabRunReport> Reports => reports;
        public int TrialCount => definition.TrialCount;
        public int CompletedCount => reports.Count;
        public string CurrentLabel { get; private set; }
        public IReadOnlyList<VehiclePhysicsLabSample> LiveSamples => runner == null ? Array.Empty<VehiclePhysicsLabSample>() : runner.LiveSamples;
        public VehiclePhysicsLabRunReport CurrentReport => runner == null ? null : runner.Report;
        public bool IsDone => stopped || disposed || (runner == null && trialIndex >= definition.TrialCount);
        public float Progress
        {
            get
            {
                if (TrialCount <= 0) return 1f;
                float current = runner == null ? 0f : runner.Progress;
                return Mathf.Clamp01((reports.Count + current) / TrialCount);
            }
        }

        public void Advance(double milliseconds = 5d)
        {
            if (IsDone) return;
            if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) || milliseconds <= 0d)
                throw new ArgumentOutOfRangeException(nameof(milliseconds), "The editor work slice must be finite and positive.");

            if (ComputeFingerprint(definition) != sweepFingerprint)
            {
                runner?.MarkIncomplete("STALE_INPUT", "The sweep or its base experiment changed while trials were active.");
                HarvestCurrent();
                stopped = true;
                return;
            }

            runner.Advance(milliseconds);
            if (runner.IsDone) HarvestCurrent();
        }

        public void Cancel()
        {
            if (IsDone) return;
            runner?.Cancel();
            HarvestCurrent();
            stopped = true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            runner?.Dispose();
            runner = null;
            DestroyScenario();
            CurrentLabel = string.Empty;
        }

        private void StartTrial()
        {
            if (stopped || trialIndex >= definition.TrialCount)
            {
                stopped = true;
                return;
            }

            if (!definition.TryGetTrial(trialIndex, out VehiclePhysicsLabTuningOverride[] trialOverrides, out string label))
                throw new InvalidOperationException("PHYSICS_LAB_SWEEP_TRIAL: could not resolve trial " + trialIndex + ".");

            scenario = UnityEngine.Object.Instantiate(definition.baseExperiment);
            scenario.name = definition.baseExperiment.name + " (Physics Lab Sweep " + trialIndex + ")";
            scenario.hideFlags = HideFlags.HideAndDontSave;
            scenario.temporaryOverrides = MergeOverrides(definition.baseExperiment.temporaryOverrides, trialOverrides);
            CurrentLabel = label;
            try
            {
                runner = new VehiclePhysicsLabRunner(scenario, liveInput ?? VehiclePhysicsLabLiveInput.ReadKeyboard);
                runner.Report.sweepId = definition.id;
                runner.Report.sweepTrial = trialIndex;
                runner.Report.sweepLabel = label;
            }
            catch
            {
                DestroyScenario();
                throw;
            }
        }

        private void HarvestCurrent()
        {
            if (runner == null) return;
            VehiclePhysicsLabRunReport completed = runner.Report;
            completed.sweepId = definition.id;
            completed.sweepTrial = trialIndex;
            completed.sweepLabel = CurrentLabel;
            reports.Add(completed);
            bool passed = completed.Passed;
            runner.Dispose();
            runner = null;
            DestroyScenario();
            trialIndex++;
            if (!passed && definition.stopOnFailure)
            {
                stopped = true;
                return;
            }
            if (trialIndex >= definition.TrialCount)
            {
                stopped = true;
                return;
            }
            StartTrial();
        }

        private void DestroyScenario()
        {
            if (scenario != null) UnityEngine.Object.DestroyImmediate(scenario);
            scenario = null;
        }

        private static VehiclePhysicsLabTuningOverride[] MergeOverrides(
            VehiclePhysicsLabTuningOverride[] baseOverrides,
            VehiclePhysicsLabTuningOverride[] trialOverrides)
        {
            var result = new List<VehiclePhysicsLabTuningOverride>();
            if (baseOverrides != null) result.AddRange(baseOverrides);
            if (trialOverrides != null) result.AddRange(trialOverrides);
            return result.ToArray();
        }

        private static string ComputeFingerprint(VehiclePhysicsLabSweepDefinition sweep)
        {
            using (var digest = new RacingDigest())
            {
                digest.Add("vehicle-physics-lab-sweep.1");
                digest.Add(sweep.id);
                digest.Add(JsonUtility.ToJson(sweep));
                digest.Add(sweep.baseExperiment == null || sweep.baseExperiment.vehicle == null
                    ? "<null-vehicle>"
                    : RacingLineSnapshot.VehicleFingerprintOf(sweep.baseExperiment.vehicle));
                digest.Add(VehicleController.SimulationRevision);
                digest.Add(Application.unityVersion);
                return digest.Finish();
            }
        }
    }
}
