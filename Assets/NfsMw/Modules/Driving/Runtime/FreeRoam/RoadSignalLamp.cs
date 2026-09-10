using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public sealed class RoadSignalLamp : MonoBehaviour
    {
        [SerializeField] private RoadTrafficSignals signals;
        [SerializeField] private Vector3 intersection;
        [SerializeField] private Vector3 direction;
        [SerializeField] private int movementId = -1, intersectionId = -1;
        private Renderer lamp;
        private MaterialPropertyBlock properties;
        public void Configure(RoadTrafficSignals controller, Vector3 point, Vector3 approach)
        { signals = controller; intersection = point; direction = approach; }
        public void ConfigureMovement(RoadTrafficSignals controller, int junction, int laneId)
        { signals = controller; intersectionId = junction; movementId = laneId; }
        private void Awake() { lamp = GetComponent<Renderer>(); properties = new MaterialPropertyBlock(); }
        private void Update()
        {
            if (signals == null || lamp == null) return;
            RoadSignalAspect aspect = movementId >= 0
                ? signals.TryMovementAspect(intersectionId, movementId, Time.time, out var movementAspect) ? movementAspect : RoadSignalAspect.Red
                : signals.Aspect(intersection, direction, Time.time);
            Color color = aspect == RoadSignalAspect.Green ? Color.green : aspect == RoadSignalAspect.Yellow ? Color.yellow : Color.red;
            properties.SetColor("_BaseColor", color); properties.SetColor("_Color", color);
            properties.SetColor("_EmissiveColor",color*3000);
            lamp.SetPropertyBlock(properties);
        }
    }
}
