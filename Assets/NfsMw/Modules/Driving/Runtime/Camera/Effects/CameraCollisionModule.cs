using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving
{
    /// <summary>Non-allocating near-plane sphere sweep, followed by overlap recovery.</summary>
    public sealed class CameraCollisionModule : System.IDisposable
    {
        private readonly RaycastHit[] hits = new RaycastHit[48];
        private readonly Collider[] overlaps = new Collider[48];
        private Collider[] own = System.Array.Empty<Collider>();
        private PhysicsScene scene;
        private SphereCollider probe;
        private float previousDistance = -1;
        public void Bind(Transform vehicle)
        {
            own = vehicle ? vehicle.GetComponentsInChildren<Collider>(true) : System.Array.Empty<Collider>();
            scene = vehicle ? vehicle.gameObject.scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
            previousDistance = -1;
        }
        public void Reset() => previousDistance = -1;
        private bool Ignore(Collider collider)
        {
            if (!collider || collider == probe) return true;
            for (int i = 0; i < own.Length; i++) if (collider == own[i]) return true;
            return false;
        }
        public Vector3 Resolve(Vector3 origin, Vector3 desired, VehicleCameraProfile.Collision p, float nearPlane, float verticalFov, float aspect, float dt)
        {
            if (p.mask.value == 0 || !scene.IsValid()) return desired;
            float halfHeight = nearPlane * Mathf.Tan(verticalFov * Mathf.Deg2Rad * .5f);
            float radius = Mathf.Max(p.radius, Mathf.Sqrt(halfHeight * halfHeight * (1 + aspect * aspect))) + p.padding;
            Vector3 delta = desired - origin; float distance = delta.magnitude;
            if (distance < .0001f) return Recover(desired, radius, p.mask);
            Vector3 direction = delta / distance;
            int count = scene.SphereCast(origin, radius, direction, hits, distance, p.mask, QueryTriggerInteraction.Ignore);
            float safe = distance;
            for (int i = 0; i < count; i++) if (!Ignore(hits[i].collider)) safe = Mathf.Min(safe, Mathf.Max(0, hits[i].distance - .01f));
            if (count == hits.Length) safe = 0;
            float resolved = previousDistance < 0 || safe < previousDistance ? safe : CameraResponse.Smooth(previousDistance, safe, p.recoveryResponse, dt);
            previousDistance = resolved;
            return Recover(origin + direction * resolved, radius, p.mask);
        }
        private Vector3 Recover(Vector3 position, float radius, int mask)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                int count = scene.OverlapSphere(position, radius, overlaps, mask, QueryTriggerInteraction.Ignore);
                bool moved = false;
                for (int i = 0; i < count; i++)
                {
                    Collider other = overlaps[i]; if (Ignore(other)) continue;
                    if (!probe)
                    {
                        var go = new GameObject("Camera collision probe") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
                        go.transform.position = new Vector3(0, -100000, 0);
                        probe = go.AddComponent<SphereCollider>(); probe.isTrigger = true;
                    }
                    probe.radius = radius;
                    if (Physics.ComputePenetration(probe, position, Quaternion.identity, other, other.transform.position, other.transform.rotation, out Vector3 direction, out float depth))
                    { position += direction * (depth + .001f); moved = true; }
                }
                if (!moved) break;
            }
            return position;
        }
        public void Dispose()
        {
            if (!probe) return;
            if (Application.isPlaying) Object.Destroy(probe.gameObject); else Object.DestroyImmediate(probe.gameObject);
            probe = null;
        }
    }
}
