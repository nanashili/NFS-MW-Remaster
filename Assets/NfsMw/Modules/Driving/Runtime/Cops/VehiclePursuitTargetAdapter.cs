using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Default target adapter for a player vehicle. Alternate targets can
    /// implement IVehiclePursuitTarget directly without changing police code.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class VehiclePursuitTargetAdapter :
        MonoBehaviour,
        IVehiclePursuitTarget, IPoliceIgnitionState
    {
        [SerializeField] private string targetId = "player";

        private Rigidbody body;
        private VehicleController vehicle;
        public bool EngineRunning => vehicle == null || vehicle.EngineRunning;

        public string TargetId
        {
            get { return string.IsNullOrEmpty(targetId) ? name : targetId; }
        }

        public Transform Transform
        {
            get { return transform; }
        }

        public Rigidbody Body
        {
            get
            {
                if (body == null)
                {
                    body = GetComponent<Rigidbody>();
                }

                return body;
            }
        }

        public Vector3 Position
        {
            get { return transform.position; }
        }

        public Vector3 Velocity
        {
            get { return Body == null ? Vector3.zero : Body.linearVelocity; }
        }

        public bool IsAvailable
        {
            get { return isActiveAndEnabled && gameObject.activeInHierarchy; }
        }

        public void Configure(string configuredTargetId)
        {
            if (!string.IsNullOrEmpty(configuredTargetId))
            {
                targetId = configuredTargetId;
            }
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            vehicle = GetComponent<VehicleController>();
        }
    }
}
