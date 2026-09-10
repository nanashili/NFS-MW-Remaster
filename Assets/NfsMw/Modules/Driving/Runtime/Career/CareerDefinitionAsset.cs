using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Authoring asset only. Runtime consumers keep the compiled immutable graph.</summary>
    [CreateAssetMenu(menuName = "Driving/Career/Progression Definition", fileName = "CareerDefinition")]
    public sealed class CareerDefinitionAsset : ScriptableObject
    {
        [SerializeField] private CareerGraphDefinition definition = new CareerGraphDefinition();
        public CareerGraph Compile() => new CareerGraph(definition);

        /// <summary>
        /// Returns an isolated copy of the authored source document for editor
        /// projections, reports and migration tooling. Callers must not treat
        /// this copy as live runtime state; <see cref="Compile"/> remains the
        /// runtime read seam.
        /// </summary>
        public CareerGraphDefinition Definition()
        {
            if (definition == null)
            {
                return new CareerGraphDefinition();
            }

            return JsonUtility.FromJson<CareerGraphDefinition>(JsonUtility.ToJson(definition));
        }

        /// <summary>
        /// Replaces the authored source only after the same typed compiler has
        /// accepted it. Editor authoring tools use this to keep validation and
        /// runtime compilation on one semantic path.
        /// </summary>
        public void Configure(CareerGraphDefinition authoredDefinition)
        {
            if (authoredDefinition == null)
            {
                throw new System.ArgumentNullException(nameof(authoredDefinition));
            }

            _ = new CareerGraph(authoredDefinition);
            definition = JsonUtility.FromJson<CareerGraphDefinition>(JsonUtility.ToJson(authoredDefinition));
        }
    }
}
