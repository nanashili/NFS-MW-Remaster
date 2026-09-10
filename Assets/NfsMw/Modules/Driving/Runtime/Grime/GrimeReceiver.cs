using System;
using UnityEngine;
namespace NfsMwRemaster.Driving
{
    [DisallowMultipleComponent]
    public sealed class GrimeReceiver : MonoBehaviour
    {
        public string id = Guid.NewGuid().ToString("N");
        public MeshCollider surface;
        public Renderer surfaceRenderer;
        public string surfaceCategory = "asphalt";
        public bool allowDressing = true;
        public bool approvedAllSubmeshes;
        public Mesh Mesh => surface != null ? surface.sharedMesh : null;
    }
}
