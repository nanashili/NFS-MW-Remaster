using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum VehicleDriveLayout { LegacyBindings = 0, Fwd = 1, Rwd = 2, Awd = 3 }
    public enum VehicleTransmissionMode { Automatic = 0, Manual = 1 }
    public enum VehicleDifferentialMode { Open = 0, LimitedSlip = 1, Locked = 2 }
    public enum VehicleSimulationModel { Authored = 0, MostWantedReference = 1 }
    /// <summary>
    /// Data-only vehicle specification. Multiple cars can share the same
    /// runtime modules while using different tuning assets.
    /// </summary>
    [CreateAssetMenu(menuName = "NFS MW Remaster/Driving/Vehicle Tuning", fileName = "VehicleTuning")]
    public sealed class VehicleTuning : ScriptableObject
    {
        public string displayName = "MW Street Racer";
        public ChassisSettings chassis = new ChassisSettings();
        public EngineSettings engine = new EngineSettings();
        public TireSettings tires = new TireSettings();
        public ControlSettings controls = new ControlSettings();
        public AssistSettings assists = new AssistSettings();
        public VehicleHandlingSettings handling = new VehicleHandlingSettings();
        public AeroSettings aero = new AeroSettings();
        public VehicleDriveLayout driveLayout = VehicleDriveLayout.LegacyBindings;
        public VehicleTransmissionMode transmissionMode = VehicleTransmissionMode.Automatic;
        public VehicleDifferentialMode differential = VehicleDifferentialMode.Open;
        [Range(0f, 1f)] public float awdFrontTorqueBias = 0.5f;
        [Min(0f)] public float differentialPreload = 0f;
        [Range(0f, 1f)] public float differentialLockStrength = 0f;
        public bool speedGovernor;
        [Tooltip("Authored preserves existing vehicles. Most Wanted uses the recovered reference equations with the shared Unity contact solver; it is not a binary-identical port.")]
        public VehicleSimulationModel simulationModel;
        public MostWantedDrivingSettings mostWanted = new MostWantedDrivingSettings();
        public bool UsesMostWantedReference => simulationModel == VehicleSimulationModel.MostWantedReference;

        /// <summary>
        /// Creates a playable baseline without requiring an asset to exist.
        /// The editor demo serializes this same baseline to an asset.
        /// </summary>
        public static VehicleTuning CreateStreetRacer()
        {
            VehicleTuning tuning = CreateInstance<VehicleTuning>();
            tuning.displayName = "MW Street Racer";
            tuning.chassis = new ChassisSettings();
            tuning.engine = new EngineSettings();
            tuning.tires = new TireSettings();
            tuning.controls = new ControlSettings();
            tuning.assists = new AssistSettings();
            tuning.aero = new AeroSettings();

            tuning.chassis.mass = 1450f;
            tuning.chassis.centerOfMass = new Vector3(0f, -0.34f, 0.05f);
            tuning.chassis.maxSpeedKph = 320f;
            tuning.chassis.rollingResistance = 0.018f;

            tuning.engine.idleRpm = 900f;
            tuning.engine.redlineRpm = 7200f;
            tuning.engine.maxTorqueNewtonMeters = 390f;
            tuning.engine.peakTorqueRpm = 4200f;
            tuning.engine.engineInertia = 0.22f;
            tuning.engine.drivelineEfficiency = 0.88f;
            tuning.engine.finalDrive = 3.42f;
            tuning.engine.gearRatios = new[] { 3.10f, 2.14f, 1.48f, 1.16f, 0.94f, 0.78f };
            tuning.engine.reverseRatio = -3.20f;
            tuning.engine.shiftUpRpm = 6800f;
            tuning.engine.shiftDownRpm = 2250f;
            tuning.engine.shiftDuration = 0.13f;
            tuning.engine.engineBrakingTorque = 42f;
            tuning.engine.nitrousTorque = 190f;
            tuning.engine.nitrousFuelSeconds = 8f;

            tuning.tires.wheelRadius = 0.34f;
            tuning.tires.wheelWidth = 0.26f;
            tuning.tires.suspensionRestLength = 0.32f;
            tuning.tires.suspensionTravel = 0.18f;
            tuning.tires.springRate = 31000f;
            tuning.tires.damperRate = 4700f;
            tuning.tires.wheelMass = 19f;
            tuning.tires.longitudinalGrip = 1.16f;
            tuning.tires.lateralGrip = 1.22f;
            tuning.tires.handbrakeRearGrip = 0.30f;
            tuning.tires.peakLongitudinalSlip = 0.105f;
            tuning.tires.peakLateralSlipRadians = 0.115f;
            tuning.tires.postPeakGrip = 0.78f;
            tuning.tires.rollingResistance = 0.014f;

            tuning.controls.maxSteerAngle = 31f;
            tuning.controls.highSpeedSteerScale = 0.38f;
            tuning.controls.highSpeedSteerKph = 185f;
            tuning.controls.steeringResponse = 7.5f;
            tuning.controls.throttleResponse = 6.5f;
            tuning.controls.brakeResponse = 11f;
            tuning.controls.steeringExponent = 1.35f;
            tuning.controls.autoReverseSpeedKph = 4f;
            tuning.controls.serviceBrakeTorque = 5200f;
            tuning.controls.handbrakeTorque = 6200f;
            tuning.controls.frontBrakeBias = 0.64f;

            tuning.assists.tractionControl = true;
            tuning.assists.tractionSlipLimit = 0.16f;
            tuning.assists.abs = true;
            tuning.assists.absSlipLimit = 0.19f;
            tuning.assists.stabilityControl = true;
            tuning.assists.stabilityStrength = 0.42f;
            tuning.assists.driftYawStrength = 0.35f;
            tuning.assists.driftSpeedKph = 25f;

            tuning.aero.airDensity = 1.225f;
            tuning.aero.dragCoefficient = 0.32f;
            tuning.aero.frontalArea = 2.05f;
            tuning.aero.downforceCoefficient = 0.85f;
            tuning.aero.downforceBalance = 0.50f;

            return tuning;
        }

        /// <summary>
        /// Creates an isolated runtime copy. Performance upgrades apply to the
        /// copy so a shared catalog asset remains immutable for every vehicle.
        /// </summary>
        public VehicleTuning CreateRuntimeCopy()
        {
            VehicleTuning copy = Instantiate(this);
            copy.name = name + " (Runtime)";
            copy.hideFlags = HideFlags.DontSave;
            return copy;
        }

        [Serializable]
        public sealed class ChassisSettings
        {
            [Min(100f)] public float mass = 1450f;
            public Vector3 centerOfMass = new Vector3(0f, -0.34f, 0.05f);
            [Min(1f)] public float maxSpeedKph = 320f;
            [Range(0f, 0.1f)] public float rollingResistance = 0.018f;
            [Min(0f)] public float yawInertiaMultiplier = 1f;
        }

        [Serializable]
        public sealed class EngineSettings
        {
            [Min(100f)] public float idleRpm = 900f;
            [Min(500f)] public float redlineRpm = 7200f;
            [Min(0f)] public float maxTorqueNewtonMeters = 390f;
            [Min(100f)] public float peakTorqueRpm = 4200f;
            [Min(0.01f)] public float engineInertia = 0.22f;
            [Range(0.1f, 1f)] public float drivelineEfficiency = 0.88f;
            public float finalDrive = 3.42f;
            public float[] gearRatios = { 3.10f, 2.14f, 1.48f, 1.16f, 0.94f, 0.78f };
            public float reverseRatio = -3.20f;
            [Min(100f)] public float shiftUpRpm = 6800f;
            [Min(100f)] public float shiftDownRpm = 2250f;
            [Min(0f)] public float shiftDuration = 0.13f;
            [Min(0f)] public float engineBrakingTorque = 42f;
            [Min(0f)] public float nitrousTorque = 190f;
            [Min(0f)] public float nitrousFuelSeconds = 8f;
            [Min(0f)] public float revLimiterHysteresisRpm = 150f;
            public bool forcedInduction;
            [Min(0f)] public float boostTorqueMultiplier = 1f;
            [Min(0f)] public float boostSpoolSeconds = 0.35f;
            [Range(0f, 1f)] public float clutchEngagement = 1f;
            [Min(0f)] public float shiftDownHysteresisRpm = 250f;
            public AnimationCurve torqueCurve = new AnimationCurve(
                new Keyframe(0f, 0.7f), new Keyframe(0.45f, 1f), new Keyframe(1f, 0.72f));
        }

        [Serializable]
        public sealed class TireSettings
        {
            [Min(0.05f)] public float wheelRadius = 0.34f;
            [Min(0f)] public float wheelLateralOffset;
            [Min(0.05f)] public float wheelWidth = 0.26f;
            [Min(0.05f)] public float suspensionRestLength = 0.32f;
            [Min(0f)] public float suspensionTravel = 0.18f;
            [Min(0f)] public float springRate = 31000f;
            [Min(0f)] public float damperRate = 4700f;
            [Min(0.1f)] public float wheelMass = 19f;
            [Min(0f)] public float longitudinalGrip = 1.16f;
            [Min(0f)] public float lateralGrip = 1.22f;
            [Range(0f, 1f)] public float handbrakeRearGrip = 0.30f;
            [Min(0.001f)] public float peakLongitudinalSlip = 0.105f;
            [Min(0.001f)] public float peakLateralSlipRadians = 0.115f;
            [Range(0f, 1f)] public float postPeakGrip = 0.78f;
            [Min(0f)] public float rollingResistance = 0.014f;
        }

        [Serializable]
        public sealed class ControlSettings
        {
            [Range(1f, 60f)] public float maxSteerAngle = 31f;
            [Range(0.1f, 1f)] public float highSpeedSteerScale = 0.38f;
            [Min(1f)] public float highSpeedSteerKph = 185f;
            [Min(0.1f)] public float steeringResponse = 7.5f;
            [Min(0.1f)] public float throttleResponse = 6.5f;
            [Min(0.1f)] public float brakeResponse = 11f;
            [Min(0.1f)] public float steeringExponent = 1.35f;
            [Min(0f)] public float autoReverseSpeedKph = 4f;
            [Min(0f)] public float serviceBrakeTorque = 5200f;
            [Min(0f)] public float handbrakeTorque = 6200f;
            [Range(0f, 1f)] public float frontBrakeBias = 0.64f;
        }

        [Serializable]
        public sealed class AssistSettings
        {
            public bool tractionControl = true;
            [Min(0f)] public float tractionSlipLimit = 0.16f;
            public bool abs = true;
            [Min(0f)] public float absSlipLimit = 0.19f;
            public bool stabilityControl = true;
            [Range(0f, 1f)] public float stabilityStrength = 0.42f;
            [Range(0f, 1f)] public float driftYawStrength = 0.35f;
            [Min(0f)] public float driftSpeedKph = 25f;
            public bool countersteering;
            [Range(0f, 1f)] public float countersteeringStrength;
        }

        [Serializable]
        public sealed class AeroSettings
        {
            [Min(0f)] public float airDensity = 1.225f;
            [Min(0f)] public float dragCoefficient = 0.32f;
            [Min(0f)] public float frontalArea = 2.05f;
            [Min(0f)] public float downforceCoefficient = 0.0018f;
            [Range(0f, 1f)] public float downforceBalance = 0.50f;
        }
    }
}
