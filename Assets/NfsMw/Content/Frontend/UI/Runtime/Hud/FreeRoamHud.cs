using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public sealed class FreeRoamHud : MonoBehaviour
    {
        [SerializeField] private FreeRoamSession session;
        [SerializeField] private RoadNetwork roads;
        [SerializeField] private FreeRoamTraffic traffic;
        [SerializeField] private VehicleController player;
        private Vector2 scroll;
        private GUIStyle title;
        private GUIStyle body;
        private GUIStyle small;
        private string feedback;
        private Texture2D roadMapTexture;
        private IReadOnlyList<RoadNode> roadMapNodes;
        public void Configure(FreeRoamSession game, RoadNetwork network, FreeRoamTraffic population, VehicleController vehicle)
        { session = game; roads = network; traffic = population; player = vehicle; }

        private void OnGUI()
        {
            if (session == null || roads == null || player == null) return;
            if (session.ManagedFlow && GameFlowRuntime.Instance?.AllowsWorldInput != true) return;
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
                body = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
                small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            }
            Matrix4x4 original = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1280f, Screen.height / 720f, 1));
            Panel(new Rect(18, 18, 480, 158));
            GUILayout.BeginArea(new Rect(32, 26, 452, 145));
            GUILayout.Label("ROCKPORT / FREE ROAM", title);
            GUILayout.Label($"{player.Telemetry.SpeedKph:000} km/h   {player.Telemetry.GearLabel}   CASH ${session.Cash:N0}"
                + (session.NavigationRoute.Count > 0 ? $"   GPS {session.NavigationDistance:0}m" : ""), body);
            var pursuit = session.Pursuit;
            GUILayout.Label(pursuit != null && pursuit.IsActive
                ? $"POLICE  ${pursuit.CurrentFine} / LEVEL {pursuit.HeatLevel} / {pursuit.EncounterState} / BUST {pursuit.BustProgress:P0} / ESCAPE {pursuit.CooldownProgress:P0}"
                : traffic != null && traffic.isActiveAndEnabled
                    ? "PATROLS ACTIVE  /  speed limit 70 km/h" : "FREE ROAM", body);
            GUILayout.Label(session.Status, small);
            GUILayout.EndArea();
            if (pursuit != null && pursuit.CanPayFine)
            {
                if (GUI.Button(new Rect(18, 181, 480, 32), $"Stop and pay fine: ${pursuit.CurrentFine}")) pursuit.TryPayFine(out feedback);
            }
            else if (pursuit != null && pursuit.EncounterState == PoliceEncounterState.OutcomePending)
                GUI.Label(new Rect(18, 181, 600, 45), "Settlement pending: " + pursuit.Status, small);
            if (!MapInputFocus.HasPresentation) DrawMap(session.MapOpen ? new Rect(635, 18, 620, 620) : new Rect(995, 18, 260, 260));
            DrawEvent();
            if (session.State == FreeRoamState.Location) DrawLocation();
            else if (session.State == FreeRoamState.Paused || session.State == FreeRoamState.Results) DrawMenu();
            else DrawPrompt();
            Panel(new Rect(18, 657, 1237, 48));
            GUI.Label(new Rect(30, 661, 1210, 42),
                "WASD / arrows: drive  •  Space: handbrake  •  Shift: nitrous  •  C: camera  •  E: enter  •  M: map  •  R: recover  •  Esc: pause/back\nGamepad: sticks + triggers drive, A handbrake, LB nitrous, RB camera, X enter, View map, Menu pause", small);
            GUI.matrix = original;
        }

        private void DrawPrompt()
        {
            foreach (var instance in ActivityRegistry.Loaded)
                if (instance.Data.Contains(player.transform.position))
                {
                    bool eligible = session.ActivityEligible(instance, out string reason);
                    GUI.Label(new Rect(30, 565, 580, 84), eligible ? "E / X — " + instance.Data.label : reason, title); return;
                }
            foreach (WorldLocation location in session.Locations)
                if (Vector3.Distance(player.transform.position, location.Position) <= location.Radius)
                { GUI.Label(new Rect(30, 590, 580, 60), "E / X — Stop to enter " + location.DisplayName, title); return; }
            foreach (FreeRoamEventDefinition definition in session.Events)
                if (Vector3.Distance(player.transform.position, definition.transform.position) <= 15)
                { GUI.Label(new Rect(30, 565, 580, 84), $"E / X — {definition.DisplayName}\n{definition.Kind}  /  ${definition.Reward:N0}", title); return; }
        }

        private void DrawEvent()
        {
            if (session.ActiveEvent == null) return;
            Panel(new Rect(18, 186, 480, 95));
            var progress = session.EventProgress;
            string value = session.Countdown > 0 ? $"READY  {Mathf.CeilToInt(session.Countdown)}"
                : session.ActiveEvent.Kind == FreeRoamEventKind.Pursuit ? session.Status
                : $"{progress.Remaining:0.0}s   CP {progress.CheckpointsPassed}/{progress.TotalCheckpoints}   Lap {progress.Lap}/{session.ActiveEvent.Laps}";
            GUI.Label(new Rect(32, 194, 450, 36), session.ActiveEvent.DisplayName, title);
            GUI.Label(new Rect(32, 230, 450, 45), value, body);
        }


        private void DrawLocation()
        {
            Panel(new Rect(18, 187, 596, 458));
            GUILayout.BeginArea(new Rect(32, 198, 568, 433));
            GUILayout.Label(session.ActiveLocation.DisplayName, title);
            if (session.ActiveLocation.Kind == WorldLocationKind.Safehouse || session.ActiveLocation.Kind == WorldLocationKind.Garage)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Save profile", GUILayout.Height(32))) session.TrySave(out feedback);
                if (GUILayout.Button("Load saved profile", GUILayout.Height(32))) session.TryLoad(out feedback);
                GUILayout.EndHorizontal();
                if (session.State != FreeRoamState.Location) { GUILayout.EndArea(); return; }
            }
            if (session.ActiveLocation.Storefront != null && session.Store != null)
            {
                if (session.ActiveLocation.Kind == WorldLocationKind.CarShow)
                    GUILayout.Label("Cars are added to garage ownership. Vehicle models / switching come with the asset integration.", small);
                scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(290));
                foreach (IVehicleStoreProduct product in session.Store.VisibleProducts)
                {
                    GUILayout.BeginHorizontal(GUI.skin.box);
                    GUILayout.Label(product.DisplayName + $"\n${product.Price:N0} / {product.ProductKind}", body, GUILayout.Width(390));
                    if (GUILayout.Button("Buy", GUILayout.Width(120), GUILayout.Height(42))) session.TryPurchase(product.ProductId, out feedback);
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }
            else GUILayout.Label(session.ActiveLocation.Kind == WorldLocationKind.PoliceStation
                ? "Police station. Patrols operate throughout the district. This location is inaccessible during a pursuit."
                : "Progress resumes here after loading. Discover the district, win events and upgrade your car.", body);
            if (!string.IsNullOrEmpty(feedback)) GUILayout.Label(feedback, small);
            if (GUILayout.Button("Return to free roam  [Esc]", GUILayout.Height(34))) { feedback = null; session.ExitActivity(); }
            GUILayout.EndArea();
        }

        private void DrawMenu()
        {
            Panel(new Rect(18, 300, 480, 260));
            GUILayout.BeginArea(new Rect(32, 312, 452, 240));
            GUILayout.Label(session.State == FreeRoamState.Paused ? "PAUSED" : "EVENT RESULT", title);
            GUILayout.Label(session.Status, body);
            if (GUILayout.Button("Continue / return", GUILayout.Height(36))) session.ExitActivity();
            if (session.State == FreeRoamState.Results && GUILayout.Button("Retry event", GUILayout.Height(32))) session.RetryEvent(out feedback);
            if (session.State == FreeRoamState.Paused)
            {
                if (GUILayout.Button("Save profile (outside events / pursuits)", GUILayout.Height(32))) session.TrySave(out feedback);
                if (GUILayout.Button("Abandon event / return to driving", GUILayout.Height(32))) { session.TogglePause(); session.ExitActivity(); }
            }
            if (!string.IsNullOrEmpty(feedback)) GUILayout.Label(feedback, small);
            GUILayout.EndArea();
        }

        private void DrawMap(Rect rect)
        {
            Panel(rect);
            Rect map = new Rect(rect.x + 12, rect.y + 25, rect.width - 24, rect.height - 54);
            GUI.Label(new Rect(rect.x + 10, rect.y + 3, rect.width - 20, 22), "N ↑   DISTRICT MAP   [M]", small);
            DrawRoadBackground(map);
            var route = session.NavigationRoute;
            for (int i = 1; i < route.Count; i++) Line(MapPoint(route[i - 1], map), MapPoint(route[i], map), Color.cyan, 3);
            foreach (var car in traffic.Civilians) if (car.gameObject.activeSelf) Dot(MapPoint(car.transform.position, map), Color.gray, 3);
            foreach (var cop in traffic.Police) if (session.Pursuit != null && session.Pursuit.CanDisplayUnit(cop)) Dot(MapPoint(cop.Position, map), Color.red, 5);
            foreach (WorldLocation location in session.Locations)
            {
                if (location.GetComponent<WorldActivityInstance>() != null) continue;
                Vector2 point = MapPoint(location.Position, map);
                Dot(point, Color.cyan, 7);
                string label = location.Kind == WorldLocationKind.PerformanceShop ? "Performance"
                    : location.Kind == WorldLocationKind.BodyShop ? "Body shop"
                    : location.Kind == WorldLocationKind.CarShow ? "Car show"
                    : location.Kind == WorldLocationKind.PoliceStation ? "Police station" : location.DisplayName;
                if (session.MapOpen && GUI.Button(new Rect(point.x - 52, point.y - 11, 104, 25), label)) session.NavigateTo(location.Position);
            }
            foreach (FreeRoamEventDefinition definition in session.Events)
            {
                if (definition.GetComponent<WorldActivityInstance>() != null) continue;
                Vector2 point = MapPoint(definition.transform.position, map);
                Dot(point, Color.yellow, 6);
                if (session.MapOpen && GUI.Button(new Rect(point.x - 38, point.y - 10, 76, 22), definition.Kind.ToString())) session.NavigateTo(definition.transform.position);
            }
            foreach (var marker in ActivityMapRegistry.Markers)
            {
                var activity = marker.Data;
                bool eligible = session.ActivityEligible(marker);
                if (!eligible && activity.hideWhenLocked) continue;
                Vector2 point = MapPoint(activity.icon, map);
                Dot(point, eligible ? activity.color : Color.gray, 7);
                if (session.MapOpen && GUI.Button(new Rect(point.x - 52, point.y - 11, 104, 25), activity.label + (marker.Loaded ? "" : " · unloaded"))) session.NavigateTo(activity.access);
            }
            if (session.ActiveEvent != null && session.ActiveEvent.Kind != FreeRoamEventKind.Pursuit)
                Dot(MapPoint(session.EventProgress.NextCheckpoint, map), Color.green, 11);
            Vector2 playerPoint = MapPoint(player.transform.position, map);
            Dot(playerPoint, Color.white, 7);
            Vector3 forward = player.transform.forward * 18;
            Line(playerPoint, MapPoint(player.transform.position + forward, map), Color.white, 2);
            GUI.Label(new Rect(rect.x + 10, rect.yMax - 27, rect.width - 20, 24), "Cyan shops  •  Yellow events  •  Red police", small);
            if (session.MapOpen && GUI.Button(new Rect(rect.xMax - 110, rect.y + 2, 100, 21), "Clear route")) session.ClearNavigation();
        }

        private static Vector2 MapPoint(Vector3 point, Rect map)
            => new Vector2(map.x + (Mathf.Clamp(point.x, -250, 250) + 250) / 500 * map.width,
                map.y + (250 - Mathf.Clamp(point.z, -250, 250)) / 500 * map.height);

        private void OnDestroy()
        {
            if (roadMapTexture != null) Destroy(roadMapTexture);
        }

        private void DrawRoadBackground(Rect map)
        {
            if (Event.current.type != EventType.Repaint) return;
            int width = Mathf.Max(1, Mathf.RoundToInt(map.width));
            int height = Mathf.Max(1, Mathf.RoundToInt(map.height));
            var nodes = roads.Nodes;
            if (roadMapTexture == null || !ReferenceEquals(roadMapNodes, nodes)
                || roadMapTexture.width != width || roadMapTexture.height != height)
            {
                if (roadMapTexture != null) Destroy(roadMapTexture);
                var pixels = new Color32[width * height];
                var local = new Rect(0, 0, width - 1, height - 1);
                var color = (Color32)new Color(.38f, .43f, .48f, 1f);
                for (int i = 0; i < nodes.Count; i++)
                    foreach (int next in nodes[i].exits)
                    {
                        if (next <= i || next >= nodes.Count) continue;
                        Vector2 from = MapPoint(nodes[i].position, local);
                        Vector2 to = MapPoint(nodes[next].position, local);
                        if (from == to) continue;
                        int x = Mathf.RoundToInt(from.x), y = Mathf.RoundToInt(from.y);
                        int endX = Mathf.RoundToInt(to.x), endY = Mathf.RoundToInt(to.y);
                        int dx = Mathf.Abs(endX - x), dy = -Mathf.Abs(endY - y);
                        int sx = x < endX ? 1 : -1, sy = y < endY ? 1 : -1, error = dx + dy;
                        while (true)
                        {
                            // Four-pixel stroke, matching the existing fallback map. Texture Y
                            // is inverted because IMGUI positions are measured from the top.
                            for (int oy = -1; oy <= 2; oy++)
                                for (int ox = -1; ox <= 2; ox++)
                                {
                                    int px = x + ox, py = y + oy;
                                    if (px >= 0 && px < width && py >= 0 && py < height)
                                        pixels[(height - 1 - py) * width + px] = color;
                                }
                            if (x == endX && y == endY) break;
                            int twice = error * 2;
                            if (twice >= dy) { error += dy; x += sx; }
                            if (twice <= dx) { error += dx; y += sy; }
                        }
                    }
                roadMapTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    name = "Free roam static road map",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                roadMapTexture.SetPixels32(pixels);
                roadMapTexture.Apply(false, true);
                roadMapNodes = nodes;
            }
            GUI.DrawTexture(map, roadMapTexture);
        }

        private static void Panel(Rect rect) { Color previous = GUI.color; GUI.color = new Color(0.045f, 0.06f, 0.085f, 0.93f); GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous; }
        private static void Dot(Vector2 point, Color color, float size) { Color previous = GUI.color; GUI.color = color; GUI.DrawTexture(new Rect(point.x - size / 2, point.y - size / 2, size, size), Texture2D.whiteTexture); GUI.color = previous; }
        private static void Line(Vector2 from, Vector2 to, Color color, float width)
        {
            // The fallback map clamps distant roads to its boundary. Most segments in a
            // full city collapse to one point; submitting those still incurs a GUI draw call.
            if (Event.current.type != EventType.Repaint || from == to) return;
            Matrix4x4 matrix = GUI.matrix; Color previous = GUI.color;
            GUI.color = color; GUIUtility.RotateAroundPivot(Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg, from);
            GUI.DrawTexture(new Rect(from.x, from.y - width / 2, Vector2.Distance(from, to), width), Texture2D.whiteTexture);
            GUI.matrix = matrix; GUI.color = previous;
        }
    }
}
