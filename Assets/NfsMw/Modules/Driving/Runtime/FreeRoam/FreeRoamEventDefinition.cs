using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public sealed class FreeRoamEventDefinition : MonoBehaviour
    {
        [SerializeField] private string eventId;
        [SerializeField] private string displayName;
        [SerializeField] private FreeRoamEventKind kind;
        [SerializeField] private Vector3[] checkpoints = Array.Empty<Vector3>();
        [SerializeField, Min(1)] private int laps = 1;
        [SerializeField, Min(1)] private float timeLimit = 120;
        [SerializeField, Min(0)] private int reward = 1500;
        [SerializeField, Min(0)] private float targetSpeedKph = 100;
        [SerializeField] private RaceRoutePublication routePublication;
        public RaceRoutePublication RoutePublication => routePublication;
        public void BindRoute(RaceRoutePublication value) { routePublication=value; if(value!=null) checkpoints=value.Checkpoints; }
        public string Id => eventId;
        public string DisplayName => displayName;
        public FreeRoamEventKind Kind => kind;
        public Vector3[] Checkpoints => checkpoints;
        public int Laps => laps;
        public float TimeLimit => timeLimit;
        public int Reward => reward;
        public float TargetSpeedKph => targetSpeedKph;
        public bool TryValidate(out string failure)
        {
            failure = string.Empty;
            if(routePublication!=null && (routePublication.Schema!=1||checkpoints==null||routePublication.LegCount!=checkpoints.Length||routePublication.Network==null))
            {failure="Invalid published route binding.";return false;}
            if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(displayName)
                || checkpoints == null || checkpoints.Length == 0 || laps < 1 || laps > 1000
                || !float.IsFinite(timeLimit) || timeLimit <= 0 || reward < 0
                || !float.IsFinite(targetSpeedKph) || targetSpeedKph < 0 || !Enum.IsDefined(typeof(FreeRoamEventKind), kind))
            { failure = "Invalid event identity, course or rules."; return false; }
            foreach (Vector3 point in checkpoints)
                if (!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z))
                { failure = "Event checkpoints must be finite positions."; return false; }
            return true;
        }
        public void Configure(string id, string label, FreeRoamEventKind type, Vector3[] route,
            int lapCount, float limit, int cash, float speed = 100)
        {
            eventId = id; displayName = label; kind = type; checkpoints = route;
            laps = lapCount; timeLimit = limit; reward = cash; targetSpeedKph = speed;
        }
    }
}
