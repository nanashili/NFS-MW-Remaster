using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public sealed class CityMapPart : MonoBehaviour
    {
        public int sceneNumber;
        public string sourceGuid, sourceRevision;
        public Vector3 importedPosition, importedScale;
        public Quaternion importedRotation;
    }
}
