using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public static class MusicTiming
    {
        public static double BeatDuration(double bpm)
        {
            if (double.IsNaN(bpm) || double.IsInfinity(bpm) || bpm <= 0) throw new ArgumentOutOfRangeException(nameof(bpm));
            return 60d / bpm;
        }

        public static double NextBeat(double now, double origin, double bpm, double lead)
        {
            Validate(now, origin, bpm, 1, lead);
            double duration = BeatDuration(bpm);
            return origin + Math.Ceiling((now + lead - origin) / duration) * duration;
        }

        public static double NextBar(double now, double origin, double bpm, int beatsPerBar, double lead)
        {
            Validate(now, origin, bpm, beatsPerBar, lead);
            double bar = BeatDuration(bpm) * beatsPerBar;
            return origin + Math.Ceiling((now + lead - origin) / bar) * bar;
        }

        public static double NextBoundary(double now, double origin, double bpm, int beatsPerBar,
            MusicQuantization quantization, double lead, double sectionEnd = 0, double markerBeat = -1, double loopBeats = 0)
        {
            Validate(now, origin, bpm, beatsPerBar, lead);
            switch (quantization)
            {
                case MusicQuantization.Immediate: return Math.Max(now + lead, now);
                case MusicQuantization.Beat: return NextBeat(now, origin, bpm, lead);
                case MusicQuantization.SectionEnd:
                    if (sectionEnd > now + lead && !double.IsNaN(sectionEnd) && !double.IsInfinity(sectionEnd)) return sectionEnd;
                    return NextBar(now, origin, bpm, beatsPerBar, lead);
                case MusicQuantization.Marker:
                    if (markerBeat >= 0 && loopBeats > 0) return NextMarker(now, origin, bpm, markerBeat, loopBeats, lead);
                    return NextBar(now, origin, bpm, beatsPerBar, lead);
                case MusicQuantization.Bar:
                default: return NextBar(now, origin, bpm, beatsPerBar, lead);
            }
        }

        public static double NextMarker(double now, double origin, double bpm, double markerBeat, double loopBeats, double lead)
        {
            Validate(now, origin, bpm, 1, lead);
            if (double.IsNaN(markerBeat) || double.IsInfinity(markerBeat) || markerBeat < 0
                || double.IsNaN(loopBeats) || double.IsInfinity(loopBeats) || loopBeats <= 0 || markerBeat >= loopBeats)
                throw new ArgumentOutOfRangeException(nameof(markerBeat));
            double beatDuration = BeatDuration(bpm);
            double cycle = loopBeats * beatDuration;
            double markerOrigin = origin + markerBeat * beatDuration;
            double target = now + lead;
            double occurrence = markerOrigin + Math.Ceiling((target - markerOrigin) / cycle) * cycle;
            if (occurrence < target) occurrence += cycle;
            return occurrence;
        }

        public static double PositionBeats(double now, double origin, double bpm)
        {
            Validate(now, origin, bpm, 1, 0);
            return Math.Max(0, (now - origin) / BeatDuration(bpm));
        }

        public static int BeatInBar(double now, double origin, double bpm, int beatsPerBar)
        {
            int beat = (int)Math.Floor(PositionBeats(now, origin, bpm));
            return Mod(beat, Math.Max(1, beatsPerBar));
        }

        public static int BarIndex(double now, double origin, double bpm, int beatsPerBar)
        {
            return Math.Max(0, (int)Math.Floor(PositionBeats(now, origin, bpm) / Math.Max(1, beatsPerBar)));
        }

        private static int Mod(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        private static void Validate(double now, double origin, double bpm, int beatsPerBar, double lead)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || double.IsNaN(origin) || double.IsInfinity(origin)
                || double.IsNaN(bpm) || double.IsInfinity(bpm) || bpm <= 0 || beatsPerBar < 1
                || double.IsNaN(lead) || double.IsInfinity(lead) || lead < 0)
                throw new ArgumentOutOfRangeException(nameof(bpm));
        }
    }

    /// <summary>
    /// The one runtime owner of adaptive music transport and state arbitration.
    /// SensoryAudioWorld remains the voice/mixer authority; this component only
    /// decides what to schedule and when.
    /// </summary>
    [DefaultExecutionOrder(-190), DisallowMultipleComponent]
    public sealed class AdaptiveMusic : MonoBehaviour
    {
        private const int MaxRequests = 32;
        private const int MaxStingerRequests = 16;
        private const int TraceCapacity = 96;
        private const int MaxHistory = 32;
        private const string AutomaticRequestId = "runtime.automatic";

        private sealed class MusicRequestSlot
        {
            public bool active;
            public string id, source, sectionId;
            public AdaptiveMusicContext context;
            public MusicRequestKind kind;
            public MusicQuantization quantization;
            public float intensity;
            public int priority;
            public long sequence;
            public double createdAt, expiresAt;

            public void Clear()
            {
                active = false; id = source = sectionId = string.Empty; context = AdaptiveMusicContext.None;
                intensity = 0; priority = 0; sequence = 0; createdAt = expiresAt = 0;
                quantization = MusicQuantization.Bar;
            }
        }

        private sealed class StingerRequestSlot
        {
            public bool active;
            public MusicStinger definition;
            public string id, source;
            public AdaptiveMusicContext context;
            public int priority;
            public long sequence;
            public double requestedAt, expiresAt;

            public void Clear()
            {
                active = false; definition = null; id = source = string.Empty; context = AdaptiveMusicContext.None;
                priority = 0; sequence = 0; requestedAt = expiresAt = 0;
            }
        }

        private sealed class BedVoice
        {
            public MusicStem stem;
            public FeedbackVoiceLease lease;
            public double scheduledAt;

            public void Clear()
            {
                stem = null; lease = default; scheduledAt = 0;
            }
        }

        private sealed class BedState
        {
            public readonly BedVoice[] voices;
            public MusicSection section;
            public bool active;
            public double scheduledAt;
            public int count;

            public BedState(int capacity)
            {
                voices = new BedVoice[Mathf.Clamp(capacity, 1, 16)];
                for (int i = 0; i < voices.Length; i++) voices[i] = new BedVoice();
            }

            public void ClearData()
            {
                section = null; active = false; scheduledAt = 0; count = 0;
                for (int i = 0; i < voices.Length; i++) voices[i].Clear();
            }
        }

        private sealed class StingerHistory
        {
            public string id;
            public double requestedAt = double.NegativeInfinity;
            public double playedAt = double.NegativeInfinity;
        }

        [SerializeField] private SensoryAudioWorld world;
        [SerializeField] private SensoryMusicProfile profile;
        [SerializeField] private PursuitSensoryBridge pursuit;

        private MusicRequestSlot[] requests;
        private StingerRequestSlot[] stingerRequests;
        private AdaptiveMusicTraceEntry[] trace;
        private StingerHistory[] history;
        private BedState activeBed, pendingBed, fadingBed;
        private MusicSection legacySection;
        private MusicStinger[] legacyStingers;
        private MusicStinger currentStinger;
        private FeedbackVoiceLease stingerLease;
        private AdaptiveMusicTransportSnapshot snapshot;
        private MusicRequestSlot winner;
        private MusicGameplaySnapshot injectedSnapshot;
        private bool hasInjectedSnapshot;
        private bool wasAudioPaused;
        private bool frontendClock;
        private bool previewActive;
        private GameObject overlayRoot;
        private bool started;
        private bool sawDspClock;
        private bool legacyBuilt;
        private bool startedOnce;
        private long sequence;
        private int traceCursor, traceCount;
        private int droppedStems;
        private double transportOrigin;
        private double lastDspTime = -1;
        private double lastCommitAt = double.NegativeInfinity;
        private double fadingUntil;
        private double lastIntensityChange = double.NegativeInfinity;
        private float currentIntensity;
        private float targetIntensity;
        private AdaptiveMusicContext activeContext = AdaptiveMusicContext.FreeRoam;
        private string lastTransitionId = string.Empty;
        private double lastTransitionAt = double.NegativeInfinity;
        private string lastError = string.Empty;
        private string pendingRequestId = string.Empty;
        private string pendingSource = string.Empty;
        private string bridgeTargetSectionId = string.Empty;
        private string bridgeSectionId = string.Empty;
        private string bridgeTransitionId = string.Empty;
        private string currentStingerRequestId = string.Empty;
        private string currentStingerSource = string.Empty;
        private double stingerScheduledAt;
        private static AdaptiveMusic instance;

        public static AdaptiveMusic Instance => instance;
        public SensoryMusicProfile Profile => profile;
        public SensoryAudioWorld AudioWorld => world;
        public AdaptiveMusicTransportSnapshot Transport => snapshot;
        public double NextTransition => pendingBed != null && pendingBed.active ? pendingBed.scheduledAt : 0;
        public string ActiveSectionId => activeBed != null && activeBed.active && activeBed.section != null ? activeBed.section.stableId : string.Empty;
        public string LastError => lastError;
        public AdaptiveMusicContext ActiveContext => activeContext;
        public bool HasStarted => started;
        public void SetPreviewActive(bool value) { previewActive = value; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; }

        public static int ContextPriority(AdaptiveMusicContext context)
        {
            if ((context & AdaptiveMusicContext.Escape) != 0 || (context & AdaptiveMusicContext.Failure) != 0 || (context & AdaptiveMusicContext.Results) != 0) return 1000;
            if ((context & AdaptiveMusicContext.Pause) != 0) return 950;
            if ((context & AdaptiveMusicContext.Cinematic) != 0) return 900;
            if ((context & AdaptiveMusicContext.Pursuit) != 0) return 800;
            if ((context & AdaptiveMusicContext.Cooldown) != 0) return 700;
            if ((context & AdaptiveMusicContext.Race) != 0) return 600;
            if ((context & AdaptiveMusicContext.Frontend) != 0) return 500;
            return 400;
        }

        public void Configure(SensoryAudioWorld audioWorld, SensoryMusicProfile content)
        {
            bool restart = started || startedOnce;
            if (restart) ResetTransport();
            world = audioWorld; profile = content;
            if (restart) StartTransport();
        }

        public void SetPursuit(PursuitSensoryBridge source)
        {
            if (pursuit != null) pursuit.OutcomeCommitted -= Outcome;
            pursuit = source;
            if (isActiveAndEnabled && pursuit != null) pursuit.OutcomeCommitted += Outcome;
        }

        /// <summary>Explicit semantic request. Source IDs are stable ownership keys, not gameplay state.</summary>
        public bool RequestContext(AdaptiveMusicContext context, float intensity, string sourceId,
            int priority = -1, double lifetimeSeconds = 0, MusicQuantization quantization = MusicQuantization.Bar)
        {
            if (requests == null) return RejectRequest("Adaptive music director is not initialized.");
            context = NormalizeContext(context);
            if (context == AdaptiveMusicContext.None) return RejectRequest("A music context is required.");
            if (!ValidLifetime(lifetimeSeconds)) return RejectRequest("Music request lifetime must be finite and non-negative.");
            string source = NormalizeSource(sourceId);
            string id = "context:" + source;
            var slot = FindOrCreateRequest(id);
            if (slot == null) return false;
            double now = SafeDspTime();
            bool changed = !slot.active || slot.kind != MusicRequestKind.Context || slot.context != context
                || Mathf.Abs(slot.intensity - Mathf.Clamp01(intensity)) > 0.0001f || slot.priority != (priority < 0 ? ContextPriority(context) : priority);
            slot.active = true; slot.id = id; slot.source = source; slot.context = context; slot.kind = MusicRequestKind.Context;
            slot.sectionId = string.Empty; slot.intensity = Mathf.Clamp01(SensoryMath.Finite(intensity));
            slot.priority = Mathf.Clamp(priority < 0 ? ContextPriority(context) : priority, 0, 2048);
            slot.quantization = quantization; slot.createdAt = now;
            slot.expiresAt = lifetimeSeconds <= 0 ? double.PositiveInfinity : now + lifetimeSeconds;
            if (changed) slot.sequence = ++sequence;
            Record("context.request", id, source, ActiveSectionId, string.Empty, context, quantization, now, 0, changed ? "accepted" : "refreshed");
            return true;
        }

        /// <summary>Requests a concrete section while preserving the same arbitration path.</summary>
        public bool RequestSection(string sectionId, string sourceId, int priority = -1, double lifetimeSeconds = 0,
            MusicQuantization quantization = MusicQuantization.Bar)
        {
            if (requests == null) return RejectRequest("Adaptive music director is not initialized.");
            if (string.IsNullOrWhiteSpace(sectionId)) return RejectRequest("A section ID is required.");
            if (!ValidLifetime(lifetimeSeconds)) return RejectRequest("Music request lifetime must be finite and non-negative.");
            if (profile != null && ResolveSection(sectionId) == null) return RejectRequest("Unknown music section: " + sectionId);
            string source = NormalizeSource(sourceId);
            string id = "section:" + source;
            var slot = FindOrCreateRequest(id);
            if (slot == null) return false;
            double now = SafeDspTime();
            AdaptiveMusicContext context = activeContext == AdaptiveMusicContext.None ? AdaptiveMusicContext.FreeRoam : activeContext;
            int resolvedPriority = priority < 0 ? ContextPriority(context) : priority;
            bool changed = !slot.active || slot.kind != MusicRequestKind.Section || !string.Equals(slot.sectionId, sectionId, StringComparison.Ordinal)
                || slot.priority != resolvedPriority;
            slot.active = true; slot.id = id; slot.source = source; slot.context = context; slot.kind = MusicRequestKind.Section;
            slot.sectionId = sectionId; slot.intensity = targetIntensity; slot.priority = Mathf.Clamp(resolvedPriority, 0, 2048);
            slot.quantization = quantization; slot.createdAt = now;
            slot.expiresAt = lifetimeSeconds <= 0 ? double.PositiveInfinity : now + lifetimeSeconds;
            if (changed) slot.sequence = ++sequence;
            Record("section.request", id, source, ActiveSectionId, sectionId, context, quantization, now, 0, changed ? "accepted" : "refreshed");
            return true;
        }

        public bool ClearRequest(string requestId)
        {
            if (requests == null || string.IsNullOrWhiteSpace(requestId)) return false;
            for (int i = 0; i < requests.Length; i++)
                if (requests[i].active && (requests[i].id == requestId || requests[i].source == requestId))
                { requests[i].Clear(); Record("request.cancel", requestId, requestId, ActiveSectionId, string.Empty, activeContext, MusicQuantization.Immediate, SafeDspTime(), 0, "explicit"); return true; }
            return false;
        }

        public void CancelRequestsFromSource(string sourceId)
        {
            if (requests == null || stingerRequests == null) return;
            string source = NormalizeSource(sourceId);
            for (int i = 0; i < requests.Length; i++) if (requests[i].active && requests[i].source == source) requests[i].Clear();
            for (int i = 0; i < stingerRequests.Length; i++) if (stingerRequests[i].active && stingerRequests[i].source == source) stingerRequests[i].Clear();
            if (currentStinger != null && currentStingerSource == source) CancelCurrentStinger("source cancelled");
            Record("source.cancel", source, source, ActiveSectionId, string.Empty, activeContext, MusicQuantization.Immediate, SafeDspTime(), 0, "all requests");
        }

        /// <summary>Injects a read-only gameplay snapshot for a test harness or an integration adapter.</summary>
        public void SetGameplaySnapshot(MusicGameplaySnapshot value)
        {
            value.context = NormalizeContext(value.context);
            value.intensity = Mathf.Clamp01(SensoryMath.Finite(value.intensity));
            value.sourceId = NormalizeSource(value.sourceId);
            injectedSnapshot = value; hasInjectedSnapshot = value.context != AdaptiveMusicContext.None;
            Record("snapshot.inject", value.sourceId, value.sourceId, ActiveSectionId, string.Empty, value.context, MusicQuantization.Bar, SafeDspTime(), 0, "explicit test/integration input");
        }

        public void ClearGameplaySnapshot()
        {
            hasInjectedSnapshot = false; injectedSnapshot = default;
            Record("snapshot.clear", string.Empty, string.Empty, ActiveSectionId, string.Empty, activeContext, MusicQuantization.Immediate, SafeDspTime(), 0, "returned to runtime flow");
        }

        public bool RequestStinger(string stingerId, string sourceId, AdaptiveMusicContext context = AdaptiveMusicContext.None)
        {
            if (profile == null || stingerRequests == null || string.IsNullOrWhiteSpace(stingerId)) return false;
            var definition = profile.FindStinger(stingerId);
            return QueueStinger(definition, sourceId, context, definition != null ? definition.priority : 0);
        }

        public int CopyStemDiagnostics(List<AdaptiveMusicStemSnapshot> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear(); double now = SafeDspTime();
            AddBedDiagnostics(destination, activeBed, now, 1f);
            AddBedDiagnostics(destination, fadingBed, now, FadingGain(now));
            AddBedDiagnostics(destination, pendingBed, now, pendingBed != null && pendingBed.active && now >= pendingBed.scheduledAt ? 1f : 0f);
            return destination.Count;
        }

        public int CopyTrace(List<AdaptiveMusicTraceEntry> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            if (trace == null || traceCount == 0) return 0;
            int first = (traceCursor - traceCount + trace.Length) % trace.Length;
            for (int i = 0; i < traceCount; i++) destination.Add(trace[(first + i) % trace.Length]);
            return destination.Count;
        }

        public void ClearDiagnostics()
        {
            lastError = string.Empty; traceCursor = traceCount = 0;
            if (trace != null) Array.Clear(trace, 0, trace.Length);
        }

        private void Awake()
        {
            if (!TryClaimInstance()) return;
            requests = new MusicRequestSlot[MaxRequests];
            for (int i = 0; i < requests.Length; i++) requests[i] = new MusicRequestSlot();
            stingerRequests = new StingerRequestSlot[MaxStingerRequests];
            for (int i = 0; i < stingerRequests.Length; i++) stingerRequests[i] = new StingerRequestSlot();
            trace = new AdaptiveMusicTraceEntry[TraceCapacity]; history = new StingerHistory[MaxHistory];
            AudioSettings.OnAudioConfigurationChanged += AudioDeviceChanged;
        }

        private void OnEnable()
        {
            if (!TryClaimInstance()) return;
            if (overlayRoot != null) overlayRoot.SetActive(true);
            if (pursuit != null) pursuit.OutcomeCommitted += Outcome;
            if (startedOnce && !started) StartTransport();
        }

        private void Start()
        {
            if (!started) StartTransport();
            startedOnce = true;
            if (started)
            {
                // A nested UIDocument inherits the frontend's panel and disappears in
                // gameplay. Keep this document independent, with the same owner lifetime.
                overlayRoot = new GameObject("EA TRAX Overlay");
                if (GetComponent<GameFlowRuntime>() != null) DontDestroyOnLoad(overlayRoot);
                overlayRoot.AddComponent<MostWantedMusicOverlay>();
            }
        }

        private void StartTransport()
        {
            started = false;
            frontendClock = IsFrontendAudioState();
            if (profile == null) { SetError("No SensoryMusicProfile is assigned."); return; }
            if (world == null) { SetError("No SensoryAudioWorld is assigned."); return; }
            if (!profile.Validate(out string failure)) { SetError(failure); return; }
            BuildLegacyCompatibility();
            int capacity = profile.playback == null ? 8 : Mathf.Clamp(profile.playback.maxConcurrentStems, 1, 16);
            activeBed = new BedState(capacity); pendingBed = new BedState(capacity); fadingBed = new BedState(capacity);
            transportOrigin = SafeDspTime() + Lookahead(); lastDspTime = -1; sawDspClock = false;
            lastCommitAt = double.NegativeInfinity; fadingUntil = 0; currentIntensity = targetIntensity = 0;
            activeContext = AdaptiveMusicContext.FreeRoam; pendingRequestId = pendingSource = string.Empty;
            started = true;
            Record("transport.start", string.Empty, "runtime", string.Empty, string.Empty, activeContext, MusicQuantization.Bar, SafeDspTime(), transportOrigin, profile.HasArrangement ? "arrangement" : "legacy compatibility");
        }

        private void Update()
        {
            if (!started || profile == null) { RebuildSnapshot(SafeDspTime()); return; }
            // Title/boot remains silent regardless of the authored gameplay pause policy.
            if (GameFlowRuntime.Instance?.Flow?.State == GameFlowState.Boot) return;
            bool frontend = IsFrontendAudioState();
            if (frontendClock != frontend)
            {
                frontendClock = frontend;
                ReanchorAfterDiscontinuity("frontend clock changed", false);
            }
            double now = SafeDspTime();
            if (sawDspClock && (now + 0.001 < lastDspTime || now - lastDspTime > 30)) ReanchorAfterDiscontinuity("DSP clock discontinuity");
            sawDspClock = true; lastDspTime = now;
            bool pauseTransport = profile.playback == null || profile.playback.pauseMode == MusicPauseMode.PauseTransport;
            if (AudioListener.pause && pauseTransport && !IsFrontendAudioState())
            {
                wasAudioPaused = true; RebuildSnapshot(now); return;
            }
            if (wasAudioPaused)
            {
                wasAudioPaused = false; ReanchorAfterDiscontinuity("resume after audio pause", false);
                now = SafeDspTime();
            }
            UpdateAutomaticRequest(now);
            ExpireRequests(now);
            winner = ResolveWinner(now);
            if (winner == null) winner = AutomaticFallback(now);
            activeContext = winner.context;
            UpdateIntensity(winner.intensity, now);
            EnsureTransition(winner, now);
            CommitPending(now);
            UpdateBeds(now, AudioListener.pause && profile.playback != null && profile.playback.pauseMode == MusicPauseMode.DuckOnly ? 0.35f : 1f);
            ProcessStingers(now);
            RebuildSnapshot(now);
        }

        private void UpdateAutomaticRequest(double now)
        {
            MusicGameplaySnapshot value = ResolveRuntimeSnapshot();
            var slot = FindOrCreateRequest(AutomaticRequestId);
            if (slot == null) return;
            int priority = ContextPriority(value.context);
            bool changed = !slot.active || slot.context != value.context || Mathf.Abs(slot.intensity - value.intensity) > 0.0001f;
            slot.active = true; slot.id = AutomaticRequestId; slot.source = string.IsNullOrEmpty(value.sourceId) ? "runtime.flow" : value.sourceId;
            slot.context = value.context; slot.kind = MusicRequestKind.Context; slot.sectionId = string.Empty; slot.intensity = value.intensity;
            slot.priority = priority; slot.quantization = MusicQuantization.Bar; slot.createdAt = now; slot.expiresAt = double.PositiveInfinity;
            if (changed) slot.sequence = ++sequence;
        }

        private MusicGameplaySnapshot ResolveRuntimeSnapshot()
        {
            if (hasInjectedSnapshot) return injectedSnapshot;
            var value = new MusicGameplaySnapshot { context = AdaptiveMusicContext.FreeRoam, intensity = 0.08f, sourceId = "runtime.flow" };
            var flow = GameFlowRuntime.Instance != null ? GameFlowRuntime.Instance.Flow : null;
            // Application state owns the frontend boundary. A gameplay world's default mix is
            // intentionally neutral, so it must not mask menu, results or pause music.
            if (flow != null)
            {
                switch (flow.State)
                {
                    case GameFlowState.Boot:
                    case GameFlowState.MainMenu:
                    case GameFlowState.SceneTransition:
                        value.context = AdaptiveMusicContext.Frontend; value.intensity = 0.12f; value.sourceId = "runtime.game-flow"; return value;
                    case GameFlowState.Results:
                        value.context = AdaptiveMusicContext.Results; value.intensity = 0.18f; value.sourceId = "runtime.game-flow"; return value;
                    case GameFlowState.Paused:
                        value.context = AdaptiveMusicContext.Pause; value.intensity = 0.15f; value.sourceId = "runtime.game-flow"; return value;
                    case GameFlowState.RaceActive:
                    case GameFlowState.EventLoading:
                        value.context = AdaptiveMusicContext.Race; value.intensity = 0.55f; value.sourceId = "runtime.game-flow"; return value;
                }
                if (GameFlowRuntime.Instance.WorldSession?.State == FreeRoamState.Location)
                {
                    value.context = AdaptiveMusicContext.Frontend; value.intensity = 0.12f; value.sourceId = "runtime.location"; return value;
                }
            }
            if (pursuit != null)
            {
                if (pursuit.GetComponent<VehiclePursuitDirector>() != null)
                {
                    var state = pursuit.GetComponent<VehiclePursuitDirector>().EncounterState;
                    if (state == PoliceEncounterState.Pursuit)
                    { value.context = AdaptiveMusicContext.Pursuit; value.intensity = pursuit.Threat; value.sourceId = "runtime.pursuit"; return value; }
                    if (state == PoliceEncounterState.Cooldown)
                    { value.context = AdaptiveMusicContext.Cooldown; value.intensity = 0.35f + pursuit.Threat * 0.25f; value.sourceId = "runtime.pursuit"; return value; }
                }
            }
            if (world != null)
            {
                if (world.MixState == SensoryMixState.Pursuit)
                { value.context = AdaptiveMusicContext.Pursuit; value.intensity = pursuit != null ? pursuit.Threat : 0.75f; value.sourceId = "runtime.sensory-mix"; return value; }
                if (world.MixState == SensoryMixState.Cooldown)
                { value.context = AdaptiveMusicContext.Cooldown; value.intensity = 0.4f; value.sourceId = "runtime.sensory-mix"; return value; }
                if (world.MixState == SensoryMixState.Race)
                { value.context = AdaptiveMusicContext.Race; value.intensity = 0.55f; value.sourceId = "runtime.sensory-mix"; return value; }
                if (world.MixState == SensoryMixState.Paused)
                { value.context = AdaptiveMusicContext.Pause; value.intensity = 0.15f; value.sourceId = "runtime.sensory-mix"; return value; }
            }
            return value;
        }

        private MusicRequestSlot AutomaticFallback(double now)
        {
            var slot = new MusicRequestSlot { active = true, id = AutomaticRequestId, source = "runtime.fallback",
                context = AdaptiveMusicContext.FreeRoam, kind = MusicRequestKind.Context, intensity = 0.08f,
                priority = ContextPriority(AdaptiveMusicContext.FreeRoam), quantization = MusicQuantization.Bar,
                createdAt = now, expiresAt = double.PositiveInfinity, sequence = sequence };
            return slot;
        }

        private MusicRequestSlot ResolveWinner(double now)
        {
            MusicRequestSlot best = null;
            for (int i = 0; i < requests.Length; i++)
            {
                var slot = requests[i];
                if (!slot.active || slot.expiresAt < now) continue;
                if (best == null || slot.priority > best.priority || slot.priority == best.priority && slot.sequence > best.sequence) best = slot;
            }
            return best;
        }

        private void ExpireRequests(double now)
        {
            for (int i = 0; i < requests.Length; i++)
                if (requests[i].active && requests[i].expiresAt < now)
                { Record("request.expire", requests[i].id, requests[i].source, ActiveSectionId, requests[i].sectionId, requests[i].context, requests[i].quantization, requests[i].createdAt, 0, "lifetime elapsed"); requests[i].Clear(); }
        }

        private void UpdateIntensity(float requested, double now)
        {
            float value = profile.intensity == null ? Mathf.Clamp01(requested) : profile.intensity.Evaluate(requested);
            float hysteresis = profile.intensity == null ? 0.05f : Mathf.Clamp(profile.intensity.hysteresis, 0, 0.5f);
            float dwell = profile.intensity == null ? 0.5f : Mathf.Max(0, profile.intensity.minimumDwellSeconds);
            if (double.IsNegativeInfinity(lastIntensityChange) || Mathf.Abs(value - targetIntensity) >= hysteresis && now - lastIntensityChange >= dwell)
            { targetIntensity = value; lastIntensityChange = now; }
            currentIntensity = SensoryMath.Envelope(currentIntensity, targetIntensity, Time.unscaledDeltaTime, 0.16f, 0.45f);
        }

        private void EnsureTransition(MusicRequestSlot request, double now)
        {
            string desiredId = request.kind == MusicRequestKind.Section ? request.sectionId : DefaultSectionFor(request.context);
            MusicSection desired = ResolveSection(desiredId);
            if (desired == null)
            {
                SetError("No section is assigned for context " + request.context + ".");
                return;
            }
            desired = ResolveReadyFallback(desired);
            if (desired == null) return;

            bool completingBridge = !string.IsNullOrEmpty(bridgeTargetSectionId)
                && activeBed != null && activeBed.active && activeBed.section != null
                && string.Equals(activeBed.section.stableId, bridgeSectionId, StringComparison.Ordinal)
                && string.Equals(desired.stableId, bridgeTargetSectionId, StringComparison.Ordinal);
            if (pendingBed != null && pendingBed.active && pendingBed.section != null)
            {
                bool pendingIsDesired = string.Equals(pendingBed.section.stableId, desired.stableId, StringComparison.Ordinal);
                bool pendingIsBridge = !string.IsNullOrEmpty(bridgeTargetSectionId)
                    && string.Equals(pendingBed.section.stableId, bridgeSectionId, StringComparison.Ordinal)
                    && string.Equals(desired.stableId, bridgeTargetSectionId, StringComparison.Ordinal);
                if (pendingIsDesired || pendingIsBridge)
                {
                    pendingRequestId = request.id; pendingSource = request.source;
                    return;
                }
                ReleaseBed(pendingBed);
                ClearBridgePlan();
            }
            if (!completingBridge && !string.IsNullOrEmpty(bridgeTargetSectionId)) ClearBridgePlan();
            if (activeBed != null && activeBed.active && activeBed.section != null && activeBed.section.stableId == desired.stableId
                && (pendingBed == null || !pendingBed.active)) return;

            MusicTransitionRule rule = SelectTransition(ActiveSectionId, desired.stableId, request.context, now);
            double minimumDwell = Math.Max(MinimumDwell(), rule != null ? rule.minimumDwellSeconds : 0);
            if (now - lastCommitAt < minimumDwell && activeBed != null && activeBed.active && request.priority < ContextPriority(AdaptiveMusicContext.Results))
            {
                pendingRequestId = request.id; pendingSource = request.source;
                Record("transition.defer", request.id, request.source, ActiveSectionId, desired.stableId, request.context, request.quantization, now, 0, "minimum dwell");
                return;
            }
            bool namedMarkerAvailable = rule == null || string.IsNullOrEmpty(rule.requiredMarker)
                || ActiveSection() != null && ActiveSection().FindMarker(rule.requiredMarker) != null
                || desired.FindMarker(rule.requiredMarker) != null
                || ActiveSection() != null && ActiveSection().exitMarker == rule.requiredMarker
                || desired.entryMarker == rule.requiredMarker;
            if (rule != null && !string.IsNullOrEmpty(rule.requiredMarker) && !namedMarkerAvailable
                && (ActiveSection() == null || !ActiveSection().allowFreeTimeTransition))
            {
                SetError("Transition " + rule.stableId + " requires marker " + rule.requiredMarker + ".");
                return;
            }
            MusicSection bridge = null;
            if (!completingBridge && rule != null && !string.IsNullOrEmpty(rule.bridgeSectionId))
            {
                bridge = ResolveReadyFallback(ResolveSection(rule.bridgeSectionId));
                if (bridge == null) return;
                if (!bridge.Allows(request.context))
                {
                    SetError("Bridge section " + bridge.stableId + " is not eligible for " + request.context + ".");
                    return;
                }
                if (string.Equals(bridge.stableId, desired.stableId, StringComparison.Ordinal)
                    || string.Equals(bridge.stableId, ActiveSectionId, StringComparison.Ordinal)) bridge = null;
                else if (!CompatibleTransition(ActiveSection(), bridge, rule)) return;
            }
            if (bridge == null && rule != null && !CompatibleTransition(ActiveSection(), desired, rule)) return;
            MusicSection scheduledSection = bridge ?? desired;
            double sectionEnd = NextSectionEnd(now);
            MusicQuantization quantization = rule != null ? rule.quantization : request.quantization;
            MusicMarker marker = null;
            if (quantization == MusicQuantization.Marker)
            {
                string markerId = rule != null ? rule.requiredMarker : string.Empty;
                marker = ActiveSection() != null && !string.IsNullOrEmpty(markerId) ? ActiveSection().FindMarker(markerId) : null;
                if (marker == null && scheduledSection != null && !string.IsNullOrEmpty(markerId)) marker = scheduledSection.FindMarker(markerId);
                if (marker == null && string.IsNullOrEmpty(markerId)) marker = ActiveSection() != null ? ActiveSection().FirstTransitionMarker() : null;
                if (marker == null && scheduledSection != null) marker = scheduledSection.FirstTransitionMarker();
                if (marker == null)
                {
                    Record("transition.marker-fallback", request.id, request.source, ActiveSectionId, desired.stableId, request.context,
                        quantization, now, 0, "No named transition marker; fell back to bar grid.");
                    quantization = MusicQuantization.Bar;
                }
            }
            double at = MusicTiming.NextBoundary(now, transportOrigin, ActiveBpm(), ActiveBeatsPerBar(), quantization, Lookahead(), sectionEnd,
                marker != null ? marker.beat : -1, ActiveSection() != null ? ActiveSection().LoopBeats() : desired.LoopBeats());
            if (rule != null && rule.cooldownSeconds > 0 && rule.stableId == lastTransitionId && now - lastTransitionAt < rule.cooldownSeconds)
            {
                pendingRequestId = request.id; pendingSource = request.source;
                SetError("Transition " + rule.stableId + " is on cooldown."); return;
            }
            if (pendingBed != null && pendingBed.active) ReleaseBed(pendingBed);
            pendingRequestId = request.id; pendingSource = request.source;
            string transitionId = rule != null ? rule.stableId : string.Empty;
            if (!ScheduleBed(scheduledSection, at, bridge != null ? transitionId + ".bridge" : transitionId)) return;
            if (bridge != null)
            {
                bridgeTargetSectionId = desired.stableId;
                bridgeSectionId = bridge.stableId;
                bridgeTransitionId = transitionId;
                Record("transition.bridge", request.id, request.source, ActiveSectionId, desired.stableId, request.context, quantization, now, at,
                    "bridge=" + bridge.stableId + (string.IsNullOrEmpty(bridgeTransitionId) ? string.Empty : " transition=" + bridgeTransitionId));
            }
            else if (completingBridge)
            {
                ClearBridgePlan();
            }
            if (rule != null) { lastTransitionId = rule.stableId; lastTransitionAt = now; }
        }

        private MusicTransitionRule SelectTransition(string fromId, string toId, AdaptiveMusicContext context, double now)
        {
            if (!profile.HasArrangement || profile.transitions == null) return null;
            MusicTransitionRule best = null; int bestSpecificity = -1;
            foreach (var rule in profile.transitions)
            {
                if (rule == null || !rule.Matches(fromId, toId, context)) continue;
                if (rule.cooldownSeconds > 0 && rule.stableId == lastTransitionId && now - lastTransitionAt < rule.cooldownSeconds) continue;
                int specificity = string.IsNullOrEmpty(rule.fromSectionId) || rule.fromSectionId == "*" ? 0 : 1;
                if (best == null || rule.priority > best.priority || rule.priority == best.priority && specificity > bestSpecificity
                    || rule.priority == best.priority && specificity == bestSpecificity && string.CompareOrdinal(rule.stableId, best.stableId) < 0)
                { best = rule; bestSpecificity = specificity; }
            }
            return best;
        }

        private bool CompatibleTransition(MusicSection from, MusicSection to, MusicTransitionRule rule)
        {
            if (from == null || to == null) return true;
            bool tempoMismatch = Mathf.Abs(from.bpm - to.bpm) > 0.001f || from.beatsPerBar != to.beatsPerBar;
            bool harmonicMismatch = !string.IsNullOrEmpty(from.harmonicFamily) && !string.IsNullOrEmpty(to.harmonicFamily)
                && !string.Equals(from.harmonicFamily, to.harmonicFamily, StringComparison.Ordinal);
            if (tempoMismatch && !rule.allowTempoChange)
            { SetError("Transition " + rule.stableId + " changes tempo/meter without allowTempoChange."); return false; }
            if (harmonicMismatch && !rule.allowHarmonicMismatch && !to.allowHarmonicMismatch)
            { SetError("Transition " + rule.stableId + " changes harmonic family without an override."); return false; }
            return true;
        }

        private bool ScheduleBed(MusicSection section, double at, string transitionId)
        {
            if (section == null) return false;
            if (world == null) { SetError("No SensoryAudioWorld is assigned; music scheduling is unavailable."); return false; }
            pendingBed.active = true; pendingBed.section = section; pendingBed.scheduledAt = Math.Max(at, SafeDspTime()); pendingBed.count = 0;
            droppedStems = 0;
            bool missingCriticalStem = false;
            MusicStem[] sectionStems = section.stems ?? Array.Empty<MusicStem>();
            for (int i = 0; i < sectionStems.Length && i < pendingBed.voices.Length; i++)
            {
                var stem = sectionStems[i];
                if (stem == null || stem.clip == null)
                {
                    if (stem != null && stem.critical) { missingCriticalStem = true; SetError("Critical stem " + stem.stableId + " has no clip."); }
                    droppedStems++; continue;
                }
                if (stem.clip.loadState == AudioDataLoadState.Unloaded) stem.clip.LoadAudioData();
                if (stem.clip.loadState != AudioDataLoadState.Loaded)
                { if (stem.critical) missingCriticalStem = true; droppedStems++; continue; }
                var voice = pendingBed.voices[pendingBed.count++]; voice.stem = stem; voice.scheduledAt = pendingBed.scheduledAt;
                float offsetSeconds = stem.entryOffsetBeats * (float)(60d / Mathf.Max(0.001f, section.bpm));
                float phase = stem.clip.length > 0 ? Mathf.Repeat(offsetSeconds / stem.clip.length, 1f) : 0;
                bool frontend = IsFrontendAudioState();
                voice.lease = world.Play(stem.clip, SensoryCategory.Music, null, Vector3.zero, 0, 1,
                    Mathf.Clamp(48 + stem.priorityValue(), 0, 256), true, pendingBed.scheduledAt, phase,
                    ignoreListenerPause: frontend, scheduleOnUnscaledClock: frontend);
                if (!world.Owns(voice.lease)) { if (stem.critical) missingCriticalStem = true; voice.Clear(); pendingBed.count--; droppedStems++; }
            }
            if (missingCriticalStem)
            {
                ReleaseBed(pendingBed); SetError("A critical music stem is not ready for section " + section.stableId + "."); return false;
            }
            if (pendingBed.count == 0)
            {
                ReleaseBed(pendingBed); SetError("No music stem is loaded for section " + section.stableId + "."); return false;
            }
            Record("bed.schedule", pendingRequestId, pendingSource, ActiveSectionId, section.stableId, activeContext,
                transitionId.Length > 0 ? MusicQuantization.Bar : MusicQuantization.Immediate, SafeDspTime(), pendingBed.scheduledAt,
                transitionId.Length > 0 ? "transition=" + transitionId : "initial/fallback");
            return true;
        }

        private void CommitPending(double now)
        {
            if (pendingBed == null || !pendingBed.active || now + 0.0005 < pendingBed.scheduledAt) return;
            if (fadingBed != null && fadingBed.active) ReleaseBed(fadingBed);
            var oldActive = activeBed;
            var oldFading = fadingBed;
            activeBed = pendingBed;
            fadingBed = oldActive;
            pendingBed = oldFading;
            if (pendingBed != null && pendingBed.active) ReleaseBed(pendingBed);
            transportOrigin = activeBed.scheduledAt;
            lastCommitAt = now;
            fadingUntil = now + Crossfade();
            activeContext = winner != null ? winner.context : activeContext;
            Record("bed.commit", pendingRequestId, pendingSource, fadingBed != null && fadingBed.section != null ? fadingBed.section.stableId : string.Empty,
                ActiveSectionId, activeContext, MusicQuantization.Bar, now, transportOrigin, "single active bed with bounded crossfade");
            if (Crossfade() <= 0) ReleaseBed(fadingBed);
        }

        private void UpdateBeds(double now, float globalScale)
        {
            if (previewActive) globalScale = 0;
            if (activeBed != null && activeBed.active)
                UpdateBed(activeBed, now, globalScale);
            if (fadingBed != null && fadingBed.active)
            {
                float gain = FadingGain(now) * globalScale;
                UpdateBed(fadingBed, now, gain);
                if (gain <= 0.0005f) ReleaseBed(fadingBed);
            }
            if (pendingBed != null && pendingBed.active && now < pendingBed.scheduledAt) UpdateBed(pendingBed, now, 0);
        }

        private void UpdateBed(BedState bed, double now, float scale)
        {
            if (world == null || bed == null || !bed.active || bed.section == null) return;
            for (int i = 0; i < bed.count; i++)
            {
                var voice = bed.voices[i];
                if (voice.stem == null) continue;
                if (!world.Owns(voice.lease))
                {
                    if (voice.stem.critical) { ReleaseBed(bed); return; }
                    continue;
                }
                float curve = voice.stem.intensityGain == null ? 1 : Mathf.Clamp01(voice.stem.intensityGain.Evaluate(currentIntensity));
                world.UpdateVoice(voice.lease, Mathf.Clamp01(voice.stem.gain * curve * scale));
            }
        }

        private void ProcessStingers(double now)
        {
            if (currentStinger != null)
            {
                bool owns = world != null && world.Owns(stingerLease);
                if (!owns || now >= currentStingerEnd())
                {
                    if (owns) world.Release(stingerLease);
                    Record("stinger.finish", currentStinger.stableId, currentStingerSource,
                        ActiveSectionId, string.Empty, activeContext, currentStinger.quantization, now, 0, owns ? "completed" : "voice released");
                    currentStinger = null; currentStingerRequestId = currentStingerSource = string.Empty; stingerLease = default; stingerScheduledAt = 0;
                }
                else if (currentStinger.interruptible && !currentStinger.Allows(activeContext) && now < currentStingerScheduledAt)
                { CancelCurrentStinger("context became stale"); }
                else if (currentStinger != null && !currentStinger.interruptible) return;
                else if (currentStinger != null && now >= currentStingerScheduledAt) return;
            }
            int best = -1;
            for (int i = 0; i < stingerRequests.Length; i++)
            {
                var request = stingerRequests[i]; if (!request.active) continue;
                if (request.expiresAt < now) { request.Clear(); continue; }
                if (request.definition == null || !request.definition.Allows(activeContext)) continue;
                if (!string.IsNullOrEmpty(request.definition.requiredSectionId) && request.definition.requiredSectionId != ActiveSectionId) continue;
                if (IsSuppressed(request.definition, now)) { request.Clear(); continue; }
                if (best < 0 || request.priority > stingerRequests[best].priority
                    || request.priority == stingerRequests[best].priority && request.sequence < stingerRequests[best].sequence) best = i;
            }
            if (best < 0) return;
            var selected = stingerRequests[best]; var definition = selected.definition;
            double sectionEnd = NextSectionEnd(now);
            double at = MusicTiming.NextBoundary(now, transportOrigin, ActiveBpm(), ActiveBeatsPerBar(), definition.quantization, Lookahead(), sectionEnd);
            if (world == null || definition.clip == null || definition.clip.loadState != AudioDataLoadState.Loaded)
            {
                if (definition.clip != null && definition.clip.loadState == AudioDataLoadState.Unloaded) definition.clip.LoadAudioData();
                SetError("Stinger " + definition.stableId + " is not ready; request remains queued until expiry."); return;
            }
            int sourcePriority = Mathf.Clamp(256 - definition.priority, 0, 256);
            var lease = world.Play(definition.clip, SensoryCategory.Music, null, Vector3.zero, definition.gain, 1, sourcePriority, false, at);
            if (!world.Owns(lease)) { SetError("Music voice budget rejected stinger " + definition.stableId + "."); return; }
            currentStingerRequestId = selected.id; currentStingerSource = selected.source; currentStinger = definition; stingerLease = lease; stingerScheduledAt = at;
            MarkPlayed(definition.stableId, now, at);
            selected.Clear();
            Record("stinger.schedule", currentStinger.stableId, currentStingerSource, ActiveSectionId, string.Empty, activeContext,
                definition.quantization, now, at, "admitted");
        }

        private bool QueueStinger(MusicStinger definition, string sourceId, AdaptiveMusicContext context, int priority)
        {
            if (definition == null) return RejectRequest("Unknown or missing music stinger.");
            if (definition.clip == null) return RejectRequest("Stinger " + definition.stableId + " has no clip.");
            context = NormalizeContext(context == AdaptiveMusicContext.None ? activeContext : context);
            if (!definition.Allows(context)) return RejectRequest("Stinger " + definition.stableId + " is not eligible for " + context + ".");
            double now = SafeDspTime();
            if (IsSuppressed(definition, now))
            { Record("stinger.suppress", definition.stableId, NormalizeSource(sourceId), ActiveSectionId, string.Empty, context, definition.quantization, now, 0, "cooldown/repetition"); return false; }
            StingerRequestSlot slot = null;
            for (int i = 0; i < stingerRequests.Length; i++) if (!stingerRequests[i].active) { slot = stingerRequests[i]; break; }
            if (slot == null) { SetError("Stinger request queue is full."); return false; }
            slot.active = true; slot.definition = definition; slot.id = definition.stableId + ":" + (++sequence);
            slot.source = NormalizeSource(sourceId); slot.context = context; slot.priority = Mathf.Clamp(priority, 0, 2048); slot.sequence = sequence;
            slot.requestedAt = now; slot.expiresAt = now + Mathf.Max(0.1f, definition.maxQueueAgeSeconds);
            Record("stinger.request", slot.id, slot.source, ActiveSectionId, string.Empty, context, definition.quantization, now, 0, "queued");
            return true;
        }

        private void Outcome(PoliceOutcomeKind outcome)
        {
            AdaptiveMusicContext context = outcome == PoliceOutcomeKind.Escaped ? AdaptiveMusicContext.Escape : AdaptiveMusicContext.Failure;
            RequestContext(context, 0.45f, "pursuit.outcome", ContextPriority(context), 5, MusicQuantization.Bar);
            string id = outcome == PoliceOutcomeKind.Escaped ? "outcome.escaped" : outcome == PoliceOutcomeKind.Arrested ? "outcome.arrested" : "outcome.paid-fine";
            var definition = profile != null ? profile.FindStinger(id) : null;
            if (definition == null && legacyStingers != null)
            {
                int index = outcome == PoliceOutcomeKind.Escaped ? 0 : outcome == PoliceOutcomeKind.Arrested ? 1 : 2;
                definition = legacyStingers[index];
            }
            if (definition != null) QueueStinger(definition, "pursuit.outcome", context, definition.priority);
        }

        private void OnDisable()
        {
            if (overlayRoot != null) overlayRoot.SetActive(false);
            if (pursuit != null) pursuit.OutcomeCommitted -= Outcome;
            ReleaseBed(activeBed); ReleaseBed(pendingBed); ReleaseBed(fadingBed); CancelCurrentStinger("director disabled");
            started = false;
            ClearBridgePlan();
            if (instance == this) instance = null;
        }

        private void OnDestroy()
        {
            if (overlayRoot != null) Destroy(overlayRoot);
            AudioSettings.OnAudioConfigurationChanged -= AudioDeviceChanged;
            if (instance == this) instance = null;
        }

        private void AudioDeviceChanged(bool changed)
        {
            if (!started) return;
            ReanchorAfterDiscontinuity("audio configuration changed");
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused && started) ReanchorAfterDiscontinuity("application resumed");
        }

        private void ReanchorAfterDiscontinuity(string reason, bool reportError = true)
        {
            ReleaseBed(activeBed); ReleaseBed(pendingBed); ReleaseBed(fadingBed); CancelCurrentStinger(reason);
            transportOrigin = SafeDspTime() + Lookahead(); lastDspTime = -1; sawDspClock = false;
            lastCommitAt = double.NegativeInfinity; fadingUntil = 0;
            if (reportError) SetError(reason + "; transport will restart at a safe section boundary.");
            Record("transport.reanchor", string.Empty, "runtime", string.Empty, string.Empty, activeContext, MusicQuantization.Bar, SafeDspTime(), transportOrigin, reason);
        }

        private void ResetTransport()
        {
            ReleaseBed(activeBed); ReleaseBed(pendingBed); ReleaseBed(fadingBed); CancelCurrentStinger("reconfigured");
            started = false; activeBed = pendingBed = fadingBed = null; legacyBuilt = false; ClearBridgePlan();
        }

        private void ClearBridgePlan()
        {
            bridgeTargetSectionId = bridgeSectionId = bridgeTransitionId = string.Empty;
        }

        private bool TryClaimInstance()
        {
            if (instance != null && instance != this)
            {
                // The application owns music across additive world loads. A world
                // director contributes its pursuit events to that persistent owner.
                if (instance.GetComponent<GameFlowRuntime>() != null) instance.SetPursuit(pursuit);
                else Debug.LogError("Only one AdaptiveMusic director may be active. " + name + " was disabled.", this);
                enabled = false;
                return false;
            }
            instance = this;
            return true;
        }

        private void BuildLegacyCompatibility()
        {
            if (legacyBuilt || profile == null) return;
            legacySection = new MusicSection { stableId = "legacy", displayName = "Legacy sensory arrangement", kind = MusicSectionKind.Custom,
                eligibleContexts = AdaptiveMusicContext.All, bpm = profile.bpm, beatsPerBar = profile.beatsPerBar, bars = profile.bars,
                harmonicFamily = "legacy", gridEnabled = true, stems = Array.Empty<MusicStem>() };
            var legacy = profile.stems ?? Array.Empty<SensoryMusicStem>(); var converted = new List<MusicStem>(legacy.Length);
            for (int i = 0; i < legacy.Length; i++)
            {
                var stem = legacy[i]; if (stem == null) continue;
                converted.Add(new MusicStem { stableId = string.IsNullOrWhiteSpace(stem.stableId) || stem.stableId == "legacy-stem" ? "legacy.stem." + i : stem.stableId,
                    displayName = string.IsNullOrWhiteSpace(stem.displayName) ? "Legacy stem " + i : stem.displayName, clip = stem.clip,
                    gain = 1, intensityGain = stem.intensityGain, role = i == 0 ? MusicStemRole.Base : i == 1 ? MusicStemRole.Rhythm : MusicStemRole.Tension,
                    alwaysOn = true, loadPolicy = MusicClipLoadPolicy.Preload });
            }
            legacySection.stems = converted.ToArray();
            legacyStingers = new[]
            {
                LegacyStinger("outcome.escaped", "Escaped", profile.escapedStinger),
                LegacyStinger("outcome.arrested", "Arrested", profile.arrestedStinger),
                LegacyStinger("outcome.paid-fine", "Fine paid", profile.finePaidStinger)
            };
            legacyBuilt = true;
        }

        private static MusicStinger LegacyStinger(string id, string name, AudioClip clip)
        {
            return new MusicStinger { stableId = id, displayName = name, clip = clip, eligibleContexts = AdaptiveMusicContext.All,
                priority = 220, quantization = MusicQuantization.Bar, cooldownSeconds = 1, repetitionSuppressionSeconds = 8,
                maxQueueAgeSeconds = 5, gain = 0.8f, interruptible = true };
        }

        private MusicSection ResolveSection(string id)
        {
            BuildLegacyCompatibility();
            if (profile == null) return null;
            if (profile.HasArrangement) return profile.FindSection(id);
            return string.IsNullOrEmpty(id) || id == "legacy" ? legacySection : null;
        }

        private MusicSection ResolveReadyFallback(MusicSection section)
        {
            MusicSection current = section; string[] visited = new string[16]; int count = 0;
            while (current != null && count < visited.Length)
            {
                for (int i = 0; i < count; i++) if (visited[i] == current.stableId) { SetError("Cyclic music fallback at " + current.stableId + "."); return null; }
                visited[count++] = current.stableId;
                if (HasAuthoringClip(current)) return current;
                if (string.IsNullOrEmpty(current.fallbackSectionId)) { SetError("Section " + current.stableId + " has no ready clip or fallback."); return null; }
                current = ResolveSection(current.fallbackSectionId);
            }
            SetError("Music fallback chain exceeded its bounded depth."); return null;
        }

        private bool HasAuthoringClip(MusicSection section)
        {
            if (section == null || section.stems == null) return false;
            for (int i = 0; i < section.stems.Length; i++) if (section.stems[i] != null && section.stems[i].clip != null) return true;
            return false;
        }

        private string DefaultSectionFor(AdaptiveMusicContext context)
        {
            BuildLegacyCompatibility();
            return profile.HasArrangement ? profile.DefaultSectionFor(context) : "legacy";
        }

        private MusicSection ActiveSection() => activeBed != null && activeBed.active ? activeBed.section : null;
        private float ActiveBpm() => Mathf.Max(1, ActiveSection() != null ? ActiveSection().bpm : profile != null ? profile.bpm : 120);
        private int ActiveBeatsPerBar() => Mathf.Max(1, ActiveSection() != null ? ActiveSection().beatsPerBar : profile != null ? profile.beatsPerBar : 4);
        private double Lookahead() => profile != null && profile.playback != null ? Mathf.Clamp(profile.playback.scheduleLookaheadSeconds, 0.05f, 4f) : 0.2;
        private double Crossfade() => profile != null && profile.playback != null ? Mathf.Max(0, profile.playback.crossfadeSeconds) : 0.35;
        private double MinimumDwell() => profile != null && profile.playback != null ? Mathf.Max(0, profile.playback.minimumDwellSeconds) : 0.4;
        private double NextSectionEnd(double now)
        {
            var section = ActiveSection(); if (section == null) return 0;
            double duration = section.LoopDurationSeconds();
            if (duration <= 0) return 0;
            return transportOrigin + Math.Ceiling((now + Lookahead() - transportOrigin) / duration) * duration;
        }

        private float FadingGain(double now)
        {
            if (fadingBed == null || !fadingBed.active) return 0;
            if (Crossfade() <= 0 || now >= fadingUntil) return 0;
            return Mathf.Clamp01((float)((fadingUntil - now) / Crossfade()));
        }

        private void ReleaseBed(BedState bed)
        {
            if (bed == null) return;
            if (world != null) for (int i = 0; i < bed.count; i++) world.Release(bed.voices[i].lease);
            bed.ClearData();
        }

        private void CancelCurrentStinger(string reason)
        {
            if (currentStinger == null) return;
            if (world != null) world.Release(stingerLease);
            Record("stinger.cancel", currentStinger.stableId, currentStingerSource,
                ActiveSectionId, string.Empty, activeContext, currentStinger.quantization, SafeDspTime(), currentStingerScheduledAt, reason);
            currentStinger = null; currentStingerRequestId = currentStingerSource = string.Empty; stingerLease = default; stingerScheduledAt = 0;
        }

        private double currentStingerScheduledAt => stingerScheduledAt;
        private double currentStingerEnd() => stingerScheduledAt + (currentStinger != null && currentStinger.clip != null ? currentStinger.clip.length : 0);

        private bool IsSuppressed(MusicStinger definition, double now)
        {
            if (definition == null) return true;
            if (currentStinger != null && currentStinger.stableId == definition.stableId) return true;
            for (int i = 0; i < history.Length; i++)
                if (history[i] != null && history[i].id == definition.stableId)
                    return now - history[i].playedAt < Mathf.Max(definition.cooldownSeconds, definition.repetitionSuppressionSeconds);
            return false;
        }

        private void MarkPlayed(string id, double requestedAt, double scheduledAt)
        {
            StingerHistory item = null;
            for (int i = 0; i < history.Length; i++) if (history[i] != null && history[i].id == id) { item = history[i]; break; }
            if (item == null) for (int i = 0; i < history.Length; i++) if (history[i] == null) { item = history[i] = new StingerHistory { id = id }; break; }
            if (item == null)
            {
                int oldest = 0; for (int i = 1; i < history.Length; i++) if (history[i].playedAt < history[oldest].playedAt) oldest = i;
                item = history[oldest]; item.id = id;
            }
            item.requestedAt = requestedAt; item.playedAt = scheduledAt;
        }

        private void AddBedDiagnostics(List<AdaptiveMusicStemSnapshot> destination, BedState bed, double now, float scale)
        {
            if (bed == null || !bed.active || bed.section == null) return;
            for (int i = 0; i < bed.count; i++)
            {
                var voice = bed.voices[i]; if (voice.stem == null) continue;
                string asset = voice.stem.clip == null ? "Missing" : voice.stem.clip.loadState.ToString();
                bool scheduled = now < voice.scheduledAt;
                float curve = voice.stem.intensityGain == null ? 1 : Mathf.Clamp01(voice.stem.intensityGain.Evaluate(currentIntensity));
                destination.Add(new AdaptiveMusicStemSnapshot { sectionId = bed.section.stableId, stemId = voice.stem.stableId,
                    displayName = voice.stem.displayName, role = voice.stem.role, scheduled = scheduled,
                    playing = !scheduled && world != null && world.Owns(voice.lease), critical = voice.stem.critical,
                    targetGain = Mathf.Clamp01(voice.stem.gain * curve * scale), currentGain = Mathf.Clamp01(voice.stem.gain * curve * scale),
                    phase = voice.stem.clip != null && voice.stem.clip.length > 0 ? Mathf.Repeat((float)((now - voice.scheduledAt) / voice.stem.clip.length), 1) : 0,
                    scheduledAt = voice.scheduledAt, assetState = asset });
            }
        }

        private void RebuildSnapshot(double now)
        {
            MusicSection section = ActiveSection();
            double position = section == null || section.LoopDurationSeconds() <= 0 ? 0 : Math.Max(0, now - transportOrigin) % section.LoopDurationSeconds();
            double beatDuration = MusicTiming.BeatDuration(ActiveBpm());
            int beat = section == null ? 0 : (int)Math.Floor(position / beatDuration) % Mathf.Max(1, section.beatsPerBar);
            snapshot = new AdaptiveMusicTransportSnapshot { running = started, paused = AudioListener.pause, usingLegacyArrangement = profile != null && !profile.HasArrangement,
                dspTime = now, transportOrigin = transportOrigin, cuePositionSeconds = position, beat = section == null ? 0 : position / beatDuration,
                beatInBar = beat, bar = section == null ? 0 : (int)Math.Floor(position / (beatDuration * Mathf.Max(1, section.beatsPerBar))),
                intensity = currentIntensity, targetIntensity = targetIntensity, context = activeContext,
                profileId = profile != null ? profile.profileId : string.Empty, activeSectionId = ActiveSectionId,
                pendingSectionId = pendingBed != null && pendingBed.active && pendingBed.section != null ? pendingBed.section.stableId : string.Empty,
                bridgeSectionId = bridgeSectionId, pendingFinalSectionId = bridgeTargetSectionId,
                pendingRequestId = pendingRequestId, pendingSource = pendingSource, selectedTransitionId = lastTransitionId,
                lastError = lastError, activeStemCount = activeBed != null ? activeBed.count : 0,
                scheduledStemCount = pendingBed != null && pendingBed.active ? pendingBed.count : 0, droppedStemCount = droppedStems,
                scheduledAt = pendingBed != null && pendingBed.active ? pendingBed.scheduledAt : 0 };
        }

        private void AddBedDiagnostics(List<AdaptiveMusicStemSnapshot> destination, BedState bed, double now, double scale)
        { AddBedDiagnostics(destination, bed, now, Mathf.Clamp01((float)scale)); }

        private MusicRequestSlot FindOrCreateRequest(string id)
        {
            for (int i = 0; i < requests.Length; i++) if (requests[i].active && requests[i].id == id) return requests[i];
            for (int i = 0; i < requests.Length; i++) if (!requests[i].active) { requests[i].id = id; return requests[i]; }
            SetError("Music request table is full."); return null;
        }

        private bool RejectRequest(string message) { SetError(message); return false; }
        private void SetError(string message)
        {
            if (string.IsNullOrEmpty(message) || message == lastError) return;
            lastError = message; Record("error", string.Empty, "runtime", ActiveSectionId, string.Empty, activeContext, MusicQuantization.Immediate, SafeDspTime(), 0, message);
        }

        private void Record(string eventName, string requestId, string source, string from, string to, AdaptiveMusicContext context,
            MusicQuantization quantization, double requestTime, double scheduledAt, string detail)
        {
            if (trace == null) return;
            trace[traceCursor] = new AdaptiveMusicTraceEntry { dspTime = SafeDspTime(), requestTime = requestTime, scheduledAt = scheduledAt,
                eventName = eventName ?? string.Empty, requestId = requestId ?? string.Empty, source = source ?? string.Empty,
                fromSectionId = from ?? string.Empty, toSectionId = to ?? string.Empty, context = context, quantization = quantization, detail = detail ?? string.Empty };
            traceCursor = (traceCursor + 1) % trace.Length; traceCount = Mathf.Min(traceCount + 1, trace.Length);
        }

        private static string NormalizeSource(string value) => string.IsNullOrWhiteSpace(value) ? "anonymous" : value.Trim();
        private static bool ValidLifetime(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
        private static AdaptiveMusicContext NormalizeContext(AdaptiveMusicContext value)
        {
            if (value == AdaptiveMusicContext.None) return AdaptiveMusicContext.None;
            AdaptiveMusicContext[] ordered = { AdaptiveMusicContext.Escape, AdaptiveMusicContext.Failure, AdaptiveMusicContext.Results,
                AdaptiveMusicContext.Pause, AdaptiveMusicContext.Cinematic, AdaptiveMusicContext.Pursuit, AdaptiveMusicContext.Cooldown,
                AdaptiveMusicContext.Race, AdaptiveMusicContext.Frontend, AdaptiveMusicContext.FreeRoam };
            for (int i = 0; i < ordered.Length; i++) if ((value & ordered[i]) != 0) return ordered[i];
            return AdaptiveMusicContext.FreeRoam;
        }

        private static bool IsFrontendAudioState()
        {
            return GameFlowRuntime.Instance?.AllowsFrontendMusic == true;
        }

        private double SafeDspTime()
        {
            double value = frontendClock ? Time.unscaledTimeAsDouble : AudioSettings.dspTime;
            return double.IsNaN(value) || double.IsInfinity(value) ? 0 : value;
        }
    }

    internal static class MusicStemPriorityExtensions
    {
        public static int priorityValue(this MusicStem stem) => stem != null && stem.critical ? 0 : 8;
    }
}
