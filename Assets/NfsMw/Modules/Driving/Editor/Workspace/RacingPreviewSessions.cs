using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.Workspace
{
    public sealed class RacingPreviewLease : IDisposable
    {
        internal readonly string Resource;
        internal readonly Action Stop;
        public string Owner { get; }
        internal RacingPreviewLease(string resource, string owner, Action stop)
        { Resource=resource; Owner=owner; Stop=stop; }
        public void Dispose() => RacingPreviewSessions.Release(this);
    }

    [InitializeOnLoad]
    public static class RacingPreviewSessions
    {
        private static readonly Dictionary<string,RacingPreviewLease> active = new Dictionary<string,RacingPreviewLease>(StringComparer.Ordinal);
        private static bool stopping;
        public static int Count => active.Count;
        static RacingPreviewSessions()
        {
            AssemblyReloadEvents.beforeAssemblyReload += StopAll;
            EditorApplication.quitting += StopAll;
            EditorApplication.playModeStateChanged += _ => StopAll();
        }
        public static RacingPreviewLease Acquire(string resource, string owner, Action stop)
        {
            if(stopping)throw new InvalidOperationException("Preview shutdown is in progress.");
            if(string.IsNullOrWhiteSpace(resource)||string.IsNullOrWhiteSpace(owner)||stop==null)
                throw new ArgumentException("A preview needs a resource, owner and cleanup callback.");
            if(active.TryGetValue(resource,out var existing))
                throw new InvalidOperationException(resource+" is in use by "+existing.Owner+". Stop that preview first.");
            var lease=new RacingPreviewLease(resource,owner,stop);active.Add(resource,lease);return lease;
        }
        internal static void Release(RacingPreviewLease lease)
        { if(active.TryGetValue(lease.Resource,out var current)&&ReferenceEquals(current,lease))active.Remove(lease.Resource); }
        public static void StopAll()
        {
            // Snapshot and remove before callbacks: cleanup may dispose/reenter a lease.
            if(stopping)return;
            stopping=true;
            try
            {
                var leases=active.Values.ToArray();active.Clear();
                foreach(var lease in leases)try{lease.Stop();}catch(Exception exception){Debug.LogException(exception);}
            }
            finally{stopping=false;}
        }
    }
}
