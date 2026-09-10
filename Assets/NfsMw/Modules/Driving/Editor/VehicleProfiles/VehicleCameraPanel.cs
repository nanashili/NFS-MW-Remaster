using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    internal sealed class VehicleCameraPanel
    {
        private VehicleCameraRig rig;
        private VehicleCameraProfile profile;
        private SerializedObject serialized;
        public void SelectCamera(VehicleCameraRig selected) { rig = selected; Close(); profile = selected ? selected.Profile : null; }
        public void Close() { serialized?.Dispose(); serialized = null; }
        public void Draw()
        {
            EditorGUILayout.LabelField("Sense of Speed", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Original Most Wanted inspired tuning. Chase and cockpit have separate curves and movement limits. Temporary effect switches below affect only this camera session.", MessageType.Info);
            rig = (VehicleCameraRig)EditorGUILayout.ObjectField("Camera", rig, typeof(VehicleCameraRig), true);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Selected Camera")) rig = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<VehicleCameraRig>() : null;
                if (GUILayout.Button("Find Active Camera")) rig = Object.FindFirstObjectByType<VehicleCameraRig>();
                if (GUILayout.Button("Ensure Presets")) VehicleCameraPresets.Ensure();
            }
            if (!profile && rig) profile = rig.Profile;
            var selected = (VehicleCameraProfile)EditorGUILayout.ObjectField("Profile to Edit", profile, typeof(VehicleCameraProfile), false);
            if (selected != profile) { Close(); profile = selected; }
            if (rig && profile && rig.Profile != profile && GUILayout.Button("Apply Profile to Camera"))
            { Undo.RecordObject(rig, "Change camera profile"); rig.SetProfile(profile); EditorUtility.SetDirty(rig); }
            if (rig)
            {
                rig.DebugEffects = (VehicleCameraEffects)EditorGUILayout.EnumFlagsField("Temporary Enabled Effects", rig.DebugEffects);
                bool blur = EditorGUILayout.Toggle("Motion Blur (User Setting)", rig.MotionBlurEnabled);
                if (blur != rig.MotionBlurEnabled) { Undo.RecordObject(rig, "Change motion blur"); rig.MotionBlurEnabled = blur; EditorUtility.SetDirty(rig); }
                if (!rig.GetComponent<VehicleCameraPostProcessing>() && GUILayout.Button("Add HDRP Speed Effects")) Undo.AddComponent<VehicleCameraPostProcessing>(rig.gameObject);
                if (Application.isPlaying) DrawDebug(rig.Diagnostics);
            }
            if (!profile) return;
            if (profile.fovAxis == VehicleCameraFovAxis.Horizontal) EditorGUILayout.HelpBox("Profile FOV contributions are horizontal degrees; Current FOV shows Unity's resolved vertical field of view.", MessageType.None);
            if (GUILayout.Button("Duplicate as Custom Profile"))
            {
                string path = EditorUtility.SaveFilePanelInProject("Custom Camera Profile", "Custom Camera", "asset", "Choose a profile location");
                if (!string.IsNullOrEmpty(path)) { var copy = Object.Instantiate(profile); copy.style = VehicleCameraStyle.Custom; AssetDatabase.CreateAsset(copy, path); Close(); profile = copy; }
            }
            if (serialized == null || serialized.targetObject != profile) { Close(); serialized = new SerializedObject(profile); }
            serialized.Update();
            var property = serialized.GetIterator(); bool children = true;
            while (property.NextVisible(children)) { children = false; if (property.name != "m_Script") EditorGUILayout.PropertyField(property, true); }
            serialized.ApplyModifiedProperties();
            if (GUILayout.Button("Save Profile")) AssetDatabase.SaveAssetIfDirty(profile);
        }
        private static void DrawDebug(VehicleCameraDiagnostics d)
        {
            EditorGUILayout.LabelField("Live Camera", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Speed / acceleration", $"{d.speed:F1} km/h / {d.acceleration:F2} m/s²");
            EditorGUILayout.LabelField("Longitudinal / lateral acceleration", $"{d.longitudinalAcceleration:F2} / {d.lateralAcceleration:F2} m/s²");
            EditorGUILayout.LabelField("Yaw rate / slip angle", $"{d.yawRate:F1}°/s / {d.slipAngle:F1}°");
            EditorGUILayout.LabelField("Current FOV / configured base", $"{d.currentFov:F2}° / {d.baseFov:F2}°");
            EditorGUILayout.LabelField("Speed / acceleration / nitrous FOV", $"{d.speedFov:F2} / {d.accelerationFov:F2} / {d.nitrousFov:F2}°");
            EditorGUILayout.LabelField("Distance / lag / look ahead", $"{d.distance:F2} / {d.lag:F3} / {d.lookAhead:F2} m");
            EditorGUILayout.LabelField("Drift / shake / landing", $"{d.driftBlend:F2} / {d.shakeStrength:F2} / {d.landingStrength:F2}");
            EditorGUILayout.LabelField("Motion blur / peripheral", $"{d.motionBlur:F3} / {d.peripheral:F3}");
            EditorGUILayout.LabelField("Parked stabilization", d.parked ? "Settled" : "Following");
        }
    }
}
