using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Serializable] public sealed class CityDressingAsset
    {
        public GameObject prefab;
        [Min(0)] public float weight = 1;
        public Vector2 scaleRange = Vector2.one;
        [Min(0.1f)] public float clearanceRadius = 1;
    }
    [CreateAssetMenu(menuName = "NFS MW Remaster/City/Structure kit")]
    public sealed class CityKit : ScriptableObject
    {
        public int schemaVersion = 1;
        public string contentId;
        [Tooltip("Optional authored exterior, with its pivot at ground centre. Leave empty for the modular fixture generator.")]
        public GameObject exteriorPrefab;
        public Vector3 prefabDimensions = new Vector3(20, 8, 20);
        public Material wall, roof, trim, glass, yard;
        public CityRoof roofFamily;
        [Min(1)] public float bayWidth = 5, floorHeight = 3.5f, minimumWidth = 8, minimumDepth = 8;
        [Range(1,60)] public int maximumFloors = 12;
        public bool loadingDock = true, roofDressing = true, collision = true;
        public CityDressingAsset[] dressing = Array.Empty<CityDressingAsset>();
    }
}
