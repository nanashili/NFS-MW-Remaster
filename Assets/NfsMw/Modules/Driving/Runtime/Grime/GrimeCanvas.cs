using System;
using System.Collections.Generic;
using UnityEngine;
namespace NfsMwRemaster.Driving
{
    [Serializable] public sealed class GrimeAnchor
    {
        public GrimeAnchorKind kind;
        public GrimeReceiver receiver;
        public string receiverId, meshRevision;
        public Vector3 position, normal = Vector3.up, tangent = Vector3.forward;
        public int triangle;
        public Vector3 barycentric;
        public RoadNetworkAsset network;
        public string laneId, roadRevision;
        public float station, lateral;
    }
    [Serializable] public sealed class GrimeStroke
    {
        public string id = Guid.NewGuid().ToString("N"), layerId, brushRevision;
        public GrimeBrush brush;
        public int seed = 123;
        public float width = 1, opacity = 1, rotation;
        public bool enabled = true;
        public List<GrimeAnchor> samples = new List<GrimeAnchor>();
    }
    [Serializable] public sealed class GrimeLayer
    {
        public string id = Guid.NewGuid().ToString("N"), name = "Dressing";
        public bool enabled = true, locked;
        [Range(0, 1)] public float opacity = 1;
    }
    [Serializable] public sealed class GrimeMask
    {
        public string id = Guid.NewGuid().ToString("N"), name = "Protected markings", layerId;
        public bool enabled = true, inclusion;
        public Vector3 origin, euler;
        public float depth = 2;
        public List<Vector2> polygon = new List<Vector2> { new Vector2(-2,-2), new Vector2(2,-2), new Vector2(2,2), new Vector2(-2,2) };
        public bool Contains(Vector3 point)
        {
            var p = Quaternion.Inverse(Quaternion.Euler(euler)) * (point - origin);
            if (Mathf.Abs(p.y) > depth / 2) return false;
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[i]; var b = polygon[j];
                if ((a.y > p.z) != (b.y > p.z) && p.x < (b.x-a.x) * (p.z-a.y) / (b.y-a.y) + a.x) inside = !inside;
            }
            return inside;
        }
    }
    [DisallowMultipleComponent]
    public sealed class GrimeCanvas : MonoBehaviour
    {
        public int schema = 1;
        public string id = Guid.NewGuid().ToString("N");
        public List<GrimeLayer> layers = new List<GrimeLayer> { new GrimeLayer() };
        public List<GrimeStroke> strokes = new List<GrimeStroke>();
        public List<GrimeMask> masks = new List<GrimeMask>();
        public LayerMask physicsLayers = ~0;
        public uint renderingLayers = uint.MaxValue;
        public string requiredCategory = "asphalt";
        public Material requiredMaterial;
        [Range(0, 180)] public float maximumSlope = 85;
        [Range(5, 200)] public float chunkSize = 32;
        [Range(100, 50000)] public int maximumStamps = 10000;
        [Range(1, 1024)] public int maximumDraws = 256;
        [HideInInspector] public GrimePublication published;
        [HideInInspector] public GameObject generatedRoot;
    }
}
