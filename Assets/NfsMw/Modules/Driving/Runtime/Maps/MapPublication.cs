using System;
using UnityEngine;
using NfsMwRemaster.Driving;
namespace NfsMwRemaster.Maps
{
    [Serializable] public sealed class MapSegment
    {
        public RoadId lane, road;
        public int level, detail;
        public MapRoadStructure structure;
        public RoadClass roadClass;
        public string surface;
        public MapPoint a, b;
        public float start, end, heightA, heightB, width;
    }
    [Serializable] public sealed class MapTileData { public int schema = 1; public string frameRevision; public MapSegment[] segments; }
    [Serializable] public struct MapTileEntry
    { public int x, y, segments, bytes; public string resource, fingerprint; }
    [Serializable] public struct MapLandmark
    { public string id, district, label; public MapPoint point; public RoadId accessLane; public bool accessible; }
    [Serializable] public struct MapLaneInfo { public RoadId lane; public int level; public float length; }
    public sealed class MapPublication : ScriptableObject
    {
        public const int CurrentSchema = 1;
        [SerializeField] private int schema;
        [SerializeField] private string mapId, fingerprint, frameRevision, roadRevision;
        [SerializeField] private string roadNetworkId;
        [SerializeField] private MapFrame frame;
        [SerializeField] private float tileSize;
        [SerializeField] private MapTileEntry[] tiles;
        [SerializeField] private MapTileEntry overview;
        public MapTileEntry Overview => overview;
        [SerializeField] private MapLandmark[] landmarks;
        [SerializeField] private MapLaneInfo[] laneInfo=Array.Empty<MapLaneInfo>();
        private System.Collections.Generic.Dictionary<RoadId,MapLaneInfo> laneLookup;
        public bool TryLane(RoadId id,out MapLaneInfo info)
        {if(laneLookup==null){laneLookup=new System.Collections.Generic.Dictionary<RoadId,MapLaneInfo>();foreach(var lane in laneInfo)laneLookup[lane.lane]=lane;}return laneLookup.TryGetValue(id,out info); }
        [SerializeField] private MapPoint minimum, maximum;
        public int Schema => schema;
        public string Id => mapId;
        public string Fingerprint => fingerprint;
        public string FrameRevision => frameRevision;
        public string RoadRevision => roadRevision;
        public string RoadNetworkId => roadNetworkId;
        public MapFrame Frame => frame.Copy();
        public float TileSize => tileSize;
        public System.Collections.Generic.IReadOnlyList<MapTileEntry> Tiles => Array.AsReadOnly(tiles);
        public System.Collections.Generic.IReadOnlyList<MapLandmark> Landmarks => Array.AsReadOnly(landmarks);
        public MapPoint Minimum => minimum;
        public MapPoint Maximum => maximum;
        public void Initialize(string id, string revision, string coordinates, RoadNetworkAsset source, MapFrame basis,
            float size, MapTileEntry[] index, MapLandmark[] locations, MapPoint min, MapPoint max, MapLaneInfo[] laneIndex=null,MapTileEntry overviewTile=default)
        {
            if (schema != 0) throw new InvalidOperationException("Map publications are immutable; publish a new revision.");
            basis.Validate();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(revision) || source == null || size < 16 || index == null || locations == null)
                throw new ArgumentException("Invalid map publication.");
            mapId = id; fingerprint = revision; frameRevision = coordinates; roadNetworkId = source.NetworkId.ToString(); roadRevision = source.Fingerprint;
            frame = basis.Copy(); tileSize = size; tiles = (MapTileEntry[])index.Clone(); landmarks = (MapLandmark[])locations.Clone();
            overview=overviewTile;
            laneInfo=laneIndex==null?Array.Empty<MapLaneInfo>():(MapLaneInfo[])laneIndex.Clone();laneLookup=null;
            minimum = min; maximum = max; schema = CurrentSchema;
        }
    }
}
