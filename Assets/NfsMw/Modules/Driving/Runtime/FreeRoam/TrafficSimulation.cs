using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public interface ITrafficEnvironment
    {
        RoadSignalAspect Signal(int intersection, Vector3 approach, float simulationTime);
        bool CanPerceive(TrafficVehicleSnapshot observer, TrafficVehicleSnapshot target);
    }
    public interface ITrafficMovementSignals
    {
        RoadSignalAspect MovementSignal(int intersection, int movementLaneId, Vector3 approach, float simulationTime);
    }

    public sealed class TrafficAgentState
    {
        public int Id { get; internal set; }
        public bool Active { get; internal set; }
        public bool Arrived { get; internal set; }
        public bool Disabled { get; internal set; }
        public int Lane { get; internal set; }
        public float Along { get; internal set; }
        public float Speed { get; internal set; }
        public Vector3 Position { get; internal set; }
        public Vector3 Forward { get; internal set; }
        public Vector3 Velocity { get; internal set; }
        public Vector3 Aim { get; internal set; }
        public float TargetSpeed { get; internal set; }
        public float HalfLength { get; internal set; } = 2.05f;
        public float HalfWidth { get; internal set; } = 0.9f;
        public float Gap { get; internal set; }
        public int Leader { get; internal set; } = -1;
        public bool Panic { get; internal set; }
        public bool Yielding { get; internal set; }
        public TrafficSimulationLod Lod { get; internal set; }
        public TrafficLaneChangeDecision LaneDecision { get; internal set; }
        public TrafficIntersectionDecision IntersectionDecision { get; internal set; }
        public TrafficDriverProfile Profile { get; internal set; }
        public TrafficDriver Driver { get; internal set; }
        public float IncidentAge { get; internal set; }
        public int Destination { get; internal set; } = -1;
        public bool RouteBlocked { get; internal set; }
        public int NextLane => routeIndex + 1 < routeCount ? route[routeIndex + 1] : -1;
        public int CommittedMovement => reserved;
        internal readonly int[] route;
        internal int routeCount, routeIndex, laneTarget = -1, reserved = -1, occupiedMovement = -1, revision;
        internal float laneBlend, laneCooldown, waiting, stopped, lateral, nextLaneDecision;
        internal float physicalAccelerationLimit = 4, physicalBrakingLimit = 12;
        internal float obstacleGap = float.PositiveInfinity, obstacleSpeed, lateralAcceleration = 2.5f;
        internal TrafficAgentState(int routeCapacity)
        { route = new int[routeCapacity]; Profile = new TrafficDriverProfile(); Driver = new TrafficDriver(Profile); }
    }

    public struct TrafficSimulationStatistics
    {
        public int Active, Physical, Nearby, Logical, Queued, Disabled, LaneChanges, Completed, Spawned, Collisions, TruncatedQueries, PhysicalIncursions;
        public float MeanSpeed, Elapsed;
    }

    // Fixed-step microscopic state, independent of GameObjects and race results.
    // The road owns topology/closures; this module owns trips, decisions and physical/logical continuity.
    public sealed class TrafficSimulation
    {
        private readonly RoadLaneNetwork roads;
        private readonly TrafficAgentState[] agents;
        private readonly int[] neighbors = new int[128];
        private readonly TrafficDriverProfile defaultFollower = new TrafficDriverProfile();
        private readonly int[] reservationOwners;
        private int nextId = 1000, tick, spawned, completed, laneChanges, collisions, truncated, physicalIncursions;
        private float clock;
        public int Capacity => agents.Length;
        public TrafficAgentState this[int index] => agents[index];
        public TrafficSimulationStatistics Statistics { get; private set; }

        public TrafficSimulation(RoadLaneNetwork network, int capacity)
        {
            roads = network ?? throw new ArgumentNullException(nameof(network));
            if (capacity < 1 || capacity > 4096) throw new ArgumentOutOfRangeException(nameof(capacity));
            agents = new TrafficAgentState[capacity];
            reservationOwners = new int[network.Count];
            for (int i = 0; i < reservationOwners.Length; i++) reservationOwners[i] = -1;
            for (int i = 0; i < capacity; i++) agents[i] = new TrafficAgentState(Mathf.Max(1, network.Count));
        }

        public bool TrySpawn(int origin, int destination, int seed, out int slot, TrafficDriverPopulationProfile population = null)
        {
            slot = -1;
            if (origin == destination || roads.IsClosed(origin) || roads.IsClosed(destination)) return false;
            Vector3 entrance = roads[origin].Start;
            for (int i = 0; i < Capacity; i++)
                if (agents[i].Active && (agents[i].Position - entrance).sqrMagnitude
                    < Mathf.Pow(agents[i].HalfLength + 2.05f + 2, 2)) return false;
            for (int i = 0; i < Capacity; i++)
            {
                TrafficAgentState agent = agents[i];
                if (agent.Active) continue;
                if (!roads.TryRoute(origin, destination, seed, agent.route, out int count) || count < 2) return false;
                if (nextId == int.MaxValue) return false;
                agent.Id = nextId++; agent.Active = true; agent.Arrived = agent.Disabled = false;
                agent.routeCount = count; agent.routeIndex = 0; agent.Lane = origin; agent.Along = 0;
                agent.Destination = destination; agent.RouteBlocked = false;
                agent.Speed = 0; agent.Velocity = Vector3.zero;
                agent.Position = roads[origin].Sample(0, out Vector3 forward); agent.Forward = forward;
                agent.Aim = agent.Position + forward * 8; agent.TargetSpeed = 0;
                if (population != null) population.SampleInto(seed, agent.Profile); else agent.Profile.ApplySeed(seed);
                agent.Driver.Reset();
                agent.Lod = TrafficSimulationLod.Logical; agent.laneTarget = agent.reserved = agent.occupiedMovement = -1;
                agent.laneBlend = agent.laneCooldown = agent.waiting = agent.stopped = agent.lateral = agent.IncidentAge = 0;
                agent.nextLaneDecision = clock + i * 0.017f % 0.2f; agent.revision = roads.Revision;
                agent.HalfLength = 2.05f; agent.HalfWidth = 0.9f; agent.physicalAccelerationLimit = 4; agent.physicalBrakingLimit = 12;
                agent.Panic = agent.Yielding = false; agent.LaneDecision = default; agent.IntersectionDecision = default;
                agent.Gap = float.PositiveInfinity; agent.Leader = -1;
                agent.obstacleGap = float.PositiveInfinity; agent.obstacleSpeed = 0; agent.lateralAcceleration = 2.5f;
                spawned++; slot = i; return true;
            }
            return false;
        }

        public void Retire(int slot)
        {
            TrafficAgentState agent = agents[slot];
            if (!agent.Active) return;
            if (!agent.Arrived && !agent.Disabled) throw new InvalidOperationException("Active trips must retain continuity.");
            if (agent.reserved >= 0 && reservationOwners[agent.reserved] == slot) reservationOwners[agent.reserved] = -1;
            agent.Active = false; agent.reserved = agent.occupiedMovement = -1; if (agent.Arrived) completed++;
        }

        public TrafficVehicleSnapshot Snapshot(int slot)
        {
            TrafficAgentState a = agents[slot];
            return new TrafficVehicleSnapshot { Id = a.Id, AgentSlot = slot, Kind = TrafficActorKind.Civilian, Lane = a.Lane, Along = a.Along,
                Position = a.Position, Forward = a.Forward, Velocity = a.Velocity, HalfLength = a.HalfLength, HalfWidth = a.HalfWidth };
        }

        public void SynchronizePhysical(int slot, Vector3 position, Vector3 velocity, Vector3 forward, float halfLength, float halfWidth,
            float maximumAcceleration, float maximumBraking)
        {
            TrafficAgentState a = agents[slot];
            if (!a.Active) return;
            if (!TrafficDriver.Finite(position) || !TrafficDriver.Finite(velocity) || !TrafficDriver.Finite(forward)
                || forward.sqrMagnitude < 0.5f || !TrafficDriver.Finite(halfLength) || !TrafficDriver.Finite(halfWidth)
                || halfLength <= 0 || halfWidth <= 0 || !TrafficDriver.Finite(maximumAcceleration) || !TrafficDriver.Finite(maximumBraking))
                throw new ArgumentException("Physical traffic observations must be finite and have valid dimensions/heading.");
            a.Position = position; a.Velocity = velocity; a.Forward = forward;
            a.Speed = Mathf.Max(0, Vector3.Dot(velocity, forward)); a.HalfLength = halfLength; a.HalfWidth = halfWidth;
            a.physicalAccelerationLimit = Mathf.Clamp(maximumAcceleration, 0.5f, 4);
            a.physicalBrakingLimit = Mathf.Clamp(maximumBraking, 4, 14);
            a.Profile.acceleration = Mathf.Min(a.Profile.acceleration, a.physicalAccelerationLimit);
            a.Profile.emergencyBraking = Mathf.Min(a.Profile.emergencyBraking, a.physicalBrakingLimit);
            a.Along = roads[a.Lane].Project(position, out _);
            // Physics is authoritative even if a collision pushes a vehicle past a red line. Do
            // not freeze its route on the old approach or invent a legal reservation. Reconcile
            // only connected route corridors, never snap the body or choose an unrelated road.
            for (int crossed = 0; crossed < 4 && a.NextLane >= 0; crossed++)
            {
                RoadLane current = roads[a.Lane], next = roads[a.NextLane];
                Vector3 end = current.Sample(current.Length, out Vector3 tangent);
                if (Vector3.Dot(position - end, tangent) < 0) break;
                float nextAlong = next.Project(position, out float error);
                if (error > next.Width * 0.5f + a.HalfWidth + 1) break;
                int previousLane = a.Lane;
                AdvanceLane(a, true);
                if (a.Lane == previousLane) break;
                a.Along = nextAlong;
            }
        }

        public void NotifyCollision(int slot, float relativeSpeed)
        {
            TrafficAgentState a = agents[slot]; if (!a.Active || relativeSpeed < 1) return;
            collisions++; a.Driver.NotifyCollision(relativeSpeed); a.Panic = true; a.IncidentAge = 0;
            if (relativeSpeed >= 18) a.Disabled = true;
        }

        public void ObserveObstacle(int slot, float bumperGap, float leaderSpeed)
        {
            TrafficAgentState a = agents[slot];
            a.obstacleGap = float.IsPositiveInfinity(bumperGap) ? bumperGap : TrafficDriver.Finite(bumperGap) ? Mathf.Max(0, bumperGap) : 0;
            a.obstacleSpeed = TrafficDriver.Finite(leaderSpeed) ? Mathf.Max(0, leaderSpeed) : 0;
        }

        public void Step(float dt, TrafficSpatialRegistry registry, ITrafficEnvironment environment)
        {
            if (!TrafficDriver.Finite(dt) || dt <= 0) return;
            if (dt > 0.1f) throw new ArgumentOutOfRangeException(nameof(dt), "Traffic must be advanced in bounded fixed steps.");
            if (registry == null || environment == null) throw new ArgumentNullException(nameof(registry));
            clock += dt; tick = (tick + 1) % Capacity;
            for (int j = 0; j < Capacity; j++)
            {
                int index = (j + tick) % Capacity;
                if (agents[index].Active) StepAgent(index, dt, registry, environment);
            }
            var statistics = new TrafficSimulationStatistics { Elapsed = clock, Spawned = spawned, Completed = completed,
                LaneChanges = laneChanges, Collisions = collisions, TruncatedQueries = truncated, PhysicalIncursions = physicalIncursions };
            for (int i = 0; i < Capacity; i++)
            {
                TrafficAgentState a = agents[i]; if (!a.Active) continue;
                statistics.Active++; statistics.MeanSpeed += a.Speed;
                if (a.Lod == TrafficSimulationLod.Physical) statistics.Physical++;
                if (a.Lod == TrafficSimulationLod.Nearby) statistics.Nearby++;
                if (a.Lod == TrafficSimulationLod.Logical) statistics.Logical++;
                if (a.Speed < 0.5f) statistics.Queued++;
                if (a.Disabled) statistics.Disabled++;
            }
            if (statistics.Active > 0) statistics.MeanSpeed /= statistics.Active;
            Statistics = statistics;
        }

        private void StepAgent(int index, float dt, TrafficSpatialRegistry registry, ITrafficEnvironment environment)
        {
            TrafficAgentState a = agents[index]; RoadLane lane = roads[a.Lane];
            if (a.Disabled || a.Driver.State == TrafficDriverState.Recovering) a.IncidentAge += dt;
            int count = registry.Query(a.Position, Mathf.Clamp(40 + a.Speed * 5, 60, 160), neighbors, out bool saturated);
            if (saturated) truncated++;
            a.laneCooldown = Mathf.Max(0, a.laneCooldown - dt);
            if (a.reserved >= 0 && a.routeIndex > 0 && a.route[a.routeIndex - 1] == a.reserved
                && lane.Intersection < 0 && a.Along > a.HalfLength + 2)
            { if (reservationOwners[a.reserved] == index) reservationOwners[a.reserved] = -1; a.reserved = -1; }
            if (a.occupiedMovement >= 0 && lane.Intersection < 0 && a.Along > a.HalfLength + 2) a.occupiedMovement = -1;
            float target = Mathf.Min(lane.Speed * a.Profile.speedMultiplier, lane.CurvatureSpeed * Mathf.Sqrt(a.lateralAcceleration / 2.5f));
            a.Panic = false; a.Yielding = false;
            FindLeader(a, a.Lane, registry, count, out float gap, out float leaderSpeed, out int leader);
            if (a.obstacleGap < gap) { gap = a.obstacleGap; leaderSpeed = a.obstacleSpeed; }
            float blockage = roads.DistanceToBlockage(a.Lane, a.Along);
            if (blockage < gap) { gap = Mathf.Max(0, blockage - a.HalfLength); leaderSpeed = 0; }
            TrafficVehicleSnapshot observer = Snapshot(index);
            for (int i = 0; i < count; i++)
            {
                TrafficVehicleSnapshot other = registry[neighbors[i]];
                if (other.Id == a.Id || other.Kind == TrafficActorKind.Civilian) continue;
                Vector3 offset = other.Position - a.Position, relative = other.Velocity - a.Velocity;
                float approach = relative.sqrMagnitude > 0.01f ? -Vector3.Dot(offset, relative) / relative.sqrMagnitude : float.PositiveInfinity;
                bool imminent = approach > 0 && approach < 3 && (offset + relative * approach).sqrMagnitude < 25;
                if ((!imminent && !(other.Siren && offset.sqrMagnitude < 6400)) || !environment.CanPerceive(observer, other)) continue;
                a.Panic |= imminent;
                // Rear threats inhibit discretionary moves; braking in their path would make the threat worse.
                if (other.Siren && other.Kind == TrafficActorKind.Police) a.Yielding = true;
                if (imminent && Vector3.Dot(offset, a.Forward) > 0 && Vector3.Dot(other.Forward, a.Forward) < 0.5f)
                { gap = Mathf.Min(gap, Mathf.Max(0, Vector3.Dot(offset, a.Forward) - a.HalfLength - other.HalfLength)); leaderSpeed = 0; }
            }
            a.Leader = leader;
            int next = a.routeIndex + 1 < a.routeCount ? a.route[a.routeIndex + 1] : -1;
            if (a.revision != roads.Revision && a.laneTarget < 0 && a.reserved < 0)
            {
                int goal = a.Destination;
                a.revision = roads.Revision;
                a.RouteBlocked = !roads.TryRoute(a.Lane, goal, a.Id, a.route, out int newCount, true);
                if (!a.RouteBlocked)
                { a.routeCount = newCount; a.routeIndex = 0; next = newCount > 1 ? a.route[1] : -1; }
            }
            bool adjacent = next >= 0 && (next == lane.Left || next == lane.Right);
            if (next < 0)
            {
                gap = Mathf.Min(gap, Mathf.Max(0, lane.Length - a.Along - a.HalfLength)); leaderSpeed = 0;
                if (a.Lane == a.Destination && lane.Length - a.Along < a.HalfLength + a.Profile.standstillGap + 1 && a.Speed < 0.5f) a.Arrived = true;
            }
            if (next >= 0 && !adjacent)
            {
                float remaining = lane.Length - a.Along;
                float cornerTarget = Mathf.Min(roads[next].Speed, roads[next].CurvatureSpeed * Mathf.Sqrt(a.lateralAcceleration / 2.5f));
                target = Mathf.Min(target, Mathf.Sqrt(cornerTarget * cornerTarget + 2 * a.Profile.comfortableBraking * Mathf.Max(0, remaining - 4)));
                if (roads[next].Intersection >= 0 && a.reserved != next && remaining < 100)
                    EvaluateIntersection(index, next, remaining, registry, count, environment, ref gap, ref leaderSpeed);
                if (roads.IsClosed(next)) { gap = Mathf.Min(gap, Mathf.Max(0, remaining - a.HalfLength)); leaderSpeed = 0; }
            }
            if (adjacent && a.laneTarget < 0)
            {
                // Reserve manoeuvring room at an ending lane while waiting for a safe merge.
                // A delayed target-speed drop at the final 10m can strand the car past the
                // decision horizon. A virtual stop queue uses the same fresh braking bound.
                float mergeStop = Mathf.Max(0, lane.Length - a.Along - a.HalfLength - 8);
                if (mergeStop < gap) { gap = mergeStop; leaderSpeed = 0; }
            }
            if (a.laneTarget < 0 && a.laneCooldown <= 0 && clock >= a.nextLaneDecision && lane.Intersection < 0 && !a.Panic && !a.Yielding)
            {
                a.nextLaneDecision = clock + 0.2f;
                int candidate = adjacent ? next : lane.Left >= 0 ? lane.Left : lane.Right;
                TryLaneChange(a, candidate, adjacent, target, registry, count);
            }
            if (a.laneTarget >= 0)
            {
                FindLeader(a, a.laneTarget, registry, count, out float targetGap, out float targetLeader, out _);
                if (targetGap < gap) { gap = targetGap; leaderSpeed = targetLeader; }
                a.laneBlend = Mathf.Min(1, a.laneBlend + dt / 3.5f);
                roads[a.laneTarget].Project(a.Position, out float lateralError);
                if (a.laneBlend >= 1 && (a.Lod != TrafficSimulationLod.Physical || lateralError < 0.6f))
                {
                    a.Lane = a.laneTarget; a.Along = roads[a.Lane].Project(a.Position, out _); a.laneTarget = -1;
                    if (next == a.Lane) a.routeIndex++;
                    else if (roads.TryRoute(a.Lane, a.Destination, a.Id, a.route, out int newCount))
                    { a.routeCount = newCount; a.routeIndex = 0; a.RouteBlocked = false; }
                    else { a.route[0] = a.Lane; a.routeCount = 1; a.routeIndex = 0; a.RouteBlocked = true; }
                    a.laneCooldown = 6; laneChanges++; lane = roads[a.Lane];
                }
            }
            a.stopped = a.Speed < 0.2f && lane.Length - a.Along < a.HalfLength + 5 ? a.stopped + dt : 0;
            a.waiting = a.IntersectionDecision != TrafficIntersectionDecision.Proceed && lane.Length - a.Along < 40 ? a.waiting + dt : 0;
            if (a.Arrived || a.Disabled) target = 0;
            if (saturated) { gap = 0; leaderSpeed = 0; }
            a.Gap = gap;
            float nextSpeed = a.Driver.Step(dt, a.Speed, target, gap, leaderSpeed, false);
            a.TargetSpeed = nextSpeed;
            float shoulder = a.Yielding && lane.Right < 0 && lane.Intersection < 0 ? Mathf.Max(0, lane.Width * 0.5f - a.HalfWidth - 0.25f) : 0;
            float lateralLimit = Mathf.Max(0, lane.Width * 0.5f - a.HalfWidth - 0.25f);
            a.lateral = Mathf.MoveTowards(a.lateral, Mathf.Clamp(shoulder + a.Profile.lateralPreference, -lateralLimit, lateralLimit), dt * 0.4f);
            float lookAhead = Mathf.Clamp(5 + a.Speed * 0.7f, 5, 20);
            a.Aim = SampleAhead(a, lookAhead, out Vector3 aimDirection) + Vector3.Cross(Vector3.up, aimDirection) * a.lateral;
            if (a.laneTarget >= 0)
            {
                RoadLane change = roads[a.laneTarget]; float s = change.Project(a.Position, out _);
                Vector3 changeAim = change.Sample(s + lookAhead, out _);
                float t = a.laneBlend; float blend = t * t * t * (10 + t * (-15 + 6 * t));
                a.Aim = Vector3.Lerp(a.Aim, changeAim, blend);
            }
            if (a.Lod != TrafficSimulationLod.Physical)
            {
                float advance = (a.Speed + nextSpeed) * 0.5f * dt;
                a.Speed = nextSpeed; a.Along += advance;
                if (a.Along >= lane.Length) AdvanceLane(a);
                a.Position = roads[a.Lane].Sample(a.Along, out Vector3 forward); a.Forward = forward;
                a.Position += Vector3.Cross(Vector3.up, forward) * a.lateral;
                if (a.laneTarget >= 0)
                {
                    Vector3 targetPoint = roads[a.laneTarget].Sample(a.Along, out _);
                    float t = a.laneBlend; a.Position = Vector3.Lerp(a.Position, targetPoint, t * t * t * (10 + t * (-15 + 6 * t)));
                }
                a.Velocity = a.Forward * a.Speed;
            }
        }

        private void FindLeader(TrafficAgentState a, int laneIndex, TrafficSpatialRegistry registry, int count,
            out float gap, out float speed, out int leader)
        {
            gap = float.PositiveInfinity; speed = 0; leader = -1;
            RoadLane lane = roads[laneIndex]; float self = lane.Project(a.Position, out _);
            float offset = -self;
            for (int segment = 0; segment < 3; segment++)
            {
                for (int i = 0; i < count; i++)
                {
                    TrafficVehicleSnapshot other = registry[neighbors[i]]; if (other.Id == a.Id) continue;
                    float along = lane.Project(other.Position, out float error);
                    if (error > a.HalfWidth + other.HalfWidth + 0.3f || along + offset <= 0) continue;
                    float clearance = along + offset - a.HalfLength - other.HalfLength;
                    if (clearance >= gap) continue;
                    lane.Sample(along, out Vector3 heading);
                    gap = Mathf.Max(0, clearance); speed = Mathf.Max(0, Vector3.Dot(other.Velocity, heading)); leader = other.Id;
                }
                if (laneIndex != a.Lane || a.routeIndex + segment + 1 >= a.routeCount) break;
                int next = a.route[a.routeIndex + segment + 1]; if (next == lane.Left || next == lane.Right) break;
                offset += lane.Length; lane = roads[next];
            }
        }

        private void TryLaneChange(TrafficAgentState a, int candidate, bool mandatory, float desired, TrafficSpatialRegistry registry, int count)
        {
            if (candidate < 0 || roads.IsClosed(candidate) || roads[a.Lane].Length - a.Along < (mandatory ? 4 : 25)) return;
            RoadLane lane = roads[candidate]; float self = lane.Project(a.Position, out _);
            if (!mandatory && lane.Successors.Count == 0 && lane.Length - self < 120) return;
            FindLeader(a, candidate, registry, count, out float frontGap, out float frontSpeed, out _);
            float rearGap = float.PositiveInfinity, rearSpeed = 0, rearBefore = 0;
            TrafficDriverProfile follower = defaultFollower;
            for (int i = 0; i < count; i++)
            {
                TrafficVehicleSnapshot other = registry[neighbors[i]]; if (other.Id == a.Id) continue;
                float s = lane.Project(other.Position, out float error);
                if (s > self || error > lane.Width * 0.5f + other.HalfWidth) continue;
                float clearance = self - s - a.HalfLength - other.HalfLength;
                if (clearance >= rearGap) continue;
                rearGap = clearance; rearSpeed = other.Velocity.magnitude;
                TrafficAgentState rear = AgentFor(other); follower = rear?.Profile ?? defaultFollower;
                rearBefore = rear?.Driver.DesiredAcceleration ?? 0;
            }
            a.LaneDecision = TrafficLaneChangePlanner.Evaluate(new TrafficLaneChangeObservation { Available = true, Mandatory = mandatory,
                Speed = a.Speed, DesiredSpeed = desired, CurrentAcceleration = a.Driver.DesiredAcceleration,
                FrontGap = frontGap, FrontSpeed = frontSpeed, RearGap = rearGap, RearSpeed = rearSpeed,
                RearDesiredSpeed = lane.Speed * follower.speedMultiplier, RearBeforeAcceleration = rearBefore }, a.Profile, follower);
            if (!a.LaneDecision.Accepted) return;
            // Reject a simultaneous opposing reservation into either occupied lane.
            for (int i = 0; i < Capacity; i++)
                if (agents[i].Active && agents[i].Id != a.Id && agents[i].laneTarget >= 0
                    && (agents[i].laneTarget == candidate || agents[i].Lane == candidate)
                    && (agents[i].Position - a.Position).sqrMagnitude < 1600) return;
            a.laneTarget = candidate; a.laneBlend = 0;
        }

        private void EvaluateIntersection(int slot, int next, float remaining, TrafficSpatialRegistry registry, int count,
            ITrafficEnvironment environment, ref float gap, ref float leaderSpeed)
        {
            TrafficAgentState a = agents[slot];
            RoadLane connector = roads[next]; int outgoing = connector.Successors.Count > 0 ? connector.Successors[0] : -1;
            bool storage = outgoing >= 0 && !roads.IsClosed(outgoing), conflict = false;
            float crossingTime = float.PositiveInfinity;
            for (int i = 0; i < connector.ConflictingMovements.Count; i++)
            {
                int owner = reservationOwners[connector.ConflictingMovements[i]];
                if (owner >= 0 && owner != slot) conflict = true;
            }
            for (int i = 0; i < count; i++)
            {
                TrafficVehicleSnapshot other = registry[neighbors[i]];
                if (other.Id == a.Id) continue;
                TrafficAgentState approaching = AgentFor(other);
                // A pushed/unreserved car is still a physical occupant. Preserve its rear-clear
                // envelope independently of permission, including while entering the exit lane.
                int occupied = approaching != null && approaching.occupiedMovement >= 0 ? approaching.occupiedMovement : other.Lane;
                if (roads.MovementsConflict(next, occupied)) conflict = true;
                if (outgoing >= 0)
                {
                    float s = roads[outgoing].Project(other.Position, out float error);
                    if (error < a.HalfWidth + other.HalfWidth + 0.2f && s < a.HalfLength * 2 + other.HalfLength + a.Profile.standstillGap) storage = false;
                }
                if (other.Kind == TrafficActorKind.Civilian)
                {
                    // Fresh priority-gap checks for competing unsignalized approaches; committed movements
                    // remain protected by the swept-corridor reservation until the rear has cleared.
                    if (connector.EntryControl == RoadEntryControl.Stop || connector.EntryControl == RoadEntryControl.Yield
                        || connector.EntryControl == RoadEntryControl.Uncontrolled)
                    {
                        if (approaching != null && approaching.Speed > 0.5f && approaching.routeIndex + 1 < approaching.routeCount)
                        {
                            int movement = approaching.route[approaching.routeIndex + 1];
                            if (roads.MovementsConflict(next, movement))
                                crossingTime = Mathf.Min(crossingTime, (roads[approaching.Lane].Length - approaching.Along) / approaching.Speed);
                        }
                    }
                    continue;
                }
                Vector3 junction = connector.Sample(connector.Length * 0.5f, out _), offset = junction - other.Position;
                if (Vector3.Dot(offset, other.Forward) <= 0 || !environment.CanPerceive(SnapshotFor(a), other)) continue;
                float arrival = offset.magnitude / Mathf.Max(0.1f, other.Velocity.magnitude);
                if (Vector3.Cross(offset.normalized, other.Forward).magnitude < 0.35f) crossingTime = Mathf.Min(crossingTime, arrival);
            }
            RoadSignalAspect signal = environment is ITrafficMovementSignals movementSignals
                ? movementSignals.MovementSignal(connector.Intersection, connector.Id, a.Forward, clock)
                : environment.Signal(connector.Intersection, a.Forward, clock);
            a.IntersectionDecision = TrafficIntersectionRules.Evaluate(connector.EntryControl,
                signal, a.Speed, remaining - a.HalfLength,
                a.stopped, a.waiting, crossingTime, storage, conflict, a.Profile);
            if (a.IntersectionDecision != TrafficIntersectionDecision.Proceed)
            { gap = Mathf.Min(gap, Mathf.Max(0, remaining - a.HalfLength)); leaderSpeed = 0; }
            else if (remaining < a.HalfLength + 6) { a.reserved = next; reservationOwners[next] = slot; }
        }

        private TrafficAgentState AgentFor(TrafficVehicleSnapshot snapshot) => snapshot.Kind == TrafficActorKind.Civilian
            && snapshot.AgentSlot >= 0 && snapshot.AgentSlot < Capacity && agents[snapshot.AgentSlot].Active
            && agents[snapshot.AgentSlot].Id == snapshot.Id ? agents[snapshot.AgentSlot] : null;

        private static TrafficVehicleSnapshot SnapshotFor(TrafficAgentState a) => new TrafficVehicleSnapshot
        { Id = a.Id, Position = a.Position, Forward = a.Forward, Velocity = a.Velocity, HalfLength = a.HalfLength, HalfWidth = a.HalfWidth };

        private Vector3 SampleAhead(TrafficAgentState a, float distance, out Vector3 direction)
        {
            float s = a.Along + distance;
            for (int i = a.routeIndex; i < a.routeCount; i++)
            {
                RoadLane lane = roads[a.route[i]];
                if (s <= lane.Length || i == a.routeCount - 1) return lane.Sample(s, out direction);
                s -= lane.Length;
            }
            direction = a.Forward; return a.Position + direction * distance;
        }

        private void AdvanceLane(TrafficAgentState a, bool observedCrossing = false)
        {
            RoadLane lane = roads[a.Lane];
            if (a.routeIndex + 1 >= a.routeCount) { a.Arrived = a.Lane == a.Destination; a.Along = lane.Length; return; }
            int next = a.route[a.routeIndex + 1];
            if (next == lane.Left || next == lane.Right) { a.Along = lane.Length; return; }
            bool forbidden = roads.IsClosed(next) || (roads[next].Intersection >= 0 && a.reserved != next);
            if (forbidden && !observedCrossing) { a.Along = lane.Length; return; }
            if (forbidden) physicalIncursions++;
            float carry = Mathf.Max(0, a.Along - lane.Length);
            a.routeIndex++; a.Lane = next; a.Along = carry;
            if (roads[next].Intersection >= 0) a.occupiedMovement = next;
            a.waiting = a.stopped = 0; a.IntersectionDecision = TrafficIntersectionDecision.Proceed;
        }
    }
}
