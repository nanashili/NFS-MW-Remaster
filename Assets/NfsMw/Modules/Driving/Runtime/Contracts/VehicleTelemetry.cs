using System;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Read-only simulation output shared by gameplay, tools, replay, and presentation.
    /// The contract deliberately contains no Unity object references.
    /// </summary>
    [Serializable]
    public struct VehicleTelemetry
    {
        public float SpeedKph;
        public float ForwardSpeedKph;
        public float EngineRpm;
        public float EngineTorque;
        public float DrivetrainTorque;
        public float Steering;
        public float Throttle;
        public float Brake;
        public float AverageSlip;
        public float NitrousSeconds;
        /// <summary>Input sampled at the vehicle boundary before shaping.</summary>
        public VehicleInputState RawInput;
        /// <summary>Input after the shared steering curve but before response smoothing.</summary>
        public VehicleInputState ShapedInput;
        /// <summary>Input actually used for the current force calculation.</summary>
        public VehicleInputState FinalInput;
        public VehicleHandlingMode HandlingMode;
        public float AssistYawTorque;
        public float AverageLateralSlipRadians;
        public float AverageFrictionUtilization;
        public float TractionControlReduction;
        public float AbsReduction;
        public float CountersteeringContribution;
        public bool BrakeLightsRequired;
        public int GroundedWheels;
        public int Gear;
        public bool NitrousActive;
        public bool IsShifting;

        public string GearLabel
        {
            get
            {
                if (Gear < 0)
                {
                    return "R";
                }

                return Gear == 0 ? "N" : Gear.ToString();
            }
        }
    }
}
