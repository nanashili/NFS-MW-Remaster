using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace NfsMwRemaster.Driving
{
    [Serializable]
    public sealed class RoadBandBinding
    {
        [HideInInspector] public RoadId templateId;
        [HideInInspector] public RoadId id;
        public bool overrideWidth;
        public AnimationCurve width = AnimationCurve.Constant(0, 100, 3.5f);
    }

    [DisallowMultipleComponent, RequireComponent(typeof(SplineContainer))]
    public sealed class RoadAuthoring : MonoBehaviour
    {
        public const int CurrentSchema = 1;
        [SerializeField, HideInInspector] private int schemaVersion = CurrentSchema;
        [SerializeField, HideInInspector] private RoadId id;
        [SerializeField] private SplineContainer reference;
        [SerializeField] private RoadProfile profile;
        [SerializeField] private RoadBandBinding[] bands = Array.Empty<RoadBandBinding>();
        public AnimationCurve bankRadians = AnimationCurve.Constant(0, 100, 0);
        [Min(0.0001f)] public float chordTolerance = 0.01f;
        [Min(0.1f)] public float maximumSampleSpacing = 2;
        [Min(1)] public float chunkLength = 100;
        public RoadId Id => id;
        public int SchemaVersion => schemaVersion;
        public SplineContainer Reference => reference;
        public RoadProfile Profile => profile;
        public RoadBandBinding[] Bands => bands;

        // Called by explicit create/profile commands; never from OnValidate or deserialization.
        public void Initialize(RoadProfile value, SplineContainer spline)
        {
            if (value == null || spline == null) throw new ArgumentNullException();
            if (spline.gameObject != gameObject) throw new ArgumentException("The reference spline must belong to this road.");
            if (value.bands == null || value.bands.Length == 0) throw new ArgumentException("ROAD_PROFILE: define at least one profile band.");
            var previous = bands ?? Array.Empty<RoadBandBinding>();
            var next = new RoadBandBinding[value.bands.Length];
            var templates = new HashSet<RoadId>();
            for (int i = 0; i < next.Length; i++)
            {
                var band = value.bands[i];
                if (band == null || !band.id.IsValid || !templates.Add(band.id)) throw new ArgumentException("ROAD_BAND_ID: assign unique profile band identities first.");
                foreach (var binding in previous)
                    if (binding != null && binding.templateId == band.id) { next[i] = binding; break; }
                if (next[i] == null) next[i] = new RoadBandBinding
                {
                    templateId = band.id, id = RoadId.New(),
                    width = AnimationCurve.Constant(0, 100, band.width)
                };
            }
            if (!id.IsValid) id = RoadId.New();
            reference = spline; profile = value; bands = next;
        }
    }
}
