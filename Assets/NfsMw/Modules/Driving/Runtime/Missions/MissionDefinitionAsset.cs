using System;
using Newtonsoft.Json;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [CreateAssetMenu(menuName = "Driving/Mission Graph", fileName = "Mission")]
    public sealed class MissionDefinitionAsset : ScriptableObject
    {
        [SerializeField, TextArea(15, 40)] private string definitionJson = "{}";
        public string Json => definitionJson;
        public MissionGraph Compile()
        {
            var json = CareerSaveCodec.Parse(definitionJson);
            return new MissionGraph(json.ToObject<MissionDefinition>());
        }
        public void Configure(MissionDefinition definition)
        {
            _ = new MissionGraph(definition); definitionJson = JsonConvert.SerializeObject(definition, Formatting.Indented);
        }

        /// <summary>
        /// Stores a structurally valid authoring document without compiling it.
        /// The graph editor uses this only for recovery/editing of a draft; the
        /// runtime-facing Compile method remains fail-closed for invalid content.
        /// </summary>
        public void SetAuthoringJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Mission JSON cannot be empty.", nameof(json));
            var parsed = CareerSaveCodec.Parse(json).ToObject<MissionDefinition>();
            if (parsed == null) throw new ArgumentException("Mission JSON did not contain a definition.", nameof(json));
            definitionJson = json;
        }
    }
}
