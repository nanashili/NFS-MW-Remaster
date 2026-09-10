using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NfsMwRemaster.Driving
{
    [DisallowMultipleComponent]
    public sealed partial class FreeRoamSession : MonoBehaviour, IFreeRoamSession, ICareerProfileParticipant, IGameFlowSession
    {
        [SerializeField] private VehicleController vehicle;
        [SerializeField] private RoadNetwork roads;
        [SerializeField] private VehiclePursuitDirector pursuit;
        [SerializeField] private WorldLocation[] locations = Array.Empty<WorldLocation>();
        [SerializeField] private FreeRoamEventDefinition[] events = Array.Empty<FreeRoamEventDefinition>();
        [SerializeField] private bool resumeSavedProfile = true;
        private CareerProfileSystem profile;
        private VehicleBountySystem bounty;
        private RockportWorldStreamer worldStreamer;
        private IVehicleWallet wallet;
        private IVehicleStoreSession store;
        private readonly List<Vector3> route = new List<Vector3>();
        private readonly List<string> discovered = new List<string>();
        private readonly List<string> completed = new List<string>();
        private string safehouseId = "safehouse";
        private FreeRoamState beforePause;
        private bool bodyHeld;
        private bool wasKinematic;
        private bool wasVehicleEnabled;
        private float previousTimeScale = 1;
        private float countdown;
        private bool missionEscaped, missionBusted;
        private double nextSettlementAttempt;
        private int escapedBefore;
        private int bustedBefore;
        private VehiclePursuitDirector subscribedPolice;
        private PoliceOutcome queuedPoliceOutcome;
        private string handledPoliceOutcome;
        private double nextPoliceWorldAttempt;
        private Vector3 previousEventPosition;
        private Vector3 destination;
        private bool hasDestination;
        private float routeTimer;
        private FreeRoamEventDefinition lastEvent;
        public FreeRoamState State { get; private set; }
        public bool CanDrive => !MapInputFocus.Captured && (!ManagedFlow || GameFlowRuntime.Instance?.AllowsWorldInput == true)
            && (queuedPoliceOutcome == null || queuedPoliceOutcome.kind != PoliceOutcomeKind.Arrested)
            && (pursuit == null || pursuit.EncounterState != PoliceEncounterState.OutcomePending)
            && (State == FreeRoamState.Driving || (State == FreeRoamState.Event && countdown <= 0));
        public string Status { get; private set; } = "Explore the district. Stop at a marker and press E.";
        public IWorldLocation ActiveLocation { get; private set; }
        public IReadOnlyList<Vector3> NavigationRoute => route;
        public float NavigationDistance
        {
            get
            {
                if (route.Count == 0) return 0;
                float distance = Vector3.Distance(transform.position, route[0]);
                for (int i = 1; i < route.Count; i++) distance += Vector3.Distance(route[i - 1], route[i]);
                return distance;
            }
        }
        public IReadOnlyList<WorldLocation> Locations => locations;
        public IReadOnlyList<FreeRoamEventDefinition> Events => events;
        public IReadOnlyList<string> CompletedEvents => completed;
        public FreeRoamEventDefinition ActiveEvent { get; private set; }
        public MissionCourse EventProgress { get; private set; }
        public float Countdown => countdown;
        public bool MapOpen { get; private set; }
        public IVehicleStoreSession Store => store;
        public int Cash => wallet?.Balance ?? 0;
        public string ProfileSectionId => "free-roam";
        public VehiclePursuitDirector Pursuit => pursuit;

        public void Configure(VehicleController player, RoadNetwork network, VehiclePursuitDirector director,
            WorldLocation[] worldLocations, FreeRoamEventDefinition[] worldEvents, bool resume = true)
        {
            vehicle = player; roads = network; pursuit = director; locations = worldLocations;
            events = worldEvents; resumeSavedProfile = resume; ResolvePorts();
            BindPolice();
        }

        private void ResolvePorts()
        {
            if (vehicle == null) vehicle = GetComponent<VehicleController>();
            profile = GetComponent<CareerProfileSystem>();
            worldStreamer = FindAnyObjectByType<RockportWorldStreamer>();
            profile?.ConfigureAutosave(() => !Pursued && (State == FreeRoamState.Driving || State == FreeRoamState.Location));
            bounty = GetComponent<VehicleBountySystem>();
            wallet = GetComponent<VehicleStoreWallet>();
            store = GetComponent<VehicleStoreSystem>();
        }

        private void Start()
        {
            ResolvePorts();
            BindPolice();
            if (GameFlowRuntime.Instance != null) UseApplicationFlow();
            if (resumeSavedProfile && profile != null)
            {
                if (profile.TryLoad(out string failure)) Status = "Profile loaded. Welcome back.";
                else Status = failure + " Use a safehouse to save this session.";
            }
            escapedBefore = bounty != null ? bounty.PursuitsEscaped : 0;
            bustedBefore = bounty != null ? bounty.PursuitsBusted : 0;
        }

        private bool Pursued => pursuit != null ? pursuit.IsActive : bounty != null && bounty.PursuitActive;
        private bool Stopped => vehicle != null && vehicle.Body != null && vehicle.Body.linearVelocity.magnitude < 3;
        private static bool Reject(string reason, out string failure) { failure = reason; return false; }

        public bool TryEnter(IWorldLocation location, out string failure)
        {
            if (location == null || (!Array.Exists(locations, candidate => ReferenceEquals(candidate, location)) && !IsPlacedLocation(location)))
                return Reject("Unknown location.", out failure);
            if (location is WorldLocation placedLocation && !CheckPlacedInteraction(placedLocation.GetComponent<WorldActivityInstance>(), out failure)) return false;
            if (State != FreeRoamState.Driving || Pursued) return Reject("Finish the activity or escape the police first.", out failure);
            if (!Stopped || Vector3.Distance(transform.position, location.Position) > location.Radius)
                return Reject("Stop inside the location marker to enter.", out failure);
            if (location.Storefront != null && (store == null || !store.OpenStore(location.Storefront, out failure)))
                return Reject("The storefront is unavailable.", out failure);
            ActiveLocation = location;
            State = FreeRoamState.Location;
            HoldVehicle(true);
            if (!discovered.Contains(location.Id)) discovered.Add(location.Id);
            Status = location.DisplayName;
            if (location.Kind == WorldLocationKind.Safehouse)
            {
                safehouseId = location.Id;
                TrySave(out _);
            }
            failure = string.Empty;
            return true;
        }

        public bool TryStartEvent(FreeRoamEventDefinition definition, out string failure)
        {
            if (definition == null || (Array.IndexOf(events, definition) < 0 && !ActivityRegistry.Owns(definition.GetComponent<WorldActivityInstance>()))) return Reject("Unknown event.", out failure);
            if (!CheckPlacedInteraction(definition.GetComponent<WorldActivityInstance>(), out failure)) return false;
            if (State != FreeRoamState.Driving || Pursued) return Reject("Return to free roam without a pursuit first.", out failure);
            if (!Stopped || Vector3.Distance(transform.position, definition.transform.position) > 15)
                return Reject("Stop at the event marker first.", out failure);
            if (!definition.TryValidate(out failure)) return false;
            if (definition.Kind == FreeRoamEventKind.Pursuit && (pursuit == null || bounty == null))
                return Reject("This challenge requires a pursuit director and bounty tracker.", out failure);
            if (!TryFindRaceStart(definition, out Vector3 gridPosition, out Quaternion gridRotation)) return Reject("The event start is occupied. Wait for traffic to clear.", out failure);
            ActiveEvent = lastEvent = definition;
            EventProgress?.Dispose();
            EventProgress = new MissionCourse(definition.Checkpoints, definition.Laps, definition.TimeLimit,
                definition.Kind == FreeRoamEventKind.Speedtrap ? definition.TargetSpeedKph : 0,
                definition.Id, definition.Reward, definition.Kind == FreeRoamEventKind.Pursuit, publishedRoute: definition.RoutePublication);
            countdown = 3;
            missionEscaped = missionBusted = false;
            State = ManagedFlow ? FreeRoamState.EventLoading : FreeRoamState.Event;
            LastRaceResult = null;
            MapOpen = false;
            HoldVehicle(true);
            MovePlayer(gridPosition, gridRotation);
            previousEventPosition = transform.position;
            Status = definition.DisplayName;
            NavigateTo(EventProgress.NextCheckpoint);
            failure = string.Empty;
            return true;
        }

        public bool TryPurchase(string productId, out string failure)
        {
            if (State != FreeRoamState.Location || ActiveLocation?.Storefront == null || Pursued || store == null)
                return Reject("Enter a shop before buying products.", out failure);
            if (!store.TryPurchaseById(productId, out failure)) { Status = failure; return false; }
            Status = "Purchased and installed / added to garage.";
            if (!TrySave(out string saveFailure)) Status = "Purchased, but save failed: " + saveFailure;
            failure = string.Empty;
            return true;
        }

        public bool TrySave(out string failure)
        {
            if (Pursued || State == FreeRoamState.EventLoading || State == FreeRoamState.Event || (State == FreeRoamState.Paused && beforePause == FreeRoamState.Event))
                return Reject("Saving is unavailable during an event or pursuit.", out failure);
            if (profile == null) return Reject("No profile service connected.", out failure);
            bool saved = profile.TrySave(out failure);
            Status = saved ? "Profile saved. Resume point: safehouse." : failure;
            return saved;
        }

        public bool TryLoad(out string failure)
        {
            if (State != FreeRoamState.Location || Pursued || ActiveLocation == null
                || (ActiveLocation.Kind != WorldLocationKind.Safehouse && ActiveLocation.Kind != WorldLocationKind.Garage))
                return Reject("Load at a safehouse or garage, outside a pursuit.", out failure);
            if (profile == null) return Reject("No profile service connected.", out failure);
            bool loaded = profile.TryLoad(out failure);
            if (loaded) { if (ActiveEvent == null) ExitActivity(); Status = string.IsNullOrEmpty(profile.LastRecovery) ? "Profile loaded." : "Profile recovered from an older valid save. See Save Inspector for details."; }
            else Status = failure;
            return loaded;
        }

        public bool TryRecover(out string failure)
        {
            if (State != FreeRoamState.Driving || Pursued) return Reject("Recovery is unavailable during an activity or pursuit.", out failure);
            if (roads == null || vehicle?.Body == null) return Reject("No road recovery connected.", out failure);
            if (!FindClearRecovery(roads.NearestRoadPoint(transform.position), out Vector3 point))
                return Reject("The nearest road is occupied. Try again when clear.", out failure);
            MovePlayer(point, Quaternion.identity);
            Status = "Vehicle recovered to the road.";
            failure = string.Empty;
            return true;
        }

        private bool FindClearRecovery(Vector3 origin, out Vector3 point)
        {
            point = origin + Vector3.up * 0.8f;
            // Prefer the nearest road, then graph nodes; never place the car inside traffic or a wall.
            for (int i = -1; i < roads.Nodes.Count; i++)
            {
                Vector3 candidate = (i < 0 ? origin : roads.Nodes[i].position) + Vector3.up * 0.8f;
                bool clear = true;
                foreach (Collider hit in Physics.OverlapBox(candidate + Vector3.up * 0.7f, new Vector3(1.2f, 0.6f, 2.5f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                    if (!hit.transform.IsChildOf(transform)) { clear = false; break; }
                if (!clear) continue;
                point = candidate; return true;
            }
            return false;
        }

        public void ExitActivity()
        {
            if (!TryExitPlacedService()) return;
            if (State == FreeRoamState.Paused) { TogglePause(); return; }
            if (State == FreeRoamState.Event || State == FreeRoamState.EventLoading) EventProgress?.Fail();
            ActiveLocation = null; ActiveEvent = null; countdown = 0;
            State = FreeRoamState.Driving; HoldVehicle(false);
            Status = Pursued ? "Escape the police. Shops and recovery are locked." : "Free roam";
        }

        public void TogglePause()
        {
            if (State == FreeRoamState.EventLoading) return;
            if (State == FreeRoamState.Paused) { State = beforePause; if (!ManagedFlow) Time.timeScale = previousTimeScale; return; }
            beforePause = State; previousTimeScale = Time.timeScale; State = FreeRoamState.Paused; if (!ManagedFlow) Time.timeScale = 0;
        }

        public void ToggleMap() { MapOpen = !MapOpen; }
        public void NavigateTo(Vector3 point)
        {
            destination = point; hasDestination = true; routeTimer = 0;
            if (roads != null && roads.TryRoute(transform.position, point, route)) route.Add(point);
        }
        public void ClearNavigation() { route.Clear(); hasDestination = false; }

        public bool RetryEvent(out string failure)
        {
            return TryRestartRace(out failure);
        }

        private void Update()
        {
            TickPlacementDwell();
            var keyboard = MapInputFocus.Captured ? null : Keyboard.current; var pad = MapInputFocus.Captured ? null : Gamepad.current;
            if (!ManagedFlow && (keyboard?.escapeKey.wasPressedThisFrame == true || pad?.startButton.wasPressedThisFrame == true))
            {
                if (State == FreeRoamState.Location || State == FreeRoamState.Results) ExitActivity(); else TogglePause();
            }
            if (State == FreeRoamState.Paused) return;
            if (ManagedFlow && GameFlowRuntime.Instance?.AllowsWorldInput != true) return;
            if (!MapInputFocus.HasPresentation && (keyboard?.mKey.wasPressedThisFrame == true || pad?.selectButton.wasPressedThisFrame == true)) ToggleMap();
            if (keyboard?.rKey.wasPressedThisFrame == true) { if (!TryRecover(out string reason)) Status = reason; }
            if (State == FreeRoamState.Driving && (keyboard?.eKey.wasPressedThisFrame == true || pad?.buttonWest.wasPressedThisFrame == true)) Interact();
            foreach (WorldLocation location in locations)
                if (Vector3.Distance(transform.position, location.Position) < 35 && !discovered.Contains(location.Id)) discovered.Add(location.Id);
            if (queuedPoliceOutcome != null) ApplyPoliceWorldOutcome();
            // Standalone historical bounty fixtures have no director. Live police use acknowledged outcomes above.
            if (pursuit == null && bounty != null)
            {
                if (bounty.PursuitsBusted > bustedBefore)
                {
                    bustedBefore = bounty.PursuitsBusted;
                    missionBusted = true;
                    if (State == FreeRoamState.Event) TickEvent();
                    HoldVehicle(false); State = FreeRoamState.Driving;
                    RestorePoliceGarage(); Status = "Arrested. Returned to garage.";
                    TrySave(out _);
                }
                if (bounty.PursuitsEscaped > escapedBefore)
                {
                    escapedBefore = bounty.PursuitsEscaped;
                    missionEscaped = true;
                    if (State == FreeRoamState.Driving) { Status = "Pursuit escaped."; TrySave(out _); }
                }
            }
            if (State == FreeRoamState.Event) TickEvent();
            bool belowWorld = transform.position.y < -10;
            bool outsideLegacyWorld = worldStreamer == null
                && (Mathf.Abs(transform.position.x) > 260 || Mathf.Abs(transform.position.z) > 260);
            bool worldReady = worldStreamer == null || worldStreamer.IsInitialLoadComplete;
            if (worldReady && (belowWorld || outsideLegacyWorld))
            {
                if (Pursued) Status = "Recovery is unavailable during a pursuit. No automatic out-of-bounds arrest.";
                else { ExitActivity(); TryRecover(out _); }
            }
            routeTimer -= Time.deltaTime;
            if (hasDestination && routeTimer <= 0) { NavigateTo(destination); routeTimer = 1; }
        }

        private void Interact()
        {
            if (TryInteractWithPlacement()) return;
            foreach (WorldLocation location in locations)
                if (Vector3.Distance(transform.position, location.Position) <= location.Radius)
                { if (!TryEnter(location, out string reason)) Status = reason; return; }
            foreach (FreeRoamEventDefinition definition in events)
                if (Vector3.Distance(transform.position, definition.transform.position) <= 15)
                { if (!TryStartEvent(definition, out string reason)) Status = reason; return; }
            Status = "Drive to a shop, safehouse or event marker on the map.";
        }

        private void TickEvent()
        {
            if (countdown > 0)
            {
                countdown -= Time.deltaTime;
                if (countdown > 0) return;
                countdown = 0;
                HoldVehicle(false); previousEventPosition = transform.position;
                if (ActiveEvent.Kind == FreeRoamEventKind.Pursuit && !pursuit.TryStartPursuit(out string failure))
                { FinishEvent(false, failure); return; }
            }
            int passed = EventProgress.CheckpointsPassed;
            EventProgress.Advance(Time.deltaTime, previousEventPosition, transform.position, vehicle.Body.linearVelocity.magnitude * 3.6f,
                Pursued, missionEscaped, missionBusted);
            missionEscaped = missionBusted = false;
            previousEventPosition = transform.position;
            if (EventProgress.CheckpointsPassed != passed) NavigateTo(EventProgress.NextCheckpoint);
            if (ActiveEvent.Kind == FreeRoamEventKind.Pursuit)
                Status = EventProgress.Runtime.ObjectiveStatus("survive") == ObjectiveState.Succeeded
                    ? "Lose the police and finish cooldown." : $"Survive pursuit: {Mathf.CeilToInt((float)(20 - EventProgress.Runtime.ObjectiveElapsed("survive")))}s";
            if (EventProgress.Outcome == MissionState.Failed || EventProgress.Outcome == MissionState.Aborted)
                FinishEvent(false, EventProgress.Runtime.Result?.failure ?? "Event aborted");
            else if (EventProgress.Outcome == MissionState.Succeeded)
            {
                if (Pursued) Status = "Course complete. Escape the police to claim the reward.";
                else FinishEvent(true, "Event complete");
            }
        }

        private void FinishEvent(bool won, string message)
        {
            if (State != FreeRoamState.Event || ActiveEvent == null) return;
            string eventId = ActiveEvent.Id, eventName = ActiveEvent.DisplayName;
            float elapsed = EventProgress.Elapsed;
            int paid = 0;
            if (won)
            {
                if (Time.unscaledTimeAsDouble < nextSettlementAttempt) return;
                string reason = "No profile connected.";
                if (profile == null || !profile.TrySettleMission(EventProgress.Runtime, out reason))
                { nextSettlementAttempt = Time.unscaledTimeAsDouble + 2; Status = "Mission succeeded; settlement pending: " + reason; return; }
                else { paid = ActiveEvent.Reward; message += $" — ${paid}"; if (!completed.Contains(ActiveEvent.Id)) completed.Add(ActiveEvent.Id); }
            }
            else EventProgress.Fail();
            ActiveEvent = null; countdown = 0; ClearNavigation();
            State = Pursued ? FreeRoamState.Driving : FreeRoamState.Results;
            HoldVehicle(State == FreeRoamState.Results);
            if (!Pursued && !TrySave(out string failure)) message += " Save failed: " + failure;
            Status = message;
            LastRaceResult = new RaceResult(eventId, eventName, won, elapsed, paid, message, EventProgress.Runtime.Capture().claimId);
            if (RaceCompleted != null)
                foreach (Action<RaceResult> observer in RaceCompleted.GetInvocationList())
                    try { observer(LastRaceResult); } catch (Exception exception) { Debug.LogWarning("Race result observer failed: " + exception.Message, this); }
        }

        private void HoldVehicle(bool held)
        {
            if (vehicle?.Body == null || bodyHeld == held) return;
            if (held)
            {
                wasKinematic = vehicle.Body.isKinematic; wasVehicleEnabled = vehicle.enabled;
                vehicle.Body.linearVelocity = Vector3.zero; vehicle.Body.angularVelocity = Vector3.zero;
                vehicle.enabled = false; vehicle.Body.isKinematic = true;
            }
            else { vehicle.Body.isKinematic = wasKinematic; vehicle.enabled = wasVehicleEnabled; }
            bodyHeld = held;
        }

        private void MovePlayer(Vector3 point, Quaternion rotation)
        {
            if (vehicle?.Body == null) return;
            worldStreamer?.PrepareForRelocation(point, rotation);
            if (!vehicle.Body.isKinematic) { vehicle.Body.linearVelocity = Vector3.zero; vehicle.Body.angularVelocity = Vector3.zero; }
            vehicle.Body.position = point; vehicle.Body.rotation = rotation;
            transform.SetPositionAndRotation(point, rotation);
            vehicle.NotifyPoseReset();
            Physics.SyncTransforms();
        }

        private void RestoreSafehouse()
        {
            WorldLocation safe = Array.Find(locations, item => item.Id == safehouseId && item.Kind == WorldLocationKind.Safehouse);
            if (safe == null) safe = Array.Find(locations, item => item.Kind == WorldLocationKind.Safehouse);
            if (safe != null && FindClearRecovery(safe.Position, out Vector3 point)) MovePlayer(point, safe.transform.rotation);
        }

        private bool RestorePoliceGarage()
        {
            WorldLocation garage = Array.Find(locations, item => item.Kind == WorldLocationKind.Garage);
            if (garage == null) garage = Array.Find(locations, item => item.Kind == WorldLocationKind.Safehouse);
            if (garage == null || !FindClearRecovery(garage.Position, out Vector3 point)) return false;
            MovePlayer(point, garage.transform.rotation); return true;
        }

        private void BindPolice()
        {
            if (subscribedPolice == pursuit) return;
            if (subscribedPolice != null) subscribedPolice.OutcomeAcknowledged -= QueuePoliceOutcome;
            subscribedPolice = pursuit;
            if (subscribedPolice != null) subscribedPolice.OutcomeAcknowledged += QueuePoliceOutcome;
        }
        private void OnEnable() => BindPolice();
        private void QueuePoliceOutcome(PoliceOutcome outcome) { queuedPoliceOutcome = outcome.Copy(); nextPoliceWorldAttempt = 0; }
        private void ApplyPoliceWorldOutcome()
        {
            if (Time.unscaledTimeAsDouble < nextPoliceWorldAttempt) return;
            nextPoliceWorldAttempt = Time.unscaledTimeAsDouble + 1;
            var result = queuedPoliceOutcome;
            if (result.kind == PoliceOutcomeKind.Arrested)
            {
                missionBusted = true;
                if (State == FreeRoamState.Event) { countdown = 0; TickEvent(); }
                HoldVehicle(false); State = FreeRoamState.Driving;
                if (!RestorePoliceGarage()) { Status = "Fine committed. Waiting for a clear authored garage recovery point."; return; }
                Status = "Arrested. Returned to garage.";
            }
            else if (result.kind == PoliceOutcomeKind.Escaped)
            {
                missionEscaped = true;
                if (State == FreeRoamState.Event) { countdown = 0; TickEvent(); }
                else Status = "Pursuit escaped. Fine cancelled; REP secured.";
            }
            else Status = "Fine paid. Free to leave.";
            handledPoliceOutcome = result.encounterId; queuedPoliceOutcome = null;
            // If this fails, the durable world directive remains and is safely replayed after loading.
            TrySave(out _);
        }

        public void Capture(CareerProfileData data)
        {
            if (data.police.pendingWorldOutcomeId == handledPoliceOutcome) data.police.pendingWorldOutcomeId = string.Empty;
            data.freeRoam = new CareerFreeRoamData { safehouseId = safehouseId,
                discoveredLocationIds = new List<string>(discovered), completedEventIds = new List<string>(completed) };
            if (EventProgress != null) MissionPersistence.Upsert(data.missions, EventProgress.Runtime.Capture());
            if (ActiveEvent != null && EventProgress != null)
                data.freeRoam.missionResume = new CareerMissionResumeData { active = true, eventId = ActiveEvent.Id,
                    claimId = EventProgress.Runtime.Capture().claimId, position = transform.position, rotation = transform.rotation, countdown = countdown };
        }

        public bool Restore(CareerProfileData data, out string failure)
        {
            if (data == null) return Reject("No profile supplied.", out failure);
            data.Normalize();
            PoliceOutcome worldOutcome = string.IsNullOrEmpty(data.police.pendingWorldOutcomeId) ? null
                : data.police.settlements.Find(item => item.outcome.encounterId == data.police.pendingWorldOutcomeId)?.outcome;
            MissionCourse restored = null;
            FreeRoamEventDefinition definition = null;
            var resume = data.freeRoam.missionResume;
            if (resume != null && !resume.active) resume = null;
            if (resume != null)
            {
                definition = Array.Find(events, item => item != null && item.Id == resume.eventId);
                var saved = data.missions.instances.Find(item => item.claimId == resume.claimId);
                if (definition == null || saved == null) return Reject("Saved mission/course is unavailable; no progress was reset.", out failure);
                if (definition.Kind == FreeRoamEventKind.Pursuit && saved.state == MissionState.Active
                    && (worldOutcome == null || worldOutcome.kind == PoliceOutcomeKind.PaidFine))
                    return Reject("Active pursuit reconstruction requires a police-world checkpoint adapter.", out failure);
                try { restored = new MissionCourse(definition.Checkpoints, definition.Laps, definition.TimeLimit,
                    definition.Kind == FreeRoamEventKind.Speedtrap ? definition.TargetSpeedKph : 0,
                    definition.Id, definition.Reward, definition.Kind == FreeRoamEventKind.Pursuit, saved, definition.RoutePublication); }
                catch (Exception exception) { return Reject(exception.Message, out failure); }
            }
            EventProgress?.Dispose(); EventProgress = restored; ActiveEvent = definition;
            safehouseId = data.freeRoam.safehouseId;
            discovered.Clear(); completed.Clear();
            discovered.AddRange(data.freeRoam.discoveredLocationIds);
            completed.AddRange(data.freeRoam.completedEventIds);
            if (resume != null)
            {
                lastEvent = definition; countdown = resume.countdown; State = FreeRoamState.Event;
                MovePlayer(resume.position, resume.rotation); previousEventPosition = resume.position;
                HoldVehicle(countdown > 0); NavigateTo(EventProgress.NextCheckpoint);
            }
            else { State = FreeRoamState.Driving; RestoreSafehouse(); }
            handledPoliceOutcome = null; queuedPoliceOutcome = worldOutcome?.Copy(); nextPoliceWorldAttempt = 0;
            if (queuedPoliceOutcome != null && resume != null) countdown = 0;
            failure = string.Empty; return true;
        }

        private void OnDisable()
        {
            if (subscribedPolice != null) subscribedPolice.OutcomeAcknowledged -= QueuePoliceOutcome;
            subscribedPolice = null;
            if (State == FreeRoamState.Paused) { State = beforePause; if (!ManagedFlow) Time.timeScale = previousTimeScale; }
            HoldVehicle(false);
            EventProgress?.Dispose(); EventProgress = null;
        }
    }
}
