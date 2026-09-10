using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public sealed class RaceResult
    {
        public string EventId { get; }
        public string EventName { get; }
        public bool Won { get; }
        public float ElapsedSeconds { get; }
        public int CashAwarded { get; }
        public string Message { get; }
        public string SettlementId { get; }
        public RaceResult(string id, string name, bool won, float elapsed, int cash, string message, string settlementId = "")
        { EventId = id; EventName = name; Won = won; ElapsedSeconds = elapsed; CashAwarded = cash; Message = message; SettlementId = settlementId; }
    }

    public sealed partial class FreeRoamSession
    {
        public event Action<RaceResult> RaceCompleted;
        public bool ManagedFlow { get; private set; }
        public RaceResult LastRaceResult { get; private set; }
        public bool CanRestartRace => !Pursued && lastEvent != null && (State == FreeRoamState.Results
            || State == FreeRoamState.Paused && beforePause == FreeRoamState.Event);
        public GameFlowState FlowState => State == FreeRoamState.Paused ? GameFlowState.Paused
            : State == FreeRoamState.EventLoading ? GameFlowState.EventLoading
            : State == FreeRoamState.Event ? GameFlowState.RaceActive
            : State == FreeRoamState.Results ? GameFlowState.Results : GameFlowState.FreeRoam;

        public void UseApplicationFlow()
        {
            ResolvePorts(); ManagedFlow = true; resumeSavedProfile = false;
            profile?.ConfigureAutomaticPersistence(false, false, false);
        }

        public bool TryInitializeCareer(string alias, bool create, out string failure)
        {
            UseApplicationFlow();
            if (string.IsNullOrWhiteSpace(alias) || alias.Length > 64)
                return Reject("Use an alias of 1–64 letters, numbers, hyphens or underscores.", out failure);
            foreach (char character in alias)
                if (!char.IsLetterOrDigit(character) && character != '-' && character != '_')
                    return Reject("Alias names allow letters, numbers, hyphens and underscores.", out failure);
            if (profile == null || vehicle?.Body == null || roads == null || bounty == null || wallet == null)
                return Reject("World is missing player, career, roads, bounty or wallet bindings.", out failure);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (FreeRoamEventDefinition definition in events)
            {
                if (definition == null) return Reject("World has an unbound event.", out failure);
                if (!definition.TryValidate(out failure)) return false;
                if (!ids.Add(definition.Id)) return Reject("Duplicate event ID: " + definition.Id, out failure);
            }
            if (Array.Find(locations, location => location != null && location.Kind == WorldLocationKind.Safehouse) == null)
                return Reject("World needs a safehouse resume point.", out failure);
            profile.SetProfileId(alias);
            if (create)
            {
                if (!(profile.Storage is ICareerProfilePresence presence))
                    return Reject("Storage cannot safely check whether this alias exists.", out failure);
                if (!presence.TryExists(alias, out bool exists, out failure)) return false;
                if (exists) return Reject("This alias already exists. Resume it or choose a different name.", out failure);
                if (!profile.TryCreateNewProfile(out failure) || !profile.TrySaveNew(out failure)) return false;
            }
            else if (!profile.TryLoad(out failure)) return false;
            escapedBefore = bounty.PursuitsEscaped; bustedBefore = bounty.PursuitsBusted;
            if (EventProgress == null || ActiveEvent == null) State = FreeRoamState.Driving;
            Status = string.IsNullOrEmpty(profile.LastRecovery)
                ? "Career ready. Explore Rockport." : "Career recovered from an older valid save. Explore Rockport.";
            failure = string.Empty; return true;
        }

        public bool TryExecute(GameFlowCommand command, out string failure)
        {
            failure = string.Empty;
            switch (command)
            {
                case GameFlowCommand.Pause:
                    if (State == FreeRoamState.Paused || State == FreeRoamState.EventLoading || State == FreeRoamState.Results)
                        return Reject("This screen cannot be paused.", out failure);
                    TogglePause(); return true;
                case GameFlowCommand.Resume:
                    if (State != FreeRoamState.Paused) return Reject("The game is not paused.", out failure);
                    TogglePause(); return true;
                case GameFlowCommand.ActivateEvent:
                    if (State != FreeRoamState.EventLoading || ActiveEvent == null) return Reject("No event is being prepared.", out failure);
                    if (!ActiveEvent.TryValidate(out failure)) { ExitActivity(); return false; }
                    State = FreeRoamState.Event; return true;
                case GameFlowCommand.Restart: return TryRestartRace(out failure);
                case GameFlowCommand.Save: return TrySave(out failure);
                case GameFlowCommand.Continue:
                    if (State != FreeRoamState.Results && State != FreeRoamState.Location)
                        return Reject("There is no completed activity to leave.", out failure);
                    ExitActivity(); return true;
                default: return Reject("Unknown game-flow command.", out failure);
            }
        }

        public bool TrySaveForExit(out string failure)
        {
            if (Pursued) return Reject("Escape the police before leaving the career.", out failure);
            if (State == FreeRoamState.EventLoading) return Reject("Wait for event preparation to finish.", out failure);
            if (profile == null) return Reject("No career storage is connected.", out failure);
            // Mission snapshot and course resume pose are part of the career transaction.
            return profile.TrySave(out failure);
        }

        private bool TryRestartRace(out string failure)
        {
            if (!CanRestartRace) return Reject("Restart is available from race pause/results, outside a pursuit.", out failure);
            if (!lastEvent.TryValidate(out failure)) return false;
            if (!TryFindRaceStart(lastEvent, out Vector3 spawn, out Quaternion rotation)) return Reject("The race start is occupied. Resume or try again shortly.", out failure);
            if (State == FreeRoamState.Paused) TogglePause();
            EventProgress?.Fail(); ExitActivity(); MovePlayer(spawn, rotation);
            return TryStartEvent(lastEvent, out failure);
        }

        private bool TryFindRaceStart(FreeRoamEventDefinition definition, out Vector3 spawn, out Quaternion rotation)
        {
            rotation = definition.transform.rotation;
            var publication = definition.RoutePublication;
            if (publication != null)
            {
                for (int i = 0; i < publication.GridCount; i++)
                {
                    rotation = publication.GridRotation(i);
                    Vector3 up = rotation * Vector3.up;
                    Vector3 ground = publication.GridPosition(i);
                    bool clear = true;
                    foreach (Collider hit in Physics.OverlapBox(ground + up * (publication.EntrantDimensions.y / 2 + .15f), publication.EntrantDimensions / 2, rotation, ~0, QueryTriggerInteraction.Ignore))
                        if (!hit.transform.IsChildOf(transform)) { clear = false; break; }
                    if (clear) { spawn = ground + up * .8f; return true; }
                }
                spawn = default; return false;
            }
            // Bounded candidates near the actual start, never a remote road-node fallback.
            for (int i = 0; i < 9; i++)
            {
                int cell = (i + 4) % 9;
                int x = cell % 3 - 1;
                int z = cell / 3 - 1;
                spawn = definition.transform.position + definition.transform.right * (x * 3)
                    + definition.transform.forward * (z * 3) + Vector3.up * 0.8f;
                bool clear = true;
                foreach (Collider hit in Physics.OverlapBox(spawn + Vector3.up * 0.7f, new Vector3(1.2f, 0.6f, 2.5f),
                    definition.transform.rotation, ~0, QueryTriggerInteraction.Ignore))
                    if (!hit.transform.IsChildOf(transform)) { clear = false; break; }
                if (clear) return true;
            }
            spawn = default; return false;
        }
    }
}
