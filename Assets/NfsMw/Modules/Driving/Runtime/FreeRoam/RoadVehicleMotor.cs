using UnityEngine;

namespace NfsMwRemaster.Driving
{
    // Scene-compatible adapter: decisions belong to TrafficSimulation; tires and contacts belong to VehicleController.
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RoadVehicleMotor : MonoBehaviour, IVehicleInputSource
    {
        [SerializeField] private RoadNetwork network;
        [SerializeField] private TrafficVehicleProfile vehicleProfile;
        [SerializeField] private int driverSeed = 7919;
        private TrafficWorldDirector world;
        private VehicleController vehicle;
        private int slot = -1;
        private float stuckTime, commandedSpeed, speedIntegral;
        public Rigidbody Body { get; private set; }
        public VehicleController Vehicle => vehicle;
        public RoadNetwork Network => network;
        public TrafficVehicleProfile VehicleProfile => vehicleProfile;
        public int DriverSeed => driverSeed;
        public VehicleInputState Current { get; private set; }
        public bool BrakeLights => Current.Brake > 0.1f || Current.Handbrake;
        public int TurnSignal { get; private set; }
        public TrafficAgentState Agent => world != null && world.isActiveAndEnabled && slot >= 0 ? world.Simulation[slot] : null;
        public TrafficDriverState DriverState => Immobilized || Agent?.Disabled == true ? TrafficDriverState.Disabled
            : Agent?.Driver.State ?? TrafficDriverState.Waiting;
        public bool WaitingForTraffic => Agent != null && Agent.Gap < 8 && Agent.Speed < 0.8f;
        public float StuckTime => stuckTime;
        public bool Immobilized { get; set; }
        public bool ConsumeResetRequest() => false;
        public bool ConsumeCameraToggleRequest() => false;

        // Speed is now road/driver/vehicle data, not a private motor override. Retains existing scene-builder API.
        public void Configure(RoadNetwork roads, RoadTrafficSignals signals, float speed) { network = roads; }
        public void ConfigureDriver(int seed) { driverSeed = seed; }
        public void ConfigureVehicle(TrafficVehicleProfile profile) { vehicleProfile = profile; Cache(); }
        private void Awake() { Cache(); Hold(); }
        private void Cache()
        {
            if (Body == null) Body = GetComponent<Rigidbody>();
            if (vehicle == null) vehicle = GetComponent<VehicleController>();
            if (vehicle != null) vehicle.SetInputSource(this);
        }
        internal void Bind(TrafficWorldDirector owner, int index)
        {
            Cache(); world = owner; slot = index; Immobilized = false; stuckTime = speedIntegral = 0;
            vehicle?.ResetSimulationForPool();
            commandedSpeed = Agent.Speed; Hold();
        }
        internal void Unbind()
        {
            world = null; slot = -1; stuckTime = commandedSpeed = speedIntegral = 0;
            Immobilized = false; TurnSignal = 0; Hold(); vehicle?.NotifyPoseReset();
        }
        public bool PlaceWhileInactive(Vector3 position, Quaternion rotation, Vector3 velocity)
        {
            if (gameObject.activeSelf) return false;
            Cache(); Body.isKinematic = false; Body.detectCollisions = true;
            Body.position = position; Body.rotation = rotation; transform.SetPositionAndRotation(position, rotation);
            Body.linearVelocity = velocity; Body.angularVelocity = Vector3.zero;
            return true;
        }
        internal void SetDistantProxy(bool distant)
        {
            // Only the director may cross this boundary, outside every interaction horizon.
            bool changed = Body.isKinematic != distant;
            Body.isKinematic = distant; Body.detectCollisions = !distant;
            if (!distant && Agent != null && changed) { Body.linearVelocity = Agent.Velocity; commandedSpeed = Agent.Speed; }
            speedIntegral = 0; vehicle.enabled = !distant; vehicle.NotifyPoseReset();
        }
        internal void UpdateDistantProxy()
        {
            if (!Body.isKinematic || Agent == null) return;
            Body.MovePosition(Agent.Position + Vector3.up * 0.65f);
            Body.MoveRotation(Quaternion.LookRotation(Agent.Forward));
        }
        private void FixedUpdate()
        {
            TrafficAgentState agent = Agent;
            if (vehicle == null || Body == null || Body.isKinematic) return;
            if (agent == null || !agent.Active || agent.Disabled || Immobilized) { Hold(); return; }
            float speed = Mathf.Max(0, Vector3.Dot(Body.linearVelocity, transform.forward));
            Vector3 local = transform.InverseTransformPoint(agent.Aim + Vector3.up * 0.65f);
            float wheelbase = vehicleProfile != null ? vehicleProfile.wheelbase : 2.5f;
            float angle = Mathf.Atan2(2 * wheelbase * local.x, Mathf.Max(1, local.x * local.x + local.z * local.z)) * Mathf.Rad2Deg;
            VehicleTuning tuning = vehicle.Tuning;
            float steer = Mathf.Clamp(angle / Mathf.Max(1, tuning.controls.maxSteerAngle), -1, 1);
            float requested = agent.Driver.FinalAcceleration;
            commandedSpeed = Mathf.Clamp(commandedSpeed + requested * Time.fixedDeltaTime, Mathf.Max(0, speed - 2), speed + 2);
            if (agent.Arrived || agent.TargetSpeed < 0.01f) commandedSpeed = 0;
            float error = commandedSpeed - speed;
            speedIntegral = Mathf.Clamp(speedIntegral + error * Time.fixedDeltaTime, -1, 1);
            float throttle = Mathf.Clamp01(0.1f + requested * 0.25f + error * 0.35f + speedIntegral * 0.15f);
            float brake = requested < -0.4f || error < -0.4f
                ? Mathf.Clamp01(-requested / Mathf.Max(4, agent.Profile.emergencyBraking) - error * 0.3f) : 0;
            bool hold = commandedSpeed < 0.05f && speed < 1.2f;
            // Shared player controls use service brake as reverse below 4 km/h.
            // Civilian stops use the parking brake so a queue never silently selects reverse.
            if (brake > 0 && speed < 1.2f) { hold = true; brake = 0; }
            Current = new VehicleInputState { Steering = steer, Throttle = hold || brake > 0 ? 0 : throttle,
                Brake = brake, Handbrake = hold };
            TurnSignal = agent.laneTarget >= 0 ? network.Lanes[agent.Lane].Left == agent.laneTarget ? -1 : 1 : agent.Yielding ? 1 : 0;
            stuckTime = speed < 0.8f && !WaitingForTraffic && commandedSpeed > 0.5f ? stuckTime + Time.fixedDeltaTime : 0;
        }
        private void Hold()
        {
            float speed = Body != null ? Body.linearVelocity.magnitude : 0;
            Current = new VehicleInputState { Brake = speed > 1.2f ? 1 : 0, Handbrake = speed <= 1.2f };
        }
        private void OnCollisionEnter(Collision collision)
        {
            if (world != null && slot >= 0 && collision.collider.GetComponent<VehicleSurface>() == null)
                world.NotifyCollision(slot, collision.relativeVelocity.magnitude);
        }
        private void OnDisable() { Current = VehicleInputState.Neutral; TurnSignal = 0; }
    }
}
