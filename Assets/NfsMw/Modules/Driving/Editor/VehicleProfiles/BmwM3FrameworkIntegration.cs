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
    /// <summary>One-time content migration of the existing imported BMW. No model-specific runtime.</summary>
    internal static class BmwM3FrameworkIntegration
    {
        internal const string Folder = "Assets/NfsMw/Content/Vehicles/Street/BMW/M3 E42/Framework";
        private const string DraftPath = "Assets/NfsMw/Content/Vehicles/Street/BMW/M3 E42/Editor/BMWM3GTRE46.asset";
        public static void ValidateMigrationInBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Isolated batch validation only.");
            EditorSceneManager.OpenScene("Assets/NfsMw/Scenes/Showcase/WeatherDemo.unity");
            Selection.activeGameObject = Object.FindObjectsByType<VehicleController>(FindObjectsSortMode.None).Single(v => v.name == "Player Vehicle - BMW M3 E42").gameObject;
            ApplySelected();
        }
        [MenuItem("Racing Tools/Vehicles/Integrate Selected BMW M3")]
        internal static void ApplySelected()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
            var vehicle = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<VehicleController>() : null;
            if (!vehicle || !vehicle.transform.Find("BMW M3 E42 Visual/BMW M3 E42")) throw new InvalidOperationException("Select the existing BMW vehicle root.");
            if (vehicle.GetComponent<VehicleConfiguration>() || AssetDatabase.IsValidFolder(Folder)) throw new InvalidOperationException("BMW is already integrated. Edit its profile instead of rerunning migration.");
            Directory.CreateDirectory("Library/BmwFramework");
            if (!File.Exists("Library/BmwFramework/Before.unity"))
                EditorSceneManager.SaveScene(vehicle.gameObject.scene, "Library/BmwFramework/Before.unity", true);
            Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Integrate BMW vehicle framework");
            Undo.RegisterFullObjectHierarchyUndo(vehicle.gameObject, "Integrate BMW vehicle framework");
            EnsureFolder(Folder); EnsureFolder(Folder + "/Meshes");
            var draft = AssetDatabase.LoadAssetAtPath<VehicleProfileDraft>(DraftPath);
            Undo.RecordObject(draft, "Bind BMW profile"); draft.MigrateLegacySchema();
            var factory = Save(Object.Instantiate(vehicle.FactoryTuning), "Factory.asset");
            factory.name = "BMW M3 E42 - Scene Factory"; factory.displayName = "BMW M3 E42"; factory.driveLayout = VehicleDriveLayout.Rwd;
            var definition = Save(ScriptableObject.CreateInstance<VehicleDefinition>(), "Definition.asset");
            definition.vehicleId = "bmwm3gtre46"; definition.variantId = "scene-stock"; definition.manufacturer = "BMW"; definition.model = "M3 E42"; definition.year = 2005;
            definition.factoryTuning = factory;
            definition.capabilities = VehicleCapabilities.Lighting | VehicleCapabilities.Glass | VehicleCapabilities.Windows | VehicleCapabilities.Mirrors | VehicleCapabilities.Cockpit | VehicleCapabilities.Nitrous | VehicleCapabilities.Induction;
            definition.referenceEvidence = "Preserves WeatherDemo street-racer handling; RWD and contact geometry explicitly bound to the imported model. 2005 is the game-content authoring year. Not measured BMW or original-game physics. Folder label E42 is retained; audio identity is BMWM3GTRE46. No separately rigged cockpit controls or wipers in this source.";
            draft.runtimeDefinition = definition; draft.tuning = factory; draft.audio = vehicle.GetComponent<VehicleAudio>().Profile;
            draft.performance = Save(Object.Instantiate(vehicle.GetComponent<VehiclePerformanceSystem>().Catalog), "Performance.asset");
            draft.customization = Save(Object.Instantiate(vehicle.GetComponent<VehicleCustomizationSystem>().Catalog), "Customization.asset");
            definition.performanceCatalog = draft.performance; definition.customizationCatalog = draft.customization;
            draft.description = "Current-scene BMW, assembled through the shared vehicle framework. Model scale and visual geometry preserved; wheel and caliper geometry bound to suspension. Existing handling, audio and catalogue IDs retained. Cockpit controls and wipers need an artist rig.";
            var identity = Save(ScriptableObject.CreateInstance<VehicleIdentityCatalog>(), "Identity.asset");
            var brand = identity.AddBrand("BMW"); var model = identity.AddModel(brand.Id, "M3", "E42 (source folder)"); identity.AddYear(model.Id, 2005);
            var variant = identity.AddVariant(model.Id, 2005, "WeatherDemo"); draft.catalog = identity; draft.modelId = model.Id; draft.variantId = variant.Id; draft.modelYear = 2005;
            var imported = vehicle.transform.Find("BMW M3 E42 Visual/BMW M3 E42");
            var sources = imported.GetComponentsInChildren<MeshRenderer>();
            var body = Node(vehicle.transform, "BMW Framework Body", Vector3.zero);
            var bindings = body.gameObject.AddComponent<VehiclePresentationBindings>();
            bindings.cockpitCameraAnchor = Node(body, "Driver view", new Vector3(-.4f, .6f, -.15f));
            var pieces = new Dictionary<string, MeshRenderer>(); var paint = new List<Renderer>();
            var wheels = new[] { "wheel.002", "wheel", "wheel.003", "wheel.001" };
            var centers = new Vector3[4]; var wheelRoots = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                var source = sources.Single(r => r.name == wheels[i]); var mesh = source.GetComponent<MeshFilter>().sharedMesh;
                var bounds = BoundsInVehicle(source, vehicle.transform); centers[i] = bounds.center;
                if (i == 0) factory.tires.wheelRadius = bounds.size.y * .5f;
                wheelRoots[i] = Node(vehicle.transform, "BMW Wheel " + i, centers[i]);
                for (int s = 0; s < mesh.subMeshCount; s++) Piece(source, s, mesh.GetTriangles(s), wheelRoots[i], vehicle.transform, centers[i], "Wheel" + i + "-" + s);
                draft.assembly.wheels[i] = new VehicleAssemblyWheel { visualSource = PrefabUtility.SaveAsPrefabAsset(wheelRoots[i].gameObject, Folder + "/Wheel" + i + ".prefab"), suspensionAnchor = centers[i] + Vector3.up * factory.tires.suspensionRestLength, driven = i >= 2, handbrake = i >= 2 };
                var wheel = vehicle.Wheels[i]; wheel.transform.localPosition = draft.assembly.wheels[i].suspensionAnchor;
                wheel.Setup(i < 2 ? VehicleAxle.Front : VehicleAxle.Rear, i < 2, i >= 2, i >= 2, wheelRoots[i]); wheel.SetVisualRotationOffset(Vector3.zero); wheel.SetGroundMask(Physics.DefaultRaycastLayers);
                wheel.SetVisualReferenceRadius(factory.tires.wheelRadius);
            }
            bindings.wheels = new VehicleWheelPresentationBinding[4];
            foreach (var source in sources)
            {
                if (wheels.Contains(source.name)) { source.enabled = false; continue; }
                var mesh = source.GetComponent<MeshFilter>().sharedMesh;
                if (source.name == "brembo")
                {
                    var vertices = mesh.vertices; var indices = mesh.GetTriangles(0); var groups = Enumerable.Range(0, 4).Select(_ => new List<int>()).ToArray();
                    for (int k = 0; k < indices.Length; k += 3)
                    {
                        Vector3 point = vehicle.transform.InverseTransformPoint(source.transform.TransformPoint((vertices[indices[k]] + vertices[indices[k + 1]] + vertices[indices[k + 2]]) / 3));
                        int index = (point.z > 0 ? 0 : 2) + (point.x > 0 ? 1 : 0); groups[index].AddRange(new[] { indices[k], indices[k + 1], indices[k + 2] });
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        var pivot = Node(body, "Caliper " + i, centers[i]); Piece(source, 0, groups[i].ToArray(), pivot, vehicle.transform, centers[i], "Caliper" + i);
                        bindings.wheels[i] = new VehicleWheelPresentationBinding { id = new[] { "FL", "FR", "RL", "RR" }[i], caliper = pivot, wheel = vehicle.Wheels[i].transform };
                    }
                }
                else for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    string key = source.name + "-" + s; var r = Piece(source, s, mesh.GetTriangles(s), body, vehicle.transform, Vector3.zero, key); pieces.Add(key, r);
                    if (source.sharedMaterials[s].name == "primary") paint.Add(r);
                }
                source.enabled = false;
            }
            bindings.windscreen = new Renderer[] { pieces["windscreen_ok-0"] }; bindings.rearWindow = new Renderer[] { pieces["stecla-1"] };
            bindings.sideWindows = new Renderer[] { pieces["door_lf_ok-4"], pieces["door_rf_ok-4"] };
            bindings.sideWindowPivots = bindings.sideWindows.Select(r => r.transform).ToArray(); bindings.sideWindowOpenAxis = Vector3.down; bindings.sideWindowOpenDistance = .42f;
            bindings.headlights = Lamp(body, "headlights", new[] { new Vector3(-.7f, -.08f, 2.52f), new Vector3(.7f, -.08f, 2.52f) }, Color.white, 900, 75, true);
            bindings.headlights.emissive = new Renderer[] { pieces["peredfar-0"] }; bindings.headlights.mode = VehicleLampMode.Low;
            bindings.highBeams = Lamp(body, "high-beams", new[] { new Vector3(-.6f, -.08f, 2.52f), new Vector3(.6f, -.08f, 2.52f) }, Color.white, 1700, 110, true);
            bindings.tailLights = Lamp(body, "tail", new[] { new Vector3(-.78f, .17f, -2.64f), new Vector3(.78f, .17f, -2.64f) }, Color.red, 2, 3, false);
            bindings.tailLights.emissive = new Renderer[] { pieces["chassis-2"] };
            bindings.brakeLights = new VehicleLampBinding { id = "brake", lights = bindings.tailLights.lights, emissive = bindings.tailLights.emissive, color = Color.red, intensity = 8, emissionIntensity = 5, range = 4 };
            bindings.reverseLights = Lamp(body, "reverse", new[] { new Vector3(-.52f, .11f, -2.66f), new Vector3(.52f, .11f, -2.66f) }, Color.white, 4, 5, false);
            bindings.leftIndicator = Lamp(body, "indicator-left", new[] { new Vector3(-.97f, -.08f, 2.5f), new Vector3(-.95f, .13f, -2.55f) }, new Color(1, .3f, .01f), 3, 3, false);
            bindings.rightIndicator = Lamp(body, "indicator-right", new[] { new Vector3(.97f, -.08f, 2.5f), new Vector3(.95f, .13f, -2.55f) }, new Color(1, .3f, .01f), 3, 3, false);
            vehicle.transform.Find("HDRP low beams").gameObject.SetActive(false);
            var mirrorMaterial = new Material(Shader.Find("HDRP/Unlit")); mirrorMaterial.SetColor("_UnlitColor", Color.white); HDMaterial.ValidateMaterial(mirrorMaterial); Save(mirrorMaterial, "Mirror.mat");
            var mirrorBindings = new List<VehicleMirrorBinding>();
            foreach (var key in new[] { "door_lf_ok-5", "door_rf_ok-5" })
            {
                var surface = pieces[key]; surface.sharedMaterial = mirrorMaterial;
                var mesh = surface.GetComponent<MeshFilter>().sharedMesh; var b = mesh.bounds; var uv = mesh.vertices.Select(p => new Vector2((p.x - b.min.x) / b.size.x, (p.y - b.min.y) / b.size.y)).ToArray(); mesh.uv = uv; EditorUtility.SetDirty(mesh);
                var view = Node(body, key + " view", b.center + Vector3.back * .04f); view.localRotation = Quaternion.Euler(0, 180, 0);
                mirrorBindings.Add(new VehicleMirrorBinding { id = key.StartsWith("door_lf") ? "left" : "right", surfaces = new Renderer[] { surface }, view = view, horizontalFlip = true, resolution = 256, refreshSeconds = .1f });
            }
            var mirrorSource = body.gameObject.AddComponent<VehicleMirrorRenderer>(); mirrorSource.enabled = false; mirrorSource.Configure(null, mirrorBindings.ToArray());
            SetReferenceOrString(mirrorSource, "textureProperty", "_UnlitColorMap");
            var sockets = new List<VehicleAssemblySocket>
            {
                Socket("paint", VehicleCustomizationCategory.Paint, paint.ToArray()),
                Socket("glass", VehicleCustomizationCategory.WindowTint, bindings.sideWindows),
                Socket("front-bumper", VehicleCustomizationCategory.BodyKit, pieces.Where(p => p.Key.StartsWith("bump_front_ok-")).Select(p => (Renderer)p.Value).ToArray()),
                Socket("rear-bumper", VehicleCustomizationCategory.BodyKit, pieces.Where(p => p.Key.StartsWith("bump_rear_ok-")).Select(p => (Renderer)p.Value).ToArray()),
                Socket("hood", VehicleCustomizationCategory.Hood, pieces.Where(p => p.Key.StartsWith("bonnet_ok-")).Select(p => (Renderer)p.Value).ToArray())
            };
            definition.supportedSlots = sockets.Select(s => s.id).Concat(new[] { "wheels" }).ToArray();
            AddFinish(draft, "Graphite", VehicleCustomizationCategory.Paint, "paint", new Color(.18f, .19f, .21f), 700);
            AddFinish(draft, "SideTint", VehicleCustomizationCategory.WindowTint, "glass", new Color(.16f, .18f, .2f, .65f), 250);
            var bodyPrefab = PrefabUtility.SaveAsPrefabAsset(body.gameObject, Folder + "/Body.prefab");
            // Persistent sources for Studio rebuild; copy reference mappings while both hierarchies are available.
            draft.assembly.bodySource = bodyPrefab; draft.assembly.presentationSource = bodyPrefab.GetComponent<VehiclePresentationBindings>(); draft.assembly.mirrorSource = bodyPrefab.GetComponent<VehicleMirrorRenderer>(); draft.assembly.includePlayerInput = true;
            draft.assembly.sockets = sockets.Select(s => new VehicleAssemblySocket { id = s.id, category = s.category, stockRenderers = s.stockRenderers.Select(r => bodyPrefab.transform.Find(AnimationUtility.CalculateTransformPath(r.transform, body)).GetComponent<Renderer>()).ToArray() }).ToArray();
            foreach (var socket in sockets)
            { var t = Node(vehicle.transform, "BMW Slot " + socket.id, Vector3.zero); var slot = t.gameObject.AddComponent<VehicleCustomizationVisualSlot>(); slot.Configure(socket.category, t, socket.stockRenderers, true); slot.ConfigureSlot(socket.id, t, socket.stockRenderers, true); }
            var box = vehicle.GetComponent<BoxCollider>(); box.center = new Vector3(0, -.06f, .07f); box.size = new Vector3(1.98f, 1.12f, 5.16f);
            draft.assembly.colliderCenter = box.center; draft.assembly.colliderSize = box.size;
            var rootBindings = Undo.AddComponent<VehiclePresentationBindings>(vehicle.gameObject); EditorUtility.CopySerialized(bindings, rootBindings);
            var presentation = Undo.AddComponent<VehiclePresentationModule>(vehicle.gameObject);
            var mirrors = Undo.AddComponent<VehicleMirrorRenderer>(vehicle.gameObject); EditorUtility.CopySerialized(mirrorSource, mirrors); mirrors.enabled = true;
            mirrors.Configure(vehicle.CameraRig ? vehicle.CameraRig.GetComponent<Camera>() : null, mirrorBindings.ToArray());
            Object.DestroyImmediate(bindings); Object.DestroyImmediate(mirrorSource);
            var authority = Undo.AddComponent<VehicleInputAuthority>(vehicle.gameObject); authority.ConfigureDefault(vehicle.GetComponent<PlayerVehicleInput>()); vehicle.SetInputSource(authority);
            var configuration = Undo.AddComponent<VehicleConfiguration>(vehicle.gameObject);
            if (!configuration.TryConfigure(definition, Array.Empty<VehicleTuningAdjustment>(), out var reason)) throw new InvalidOperationException(reason);
            var adapter = vehicle.GetComponent<VehicleCustomizationVisualAdapter>();
            using (var obj = new SerializedObject(adapter)) { obj.FindProperty("slots").arraySize = 0; obj.ApplyModifiedPropertiesWithoutUndo(); }
            draft.physicsLabSetup = Save(ScriptableObject.CreateInstance<RacingVehicleSetup>(), "PhysicsSetup.asset"); draft.physicsLabSetup.definition = definition; draft.physicsLabSetup.tuning = factory;
            draft.physicsLabSetup.dimensions = box.size; draft.physicsLabSetup.wheelbase = ((centers[0].z + centers[1].z) - (centers[2].z + centers[3].z)) * .5f; draft.physicsLabSetup.trackWidth = centers[1].x - centers[0].x;
            var track = Save(ScriptableObject.CreateInstance<VehiclePhysicsLabTrack>(), "LaunchTrack.asset"); track.length = 1200; track.width = 80;
            draft.physicsLabExperiment = Save(ScriptableObject.CreateInstance<VehiclePhysicsLabDefinition>(), "LaunchExperiment.asset"); draft.physicsLabExperiment.vehicle = draft.physicsLabSetup; draft.physicsLabExperiment.track = track; draft.physicsLabExperiment.displayName = "BMW scene standing launch"; draft.physicsLabExperiment.safety.maximumSeconds = 15;
            // Authoritative persistent source builds a portable runtime prefab without scene-owned career/camera state.
            draft.vehiclePrefab = VehicleProfileAssembly.CreatePrefab(draft, Folder + "/Vehicle.prefab"); definition.prefab = draft.vehiclePrefab.GetComponent<VehicleController>();
            foreach (var asset in new Object[] { draft, factory, definition, identity, draft.performance, draft.customization, draft.physicsLabSetup, draft.physicsLabExperiment }) EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets(); Undo.CollapseUndoOperations(undo); EditorSceneManager.MarkSceneDirty(vehicle.gameObject.scene);
            if (!EditorSceneManager.SaveScene(vehicle.gameObject.scene)) throw new IOException("Could not save current scene.");
            File.WriteAllText("Library/BmwFramework/applied.txt", "BMW integrated in " + vehicle.gameObject.scene.path + "\n" + definition.referenceEvidence);
        }
        private static VehicleAssemblySocket Socket(string id, VehicleCustomizationCategory category, Renderer[] renderers) => new VehicleAssemblySocket { id = id, category = category, stockRenderers = renderers };
        private static void AddFinish(VehicleProfileDraft draft, string name, VehicleCustomizationCategory category, string slot, Color color, int price)
        {
            var part = Save(ScriptableObject.CreateInstance<AssetVehicleCustomization>(), name + ".asset"); part.ConfigureMetadata("bmw-" + name.ToLowerInvariant(), name, category, VehicleCustomizationStyle.Standard, price, false); part.ConfigureCompatibility(new[] { draft.runtimeDefinition.vehicleId }, new[] { draft.runtimeDefinition.variantId });
            var payload = new VehicleCustomizationVisualPayload(); payload.Configure(null, null, null, null, color, true, color.a); part.ConfigureVisual(payload);
            using (var obj = new SerializedObject(part)) { foreach (string field in new[] { "mountSlotIds", "requiredSlotIds" }) { var p = obj.FindProperty(field); p.arraySize = 1; p.GetArrayElementAtIndex(0).stringValue = slot; } obj.ApplyModifiedPropertiesWithoutUndo(); }
            draft.customization.SetItems(draft.customization.Items.Concat(new[] { part }).ToArray());
        }
        private static MeshRenderer Piece(MeshRenderer source, int submesh, int[] indices, Transform parent, Transform vehicle, Vector3 origin, string name)
        {
            var mesh = source.GetComponent<MeshFilter>().sharedMesh; var original = mesh.vertices; var normal = mesh.normals; var uv = mesh.uv; var tangents = mesh.tangents; var colors = mesh.colors;
            var matrix = vehicle.worldToLocalMatrix * source.transform.localToWorldMatrix; var normalMatrix = matrix.inverse.transpose;
            var used = indices.Distinct().ToArray(); var lookup = new Dictionary<int, int>(); for (int i = 0; i < used.Length; i++) lookup.Add(used[i], i);
            var copy = new Mesh { name = name, vertices = used.Select(i => matrix.MultiplyPoint3x4(original[i]) - origin).ToArray() };
            if (normal.Length == original.Length) copy.normals = used.Select(i => normalMatrix.MultiplyVector(normal[i]).normalized).ToArray();
            if (uv.Length == original.Length) copy.uv = used.Select(i => uv[i]).ToArray();
            if (colors.Length == original.Length) copy.colors = used.Select(i => colors[i]).ToArray();
            if (tangents.Length == original.Length) copy.tangents = used.Select(i => { Vector3 t = matrix.MultiplyVector(tangents[i]).normalized; return new Vector4(t.x, t.y, t.z, tangents[i].w); }).ToArray();
            copy.triangles = indices.Select(i => lookup[i]).ToArray(); copy.RecalculateBounds(); if (normal.Length != original.Length) copy.RecalculateNormals();
            Save(copy, "Meshes/" + name + ".asset"); var node = Node(parent, name, Vector3.zero); node.gameObject.AddComponent<MeshFilter>().sharedMesh = copy; var renderer = node.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = source.sharedMaterials[submesh]; renderer.shadowCastingMode = source.shadowCastingMode; renderer.receiveShadows = source.receiveShadows; return renderer;
        }
        private static Bounds BoundsInVehicle(MeshRenderer source, Transform vehicle)
        { var points = source.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v => vehicle.InverseTransformPoint(source.transform.TransformPoint(v))).ToArray(); var b = new Bounds(points[0], Vector3.zero); foreach (var point in points) b.Encapsulate(point); return b; }
        private static VehicleLampBinding Lamp(Transform root, string id, Vector3[] points, Color color, float intensity, float range, bool spot)
        {
            var result = new VehicleLampBinding { id = id, color = color, intensity = intensity, range = range };
            result.lights = points.Select((p, i) => { var node = Node(root, id + " " + i, p); var light = node.gameObject.AddComponent<Light>(); light.type = spot ? LightType.Spot : LightType.Point; light.spotAngle = 50; light.color = color; light.intensity = intensity; light.range = range; light.shadows = LightShadows.None; node.gameObject.AddComponent<HDAdditionalLightData>(); if (spot) node.localRotation = Quaternion.Euler(3, 0, 0); return light; }).ToArray(); return result;
        }
        private static Transform Node(Transform parent, string name, Vector3 position) { var node = new GameObject(name).transform; node.SetParent(parent, false); node.localPosition = position; node.gameObject.layer = parent.gameObject.layer; return node; }
        private static T Save<T>(T value, string name) where T : Object { AssetDatabase.CreateAsset(value, Folder + "/" + name); return value; }
        private static void EnsureFolder(string path) { if (AssetDatabase.IsValidFolder(path)) return; EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/')); AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path)); }
        private static void SetReferenceOrString(Object value, string field, string text) { using var obj = new SerializedObject(value); obj.FindProperty(field).stringValue = text; obj.ApplyModifiedPropertiesWithoutUndo(); }
    }
}
