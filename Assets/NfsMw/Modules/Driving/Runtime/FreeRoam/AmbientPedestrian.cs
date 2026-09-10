using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum AmbientActivity { Walking, LookingAround, Fleeing, CatchingBreath }

    public sealed class AmbientRoutine
    {
        private float remaining;
        public AmbientActivity Activity { get; private set; }
        public void Pause(float seconds)
        {
            if (Activity == AmbientActivity.Fleeing || Activity == AmbientActivity.CatchingBreath) return;
            Activity = AmbientActivity.LookingAround; remaining = Mathf.Max(0, seconds);
        }
        public void Advance(float dt, bool danger)
        {
            if (dt <= 0) return;
            if (danger) { Activity = AmbientActivity.Fleeing; remaining = 4; return; }
            remaining = Mathf.Max(0, remaining - dt);
            if (remaining > 0) return;
            if (Activity == AmbientActivity.Fleeing) { Activity = AmbientActivity.CatchingBreath; remaining = 3; }
            else Activity = AmbientActivity.Walking;
        }
    }

    // Routes are authored on sidewalks, not inferred from road nodes. Fleeing follows
    // those same safe edges instead of taking a straight line through a building.
    public sealed class AmbientPedestrian : MonoBehaviour
    {
        [SerializeField] private Vector3[] route = Array.Empty<Vector3>();
        [SerializeField] private float walkingSpeed = 1.3f;
        [SerializeField] private int seed = 1;
        private readonly Collider[] nearby = new Collider[24];
        private readonly RaycastHit[] obstacles = new RaycastHit[12];
        private readonly AmbientRoutine routine = new AmbientRoutine();
        private System.Random random;
        private int waypoint;
        private int direction = 1;
        private float senseAt;
        private bool danger;
        public AmbientActivity Activity => routine.Activity;
        public void Configure(Vector3[] points, int identity)
        { route = points == null ? Array.Empty<Vector3>() : (Vector3[])points.Clone(); seed = identity; }
        private void Start()
        {
            random = new System.Random(seed);
            walkingSpeed *= Mathf.Lerp(0.85f, 1.2f, (float)random.NextDouble());
            if (route.Length < 2) { enabled = false; return; }
            waypoint = 1;
            routine.Pause((float)random.NextDouble() * 4);
        }
        private void Update()
        {
            if (Time.deltaTime <= 0 || route.Length < 2) return;
            if (Time.time >= senseAt)
            {
                senseAt = Time.time + 0.2f;
                danger = SenseDanger(out Vector3 threat);
                if (danger && routine.Activity != AmbientActivity.Fleeing
                    && Vector3.Dot(route[waypoint] - transform.position, threat - transform.position) > 0)
                { waypoint = (waypoint - direction + route.Length) % route.Length; direction = -direction; }
            }
            routine.Advance(Time.deltaTime, danger);
            if (routine.Activity == AmbientActivity.LookingAround || routine.Activity == AmbientActivity.CatchingBreath) return;
            Vector3 delta = route[waypoint] - transform.position;
            float distance = delta.magnitude;
            if (distance < 0.1f)
            {
                waypoint = (waypoint + direction + route.Length) % route.Length;
                if (random.NextDouble() < 0.35) routine.Pause(2 + (float)random.NextDouble() * 5);
                return;
            }
            float step = Mathf.Min(distance, walkingSpeed * (danger || routine.Activity == AmbientActivity.Fleeing ? 2.7f : 1) * Time.deltaTime);
            int count = Physics.SphereCastNonAlloc(transform.position + Vector3.up, 0.28f, delta.normalized,
                obstacles, step + 0.4f, ~0, QueryTriggerInteraction.Ignore);
            if (count > 0) return;
            transform.position += delta.normalized * step;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(delta), 240 * Time.deltaTime);
        }
        private bool SenseDanger(out Vector3 threat)
        {
            threat = transform.position;
            int count = Physics.OverlapSphereNonAlloc(transform.position, 18, nearby, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = nearby[i].attachedRigidbody;
                if (body == null || body.linearVelocity.sqrMagnitude < 25) continue;
                Vector3 offset = transform.position - body.position;
                float closing = Vector3.Dot(body.linearVelocity, offset.normalized);
                if (closing < 2 || offset.magnitude / closing > 2.5f) continue;
                // Do not react to unseen traffic behind warehouse walls.
                if (Physics.Linecast(transform.position + Vector3.up, body.position + Vector3.up, out RaycastHit hit,
                    ~0, QueryTriggerInteraction.Ignore) && hit.rigidbody != body) continue;
                threat = body.position; return true;
            }
            return false;
        }
    }
}
