using System;
using System.Threading;
using System.Threading.Tasks;

namespace NfsMwRemaster.Driving
{
    public enum GameFlowState { Boot, MainMenu, SceneTransition, FreeRoam, EventLoading, RaceActive, Results, Paused, Faulted }
    public enum GameFlowCommand { Pause, Resume, Restart, Continue, Save, ActivateEvent }

    public readonly struct FlowResult
    {
        public bool Succeeded { get; }
        public string Message { get; }
        private FlowResult(bool succeeded, string message) { Succeeded = succeeded; Message = message ?? string.Empty; }
        public static FlowResult Success() => new FlowResult(true, string.Empty);
        public static FlowResult Failure(string message) => new FlowResult(false, message);
    }

    public interface IGameFlowSession
    {
        GameFlowState FlowState { get; }
        bool TryInitializeCareer(string alias, bool create, out string failure);
        bool TryExecute(GameFlowCommand command, out string failure);
        bool TrySaveForExit(out string failure);
    }

    public interface IGameSceneLease
    {
        IGameFlowSession Session { get; }
        void Activate();
        Task UnloadAsync();
    }

    public interface IGameSceneLoader
    {
        // On failure/cancellation the adapter must drain the engine operation and
        // release any partially loaded scene before completing this task.
        Task<IGameSceneLease> LoadAsync(string scenePath, Action<float> progress, CancellationToken cancellation);
    }

    public sealed class SceneTransitionCleanupException : Exception
    {
        public SceneTransitionCleanupException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Application flow. All calls belong to the Unity/main synchronization context.
    /// No async-void commands, no concurrent scene operations, no reward ownership.</summary>
    public sealed class GameFlow
    {
        private readonly IGameSceneLoader loader;
        private readonly Action<Exception> report;
        private IGameSceneLease world;
        private bool busy;
        private bool publishing;
        public GameFlowState State { get; private set; } = GameFlowState.Boot;
        public bool IsBusy => busy;
        public string Failure { get; private set; } = string.Empty;
        public float LoadProgress { get; private set; }
        public IGameFlowSession Session => world?.Session;
        public event Action Changed;

        public GameFlow(IGameSceneLoader scenes, Action<Exception> diagnostics = null)
        { loader = scenes ?? throw new ArgumentNullException(nameof(scenes)); report = diagnostics; }

        public void CompleteBoot()
        { if (State == GameFlowState.Boot && !IsBusy && !publishing) SetState(GameFlowState.MainMenu); }

        public async Task<FlowResult> EnterCareerAsync(string scenePath, string alias, bool create, CancellationToken cancellation = default)
        {
            if (IsBusy || publishing || State != GameFlowState.MainMenu) return FlowResult.Failure("A career can only be loaded from the main menu.");
            if (string.IsNullOrWhiteSpace(scenePath) || string.IsNullOrWhiteSpace(alias)) return Fail("A scene and alias are required.");
            busy = true; Failure = string.Empty; LoadProgress = 0; SetState(GameFlowState.SceneTransition);
            IGameSceneLease candidate = null;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                candidate = await loader.LoadAsync(scenePath, value => LoadProgress = Math.Max(0, Math.Min(1, value)), cancellation);
                cancellation.ThrowIfCancellationRequested();
                if (candidate?.Session == null) throw new InvalidOperationException("The loaded scene has no game session.");
                if (!candidate.Session.TryInitializeCareer(alias.Trim(), create, out string failure)) throw new InvalidOperationException(failure);
                candidate.Activate(); world = candidate; candidate = null;
                LoadProgress = 1; SetState(world.Session.FlowState);
                return FlowResult.Success();
            }
            catch (Exception exception)
            {
                bool cleanupFailed = exception is SceneTransitionCleanupException;
                Failure = exception is OperationCanceledException ? "Loading cancelled. No world was activated." : exception.Message;
                if (candidate != null)
                {
                    try { await candidate.UnloadAsync(); }
                    catch (Exception cleanup) { cleanupFailed = true; Failure += " Cleanup failed: " + cleanup.Message; Report(cleanup); }
                }
                SetState(cleanupFailed ? GameFlowState.Faulted : GameFlowState.MainMenu);
                return FlowResult.Failure(Failure);
            }
            finally { busy = false; Publish(); }
        }

        public FlowResult Execute(GameFlowCommand command)
        {
            if (IsBusy || publishing || world == null) return FlowResult.Failure("The world is not ready.");
            busy = true;
            try
            {
                if (!world.Session.TryExecute(command, out string failure)) return Fail(failure);
                Failure = string.Empty; SetState(world.Session.FlowState); return FlowResult.Success();
            }
            catch (Exception exception) { Report(exception); return Fail(exception.Message); }
            finally { busy = false; Publish(); }
        }

        public async Task<FlowResult> ReturnToMenuAsync()
        {
            if (IsBusy || publishing || world == null) return FlowResult.Failure("The world is not ready.");
            busy = true;
            try
            {
                // Validate and save before any scene or activity is discarded.
                if (!world.Session.TrySaveForExit(out string failure)) return Fail(failure);
                Failure = string.Empty; SetState(GameFlowState.SceneTransition);
                await world.UnloadAsync(); world = null; LoadProgress = 0;
                SetState(GameFlowState.MainMenu); return FlowResult.Success();
            }
            catch (Exception exception)
            { Report(exception); SetState(world?.Session.FlowState ?? GameFlowState.MainMenu); return Fail(exception.Message); }
            finally { busy = false; Publish(); }
        }

        public void Refresh()
        { if (!IsBusy && !publishing && world != null) SetState(world.Session.FlowState); }

        private void Report(Exception exception)
        {
            // Diagnostics must never prevent rollback or release of the command gate.
            try { report?.Invoke(exception); } catch (Exception) { }
        }

        private FlowResult Fail(string message) { Failure = message; Publish(); return FlowResult.Failure(message); }
        private void SetState(GameFlowState state) { if (State == state) return; State = state; Publish(); }
        private void Publish()
        {
            if (publishing || Changed == null) return;
            publishing = true;
            try
            {
                foreach (Action subscriber in Changed.GetInvocationList())
                    try { subscriber(); } catch (Exception exception) { Report(exception); }
            }
            finally { publishing = false; }
        }
    }
}
