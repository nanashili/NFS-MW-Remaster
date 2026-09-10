using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public sealed class RoadPolicePerception : MonoBehaviour, IVehiclePursuitPerception
    {
        private readonly RaycastHit[] hits = new RaycastHit[32];
        public bool CanSee(IVehiclePoliceUnit observer, IVehiclePursuitTarget target, float range)
        {
            if (observer == null || observer.IsDisabled || target == null || !target.IsAvailable) return false;
            if (observer is Component component)
            {
                Vector3 delta = target.Position - observer.Position;
                // Close contact can be noticed behind the officer; distant recognition requires an observer-facing cone.
                if (delta.sqrMagnitude > 64f && Vector3.Angle(component.transform.forward, delta) > 75f) return false;
            }
            return ClearLine(observer.Position + Vector3.up, target.Position + Vector3.up,
                (observer as Component)?.transform, target.Transform, range);
        }
        public bool ClearLine(Vector3 from, Vector3 to, Transform observer, Transform target, float range)
        {
            Vector3 delta = to - from;
            if (delta.magnitude > range) return false;
            int count = Physics.RaycastNonAlloc(from, delta.normalized, hits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            for (int i = 0; i < count; i++)
                if ((observer == null || !hits[i].transform.IsChildOf(observer))
                    && (target == null || !hits[i].transform.IsChildOf(target))) return false;
            return true;
        }
    }
}
