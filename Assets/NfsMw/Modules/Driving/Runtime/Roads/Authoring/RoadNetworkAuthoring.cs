using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Serializable]
    public sealed class RoadLaneConnection
    {
        public RoadId from, to;
    }

    [DisallowMultipleComponent, RequireComponent(typeof(RoadNetwork))]
    public sealed class RoadNetworkAuthoring : MonoBehaviour
    {
        [SerializeField, HideInInspector] private RoadId id;
        [SerializeField] private RoadAuthoring[] roads = Array.Empty<RoadAuthoring>();
        [SerializeField, HideInInspector] private RoadNetworkAsset baked;
        public RoadLaneConnection[] connections = Array.Empty<RoadLaneConnection>();
        public RoadId Id => id;
        public RoadAuthoring[] Roads => roads;
        public RoadNetworkAsset Baked => baked;
        public void Initialize(RoadAuthoring[] values)
        {
            if (!id.IsValid) id = RoadId.New();
            roads = (RoadAuthoring[])values.Clone();
        }
        public void SetPublication(RoadNetworkAsset asset) => baked = asset;
    }
}
