using NfsMwRemaster.Driving;
using UnityEngine;
namespace NfsMwRemaster.Maps
{
    /// <summary>Uses existing activity eligibility. Optional discovery/pursuit feeds require an explicit owner adapter.</summary>
    public sealed class FreeRoamMapKnowledge : MonoBehaviour,IMapKnowledge
    {
        public FreeRoamSession session;
        [Tooltip("Explicitly approve ordinary road geometry. Hidden roads require a specialized discovery adapter.")]
        public bool publicRoadNetwork;
        public bool RoadVisible(RoadId lane)=>publicRoadNetwork;
        public bool MarkerVisible(string id,string category)=>false;
        public bool ActivityVisible(ActivityMapMarker marker)=>session!=null && (session.ActivityEligible(marker)||!marker.Snapshot.hideWhenLocked);
        public string Localize(string key,string fallback)=>string.IsNullOrEmpty(fallback)?key:fallback;
    }
}
