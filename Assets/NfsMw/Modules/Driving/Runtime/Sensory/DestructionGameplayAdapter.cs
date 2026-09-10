using System;
using Unity.Profiling;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Game integration observes the same fact, not a second collision detector. No sensory reward writes.</summary>
    public sealed class DestructionGameplayAdapter : MonoBehaviour
    {
        [SerializeField] private DestructionWorld world;
        [SerializeField] private VehicleController player;
        [SerializeField] private FreeRoamTraffic traffic;
        [SerializeField] private MissionHost missions;
        public void Configure(DestructionWorld events, VehicleController instigator, FreeRoamTraffic roads, MissionHost objectives)
        {
            if (world != null) world.Broken -= OnBroken;
            world = events; player = instigator; traffic = roads; missions = objectives;
            if (isActiveAndEnabled && world != null) world.Broken += OnBroken;
        }
        private void OnEnable() { if (world != null) world.Broken += OnBroken; }
        private void OnBroken(DestructionFact fact)
        {
            if (fact.Instigator != player) return;
            if (traffic != null) traffic.ReportOffence(VehicleBountyEventKind.PropertyDamage);
            if (missions != null) missions.Publish("world.destroyed", fact.Id, identity: "destroy." + fact.Id + "." + fact.Generation);
        }
        private void OnDisable() { if (world != null) world.Broken -= OnBroken; }
    }
}
