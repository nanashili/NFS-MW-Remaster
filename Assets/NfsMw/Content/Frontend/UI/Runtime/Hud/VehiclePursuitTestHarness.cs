using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Playable IMGUI surface for the pursuit sandbox. It deliberately talks
    /// through the pursuit and bounty interfaces so it also serves as a
    /// smoke-test for integration seams.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehiclePursuitTestHarness : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour directorComponent = null!;
        [SerializeField] private MonoBehaviour trackerComponent = null!;

        private IVehiclePursuitDirector director;
        private IVehicleBountyTracker tracker;
        private VehiclePursuitDirector concreteDirector;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle statusStyle;
        private string status =
            "Start a pursuit, then drive hard and try to break contact.";

        public void Configure(
            MonoBehaviour configuredDirector,
            MonoBehaviour configuredTracker)
        {
            directorComponent = configuredDirector;
            trackerComponent = configuredTracker;
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
            if (director == null)
            {
                DrawUnavailable("No IVehiclePursuitDirector is attached.");
                return;
            }

            GUILayout.BeginArea(
                new Rect(
                    16f,
                    16f,
                    700f,
                    Mathf.Max(360f, Screen.height - 32f)),
                GUI.skin.box);
            GUILayout.Label("NFS 2015-STYLE PURSUIT TEST", titleStyle);
            GUILayout.Label(
                "WASD / arrows drive. Space handbrakes. Left Shift uses nitrous. "
                + "C changes camera.",
                bodyStyle);
            DrawPursuitSnapshot();
            if (concreteDirector != null)
            {
                GUILayout.Label($"FINE $ {concreteDirector.CurrentFine} / {concreteDirector.EncounterState} / bust {concreteDirector.BustProgress:P0} / escape {concreteDirector.CooldownProgress:P0}");
                if (concreteDirector.CanPayFine && GUILayout.Button("Pay offered fine (stop first)")) concreteDirector.TryPayFine(out status);
                if (concreteDirector.EncounterState == PoliceEncounterState.OutcomePending && GUILayout.Button("Retry pending settlement")) concreteDirector.TryAcknowledgeOutcome(out status);
            }
            DrawPursuitControls();
            DrawBountyControls();
            GUILayout.Label(status, statusStyle);
            GUILayout.EndArea();
        }

        private void DrawPursuitSnapshot()
        {
            VehiclePoliceResponseTier tier = director.CurrentTier;
            string targetId = director.Target == null
                ? "none"
                : director.Target.TargetId;
            GUILayout.Label(
                string.Format(
                    "PHASE: {0}  |  HEAT: {1}/{2}  |  UNITS: {3}/{4}\n"
                    + "CONTACT LOST: {5:0.0}s  |  PHASE TIME: {6:0.0}s\n"
                    + "TARGET: {7}  |  SEARCH: {8:0.0}s  |  DISENGAGE: {9:0}m",
                    director.Phase,
                    director.HeatLevel,
                    director.MaxHeatLevel,
                    director.ActiveUnitCount,
                    director.RegisteredUnitCount,
                    director.TimeSinceTargetSeen,
                    director.TimeInPhase,
                    targetId,
                    tier == null ? 0f : tier.SearchDuration,
                    tier == null ? 0f : tier.DisengageRadius),
                bodyStyle);

            if (tracker != null)
            {
                VehicleBountySnapshot snapshot = tracker.Snapshot;
                GUILayout.Label(
                    string.Format(
                        "BOUNTY: ${0:N0}  |  PURSUIT: ${1:N0}  |  "
                        + "DISABLED: {2}  |  TRADE PAINT: {3}\n"
                        + "ESCAPED: {4}  |  BUSTED: {5}",
                        snapshot.TotalBounty,
                        snapshot.CurrentPursuitBounty,
                        snapshot.PoliceVehiclesDisabled,
                        snapshot.TradePaintEvents,
                        snapshot.PursuitsEscaped,
                        snapshot.PursuitsBusted),
                    bodyStyle);
            }
        }

        private void DrawPursuitControls()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("START PURSUIT", GUILayout.Height(32f)))
            {
                status = director.TryStartPursuit(out string failure)
                    ? "All available units alerted. Stay moving."
                    : failure;
            }

            if (GUILayout.Button("HEAT 1", GUILayout.Height(32f)))
            {
                SetHeat(1);
            }

            if (GUILayout.Button("HEAT 3", GUILayout.Height(32f)))
            {
                SetHeat(3);
            }

            if (GUILayout.Button("HEAT 5", GUILayout.Height(32f)))
            {
                SetHeat(5);
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("DISABLE LEAD COP"))
            {
                status = DisableLeadUnit();
            }

            if (GUILayout.Button("FORCE ESCAPE (TEST)"))
            {
                status = director.TryForceEscape(out string failure)
                    ? "Forced escape recorded."
                    : failure;
            }

            if (GUILayout.Button("FORCE BUST (TEST)"))
            {
                status = director.TryForceBust(out string failure)
                    ? "Forced bust recorded."
                    : failure;
            }

            GUILayout.EndHorizontal();
        }

        private void DrawBountyControls()
        {
            GUILayout.Label("WORLD FACTS", bodyStyle);
            GUILayout.BeginHorizontal();
            DrawEventButton("ROADBLOCK DODGED", VehicleBountyEventKind.RoadblockDodged);
            DrawEventButton("SPIKES DODGED", VehicleBountyEventKind.SpikeStripDodged);
            DrawEventButton("PROPERTY DAMAGE", VehicleBountyEventKind.PropertyDamage);
            GUILayout.EndHorizontal();
        }

        private void DrawEventButton(
            string label,
            VehicleBountyEventKind eventKind)
        {
            if (GUILayout.Button(label))
            {
                status = director.TryRecordBountyEvent(
                    eventKind,
                    1,
                    out string failure)
                    ? label + " recorded."
                    : failure;
            }
        }

        private void SetHeat(int heat)
        {
            status = director.TrySetHeatLevel(heat, out string failure)
                ? "Heat set to level " + heat + "."
                : failure;
        }

        private string DisableLeadUnit()
        {
            if (concreteDirector == null)
            {
                return "No concrete pursuit director is attached.";
            }

            return concreteDirector.TryDisableLeadUnit(out string failure)
                ? "Lead unit disabled; the response is still coordinated."
                : failure;
        }

        private void DrawUnavailable(string message)
        {
            GUILayout.BeginArea(new Rect(16f, 16f, 700f, 100f), GUI.skin.box);
            GUILayout.Label("NFS 2015-STYLE PURSUIT TEST", titleStyle);
            GUILayout.Label(message, bodyStyle);
            GUILayout.EndArea();
        }

        private void ResolvePorts()
        {
            director = directorComponent as IVehiclePursuitDirector;
            tracker = trackerComponent as IVehicleBountyTracker;
            concreteDirector = directorComponent as VehiclePursuitDirector;
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
            statusStyle.normal.textColor = new Color(1f, 0.36f, 0.28f);
            statusStyle.wordWrap = true;
        }
    }
}
