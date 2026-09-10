using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Serializable]
    public sealed class AudioZoneTraversalSample
    {
        public float time;
        public Vector3 position;
        public string primaryZoneId = string.Empty;
        public string[] zoneIds = Array.Empty<string>();
        public float[] weights = Array.Empty<float>();

        public void Sanitize()
        {
            time = Mathf.Max(0, SensoryMath.Finite(time));
            if (!SensoryMath.IsFinite(position.x) || !SensoryMath.IsFinite(position.y) || !SensoryMath.IsFinite(position.z)) position = Vector3.zero;
            primaryZoneId = primaryZoneId ?? string.Empty;
            int count = Mathf.Min(zoneIds == null ? 0 : zoneIds.Length, weights == null ? 0 : weights.Length);
            if (zoneIds == null || zoneIds.Length != count) Array.Resize(ref zoneIds, count);
            if (weights == null || weights.Length != count) Array.Resize(ref weights, count);
            for (int i = 0; i < count; i++) weights[i] = Mathf.Clamp01(SensoryMath.Finite(weights[i]));
        }
    }

    /// <summary>
    /// Editor-authored traversal evidence. It stores positions and resolved
    /// membership only; it is not a gameplay save or a runtime state object.
    /// </summary>
    [CreateAssetMenu(menuName = "NFS MW Remaster/Sensory/Audio Zone Traversal")]
    public sealed class AudioZoneTraversalAsset : ScriptableObject
    {
        public const int CurrentSchema = 1;

        [SerializeField] private int schema = CurrentSchema;
        [SerializeField] private string stableId = string.Empty;
        [SerializeField] private string sourceScene = string.Empty;
        [SerializeField] private string sourceRevision = string.Empty;
        [SerializeField] private AudioZoneListenerPolicy listenerPolicy;
        [SerializeField, Min(0.01f)] private float sampleInterval = 0.05f;
        [SerializeField] private string captureNotes = string.Empty;
        [SerializeField] private AudioZoneTraversalSample[] samples = Array.Empty<AudioZoneTraversalSample>();

        public int Schema => schema;
        public string StableId => stableId;
        public string SourceScene => sourceScene;
        public string SourceRevision => sourceRevision;
        public AudioZoneListenerPolicy ListenerPolicy => listenerPolicy;
        public float SampleInterval => sampleInterval;
        public string CaptureNotes => captureNotes;
        public AudioZoneTraversalSample[] Samples => samples ?? Array.Empty<AudioZoneTraversalSample>();

        public void Configure(string id, string scene, string revision, AudioZoneListenerPolicy policy, float interval)
        {
            schema = CurrentSchema;
            stableId = id ?? string.Empty;
            sourceScene = scene ?? string.Empty;
            sourceRevision = revision ?? string.Empty;
            listenerPolicy = policy;
            sampleInterval = Mathf.Clamp(SensoryMath.Finite(interval), 0.01f, 1f);
        }

        public void SetSamples(List<AudioZoneTraversalSample> values)
        {
            samples = values == null ? Array.Empty<AudioZoneTraversalSample>() : values.ToArray();
            foreach (var sample in samples) sample?.Sanitize();
        }

        public void SetCaptureNotes(string value) => captureNotes = value ?? string.Empty;

        public bool Validate(out string failure)
        {
            var errors = new List<string>();
            if (schema != CurrentSchema) errors.Add("schema " + schema + " is unsupported");
            if (string.IsNullOrWhiteSpace(stableId)) errors.Add("stable ID is empty");
            if (string.IsNullOrWhiteSpace(sourceScene)) errors.Add("source scene is empty");
            if (string.IsNullOrWhiteSpace(sourceRevision)) errors.Add("source revision is empty");
            if (sampleInterval < 0.01f || sampleInterval > 1f || !SensoryMath.IsFinite(sampleInterval)) errors.Add("sample interval is invalid");
            float previous = -1;
            foreach (var sample in Samples)
            {
                if (sample == null) { errors.Add("samples contain a null entry"); continue; }
                sample.Sanitize();
                if (sample.time < previous) errors.Add("sample times must be monotonic");
                previous = sample.time;
                if (sample.zoneIds.Length != sample.weights.Length) errors.Add("sample zone and weight arrays differ");
            }
            failure = string.Join("; ", errors);
            return errors.Count == 0;
        }
    }
}
