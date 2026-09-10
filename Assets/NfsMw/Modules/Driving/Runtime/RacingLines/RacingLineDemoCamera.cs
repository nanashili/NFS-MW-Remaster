using UnityEngine;

namespace NfsMwRemaster.Driving
{
    // Demonstration-only presentation; not a gameplay camera replacement.
    public sealed class RacingLineDemoCamera : MonoBehaviour
    {
        public Transform target;
        private void LateUpdate()
        {
            if (target == null) return;
            Vector3 desired = target.position - target.forward * 11 + Vector3.up * 6;
            transform.position = Vector3.Lerp(transform.position, desired, 1 - Mathf.Exp(-4 * Time.unscaledDeltaTime));
            transform.LookAt(target.position + target.forward * 4 + Vector3.up);
        }
        private void OnGUI()
        {
            if (target == null) return;
            var input = target.GetComponent<RacingLineInput>(); var vehicle = target.GetComponent<VehicleController>();
            GUI.Box(new Rect(12, 12, 600, 65), "RACING LINE STUDIO · shared physics demonstration\n"
                + (input == null ? "Missing trajectory input" : input.Failure ?? "Verified trajectory input active")
                + "\n" + (vehicle == null ? "" : vehicle.Telemetry.SpeedKph.ToString("F1") + " km/h · ordinary steering / throttle / brake"));
        }
    }
}
