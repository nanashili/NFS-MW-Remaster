using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Serializable]
    public sealed class VehicleParameterContribution
    {
        public string source;
        public float before, after;
    }

    [Serializable]
    public sealed class VehicleResolvedParameter
    {
        public VehiclePhysicsLabTuningParameter parameter;
        public string unit;
        public float factoryValue, finalValue;
        public List<VehicleParameterContribution> contributions = new List<VehicleParameterContribution>();
    }

    /// <summary>Owns one runtime copy. Dispose on replacement/despawn; factory assets are never destroyed or edited.</summary>
    public sealed class ResolvedVehicleConfiguration : IDisposable
    {
        public VehicleTuning Tuning { get; private set; }
        public IReadOnlyList<VehicleResolvedParameter> Breakdown { get; }
        internal ResolvedVehicleConfiguration(VehicleTuning tuning, VehicleResolvedParameter[] breakdown)
        { Tuning = tuning; Breakdown = breakdown; }
        public VehicleTuning DetachTuning() { var value = Tuning; Tuning = null; return value; }
        public void Dispose()
        {
            if (Tuning == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(Tuning); else UnityEngine.Object.DestroyImmediate(Tuning);
            Tuning = null;
        }
    }

    /// <summary>Factory -> parts (category then stable ID) -> authored body effects -> bounded absolute tuning.</summary>
    public static class VehicleConfigurationResolver
    {
        private static readonly VehiclePhysicsLabTuningParameter[] Parameters =
            (VehiclePhysicsLabTuningParameter[])Enum.GetValues(typeof(VehiclePhysicsLabTuningParameter));

        public static ResolvedVehicleConfiguration Resolve(VehicleTuning factory, VehiclePerformanceBuild performance,
            VehicleCustomizationBuild customization, VehicleTuningAdjustment[] adjustments = null, VehicleDefinition definition = null)
        {
            if (definition != null)
            {
                if (!definition.Validate(out string error)) throw new ArgumentException(error);
                factory = definition.factoryTuning;
            }
            RacingLineSnapshot.ValidateTuning(factory);
            if (performance != null && !performance.ValidateComplete(definition != null ? definition.vehicleId : string.Empty,
                definition != null ? definition.variantId : string.Empty, definition != null ? definition.capabilities : VehicleCapabilities.All, out string performanceFailure))
                throw new ArgumentException(performanceFailure);
            if (customization != null && !customization.ValidateComplete(definition != null ? definition.vehicleId : string.Empty,
                definition != null ? definition.variantId : string.Empty,
                definition != null ? definition.supportedSlots : Array.Empty<string>(), out string customizationFailure))
                throw new ArgumentException(customizationFailure);
            var copy = factory.CreateRuntimeCopy();
            try
            {
                var rows = new VehicleResolvedParameter[Parameters.Length];
                for (int i = 0; i < rows.Length; i++) rows[i] = new VehicleResolvedParameter
                { parameter = Parameters[i], unit = Unit(Parameters[i]), factoryValue = Read(copy, Parameters[i]), finalValue = Read(copy, Parameters[i]) };
                var ordered = new List<IVehiclePerformanceUpgrade>();
                if (performance != null) ordered.AddRange(performance.Installed);
                ordered.Sort((a, b) => a.Category != b.Category ? a.Category.CompareTo(b.Category) : string.CompareOrdinal(a.UpgradeId, b.UpgradeId));
                foreach (var part in ordered) { part.Apply(copy); Record(rows, copy, "Upgrade: " + part.UpgradeId); }
                if (customization != null)
                {
                    var bodyParts = new List<IVehicleCustomizationItem>(customization.Installed);
                    bodyParts.Sort((a, b) => a.Category != b.Category ? a.Category.CompareTo(b.Category) : string.CompareOrdinal(a.CustomizationId, b.CustomizationId));
                    foreach (var part in bodyParts)
                        if (part is IVehicleCustomizationPhysicalEffect effect)
                        { effect.ApplyPhysicalEffects(copy); Record(rows, copy, "Body part: " + part.CustomizationId); }
                }
                var seen = new HashSet<VehiclePhysicsLabTuningParameter>();
                foreach (var adjustment in adjustments ?? Array.Empty<VehicleTuningAdjustment>())
                {
                    if (!seen.Add(adjustment.parameter)) throw new ArgumentException("Duplicate tuning adjustment: " + adjustment.parameter);
                    if (!VehiclePhysicsLabTuningOverrides.TryGetRange(adjustment.parameter, out float min, out float max))
                        throw new ArgumentException("Unknown tuning parameter: " + adjustment.parameter);
                    Intersect(definition != null ? definition.tuningLimits : null, adjustment.parameter, ref min, ref max);
                    foreach (var part in ordered)
                        if (part is IVehiclePerformancePartMetadata metadata) Intersect(metadata.TuningLimits, adjustment.parameter, ref min, ref max);
                    if (!float.IsFinite(adjustment.value) || adjustment.value < min || adjustment.value > max)
                        throw new ArgumentException("Tuning " + adjustment.parameter + " requires " + min + " to " + max + " " + Unit(adjustment.parameter) + ".");
                    if (!VehiclePhysicsLabTuningOverrides.Write(copy, adjustment.parameter, adjustment.value))
                        throw new ArgumentException("Tuning value rejected: " + adjustment.parameter);
                    Record(rows, copy, "Tuning: " + adjustment.parameter);
                }
                RacingLineSnapshot.ValidateTuning(copy);
                return new ResolvedVehicleConfiguration(copy, rows);
            }
            catch
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(copy); else UnityEngine.Object.DestroyImmediate(copy);
                throw;
            }
        }

        private static float Read(VehicleTuning tuning, VehiclePhysicsLabTuningParameter parameter) => VehiclePhysicsLabTuningOverrides.Read(tuning, parameter);
        private static void Record(VehicleResolvedParameter[] rows, VehicleTuning tuning, string source)
        {
            foreach (var row in rows)
            {
                float value = Read(tuning, row.parameter);
                if (value != row.finalValue) row.contributions.Add(new VehicleParameterContribution { source = source, before = row.finalValue, after = value });
                row.finalValue = value;
            }
        }
        private static void Intersect(IReadOnlyList<VehicleTuningLimit> limits, VehiclePhysicsLabTuningParameter parameter, ref float min, ref float max)
        {
            if (limits == null) return;
            foreach (var limit in limits)
            {
                if (limit.parameter != parameter) continue;
                if (!float.IsFinite(limit.minimum) || !float.IsFinite(limit.maximum) || limit.minimum > limit.maximum)
                    throw new ArgumentException("Part has invalid tuning limits for " + parameter);
                min = Mathf.Max(min, limit.minimum); max = Mathf.Min(max, limit.maximum);
            }
        }
        public static string Unit(VehiclePhysicsLabTuningParameter parameter)
        {
            switch (parameter)
            {
                case VehiclePhysicsLabTuningParameter.Mass: return "kg";
                case VehiclePhysicsLabTuningParameter.MaxSpeedKph: return "km/h target";
                case VehiclePhysicsLabTuningParameter.EngineTorque:
                case VehiclePhysicsLabTuningParameter.NitrousTorque:
                case VehiclePhysicsLabTuningParameter.ServiceBrakeTorque:
                case VehiclePhysicsLabTuningParameter.DriftMaximumYawTorque: return "N m";
                case VehiclePhysicsLabTuningParameter.EngineInertia: return "kg m²";
                case VehiclePhysicsLabTuningParameter.ShiftDuration:
                case VehiclePhysicsLabTuningParameter.NitrousFuelSeconds: return "s";
                case VehiclePhysicsLabTuningParameter.SuspensionSpringRate: return "N/m";
                case VehiclePhysicsLabTuningParameter.SuspensionDamperRate: return "N s/m";
                case VehiclePhysicsLabTuningParameter.SuspensionTravel: return "m";
                case VehiclePhysicsLabTuningParameter.WheelRadius:
                case VehiclePhysicsLabTuningParameter.WheelLateralOffset:
                case VehiclePhysicsLabTuningParameter.SuspensionRestLength: return "m";
                case VehiclePhysicsLabTuningParameter.MaxSteerAngle: return "deg";
                case VehiclePhysicsLabTuningParameter.SteeringResponse:
                case VehiclePhysicsLabTuningParameter.ThrottleResponse:
                case VehiclePhysicsLabTuningParameter.BrakeResponse: return "1/s";
                default: return "ratio";
            }
        }
    }
}
