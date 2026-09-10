using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Serializable]
    public sealed class TrafficInitialTrip
    {
        public int originLaneId, destinationLaneId, seed = 2015;
        [Min(0)] public float along;
    }
    public enum TrafficActorKind { Civilian, Player, Racer, Police }
    public enum TrafficSimulationLod { Physical, Nearby, Logical, Abstract }
    public enum TrafficLaneChangeReason { None, SpeedGain, Mandatory, Merge, Courtesy, Obstruction, UnsafeFront, UnsafeRear, NoLane, Committed }
    public enum TrafficIntersectionDecision { Proceed, Signal, YellowStop, StopSign, CrossingGap, ExitBlocked, Conflict }

    public struct TrafficVehicleSnapshot
    {
        public int Id, Lane, AgentSlot;
        public TrafficActorKind Kind;
        public Vector3 Position, Velocity, Forward;
        public float HalfLength, HalfWidth, Along;
        public bool Siren;
    }

    // One bounded registry for every road user. Queries return indices into a frame snapshot.
    // Writers call BeginFrame/Add before readers; no allocations or scene searches during queries.
    public sealed class TrafficSpatialRegistry
    {
        private const float CellSize = 32;
        private readonly TrafficVehicleSnapshot[] entries;
        private readonly int[] heads, next, cellX, cellZ;
        public int Count { get; private set; }
        public bool Saturated { get; private set; }
        public TrafficVehicleSnapshot this[int index] => entries[index];
        public TrafficSpatialRegistry(int capacity)
        {
            if (capacity < 1 || capacity > 16384) throw new ArgumentOutOfRangeException(nameof(capacity));
            entries = new TrafficVehicleSnapshot[capacity]; next = new int[capacity]; cellX = new int[capacity]; cellZ = new int[capacity];
            heads = new int[Mathf.NextPowerOfTwo(capacity * 4)]; BeginFrame();
        }
        public void BeginFrame() { Count = 0; Saturated = false; for (int i = 0; i < heads.Length; i++) heads[i] = -1; }
        public bool Add(TrafficVehicleSnapshot state)
        {
            if (Count >= entries.Length) { Saturated = true; return false; }
            if (!TrafficDriver.Finite(state.Position) || !TrafficDriver.Finite(state.Velocity) || !TrafficDriver.Finite(state.Forward)
                || !TrafficDriver.Finite(state.HalfLength) || !TrafficDriver.Finite(state.HalfWidth)
                || state.HalfLength < 0 || state.HalfWidth < 0) { Saturated = true; return false; }
            int index = Count++, x = Mathf.FloorToInt(state.Position.x / CellSize), z = Mathf.FloorToInt(state.Position.z / CellSize);
            int bucket = Hash(x, z); entries[index] = state; cellX[index] = x; cellZ[index] = z;
            next[index] = heads[bucket]; heads[bucket] = index; return true;
        }
        public int Query(Vector3 point, float radius, int[] results, out bool truncated)
        {
            truncated = Saturated;
            if (results == null || !TrafficDriver.Finite(point)
                || !TrafficDriver.Finite(radius) || radius < 0 || radius > 512) { truncated = true; return 0; }
            int count = 0, minX = Mathf.FloorToInt((point.x - radius) / CellSize), maxX = Mathf.FloorToInt((point.x + radius) / CellSize);
            int minZ = Mathf.FloorToInt((point.z - radius) / CellSize), maxZ = Mathf.FloorToInt((point.z + radius) / CellSize);
            for (int x = minX; x <= maxX; x++) for (int z = minZ; z <= maxZ; z++)
                for (int index = heads[Hash(x, z)]; index >= 0; index = next[index])
                {
                    if (cellX[index] != x || cellZ[index] != z || (entries[index].Position - point).sqrMagnitude > radius * radius) continue;
                    if (count == results.Length) { truncated = true; return count; }
                    results[count++] = index;
                }
            return count;
        }
        private int Hash(int x, int z) => unchecked((x * 73856093 ^ z * 19349663) & (heads.Length - 1));
    }

    public struct TrafficLaneChangeObservation
    {
        public bool Available, Mandatory;
        public float Speed, DesiredSpeed, CurrentAcceleration, FrontGap, FrontSpeed, RearGap, RearSpeed, RearDesiredSpeed, RearBeforeAcceleration;
    }
    public struct TrafficLaneChangeDecision
    {
        public bool Accepted;
        public TrafficLaneChangeReason Reason;
        public float Incentive, RearAcceleration, RearTimeToCollision;
    }
    public static class TrafficLaneChangePlanner
    {
        // Reduced MOBIL incentive: omits old-lane follower benefit, conservatively treating it as zero.
        // Mandatory route need never overrides front/rear safety or the physical maneuver horizon.
        public static TrafficLaneChangeDecision Evaluate(TrafficLaneChangeObservation observation, TrafficDriverProfile driver, TrafficDriverProfile follower)
        {
            if (driver == null || follower == null) throw new ArgumentNullException(nameof(driver));
            var result = new TrafficLaneChangeDecision { Reason = TrafficLaneChangeReason.NoLane, RearTimeToCollision = float.PositiveInfinity };
            if (!observation.Available) return result;
            float frontSafe = TrafficDriver.SafeFollowingSpeed(observation.FrontGap, observation.FrontSpeed, driver.emergencyBraking,
                12, 0.5f, driver.standstillGap);
            if (!TrafficDriver.Finite(observation.Speed) || !TrafficDriver.Finite(observation.FrontSpeed)
                || float.IsNaN(observation.FrontGap) || observation.FrontGap < driver.standstillGap || frontSafe + 0.5f < observation.Speed)
            { result.Reason = TrafficLaneChangeReason.UnsafeFront; return result; }
            float closing = observation.RearSpeed - observation.Speed;
            result.RearTimeToCollision = closing > 0 ? observation.RearGap / closing : float.PositiveInfinity;
            result.RearAcceleration = TrafficDriver.CarFollowingAcceleration(observation.RearSpeed, observation.RearDesiredSpeed,
                observation.RearGap, observation.Speed, follower);
            if (!TrafficDriver.Finite(observation.RearSpeed) || float.IsNaN(observation.RearGap)
                || observation.RearGap < follower.standstillGap || result.RearTimeToCollision < 3.5f
                || result.RearAcceleration < -Mathf.Max(1, follower.comfortableBraking))
            { result.Reason = TrafficLaneChangeReason.UnsafeRear; return result; }
            float ownAfter = TrafficDriver.CarFollowingAcceleration(observation.Speed, observation.DesiredSpeed,
                observation.FrontGap, observation.FrontSpeed, driver);
            result.Incentive = ownAfter - observation.CurrentAcceleration + Mathf.Clamp01(driver.politeness)
                * (result.RearAcceleration - observation.RearBeforeAcceleration);
            result.Accepted = observation.Mandatory || result.Incentive > 0.3f;
            result.Reason = result.Accepted ? observation.Mandatory ? TrafficLaneChangeReason.Mandatory : TrafficLaneChangeReason.SpeedGain
                : TrafficLaneChangeReason.None;
            return result;
        }
    }

    public static class TrafficIntersectionRules
    {
        public static float CriticalGap(TrafficDriverProfile driver, float waitingSeconds) =>
            Mathf.Max(2.5f, driver.intersectionGap * Mathf.Lerp(1, 0.75f, Mathf.Clamp01(waitingSeconds / Mathf.Max(1, driver.patienceSeconds))));

        public static bool StopForYellow(float speed, float stopDistance, TrafficDriverProfile driver) =>
            speed * driver.reactionSeconds + speed * speed / (2 * Mathf.Max(1, driver.comfortableBraking)) <= Mathf.Max(0, stopDistance);

        public static TrafficIntersectionDecision Evaluate(RoadEntryControl control, RoadSignalAspect signal, float speed,
            float stopDistance, float stoppedSeconds, float waitingSeconds, float crossingArrivalSeconds, bool exitHasStorage,
            bool conflictingReservation, TrafficDriverProfile driver)
        {
            if (!exitHasStorage) return TrafficIntersectionDecision.ExitBlocked;
            if (conflictingReservation) return TrafficIntersectionDecision.Conflict;
            if ((control == RoadEntryControl.Signal || control == RoadEntryControl.ProtectedSignal) && signal == RoadSignalAspect.Red)
                return TrafficIntersectionDecision.Signal;
            if ((control == RoadEntryControl.Signal || control == RoadEntryControl.ProtectedSignal) && signal == RoadSignalAspect.Yellow
                && StopForYellow(speed, stopDistance, driver)) return TrafficIntersectionDecision.YellowStop;
            if (control == RoadEntryControl.Stop && stoppedSeconds < 1 + driver.reactionSeconds) return TrafficIntersectionDecision.StopSign;
            if (control != RoadEntryControl.None && control != RoadEntryControl.ProtectedSignal
                && crossingArrivalSeconds < CriticalGap(driver, waitingSeconds)) return TrafficIntersectionDecision.CrossingGap;
            return TrafficIntersectionDecision.Proceed;
        }
    }

    public static class TrafficSpawnSafety
    {
        public static float RequiredClearance(float observerSpeed, float trafficSpeed, float reactionSeconds = 5) =>
            45 + Mathf.Max(0, observerSpeed + trafficSpeed) * Mathf.Max(2, reactionSeconds);

        public static bool IsOutsideInteraction(Vector3 position, float trafficSpeed, TrafficVehicleSnapshot observer)
        {
            float required = RequiredClearance(observer.Velocity.magnitude, trafficSpeed);
            return (position - observer.Position).sqrMagnitude > required * required;
        }
    }

    public struct TrafficRandom
    {
        private uint state;
        public TrafficRandom(int seed)
        {
            // Avalanche adjacent authored seeds before xorshift; nearby integer seeds must not
            // make every driver the same temperament or align every portal arrival.
            uint value = unchecked((uint)seed + 0x9e3779b9u);
            value = unchecked((value ^ value >> 16) * 0x85ebca6bu);
            value = unchecked((value ^ value >> 13) * 0xc2b2ae35u);
            state = value ^ value >> 16; if (state == 0) state = 0x9e3779b9u;
        }
        public uint Next() { if (state == 0) state = 0x9e3779b9u; state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state; }
        public float Unit() => (Next() >> 8) * (1f / 16777216f);
        public int Index(int count) => count > 0 ? (int)(Next() % (uint)count) : -1;
        public float ArrivalSeconds(float vehiclesPerHour) => vehiclesPerHour > 0
            ? -Mathf.Log(Mathf.Max(0.000001f, 1 - Unit())) * 3600 / vehiclesPerHour : float.PositiveInfinity;
    }
}
