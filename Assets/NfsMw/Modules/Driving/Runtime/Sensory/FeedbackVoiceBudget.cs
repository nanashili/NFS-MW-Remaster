using System;

namespace NfsMwRemaster.Driving
{
    public readonly struct FeedbackVoiceLease
    {
        public readonly int Index;
        public readonly uint Generation;
        public bool IsValid => Generation != 0;
        public FeedbackVoiceLease(int index, uint generation) { Index = index; Generation = generation; }
    }

    /// <summary>Fixed-capacity admission, lower priority number wins. No allocation after construction.</summary>
    public sealed class FeedbackVoiceBudget
    {
        private readonly bool[] active;
        private readonly uint[] generations;
        private readonly int[] priorities;
        public int Count { get; private set; }
        public int Capacity => active.Length;
        public FeedbackVoiceBudget(int capacity)
        {
            if (capacity < 1 || capacity > 64) throw new ArgumentOutOfRangeException(nameof(capacity));
            active = new bool[capacity]; generations = new uint[capacity]; priorities = new int[capacity];
        }
        public FeedbackVoiceLease Acquire(int priority)
        {
            int candidate = -1;
            for (int i = 0; i < active.Length; i++)
            {
                if (!active[i]) { candidate = i; break; }
                if (priorities[i] > priority && (candidate < 0 || priorities[i] > priorities[candidate])) candidate = i;
            }
            if (candidate < 0) return default;
            if (!active[candidate]) Count++;
            active[candidate] = true; priorities[candidate] = priority;
            if (++generations[candidate] == 0) generations[candidate]++;
            return new FeedbackVoiceLease(candidate, generations[candidate]);
        }
        public bool Owns(FeedbackVoiceLease lease) => lease.IsValid && lease.Index >= 0 && lease.Index < active.Length
            && active[lease.Index] && generations[lease.Index] == lease.Generation;
        public void Release(FeedbackVoiceLease lease)
        {
            if (!Owns(lease)) return;
            active[lease.Index] = false; Count--;
        }
    }
}
