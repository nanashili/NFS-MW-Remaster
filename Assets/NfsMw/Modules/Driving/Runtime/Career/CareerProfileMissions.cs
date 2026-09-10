using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public sealed partial class CareerProfileSystem
    {
        [SerializeField] private EconomyDefinitionAsset missionRewardCatalog;
        private EconomyDefinition missionCatalogOverride;
        public void ConfigureMissionRewardCatalog(EconomyDefinition catalog) => missionCatalogOverride = MissionData.Copy(catalog);
        public bool TrySettleMission(MissionRuntime runtime, out string failure)
        {
            if (runtime == null || SaveInProgress || restoreFaulted) { failure = "Mission settlement unavailable while saving/restoring."; return false; }
            ResolveStorage(); ResolveParticipants();
            var wallet = GetComponent<VehicleStoreWallet>();
            if (storage == null || wallet == null || !ValidateProfileIdentity(out failure)) { failure = "Mission requires profile storage and wallet."; return false; }
            var candidate = currentProfile == null ? CareerProfileData.Create(profileId, playerName) : JsonUtility.FromJson<CareerProfileData>(JsonUtility.ToJson(currentProfile));
            CaptureParticipants(candidate, out failure); if (!string.IsNullOrEmpty(failure)) return false;
            try
            {
                EconomySettlement.StageMission(candidate, runtime.Capture(), missionCatalogOverride ?? missionRewardCatalog?.Definition(), storage);
                string json = JsonUtility.ToJson(candidate, true); CareerSaveCodec.Validate(profileId, json);
                try { if (!storage.TrySave(profileId, json, out failure)) return false; }
                catch (Exception exception)
                {
                    bool committed = false;
                    try { committed = storage.TryLoad(profileId, out string observed, out _) && observed == json; } catch { }
                    if (!committed)
                    {
                        restoreFaulted = true;
                        failure = "Mission commit outcome uncertain; reload before further saves: " + exception.Message;
                        return false;
                    }
                }
                currentProfile = candidate;
                if (!wallet.Restore(candidate, out failure)) { restoreFaulted = true; return false; }
                var ownership = GetComponent<VehicleStoreOwnership>();
                var garage = GetComponent<VehicleStoreGarage>();
                if (ownership != null && !ownership.Restore(candidate, out failure) || garage != null && !garage.Restore(candidate, out failure))
                { restoreFaulted = true; return false; }
                autosave.AcknowledgeCurrent(); SavedSnapshot(json); Notify(ProfileSaved, currentProfile);
                failure = ""; return true;
            }
            catch (Exception exception) { failure = "Mission settlement not acknowledged: " + exception.Message; return false; }
        }
    }
}
