using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum RoadNetworkSource { Legacy, Baked }
    public sealed class RoadNetwork : MonoBehaviour, IRoadNetwork
    {
        [SerializeField] private RoadNode[] nodes = Array.Empty<RoadNode>();
        [SerializeField, Range(1, 3)] private int lanesPerDirection = 1;
        [SerializeField] private RoadLaneDefinition[] bakedLanes = Array.Empty<RoadLaneDefinition>();
        [SerializeField] private RoadNetworkSource source;
        [SerializeField] private RoadNetworkAsset publication;
        private RoadGraph graph;
        private RoadLaneNetwork lanes;
        private RoadRuntimeNetwork runtime;
        private RoadRouteBuffer routeBuffer;
        private IReadOnlyList<RoadNode> mapNodes;
        public bool UsesBakedData => source == RoadNetworkSource.Baked;
        public RoadNetworkAsset Publication => publication;
        public RoadRuntimeNetwork Runtime { get { if (!UsesBakedData) return null; EnsureBaked(); return runtime; } }
        public RoadLaneNetwork Lanes
        {
            get
            {
                if (UsesBakedData) { EnsureBaked(); return lanes; }
                return lanes ?? (lanes = new RoadLaneNetwork(bakedLanes != null && bakedLanes.Length > 0 ? bakedLanes : RoadLaneNetwork.Bake(Nodes, lanesPerDirection)));
            }
        }
        private RoadGraph Graph => graph ?? (graph = new RoadGraph(nodes));
        public IReadOnlyList<RoadNode> Nodes { get { if (!UsesBakedData) return Graph.Nodes; EnsureBaked(); return mapNodes; } }
        public bool TryRoute(Vector3 from, Vector3 to, List<Vector3> route)
        {
            if (!UsesBakedData) return Graph.TryRoute(from, to, route);
            if (route == null) throw new ArgumentNullException(nameof(route)); route.Clear(); EnsureBaked();
            if (!runtime.TryNearest(from, Vector3.zero, 25, out var start, true) || !runtime.TryNearest(to, Vector3.zero, 25, out var end, true)
                || runtime.Route(start, end, routeBuffer, minimumWidth: 2) != RoadRouteResult.Success) return false;
            for (int i = 0; i < routeBuffer.Count; i++)
            {
                var span = routeBuffer[i]; runtime.TryIndex(span.LaneId, out int index); var lane = runtime[index];
                AddRoutePoint(route, lane.Sample(span.Start).position);
                for (int sampleIndex = 0; sampleIndex < lane.Samples.Count; sampleIndex++)
                {
                    var sample = lane.Samples[sampleIndex];
                    if (sample.distance > span.Start && sample.distance < span.End) AddRoutePoint(route, sample.position);
                }
                AddRoutePoint(route, lane.Sample(span.End).position);
            }
            return true;
        }
        public Vector3 NearestRoadPoint(Vector3 position)
        {
            if (!UsesBakedData) return Graph.NearestRoadPoint(position);
            EnsureBaked(); return runtime.TryNearest(position, Vector3.zero, float.MaxValue, out var anchor) ? runtime.Sample(anchor).position : position;
        }
        public bool TryLaneIndex(RoadId id, out int index) { index = -1; return UsesBakedData && Runtime.TryIndex(id, out index); }
        public void Configure(RoadNode[] data)
        {
            source = RoadNetworkSource.Legacy; publication = null; runtime = null; routeBuffer = null; mapNodes = null;
            nodes = data; graph = new RoadGraph(nodes); bakedLanes = Array.Empty<RoadLaneDefinition>(); lanes = null;
        }
        public void ConfigureLanes(RoadLaneDefinition[] data)
        {
            if (UsesBakedData) throw new InvalidOperationException("Published lanes are derived from the source bake and cannot be overridden independently.");
            var validated = new RoadLaneNetwork(data); bakedLanes = data; lanes = validated;
        }
        public void BakeLanes(int count = 1)
        { lanesPerDirection = Mathf.Clamp(count, 1, 3); ConfigureLanes(RoadLaneNetwork.Bake(Nodes, lanesPerDirection)); }
        public void ConfigureBaked(RoadNetworkAsset asset)
        {
            var candidate = new RoadRuntimeNetwork(asset);
            CreateAdapter(candidate, out var traffic, out var map);
            publication = asset; source = RoadNetworkSource.Baked; lanes = traffic; mapNodes = map;
            runtime = new RoadRuntimeNetwork(asset, index => traffic.IsClosed(index));
            routeBuffer = new RoadRouteBuffer(Mathf.Max(1, runtime.Count)); graph = null;
        }
        public static void ValidatePublication(RoadNetworkAsset asset) => CreateAdapter(new RoadRuntimeNetwork(asset), out _, out _);
        private void EnsureBaked()
        { if (runtime == null) ConfigureBaked(publication); }
        private static void AddRoutePoint(List<Vector3> route, Vector3 point)
        { if (route.Count == 0 || (route[route.Count - 1] - point).sqrMagnitude > 0.000001f) route.Add(point); }
        private static void CreateAdapter(RoadRuntimeNetwork runtime, out RoadLaneNetwork traffic, out IReadOnlyList<RoadNode> map)
        {
            var definitions = new RoadLaneDefinition[runtime.Count]; var nodes = new List<RoadNode>();
            for (int i = 0; i < runtime.Count; i++)
            {
                var lane = runtime[i]; int start = nodes.Count; var points = new Vector3[lane.Samples.Count]; float width = float.PositiveInfinity;
                for (int j = 0; j < points.Length; j++)
                {
                    points[j] = lane.Samples[j].position; width = Mathf.Min(width, lane.Samples[j].width);
                    nodes.Add(new RoadNode { position = points[j], exits = j + 1 < points.Length ? new[] { nodes.Count + 1 } : Array.Empty<int>() });
                }
                var next = new int[lane.Successors.Count];
                for (int j = 0; j < next.Length; j++) { runtime.TryIndex(lane.Successors[j], out int index); next[j] = runtime[index].CompatibilityId; }
                definitions[i] = new RoadLaneDefinition { id = lane.CompatibilityId, fromNode = start, toNode = nodes.Count - 1,
                    roadClass = lane.Class, width = width, speedMetersPerSecond = lane.Speed, points = points, successors = next };
            }
            // A conservative traffic adapter keeps the narrowest sampled width. Authoring and generic queries retain all widths.
            traffic = new RoadLaneNetwork(definitions); map = nodes.AsReadOnly();
        }
        private void OnValidate() { if (Application.isPlaying) return; graph = null; lanes = null; runtime = null; routeBuffer = null; mapNodes = null; }
    }
}
