namespace NfsMwRemaster.Driving
{
    /// <summary>One-way historical compatibility. This adapter never returns heat to pursuit authority.</summary>
    internal sealed class PoliceCareerAdapter
    {
        private readonly VehicleBountySystem history;
        public PoliceCareerAdapter(VehicleBountySystem history) { this.history = history; }
        public void Begin()
        {
            if (history == null) return;
            if (history.PursuitActive)
            {
                // A migrated legacy save may contain an unfinished bounty encounter. It is not this new police encounter.
                // Preserve banked career totals/counters, discard only its uncommitted transient bucket, and award nothing.
                var snapshot = new CareerProfileData(); history.Capture(snapshot);
                snapshot.bounty.pursuitActive = false; snapshot.bounty.heatLevel = 0; snapshot.bounty.Normalize();
                history.Restore(snapshot, out _);
            }
            history.TryStartPursuit(out _);
        }
        public void Sync(int engagement)
        { if (history != null && history.HeatLevel != engagement) history.SetHeatLevel(engagement); }
        public void Record(VehicleBountyEventKind kind, int count)
        { if (history != null && history.PursuitActive) history.TryRecordEvent(kind, count, out _, out _); }
        public void Advance(float dt)
        { if (history != null && history.PursuitActive) history.TryAdvancePursuitTime(dt, out _, out _); }
        public int PendingBounty => history != null ? history.CurrentPursuitBounty : 0;
    }
}
