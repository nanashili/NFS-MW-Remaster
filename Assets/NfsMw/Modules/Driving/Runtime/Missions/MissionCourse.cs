using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    // Geometry reports facts. All sequencing, deadlines and outcomes belong to MissionRuntime.
    public sealed class MissionCourse : IDisposable
    {
        private readonly Vector3[] route;
        private readonly int laps;
        private readonly float limit;
        private readonly string[] checkpointIds;
        private readonly List<MissionEvent> batch = new List<MissionEvent>();
        private long step;
        private readonly RaceRouteTraversal traversal;
        public MissionRuntime Runtime { get; }
        public bool IsPursuit { get; }
        public MissionState Outcome => Runtime.State;
        public int CheckpointsPassed => IsPursuit ? 0 : (int)Runtime.Progress("course");
        public int TotalCheckpoints => route.Length * laps;
        public float LegalProgress => CheckpointsPassed + (traversal == null || CheckpointsPassed >= TotalCheckpoints ? 0 : traversal.NormalizedLegProgress(CheckpointsPassed));
        public int Lap => Mathf.Min(laps, CheckpointsPassed / route.Length + 1);
        public float Elapsed => (float)Runtime.Elapsed;
        public float Remaining => Mathf.Max(0, limit - Elapsed);
        public Vector3 NextCheckpoint => route[Mathf.Min(CheckpointsPassed, TotalCheckpoints - 1) % route.Length];

        public MissionCourse(Vector3[] route, int laps, float timeLimit, float minimumSpeed = 0,
            string id = "mission.course", int cash = 0, bool pursuit = false, MissionSnapshot restore = null, RaceRoutePublication publishedRoute = null)
        {
            if (route == null || route.Length == 0 || laps < 1 || (long)route.Length * laps > 100000 || !float.IsFinite(timeLimit) || timeLimit <= 0 || !float.IsFinite(minimumSpeed) || minimumSpeed < 0)
                throw new ArgumentException("Invalid mission course.");
            this.route = (Vector3[])route.Clone(); this.laps = laps; limit = timeLimit; IsPursuit = pursuit;
            if(publishedRoute!=null&&(pursuit||publishedRoute.LegCount!=route.Length||publishedRoute.Schema!=1))throw new ArgumentException("Published route does not match mission course.");
            checkpointIds = new string[route.Length * laps];
            for (int i = 0; i < checkpointIds.Length; i++)
            {
                var point = route[i % route.Length];
                if (!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)) throw new ArgumentException("Non-finite checkpoint.");
                string stablePoint = point.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":"
                    + point.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + point.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                using var hash = System.Security.Cryptography.SHA256.Create();
                checkpointIds[i] = "checkpoint." + BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(stablePoint))).Replace("-", "") + ".lap." + (i / route.Length + 1);
                if(publishedRoute!=null)checkpointIds[i]="route."+publishedRoute.RouteId+"."+publishedRoute.ReadLeg(i%route.Length).id+".lap."+(i/route.Length+1);
            }
            var definition = Build(id, route.Length, laps, timeLimit, minimumSpeed, cash, pursuit, checkpointIds);
            // Bind saved attempts to the exact immutable geometry revision.
            if (publishedRoute != null) definition.title += ":" + publishedRoute.Fingerprint;
            Runtime = new MissionRuntime(new MissionGraph(definition), restore: restore);
            step = restore?.step ?? 0;
            if(publishedRoute!=null)traversal=new RaceRouteTraversal(publishedRoute,(int)Runtime.Fact("route.path")-1,(int)Runtime.Fact("route.gate"));
        }
        public static MissionDefinition Build(string id, int points, int laps, float limit, float speed, int cash, bool pursuit, string[] checkpointIds = null)
        {
            var nodes = new List<MissionObjective>();
            nodes.Add(new MissionObjective { id = "deadline", kind = "deadline", duration = limit });
            MissionCondition success;
            if (pursuit)
            {
                nodes.Add(new MissionObjective { id = "survive", kind = "hold", duration = 20, success = MissionCondition.Fact("pursuit.active"), resetWhenFalse = true,
                    failure = MissionCondition.Fact("pursuit.escaped"), failureReason = "EscapedTooEarly" });
                nodes.Add(new MissionObjective { id = "escape", kind = "event", eventType = "pursuit.escaped", dependencies = new[] { "survive" } });
                success = MissionCondition.Done("escape");
            }
            else
            {
                var sequence = new string[points * laps];
                for (int i = 0; i < sequence.Length; i++) sequence[i] = checkpointIds == null ? "checkpoint." + i : checkpointIds[i];
                nodes.Add(new MissionObjective { id = "course", kind = "sequence", eventType = "checkpoint.reached", sequence = sequence });
                nodes.Add(new MissionObjective { id = "speed", kind = "condition", optional = true,
                    activate = MissionCondition.Done("course"), success = MissionCondition.Fact("vehicle.speed", speed) });
                success = MissionCondition.All(MissionCondition.Done("course"), MissionCondition.Fact("vehicle.speed", speed), MissionCondition.Not(MissionCondition.Fact("pursuit.active")));
            }
            return new MissionDefinition { id = id, title = id, objectives = nodes.ToArray(), success = success,
                failure = pursuit ? MissionCondition.Fact("pursuit.busted") : MissionCondition.Any(MissionCondition.Fact("pursuit.busted"),
                    MissionCondition.All(MissionCondition.Done("course"), MissionCondition.Not(MissionCondition.Fact("vehicle.speed", speed)))),
                failureReason = "BustedOrSpeedTargetMissed", rewards = new[] { new MissionReward { id = "completion", cash = cash } } };
        }
        public void Advance(float delta, Vector3 previous, Vector3 current, float speed, bool pursued = false, bool escaped = false, bool busted = false)
        {
            if (Outcome != MissionState.Active || delta <= 0 || !float.IsFinite(delta)) return;
            batch.Clear(); long currentStep = ++step;
            batch.Add(new MissionEvent(currentStep + ".speed", "vehicle.speed", value: speed, fact: "vehicle.speed"));
            batch.Add(new MissionEvent(currentStep + ".active", "pursuit.state", value: pursued ? 1 : 0, fact: "pursuit.active"));
            if (escaped) batch.Add(new MissionEvent(currentStep + ".escape", "pursuit.escaped", fact: "pursuit.escaped"));
            if (busted) batch.Add(new MissionEvent(currentStep + ".bust", "pursuit.busted", fact: "pursuit.busted"));
            if (!IsPursuit && CheckpointsPassed < TotalCheckpoints)
            {
                if(traversal!=null)
                {
                    int completed=CheckpointsPassed;
                    while(completed<TotalCheckpoints && traversal.Advance(completed,previous,current,out float fraction))
                    {
                        batch.Add(new MissionEvent(currentStep+".route."+completed.ToString("D6"),"checkpoint.reached",checkpointIds[completed++]));
                        previous=Vector3.Lerp(previous,current,Mathf.Min(1,fraction+.000001f));
                    }
                    batch.Add(new MissionEvent(currentStep+".route.path","route.state",value:traversal.Path+1,fact:"route.path"));
                    batch.Add(new MissionEvent(currentStep+".route.gate","route.state",value:traversal.Gate,fact:"route.gate"));
                }
                else
                {
                previous.y = current.y = 0; Vector3 point = NextCheckpoint; point.y = 0;
                Vector3 segment = current - previous;
                float t = segment.sqrMagnitude > 0 ? Mathf.Clamp01(Vector3.Dot(point - previous, segment) / segment.sqrMagnitude) : 0;
                if (Vector3.Distance(previous + segment * t, point) <= 9)
                    batch.Add(new MissionEvent(currentStep + ".checkpoint", "checkpoint.reached", checkpointIds[CheckpointsPassed]));
                }
            }
            Runtime.Step(currentStep, delta, delta, batch);
            // Course completion/speed facts are evaluated in the same transaction; no wallet effects here.
        }
        public void Fail() => Runtime.Abort();
        public void Dispose() => Runtime.Dispose();
    }
}
