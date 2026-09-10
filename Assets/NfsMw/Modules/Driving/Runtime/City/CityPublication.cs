using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Serializable] public sealed class CityLocationRecord
    {
        public string id, label, districtId, blockId;
        public CityLandUse use;
        public CityPolygon footprint;
        public Vector3 localPosition;
        public RoadId entranceLane;
        public bool accessValidated;
        public Vector2Int[] cells;
    }
    public sealed class CityPublication : ScriptableObject
    {
        [SerializeField] private int schemaVersion;
        [SerializeField] private string districtId, fingerprint, roadFingerprint;
        [SerializeField] private Vector3 origin;
        [SerializeField] private CityLocationRecord[] locations = Array.Empty<CityLocationRecord>();
        public int SchemaVersion => schemaVersion;
        public string DistrictId => districtId;
        public string Fingerprint => fingerprint;
        public string RoadFingerprint => roadFingerprint;
        public Vector3 Origin => origin;
        public IReadOnlyList<CityLocationRecord> Locations => Array.AsReadOnly(Clone(locations));
        public void Initialize(string id, string revision, string roadRevision, Vector3 position, CityLocationRecord[] records)
        {
            if (schemaVersion != 0) throw new InvalidOperationException("Publish a new city revision.");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(revision) || records == null) throw new ArgumentException("Invalid city publication.");
            var unique = new HashSet<string>();
            foreach (var record in records) if (record == null || string.IsNullOrEmpty(record.id) || !unique.Add(record.id)) throw new ArgumentException("Duplicate or missing city location.");
            locations = Clone(records); districtId = id; fingerprint = revision; roadFingerprint = roadRevision;
            origin = position; schemaVersion = 1;
        }
        private static CityLocationRecord[] Clone(CityLocationRecord[] records)
        {
            var copy = new CityLocationRecord[records.Length];
            for (int i = 0; i < records.Length; i++) copy[i] = JsonUtility.FromJson<CityLocationRecord>(JsonUtility.ToJson(records[i]));
            return copy;
        }
        public bool TryResolve(string id, out CityLocationRecord location)
        {
            foreach (var record in locations) if (record.id == id)
            { location = JsonUtility.FromJson<CityLocationRecord>(JsonUtility.ToJson(record)); return true; }
            location = null; return false;
        }
    }
}
