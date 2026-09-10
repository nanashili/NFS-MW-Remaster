using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Creates a script-free visual wrapper and the existing physics rig in a private preview scene.
    /// First-create only: existing prefabs belong to their authors and cannot be overwritten by this command.
    /// Publication remains a separate reviewed operation. No source scripts, wallet or career modules are cloned.
    /// </summary>
    public static class VehicleProfileAssembly
    {
        public static string[] Validate(VehicleProfileDraft draft)
        {
            var failures = new List<string>();
            if (draft == null) return new[] { "Assign a draft." };
            if (!AssetDatabase.Contains(draft) || draft.Schema != VehicleProfileDraft.CurrentSchema || !Guid.TryParseExact(draft.Id, "N", out _))
                failures.Add("Save a supported, valid draft before assembly.");
            if (draft.tuning == null || !AssetDatabase.Contains(draft.tuning)) failures.Add("Assign persistent authoritative tuning.");
            else try { RacingLineSnapshot.ValidateTuning(draft.tuning); }
                catch (ArgumentException e) { failures.Add(e.Message); }
            var a = draft.assembly;
            if (a == null) return new[] { "Assembly mappings are missing." };
            ValidateSource(a.bodySource, "Body", failures);
            if (!Finite(a.bodyPosition) || !Finite(a.bodyEuler) || !Positive(a.bodyScale)) failures.Add("Body transform must be finite with positive scale.");
            if (!Finite(a.colliderCenter) || !Positive(a.colliderSize)) failures.Add("Author a finite chassis box with positive dimensions in metres.");
            if (a.excludedBodyRenderers == null || a.excludedBodyRenderers.Any(r => r == null || a.bodySource == null || !r.transform.IsChildOf(a.bodySource.transform)))
                failures.Add("Every excluded body renderer must resolve inside the selected source.");
            if (a.bodySource != null && !a.bodySource.GetComponentsInChildren<MeshRenderer>(true).Any(r =>
                !(a.excludedBodyRenderers ?? Array.Empty<Renderer>()).Contains(r) && r.enabled))
                failures.Add("At least one body renderer must remain after exclusions.");
            if (a.wheels == null || a.wheels.Length != 4) failures.Add("Map exactly four wheels in FL, FR, RL, RR order.");
            else
            {
                for (int i = 0; i < a.wheels.Length; i++)
                {
                    var wheel = a.wheels[i];
                    if (wheel == null) { failures.Add("Missing wheel mapping at index " + i); continue; }
                    ValidateSource(wheel.visualSource, "Wheel " + i, failures);
                    if (!Finite(wheel.suspensionAnchor) || !Finite(wheel.visualEuler) || !Positive(wheel.visualScale)) failures.Add("Invalid wheel transform at index " + i);
                    if (i < 2 && wheel.suspensionAnchor.z <= 0 || i >= 2 && wheel.suspensionAnchor.z >= 0 ||
                        i % 2 == 0 && wheel.suspensionAnchor.x >= 0 || i % 2 == 1 && wheel.suspensionAnchor.x <= 0)
                        failures.Add("Wheel anchors must respect FL/FR/RL/RR in +Z-forward space: index " + i);
                    if (wheel.visualSource != null && a.bodySource != null)
                        foreach (var renderer in wheel.visualSource.GetComponentsInChildren<MeshRenderer>(true))
                            if (renderer.transform.IsChildOf(a.bodySource.transform) &&
                                !(a.excludedBodyRenderers ?? Array.Empty<Renderer>()).Contains(renderer))
                                failures.Add("Spinning wheel is still present in body geometry; explicitly exclude " + renderer.name);
                }
                if (!a.wheels.Any(w => w != null && w.driven)) failures.Add("At least one wheel must explicitly be driven.");
            }
            var socketIds = new HashSet<string>(StringComparer.Ordinal);
            if (a.sockets == null) failures.Add("Socket collection is missing.");
            else foreach (var socket in a.sockets)
            {
                if (socket == null || string.IsNullOrWhiteSpace(socket.id) || !socketIds.Add(socket.id) || !Finite(socket.position) || !Finite(socket.euler))
                    failures.Add("Each socket needs a unique stable ID and finite pose.");
                else try { VehicleProfilePublication.ValidateSegment(socket.id); } catch (ArgumentException e) { failures.Add(e.Message); }
            }
            if (draft.runtimeDefinition != null)
            {
                if (!draft.runtimeDefinition.Validate(out string definitionFailure)) failures.Add(definitionFailure);
                if (draft.runtimeDefinition.factoryTuning != draft.tuning) failures.Add("Definition factory tuning must match this draft's authoritative tuning.");
            }
            if (a.presentationSource != null && (a.bodySource == null || !a.presentationSource.transform.IsChildOf(a.bodySource.transform)))
                failures.Add("Presentation Source must be within Body Source so its references can be remapped.");
            if (a.mirrorSource != null && (a.bodySource == null || !a.mirrorSource.transform.IsChildOf(a.bodySource.transform)))
                failures.Add("Mirror Source must be within Body Source.");
            return failures.Distinct().ToArray();
        }

        public static GameObject CreatePrefab(VehicleProfileDraft draft, string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Assemble in settled Edit Mode.");
            string[] failures = Validate(draft);
            if (failures.Length != 0) throw new InvalidOperationException(string.Join("\n", failures));
            VehicleProfilePublication.ValidatePrefabOutputPath(path);
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                // No artist GameObject is instantiated. Only deliberately constructed transforms,
                // MeshFilter/MeshRenderer, BoxCollider and existing physics modules are created.
                root = new GameObject("Vehicle-" + draft.Id);
                root.SetActive(false);
                // Match the established scene builder: chassis excluded from wheel ground raycasts.
                root.layer = 2;
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                var rigidbody = root.AddComponent<Rigidbody>(); rigidbody.mass = draft.tuning.chassis.mass;
                var box = root.AddComponent<BoxCollider>(); box.center = draft.assembly.colliderCenter; box.size = draft.assembly.colliderSize;
                var controller = root.AddComponent<VehicleController>(); controller.Tuning = draft.tuning;
                // The existing performance owner creates/disposes per-instance tuning, even without upgrades.
                root.AddComponent<VehiclePerformanceSystem>().SetCatalog(draft.performance);
                var chassis = Child(root.transform, "Body");
                chassis.localPosition = draft.assembly.bodyPosition; chassis.localRotation = Quaternion.Euler(draft.assembly.bodyEuler); chassis.localScale = draft.assembly.bodyScale;
                var bodyMap = CopyGeometry(draft.assembly.bodySource, chassis, new HashSet<Renderer>(draft.assembly.excludedBodyRenderers));
                controller.Wheels = new VehicleWheel[4];
                string[] names = { "FL", "FR", "RL", "RR" };
                for (int i = 0; i < 4; i++)
                {
                    var mapping = draft.assembly.wheels[i];
                    var anchor = Child(root.transform, "Suspension-" + names[i]); anchor.localPosition = mapping.suspensionAnchor;
                    var spin = Child(root.transform, "Wheel-" + names[i]);
                    spin.localPosition = mapping.suspensionAnchor - Vector3.up * draft.tuning.tires.suspensionRestLength;
                    var geometry = Child(spin, "Geometry"); geometry.localRotation = Quaternion.Euler(mapping.visualEuler); geometry.localScale = mapping.visualScale;
                    CopyGeometry(mapping.visualSource, geometry, new HashSet<Renderer>());
                    var wheel = anchor.gameObject.AddComponent<VehicleWheel>();
                    wheel.Setup(i < 2 ? VehicleAxle.Front : VehicleAxle.Rear, i < 2, mapping.driven, mapping.handbrake, spin);
                    wheel.SetVisualRotationOffset(Vector3.zero);
                    wheel.SetVisualReferenceRadius(draft.tuning.tires.wheelRadius);
                    wheel.SetGroundMask(Physics.DefaultRaycastLayers);
                    controller.Wheels[i] = wheel;
                }
                var sockets = Child(root.transform, "Sockets");
                foreach (var socket in draft.assembly.sockets)
                {
                    var t = Child(sockets, socket.id); t.localPosition = socket.position; t.localRotation = Quaternion.Euler(socket.euler);
                    var slot = t.gameObject.AddComponent<VehicleCustomizationVisualSlot>();
                    var renderers = (socket.stockRenderers ?? Array.Empty<Renderer>()).Select(r => MapReference(r, bodyMap) as Renderer).ToArray();
                    slot.Configure(socket.category, t, renderers, true); slot.ConfigureSlot(socket.id, t, renderers, true);
                }
                if (draft.assembly.presentationSource != null)
                {
                    var bindings = root.AddComponent<VehiclePresentationBindings>();
                    CopyBindings(draft.assembly.presentationSource, bindings, bodyMap);
                    if (bindings.wheels != null) for (int i = 0; i < bindings.wheels.Length && i < controller.Wheels.Length; i++)
                        if (bindings.wheels[i] != null) bindings.wheels[i].wheel = controller.Wheels[i].transform;
                    root.AddComponent<VehiclePresentationModule>();
                }
                if (draft.assembly.mirrorSource != null)
                {
                    var mirrors = root.AddComponent<VehicleMirrorRenderer>();
                    CopyBindings(draft.assembly.mirrorSource, mirrors, bodyMap);
                    mirrors.enabled = true;
                }
                if (draft.assembly.includePlayerInput)
                {
                    var authority = root.AddComponent<VehicleInputAuthority>();
                    authority.ConfigureDefault(root.AddComponent<PlayerVehicleInput>());
                    controller.SetInputSource(authority);
                }
                ApplyRuntimeConfiguration(root, draft);
                // These known physics scripts are not ExecuteAlways. Saving an active prefab does not step physics.
                root.SetActive(true);
                var result = PrefabUtility.SaveAsPrefabAsset(root, path, out bool succeeded);
                if (!succeeded || result == null) throw new IOException("Prefab save failed. Inspect the destination before retrying: " + path);
                return result;
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static Transform Child(Transform parent, string name)
        { var child = new GameObject(name).transform; child.SetParent(parent, false); child.gameObject.layer = parent.gameObject.layer; return child; }

        private static void ValidateSource(GameObject source, string role, List<string> failures)
        {
            if (source == null || !AssetDatabase.Contains(source)) { failures.Add(role + ": assign a persistent source node."); return; }
            if (source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0)
                failures.Add(role + ": skinned assembly requires a bone/animation mapping compiler; static copying is not supported.");
            var renderers = source.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0) failures.Add(role + ": no static mesh renderers.");
            foreach (var renderer in renderers)
            {
                if (renderer.GetComponent<TextMesh>() != null) continue;
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || renderer.sharedMaterials.Length != mesh.subMeshCount || renderer.sharedMaterials.Any(m => m == null))
                    failures.Add(role + ": missing mesh or mismatched material slots on " + renderer.name);
                // Copying a sheared or mirrored hierarchy as TRS would change the geometry silently.
                for (var t = renderer.transform; t != null; t = t.parent)
                { if (!Positive(t.localScale) || !Finite(t.localPosition) || !Finite(t.localEulerAngles)) failures.Add(role + ": unsupported source transform on " + t.name); if (t == source.transform) break; }
            }
        }

        private static Dictionary<Transform, Transform> CopyGeometry(GameObject source, Transform destination, HashSet<Renderer> excluded)
        {
            var transforms = new Dictionary<Transform, Transform> { { source.transform, destination } };
            foreach (var t in source.GetComponentsInChildren<Transform>(true))
            {
                if (t == source.transform) continue;
                var copy = Child(transforms[t.parent], t.name);
                copy.localPosition = t.localPosition; copy.localRotation = t.localRotation; copy.localScale = t.localScale;
                copy.gameObject.SetActive(t.gameObject.activeSelf); transforms.Add(t, copy);
            }
            foreach (var renderer in source.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (excluded.Contains(renderer)) continue;
                var target = transforms[renderer.transform].gameObject;
                if (renderer.TryGetComponent<TextMesh>(out var text))
                {
                    EditorUtility.CopySerialized(text, target.AddComponent<TextMesh>());
                    target.GetComponent<Renderer>().sharedMaterials = renderer.sharedMaterials;
                    continue;
                }
                target.AddComponent<MeshFilter>().sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                var copy = target.AddComponent<MeshRenderer>(); copy.sharedMaterials = renderer.sharedMaterials;
                copy.enabled = renderer.enabled; copy.shadowCastingMode = renderer.shadowCastingMode; copy.receiveShadows = renderer.receiveShadows;
            }
            foreach (var light in source.GetComponentsInChildren<Light>(true))
            {
                var target = transforms[light.transform].gameObject;
                var copy = target.AddComponent<Light>(); EditorUtility.CopySerialized(light, copy);
                var hd = target.AddComponent<HDAdditionalLightData>();
                var sourceHd = light.GetComponent<HDAdditionalLightData>();
                if (sourceHd != null) EditorUtility.CopySerialized(sourceHd, hd);
            }
            // Preserve authored LOD assignments, including exclusions, without importing source scripts.
            foreach (var group in source.GetComponentsInChildren<LODGroup>(true))
            {
                if (!transforms.ContainsKey(group.transform)) continue;
                var copy = transforms[group.transform].gameObject.AddComponent<LODGroup>();
                var levels = group.GetLODs();
                for (int i = 0; i < levels.Length; i++) levels[i].renderers = levels[i].renderers
                    .Where(r => r != null && !excluded.Contains(r) && transforms.ContainsKey(r.transform))
                    .Select(r => transforms[r.transform].GetComponent<Renderer>()).Where(r => r != null).ToArray();
                copy.SetLODs(levels); copy.fadeMode = group.fadeMode; copy.animateCrossFading = group.animateCrossFading; copy.RecalculateBounds();
            }
            return transforms;
        }

        private static Object MapReference(Object reference, Dictionary<Transform, Transform> map)
        {
            if (reference == null) return null;
            var sourceTransform = reference is GameObject go ? go.transform : reference is Component component ? component.transform : null;
            if (sourceTransform == null) return reference; // Shared materials, meshes and profiles remain shared assets.
            if (!map.TryGetValue(sourceTransform, out var target))
                throw new ArgumentException("Presentation binding points outside mapped model: " + reference.name);
            if (reference is GameObject) return target.gameObject;
            if (reference is Transform) return target;
            var mapped = target.GetComponent(reference.GetType());
            if (mapped == null) throw new ArgumentException("Unsupported presentation component binding: " + reference.GetType().Name + " on " + reference.name);
            return mapped;
        }

        private static void CopyBindings(Component source, Component destination, Dictionary<Transform, Transform> map)
        {
            EditorUtility.CopySerialized(source, destination);
            using var serialized = new SerializedObject(destination);
            var property = serialized.GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference && !property.name.StartsWith("m_", StringComparison.Ordinal)
                    && property.objectReferenceValue != null)
                    property.objectReferenceValue = MapReference(property.objectReferenceValue, map);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void ApplyRuntimeConfiguration(GameObject root, VehicleProfileDraft draft)
        {
            var controller = root.GetComponent<VehicleController>();
            if (controller == null) throw new ArgumentException("The prefab needs an existing VehicleController.");
            controller.Tuning = draft.runtimeDefinition != null ? draft.runtimeDefinition.factoryTuning : draft.tuning;
            var performance = root.GetComponent<VehiclePerformanceSystem>() ?? root.AddComponent<VehiclePerformanceSystem>();
            performance.SetCatalog(draft.performance);
            var customization = root.GetComponent<VehicleCustomizationSystem>() ?? root.AddComponent<VehicleCustomizationSystem>();
            customization.SetCatalog(draft.customization);
            customization.SetVisualAdapter(root.GetComponent<VehicleCustomizationVisualAdapter>() ?? root.AddComponent<VehicleCustomizationVisualAdapter>());
            if (draft.runtimeDefinition != null)
            {
                var configuration = root.GetComponent<VehicleConfiguration>() ?? root.AddComponent<VehicleConfiguration>();
                if (!configuration.TryConfigure(draft.runtimeDefinition, configuration.Adjustments, out string failure)) throw new ArgumentException(failure);
            }
            if (draft.audio != null)
                (root.GetComponent<VehicleAudio>() ?? root.AddComponent<VehicleAudio>()).Configure(controller, null, draft.audio, draft.assembly.includePlayerInput);
        }

        /// <summary>Updates known runtime configuration in place; authored geometry and component references retain their IDs.</summary>
        public static void UpdatePrefabConfiguration(VehicleProfileDraft draft)
        {
            if (draft == null || draft.vehiclePrefab == null) throw new ArgumentException("Assign the assembled prefab to update.");
            string path = AssetDatabase.GetAssetPath(draft.vehiclePrefab);
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Update requires an editable prefab asset.");
            var root = PrefabUtility.LoadPrefabContents(path);
            try { ApplyRuntimeConfiguration(root, draft); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        private static bool Positive(Vector3 value) => Finite(value) && value.x > 0 && value.y > 0 && value.z > 0;
    }
}
