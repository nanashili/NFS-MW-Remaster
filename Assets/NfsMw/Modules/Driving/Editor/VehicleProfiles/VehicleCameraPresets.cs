using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public static class VehicleCameraPresets
    {
        public const string Folder = "Assets/NfsMw/Modules/Driving/Resources/VehicleCamera";
        [MenuItem("Racing Tools/Vehicles/Create Camera Presets")]
        public static void Ensure()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/NfsMw/Modules/Driving/Resources", "VehicleCamera");
            foreach (VehicleCameraStyle style in System.Enum.GetValues(typeof(VehicleCameraStyle)))
            {
                string path = Folder + "/" + style + ".asset";
                if (AssetDatabase.LoadAssetAtPath<VehicleCameraProfile>(path)) continue;
                var profile = ScriptableObject.CreateInstance<VehicleCameraProfile>(); Configure(profile, style);
                AssetDatabase.CreateAsset(profile, path);
            }
            AssetDatabase.SaveAssets();
        }
        public static void Configure(VehicleCameraProfile p, VehicleCameraStyle style)
        {
            p.style = style;
            switch (style)
            {
                case VehicleCameraStyle.Cinematic:
                    p.chase.follow.distance = 7.5f; p.chase.follow.yawResponse = 6;
                    p.chase.speed.maximumExtraDistance = 1; p.chase.shake.maximumStrength = .35f;
                    p.chase.acceleration.forwardLag = .4f; break;
                case VehicleCameraStyle.Arcade:
                    p.chase.speed.maximumExtraDistance = 1.7f; p.chase.acceleration.maximumAdditionalFov = 5;
                    p.chase.nitrous.fovBoost = 8; p.chase.follow.yawResponse = 16; break;
                case VehicleCameraStyle.Realistic:
                    p.chase.speed.maximumExtraDistance = .5f; p.chase.acceleration.maximumAdditionalFov = 1.5f;
                    p.chase.nitrous.fovBoost = 2; p.chase.shake.maximumStrength = .35f;
                    p.chase.speed.speedToFov = AnimationCurve.Linear(0, 65, 320, 73); break;
                case VehicleCameraStyle.FirstPerson:
                    p.cockpit.shake.maximumStrength = .6f; p.cockpit.maximumRoll = .4f;
                    p.cockpit.follow.airborneHorizon = .95f; break;
                case VehicleCameraStyle.Drift:
                    p.chase.drift.yawInfluence = .45f; p.chase.drift.horizontalOffset = .75f;
                    p.chase.drift.slipThreshold = 7; p.chase.drift.additionalFov = 2.5f; break;
            }
        }
    }

    [CustomEditor(typeof(VehicleCameraRig))]
    internal sealed class VehicleCameraRigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (GUILayout.Button("Open Camera Tools")) VehicleProfilesWindow.OpenCamera();
        }
    }
}
