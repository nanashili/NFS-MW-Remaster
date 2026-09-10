using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Renderer-only publication. It cannot spawn a vehicle controller, save participant or collider.</summary>
    [CreateAssetMenu(menuName = "NFS MW Remaster/Frontend/Showroom")]
    public sealed class MostWantedShowroomDefinition : ScriptableObject
    {
        [Serializable]
        public sealed class MeshPart
        {
            public string name = "";
            public Mesh mesh;
            public Material[] materials = Array.Empty<Material>();
            public Vector3 position;
            public Quaternion rotation = Quaternion.identity;
            public Vector3 scale = Vector3.one;
            public bool paintable;
        }

        public string vehicleId = "";
        public string displayName = "";
        public string sourcePrefab = "";
        public Bounds bounds;
        public MeshPart[] parts = Array.Empty<MeshPart>();
        public string environmentSource = "";
        public MeshPart[] environmentParts = Array.Empty<MeshPart>();
        public Vector3 vehiclePosition;
        public float vehicleYaw;
        public CameraShot entranceShot;
        [Min(.1f)] public float entranceSeconds = 3.2f;
        [Min(.1f)] public float transitionSeconds = .65f;
        public CameraShot[] cameraShots = Array.Empty<CameraShot>();

        [Serializable]
        public struct CameraShot
        {
            public MostWantedFrontendPage page;
            public Vector3 position, target;
            public float roll;
            [Range(15, 100)] public float fieldOfView;
        }

        public CameraShot ShotFor(MostWantedFrontendPage page)
        {
            foreach (var shot in cameraShots) if (shot.page == page) return shot;
            return new CameraShot { page = page, position = new Vector3(5.1f, 1.9f, 6.1f),
                target = new Vector3(0, .8f, 0), roll = 6, fieldOfView = 51 };
        }
    }
}
