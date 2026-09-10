using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(menuName = "NFS MW Remaster/City/District style")]
    public sealed class CityStyle : ScriptableObject
    {
        public int schemaVersion = 1;
        public string contentId;
        public string description;
        public Color mapColor = new Color(0.3f,0.65f,0.7f);
        public CityLandUse defaultUse;
        public CityKit kit;
        [Min(1)] public float targetParcelArea = 1600, minimumParcelArea = 150;
        [Min(0)] public float setback = 3, minimumFrontage = 5;
        [Range(1,60)] public int minimumFloors = 1, maximumFloors = 4;
        [Range(0,1)] public float coverage = 0.6f;
        [Min(1)] public float dressingSpacing = 10;
        [Range(0,1000)] public int dressingPerParcel = 8;
        [Min(1)] public int maximumInstancesPerCell = 1500, maximumCollidersPerCell = 300;
        [Min(1)] public int maximumVerticesPerCell = 500000;
    }
}
