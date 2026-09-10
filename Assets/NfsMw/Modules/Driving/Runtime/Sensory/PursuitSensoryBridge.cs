using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Read-only pursuit presentation. A result cue is admitted only after durable acknowledgement.</summary>
    public sealed class PursuitSensoryBridge : MonoBehaviour
    {
        [SerializeField] private VehiclePursuitDirector director;
        [SerializeField] private FreeRoamSession session;
        [SerializeField] private SensoryAudioWorld world;
        [SerializeField] private PoliceSensoryProfile profile;
        [SerializeField] private VehicleAudio vehicleAudio;
        [SerializeField] private VehiclePoliceUnit[] units = System.Array.Empty<VehiclePoliceUnit>();
        [SerializeField] private PoliceRoadHazard[] hazards = System.Array.Empty<PoliceRoadHazard>();
        private readonly PoliceRadioQueue queue = new PoliceRadioQueue();
        private FeedbackVoiceLease voice;
        private PoliceRadioCue playing;
        private int encounter, lastUnits, lastDisabled, lastRoadblocks, lastSpikes, variant;
        private string encounterId;
        private PoliceEncounterState lastState;
        private bool initialized, searching;
        private uint valid, outcomeMask;
        private double endsAt, nextPoll;
        private int lastImpactSequence;
        public event System.Action<PoliceOutcomeKind> OutcomeCommitted;
        public string Subtitle { get; private set; } = "";
        public int Queued => queue.Count;
        public PoliceSensoryProfile Profile => profile;
        public float Threat { get; private set; }
        public void SetVehicleAudio(VehicleAudio source) { vehicleAudio = source; }
        public void Configure(VehiclePursuitDirector pursuit, FreeRoamSession flow, SensoryAudioWorld audioWorld,
            PoliceSensoryProfile content, VehiclePoliceUnit[] police, PoliceRoadHazard[] sites)
        {
            if (director != null) director.OutcomeAcknowledged -= Outcome;
            director = pursuit; session = flow; world = audioWorld; profile = content; units = police; hazards = sites;
            if (isActiveAndEnabled && director != null) director.OutcomeAcknowledged += Outcome;
        }
        private void OnEnable() { if (director != null) director.OutcomeAcknowledged += Outcome; }
        private void Outcome(PoliceOutcome outcome)
        {
            var kind = outcome.kind == PoliceOutcomeKind.Escaped ? RadioCueKind.Escaped
                : outcome.kind == PoliceOutcomeKind.Arrested ? RadioCueKind.Arrested : RadioCueKind.PaidFine;
            outcomeMask = Bit(kind); Queue(kind); OutcomeCommitted?.Invoke(outcome.kind);
        }
        private static uint Bit(RadioCueKind kind) => 1u << (int)kind;
        private void Queue(RadioCueKind kind)
        {
            var cue = profile != null ? profile.Cue(kind) : null;
            if (cue != null) queue.Enqueue(new RadioRequest(kind, cue.priority, encounter, Time.timeAsDouble + cue.expiry), Time.timeAsDouble);
        }
        private void Update()
        {
            if (world == null) return;
            var flow = GameFlowRuntime.Instance != null ? GameFlowRuntime.Instance.Flow?.State : session != null ? session.FlowState : GameFlowState.FreeRoam;
            world.SetMix(Time.timeScale <= 0 ? SensoryMixState.Paused
                : RecentHeavyImpact(0.65) ? SensoryMixState.Crash
                : director != null && director.EncounterState == PoliceEncounterState.Pursuit ? SensoryMixState.Pursuit
                : director != null && director.EncounterState == PoliceEncounterState.Cooldown ? SensoryMixState.Cooldown
                : flow == GameFlowState.RaceActive ? SensoryMixState.Race : SensoryMixState.FreeRoam);
            if (Time.timeScale <= 0 || director == null || profile == null) return;
            if (Time.timeAsDouble >= nextPoll || director.EncounterState != lastState) { nextPoll = Time.timeAsDouble + 0.1; Poll(); }
            double now = Time.timeAsDouble;
            if (playing != null && now >= endsAt) { playing = null; Subtitle = ""; world.Release(voice); }
            if (playing != null && ((valid & Bit(playing.kind)) == 0 || !Eligible(playing)))
            { world.Release(voice); playing = null; Subtitle = ""; }
            int ceiling = playing == null ? 256 : playing.interruptible ? playing.priority : 0;
            if (!queue.TryTake(now, encounter, valid, ceiling, out var request)) return;
            var cue = profile.Cue(request.Kind);
            if (cue == null || !Eligible(cue)) return;
            world.Release(voice);
            AudioClip clip = cue.variants.Length > 0 ? cue.variants[variant++ % cue.variants.Length] : null;
            voice = world.Play(clip, SensoryCategory.Radio, null, Vector3.zero, 1, 1, 4);
            // Subtitle-only cues are useful for asset-free authoring; no synthesized voices or claimed recordings.
            playing = cue; endsAt = now + (clip != null ? clip.length : 3);
            Subtitle = cue.subtitle ?? "";
            queue.MarkPlayed(cue.kind, now, cue.cooldown); world.DuckForRadio((float)(endsAt - now));
        }
        private bool Eligible(PoliceRadioCue cue)
        {
            if (director.HeatLevel < cue.minimumHeat || director.HeatLevel > cue.maximumHeat) return false;
            if (cue.kind == RadioCueKind.Roadblock || cue.kind == RadioCueKind.Spikes)
            {
                var kind = cue.kind == RadioCueKind.Roadblock ? PoliceRoadHazardKind.Roadblock : PoliceRoadHazardKind.SpikeStrip;
                foreach (var hazard in hazards) if (hazard != null && hazard.IsDeployed && hazard.Kind == kind) return true;
                return false;
            }
            if (cue.kind == RadioCueKind.Reinforcements) return director.ActiveUnitCount > 1;
            if (cue.kind == RadioCueKind.UnitDisabled) { foreach (var unit in units) if (unit != null && unit.IsDisabled) return true; return false; }
            if (cue.kind == RadioCueKind.HeavyImpact) return RecentHeavyImpact(5) && director.TimeSinceTargetSeen <= 1;
            return true;
        }
        private bool RecentHeavyImpact(double duration)
        {
            if (vehicleAudio == null) return false;
            var impact = vehicleAudio.Frame.LastImpact;
            double age = Time.timeAsDouble - impact.Time;
            return impact.Sequence != 0 && impact.Severity >= 0.6f && age >= 0 && age < duration;
        }
        private void Poll()
        {
            string id = director.EncounterId;
            // The committed outcome may clear the director ID. Retain our generation until a new nonempty encounter starts.
            if (!string.IsNullOrEmpty(id) && id != encounterId)
            {
                encounterId = id; encounter++; queue.Clear(); outcomeMask = 0; initialized = false;
                world.Release(voice); playing = null; Subtitle = "";
            }
            var state = director.EncounterState;
            int responding = director.ActiveUnitCount, disabled = 0, roadblocks = 0, spikes = 0;
            foreach (var unit in units) if (unit != null && unit.IsDisabled) disabled++;
            foreach (var hazard in hazards) if (hazard != null && hazard.IsDeployed)
            { if (hazard.Kind == PoliceRoadHazardKind.Roadblock) roadblocks++; else spikes++; }
            bool lost = state == PoliceEncounterState.Pursuit && director.TimeSinceTargetSeen > 1;
            Threat = state == PoliceEncounterState.Pursuit
                ? SensoryMath.Unit(director.HeatLevel / 5f * 0.5f + responding / 8f * 0.25f + director.BustProgress * 0.25f) : 0;
            valid = outcomeMask;
            if (state == PoliceEncounterState.Observed) valid |= Bit(RadioCueKind.Observed);
            if (state == PoliceEncounterState.TrafficStop) valid |= Bit(RadioCueKind.TrafficStop);
            if (state == PoliceEncounterState.Cooldown) valid |= Bit(RadioCueKind.Cooldown);
            if (state == PoliceEncounterState.Pursuit)
            {
                valid |= Bit(lost ? RadioCueKind.Searching : RadioCueKind.Pursuit);
                if (responding > 1) valid |= Bit(RadioCueKind.Reinforcements);
                if (disabled > 0) valid |= Bit(RadioCueKind.UnitDisabled);
                if (roadblocks > 0) valid |= Bit(RadioCueKind.Roadblock);
                if (spikes > 0) valid |= Bit(RadioCueKind.Spikes);
                if (!lost && RecentHeavyImpact(5))
                {
                    valid |= Bit(RadioCueKind.HeavyImpact);
                    int impact = vehicleAudio.Frame.LastImpact.Sequence;
                    if (impact != lastImpactSequence) { lastImpactSequence = impact; Queue(RadioCueKind.HeavyImpact); }
                }
            }
            if (!initialized || state != lastState)
            {
                if (state == PoliceEncounterState.Observed) Queue(RadioCueKind.Observed);
                if (state == PoliceEncounterState.TrafficStop) Queue(RadioCueKind.TrafficStop);
                if (state == PoliceEncounterState.Pursuit) Queue(RadioCueKind.Pursuit);
                if (state == PoliceEncounterState.Cooldown) Queue(RadioCueKind.Cooldown);
            }
            if (lost && !searching) Queue(RadioCueKind.Searching);
            if (initialized && responding > lastUnits) Queue(RadioCueKind.Reinforcements);
            if (initialized && disabled > lastDisabled) Queue(RadioCueKind.UnitDisabled);
            if (roadblocks > lastRoadblocks) Queue(RadioCueKind.Roadblock);
            if (spikes > lastSpikes) Queue(RadioCueKind.Spikes);
            lastState = state; lastUnits = responding; lastDisabled = disabled;
            lastRoadblocks = roadblocks; lastSpikes = spikes; searching = lost; initialized = true;
        }
        private void OnDisable()
        {
            if (director != null) director.OutcomeAcknowledged -= Outcome;
            if (world != null) world.Release(voice);
            queue.Clear(); playing = null; Subtitle = ""; initialized = false; outcomeMask = 0; Threat = 0;
        }
    }
}
