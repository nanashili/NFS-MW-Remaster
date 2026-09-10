using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Makes one imported Rockport chunk's road meshes the physical driving
    /// surface. The source map is an environment assembly, so its render
    /// hierarchy is intentionally left untouched and collision is generated
    /// when that additive chunk is loaded.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RockportMapSurface : MonoBehaviour
    {
        [SerializeField] private bool buildRoadCollision = true;
        [SerializeField] private bool combineReadableMeshes = true;

        private readonly List<GameObject> generatedObjects = new List<GameObject>();
        private readonly List<Mesh> generatedMeshes = new List<Mesh>();
        private bool collisionBuilt;

        private void Awake()
        {
            VehicleSurface surface = GetComponent<VehicleSurface>();
            if (surface == null) surface = gameObject.AddComponent<VehicleSurface>();
            surface.Configure("Rockport Asphalt", 1.02f, 0.92f);

            if (buildRoadCollision) BuildRoadCollision();
        }

        private void OnDestroy()
        {
            for (int i = 0; i < generatedObjects.Count; i++)
                if (generatedObjects[i] != null) Destroy(generatedObjects[i]);
            for (int i = 0; i < generatedMeshes.Count; i++)
                if (generatedMeshes[i] != null) Destroy(generatedMeshes[i]);
            generatedObjects.Clear();
            generatedMeshes.Clear();
        }

        private void BuildRoadCollision()
        {
            if (collisionBuilt) return;
            collisionBuilt = true;

            var sections = new Dictionary<Transform, List<MeshFilter>>();
            MeshFilter[] filters = GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                if (filter.sharedMesh == null || !IsRoadFilter(filter.transform)) continue;
                Transform section = FindRoadSection(filter.transform);
                if (section == null) continue;
                if (!sections.TryGetValue(section, out List<MeshFilter> sectionFilters))
                {
                    sectionFilters = new List<MeshFilter>();
                    sections.Add(section, sectionFilters);
                }
                sectionFilters.Add(filter);
            }

            int combinedSections = 0;
            int individualColliders = 0;
            foreach (KeyValuePair<Transform, List<MeshFilter>> section in sections)
            {
                if (combineReadableMeshes && TryCreateCombinedCollider(section.Key, section.Value))
                {
                    combinedSections++;
                    continue;
                }

                for (int i = 0; i < section.Value.Count; i++)
                    if (AttachIndividualCollider(section.Value[i])) individualColliders++;
            }

            Debug.Log("Rockport road collision ready: " + combinedSections + " combined sections, "
                + individualColliders + " individual mesh colliders.", this);
        }

        private bool TryCreateCombinedCollider(Transform section, List<MeshFilter> filters)
        {
            if (filters == null || filters.Count == 0) return false;

            long vertexCount = 0;
            for (int i = 0; i < filters.Count; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null || !mesh.isReadable) return false;
                vertexCount += mesh.vertexCount;
            }

            try
            {
                var combines = new CombineInstance[filters.Count];
                for (int i = 0; i < filters.Count; i++)
                {
                    combines[i] = new CombineInstance
                    {
                        mesh = filters[i].sharedMesh,
                        transform = transform.worldToLocalMatrix * filters[i].transform.localToWorldMatrix
                    };
                }

                var combined = new Mesh { name = section.name + " Rockport Road Collision" };
                if (vertexCount > 65535) combined.indexFormat = IndexFormat.UInt32;
                combined.CombineMeshes(combines, true, true, false);
                if (combined.vertexCount == 0 || combined.subMeshCount == 0 || combined.GetIndexCount(0) == 0)
                {
                    Destroy(combined);
                    return false;
                }

                GameObject colliderObject = new GameObject(section.name + " — Road Collision");
                colliderObject.transform.SetParent(transform, false);
                colliderObject.isStatic = true;
                MeshCollider collider = colliderObject.AddComponent<MeshCollider>();
                collider.sharedMesh = combined;
                collider.convex = false;
                generatedObjects.Add(colliderObject);
                generatedMeshes.Add(combined);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Rockport section collision combine failed for " + section.name
                    + "; using its source meshes. " + exception.Message, this);
                return false;
            }
        }

        private static bool AttachIndividualCollider(MeshFilter filter)
        {
            if (filter == null || filter.sharedMesh == null) return false;
            MeshCollider collider = filter.GetComponent<MeshCollider>();
            if (collider == null) collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = false;
            return true;
        }

        private Transform FindRoadSection(Transform candidate)
        {
            for (Transform current = candidate; current != null && current != transform; current = current.parent)
                if (IsRoadNodeName(current.name)) return current;
            return null;
        }

        private bool IsRoadFilter(Transform candidate)
        {
            for (Transform current = candidate; current != null && current != transform; current = current.parent)
                if (IsRoadNodeName(current.name)) return true;
            return false;
        }

        private static bool IsRoadNodeName(string name)
        {
            if (name.StartsWith("Road", StringComparison.OrdinalIgnoreCase)) return true;

            int roadIndex = name.IndexOf("_Road", StringComparison.OrdinalIgnoreCase);
            if (roadIndex < 0) return false;
            int suffixIndex = roadIndex + "_Road".Length;
            return suffixIndex == name.Length || name[suffixIndex] == '_' || char.IsUpper(name[suffixIndex]);
        }

    }
}
