using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NfsMwRemaster.Driving
{
    [DefaultExecutionOrder(500), DisallowMultipleComponent]
    public sealed class GameFlowRuntime : MonoBehaviour
    {
        [SerializeField] private GameFlowSettings settings;
        private CancellationTokenSource lifetime;
        private CancellationTokenSource loadCancellation;
        private GameFlowScreen screen;
        private AudioListener frontendListener;
        private MostWantedFrontendPreferences frontendPreferences;
        private IGameSceneLoader configuredSceneLoader;
        private bool wasAtLocation;
        private float bootStarted;
        private float preparationStarted;
        private bool frozen;
        private bool ownsSettings;
        private float savedTimeScale;
        private bool savedAudioPause;
        private CursorLockMode savedCursorLock;
        private bool savedCursorVisible;
        private GameFlowState previousState;
        private Task<FlowResult> uiOperation;
        public static GameFlowRuntime Instance { get; private set; }
        public GameFlow Flow { get; private set; }
        public GameFlowSettings Settings => settings;
        public MostWantedFrontendPreferences Preferences => frontendPreferences;
        public FreeRoamSession WorldSession => Flow?.Session as FreeRoamSession;
        public bool AllowsWorldInput => Flow != null && !Flow.IsBusy
            && WorldSession?.State != FreeRoamState.Location
            && (Flow.State == GameFlowState.FreeRoam || Flow.State == GameFlowState.RaceActive);
        public bool CanCancelLoad => loadCancellation != null && !loadCancellation.IsCancellationRequested;
        public bool AllowsFrontendMusic => Flow != null && (Flow.State == GameFlowState.MainMenu
            || WorldSession?.State == FreeRoamState.Location);
        public void Configure(GameFlowSettings configuration) { settings = configuration; }

        /// <summary>Inject application boundaries before activation, including isolated frontend test fixtures.</summary>
        public void Configure(GameFlowSettings configuration, IGameSceneLoader sceneLoader,
            MostWantedFrontendPreferences preferenceService)
        {
            if (Flow != null) throw new InvalidOperationException("Configure application services before activating the GameFlowRuntime.");
            settings = configuration;
            configuredSceneLoader = sceneLoader ?? throw new ArgumentNullException(nameof(sceneLoader));
            frontendPreferences = preferenceService ?? throw new ArgumentNullException(nameof(preferenceService));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Instance = null; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this; DontDestroyOnLoad(gameObject);
            if (settings == null) { settings = ScriptableObject.CreateInstance<GameFlowSettings>(); ownsSettings = true; }
            lifetime = new CancellationTokenSource();
            Flow = new GameFlow(configuredSceneLoader ?? new UnityGameSceneLoader(), Debug.LogException);
            Flow.Changed += ApplyState;
            InitializeAudio();
            frontendPreferences ??= MostWantedFrontendPreferences.Initialize(settings);
            screen = gameObject.AddComponent<GameFlowScreen>(); screen.Initialize(this);
            bootStarted = Time.unscaledTime; ApplyState();
        }

        private void InitializeAudio()
        {
            var content = settings.FrontendContent;
            if (content?.soundtrack != null && AdaptiveMusic.Instance == null)
            {
                var audio = GetComponent<SensoryAudioWorld>() ?? gameObject.AddComponent<SensoryAudioWorld>();
                audio.Configure(null, null, content.audioMix);
                var music = GetComponent<AdaptiveMusic>() ?? gameObject.AddComponent<AdaptiveMusic>();
                music.Configure(audio, content.soundtrack);
            }
            frontendListener = gameObject.AddComponent<AudioListener>();
        }

        public async Task<FlowResult> EnterCareerAsync(string alias, bool create)
        {
            if (Flow == null || Flow.IsBusy || Flow.State != GameFlowState.MainMenu) return FlowResult.Failure("The main menu is not ready.");
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                loadCancellation = cancellation;
                cancellation.CancelAfter(TimeSpan.FromSeconds(settings.SceneLoadTimeoutSeconds));
                try { return await Flow.EnterCareerAsync(settings.WorldScenePath, alias, create, cancellation.Token); }
                finally { loadCancellation = null; }
            }
        }

        public void Queue(Task<FlowResult> operation) { uiOperation = operation; }
        public void CancelLoading() { loadCancellation?.Cancel(); }

        private void Update()
        {
            if (Instance != this || Flow == null) return;
            // Display rollback and rebind deadlines must keep ticking while the menu is hidden.
            frontendPreferences?.Tick();
            if (uiOperation != null && uiOperation.IsCompleted)
            {
                if (uiOperation.IsFaulted) Debug.LogException(uiOperation.Exception);
                uiOperation = null;
            }
            if (Flow.State == GameFlowState.Boot && !settings.RequireTitleConfirmation
                && Time.unscaledTime - bootStarted >= settings.BootSeconds) Flow.CompleteBoot();
            Flow.Refresh();
            if (wasAtLocation != (WorldSession?.State == FreeRoamState.Location)) ApplyState();
            if (Flow.State == GameFlowState.EventLoading && Time.unscaledTime - preparationStarted >= settings.EventPreparationSeconds)
                Flow.Execute(GameFlowCommand.ActivateEvent);
            bool back = Keyboard.current?.escapeKey.wasPressedThisFrame == true || Gamepad.current?.startButton.wasPressedThisFrame == true;
            if (back && screen != null && screen.IsVisible)
            {
                screen.TryHandleBack();
            }
            else if (back && !MapInputFocus.Captured)
            {
                if (Flow.State == GameFlowState.Paused) Flow.Execute(GameFlowCommand.Resume);
                else if (Flow.State == GameFlowState.Results || WorldSession?.State == FreeRoamState.Location) Flow.Execute(GameFlowCommand.Continue);
                else if (AllowsWorldInput) Flow.Execute(GameFlowCommand.Pause);
            }
        }

        private void ApplyState()
        {
            if (frontendListener != null)
            {
                bool hasWorldListener = false;
                foreach (var candidate in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                    if (candidate != frontendListener && candidate.isActiveAndEnabled) { hasWorldListener = true; break; }
                frontendListener.enabled = !hasWorldListener;
            }
            if (Flow.State == GameFlowState.EventLoading && previousState != GameFlowState.EventLoading) preparationStarted = Time.unscaledTime;
            previousState = Flow.State;
            wasAtLocation = WorldSession?.State == FreeRoamState.Location;
            bool shouldFreeze = wasAtLocation || (Flow.State != GameFlowState.FreeRoam && Flow.State != GameFlowState.RaceActive);
            // Frontend state still freezes the simulation and listener. The frontend music
            // transport explicitly opts into Unity's pause exemption for its own pooled voices.
            bool shouldPauseAudio = shouldFreeze;
            if (shouldFreeze && !frozen)
            {
                savedTimeScale = Time.timeScale; savedAudioPause = AudioListener.pause;
                savedCursorLock = Cursor.lockState; savedCursorVisible = Cursor.visible;
                frozen = true; Time.timeScale = 0; AudioListener.pause = shouldPauseAudio;
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            }
            else if (shouldFreeze && frozen) AudioListener.pause = shouldPauseAudio;
            else if (!shouldFreeze) ReleaseSimulation();
            screen?.Render();
        }

        private void ReleaseSimulation()
        {
            if (!frozen) return;
            Time.timeScale = savedTimeScale; AudioListener.pause = savedAudioPause;
            Cursor.lockState = savedCursorLock; Cursor.visible = savedCursorVisible; frozen = false;
        }
        private void OnApplicationFocus(bool focused)
        { if (!focused && (frontendPreferences?.PauseOnFocusLoss ?? settings?.PauseOnFocusLoss ?? true) && AllowsWorldInput) Flow.Execute(GameFlowCommand.Pause); }
        private void OnApplicationPause(bool paused) { if (paused && AllowsWorldInput) Flow.Execute(GameFlowCommand.Pause); }
        private void OnDisable() { if (Instance == this) ReleaseSimulation(); }
        private void OnDestroy()
        {
            if (Instance != this) return;
            lifetime?.Cancel(); lifetime?.Dispose();
            if (Flow != null) Flow.Changed -= ApplyState;
            frontendPreferences?.Dispose();
            ReleaseSimulation(); Instance = null;
            if (ownsSettings) Destroy(settings);
        }
    }
}
