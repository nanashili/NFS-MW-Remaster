using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    /// <summary>Connects reviewed engine audio to an existing scene vehicle without replacing its rig or other sound settings.</summary>
    public static class BlackBoxVehicleAttachment
    {
        public static VehicleAudio[] FindSceneVehicles(Object selection)
        {
            if (selection == null) return Array.Empty<VehicleAudio>();
            var selectedObject = selection as GameObject ?? (selection as Component)?.gameObject;
            string assetPath = AssetDatabase.GetAssetPath(selection);
            bool folder = AssetDatabase.IsValidFolder(assetPath);
            return Object.FindObjectsByType<VehicleAudio>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(p => p.gameObject.scene.IsValid() && !EditorSceneManager.IsPreviewScene(p.gameObject.scene) &&
                    p.GetComponent<VehicleController>() != null &&
                    (string.IsNullOrEmpty(assetPath)
                        ? selectedObject != null && (selectedObject.transform.IsChildOf(p.transform) || p.transform.IsChildOf(selectedObject.transform))
                        : p.GetComponentsInChildren<Transform>(true).Any(t =>
                        {
                            string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
                            return !string.IsNullOrEmpty(source) && (folder ? source.StartsWith(assetPath + "/", StringComparison.Ordinal) : source == assetPath);
                        })))
                .OrderBy(p => p.gameObject.scene.path, StringComparer.Ordinal).ThenBy(p => p.name, StringComparer.Ordinal).ToArray();
        }

        public static VehicleAudio RestoreTarget(BlackBoxSession session)
            => session != null && GlobalObjectId.TryParse(session.targetSceneObjectId, out var id)
                ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as VehicleAudio : null;

        public static void RememberTarget(BlackBoxSession session, VehicleAudio target)
        {
            Undo.RecordObject(session, "Select engine audio vehicle");
            session.targetSceneObjectId = target != null && !string.IsNullOrEmpty(target.gameObject.scene.path)
                ? GlobalObjectId.GetGlobalObjectIdSlow(target).ToString() : "";
            session.targetScenePath = target != null ? target.gameObject.scene.path : "";
            EditorUtility.SetDirty(session);
        }

        public static BlackBoxMappingPlan Prepare(BlackBoxSession session, BlackBoxDocument[] documents, VehicleAudio target)
        {
            if (session == null) throw new InvalidOperationException("Create an analysis session first.");
            ValidateTarget(target);
            AutoWire(target);
            var generated = AssetDatabase.LoadAssetAtPath<VehicleSensoryProfile>(session.generatedProfilePath);
            if (generated == null)
            {
                Undo.RecordObject(session, "Preserve target vehicle sound settings");
                session.profile = target.Profile;
                EditorUtility.SetDirty(session);
            }
            else if (target.Profile != generated && target.Profile != session.profile)
                throw new InvalidOperationException("This vehicle uses different sound settings. Use a separate analysis session to preserve them.");
            var plan = BlackBoxNativeMapping.Prepare(session, documents, true);
            plan.SceneTarget = target;
            plan.SceneOriginalProfile = target.Profile;
            plan.Changes[3] = "Attach to " + target.gameObject.scene.name + " / " + target.name + ". The scene is marked modified for saving; telemetry, audio-world routing and vehicle tuning stay intact.";
            return plan;
        }

        public static void ValidateTarget(VehicleAudio target)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before attaching a saved engine mapping.");
            if (target == null || EditorUtility.IsPersistent(target) || !target.gameObject.scene.IsValid() ||
                !target.gameObject.scene.isLoaded || EditorSceneManager.IsPreviewScene(target.gameObject.scene))
                throw new InvalidOperationException("Choose an existing vehicle in an open scene.");
            if (target.Profile != null && !AssetDatabase.Contains(target.Profile))
                throw new InvalidOperationException("Save the vehicle's current sound profile before using it as the attachment template.");
        }

        /// <summary>Fill only missing scene routing references; never replace an author's existing wiring.</summary>
        private static void AutoWire(VehicleAudio target)
        {
            if (target == null || EditorUtility.IsPersistent(target)) return;
            using var serialized = new SerializedObject(target);
            bool changed = false;
            var world = serialized.FindProperty("world");
            if (world != null && world.objectReferenceValue == null)
            {
                var candidate = UnityEngine.Object.FindFirstObjectByType<SensoryAudioWorld>(FindObjectsInactive.Include);
                if (candidate != null) { world.objectReferenceValue = candidate; changed = true; }
            }
            if (changed)
            {
                Undo.RecordObject(target, "Auto-wire VehicleAudio routing");
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
            }
        }

        internal static void Bind(VehicleAudio target, VehicleSensoryProfile profile)
        {
            Undo.RecordObject(target, "Attach reviewed engine audio");
            using var serialized = new SerializedObject(target);
            serialized.FindProperty("profile").objectReferenceValue = profile;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
        }
    }
}
