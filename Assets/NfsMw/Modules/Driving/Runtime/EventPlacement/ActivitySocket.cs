using System;
using UnityEngine;
namespace NfsMwRemaster.Driving
{
    [DisallowMultipleComponent]
    public sealed class ActivitySocket : MonoBehaviour
    {
        public string id = Guid.NewGuid().ToString("N");
        public string revision = "1";
    }

}
