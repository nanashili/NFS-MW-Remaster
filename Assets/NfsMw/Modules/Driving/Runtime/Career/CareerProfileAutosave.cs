using System;
using System.Threading.Tasks;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum CareerSaveReason { Periodic, Checkpoint, Manual, CriticalProgression, Suspend }

    public sealed partial class CareerProfileSystem
    {
        private CareerAutosavePolicy autosave = new CareerAutosavePolicy();
        private Func<bool> safePoint;
        private Task<string> pendingSave;
        private CareerProfileData pendingSnapshot;
        private string pendingJson, lastCaptured;
        private double nextInspection;
        private bool restoreFaulted;
        private CareerSaveReason queuedReason;
        public CareerSaveReason QueuedReason => queuedReason;
        public bool SaveInProgress => policeSettlementInProgress || pendingSave != null || storage is JsonCareerProfileStorage local && local.IsBusy;
        public bool SaveDirty => autosave.Dirty;
        public long DirtyRevision => autosave.Revision;
        public long CommittedRevision => autosave.SavedRevision;
        public string SaveStatus { get; private set; } = "Idle";
        public string LastSaveFailure { get; private set; } = string.Empty;
        public string LastRecovery { get; private set; } = string.Empty;
        public double LastSnapshotMilliseconds { get; private set; }

        /// <summary>Gameplay owns safe-point policy. Null disables periodic autosaving.</summary>
        public void ConfigureAutosave(Func<bool> canCapture) { safePoint = canCapture; }
        public void MarkPersistentStateChanged() { RequestSave(CareerSaveReason.Periodic); }
        /// <summary>Queue/coalesce a request. Completion is reported by ProfileSaved, not this method.</summary>
        public void RequestSave(CareerSaveReason reason)
        {
            if (!Enum.IsDefined(typeof(CareerSaveReason), reason)) throw new ArgumentOutOfRangeException(nameof(reason));
            autosave.Changed(Time.realtimeSinceStartupAsDouble);
            if (reason > queuedReason) queuedReason = reason;
            if (reason >= CareerSaveReason.Manual) nextInspection = 0;
            if (!SaveInProgress) SaveStatus = "Queued";
        }
        private void ResetAutosave()
        { autosave = new CareerAutosavePolicy(); lastCaptured = null; nextInspection = 0; queuedReason = CareerSaveReason.Periodic; }
        private void SavedSnapshot(string json)
        { lastCaptured = json; SaveStatus = "Committed"; LastSaveFailure = string.Empty; }

        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (pendingSave != null)
            {
                if (!pendingSave.IsCompleted) return;
                string failure = pendingSave.IsFaulted ? pendingSave.Exception.GetBaseException().Message : pendingSave.Result;
                pendingSave = null;
                autosave.Complete(string.IsNullOrEmpty(failure), now);
                if (string.IsNullOrEmpty(failure))
                {
                    currentProfile = pendingSnapshot; SavedSnapshot(pendingJson); Notify(ProfileSaved, currentProfile);
                }
                else { LastSaveFailure = failure; SaveStatus = "Failed"; }
                pendingSnapshot = null; pendingJson = null;
            }
            if (restoreFaulted || currentProfile == null || safePoint == null || !safePoint() || now < nextInspection) return;
            nextInspection = now + 5;
            ResolveStorage(); ResolveParticipants();
            if (!(storage is JsonCareerProfileStorage backend)) return;
            if (backend.RequiresReload) { SaveStatus = "Reload required"; return; }
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var snapshot = JsonUtility.FromJson<CareerProfileData>(JsonUtility.ToJson(currentProfile));
                CaptureParticipants(snapshot, out string failure);
                if (!string.IsNullOrEmpty(failure)) { LastSaveFailure = failure; SaveStatus = "Failed"; return; }
                string json = JsonUtility.ToJson(snapshot, true);
                LastSnapshotMilliseconds = watch.Elapsed.TotalMilliseconds;
                if (json != lastCaptured) { autosave.Changed(now); lastCaptured = json; }
                if (!autosave.Due(now, true, queuedReason >= CareerSaveReason.Manual)) return;
                autosave.Begin(now); pendingSnapshot = snapshot; pendingJson = json; SaveStatus = "Writing";
                queuedReason = CareerSaveReason.Periodic;
                pendingSave = backend.SaveAsync(profileId, json);
            }
            catch (Exception exception)
            {
                if (autosave.Saving) autosave.Complete(false, now);
                LastSaveFailure = exception.Message; SaveStatus = "Failed";
            }
        }
    }
}
