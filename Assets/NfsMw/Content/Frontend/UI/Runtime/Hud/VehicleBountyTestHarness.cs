using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Manual pursuit simulator for the bounty test scene. It emits the same
    /// event facts that police, traffic, and world systems will emit later,
    /// then displays the tracker snapshot and persistence result.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleBountyTestHarness : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour trackerComponent = null!;
        [SerializeField] private MonoBehaviour profileComponent = null!;

        private IVehicleBountyTracker tracker;
        private ICareerProfileService profile;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle statusStyle;
        private string status = "Start a pursuit to emit bounty facts.";

        public void Configure(
            MonoBehaviour configuredTracker,
            MonoBehaviour configuredProfile)
        {
            trackerComponent = configuredTracker;
            profileComponent = configuredProfile;
            ResolvePorts();
        }

        private void Awake()
        {
            ResolvePorts();
        }

        private void OnGUI()
        {
            ResolvePorts();
            BuildStyles();
            if (tracker == null)
            {
                DrawUnavailable("No IVehicleBountyTracker is attached.");
                return;
            }

            GUILayout.BeginArea(new Rect(16f, 16f, 610f, Mathf.Max(300f, Screen.height - 32f)), GUI.skin.box);
            GUILayout.Label("BOUNTY TEST SCENE", titleStyle);
            GUILayout.Label(
                "Emit pursuit facts, escape or bust, then verify the profile round-trip.",
                bodyStyle);
            DrawSnapshot();
            DrawPursuitControls();
            DrawEventControls();
            GUILayout.Label(status, statusStyle);
            DrawProfileButtons();
            GUILayout.EndArea();
        }

        private void DrawSnapshot()
        {
            VehicleBountySnapshot snapshot = tracker.Snapshot;
            GUILayout.Label(
                string.Format(
                    "TOTAL BOUNTY: ${0:N0}  |  PURSUIT: ${1:N0}\nHEAT: {2}/{3}  |  ACTIVE: {4}  |  TIME: {5:0.0}s",
                    snapshot.TotalBounty,
                    snapshot.CurrentPursuitBounty,
                    snapshot.HeatLevel,
                    tracker.MaxHeatLevel,
                    snapshot.PursuitActive,
                    snapshot.PursuitDurationSeconds),
                bodyStyle);
            GUILayout.Label(
                string.Format(
                    "DISABLED {0}  |  ROADBLOCKS {1}  |  SPIKES {2}  |  DAMAGE {3}\nCOST TO STATE {4}  |  TRADE PAINT {5}  |  INFRACTIONS {6}\nESCAPED {7}  |  BUSTED {8}",
                    snapshot.PoliceVehiclesDisabled,
                    snapshot.RoadblocksDodged,
                    snapshot.SpikeStripsDodged,
                    snapshot.PropertyDamageEvents,
                    snapshot.CostToState,
                    snapshot.TradePaintEvents,
                    snapshot.TrafficInfractions,
                    snapshot.PursuitsEscaped,
                    snapshot.PursuitsBusted),
                bodyStyle);
        }

        private void DrawPursuitControls()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("START PURSUIT", GUILayout.Height(32f)))
            {
                if (!tracker.TryStartPursuit(out string failure))
                {
                    status = failure;
                }
                else
                {
                    status = "Pursuit started.";
                }
            }

            if (GUILayout.Button("+1 SECOND", GUILayout.Height(32f)))
            {
                if (!tracker.TryAdvancePursuitTime(
                    1f,
                    out VehicleBountyAward award,
                    out string failure))
                {
                    status = failure;
                }
                else
                {
                    status = FormatAward(award);
                }
            }

            if (GUILayout.Button("ESCAPE", GUILayout.Height(32f)))
            {
                if (!tracker.TryEscape(
                    out VehicleBountyPursuitResult result,
                    out string failure))
                {
                    status = failure;
                }
                else
                {
                    status = string.Format(
                        "Escaped: +${0:N0} bounty. Career total ${1:N0}.",
                        result.BountyEarned,
                        result.CareerBounty);
                }
            }

            if (GUILayout.Button("BUST", GUILayout.Height(32f)))
            {
                if (!tracker.TryBust(
                    out VehicleBountyPursuitResult result,
                    out string failure))
                {
                    status = failure;
                }
                else
                {
                    status = string.Format(
                        "Busted: lost provisional ${0:N0} bounty.",
                        result.BountyLost);
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawEventControls()
        {
            GUILayout.Label("PURSUIT FACTS", bodyStyle);
            DrawEventRow(
                "POLICE DISABLED",
                VehicleBountyEventKind.PoliceVehicleDisabled,
                1);
            DrawEventRow("ROADBLOCK DODGED", VehicleBountyEventKind.RoadblockDodged, 1);
            DrawEventRow("SPIKE STRIP DODGED", VehicleBountyEventKind.SpikeStripDodged, 1);
            DrawEventRow("PROPERTY DAMAGE", VehicleBountyEventKind.PropertyDamage, 1);
            DrawEventRow("COST TO STATE", VehicleBountyEventKind.CostToState, 1);
            DrawEventRow("TRADE PAINT", VehicleBountyEventKind.TradePaint, 1);
            DrawEventRow("TRAFFIC INFRACTION", VehicleBountyEventKind.TrafficInfraction, 1);
        }

        private void DrawEventRow(
            string label,
            VehicleBountyEventKind eventKind,
            int count)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, bodyStyle, GUILayout.Width(220f));
            if (GUILayout.Button("EMIT", GUILayout.Width(80f)))
            {
                if (!tracker.TryRecordEvent(
                    eventKind,
                    count,
                    out VehicleBountyAward award,
                    out string failure))
                {
                    status = failure;
                }
                else
                {
                    status = FormatAward(award);
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawProfileButtons()
        {
            if (profile == null)
            {
                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("SAVE PROFILE"))
            {
                status = profile.TrySave(out string failure)
                    ? "Profile saved."
                    : failure;
            }

            if (GUILayout.Button("LOAD PROFILE"))
            {
                status = profile.TryLoad(out string failure)
                    ? "Profile loaded."
                    : failure;
            }

            GUILayout.EndHorizontal();
        }

        private string FormatAward(VehicleBountyAward award)
        {
            if (award == null)
            {
                return "No award returned.";
            }

            string heat = award.HeatDelta >= 0
                ? "+" + award.HeatDelta
                : award.HeatDelta.ToString();
            return string.Format(
                "{0} x{1}: +${2:N0} bounty, heat {3}.",
                award.EventKind,
                award.Count,
                award.Bounty,
                heat);
        }

        private void DrawUnavailable(string message)
        {
            GUILayout.BeginArea(new Rect(16f, 16f, 610f, 100f), GUI.skin.box);
            GUILayout.Label("BOUNTY TEST SCENE", titleStyle);
            GUILayout.Label(message, bodyStyle);
            GUILayout.EndArea();
        }

        private void ResolvePorts()
        {
            tracker = trackerComponent as IVehicleBountyTracker;
            profile = profileComponent as ICareerProfileService;
        }

        private void BuildStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 20;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = Color.white;

            bodyStyle = new GUIStyle(GUI.skin.label);
            bodyStyle.fontSize = 13;
            bodyStyle.normal.textColor = new Color(0.92f, 0.95f, 1f);

            statusStyle = new GUIStyle(bodyStyle);
            statusStyle.normal.textColor = new Color(1f, 0.82f, 0.35f);
            statusStyle.wordWrap = true;
        }
    }
}
