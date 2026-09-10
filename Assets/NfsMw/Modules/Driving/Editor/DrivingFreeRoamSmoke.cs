#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace NfsMwRemaster.Driving.Editor
{
    [InitializeOnLoad]
    public static class DrivingFreeRoamSmoke
    {
        private const string RunningKey = "Driving.FreeRoamSmoke.Running";
        private const string VisualKey = "Driving.FreeRoamSmoke.IsolatedPreview";
        private static VehicleController vehicle;
        private static float started;
        private static Vector3 origin;
        private static double deadline;
        private static Keyboard keyboard;
        private static FreeRoamSession session;
        private static FreeRoamTraffic traffic;
        private static RockportWorldStreamer streamer;
        private static int stage;
        private static float stageStarted;
        private static RoadVehicleMotor observedCar;
        private static Vector3 carStart;
        private static Vector3 originForward;
        private static int cashBeforeEvent;
        private static int expectedCheckpoints;
        private static int lastCheckpoint;
        private static VehiclePoliceUnit observedCop;
        private static GameObject sightWall;
        private static int escapedBefore;
        private static AmbientPedestrian[] residents;
        private static Vector3[] residentStarts;
        private static RoadNetwork roadNetwork;
        private static RoadLane pursuitLane;

        static DrivingFreeRoamSmoke()
        {
            if (SessionState.GetBool(RunningKey, false)) Subscribe();
        }

        public static void Run()
        {
            if (!Application.isBatchMode && !SessionState.GetBool(VisualKey, false))
                throw new InvalidOperationException("Run this smoke check in a separate Unity batch process.");
            EditorSceneManager.OpenScene(DrivingDemoBuilder.FreeRoamScenePath);
            DrivingDemoBuilder.UpgradeFreeRoamAmbientAI();
            DrivingDemoBuilder.UpgradeFreeRoamAmbientAI();
            Require(UnityEngine.Object.FindObjectsByType<TrafficVehicleLights>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Length == 12, "Ambient upgrade must add exactly one lamp controller per civilian car; police use their own feedback.");
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0,
                    "Missing scene script on " + child.name);
            session = UnityEngine.Object.FindAnyObjectByType<FreeRoamSession>();
            Require(session != null, "Free-roam session missing.");
            var serialized = new SerializedObject(session);
            serialized.FindProperty("resumeSavedProfile").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            UnityEngine.Object.FindAnyObjectByType<JsonCareerProfileStorage>().SetDirectory(
                System.IO.Path.Combine(Application.temporaryCachePath, "free-roam-smoke-" + Guid.NewGuid().ToString("N")));
            SessionState.SetBool(RunningKey, true);
            Subscribe();
            EditorApplication.isPlaying = true;
        }

        public static void BuildAndRun()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch verification only.");
            DrivingDemoBuilder.BuildFreeRoamScene();
            Run();
        }

        public static void RunVisualPreview()
        {
            // A graphical editor is needed for OnGUI / Game-view screenshot capture.
            // Never take ownership of or close the user's working editor.
            if (!Application.dataPath.StartsWith("/private/tmp/nfs-free-roam-", StringComparison.Ordinal))
                throw new InvalidOperationException("Visual smoke must run in an isolated nfs-free-roam temporary project.");
            Type gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null) throw new InvalidOperationException("Game view unavailable.");
            EditorWindow gameView = EditorWindow.GetWindow(gameViewType, false, "Free Roam Preview", true);
            gameView.position = new Rect(80, 80, 1280, 760);
            gameView.Focus();
            SessionState.SetBool(VisualKey, true);
            Run();
        }

        private static void CheckRoad(Vector3 point)
        {
            Ray ray = new Ray(point + Vector3.up * 40, Vector3.down);
            RaycastHit[] hits = Physics.RaycastAll(ray, 41, Physics.DefaultRaycastLayers);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            RaycastHit nearestHit = hits.Length > 0 ? hits[0] : default;
            for (int i = 0; i < hits.Length; i++)
            {
                VehicleSurface candidate = hits[i].collider.GetComponentInParent<VehicleSurface>();
                if (candidate == null || hits[i].point.y > point.y + 1f || hits[i].point.y < point.y - 2f) continue;
                return;
            }
            string hitDescription = hits.Length > 0
                ? nearestHit.collider.name + " at " + nearestHit.point + ", surface="
                    + (nearestHit.collider.GetComponentInParent<VehicleSurface>() != null)
                : "no collider hit";
            throw new InvalidOperationException("Road is obstructed or missing at " + point
                + "; " + hitDescription);
        }

        private static void Subscribe()
        {
            vehicle = null;
            streamer = null;
            stage = 0;
            deadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline)
                    throw new InvalidOperationException("Free roam smoke timed out.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                if (vehicle == null)
                {
                    session = UnityEngine.Object.FindAnyObjectByType<FreeRoamSession>();
                    vehicle = session != null ? session.GetComponent<VehicleController>() : null;
                    traffic = UnityEngine.Object.FindAnyObjectByType<FreeRoamTraffic>();
                    streamer = UnityEngine.Object.FindAnyObjectByType<RockportWorldStreamer>();
                    roadNetwork = UnityEngine.Object.FindAnyObjectByType<RoadNetwork>();
                    Require(session != null && traffic != null && streamer != null && roadNetwork != null,
                        "Free-roam scene bindings missing.");
                    Require(roadNetwork.UsesBakedData && roadNetwork.Publication != null
                        && roadNetwork.Publication.Lanes.Count == 13076,
                        "Free roam is not using the recovered Rockport road publication.");
                    Require(streamer.ChunkCount == 39, "Rockport streaming manifest must contain all 39 terrain-aligned cells.");
                    if (streamer.LoadedChunkCount == 0 || streamer.IsStreaming)
                    {
                        vehicle = null;
                        return;
                    }
                    Require(streamer.LoadedChunkCount <= 6, "Rockport streamer exceeded its resident cell budget.");
                    if (vehicle == null || Camera.main == null || vehicle.Wheels.Length != 4)
                        throw new InvalidOperationException("Player, camera, or wheels missing.");
                    if (traffic.ActivePoliceCount < 1)
                    {
                        vehicle = null;
                        return;
                    }
                    // The first probe holds full throttle to validate the physical road. Keep the already
                    // spawned patrol present, but defer offence reporting until the dedicated pursuit stage.
                    traffic.enabled = false;
                    Application.runInBackground = true;
                    InputSystem.settings = UnityEngine.Object.Instantiate(InputSystem.settings);
                    InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                    InputSystem.settings.editorInputBehaviorInPlayMode =
                        InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                    keyboard = InputSystem.AddDevice<Keyboard>();
                    started = Time.time;
                    origin = vehicle.transform.position;
                    originForward = vehicle.transform.forward;
                    int laneIndex = roadNetwork.Lanes.NearestLane(origin, originForward, out float along);
                    Require(laneIndex >= 0, "Player start has no Rockport driving lane.");
                    pursuitLane = roadNetwork.Lanes[laneIndex];
                    CheckRoad(pursuitLane.Sample(Mathf.Max(0, along - 4), out _));
                    CheckRoad(pursuitLane.Sample(along, out _));
                    CheckRoad(pursuitLane.Sample(Mathf.Min(pursuitLane.Length, along + 4), out _));
                    residents = UnityEngine.Object.FindObjectsByType<AmbientPedestrian>(FindObjectsSortMode.None);
                    Require(residents.Length == 32, "Expected 32 authored sidewalk residents.");
                    residentStarts = new Vector3[residents.Length];
                    for (int i = 0; i < residents.Length; i++) residentStarts[i] = residents[i].transform.position;
                }
                InputSystem.QueueStateEvent(keyboard, stage == 0 ? new KeyboardState(Key.W) : new KeyboardState());
                if (stage > 0) { RunIntegration(); return; }
                if (Time.time - started < 8) return;
                int grounded = 0;
                foreach (var wheel in vehicle.Wheels) if (wheel.Grounded) grounded++;
                float distance = Vector3.Dot(vehicle.transform.position - origin, originForward);
                if (distance < 5 || vehicle.Body.position.y < 0 || grounded < 2)
                    throw new InvalidOperationException("Vehicle failed driving probe: distance=" + distance
                        + ", grounded=" + grounded + ", position=" + vehicle.Body.position
                        + ", throttle=" + vehicle.Telemetry.Throttle);
                Debug.Log("Free roam smoke passed: clear road network; driven=" + distance
                    + "m; grounded wheels=" + grounded);
                var trafficWorld = UnityEngine.Object.FindAnyObjectByType<TrafficWorldDirector>();
                Require(trafficWorld != null && trafficWorld.Capacity == 12 && trafficWorld.Simulation.Statistics.Spawned >= 6,
                    "Free-roam's twelve-rig pool and six authored starting trips were not initialized: capacity="
                    + (trafficWorld == null ? -1 : trafficWorld.Capacity) + ", spawned="
                    + (trafficWorld?.Simulation == null ? -1 : trafficWorld.Simulation.Statistics.Spawned) + ".");
                Require(traffic.ActiveCivilianCount > 0, "No civilian representations are active near free roam.");
                Require(traffic.ActivePoliceCount >= 1, "Police patrol did not spawn.");
                int walkingResidents = 0;
                for (int i = 0; i < residents.Length; i++)
                    if (Vector3.Distance(residentStarts[i], residents[i].transform.position) > 1) walkingResidents++;
                Require(walkingResidents >= 16, "Sidewalk residents did not walk: " + walkingResidents);
                Debug.Log("Ambient smoke passed: " + walkingResidents + "/32 residents progressed along authored sidewalks.");
                observedCar = Array.Find(traffic.Civilians, car => car.gameObject.activeSelf && car.Body.linearVelocity.magnitude > 1);
                if (observedCar == null)
                    foreach (var car in traffic.Civilians)
                        if (car.gameObject.activeSelf) Debug.Log($"Traffic diagnostic: {car.name} pos={car.transform.position} velocity={car.Body.linearVelocity} waiting={car.WaitingForTraffic} stuck={car.StuckTime}");
                Require(observedCar != null, "No traffic car is moving.");
                carStart = observedCar.transform.position;
                traffic.enabled = true;
                string capturePath = Environment.GetEnvironmentVariable("NFS_FREE_ROAM_CAPTURE");
                if (!string.IsNullOrEmpty(capturePath))
                {
                    session.ToggleMap();
                    ScreenCapture.CaptureScreenshot(capturePath);
                }
                NextStage();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Finish(1);
            }
        }

        private static void RunIntegration()
        {
            switch (stage)
            {
                case 1:
                    if (Time.time - stageStarted < 2) return;
                    Require(Vector3.Distance(carStart, observedCar.transform.position) > 1, "Traffic motor did not progress.");
                    TestShopsAndSaving();
                    var race = FindEvent(FreeRoamEventKind.Drag);
                    MovePlayer(race.transform.position, race.transform.rotation);
                    cashBeforeEvent = session.Cash;
                    Require(session.TryStartEvent(race, out string eventFailure), eventFailure);
                    Require(!session.CanDrive, "Countdown did not lock input.");
                    expectedCheckpoints = race.Checkpoints.Length;
                    lastCheckpoint = -1;
                    NextStage();
                    break;
                case 2:
                    if (session.Countdown > 0) return;
                    if (session.EventProgress.CheckpointsPassed < expectedCheckpoints)
                    {
                        Require(session.CanDrive, "Countdown did not release input: " + session.State);
                        if (session.EventProgress.CheckpointsPassed == lastCheckpoint) return;
                        lastCheckpoint = session.EventProgress.CheckpointsPassed;
                        MovePlayer(session.EventProgress.NextCheckpoint, Quaternion.identity);
                        return;
                    }
                    Require(session.State == FreeRoamState.Results, "Event did not enter results: " + session.State);
                    Require(session.Cash == cashBeforeEvent + FindEvent(FreeRoamEventKind.Drag).Reward, "Event reward mismatch.");
                    Require(session.CompletedEvents.Count > 0, "Event completion was not recorded.");
                    Debug.Log("Free roam integration: purchase, profile save/load, countdown, checkpoints and reward passed.");
                    session.ExitActivity();
                    Vector3 pursuitPoint = pursuitLane.Sample(pursuitLane.Length * 0.7f, out Vector3 pursuitDirection);
                    Quaternion pursuitRotation = Quaternion.LookRotation(pursuitDirection, Vector3.up);
                    MovePlayer(pursuitPoint, pursuitRotation);
                    observedCop = Array.Find(traffic.Police, cop => cop.gameObject.activeSelf && !cop.IsDisabled);
                    observedCop ??= Array.Find(traffic.Police, cop => cop != null);
                    Require(observedCop != null, "No patrol rig is available for the witnessed-offence probe.");
                    observedCop.gameObject.SetActive(false);
                    observedCop.PlaceWhileInactive(pursuitPoint - pursuitDirection * 25 + Vector3.up * 0.65f, pursuitRotation);
                    observedCop.gameObject.SetActive(true);
                    NextStage();
                    break;
                case 3:
                    // Speed in sight of a live patrol. Production offence detection starts the pursuit.
                    pursuitLane.Sample(pursuitLane.Length * 0.7f, out Vector3 speedingDirection);
                    vehicle.Body.linearVelocity = speedingDirection * 24;
                    if (!session.Pursuit.IsActive)
                    {
                        Require(Time.time - stageStarted < 5, "Patrol failed to detect speeding.");
                        return;
                    }
                    vehicle.Body.linearVelocity = Vector3.zero;
                    vehicle.Body.angularVelocity = Vector3.zero;
                    Require(!session.TryRecover(out _), "Pursuit recovery loophole.");
                    Require(!session.TrySave(out _), "Pursuit save loophole.");
                    Require(session.Pursuit.TrySetHeatLevel(5, out string heatFailure), heatFailure);
                    NextStage();
                    break;
                case 4:
                    if (Time.time - stageStarted < 15) return;
                    Require(traffic.ActivePoliceCount >= 4, "Heat did not dispatch reinforcements: " + traffic.ActivePoliceCount
                        + "; encounter=" + session.Pursuit.EncounterState + "; heat=" + session.Pursuit.HeatLevel
                        + "; player=" + vehicle.Body.position + "; lastKnown=" + session.Pursuit.LastKnownPosition);
                    session.Pursuit.TrySetHeatLevel(1, out _);
                    // Keep police close, but hide the target behind a real collider. This exercises
                    // search -> cooldown -> escape without the debug force-escape command.
                    traffic.enabled = false;
                    Vector3 hidePoint = pursuitLane.Sample(pursuitLane.Length * 0.8f, out Vector3 hideDirection);
                    Quaternion hideRotation = Quaternion.LookRotation(hideDirection, Vector3.up);
                    MovePlayer(hidePoint, hideRotation);
                    vehicle.Body.isKinematic = true; vehicle.enabled = false;
                    foreach (VehiclePoliceUnit cop in traffic.Police)
                    {
                        if (!cop.gameObject.activeSelf) continue;
                        cop.gameObject.SetActive(false);
                        cop.PlaceWhileInactive(hidePoint - hideDirection * 25 + Vector3.up * 0.65f, hideRotation);
                        cop.gameObject.SetActive(true);
                        cop.Vehicle.enabled = false; cop.Vehicle.Body.isKinematic = true;
                    }
                    sightWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    sightWall.name = "Smoke LOS Occluder";
                    sightWall.transform.SetPositionAndRotation(hidePoint - hideDirection * 12.5f + Vector3.up * 3f, hideRotation);
                    sightWall.transform.localScale = new Vector3(16, 6, 2); Physics.SyncTransforms();
                    escapedBefore = vehicle.GetComponent<VehicleBountySystem>().PursuitsEscaped;
                    NextStage();
                    break;
                case 5:
                    if (session.Pursuit.IsActive)
                    {
                        Require(Time.time - stageStarted < 35, "Line-of-sight escape failed: " + session.Pursuit.Phase);
                        return;
                    }
                    Require(vehicle.GetComponent<VehicleBountySystem>().PursuitsEscaped == escapedBefore + 1, "Escape did not commit bounty.");
                    Debug.Log("Free roam integration passed: traffic movement, shops, persistence, event reward, witnessed speeding, reinforcements, LOS search/cooldown/escape.");
                    // Civilian queue/LOD/physics coverage now lives in DrivingTrafficSmoke.
                    Finish(0);
                    break;
            }
        }

        private static void TestShopsAndSaving()
        {
            WorldLocation safehouse = FindLocation(WorldLocationKind.Safehouse);
            WorldLocation shop = FindLocation(WorldLocationKind.PerformanceShop);
            Require(!session.TryEnter(shop, out _), "Shop opened from across the map.");
            session.NavigateTo(shop.Position); Require(session.NavigationRoute.Count >= 3, "Map route missing.");
            session.TogglePause(); Require(Time.timeScale == 0 && !session.CanDrive, "Pause failed.");
            session.TogglePause(); Require(Time.timeScale > 0, "Resume failed.");
            MovePlayer(shop.Position, Quaternion.identity);
            Require(session.TryEnter(shop, out string enterFailure), enterFailure);
            int before = session.Cash; bool purchased = false;
            foreach (IVehicleStoreProduct product in session.Store.VisibleProducts)
            {
                if (product.Price <= 0 || product.Price > before) continue;
                if (!session.TryPurchase(product.ProductId, out _)) continue;
                purchased = true; break;
            }
            Require(purchased && session.Cash < before, "No purchasable performance product.");
            int savedCash = session.Cash;
            session.ExitActivity(); MovePlayer(safehouse.Position, Quaternion.identity);
            Require(session.TryEnter(safehouse, out string safeFailure), safeFailure);
            vehicle.GetComponent<VehicleStoreWallet>().SetBalance(1);
            Require(session.TryLoad(out string loadFailure), loadFailure);
            Require(session.Cash == savedCash, "Wallet was not restored from profile.");
            Require(session.State == FreeRoamState.Driving, "Loading did not release location state.");
        }

        private static WorldLocation FindLocation(WorldLocationKind kind)
        { foreach (WorldLocation location in session.Locations) if (location.Kind == kind) return location; throw new InvalidOperationException("Location missing: " + kind); }
        private static FreeRoamEventDefinition FindEvent(FreeRoamEventKind kind)
        { foreach (var definition in session.Events) if (definition.Kind == kind) return definition; throw new InvalidOperationException("Event missing: " + kind); }
        private static void MovePlayer(Vector3 point, Quaternion rotation)
        {
            vehicle.Body.linearVelocity = Vector3.zero; vehicle.Body.angularVelocity = Vector3.zero;
            vehicle.Body.position = point + Vector3.up * 0.8f; vehicle.Body.rotation = rotation;
            vehicle.transform.SetPositionAndRotation(vehicle.Body.position, rotation); Physics.SyncTransforms();
        }
        private static void NextStage() { stage++; stageStarted = Time.time; }
        private static void Require(bool condition, string failure) { if (!condition) throw new InvalidOperationException(failure); }

        private static void Finish(int code)
        {
            SessionState.SetBool(RunningKey, false);
            SessionState.SetBool(VisualKey, false);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            EditorApplication.update -= Poll;
            EditorApplication.Exit(code);
        }

    }
}
#endif
