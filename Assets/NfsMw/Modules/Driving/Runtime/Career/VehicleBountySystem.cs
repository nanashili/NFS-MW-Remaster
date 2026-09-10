using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Unity-facing bounty facade. Police and world modules submit events;
    /// menus subscribe to snapshots and pursuit results without knowing the
    /// underlying rules or persistence implementation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleBountySystem :
        MonoBehaviour,
        ICareerProfileParticipant,
        IVehicleBountyTracker,
        IVehiclePursuitHeatSink
    {
        [SerializeField] private VehicleBountyRules rules = null!;
        [SerializeField, Min(0)] private int startingHeatLevel;

        private VehicleBountyProgress progress = null!;

        public event Action<VehicleBountySnapshot> StateChanged;

        public event Action<VehicleBountyPursuitResult> PursuitEscaped;

        public event Action<VehicleBountyPursuitResult> PursuitBusted;

        public string ProfileSectionId
        {
            get { return "bounty"; }
        }

        public VehicleBountyRules Rules
        {
            get { return rules; }
        }

        public VehicleBountySnapshot Snapshot
        {
            get { return Progress.CreateSnapshot(); }
        }

        public int MaxHeatLevel
        {
            get { return Progress.Rules.MaxHeatLevel; }
        }

        public int TotalBounty
        {
            get { return Progress.TotalBounty; }
        }

        public int CurrentPursuitBounty
        {
            get { return Progress.CurrentPursuitBounty; }
        }

        public int HeatLevel
        {
            get { return Progress.HeatLevel; }
        }

        public bool PursuitActive
        {
            get { return Progress.PursuitActive; }
        }

        public int PursuitsEscaped
        {
            get { return Progress.PursuitsEscaped; }
        }

        public int PursuitsBusted
        {
            get { return Progress.PursuitsBusted; }
        }

        public void SetRules(VehicleBountyRules configuredRules)
        {
            rules = configuredRules;
            progress = null!;
        }

        public void SetStartingHeatLevel(int configuredHeatLevel)
        {
            startingHeatLevel = Mathf.Max(0, configuredHeatLevel);
            if (progress != null)
            {
                progress.SetHeatLevel(startingHeatLevel);
                PublishState();
            }
        }

        public bool TryStartPursuit(out string failure)
        {
            bool started = Progress.TryStartPursuit(out failure);
            if (started)
            {
                PublishState();
            }

            return started;
        }

        public bool TryRecordEvent(
            VehicleBountyEventKind eventKind,
            int count,
            out VehicleBountyAward award,
            out string failure)
        {
            bool recorded = Progress.TryRecord(
                eventKind,
                count,
                out award,
                out failure);
            if (recorded)
            {
                PublishState();
            }

            return recorded;
        }

        public bool TryAdvancePursuitTime(
            float seconds,
            out VehicleBountyAward award,
            out string failure)
        {
            bool advanced = Progress.TryAdvanceTime(
                seconds,
                out award,
                out failure);
            if (advanced)
            {
                PublishState();
            }

            return advanced;
        }

        public bool TryEscape(
            out VehicleBountyPursuitResult result,
            out string failure)
        {
            bool escaped = Progress.TryEscape(out result, out failure);
            if (escaped)
            {
                PublishState();
                PursuitEscaped?.Invoke(result);
            }

            return escaped;
        }

        public bool TryBust(
            out VehicleBountyPursuitResult result,
            out string failure)
        {
            bool busted = Progress.TryBust(out result, out failure);
            if (busted)
            {
                PublishState();
                PursuitBusted?.Invoke(result);
            }

            return busted;
        }

        public void SetHeatLevel(int configuredHeatLevel)
        {
            Progress.SetHeatLevel(configuredHeatLevel);
            PublishState();
        }

        public void Capture(CareerProfileData profile)
        {
            if (profile == null)
            {
                return;
            }

            profile.Normalize();
            Progress.Capture(profile.bounty);
        }

        public bool Restore(CareerProfileData profile, out string failure)
        {
            if (profile == null)
            {
                failure = "Profile is null.";
                return false;
            }

            profile.Normalize();
            bool restored = Progress.Restore(profile.bounty, out failure);
            if (restored)
            {
                PublishState();
            }

            return restored;
        }

        private VehicleBountyProgress Progress
        {
            get
            {
                if (progress == null)
                {
                    IVehicleBountyRules configuredRules = rules != null
                        ? rules
                        : new VehicleBountyDefaultRules();
                    progress = new VehicleBountyProgress(configuredRules);
                    progress.SetHeatLevel(startingHeatLevel);
                }

                return progress;
            }
        }

        private void Awake()
        {
            _ = Progress;
        }

        private void OnValidate()
        {
            startingHeatLevel = Mathf.Max(0, startingHeatLevel);
        }

        private void PublishState()
        {
            StateChanged?.Invoke(Progress.CreateSnapshot());
        }
    }
}
