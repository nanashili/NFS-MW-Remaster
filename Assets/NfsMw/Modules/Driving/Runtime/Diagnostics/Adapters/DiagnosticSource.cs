using System;
using System.Collections.Generic;
using NfsMwRemaster.Driving;
using UnityEngine;

namespace NfsMwRemaster.Diagnostics
{
    /// <summary>Explicit binding to one owner. No scene scans and no gameplay mutations.</summary>
    [DisallowMultipleComponent]
    public sealed class DiagnosticSource : MonoBehaviour, IDiagnosticProvider
    {
        [SerializeField] private string providerId = "";
        [SerializeField] private MonoBehaviour source;
        [SerializeField, Range(0.02f,5)] private float interval=0.1f;
        [SerializeField, Min(-1)] private int trafficSlot=-1;
        private IDisposable registration;
        private long generation, tick;
        private int epoch=-1;
        private bool demand, geometry;
        private DiagnosticSnapshot vehicleSnapshot;
        private VehicleController boundVehicle;
        private string runtimeId;
        private double nextVehicleSample;
        public MonoBehaviour Source => source;
        public string Id => runtimeId ?? providerId;
        public string Category => source is VehicleController ? "Vehicle" : source is TrafficWorldDirector ? "Traffic" :
            source is VehiclePoliceUnit || source is VehiclePursuitDirector ? "Police" : source is MissionHost ? "Mission" :
            source is SensoryAudioWorld ? "Audio" : source is SensoryEffectsWorld ? "VFX" : source is RacingLineInput ? "Racing AI" :
            source is VehicleBountySystem ? "Career" : source is VehicleStoreWallet ? "Wallet" : source is RoadNetwork ? "Roads" : source is RockportWorldStreamer ? "Streaming" : "Unavailable";
        public string Label => source != null ? source.GetType().Name + (trafficSlot>=0 ? " / slot " + trafficSlot : "") : "Missing owner";
        public double Interval => Mathf.Clamp(interval,0.02f,5);
        public void Configure(MonoBehaviour owner, string id, int slot=-1)
        {
            Release();source=owner;providerId=id;trafficSlot=slot;runtimeId=null;
            if(isActiveAndEnabled && Application.isPlaying)Register();
        }
        private void OnEnable(){if(Application.isPlaying && DiagnosticBuild.Enabled)Register();}
        private void Update(){if(DiagnosticBuild.Enabled && Application.isPlaying && epoch!=DiagnosticSession.Epoch)Register();}
        private void Register()
        {
            Release();if(!DiagnosticBuild.Enabled)return;
            // Authored ID identifies this binding. Runtime suffix separates simultaneous prefab instances.
            if(string.IsNullOrWhiteSpace(providerId))providerId=Guid.NewGuid().ToString("N");
            runtimeId=providerId+"@"+GetEntityId();generation++;epoch=DiagnosticSession.Epoch;
            registration=DiagnosticSession.Hub.Register(this);
        }
        public void SetDemand(bool active,bool drawGeometry)
        {
            active=active&&DiagnosticBuild.Enabled;
            geometry=drawGeometry;
            if(active==demand)return;demand=active;
            if(active && source is VehicleController v){nextVehicleSample=0;boundVehicle=v;v.PhysicsSampled+=OnVehicleSample;v.PoseReset+=OnPoseReset;}
            else Unbind();
        }
        private void OnPoseReset(){generation++;vehicleSnapshot=null;tick=0;}
        private void OnVehicleSample(float dt)
        {
            if(!demand || boundVehicle==null)return;
            tick++;
            if(Time.realtimeSinceStartupAsDouble<nextVehicleSample)return;
            nextVehicleSample=Time.realtimeSinceStartupAsDouble+Interval;
            var t=boundVehicle.Telemetry;
            var values=new List<DiagnosticMetric>{
                DiagnosticMetric.Number("speed",t.SpeedKph,"km/h"),DiagnosticMetric.Number("engine.rpm",t.EngineRpm,"rpm"),
                DiagnosticMetric.Number("gear",t.Gear),DiagnosticMetric.Number("slip.average",t.AverageSlip),
                DiagnosticMetric.Number("input.raw.steer",t.RawInput.Steering),DiagnosticMetric.Number("input.shaped.steer",t.ShapedInput.Steering),
                DiagnosticMetric.Number("input.final.steer",t.FinalInput.Steering),DiagnosticMetric.Number("input.raw.brake",t.RawInput.Brake),
                DiagnosticMetric.Number("input.final.brake",t.FinalInput.Brake),DiagnosticMetric.Number("input.final.throttle",t.FinalInput.Throttle),
                DiagnosticMetric.Number("assist.yaw",t.AssistYawTorque,"N m"),DiagnosticMetric.Number("wheels.grounded",t.GroundedWheels),
                DiagnosticMetric.State("handling.mode",t.HandlingMode.ToString()),DiagnosticMetric.Missing("configuration.fingerprint"),
                DiagnosticMetric.Missing("collision.evidence")};
            var wheels=boundVehicle.Wheels;
            if(wheels!=null)for(int i=0;i<Math.Min(6,wheels.Length);i++)
            {
                var w=wheels[i];if(w==null)continue;string prefix="wheel."+i+".";
                values.Add(DiagnosticMetric.Number(prefix+"load",w.NormalLoad,"N"));
                values.Add(DiagnosticMetric.Number(prefix+"slip.longitudinal",w.LongitudinalSlip));
                values.Add(DiagnosticMetric.Number(prefix+"slip.lateral",w.LateralSlipRadians,"rad"));
                values.Add(DiagnosticMetric.Number(prefix+"suspension",w.SuspensionCompression,"ratio"));
            }
            vehicleSnapshot=new DiagnosticSnapshot(Id,"vehicle-"+boundVehicle.GetEntityId(),Math.Max(1,generation),VehicleController.SimulationRevision,
                DiagnosticClock.Now(SamplePhase.OwnerBoundary,tick),values.ToArray());
        }
        public DiagnosticSnapshot Sample(DiagnosticClock clock)
        {
            if(!DiagnosticBuild.Enabled || source==null || !source.isActiveAndEnabled)return null;
            if(source is VehicleController)return vehicleSnapshot;
            var metrics=new List<DiagnosticMetric>();var lines=new List<DiagnosticLine>();
            string entity="owner-"+source.GetEntityId();long life=Math.Max(1,generation);
            string revision="unavailable";
            if(source is TrafficWorldDirector traffic && traffic.Simulation!=null)
            {
                if(traffic.Roads!=null && traffic.Roads.Publication!=null)revision=traffic.Roads.Publication.Fingerprint;
                var sim=traffic.Simulation;var stats=sim.Statistics;
                metrics.Add(DiagnosticMetric.Number("owner.simulation.time",stats.Elapsed,"s"));
                if(trafficSlot>=0)
                {
                    if(trafficSlot>=sim.Capacity || !sim[trafficSlot].Active)return null;
                    var a=sim[trafficSlot];entity="traffic-trip-"+a.Id;life=a.Id;
                    metrics.Add(DiagnosticMetric.Number("road.lane.id",traffic.Roads.Lanes[a.Lane].Id));
                    metrics.Add(DiagnosticMetric.Number("speed.actual",a.Speed,"m/s"));metrics.Add(DiagnosticMetric.Number("speed.target",a.TargetSpeed,"m/s"));
                    metrics.Add(DiagnosticMetric.Number("leader.id",a.Leader));metrics.Add(DiagnosticMetric.Number("headway",a.Gap,"m"));
                    metrics.Add(DiagnosticMetric.Number("acceleration.desired",a.Driver.DesiredAcceleration,"m/s²"));
                    metrics.Add(DiagnosticMetric.Number("acceleration.safety",a.Driver.SafetyAcceleration,"m/s²"));
                    metrics.Add(DiagnosticMetric.Number("acceleration.final",a.Driver.FinalAcceleration,"m/s²"));
                    metrics.Add(DiagnosticMetric.State("lane.change.reason",a.LaneDecision.Reason.ToString()));
                    metrics.Add(DiagnosticMetric.State("intersection",a.IntersectionDecision.ToString()));
                    metrics.Add(DiagnosticMetric.State("lod",a.Lod.ToString()));metrics.Add(DiagnosticMetric.State("panic",a.Panic.ToString()));
                    if(geometry)lines.Add(new DiagnosticLine{from=a.Position,to=a.Aim,color=Color.cyan});
                }
                else
                {
                    metrics.Add(DiagnosticMetric.Number("trips.active",stats.Active));metrics.Add(DiagnosticMetric.Number("speed.mean",stats.MeanSpeed,"m/s"));
                    metrics.Add(DiagnosticMetric.Number("collisions",stats.Collisions,"count",MetricKind.Counter));
                    metrics.Add(DiagnosticMetric.Number("queries.truncated",stats.TruncatedQueries,"count",MetricKind.Counter));
                    metrics.Add(DiagnosticMetric.Number("admission.deferred",traffic.DeferredArrivals,"count",MetricKind.Counter));
                }
            }
            else if(source is VehiclePoliceUnit unit)
            {
                metrics.Add(DiagnosticMetric.State("unit.role",unit.Role.ToString()));metrics.Add(DiagnosticMetric.State("unit.state",unit.State.ToString()));
                metrics.Add(DiagnosticMetric.State("tactic",unit.Tactic.ToString()));metrics.Add(DiagnosticMetric.Number("control.brake",unit.Current.Brake));
                metrics.Add(DiagnosticMetric.Number("control.throttle",unit.Current.Throttle));metrics.Add(DiagnosticMetric.Number("integrity",unit.Integrity));
                metrics.Add(DiagnosticMetric.Missing("sensing.evidence"));metrics.Add(DiagnosticMetric.Missing("planning.cost"));
            }
            else if(source is VehiclePursuitDirector police)
            {
                metrics.Add(DiagnosticMetric.State("encounter.state",police.EncounterState.ToString()));metrics.Add(DiagnosticMetric.Number("heat",police.HeatLevel));
                metrics.Add(DiagnosticMetric.Number("fine.pending",police.CurrentFine));metrics.Add(DiagnosticMetric.Number("units.active",police.ActiveUnitCount));
                metrics.Add(DiagnosticMetric.Number("target.unseen.age",police.TimeSinceTargetSeen,"s"));
                metrics.Add(police.IsActive?DiagnosticMetric.Number("knowledge.lastKnown.x",police.LastKnownPosition.x,"m"):DiagnosticMetric.Missing("knowledge.lastKnown.x",MetricValidity.Unknown));
                metrics.Add(police.IsActive?DiagnosticMetric.Number("knowledge.lastKnown.z",police.LastKnownPosition.z,"m"):DiagnosticMetric.Missing("knowledge.lastKnown.z",MetricValidity.Unknown));
                metrics.Add(DiagnosticMetric.State("settlement",police.PendingOutcome==null ? "No pending outcome" : "Pending owner acknowledgement"));
                if(geometry && police.IsActive)lines.Add(new DiagnosticLine{from=police.LastKnownPosition,to=police.LastKnownPosition+Vector3.up*5,color=Color.yellow});
            }
            else if(source is MissionHost mission)
            {
                var r=mission.Runtime;
                metrics.Add(DiagnosticMetric.State("mission.state",r==null ? "No active mission" : r.State.ToString()));
                if(r!=null){metrics.Add(DiagnosticMetric.Number("mission.elapsed",r.Elapsed,"s"));metrics.Add(DiagnosticMetric.Number("events.delivered",r.EventsDelivered,"count",MetricKind.Counter));
                    metrics.Add(DiagnosticMetric.Number("subscriptions",r.SubscriptionCount));}
                metrics.Add(DiagnosticMetric.Missing("settlement.receipt"));
            }
            else if(source is SensoryAudioWorld audio)
            {
                metrics.Add(DiagnosticMetric.Number("voices.active",audio.ActiveVoices));metrics.Add(DiagnosticMetric.Number("voices.limit",audio.VoiceLimit));
                metrics.Add(DiagnosticMetric.State("mixer.state",audio.MixState.ToString()));metrics.Add(DiagnosticMetric.Missing("scheduling.latency"));
            }
            else if(source is SensoryEffectsWorld effects)
            {metrics.Add(DiagnosticMetric.Number("particles.live",effects.LiveParticles));metrics.Add(DiagnosticMetric.Number("particles.limit",effects.ParticleLimit));}
            else if(source is RacingLineInput racer)
            {
                if(racer.source!=null && racer.source.published!=null)revision=racer.source.published.Fingerprint;
                metrics.Add(string.IsNullOrEmpty(racer.Failure)?DiagnosticMetric.Number("route.station",racer.Station,"m"):DiagnosticMetric.Missing("route.station",MetricValidity.Unknown));
                metrics.Add(DiagnosticMetric.Number("pacing.authored.multiplier",racer.pacing));
                metrics.Add(DiagnosticMetric.Number("control.brake",racer.Current.Brake));
                metrics.Add(DiagnosticMetric.Number("control.throttle",racer.Current.Throttle));
                metrics.Add(DiagnosticMetric.State("route.finished",racer.Finished.ToString()));
                metrics.Add(DiagnosticMetric.State("tracker.health",string.IsNullOrEmpty(racer.Failure)?"Ready":"Owner reported failure"));
                metrics.Add(DiagnosticMetric.Missing("candidate.rejection.costs"));
                metrics.Add(DiagnosticMetric.Missing("speed.natural.target"));
            }
            else if(source is VehicleBountySystem bounty)
            {metrics.Add(DiagnosticMetric.Number("bounty.secured",bounty.TotalBounty));metrics.Add(DiagnosticMetric.Number("bounty.pending",bounty.CurrentPursuitBounty));metrics.Add(DiagnosticMetric.Number("heat",bounty.HeatLevel));}
            else if(source is VehicleStoreWallet wallet)
            {metrics.Add(DiagnosticMetric.Number("wallet.balance",wallet.Balance));metrics.Add(DiagnosticMetric.Missing("settlement.receipt"));}
            else if(source is RoadNetwork roads)
            {if(roads.Publication!=null)revision=roads.Publication.Fingerprint;
                metrics.Add(DiagnosticMetric.State("topology.source",roads.UsesBakedData?"Authoritative publication":"Legacy road adapter"));
                if(roads.Publication!=null)metrics.Add(DiagnosticMetric.State("publication.revision",roads.Publication.Fingerprint));
                else metrics.Add(DiagnosticMetric.Missing("publication.revision",MetricValidity.Unknown));}
            else if(source is RockportWorldStreamer streamer)
            {metrics.Add(DiagnosticMetric.Number("chunks.loaded",streamer.LoadedChunkCount));metrics.Add(DiagnosticMetric.Number("chunks.defined",streamer.ChunkCount));
                metrics.Add(DiagnosticMetric.Number("chunks.unavailable",streamer.UnavailableChunkCount));
                metrics.Add(DiagnosticMetric.Number("chunks.resident.limit",streamer.MaximumLoadedChunks));
                metrics.Add(DiagnosticMetric.Number("textures.loading",Texture.streamingTextureLoadingCount));
                metrics.Add(DiagnosticMetric.Number("textures.memory.current_mb",Texture.currentTextureMemory/1048576d));
                metrics.Add(DiagnosticMetric.Number("textures.memory.desired_mb",Texture.desiredTextureMemory/1048576d));
                metrics.Add(DiagnosticMetric.Number("textures.memory.target_mb",Texture.targetTextureMemory/1048576d));
                metrics.Add(DiagnosticMetric.State("streaming.operation",streamer.IsCleaningUnusedAssets?"Reclaiming unused assets":streamer.IsStreaming?"Scene operation pending":"Idle"));}
            else metrics.Add(DiagnosticMetric.Missing("owner.adapter"));
            return new DiagnosticSnapshot(Id,entity,life,revision,clock,metrics.ToArray(),lines.ToArray());
        }
        private void Unbind(){if(boundVehicle!=null){boundVehicle.PhysicsSampled-=OnVehicleSample;boundVehicle.PoseReset-=OnPoseReset;}boundVehicle=null;vehicleSnapshot=null;}
        private void Release(){registration?.Dispose();registration=null;Unbind();demand=false;}
        private void OnDisable(){Release();}
        private void OnDestroy(){Release();}
    }
}
