using System;
using System.Collections.Generic;
using UnityEngine;
namespace NfsMwRemaster.Driving
{
    /// <summary>Shared input ownership, independent of map rendering assembly.</summary>
    public static class MapInputFocus
    {
        private static readonly HashSet<object> owners = new HashSet<object>();
        private static readonly HashSet<object> presentations = new HashSet<object>();
        private static int releasedFrame = -1;
        public static bool Captured => owners.Count > 0 || releasedFrame == Time.frameCount;
        public static bool HasPresentation => presentations.Count > 0;
        public static void Register(object owner) { if(owner==null)throw new ArgumentNullException(nameof(owner));presentations.Add(owner); }
        public static void Acquire(object owner) { if(owner==null)throw new ArgumentNullException(nameof(owner));owners.Add(owner); }
        public static void Release(object owner) { if(owners.Remove(owner))releasedFrame=Time.frameCount; }
        public static void Unregister(object owner) { Release(owner);presentations.Remove(owner); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset(){owners.Clear();presentations.Clear();releasedFrame=-1;}
    }
}
