using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [RequireComponent(typeof(LineRenderer))]
    public sealed class FreeRoamCheckpointView : MonoBehaviour
    {
        [SerializeField] private FreeRoamSession session;
        private LineRenderer ring;
        private readonly Vector3[] points = new Vector3[48];
        public void Configure(FreeRoamSession game) { session = game; }
        private void Awake() { ring = GetComponent<LineRenderer>(); }
        private void LateUpdate()
        {
            bool visible = session.ActiveEvent != null && session.ActiveEvent.Kind != FreeRoamEventKind.Pursuit;
            ring.enabled = visible;
            if (!visible) return;
            Vector3 center = session.EventProgress.NextCheckpoint + Vector3.up * 0.4f;
            for (int i = 0; i < points.Length; i++)
            {
                float angle = i * Mathf.PI * 2 / points.Length;
                points[i] = center + new Vector3(Mathf.Sin(angle) * 9, 0, Mathf.Cos(angle) * 9);
            }
            ring.SetPositions(points);
        }
    }
}
