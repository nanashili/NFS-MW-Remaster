using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum RadioCueKind { Observed, TrafficStop, Pursuit, Searching, Cooldown, Reinforcements, Roadblock, Spikes, UnitDisabled, HeavyImpact, Escaped, Arrested, PaidFine }
    [Serializable] public sealed class PoliceRadioCue
    {
        public RadioCueKind kind;
        public AudioClip[] variants = Array.Empty<AudioClip>();
        [TextArea] public string subtitle;
        [Range(0, 255)] public int priority = 60;
        [Min(0)] public float cooldown = 12;
        [Min(0.1f)] public float expiry = 5;
        public bool interruptible = true;
        [Range(0, 5)] public int minimumHeat, maximumHeat = 5;
    }
    public readonly struct RadioRequest
    {
        public readonly RadioCueKind Kind;
        public readonly int Priority, Encounter;
        public readonly double Expires;
        public RadioRequest(RadioCueKind kind, int priority, int encounter, double expires)
        { Kind = kind; Priority = priority; Encounter = encounter; Expires = expires; }
    }
    /// <summary>Fixed-capacity queue; newest context cannot revive a stale request from an earlier encounter.</summary>
    public sealed class PoliceRadioQueue
    {
        private readonly RadioRequest[] items;
        private readonly double[] nextAllowed = new double[14];
        private int count;
        public int Count => count;
        public PoliceRadioQueue(int capacity = 12)
        { if (capacity < 1 || capacity > 64) throw new ArgumentOutOfRangeException(nameof(capacity)); items = new RadioRequest[capacity]; }
        public bool Enqueue(RadioRequest request, double now)
        {
            int kind = (int)request.Kind;
            if (kind < 0 || kind >= nextAllowed.Length || request.Expires <= now || now < nextAllowed[kind]) return false;
            for (int i = 0; i < count; i++) if (items[i].Kind == request.Kind && items[i].Encounter == request.Encounter) return false;
            if (count == items.Length)
            {
                int worst = 0;
                for (int i = 1; i < count; i++) if (items[i].Priority > items[worst].Priority) worst = i;
                if (items[worst].Priority <= request.Priority) return false;
                items[worst] = request;
            }
            else items[count++] = request;
            return true;
        }
        public bool TryTake(double now, int encounter, uint validKinds, int priorityCeiling, out RadioRequest request)
        {
            int best = -1;
            for (int i = count - 1; i >= 0; i--)
            {
                var item = items[i];
                if (item.Expires <= now || item.Encounter != encounter || (validKinds & (1u << (int)item.Kind)) == 0 || now < nextAllowed[(int)item.Kind])
                { items[i] = items[--count]; continue; }
            }
            for (int i = 0; i < count; i++)
                if (items[i].Priority < priorityCeiling && (best < 0 || items[i].Priority < items[best].Priority)) best = i;
            request = best >= 0 ? items[best] : default;
            if (best < 0) return false;
            items[best] = items[--count]; return true;
        }
        public void MarkPlayed(RadioCueKind kind, double now, double cooldown)
        { nextAllowed[(int)kind] = now + Math.Max(0, cooldown); }
        public void Clear() { count = 0; Array.Clear(nextAllowed, 0, nextAllowed.Length); }
    }
}
