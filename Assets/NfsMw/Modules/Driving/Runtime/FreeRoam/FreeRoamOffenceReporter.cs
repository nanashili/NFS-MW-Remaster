using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public sealed class FreeRoamOffenceReporter : MonoBehaviour
    {
        [SerializeField] private FreeRoamTraffic traffic;
        public void Configure(FreeRoamTraffic worldTraffic) { traffic = worldTraffic; }
        private void OnCollisionEnter(Collision collision)
        {
            if (traffic == null || collision.relativeVelocity.magnitude < 6) return;
            if (collision.collider.GetComponentInParent<DestructibleProp>() != null) return; // One semantic destruction fact owns this offence.
            // Ground contacts and ordinary landings are not property damage.
            if (collision.collider.GetComponent<VehicleSurface>() != null || collision.collider.GetComponent<VehiclePoliceUnit>() != null) return;
            traffic.ReportOffence(collision.rigidbody != null ? VehicleBountyEventKind.TradePaint : VehicleBountyEventKind.PropertyDamage);
        }
    }
}
