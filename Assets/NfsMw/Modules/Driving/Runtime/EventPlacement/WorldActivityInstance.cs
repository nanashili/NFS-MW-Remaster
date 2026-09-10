using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Scene ownership registry: unloading a losing duplicate cannot remove the winner.</summary>
    public static class ActivityRegistry
    {
        private static readonly Dictionary<string, WorldActivityInstance> instances = new Dictionary<string, WorldActivityInstance>(StringComparer.Ordinal);
        public static IEnumerable<WorldActivityInstance> Loaded => instances.Values;
        public static int Version { get; private set; }
        public static bool Register(WorldActivityInstance instance)
        {
            if (instance == null || instance.Data == null || !ActivityMapRegistry.Accepts(instance.Data)) return false;
            string id = instance.Data.id;
            if (instances.TryGetValue(id, out var existing) && existing != instance) return false;
            foreach (var other in instances.Values)
                if (other != instance && other.Data.definitionId == instance.Data.definitionId && (instance.Data.uniqueDefinition || other.Data.uniqueDefinition)) return false;
            instances[id] = instance; Version++; return true;
        }
        public static bool Owns(WorldActivityInstance instance) => instance != null && instance.Data != null && instances.TryGetValue(instance.Data.id, out var owner) && owner == instance;
        public static void Unregister(WorldActivityInstance instance)
        { if (Owns(instance)) { instances.Remove(instance.Data.id); Version++; } }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear() { instances.Clear(); Version++; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Restore()
        { foreach (var instance in UnityEngine.Object.FindObjectsByType<WorldActivityInstance>(FindObjectsSortMode.None)) instance.Register(); }
    }

    [DisallowMultipleComponent]
    public sealed class WorldActivityInstance : MonoBehaviour
    {
        [SerializeField] private EventPlacementPublication publication;
        private ActivityRecord record;
        private CareerRequirement requirement;
        private bool needsFacts;
        internal ActivityRecord Data => record;
        public ActivityRecord Record => record?.Copy();
        public EventPlacementPublication Publication => publication;
        public string Failure { get; private set; }
        public void Configure(EventPlacementPublication value)
        {
            ActivityRegistry.Unregister(this); publication = value; ReadPublication();
            if (Application.isPlaying && isActiveAndEnabled) Register();
        }
        private void ReadPublication()
        {
            record = publication != null && publication.Schema == 1 ? publication.Snapshot : null;
            requirement = null; needsFacts = false;
            if (record == null) { Failure = "Missing or unsupported publication."; return; }
            try
            {
                requirement = CareerRequirement.Compile(record.availability);
                requirement.VisitDependencies((kind, subject, inverted) => needsFacts = true);
                Failure = string.Empty;
            }
            catch (Exception e) { Failure = e.Message; record = null; }
        }
        private void OnEnable() { ReadPublication(); if (Application.isPlaying) Register(); }
        private void OnDisable() => ActivityRegistry.Unregister(this);
        internal void Register()
        {
            if (record == null) ReadPublication();
            if (!ActivityRegistry.Register(this)) Failure = "Duplicate identity or invalid publication; activity disabled.";
        }
        public bool Eligible(ICareerFacts facts, out string reason)
        {
            reason = Failure;
            if (record == null || requirement == null) return false;
            if (needsFacts && facts == null) { reason = "Career facts provider is not connected."; return false; }
            if (!requirement.Evaluate(facts ?? EmptyFacts)) { reason = "Career requirements are not satisfied."; return false; }
            reason = string.Empty; return true;
        }
        private static readonly ActivityPreviewFacts EmptyFacts = new ActivityPreviewFacts();
    }
}
