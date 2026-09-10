using System;

namespace NfsMwRemaster.Driving
{
    /// <summary>Revision-aware debounce; a completed older snapshot never clears newer dirt.</summary>
    public sealed class CareerAutosavePolicy
    {
        private readonly double debounce, interval, maximumDirty, retry;
        private double firstDirty, lastDirty, lastAttempt = double.NegativeInfinity;
        private long inFlight;
        public long Revision { get; private set; }
        public long SavedRevision { get; private set; }
        public bool Dirty => Revision > SavedRevision;
        public bool Saving { get; private set; }
        public CareerAutosavePolicy(double debounceSeconds = 2, double minimumInterval = 5,
            double maximumDirtySeconds = 30, double retrySeconds = 5)
        {
            foreach (double value in new[] { debounceSeconds, minimumInterval, maximumDirtySeconds, retrySeconds })
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException();
            debounce = debounceSeconds; interval = minimumInterval; maximumDirty = maximumDirtySeconds; retry = retrySeconds;
        }
        public void Changed(double now)
        {
            if (!Dirty) firstDirty = now;
            lastDirty = now; Revision = checked(Revision + 1);
        }
        public bool Due(double now, bool safe, bool urgent = false) => safe && Dirty && !Saving
            && now - lastAttempt >= (urgent ? 0 : interval)
            && (urgent || now - lastDirty >= debounce || now - firstDirty >= maximumDirty);
        public long Begin(double now)
        {
            if (Saving || !Dirty) throw new InvalidOperationException("No unsaved snapshot is ready.");
            Saving = true; lastAttempt = now; return inFlight = Revision;
        }
        public void Complete(bool success, double now)
        {
            if (!Saving) throw new InvalidOperationException("No save is in progress.");
            if (success) SavedRevision = inFlight;
            else lastAttempt = now + retry - interval;
            Saving = false;
        }
        public void AcknowledgeCurrent()
        {
            if (Saving) throw new InvalidOperationException("A snapshot is still in flight.");
            SavedRevision = Revision;
        }
    }
}
