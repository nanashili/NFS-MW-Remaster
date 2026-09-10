using UnityEngine;

namespace NfsMwRemaster.Driving
{
    // Semantic adapter only. Facts and terminal results come from the police authority, not historical bounty.
    public sealed class MissionPursuitBridge : MonoBehaviour
    {
        [SerializeField] private MissionHost host;
        [SerializeField] private VehiclePursuitDirector director;
        private int heat = -1;
        private bool active, subscribed;
        public void Configure(VehiclePursuitDirector pursuit) { Unsubscribe(); director = pursuit; Subscribe(); }
        private void Start() { Subscribe(); }
        private void OnEnable() { if (host != null && director != null) Subscribe(); }
        private void Subscribe()
        {
            if (subscribed) return;
            if (host == null) host = GetComponent<MissionHost>();
            if (director == null) director = GetComponent<FreeRoamSession>()?.Pursuit;
            if (host == null || director == null) return;
            host.RuntimeStarted += InitializeFacts; director.StateChanged += Changed;
            director.OutcomeAcknowledged += Settled; subscribed = true; InitializeFacts();
        }
        private void InitializeFacts() { heat = -1; active = !director.IsActive; Changed(); }
        private void Changed()
        {
            if (heat != director.HeatLevel) { heat = director.HeatLevel; host.Publish("pursuit.heat", value: heat, fact: "pursuit.heat"); }
            if (active != director.IsActive) { active = director.IsActive; host.Publish(active ? "pursuit.started" : "pursuit.ended", value: active ? 1 : 0, fact: "pursuit.active"); }
        }
        private void Settled(PoliceOutcome result)
        {
            if (result.kind == PoliceOutcomeKind.Escaped) host.Publish("pursuit.escaped", fact: "pursuit.escaped");
            else if (result.kind == PoliceOutcomeKind.Arrested) host.Publish("pursuit.busted", fact: "pursuit.busted");
            else host.Publish("pursuit.fine_paid", fact: "pursuit.fine_paid");
        }
        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (host != null) host.RuntimeStarted -= InitializeFacts;
            if (director != null) { director.StateChanged -= Changed; director.OutcomeAcknowledged -= Settled; }
            subscribed = false;
        }
        private void OnDisable() => Unsubscribe();
    }
}
