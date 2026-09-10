using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Patrol/response navigation emits vehicle intent. The shared controller owns all driving forces.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VehicleController))]
    [DefaultExecutionOrder(-100)]
    public sealed class VehiclePoliceUnit : MonoBehaviour, IVehiclePoliceUnit, IVehicleInputSource
    {
        private const int MaxPatrolRouteAttempts = 16;

        [SerializeField] private string unitId = "police_unit";
        [SerializeField] private VehiclePoliceUnitRole role;
        [SerializeField] private VehiclePursuitDirector director;
        [SerializeField] private RoadNetwork network;
        [SerializeField] private RoadTrafficSignals signals;
        [SerializeField, Min(1)] private float patrolSpeed = 12f, responseSpeed = 28f;
        [SerializeField, Min(1)] private float maxIntegrity = 100f;
        private readonly List<Vector3> route = new List<Vector3>(32);
        private readonly RaycastHit[] hits = new RaycastHit[24];
        private VehicleController vehicle;
        private VehiclePoliceUnitCommand command;
        private Transform visibleContactTarget;
        private int waypoint, patrolDestination;
        private float nextPlan, stuckTime, reverseTime, integrity = 100f;
        private bool commanded;
        public string UnitId => string.IsNullOrEmpty(unitId) ? name : unitId;
        public VehiclePoliceUnitRole Role => role;
        public VehiclePoliceUnitState State { get; private set; } = VehiclePoliceUnitState.Released;
        public VehiclePoliceTactic Tactic => command.Tactic;
        public Vector3 Position => transform.position;
        public float Integrity => integrity;
        public bool IsDisabled => integrity <= 0f || State == VehiclePoliceUnitState.Disabled || !isActiveAndEnabled;
        public bool IsOperational => !IsDisabled;
        public float DisabledAt { get; private set; }
        public VehicleInputState Current { get; private set; }
        public VehicleController Vehicle => vehicle != null ? vehicle : (vehicle = GetComponent<VehicleController>());
        public bool ConsumeResetRequest() => false;
        public bool ConsumeCameraToggleRequest() => false;
        public void Configure(string id, VehiclePoliceUnitRole unitRole, VehiclePursuitDirector pursuit)
        { unitId = id; role = unitRole; SetDirector(pursuit); Vehicle.SetInputSource(this); }
        public void ConfigureNavigation(RoadNetwork roads, RoadTrafficSignals trafficSignals)
        { network = roads; signals = trafficSignals; route.Clear(); nextPlan = 0f; }
        public void SetDirector(VehiclePursuitDirector pursuit)
        {
            if (director != null && director != pursuit) director.UnregisterUnit(this);
            director = pursuit;
            if (isActiveAndEnabled) director?.RegisterUnit((IVehiclePoliceUnit)this);
        }
        public void SetPursuitCommand(VehiclePoliceUnitCommand intent, IVehiclePursuitTarget target, VehiclePoliceResponseTier tier)
        {
            if (IsDisabled) return;
            if (!commanded || command.Phase != intent.Phase) nextPlan = 0f;
            command = intent; commanded = true;
            // No retained target data during search. Only collider identity is kept during actual visual contact.
            visibleContactTarget = intent.TargetVisible ? target?.Transform : null;
            State = intent.Phase == VehiclePursuitPhase.Cooldown || intent.Phase == VehiclePursuitPhase.Search
                ? VehiclePoliceUnitState.Searching : intent.Phase == VehiclePursuitPhase.Engaged
                ? VehiclePoliceUnitState.Engaged : VehiclePoliceUnitState.Responding;
        }
        public void ReleaseFromPursuit()
        {
            if (commanded) { route.Clear(); nextPlan = 0f; }
            commanded = false; visibleContactTarget = null;
            if (State != VehiclePoliceUnitState.Disabled) State = VehiclePoliceUnitState.Released;
        }
        public void ApplyDamage(float amount)
        {
            if (IsDisabled || !PolicePursuitRules.Finite(amount) || amount <= 0f) return;
            integrity = Mathf.Max(0f, integrity - amount);
            if (integrity <= 0f) DisableFor(20f);
        }
        public void DisableFor(float seconds)
        {
            if (State == VehiclePoliceUnitState.Disabled) return;
            integrity = 0f; State = VehiclePoliceUnitState.Disabled; DisabledAt = Time.time;
            commanded = false; Current = new VehicleInputState { Handbrake = true };
            director?.ReportUnitDisabled(this);
        }
        public void Repair()
        {
            integrity = maxIntegrity; State = VehiclePoliceUnitState.Released; ReleaseFromPursuit();
            Current = VehicleInputState.Neutral; stuckTime = reverseTime = 0f;
        }
        public bool PlaceWhileInactive(Vector3 position, Quaternion rotation)
        {
            if (gameObject.activeInHierarchy) return false;
            var body = GetComponent<Rigidbody>();
            body.position = position; body.rotation = rotation; body.linearVelocity = body.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(position, rotation);
            route.Clear(); nextPlan = 0f; waypoint = 0; Repair(); return true;
        }
        private void Awake() { vehicle = GetComponent<VehicleController>(); vehicle.SetInputSource(this); integrity = maxIntegrity; }
        private void OnEnable() { director?.RegisterUnit((IVehiclePoliceUnit)this); }
        private void OnDisable()
        {
            director?.UnregisterUnit((IVehiclePoliceUnit)this);
            Current = VehicleInputState.Neutral; commanded = false; visibleContactTarget = null; route.Clear();
        }
        private void FixedUpdate()
        {
            Current = new VehicleInputState { Handbrake = true };
            if (IsDisabled || Vehicle.Body == null || Vehicle.Wheels == null || Vehicle.Wheels.Length < 4) return;
            Vector3 aim;
            if (network != null && network.Nodes.Count > 0)
            {
                bool routeEnded = route.Count == 0 || waypoint >= route.Count;
                if (routeEnded)
                {
                    // A patrol that cannot find a forward route must not retry an entire graph
                    // search every physics step. PlanRoute schedules the next bounded attempt.
                    if (Time.time < nextPlan) return;
                    PlanRoute();
                }
                else if (commanded && Time.time >= nextPlan) PlanRoute();
                if (route.Count == 0) return;
                aim = LanePoint(waypoint);
                if (Vector3.Distance(Position, aim) < 7f && waypoint < route.Count - 1) aim = LanePoint(++waypoint);
            }
            else if (commanded) aim = command.AimPoint;
            else return;
            float speed = Vehicle.Body.linearVelocity.magnitude;
            Vector3 delta = aim - Position; delta.y = 0f;
            float distance = delta.magnitude;
            float remaining = network != null && route.Count > 0 ? Vector3.Distance(Position, route[Mathf.Min(waypoint, route.Count - 1)]) : distance;
            bool final = network == null || waypoint >= route.Count - 1;
            if (final && remaining < 6f)
            {
                if (!commanded) route.Clear();
                return;
            }
            Vector3 direction = distance > 0.01f ? delta / distance : transform.forward;
            float turn = Vector3.SignedAngle(transform.forward, direction, Vector3.up);
            float desired = commanded ? responseSpeed : patrolSpeed;
            if (commanded && command.Phase != VehiclePursuitPhase.Engaged) desired = patrolSpeed;
            desired = Mathf.Min(desired, Mathf.Lerp(desired, 4f, Mathf.Clamp01(Mathf.Abs(turn) / 75f)));
            if (waypoint < route.Count - 1)
            {
                float bend = Vector3.Angle(direction, route[waypoint + 1] - route[waypoint]);
                if (bend > 25f) desired = Mathf.Min(desired, Mathf.Sqrt(16f + 7f * Mathf.Max(0f, remaining - 5f)));
            }
            if (final) desired = Mathf.Min(desired, Mathf.Sqrt(8f * Mathf.Max(0f, remaining - 4f)));
            float gap = !commanded && signals != null && network != null
                ? signals.StopLineDistance(Position, transform.forward, network) : float.PositiveInfinity;
            float sensing = Mathf.Clamp(speed * speed / 10f + 8f, 10f, 90f);
            int count = Physics.SphereCastNonAlloc(Position + Vector3.up * 0.3f + transform.forward * 2.25f,
                0.65f, transform.forward, hits, sensing, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i].collider;
                if (hit.attachedRigidbody == Vehicle.Body) continue;
                // Physical contact is opt-in and still never injects an impulse.
                if (commanded && command.Tactic == VehiclePoliceTactic.Ram && visibleContactTarget != null
                    && hit.transform.IsChildOf(visibleContactTarget)) continue;
                gap = Mathf.Min(gap, hits[i].distance);
            }
            if (count == hits.Length) gap = 0f;
            desired = Mathf.Min(desired, Mathf.Sqrt(8f * Mathf.Max(0f, gap - 2f)));
            if (desired > 1f && speed < 0.75f && gap > 5f) stuckTime += Time.fixedDeltaTime; else stuckTime = 0f;
            if (stuckTime > 4f && reverseTime <= 0f) { reverseTime = 1.2f; stuckTime = 0f; route.Clear(); }
            if (reverseTime > 0f)
            {
                reverseTime -= Time.fixedDeltaTime;
                // Bounded recovery uses the controller's reverse intent, never a transform teleport.
                int behind = Physics.SphereCastNonAlloc(Position + Vector3.up * 0.3f - transform.forward * 2.3f,
                    0.7f, -transform.forward, hits, 5f, ~0, QueryTriggerInteraction.Ignore);
                bool clear = behind < hits.Length;
                for (int i = 0; i < behind; i++) if (hits[i].rigidbody != Vehicle.Body) clear = false;
                if (clear) Current = new VehicleInputState { Brake = 0.4f, Steering = -Mathf.Sign(turn) * 0.7f };
                return;
            }
            if (desired < 0.4f && speed < 1f) return;
            float error = desired - Vector3.Dot(Vehicle.Body.linearVelocity, transform.forward);
            Current = new VehicleInputState
            {
                Steering = Mathf.Clamp(turn / 32f, -1f, 1f),
                Throttle = Mathf.Clamp01(error / 5f),
                Brake = Mathf.Clamp01(-error / 6f)
            };
        }
        private void PlanRoute()
        {
            nextPlan = Time.time + 1.25f;
            if (commanded) network.TryRoute(Position, command.AimPoint, route);
            else
            {
                int attempts = Mathf.Min(MaxPatrolRouteAttempts, network.Nodes.Count);
                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    patrolDestination = (patrolDestination + 1) % network.Nodes.Count;
                    network.TryRoute(Position, network.Nodes[patrolDestination].position, route);
                    if (route.Count > 1 && Vector3.Dot(route[1] - Position, transform.forward) > 1f) break;
                    route.Clear();
                }
            }
            waypoint = route.Count > 1 ? 1 : 0;
        }
        private Vector3 LanePoint(int index)
        {
            index = Mathf.Clamp(index, 0, route.Count - 1);
            Vector3 along = index > 0 ? route[index] - route[index - 1] : transform.forward; along.y = 0f;
            float lateralOffset = network != null && network.UsesBakedData ? 0 : 3;
            Vector3 end = route[index] + Vector3.Cross(Vector3.up, along.normalized) * lateralOffset;
            if (index <= 0 || along.sqrMagnitude < 1f) return end;
            Vector3 start = route[index - 1] + Vector3.Cross(Vector3.up, along.normalized) * lateralOffset;
            // Stay on the lane while approaching the intersection, not a diagonal across the block.
            float projection = Vector3.Dot(Position - start, along.normalized);
            float lookAhead = Mathf.Clamp(Vehicle.Body.linearVelocity.magnitude * 0.6f + 8f, 8f, 20f);
            return start + along.normalized * Mathf.Clamp(projection + lookAhead, 0f, along.magnitude);
        }
        private void OnCollisionEnter(Collision collision)
        {
            if (director == null || director.Target?.Body == null || collision.rigidbody != director.Target.Body) return;
            if (collision.rigidbody.GetComponent<FreeRoamSession>()?.CanDrive == false) return;
            if (collision.relativeVelocity.magnitude >= 2f) director.ReportTradePaint(this);
        }
    }
}
