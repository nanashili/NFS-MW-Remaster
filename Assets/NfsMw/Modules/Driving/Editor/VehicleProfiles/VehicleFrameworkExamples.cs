using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>Original example content compiled through the same Studio assembly and gameplay composition.</summary>
    public static partial class DrivingDemoBuilder
    {
        public const string FrameworkExamplesFolder = "Assets/NfsMw/Modules/Driving/Examples/VehicleFramework";

        [MenuItem("Racing Tools/Vehicles/Build Framework Examples")]
        public static void BuildVehicleFrameworkExamples()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Build examples in Edit Mode.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var hatch = CreateFrameworkExample(FrameworkExamplesFolder, "hatch-fwd", VehicleDriveLayout.Fwd, new Color(.06f, .38f, .7f));
            CreateFrameworkExample(FrameworkExamplesFolder, "coupe-rwd", VehicleDriveLayout.Rwd, new Color(.8f, .22f, .07f));
            BuildFrameworkDrivingScene(hatch);
            // Opening/saving the first scene can unload unreferenced native asset objects.
            BuildFrameworkDrivingScene(AssetDatabase.LoadAssetAtPath<VehicleProfileDraft>(FrameworkExamplesFolder + "/coupe-rwd/Profile.asset"));
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { FrameworkExamplesFolder }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                HDMaterial.ValidateMaterial(material); EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
        }

        public static VehicleProfileDraft CreateFrameworkExample(string folder, string id, VehicleDriveLayout layout, Color paintColor)
        {
            EnsureFolder(folder); string path = folder + "/" + id; EnsureFolder(path);
            var existing = AssetDatabase.LoadAssetAtPath<VehicleProfileDraft>(path + "/Profile.asset");
            if (existing != null) return existing; // Never reset an author's example edits on another menu invocation.
            var draft = SaveExample(ScriptableObject.CreateInstance<VehicleProfileDraft>(), path + "/Profile.asset");
            draft.modelId = id; draft.variantId = "street"; draft.modelYear = 2005;
            draft.description = "Original authored " + layout + " vehicle. Shared production runtime; no reference-game physics calibration.";
            var tuning = VehicleTuning.CreateStreetRacer(); tuning.driveLayout = layout; tuning.displayName = layout == VehicleDriveLayout.Fwd ? "Compact 180" : "Coupe 390";
            tuning.chassis.mass = layout == VehicleDriveLayout.Fwd ? 1120 : 1450;
            tuning.engine.maxTorqueNewtonMeters = layout == VehicleDriveLayout.Fwd ? 180 : 390;
            tuning.engine.finalDrive = layout == VehicleDriveLayout.Fwd ? 4.1f : 3.42f;
            tuning.tires.springRate = layout == VehicleDriveLayout.Fwd ? 27000 : 31000;
            tuning.tires.lateralGrip = layout == VehicleDriveLayout.Fwd ? 1.3f : 1.18f;
            tuning.differential = VehicleDifferentialMode.LimitedSlip; tuning.differentialLockStrength = .35f;
            tuning.chassis.maxSpeedKph = layout == VehicleDriveLayout.Fwd ? 220 : 300;
            draft.tuning = SaveExample(tuning, path + "/Factory.asset");
            var definition = SaveExample(ScriptableObject.CreateInstance<VehicleDefinition>(), path + "/Definition.asset");
            definition.vehicleId = id; definition.variantId = "street"; definition.manufacturer = "Framework"; definition.model = tuning.displayName; definition.year = 2005;
            definition.factoryTuning = tuning; definition.capabilities = VehicleCapabilities.All; definition.supportedSlots = new[] { "front-bumper", "rear-bumper", "paint", "glass", "wheels" };
            draft.runtimeDefinition = definition;
            Material paint = ExampleMaterial(path, "Paint", paintColor);
            Material glass = ExampleMaterial(path, "Glass", new Color(.1f, .15f, .18f, .22f));
            glass.SetFloat("_SurfaceType", 1); glass.SetFloat("_BlendMode", 0); glass.SetFloat("_ZWrite", 0); glass.renderQueue = 3000; HDMaterial.ValidateMaterial(glass);
            Material tire = ExampleMaterial(path, "Rubber", new Color(.018f, .019f, .02f));
            Material silver = ExampleMaterial(path, "Interior", new Color(.18f, .18f, .2f));
            Material lamp = ExampleMaterial(path, "Lamp", Color.white);
            Material mirror = new Material(Shader.Find("HDRP/Unlit")); mirror.SetColor("_UnlitColor", Color.white); HDMaterial.ValidateMaterial(mirror); SaveExample(mirror, path + "/Mirror.mat");
            GameObject wheelSource = new GameObject("Authored wheel");
            ExampleShape(wheelSource.transform, "Tire", Vector3.zero, new Vector3(.68f, .13f, .68f), tire, PrimitiveType.Cylinder).transform.localRotation = Quaternion.Euler(0, 0, 90);
            var wheelPrefab = PrefabUtility.SaveAsPrefabAsset(wheelSource, path + "/WheelSource.prefab"); Object.DestroyImmediate(wheelSource);
            GameObject body = new GameObject("Authored " + tuning.displayName);
            var bodyRenderer = ExampleShape(body.transform, "Body", new Vector3(0, .1f, 0), new Vector3(1.75f, .5f, layout == VehicleDriveLayout.Fwd ? 3.8f : 4.3f), paint).GetComponent<Renderer>();
            var front = ExampleShape(body.transform, "Front bumper", new Vector3(0, .02f, 2.05f), new Vector3(1.8f, .2f, .2f), paint).GetComponent<Renderer>();
            var rear = ExampleShape(body.transform, "Rear bumper", new Vector3(0, .02f, -2.05f), new Vector3(1.8f, .2f, .2f), paint).GetComponent<Renderer>();
            var binding = body.AddComponent<VehiclePresentationBindings>();
            binding.steeringRatio = 14; binding.steeringWheelDegrees = 450;
            binding.headlights.mode = VehicleLampMode.Low;
            binding.windscreen = new[] { ExampleShape(body.transform, "Windscreen", new Vector3(0, .67f, .8f), new Vector3(1.45f, .6f, .03f), glass).GetComponent<Renderer>() };
            binding.rearWindow = new[] { ExampleShape(body.transform, "Rear glass", new Vector3(0, .65f, -.9f), new Vector3(1.4f, .55f, .03f), glass).GetComponent<Renderer>() };
            var windows = new List<Renderer>(); var pivots = new List<Transform>();
            foreach (float x in new[] { -.76f, .76f })
            { var pivot = ExampleNode(body.transform, "Window pivot", new Vector3(x, .65f, 0)); pivots.Add(pivot); windows.Add(ExampleShape(pivot, "Side glass", Vector3.zero, new Vector3(.03f, .55f, 1.65f), glass).GetComponent<Renderer>()); }
            binding.sideWindows = windows.ToArray(); binding.sideWindowPivots = pivots.ToArray(); binding.sideWindowOpenAxis = Vector3.down;
            binding.steeringWheel = ExampleNode(body.transform, "Steering pivot", new Vector3(-.38f, .58f, .42f));
            var rimMesh = CreateExampleSteeringRim(); SaveExample(rimMesh, path + "/SteeringRim.asset");
            var rim = ExampleShape(binding.steeringWheel, "Steering rim", Vector3.zero, Vector3.one, silver);
            rim.GetComponent<MeshFilter>().sharedMesh = rimMesh;
            ExampleShape(binding.steeringWheel, "Steering spoke", Vector3.zero, new Vector3(.3f, .028f, .04f), lamp);
            binding.cockpitCameraAnchor = ExampleNode(body.transform, "Driver camera", new Vector3(-.38f, .8f, -.2f));
            binding.speedometer = ExampleGauge(body.transform, "Speed kmh", new Vector3(-.52f, .63f, .62f), 300, lamp);
            binding.tachometer = ExampleGauge(body.transform, "Engine rpm", new Vector3(-.26f, .63f, .62f), 8000, lamp);
            var gear = ExampleNode(body.transform, "Gear display", new Vector3(-.1f, .6f, .6f)); binding.gearText = gear.gameObject.AddComponent<TextMesh>(); binding.gearText.text = "N"; binding.gearText.characterSize = .08f; binding.gearText.anchor = TextAnchor.MiddleCenter;
            binding.gearLever = ExampleNode(body.transform, "Gear lever", new Vector3(.08f, .3f, .13f)); ExampleShape(binding.gearLever, "Lever", new Vector3(0, .07f, 0), new Vector3(.03f, .16f, .03f), silver);
            binding.gearPoses = Enumerable.Range(0, 7).Select(i => Quaternion.Euler(i == 0 ? 0 : (i % 2 == 0 ? -15 : 15), 0, (i / 2 - 1) * 8)).ToArray();
            binding.pedals = new[] { ExampleNode(body.transform, "Brake pedal", new Vector3(-.44f, -.02f, .5f)), ExampleNode(body.transform, "Throttle pedal", new Vector3(-.25f, -.02f, .5f)) };
            foreach (var pedal in binding.pedals) ExampleShape(pedal, "Pedal", Vector3.zero, new Vector3(.08f, .15f, .02f), silver);
            binding.wiperPivots = new[] { ExampleNode(body.transform, "Wiper L", new Vector3(-.52f, .38f, .83f)), ExampleNode(body.transform, "Wiper R", new Vector3(.13f, .38f, .83f)) };
            foreach (var wiper in binding.wiperPivots) ExampleShape(wiper, "Blade", new Vector3(.2f, 0, 0), new Vector3(.42f, .018f, .018f), tire);
            binding.headlights = ExampleLamp(body.transform, "Low", new Vector3(0, .15f, 2.17f), Color.white, 900, 60, lamp, true);
            binding.headlights.mode = VehicleLampMode.Low;
            binding.highBeams = ExampleLamp(body.transform, "High", new Vector3(0, .25f, 2.16f), Color.white, 1700, 100, lamp, true);
            binding.tailLights = ExampleLamp(body.transform, "Tail", new Vector3(0, .15f, -2.17f), Color.red, 2, 4, lamp, false);
            binding.brakeLights = new VehicleLampBinding { id = "brake", lights = binding.tailLights.lights, emissive = binding.tailLights.emissive, color = Color.red, intensity = 8, emissionIntensity = 8, range = 5 };
            binding.reverseLights = ExampleLamp(body.transform, "Reverse", new Vector3(0, -.05f, -2.17f), Color.white, 5, 5, lamp, false);
            binding.daytimeRunning = ExampleLamp(body.transform, "DRL", new Vector3(0, -.03f, 2.18f), Color.white, 1, 2, lamp, false);
            binding.leftIndicator = ExampleLamp(body.transform, "Left indicator", new Vector3(-.9f, .2f, 0), new Color(1, .35f, 0), 3, 3, lamp, false);
            binding.rightIndicator = ExampleLamp(body.transform, "Right indicator", new Vector3(.9f, .2f, 0), new Color(1, .35f, 0), 3, 3, lamp, false);
            binding.wheels = new VehicleWheelPresentationBinding[4];
            for (int i = 0; i < 4; i++) binding.wheels[i] = new VehicleWheelPresentationBinding { id = new[] { "FL", "FR", "RL", "RR" }[i], caliper = ExampleShape(body.transform, "Caliper " + i, new Vector3(i % 2 == 0 ? -.82f : .82f, -.45f, i < 2 ? 1.3f : -1.3f), new Vector3(.06f, .18f, .11f), paint).transform };
            var mirrorBindings = new List<VehicleMirrorBinding>();
            foreach (var point in new[] { new Vector3(-1, .65f, .45f), new Vector3(1, .65f, .45f), new Vector3(0, .88f, .65f) })
            {
                var view = ExampleNode(body.transform, "Rear-facing mirror camera", point); view.localRotation = Quaternion.Euler(0, 180, 0);
                var surface = ExampleShape(body.transform, "Mirror glass", point, new Vector3(.22f, .13f, 1), mirror, PrimitiveType.Quad).GetComponent<Renderer>();
                mirrorBindings.Add(new VehicleMirrorBinding { id = "mirror-" + mirrorBindings.Count, view = view, surfaces = new[] { surface }, horizontalFlip = true, resolution = 256, refreshSeconds = .1f, nearClip = .05f, farClip = 100 });
            }
            var mirrors = body.AddComponent<VehicleMirrorRenderer>();
            using (var serialized = new SerializedObject(mirrors)) { serialized.FindProperty("textureProperty").stringValue = "_UnlitColorMap"; serialized.ApplyModifiedPropertiesWithoutUndo(); }
            mirrors.Configure(null, mirrorBindings.ToArray()); mirrors.enabled = false; // Source is authoring data; assembly enables the known runtime component.
            using (var serialized = new SerializedObject(mirrors)) { serialized.FindProperty("observerCamera").objectReferenceValue = null; serialized.ApplyModifiedPropertiesWithoutUndo(); }
            var bodyPrefab = PrefabUtility.SaveAsPrefabAsset(body, path + "/BodySource.prefab"); Object.DestroyImmediate(body);
            draft.assembly.bodySource = bodyPrefab; draft.assembly.presentationSource = bodyPrefab.GetComponent<VehiclePresentationBindings>(); draft.assembly.mirrorSource = bodyPrefab.GetComponent<VehicleMirrorRenderer>();
            draft.assembly.colliderCenter = new Vector3(0, .1f, 0); draft.assembly.colliderSize = new Vector3(1.75f, .5f, 4.1f); draft.assembly.includePlayerInput = true;
            draft.assembly.sockets = new[]
            {
                new VehicleAssemblySocket { id = "front-bumper", position = new Vector3(0, .02f, 2.05f), stockRenderers = new[] { bodyPrefab.transform.Find("Front bumper").GetComponent<Renderer>() } },
                new VehicleAssemblySocket { id = "rear-bumper", position = new Vector3(0, .02f, -2.05f), stockRenderers = new[] { bodyPrefab.transform.Find("Rear bumper").GetComponent<Renderer>() } },
                new VehicleAssemblySocket { id = "paint", category = VehicleCustomizationCategory.Paint, stockRenderers = new[] { bodyPrefab.transform.Find("Body").GetComponent<Renderer>() } },
                new VehicleAssemblySocket { id = "glass", category = VehicleCustomizationCategory.WindowTint, stockRenderers = bodyPrefab.GetComponent<VehiclePresentationBindings>().sideWindows },
            };
            for (int i = 0; i < 4; i++) draft.assembly.wheels[i] = new VehicleAssemblyWheel { visualSource = wheelPrefab, suspensionAnchor = new Vector3(i % 2 == 0 ? -.82f : .82f, -.13f, i < 2 ? 1.3f : -1.3f), driven = layout == VehicleDriveLayout.Fwd ? i < 2 : i >= 2, handbrake = i >= 2 };
            var upgrade = SaveExample(ScriptableObject.CreateInstance<TunedVehiclePerformanceUpgrade>(), path + "/EngineStreet.asset");
            upgrade.ConfigureMetadata(id + "-engine-street", "Street engine +20% torque", VehiclePerformanceCategory.Engine, VehiclePerformanceTier.Street, 1800, false);
            upgrade.ConfigureModifier(new VehiclePerformanceModifier { engineTorqueMultiplier = 1.2f });
            using (var obj = new SerializedObject(upgrade)) { SetStrings(obj, "compatibleVehicleIds", new[] { id }); SetStrings(obj, "compatibleVariantIds", new[] { "street" }); obj.ApplyModifiedPropertiesWithoutUndo(); }
            draft.performance = SaveExample(ScriptableObject.CreateInstance<VehiclePerformanceCatalog>(), path + "/Performance.asset"); draft.performance.SetUpgrades(new VehiclePerformanceUpgradeDefinition[] { upgrade }); definition.performanceCatalog = draft.performance;
            var kit = SaveExample(ScriptableObject.CreateInstance<AssetVehicleCustomization>(), path + "/SportKit.asset");
            kit.ConfigureMetadata(id + "-sport-kit", "Sport front and rear bumper kit", VehicleCustomizationCategory.BodyKit, VehicleCustomizationStyle.Sport, 2500, false); kit.ConfigureCompatibility(new[] { id }, new[] { "street" });
            var replacement = new GameObject("Sport bumper"); ExampleShape(replacement.transform, "Bumper", Vector3.zero, new Vector3(1.88f, .25f, .25f), paint);
            var bumperPrefab = PrefabUtility.SaveAsPrefabAsset(replacement, path + "/SportBumper.prefab"); Object.DestroyImmediate(replacement);
            ConfigureExampleMounts(kit, new[] { "front-bumper", "rear-bumper" }, bumperPrefab);
            var paintPart = SaveExample(ScriptableObject.CreateInstance<AssetVehicleCustomization>(), path + "/GraphitePaint.asset");
            paintPart.ConfigureMetadata(id + "-graphite", "Graphite finish", VehicleCustomizationCategory.Paint, VehicleCustomizationStyle.Standard, 700, false); paintPart.ConfigureCompatibility(new[] { id }, new[] { "street" });
            var colorPayload = new VehicleCustomizationVisualPayload(); colorPayload.Configure(null, null, null, null, new Color(.12f, .13f, .15f), true, 1); paintPart.ConfigureVisual(colorPayload); SetExampleSlots(paintPart, new[] { "paint" });
            var tintPart = SaveExample(ScriptableObject.CreateInstance<AssetVehicleCustomization>(), path + "/WindowTint.asset");
            tintPart.ConfigureMetadata(id + "-tint", "Side window tint", VehicleCustomizationCategory.WindowTint, VehicleCustomizationStyle.Standard, 250, false); tintPart.ConfigureCompatibility(new[] { id }, new[] { "street" });
            var tintPayload = new VehicleCustomizationVisualPayload(); tintPayload.Configure(null, null, null, null, new Color(.05f, .07f, .09f, .5f), true, .5f); tintPart.ConfigureVisual(tintPayload); SetExampleSlots(tintPart, new[] { "glass" });
            draft.customization = SaveExample(ScriptableObject.CreateInstance<VehicleCustomizationCatalog>(), path + "/Customization.asset"); draft.customization.SetItems(new VehicleCustomizationDefinition[] { kit, paintPart, tintPart }); definition.customizationCatalog = draft.customization;
            var audioProfiles = AssetDatabase.FindAssets("t:VehicleSensoryProfile", new[] { "Assets/NfsMw/Content/Vehicles" }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            string audioPath = audioProfiles.FirstOrDefault(p => p.IndexOf(layout == VehicleDriveLayout.Fwd ? "gti" : "m3gtr", StringComparison.OrdinalIgnoreCase) >= 0);
            if (audioPath != null) draft.audio = AssetDatabase.LoadAssetAtPath<VehicleSensoryProfile>(audioPath);
            draft.physicsLabSetup = SaveExample(ScriptableObject.CreateInstance<RacingVehicleSetup>(), path + "/PhysicsSetup.asset"); draft.physicsLabSetup.definition = definition; draft.physicsLabSetup.tuning = tuning;
            draft.physicsLabSetup.wheelbase = 2.6f; draft.physicsLabSetup.trackWidth = 1.64f; draft.physicsLabSetup.dimensions = draft.assembly.colliderSize;
            var track = SaveExample(ScriptableObject.CreateInstance<VehiclePhysicsLabTrack>(), path + "/LaunchTrack.asset"); track.length = 1200; track.width = 80;
            draft.physicsLabExperiment = SaveExample(ScriptableObject.CreateInstance<VehiclePhysicsLabDefinition>(), path + "/LaunchExperiment.asset"); draft.physicsLabExperiment.vehicle = draft.physicsLabSetup; draft.physicsLabExperiment.track = track; draft.physicsLabExperiment.displayName = tuning.displayName + " standing launch"; draft.physicsLabExperiment.safety.maximumSeconds = 15; draft.physicsLabExperiment.capture.maximumSamples = 1024;
            foreach (var guid in AssetDatabase.FindAssets("", new[] { path }))
            { var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)); if (asset is ScriptableObject || asset is Material) EditorUtility.SetDirty(asset); }
            EditorUtility.SetDirty(draft); EditorUtility.SetDirty(definition); AssetDatabase.SaveAssets();
            draft.vehiclePrefab = VehicleProfileAssembly.CreatePrefab(draft, path + "/Vehicle.prefab"); definition.prefab = draft.vehiclePrefab.GetComponent<VehicleController>();
            EditorUtility.SetDirty(draft); EditorUtility.SetDirty(definition); AssetDatabase.SaveAssets();
            return draft;
        }

        private static T SaveExample<T>(T asset, string path) where T : Object { AssetDatabase.CreateAsset(asset, path); return asset; }
        private static Material ExampleMaterial(string path, string name, Color color)
        { var material = new Material(Shader.Find("HDRP/Lit")); material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .65f); HDMaterial.ValidateMaterial(material); return SaveExample(material, path + "/" + name + ".mat"); }
        private static Transform ExampleNode(Transform parent, string name, Vector3 position)
        { var node = new GameObject(name).transform; node.SetParent(parent, false); node.localPosition = position; return node; }
        private static GameObject ExampleShape(Transform parent, string name, Vector3 position, Vector3 scale, Material material, PrimitiveType primitive = PrimitiveType.Cube)
        { var shape = GameObject.CreatePrimitive(primitive); shape.name = name; shape.transform.SetParent(parent, false); shape.transform.localPosition = position; shape.transform.localScale = scale; shape.GetComponent<Renderer>().sharedMaterial = material; Object.DestroyImmediate(shape.GetComponent<Collider>()); return shape; }
        internal static Mesh CreateExampleSteeringRim()
        {
            const int segments = 32, sides = 8;
            var vertices = new Vector3[segments * sides]; var normals = new Vector3[vertices.Length]; var triangles = new int[segments * sides * 6];
            for (int ring = 0; ring < segments; ring++) for (int side = 0; side < sides; side++)
            {
                float angle = ring * 2 * Mathf.PI / segments, tube = side * 2 * Mathf.PI / sides;
                Vector3 radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                int vertex = ring * sides + side; normals[vertex] = radial * Mathf.Cos(tube) + Vector3.forward * Mathf.Sin(tube);
                vertices[vertex] = radial * .14f + normals[vertex] * .014f;
                int next = (ring + 1) % segments * sides + side, adjacent = ring * sides + (side + 1) % sides, diagonal = (ring + 1) % segments * sides + (side + 1) % sides;
                int index = vertex * 6; triangles[index] = vertex; triangles[index + 1] = next; triangles[index + 2] = adjacent; triangles[index + 3] = adjacent; triangles[index + 4] = next; triangles[index + 5] = diagonal;
            }
            var mesh = new Mesh { name = "Steering rim", vertices = vertices, normals = normals, triangles = triangles }; mesh.RecalculateBounds(); return mesh;
        }
        private static VehicleGaugeBinding ExampleGauge(Transform parent, string name, Vector3 position, float max, Material material)
        { var node = ExampleNode(parent, name, position); var needle = ExampleShape(node, "Needle", new Vector3(0, .025f, 0), new Vector3(.007f, .06f, .008f), material); return new VehicleGaugeBinding { id = name, needle = needle.transform, maximumValue = max }; }
        private static VehicleLampBinding ExampleLamp(Transform parent, string name, Vector3 position, Color color, float intensity, float range, Material material, bool beam)
        {
            var lights = new List<Light>(); var renderers = new List<Renderer>();
            foreach (float x in new[] { -.58f, .58f })
            { var node = ExampleNode(parent, name, position + (Mathf.Abs(position.x) > .1f ? Vector3.zero : Vector3.right * x)); var light = node.gameObject.AddComponent<Light>(); light.type = beam ? LightType.Spot : LightType.Point; light.range = range; light.intensity = intensity; light.color = color; light.spotAngle = 60; light.innerSpotAngle = 30; node.gameObject.AddComponent<HDAdditionalLightData>(); light.lightUnit = UnityEngine.Rendering.LightUnit.Lumen; lights.Add(light); renderers.Add(ExampleShape(node, "Lens", Vector3.zero, new Vector3(.25f, .1f, .035f), material).GetComponent<Renderer>()); if (Mathf.Abs(position.x) > .1f) break; }
            return new VehicleLampBinding { id = name, lights = lights.ToArray(), emissive = renderers.ToArray(), color = color, intensity = intensity, range = range };
        }
        private static void SetStrings(SerializedObject obj, string field, string[] values)
        { var property = obj.FindProperty(field); property.arraySize = values.Length; for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).stringValue = values[i]; }
        private static void SetExampleSlots(VehicleCustomizationDefinition part, string[] slots)
        { using var obj = new SerializedObject(part); SetStrings(obj, "mountSlotIds", slots); SetStrings(obj, "requiredSlotIds", slots); obj.ApplyModifiedPropertiesWithoutUndo(); }
        private static void ConfigureExampleMounts(VehicleCustomizationDefinition part, string[] slots, GameObject prefab)
        {
            using var obj = new SerializedObject(part); SetStrings(obj, "mountSlotIds", slots); SetStrings(obj, "requiredSlotIds", slots);
            var mounts = obj.FindProperty("mounts"); mounts.arraySize = slots.Length;
            for (int i = 0; i < slots.Length; i++) { var mount = mounts.GetArrayElementAtIndex(i); mount.FindPropertyRelative("slotId").stringValue = slots[i]; mount.FindPropertyRelative("visual").FindPropertyRelative("replacementPrefab").objectReferenceValue = prefab; }
            obj.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildFrameworkDrivingScene(VehicleProfileDraft draft)
        {
            string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(draft)).Replace('\\', '/');
            string scenePath = folder + "/Driving.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var asphalt = ExampleMaterial(folder, "CourseAsphalt", new Color(.06f, .065f, .07f));
            var grass = ExampleMaterial(folder, "CourseGrass", new Color(.07f, .14f, .05f));
            var curb = ExampleMaterial(folder, "CourseCurb", new Color(.45f, .12f, .09f));
            var lane = ExampleMaterial(folder, "CourseLane", new Color(.8f, .8f, .72f));
            CreateTrack(asphalt, grass, curb, lane); CreateLighting();
            var vehicle = ((GameObject)PrefabUtility.InstantiatePrefab(draft.vehiclePrefab, scene)).GetComponent<VehicleController>(); vehicle.transform.position = new Vector3(0, .8f, -65);
            var products = new List<ScriptableObject>(); products.AddRange(draft.performance.Upgrades); products.AddRange(draft.customization.Items);
            var catalog = SaveExample(ScriptableObject.CreateInstance<VehicleStoreCatalog>(), folder + "/Store.asset"); catalog.SetProducts(products.ToArray());
            var store = SaveExample(ScriptableObject.CreateInstance<AssetVehicleStorefront>(), folder + "/Garage.asset"); store.Configure(draft.modelId + "-garage", "Vehicle workshop", VehicleStoreCategory.OneStopShop, catalog);
            EditorUtility.SetDirty(catalog); EditorUtility.SetDirty(store);
            AttachVehicleCareer(vehicle.gameObject, catalog, store, GetOrCreateBountyRules());
            var camera = CreateCamera(vehicle); vehicle.ConfigureForRuntime(draft.tuning, vehicle.GetComponent<VehicleInputAuthority>(), vehicle.Wheels, camera);
            camera.SetCockpitAnchor(vehicle.GetComponent<VehiclePresentationBindings>().cockpitCameraAnchor);
            var mirror = vehicle.GetComponent<VehicleMirrorRenderer>(); mirror.enabled = true;
            using (var obj = new SerializedObject(mirror)) { obj.FindProperty("observerCamera").objectReferenceValue = camera.GetComponent<Camera>(); obj.FindProperty("observerRig").objectReferenceValue = camera; obj.ApplyModifiedPropertiesWithoutUndo(); }
            var profile = vehicle.GetComponent<CareerProfileSystem>(); profile.SetProfileId("framework-" + draft.modelId); profile.SetActiveVehicleId("framework-instance-" + draft.modelId);
            var harness = new GameObject("Existing workshop interface").AddComponent<VehicleShopTestHarness>();
            harness.Configure(vehicle.GetComponent<VehicleStoreSystem>(), vehicle.GetComponent<VehicleStoreWallet>(), vehicle.GetComponent<VehicleStoreOwnership>(), vehicle.GetComponent<VehicleStoreGarage>(), profile, new[] { store });
            vehicle.gameObject.AddComponent<VehicleTelemetryHud>();
            SaveTestScene(scene, scenePath, vehicle.gameObject, "Built authored vehicle driving scene: ");
        }
    }
}
