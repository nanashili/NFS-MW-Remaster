using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [DisallowMultipleComponent]
    public sealed class RoadGeneratedNetwork : MonoBehaviour
    {
        [SerializeField, HideInInspector] private RoadNetworkAsset asset;
        public RoadNetworkAsset Asset => asset;
        public void Initialize(RoadNetworkAsset value) => asset = value;
    }
}
