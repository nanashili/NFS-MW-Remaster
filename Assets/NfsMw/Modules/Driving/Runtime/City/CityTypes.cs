using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum CityLandUse { Industrial, Commercial, Residential, Landmark, Parking, Park, ServiceYard }
    public enum CityOwnership { Generated, Overridden, Pinned, Detached }
    public enum CityRoof { Flat, Pitched, Sawtooth }
    public enum CitySeverity { Info, Warning, Error }

    [Serializable] public sealed class CityRing { public List<Vector2> points = new List<Vector2>(); }
    [Serializable] public sealed class CityPolygon
    {
        public List<Vector2> outline = new List<Vector2>();
        public List<CityRing> holes = new List<CityRing>();
        public CityPolygon Copy() => JsonUtility.FromJson<CityPolygon>(JsonUtility.ToJson(this));
        public static CityPolygon Rectangle(float x, float z, float width, float depth) => new CityPolygon
        { outline = new List<Vector2> { new Vector2(x,z), new Vector2(x+width,z), new Vector2(x+width,z+depth), new Vector2(x,z+depth) } };
    }
    [Serializable] public sealed class CityBlock
    {
        public string id, label;
        public CityPolygon polygon = new CityPolygon();
        public bool locked;
    }
    [Serializable] public sealed class CityEntrance
    {
        public bool enabled, approvedPublicAccess;
        public RoadId laneId;
        [Min(0)] public float laneDistance;
        public Vector3 localPosition;
        [Min(0.1f)] public float width = 5, height = 4.5f;
        [Min(0)] public float approachLength = 8;
        [Range(0,45)] public float maximumSlopeDegrees = 12;
    }
    [Serializable] public sealed class CityParcel
    {
        public string id, blockId, label;
        public CityPolygon polygon = new CityPolygon();
        public CityLandUse use;
        public CityKit kit;
        public int seed = 1;
        public bool locked, generate = true;
        [Min(0)] public float setback = 3;
        public float padHeight;
        [Range(1,60)] public int floors = 2;
        [Range(0,1)] public float coverage = 0.6f;
        public CityEntrance entrance = new CityEntrance();
    }
    [Serializable] public sealed class CityOverride
    {
        public string key;
        public CityOwnership state;
        public Vector3 position, euler, scale = Vector3.one;
    }
    [Serializable] public sealed class CityReservation
    {
        public string id, label;
        public CityPolygon polygon = new CityPolygon();
        public float minimumHeight, maximumHeight = 10;
    }
    [Serializable] public sealed class CityDiagnostic
    {
        public string rule, owner, message, action;
        public CitySeverity severity;
        public Vector3 localPosition;
        public CityDiagnostic(string rule, string owner, string message, CitySeverity severity = CitySeverity.Error,
            string action = "Inspect the highlighted source and correct it before committing.")
        { this.rule = rule; this.owner = owner; this.message = message; this.severity = severity; this.action = action; }
        public override string ToString() => severity + " · " + rule + " · " + message;
    }
}
