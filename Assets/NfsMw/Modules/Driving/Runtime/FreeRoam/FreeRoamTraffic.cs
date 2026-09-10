using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public sealed class FreeRoamTraffic : MonoBehaviour
    {
        [SerializeField] private RoadNetwork roads;
        [SerializeField] private VehicleController player;
        [SerializeField] private VehiclePursuitDirector pursuit;
        [SerializeField] private RoadPolicePerception perception;
        [SerializeField] private RoadVehicleMotor[] civilians = Array.Empty<RoadVehicleMotor>();
        [SerializeField] private VehiclePoliceUnit[] police = Array.Empty<VehiclePoliceUnit>();
        private float poolTimer;
        [SerializeField, Min(0.1f)] private float reinforcementInterval = 3f;
        private float nextPoliceSpawn;
        private float speedingTime;
        private float offenceCooldown;
        private float pursuitEndedAt;
        private bool wasPursued;
        private Vector3 previousPlayerPosition;
        private RoadTrafficSignals signals;
        public RoadVehicleMotor[] Civilians => civilians;
        public VehiclePoliceUnit[] Police => police;
        public int ActiveCivilianCount { get { int count = 0; foreach (var car in civilians) if (car != null && car.gameObject.activeSelf) count++; return count; } }
        public int ActivePoliceCount { get { int count = 0; foreach (var car in police) if (car != null && car.gameObject.activeSelf && !car.IsDisabled) count++; return count; } }
        public void Configure(RoadNetwork network, VehicleController vehicle, VehiclePursuitDirector director,
            RoadPolicePerception sight, RoadVehicleMotor[] traffic, VehiclePoliceUnit[] cops)
        { roads = network; player = vehicle; pursuit = director; perception = sight; civilians = traffic; police = cops; }

        private void Start()
        {
            if (player != null) previousPlayerPosition = player.transform.position;
            if (roads != null) signals = roads.GetComponent<RoadTrafficSignals>();
            if (GetComponent<TrafficWorldDirector>() == null && roads != null)
                gameObject.AddComponent<TrafficWorldDirector>().Configure(roads, signals, civilians, player, police);
        }

        private void Update()
        {
            if (player == null || roads == null || pursuit == null) return;
            if (wasPursued && !pursuit.IsActive) pursuitEndedAt = Time.time;
            wasPursued = pursuit.IsActive;
            offenceCooldown -= Time.deltaTime;
            bool speeding = player.Body != null && player.Body.linearVelocity.magnitude * 3.6f > 70;
            DetectRedLightCrossing();
            speedingTime = speeding ? speedingTime + Time.deltaTime : 0;
            if (speedingTime > 1.2f && offenceCooldown <= 0 && Time.time - pursuitEndedAt > 6)
                ReportOffence(VehicleBountyEventKind.TrafficInfraction);
            if (Time.time < poolTimer) return;
            poolTimer = Time.time + 1;
            int budget = pursuit.IsActive ? Mathf.Min(police.Length, pursuit.CurrentTier.MaxUnits) : Mathf.Min(2, police.Length);
            int active = ActivePoliceCount;
            foreach (VehiclePoliceUnit unit in police)
            {
                bool hidden = HiddenFromPlayer(unit.Position);
                if (unit.gameObject.activeSelf && hidden && (unit.IsDisabled && Time.time - unit.DisabledAt > 20
                    || active > budget || Vector3.Distance(player.transform.position, unit.Position) > 320))
                { if (!unit.IsDisabled) active--; unit.gameObject.SetActive(false); }
                if (!unit.gameObject.activeSelf && active < budget && Time.time >= nextPoliceSpawn
                    && pursuit.EncounterState != PoliceEncounterState.Cooldown
                    && pursuit.EncounterState != PoliceEncounterState.OutcomePending && TrySpawnPolice(unit))
                { active++; nextPoliceSpawn = Time.time + reinforcementInterval; }
            }
        }

        private void DetectRedLightCrossing()
        {
            Vector3 position = player.transform.position;
            Vector3 movement = position - previousPlayerPosition; movement.y = 0;
            if (signals != null && movement.sqrMagnitude > 0.0001f && movement.magnitude < 15)
            {
                Vector3 direction = movement.normalized;
                foreach (RoadNode node in roads.Nodes)
                {
                    if (node.exits.Length < 3) continue;
                    Vector3 before = node.position - previousPlayerPosition;
                    Vector3 after = node.position - position;
                    if (Vector3.Dot(before, direction) > 8 && Vector3.Dot(after, direction) <= 8
                        && Mathf.Abs(Vector3.Dot(after, Vector3.Cross(Vector3.up, direction))) < 8
                        && signals.Aspect(node.position, direction, Time.time) == RoadSignalAspect.Red)
                    { ReportOffence(VehicleBountyEventKind.TrafficInfraction); break; }
                }
            }
            previousPlayerPosition = position;
        }

        public bool ReportOffence(VehicleBountyEventKind kind)
        {
            if (offenceCooldown > 0 || player.GetComponent<FreeRoamSession>()?.CanDrive == false) return false;
            bool reported = false;
            foreach (VehiclePoliceUnit unit in police)
                if (!unit.IsDisabled && perception.CanSee(unit, pursuit.Target, pursuit.IsActive ? pursuit.CurrentTier.DetectionRadius : 65))
                {
                    PoliceOffence offence = kind == VehicleBountyEventKind.PropertyDamage ? PoliceOffence.PropertyDamage
                        : kind == VehicleBountyEventKind.TradePaint ? PoliceOffence.Collision : PoliceOffence.TrafficInfraction;
                    reported = pursuit.ReportObservedOffence(unit, offence, out _); break;
                }
            if (!reported) return false;
            offenceCooldown = 6;
            return true;
        }

        private bool HiddenFromPlayer(Vector3 point)
        {
            if (Vector3.Distance(player.transform.position, point) < 65) return false;
            Camera camera = Camera.main;
            if (camera == null) return true;
            Vector3 viewport = camera.WorldToViewportPoint(point + Vector3.up);
            return viewport.z < 0 || viewport.x < -0.15f || viewport.x > 1.15f || viewport.y < -0.15f || viewport.y > 1.15f;
        }

        private bool TrySpawnPolice(VehiclePoliceUnit unit)
        {
            if (roads.Nodes.Count == 0 || unit.gameObject.activeSelf) return false;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                RoadNode node = roads.Nodes[UnityEngine.Random.Range(0, roads.Nodes.Count)];
                if (node.exits.Length == 0) continue;
                RoadNode end = roads.Nodes[node.exits[UnityEngine.Random.Range(0, node.exits.Length)]];
                Vector3 forward = (end.position - node.position).normalized;
                Vector3 point = Vector3.Lerp(node.position, end.position, 0.5f)
                    + Vector3.Cross(Vector3.up, forward) * 3f + Vector3.up * 0.65f;
                // Arrival routing uses last-known knowledge. Player position is consulted only for fair, hidden placement.
                Vector3 centre = pursuit.IsActive ? pursuit.LastKnownPosition : player.transform.position;
                if (Vector3.Distance(centre, point) > 260f || !HiddenFromPlayer(point)) continue;
                if (Physics.CheckBox(point + Vector3.up * 0.4f, new Vector3(1.3f, 0.5f, 2.7f), Quaternion.LookRotation(forward), ~0, QueryTriggerInteraction.Ignore)) continue;
                if (!unit.PlaceWhileInactive(point, Quaternion.LookRotation(forward))) continue;
                unit.gameObject.SetActive(true); return true;
            }
            return false;
        }
    }
}
