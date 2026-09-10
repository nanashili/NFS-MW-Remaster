using System;
using System.Linq;
using UnityEngine;
namespace NfsMwRemaster.Driving
{
    public sealed class RaceRoutePublication : ScriptableObject
    {
        [SerializeField] private int schema;
        [SerializeField] private string routeId,fingerprint,roadFingerprint;
        [SerializeField] private RoadNetworkAsset network;
        [SerializeField] private RaceRouteBakedLeg[] legs;
        [SerializeField] private Vector3[] grid = Array.Empty<Vector3>();
        [SerializeField] private Quaternion[] gridRotations = Array.Empty<Quaternion>();
        [SerializeField] private Vector3 entrantDimensions;
        public int GridCount => grid.Length;
        public Vector3 GridPosition(int index) => grid[index];
        public Quaternion GridRotation(int index) => gridRotations[index];
        public Vector3 EntrantDimensions => entrantDimensions;
        public int Schema=>schema;
        public string RouteId=>routeId;
        public string Fingerprint=>fingerprint;
        public string RoadFingerprint=>roadFingerprint;
        public RoadNetworkAsset Network=>network;
        public int LegCount=>legs?.Length??0;
        public RaceRouteBakedLeg Leg(int index)=>JsonUtility.FromJson<RaceRouteBakedLeg>(JsonUtility.ToJson(legs[index]));
        internal RaceRouteBakedLeg ReadLeg(int index)=>legs[index];
        public Vector3[] Checkpoints=>legs.Select(l=>l.paths[0].gates.Last().position).ToArray();
        public void Initialize(string id,string revision,RoadNetworkAsset roads,RaceRouteBakedLeg[] content, Vector3[] starts=null, Quaternion[] rotations=null, Vector3 dimensions=default)
        {
            if(schema!=0)throw new InvalidOperationException("Published routes are immutable.");
            if(string.IsNullOrEmpty(id)||string.IsNullOrEmpty(revision)||roads==null||content==null||content.Length==0)throw new ArgumentException("Invalid route publication.");
            foreach(var leg in content)if(leg?.paths==null||leg.paths.Length==0||leg.paths.Any(p=>p?.gates==null||p.gates.Length==0))throw new ArgumentException("Empty route paths.");
            foreach (var leg in content)
                foreach (var path in leg.paths)
                {
                    if (!float.IsFinite(path.length) || path.length <= 0) throw new ArgumentException("Invalid path length.");
                    float previous = -1;
                    foreach (var gate in path.gates)
                    {
                        if (string.IsNullOrEmpty(gate.id) || !RacingLineSnapshot.Finite(gate.position) || !RacingLineSnapshot.Finite(gate.forward) || !RacingLineSnapshot.Finite(gate.up)
                            || Mathf.Abs(gate.forward.sqrMagnitude - 1) > .01f || Mathf.Abs(gate.up.sqrMagnitude - 1) > .01f || Mathf.Abs(Vector3.Dot(gate.forward, gate.up)) > .01f
                            || !float.IsFinite(gate.width) || !float.IsFinite(gate.height) || gate.width <= 0 || gate.height <= 0 || !float.IsFinite(gate.distance) || gate.distance < previous)
                            throw new ArgumentException("Invalid or unordered gate geometry.");
                        previous = gate.distance;
                    }
                }
            if (starts != null && (rotations == null || starts.Length != rotations.Length || dimensions.x <= 0 || dimensions.y <= 0 || dimensions.z <= 0)) throw new ArgumentException("Invalid grid envelope.");
            grid = starts == null ? Array.Empty<Vector3>() : (Vector3[])starts.Clone();
            gridRotations = rotations == null ? Array.Empty<Quaternion>() : (Quaternion[])rotations.Clone();
            entrantDimensions = dimensions;
            legs=content.Select(l=>JsonUtility.FromJson<RaceRouteBakedLeg>(JsonUtility.ToJson(l))).ToArray();routeId=id;fingerprint=revision;network=roads;roadFingerprint=roads.Fingerprint;schema=1;
        }
    }
    // Geometry-only fact adapter. MissionRuntime owns laps, deadlines, outcomes and settlement.
    public sealed class RaceRouteTraversal
    {
        private readonly RaceRoutePublication route;
        public int Path {get;private set;}=-1;
        public int Gate {get;private set;}
                public RaceRouteTraversal(RaceRoutePublication route,int path=-1,int gate=0)
        {this.route=route??throw new ArgumentNullException(nameof(route));Path=path;Gate=gate;}
        public bool Advance(int completed,Vector3 previous,Vector3 current,out float fraction)
        {
            fraction=0;
            var leg=route.ReadLeg(completed%route.LegCount);
            if(Path< -1||Path>=leg.paths.Length||Gate<0||leg.paths.All(p=>Gate>=p.gates.Length))throw new InvalidOperationException("Invalid restored route traversal.");
            float cursor=0;
            for(int iteration=0;iteration<4096;iteration++)
            {
                int selected=-1;float best=2;
                for(int p=0;p<leg.paths.Length;p++)
                {
                    if(Path>=0&&Path!=p || Gate>=leg.paths[p].gates.Length)continue;
                    if(leg.paths[p].gates[Gate].Cross(previous,current,out var hit)&&hit>=cursor&&hit<best){best=hit;selected=p;}
                }
                if(selected<0)return false;
                var chosen=leg.paths[selected].gates[Gate];
                bool common=Path<0&&leg.paths.All(p=>Gate<p.gates.Length && Vector3.Distance(p.gates[Gate].position,chosen.position)<.001f && Vector3.Dot(p.gates[Gate].forward,chosen.forward)>.999f);
                if(!common)Path=selected;
                Gate++;cursor=best+.000001f;
                if(Gate>=leg.paths[selected].gates.Length){Path=-1;Gate=0;fraction=best;return true;}
            }
            throw new InvalidOperationException("Route crossing budget exceeded.");
        }
        public float NormalizedLegProgress(int completed)
        {
            var leg=route.ReadLeg(completed%route.LegCount);var path=leg.paths[Math.Max(0,Path)];
            return Gate==0?0:Mathf.Clamp01(path.gates[Math.Min(Gate-1,path.gates.Length-1)].distance/Mathf.Max(1,path.length));
        }
    }
}
