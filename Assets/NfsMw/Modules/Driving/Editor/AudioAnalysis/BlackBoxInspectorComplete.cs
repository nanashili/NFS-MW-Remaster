using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    public sealed partial class BlackBoxInspectorWindow
    {
        private BlackBoxCompleteAttachment completePlan;
        private string completeDiscoverySession;
        [SerializeField] private bool manualEngineAttachment;
        private void DrawCompleteAttachment()
        {
            if (completeDiscoverySession != session.id)
            {
                completeDiscoverySession = session.id; completePlan = null;
                if (string.IsNullOrEmpty(session.completePackFolder))
                    foreach (var source in session.sources)
                    {
                        var directory = new DirectoryInfo(source.root);
                        for (int i = 0; directory != null && i < 5; i++, directory = directory.Parent)
                            if (directory.Exists && Directory.Exists(Path.Combine(directory.FullName, "SOUND")) && directory.GetFiles("*.nfsms").Length == 1)
                            { session.completePackFolder = directory.FullName; break; }
                        if (session.completePackFolder.Length > 0) break;
                    }
                if (string.IsNullOrEmpty(session.gameInstallationFolder))
                {
                    string saved = EditorPrefs.GetString("BlackBox.MostWantedInstallation", "");
                    string crossover = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library/Application Support/CrossOver/Bottles/NFS MW/drive_c/Program Files (x86)/NFS Most Wanted");
                    session.gameInstallationFolder = Directory.Exists(Path.Combine(saved, "GLOBAL")) ? saved : Directory.Exists(Path.Combine(crossover, "GLOBAL")) ? crossover : "";
                }
            }
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Complete Most Wanted setup", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Automatically connect idle/exhaust, acceleration/coast, shifting, hard braking, tires, transmission, road, wind and nitrous.", EditorStyles.wordWrappedLabel);
                CompleteFolder("Sound pack", ref session.completePackFolder);
                CompleteFolder("Installed game", ref session.gameInstallationFolder);
                using (new EditorGUI.DisabledScope(presenter.Busy || attachmentTarget == null || EditorApplication.isPlayingOrWillChangePlaymode))
                    if (GUILayout.Button("Prepare complete setup", GUILayout.Height(30))) Queue(() =>
                    {
                        completePlan = BlackBoxCompleteAttachment.Prepare(session.completePackFolder, session.gameInstallationFolder, attachmentTarget);
                        EditorPrefs.SetString("BlackBox.MostWantedInstallation", session.gameInstallationFolder);
                        presenter.Touch(); presenter.SetStatus("Resolved " + completePlan.Setup.Sources.Count + " sources for " + completePlan.Setup.Vehicle + ". Review the assignments below.");
                    });
                if (completePlan == null) return;
                if (completePlan.Target != attachmentTarget) { completePlan = null; return; }
                EditorGUILayout.LabelField(completePlan.Setup.Vehicle + " · " + completePlan.Setup.IdleRpm + "–" + completePlan.Setup.MaximumRpm + " RPM", EditorStyles.boldLabel);
                foreach (var source in completePlan.Setup.Sources)
                    EditorGUILayout.LabelField(CompleteRole(source.Role), Path.GetFileName(source.Path), EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.HelpBox("Uses the pack's inherited game sounds and sample routing, including sputters and road surfaces. Volume, blending and spatial sound are adapted for Unity; this is not an exact recreation of the original game's mix.", MessageType.Info);
                using (new EditorGUI.DisabledScope(presenter.Busy || EditorApplication.isPlayingOrWillChangePlaymode))
                    if (GUILayout.Button("Apply complete setup", GUILayout.Height(32))) Queue(() =>
                    {
                        var profile = completePlan.Apply(); session.generatedProfilePath = AssetDatabase.GetAssetPath(profile);
                        presenter.Touch(); presenter.SetStatus("Complete setup attached to " + attachmentTarget.name + ". Save the vehicle scene to keep it.");
                        Selection.activeGameObject = attachmentTarget.gameObject; completePlan = null;
                    });
            }
        }
        private void CompleteFolder(string label, ref string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck(); string next = EditorGUILayout.TextField(label, value);
                if (EditorGUI.EndChangeCheck()) { value = next; completePlan = null; presenter.Touch(); }
                if (GUILayout.Button("Browse", GUILayout.Width(64)))
                {
                    string chosen = EditorUtility.OpenFolderPanel(label, value, "");
                    if (chosen.Length > 0) { value = chosen; completePlan = null; presenter.Touch(); }
                }
            }
        }
        private static string CompleteRole(string role)
        {
            switch (role)
            {
                case "engine": return "Idle / exhaust / limiter";
                case "acceleration": return "On throttle";
                case "deceleration": return "Coast";
                case "sweeteners": return "Throttle / shift exhaust";
                case "shifts": return "Shifts / hard braking";
                case "skids": return "Tire slip / braking";
                default: return char.ToUpperInvariant(role[0]) + role.Substring(1);
            }
        }
    }
}
