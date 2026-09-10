using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public sealed class WorldLocation : MonoBehaviour, IWorldLocation
    {
        [SerializeField] private string locationId;
        [SerializeField] private string displayName;
        [SerializeField] private WorldLocationKind kind;
        [SerializeField] private AssetVehicleStorefront storefront;
        [SerializeField, Min(1)] private float radius = 12;
        public string Id => locationId;
        public string DisplayName => displayName;
        public WorldLocationKind Kind => kind;
        public Vector3 Position => transform.position;
        public float Radius => radius;
        public IVehicleStorefront Storefront => storefront;
        public void Configure(string id, string label, WorldLocationKind type, AssetVehicleStorefront store)
        { locationId = id; displayName = label; kind = type; storefront = store; }
        private void OnDrawGizmosSelected()
        { Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(Position, radius); }
    }
}
