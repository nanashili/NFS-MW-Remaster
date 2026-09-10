#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace NfsMwRemaster.Driving.Editor
{
    [InitializeOnLoad]
    public static class DrivingTrafficSmoke
    {
        private const string Key = "Driving.TrafficSmoke";
        private static TrafficWorldDirector world;
        private static RoadVehicleMotor observed;
        private static VehicleController player;
        private static int stage, identity, destination;
        private static float phaseAt, startZ, startX;
        private static double deadline;
        private static bool failure;
        private static string scenario;
        private static int initialAgents;
        private static RoadVehicleMotor[] brakingFleet;
        private static Vector3[] brakeStarts;
        private static float[] brakeSpeeds, brakeTimes;
        private static bool[] brakeComplete;
        static DrivingTrafficSmoke() { if (SessionState.GetBool(Key, false)) EditorApplication.delayCall += Resume; }
        public static void Run()
        {
            SessionState.SetString(Key + ".scenario", "Queue");
            RequireIsolated(); EditorSceneManager.OpenScene("Assets/NfsMw/Scenes/Tests/Traffic/TrafficQueue.unity");
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true; Resume();
        }
        public static void RunIntersection() { RunScenario("Signal"); }
        public static void RunMerge() { RunScenario("Merge"); }
        public static void RunStop() { RunScenario("Stop"); }
        public static void ProfileBraking() { RunScenario("Braking"); }
        private static void RunScenario(string name)
        {
            RequireIsolated(); EditorSceneManager.OpenScene("Assets/NfsMw/Scenes/Tests/Traffic/Traffic" + (name == "Braking" ? "Straight" : name) + ".unity");
            if (name == "Braking") ConfigureBrakingFixture();
            SessionState.SetString(Key + ".scenario", name); SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true; Resume();
        }
        private static void Resume()
        {
            deadline = EditorApplication.timeSinceStartup + 150; stage = 0; failure = false;
            observed = null; scenario = SessionState.GetString(Key + ".scenario", "Queue");
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
        }
        private static void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Exception && trace.Contains("UnityEditor.Search.SearchDatabase")) return;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = true;
        }
        private static void Poll()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Timed out in phase " + stage);
                if (!EditorApplication.isPlaying || Time.time < 0.15f) return;
                Require(!failure, "Unity runtime error; inspect the preceding log.");
                if (scenario == "Braking") { PollBraking(); return; }
                if (scenario != "Queue") { PollScenario(); return; }
                switch (stage)
                {
                    case 0:
                        world = UnityEngine.Object.FindAnyObjectByType<TrafficWorldDirector>();
                        player = GameObject.Find("Traffic test player — WASD").GetComponent<VehicleController>();
                        Require(world != null && world.Simulation != null && world.Simulation.Statistics.Active >= 3, "Authored initial traffic missing.");
                        var cars = UnityEngine.Object.FindObjectsByType<RoadVehicleMotor>(FindObjectsSortMode.None);
                        foreach (var motor in cars)
                        {
                            Require(motor.Vehicle != null && motor.Vehicle.Wheels.Length == 4, "Missing physical civilian rig.");
                            Require(motor.Vehicle.InputSourceComponent == motor, "Vehicle does not consume civilian controls.");
                            Require(motor.Body.constraints == RigidbodyConstraints.None, "Legacy rotation lock survived migration.");
                            if (observed == null || motor.Body.position.z > observed.Body.position.z) observed = motor;
                        }
                        Require(UnityEngine.Object.FindAnyObjectByType<VehicleCameraRig>().Target == player.transform, "An NPC stole the player camera.");
                        identity = observed.Agent.Id; destination = observed.Agent.Destination; startZ = observed.Body.position.z;
                        Time.timeScale = 4; Next(); break;
                    case 1:
                        Require(observed.Body.position.z < -42.5f, "Traffic drove through the physical queue obstacle.");
                        Require(float.IsFinite(observed.Current.Throttle) && float.IsFinite(observed.Current.Steering), "Non-finite vehicle control.");
                        if (Time.time - phaseAt < 30) return;
                        Require(observed.Body.position.z > startZ + 45, "Civilian failed to drive through tire forces: " + observed.Body.position);
                        Require(observed.Body.linearVelocity.magnitude < 0.7f && observed.WaitingForTraffic,
                            "Civilian failed to stop in queue: pos=" + observed.Body.position + " velocity=" + observed.Body.linearVelocity + " gap=" + observed.Agent.Gap);
                        Require(world.Simulation.Statistics.Collisions == 0, "Queue stopped by physical contact instead of braking.");
                        Debug.Log("TRAFFIC_PHYSICAL_QUEUE_PASS " + observed.Body.position);
                        GameObject.Find("Removable queue obstruction").SetActive(false); startZ = observed.Body.position.z; Next(); break;
                    case 2:
                        if (Time.time - phaseAt < 8) return;
                        Require(observed.Body.position.z > startZ + 10, "Queue failed to restart after obstacle clearance.");
                        startX = observed.Body.position.x; observed.Body.AddForce(Vector3.right * observed.Body.mass * 3, ForceMode.Impulse);
                        Next(); break;
                    case 3:
                        if (Time.time - phaseAt < 0.2f) return;
                        Require(observed.Body.position.x > startX + 0.04f, "Interactive traffic erased physical lateral momentum.");
                        Debug.Log("TRAFFIC_MOMENTUM_PASS lateral=" + (observed.Body.position.x - startX));
                        player.Body.position = new Vector3(6000, 2, 6000); player.Body.linearVelocity = Vector3.zero;
                        Next(); break;
                    case 4:
                        if (Time.time - phaseAt < 5) return;
                        Require(observed.Agent != null && observed.Agent.Id == identity && observed.Agent.Destination == destination, "LOD lost trip identity or destination.");
                        Require(observed.Agent.Lod == TrafficSimulationLod.Logical, "Beyond-camera trip failed to enter logical LOD: " + observed.Agent.Lod);
                        Require(!observed.gameObject.activeSelf, "Logical LOD retained an active GameObject.");
                        startZ = observed.Agent.Position.z; Next(); break;
                    case 5:
                        if (Time.time - phaseAt < 2) return;
                        Require(observed.Agent.Position.z > startZ + 1, "Distant trip stopped progressing.");
                        player.Body.position = observed.Agent.Position + new Vector3(10, 0.65f, 0); player.Body.linearVelocity = Vector3.zero;
                        Next(); break;
                    case 6:
                        if (Time.time - phaseAt < 0.1f) return;
                        Require(observed.gameObject.activeSelf && observed.Agent.Lod == TrafficSimulationLod.Physical
                            && !observed.Body.isKinematic && observed.Body.detectCollisions && observed.Vehicle.enabled, "Promotion failed to restore collision physics.");
                        Require(observed.Agent.Id == identity && observed.Agent.Destination == destination, "Promotion reset trip.");
                        Debug.Log("TRAFFIC_LOD_CONTINUITY_PASS id=" + identity + " handovers=" + world.HandoverCount);
                        VerifyCsvExport();
                        Finish(0); break;
                }
            }
            catch (Exception error) { Debug.LogException(error); Finish(1); }
        }
        private static void Next() { stage++; phaseAt = Time.time; }
        private static void VerifyCsvExport()
        {
            string path = UnityEngine.Object.FindAnyObjectByType<TrafficDebugPanel>().ExportCsv();
            string[] rows = File.ReadAllLines(path);
            Require(rows.Length > 50 && rows.Length <= 8193, "Missing/beyond-capacity traffic CSV window.");
            Require(rows[0].EndsWith(",measured_accel_mps2,sample_dt_s", StringComparison.Ordinal), "Missing measured CSV fields.");
            for (int i = 1; i < rows.Length; i++)
            {
                string[] fields = rows[i].Split(','); Require(fields.Length == 14, "Malformed CSV row " + i);
                foreach (int column in new[] { 0, 3, 4, 5, 7, 12, 13 })
                    Require(float.TryParse(fields[column], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value),
                        "Non-finite/culture-dependent CSV field at row " + i + ", column " + column);
            }
            File.Copy(path, Path.GetFullPath(Path.Combine(Application.dataPath, "../TrafficQueueTrace.csv")), true);
            Debug.Log("TRAFFIC_CSV_EXPORT_PASS rows=" + (rows.Length - 1) + ", invariant SI units and measured acceleration.");
        }
        private static void PollBraking()
        {
            if (stage == 0)
            {
                world = UnityEngine.Object.FindAnyObjectByType<TrafficWorldDirector>();
                brakingFleet = UnityEngine.Object.FindObjectsByType<RoadVehicleMotor>(FindObjectsSortMode.None);
                Require(brakingFleet.Length == 7, "Braking test needs all seven civilian categories.");
                brakeStarts = new Vector3[7]; brakeSpeeds = new float[7]; brakeTimes = new float[7]; brakeComplete = new bool[7];
                Time.timeScale = 4; Next(); return;
            }
            int complete = 0;
            for (int i = 0; i < brakingFleet.Length; i++)
            {
                if (brakeComplete[i]) { complete++; continue; }
                var motor = brakingFleet[i]; float speed = motor.Body.linearVelocity.magnitude;
                if (brakeSpeeds[i] == 0)
                {
                    Require(Time.time - phaseAt < 60, "Civilian failed to reach braking speed: " + motor.VehicleProfile.category);
                    if (speed < 8) continue;
                    brakeStarts[i] = motor.Body.position; brakeSpeeds[i] = speed; brakeTimes[i] = Time.time;
                    motor.Immobilized = true; continue;
                }
                Require(Time.time - brakeTimes[i] < 15, "Full brakes did not stop " + motor.VehicleProfile.category);
                if (speed > 1.3f) continue;
                float distance = Vector3.Distance(brakeStarts[i], motor.Body.position);
                float deceleration = (brakeSpeeds[i] * brakeSpeeds[i] - speed * speed) / (2 * distance);
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "TRAFFIC_BRAKING_MEASURED category={0} initialSpeed={1:F3} remainingSpeed={2:F3} distance={3:F3} seconds={4:F3} effectiveDeceleration={5:F3} authoredBound={6:F3}",
                    motor.VehicleProfile.category, brakeSpeeds[i], speed, distance, Time.time - brakeTimes[i], deceleration, motor.VehicleProfile.brakingLimit));
                Require(deceleration >= motor.VehicleProfile.brakingLimit, "Authored braking limit exceeds this dry straight-line observation: " + motor.VehicleProfile.category);
                brakeComplete[i] = true; complete++;
            }
            if (complete == brakingFleet.Length) { Debug.Log("TRAFFIC_BRAKING_FLEET_PASS"); Finish(0); }
        }
        private static void ConfigureBrakingFixture()
        {
            var director = UnityEngine.Object.FindAnyObjectByType<TrafficWorldDirector>();
            var pool = UnityEngine.Object.FindObjectsByType<RoadVehicleMotor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var fleet = new RoadVehicleMotor[7]; var definitions = new RoadLaneDefinition[14]; var trips = new TrafficInitialTrip[7];
            for (int i = 0; i < 7; i++)
            {
                foreach (var motor in pool) if ((int)motor.VehicleProfile.category == i) { fleet[i] = motor; break; }
                Require(fleet[i] != null, "Missing test category " + i);
                float x = -100 + 20 * i;
                definitions[i * 2] = new RoadLaneDefinition { id = i * 2, points = new[] { new Vector3(x, 0, -230), new Vector3(x, 0, 0) }, successors = new[] { i * 2 + 1 } };
                definitions[i * 2 + 1] = new RoadLaneDefinition { id = i * 2 + 1, points = new[] { new Vector3(x, 0, 0), new Vector3(x, 0, 230) } };
                trips[i] = new TrafficInitialTrip { originLaneId = i * 2, destinationLaneId = i * 2 + 1, seed = 2015, along = 20 };
            }
            director.Roads.ConfigureLanes(definitions);
            director.Configure(director.Roads, UnityEngine.Object.FindAnyObjectByType<RoadTrafficSignals>(), fleet,
                GameObject.Find("Traffic test player — WASD").GetComponent<VehicleController>(), Array.Empty<VehiclePoliceUnit>(), director.Profile);
            director.ConfigureInitialTrips(trips);
        }
        private static void PollScenario()
        {
            if (stage == 0)
            {
                world = UnityEngine.Object.FindAnyObjectByType<TrafficWorldDirector>();
                Require(world?.Simulation != null, "Missing traffic world."); initialAgents = world.Simulation.Statistics.Active;
                Require(initialAgents >= 3, "Missing validation traffic."); Time.timeScale = 6; Next();
            }
            int progressed = 0;
            for (int i = 0; i < world.Capacity; i++)
            {
                var a = world.Simulation[i]; if (!a.Active) continue;
                Require(!a.Disabled && float.IsFinite(a.Speed), "Validation trip became disabled/non-finite.");
                if (world.Roads.Lanes[a.Lane].FromNode == (scenario == "Merge" ? 1 : 0)
                    && world.Roads.Lanes[a.Lane].Intersection < 0 && a.Along > 35) progressed++;
            }
            if (Time.time - phaseAt < 180) return;
            Debug.Log("TRAFFIC_SCENARIO_STATS " + scenario + " progressed=" + (progressed + world.Simulation.Statistics.Completed) + "/" + initialAgents
                + " contacts=" + world.Simulation.Statistics.Collisions + " laneChanges=" + world.Simulation.Statistics.LaneChanges
                + " incursions=" + world.Simulation.Statistics.PhysicalIncursions);
            for (int i = 0; i < world.Capacity; i++)
            {
                var a = world.Simulation[i]; if (!a.Active) continue;
                Debug.Log("TRAFFIC_TRIP " + a.Id + " lane=" + a.Lane + " along=" + a.Along + " speed=" + a.Speed
                    + " decision=" + a.IntersectionDecision + " gap=" + a.Gap + " pos=" + a.Position);
            }
            Require(progressed + world.Simulation.Statistics.Completed >= initialAgents, "Not every trip cleared the " + scenario + " within 180 simulation seconds.");
            Require(world.Simulation.Statistics.Collisions == 0, "Ordinary traffic collided in " + scenario);
            Require(world.Simulation.Statistics.PhysicalIncursions == 0, "Ordinary traffic crossed a controlled boundary without permission in " + scenario);
            if (scenario == "Merge") Require(world.Simulation.Statistics.LaneChanges >= 3, "Ending lane failed mandatory merge.");
            Debug.Log("TRAFFIC_" + scenario.ToUpperInvariant() + "_SMOKE_PASS"); Finish(0);
        }
        private static void Finish(int code)
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Poll; Application.logMessageReceived -= OnLog;
            Time.timeScale = 1;
            if (code == 0 && scenario == "Queue") Debug.Log("TRAFFIC_PHYSICS_SMOKE_PASS: tire-driven queue, restart, impulse momentum, camera ownership, continuous trip LOD.");
            EditorApplication.Exit(code);
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void RequireIsolated()
        {
            Require(Application.isBatchMode && Application.dataPath.StartsWith("/private/tmp/", StringComparison.Ordinal), "Use an isolated /private/tmp Unity project.");
        }

        public static void ProfileLogicalTraffic()
        {
            RequireIsolated();
            var results = new StringBuilder("agents,simulation_seconds,step_ms_mean,step_ms_p95,step_ms_max,allocated_bytes,min_bumper_gap_m\n");
            foreach (int population in new[] { 100, 250, 500, 1000 })
            {
                var network = new RoadLaneNetwork(new[] {
                    new RoadLaneDefinition { id = 0, points = new[] { Vector3.zero, Vector3.forward * 100000 }, successors = new[] { 1 } },
                    new RoadLaneDefinition { id = 1, points = new[] { Vector3.forward * 100000, Vector3.forward * 200000 } } });
                var simulation = new TrafficSimulation(network, population); var registry = new TrafficSpatialRegistry(population);
                for (int i = 0; i < population; i++)
                {
                    Require(simulation.TrySpawn(0, 1, 2015 + i, out int slot), "Stress trip admission failed.");
                    simulation.SynchronizePhysical(slot, Vector3.forward * ((population - i) * 70), Vector3.zero, Vector3.forward, 2, 0.9f, 2, 9);
                }
                var environment = new LogicalEnvironment();
                const int steps = 1200; var timings = new double[steps];
                for (int i = 0; i < 32; i++) Tick(simulation, registry, environment);
                long bytesBefore = GC.GetAllocatedBytesForCurrentThread(); double sum = 0, max = 0;
                float minimumGap = float.PositiveInfinity;
                for (int step = 0; step < steps; step++)
                {
                    long begin = Stopwatch.GetTimestamp(); Tick(simulation, registry, environment);
                    double ms = (Stopwatch.GetTimestamp() - begin) * 1000.0 / Stopwatch.Frequency;
                    timings[step] = ms; sum += ms; max = Math.Max(max, ms);
                    for (int i = 1; i < population; i++) minimumGap = Mathf.Min(minimumGap,
                        simulation[i - 1].Position.z - simulation[i].Position.z - 4);
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - bytesBefore; Array.Sort(timings);
                Require(minimumGap >= 0, "Microscopic overlap in stress population " + population);
                Require(allocated == 0, "Steady-state logical traffic allocated " + allocated + " bytes.");
                results.AppendFormat(CultureInfo.InvariantCulture, "{0},60,{1:F4},{2:F4},{3:F4},{4},{5:F3}\n", population,
                    sum / steps, timings[(int)(steps * 0.95)], max, allocated, minimumGap);
            }
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../TrafficLogicalBenchmark.csv"));
            File.WriteAllText(path, results.ToString()); Debug.Log("TRAFFIC_LOGICAL_BENCHMARK_PASS\n" + results + path);
        }
        private static void Tick(TrafficSimulation simulation, TrafficSpatialRegistry registry, ITrafficEnvironment environment)
        {
            registry.BeginFrame(); for (int i = 0; i < simulation.Capacity; i++) registry.Add(simulation.Snapshot(i));
            simulation.Step(0.05f, registry, environment);
        }
        private sealed class LogicalEnvironment : ITrafficEnvironment
        {
            public RoadSignalAspect Signal(int intersection, Vector3 approach, float simulationTime) => RoadSignalAspect.Green;
            public bool CanPerceive(TrafficVehicleSnapshot observer, TrafficVehicleSnapshot target) => true;
        }
    }
}
#endif
