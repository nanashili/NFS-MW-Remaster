using System;
using System.Globalization;
using UnityEngine;

namespace NfsMwRemaster.Diagnostics
{
    public enum MetricValidity { Valid, Unknown, Unsupported, Stale, Error }
    public enum MetricKind { Gauge, Counter, Duration, State }
    public enum SamplePhase { OwnerBoundary, LateUpdate, Render, Event }
    [Serializable] public struct DiagnosticClock
    {
        public double realtime, game, dsp;
        public long tick;
        public SamplePhase phase;
        public static DiagnosticClock Now(SamplePhase phase, long tick = -1) => new DiagnosticClock
        { realtime = Time.realtimeSinceStartupAsDouble, game = Time.timeAsDouble, dsp = AudioSettings.dspTime, tick = tick, phase = phase };
    }
    [Serializable] public struct DiagnosticMetric
    {
        public string id, unit, text;
        public double value;
        public MetricKind kind;
        public MetricValidity validity;
        public static DiagnosticMetric Number(string id, double value, string unit = "", MetricKind kind = MetricKind.Gauge) =>
            new DiagnosticMetric { id = id, value = double.IsFinite(value) ? value : 0, unit = unit, kind = kind,
                validity = double.IsFinite(value) ? MetricValidity.Valid : MetricValidity.Unknown };
        public static DiagnosticMetric State(string id, string text) => new DiagnosticMetric
        { id = id, text = text, kind = MetricKind.State, validity = MetricValidity.Valid, unit = "" };
        public static DiagnosticMetric Missing(string id, MetricValidity validity = MetricValidity.Unsupported) =>
            new DiagnosticMetric { id = id, validity = validity, unit = "" };
        public override string ToString() => validity != MetricValidity.Valid ? validity.ToString() : kind == MetricKind.State
            ? text : value.ToString("0.###", CultureInfo.InvariantCulture) + " " + unit;
    }
    [Serializable] public struct DiagnosticLine
    {
        public Vector3 from, to;
        public Color color;
    }
    public sealed class DiagnosticSnapshot
    {
        public readonly string ProviderId, EntityId, Revision;
        public readonly long Generation;
        public readonly DiagnosticClock Clock;
        private readonly DiagnosticMetric[] metrics;
        private readonly DiagnosticLine[] lines;
        public int Count => metrics.Length;
        public int LineCount => lines.Length;
        public DiagnosticMetric this[int i] => metrics[i];
        public DiagnosticLine Line(int i) => lines[i];
        public DiagnosticSnapshot(string providerId, string entityId, long generation, string revision, DiagnosticClock clock,
            DiagnosticMetric[] values, DiagnosticLine[] geometry = null)
        {
            if (string.IsNullOrWhiteSpace(providerId) || providerId.Length > 128 || string.IsNullOrWhiteSpace(entityId) || entityId.Length > 128 || generation < 1)
                throw new ArgumentException("A bounded provider/entity ID and positive generation are required.");
            if (!double.IsFinite(clock.realtime) || !double.IsFinite(clock.game) || !double.IsFinite(clock.dsp)) throw new ArgumentException("Invalid clock.");
            if (values == null || values.Length > 64 || (geometry?.Length ?? 0) > 128) throw new ArgumentException("Snapshot budget exceeded.");
            ProviderId = providerId; EntityId = entityId; Generation = generation; Revision = Bound(revision,128); Clock = clock;
            metrics = (DiagnosticMetric[])values.Clone(); lines = geometry == null ? Array.Empty<DiagnosticLine>() : (DiagnosticLine[])geometry.Clone();
            var ids = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < metrics.Length; i++)
            {
                var m = metrics[i];
                if (string.IsNullOrWhiteSpace(m.id) || m.id.Length > 96 || !ids.Add(m.id)) throw new ArgumentException("Invalid or duplicate metric ID.");
                m.unit = Bound(m.unit, 24); m.text = Bound(m.text, 256);
                if (!double.IsFinite(m.value)) { m.value = 0; m.validity = MetricValidity.Unknown; }
                metrics[i] = m;
            }
            foreach(var line in lines) if (!Finite(line.from) || !Finite(line.to)) throw new ArgumentException("Non-finite geometry.");
        }
        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        public static string Bound(string value, int max) => string.IsNullOrEmpty(value) ? "" : value.Length <= max ? value : value.Substring(0,max);
    }
    public interface IDiagnosticProvider
    {
        string Id { get; }
        string Category { get; }
        string Label { get; }
        double Interval { get; }
        void SetDemand(bool active, bool geometry);
        DiagnosticSnapshot Sample(DiagnosticClock clock);
    }
    public sealed class DiagnosticRing<T>
    {
        private readonly T[] values;
        private int next;
        public int Count { get; private set; }
        public long Overwritten { get; private set; }
        public int Capacity => values.Length;
        public DiagnosticRing(int capacity) { if(capacity < 1 || capacity > 16384)throw new ArgumentOutOfRangeException(nameof(capacity));values=new T[capacity]; }
        public void Add(T value) { values[next]=value;next=(next+1)%values.Length;if(Count<values.Length)Count++;else Overwritten++; }
        public T this[int i] => i < 0 || i >= Count ? throw new ArgumentOutOfRangeException(nameof(i)) : values[(next-Count+i+values.Length)%values.Length];
        public void Clear() { Array.Clear(values,0,values.Length);next=0;Count=0;Overwritten=0; }
    }
    [Serializable] public struct DiagnosticEvent
    {
        public DiagnosticClock clock;
        public string provider, code;
        public int severity;
    }
    public static class DiagnosticBuild
    {
        public static bool Enabled
        {
            get {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }
    }
}
