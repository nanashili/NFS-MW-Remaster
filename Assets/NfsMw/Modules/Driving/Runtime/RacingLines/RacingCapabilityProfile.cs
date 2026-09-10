using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Serializable]
    public struct RacingCapabilityPoint
    {
        public float speed, acceleration, braking, lateralAcceleration;
    }

    [CreateAssetMenu(menuName = "NFS MW Remaster/Racing Lines/Capability Profile", fileName = "LineCapability")]
    public sealed class RacingCapabilityProfile : ScriptableObject
    {
        [HideInInspector] public int schema = 1;
        [TextArea] public string evidence = "Uncalibrated conservative design assumptions; not measured vehicle capability.";
        public string vehicleFingerprint;
        public bool longitudinalMeasured;
        public bool lateralMeasured;
        public string physicsLabRunId;
        public string physicsLabFingerprint;
        [TextArea] public string physicsLabEvidence;
        public RacingCapabilityPoint[] points =
        {
            new RacingCapabilityPoint { speed = 0, acceleration = 2, braking = 4, lateralAcceleration = 3 },
            new RacingCapabilityPoint { speed = 15, acceleration = 1.5f, braking = 4, lateralAcceleration = 3 },
            new RacingCapabilityPoint { speed = 30, acceleration = 0.8f, braking = 4, lateralAcceleration = 3 }
        };
    }
}
