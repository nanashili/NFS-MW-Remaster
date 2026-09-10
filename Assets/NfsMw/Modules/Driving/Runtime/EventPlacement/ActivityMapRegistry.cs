using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public sealed class ActivityMapMarker
    {
        internal readonly ActivityRecord Data;
        private readonly CareerRequirement requirement;
        private readonly bool needsFacts;
        public ActivityRecord Snapshot => Data.Copy();
        public bool Loaded { get; internal set; }
        internal ActivityMapMarker(ActivityRecord record)
        {
            Data = record; requirement = CareerRequirement.Compile(record.availability);
            bool needed = false; requirement.VisitDependencies((kind, subject, inverted) => needed = true); needsFacts = needed;
        }
        private static readonly ActivityPreviewFacts Empty = new ActivityPreviewFacts();
        public bool Eligible(ICareerFacts facts) => (!needsFacts || facts != null) && requirement.Evaluate(facts ?? Empty);
    }

    public static class ActivityMapRegistry
    {
        private static readonly Dictionary<ActivityMapCatalog, EventPlacementPublication[]> catalogs = new Dictionary<ActivityMapCatalog, EventPlacementPublication[]>();
        private static readonly SortedDictionary<string, ActivityMapMarker> markers = new SortedDictionary<string, ActivityMapMarker>(StringComparer.Ordinal);
        private static int loadedVersion = -1;
        public static IEnumerable<ActivityMapMarker> Markers { get { Refresh(); return markers.Values; } }
        public static bool Register(ActivityMapCatalog owner, EventPlacementPublication[] values, out string failure)
        {
            failure = ""; if (owner == null || values == null) { failure = "Missing catalog."; return false; }
            var ids = new HashSet<string>();
            var records = new List<ActivityRecord>();
            foreach (var publication in values)
            {
                if (publication == null || publication.Schema != 1 || !ids.Add(publication.Id)) { failure = "Missing, duplicate or unsupported catalog publication."; return false; }
                try
                {
                    var incoming = publication.Snapshot; new ActivityMapMarker(incoming);
                    foreach (var existing in records) if (Conflict(existing, incoming)) { failure = "Unique definition or revision conflicts within catalog."; return false; }
                    records.Add(incoming);
                } catch (Exception e) { failure = e.Message; return false; }
                foreach (var pair in catalogs)
                    if (pair.Key != owner) foreach (var existing in pair.Value)
                        if (Conflict(existing.Snapshot, publication.Snapshot)) { failure = "Catalog revision conflict: " + publication.Id; return false; }
                foreach (var instance in ActivityRegistry.Loaded)
                    if (Conflict(instance.Data, publication.Snapshot)) { failure = "Loaded placement revision conflicts with catalog."; return false; }
            }
            catalogs[owner] = (EventPlacementPublication[])values.Clone(); loadedVersion = -1; return true;
        }
        private static bool Conflict(ActivityRecord a, ActivityRecord b) =>
            (a.id == b.id && a.fingerprint != b.fingerprint) ||
            (a.id != b.id && a.definitionId == b.definitionId && (a.uniqueDefinition || b.uniqueDefinition));
        public static void Remove(ActivityMapCatalog owner) { if (catalogs.Remove(owner)) loadedVersion = -1; }
        internal static bool Accepts(ActivityRecord record)
        {
            foreach (var publications in catalogs.Values) foreach (var publication in publications)
                {
                    var existing = publication.Snapshot;
                    if (publication.Id == record.id && publication.Fingerprint != record.fingerprint) return false;
                    if (publication.Id != record.id && existing.definitionId == record.definitionId && (existing.uniqueDefinition || record.uniqueDefinition)) return false;
                }
            return true;
        }
        private static void Refresh()
        {
            if (loadedVersion == ActivityRegistry.Version) return;
            markers.Clear();
            foreach (var publications in catalogs.Values) foreach (var publication in publications)
                if (!markers.ContainsKey(publication.Id)) markers.Add(publication.Id, new ActivityMapMarker(publication.Snapshot));
            foreach (var instance in ActivityRegistry.Loaded)
            {
                if (!markers.TryGetValue(instance.Data.id, out var marker)) markers.Add(instance.Data.id, marker = new ActivityMapMarker(instance.Record));
                marker.Loaded = true;
            }
            loadedVersion = ActivityRegistry.Version;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear() { catalogs.Clear(); markers.Clear(); loadedVersion = -1; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Restore() { foreach (var catalog in UnityEngine.Object.FindObjectsByType<ActivityMapCatalog>(FindObjectsSortMode.None)) catalog.Register(); }
    }
}
