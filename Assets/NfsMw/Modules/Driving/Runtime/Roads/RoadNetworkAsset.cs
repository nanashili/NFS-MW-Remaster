using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Serializable]
    public struct RoadLaneSample
    {
        public float station, distance, width;
        public Vector3 position, forward, left, up;
    }

    [Serializable]
    public sealed class RoadBakedLane
    {
        [SerializeField] private RoadId id, roadId;
        [SerializeField] private int compatibilityId = -1;
        [SerializeField] private RoadClass roadClass;
        [SerializeField] private float speed;
        [SerializeField] private RoadLaneSample[] samples;
        [SerializeField] private RoadId[] successors;
        [SerializeField] private SensorySurfaceProfile surface;
        [NonSerialized] private IReadOnlyList<RoadLaneSample> sampleView;
        [NonSerialized] private IReadOnlyList<RoadId> successorView;
        public RoadId Id => id;
        public RoadId RoadId => roadId;
        public int CompatibilityId => compatibilityId;
        public RoadClass Class => roadClass;
        public float Speed => speed;
        public float Length => samples[samples.Length - 1].distance;
        public SensorySurfaceProfile Surface => surface;
        public IReadOnlyList<RoadLaneSample> Samples => sampleView ?? (sampleView = Array.AsReadOnly(samples));
        public IReadOnlyList<RoadId> Successors => successorView ?? (successorView = Array.AsReadOnly(successors));

        public RoadBakedLane(RoadId id, RoadId roadId, RoadClass roadClass, float speed,
            RoadLaneSample[] samples, RoadId[] successors, SensorySurfaceProfile surface, int compatibilityId = -1)
        {
            this.id = id; this.roadId = roadId; this.roadClass = roadClass; this.speed = speed;
            this.samples = (RoadLaneSample[])samples.Clone(); this.successors = (RoadId[])successors.Clone(); this.surface = surface;
            this.compatibilityId = compatibilityId;
        }
        internal RoadBakedLane WithCompatibilityId(int value) => new RoadBakedLane(id, roadId, roadClass, speed, samples, successors, surface, value);

        public RoadLaneSample Sample(float distance)
        {
            distance = Mathf.Clamp(distance, 0, Length);
            int low = 1, high = samples.Length - 1;
            while (low < high) { int mid = (low + high) / 2; if (samples[mid].distance < distance) low = mid + 1; else high = mid; }
            var a = samples[low - 1]; var b = samples[low];
            float t = (distance - a.distance) / (b.distance - a.distance);
            var rotation = Quaternion.Slerp(Quaternion.LookRotation(a.forward, a.up), Quaternion.LookRotation(b.forward, b.up), t);
            return new RoadLaneSample { distance = distance, station = Mathf.Lerp(a.station, b.station, t),
                width = Mathf.Lerp(a.width, b.width, t), position = Vector3.Lerp(a.position, b.position, t),
                forward = rotation * Vector3.forward, left = rotation * Vector3.left, up = rotation * Vector3.up };
        }

        public float Project(Vector3 position, out float error)
        {
            float best = float.PositiveInfinity, along = 0;
            for (int i = 1; i < samples.Length; i++)
            {
                var a = samples[i - 1]; var b = samples[i]; var edge = b.position - a.position;
                float t = Mathf.Clamp01(Vector3.Dot(position - a.position, edge) / edge.sqrMagnitude);
                float square = (position - a.position - edge * t).sqrMagnitude;
                if (square >= best) continue;
                best = square; along = Mathf.Lerp(a.distance, b.distance, t);
            }
            error = Mathf.Sqrt(best); return along;
        }
    }

    [Serializable]
    public sealed class RoadBakedChunk
    {
        [SerializeField] private RoadId roadId, bandId;
        [SerializeField] private Mesh mesh;
        [SerializeField] private Vector3 origin;
        [SerializeField] private Material material;
        [SerializeField] private SensorySurfaceProfile surface;
        [SerializeField] private bool collision;
        public RoadId RoadId => roadId;
        public RoadId BandId => bandId;
        public Mesh Mesh => mesh;
        public Vector3 Origin => origin;
        public Material Material => material;
        public SensorySurfaceProfile Surface => surface;
        public bool Collision => collision;
        public RoadBakedChunk(RoadId roadId, RoadId bandId, Mesh mesh, Vector3 origin, Material material, SensorySurfaceProfile surface, bool collision)
        { this.roadId = roadId; this.bandId = bandId; this.mesh = mesh; this.origin = origin; this.material = material; this.surface = surface; this.collision = collision; }
    }

    // One immutable publication owns both geometry and directed lane samples.
    public sealed class RoadNetworkAsset : ScriptableObject
    {
        public const int CurrentSchema = 2;
        [SerializeField] private int schemaVersion;
        [SerializeField] private RoadId networkId;
        [SerializeField] private string fingerprint;
        [SerializeField] private int nextCompatibilityId;
        [SerializeField] private RoadBakedLane[] lanes = Array.Empty<RoadBakedLane>();
        [SerializeField] private RoadBakedChunk[] chunks = Array.Empty<RoadBakedChunk>();
        [NonSerialized] private IReadOnlyList<RoadBakedLane> laneView;
        [NonSerialized] private IReadOnlyList<RoadBakedChunk> chunkView;
        public int SchemaVersion => schemaVersion;
        public RoadId NetworkId => networkId;
        public string Fingerprint => fingerprint;
        public int NextCompatibilityId => nextCompatibilityId;
        public IReadOnlyList<RoadBakedLane> Lanes => laneView ?? (laneView = Array.AsReadOnly(lanes));
        public IReadOnlyList<RoadBakedChunk> Chunks => chunkView ?? (chunkView = Array.AsReadOnly(chunks));
        public void Initialize(RoadId id, string sourceFingerprint, RoadBakedLane[] laneData, RoadBakedChunk[] meshData, int nextLaneId = 0)
        {
            if (schemaVersion != 0) throw new InvalidOperationException("A published road asset is immutable. Publish a new revision.");
            if (!id.IsValid || string.IsNullOrEmpty(sourceFingerprint) || laneData == null || meshData == null || nextLaneId < 0)
                throw new ArgumentException("Invalid road publication.");
            var stagedLanes = (RoadBakedLane[])laneData.Clone();
            var stagedChunks = (RoadBakedChunk[])meshData.Clone();
            int next = nextLaneId;
            var identities = new HashSet<RoadId>();
            foreach (var lane in stagedLanes)
            {
                if (lane == null || !lane.Id.IsValid || !identities.Add(lane.Id)) throw new ArgumentException("Missing or duplicate lane identity.");
                if (lane.CompatibilityId >= next) next = checked(lane.CompatibilityId + 1);
            }
            foreach (var chunk in stagedChunks) if (chunk == null) throw new ArgumentException("Missing road chunk.");
            var compatibilityIds = new HashSet<int>();
            for (int i = 0; i < stagedLanes.Length; i++)
            {
                if (stagedLanes[i].CompatibilityId < 0) stagedLanes[i] = stagedLanes[i].WithCompatibilityId(checked(next++));
                if (!compatibilityIds.Add(stagedLanes[i].CompatibilityId)) throw new ArgumentException("Duplicate compatibility lane ID.");
            }
            networkId = id; fingerprint = sourceFingerprint; nextCompatibilityId = next;
            lanes = stagedLanes; chunks = stagedChunks; schemaVersion = CurrentSchema;
            laneView = null; chunkView = null;
        }
    }
}
