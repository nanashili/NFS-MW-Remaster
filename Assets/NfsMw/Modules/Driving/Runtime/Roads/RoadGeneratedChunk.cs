using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [DisallowMultipleComponent]
    public sealed class RoadGeneratedChunk : MonoBehaviour
    {
        [SerializeField, HideInInspector] private RoadNetworkAsset asset;
        [SerializeField, HideInInspector] private int index;
        public RoadNetworkAsset Asset => asset;
        public int Index => index;
        public void Initialize(RoadNetworkAsset value, int chunkIndex) { asset = value; index = chunkIndex; }
    }
}
