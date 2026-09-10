using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(menuName = "NFS MW Remaster/Racing Lines/Vehicle Setup", fileName = "LineVehicle")]
    public sealed class RacingVehicleSetup : ScriptableObject
    {
        [HideInInspector] public string id = Guid.NewGuid().ToString("N");
        public VehicleTuning tuning;
        [Tooltip("When assigned, uses the same immutable factory definition and resolver as the gameplay vehicle.")]
        public VehicleDefinition definition;
        public VehiclePerformanceUpgradeDefinition[] upgrades = Array.Empty<VehiclePerformanceUpgradeDefinition>();
        public VehicleCustomizationDefinition[] bodyParts = Array.Empty<VehicleCustomizationDefinition>();
        public VehicleTuningAdjustment[] tuningAdjustments = Array.Empty<VehicleTuningAdjustment>();
        [Tooltip("Optional physics-only prefab. Unknown scripts are rejected before activation; never use a career/player root.")]
        public VehicleController physicsPrefab;
        [Tooltip("Full body width, height, length in metres. Used by the synthetic rig when no prefab exists.")]
        public Vector3 dimensions = new Vector3(1.8f, 1.2f, 4.3f);
        [Min(0.5f)] public float wheelbase = 2.65f;
        [Min(0.5f)] public float trackWidth = 1.55f;
        [Range(0.005f, 0.05f)] public float fixedStep = 0.02f;
        public RacingControllerSettings controller = new RacingControllerSettings();

        public VehicleTuning CreateEffectiveTuning()
        {
            var build = new VehiclePerformanceBuild();
            if (upgrades == null) throw new ArgumentException("LINE_UPGRADES: missing upgrade list.");
            foreach (var upgrade in upgrades)
                if (!build.TryInstall(upgrade, out string failure)) throw new ArgumentException("LINE_UPGRADE: " + failure);
            var parts = new VehicleCustomizationBuild();
            foreach (var part in bodyParts ?? Array.Empty<VehicleCustomizationDefinition>())
                if (!parts.TryInstall(part, out string failure)) throw new ArgumentException("LINE_BODY_PART: " + failure);
            using var resolved = VehicleConfigurationResolver.Resolve(tuning, build, parts, tuningAdjustments, definition);
            return resolved.DetachTuning();
        }
    }
}
