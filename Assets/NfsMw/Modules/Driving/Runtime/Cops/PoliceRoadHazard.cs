using System;
using UnityEngine;
using UnityEngine.Events;

namespace NfsMwRemaster.Driving
{
    public enum PoliceRoadHazardKind { Roadblock, SpikeStrip }

    /// <summary>
    /// An authored deployment site, not a teleporting obstacle. Its inactive physical child is pooled in place.
    /// Knowledge selects sites; player/camera data only veto unfair placement or removal.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoliceRoadHazard : MonoBehaviour
    {
        [SerializeField] private VehiclePursuitDirector director;
        [SerializeField] private PoliceRoadHazardKind kind;
        [SerializeField] private GameObject physicalContent;
        [SerializeField, Range(1, 5)] private int minimumLevel = 3;
        [SerializeField, Min(1)] private float minimumDistance = 90f, maximumDistance = 300f;
        [SerializeField] private Vector3 clearanceHalfExtents = new Vector3(9f, 1.4f, 4f);
        [SerializeField] private UnityEvent onSpikeContact = new UnityEvent();
        private readonly Collider[] overlaps = new Collider[32];
        private readonly Plane[] frustum = new Plane[6];
        private string deployedEncounter;
        private Vector3 previousPosition;
        private bool tracking, resolved, spikeHit;
        private float nextCheck;
        public bool IsDeployed => physicalContent != null && physicalContent.activeSelf;
        public PoliceRoadHazardKind Kind => kind;
        public bool TargetContactedSpikes => spikeHit;
        // Actual contact is an integration fact, not an invented puncture model or health-based arrest.
        public event Action<IVehiclePursuitTarget> SpikeContact;

        public void Configure(VehiclePursuitDirector pursuit, PoliceRoadHazardKind type, GameObject content, int level)
        {
            if (content == null || content == gameObject || !content.transform.IsChildOf(transform))
                throw new ArgumentException("Hazard content must be a dedicated child of its authored site.");
            if (level < 1 || level > 5) throw new ArgumentOutOfRangeException(nameof(level));
            if (IsDeployed) throw new InvalidOperationException("Cannot reconfigure a deployed hazard.");
            director = pursuit; kind = type; physicalContent = content; minimumLevel = level;
            physicalContent.SetActive(false);
        }

        private void Start()
        {
            if (physicalContent == null || physicalContent == gameObject || !physicalContent.transform.IsChildOf(transform)
                || !PolicePursuitRules.Finite(minimumDistance) || !PolicePursuitRules.Finite(maximumDistance)
                || minimumDistance < 1 || maximumDistance <= minimumDistance)
            { Debug.LogError("Invalid authored police hazard: " + name, this); enabled = false; return; }
            physicalContent.SetActive(false);
        }

        private void Update()
        {
            var target = director != null ? director.Target : null;
            if (target == null || !target.IsAvailable || Time.deltaTime <= 0f) return;
            if (IsDeployed)
            {
                Vector3 position = target.Position;
                if (tracking && !resolved && director.EncounterId == deployedEncounter)
                {
                    Vector3 before = transform.InverseTransformPoint(previousPosition), after = transform.InverseTransformPoint(position);
                    // Crossing the downstream plane after a continuous approach, not a respawn/teleport.
                    if (before.z <= 8f && after.z > 8f && Mathf.Abs(after.x) <= clearanceHalfExtents.x
                        && (position - previousPosition).sqrMagnitude < 2500f)
                    {
                        resolved = true;
                        if (!spikeHit) director.TryRecordBountyEvent(kind == PoliceRoadHazardKind.SpikeStrip
                            ? VehicleBountyEventKind.SpikeStripDodged : VehicleBountyEventKind.RoadblockDodged, 1, out _);
                    }
                }
                previousPosition = position; tracking = true;
            }
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + 0.5f;
            if (IsDeployed)
            {
                if ((director.EncounterId != deployedEncounter || director.EncounterState == PoliceEncounterState.OutcomePending)
                    && FairToChange(target, Camera.main) && ClearanceIsEmpty())
                { physicalContent.SetActive(false); tracking = false; }
            }
            else TryDeploy(Camera.main);
        }

        public bool TryDeploy(Camera view)
        {
            var target = director != null ? director.Target : null;
            if (IsDeployed || physicalContent == null || target == null || !target.IsAvailable
                || director.EncounterId == deployedEncounter || director.EncounterState != PoliceEncounterState.Pursuit
                || director.HeatLevel < minimumLevel || !FairToChange(target, view)) return false;
            Vector3 offset = transform.position - director.LastKnownPosition;
            float distance = offset.magnitude;
            // Only sites ahead of the last observed approach are eligible. No hidden player tracking.
            if (distance < minimumDistance || distance > maximumDistance || Vector3.Dot(offset, transform.forward) < minimumDistance * 0.5f
                || !ClearanceIsEmpty()) return false;
            deployedEncounter = director.EncounterId; resolved = spikeHit = false;
            previousPosition = target.Position; tracking = true;
            physicalContent.SetActive(true); return true;
        }

        private bool FairToChange(IVehiclePursuitTarget target, Camera view)
        {
            if (view == null) return false; // An unknown view is not proof of hidden placement.
            float safeDistance = Mathf.Max(minimumDistance, target.Velocity.magnitude * 4f);
            if ((target.Position - transform.position).sqrMagnitude < safeDistance * safeDistance) return false;
            GeometryUtility.CalculateFrustumPlanes(view, frustum);
            Vector3 right = transform.right * clearanceHalfExtents.x, forward = transform.forward * clearanceHalfExtents.z;
            Vector3 extents = new Vector3(Mathf.Abs(right.x) + Mathf.Abs(forward.x), clearanceHalfExtents.y,
                Mathf.Abs(right.z) + Mathf.Abs(forward.z));
            return !GeometryUtility.TestPlanesAABB(frustum, new Bounds(transform.position + Vector3.up * clearanceHalfExtents.y, extents * 2f));
        }

        private bool ClearanceIsEmpty()
        {
            int count = Physics.OverlapBoxNonAlloc(transform.position + Vector3.up * (clearanceHalfExtents.y + 0.05f),
                clearanceHalfExtents, overlaps, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return false;
            for (int i = 0; i < count; i++) if (!overlaps[i].transform.IsChildOf(transform)) return false;
            return true;
        }

        public bool ReportSpikeContact(Collider contact)
        {
            var target = director != null ? director.Target : null;
            if (!IsDeployed || kind != PoliceRoadHazardKind.SpikeStrip || spikeHit || contact == null
                || target == null || target.Body == null || !target.IsAvailable || contact.attachedRigidbody != target.Body
                || director.EncounterId != deployedEncounter) return false;
            Vector3 centre = transform.position + Vector3.up * 0.5f;
            if ((contact.ClosestPoint(centre) - centre).sqrMagnitude > 16f) return false;
            spikeHit = true;
            if (SpikeContact != null)
                foreach (Action<IVehiclePursuitTarget> listener in SpikeContact.GetInvocationList())
                    try { listener(target); } catch (Exception exception) { Debug.LogException(exception); }
            try { onSpikeContact.Invoke(); } catch (Exception exception) { Debug.LogException(exception); }
            return true;
        }
        private void OnDisable() { if (physicalContent != null && physicalContent != gameObject) physicalContent.SetActive(false); tracking = false; }
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = kind == PoliceRoadHazardKind.Roadblock ? Color.yellow : Color.red;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up * clearanceHalfExtents.y, clearanceHalfExtents * 2f);
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * 12f);
        }
    }
}
