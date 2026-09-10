using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    [CustomEditor(typeof(RoadProfile))]
    public sealed class RoadProfileInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck(); DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();
            if (GUILayout.Button("Add driving lane")) { AddBand(RoadBandKind.Driving, 3.5f); changed = true; }
            if (GUILayout.Button("Add shoulder")) { AddBand(RoadBandKind.Shoulder, 1); changed = true; }
            if (GUILayout.Button("Assign missing / duplicate band identities"))
            {
                var profile = (RoadProfile)target; Undo.RecordObject(profile, "Assign band identities");
                var ids = new HashSet<RoadId>();
                foreach (var band in profile.bands)
                    if (band != null && (!band.id.IsValid || !ids.Add(band.id))) { band.id = RoadId.New(); ids.Add(band.id); }
                EditorUtility.SetDirty(profile); changed = true;
            }
            if (changed)
                foreach (var road in UnityEngine.Object.FindObjectsByType<RoadAuthoring>(FindObjectsInactive.Include))
                    if (road.Profile == target) RoadPreview.Invalidate(road);
            EditorGUILayout.HelpBox("Bands run left to right. After changing their order or number, use Apply / reconcile profile on affected roads. Existing band identities are retained.", MessageType.Info);
        }
        private void AddBand(RoadBandKind kind, float width)
        {
            var profile = (RoadProfile)target; Undo.RecordObject(profile, "Add road band");
            var bands = new List<RoadBand>(profile.bands) { new RoadBand { id = RoadId.New(), label = kind.ToString(), kind = kind,
                width = width, direction = kind == RoadBandKind.Driving ? RoadTravelDirection.Forward : RoadTravelDirection.None } };
            profile.bands = bands.ToArray(); EditorUtility.SetDirty(profile);
        }
        public static RoadProfile GetDefaultProfile()
        {
            const string folder = "Assets/NfsMw/Modules/Driving/Data/Roads";
            EnsureFolder(folder);
            const string path = folder + "/TwoLane.asset";
            var existing = AssetDatabase.LoadAssetAtPath<RoadProfile>(path); if (existing != null) return existing;
            var material = AssetDatabase.LoadAssetAtPath<Material>(folder + "/Asphalt.mat");
            if (material == null)
            {
                material = Rendering.HdrpMaterialDefaults.Road();material.name="Road asphalt";
                AssetDatabase.CreateAsset(material, folder + "/Asphalt.mat");
            }
            SensorySurfaceProfile surface = null;
            foreach (string guid in AssetDatabase.FindAssets("t:SensorySurfaceProfile"))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<SensorySurfaceProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate.surface == SensorySurface.AsphaltDry) { surface = candidate; break; }
            }
            if (surface == null)
            {
                surface = CreateInstance<SensorySurfaceProfile>(); surface.name = "Dry asphalt";
                AssetDatabase.CreateAsset(surface, folder + "/DryAsphalt.asset");
            }
            var profile = RoadProfile.CreateTwoLane();
            foreach (var band in profile.bands) { band.material = material; band.surface = surface; }
            AssetDatabase.CreateAsset(profile, path); AssetDatabase.SaveAssets(); return AssetDatabase.LoadAssetAtPath<RoadProfile>(path);
        }
        public static RoadProfile GetLocalStreetProfile()
        {
            const string folder = "Assets/NfsMw/Modules/Driving/Data/Roads";
            const string path = folder + "/LocalStreet.asset";
            var existing = AssetDatabase.LoadAssetAtPath<RoadProfile>(path); if (existing != null) return existing;
            var asphalt = GetDefaultProfile().bands[0];
            var material = AssetDatabase.LoadAssetAtPath<Material>(folder + "/Concrete.mat");
            if (material == null)
            {
                material = new Material(asphalt.material) { name = "Road concrete" };
                material.SetColor("_BaseColor", new Color(0.48f, 0.47f, 0.44f));
                AssetDatabase.CreateAsset(material, folder + "/Concrete.mat");
            }
            var surface = AssetDatabase.LoadAssetAtPath<SensorySurfaceProfile>(folder + "/Concrete.asset");
            if (surface == null)
            {
                surface = CreateInstance<SensorySurfaceProfile>(); surface.name = "Road concrete"; surface.surface = SensorySurface.Concrete;
                AssetDatabase.CreateAsset(surface, folder + "/Concrete.asset");
            }
            var profile = RoadProfile.CreateLocalStreet();
            foreach (var band in profile.bands)
            {
                bool pavement = band.kind == RoadBandKind.Driving;
                band.material = pavement ? asphalt.material : material; band.surface = pavement ? asphalt.surface : surface;
            }
            AssetDatabase.CreateAsset(profile, path); AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<RoadProfile>(path);
        }
        internal static void EnsureFolder(string path)
        {
            var parts = path.Split('/'); string current = parts[0];
            if (current != "Assets") throw new ArgumentException("Road assets must be stored below Assets.");
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
