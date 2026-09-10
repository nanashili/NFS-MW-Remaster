using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    [Serializable]
    public struct RoadId : IEquatable<RoadId>
    {
        [SerializeField] private string value;
        public bool IsValid => Guid.TryParseExact(value, "N", out _);
        public static RoadId New() => new RoadId { value = Guid.NewGuid().ToString("N") };
        public bool Equals(RoadId other) => string.Equals(value, other.value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is RoadId other && Equals(other);
        public override int GetHashCode() => value == null ? 0 : StringComparer.Ordinal.GetHashCode(value);
        public override string ToString() => value ?? string.Empty;
        public static bool operator ==(RoadId a, RoadId b) => a.Equals(b);
        public static bool operator !=(RoadId a, RoadId b) => !a.Equals(b);
    }
}
