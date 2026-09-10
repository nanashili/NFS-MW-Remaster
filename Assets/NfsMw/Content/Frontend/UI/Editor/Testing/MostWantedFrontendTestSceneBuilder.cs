#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// Explicit, create-only authoring for the frontend's isolated test scenes.
    /// Never opens a production scene, instantiates the driving prefab, edits an
    /// existing asset, or changes the project's scene/build configuration.
    /// </summary>
    public static class MostWantedFrontendTestSceneBuilder
    {
        public const string Root = "Assets/NfsMw/Content/Frontend/UI/Tests/Scenes";
        public const string DataRoot = Root + "/Data";
        public const string FixturePath = Root + "/Fixture.prefab";
        public const string SettingsPath = DataRoot + "/Settings.asset";
        public const string GalleryPath = Root + "/FrontendGallery.unity";
        public const string SourceVehiclePath = "Assets/NfsMw/Content/Vehicles/Street/BMW/M3 E42/Framework/Vehicle.prefab";
        public const int PaintCount = 80;
        public const int RaceCount = 10;
        public const int PursuitCount = 7;
        public const int LocationCount = 9;
        public const int StartingCash = 100000;

        public static string ScenePath(MostWantedFrontendPage page)
        {
            if (!MostWantedFrontendTestDriver.Pages.Contains(page))
                throw new ArgumentOutOfRangeException(nameof(page), "This page needs a real application transition.");
            return Root + "/" + page + "Test.unity";
        }

        [MenuItem("NFS MW Remaster/Frontend Tests/Create Missing Test Scenes")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before authoring frontend test scenes.");
            if (MostWantedFrontendTestDriver.Pages.Length != 26)
                throw new InvalidOperationException("Review the test scene catalog when the frontend page contract changes.");

            EnsureFolder(Root);
            EnsureFolder(DataRoot);
            EnsureFolder(DataRoot + "/Paints");
            EnsureFolder(DataRoot + "/Audio");
            var catalog = CreatePaintCatalog();
            var storeCatalog = Asset<VehicleStoreCatalog>(DataRoot + "/StoreCatalog.asset", value =>
                value.SetProducts(catalog.Items.Cast<ScriptableObject>().ToArray()));
            var bodyShop = Asset<AssetVehicleStorefront>(DataRoot + "/BodyShop.asset", value =>
                value.Configure("frontend_test_body_shop", "Test Body Shop", VehicleStoreCategory.BodyShop, storeCatalog));
            var tuning = Asset<VehicleTuning>(DataRoot + "/FixtureTuning.asset", null, VehicleTuning.CreateStreetRacer);
            var content = CreateContent();
            var showroom = CreateShowroom();
            var settings = Asset<GameFlowSettings>(SettingsPath, value =>
            {
                var serialized = new SerializedObject(value);
                Property(serialized, "worldScenePath").stringValue = "frontend-ui-test-fixture";
                Property(serialized, "defaultAlias").stringValue = "frontend_test";
                Property(serialized, "bootSeconds").floatValue = 0;
                Property(serialized, "eventPreparationSeconds").floatValue = .1f;
                Property(serialized, "pauseOnFocusLoss").boolValue = false;
                Property(serialized, "frontendContent").objectReferenceValue = content;
                Property(serialized, "showroom").objectReferenceValue = showroom;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            });
            var fixture = CreateFixture(tuning, catalog, storeCatalog, bodyShop, CreateDiagnosticMusic());
            ValidateFixture(fixture);

            int created = CreateScene(GalleryPath, MostWantedFrontendPage.MainMenu, settings, fixture) ? 1 : 0;
            foreach (var page in MostWantedFrontendTestDriver.Pages)
                if (CreateScene(ScenePath(page), page, settings, fixture)) created++;

            var manifest = new GenerationManifest
            {
                sourcePrefabSha256 = FileHash(SourceVehiclePath),
                sourceDependencyHash = AssetDatabase.GetAssetDependencyHash(SourceVehiclePath).ToString(),
                showroomPartCount = showroom.parts.Length,
                showroomPaintPartCount = showroom.parts.Count(part => part.paintable),
                scenes = new[] { new SceneEntry { path = GalleryPath, page = MostWantedFrontendPage.MainMenu.ToString() } }
                    .Concat(MostWantedFrontendTestDriver.Pages.Select(page => new SceneEntry
                    { path = ScenePath(page), page = page.ToString() })).ToArray()
            };
            WriteNewText(Root + "/Generation.json", JsonUtility.ToJson(manifest, true) + "\n");
            Debug.Log($"FRONTEND_TEST_SCENES_READY created={created} preserved={27 - created} " +
                $"total=27 fixture={FixturePath} paints={catalog.Items.Count} " +
                $"showroomParts={showroom.parts.Length}. No production scenes or Build Settings were changed.");
        }

        /// <summary>Called by the prime's isolated Unity authoring process, never launches Unity itself.</summary>
        public static void BuildBatch()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("BuildBatch is reserved for the isolated batch authoring process. Use Create Missing Test Scenes interactively.");
            if (!File.Exists(".mw-frontend-validation-source"))
                throw new InvalidOperationException("Batch authoring requires an owned frontend validation workspace.");
            // Unity cannot create additive authoring scenes while its initial scene is untitled.
            // Only the isolated batch process may temporarily save that scene, never the live editor.
            string scratch = null;
            Scene initial = SceneManager.GetActiveScene();
            if (initial.IsValid() && string.IsNullOrEmpty(initial.path))
            {
                EnsureFolder(Root); EnsureFolder(DataRoot);
                scratch = AssetDatabase.GenerateUniqueAssetPath(DataRoot + "/AuthoringWorkspace.unity");
                if (!EditorSceneManager.SaveScene(initial, scratch))
                    throw new IOException("Could not prepare the isolated authoring scene.");
            }
            try { Build(); }
            finally
            {
                if (scratch != null)
                {
                    if (initial.IsValid() && initial.isLoaded) EditorSceneManager.CloseScene(initial, true);
                    AssetDatabase.DeleteAsset(scratch);
                }
            }
        }

        private static VehicleCustomizationCatalog CreatePaintCatalog()
        {
            var items = new VehicleCustomizationDefinition[PaintCount];
            for (int i = 0; i < items.Length; i++)
            {
                int index = i;
                items[i] = Asset<AssetVehicleCustomization>($"{DataRoot}/Paints/Paint{index:00}.asset", value =>
                {
                    // These are deliberately authored test swatches, not a recovered factory palette.
                    Color color = Color.HSVToRGB((index % 20) / 20f, .3f + index / 20 * .2f, .9f - index / 20 * .14f);
                    value.ConfigureMetadata($"frontend_test_paint_{index:00}", $"Test Paint {index + 1:00}",
                        VehicleCustomizationCategory.Paint, VehicleCustomizationStyle.Standard, 1000, false);
                    var visual = new VehicleCustomizationVisualPayload();
                    visual.Configure(null, Array.Empty<Material>(), null, null, color, true, 1);
                    value.ConfigureVisual(visual);
                });
            }
            return Asset<VehicleCustomizationCatalog>(DataRoot + "/PaintCatalog.asset", value => value.SetItems(items));
        }

        private static MostWantedFrontendContent CreateContent()
        {
            return Asset<MostWantedFrontendContent>(DataRoot + "/Content.asset", value =>
            {
                value.soundtrack = AssetDatabase.LoadAssetAtPath<SensoryMusicProfile>(DrivingGameFlowBuilder.SoundtrackPath);
                value.rivals = new[]
                {
                    new MostWantedFrontendContent.Rival
                    {
                        id = "frontend_test_rival", displayName = "Test Rival", fullName = "Frontend Test Fixture",
                        rank = 1, carName = "BMW renderer-only test preview", strength = "UI layout and navigation testing",
                        portraitTexture = "RIVAL_01", backgroundTexture = "RIVAL_01_BG",
                        biography = "This is authored test metadata using locally recovered rival artwork. " +
                            "The map, events, wallet and paint prices belong only to this UI fixture. " +
                            "Race wins, milestones and bounty begin at zero; no production career progress is imported or invented. " +
                            "Use F6/F7 to compare pages. Stop Play Mode to discard the in-memory test profile.",
                        challengeEventIds = new[] { RaceId(0), RaceId(1) }
                    }
                };
                value.credits = "NFS MW Remaster\nFrontend Test Scenes\n\n" +
                    "Production frontend with isolated, in-memory test services.\n" +
                    "Authored test map, event markers, rival metadata and paint swatches.\n\n" +
                    "Original game and recovered artwork\nElectronic Arts / EA Black Box\n\n" +
                    "Local recovery provenance: Tools/FrontendAssets.\n" +
                    "Preview meshes reference the existing BMW framework prefab without running its gameplay components.\n\n" +
                    "LAN and online authentication remain unavailable; these scenes do not emulate either service.";
            });
        }

        private static MostWantedShowroomDefinition CreateShowroom()
        {
            return Asset<MostWantedShowroomDefinition>(DataRoot + "/Showroom.asset", value =>
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceVehiclePath);
                if (source == null) throw new FileNotFoundException("The existing BMW preview source is unavailable.", SourceVehiclePath);
                var paintTargets = new HashSet<Renderer>();
                foreach (var slot in source.GetComponentsInChildren<VehicleCustomizationVisualSlot>(true))
                {
                    if (slot.Category != VehicleCustomizationCategory.Paint) continue;
                    var renderers = Property(new SerializedObject(slot), "targetRenderers");
                    for (int i = 0; i < renderers.arraySize; i++)
                        if (renderers.GetArrayElementAtIndex(i).objectReferenceValue is Renderer renderer) paintTargets.Add(renderer);
                }
                var parts = new List<MostWantedShowroomDefinition.MeshPart>();
                Bounds bounds = default;
                bool hasBounds = false;
                foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (filter.sharedMesh == null || renderer == null || !renderer.enabled || !IsAuthoredActive(filter.transform, source.transform)) continue;
                    if (parts.Count >= 1024) throw new InvalidOperationException("The test showroom exceeds its 1024-part authoring limit.");
                    var matrix = source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    Decompose(matrix, out Vector3 position, out Quaternion rotation, out Vector3 scale);
                    var materials = renderer.sharedMaterials;
                    if (materials.Length == 0 || materials.Any(material => material == null || !EditorUtility.IsPersistent(material)))
                        throw new InvalidOperationException("Preview part needs persistent assigned materials: " + filter.name);
                    parts.Add(new MostWantedShowroomDefinition.MeshPart
                    {
                        name = AnimationUtility.CalculateTransformPath(filter.transform, source.transform),
                        mesh = filter.sharedMesh, materials = materials, position = position,
                        rotation = rotation, scale = scale, paintable = paintTargets.Contains(renderer)
                    });
                    Bounds localBounds = filter.sharedMesh.bounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 point = matrix.MultiplyPoint3x4(localBounds.center + Vector3.Scale(localBounds.extents,
                            new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                        if (!hasBounds) { bounds = new Bounds(point, Vector3.zero); hasBounds = true; }
                        else bounds.Encapsulate(point);
                    }
                }
                if (parts.Count == 0 || !parts.Any(part => part.paintable))
                    throw new InvalidOperationException("No usable renderer-only vehicle or authored Paint slot was found.");
                value.vehicleId = "frontend_test_vehicle";
                value.displayName = "BMW frontend test preview";
                value.sourcePrefab = SourceVehiclePath;
                value.bounds = bounds;
                value.parts = parts.ToArray();
            });
        }

        private static GameObject CreateFixture(VehicleTuning tuning, VehicleCustomizationCatalog customizations,
            VehicleStoreCatalog catalog, AssetVehicleStorefront bodyShop, SensoryMusicProfile musicProfile)
        {
            if (Existing(FixturePath)) return RequiredAsset<GameObject>(FixturePath);
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            GameObject root = null;
            try
            {
                SceneManager.SetActiveScene(scene);
                root = new GameObject("Frontend UI Fixture - authored test data only");
                root.SetActive(false);
                // The player must not parent locations: the driver moves it between services.
                var player = Child(root.transform, "Test Player Services");
                var world = Child(root.transform, "Test World - no traffic or driving geometry");
                var roads = world.AddComponent<RoadNetwork>();
                roads.Configure(CreateRoadNodes());
                var audio = world.AddComponent<SensoryAudioWorld>();
                var music = world.AddComponent<AdaptiveMusic>();
                music.Configure(audio, musicProfile);
                var vehicle = player.AddComponent<VehicleController>();
                vehicle.enabled = false;
                var wallet = player.AddComponent<VehicleStoreWallet>(); wallet.SetBalance(StartingCash);
                var bounty = player.AddComponent<VehicleBountySystem>(); bounty.SetStartingHeatLevel(0);
                var ownership = player.AddComponent<VehicleStoreOwnership>();
                var garage = player.AddComponent<VehicleStoreGarage>();
                var customization = player.AddComponent<VehicleCustomizationSystem>(); customization.SetCatalog(customizations);
                var visual = player.AddComponent<VehicleCustomizationVisualAdapter>(); customization.SetVisualAdapter(visual);
                var adapter = player.AddComponent<VehicleCustomizationStoreAdapter>();
                var store = player.AddComponent<VehicleStoreSystem>();
                store.SetCatalog(catalog); store.SetCurrentVehicle(vehicle); store.SetWallet(wallet);
                store.SetOwnership(ownership); store.SetGarage(garage); store.SetAdapters(new MonoBehaviour[] { adapter });
                store.SetDefaultStore(bodyShop);
                var memory = player.AddComponent<MostWantedFrontendTestStorage>();
                var career = player.AddComponent<CareerProfileSystem>();
                career.ConfigureAutomaticPersistence(false, false, false);
                career.SetStorage(memory); career.SetProfileId("frontend_test");
                career.SetPlayerName("Frontend Tester"); career.SetActiveVehicleId("frontend_test_vehicle");
                var locations = CreateLocations(world.transform, bodyShop);
                var events = CreateEvents(world.transform);
                var session = player.AddComponent<FreeRoamSession>();
                session.Configure(vehicle, roads, null, locations, events, false);
                career.SetParticipants(new MonoBehaviour[] { wallet, bounty, ownership, garage, customization, session });
                // Initialize the existing module host only after the customization installer is present.
                vehicle.ConfigureForRuntime(tuning, null, Array.Empty<VehicleWheel>());
                var body = player.GetComponent<Rigidbody>();
                body.useGravity = false; body.isKinematic = false; body.detectCollisions = false;
                body.constraints = RigidbodyConstraints.FreezeAll;
                body.interpolation = RigidbodyInterpolation.None;
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                ValidateFixture(root);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, FixturePath, out bool succeeded);
                if (!succeeded || prefab == null) throw new IOException("Unity did not save " + FixturePath);
                return prefab;
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        private static RoadNode[] CreateRoadNodes()
        {
            var nodes = new RoadNode[25];
            for (int z = 0; z < 5; z++)
            for (int x = 0; x < 5; x++)
            {
                var exits = new List<int>(4);
                if (x > 0) exits.Add(z * 5 + x - 1);
                if (x < 4) exits.Add(z * 5 + x + 1);
                if (z > 0) exits.Add((z - 1) * 5 + x);
                if (z < 4) exits.Add((z + 1) * 5 + x);
                nodes[z * 5 + x] = new RoadNode { position = new Vector3((x - 2) * 60, 0, (z - 2) * 60), exits = exits.ToArray() };
            }
            return nodes;
        }

        private static WorldLocation[] CreateLocations(Transform parent, AssetVehicleStorefront bodyShop)
        {
            var kinds = new[] { WorldLocationKind.Safehouse, WorldLocationKind.BodyShop, WorldLocationKind.Garage,
                WorldLocationKind.PoliceStation, WorldLocationKind.Safehouse, WorldLocationKind.CarShow,
                WorldLocationKind.BodyShop, WorldLocationKind.PerformanceShop, WorldLocationKind.PoliceStation };
            var result = new WorldLocation[kinds.Length];
            for (int i = 0; i < result.Length; i++)
            {
                var node = Child(parent, $"Test Location {i + 1:00} - {kinds[i]}");
                node.transform.localPosition = i == 0 ? Vector3.zero : new Vector3((i % 3 - 1) * 120, 0, (i / 3 - 1) * 120);
                var location = node.AddComponent<WorldLocation>();
                location.Configure($"frontend_test_location_{i:00}", $"Test {kinds[i]} {i + 1:00}", kinds[i],
                    kinds[i] == WorldLocationKind.BodyShop || kinds[i] == WorldLocationKind.Garage ? bodyShop : null);
                result[i] = location;
            }
            return result;
        }

        private static string RaceId(int index) => $"frontend_test_race_{index:00}";

        private static FreeRoamEventDefinition[] CreateEvents(Transform parent)
        {
            var result = new FreeRoamEventDefinition[RaceCount + PursuitCount];
            var kinds = new[] { FreeRoamEventKind.Sprint, FreeRoamEventKind.Circuit, FreeRoamEventKind.Drag, FreeRoamEventKind.Speedtrap };
            for (int i = 0; i < result.Length; i++)
            {
                bool pursuit = i >= RaceCount;
                int index = pursuit ? i - RaceCount : i;
                var kind = pursuit ? FreeRoamEventKind.Pursuit : kinds[index % kinds.Length];
                var node = Child(parent, $"Test {kind} {index + 1:00}");
                var start = new Vector3((i % 5 - 2) * 60, 0, (i / 5 - 2) * 60);
                node.transform.localPosition = start;
                var definition = node.AddComponent<FreeRoamEventDefinition>();
                definition.Configure(pursuit ? $"frontend_test_pursuit_{index:00}" : RaceId(index), node.name, kind,
                    new[] { start + Vector3.forward * 30, start + Vector3.forward * 60 },
                    kind == FreeRoamEventKind.Circuit ? 2 : 1, 180, 1000 + index * 100, 100);
                if (!definition.TryValidate(out string failure)) throw new InvalidOperationException(failure);
                result[i] = definition;
            }
            return result;
        }

        public static void ValidateFixture(GameObject fixture)
        {
            if (fixture == null || fixture.activeSelf) throw new InvalidOperationException("The frontend fixture must be saved inactive.");
            var sessions = fixture.GetComponentsInChildren<FreeRoamSession>(true);
            if (sessions.Length != 1) throw new InvalidOperationException("Expected exactly one test FreeRoamSession.");
            var session = sessions[0];
            var vehicle = session.GetComponent<VehicleController>();
            var body = session.GetComponent<Rigidbody>();
            var career = session.GetComponent<CareerProfileSystem>();
            // detectCollisions is runtime state, not a persisted prefab guarantee. The test loader
            // disables collision detection on every instantiated body before activating the fixture.
            if (vehicle == null || vehicle.enabled || body == null || body.useGravity || body.isKinematic
                || body.constraints != RigidbodyConstraints.FreezeAll || vehicle.FactoryTuning == null)
                throw new InvalidOperationException("Invalid frontend test player: controller=" + (vehicle != null)
                    + ", enabled=" + (vehicle != null && vehicle.enabled) + ", body=" + (body != null)
                    + ", gravity=" + (body != null && body.useGravity) + ", kinematic=" + (body != null && body.isKinematic)
                    + ", constraints=" + (body != null ? body.constraints.ToString() : "missing")
                    + ", tuning=" + (vehicle != null && vehicle.FactoryTuning != null));
            if (fixture.GetComponentsInChildren<VehicleWheel>(true).Length != 0
                || fixture.GetComponentsInChildren<JsonCareerProfileStorage>(true).Length != 0
                || fixture.GetComponentsInChildren<GameFlowRuntime>(true).Length != 0)
                throw new InvalidOperationException("The fixture contains production simulation, application or disk-storage components.");
            if (career == null || !(career.Storage is MostWantedFrontendTestStorage)
                || session.GetComponent<VehicleStoreWallet>() == null || session.GetComponent<VehicleBountySystem>() == null
                || session.GetComponent<VehicleStoreSystem>() == null || session.GetComponent<VehicleCustomizationStoreAdapter>() == null)
                throw new InvalidOperationException("The fixture is missing its real store/profile adapters or in-memory storage.");
            var profile = new SerializedObject(career);
            foreach (string key in new[] { "loadOnAwake", "saveOnApplicationPause", "saveOnApplicationQuit", "autoDiscoverStorage", "autoDiscoverParticipants" })
                if (Property(profile, key).boolValue) throw new InvalidOperationException("Test persistence/discovery must be explicit: " + key);
            if (session.Locations.Count != LocationCount || session.Events.Count != RaceCount + PursuitCount
                || session.Events.Count(item => item.Kind == FreeRoamEventKind.Pursuit) != PursuitCount)
                throw new InvalidOperationException("Unexpected fixture event/location counts.");
            if (session.Locations.Any(item => item == null || item.transform.IsChildOf(session.transform)))
                throw new InvalidOperationException("Test locations must not move with the player.");
            if (session.GetComponent<VehicleCustomizationSystem>()?.Catalog?.Items.Count != PaintCount)
                throw new InvalidOperationException("Expected the 80-swatch test paint catalog.");
            foreach (var activity in session.Events)
                if (!activity.TryValidate(out string failure)) throw new InvalidOperationException(failure);
        }

        private static bool CreateScene(string path, MostWantedFrontendPage page, GameFlowSettings settings, GameObject fixture)
        {
            if (Existing(path)) { RequiredAsset<SceneAsset>(path); return false; }
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var cameraObject = new GameObject("Test Output Camera");
                var camera = cameraObject.AddComponent<Camera>();
                Rendering.HdrpSceneDefaults.Camera(camera, false);
                camera.tag = "MainCamera";
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.035f, .04f, .035f);
                camera.cullingMask = 0; camera.depth = -100; camera.allowHDR = true; camera.allowMSAA = false;
                cameraObject.AddComponent<AudioListener>();
                var driverObject = new GameObject("Frontend Test Driver - " + page);
                driverObject.SetActive(false);
                var driver = driverObject.AddComponent<MostWantedFrontendTestDriver>();
                driver.Configure(settings, page, fixture);
                driverObject.SetActive(true);
                if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("Unity did not save " + path);
                return true;
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        private static GameObject Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static SensoryMusicProfile CreateDiagnosticMusic()
        {
            var clips = new AudioClip[3];
            for (int i = 0; i < clips.Length; i++)
            {
                string path = $"{DataRoot}/Audio/PreviewTone{i + 1}.wav";
                if (!Existing(path))
                {
                    // Authored quiet, two-second test signals; never copied soundtrack material.
                    const int sampleRate = 22050, sampleCount = sampleRate * 2, dataBytes = sampleCount * 2;
                    using (var stream = new FileStream(AssetFile(path), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var writer = new BinaryWriter(stream, Encoding.ASCII))
                    {
                        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + dataBytes);
                        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                        writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate);
                        writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
                        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(dataBytes);
                        double frequency = 220 * (i + 1);
                        for (int sample = 0; sample < sampleCount; sample++)
                        {
                            double fade = Math.Min(1, Math.Min(sample, sampleCount - 1 - sample) / 441d);
                            writer.Write((short)(Math.Sin(2 * Math.PI * frequency * sample / sampleRate) * fade * .05 * short.MaxValue));
                        }
                    }
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                }
                clips[i] = RequiredAsset<AudioClip>(path);
            }
            return Asset<SensoryMusicProfile>(DataRoot + "/DiagnosticMusic.asset", value =>
            {
                value.profileId = "frontend.test.diagnostic-music";
                value.sourceRevision = "authored-two-second-test-tones";
                value.sections = clips.Select((clip, index) => new MusicSection
                {
                    stableId = "frontend.test.tone." + index, displayName = "UI Test Tone " + (index + 1),
                    bpm = 120, beatsPerBar = 4, bars = 1, fullMix = true,
                    eligibleContexts = AdaptiveMusicContext.All,
                    stems = new[] { new MusicStem { stableId = "tone", displayName = "Quiet diagnostic tone", clip = clip, gain = .5f } }
                }).ToArray();
                value.playback = new MusicPlaybackPolicy { maxConcurrentStems = 4, pauseMode = MusicPauseMode.PauseTransport };
                if (!value.Validate(out string failure)) throw new InvalidOperationException(failure);
            });
        }

        private static bool IsAuthoredActive(Transform transform, Transform root)
        {
            while (transform != null && transform != root)
            {
                if (!transform.gameObject.activeSelf) return false;
                transform = transform.parent;
            }
            // A prefab's inactive root is a spawning policy, not hidden bodywork.
            return transform == root;
        }

        private static void Decompose(Matrix4x4 matrix, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = matrix.GetColumn(3);
            Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
            scale = new Vector3(x.magnitude, y.magnitude, z.magnitude);
            if (scale.x < .000001f || scale.y < .000001f || scale.z < .000001f)
                throw new InvalidOperationException("Degenerate preview mesh transform.");
            if (Vector3.Dot(Vector3.Cross(x, y), z) < 0) scale.x = -scale.x;
            rotation = Quaternion.LookRotation(z / scale.z, y / scale.y);
            var reconstructed = Matrix4x4.TRS(position, rotation, scale);
            for (int i = 0; i < 16; i++)
                if (!float.IsFinite(matrix[i]) || Mathf.Abs(matrix[i] - reconstructed[i]) > .001f)
                    throw new InvalidOperationException("Preview publication cannot represent this source transform without shear. Source is unchanged.");
        }

        private static T Asset<T>(string path, Action<T> configure, Func<T> create = null) where T : ScriptableObject
        {
            if (Existing(path)) return RequiredAsset<T>(path);
            T value = create != null ? create() : ScriptableObject.CreateInstance<T>();
            try
            {
                value.name = Path.GetFileNameWithoutExtension(path);
                configure?.Invoke(value);
                AssetDatabase.CreateAsset(value, path);
                AssetDatabase.SaveAssetIfDirty(value);
                return value;
            }
            catch
            {
                if (value != null && !EditorUtility.IsPersistent(value)) Object.DestroyImmediate(value);
                throw;
            }
        }

        private static T RequiredAsset<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            return asset != null ? asset : throw new IOException("Existing path is not a valid " + typeof(T).Name + "; it was preserved: " + path);
        }

        private static SerializedProperty Property(SerializedObject owner, string name)
            => owner.FindProperty(name) ?? throw new InvalidOperationException("Authoring contract changed: missing " + owner.targetObject.GetType().Name + "." + name);

        private static void CheckPath(string path)
        {
            if (path != Root && !path.StartsWith(Root + "/", StringComparison.Ordinal))
                throw new ArgumentException("Frontend test writes are restricted to " + Root, nameof(path));
            if (path.Contains("..") || path.Contains('\\')) throw new ArgumentException("Use a normalized project asset path.", nameof(path));
            string directory = AssetFile(path);
            while (!string.IsNullOrEmpty(directory))
            {
                if ((File.Exists(directory) || Directory.Exists(directory)) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Test output cannot traverse a symbolic link: " + directory);
                if (directory == Path.GetFullPath(Application.dataPath)) break;
                directory = Path.GetDirectoryName(directory);
            }
        }

        private static string AssetFile(string path)
        {
            string project = Path.GetDirectoryName(Application.dataPath)
                ?? throw new InvalidOperationException("Unity has no project asset directory.");
            return Path.GetFullPath(Path.Combine(project, path));
        }

        private static bool Existing(string path)
        {
            CheckPath(path);
            string absolute = AssetFile(path);
            bool exists = File.Exists(absolute) || Directory.Exists(absolute) || AssetDatabase.LoadMainAssetAtPath(path) != null;
            if (!exists && File.Exists(absolute + ".meta"))
                throw new IOException("An orphan metadata file occupies the new test asset path; it was preserved: " + path);
            return exists;
        }

        private static void EnsureFolder(string path)
        {
            CheckPath(path);
            if (AssetDatabase.IsValidFolder(path)) return;
            string absolute = AssetFile(path);
            if (Directory.Exists(absolute))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                if (AssetDatabase.IsValidFolder(path)) return;
                throw new IOException("Existing folder could not be imported: " + path);
            }
            if (File.Exists(absolute) || File.Exists(absolute + ".meta")) throw new IOException("Existing item occupies " + path);
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (parent == null) throw new ArgumentException("Invalid test folder.", nameof(path));
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path)))) throw new IOException("Could not create " + path);
        }

        private static string FileHash(string path)
        {
            using var algorithm = SHA256.Create();
            using var input = File.OpenRead(AssetFile(path));
            return BitConverter.ToString(algorithm.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }

        private static void WriteNewText(string path, string text)
        {
            if (Existing(path)) return;
            using (var output = new FileStream(AssetFile(path), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(output, new UTF8Encoding(false))) writer.Write(text);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        [Serializable]
        private sealed class SceneEntry { public string path; public string page; }

        [Serializable]
        private sealed class GenerationManifest
        {
            public int schemaVersion = 1;
            public string generator = nameof(MostWantedFrontendTestSceneBuilder);
            public string fixture = FixturePath;
            public string settings = SettingsPath;
            public string sourcePrefab = SourceVehiclePath;
            public string sourcePrefabSha256;
            public string sourceDependencyHash;
            public string paintMapping = "Exact targetRenderers from existing VehicleCustomizationVisualSlot with Category Paint";
            public string evidenceBoundary = "Authoring inventory only; runtime smoke and rendered fidelity need separate validation. Test events, colors and rival metadata are synthetic. No production progress is seeded.";
            public int showroomPartCount;
            public int showroomPaintPartCount;
            public int paintCount = PaintCount;
            public int raceCount = RaceCount;
            public int pursuitCount = PursuitCount;
            public int locationCount = LocationCount;
            public int startingCash = StartingCash;
            public SceneEntry[] scenes;
        }
    }
}
#endif
