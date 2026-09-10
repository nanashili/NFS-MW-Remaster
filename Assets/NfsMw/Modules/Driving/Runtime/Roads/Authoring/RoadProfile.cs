using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Serializable]
    public struct RoadProfilePoint
    {
        [Range(0, 1)] public float fraction;
        public float height;
        public RoadProfilePoint(float fraction, float height) { this.fraction = fraction; this.height = height; }
    }

    [Serializable]
    public sealed class RoadBand
    {
        [HideInInspector] public RoadId id;
        public string label;
        public RoadBandKind kind;
        public RoadTravelDirection direction;
        [Min(0)] public float width = 3.5f;
        public float height;
        [Range(-1, 1)] public float crossfall;
        public Material material;
        public SensorySurfaceProfile surface;
        public bool collision = true;
        public RoadProfilePoint[] shape = { new RoadProfilePoint(0, 0), new RoadProfilePoint(1, 0) };
    }

    [CreateAssetMenu(menuName = "NFS MW Remaster/Roads/Profile")]
    public sealed class RoadProfile : ScriptableObject
    {
        public RoadClass roadClass = RoadClass.Local;
        [Min(0.1f)] public float speedLimit = 13.888889f;
        [Min(0.1f)] public float textureMetres = 5;
        public float lateralOffset;
        [Tooltip("Bands ordered from the left edge to the right edge, looking forward.")]
        public RoadBand[] bands = Array.Empty<RoadBand>();

        public static RoadProfile CreateTwoLane()
        {
            var profile = CreateInstance<RoadProfile>();
            profile.name = "Two lane road";
            profile.bands = new[]
            {
                new RoadBand { id = RoadId.New(), label = "Left lane", direction = RoadTravelDirection.Reverse },
                new RoadBand { id = RoadId.New(), label = "Right lane", direction = RoadTravelDirection.Forward }
            };
            return profile;
        }

        public static RoadProfile CreateLocalStreet()
        {
            var profile = CreateTwoLane(); profile.name = "Local street";
            profile.bands = new[]
            {
                new RoadBand { id = RoadId.New(), label = "Left sidewalk", kind = RoadBandKind.Sidewalk, width = 2, height = 0.15f },
                new RoadBand { id = RoadId.New(), label = "Left curb", kind = RoadBandKind.Curb, width = 0.2f,
                    shape = new[] { new RoadProfilePoint(0, 0.15f), new RoadProfilePoint(0.5f, 0.15f), new RoadProfilePoint(0.5f, 0), new RoadProfilePoint(1, 0) } },
                profile.bands[0], profile.bands[1],
                new RoadBand { id = RoadId.New(), label = "Right curb", kind = RoadBandKind.Curb, width = 0.2f,
                    shape = new[] { new RoadProfilePoint(0, 0), new RoadProfilePoint(0.5f, 0), new RoadProfilePoint(0.5f, 0.15f), new RoadProfilePoint(1, 0.15f) } },
                new RoadBand { id = RoadId.New(), label = "Right sidewalk", kind = RoadBandKind.Sidewalk, width = 2, height = 0.15f }
            };
            return profile;
        }
    }
}
