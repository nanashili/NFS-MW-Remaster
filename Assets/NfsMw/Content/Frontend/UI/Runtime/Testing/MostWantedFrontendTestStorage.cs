using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Test-scene-only career storage. It never reads or writes the player's save directory.</summary>
    [DisallowMultipleComponent, AddComponentMenu("NFS MW Remaster/Testing/Frontend Memory Storage")]
    public sealed class MostWantedFrontendTestStorage : MonoBehaviour,
        ICareerProfileStorage, ICareerProfilePresence, ICareerProfileCreation
    {
        private readonly Dictionary<string, string> profiles = new Dictionary<string, string>(StringComparer.Ordinal);
        public int Count => profiles.Count;

        public bool TryExists(string profileId, out bool exists, out string failure)
        {
            exists = false;
            if (!ValidateId(profileId, out failure)) return false;
            exists = profiles.ContainsKey(profileId);
            return true;
        }

        public bool TryLoad(string profileId, out string serializedProfile, out string failure)
        {
            serializedProfile = string.Empty;
            if (!ValidateId(profileId, out failure)) return false;
            if (profiles.TryGetValue(profileId, out serializedProfile)) return true;
            failure = "No in-memory test profile exists for this alias. Start a new test career first.";
            return false;
        }

        public bool TrySave(string profileId, string serializedProfile, out string failure)
        {
            if (!ValidateDocument(profileId, serializedProfile, out failure)) return false;
            profiles[profileId] = serializedProfile;
            return true;
        }

        public bool TryCreate(string profileId, string serializedProfile, out string failure)
        {
            if (!ValidateDocument(profileId, serializedProfile, out failure)) return false;
            if (profiles.ContainsKey(profileId)) { failure = "This test alias already exists."; return false; }
            profiles.Add(profileId, serializedProfile);
            return true;
        }

        private static bool ValidateId(string profileId, out string failure)
        {
            if (MostWantedFrontendSettings.TryValidateAlias(profileId, out failure)) return true;
            failure = "Invalid test profile alias: " + failure;
            return false;
        }

        private static bool ValidateDocument(string profileId, string serializedProfile, out string failure)
        {
            if (!ValidateId(profileId, out failure)) return false;
            try
            {
                CareerSaveCodec.Validate(profileId, serializedProfile);
                failure = string.Empty;
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is System.IO.IOException)
            {
                failure = "Invalid test profile: " + exception.Message;
                return false;
            }
        }
    }
}
