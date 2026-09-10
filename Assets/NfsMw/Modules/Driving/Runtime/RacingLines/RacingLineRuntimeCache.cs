using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    // One source-validation allocation per 0.5 s per document, not one per car. No history retained after release.
    internal static class RacingLineRuntimeCache
    {
        internal sealed class Lease : IDisposable
        {
            internal RacingLineSource Source;
            internal int references;
            internal float expires;
            internal string fingerprint, failure;
            public string Fingerprint()
            {
                if (Source == null) throw new ArgumentException("LINE_SOURCE: the bound document was removed.");
                if (Time.realtimeSinceStartup >= expires)
                {
                    expires = Time.realtimeSinceStartup + 0.5f;
                    try { fingerprint = RacingLineSnapshot.Capture(Source).Fingerprint; failure = null; }
                    catch (ArgumentException e) { fingerprint = null; failure = e.Message; }
                }
                if (failure != null) throw new ArgumentException(failure);
                return fingerprint;
            }
            public void Dispose() { if (--references == 0) cache.Remove(Source); }
        }
        private static readonly Dictionary<RacingLineSource, Lease> cache = new Dictionary<RacingLineSource, Lease>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => cache.Clear();
        internal static Lease Acquire(RacingLineSource source)
        {
            if (source == null) throw new ArgumentException("LINE_SOURCE: assign a studio document.");
            if (!cache.TryGetValue(source, out var lease))
            {
                if (cache.Count >= 64) throw new ArgumentException("LINE_CACHE_BUDGET: more than 64 concurrently bound line documents.");
                lease = new Lease { Source = source, expires = -1 }; cache.Add(source, lease);
            }
            lease.references++; return lease;
        }
    }
}
