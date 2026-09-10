using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NfsMwRemaster.Driving
{
    public sealed class TrafficDebugPanel : MonoBehaviour
    {
        [SerializeField] private TrafficWorldDirector world;
        [SerializeField] private bool visible;
        private readonly Sample[] samples = new Sample[8192];
        private int head, count, overwritten;
        private float nextSample;
        private string exported;
        private int[] previousTrips, laneVehicles;
        private float[] previousSpeeds, previousTimes;
        private struct Sample
        {
            public float time, speed, acceleration, gap, desiredGap, safety, ttc, measuredAcceleration, sampleDuration;
            public int id, lane, lod, decision, intersection;
        }
        public void Configure(TrafficWorldDirector director) { world = director; }
        private void Update()
        {
            if (Keyboard.current?.f9Key.wasPressedThisFrame == true) visible = !visible;
            if (world?.Simulation == null || Time.time < nextSample) return;
            nextSample = Time.time + 0.2f;
            var sim = world.Simulation;
            if (previousTrips == null)
            {
                previousTrips = new int[sim.Capacity]; previousSpeeds = new float[sim.Capacity]; previousTimes = new float[sim.Capacity];
                laneVehicles = new int[world.Roads.Lanes.Count];
            }
            Array.Clear(laneVehicles, 0, laneVehicles.Length);
            for (int i = 0; i < sim.Capacity; i++)
            {
                TrafficAgentState a = sim[i]; if (!a.Active) continue;
                float duration = previousTrips[i] == a.Id ? sim.Statistics.Elapsed - previousTimes[i] : 0;
                float measured = duration > 0 ? (a.Speed - previousSpeeds[i]) / duration : 0;
                previousTrips[i] = a.Id; previousTimes[i] = sim.Statistics.Elapsed; previousSpeeds[i] = a.Speed; laneVehicles[a.Lane]++;
                samples[head] = new Sample { time = sim.Statistics.Elapsed, id = a.Id, lane = world.Roads.Lanes[a.Lane].Id,
                    speed = a.Speed, acceleration = a.Driver.FinalAcceleration, safety = a.Driver.SafetyAcceleration,
                    gap = a.Gap, desiredGap = a.Driver.DesiredGap, ttc = a.Driver.TimeToCollision, lod = (int)a.Lod,
                    decision = (int)a.LaneDecision.Reason, intersection = (int)a.IntersectionDecision,
                    measuredAcceleration = measured, sampleDuration = duration };
                head = (head + 1) % samples.Length; if (count < samples.Length) count++; else overwritten++;
            }
        }
        private void OnGUI()
        {
            if (!visible || world?.Simulation == null) return;
            var stats = world.Simulation.Statistics;
            GUILayout.BeginArea(new Rect(12, 160, 480, 360), GUI.skin.box);
            GUILayout.Label("TRAFFIC / " + world.Profile.profileId);
            GUILayout.Label(world.Profile.calibrationStatus);
            GUILayout.Label($"Trips {stats.Active}/{world.Capacity} | full physics {stats.Physical} | queued {stats.Queued} | disabled {stats.Disabled}");
            GUILayout.Label($"Mean speed {stats.MeanSpeed * 3.6f:F1} km/h | arrivals {stats.Spawned} | completed {stats.Completed}");
            GUILayout.Label($"Lane changes {stats.LaneChanges} | contacts {stats.Collisions} | LOD handovers {world.HandoverCount}");
            GUILayout.Label($"Physical boundary incursions {stats.PhysicalIncursions}");
            GUILayout.Label($"Deferred admission attempts {world.DeferredArrivals} | truncated spatial queries {stats.TruncatedQueries}");
            if (world.Demand != null) GUILayout.Label($"Abstract OD demand: pending {world.Demand.Pending}, admitted {world.Demand.Admitted}, capacity-suppressed {world.Demand.SuppressedAtCapacity}");
            GUILayout.Label($"Trace {count} rows / older rows overwritten {overwritten}. Speeds m/s, acceleration m/s², bumper gaps m.");
            if (GUILayout.Button("Export bounded CSV diagnostic window"))
            {
                try { exported = ExportCsv(); }
                catch (IOException error) { exported = error.Message; }
                catch (UnauthorizedAccessException error) { exported = error.Message; }
            }
            if (!string.IsNullOrEmpty(exported)) GUILayout.Label(exported);
            GUILayout.EndArea();
        }
        public string ExportCsv()
        {
            string directory = Path.Combine(Application.persistentDataPath, "TrafficDiagnostics"); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "traffic-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".csv");
            var content = new StringBuilder("time_s,trip_id,lane_id,speed_mps,command_accel_mps2,safety_accel_mps2,bumper_gap_m,desired_gap_m,ttc_s,lod,lane_reason,intersection_reason,measured_accel_mps2,sample_dt_s\n");
            for (int i = 0; i < count; i++)
            {
                Sample s = samples[(head - count + i + samples.Length) % samples.Length];
                content.AppendFormat(CultureInfo.InvariantCulture, "{0:R},{1},{2},{3:R},{4:R},{5:R},{6:R},{7:R},{8:R},{9},{10},{11},{12:R},{13:R}\n",
                    s.time, s.id, s.lane, s.speed, s.acceleration, s.safety, s.gap, s.desiredGap, s.ttc, s.lod, s.decision, s.intersection,
                    s.measuredAcceleration, s.sampleDuration);
            }
            File.WriteAllText(path, content.ToString()); return path;
        }
        private void OnDrawGizmosSelected()
        {
            if (world?.Simulation == null) return;
            // Scene-view occupancy heatmap. This is instantaneous sampled occupancy, not calibrated flow.
            if (laneVehicles != null) for (int lane = 0; lane < laneVehicles.Length; lane++)
            {
                RoadLane road = world.Roads.Lanes[lane];
                float occupied = Mathf.Clamp01(laneVehicles[lane] * 6f / Mathf.Max(1, road.Length));
                Gizmos.color = world.Roads.Lanes.IsClosed(lane) ? Color.magenta : Color.Lerp(Color.green, Color.red, occupied);
                Vector3 previous = road.Start + Vector3.up * 0.15f;
                for (float along = 2; along < road.Length + 2; along += 2)
                { Vector3 point = road.Sample(along, out _) + Vector3.up * 0.15f; Gizmos.DrawLine(previous, point); previous = point; }
            }
            for (int i = 0; i < world.Simulation.Capacity; i++)
            {
                TrafficAgentState a = world.Simulation[i]; if (!a.Active) continue;
                Gizmos.color = a.Disabled ? Color.red : a.Yielding ? Color.yellow : a.Lod == TrafficSimulationLod.Physical ? Color.cyan : Color.gray;
                Gizmos.DrawWireSphere(a.Position + Vector3.up, 1); Gizmos.DrawLine(a.Position + Vector3.up, a.Aim + Vector3.up);
                if (!float.IsInfinity(a.Gap)) Gizmos.DrawLine(a.Position + Vector3.up, a.Position + Vector3.up + a.Forward * a.Gap);
            }
        }
    }
}
