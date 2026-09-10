using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum RoadClass { Local, Arterial, Highway, Industrial, Canyon }
    public enum RoadEntryControl { None, Signal, Stop, Yield, Uncontrolled, ProtectedSignal }

    [Serializable]
    public sealed class RoadLaneDefinition
    {
        public int id, fromNode, toNode, district;
        public RoadClass roadClass;
        [Min(0.01f)] public float width = 3.5f;
        [Min(1)] public float speedMetersPerSecond = 13.9f;
        public Vector3[] points = Array.Empty<Vector3>();
        public int[] successors = Array.Empty<int>();
        public int leftLane = -1, rightLane = -1, intersection = -1;
        public RoadEntryControl entryControl;
        public bool portal;
    }

    // Immutable lane geometry baked from the SAME RoadNetwork used by GPS/police.
    // Stable author IDs are resolved to compact indices once, never in hot queries.
    public sealed class RoadLane
    {
        public readonly int Id, FromNode, ToNode, District, Intersection;
        public readonly RoadClass Class;
        public readonly RoadEntryControl EntryControl;
        public readonly float Width, Speed, Length, CurvatureSpeed;
        public readonly bool Portal;
        internal readonly Vector3[] points;
        internal readonly float[] distances;
        internal int[] successors;
        internal int[] predecessors = Array.Empty<int>(), conflicts = Array.Empty<int>();
        public int Left { get; internal set; }
        public int Right { get; internal set; }
        public IReadOnlyList<int> Successors => successors;
        public IReadOnlyList<int> Predecessors => predecessors;
        public IReadOnlyList<int> ConflictingMovements => conflicts;
        public Vector3 Start => points[0];
        public Vector3 End => points[points.Length - 1];

        internal RoadLane(RoadLaneDefinition data)
        {
            if (data == null || data.id < 0 || data.points == null || data.points.Length < 2 || !TrafficDriver.Finite(data.width)
                || data.width <= 0 || !TrafficDriver.Finite(data.speedMetersPerSecond) || data.speedMetersPerSecond <= 0)
                throw new ArgumentException("A lane requires finite geometry, positive width and positive speed.");
            Id = data.id; FromNode = data.fromNode; ToNode = data.toNode; District = data.district;
            Intersection = data.intersection; EntryControl = data.entryControl; Class = data.roadClass;
            Width = data.width; Speed = data.speedMetersPerSecond; Portal = data.portal;
            points = (Vector3[])data.points.Clone(); distances = new float[points.Length];
            float maxCurvature = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if (!TrafficDriver.Finite(points[i].x) || !TrafficDriver.Finite(points[i].y) || !TrafficDriver.Finite(points[i].z))
                    throw new ArgumentException("Lane geometry must be finite.");
                if (i == 0) continue;
                float length = Vector3.Distance(points[i - 1], points[i]);
                if (length < 0.00001f) throw new ArgumentException("Lane samples must not coincide.");
                distances[i] = distances[i - 1] + length;
                if (!TrafficDriver.Finite(distances[i]) || distances[i] <= distances[i - 1])
                    throw new ArgumentException("Lane distance must advance at the supported coordinate precision.");
                if (i > 1) maxCurvature = Mathf.Max(maxCurvature, Vector3.Angle(points[i - 1] - points[i - 2], points[i] - points[i - 1])
                    * Mathf.Deg2Rad / Mathf.Max(0.1f, (length + distances[i - 1] - distances[i - 2]) * 0.5f));
            }
            Length = distances[distances.Length - 1];
            CurvatureSpeed = Mathf.Sqrt(2.5f / Mathf.Max(0.0001f, maxCurvature));
        }

        public Vector3 Sample(float distance, out Vector3 direction)
        {
            distance = Mathf.Clamp(distance, 0, Length);
            int low = 1, high = distances.Length - 1;
            while (low < high) { int mid = (low + high) / 2; if (distances[mid] < distance) low = mid + 1; else high = mid; }
            direction = (points[low] - points[low - 1]).normalized;
            return Vector3.Lerp(points[low - 1], points[low], (distance - distances[low - 1]) / (distances[low] - distances[low - 1]));
        }

        public float Project(Vector3 position, out float lateralDistance)
        {
            float best = float.PositiveInfinity, along = 0;
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 edge = points[i] - points[i - 1];
                float t = Mathf.Clamp01(Vector3.Dot(position - points[i - 1], edge) / edge.sqrMagnitude);
                float square = (position - points[i - 1] - edge * t).sqrMagnitude;
                if (square >= best) continue;
                best = square; along = Mathf.Lerp(distances[i - 1], distances[i], t);
            }
            lateralDistance = Mathf.Sqrt(best); return along;
        }
    }

    public sealed class RoadLaneNetwork
    {
        private readonly RoadLane[] lanes;
        private readonly float[] costs;
        private readonly int[] previous, heap, heapPosition;
        private readonly bool[] visited, closed;
        private readonly List<Closure>[] incidentClosures;
        private readonly Dictionary<long, int[]> spatialLanes;
        private readonly int[] spatialQueryMarks;
        private readonly int minimumSpatialX, maximumSpatialX, minimumSpatialZ, maximumSpatialZ;
        private int spatialQueryRevision;
        private const float SpatialCellSize = 128;
        private const int MaximumNearestLaneRings = 4;
        private float routeMinimumWidth;
        private int heapCount;
        public int Count => lanes.Length;
        public RoadLane this[int index] => lanes[index];
        public int Revision { get; private set; }

        public RoadLaneNetwork(RoadLaneDefinition[] definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            lanes = new RoadLane[definitions.Length];
            var ids = new Dictionary<int, int>(definitions.Length);
            var predecessors = new List<int>[definitions.Length];
            var intersections = new Dictionary<int, List<int>>();
            for (int i = 0; i < lanes.Length; i++)
            {
                lanes[i] = new RoadLane(definitions[i]);
                if (ids.ContainsKey(lanes[i].Id)) throw new ArgumentException("Duplicate stable lane ID.");
                ids.Add(lanes[i].Id, i);
                predecessors[i] = new List<int>();
                if (lanes[i].Intersection >= 0)
                {
                    if (!intersections.TryGetValue(lanes[i].Intersection, out List<int> group))
                    { group = new List<int>(); intersections.Add(lanes[i].Intersection, group); }
                    group.Add(i);
                }
            }
            for (int i = 0; i < lanes.Length; i++)
            {
                RoadLaneDefinition definition = definitions[i];
                int[] next = definition.successors ?? Array.Empty<int>();
                lanes[i].successors = new int[next.Length];
                for (int j = 0; j < next.Length; j++)
                {
                    if (!ids.TryGetValue(next[j], out int index)) throw new ArgumentException("Unknown successor lane.");
                    if (Vector3.Distance(lanes[i].End, lanes[index].Start) > 0.5f) throw new ArgumentException("Disconnected lane successor geometry.");
                    lanes[i].successors[j] = index;
                    predecessors[index].Add(i);
                }
                lanes[i].Left = ResolveNeighbor(definition.leftLane, ids);
                lanes[i].Right = ResolveNeighbor(definition.rightLane, ids);
            }
            for (int i = 0; i < lanes.Length; i++)
            {
                ValidateNeighbor(i, lanes[i].Left); ValidateNeighbor(i, lanes[i].Right);
                lanes[i].predecessors = predecessors[i].ToArray();
            }
            foreach (List<int> group in intersections.Values)
            for (int a = 0; a < group.Count; a++)
            {
                int lane = group[a]; var conflicts = new List<int>();
                for (int b = 0; b < group.Count; b++)
                {
                    int other = group[b];
                    if (GeometricallyConflict(lanes[lane], lanes[other]) || GeometricallyConflict(lanes[other], lanes[lane]))
                        conflicts.Add(other);
                }
                lanes[lane].conflicts = conflicts.ToArray();
            }
            costs = new float[lanes.Length]; previous = new int[lanes.Length]; heap = new int[lanes.Length]; heapPosition = new int[lanes.Length];
            visited = new bool[lanes.Length]; closed = new bool[lanes.Length];
            incidentClosures = new List<Closure>[lanes.Length];
            spatialQueryMarks = new int[lanes.Length];
            BuildSpatialIndex(out spatialLanes, out minimumSpatialX, out maximumSpatialX,
                out minimumSpatialZ, out maximumSpatialZ);
        }

        private void BuildSpatialIndex(out Dictionary<long, int[]> index, out int minimumX, out int maximumX,
            out int minimumZ, out int maximumZ)
        {
            var building = new Dictionary<long, List<int>>();
            minimumX = minimumZ = int.MaxValue; maximumX = maximumZ = int.MinValue;
            for (int laneIndex = 0; laneIndex < lanes.Length; laneIndex++)
            {
                Vector3[] points = lanes[laneIndex].points;
                for (int segment = 1; segment < points.Length; segment++)
                {
                    int firstX = Cell(Mathf.Min(points[segment - 1].x, points[segment].x));
                    int lastX = Cell(Mathf.Max(points[segment - 1].x, points[segment].x));
                    int firstZ = Cell(Mathf.Min(points[segment - 1].z, points[segment].z));
                    int lastZ = Cell(Mathf.Max(points[segment - 1].z, points[segment].z));
                    minimumX = Mathf.Min(minimumX, firstX); maximumX = Mathf.Max(maximumX, lastX);
                    minimumZ = Mathf.Min(minimumZ, firstZ); maximumZ = Mathf.Max(maximumZ, lastZ);
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
            index = new Dictionary<long, int[]>(building.Count);
            foreach (KeyValuePair<long, List<int>> pair in building) index.Add(pair.Key, pair.Value.ToArray());
            if (lanes.Length == 0) minimumX = maximumX = minimumZ = maximumZ = 0;
        }

        private static int Cell(float coordinate) => Mathf.FloorToInt(coordinate / SpatialCellSize);
        private static long SpatialKey(int x, int z) => ((long)x << 32) ^ (uint)z;

        private void ValidateNeighbor(int lane, int neighbor)
        {
            if (neighbor < 0) return;
            RoadLane a = lanes[lane], b = lanes[neighbor];
            if (neighbor == lane || a.Intersection >= 0 || b.Intersection >= 0
                || Vector3.Dot((a.End - a.Start).normalized, (b.End - b.Start).normalized) < 0.8f)
                throw new ArgumentException("Lane changes require a distinct, same-direction segment neighbor.");
            b.Project(a.Sample(a.Length * 0.5f, out _), out float separation);
            if (separation > (a.Width + b.Width) * 0.5f + 0.5f)
                throw new ArgumentException("Neighbor lanes must share a boundary.");
        }
        private static bool GeometricallyConflict(RoadLane a, RoadLane b)
        {
            if (a.Id == b.Id || (a.End - b.End).sqrMagnitude < 9) return true;
            // Sample the swept center corridors, including turn curves, at <= 1m spacing.
            // Conservative vehicle envelope; parallel non-overlapping through movements remain independent.
            for (float s = 0; s <= a.Length; s += 1)
            { b.Project(a.Sample(s, out _), out float distance); if (distance < 2.6f) return true; }
            return false;
        }
        public bool MovementsConflict(int first, int second)
        {
            if (first < 0 || second < 0 || first >= Count || second >= Count) return false;
            int[] conflicts = lanes[first].conflicts;
            for (int i = 0; i < conflicts.Length; i++) if (conflicts[i] == second) return true;
            return false;
        }

        private static int ResolveNeighbor(int id, Dictionary<int, int> ids)
        {
            if (id < 0) return -1;
            if (!ids.TryGetValue(id, out int index)) throw new ArgumentException("Unknown neighboring lane.");
            return index;
        }

        public bool IsClosed(int lane) => lane < 0 || lane >= Count || closed[lane] || incidentClosures[lane]?.Count > 0;
        public IDisposable AcquireClosure(int lane, float along)
        {
            if (lane < 0 || lane >= Count || !TrafficDriver.Finite(along)) throw new ArgumentOutOfRangeException(nameof(lane));
            var lease = new Closure(this, lane, Mathf.Clamp(along, 0, lanes[lane].Length));
            if (incidentClosures[lane] == null) incidentClosures[lane] = new List<Closure>();
            incidentClosures[lane].Add(lease); Revision++; return lease;
        }
        public float DistanceToBlockage(int lane, float along)
        {
            float distance = float.PositiveInfinity; var closures = incidentClosures[lane];
            if (closures != null) for (int i = 0; i < closures.Count; i++)
                if (closures[i].Along >= along) distance = Mathf.Min(distance, closures[i].Along - along);
            return distance;
        }
        private sealed class Closure : IDisposable
        {
            private RoadLaneNetwork owner;
            private readonly int lane;
            internal readonly float Along;
            internal Closure(RoadLaneNetwork network, int index, float along) { owner = network; lane = index; Along = along; }
            public void Dispose()
            {
                if (owner == null) return;
                owner.incidentClosures[lane].Remove(this); owner.Revision++; owner = null;
            }
        }
        public void SetClosed(int lane, bool value)
        {
            if (lane < 0 || lane >= Count) throw new ArgumentOutOfRangeException(nameof(lane));
            if (closed[lane] == value) return;
            closed[lane] = value; Revision++;
        }

        public int NearestLane(Vector3 point, Vector3 heading, out float along)
        {
            int found = -1; float best = float.PositiveInfinity; along = 0;
            heading.y = 0;
            if (heading.sqrMagnitude > 0.0001f) heading.Normalize();
            int queryX = Cell(point.x), queryZ = Cell(point.z);
            if (++spatialQueryRevision == int.MaxValue)
            {
                Array.Clear(spatialQueryMarks, 0, spatialQueryMarks.Length);
                spatialQueryRevision = 1;
            }
            int maximumRing = Mathf.Min(MaximumNearestLaneRings,
                Mathf.Max(Mathf.Max(Mathf.Abs(queryX - minimumSpatialX), Mathf.Abs(queryX - maximumSpatialX)),
                    Mathf.Max(Mathf.Abs(queryZ - minimumSpatialZ), Mathf.Abs(queryZ - maximumSpatialZ))));
            for (int ring = 0; ring <= maximumRing; ring++)
            {
                if (ring == 0) EvaluateSpatialCell(queryX, queryZ, point, heading, ref found, ref best, ref along);
                else
                {
                    int firstX = queryX - ring, lastX = queryX + ring;
                    int firstZ = queryZ - ring, lastZ = queryZ + ring;
                    for (int x = firstX; x <= lastX; x++)
                    {
                        EvaluateSpatialCell(x, firstZ, point, heading, ref found, ref best, ref along);
                        EvaluateSpatialCell(x, lastZ, point, heading, ref found, ref best, ref along);
                    }
                    for (int z = firstZ + 1; z < lastZ; z++)
                    {
                        EvaluateSpatialCell(firstX, z, point, heading, ref found, ref best, ref along);
                        EvaluateSpatialCell(lastX, z, point, heading, ref found, ref best, ref along);
                    }
                }
                float boundary = Mathf.Min(point.x - (queryX - ring) * SpatialCellSize,
                    (queryX + ring + 1) * SpatialCellSize - point.x,
                    point.z - (queryZ - ring) * SpatialCellSize,
                    (queryZ + ring + 1) * SpatialCellSize - point.z);
                if (found >= 0 && best <= boundary) break;
            }
            return found;
        }

        private void EvaluateSpatialCell(int x, int z, Vector3 point, Vector3 heading, ref int found,
            ref float best, ref float along)
        {
            if (!spatialLanes.TryGetValue(SpatialKey(x, z), out int[] candidates)) return;
            for (int candidate = 0; candidate < candidates.Length; candidate++)
            {
                int lane = candidates[candidate];
                if (spatialQueryMarks[lane] == spatialQueryRevision) continue;
                spatialQueryMarks[lane] = spatialQueryRevision;
                float projected = lanes[lane].Project(point, out float error);
                lanes[lane].Sample(projected, out Vector3 direction);
                if (heading.sqrMagnitude > 0 && Vector3.Dot(direction, heading) < 0.2f || error >= best) continue;
                best = error; found = lane; along = projected;
            }
        }

        // Main-thread, non-reentrant route query. Caller owns its reusable result buffer.
        public bool TryRoute(int start, int destination, int seed, int[] result, out int count, bool allowClosedStart = false, float minimumWidth = 2)
        {
            count = 0;
            if (start < 0 || start >= Count || destination < 0 || destination >= Count || result == null
                || !TrafficDriver.Finite(minimumWidth) || minimumWidth < 0 || lanes[start].Width < minimumWidth || lanes[destination].Width < minimumWidth
                || IsClosed(destination) || !allowClosedStart && IsClosed(start)) return false;
            routeMinimumWidth = minimumWidth;
            heapCount = 0;
            for (int i = 0; i < Count; i++)
            { costs[i] = float.PositiveInfinity; previous[i] = -1; visited[i] = false; heapPosition[i] = -1; }
            costs[start] = 0; PushOrDecrease(start);
            while (heapCount > 0)
            {
                int current = Pop();
                if (current == destination) break;
                visited[current] = true;
                for (int j = 0; j < lanes[current].successors.Length; j++) Relax(current, lanes[current].successors[j], seed, 0);
                // Adjacent-lane access is legal topology, not a teleport: the driver must execute the maneuver.
                if (lanes[current].Left >= 0) Relax(current, lanes[current].Left, seed, 4);
                if (lanes[current].Right >= 0) Relax(current, lanes[current].Right, seed, 4);
            }
            if (float.IsPositiveInfinity(costs[destination])) return false;
            for (int node = destination; node >= 0; node = previous[node])
            {
                if (count >= result.Length) { count = 0; return false; }
                result[count++] = node;
            }
            Array.Reverse(result, 0, count); return true;
        }

        private void Relax(int from, int to, int seed, float penalty)
        {
            if (IsClosed(to) || visited[to] || lanes[to].Width < routeMinimumWidth) return;
            uint hash = unchecked((uint)(seed * 397 ^ lanes[to].Id * 7919)); hash ^= hash >> 16;
            float cost = costs[from] + lanes[to].Length / lanes[to].Speed * (1 + (hash % 1000) * 0.00015f) + penalty;
            if (cost >= costs[to]) return;
            costs[to] = cost; previous[to] = from; PushOrDecrease(to);
        }

        private void PushOrDecrease(int lane)
        {
            int at = heapPosition[lane];
            if (at < 0) { at = heapCount++; heap[at] = lane; heapPosition[lane] = at; }
            while (at > 0)
            {
                int parent = (at - 1) / 2;
                if (!Before(heap[at], heap[parent])) break;
                SwapHeap(at, parent); at = parent;
            }
        }

        private int Pop()
        {
            int result = heap[0]; heapPosition[result] = -2; heapCount--;
            if (heapCount == 0) return result;
            heap[0] = heap[heapCount]; heapPosition[heap[0]] = 0;
            int at = 0;
            while (true)
            {
                int child = at * 2 + 1;
                if (child >= heapCount) break;
                if (child + 1 < heapCount && Before(heap[child + 1], heap[child])) child++;
                if (!Before(heap[child], heap[at])) break;
                SwapHeap(at, child); at = child;
            }
            return result;
        }

        private bool Before(int a, int b) => costs[a] < costs[b] || costs[a] == costs[b] && a < b;
        private void SwapHeap(int a, int b)
        {
            int value = heap[a]; heap[a] = heap[b]; heap[b] = value;
            heapPosition[heap[a]] = a; heapPosition[heap[b]] = b;
        }

        public static RoadLaneDefinition[] Bake(IReadOnlyList<RoadNode> nodes, int lanesPerDirection = 1)
        {
            lanesPerDirection = Mathf.Clamp(lanesPerDirection, 1, 3);
            var definitions = new List<RoadLaneDefinition>();
            for (int from = 0; from < nodes.Count; from++)
            {
                if (nodes[from] == null) continue;
                foreach (int to in nodes[from].exits ?? Array.Empty<int>())
                {
                    if (to < 0 || to >= nodes.Count || to == from || nodes[to] == null) continue;
                    Vector3 edge = nodes[to].position - nodes[from].position;
                    if (edge.magnitude < 12) continue;
                    Vector3 direction = edge.normalized, right = Vector3.Cross(Vector3.up, direction);
                    float inset = Mathf.Min(15, edge.magnitude * 0.2f);
                    int first = definitions.Count;
                    for (int lane = 0; lane < lanesPerDirection; lane++)
                        definitions.Add(new RoadLaneDefinition { id = definitions.Count, fromNode = from, toNode = to,
                            width = 3.5f, portal = nodes[from].exits.Length <= 2, leftLane = lane > 0 ? first + lane - 1 : -1,
                            rightLane = lane + 1 < lanesPerDirection ? first + lane + 1 : -1,
                            points = new[] { nodes[from].position + direction * inset + right * (3 + lane * 3.5f),
                                nodes[to].position - direction * inset + right * (3 + lane * 3.5f) } });
                }
            }
            int segments = definitions.Count;
            for (int i = 0; i < segments; i++)
            {
                var incoming = definitions[i]; var next = new List<int>();
                for (int j = 0; j < segments; j++)
                {
                    var outgoing = definitions[j];
                    if (incoming.toNode != outgoing.fromNode || incoming.fromNode == outgoing.toNode) continue;
                    // Keep turn connectivity stable; lane changes happen on segments, never by jumping connectors.
                    if (i % lanesPerDirection != j % lanesPerDirection) continue;
                    Vector3 start = incoming.points[1], end = outgoing.points[0];
                    Vector3 inDirection = (incoming.points[1] - incoming.points[0]).normalized;
                    Vector3 outDirection = (outgoing.points[1] - outgoing.points[0]).normalized;
                    float handle = Vector3.Distance(start, end) * 0.5f;
                    var points = new Vector3[13];
                    for (int s = 0; s < points.Length; s++)
                    {
                        float t = s / (float)(points.Length - 1), u = 1 - t;
                        points[s] = u * u * u * start + 3 * u * u * t * (start + inDirection * handle)
                            + 3 * u * t * t * (end - outDirection * handle) + t * t * t * end;
                    }
                    int id = definitions.Count; next.Add(id);
                    definitions.Add(new RoadLaneDefinition { id = id, fromNode = incoming.toNode, toNode = incoming.toNode,
                        intersection = incoming.toNode, points = points, successors = new[] { outgoing.id },
                        entryControl = nodes[incoming.toNode].exits.Length >= 3 ? RoadEntryControl.Signal : RoadEntryControl.Yield,
                        speedMetersPerSecond = 13.9f });
                }
                incoming.successors = next.ToArray();
            }
            return definitions.ToArray();
        }
    }
}
