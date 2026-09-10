using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Explicit opt-in test-scene bootstrap. Runs the production frontend against a small
    /// fixture and memory-backed profiles/preferences, without loading or changing career saves.
    /// </summary>
    [DefaultExecutionOrder(-10000), DisallowMultipleComponent]
    [AddComponentMenu("NFS MW Remaster/Testing/Frontend Test Driver")]
    public sealed class MostWantedFrontendTestDriver : MonoBehaviour
    {
        [SerializeField] private GameFlowSettings settings;
        [SerializeField] private MostWantedFrontendPage entryPage = MostWantedFrontendPage.MainMenu;
        [SerializeField] private GameObject fixturePrefab;
        [SerializeField] private bool showTestBar = true;
        private GameFlowRuntime application;
        private GameObject applicationRoot;
        private MostWantedFrontendPreferences preferences;
        private MostWantedFrontendUnityBackend preferenceBackend;
        private MostWantedFrontendSettings originalSettings;
        private TestSceneLoader loader;
        private CancellationTokenSource lifetime;
        private Task operation;
        private bool restored;
        private string status = "Preparing frontend test scene…";

        public static readonly MostWantedFrontendPage[] Pages = Enum.GetValues(typeof(MostWantedFrontendPage))
            .Cast<MostWantedFrontendPage>()
            .Where(page => page != MostWantedFrontendPage.Loading && page != MostWantedFrontendPage.Results
                && page != MostWantedFrontendPage.Faulted).ToArray();
        public MostWantedFrontendPage EntryPage => entryPage;
        public GameFlowRuntime ApplicationRuntime => application;
        public bool IsReady => application != null && operation != null && operation.IsCompletedSuccessfully;
        public string Status => status;

        public void Configure(GameFlowSettings configuration, MostWantedFrontendPage page, GameObject fixture)
        {
            if (application != null) throw new InvalidOperationException("Configure the frontend test before starting it.");
            settings = configuration; entryPage = page; fixturePrefab = fixture;
        }

        private void Awake()
        {
            if (GameFlowRuntime.Instance != null)
            {
                status = "Another game application is already running. Stop Play Mode, then open this test scene directly.";
                Debug.LogWarning(status, this);
                return;
            }
            if (settings == null || fixturePrefab == null)
            {
                status = "Test scene settings or fixture are missing. Rebuild the frontend test scenes.";
                Debug.LogError(status, this);
                return;
            }
            lifetime = new CancellationTokenSource();
            preferenceBackend = new MostWantedFrontendUnityBackend();
            originalSettings = preferenceBackend.Capture(new MostWantedFrontendSettings
            { playerAlias = settings.DefaultAlias, pauseOnFocusLoss = settings.PauseOnFocusLoss });
            preferences = new MostWantedFrontendPreferences(originalSettings,
                new MemoryPreferenceStore(), preferenceBackend);
            var storage = GetComponent<MostWantedFrontendTestStorage>() ?? gameObject.AddComponent<MostWantedFrontendTestStorage>();
            loader = new TestSceneLoader(this, fixturePrefab, storage);
            applicationRoot = new GameObject("Frontend test application — memory-backed");
            applicationRoot.SetActive(false);
            application = applicationRoot.AddComponent<GameFlowRuntime>();
            application.Configure(settings, loader, preferences);
            applicationRoot.SetActive(true);
        }

        private void Start()
        {
            if (application != null) RequestPage(entryPage);
        }

        public void RequestPage(MostWantedFrontendPage page)
        {
            if (application == null || lifetime == null || lifetime.IsCancellationRequested) return;
            if (operation != null && !operation.IsCompleted) return;
            if (!Pages.Contains(page)) { status = "This page requires a real loading, result or failure transition."; return; }
            operation = SelectPageAsync(page, lifetime.Token);
        }

        private async Task SelectPageAsync(MostWantedFrontendPage page, CancellationToken cancellation)
        {
            try
            {
                double deadline = Time.realtimeSinceStartupAsDouble + 30;
                while (application.Flow.State == GameFlowState.Boot || application.Flow.IsBusy)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Frontend test startup timed out.");
                    await Task.Yield();
                }
                cancellation.ThrowIfCancellationRequested();
                preferences.Cancel();
                var screen = application.GetComponent<GameFlowScreen>();
                screen.DismissConfirmation();
                if (!NeedsWorld(page))
                {
                    if (application.Flow.Session != null)
                    {
                        var leave = await application.Flow.ReturnToMenuAsync();
                        if (!leave.Succeeded) throw new InvalidOperationException(leave.Message);
                    }
                    cancellation.ThrowIfCancellationRequested();
                    screen.Navigation.Reset(MostWantedFrontendPage.MainMenu);
                    screen.Render();
                    if (page != MostWantedFrontendPage.MainMenu) screen.OpenPage(page);
                }
                else
                {
                    if (application.WorldSession == null)
                    {
                        var storage = GetComponent<MostWantedFrontendTestStorage>();
                        if (!storage.TryExists(settings.DefaultAlias, out bool exists, out string reason))
                            throw new InvalidOperationException(reason);
                        var enter = await application.EnterCareerAsync(settings.DefaultAlias, !exists);
                        if (!enter.Succeeded) throw new InvalidOperationException(enter.Message);
                    }
                    cancellation.ThrowIfCancellationRequested();
                    var session = application.WorldSession;
                    if (session == null) throw new InvalidOperationException("The test loader did not publish a FreeRoamSession.");
                    if (application.Flow.State == GameFlowState.Paused)
                    {
                        var resume = application.Flow.Execute(GameFlowCommand.Resume);
                        if (!resume.Succeeded) throw new InvalidOperationException(resume.Message);
                    }
                    if (session.State == FreeRoamState.Location)
                    {
                        var leave = application.Flow.Execute(GameFlowCommand.Continue);
                        if (!leave.Succeeded) throw new InvalidOperationException(leave.Message);
                    }
                    if (page == MostWantedFrontendPage.Pause)
                    {
                        var pause = application.Flow.Execute(GameFlowCommand.Pause);
                        if (!pause.Succeeded) throw new InvalidOperationException(pause.Message);
                    }
                    else
                    {
                        bool shop = page == MostWantedFrontendPage.Customize || page == MostWantedFrontendPage.Paint
                            || page == MostWantedFrontendPage.Cart || page == MostWantedFrontendPage.Showcase;
                        var kind = shop ? WorldLocationKind.BodyShop : WorldLocationKind.Safehouse;
                        var location = session.Locations.FirstOrDefault(item => item != null && item.Kind == kind);
                        if (location == null) throw new InvalidOperationException("The test fixture is missing its " + kind + ".");
                        var vehicle = session.GetComponent<VehicleController>();
                        vehicle.Body.position = location.Position;
                        vehicle.transform.position = location.Position;
                        vehicle.NotifyPoseReset();
                        Physics.SyncTransforms();
                        if (!session.TryEnter(location, out string failure)) throw new InvalidOperationException(failure);
                        application.Flow.Refresh();
                        // The application observes the location even though its high-level state remains FreeRoam.
                        await Task.Yield();
                        cancellation.ThrowIfCancellationRequested();
                        screen.Render();
                        if (screen.Navigation.Current.Page != page) screen.OpenPage(page);
                    }
                }
                entryPage = page;
                status = "Test data only · profiles and preferences stay in memory · F6/F7 switch page · F8 hides this bar";
            }
            catch (OperationCanceledException) { status = "Frontend test cancelled."; }
            catch (Exception exception)
            {
                status = "Frontend test failed: " + exception.Message;
                Debug.LogException(exception, this);
                throw;
            }
        }

        public static bool NeedsWorld(MostWantedFrontendPage page) => page == MostWantedFrontendPage.Safehouse
            || page == MostWantedFrontendPage.Blacklist || page == MostWantedFrontendPage.RivalBio
            || page == MostWantedFrontendPage.RivalCar || page == MostWantedFrontendPage.RaceEvents
            || page == MostWantedFrontendPage.Milestones || page == MostWantedFrontendPage.Bounty
            || page == MostWantedFrontendPage.Garage || page == MostWantedFrontendPage.Customize
            || page == MostWantedFrontendPage.Paint || page == MostWantedFrontendPage.Cart
            || page == MostWantedFrontendPage.Showcase || page == MostWantedFrontendPage.Pause
            || page == MostWantedFrontendPage.Music;

        private void Update()
        {
            // Observe task exceptions even if the user does not interact with the test bar.
            if (operation?.IsFaulted == true) _ = operation.Exception;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f8Key.wasPressedThisFrame) showTestBar = !showTestBar;
            if (preferences?.RebindingConsumesInput == true || preferences?.HasPendingVideoChange == true) return;
            if (keyboard.f6Key.wasPressedThisFrame) MovePage(-1);
            if (keyboard.f7Key.wasPressedThisFrame) MovePage(1);
        }

        private void MovePage(int delta)
        {
            int index = Array.IndexOf(Pages, entryPage);
            RequestPage(Pages[(Math.Max(0, index) + delta + Pages.Length) % Pages.Length]);
        }

        private void OnGUI()
        {
            if (!showTestBar) return;
            GUILayout.BeginArea(new Rect(8, 6, Mathf.Max(320, Screen.width - 16), 62), GUI.skin.box);
            GUILayout.BeginHorizontal();
            bool enabled = GUI.enabled;
            GUI.enabled = application != null && (operation == null || operation.IsCompleted)
                && preferences?.RebindingConsumesInput != true && preferences?.HasPendingVideoChange != true;
            if (GUILayout.Button("<", GUILayout.Width(36))) MovePage(-1);
            GUILayout.Label("UI TEST  /  " + entryPage, GUILayout.Width(235));
            if (GUILayout.Button(">", GUILayout.Width(36))) MovePage(1);
            GUI.enabled = enabled;
            GUILayout.Label(status);
            GUILayout.EndHorizontal();
            GUILayout.Label("Live display/audio/quality changes are restored when this test exits. This is not a driving scene.");
            GUILayout.EndArea();
        }

        private void OnDisable() => ShutDown();
        private void OnDestroy() => ShutDown();

        private void ShutDown()
        {
            if (restored) return;
            if (applicationRoot != null) applicationRoot.SetActive(false);
            lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null;
            preferences?.Cancel();
            if (!restored && preferenceBackend != null && originalSettings != null)
            {
                restored = true;
                try
                {
                    var current = preferenceBackend.Capture(originalSettings);
                    if (!preferenceBackend.TryApply(current, originalSettings, out string failure))
                        Debug.LogWarning("Frontend test settings restoration: " + failure);
                }
                catch (Exception exception) { Debug.LogWarning("Frontend test settings restoration: " + exception.Message); }
            }
            loader?.Dispose();
            if (applicationRoot != null) Destroy(applicationRoot);
            preferences?.Dispose();
            restored = true;
        }

        private sealed class MemoryPreferenceStore : IMostWantedFrontendPreferenceStore
        {
            private MostWantedFrontendSettings saved;
            public bool TryLoad(MostWantedFrontendSettings defaults, out MostWantedFrontendSettings value, out string failure)
            { value = (saved ?? defaults).Clone(); failure = string.Empty; return true; }
            public bool TrySave(MostWantedFrontendSettings value, out string failure)
            {
                if (!value.TryValidate(out failure)) return false;
                saved = value.Clone(); return true;
            }
        }

        private sealed class TestSceneLoader : IGameSceneLoader, IDisposable
        {
            private readonly MostWantedFrontendTestDriver owner;
            private readonly GameObject prefab;
            private readonly MostWantedFrontendTestStorage storage;
            private GameObject fixture;
            public TestSceneLoader(MostWantedFrontendTestDriver driver, GameObject asset, MostWantedFrontendTestStorage memory)
            { owner = driver; prefab = asset; storage = memory; }

            public Task<IGameSceneLease> LoadAsync(string scenePath, Action<float> progress, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                if (fixture != null) throw new InvalidOperationException("A frontend fixture is already loaded.");
                if (prefab.activeSelf) throw new InvalidOperationException("Frontend fixture prefabs must be saved inactive.");
                fixture = Instantiate(prefab, owner.transform);
                fixture.name = "Frontend test fixture — no disk saves";
                try
                {
                    var sessions = fixture.GetComponentsInChildren<FreeRoamSession>(true);
                    if (sessions.Length != 1) throw new InvalidOperationException("The fixture must contain exactly one FreeRoamSession.");
                    foreach (var profile in fixture.GetComponentsInChildren<CareerProfileSystem>(true))
                    {
                        profile.SetStorage(storage);
                        profile.ConfigureAutomaticPersistence(false, false, false);
                    }
                    foreach (var body in fixture.GetComponentsInChildren<Rigidbody>(true))
                    {
                        body.useGravity = false;
                        body.detectCollisions = false;
                        body.constraints = RigidbodyConstraints.FreezeAll;
                    }
                    fixture.SetActive(true);
                    sessions[0].UseApplicationFlow();
                    progress?.Invoke(1);
                    return Task.FromResult<IGameSceneLease>(new Lease(this, sessions[0]));
                }
                catch { Dispose(); throw; }
            }

            public void Dispose()
            {
                if (fixture != null) { fixture.SetActive(false); Destroy(fixture); fixture = null; }
            }

            private sealed class Lease : IGameSceneLease
            {
                private readonly TestSceneLoader loader;
                public IGameFlowSession Session { get; }
                public Lease(TestSceneLoader owner, FreeRoamSession session) { loader = owner; Session = session; }
                public void Activate() { }
                public Task UnloadAsync() { loader.Dispose(); return Task.CompletedTask; }
            }
        }
    }
}
