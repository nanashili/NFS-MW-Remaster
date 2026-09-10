using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum RoadRouteResult { Success, Disconnected, InvalidAnchor, Closed, CapacityExceeded, Incompatible }
    public readonly struct RoadLaneAnchor
    {
        public readonly RoadId LaneId;
        public readonly float Distance;
        public RoadLaneAnchor(RoadId laneId, float distance) { LaneId = laneId; Distance = distance; }
    }
    public readonly struct RoadRouteSpan
    {
        public readonly RoadId LaneId;
        public readonly float Start, End;
        public RoadRouteSpan(RoadId laneId, float start, float end) { LaneId = laneId; Start = start; End = end; }
    }

    // Caller-owned scratch and output; independent buffers allow reentrant queries on immutable geometry.
    public sealed class RoadRouteBuffer
    {
        internal readonly float[] costs;
        internal readonly int[] previous, heap, heapPosition, path;
        private readonly RoadRouteSpan[] spans;
        internal int heapCount;
        public int Capacity => costs.Length;
        public int Count { get; private set; }
        public float Distance { get; private set; }
        public RoadRouteSpan this[int index] => index >= 0 && index < Count ? spans[index] : throw new ArgumentOutOfRangeException(nameof(index));
        public RoadRouteBuffer(int laneCapacity)
        {
            if (laneCapacity < 1) throw new ArgumentOutOfRangeException(nameof(laneCapacity));
            costs = new float[laneCapacity]; previous = new int[laneCapacity]; heap = new int[laneCapacity]; heapPosition = new int[laneCapacity];
            path = new int[laneCapacity]; spans = new RoadRouteSpan[laneCapacity + 1];
        }
        internal void Clear() { Count = 0; Distance = 0; heapCount = 0; }
        internal void Reset(int count)
        { Clear(); for (int i = 0; i < count; i++) { costs[i] = float.PositiveInfinity; previous[i] = -1; heapPosition[i] = -1; } }
        internal void Add(RoadId lane, float start, float end) { spans[Count++] = new RoadRouteSpan(lane, start, end); Distance += end - start; }
        internal void PushOrDecrease(int node)
        {
            int at = heapPosition[node];
            if (at < 0) { at = heapCount++; heap[at] = node; heapPosition[node] = at; }
            while (at > 0)
            {
                int parent = (at - 1) / 2; if (!Before(heap[at], heap[parent])) break;
                Swap(at, parent); at = parent;
            }
        }
        internal int Pop()
        {
            int result = heap[0]; heapPosition[result] = -2; heapCount--;
            if (heapCount == 0) return result;
            heap[0] = heap[heapCount]; heapPosition[heap[0]] = 0; int at = 0;
            while (true)
            {
                int child = at * 2 + 1; if (child >= heapCount) break;
                if (child + 1 < heapCount && Before(heap[child + 1], heap[child])) child++;
                if (!Before(heap[child], heap[at])) break;
                Swap(at, child); at = child;
            }
            return result;
        }
        private bool Before(int a, int b) => costs[a] < costs[b] || costs[a] == costs[b] && a < b;
        private void Swap(int a, int b)
        { int value = heap[a]; heap[a] = heap[b]; heap[b] = value; heapPosition[heap[a]] = a; heapPosition[heap[b]] = b; }
    }

    public sealed class RoadRuntimeNetwork
    {
        private readonly RoadNetworkAsset asset;
        private readonly Dictionary<RoadId, int> indices;
        private readonly int[][] successors;
        private readonly float[] minimumWidths;
        private readonly Func<int, bool> isClosed;
        private readonly Dictionary<long, int[]> spatialLanes;
        private readonly int[] spatialQueryMarks;
        private int spatialQueryRevision;
        private const float SpatialCellSize = 128;
        public int Count => asset.Lanes.Count;
        public RoadNetworkAsset Asset => asset;
        public RoadBakedLane this[int index] => asset.Lanes[index];
        public RoadRuntimeNetwork(RoadNetworkAsset asset, Func<int, bool> isClosed = null)
        {
            if (asset == null || asset.SchemaVersion != RoadNetworkAsset.CurrentSchema) throw new ArgumentException("Unsupported road network publication.");
            this.asset = asset; this.isClosed = isClosed; indices = new Dictionary<RoadId, int>(Count); successors = new int[Count][]; minimumWidths = new float[Count];
            for (int i = 0; i < Count; i++)
            {
                var lane = this[i]; ValidateLane(lane);
                if (indices.ContainsKey(lane.Id)) throw new ArgumentException("Duplicate lane identity in publication.");
                indices.Add(lane.Id, i);
                minimumWidths[i] = float.PositiveInfinity;
                foreach (var sample in lane.Samples) minimumWidths[i] = Mathf.Min(minimumWidths[i], sample.width);
            }
            for (int i = 0; i < Count; i++)
            {
                var links = this[i].Successors; successors[i] = new int[links.Count]; var unique = new HashSet<RoadId>();
                for (int j = 0; j < links.Count; j++)
                    if (!unique.Add(links[j]) || !indices.TryGetValue(links[j], out successors[i][j])) throw new ArgumentException("Invalid lane successor identity.");
            }
            spatialQueryMarks = new int[Count]; spatialLanes = BuildSpatialIndex();
        }
        public bool TryIndex(RoadId id, out int index) => indices.TryGetValue(id, out index);
        public RoadLaneSample Sample(RoadLaneAnchor anchor)
        { if (!ValidAnchor(anchor, out int index)) throw new ArgumentOutOfRangeException(nameof(anchor)); return this[index].Sample(anchor.Distance); }

        public bool TryNearest(Vector3 position, Vector3 heading, float radius, out RoadLaneAnchor anchor, bool includeClosed = false)
        {
            anchor = default;
            if (!Finite(position) || !Finite(heading) || !Finite(radius) || radius < 0) return false;
            float best = radius; bool found = false; heading.Normalize();
            if (radius == float.MaxValue)
            {
                for (int i = 0; i < Count; i++)
                    EvaluateNearest(i, position, heading, includeClosed, ref found, ref best, ref anchor);
                return found;
            }
            if (++spatialQueryRevision == int.MaxValue)
            { Array.Clear(spatialQueryMarks, 0, spatialQueryMarks.Length); spatialQueryRevision = 1; }
            int firstX = Cell(position.x - radius), lastX = Cell(position.x + radius);
            int firstZ = Cell(position.z - radius), lastZ = Cell(position.z + radius);
            for (int x = firstX; x <= lastX; x++)
            for (int z = firstZ; z <= lastZ; z++)
            {
                if (!spatialLanes.TryGetValue(SpatialKey(x, z), out int[] candidates)) continue;
                for (int candidate = 0; candidate < candidates.Length; candidate++)
                {
                    int lane = candidates[candidate];
                    if (spatialQueryMarks[lane] == spatialQueryRevision) continue;
                    spatialQueryMarks[lane] = spatialQueryRevision;
                    EvaluateNearest(lane, position, heading, includeClosed, ref found, ref best, ref anchor);
                }
            }
            return found;
        }

        private void EvaluateNearest(int lane, Vector3 position, Vector3 heading, bool includeClosed,
            ref bool found, ref float best, ref RoadLaneAnchor anchor)
        {
            if (!includeClosed && Closed(lane)) return;
            float distance = this[lane].Project(position, out float error);
            if (error > best || found && error == best
                || heading.sqrMagnitude > 0 && Vector3.Dot(heading, this[lane].Sample(distance).forward) < 0.2f) return;
            found = true; best = error; anchor = new RoadLaneAnchor(this[lane].Id, distance);
        }

        private Dictionary<long, int[]> BuildSpatialIndex()
        {
            var building = new Dictionary<long, List<int>>();
            for (int laneIndex = 0; laneIndex < Count; laneIndex++)
            {
                IReadOnlyList<RoadLaneSample> samples = this[laneIndex].Samples;
                for (int segment = 1; segment < samples.Count; segment++)
                {
                    Vector3 first = samples[segment - 1].position, last = samples[segment].position;
                    int firstX = Cell(Mathf.Min(first.x, last.x)), lastX = Cell(Mathf.Max(first.x, last.x));
                    int firstZ = Cell(Mathf.Min(first.z, last.z)), lastZ = Cell(Mathf.Max(first.z, last.z));
                    for (int x = firstX; x <= lastX; x++)
                    for (int z = firstZ; z <= lastZ; z++)
                    {
                        long key = SpatialKey(x, z);
                        if (!building.TryGetValue(key, out List<int> cellLanes))
                        { cellLanes = new List<int>(); building.Add(key, cellLanes); }
                        if (cellLanes.Count == 0 || cellLanes[cellLanes.Count - 1] != laneIndex)
                            cellLanes.Add(laneIndex);
                    }
                }
            }
            var index = new Dictionary<long, int[]>(building.Count);
            foreach (KeyValuePair<long, List<int>> pair in building) index.Add(pair.Key, pair.Value.ToArray());
            return index;
        }

        private static int Cell(float coordinate) => Mathf.FloorToInt(coordinate / SpatialCellSize);
        private static long SpatialKey(int x, int z) => ((long)x << 32) ^ (uint)z;

        public RoadRouteResult Route(RoadLaneAnchor from, RoadLaneAnchor to, RoadRouteBuffer buffer, float minimumWidth = 0)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer)); buffer.Clear();
            if (!ValidAnchor(from, out int start) || !ValidAnchor(to, out int end)) return RoadRouteResult.InvalidAnchor;
            if (!Finite(minimumWidth) || minimumWidth < 0 || minimumWidths[start] < minimumWidth || minimumWidths[end] < minimumWidth) return RoadRouteResult.Incompatible;
            if (buffer.Capacity < Count) return RoadRouteResult.CapacityExceeded;
            if (Closed(start) || Closed(end)) return RoadRouteResult.Closed;
            if (start == end && to.Distance >= from.Distance) { buffer.Add(from.LaneId, from.Distance, to.Distance); return RoadRouteResult.Success; }
            buffer.Reset(Count); buffer.costs[start] = (this[start].Length - from.Distance) / this[start].Speed; buffer.PushOrDecrease(start);
            float best = float.PositiveInfinity; int goalParent = -1;
            while (buffer.heapCount > 0)
            {
                int current = buffer.Pop(); float cost = buffer.costs[current]; if (cost >= best) break;
                foreach (int next in successors[current])
                {
                    if (Closed(next) || minimumWidths[next] < minimumWidth) continue;
                    // Evaluate an arrival at the goal before the visited check: a backwards same-lane trip needs a real cycle.
                    if (next == end)
                    {
                        float arrival = cost + to.Distance / this[next].Speed;
                        if (arrival < best) { best = arrival; goalParent = current; }
                    }
                    if (buffer.heapPosition[next] == -2) continue;
                    float candidate = cost + this[next].Length / this[next].Speed;
                    if (candidate >= buffer.costs[next]) continue;
                    buffer.costs[next] = candidate; buffer.previous[next] = current; buffer.PushOrDecrease(next);
                }
            }
            if (goalParent < 0) return RoadRouteResult.Disconnected;
            int count = 0;
            for (int node = goalParent; node >= 0; node = buffer.previous[node]) buffer.path[count++] = node;
            for (int i = count - 1; i >= 0; i--)
            { var lane = this[buffer.path[i]]; buffer.Add(lane.Id, i == count - 1 ? from.Distance : 0, lane.Length); }
            buffer.Add(to.LaneId, 0, to.Distance); return RoadRouteResult.Success;
        }

        private bool Closed(int index) => isClosed != null && isClosed(index);
        private bool ValidAnchor(RoadLaneAnchor anchor, out int index)
        { index = -1; return Finite(anchor.Distance) && indices.TryGetValue(anchor.LaneId, out index) && anchor.Distance >= 0 && anchor.Distance <= this[index].Length; }
        private static void ValidateLane(RoadBakedLane lane)
        {
            if (lane == null || !lane.Id.IsValid || !lane.RoadId.IsValid || !Finite(lane.Speed) || lane.Speed <= 0 || lane.Samples.Count < 2 || lane.Samples[0].distance != 0)
                throw new ArgumentException("Invalid baked lane identity, speed or samples.");
            for (int i = 0; i < lane.Samples.Count; i++)
            {
                var sample = lane.Samples[i];
                if (!Finite(sample.position) || !Finite(sample.forward) || !Finite(sample.left) || !Finite(sample.up) || !Finite(sample.width)
                    || sample.width <= 0 || !Finite(sample.station) || !Finite(sample.distance) || sample.forward.sqrMagnitude < 0.5f
                    || sample.up.sqrMagnitude < 0.5f || Vector3.Cross(sample.forward.normalized, sample.up.normalized).sqrMagnitude < 0.5f)
                    throw new ArgumentException("Invalid baked lane sample.");
                if (i > 0 && (sample.distance <= lane.Samples[i - 1].distance || (sample.position - lane.Samples[i - 1].position).sqrMagnitude < 0.0000000001f))
                    throw new ArgumentException("Lane distances must increase over distinct samples.");
            }
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
