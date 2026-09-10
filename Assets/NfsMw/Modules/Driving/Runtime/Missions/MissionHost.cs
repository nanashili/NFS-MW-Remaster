using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [DisallowMultipleComponent, DefaultExecutionOrder(1000)]
    public sealed class MissionHost : MonoBehaviour, ICareerProfileParticipant
    {
        [SerializeField] private MissionDefinitionAsset[] catalog = Array.Empty<MissionDefinitionAsset>();
        [SerializeField] private MissionWorldActions worldActions;
        [SerializeField] private CareerProfileSystem profile;
        private readonly List<MissionEvent> pending = new List<MissionEvent>();
        private long step, eventId;
        private bool settled;
        private ICareerFacts careerFacts;
        public void SetCareerFacts(ICareerFacts facts)
        { if (Runtime != null) throw new InvalidOperationException("Set career facts before starting a mission."); careerFacts = facts; }
        private string eventEpoch = Guid.NewGuid().ToString("N");
        public MissionRuntime Runtime { get; private set; }
        public event Action RuntimeStarted;
        public string LastFailure { get; private set; } = "";
        public string ProfileSectionId => "missions";
        public void Configure(MissionDefinitionAsset[] definitions, CareerProfileSystem career, MissionWorldActions actions = null)
        { if (Runtime != null) throw new InvalidOperationException("Stop mission before reconfiguring."); catalog = definitions; profile = career; worldActions = actions; }
        public void StartMission(string id)
        {
            if (Runtime != null) throw new InvalidOperationException("Release the foreground mission before starting another.");
            Runtime = new MissionRuntime(Find(id), careerFacts, worldActions); step = 0; settled = false;
            RuntimeStarted?.Invoke();
            profile?.MarkPersistentStateChanged();
        }
        private MissionGraph Find(string id)
        {
            MissionGraph match = null;
            foreach (var asset in catalog)
            {
                if (asset == null) throw new ArgumentException("Unbound mission asset.");
                var graph = asset.Compile(); if (graph.Id != id) continue;
                if (match != null) throw new ArgumentException("Duplicate mission ID: " + id); match = graph;
            }
            return match ?? throw new ArgumentException("Missing mission definition: " + id);
        }
        public void Publish(string type, string target = "", double value = 1, string fact = "", string identity = null)
        {
            if (Runtime == null || Runtime.State != MissionState.Active) return;
            if (pending.Count >= 8192) throw new InvalidOperationException("Mission event queue full.");
            pending.Add(new MissionEvent(identity ?? eventEpoch + "." + (++eventId), type, target, value, fact));
        }
        private void LateUpdate()
        {
            if (Runtime == null) return;
            try
            {
                if (Runtime.State == MissionState.Active || Runtime.State == MissionState.Suspended)
                    Runtime.Step(++step, Time.deltaTime, Time.unscaledDeltaTime, pending);
                pending.Clear();
                if (!settled && Runtime.State == MissionState.Succeeded && profile != null && !profile.SaveInProgress)
                {
                    // A failed commit remains a frozen unpaid result; explicit retry avoids disk hammering.
                    if (LastFailure.Length == 0) { settled = profile.TrySettleMission(Runtime, out var failure); LastFailure = failure; }
                }
            }
            catch (Exception exception) { LastFailure = exception.Message; pending.Clear(); }
        }
        public bool RetrySettlement(out string failure)
        {
            if (profile == null) { failure = "No profile connected."; return false; }
            bool result = profile.TrySettleMission(Runtime, out failure); settled = result; LastFailure = failure; return result;
        }
        public void Release()
        {
            Runtime?.Abort(); Runtime?.Dispose(); Runtime = null; pending.Clear(); LastFailure = "";
        }
        public void Capture(CareerProfileData data) { if (Runtime != null) MissionPersistence.Upsert(data.missions, Runtime.Capture()); }
        public bool Restore(CareerProfileData data, out string failure)
        {
            try
            {
                MissionPersistence.Validate(data.missions);
                MissionSnapshot active = null;
                foreach (var saved in data.missions.instances)
                {
                    // The nested course attempt is restored by FreeRoamSession, not this foreground host.
                    if (data.freeRoam.missionResume?.active == true && data.freeRoam.missionResume.claimId == saved.claimId) continue;
                    if (saved.state == MissionState.Active || saved.state == MissionState.Suspended
                        || saved.state == MissionState.Succeeded && !data.missions.claims.Exists(x => x.claimId == saved.claimId))
                    { if (active != null) throw new ArgumentException("Multiple foreground missions in save."); active = saved; }
                }
                // Validate without performing actions before disposing the old runtime.
                MissionGraph graph = active == null ? null : Find(active.missionId);
                if (active != null) MissionRuntime.ValidateSnapshot(active, graph);
                Release();
                if (active != null) { Runtime = new MissionRuntime(graph, careerFacts, worldActions, active); step = active.step; settled = false; RuntimeStarted?.Invoke(); }
                failure = ""; return true;
            }
            catch (Exception exception) { failure = exception.Message; return false; }
        }
        private void OnDisable() { Runtime?.Dispose(); Runtime = null; pending.Clear(); }
    }
}
