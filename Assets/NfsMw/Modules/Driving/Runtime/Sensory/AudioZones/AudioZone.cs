using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Authored acoustic volume. It owns shape membership only; mix arbitration,
    /// ambience voice admission and listener policy belong to AudioZoneWorld.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioZone : MonoBehaviour
    {
        [SerializeField] private string stableId = string.Empty;
        [SerializeField] private AudioZoneProfile profile;
        [SerializeField] private AudioZoneShape shape = AudioZoneShape.Box;
        [SerializeField] private Vector3 center;
        [SerializeField] private Vector3 size = new Vector3(24, 8, 40);
        [SerializeField, Min(0.05f)] private float radius = 6;
        [SerializeField, Min(0.1f)] private float height = 12;
        [SerializeField] private Mesh convexMesh;
        [SerializeField] private Collider convexCollider;
        [SerializeField] private bool enabledForRuntime = true;
        [SerializeField] private bool allowOutsideBlend = true;
        [SerializeField] private string authoringNotes = string.Empty;

        public string StableId => stableId;
        public AudioZoneProfile Profile => profile;
        public AudioZoneShape Shape => shape;
        public Vector3 Center => center;
        public Vector3 Size => size;
        public float Radius => radius;
        public float Height => height;
        public Mesh ConvexMesh => convexMesh;
        public Collider ConvexCollider => convexCollider;
        public bool EnabledForRuntime => enabledForRuntime;
        public bool AllowOutsideBlend => allowOutsideBlend;
        public string AuthoringNotes => authoringNotes;

        public void SetStableId(string value) => stableId = value ?? string.Empty;
        public void SetProfile(AudioZoneProfile value) => profile = value;
        public void SetShape(AudioZoneShape value) => shape = value;
        public void SetCenter(Vector3 value) => center = new Vector3(
            SensoryMath.Finite(value.x), SensoryMath.Finite(value.y), SensoryMath.Finite(value.z));
        public void SetSize(Vector3 value) => size = new Vector3(
            Mathf.Max(0.05f, SensoryMath.Finite(value.x)),
            Mathf.Max(0.05f, SensoryMath.Finite(value.y)),
            Mathf.Max(0.05f, SensoryMath.Finite(value.z)));
        public void SetRadius(float value) => radius = Mathf.Max(0.05f, SensoryMath.Finite(value));
        public void SetHeight(float value) => height = Mathf.Max(radius * 2, SensoryMath.Finite(value));
        public void SetEnabledForRuntime(bool value) => enabledForRuntime = value;

        public Bounds WorldBounds
        {
            get
            {
                switch (shape)
                {
                    case AudioZoneShape.Sphere:
                        return new Bounds(transform.TransformPoint(center), Vector3.one * (radius * 2 * MinimumScale()));
                    case AudioZoneShape.Capsule:
                        return CapsuleBounds();
                    case AudioZoneShape.Convex:
                        if (convexCollider != null) return convexCollider.bounds;
                        if (convexMesh != null) return TransformBounds(convexMesh.bounds);
                        break;
                }
                return BoxBounds(size);
            }
        }

        public Bounds BlendBounds
        {
            get
            {
                var bounds = WorldBounds;
                float expansion = profile != null ? Mathf.Max(0, profile.BlendDistance + profile.Hysteresis) : 0;
                bounds.Expand(expansion * 2);
                return bounds;
            }
        }

        private void Reset()
        {
            if (string.IsNullOrEmpty(stableId)) stableId = AudioZoneStableId.Create("audio.zone", name);
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(stableId)) stableId = AudioZoneStableId.Create("audio.zone", name);
            size.x = Mathf.Max(0.05f, SensoryMath.Finite(size.x));
            size.y = Mathf.Max(0.05f, SensoryMath.Finite(size.y));
            size.z = Mathf.Max(0.05f, SensoryMath.Finite(size.z));
            radius = Mathf.Max(0.05f, SensoryMath.Finite(radius));
            height = Mathf.Max(radius * 2, SensoryMath.Finite(height));
        }

        public bool Contains(Vector3 worldPosition)
        {
            Evaluate(worldPosition, false, out bool inside, out _, out _);
            return inside;
        }

        public float Weight(Vector3 worldPosition, bool wasActive = false)
        {
            Evaluate(worldPosition, wasActive, out _, out float weight, out _);
            return weight;
        }

        public bool TryEvaluate(Vector3 worldPosition, bool wasActive, out float weight, out bool insideCore, out bool hysteresisHeld)
        {
            return Evaluate(worldPosition, wasActive, out insideCore, out weight, out hysteresisHeld);
        }

        private bool Evaluate(Vector3 worldPosition, bool wasActive, out bool insideCore, out float weight, out bool hysteresisHeld)
        {
            insideCore = false;
            weight = 0;
            hysteresisHeld = false;
            if (!enabledForRuntime || profile == null || !IsFinite(worldPosition)) return false;

            Vector3 local = transform.InverseTransformPoint(worldPosition);
            float outsideDistance;
            switch (shape)
            {
                case AudioZoneShape.Sphere:
                    outsideDistance = SphereDistance(local, out insideCore);
                    break;
                case AudioZoneShape.Capsule:
                    outsideDistance = CapsuleDistance(local, out insideCore);
                    break;
                case AudioZoneShape.Convex:
                    outsideDistance = ConvexDistance(worldPosition, out insideCore);
                    break;
                default:
                    outsideDistance = BoxDistance(local, out insideCore);
                    break;
            }

            if (insideCore) { hysteresisHeld = false; return SetWeight(1, out weight); }
            if (!allowOutsideBlend) return false;
            float boundary = Mathf.Max(0, profile.BlendDistance) + (wasActive ? Mathf.Max(0, profile.Hysteresis) : 0);
            if (boundary <= 0 || outsideDistance >= boundary) return false;
            hysteresisHeld = wasActive && outsideDistance > profile.BlendDistance;
            float normalized = 1 - outsideDistance / boundary;
            bool result = SetWeight(profile.Curve(normalized), out weight);
            if (!result) hysteresisHeld = false;
            return result;
        }

        private static bool SetWeight(float value, out float weight)
        {
            weight = Mathf.Clamp01(SensoryMath.Finite(value));
            return weight > 0;
        }

        private float BoxDistance(Vector3 local, out bool inside)
        {
            Vector3 half = Vector3.Max(size * 0.5f, Vector3.one * 0.025f);
            Vector3 delta = new Vector3(Mathf.Abs(local.x - center.x), Mathf.Abs(local.y - center.y), Mathf.Abs(local.z - center.z)) - half;
            inside = delta.x <= 0 && delta.y <= 0 && delta.z <= 0;
            Vector3 outside = new Vector3(Mathf.Max(0, delta.x), Mathf.Max(0, delta.y), Mathf.Max(0, delta.z));
            float distance = outside.magnitude;
            if (inside) distance = Mathf.Min(half.x - Mathf.Abs(local.x - center.x), Mathf.Min(half.y - Mathf.Abs(local.y - center.y), half.z - Mathf.Abs(local.z - center.z)));
            return Mathf.Max(0, distance * MinimumScale());
        }

        private float SphereDistance(Vector3 local, out bool inside)
        {
            float distance = Vector3.Distance(local, center);
            inside = distance <= radius;
            return Mathf.Max(0, distance - radius) * MinimumScale();
        }

        private float CapsuleDistance(Vector3 local, out bool inside)
        {
            float halfLine = Mathf.Max(0, height * 0.5f - radius);
            float y = Mathf.Clamp(local.y - center.y, -halfLine, halfLine);
            Vector3 nearest = new Vector3(center.x, center.y + y, center.z);
            float distance = Vector3.Distance(local, nearest);
            inside = distance <= radius;
            return Mathf.Max(0, distance - radius) * MinimumScale();
        }

        private float ConvexDistance(Vector3 worldPosition, out bool inside)
        {
            if (convexCollider == null)
            {
                var bounds = WorldBounds;
                inside = bounds.Contains(worldPosition);
                return inside ? 0 : DistanceToBounds(bounds, worldPosition);
            }
            inside = convexCollider.bounds.Contains(worldPosition);
            Vector3 closest = convexCollider.ClosestPoint(worldPosition);
            return inside ? 0 : Vector3.Distance(worldPosition, closest);
        }

        private Bounds BoxBounds(Vector3 localSize)
        {
            Vector3 c = transform.TransformPoint(center);
            Vector3 x = transform.TransformVector(Vector3.right * localSize.x * 0.5f);
            Vector3 y = transform.TransformVector(Vector3.up * localSize.y * 0.5f);
            Vector3 z = transform.TransformVector(Vector3.forward * localSize.z * 0.5f);
            var bounds = new Bounds(c, Vector3.zero);
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sy in new[] { -1f, 1f })
                    foreach (var sz in new[] { -1f, 1f }) bounds.Encapsulate(c + x * sx + y * sy + z * sz);
            return bounds;
        }

        private Bounds CapsuleBounds()
        {
            float halfLine = Mathf.Max(0, height * 0.5f - radius);
            var bounds = new Bounds(transform.TransformPoint(center), Vector3.zero);
            Vector3 y = transform.TransformVector(Vector3.up * halfLine);
            Vector3 x = transform.TransformVector(Vector3.right * radius);
            Vector3 z = transform.TransformVector(Vector3.forward * radius);
            foreach (var sy in new[] { -1f, 1f })
            {
                Vector3 point = bounds.center + y * sy;
                bounds.Encapsulate(point + x); bounds.Encapsulate(point - x); bounds.Encapsulate(point + z); bounds.Encapsulate(point - z);
            }
            return bounds;
        }

        private Bounds TransformBounds(Bounds localBounds)
        {
            var bounds = new Bounds(transform.TransformPoint(localBounds.center), Vector3.zero);
            Vector3 e = localBounds.extents;
            Vector3 x = transform.TransformVector(Vector3.right * e.x);
            Vector3 y = transform.TransformVector(Vector3.up * e.y);
            Vector3 z = transform.TransformVector(Vector3.forward * e.z);
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sy in new[] { -1f, 1f })
                    foreach (var sz in new[] { -1f, 1f }) bounds.Encapsulate(bounds.center + x * sx + y * sy + z * sz);
            return bounds;
        }

        private float MinimumScale()
        {
            Vector3 scale = transform.lossyScale;
            return Mathf.Max(0.0001f, Mathf.Min(Mathf.Abs(scale.x), Mathf.Min(Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
        }

        private static float DistanceToBounds(Bounds bounds, Vector3 point)
        {
            Vector3 closest = bounds.ClosestPoint(point);
            return Vector3.Distance(point, closest);
        }

        private static bool IsFinite(Vector3 value)
            => SensoryMath.IsFinite(value.x) && SensoryMath.IsFinite(value.y) && SensoryMath.IsFinite(value.z);
    }
}
