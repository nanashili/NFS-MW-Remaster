using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(menuName = "NFS MW Remaster/Sensory/Telemetry replay")]
    public sealed class FeedbackReplayAsset : ScriptableObject
    {
        public string scenario;
        public VehicleFeedbackFrame[] frames = Array.Empty<VehicleFeedbackFrame>();
    }
}
