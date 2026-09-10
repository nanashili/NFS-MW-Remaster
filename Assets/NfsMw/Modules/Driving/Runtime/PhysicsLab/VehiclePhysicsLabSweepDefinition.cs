using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(menuName = "NFS MW Remaster/Driving/Vehicle Physics Lab Sweep", fileName = "PhysicsLabSweep")]
    public sealed class VehiclePhysicsLabSweepDefinition : ScriptableObject
    {
        public const int CurrentSchema = 1;
        public const int MaximumAxes = 4;
        public const int MaximumTrials = 1024;

        [HideInInspector] public int schema = CurrentSchema;
        [HideInInspector] public string id = Guid.NewGuid().ToString("N");
        public string displayName = "Vehicle physics parameter sweep";
        public VehiclePhysicsLabDefinition baseExperiment;
        public VehiclePhysicsLabSweepAxis[] axes = new[] { new VehiclePhysicsLabSweepAxis() };
        [Min(1)] public int maximumTrials = 128;
        public bool stopOnFailure = true;

        public int TrialCount
        {
            get
            {
                if (axes == null) return 0;
                long count = 1;
                int enabledAxes = 0;
                for (int i = 0; i < axes.Length; i++)
                {
                    if (axes[i] == null || !axes[i].enabled) continue;
                    enabledAxes++;
                    count *= Mathf.Max(1, axes[i].steps);
                    if (count > MaximumTrials) return MaximumTrials + 1;
                }
                return enabledAxes == 0 ? 0 : (int)count;
            }
        }

        public bool IsValid(out string failure)
        {
            failure = string.Empty;
            if (schema != CurrentSchema || !Guid.TryParseExact(id, "N", out _))
            {
                failure = "PHYSICS_LAB_SWEEP_SCHEMA: migrate the sweep asset before use.";
                return false;
            }
            if (baseExperiment == null || !baseExperiment.IsValid(out failure))
            {
                if (string.IsNullOrEmpty(failure)) failure = "PHYSICS_LAB_SWEEP_BASE: assign a valid base experiment.";
                return false;
            }
            if (axes == null || axes.Length == 0 || axes.Length > MaximumAxes || maximumTrials < 1 || maximumTrials > MaximumTrials)
            {
                failure = "PHYSICS_LAB_SWEEP_VALUES: provide 1-4 axes and a 1-1024 trial budget.";
                return false;
            }

            var parameters = new HashSet<VehiclePhysicsLabTuningParameter>();
            int enabledAxes = 0;
            long combinations = 1;
            for (int i = 0; i < axes.Length; i++)
            {
                VehiclePhysicsLabSweepAxis axis = axes[i];
                if (axis == null) { failure = "PHYSICS_LAB_SWEEP_AXIS: axis " + i + " is missing."; return false; }
                if (!axis.IsValid(out failure)) return false;
                if (!axis.enabled) continue;
                if (!parameters.Add(axis.parameter))
                {
                    failure = "PHYSICS_LAB_SWEEP_DUPLICATE: parameter " + axis.parameter + " is assigned to more than one axis.";
                    return false;
                }
                enabledAxes++;
                combinations *= axis.steps;
                if (combinations > maximumTrials)
                {
                    failure = "PHYSICS_LAB_SWEEP_BUDGET: " + combinations + " combinations exceed the configured " + maximumTrials + " trial budget.";
                    return false;
                }
            }
            if (enabledAxes == 0)
            {
                failure = "PHYSICS_LAB_SWEEP_AXES: enable at least one sweep axis.";
                return false;
            }
            return true;
        }

        public bool TryGetTrial(int index, out VehiclePhysicsLabTuningOverride[] overrides, out string label)
        {
            overrides = Array.Empty<VehiclePhysicsLabTuningOverride>();
            label = string.Empty;
            if (!IsValid(out _) || index < 0 || index >= TrialCount) return false;

            var values = new List<VehiclePhysicsLabTuningOverride>();
            var text = new StringBuilder();
            int mixedRadix = index;
            for (int i = 0; i < axes.Length; i++)
            {
                VehiclePhysicsLabSweepAxis axis = axes[i];
                if (!axis.enabled) continue;
                int axisIndex = mixedRadix % axis.steps;
                mixedRadix /= axis.steps;
                float value = axis.ValueAt(axisIndex);
                values.Add(new VehiclePhysicsLabTuningOverride { enabled = true, parameter = axis.parameter, value = value });
                if (text.Length > 0) text.Append(" · ");
                text.Append(axis.parameter).Append('=').Append(value.ToString("R"));
            }
            overrides = values.ToArray();
            label = text.ToString();
            return true;
        }
    }
}
