using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [DisallowMultipleComponent]
    public sealed class CityGeneratedInstance : MonoBehaviour
    {
        [HideInInspector] public string districtId, parcelId, key, signature;
        [HideInInspector] public Vector3 plannedPosition, plannedEuler, plannedScale;
        [HideInInspector] public CityOwnership state;
    }
}
