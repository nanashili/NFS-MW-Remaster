using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace NfsMwRemaster.Driving.Tests
{
    /// <summary>
    /// Calls the real screen bridge with a detached visual tree and memory preferences.
    /// Does not initialize GameFlow's scene loader, showroom, render textures, or PlayerPrefs.
    /// </summary>
    public sealed class MostWantedFrontendScreenLifecycleTests
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic;
        private GameObject root;
        private GameFlowRuntime runtime, previousRuntime;
        private GameFlowScreen screen;
        private GameFlowSettings configuration;
        private MostWantedFrontendPreferences preferences, previousPreferences;
        private FrontendMemoryStore store;
        private FrontendTestBackend backend;
        private VisualElement surface, content, footer;
        private int previousReleasedFrame;

        [SetUp]
        public void SetUp()
        {
            previousRuntime = GameFlowRuntime.Instance;
            previousPreferences = (MostWantedFrontendPreferences)StaticField(typeof(MostWantedFrontendPreferences), "runtime").GetValue(null);
            previousReleasedFrame = (int)StaticField(typeof(MapInputFocus), "releasedFrame").GetValue(null);
            var initial = new MostWantedFrontendSettings { qualityLevel = 2 };
            store = new FrontendMemoryStore(); backend = new FrontendTestBackend(initial);
            preferences = new MostWantedFrontendPreferences(initial, store, backend);
            StaticField(typeof(MostWantedFrontendPreferences), "runtime").SetValue(null, preferences);

            // Inactive components avoid Awake/OnEnable and therefore the real application bootstrap.
            root = new GameObject("Frontend screen lifecycle fixture"); root.SetActive(false);
            runtime = root.AddComponent<GameFlowRuntime>();
            configuration = ScriptableObject.CreateInstance<GameFlowSettings>(); runtime.Configure(configuration);
            var flow = new GameFlow(new LifecycleSession()); flow.CompleteBoot();
            SetField(runtime, "<Flow>k__BackingField", flow);
            SetField(runtime, "frontendPreferences", preferences);
            StaticField(typeof(GameFlowRuntime), "<Instance>k__BackingField").SetValue(null, runtime);
            screen = root.AddComponent<GameFlowScreen>();
            SetField(runtime, "screen", screen); SetField(screen, "runtime", runtime);
            surface = new VisualElement(); content = new VisualElement(); footer = new VisualElement();
            surface.Add(content); surface.Add(footer);
            SetField(screen, "surface", surface); SetField(screen, "content", content); SetField(screen, "footer", footer);
            Invoke(screen, "InitializePreferences");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                preferences?.Dispose();
                if (root != null) Object.DestroyImmediate(root);
                if (configuration != null) Object.DestroyImmediate(configuration);
            }
            finally
            {
                StaticField(typeof(GameFlowRuntime), "<Instance>k__BackingField").SetValue(null, previousRuntime);
                StaticField(typeof(MostWantedFrontendPreferences), "runtime").SetValue(null, previousPreferences);
                StaticField(typeof(MapInputFocus), "releasedFrame").SetValue(null, previousReleasedFrame);
            }
        }

        [TestCase("RenderMusic", false)]
        [TestCase("RenderPreferences", true)]
        [TestCase("UpdateMusicNotice", false)]
        [TestCase("InitializePreferences", false)]
        [TestCase("BeginPreferences", true)]
        [TestCase("CancelRebinding", false)]
        [TestCase("CancelPreferences", false)]
        [TestCase("TickPreferences", false)]
        [TestCase("DisposePreferences", false)]
        public void PreviouslyMissingScreenHooksHaveTheirRequiredSignatures(string name, bool takesPage)
        {
            Type[] parameters = takesPage ? new[] { typeof(MostWantedFrontendPage) } : Type.EmptyTypes;
            var method = typeof(GameFlowScreen).GetMethod(name, InstanceFlags, null, parameters, null);
            Assert.That(method, Is.Not.Null, name);
            Assert.That(method.ReturnType, Is.EqualTo(name == "CancelRebinding" ? typeof(bool) : typeof(void)));
        }

        [TestCase(MostWantedFrontendPage.Audio)]
        [TestCase(MostWantedFrontendPage.Video)]
        [TestCase(MostWantedFrontendPage.AdvancedVideo)]
        [TestCase(MostWantedFrontendPage.Gameplay)]
        [TestCase(MostWantedFrontendPage.Player)]
        [TestCase(MostWantedFrontendPage.Controls)]
        public void SettingsPagesConstructWithoutBootstrappingAWorldOrShowroom(MostWantedFrontendPage page)
        {
            screen.Navigation.Reset(page);
            Assert.DoesNotThrow(() => Invoke(screen, "RenderPage", page));
            Assert.That(preferences.IsEditing, Is.True);
            Assert.That(content.Q<Label>("preferences-status"), Is.Not.Null);
            Assert.That(footer.Q<Button>("command-apply"), Is.Not.Null);
            Assert.That(root.GetComponent<MostWantedShowroom>(), Is.Null);
            Assert.That(store.SaveCount, Is.Zero);
        }

        [Test]
        public void InitializingTheScreenUsesTheSharedServiceAndSavedLocalAlias()
        {
            preferences.BeginEdit(); preferences.Draft.playerAlias = "screen_alias";
            Assert.That(preferences.TryApply(out string failure), Is.True, failure);
            Invoke(screen, "InitializePreferences");
            Assert.That(GetField(screen, "preferences"), Is.SameAs(preferences));
            Assert.That(GetField(screen, "alias"), Is.EqualTo("screen_alias"));
        }

        [Test]
        public void ProductionBootWaitsAtTitleThenAliasPromptWithoutLoadingTheWorld()
        {
            SetField(configuration, "requireTitleConfirmation", true);
            var flow = new GameFlow(new LifecycleSession());
            SetField(runtime, "<Flow>k__BackingField", flow);
            Invoke(screen, "SyncApplicationState");
            Assert.That(screen.Navigation.Current.Page, Is.EqualTo(MostWantedFrontendPage.Title));
            Assert.That(flow.State, Is.EqualTo(GameFlowState.Boot));
            Invoke(screen, "ContinueTitle");
            Assert.That(screen.Navigation.Current.Page, Is.EqualTo(MostWantedFrontendPage.AliasPrompt));
            Assert.That(flow.State, Is.EqualTo(GameFlowState.Boot));
            Assert.That(store.SaveCount, Is.Zero);
        }

        [Test]
        public void AdvancedVideoReusesTheParentVideoDraft()
        {
            MakeScreenVisibleWithoutRendering();
            screen.Navigation.Reset(MostWantedFrontendPage.Video);
            Invoke(screen, "BeginPreferences", MostWantedFrontendPage.Video);
            preferences.Draft.width = 1280; preferences.Draft.height = 720;
            var draft = preferences.Draft;
            screen.Navigation.Push(MostWantedFrontendPage.AdvancedVideo);
            Invoke(screen, "BeginPreferences", MostWantedFrontendPage.AdvancedVideo);
            Assert.That(preferences.Draft, Is.SameAs(draft));
            Assert.That(screen.TryHandleBack(), Is.True);
            Assert.That(screen.Navigation.Current.Page, Is.EqualTo(MostWantedFrontendPage.Video));
            Assert.That(preferences.Draft, Is.SameAs(draft), "Back from Advanced Video must preserve the parent draft.");
            Assert.That(preferences.Draft.width, Is.EqualTo(1280));
            Assert.That(store.SaveCount, Is.Zero);
        }

        [Test]
        public void CancelAndScreenDisposalDiscardDraftsWithoutDisposingTheApplicationService()
        {
            Invoke(screen, "BeginPreferences", MostWantedFrontendPage.Audio);
            preferences.Draft.audio.music = .12f;
            Invoke(screen, "CancelPreferences");
            Assert.That(preferences.IsEditing, Is.False);
            Assert.That(preferences.Current.audio.music, Is.EqualTo(new SensoryPreferences().music));
            Invoke(screen, "BeginPreferences", MostWantedFrontendPage.Audio);
            Invoke(screen, "DisposePreferences");
            Assert.That(GetField(screen, "preferences"), Is.Null);
            Assert.DoesNotThrow(() => preferences.BeginEdit());
            Assert.That(store.SaveCount, Is.Zero);
        }

        [Test]
        public void ApplyThroughTheScreenCommitsOnlySupportedSettings()
        {
            screen.Navigation.Reset(MostWantedFrontendPage.Gameplay);
            Invoke(screen, "BeginPreferences", MostWantedFrontendPage.Gameplay);
            preferences.Draft.pauseOnFocusLoss = false; preferences.Draft.audio.flashes = false;
            Invoke(screen, "ApplyPreferences");
            Assert.That(store.SaveCount, Is.EqualTo(1));
            Assert.That(preferences.PauseOnFocusLoss, Is.False);
            Assert.That(backend.Live.audio.flashes, Is.False);
            Assert.That(preferences.IsEditing, Is.True, "The settings page must remain usable after Apply.");
            Assert.That(preferences.IsDirty, Is.False);
        }

        [Test]
        public void ADisplayPreviewLocksUnderlyingSettingsAndRevertNeverSaves()
        {
            StageVideo(); Invoke(screen, "ApplyPreferences");
            Assert.That(preferences.HasPendingVideoChange, Is.True);
            Assert.That(store.SaveCount, Is.Zero);
            Invoke(screen, "RenderPreferenceOverlay");
            Assert.That(content.enabledSelf, Is.False); Assert.That(footer.enabledSelf, Is.False);
            Assert.That(surface.Q<Button>("pref-confirm-video"), Is.Not.Null);
            Assert.That(surface.Q<Button>("pref-revert-video"), Is.Not.Null);
            Invoke(screen, "RevertPreferenceVideo");
            Assert.That(preferences.HasPendingVideoChange, Is.False);
            Assert.That(preferences.Draft.width, Is.EqualTo(1920));
            Assert.That(backend.Live.width, Is.EqualTo(1920));
            Assert.That(store.SaveCount, Is.Zero);
        }

        [Test]
        public void ConfirmingVideoThroughTheScreenPersistsExactlyOnce()
        {
            StageVideo(); Invoke(screen, "ApplyPreferences");
            Invoke(screen, "KeepPreferenceVideo");
            Assert.That(preferences.HasPendingVideoChange, Is.False);
            Assert.That(preferences.Current.width, Is.EqualTo(1280));
            Assert.That(store.SaveCount, Is.EqualTo(1));
            Assert.That(preferences.IsEditing, Is.True);
        }

        [Test]
        public void ScreenTickIsPresentationOnlyButRuntimeTickRevertsHiddenDisplayPreviews()
        {
            StageVideo(); Invoke(screen, "ApplyPreferences");
            Assert.That(screen.IsVisible, Is.False, "The fixture has no UIDocument or GPU-backed showroom.");
            backend.Now += MostWantedFrontendPreferences.VideoConfirmationDuration + 1;
            Invoke(screen, "TickPreferences");
            Assert.That(preferences.HasPendingVideoChange, Is.True, "Screen refresh must not own the transaction clock.");
            Invoke(runtime, "Update");
            Assert.That(preferences.HasPendingVideoChange, Is.False);
            Assert.That(backend.Live.width, Is.EqualTo(1920));
            Assert.That(store.SaveCount, Is.Zero);
        }

        [Test]
        public async Task FocusLossUsesTheCommittedPreferenceInsteadOfTheSettingsAssetDefault()
        {
            Assert.That(configuration.PauseOnFocusLoss, Is.True);
            preferences.BeginEdit(); preferences.Draft.pauseOnFocusLoss = false;
            Assert.That(preferences.TryApply(out string failure), Is.True, failure);
            var entered = await runtime.Flow.EnterCareerAsync("memory-scene", "memory_alias", true);
            Assert.That(entered.Succeeded, Is.True, entered.Message);
            Invoke(runtime, "OnApplicationFocus", false);
            Assert.That(runtime.Flow.State, Is.EqualTo(GameFlowState.FreeRoam));
            preferences.BeginEdit(); preferences.Draft.pauseOnFocusLoss = true;
            Assert.That(preferences.TryApply(out failure), Is.True, failure);
            Invoke(runtime, "OnApplicationFocus", false);
            Assert.That(runtime.Flow.State, Is.EqualTo(GameFlowState.Paused));
        }

        [Test]
        public void ScreenBackCancelsCaptureAndConsumesTheSecondCallbackInTheSameFrame()
        {
            Keyboard previous = Keyboard.current;
            var keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
            try
            {
                MakeScreenVisibleWithoutRendering();
                screen.Navigation.Reset(MostWantedFrontendPage.Options);
                screen.Navigation.Push(MostWantedFrontendPage.Controls);
                Invoke(screen, "BeginPreferences", MostWantedFrontendPage.Controls);
                Assert.That(preferences.BeginRebind(MostWantedFrontendAction.Throttle, 0, out string failure), Is.True, failure);
                Assert.That(screen.TryHandleBack(), Is.True);
                Assert.That(screen.TryHandleBack(), Is.True);
                Assert.That(screen.Navigation.Current.Page, Is.EqualTo(MostWantedFrontendPage.Controls));
                Assert.That(screen.Navigation.Depth, Is.EqualTo(2));
                Assert.That(preferences.IsRebinding, Is.False);
                Assert.That(preferences.IsEditing, Is.True);
                Assert.That(typeof(GameFlowScreen).GetProperty("PreferencesConsumeInput", InstanceFlags).GetValue(screen), Is.EqualTo(true));
                Assert.That(preferences.Draft.keyboard.Get(MostWantedFrontendAction.Throttle), Is.EqualTo(Key.W));
                Invoke(screen, "RenderPage", MostWantedFrontendPage.Controls);
                Assert.That(content.Q<Button>("pref-binding-Throttle-0").enabledSelf, Is.True,
                    "Ending-frame suppression must not leave the freshly rebuilt controls permanently disabled.");
                preferences.Draft.playerAlias = "must_not_commit_on_cancel";
                Invoke(screen, "ApplyPreferences");
                Assert.That(store.SaveCount, Is.Zero);
            }
            finally
            {
                if (keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (previous != null && previous.added) previous.MakeCurrent();
            }
        }

        [Test]
        public void AnEmptyMusicNoticeDoesNotClaimSuccessfulPlayback()
        {
            screen.Navigation.Reset(MostWantedFrontendPage.Music);
            var notice = new Label();
            SetField(screen, "musicNotice", notice);
            SetField(screen, "musicDirector", null);
            Invoke(screen, "UpdateMusicNotice");
            Assert.That(notice.text, Is.EqualTo("No active music service."));
            Assert.That(GetField(screen, "musicPreview"), Is.Null);
            Assert.That(store.SaveCount, Is.Zero);
        }

        private void MakeScreenVisibleWithoutRendering()
        {
            // Supply only UIDocument's managed root. No PanelSettings or GPU-backed panel is created.
            var document = root.AddComponent<UIDocument>();
            var rootField = typeof(UIDocument).GetField("m_RootVisualElement", InstanceFlags)
                ?? typeof(UIDocument).GetField("<rootVisualElement>k__BackingField", InstanceFlags);
            Assert.That(rootField, Is.Not.Null, "The installed UIDocument managed-root seam changed.");
            // Unity 6000.6 stores a UIDocumentRootElement, not a plain VisualElement.
            const BindingFlags constructorFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var constructor = rootField.FieldType.GetConstructor(constructorFlags, null,
                new[] { typeof(UIDocument), typeof(VisualTreeAsset) }, null);
            object[] constructorArguments = { document, null };
            if (constructor == null)
            {
                constructor = rootField.FieldType.GetConstructor(constructorFlags, null, new[] { typeof(UIDocument) }, null);
                constructorArguments = new object[] { document };
            }
            Assert.That(constructor, Is.Not.Null, "The installed UIDocument root constructor changed: " + rootField.FieldType.FullName
                + " Available: " + string.Join("; ", Array.ConvertAll(rootField.FieldType.GetConstructors(constructorFlags), item => item.ToString())));
            var managedRoot = constructor.Invoke(constructorArguments) as VisualElement;
            Assert.That(managedRoot, Is.Not.Null);
            managedRoot.style.display = DisplayStyle.Flex;
            managedRoot.Add(surface);
            rootField.SetValue(document, managedRoot);
            SetField(screen, "document", document);
            SetField(screen, "rendering", true); // Back is real; only its final renderer invocation is suppressed.
            Assert.That(screen.IsVisible, Is.True);
        }

        private void StageVideo()
        {
            screen.Navigation.Reset(MostWantedFrontendPage.Video);
            Invoke(screen, "BeginPreferences", MostWantedFrontendPage.Video);
            preferences.Draft.width = 1280; preferences.Draft.height = 720;
        }

        private static object Invoke(object target, string name, params object[] arguments)
        {
            var method = target.GetType().GetMethod(name, InstanceFlags);
            Assert.That(method, Is.Not.Null, name);
            return method.Invoke(target, arguments);
        }
        private static object GetField(object target, string name) => InstanceField(target.GetType(), name).GetValue(target);
        private static void SetField(object target, string name, object value) => InstanceField(target.GetType(), name).SetValue(target, value);
        private static FieldInfo InstanceField(Type type, string name)
        {
            var field = type.GetField(name, InstanceFlags); Assert.That(field, Is.Not.Null, name); return field;
        }
        private static FieldInfo StaticField(Type type, string name)
        {
            var field = type.GetField(name, StaticFlags); Assert.That(field, Is.Not.Null, name); return field;
        }

        private sealed class LifecycleSession : IGameSceneLoader, IGameSceneLease, IGameFlowSession
        {
            public GameFlowState FlowState { get; private set; } = GameFlowState.FreeRoam;
            public IGameFlowSession Session => this;
            public Task<IGameSceneLease> LoadAsync(string path, Action<float> progress, CancellationToken cancellation)
            { cancellation.ThrowIfCancellationRequested(); return Task.FromResult<IGameSceneLease>(this); }
            public void Activate() { }
            public Task UnloadAsync() => Task.CompletedTask;
            public bool TryInitializeCareer(string alias, bool create, out string failure) { failure = string.Empty; return true; }
            public bool TrySaveForExit(out string failure) { failure = string.Empty; return true; }
            public bool TryExecute(GameFlowCommand command, out string failure)
            {
                failure = string.Empty;
                if (command == GameFlowCommand.Pause) { FlowState = GameFlowState.Paused; return true; }
                if (command == GameFlowCommand.Resume) { FlowState = GameFlowState.FreeRoam; return true; }
                failure = "Unsupported fixture command."; return false;
            }
        }
    }
}
