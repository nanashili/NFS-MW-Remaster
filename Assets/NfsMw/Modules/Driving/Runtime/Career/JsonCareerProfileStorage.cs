using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Scene-compatible adapter for validated, recoverable career generations.</summary>
    [DisallowMultipleComponent]
    public sealed class JsonCareerProfileStorage : MonoBehaviour, ICareerProfileStorage, ICareerProfilePresence, ICareerProfileCreation
    {
        [SerializeField] private string directoryOverride = string.Empty;
        [SerializeField, Range(2, 32)] private int retainedGenerations = 3;
        private CareerSaveRepository repository;
        private readonly Dictionary<string, string> observed = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly object gate = new object();
        private bool asyncBusy;
        private bool requiresReload;
        public SaveReadResult LastRead { get; private set; }
        public string DirectoryPath => string.IsNullOrWhiteSpace(directoryOverride)
            ? Path.Combine(Application.persistentDataPath, "CareerProfiles") : directoryOverride;
        public bool IsBusy { get { lock (gate) return asyncBusy; } }
        public bool RequiresReload { get { lock (gate) return requiresReload; } }
        public string LastWarning => repository?.LastWarning ?? string.Empty;
        public double LastStorageMilliseconds => repository?.LastMilliseconds ?? 0;

        public void SetDirectory(string directory)
        {
            lock (gate)
            {
                if (asyncBusy) throw new InvalidOperationException("Wait for the current save before switching storage.");
                directoryOverride = directory ?? string.Empty; repository = null; observed.Clear(); LastRead = null; requiresReload = false;
            }
        }
        private CareerSaveRepository Repository => repository ?? (repository = new CareerSaveRepository(DirectoryPath, retainedGenerations));

        public bool TryExists(string slot, out bool exists, out string failure)
        {
            exists = false;
            try { exists = Repository.Exists(slot); failure = string.Empty; return true; }
            catch (Exception exception) when (CareerSaveRepository.IsStorageError(exception)) { failure = exception.Message; return false; }
        }
        public bool TrySave(string slot, string payload, out string failure) => Save(slot, payload, false, out failure);
        public bool TryCreate(string slot, string payload, out string failure) => Save(slot, payload, true, out failure);
        private bool Save(string slot, string payload, bool createOnly, out string failure)
        {
            lock (gate)
            {
                if (asyncBusy) { failure = "A save is still in progress."; return false; }
                if (requiresReload) { failure = "Reload is required before saving this storage session."; return false; }
                try { Commit(Repository, slot, payload, createOnly); failure = string.Empty; return true; }
                catch (SaveCommitUncertainException) { requiresReload = true; throw; }
                catch (SaveException exception) when (exception.Error == SaveError.Conflict || exception.Error == SaveError.UnsupportedVersion)
                { requiresReload = true; failure = exception.Message; return false; }
                catch (Exception exception) when (CareerSaveRepository.IsStorageError(exception)) { failure = exception.Message; return false; }
            }
        }
        private void Commit(CareerSaveRepository backend, string slot, string payload, bool createOnly)
        {
            observed.TryGetValue(slot, out string expected);
            SaveReadResult committed = backend.Commit(slot, payload, expected, createOnly);
            observed[slot] = committed.HeadStamp; LastRead = committed;
        }

        /// <summary>Called on main thread with a detached string; only backend work runs off-thread.</summary>
        public Task<string> SaveAsync(string slot, string payload)
        {
            lock (gate)
            {
                if (asyncBusy) return Task.FromResult("A save is still in progress.");
                if (requiresReload) return Task.FromResult("Reload is required before saving this storage session.");
                CareerSaveRepository backend = Repository;
                asyncBusy = true;
                return Task.Run(() =>
                {
                    try { Commit(backend, slot, payload, false); return string.Empty; }
                    catch (Exception exception)
                    {
                        if (exception is SaveCommitUncertainException || exception is SaveException failure
                            && (failure.Error == SaveError.Conflict || failure.Error == SaveError.UnsupportedVersion)) requiresReload = true;
                        return exception.Message;
                    }
                    finally { lock (gate) asyncBusy = false; }
                });
            }
        }

        public bool TryLoad(string slot, out string payload, out string failure)
        {
            payload = string.Empty;
            lock (gate)
            {
                if (asyncBusy) { failure = "Wait for the active save before loading."; return false; }
                try
                {
                    SaveReadResult read = Repository.Load(slot);
                    string migrated = CareerSaveCodec.Migrate(slot, read.Payload);
                    if (read.Header.schema != CareerProfileData.CurrentVersion)
                    {
                        string recovery = read.Recovery;
                        read = Repository.Commit(slot, migrated, read.HeadStamp);
                        read.Recovery = recovery;
                    }
                    observed[slot] = read.HeadStamp; LastRead = read; requiresReload = false;
                    payload = read.Payload; failure = string.Empty; return true;
                }
                catch (Exception exception) when (CareerSaveRepository.IsStorageError(exception)) { failure = exception.Message; return false; }
            }
        }
        public IReadOnlyList<SaveSlotInfo> InspectSlots() => Repository.Enumerate();
    }
}
