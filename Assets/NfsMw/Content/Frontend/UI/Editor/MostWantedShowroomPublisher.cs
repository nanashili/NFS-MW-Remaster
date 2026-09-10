#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public static class MostWantedShowroomPublisher
    {
        public const string AssetPath = "Assets/NfsMw/Content/Frontend/UI/Data/WarehouseShowroom.asset";
        public const string VehiclePath = "Assets/NfsMw/Content/Vehicles/Street/BMW/M3 E42/Framework/Vehicle.prefab";
        public const string WarehousePath = "Assets/NfsMw/Content/Frontend/Models/FrontendWarehouseRemake/Warehouse.gltf";

        [MenuItem("NFS MW Remaster/Frontend/Publish Warehouse Showroom")]
        public static void Publish()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before publishing the showroom.");
            var vehicle = Required(VehiclePath);
            var warehouse = Required(WarehousePath);
            var vehicleParts = Capture(vehicle, out Bounds bounds);
            var environmentParts = Capture(warehouse, out _);
            PublishGlass(vehicleParts);
            var definition = AssetDatabase.LoadAssetAtPath<MostWantedShowroomDefinition>(AssetPath);
            bool created = definition == null;
            if (created) definition = ScriptableObject.CreateInstance<MostWantedShowroomDefinition>();
            definition.vehicleId = "bmw_m3_frontend";
            definition.displayName = "BMW M3";
            definition.sourcePrefab = VehiclePath;
            definition.environmentSource = WarehousePath;
            definition.parts = vehicleParts;
            definition.bounds = bounds;
            definition.environmentParts = environmentParts;
            if (created || definition.cameraShots.Length == 0) SetReferenceComposition(definition);
            if (created) AssetDatabase.CreateAsset(definition, AssetPath);
            EditorUtility.SetDirty(definition);
            var settings = AssetDatabase.LoadAssetAtPath<GameFlowSettings>(DrivingGameFlowBuilder.SettingsPath);
            if (settings == null) throw new FileNotFoundException("Game flow settings are missing.");
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("showroom").objectReferenceValue = definition;
            serialized.FindProperty("requireTitleConfirmation").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"Published warehouse showroom: {vehicleParts.Length} vehicle parts, {environmentParts.Length} environment parts, {definition.cameraShots.Length} camera shots.");
        }

        private static GameObject Required(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path)
            ?? throw new FileNotFoundException("Showroom source is missing.", path);

        private static void PublishGlass(MostWantedShowroomDefinition.MeshPart[] parts)
        {
            const string path = "Assets/NfsMw/Content/Frontend/UI/Data/ShowroomGlass.mat";
            var glass = AssetDatabase.LoadAssetAtPath<Material>(path);
            foreach (var part in parts)
                for (int i = 0; i < part.materials.Length; i++)
                {
                    var original = part.materials[i];
                    if (original.name != "Car windshield glass") continue;
                    if (glass == null)
                    {
                        glass = new Material(original) { name = "Showroom glass" };
                        glass.SetFloat("_SurfaceType", 1);
                        glass.SetFloat("_BlendMode", 0);
                        glass.SetFloat("_ZWrite", 0);
                        glass.SetFloat("_Metallic", 0);
                        glass.SetFloat("_Smoothness", .92f);
                        glass.SetColor("_BaseColor", new Color(.12f,.15f,.14f,.24f));
                        glass.renderQueue = 3000;
                        UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(glass);
                        AssetDatabase.CreateAsset(glass, path);
                    }
                    part.materials[i] = glass;
                }
        }

        public static MostWantedShowroomDefinition.MeshPart[] Capture(GameObject source, out Bounds bounds)
        {
            var paintTargets = new HashSet<Renderer>();
            foreach (var slot in source.GetComponentsInChildren<VehicleCustomizationVisualSlot>(true))
            {
                if (slot.Category != VehicleCustomizationCategory.Paint) continue;
                var targets = new SerializedObject(slot).FindProperty("targetRenderers");
                for (int i = 0; i < targets.arraySize; i++)
                    if (targets.GetArrayElementAtIndex(i).objectReferenceValue is Renderer renderer) paintTargets.Add(renderer);
            }
            var parts = new List<MostWantedShowroomDefinition.MeshPart>();
            bounds = default;
            bool hasBounds = false;
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled || !Active(filter.transform, source.transform)) continue;
                if (parts.Count >= 1024) throw new InvalidOperationException("Showroom publication exceeds 1024 mesh parts.");
                var materials = renderer.sharedMaterials;
                if (materials.Length == 0 || materials.Any(material => material == null || !EditorUtility.IsPersistent(material)))
                    throw new InvalidOperationException("Showroom part needs persistent materials: " + filter.name);
                var matrix = source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                parts.Add(new MostWantedShowroomDefinition.MeshPart { name = filter.name,
                    mesh = filter.sharedMesh, materials = materials, position = matrix.GetColumn(3),
                    rotation = matrix.rotation, scale = matrix.lossyScale, paintable = paintTargets.Contains(renderer) });
                Bounds local = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (!hasBounds) { bounds = new Bounds(point, Vector3.zero); hasBounds = true; }
                    else bounds.Encapsulate(point);
                }
            }
            if (parts.Count == 0) throw new InvalidOperationException("No active showroom meshes in " + source.name);
            return parts.ToArray();
        }

        private static bool Active(Transform node, Transform root)
        {
            while (node != null) { if (!node.gameObject.activeSelf) return false; if (node == root) return true; node = node.parent; }
            return false;
        }

        public static MostWantedShowroomDefinition.CameraShot[] ReferenceShots()
        {
            var shots = new List<MostWantedShowroomDefinition.CameraShot>();
            void Add(MostWantedFrontendPage page, Vector3 position, Vector3 target, float roll, float fov) =>
                shots.Add(new MostWantedShowroomDefinition.CameraShot { page = page, position = position, target = target, roll = roll, fieldOfView = fov });
            Add(MostWantedFrontendPage.Title, new Vector3(-4.6f, 1.5f, 5.3f), new Vector3(0, 1.4f, 0), -4, 48);
            Add(MostWantedFrontendPage.MainMenu, new Vector3(4.8f, 1.65f, 6.1f), new Vector3(-1.85f, .38f, .1f), 8, 40);
            Add(MostWantedFrontendPage.Career, new Vector3(5.9f, .95f, -.4f), new Vector3(-.6f, -.35f, -.7f), 26, 43);
            Add(MostWantedFrontendPage.Options, new Vector3(-4.6f, 1.25f, -5.5f), new Vector3(-1.3f, .85f, .3f), -20, 39);
            foreach (var page in new[] { MostWantedFrontendPage.Audio, MostWantedFrontendPage.Gameplay, MostWantedFrontendPage.Player, MostWantedFrontendPage.Music })
                Add(page, new Vector3(1.9f, .72f, -2.1f), new Vector3(0, .30f, -2.05f), 0, 56);
            foreach (var page in new[] { MostWantedFrontendPage.Video, MostWantedFrontendPage.AdvancedVideo, MostWantedFrontendPage.Controls, MostWantedFrontendPage.Lan, MostWantedFrontendPage.Online })
                Add(page, new Vector3(1.7f, 1.2f, -4.2f), new Vector3(3.3f, 1.3f, 2.6f), 0, 64);
            Add(MostWantedFrontendPage.Credits, new Vector3(0, 3, 18), new Vector3(0, 1, 0), -5, 58);
            foreach (var page in new[] { MostWantedFrontendPage.Safehouse, MostWantedFrontendPage.Blacklist })
                Add(page, new Vector3(5, 1.3f, -5.5f), new Vector3(-.4f, 1, 0), -11, 51);
            return shots.ToArray();
        }

        public static void SetReferenceComposition(MostWantedShowroomDefinition definition)
        {
            definition.vehicleYaw = 90;
            definition.cameraShots = ReferenceShots();
            definition.entranceShot = new MostWantedShowroomDefinition.CameraShot { page=MostWantedFrontendPage.MainMenu,
                position=new Vector3(-1.5f,3.4f,24),target=new Vector3(-1.2f,1.3f,0),roll=-5,fieldOfView=58 };
            definition.entranceSeconds = 2.8f;
            definition.transitionSeconds = .65f;
        }
    }
}
#endif
