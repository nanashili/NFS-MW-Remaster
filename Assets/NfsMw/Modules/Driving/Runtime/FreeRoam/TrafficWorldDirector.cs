using System;
using Unity.Profiling;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class TrafficWorldDirector : MonoBehaviour, ITrafficEnvironment, ITrafficMovementSignals
    {
        [SerializeField] private RoadNetwork roads;
        [SerializeField] private RoadTrafficSignals signals;
        [SerializeField] private TrafficWorldProfile profile;
        [SerializeField] private RoadVehicleMotor[] pool = Array.Empty<RoadVehicleMotor>();
        [SerializeField] private VehicleController player;
        [SerializeField] private VehiclePoliceUnit[] police = Array.Empty<VehiclePoliceUnit>();
        [SerializeField] private TrafficInitialTrip[] initialTrips = Array.Empty<TrafficInitialTrip>();
        private readonly VehicleController[] racers = new VehicleController[32];
        private readonly Camera[] cameras = new Camera[32];
        private readonly RaycastHit[] visibilityHits = new RaycastHit[32];
        private readonly RaycastHit[] obstacleHits = new RaycastHit[32];
        private readonly Collider[] spawnHits = new Collider[32];
        private readonly TrafficVehicleSnapshot[] interests = new TrafficVehicleSnapshot[64];
        private readonly Rigidbody[] interestBodies = new Rigidbody[64];
        private readonly int[] query = new int[128];
        private RoadVehicleMotor[] assigned;
        private IDisposable[] disabledClosures;
        private int[] origins, destinations;
        private float[] admissionTimes, rates, demandMultipliers;
        private float elapsed, nextPopulation, lastPopulation;
        private int interestCount, cameraCount;
        private bool initialized, ownsProfile;
        private static readonly ProfilerMarker MicroMarker = new ProfilerMarker("Traffic.MicroscopicStep");
        private static readonly ProfilerMarker SpatialMarker = new ProfilerMarker("Traffic.SpatialRegistry");
        private static readonly ProfilerMarker PopulationMarker = new ProfilerMarker("Traffic.Population");
        private static readonly ProfilerMarker LodMarker = new ProfilerMarker("Traffic.LodHandover");
        public TrafficSimulation Simulation { get; private set; }
        public TrafficSpatialRegistry Registry { get; private set; }
        public TrafficDemandReservoir Demand { get; private set; }
        public TrafficWorldProfile Profile => profile;
        public RoadNetwork Roads => roads;
        public int DeferredArrivals { get; private set; }
        public int HandoverCount { get; private set; }
        public int Capacity => assigned?.Length ?? 0;

        public void Configure(RoadNetwork network, RoadTrafficSignals trafficSignals, RoadVehicleMotor[] vehicles,
            VehicleController observer, VehiclePoliceUnit[] cops, TrafficWorldProfile settings = null)
        {
            if (initialized) throw new InvalidOperationException("Configure traffic before starting the world.");
            roads = network; signals = trafficSignals; pool = vehicles ?? Array.Empty<RoadVehicleMotor>();
            player = observer; police = cops ?? Array.Empty<VehiclePoliceUnit>(); profile = settings;
        }
        public bool RegisterRacer(VehicleController racer)
        {
            if (racer == null || racer == player) return false;
            for (int i = 0; i < racers.Length; i++) if (racers[i] == racer) return true;
            for (int i = 0; i < racers.Length; i++) if (racers[i] == null) { racers[i] = racer; return true; }
            return false;
        }
        public void UnregisterRacer(VehicleController racer)
        { for (int i = 0; i < racers.Length; i++) if (racers[i] == racer) racers[i] = null; }
        public void ConfigureInitialTrips(TrafficInitialTrip[] trips)
        {
            if (initialized) throw new InvalidOperationException("Initial trips are scene-load content, not live spawns.");
            initialTrips = trips ?? Array.Empty<TrafficInitialTrip>();
        }
        private void Start() { Initialize(); }
        private void Initialize()
        {
            if (initialized || roads == null || roads.Lanes.Count == 0 || pool.Length == 0) return;
            if (profile == null) { profile = ScriptableObject.CreateInstance<TrafficWorldProfile>(); ownsProfile = true; }
            for (int i = 0; i < pool.Length; i++)
                if (pool[i] != null) pool[i].ConfigureVehicle(pool[i].VehicleProfile);
            // Reserve a representation for every admitted trip. Capacity pressure queues demand at portals,
            // never creates invisible collider-less traffic inside the player's interaction horizon.
            int capacity = Mathf.Clamp(Mathf.Min(profile.maximumLogicalAgents, pool.Length), 1, 4096);
            assigned = new RoadVehicleMotor[capacity]; Simulation = new TrafficSimulation(roads.Lanes, capacity);
            disabledClosures = new IDisposable[capacity];
            Registry = new TrafficSpatialRegistry(capacity + interests.Length);
            if (signals != null) signals.ConfigurePlans(roads.Lanes, null);
            BuildDemand(); initialized = true;
            for (int i = 0; i < initialTrips.Length; i++)
            {
                TrafficInitialTrip trip = initialTrips[i]; if (trip == null) continue;
                int origin = FindLane(trip.originLaneId), destination = FindLane(trip.destinationLaneId);
                RoadVehicleMotor motor = FreeRepresentation();
                if (origin < 0 || motor == null || trip.along >= roads.Lanes[origin].Length) continue;
                Vector3 point = roads.Lanes[origin].Sample(trip.along, out Vector3 direction);
                if (!PlacementClear(motor, point, direction) || !Simulation.TrySpawn(origin, destination, trip.seed, out int slot, profile.drivers)) continue;
                TrafficAgentState a = Simulation[slot]; a.Position = point; a.Along = trip.along; a.Forward = direction;
                BindRepresentation(slot, motor); Registry.Add(Simulation.Snapshot(slot));
            }
        }
        private void BuildDemand()
        {
            int count = 0;
            bool authored = profile.demand != null && profile.demand.Length > 0;
            if (authored) count = profile.demand.Length;
            else for (int i = 0; i < roads.Lanes.Count; i++) if (roads.Lanes[i].Portal) count++;
            origins = new int[count]; destinations = new int[count]; admissionTimes = new float[count]; rates = new float[count];
            demandMultipliers = new float[count];
            for (int i = 0, lane = 0; i < count; i++)
            {
                if (authored)
                {
                    TrafficDemandFlow flow = profile.demand[i];
                    if (flow == null) throw new ArgumentException("Authored traffic OD flow cannot be null.");
                    origins[i] = FindLane(flow.originLaneId); destinations[i] = FindLane(flow.destinationLaneId);
                    if (origins[i] < 0 || destinations[i] < 0 || origins[i] == destinations[i])
                        throw new ArgumentException("Authored OD flow requires two distinct existing lane IDs.");
                    rates[i] = flow.vehiclesPerHour;
                }
                else
                {
                    while (lane < roads.Lanes.Count && !roads.Lanes[lane].Portal) lane++;
                    origins[i] = lane++; destinations[i] = -1; rates[i] = profile.defaultPortalDemandPerHour;
                }
            }
            Demand = new TrafficDemandReservoir(rates, profile.worldSeed, profile.maximumPendingPerFlow);
        }
        private int FindLane(int id)
        { for (int i = 0; i < roads.Lanes.Count; i++) if (roads.Lanes[i].Id == id) return i; return -1; }

        private void FixedUpdate()
        {
            Initialize(); if (!initialized) return;
            float dt = Mathf.Min(Time.fixedDeltaTime, 0.1f); elapsed += dt;
            using (SpatialMarker.Auto()) Synchronize();
            // Promotion is checked every physics tick, before a distant representation can enter a collision horizon.
            using (LodMarker.Auto()) UpdateRepresentations(false);
            using (MicroMarker.Auto()) Simulation.Step(dt, Registry, this);
            for (int i = 0; i < Capacity; i++)
                if (Simulation[i].Active && Simulation[i].Lod == TrafficSimulationLod.Nearby) assigned[i].UpdateDistantProxy();
            if (elapsed < nextPopulation) return;
            nextPopulation = elapsed + Mathf.Max(0.1f, profile.populationInterval);
            using (PopulationMarker.Auto())
            {
                UpdateRepresentations(true);
                for (int i = 0; i < origins.Length; i++) demandMultipliers[i] = profile.DemandMultiplier(roads.Lanes[origins[i]], elapsed);
                Demand.Advance(Mathf.Min(60, elapsed - lastPopulation), demandMultipliers); lastPopulation = elapsed;
                AdmitDemand();
            }
        }
        private void Synchronize()
        {
            cameraCount = Camera.allCamerasCount <= cameras.Length ? Camera.GetAllCameras(cameras) : -1;
            Registry.BeginFrame(); interestCount = 0;
            AddInterest(player, 1, TrafficActorKind.Player, false);
            for (int i = 0; i < police.Length; i++)
                if (police[i] != null && police[i].isActiveAndEnabled)
                    AddInterest(police[i].Vehicle, 10 + i, TrafficActorKind.Police,
                        !police[i].IsDisabled && police[i].State == VehiclePoliceUnitState.Engaged);
            for (int i = 0; i < racers.Length; i++) AddInterest(racers[i], 100 + i, TrafficActorKind.Racer, false);
            for (int i = 0; i < Capacity; i++)
            {
                TrafficAgentState a = Simulation[i]; if (!a.Active) continue;
                RoadVehicleMotor motor = assigned[i];
                if (a.Lod == TrafficSimulationLod.Physical && motor != null)
                {
                    TrafficVehicleProfile vehicleProfile = motor.VehicleProfile;
                    Simulation.SynchronizePhysical(i, motor.Body.position - Vector3.up * 0.65f, motor.Body.linearVelocity,
                        motor.transform.forward, vehicleProfile.colliderSize.z * 0.5f, vehicleProfile.colliderSize.x * 0.5f,
                        vehicleProfile.comfortAccelerationLimit, vehicleProfile.brakingLimit);
                    a.Profile.actuatorSeconds = 0.18f;
                    ObservePhysicalObstacles(i, motor);
                }
                Registry.Add(Simulation.Snapshot(i));
            }
        }
        private void ObservePhysicalObstacles(int slot, RoadVehicleMotor motor)
        {
            TrafficAgentState a = Simulation[slot]; Vector3 forward = motor.transform.forward;
            Vector3 origin = motor.Body.position + Vector3.up * 0.45f + forward * (a.HalfLength + 0.1f);
            int count = Physics.SphereCastNonAlloc(origin, Mathf.Min(0.75f, a.HalfWidth), forward, obstacleHits,
                Mathf.Clamp(a.Speed * 5 + 20, 20, 120), ~0, QueryTriggerInteraction.Ignore);
            float gap = count == obstacleHits.Length ? 0 : float.PositiveInfinity, speed = 0;
            for (int j = 0; j < count; j++)
            {
                RaycastHit hit = obstacleHits[j]; if (hit.rigidbody == motor.Body || hit.distance >= gap) continue;
                gap = hit.distance; speed = hit.rigidbody != null ? Mathf.Max(0, Vector3.Dot(hit.rigidbody.linearVelocity, forward)) : 0;
            }
            Simulation.ObserveObstacle(slot, gap, speed);
        }
        private void AddInterest(VehicleController vehicle, int id, TrafficActorKind kind, bool siren)
        {
            if (vehicle == null || !vehicle.isActiveAndEnabled || vehicle.Body == null || interestCount == interests.Length) return;
            Vector3 position = vehicle.Body.position;
            int lane = roads.Lanes.NearestLane(position, vehicle.transform.forward, out float along);
            var state = new TrafficVehicleSnapshot { Id = id, Kind = kind, Lane = lane, Along = along,
                Position = position - Vector3.up * 0.65f, Velocity = vehicle.Body.linearVelocity, Forward = vehicle.transform.forward,
                HalfLength = 2.4f, HalfWidth = 1.1f, Siren = siren };
            interests[interestCount] = state; interestBodies[interestCount++] = vehicle.Body; Registry.Add(state);
        }
        private void AdmitDemand()
        {
            for (int row = 0; row < origins.Length; row++)
            {
                if (elapsed < admissionTimes[row] || demandMultipliers[row] <= 0 || !Demand.TryPeek(row, out int seed)) continue;
                int origin = origins[row]; bool admitted = false;
                if (!roads.Lanes.IsClosed(origin))
                {
                    Vector3 point = roads.Lanes[origin].Sample(0, out _);
                    if (HiddenAndSafe(point, 0) && Registry.Query(point, 15, query, out bool saturated) == 0 && !saturated)
                    {
                        RoadVehicleMotor motor = FreeRepresentation(profile.VehicleCategory(roads.Lanes[origin].District, seed));
                        for (int attempt = 0; motor != null && attempt < 8 && !admitted; attempt++)
                        {
                            int destination = destinations[row] >= 0 ? destinations[row] : RandomDestination(origin, unchecked(seed + attempt * 7919));
                            if (destination < 0) continue;
                            if (!PlacementClear(motor, point, roads.Lanes[origin].Sample(2, out _) - point)) break;
                            if (!Simulation.TrySpawn(origin, destination, seed, out int slot, profile.drivers)) continue;
                            BindRepresentation(slot, motor);
                            Registry.Add(Simulation.Snapshot(slot)); admitted = true;
                        }
                    }
                }
                if (!admitted) DeferredArrivals++; else Demand.CommitAdmission(row);
                // Backlogged abstract demand still materializes one physically valid configuration at a time.
                admissionTimes[row] = elapsed + (admitted ? Mathf.Max(1, profile.minimumPortalHeadway) : 2);
            }
        }
        private void BindRepresentation(int slot, RoadVehicleMotor motor)
        {
            assigned[slot] = motor; TrafficAgentState a = Simulation[slot];
            a.HalfLength = motor.VehicleProfile.colliderSize.z * 0.5f; a.HalfWidth = motor.VehicleProfile.colliderSize.x * 0.5f;
            a.Profile.acceleration = Mathf.Min(a.Profile.acceleration, motor.VehicleProfile.comfortAccelerationLimit);
            a.Profile.emergencyBraking = Mathf.Min(a.Profile.emergencyBraking, motor.VehicleProfile.brakingLimit);
            a.lateralAcceleration = motor.VehicleProfile.maximumLateralAcceleration;
            motor.PlaceWhileInactive(a.Position + Vector3.up * 0.65f, Quaternion.LookRotation(a.Forward), a.Velocity);
            motor.Bind(this, slot); a.Lod = TrafficSimulationLod.Physical; motor.gameObject.SetActive(true);
            motor.SetDistantProxy(false);
        }
        private RoadVehicleMotor FreeRepresentation(TrafficVehicleCategory? category = null)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                RoadVehicleMotor motor = pool[i];
                if (motor == null || motor.gameObject.activeSelf || motor.Agent != null || motor.VehicleProfile == null || motor.Vehicle == null) continue;
                if (category.HasValue && motor.VehicleProfile.category != category.Value) continue;
                return motor;
            }
            return null;
        }
        private int RandomDestination(int origin, int seed)
        {
            if (origins.Length == 0) return -1;
            var generator = new TrafficRandom(seed);
            int portal = origins[generator.Index(origins.Length)];
            if (portal < 0 || roads.Lanes[portal].FromNode == roads.Lanes[origin].FromNode) return -1;
            int node = roads.Lanes[portal].FromNode;
            int offset = generator.Index(roads.Lanes.Count);
            for (int j = 0; j < roads.Lanes.Count; j++)
            {
                int i = (j + offset) % roads.Lanes.Count;
                if (roads.Lanes[i].ToNode == node && roads.Lanes[i].Intersection < 0 && !roads.Lanes.IsClosed(i)) return i;
            }
            return -1;
        }
        private void UpdateRepresentations(bool allowDemotion)
        {
            for (int i = 0; i < Capacity; i++)
            {
                TrafficAgentState a = Simulation[i]; if (!a.Active) continue;
                RoadVehicleMotor motor = assigned[i]; if (motor == null) continue;
                if (allowDemotion && (a.Arrived || a.Disabled && a.IncidentAge > profile.disabledMinimumLifetime)
                    && HiddenAndSafe(a.Position, a.Speed))
                {
                    Simulation.Retire(i); disabledClosures[i]?.Dispose(); disabledClosures[i] = null;
                    motor.Unbind(); motor.gameObject.SetActive(false); assigned[i] = null; continue;
                }
                bool near = InteractionRelevant(a, a.Lod == TrafficSimulationLod.Physical ? profile.lodHysteresis : 0);
                if (near || a.Disabled || a.Driver.State == TrafficDriverState.Recovering)
                {
                    if (a.Lod != TrafficSimulationLod.Physical) SetRepresentation(i, TrafficSimulationLod.Physical);
                }
                else if (a.Lod == TrafficSimulationLod.Logical && !BeyondEveryCamera(a.Position, 200)) SetRepresentation(i, TrafficSimulationLod.Nearby);
                else if (allowDemotion && HiddenAndSafe(a.Position, a.Speed))
                {
                    // A renderer/kinematic proxy persists at visual distance. No-GO LOD is only beyond
                    // every camera's entire far-clip sphere, including mirrors, plus a wake-up margin.
                    SetRepresentation(i, BeyondEveryCamera(a.Position, 400) ? TrafficSimulationLod.Logical : TrafficSimulationLod.Nearby);
                }
            }
        }
        private bool InteractionRelevant(TrafficAgentState a, float hysteresis)
        {
            if (interestCount == interests.Length || cameraCount < 0) return true;
            for (int i = 0; i < interestCount; i++)
            {
                float radius = Mathf.Max(profile.physicalRadius, TrafficSpawnSafety.RequiredClearance(interests[i].Velocity.magnitude,
                    a.Speed, profile.promotionLookAheadSeconds) + 50) + hysteresis;
                if ((a.Position - interests[i].Position).sqrMagnitude < radius * radius) return true;
            }
            return false;
        }
        private void SetRepresentation(int slot, TrafficSimulationLod lod)
        {
            TrafficAgentState a = Simulation[slot]; if (a.Lod == lod) return;
            RoadVehicleMotor motor = assigned[slot]; HandoverCount++;
            if (lod != TrafficSimulationLod.Physical) { a.Profile.actuatorSeconds = 0; Simulation.ObserveObstacle(slot, float.PositiveInfinity, 0); }
            if (lod == TrafficSimulationLod.Logical)
            { motor.gameObject.SetActive(false); a.Lod = lod; return; }
            if (!motor.gameObject.activeSelf)
            {
                motor.PlaceWhileInactive(a.Position + Vector3.up * 0.65f, Quaternion.LookRotation(a.Forward), a.Velocity);
                motor.gameObject.SetActive(true);
            }
            a.Lod = lod; motor.SetDistantProxy(lod != TrafficSimulationLod.Physical);
        }
        private bool BeyondEveryCamera(Vector3 point, float margin)
        {
            if (cameraCount < 0) return false;
            for (int i = 0; i < cameraCount; i++)
                if (cameras[i] != null && (point - cameras[i].transform.position).sqrMagnitude
                    < Mathf.Pow(cameras[i].farClipPlane + margin, 2)) return false;
            return true;
        }
        private bool HiddenAndSafe(Vector3 point, float speed)
        {
            if (cameraCount < 0) return false;
            for (int i = 0; i < interestCount; i++) if (!TrafficSpawnSafety.IsOutsideInteraction(point, speed, interests[i])) return false;
            for (int i = 0; i < cameraCount; i++)
            {
                Camera camera = cameras[i]; if (camera == null) continue;
                Vector3 viewport = camera.WorldToViewportPoint(point + Vector3.up);
                if (viewport.z > 0 && viewport.z < camera.farClipPlane + 20 && viewport.x > -0.2f && viewport.x < 1.2f
                    && viewport.y > -0.2f && viewport.y < 1.2f) return false;
            }
            return true;
        }
        private bool PlacementClear(RoadVehicleMotor motor, Vector3 point, Vector3 forward)
        {
            if (forward.sqrMagnitude < 0.01f) return false;
            TrafficVehicleProfile p = motor.VehicleProfile;
            // Spawn-height and hull match the physical rig; the road lies below the volume.
            int count = Physics.OverlapBoxNonAlloc(point + Vector3.up * 0.65f + p.colliderCenter,
                p.colliderSize * 0.5f, spawnHits, Quaternion.LookRotation(forward), ~0, QueryTriggerInteraction.Ignore);
            return count == 0;
        }
        public void NotifyCollision(int slot, float speed)
        {
            if (Simulation == null || slot < 0 || slot >= Capacity) return;
            Simulation.NotifyCollision(slot, speed); var agent = Simulation[slot];
            if (agent.Disabled && disabledClosures[slot] == null && roads.Lanes[agent.Lane].Intersection < 0)
                disabledClosures[slot] = roads.Lanes.AcquireClosure(agent.Lane, agent.Along);
        }
        public RoadSignalAspect Signal(int intersection, Vector3 approach, float simulationTime) => signals != null
            && intersection >= 0 && intersection < roads.Nodes.Count ? signals.Aspect(roads.Nodes[intersection].position, approach, Time.time)
            : RoadSignalAspect.Green;
        public RoadSignalAspect MovementSignal(int intersection, int movementLaneId, Vector3 approach, float simulationTime) =>
            signals != null && signals.TryMovementAspect(intersection, movementLaneId, Time.time, out RoadSignalAspect aspect)
                ? aspect : Signal(intersection, approach, simulationTime);
        public bool CanPerceive(TrafficVehicleSnapshot observer, TrafficVehicleSnapshot target)
        {
            Vector3 origin = observer.Position + Vector3.up, delta = target.Position - observer.Position;
            float distance = delta.magnitude; if (distance > 160 || distance < 0.01f) return distance < 0.01f;
            Rigidbody self = FindBody(observer.Id), other = FindBody(target.Id);
            int count = Physics.RaycastNonAlloc(origin, delta / distance, visibilityHits, distance, ~0, QueryTriggerInteraction.Ignore);
            if (count == visibilityHits.Length) return false;
            for (int i = 0; i < count; i++)
                if (visibilityHits[i].rigidbody == null || visibilityHits[i].rigidbody != self && visibilityHits[i].rigidbody != other) return false;
            return true;
        }
        private Rigidbody FindBody(int id)
        {
            for (int i = 0; i < interestCount; i++) if (interests[i].Id == id) return interestBodies[i];
            for (int i = 0; i < Capacity; i++) if (Simulation[i].Active && Simulation[i].Id == id) return assigned[i]?.Body;
            return null;
        }
        private void OnDestroy()
        {
            if (disabledClosures != null) foreach (var closure in disabledClosures) closure?.Dispose();
            if (ownsProfile && profile != null) Destroy(profile);
        }
    }
}
