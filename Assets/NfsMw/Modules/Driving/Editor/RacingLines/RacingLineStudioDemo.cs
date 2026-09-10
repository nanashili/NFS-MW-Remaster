using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

namespace NfsMwRemaster.Driving.Editor
{
    public static class RacingLineStudioDemo
    {
        public const string Folder = "Assets/NfsMw/Modules/Driving/Examples/RacingLineStudio/Validated";
        public const string ScenePath = Folder + "/CompoundCorner.unity";
        public const string GripPath = Folder + "/GripLine.asset";
        public const string DriftPath = Folder + "/DriftLine.asset";

        [MenuItem("NFS MW Remaster/Racing Lines/Open Compound Corner Example")]
        public static void OpenScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) Create();
            EditorSceneManager.OpenScene(ScenePath);
            RacingLineStudioWindow.Open().SelectDocument(AssetDatabase.LoadAssetAtPath<RacingLineSource>(GripPath));
        }

        [MenuItem("NFS MW Remaster/Racing Lines/Create Compound Corner Example")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new ArgumentException("Exit Play mode before creating the example.");
            if (AssetDatabase.LoadAssetAtPath<RacingLineSource>(GripPath) != null)
            { Selection.activeObject = AssetDatabase.LoadAssetAtPath<RacingLineSource>(GripPath); return; }
            if (AssetDatabase.IsValidFolder(Folder) && AssetDatabase.FindAssets("", new[] { Folder }).Length > 0)
                throw new ArgumentException("Example folder already contains partial assets; inspect it before retrying. No existing data was overwritten.");
            var previous = SceneManager.GetActiveScene();
            bool emptyWorkspace = SceneManager.sceneCount == 1 && string.IsNullOrEmpty(previous.path) && previous.rootCount == 0;
            if (!emptyWorkspace)
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                        throw new ArgumentException("Save or close the untitled scene before creating a demo. Existing saved scenes may remain open and dirty; they will not be overwritten.");
            EnsureFolder("Assets/NfsMw/Modules/Driving/Examples"); EnsureFolder(Folder);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, emptyWorkspace ? NewSceneMode.Single : NewSceneMode.Additive);
            RacingVehicleRig rig = null;
            try
            {
                var roadMaterial = new Material(Shader.Find("HDRP/Lit"));
                roadMaterial.color = new Color(0.17f, 0.19f, 0.22f); Save(roadMaterial, "Road.mat");
                var paint = new Material(roadMaterial); paint.color = new Color(0.05f, 0.62f, 0.9f); Save(paint, "Car.mat");
                var surface = ScriptableObject.CreateInstance<SensorySurfaceProfile>(); Save(surface, "DryAsphalt.asset");
                var profile = ScriptableObject.CreateInstance<RoadProfile>(); profile.speedLimit = 30;
                profile.bands = new[] { new RoadBand { id = RoadId.New(), label = "Synthetic 14 m one-way test corridor", width = 14,
                    direction = RoadTravelDirection.Forward, material = roadMaterial, surface = surface } };
                Save(profile, "CompoundRoadProfile.asset");
                var road = RoadAuthoringCommands.Create(profile, new[] { Vector3.zero, Vector3.forward * 240 }); road.name = "Compound S-bend — synthetic calibration course";
                SceneManager.MoveGameObjectToScene(road.gameObject, scene);
                road.Reference.Spline = new Spline(new[]
                {
                    Knot(new Vector3(0, 0, 0), Vector3.forward, 23, 23),
                    Knot(new Vector3(0, 0, 70), Vector3.forward, 23, 27.614f),
                    Knot(new Vector3(50, 0, 120), Vector3.right, 27.614f, 27.614f),
                    Knot(new Vector3(100, 0, 170), Vector3.forward, 27.614f, 23),
                    Knot(new Vector3(100, 0, 240), Vector3.forward, 23, 23)
                });
                var network = RoadAuthoringCommands.CreateNetwork(new[] { road });
                RoadNetworkBake.Publish(network, Folder + "/CompoundRoad.asset");
                var route = ScriptableObject.CreateInstance<RacingLineRoute>(); route.network = network.Baked;
                route.spans = new[] { new RacingRouteSpan { laneId = network.Baked.Lanes[0].Id.ToString(), startMetres = 5, endMetres = network.Baked.Lanes[0].Length - 5 } }; Save(route, "CompoundRoute.asset");
                var gripTune = VehicleTuning.CreateStreetRacer(); gripTune.displayName = "Studio grip fixture"; gripTune.handling.brakeToDrift = false;
                Save(gripTune, "GripTuning.asset");
                var driftTune = UnityEngine.Object.Instantiate(gripTune); driftTune.displayName = "Studio B2D fixture";
                driftTune.handling.brakeToDrift = true; driftTune.handling.driftBias = 0.7f;
                // Synthetic, deliberately distinct assist tune; these are not claimed NFS reference values.
                driftTune.handling.yawGain = 6000; driftTune.handling.maximumYawTorque = 9000;
                driftTune.tires.lateralGrip = 0.45f;
                Save(driftTune, "DriftTuning.asset");
                var grip = Document("Grip", gripTune, route); var drift = Document("Drift", driftTune, route);
                drift.hints = new[] { new RacingLineHint { kind = RacingHintKind.BrakeToDrift, station = 90, radius = 10,
                    brake = 0.36f, cueSeconds = 0.36f, cueSteering = 1f } };
                EditorUtility.SetDirty(drift); AssetDatabase.SaveAssetIfDirty(drift);
                var first = network.Baked.Lanes[0].Sample(route.spans[0].startMetres);
                rig = new RacingVehicleRig(scene, grip.vehicle, first.position + first.up * RacingVehicleRig.SpawnHeight(grip.vehicle), Quaternion.LookRotation(first.forward, first.up), false);
                rig.Root.name = "Trajectory follower — no profile, wallet or save modules";
                var input = rig.Root.AddComponent<RacingLineInput>(); input.source = grip;
                rig.Vehicle.ConfigureForRuntime(gripTune, input, rig.Vehicle.Wheels);
                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube); visual.name = "Placeholder chassis"; UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
                visual.transform.SetParent(rig.Root.transform, false); visual.transform.localPosition = rig.Chassis.center; visual.transform.localScale = grip.vehicle.dimensions;
                visual.GetComponent<Renderer>().sharedMaterial = paint;
                var cameraRoot = new GameObject("Studio follow camera"); cameraRoot.tag = "MainCamera";
                SceneManager.MoveGameObjectToScene(cameraRoot, scene);
                var camera = cameraRoot.AddComponent<Camera>(); NfsMwRemaster.Driving.Editor.Rendering.HdrpSceneDefaults.Camera(camera); camera.farClipPlane = 1000; camera.backgroundColor = new Color(0.1f, 0.14f, 0.2f); camera.clearFlags = CameraClearFlags.SolidColor;
                cameraRoot.AddComponent<RacingLineDemoCamera>().target = rig.Root.transform;
                cameraRoot.transform.SetPositionAndRotation(new Vector3(0, 7, -12), Quaternion.Euler(25, 0, 0));
                var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; Rendering.HdrpSceneDefaults.Sun(light); light.transform.rotation = Quaternion.Euler(45, -25, 0);
                SceneManager.MoveGameObjectToScene(light.gameObject, scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                Selection.activeObject = grip;
                Debug.Log("RACING_LINE_EXAMPLE_CREATED: " + ScenePath + ". Generate/verify/publish a line before runtime following.");
            }
            catch
            {
                Debug.LogError("RACING_LINE_EXAMPLE_INCOMPLETE: retained partial new assets for inspection at " + Folder + ". No pre-existing assets were overwritten."); throw;
            }
            finally
            {
                rig?.Dispose();
                if (SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
        private static BezierKnot Knot(Vector3 position, Vector3 direction, float incoming, float outgoing)
            => new BezierKnot(position, -direction * incoming, direction * outgoing);
        private static RacingLineSource Document(string name, VehicleTuning tuning, RacingLineRoute route)
        {
            var setup = ScriptableObject.CreateInstance<RacingVehicleSetup>(); setup.tuning = tuning; Save(setup, name + "Setup.asset");
            var capability = ScriptableObject.CreateInstance<RacingCapabilityProfile>(); Save(capability, name + "Capability.asset");
            var source = ScriptableObject.CreateInstance<RacingLineSource>(); source.route = route; source.vehicle = setup; source.capability = capability;
            source.planner.maximumSpeed = 16; source.planner.exitSpeed = 8; source.verification.trials = 5;
            source.referenceNotes = "Synthetic compound-corner fixture, not measured NFS or real-road content. Grip and B2D use the same production simulation. "
                + "This route adapter consumes the published Road Network; replace the fixture with a legal event route when that upstream editor exists.";
            Save(source, name + "Line.asset"); return source;
        }
        private static void Save(UnityEngine.Object asset, string name)
        { asset.name = Path.GetFileNameWithoutExtension(name); AssetDatabase.CreateAsset(asset, Folder + "/" + name); AssetDatabase.SaveAssetIfDirty(asset); }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // CLI evidence command. Use a disposable project copy, not a second Editor on the live checkout.
        public static void ValidateExamples()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run the synchronous qualification helper in a disposable batch project copy.");
            Create();
            EditorSceneManager.OpenScene(ScenePath);
            ValidateExample(GripPath, RacingLineFamily.Grip);
            ValidateExample(DriftPath, RacingLineFamily.Drift);
            ValidatePairAndBenchmark();
        }
        // Explicit recovery for relocated example dependencies. Never rewrites a designer's unrelated road/route.
        public static void RepublishExampleRoadAndValidate()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use this recovery helper only in a disposable batch project copy.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var route = AssetDatabase.LoadAssetAtPath<RacingLineRoute>(Folder + "/CompoundRoute.asset");
            RoadNetworkAuthoring authoring = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent<RoadNetworkAuthoring>(out var value))
                { if (authoring != null) throw new ArgumentException("Example must own exactly one road network."); authoring = value; }
            if (route == null || authoring == null) throw new ArgumentException("Example road/route is missing; no recovery was applied.");
            route.network = RoadNetworkBake.Publish(authoring, Folder + "/CompoundRoad.asset");
            EditorUtility.SetDirty(route); AssetDatabase.SaveAssetIfDirty(route);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the republished example scene.");
            ValidateExamples();
        }
        public static void ValidateDriftExample() => ValidateExample(DriftPath, RacingLineFamily.Drift);
        public static void ValidateExample(string path, RacingLineFamily family)
        {
            var source = AssetDatabase.LoadAssetAtPath<RacingLineSource>(path);
            using (var calibration = new RacingCapabilityCalibration(source.vehicle))
            {
                while (!calibration.IsDone) calibration.Advance(8);
                source.capability.points = calibration.Points; source.capability.vehicleFingerprint = calibration.Fingerprint;
                source.capability.longitudinalMeasured = true; source.capability.lateralMeasured = false; source.capability.evidence = calibration.Evidence;
                EditorUtility.SetDirty(source.capability); AssetDatabase.SaveAssetIfDirty(source.capability);
                RacingLineEditorOperations.Export(Folder + "/" + family + "Calibration.json", JsonUtility.ToJson(new CalibrationExport { samples = calibration.Samples }, true));
            }
            var input = RacingLineEditorOperations.Capture(source);
            using var planner = new RacingLinePlanner(input, family); while (!planner.IsDone) planner.Step(8);
            RacingLineEditorOperations.Export(Folder + "/" + family + "Candidate.json", JsonUtility.ToJson(planner.Result, true));
            if (planner.Result.HasErrors) throw new ArgumentException("Example geometry failed: " + JsonUtility.ToJson(planner.Result.diagnostics));
            using var rollout = new RacingLineRollout(source, planner.Result); while (!rollout.IsDone) rollout.Advance(8);
            RacingLineEditorOperations.Export(Folder + "/" + family + "Report.json", JsonUtility.ToJson(rollout.Result, true));
            RacingLineEditorOperations.Export(Folder + "/" + family + "Telemetry.csv", RacingLineEditorOperations.TelemetryCsv(rollout.Result));
            if (!rollout.Result.passed) throw new ArgumentException("RACING_LINE_EXAMPLE_FAILED: " + family + ": " + rollout.Result.trials[0].diagnosis);
            RacingLineEditorOperations.Publish(source, planner.Result, rollout.Result, Folder + "/" + family + "Verified.asset");
            AssetDatabase.SaveAssetIfDirty(source);
            Debug.Log("RACING_LINE_EXAMPLE_PASSED: " + family + "; samples=" + input.Corridor.Length + "; numerical_ms=" + planner.Result.computeMilliseconds
                + "; rollout_ms=" + rollout.Result.computeMilliseconds + "; trials=" + rollout.Result.trials.Length);
        }
        [Serializable] private sealed class CalibrationExport { public RacingCalibrationSample[] samples; }
        public static void ValidatePairAndBenchmark()
        {
            var source = AssetDatabase.LoadAssetAtPath<RacingLineSource>(GripPath);
            var snapshot = RacingLineEditorOperations.Capture(source);
            using var inside = new RacingLinePlanner(snapshot, RacingLineFamily.Inside);
            using var outside = new RacingLinePlanner(snapshot, RacingLineFamily.Outside);
            while (!inside.IsDone) inside.Step(8); while (!outside.IsDone) outside.Step(8);
            using var run = new RacingLineRollout(source, inside.Result, outside.Result); while (!run.IsDone) run.Advance(8);
            RacingLineEditorOperations.Export(Folder + "/PairReport.json", JsonUtility.ToJson(run.Result, true));
            RacingLineEditorOperations.Export(Folder + "/PairTelemetry.csv", RacingLineEditorOperations.TelemetryCsv(run.Result));
            Debug.Log("RACING_LINE_PAIR: " + run.Result.trials.Length + " trials; passed=" + run.Result.passed + "; " + run.Result.trials[0].diagnosis);
            if (!run.Result.passed) throw new ArgumentException("RACING_LINE_PAIR_FAILED: inspect PairReport.json for each participating car's measured constraints.");
            var records = new System.Collections.Generic.List<BenchmarkRecord>();
            // Detached authoring clone: profiling never edits the designer's sample settings/publication.
            var copy = UnityEngine.Object.Instantiate(source);
            try
            {
                foreach (float spacing in new[] { 8f, 4f, 2f, 1f, 0.5f })
                    for (int repeat = 0; repeat < 4; repeat++)
                    {
                        copy.planner.spacing = spacing;
                        var watch = System.Diagnostics.Stopwatch.StartNew(); var input = RacingLineSnapshot.Capture(copy);
                        double captureMs = watch.Elapsed.TotalMilliseconds;
                        using var planner = new RacingLinePlanner(input, RacingLineFamily.Grip);
                        double longest = 0;
                        while (!planner.IsDone)
                        { var slice = System.Diagnostics.Stopwatch.StartNew(); planner.Step(4); longest = Math.Max(longest, slice.Elapsed.TotalMilliseconds); }
                        records.Add(new BenchmarkRecord { samples = input.Corridor.Length, spacing = spacing, repeat = repeat,
                            captureMilliseconds = captureMs, totalMilliseconds = watch.Elapsed.TotalMilliseconds, longestSliceMilliseconds = longest,
                            numericalMilliseconds = planner.Result.computeMilliseconds, costBefore = planner.Result.baselineCost,
                            costAfter = planner.Result.geometricCost, fingerprint = input.Fingerprint });
                    }
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
            RacingLineEditorOperations.Export(Folder + "/Benchmark.json", JsonUtility.ToJson(new BenchmarkExport { records = records.ToArray(),
                engine = Application.unityVersion, machine = SystemInfo.processorType + "; " + SystemInfo.systemMemorySize + " MB", utc = DateTime.UtcNow.ToString("O") }, true));
        }
        [Serializable] private sealed class BenchmarkExport { public string engine, machine, utc; public BenchmarkRecord[] records; }
        [Serializable] private sealed class BenchmarkRecord
        {
            public int samples, repeat; public float spacing, costBefore, costAfter;
            public string fingerprint; public double captureMilliseconds, totalMilliseconds, longestSliceMilliseconds, numericalMilliseconds;
        }
    }
}
