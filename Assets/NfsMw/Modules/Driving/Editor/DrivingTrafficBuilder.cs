#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    public static partial class DrivingDemoBuilder
    {
        private const string TrafficFolder = "Assets/NfsMw/Modules/Driving/Data/Traffic";
        private const string TrafficSceneFolder = "Assets/NfsMw/Scenes/Tests/Traffic";
        private static readonly string[] TrafficScenarios = { "Straight", "TwoLane", "Signal", "Stop", "Merge", "Highway", "Queue", "PlayerApproach", "Siren" };

        [MenuItem("NFS MW Remaster/Traffic/Build Validation Scenes")]
        public static void BuildTrafficValidationScenes()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder("Assets/NfsMw/Modules/Driving/Data"); EnsureFolder(TrafficFolder); EnsureFolder(MaterialsFolder);
            EnsureFolder("Assets/NfsMw/Scenes"); EnsureFolder(TrafficSceneFolder);
            foreach (string scenario in TrafficScenarios) BuildTrafficScene(scenario);
            EditorSceneManager.OpenScene(TrafficSceneFolder + "/TrafficSignal.unity");
            AssetDatabase.SaveAssets();
            Debug.Log("TRAFFIC_SCENES_BUILT: nine physical validation scenes in Assets/NfsMw/Scenes/Tests/Traffic. F9 diagnostics; WASD drive. Tuning is uncalibrated.");
        }

        [MenuItem("NFS MW Remaster/Traffic/Upgrade Civilian Traffic In Open Scene")]
        public static void UpgradeCivilianTrafficInOpenScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Upgrade in Edit mode.");
            EnsureFolder(TrafficFolder); EnsureFolder(MaterialsFolder);
            Material tire = FreeRoamMaterial("Tire", new Color(0.02f, 0.02f, 0.02f));
            int count = 0;
            foreach (var traffic in UnityEngine.Object.FindObjectsByType<FreeRoamTraffic>(FindObjectsSortMode.None))
            {
                RoadNetwork roads = traffic.GetComponent<RoadNetwork>(); if (roads == null) continue;
                roads.BakeLanes();
                foreach (var motor in traffic.Civilians)
                {
                    if (motor == null) continue;
                    ConfigureTrafficRig(motor, tire, (TrafficVehicleCategory)(count++ % 7));
                    EditorUtility.SetDirty(motor);
                }
                var serialized = new SerializedObject(traffic);
                var player = serialized.FindProperty("player").objectReferenceValue as VehicleController;
                var director = traffic.GetComponent<TrafficWorldDirector>() ?? traffic.gameObject.AddComponent<TrafficWorldDirector>();
                director.Configure(roads, roads.GetComponent<RoadTrafficSignals>(), traffic.Civilians, player, traffic.Police, TrafficProfile("NFS2015_TRAFFIC_FIDELITY"));
                // Scene-load prepopulation is authored content, never a live player-targeted spawn.
                if (new SerializedObject(director).FindProperty("initialTrips").arraySize == 0)
                {
                    var initial = new List<TrafficInitialTrip>(); var route = new int[roads.Lanes.Count];
                    for (int lane = 0; lane < roads.Lanes.Count && initial.Count < traffic.Civilians.Length / 2; lane++)
                    {
                        RoadLane origin = roads.Lanes[lane]; if (origin.Intersection >= 0 || origin.Length < 60) continue;
                        for (int end = roads.Lanes.Count - 1; end >= 0; end--)
                        {
                            if (roads.Lanes[end].Intersection >= 0 || end == lane
                                || !roads.Lanes.TryRoute(lane, end, 2015 + lane, route, out int length) || length < 3) continue;
                            initial.Add(new TrafficInitialTrip { originLaneId = origin.Id, destinationLaneId = roads.Lanes[end].Id,
                                seed = 2015 + lane * 7919, along = origin.Length * 0.4f }); break;
                        }
                    }
                    director.ConfigureInitialTrips(initial.ToArray());
                }
                var debug = traffic.GetComponent<TrafficDebugPanel>() ?? traffic.gameObject.AddComponent<TrafficDebugPanel>(); debug.Configure(director);
                foreach (var hazard in UnityEngine.Object.FindObjectsByType<PoliceRoadHazard>(FindObjectsSortMode.None))
                {
                    var bridge = hazard.GetComponent<TrafficIncidentBridge>() ?? hazard.gameObject.AddComponent<TrafficIncidentBridge>();
                    bridge.Configure(roads, hazard);
                }
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
            Debug.Log("Upgraded " + count + " civilian rigs; scene is dirty for review/save. Police, shops, audio, and career content retained.");
        }

        // Isolated CLI migration never re-generates the user's authored scene or other gameplay assets.
        public static void MigrateTrafficScenesInIsolatedProject()
        {
            if (!Application.isBatchMode || !Application.dataPath.StartsWith("/private/tmp/", StringComparison.Ordinal))
                throw new InvalidOperationException("Use an isolated /private/tmp Unity project for batch scene migration.");
            EditorSceneManager.OpenScene(FreeRoamScenePath); UpgradeCivilianTrafficInOpenScene();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); BuildTrafficValidationScenes();
            ValidateTrafficAssetsInIsolatedProject();
        }
        public static void ValidateTrafficAssetsInIsolatedProject()
        {
            if (!Application.isBatchMode || !Application.dataPath.StartsWith("/private/tmp/", StringComparison.Ordinal))
                throw new InvalidOperationException("Validate scene imports in an isolated /private/tmp Unity project.");
            foreach (string scenario in TrafficScenarios)
            {
                Scene scene = EditorSceneManager.OpenScene(TrafficSceneFolder + "/Traffic" + scenario + ".unity");
                int rigs = 0;
                foreach (var root in scene.GetRootGameObjects()) foreach (var child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) > 0)
                        throw new InvalidOperationException("Missing script in " + scenario + ": " + child.name);
                    var motor = child.GetComponent<RoadVehicleMotor>(); if (motor == null) continue;
                    var vehicle = child.GetComponent<VehicleController>();
                    if (motor.VehicleProfile == null || motor.VehicleProfile.tuning == null || vehicle == null
                        || vehicle.Wheels.Length != 4 || vehicle.InputSourceComponent != motor)
                        throw new InvalidOperationException("Invalid physical traffic rig in " + scenario + ": " + child.name);
                    rigs++;
                }
                if (rigs != 16) throw new InvalidOperationException("Validation scene must reserve 16 traffic rigs: " + scenario);
            }
            foreach (string path in Directory.GetFiles(TrafficFolder, "*.asset"))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null || asset.GetType().Namespace != "NfsMwRemaster.Driving")
                    throw new InvalidOperationException("Traffic data asset has a missing script: " + path);
            }
            Debug.Log("TRAFFIC_ASSET_AUDIT_PASS: nine scenes, 144 four-wheel rigs, all traffic data assets resolve.");
        }
        private static TrafficWorldProfile TrafficProfile(string name)
        {
            string path = TrafficFolder + "/" + name + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<TrafficWorldProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<TrafficWorldProfile>(); profile.profileId = name;
                if (name != "NFS2015_TRAFFIC_FIDELITY") profile.defaultPortalDemandPerHour = 0;
                AssetDatabase.CreateAsset(profile, path);
            }
            if (profile.drivers == null)
            {
                string driverPath = TrafficFolder + "/CivilianDriverPopulation.asset";
                var drivers = AssetDatabase.LoadAssetAtPath<TrafficDriverPopulationProfile>(driverPath);
                if (drivers == null) { drivers = ScriptableObject.CreateInstance<TrafficDriverPopulationProfile>(); AssetDatabase.CreateAsset(drivers, driverPath); }
                profile.drivers = drivers; EditorUtility.SetDirty(profile);
            }
            return profile;
        }
        private static TrafficVehicleProfile TrafficVehicle(TrafficVehicleCategory category)
        {
            EnsureFolder(TrafficFolder);
            string path = TrafficFolder + "/" + category + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<TrafficVehicleProfile>(path); if (profile != null) return profile;
            profile = ScriptableObject.CreateInstance<TrafficVehicleProfile>(); profile.category = category;
            int index = (int)category;
            float[] masses = { 1150, 1400, 1850, 1950, 2100, 1550, 2800 };
            float[] lengths = { 3.8f, 4.3f, 4.5f, 4.9f, 5.1f, 4.6f, 5.8f };
            float[] accelerations = { 2.4f, 2.2f, 1.9f, 1.8f, 1.5f, 2.1f, 1.2f };
            profile.colliderSize = new Vector3(index == 6 ? 2.1f : 1.8f, index == 6 ? 1.5f : 1.2f, lengths[index]);
            // Same ground clearance across this placeholder fleet; taller bodies grow upward.
            profile.colliderCenter = new Vector3(0, profile.colliderSize.y * 0.5f - 0.5f, 0);
            profile.wheelbase = lengths[index] * 0.62f; profile.comfortAccelerationLimit = accelerations[index];
            // Dry, straight 8 -> 1.3 m/s probes measured 7.85..8.93 m/s² across this fleet.
            // Keep planning below that observation; this is not a wet/slope/high-speed guarantee.
            profile.brakingLimit = 6;
            profile.maximumLateralAcceleration = index >= 2 && index != 5 ? 2 : 2.5f;
            var tuning = VehicleTuning.CreateStreetRacer(); tuning.displayName = "Civilian " + category + " — provisional";
            tuning.chassis.mass = masses[index]; tuning.chassis.maxSpeedKph = index == 6 ? 105 : 140;
            tuning.engine.maxTorqueNewtonMeters = masses[index] * 0.16f; tuning.engine.nitrousFuelSeconds = tuning.engine.nitrousTorque = 0;
            tuning.engine.shiftUpRpm = 4000; tuning.engine.shiftDownRpm = 1800;
            tuning.handling.driftBias = 0; tuning.handling.brakeToDrift = false;
            tuning.controls.steeringExponent = 1; tuning.controls.highSpeedSteerScale = 1;
            tuning.tires.springRate *= masses[index] / 1450; tuning.tires.damperRate *= masses[index] / 1450;
            AssetDatabase.CreateAsset(tuning, TrafficFolder + "/" + category + "Tuning.asset"); profile.tuning = tuning;
            AssetDatabase.CreateAsset(profile, path); return profile;
        }
        private static void ConfigureTrafficRig(RoadVehicleMotor motor, Material tire, TrafficVehicleCategory category)
        {
            TrafficVehicleProfile profile = motor.VehicleProfile != null ? motor.VehicleProfile : TrafficVehicle(category);
            var controller = motor.GetComponent<VehicleController>();
            if (controller == null)
            {
                // Only the generated legacy placeholder wheels are removed; custom meshes remain intact.
                for (int i = motor.transform.childCount - 1; i >= 0; i--)
                    if (motor.transform.GetChild(i).name == "Wheel") UnityEngine.Object.DestroyImmediate(motor.transform.GetChild(i).gameObject);
                controller = motor.gameObject.AddComponent<VehicleController>();
            }
            var wheels = motor.GetComponentsInChildren<VehicleWheel>(true);
            if (wheels.Length == 0)
            {
                wheels = new VehicleWheel[4]; int wheel = 0;
                foreach (int axle in new[] { 1, -1 }) foreach (int side in new[] { -1, 1 })
                    wheels[wheel++] = CreateWheel(motor.transform, "Traffic Wheel " + axle + " " + side,
                        new Vector3(side * profile.colliderSize.x * 0.48f, 0, axle * profile.wheelbase * 0.5f),
                        axle == 1 ? VehicleAxle.Front : VehicleAxle.Rear, axle == 1, axle == -1, axle == -1, tire, profile.tuning);
            }
            if (wheels.Length != 4) throw new InvalidOperationException("Traffic rig requires four configured wheels: " + motor.name);
            var hull = motor.GetComponent<BoxCollider>(); hull.size = profile.colliderSize; hull.center = profile.colliderCenter;
            motor.GetComponent<Rigidbody>().constraints = RigidbodyConstraints.None;
            controller.ConfigureForRuntime(profile.tuning, motor, wheels); motor.ConfigureVehicle(profile);
        }

        private static void BuildTrafficScene(string scenario)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); CreateLighting();
            RenderSettings.ambientLight = new Color(0.45f, 0.48f, 0.55f);
            Material asphalt = FreeRoamMaterial("Traffic Asphalt", new Color(0.055f, 0.065f, 0.08f));
            Material paint = FreeRoamMaterial("Traffic Blue", new Color(0.1f, 0.4f, 0.7f));
            Material white = FreeRoamMaterial("Traffic White", new Color(0.85f, 0.9f, 0.95f));
            Material tire = FreeRoamMaterial("Tire", new Color(0.02f, 0.02f, 0.02f));
            var ground = CreateBox("Test asphalt — SI metres", null, new Vector3(0, -0.5f, 0), new Vector3(550, 1, 550), asphalt, true);
            ground.AddComponent<VehicleSurface>().Configure("Asphalt", 1, 1);
            var root = new GameObject("Traffic " + scenario);
            var roads = root.AddComponent<RoadNetwork>(); var signals = root.AddComponent<RoadTrafficSignals>();
            bool junction = scenario == "Signal" || scenario == "Stop";
            RoadLaneDefinition[] lanes;
            if (junction)
            {
                roads.Configure(new[] {
                    new RoadNode { position = Vector3.zero, exits = new[] { 1, 2, 3, 4 } },
                    new RoadNode { position = new Vector3(-250, 0, 0), exits = new[] { 0 } },
                    new RoadNode { position = new Vector3(250, 0, 0), exits = new[] { 0 } },
                    new RoadNode { position = new Vector3(0, 0, -250), exits = new[] { 0 } },
                    new RoadNode { position = new Vector3(0, 0, 250), exits = new[] { 0 } } });
                lanes = RoadLaneNetwork.Bake(roads.Nodes);
                if (scenario == "Stop") foreach (var lane in lanes) if (lane.intersection >= 0) lane.entryControl = RoadEntryControl.Stop;
                if (scenario == "Stop") foreach (Vector3 direction in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                {
                    var lamp = CreateVisualBox(scenario + " control", root.transform,
                        -direction * 15 + Vector3.Cross(Vector3.up, direction) * 7 + Vector3.up * 3,
                        new Vector3(0.6f, 0.6f, 0.6f), white);
                    if (scenario == "Signal") lamp.AddComponent<RoadSignalLamp>().Configure(signals, Vector3.zero, direction);
                }
            }
            else
            {
                roads.Configure(new[] { new RoadNode { position = Vector3.back * 250, exits = new[] { 1 } },
                    new RoadNode { position = Vector3.zero, exits = new[] { 0, 2 } },
                    new RoadNode { position = Vector3.forward * 250, exits = new[] { 1 } } });
                int width = scenario == "TwoLane" || scenario == "Merge" || scenario == "Highway" ? 2 : 1;
                var list = new List<RoadLaneDefinition>();
                for (int segment = 0; segment < 2; segment++) for (int lane = 0; lane < width; lane++)
                {
                    if (scenario == "Merge" && segment == 1 && lane == 1) continue;
                    list.Add(new RoadLaneDefinition { id = segment * width + lane, fromNode = segment, toNode = segment + 1,
                        roadClass = scenario == "Highway" ? RoadClass.Highway : RoadClass.Local,
                        speedMetersPerSecond = scenario == "Highway" ? 27 : 13.9f, portal = segment == 0,
                        leftLane = lane > 0 ? segment * width : -1,
                        rightLane = width > 1 && lane == 0 && !(scenario == "Merge" && segment == 1) ? segment * width + 1 : -1,
                        successors = segment == 0 && !(scenario == "Merge" && lane == 1) ? new[] { width + lane } : Array.Empty<int>(),
                        points = new[] { new Vector3(3 + lane * 3.5f, 0, -250 + segment * 250), new Vector3(3 + lane * 3.5f, 0, segment * 250) } });
                }
                lanes = list.ToArray();
            }
            roads.ConfigureLanes(lanes);
            if (scenario == "Signal")
            {
                string path = TrafficFolder + "/Validation_ProtectedSignals.asset";
                var plan = AssetDatabase.LoadAssetAtPath<TrafficSignalPlan>(path);
                if (plan == null)
                {
                    plan = ScriptableObject.CreateInstance<TrafficSignalPlan>();
                    var groups = new List<List<int>>();
                    for (int lane = 0; lane < roads.Lanes.Count; lane++)
                    {
                        if (roads.Lanes[lane].Intersection != 0) continue;
                        List<int> selected = null;
                        foreach (var group in groups)
                        {
                            bool conflict = false; foreach (int other in group) conflict |= roads.Lanes.MovementsConflict(lane, other);
                            if (!conflict) { selected = group; break; }
                        }
                        if (selected == null) { selected = new List<int>(); groups.Add(selected); }
                        selected.Add(lane);
                    }
                    var phases = new TrafficSignalPhase[groups.Count];
                    for (int group = 0; group < groups.Count; group++)
                    {
                        var movements = new int[groups[group].Count];
                        for (int i = 0; i < movements.Length; i++) movements[i] = roads.Lanes[groups[group][i]].Id;
                        phases[group] = new TrafficSignalPhase { greenSeconds = 9, yellowSeconds = 3, allRedSeconds = 2,
                            allowedMovementLaneIds = movements };
                    }
                    plan.Configure(0, 0, phases); AssetDatabase.CreateAsset(plan, path);
                }
                if (!plan.Validate(roads.Lanes, out string error)) throw new InvalidOperationException(error);
                signals.ConfigurePlans(roads.Lanes, new[] { plan });
                // The plan gives each permitted movement an exclusive protected corridor.
                foreach (var lane in lanes) if (lane.intersection >= 0) lane.entryControl = RoadEntryControl.ProtectedSignal;
                roads.ConfigureLanes(lanes);
                foreach (var lane in lanes)
                {
                    if (lane.intersection < 0) continue;
                    var lamp = CreateVisualBox("Movement " + lane.id + " signal", root.transform,
                        lane.points[0] + Vector3.up * 2.5f, Vector3.one * 0.4f, white);
                    lamp.AddComponent<RoadSignalLamp>().ConfigureMovement(signals, lane.intersection, lane.id);
                }
            }
            foreach (var lane in lanes)
            {
                var line = new GameObject("Lane " + lane.id).AddComponent<LineRenderer>(); line.transform.SetParent(root.transform);
                line.sharedMaterial = white; line.widthMultiplier = 0.07f; line.positionCount = lane.points.Length;
                for (int i = 0; i < lane.points.Length; i++) line.SetPosition(i, lane.points[i] + Vector3.up * 0.02f);
            }
            var cars = new RoadVehicleMotor[16]; var poolRoot = new GameObject("Reserved traffic pool").transform;
            for (int i = 0; i < cars.Length; i++) cars[i] = CreateRoadCar(poolRoot, "Civilian " + i, roads, signals, paint, white, false);
            var player = CreateTrafficTestPlayer(paint, tire);
            var camera = CreateCamera(player); player.ConfigureForRuntime(player.Tuning, player.InputSourceComponent, player.Wheels, camera);
            var cops = Array.Empty<VehiclePoliceUnit>(); VehiclePursuitDirector pursuit = null;
            if (scenario == "Siren")
            {
                var bounty = player.gameObject.AddComponent<VehicleBountySystem>();
                var target = player.gameObject.AddComponent<VehiclePursuitTargetAdapter>(); target.Configure("traffic-test-player");
                pursuit = root.AddComponent<VehiclePursuitDirector>(); pursuit.Configure(target, bounty, null);
                var cop = CreatePoliceUnit(root.transform, "Traffic siren test police", VehiclePoliceUnitRole.Pursuer,
                    new Vector3(3, 0.65f, -245), white, white, paint, paint);
                cop.Configure("traffic-siren-test", cop.Role, pursuit); cop.ConfigureNavigation(roads, signals); cops = new[] { cop };
            }
            var director = root.AddComponent<TrafficWorldDirector>(); director.Configure(roads, signals, cars, player, cops, TrafficProfile("Validation_" + scenario));
            var initial = new List<TrafficInitialTrip>();
            foreach (var origin in lanes)
            {
                if (!origin.portal || junction && origin.fromNode != 3 && origin.fromNode != 1) continue;
                int destination = -1;
                foreach (var end in lanes)
                    if (junction ? end.fromNode == 0 && end.toNode == (origin.fromNode == 3 ? 4 : 2)
                        : end.fromNode == 1 && (end.id % (lanes.Length == 4 ? 2 : 1) == origin.id || scenario == "Merge")) { destination = end.id; break; }
                if (destination < 0) continue;
                for (int i = 0; i < 3; i++)
                {
                    int tripDestination = destination;
                    if (scenario == "Signal" && origin.fromNode == 3 && i == 2)
                        foreach (var end in lanes) if (end.fromNode == 0 && end.toNode == 1) { tripDestination = end.id; break; }
                    initial.Add(new TrafficInitialTrip { originLaneId = origin.id, destinationLaneId = tripDestination,
                        along = (scenario == "PlayerApproach" || scenario == "Siren" ? 100 : 45) + i * 26, seed = 2015 + origin.id * 31 + i * 7919 });
                }
            }
            director.ConfigureInitialTrips(initial.ToArray());
            GameObject obstacle = null;
            if (scenario == "Queue") obstacle = CreateBox("Removable queue obstruction", root.transform, new Vector3(3, 1, -40), new Vector3(3, 2, 2), white, true);
            root.AddComponent<TrafficDebugPanel>().Configure(director);
            root.AddComponent<TrafficTestControls>().Configure(scenario, director, obstacle, pursuit, cops.Length > 0 ? cops[0] : null);
            EditorSceneManager.SaveScene(scene, TrafficSceneFolder + "/Traffic" + scenario + ".unity");
        }
        private static VehicleController CreateTrafficTestPlayer(Material paint, Material tire)
        {
            var root = new GameObject("Traffic test player — WASD"); root.SetActive(false); root.transform.position = new Vector3(10, 0.65f, -145);
            root.AddComponent<Rigidbody>(); var hull = root.AddComponent<BoxCollider>(); hull.size = new Vector3(1.8f, 1.2f, 4.2f); hull.center = Vector3.up * 0.1f;
            CreateVisualBox("Body", root.transform, Vector3.zero, new Vector3(1.8f, 0.8f, 4.2f), paint);
            var input = root.AddComponent<PlayerVehicleInput>(); var controller = root.AddComponent<VehicleController>();
            var tuning = AssetDatabase.LoadAssetAtPath<VehicleTuning>(TuningPath);
            if (tuning == null) { tuning = VehicleTuning.CreateStreetRacer(); AssetDatabase.CreateAsset(tuning, TuningPath); }
            var wheels = new VehicleWheel[4]; int index = 0;
            foreach (int axle in new[] { 1, -1 }) foreach (int side in new[] { -1, 1 })
                wheels[index++] = CreateWheel(root.transform, "Player Wheel " + axle + " " + side, new Vector3(side * 0.86f, 0, axle * 1.38f),
                    axle == 1 ? VehicleAxle.Front : VehicleAxle.Rear, axle == 1, axle == -1, axle == -1, tire, tuning);
            controller.ConfigureForRuntime(tuning, input, wheels); root.SetActive(true); return controller;
        }
    }
}
#endif
