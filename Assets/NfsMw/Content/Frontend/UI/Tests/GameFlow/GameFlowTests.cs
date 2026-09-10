using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class GameFlowTests
    {
        private sealed class Session : IGameFlowSession
        {
            public GameFlowState FlowState { get; set; } = GameFlowState.FreeRoam;
            public bool InitializeSucceeds = true, SaveSucceeds = true;
            public int Saves, Commands;
            public bool TryInitializeCareer(string alias, bool create, out string failure)
            { failure = "Invalid career"; return InitializeSucceeds; }
            public bool TryExecute(GameFlowCommand command, out string failure)
            {
                Commands++; failure = string.Empty;
                FlowState = command == GameFlowCommand.Pause ? GameFlowState.Paused : GameFlowState.FreeRoam;
                return true;
            }
            public bool TrySaveForExit(out string failure) { Saves++; failure = "Storage unavailable"; return SaveSucceeds; }
        }
        private sealed class Lease : IGameSceneLease
        {
            public readonly Session World = new Session();
            public IGameFlowSession Session => World;
            public int Activations, Unloads;
            public bool UnloadFails;
            public void Activate() { Activations++; }
            public Task UnloadAsync()
            { Unloads++; return UnloadFails ? Task.FromException(new InvalidOperationException("Unload failed")) : Task.CompletedTask; }
        }
        private sealed class Loader : IGameSceneLoader
        {
            public int Loads;
            public readonly Lease Lease = new Lease();
            public TaskCompletionSource<IGameSceneLease> Pending;
            public bool Fails;
            public Task<IGameSceneLease> LoadAsync(string path, Action<float> progress, CancellationToken token)
            {
                Loads++; progress(0.4f);
                return Fails ? Task.FromException<IGameSceneLease>(new InvalidOperationException("Missing scene"))
                    : Pending?.Task ?? Task.FromResult<IGameSceneLease>(Lease);
            }
        }

        [Test]
        public void BootMustCompleteBeforeCareerCanLoad()
        {
            var loader = new Loader(); var flow = new GameFlow(loader);
            Assert.That(flow.EnterCareerAsync("world", "alias", false).Result.Succeeded, Is.False);
            Assert.That(loader.Loads, Is.Zero);
            flow.CompleteBoot(); flow.CompleteBoot();
            Assert.That(flow.State, Is.EqualTo(GameFlowState.MainMenu));
        }

        [Test]
        public async Task SuccessfulLoadActivatesExactlyOneWorld()
        {
            var loader = new Loader(); var flow = new GameFlow(loader); flow.CompleteBoot();
            Assert.That((await flow.EnterCareerAsync("world", "alias", false)).Succeeded, Is.True);
            Assert.That(loader.Lease.Activations, Is.EqualTo(1));
            Assert.That(flow.State, Is.EqualTo(GameFlowState.FreeRoam)); Assert.That(flow.LoadProgress, Is.EqualTo(1));
            Assert.That(flow.IsBusy, Is.False);
        }

        [Test]
        public async Task RepeatedClicksAndPauseCannotRaceAnInFlightLoad()
        {
            var loader = new Loader { Pending = new TaskCompletionSource<IGameSceneLease>() };
            var flow = new GameFlow(loader); flow.CompleteBoot();
            Task<FlowResult> first = flow.EnterCareerAsync("world", "alias", false);
            Assert.That(flow.State, Is.EqualTo(GameFlowState.SceneTransition));
            Assert.That((await flow.EnterCareerAsync("world", "alias", false)).Succeeded, Is.False);
            Assert.That(flow.Execute(GameFlowCommand.Pause).Succeeded, Is.False);
            Assert.That((await flow.ReturnToMenuAsync()).Succeeded, Is.False);
            Assert.That(loader.Loads, Is.EqualTo(1));
            loader.Pending.SetResult(loader.Lease); Assert.That((await first).Succeeded, Is.True);
        }

        [Test]
        public async Task InvalidCareerRollsBackWithoutActivatingTheScene()
        {
            var loader = new Loader(); loader.Lease.World.InitializeSucceeds = false;
            var flow = new GameFlow(loader); flow.CompleteBoot();
            Assert.That((await flow.EnterCareerAsync("world", "alias", false)).Succeeded, Is.False);
            Assert.That(loader.Lease.Unloads, Is.EqualTo(1)); Assert.That(loader.Lease.Activations, Is.Zero);
            Assert.That(flow.Session, Is.Null); Assert.That(flow.State, Is.EqualTo(GameFlowState.MainMenu));
            Assert.That(flow.Failure, Does.Contain("Invalid career"));
        }

        [Test]
        public async Task CancellationDrainsAndReleasesReturnedSceneBeforeAllowingRetry()
        {
            var loader = new Loader { Pending = new TaskCompletionSource<IGameSceneLease>() };
            var flow = new GameFlow(loader); flow.CompleteBoot();
            using (var cancellation = new CancellationTokenSource())
            {
                var task = flow.EnterCareerAsync("world", "alias", false, cancellation.Token);
                cancellation.Cancel(); Assert.That(flow.IsBusy, Is.True);
                loader.Pending.SetResult(loader.Lease);
                Assert.That((await task).Succeeded, Is.False);
                Assert.That(loader.Lease.Activations, Is.Zero); Assert.That(loader.Lease.Unloads, Is.EqualTo(1));
                Assert.That(flow.State, Is.EqualTo(GameFlowState.MainMenu)); Assert.That(flow.IsBusy, Is.False);
            }
        }

        [Test]
        public async Task CleanupFailureFailsClosedInsteadOfLoadingAnotherWorld()
        {
            var loader = new Loader(); loader.Lease.World.InitializeSucceeds = false; loader.Lease.UnloadFails = true;
            var flow = new GameFlow(loader); flow.CompleteBoot();
            Assert.That((await flow.EnterCareerAsync("world", "alias", false)).Succeeded, Is.False);
            Assert.That(flow.State, Is.EqualTo(GameFlowState.Faulted));
            Assert.That((await flow.EnterCareerAsync("other", "alias", false)).Succeeded, Is.False);
            Assert.That(loader.Loads, Is.EqualTo(1));
        }

        [Test]
        public async Task MissingSceneFailureCanBeRetried()
        {
            var loader = new Loader { Fails = true }; var flow = new GameFlow(loader); flow.CompleteBoot();
            Assert.That((await flow.EnterCareerAsync("world", "alias", false)).Succeeded, Is.False);
            loader.Fails = false;
            Assert.That((await flow.EnterCareerAsync("world", "alias", false)).Succeeded, Is.True);
            Assert.That(flow.Failure, Is.Empty);
        }

        [Test]
        public async Task FailedSavePreservesThePausedWorld()
        {
            var loader = new Loader(); var flow = new GameFlow(loader); flow.CompleteBoot();
            await flow.EnterCareerAsync("world", "alias", false); flow.Execute(GameFlowCommand.Pause);
            loader.Lease.World.SaveSucceeds = false;
            Assert.That((await flow.ReturnToMenuAsync()).Succeeded, Is.False);
            Assert.That(flow.State, Is.EqualTo(GameFlowState.Paused)); Assert.That(loader.Lease.Unloads, Is.Zero);
            Assert.That(flow.Session, Is.SameAs(loader.Lease.World));
        }

        [Test]
        public async Task LeavingSavesBeforeUnloadingAndDoesNotKeepAStaleSession()
        {
            var loader = new Loader(); var flow = new GameFlow(loader); flow.CompleteBoot();
            await flow.EnterCareerAsync("world", "alias", false);
            Assert.That((await flow.ReturnToMenuAsync()).Succeeded, Is.True);
            Assert.That(loader.Lease.World.Saves, Is.EqualTo(1)); Assert.That(loader.Lease.Unloads, Is.EqualTo(1));
            Assert.That(flow.Session, Is.Null); Assert.That(flow.State, Is.EqualTo(GameFlowState.MainMenu));
        }

        [Test]
        public async Task FailedUnloadKeepsTheSessionAvailableForRecovery()
        {
            var loader = new Loader(); var flow = new GameFlow(loader); flow.CompleteBoot();
            await flow.EnterCareerAsync("world", "alias", false); loader.Lease.UnloadFails = true;
            Assert.That((await flow.ReturnToMenuAsync()).Succeeded, Is.False);
            Assert.That(flow.State, Is.EqualTo(GameFlowState.FreeRoam)); Assert.That(flow.Session, Is.Not.Null);
        }

        [Test]
        public async Task BrokenOrReentrantViewsCannotCorruptTransitions()
        {
            int diagnostics = 0; var loader = new Loader();
            var flow = new GameFlow(loader, _ => diagnostics++);
            flow.Changed += () => { throw new InvalidOperationException("View failed"); };
            flow.Changed += () => flow.Execute(GameFlowCommand.Pause);
            flow.CompleteBoot();
            Assert.That((await flow.EnterCareerAsync("world", "alias", false)).Succeeded, Is.True);
            Assert.That(loader.Lease.World.Commands, Is.Zero); Assert.That(diagnostics, Is.GreaterThan(0));
            Assert.That(flow.State, Is.EqualTo(GameFlowState.FreeRoam));
        }

        [Test]
        public async Task BrokenDiagnosticsCannotPreventCleanupOrReleaseTheGate()
        {
            var loader = new Loader(); loader.Lease.World.InitializeSucceeds = false;
            loader.Lease.UnloadFails = true;
            var flow = new GameFlow(loader, _ => throw new InvalidOperationException("Logger failed"));
            flow.Changed += () => throw new InvalidOperationException("View failed");
            flow.CompleteBoot();
            Assert.That((await flow.EnterCareerAsync("world", "alias", false)).Succeeded, Is.False);
            Assert.That(flow.State, Is.EqualTo(GameFlowState.Faulted));
            Assert.That(flow.IsBusy, Is.False);
            Assert.That(loader.Lease.Unloads, Is.EqualTo(1));
        }

        [Test]
        public async Task RacePhaseChangesAreObservedWithoutGivingTheShellRewardOwnership()
        {
            var loader = new Loader(); var flow = new GameFlow(loader); flow.CompleteBoot();
            await flow.EnterCareerAsync("world", "alias", false);
            foreach (GameFlowState state in new[] { GameFlowState.EventLoading, GameFlowState.RaceActive, GameFlowState.Results })
            { loader.Lease.World.FlowState = state; flow.Refresh(); Assert.That(flow.State, Is.EqualTo(state)); }
            Assert.That(loader.Lease.World.Commands, Is.Zero); Assert.That(loader.Lease.World.Saves, Is.Zero);
        }
    }
}
