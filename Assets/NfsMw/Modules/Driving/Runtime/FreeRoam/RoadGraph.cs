using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Serializable]
    public sealed class RoadNode
    {
        public Vector3 position;
        public int[] exits = Array.Empty<int>();
    }

    public interface IRoadNetwork
    {
        IReadOnlyList<RoadNode> Nodes { get; }
        bool TryRoute(Vector3 from, Vector3 to, List<Vector3> route);
        Vector3 NearestRoadPoint(Vector3 position);
    }

    public sealed class RoadGraph : IRoadNetwork
    {
        private readonly RoadNode[] nodes;
        public IReadOnlyList<RoadNode> Nodes => nodes;
        public RoadGraph(RoadNode[] nodes) { this.nodes = nodes ?? Array.Empty<RoadNode>(); }
        public bool TryRoute(Vector3 from, Vector3 to, List<Vector3> route)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));
            route.Clear();
            if (!TryProject(from, out int startA, out int startB, out Vector3 start)
                || !TryProject(to, out int endA, out int endB, out Vector3 end)) return false;
            // Roads are bidirectional. Endpoints may lie partway along an edge.
            if ((startA == endA && startB == endB) || (startA == endB && startB == endA))
            {
                route.Add(start);
                route.Add(end);
                return true;
            }
            var distances = new float[nodes.Length];
            var previous = new int[nodes.Length];
            var visited = new bool[nodes.Length];
            for (int i = 0; i < nodes.Length; i++) { distances[i] = float.PositiveInfinity; previous[i] = -1; }
            distances[startA] = Vector3.Distance(start, nodes[startA].position);
            distances[startB] = Vector3.Distance(start, nodes[startB].position);
            for (int pass = 0; pass < nodes.Length; pass++)
            {
                int current = -1;
                for (int i = 0; i < nodes.Length; i++)
                    if (!visited[i] && !float.IsPositiveInfinity(distances[i])
                        && (current < 0 || distances[i] < distances[current])) current = i;
                if (current < 0) break;
                visited[current] = true;
                foreach (int next in nodes[current].exits ?? Array.Empty<int>())
                {
                    if (!Valid(next)) continue;
                    float distance = distances[current] + Vector3.Distance(nodes[current].position, nodes[next].position);
                    if (distance >= distances[next]) continue;
                    distances[next] = distance;
                    previous[next] = current;
                }
            }
            int goal = distances[endA] + Vector3.Distance(nodes[endA].position, end)
                <= distances[endB] + Vector3.Distance(nodes[endB].position, end) ? endA : endB;
            if (float.IsPositiveInfinity(distances[goal])) return false;
            for (int current = goal; current >= 0; current = previous[current]) route.Add(nodes[current].position);
            route.Reverse();
            if (Vector3.Distance(route[0], start) > 0.1f) route.Insert(0, start);
            if (Vector3.Distance(route[route.Count - 1], end) > 0.1f) route.Add(end);
            return true;
        }
        public Vector3 NearestRoadPoint(Vector3 position) => TryProject(position, out _, out _, out Vector3 point) ? point : position;

        private bool Valid(int index) => index >= 0 && index < nodes.Length && nodes[index] != null;

        private bool TryProject(Vector3 position, out int a, out int b, out Vector3 point)
        {
            a = b = -1;
            point = position;
            float best = float.PositiveInfinity;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (!Valid(i)) continue;
                foreach (int j in nodes[i].exits ?? Array.Empty<int>())
                {
                    if (!Valid(j) || j == i) continue;
                    Vector3 direction = nodes[j].position - nodes[i].position;
                    float t = direction.sqrMagnitude > 0 ? Mathf.Clamp01(Vector3.Dot(position - nodes[i].position, direction) / direction.sqrMagnitude) : 0;
                    Vector3 candidate = nodes[i].position + t * direction;
                    float distance = (candidate - position).sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance;
                    a = i; b = j; point = candidate;
                }
            }
            return a >= 0;
        }
    }
}
