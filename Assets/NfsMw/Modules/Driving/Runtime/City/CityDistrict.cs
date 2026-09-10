using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [DisallowMultipleComponent]
    public sealed class CityDistrict : MonoBehaviour
    {
        public const int CurrentSchema = 1;
        [HideInInspector] public int schemaVersion = CurrentSchema;
        [HideInInspector] public string id;
        public int worldSeed = 2005, seed = 1;
        public CityStyle style;
        public RoadNetworkAsset roads;
        public CityPolygon boundary = CityPolygon.Rectangle(-100,-100,200,200);
        [Min(0.01f)] public float surfaceLevelTolerance = 2;
        [Min(10)] public float cellSize = 128;
        public List<CityBlock> blocks = new List<CityBlock>();
        public List<CityParcel> parcels = new List<CityParcel>();
        public List<CityReservation> reservations = new List<CityReservation>();
        [HideInInspector] public List<CityOverride> overrides = new List<CityOverride>();
        [HideInInspector] public CityPublication publication;
        public Vector3 ToWorld(Vector2 point, float height = 0) => transform.position + new Vector3(point.x,height,point.y);
        public Vector2 ToLocal(Vector3 point) => new Vector2(point.x-transform.position.x,point.z-transform.position.z);
    }
}
