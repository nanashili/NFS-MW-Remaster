using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public sealed class MissionRaceBridge : MonoBehaviour
    {
        [SerializeField] private MissionHost host;
        [SerializeField] private FreeRoamSession session;
        private void OnEnable()
        {
            if (host == null) host = GetComponent<MissionHost>();
            if (session == null) session = GetComponent<FreeRoamSession>();
            if (session != null) session.RaceCompleted += Completed;
        }
        private void Completed(RaceResult result)
        {
            if (host == null) return;
            host.Publish(result.Won ? "race.won" : "race.failed", result.EventId, identity: "race." + result.SettlementId + ".outcome");
            host.Publish("race.completed", result.EventId, result.ElapsedSeconds, "race.elapsed", "race." + result.SettlementId + ".completed");
        }
        private void OnDisable() { if (session != null) session.RaceCompleted -= Completed; }
    }
}
