using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Coordinates a versioned career profile while delegating each section
    /// to an injected participant and persistence adapter. It can be attached
    /// to a vehicle root for the demo or to a dedicated career service object.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class CareerProfileSystem : MonoBehaviour, ICareerProfileService
    {
        [SerializeField] private string profileId = "career_01";
        [SerializeField] private string playerName = "Street Racer";
        [SerializeField] private string activeVehicleId = "player_vehicle";
        [SerializeField] private MonoBehaviour storageComponent = null!;
        [SerializeField] private bool autoDiscoverStorage = true;
        [SerializeField] private bool autoDiscoverParticipants = true;
        [SerializeField] private MonoBehaviour[] participantComponents =
            Array.Empty<MonoBehaviour>();
        [SerializeField] private bool loadOnAwake;
        [SerializeField] private bool saveOnApplicationPause = true;
        [SerializeField] private bool saveOnApplicationQuit = true;

        private readonly List<ICareerProfileParticipant> profileParticipants =
            new List<ICareerProfileParticipant>();
        private readonly List<ICareerVehicleStateParticipant> vehicleParticipants =
            new List<ICareerVehicleStateParticipant>();
        private ICareerProfileStorage storage;
        private CareerProfileData currentProfile = null!;

        public event Action<CareerProfileData> ProfileSaved;

        public event Action<CareerProfileData> ProfileLoaded;

        public string ProfileId
        {
            get { return profileId; }
        }

        public string PlayerName
        {
            get { return playerName; }
        }

        public string ActiveVehicleId
        {
            get { return activeVehicleId; }
        }

        public CareerProfileData CurrentProfile
        {
            get { return currentProfile == null ? null : JsonUtility.FromJson<CareerProfileData>(JsonUtility.ToJson(currentProfile)); }
        }

        public ICareerProfileStorage Storage
        {
            get
            {
                ResolveStorage();
                return storage;
            }
        }

        public void SetProfileId(string configuredProfileId)
        {
            if (SaveInProgress) throw new InvalidOperationException("Wait for the current save before switching profiles.");
            profileId = configuredProfileId == null
                ? string.Empty
                : configuredProfileId.Trim();
            currentProfile = null;
            ResetAutosave();
        }

        public void ConfigureAutomaticPersistence(bool load, bool saveOnPause, bool saveOnQuit)
        {
            loadOnAwake = load;
            saveOnApplicationPause = saveOnPause;
            saveOnApplicationQuit = saveOnQuit;
        }

        public void SetPlayerName(string configuredPlayerName)
        {
            playerName = configuredPlayerName == null
                ? string.Empty
                : configuredPlayerName.Trim();
        }

        public void SetActiveVehicleId(string configuredVehicleId)
        {
            if (!string.IsNullOrWhiteSpace(configuredVehicleId))
            {
                activeVehicleId = configuredVehicleId.Trim();
            }
        }

        public void SetStorage(MonoBehaviour configuredStorage)
        {
            if (SaveInProgress) throw new InvalidOperationException("Wait for the active save before changing storage.");
            storageComponent = configuredStorage;
            storage = configuredStorage as ICareerProfileStorage;
            autoDiscoverStorage = false;
        }

        public void SetParticipants(MonoBehaviour[] configuredParticipants)
        {
            participantComponents = configuredParticipants ?? Array.Empty<MonoBehaviour>();
            autoDiscoverParticipants = false;
            ResolveParticipants();
        }

        public bool TryCreateNewProfile(out string failure)
        {
            if (SaveInProgress) { failure = "A save is still in progress."; return false; }
            if (!ValidateProfileIdentity(out failure))
            {
                return false;
            }

            currentProfile = CareerProfileData.Create(profileId, playerName);
            currentProfile.activeVehicleId = activeVehicleId.Trim();
            failure = string.Empty;
            return true;
        }

        public bool TrySave(out string failure)
            => SaveProfile(false, out failure);

        public bool TrySaveNew(out string failure)
            => SaveProfile(true, out failure);

        private bool SaveProfile(bool createOnly, out string failure)
        {
            if (SaveInProgress || restoreFaulted)
            { failure = restoreFaulted ? "Runtime restoration failed; reload the scene before saving." : "A save is still in progress."; return false; }
            ResolveStorage();
            ResolveParticipants();
            if (!ValidateProfileIdentity(out failure))
            {
                return false;
            }

            if (storage == null)
            {
                failure = "No career profile storage adapter is attached.";
                return false;
            }

            CareerProfileData profile = currentProfile == null ? null
                : JsonUtility.FromJson<CareerProfileData>(JsonUtility.ToJson(currentProfile));
            if (profile == null)
            {
                profile = CareerProfileData.Create(profileId, playerName);
            }

            CaptureParticipants(profile, out failure);
            if (!string.IsNullOrEmpty(failure))
            {
                return false;
            }

            if (!profile.Validate(profileId, out failure))
            {
                return false;
            }

            string serializedProfile;
            try
            {
                serializedProfile = JsonUtility.ToJson(profile, true);
                CareerSaveCodec.Validate(profileId, serializedProfile);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is System.IO.IOException)
            {
                failure = "Could not serialize the profile: " + exception.Message;
                return false;
            }

            try
            {
                bool saved;
                if (createOnly)
                {
                    if (!(storage is ICareerProfileCreation creation))
                    { failure = "Storage does not support exclusive alias creation."; return false; }
                    saved = creation.TryCreate(profileId, serializedProfile, out failure);
                }
                else saved = storage.TrySave(profileId, serializedProfile, out failure);
                if (!saved)
                {
                    return false;
                }
            }
            catch (Exception exception)
            {
                failure = "Profile storage save failed: " + exception.Message;
                return false;
            }

            currentProfile = profile;
            autosave.AcknowledgeCurrent();
            SavedSnapshot(serializedProfile);
            Notify(ProfileSaved, currentProfile);
            failure = string.Empty;
            return true;
        }

        public bool TryLoad(out string failure)
        {
            if (SaveInProgress) { failure = "Wait for the current save before loading."; return false; }
            ResolveStorage();
            ResolveParticipants();
            if (!ValidateProfileIdentity(out failure))
            {
                return false;
            }

            if (storage == null)
            {
                failure = "No career profile storage adapter is attached.";
                return false;
            }

            string serializedProfile;
            try
            {
                if (!storage.TryLoad(profileId, out serializedProfile, out failure))
                {
                    return false;
                }
            }
            catch (Exception exception)
            {
                serializedProfile = string.Empty;
                failure = "Profile storage load failed: " + exception.Message;
                return false;
            }

            if (string.IsNullOrWhiteSpace(serializedProfile))
            {
                failure = "Saved profile is empty or invalid.";
                return false;
            }

            CareerProfileData loadedProfile;
            try
            {
                serializedProfile = CareerSaveCodec.Migrate(profileId, serializedProfile);
                loadedProfile = JsonUtility.FromJson<CareerProfileData>(serializedProfile);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is System.IO.IOException)
            {
                failure = "Could not deserialize the profile: " + exception.Message;
                return false;
            }

            if (loadedProfile == null
                || !loadedProfile.Validate(profileId, out failure))
            {
                if (loadedProfile == null && string.IsNullOrEmpty(failure))
                {
                    failure = "Saved profile is empty or invalid.";
                }

                return false;
            }

            string previousPlayerName = playerName;
            string previousVehicleId = activeVehicleId;
            // Capture live participant state before any restore side effects. The existing
            // participant interface cannot promise transactional construction, so rollback
            // failures explicitly disable saves instead of persisting half-restored state.
            CareerProfileData rollback = currentProfile == null ? CareerProfileData.Create(profileId, playerName)
                : JsonUtility.FromJson<CareerProfileData>(JsonUtility.ToJson(currentProfile));
            CaptureParticipants(rollback, out failure);
            if (!string.IsNullOrEmpty(failure)) return false;
            playerName = loadedProfile.playerName;
            if (!string.IsNullOrWhiteSpace(loadedProfile.activeVehicleId))
            {
                activeVehicleId = loadedProfile.activeVehicleId;
            }

            if (!RestoreParticipants(loadedProfile, out failure))
            {
                restoreFaulted = true;
                playerName = previousPlayerName;
                activeVehicleId = previousVehicleId;
                string restoreFailure = failure;
                if (!RestoreParticipants(rollback, out string rollbackFailure))
                { restoreFaulted = true; failure = restoreFailure + " Rollback also failed: " + rollbackFailure; }
                else failure = restoreFailure;
                return false;
            }

            currentProfile = loadedProfile;
            LastRecovery = storage is JsonCareerProfileStorage localStorage ? localStorage.LastRead?.Recovery ?? string.Empty : string.Empty;
            restoreFaulted = false;
            ResetAutosave(); SavedSnapshot(JsonUtility.ToJson(currentProfile, true));
            Notify(ProfileLoaded, currentProfile);
            failure = string.Empty;
            return true;
        }

        private void Awake()
        {
            ResolveStorage();
            ResolveParticipants();
            if (loadOnAwake && !TryLoad(out string failure) && !string.IsNullOrEmpty(failure))
            {
                Debug.LogWarning("Career profile was not loaded: " + failure, this);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && saveOnApplicationPause)
            {
                if (safePoint != null) { RequestSave(CareerSaveReason.Suspend); Update(); }
                else TrySave(out _);
            }
        }

        private void OnApplicationQuit()
        {
            if (saveOnApplicationQuit)
            {
                TrySave(out _);
            }
        }

        private void ResolveStorage()
        {
            if (storage != null)
            {
                return;
            }

            if (storageComponent is ICareerProfileStorage configuredStorage)
            {
                storage = configuredStorage;
                return;
            }

            if (!autoDiscoverStorage)
            {
                return;
            }

            MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ICareerProfileStorage discoveredStorage)
                {
                    storageComponent = behaviours[i];
                    storage = discoveredStorage;
                    return;
                }
            }
        }

        private void ResolveParticipants()
        {
            profileParticipants.Clear();
            vehicleParticipants.Clear();
            MonoBehaviour[] candidates = autoDiscoverParticipants
                ? GetComponentsInChildren<MonoBehaviour>(true)
                : participantComponents;
            if (candidates == null)
            {
                return;
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                MonoBehaviour candidate = candidates[i];
                if (candidate is ICareerProfileParticipant participant
                    && !profileParticipants.Contains(participant))
                {
                    profileParticipants.Add(participant);
                }

                if (candidate is ICareerVehicleStateParticipant vehicleParticipant
                    && !vehicleParticipants.Contains(vehicleParticipant))
                {
                    vehicleParticipants.Add(vehicleParticipant);
                }
            }
        }

        private void CaptureParticipants(
            CareerProfileData profile,
            out string failure)
        {
            profile.profileId = profileId.Trim();
            profile.playerName = playerName == null ? string.Empty : playerName.Trim();
            profile.activeVehicleId = activeVehicleId.Trim();
            profile.Normalize();

            for (int i = 0; i < profileParticipants.Count; i++)
            {
                try
                {
                    profileParticipants[i].Capture(profile);
                }
                catch (Exception exception)
                {
                    failure = "Profile participant capture failed for "
                        + profileParticipants[i].ProfileSectionId
                        + ": "
                        + exception.Message;
                    return;
                }
            }

            CareerVehicleData vehicle = profile.GetOrCreateVehicle(activeVehicleId);
            if (vehicle == null)
            {
                failure = "Active vehicle ID cannot be empty.";
                return;
            }

            for (int i = 0; i < vehicleParticipants.Count; i++)
            {
                try
                {
                    vehicleParticipants[i].Capture(vehicle);
                }
                catch (Exception exception)
                {
                    failure = "Vehicle profile participant capture failed for "
                        + vehicleParticipants[i].VehicleStateSectionId
                        + ": "
                        + exception.Message;
                    return;
                }
            }

            profile.Normalize();
            failure = string.Empty;
        }

        private bool RestoreParticipants(
            CareerProfileData profile,
            out string failure)
        {
            for (int i = 0; i < profileParticipants.Count; i++)
            {
                try
                {
                    if (!profileParticipants[i].Restore(profile, out failure))
                    {
                        return false;
                    }
                }
                catch (Exception exception)
                {
                    failure = "Profile participant restore failed for "
                        + profileParticipants[i].ProfileSectionId
                        + ": "
                        + exception.Message;
                    return false;
                }
            }

            CareerVehicleData vehicle = profile.FindVehicle(activeVehicleId)
                ?? new CareerVehicleData { vehicleId = activeVehicleId };
            for (int i = 0; i < vehicleParticipants.Count; i++)
            {
                try
                {
                    if (!vehicleParticipants[i].Restore(vehicle, out failure))
                    {
                        return false;
                    }
                }
                catch (Exception exception)
                {
                    failure = "Vehicle profile participant restore failed for "
                        + vehicleParticipants[i].VehicleStateSectionId
                        + ": "
                        + exception.Message;
                    return false;
                }
            }

            failure = string.Empty;
            return true;
        }

        private bool ValidateProfileIdentity(out string failure)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                failure = "Profile ID cannot be empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(activeVehicleId))
            {
                failure = "Active vehicle ID cannot be empty.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static void Notify(Action<CareerProfileData> handlers, CareerProfileData profile)
        {
            if (handlers == null) return;
            foreach (Action<CareerProfileData> handler in handlers.GetInvocationList())
            {
                try { handler(JsonUtility.FromJson<CareerProfileData>(JsonUtility.ToJson(profile))); }
                catch (Exception exception) { Debug.LogWarning("Profile committed, but an observer failed: " + exception.Message); }
            }
        }
    }
}
