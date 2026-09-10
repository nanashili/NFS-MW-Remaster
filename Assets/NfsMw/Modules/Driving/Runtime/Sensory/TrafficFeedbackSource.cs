using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Reduced adapter for the existing traffic motor. Motion only: no invented wheel slip, RPM or combustion events.</summary>
    [RequireComponent(typeof(RoadVehicleMotor))]
    public sealed class TrafficFeedbackSource : MonoBehaviour, IVehicleFeedbackSource
    {
        private Rigidbody body;
        private int sequence, epoch;
        public VehicleFeedbackFrame Frame { get; private set; }
        public event Action<VehicleFeedbackFrame> Sampled;
        public event Action<FeedbackImpact> Impact { add { } remove { } } // This simplified authority has no classified contact stream.
        private void Awake() { body = GetComponent<Rigidbody>(); }
        private void OnEnable() { epoch++; }
        private void FixedUpdate()
        {
            if (body == null) return;
            var velocity = body.linearVelocity;
            Frame = new VehicleFeedbackFrame { Sequence = ++sequence, Epoch = epoch, Time = Time.fixedTimeAsDouble,
                Capabilities = FeedbackCapabilities.Motion, Position = body.position, Rotation = body.rotation,
                Velocity = velocity, LocalVelocity = transform.InverseTransformDirection(velocity), Speed = velocity.magnitude,
                SpeedIntensity = SensoryMath.Unit(velocity.magnitude / 60) };
            Sampled?.Invoke(Frame);
        }
        private void OnDisable() { Frame = default; }
    }
}
