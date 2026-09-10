using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public sealed class FeedbackRecording
    {
        private readonly VehicleFeedbackFrame[] frames;
        private int cursor;
        public int Count { get; private set; }
        public int Capacity => frames.Length;
        public FeedbackRecording(int capacity = 3000)
        { if (capacity < 1 || capacity > 6000) throw new ArgumentOutOfRangeException(nameof(capacity)); frames = new VehicleFeedbackFrame[capacity]; }
        public void Add(VehicleFeedbackFrame frame) { frames[cursor] = frame; cursor = (cursor + 1) % frames.Length; Count = Math.Min(Count + 1, frames.Length); }
        public VehicleFeedbackFrame[] Snapshot()
        {
            var result = new VehicleFeedbackFrame[Count];
            int start = (cursor - Count + frames.Length) % frames.Length;
            for (int i = 0; i < Count; i++) result[i] = frames[(start + i) % frames.Length];
            return result;
        }
        public void Clear() { Count = cursor = 0; }
    }

}
