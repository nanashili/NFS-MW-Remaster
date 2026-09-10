#if UNITY_EDITOR
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public static partial class DrivingDemoBuilder
    {
        private static PoliceRoadHazard CreatePoliceRoadHazard(Transform parent, VehiclePursuitDirector director,
            Vector3 point, Quaternion heading, PoliceRoadHazardKind kind, Material stripe, Material warning)
        {
            var root = new GameObject("Authored Police " + kind); root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(point, heading);
            var site = root.AddComponent<PoliceRoadHazard>();
            var content = new GameObject("Pooled Physical Content"); content.transform.SetParent(root.transform, false);
            content.SetActive(false);
            if (kind == PoliceRoadHazardKind.Roadblock)
            {
                // Offset barriers leave a driveable escape lane. Replace meshes while preserving physical clearances.
                foreach (float x in new[] { -5f, -1f })
                {
                    var barrier = CreateVisualBox("Physical Roadblock", content.transform, new Vector3(x, 0.65f, 0), new Vector3(3.5f, 1.3f, 1f), stripe);
                    barrier.AddComponent<BoxCollider>();
                    CreateVisualBox("Reflective Warning", content.transform, new Vector3(x, 1.4f, 0), new Vector3(3.5f, 0.15f, 0.5f), warning);
                }
            }
            else
            {
                var strip = new GameObject("Spike Contact Volume"); strip.transform.SetParent(content.transform, false);
                var collider = strip.AddComponent<BoxCollider>(); collider.isTrigger = true;
                collider.center = Vector3.up * 0.5f; collider.size = new Vector3(7f, 1f, 0.8f);
                strip.AddComponent<PoliceSpikeContact>();
                for (int i = -6; i <= 6; i++)
                    CreateVisualBox("Spike Placeholder", content.transform, new Vector3(i * 0.5f, 0.07f, 0), new Vector3(0.25f, 0.14f, 0.6f), warning);
            }
            site.Configure(director, kind, content, kind == PoliceRoadHazardKind.Roadblock ? 3 : 5);
            return site;
        }
    }
}
#endif
