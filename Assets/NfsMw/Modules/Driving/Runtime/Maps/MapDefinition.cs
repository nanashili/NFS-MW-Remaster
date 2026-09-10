using System;
using NfsMwRemaster.Driving;
using UnityEngine;

namespace NfsMwRemaster.Maps
{
    public enum MapRoadStructure { Surface, Bridge, Tunnel }
    [Serializable] public sealed class MapLaneLevel
    { public RoadId lane; public int level; public MapRoadStructure structure; }
    [CreateAssetMenu(menuName = "NFS MW Remaster/Maps/Definition")]
    public sealed class MapDefinition : ScriptableObject
    {
        public int schema = 1;
        public string id = Guid.NewGuid().ToString("N");
        public RoadNetworkAsset roads;
        public CityPublication[] districts = Array.Empty<CityPublication>();
        public MapFrame frame = new MapFrame();
        [Min(16)] public float tileSize = 256;
        [Min(0)] public float simplifyMeters = .25f;
        public MapLaneLevel[] levels = Array.Empty<MapLaneLevel>();
        public MapStyle style;
        public MapPublication publication;
    }
}
