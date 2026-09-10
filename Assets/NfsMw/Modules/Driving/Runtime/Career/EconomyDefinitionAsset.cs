using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(menuName = "Driving/Career/Economy Definition", fileName = "EconomyDefinition")]
    public sealed class EconomyDefinitionAsset : ScriptableObject
    {
        [SerializeField] private EconomyDefinition definition = new EconomyDefinition();
        [SerializeField] private EconomySimulationScenario[] scenarios = new EconomySimulationScenario[0];
        public EconomyDefinition Definition() => EconomySettlement.Copy(definition);
        public EconomySimulationScenario[] Scenarios()
        {
            var result = new EconomySimulationScenario[scenarios.Length];
            for (int i = 0; i < result.Length; i++) result[i] = EconomySettlement.Copy(scenarios[i]);
            return result;
        }
    }
}
