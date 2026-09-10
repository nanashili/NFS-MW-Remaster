using UnityEngine;

namespace NfsMwRemaster.Driving
{
    // Spatial metadata remains available when an external world loader deactivates visual cell roots.
    public sealed class CityLocations : MonoBehaviour
    {
        [SerializeField] private CityPublication publication;
        public CityPublication Publication => publication;
        public void Configure(CityPublication value) => publication = value;
        public bool TryResolve(string id, out CityLocationRecord record, out Vector3 worldPosition)
        {
            worldPosition = default; record = null;
            if (publication == null || !publication.TryResolve(id, out record)) return false;
            worldPosition = transform.TransformPoint(record.localPosition); return true;
        }
    }
}
