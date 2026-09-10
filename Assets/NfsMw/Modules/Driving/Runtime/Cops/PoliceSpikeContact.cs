using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Physical trigger relay. Parent site verifies target identity and encounter-scoped deduplication.</summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class PoliceSpikeContact : MonoBehaviour
    {
        private PoliceRoadHazard site;
        private void Awake() { site = GetComponentInParent<PoliceRoadHazard>(); GetComponent<BoxCollider>().isTrigger = true; }
        private void OnTriggerEnter(Collider other) { if (site != null) site.ReportSpikeContact(other); }
    }
}
