using System;
using System.Collections.Generic;

namespace NfsMwRemaster.Driving
{
    public static class MissionPersistence
    {
        public static void Validate(CareerMissionData data)
        {
            if (data == null || data.version != 1 || data.instances == null || data.instances.Count > 256 || data.claims == null || data.claims.Count > 100000)
                throw new ArgumentException("Invalid mission persistence section.");
            var instances = new HashSet<string>();
            foreach (var instance in data.instances)
            { MissionRuntime.ValidateSnapshot(instance); if (!instances.Add(instance.claimId)) throw new ArgumentException("Duplicate mission instance identity."); }
            var claims = new HashSet<string>();
            foreach (var claim in data.claims)
            {
                if (claim == null || !claims.Add(claim.claimId) || claim.cash < 0 || string.IsNullOrEmpty(claim.fingerprint)) throw new ArgumentException("Invalid mission claim receipt.");
                MissionData.Id(claim.claimId); MissionData.Id(claim.missionId);
            }
        }
        public static void Upsert(CareerMissionData data, MissionSnapshot snapshot)
        {
            MissionRuntime.ValidateSnapshot(snapshot);
            int index = data.instances.FindIndex(x => x.claimId == snapshot.claimId);
            if (index >= 0) data.instances[index] = MissionData.Copy(snapshot);
            else
            {
                // Keep a bounded latest-state view; durable receipts are never removed.
                if (data.instances.Count >= 256)
                {
                    int old = data.instances.FindIndex(x => x.state == MissionState.Succeeded || x.state == MissionState.Failed || x.state == MissionState.Aborted);
                    if (old < 0) throw new ArgumentException("Too many active mission snapshots.");
                    data.instances.RemoveAt(old);
                }
                data.instances.Add(MissionData.Copy(snapshot));
            }
        }
    }
}
