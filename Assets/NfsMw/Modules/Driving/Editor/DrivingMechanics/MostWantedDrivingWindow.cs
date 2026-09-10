using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using NfsMwRemaster.Driving.Editor.AudioAnalysis;

namespace NfsMwRemaster.Driving.Editor.DrivingMechanics
{
    /// <summary>Read-only source inspection followed by an explicit, create-only asset operation.</summary>
    public sealed class MostWantedDrivingWindow : EditorWindow
    {
        private const int MaximumReportBytes = 64 * 1024 * 1024;
        [SerializeField] private string gameFolder = "";
        [SerializeField] private string vehicleKeys = "bmwm3gtre46, gti, punto";
        [SerializeField] private VehicleTuning baseline;
        [SerializeField] private int selectedVehicle;
        [SerializeField] private Vector2 scroll;
        private MostWantedHandlingReport decoding;
        private MostWantedDrivingImport preview;
        private readonly Dictionary<string, string> choices = new Dictionary<string, string>(StringComparer.Ordinal);
        private string failure = "", notice = "";
        private bool showMappings, showUnmapped, showRawIssues;

        [MenuItem("Racing Tools/Vehicles/Most Wanted driving mechanics")]
        public static MostWantedDrivingWindow Open()
        {
            var window = GetWindow<MostWantedDrivingWindow>("MW driving mechanics");
            window.minSize = new Vector2(650f, 480f);
            window.Show();
            return window;
        }

        public static void Open(VehicleTuning contactBaseline)
        {
            var window = Open(); window.ClearPreview(); window.baseline = contactBaseline;
        }

        private void OnEnable() { if (string.IsNullOrWhiteSpace(gameFolder)) gameFolder = BlackBoxVehicleLibrary.FindInstallation(); }
        private void OnDisable() => ClearPreview();
        private void ClearPreview() { preview?.Dispose(); preview = null; }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Most Wanted 2005 · Driving mechanics", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Inspect your original data, choose linked records, then create a separate Unity tuning asset. Existing cars and source files are not changed. This is a source-informed reference mode, not a verified full PC physics port.", MessageType.Info);
            DrawSource();
            if (decoding != null) DrawSelection();
            if (preview != null) DrawPreview();
            if (!string.IsNullOrEmpty(failure)) EditorGUILayout.HelpBox(failure, MessageType.Error);
            if (!string.IsNullOrEmpty(notice)) EditorGUILayout.HelpBox(notice, MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void DrawSource()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("1. Read source data", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                gameFolder = EditorGUILayout.TextField(new GUIContent("Game folder", "Contains GLOBAL/attributes.bin, FE_ATTRIB.bin and gameplay.bin."), gameFolder);
                if (GUILayout.Button("Browse…", GUILayout.Width(85)))
                {
                    string folder = EditorUtility.OpenFolderPanel("Most Wanted installation", gameFolder, "");
                    if (!string.IsNullOrEmpty(folder)) gameFolder = folder;
                }
            }
            vehicleKeys = EditorGUILayout.TextField(new GUIContent("Vehicle keys", "Comma-separated pvehicle names or hexadecimal row keys. Leave empty to inspect all rows."), vehicleKeys);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button("Read installed handling data")) Attempt(() =>
                    {
                        string[] keys = vehicleKeys.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(key => key.Trim()).Where(key => key.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
                        SetDecoding(MostWantedHandlingReader.Decode(gameFolder, keys.Length == 0 ? null : keys));
                        notice = "Read completed. Source hashes were verified unchanged.";
                    });
                    if (GUILayout.Button("Open captured JSON…")) Attempt(() =>
                    {
                        string path = EditorUtility.OpenFilePanel("Handling evidence", "Tools/DrivingMechanics/Evidence", "json");
                        if (path.Length == 0) return;
                        SetDecoding(ReadReport(path));
                        notice = "Loaded captured evidence. Its stored hashes are retained; the original installation was not reread.";
                    });
                }
            }
            if (decoding == null) return;
            EditorGUILayout.LabelField("Capture", $"{decoding.vehicles.Count} vehicles · {decoding.records.Count} linked records · {decoding.issues.Count} explicit issues");
            showRawIssues = EditorGUILayout.Foldout(showRawIssues, "Source identity and decoding issues", true);
            if (showRawIssues)
            {
                foreach (var source in decoding.sources)
                {
                    EditorGUILayout.LabelField(source.relativePath, $"{source.byteLength:N0} bytes · {(source.unchanged ? "unchanged snapshot" : "not verified")}");
                    EditorGUILayout.SelectableLabel(source.sha256, EditorStyles.miniLabel, GUILayout.Height(18));
                }
                foreach (var issue in decoding.issues.Take(100))
                    EditorGUILayout.LabelField(issue.code + " · " + issue.subject + "\n" + issue.message, EditorStyles.wordWrappedMiniLabel);
                if (decoding.issues.Count > 100) EditorGUILayout.LabelField("Showing the first 100 issues. The full JSON retains every issue.", EditorStyles.miniLabel);
            }
        }

        private void DrawSelection()
        {
            if (decoding.vehicles.Count == 0) { EditorGUILayout.HelpBox("No matching vehicle was decoded. Check the keys or select a different capture.", MessageType.Warning); return; }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("2. Choose source records", EditorStyles.boldLabel);
            int next = EditorGUILayout.Popup("Vehicle", selectedVehicle,
                decoding.vehicles.Select(vehicle => vehicle.name + " · " + vehicle.rowKey).ToArray());
            if (next != selectedVehicle) { selectedVehicle = next; ResetChoices(); }
            var vehicle = decoding.vehicles[selectedVehicle];
            EditorGUILayout.HelpBox("Reference indexes are stored database slots, not confirmed upgrade stages. Choose each subsystem explicitly; no garage state, upgrade interpolation or Junkman package is inferred.", MessageType.None);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Subsystem", GUILayout.Width(110));
                GUILayout.Label("Selected record and source slot(s)");
            }
            foreach (string role in MostWantedDrivingImporter.Roles)
            {
                var groups = vehicle.links.Where(link => link.fieldName == role && link.status == "resolved")
                    .OrderBy(link => link.index).GroupBy(link => link.targetRecordId).ToArray();
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(ObjectNames.NicifyVariableName(role), GUILayout.Width(110));
                    if (groups.Length == 0) { GUILayout.Label("No resolved record", EditorStyles.miniLabel); continue; }
                    int current = Array.FindIndex(groups, group => choices.TryGetValue(role, out string id) && group.Key == id);
                    string[] labels = groups.Select(group => "Slot " + string.Join(", ", group.Select(link => link.index)) + " · " + group.First().targetRowKey).ToArray();
                    int selection = EditorGUILayout.Popup(Mathf.Max(0, current), labels);
                    if (current != selection) { choices[role] = groups[selection].Key; ClearPreview(); }
                }
            }
            EditorGUI.BeginChangeCheck();
            baseline = (VehicleTuning)EditorGUILayout.ObjectField(new GUIContent("Unity contact baseline", "Optional existing tuning to copy geometry, tyre force calibration and unmapped dimensions from. Never edited by import."), baseline, typeof(VehicleTuning), false);
            if (EditorGUI.EndChangeCheck()) ClearPreview();
            using (new EditorGUI.DisabledScope(choices.Count != MostWantedDrivingImporter.Roles.Length || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Build reference preview")) Attempt(() =>
                {
                    ClearPreview(); preview = MostWantedDrivingImporter.Create(decoding, vehicle, choices, baseline);
                    notice = "Preview is transient. Save creates a new asset and a provenance report; nothing has been assigned to a car.";
                });
        }

        private void DrawPreview()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("3. Review and save a new tuning", EditorStyles.boldLabel);
            var tuning = preview.Tuning;
            EditorGUILayout.LabelField("Engine", $"{tuning.engine.maxTorqueNewtonMeters:0.###} N·m · idle {tuning.engine.idleRpm:0} · limiter {tuning.engine.redlineRpm:0} · table end {tuning.mostWanted.torqueTableMaximumRpm:0} RPM");
            EditorGUILayout.LabelField("Drivetrain", $"{tuning.driveLayout} · {tuning.engine.gearRatios.Length} forward gears · final drive {tuning.engine.finalDrive:0.###}");
            EditorGUILayout.LabelField("Mapped fields", preview.Report.mapped.Count.ToString());
            EditorGUILayout.HelpBox(preview.Report.fidelity, MessageType.Warning);
            foreach (string adaptation in preview.Report.retainedAdaptations)
                EditorGUILayout.LabelField(adaptation, EditorStyles.wordWrappedMiniLabel);
            showMappings = EditorGUILayout.Foldout(showMappings, "Mapped values and units", true);
            if (showMappings)
                foreach (var mapping in preview.Report.mapped)
                {
                    EditorGUILayout.LabelField(mapping.field + " → " + mapping.destination, EditorStyles.miniBoldLabel);
                    EditorGUILayout.LabelField(mapping.interpretation, EditorStyles.wordWrappedMiniLabel);
                }
            showUnmapped = EditorGUILayout.Foldout(showUnmapped, "Retained but not mapped", true);
            if (showUnmapped) foreach (string item in preview.Report.unmapped) EditorGUILayout.LabelField(item, EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Save as new tuning and provenance report…")) Attempt(() =>
                {
                    string path = EditorUtility.SaveFilePanelInProject("Save reference tuning", SafeName(preview.Report.vehicle) + "_MWReference", "asset", "Creates a separate tuning and .provenance.json. Existing files are never overwritten.");
                    if (path.Length == 0) return;
                    var saved = SaveNew(preview, path);
                    Selection.activeObject = saved; EditorGUIUtility.PingObject(saved);
                    notice = "Created " + AssetDatabase.GetAssetPath(saved) + ". Assign it to a separate Vehicle Definition or Physics Lab setup to compare; existing factory assets are unchanged.";
                    ClearPreview();
                });
        }

        internal static MostWantedHandlingReport ReadReport(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > MaximumReportBytes) throw new InvalidDataException("Evidence must be a nonempty JSON file no larger than 64 MiB.");
            using (var stream = info.OpenRead())
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            using (var json = new JsonTextReader(reader) { MaxDepth = 64 })
            {
                var serializer = JsonSerializer.Create(new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MaxDepth = 64 });
                var report = serializer.Deserialize<MostWantedHandlingReport>(json);
                MostWantedDrivingImporter.ValidateCaptureShape(report);
                return report;
            }
        }

        public static VehicleTuning SaveNew(MostWantedDrivingImport imported, string proposedAssetPath)
        {
            if (imported?.Tuning == null || imported.Report == null) throw new ArgumentNullException(nameof(imported));
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before saving an imported tuning.");
            string normalized = proposedAssetPath.Replace('\\', '/');
            string root = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(normalized);
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) || !full.StartsWith(root, StringComparison.Ordinal)
                || !normalized.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(Path.GetDirectoryName(full)))
                throw new ArgumentException("Choose an .asset path in an existing Assets folder.", nameof(proposedAssetPath));
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(full)); directory != null && directory.FullName.StartsWith(root, StringComparison.Ordinal); directory = directory.Parent)
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Save into an ordinary Assets folder, not a symbolic-link destination.");
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(normalized);
            string evidencePath = Path.ChangeExtension(assetPath, ".provenance.json");
            if (File.Exists(assetPath) || File.Exists(evidencePath)) throw new IOException("Destination already exists; choose another asset name.");
            RacingLineSnapshot.ValidateTuning(imported.Tuning);
            string evidence = JsonConvert.SerializeObject(imported.Report, Formatting.Indented,
                new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None });
            bool evidenceCreated = false, assetCreated = false;
            var saved = imported.Tuning.CreateRuntimeCopy(); saved.hideFlags = HideFlags.None; saved.name = Path.GetFileNameWithoutExtension(assetPath);
            try
            {
                using (var file = new FileStream(evidencePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(file, new UTF8Encoding(false))) { evidenceCreated = true; writer.Write(evidence); }
                AssetDatabase.CreateAsset(saved, assetPath); assetCreated = EditorUtility.IsPersistent(saved);
                if (!assetCreated) throw new IOException("Unity did not create the tuning asset.");
                AssetDatabase.SaveAssetIfDirty(saved); AssetDatabase.ImportAsset(evidencePath);
                return saved;
            }
            catch
            {
                if (assetCreated) AssetDatabase.DeleteAsset(assetPath); else UnityEngine.Object.DestroyImmediate(saved);
                if (evidenceCreated) { if (!AssetDatabase.DeleteAsset(evidencePath) && File.Exists(evidencePath)) File.Delete(evidencePath); }
                throw;
            }
        }

        private void SetDecoding(MostWantedHandlingReport report)
        {
            decoding = report ?? throw new ArgumentNullException(nameof(report)); selectedVehicle = 0; ResetChoices();
        }
        private void ResetChoices()
        {
            ClearPreview(); choices.Clear();
            if (decoding?.vehicles == null || decoding.vehicles.Count == 0) return;
            selectedVehicle = Mathf.Clamp(selectedVehicle, 0, decoding.vehicles.Count - 1);
            foreach (var pair in MostWantedDrivingImporter.FirstReferences(decoding.vehicles[selectedVehicle])) choices.Add(pair.Key, pair.Value);
        }
        private void Attempt(Action action)
        {
            failure = notice = "";
            try { action(); }
            catch (Exception error) when (error is IOException || error is ArgumentException || error is InvalidOperationException || error is JsonException || error is UnauthorizedAccessException)
            { failure = error.Message; }
        }
        private static string SafeName(string value) => new string((value ?? "vehicle").Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray());
    }
}
