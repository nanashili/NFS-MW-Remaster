#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public static partial class DrivingDemoBuilder
    {
        private static void ConfigureFreeRoamGameplay(VehicleController vehicle, Transform world,
            Transform landmarks, ShopTestContent content, Material blue, Material yellow)
        {
            var services = new GameObject("Free Roam Services"); services.transform.SetParent(world, false);
            RoadNetwork roads = services.AddComponent<RoadNetwork>();
            float[] axes = { -220, -140, 0, 140, 220 };
            var nodes = new RoadNode[25];
            for (int z = 0; z < 5; z++)
            for (int x = 0; x < 5; x++)
            {
                var exits = new List<int>();
                if (x > 0) exits.Add(z * 5 + x - 1);
                if (x < 4) exits.Add(z * 5 + x + 1);
                if (z > 0) exits.Add((z - 1) * 5 + x);
                if (z < 4) exits.Add((z + 1) * 5 + x);
                nodes[z * 5 + x] = new RoadNode { position = new Vector3(axes[x], 0, axes[z]), exits = exits.ToArray() };
            }
            roads.Configure(nodes);
            var signals = services.AddComponent<RoadTrafficSignals>();
            var locations = new[]
            {
                ConnectFreeRoamLocation(landmarks, "SAFEHOUSE", "safehouse", WorldLocationKind.Safehouse, null, blue),
                ConnectFreeRoamLocation(landmarks, "GARAGE", "garage", WorldLocationKind.Garage, content.OneStopShop, blue),
                ConnectFreeRoamLocation(landmarks, "BODY SHOP", "body_shop", WorldLocationKind.BodyShop, content.BodyShop, blue),
                ConnectFreeRoamLocation(landmarks, "PERFORMANCE SHOP", "performance_shop", WorldLocationKind.PerformanceShop, content.PerformanceShop, yellow),
                ConnectFreeRoamLocation(landmarks, "CAR SHOW", "car_show", WorldLocationKind.CarShow, content.CarShow, yellow),
                ConnectFreeRoamLocation(landmarks, "POLICE STATION", "police_station", WorldLocationKind.PoliceStation, null, blue)
            };
            var eventRoot = new GameObject("Free Roam Event Markers").transform; eventRoot.SetParent(world, false);
            var events = new[]
            {
                CreateFreeRoamEvent(eventRoot, "city_sprint", "Warehouse Sprint", FreeRoamEventKind.Sprint, new Vector3(5, 0, -110),
                    new[] { new Vector3(3, 0, -10), new Vector3(3, 0, 140), new Vector3(140, 0, 143), new Vector3(217, 0, 143), new Vector3(217, 0, 210) }, 1, 100, 1800, yellow),
                CreateFreeRoamEvent(eventRoot, "outer_circuit", "Outer Ring Circuit", FreeRoamEventKind.Circuit, new Vector3(-195, 0, -217),
                    new[] { new Vector3(-140, 0, -217), new Vector3(140, 0, -217), new Vector3(217, 0, -217), new Vector3(217, 0, 0), new Vector3(217, 0, 217), new Vector3(0, 0, 217), new Vector3(-217, 0, 217), new Vector3(-217, 0, 0), new Vector3(-217, 0, -217), new Vector3(-195, 0, -217) }, 2, 240, 3500, yellow),
                CreateFreeRoamEvent(eventRoot, "main_drag", "Main Street Drag", FreeRoamEventKind.Drag, new Vector3(5, 0, -200),
                    new[] { new Vector3(3, 0, -140), new Vector3(3, 0, 0), new Vector3(3, 0, 140), new Vector3(3, 0, 200) }, 1, 45, 1200, yellow),
                CreateFreeRoamEvent(eventRoot, "west_speedtrap", "West Avenue Speedtrap — 100 km/h", FreeRoamEventKind.Speedtrap, new Vector3(-137, 0, -190),
                    new[] { new Vector3(-137, 0, -140), new Vector3(-137, 0, 0), new Vector3(-137, 0, 140), new Vector3(-137, 0, 200) }, 1, 60, 1600, yellow),
                CreateFreeRoamEvent(eventRoot, "pursuit_milestone", "Survive 20 Seconds Then Escape", FreeRoamEventKind.Pursuit, new Vector3(180, 0, 65),
                    new[] { new Vector3(180, 0, 65) }, 1, 240, 2500, yellow)
            };
            var bounty = vehicle.GetComponent<VehicleBountySystem>(); bounty.SetStartingHeatLevel(0);
            var target = vehicle.GetComponent<VehiclePursuitTargetAdapter>();
            if (target == null) target = vehicle.gameObject.AddComponent<VehiclePursuitTargetAdapter>();
            target.Configure("free_roam_player");
            var perception = services.AddComponent<RoadPolicePerception>();
            var director = services.AddComponent<VehiclePursuitDirector>(); director.Configure(target, bounty, null); director.SetPerception(perception);
            var session = vehicle.gameObject.AddComponent<FreeRoamSession>();
            // A standalone free-roam launch must honor the authored highway
            // start. Career resume is initiated explicitly by GameFlow.
            session.Configure(vehicle, roads, director, locations, events, false);
            var input = vehicle.gameObject.AddComponent<FreeRoamVehicleInput>(); input.Configure(vehicle.GetComponent<PlayerVehicleInput>(), session);
            vehicle.SetInputSource(input);
            var profile = vehicle.GetComponent<CareerProfileSystem>(); profile.ConfigureAutomaticPersistence(false, false, false);
            var telemetry = vehicle.GetComponent<VehicleTelemetryHud>(); if (telemetry != null) telemetry.enabled = false;
            var trafficRoot = new GameObject("Pooled Civilian Traffic").transform; trafficRoot.SetParent(world, false);
            var policeRoot = new GameObject("Pooled Police Patrols and Response").transform; policeRoot.SetParent(world, false);
            var cars = new RoadVehicleMotor[12]; var cops = new VehiclePoliceUnit[8];
            Material policePaint = FreeRoamMaterial("Police", new Color(0.035f, 0.04f, 0.055f));
            Material white = FreeRoamMaterial("White", Color.white);
            Material red = FreeRoamMaterial("Red", new Color(0.95f, 0.025f, 0.035f));
            for (int i = 0; i < cars.Length; i++) cars[i] = CreateRoadCar(trafficRoot, "Traffic " + (i + 1), roads, signals, i % 2 == 0 ? blue : yellow, white, false);
            for (int i = 0; i < cops.Length; i++)
            {
                cops[i] = CreatePoliceUnit(policeRoot, "road_police_" + i,
                    i % 3 == 0 ? VehiclePoliceUnitRole.Interceptor : VehiclePoliceUnitRole.Pursuer,
                    new Vector3(0, -30, 0), policePaint, white, red, blue);
                cops[i].gameObject.SetActive(false);
                cops[i].Configure("road_police_" + i, cops[i].Role, director);
                cops[i].ConfigureNavigation(roads, signals);
            }
            var population = services.AddComponent<FreeRoamTraffic>(); population.Configure(roads, vehicle, director, perception, cars, cops);
            var hazardRoot = new GameObject("Authored Police Hazard Sites").transform; hazardRoot.SetParent(world, false);
            foreach (float x in new[] { -140f, 0f, 140f })
            {
                CreatePoliceRoadHazard(hazardRoot, director, new Vector3(x, 0.2f, 70f), Quaternion.identity,
                    PoliceRoadHazardKind.Roadblock, white, red);
                CreatePoliceRoadHazard(hazardRoot, director, new Vector3(x, 0.2f, -70f), Quaternion.Euler(0, 180, 0),
                    PoliceRoadHazardKind.SpikeStrip, white, red);
            }
            vehicle.gameObject.AddComponent<FreeRoamOffenceReporter>().Configure(population);
            services.AddComponent<FreeRoamHud>().Configure(session, roads, population, vehicle);
            var checkpoint = new GameObject("Active Event Checkpoint"); checkpoint.transform.SetParent(world, false);
            var line = checkpoint.AddComponent<LineRenderer>(); line.sharedMaterial = yellow; line.loop = true;
            line.useWorldSpace = true; line.positionCount = 48; line.widthMultiplier = 0.5f; line.enabled = false;
            checkpoint.AddComponent<FreeRoamCheckpointView>().Configure(session);
        }

        private static WorldLocation ConnectFreeRoamLocation(Transform landmarks, string label, string id,
            WorldLocationKind kind, AssetVehicleStorefront store, Material material)
        {
            Transform landmark = landmarks.Find(label + " - Rockport Anchor") ??
                landmarks.Find(label + " - Placeholder");
            if (landmark == null)
                throw new System.InvalidOperationException("Rockport location anchor was not found for " + label + ".");
            var marker = new GameObject(label + " Entry"); marker.transform.SetParent(landmark, false);
            marker.transform.localPosition = new Vector3(0, 0, -10);
            var location = marker.AddComponent<WorldLocation>(); location.Configure(id, label, kind, store);
            CreateWorldMarker(marker.transform, material, 10);
            return location;
        }

        private static FreeRoamEventDefinition CreateFreeRoamEvent(Transform parent, string id, string label,
            FreeRoamEventKind kind, Vector3 start, Vector3[] checkpoints, int laps, float limit, int cash, Material material)
        {
            var marker = new GameObject(label); marker.transform.SetParent(parent, false); marker.transform.position = start;
            if ((checkpoints[0] - start).sqrMagnitude > 0.1f) marker.transform.rotation = Quaternion.LookRotation(checkpoints[0] - start);
            var definition = marker.AddComponent<FreeRoamEventDefinition>(); definition.Configure(id, label, kind, checkpoints, laps, limit, cash);
            CreateWorldMarker(marker.transform, material, 9);
            var sign = new GameObject("Event Sign"); sign.transform.SetParent(marker.transform, false); sign.transform.localPosition = Vector3.up * 4;
            var text = sign.AddComponent<TextMesh>(); text.text = kind + "\nSTOP + E"; text.fontSize = 42; text.characterSize = 0.13f;
            text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
            return definition;
        }

        private static void CreateWorldMarker(Transform parent, Material material, float radius)
        {
            var ring = new GameObject("Interaction Ring").AddComponent<LineRenderer>(); ring.transform.SetParent(parent, false);
            ring.sharedMaterial = material; ring.loop = true; ring.useWorldSpace = false; ring.widthMultiplier = 0.22f; ring.positionCount = 48;
            for (int i = 0; i < 48; i++) { float angle = i * Mathf.PI / 24; ring.SetPosition(i, new Vector3(Mathf.Sin(angle) * radius, 0.22f, Mathf.Cos(angle) * radius)); }
        }

        private static RoadVehicleMotor CreateRoadCar(Transform parent, string name, RoadNetwork roads,
            RoadTrafficSignals signals, Material paint, Material white, bool police)
        {
            var root = new GameObject(name); root.SetActive(false); root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(0, -30, 0);
            root.AddComponent<Rigidbody>(); var collider = root.AddComponent<BoxCollider>(); collider.size = new Vector3(1.8f, 1.25f, 4.1f); collider.center = Vector3.up * 0.1f;
            CreateVisualBox("Body", root.transform, Vector3.zero, new Vector3(1.8f, 0.7f, 4.1f), paint);
            CreateVisualBox("Cabin", root.transform, new Vector3(0, 0.6f, -0.2f), new Vector3(1.55f, 0.65f, 1.9f), police ? white : paint);
            Material tire = FreeRoamMaterial("Tire", new Color(0.02f, 0.02f, 0.02f));
            var motor = root.AddComponent<RoadVehicleMotor>(); motor.Configure(roads, signals, police ? 14 : 11);
            motor.ConfigureDriver(parent.childCount * 7919);
            ConfigureTrafficRig(motor, tire, (TrafficVehicleCategory)((parent.childCount - 1) % 7));
            AddTrafficLights(motor, white);
            return motor;
        }

        private static void AddTrafficLights(RoadVehicleMotor motor, Material material)
        {
            if (motor.GetComponent<TrafficVehicleLights>() != null) return;
            var brakes = new Renderer[2]; var left = new Renderer[2]; var right = new Renderer[2];
            for (int i = 0; i < 2; i++)
            {
                brakes[i] = CreateVisualBox("Brake Lamp", motor.transform, new Vector3(i == 0 ? -0.6f : 0.6f, 0.15f, -2.07f), new Vector3(0.3f, 0.18f, 0.06f), material).GetComponent<Renderer>();
                left[i] = CreateVisualBox("Left Indicator", motor.transform, new Vector3(-0.83f, 0.15f, i == 0 ? -2.07f : 2.07f), new Vector3(0.12f, 0.18f, 0.06f), material).GetComponent<Renderer>();
                right[i] = CreateVisualBox("Right Indicator", motor.transform, new Vector3(0.83f, 0.15f, i == 0 ? -2.07f : 2.07f), new Vector3(0.12f, 0.18f, 0.06f), material).GetComponent<Renderer>();
            }
            motor.gameObject.AddComponent<TrafficVehicleLights>().Configure(brakes, left, right);
        }

        [UnityEditor.MenuItem("NFS MW Remaster/Upgrade Current Free Roam Ambient AI")]
        public static void UpgradeFreeRoamAmbientAI()
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != FreeRoamScenePath) { Debug.LogError("Open " + FreeRoamScenePath + " first."); return; }
            Transform world = GameObject.Find("Free Roam District")?.transform;
            if (world == null) { Debug.LogError("Free Roam District was not found."); return; }
            Material yellow = FreeRoamMaterial("Yellow", new Color(1, 0.74f, 0.12f));
            foreach (RoadVehicleMotor motor in world.GetComponentsInChildren<RoadVehicleMotor>(true)) AddTrafficLights(motor, yellow);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Added traffic lamps to the existing Rockport free-roam scene. Save the scene to keep the additions.");
        }
    }
}
#endif
