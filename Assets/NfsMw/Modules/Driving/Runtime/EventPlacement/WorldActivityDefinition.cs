using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum ActivityCompletionScope { Placement, Definition }
    public enum ActivityAnchorKind { World, Lane, CityEntrance, Socket }

    [CreateAssetMenu(menuName = "NFS MW Remaster/Activities/Definition")]
    public sealed class WorldActivityDefinition : ScriptableObject
    {
        public int schema = 1;
        public string id = Guid.NewGuid().ToString("N");
        public string displayName = "New activity", category = "Races", adapter = "race", localizationKey;
        public Color markerColor = Color.yellow;
        public ActivityCompletionScope completionScope;
        public bool uniqueDefinition, hideWhenLocked;
        public RaceRouteDefinition race;
        public MissionDefinitionAsset mission;
        public WorldLocationKind serviceKind = WorldLocationKind.Garage;
        public AssetVehicleStorefront storefront;
        [TextArea(3, 12)] public string availabilityJson = "{\"kind\":1,\"children\":[]}";
        public CareerRequirementDefinition availability
        {
            get => Newtonsoft.Json.JsonConvert.DeserializeObject<CareerRequirementDefinition>(availabilityJson);
            set => availabilityJson = Newtonsoft.Json.JsonConvert.SerializeObject(value);
        }
        [TextArea] public string notes;
    }

    [Serializable]
    public sealed class ActivityAnchor
    {
        public ActivityAnchorKind kind;
        public RoadNetworkAsset network;
        public string laneId, sourceRevision;
        [Min(0)] public float station;
        public CityPublication city;
        public string entranceId;
        public ActivitySocket socket;
        public string socketId;
        public Vector3 worldPosition;
        public Vector3 worldEuler;
    }

}
