using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving
{
    public sealed class UnityGameSceneLoader : IGameSceneLoader
    {
        public async Task<IGameSceneLease> LoadAsync(string scenePath, Action<float> progress, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!Application.CanStreamedLevelBeLoaded(scenePath)) throw new InvalidOperationException("Scene is not enabled in Build Profiles: " + scenePath);
            if (SceneManager.GetSceneByPath(scenePath).isLoaded) throw new InvalidOperationException("The requested world is already loaded.");
            AsyncOperation operation = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
            if (operation == null) throw new InvalidOperationException("Unity did not start the scene load.");
            // Unity scene loads cannot be cancelled. Always drain, then roll back;
            // never leave allowSceneActivation=false blocking the operation queue.
            while (!operation.isDone) { progress?.Invoke(operation.progress); await Task.Yield(); }
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            try
            {
                cancellation.ThrowIfCancellationRequested();
                FreeRoamSession session = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                foreach (FreeRoamSession found in root.GetComponentsInChildren<FreeRoamSession>(true))
                {
                    if (session != null) throw new InvalidOperationException("World scene must contain exactly one FreeRoamSession.");
                    session = found;
                }
                if (session == null || !session.isActiveAndEnabled) throw new InvalidOperationException("World session is missing or disabled.");
                session.UseApplicationFlow();
                progress?.Invoke(1);
                return new Lease(scene, session);
            }
            catch (Exception failure)
            {
                try { await Unload(scene); }
                catch (Exception cleanup) { throw new SceneTransitionCleanupException("World cleanup failed. Restart the application before loading another career.", new AggregateException(failure, cleanup)); }
                throw;
            }
        }

        private static async Task Unload(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
            if (operation == null) throw new InvalidOperationException("Unity did not start scene unloading.");
            while (!operation.isDone) await Task.Yield();
        }

        private sealed class Lease : IGameSceneLease
        {
            private readonly Scene scene;
            public IGameFlowSession Session { get; }
            public Lease(Scene loaded, FreeRoamSession session) { scene = loaded; Session = session; }
            public void Activate()
            { if (!SceneManager.SetActiveScene(scene)) throw new InvalidOperationException("World activation failed."); }
            public Task UnloadAsync() => Unload(scene);
        }
    }
}
