using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Serializable]
    public sealed class ActivityRecord
    {
        public string id, definitionId, label, adapter, fingerprint, district, level, cell;
        // Optional v1 extension: absent in older publications, so map routing stays unavailable until republished.
        public RoadId accessLane;
        public string accessNetworkId, accessRoadRevision;
        public float accessDistance;
        public ActivityCompletionScope completionScope;
        public bool uniqueDefinition, hideWhenLocked;
        public Color color;
        public Vector3 interaction, staging, icon, cinematic, access, triggerSize, vehicleSize;
        public Quaternion rotation;
        public float approachAngle, maximumSpeed, dwellSeconds;
        public Vector3[] exits;
        public string availabilityJson;
        public CareerRequirementDefinition availability
        {
            get => Newtonsoft.Json.JsonConvert.DeserializeObject<CareerRequirementDefinition>(availabilityJson);
            set => availabilityJson = Newtonsoft.Json.JsonConvert.SerializeObject(value);
        }
        public RaceRoutePublication route;
        public FreeRoamEventKind raceKind;
        public int laps;
        public float timeLimit, targetSpeedKph;
        public MissionDefinitionAsset mission;
        public string missionFingerprint;
        public WorldLocationKind serviceKind;
        public AssetVehicleStorefront storefront;
        public bool Contains(Vector3 position)
        {
            Vector3 p = Quaternion.Inverse(rotation) * (position - interaction);
            return Mathf.Abs(p.x) <= triggerSize.x / 2 && Mathf.Abs(p.y) <= triggerSize.y / 2 && Mathf.Abs(p.z) <= triggerSize.z / 2;
        }
        public ActivityRecord Copy() => JsonUtility.FromJson<ActivityRecord>(JsonUtility.ToJson(this));
    }

    public sealed class EventPlacementPublication : ScriptableObject
    {
        [SerializeField] private int schema;
        [SerializeField] private ActivityRecord record;
        public int Schema => schema;
        public ActivityRecord Snapshot => record?.Copy();
        public string Id => record?.id;
        public string Fingerprint => record?.fingerprint;
        public void Initialize(ActivityRecord value)
        {
            if (schema != 0) throw new InvalidOperationException("Create a new placement publication.");
            if (value == null || string.IsNullOrWhiteSpace(value.id) || string.IsNullOrWhiteSpace(value.fingerprint))
                throw new ArgumentException("Placement identity and revision are required.");
            record = value.Copy(); schema = 1;
        }
    }

    /// <summary>Detached facts for authoring previews. Never reads or writes a player profile.</summary>
    public sealed class ActivityPreviewFacts : ICareerFacts
    {
        private readonly System.Collections.Generic.Dictionary<string, long> values = new System.Collections.Generic.Dictionary<string, long>();
        public void Set(CareerFactKind kind, string subject, long value) => values[kind + ":" + subject] = value;
        public long Read(CareerFactKind kind, string subjectId) => values.TryGetValue(kind + ":" + subjectId, out long value) ? value : 0;
    }
}
