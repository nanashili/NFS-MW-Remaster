using System;
using UnityEngine;
namespace NfsMwRemaster.Lighting
{
    [Serializable] public sealed class LightingCameraPose
    {
        public string name="Driving camera";
        public Vector3 position,euler;
        [Range(20,100)]public float fieldOfView=60;
    }
    [CreateAssetMenu(menuName="NFS MW/Lighting/Camera Review Set")]
    public sealed class LightingCameraSet : ScriptableObject
    {
        public int schemaVersion=1;public string id=Guid.NewGuid().ToString("N");
        [TextArea]public string intent="Fixed inspection poses. This is not a race route or traffic path.";
        public LightingCameraPose[] poses=Array.Empty<LightingCameraPose>();
    }
}
