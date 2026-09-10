using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public sealed class TrafficTestControls : MonoBehaviour
    {
        [SerializeField] private string scenario;
        [SerializeField] private TrafficWorldDirector world;
        [SerializeField] private GameObject obstacle;
        [SerializeField] private VehiclePursuitDirector pursuit;
        [SerializeField] private VehiclePoliceUnit police;
        public void Configure(string label, TrafficWorldDirector director, GameObject queue, VehiclePursuitDirector response, VehiclePoliceUnit unit)
        { scenario = label; world = director; obstacle = queue; pursuit = response; police = unit; }
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12, 12, 530, 140), GUI.skin.box);
            GUILayout.Label("TRAFFIC VALIDATION / " + scenario + " — uncalibrated engineering test");
            GUILayout.Label("WASD: drive | Space: handbrake | F9: diagnostics/CSV | Select traffic world: decision gizmos");
            GUILayout.Label("Initial trips are authored at scene load. Live arrivals use hidden, gap-safe entry portals.");
            if (obstacle != null && GUILayout.Button(obstacle.activeSelf ? "Clear queue obstruction" : "Restore queue obstruction (test fixture)"))
                obstacle.SetActive(!obstacle.activeSelf);
            if (pursuit != null && police != null && !pursuit.IsActive && GUILayout.Button("Report test offence to this officer / start siren encounter"))
                pursuit.ReportObservedOffence(police, PoliceOffence.Collision, out _);
            GUILayout.EndArea();
        }
    }
}
