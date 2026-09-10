using System;
using UnityEngine;
namespace NfsMwRemaster.Lighting
{
    [DisallowMultipleComponent]
    public sealed class LightingProbeBinding : MonoBehaviour
    {
        public string id=Guid.NewGuid().ToString("N"),cellId="";
        public ReflectionProbe probe;
    }
}
