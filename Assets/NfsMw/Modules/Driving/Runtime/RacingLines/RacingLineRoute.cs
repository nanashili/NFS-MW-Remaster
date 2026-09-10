using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(menuName = "NFS MW Remaster/Racing Lines/Route Adapter", fileName = "LineRoute")]
    public sealed class RacingLineRoute : ScriptableObject
    {
        public const int CurrentSchema = 1;
        [HideInInspector] public int schema = CurrentSchema;
        [HideInInspector] public string id = Guid.NewGuid().ToString("N");
        public RoadNetworkAsset network;
        public bool closed;
        public RacingRouteSpan[] spans = Array.Empty<RacingRouteSpan>();
    }
}
