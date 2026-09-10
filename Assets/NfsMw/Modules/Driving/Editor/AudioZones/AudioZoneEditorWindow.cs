#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>
    /// UI Toolkit host with an IMGUI authoring surface. The model, preview and
    /// runtime are separate so window reloads cannot become gameplay state.
    /// </summary>
    public sealed class AudioZoneEditorWindow : EditorWindow
    {
        private static readonly string[] Tabs = { "Zones", "Profiles", "Preview", "Trace", "Validation", "Publish" };
        private readonly List<AudioZone> zones = new List<AudioZone>();
        private readonly List<AudioZonePortal> portals = new List<AudioZonePortal>();
        private readonly List<AudioZoneProfile> profiles = new List<AudioZoneProfile>();
        private readonly List<AudioZoneDiagnostic> diagnostics = new List<AudioZoneDiagnostic>();
        private readonly List<Vector3> listenerPath = new List<Vector3>();
        private readonly List<AudioZoneTraversalSample> recording = new List<AudioZoneTraversalSample>();
        private AudioZone selectedZone;
        private AudioZonePortal selectedPortal;
        private AudioZoneProfile selectedProfile;
        private AudioZoneProfile profileA;
        private AudioZoneProfile profileB;
        private AudioClip sourcePreset;
        private AudioZonePreviewSession preview;
        private Vector2 catalogScroll;
        private Vector2 inspectorScroll;
        private Vector2 diagnosticsScroll;
        private Vector2 recordingScroll;
        private string search = string.Empty;
        private string soloZoneId = string.Empty;
        private string message = "Ready. Select a zone or create an acoustic volume.";
        private string diagnosticSearch = string.Empty;
        private int tab;
        private int diagnosticSeverity = -1;
        private int categoryIndex;
        private AudioZoneShape createShape = AudioZoneShape.Box;
        private bool listPortals;
        private bool dryOnly;
        private bool useProfileB;
        private bool usePath = true;
        private bool recordingActive;
        private double nextRecordingSample;
        private float previewSpeed = 12;
        private Vector3 startPosition;

        [MenuItem("NFS MW Remaster/Driving/Audio Zones/Audio Zone Editor")]
        public static void OpenFromMenu() => Open();

        public static AudioZoneEditorWindow Open()
        {
            var window = GetWindow<AudioZoneEditorWindow>();
            window.titleContent = new GUIContent("Audio Zone Editor");
            window.minSize = new Vector2(980, 620);
            window.RefreshCatalog();
            return window;
        }

        public static AudioZoneEditorWindow Open(AudioZone zone)
        {
            var window = Open();
            window.SelectZone(zone);
            return window;
        }

        public static AudioZoneEditorWindow Open(AudioZonePortal portal)
        {
            var window = Open();
            window.SelectPortal(portal);
            return window;
        }

        public static AudioZoneEditorWindow Open(AudioZoneProfile profile)
        {
            var window = Open();
            window.SelectProfile(profile);
            return window;
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.hierarchyChanged += RefreshCatalog;
            Selection.selectionChanged += SyncSelection;
            Undo.undoRedoPerformed += OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            SyncSelection();
            RefreshCatalog();
        }

        private void OnDisable()
        {
            StopPreview();
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.hierarchyChanged -= RefreshCatalog;
            Selection.selectionChanged -= SyncSelection;
            Undo.undoRedoPerformed -= OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload -= StopPreview;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.paddingLeft = 8;
            rootVisualElement.style.paddingRight = 8;
            rootVisualElement.style.paddingTop = 6;
            rootVisualElement.style.paddingBottom = 6;
            var header = new Toolbar();
            header.Add(new Label("AUDIO ZONE EDITOR") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 12 } });
            header.Add(new ToolbarButton(() => Safe(RefreshCatalog)) { text = "Refresh" });
            header.Add(new ToolbarButton(() => Safe(RunValidation)) { text = "Validate" });
            header.Add(new ToolbarButton(() => Safe(AudioZoneDemoBuilder.Build)) { text = "Build test scene" });
            header.Add(new ToolbarButton(() => Safe(OpenGuide)) { text = "Guide" });
            rootVisualElement.Add(header);
            rootVisualElement.Add(new IMGUIContainer(DrawContent) { style = { flexGrow = 1 } });
        }

        public void RunValidation()
        {
            diagnostics.Clear(); diagnostics.AddRange(AudioZoneEditorModel.ValidateAll());
            tab = 4;
            message = diagnostics.Count == 0 ? "Validation passed with no diagnostics." : "Validation found " + diagnostics.Count + " diagnostic(s).";
            Repaint();
        }

        private void DrawContent()
        {
            tab = GUILayout.Toolbar(tab, Tabs);
            switch (tab)
            {
                case 0: DrawZones(); break;
                case 1: DrawProfiles(); break;
                case 2: DrawPreview(); break;
                case 3: DrawTrace(); break;
                case 4: DrawValidation(); break;
                default: DrawPublish(); break;
            }
            EditorGUILayout.HelpBox(message, MessageType.Info);
        }

        private void DrawZones()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                createShape = (AudioZoneShape)EditorGUILayout.EnumPopup(createShape, GUILayout.Width(100));
                categoryIndex = EditorGUILayout.Popup(categoryIndex, Enum.GetNames(typeof(AudioZoneCategory)), GUILayout.Width(125));
                if (GUILayout.Button("New zone", EditorStyles.toolbarButton, GUILayout.Width(90))) Safe(CreateZone);
                if (GUILayout.Button("New profile", EditorStyles.toolbarButton, GUILayout.Width(90))) Safe(CreateProfile);
                if (GUILayout.Button("New portal", EditorStyles.toolbarButton, GUILayout.Width(90))) Safe(CreatePortal);
                listPortals = GUILayout.Toggle(listPortals, "Portals", EditorStyles.toolbarButton, GUILayout.Width(75));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Frame selected", EditorStyles.toolbarButton, GUILayout.Width(100))) FocusSelected();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawCatalog();
                DrawSelectedInspector();
            }
        }

        private void DrawCatalog()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(330)))
            {
                EditorGUILayout.LabelField("LOADED SCENE CATALOG", EditorStyles.boldLabel);
                search = EditorGUILayout.TextField("Search", search);
                catalogScroll = EditorGUILayout.BeginScrollView(catalogScroll, GUILayout.ExpandHeight(true));
                if (!listPortals)
                {
                    foreach (var zone in zones)
                    {
                        if (!Matches(zone.name, zone.StableId, zone.Profile != null ? zone.Profile.DisplayName : "")) continue;
                        bool active = zone == selectedZone;
                        using (new EditorGUILayout.HorizontalScope(active ? "SelectionRect" : GUIStyle.none))
                        {
                            if (GUILayout.Button(zone.name, EditorStyles.label)) SelectZone(zone);
                            GUILayout.Label(zone.Profile != null ? zone.Profile.Category.ToString() : "Missing profile", GUILayout.Width(120));
                        }
                    }
                    if (zones.Count == 0) EditorGUILayout.HelpBox("No AudioZone components are loaded. Create one or build the synthetic test scene.", MessageType.Info);
                }
                else
                {
                    foreach (var portal in portals)
                    {
                        if (!Matches(portal.name, portal.StableId, portal.State.ToString())) continue;
                        if (GUILayout.Button(portal.name + " · " + portal.State, selectedPortal == portal ? "SelectionRect" : EditorStyles.label)) SelectPortal(portal);
                    }
                    if (portals.Count == 0) EditorGUILayout.HelpBox("No portals are loaded.", MessageType.Info);
                }
                EditorGUILayout.EndScrollView();
                EditorGUILayout.LabelField(zones.Count + " zones · " + portals.Count + " portals · " + profiles.Count + " profiles", EditorStyles.miniLabel);
            }
        }

        private void DrawSelectedInspector()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
            {
                inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll, GUILayout.ExpandHeight(true));
                if (listPortals && selectedPortal != null) DrawSerializedInspector(selectedPortal, "PORTAL INSPECTOR");
                else if (selectedZone != null) DrawZoneInspector(selectedZone);
                else EditorGUILayout.HelpBox("Select a zone or portal from the catalog. The Scene view handles edit the selected volume with Undo/Redo.", MessageType.Info);
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawZoneInspector(AudioZone zone)
        {
            EditorGUILayout.LabelField("ZONE INSPECTOR", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Core geometry is the authored space. The profile's blend distance and hysteresis extend the evaluation boundary; the runtime does not mutate the zone transform.", MessageType.Info);
            DrawSerializedInspector(zone, null);
            if (zone.Profile != null)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("ASSIGNED PROFILE", EditorStyles.boldLabel);
                DrawSerializedInspector(zone.Profile, null);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use in preview")) { profileA = zone.Profile; startPosition = zone.WorldBounds.center; tab = 2; }
                if (GUILayout.Button("Focus")) Focus(zone.gameObject);
                if (GUILayout.Button("Validate")) RunValidation();
            }
        }

        private void DrawProfiles()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("New open street", EditorStyles.toolbarButton)) Safe(() => { selectedProfile = AudioZoneEditorModel.CreateProfile("Assets/NfsMw/Modules/Driving/Data/AudioZones", AudioZoneCategory.OpenStreet); RefreshCatalog(); });
                if (GUILayout.Button("New tunnel", EditorStyles.toolbarButton)) Safe(() => { selectedProfile = AudioZoneEditorModel.CreateProfile("Assets/NfsMw/Modules/Driving/Data/AudioZones", AudioZoneCategory.Tunnel); RefreshCatalog(); });
                if (GUILayout.Button("New garage", EditorStyles.toolbarButton)) Safe(() => { selectedProfile = AudioZoneEditorModel.CreateProfile("Assets/NfsMw/Modules/Driving/Data/AudioZones", AudioZoneCategory.Garage); RefreshCatalog(); });
                categoryIndex = EditorGUILayout.Popup(categoryIndex, Enum.GetNames(typeof(AudioZoneCategory)), GUILayout.Width(125));
                if (GUILayout.Button("New selected category", EditorStyles.toolbarButton, GUILayout.Width(145))) Safe(CreateProfile);
                GUILayout.FlexibleSpace();
                search = EditorGUILayout.TextField(search, GUILayout.Width(200));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(330)))
                {
                    catalogScroll = EditorGUILayout.BeginScrollView(catalogScroll, GUILayout.ExpandHeight(true));
                    foreach (var profile in profiles)
                        if (Matches(profile.DisplayName, profile.StableId, profile.Category.ToString()))
                            if (GUILayout.Button(profile.DisplayName + " · " + profile.Category, selectedProfile == profile ? "SelectionRect" : EditorStyles.label)) SelectProfile(profile);
                    EditorGUILayout.EndScrollView();
                }
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll, GUILayout.ExpandHeight(true));
                    if (selectedProfile == null) EditorGUILayout.HelpBox("Select a profile. Profiles are ScriptableObject authoring assets and can be reused by many zones.", MessageType.Info);
                    else
                    {
                        DrawSerializedInspector(selectedProfile, "PROFILE INSPECTOR");
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("A/B slot A")) profileA = selectedProfile;
                            if (GUILayout.Button("A/B slot B")) profileB = selectedProfile;
                            if (GUILayout.Button("Validate")) RunValidation();
                        }
                    }
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void DrawPreview()
        {
            EditorGUILayout.LabelField("SOURCE / LISTENER PREVIEW", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("This is a disposable authoring scene. It previews authored geometry and filters, while target-device spatializers, streaming timing and the full runtime voice budget remain build validation concerns. Closing the tab/window restores every production AudioListener.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
            profileA = (AudioZoneProfile)EditorGUILayout.ObjectField("Profile A", profileA, typeof(AudioZoneProfile), false);
                profileB = (AudioZoneProfile)EditorGUILayout.ObjectField("Profile B", profileB, typeof(AudioZoneProfile), false);
            }
            useProfileB = EditorGUILayout.Toggle("Use profile B", useProfileB);
            sourcePreset = (AudioClip)EditorGUILayout.ObjectField("Source preset", sourcePreset, typeof(AudioClip), false);
            dryOnly = EditorGUILayout.Toggle("Dry / bypass profile filters", dryOnly);
            previewSpeed = EditorGUILayout.Slider("Listener speed (m/s)", previewSpeed, 0, 120);
            var options = new List<string> { "All zones" };
            foreach (var zone in zones) options.Add(zone.name + " · " + zone.StableId);
            int soloIndex = string.IsNullOrEmpty(soloZoneId) ? 0 : Mathf.Max(0, zones.FindIndex(z => z.StableId == soloZoneId) + 1);
            int newSolo = EditorGUILayout.Popup("Solo zone", soloIndex, options.ToArray());
            soloZoneId = newSolo <= 0 || newSolo > zones.Count ? string.Empty : zones[newSolo - 1].StableId;
            usePath = EditorGUILayout.Toggle("Follow listener path", usePath);
            DrawPathEditor();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (preview == null || !preview.IsValid)
                {
                    if (GUILayout.Button("Start disposable preview")) Safe(StartPreview);
                }
                else
                {
                    if (GUILayout.Button(preview.Playing ? "Pause" : "Play")) preview.SetPlaying(!preview.Playing);
                    if (GUILayout.Button("Stop & restore")) StopPreview();
                }
                if (GUILayout.Button("Record traversal")) BeginRecording();
                if (GUILayout.Button("Save traversal")) Safe(SaveTraversal);
            }
            if (preview != null)
            {
                preview.UseB = useProfileB;
                preview.DryOnly = dryOnly;
                preview.Speed = previewSpeed;
                preview.SetProfiles(profileA, profileB);
                preview.SetSourcePreset(sourcePreset != null ? sourcePreset : DefaultPreset());
                preview.SetSoloZone(soloZoneId);
                EditorGUILayout.LabelField(preview.Status, EditorStyles.wordWrappedMiniLabel);
                if (preview.MissingClipCount > 0) EditorGUILayout.HelpBox("A preview source has no resident AudioClip. Import/load it before treating the audition as valid.", MessageType.Warning);
            }
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Traversal capture", EditorStyles.boldLabel);
            recordingScroll = EditorGUILayout.BeginScrollView(recordingScroll, GUILayout.Height(120));
            EditorGUILayout.LabelField(recordingActive ? "Recording " + recording.Count + " samples…" : recording.Count + " samples buffered.", EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
            if (recordingActive && preview == null) recordingActive = false;
        }

        private void DrawPathEditor()
        {
            EditorGUILayout.LabelField("Listener path", EditorStyles.boldLabel);
            for (int i = 0; i < listenerPath.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    listenerPath[i] = EditorGUILayout.Vector3Field("Point " + i, listenerPath[i]);
                    if (GUILayout.Button("-", GUILayout.Width(24))) { listenerPath.RemoveAt(i); i--; }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add path point")) listenerPath.Add(listenerPath.Count > 0 ? listenerPath[listenerPath.Count - 1] + Vector3.forward * 20 : startPosition);
                if (GUILayout.Button("Use selected zone path")) BuildSelectedPath();
            }
        }

        private void DrawTrace()
        {
            EditorGUILayout.LabelField("RUNTIME TRACE", EditorStyles.boldLabel);
            var world = FindAnyObjectByType<AudioZoneWorld>();
            if (world == null)
            {
                EditorGUILayout.HelpBox("No AudioZoneWorld is loaded. The trace is runtime-only; use Preview for authoring geometry without entering Play Mode.", MessageType.Info);
                if (selectedZone != null)
                {
                    EditorGUILayout.LabelField("Selected zone sample", EditorStyles.boldLabel);
                    DrawMembershipSample(selectedZone.WorldBounds.center);
                }
                return;
            }
            var snapshot = world.RuntimeSnapshot;
            EditorGUILayout.LabelField("Listener", world.ListenerPolicy + " · " + snapshot.listenerPosition.ToString("F2"));
            EditorGUILayout.LabelField("Primary", string.IsNullOrEmpty(snapshot.primaryZoneId) ? "neutral" : snapshot.primaryZoneId);
            EditorGUILayout.LabelField("Transition", snapshot.transitionReason + " · sample " + snapshot.sample);
            EditorGUILayout.LabelField("Obstruction query age", float.IsInfinity(snapshot.lastObstructionQueryAge) ? "no query yet" : snapshot.lastObstructionQueryAge.ToString("F3") + " s");
            if (snapshot.mix != null)
            {
                EditorGUILayout.LabelField("Reverb", snapshot.mix.reverbPreset + " · wet " + snapshot.mix.reverbWet.ToString("P0") + " · decay " + snapshot.mix.reverbDecay.ToString("F2") + " s");
                EditorGUILayout.LabelField("Effective filters", EditorStyles.boldLabel);
                for (int i = 0; i < AudioZoneMixState.CategoryCount; i++)
                    EditorGUILayout.LabelField(((SensoryCategory)i).ToString(), "gain " + snapshot.mix.categoryGains[i].ToString("F2") + " · LP " + snapshot.mix.categoryLowPassHz[i].ToString("F0") + " Hz · HP " + snapshot.mix.categoryHighPassHz[i].ToString("F0") + " Hz");
            }
            EditorGUILayout.LabelField("Active zone influences", EditorStyles.boldLabel);
            foreach (var influence in snapshot.influences ?? Array.Empty<AudioZoneRuntimeInfluence>())
                EditorGUILayout.LabelField(influence.zoneId, influence.category + " · priority " + influence.priority + " · weight " + influence.weight.ToString("P0") + (influence.insideCore ? " · core" : " · blend"));
        }

        private void DrawMembershipSample(Vector3 position)
        {
            foreach (var zone in zones)
            {
                if (zone.Profile == null) continue;
                if (zone.TryEvaluate(position, false, out float weight, out bool inside, out bool held))
                    EditorGUILayout.LabelField(zone.name, zone.Profile.Category + " · " + weight.ToString("P0") + (inside ? " · core" : held ? " · hysteresis" : " · blend"));
            }
        }

        private void DrawValidation()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Run validation", EditorStyles.toolbarButton, GUILayout.Width(100))) RunValidation();
                diagnosticSeverity = EditorGUILayout.IntPopup("Severity", diagnosticSeverity, new[] { "All", "Info", "Warning", "Error" }, new[] { -1, 0, 1, 2 }, GUILayout.Width(180));
                diagnosticSearch = EditorGUILayout.TextField(diagnosticSearch);
            }
            if (diagnostics.Count == 0) EditorGUILayout.HelpBox("No validation run in this window. Run it before publishing or use the toolbar button.", MessageType.Info);
            diagnosticsScroll = EditorGUILayout.BeginScrollView(diagnosticsScroll, GUILayout.ExpandHeight(true));
            foreach (var diagnostic in diagnostics)
            {
                if (diagnosticSeverity >= 0 && (int)diagnostic.severity != diagnosticSeverity) continue;
                if (!Matches(diagnosticSearch, diagnostic.code, diagnostic.message, diagnostic.target != null ? diagnostic.target.name : "")) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox(diagnostic.code + " · " + diagnostic.message, ToMessageType(diagnostic.severity));
                    if (diagnostic.target != null && GUILayout.Button("Go", GUILayout.Width(36))) SelectDiagnosticTarget(diagnostic.target);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawPublish()
        {
            EditorGUILayout.LabelField("AUTHORING HANDOFF", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Publish here means save authored ScriptableObjects and scene changes after validation. There is no hidden bake that changes stable IDs or player state.", MessageType.Info);
            EditorGUILayout.LabelField("Scene revision", AudioZoneEditorModel.ComputeRevision(zones, portals));
            EditorGUILayout.LabelField("Loaded content", zones.Count + " zones · " + portals.Count + " portals · " + profiles.Count + " profiles");
            if (GUILayout.Button("Run validation and save assets")) Safe(() => { RunValidation(); AssetDatabase.SaveAssets(); message = "Validation completed and asset changes saved."; });
            if (GUILayout.Button("Build synthetic acoustic traversal scene")) Safe(AudioZoneDemoBuilder.Build);
            if (GUILayout.Button("Open setup guide")) OpenGuide();
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Ownership checklist", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("✓ SensoryAudioWorld remains the pooled voice/mixer boundary.");
            EditorGUILayout.LabelField("✓ AudioZoneWorld owns environment processing only.");
            EditorGUILayout.LabelField("✓ Music, radio, police sensing, user preferences and saves remain with their existing owners.");
            EditorGUILayout.LabelField("✓ Preview listeners and sources are disposable and never serialized into production scenes.");
        }

        private void DrawSerializedInspector(UnityEngine.Object value, string title)
        {
            if (value == null) return;
            if (!string.IsNullOrEmpty(title)) EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            var serialized = new SerializedObject(value);
            serialized.Update();
            var iterator = serialized.GetIterator();
            bool enterChildren = true;
            EditorGUI.BeginChangeCheck();
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.name == "m_Script") continue;
                bool identity = iterator.name == "stableId" || iterator.name == "schema";
                using (new EditorGUI.DisabledScope(identity)) EditorGUILayout.PropertyField(iterator, true);
            }
            if (EditorGUI.EndChangeCheck() && serialized.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(value);
                if (value is Component component) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
                RefreshCatalog(); SceneView.RepaintAll();
            }
        }

        private void StartPreview()
        {
            if (zones.Count == 0) throw new InvalidOperationException("Create or load at least one profiled AudioZone before starting preview.");
            if (startPosition == Vector3.zero && selectedZone != null) startPosition = selectedZone.WorldBounds.center;
            preview = new AudioZonePreviewSession();
            preview.Start(zones, profileA, profileB, sourcePreset != null ? sourcePreset : DefaultPreset(), startPosition, previewSpeed);
            preview.DryOnly = dryOnly; preview.SetSoloZone(soloZoneId); preview.SetPath(usePath ? listenerPath : null);
            message = "Disposable acoustic preview started.";
        }

        private void StopPreview()
        {
            preview?.Dispose();
            preview = null;
            recordingActive = false;
        }

        private void BeginRecording()
        {
            if (preview == null || !preview.IsValid) { message = "Start the disposable preview before recording a traversal."; return; }
            recording.Clear(); recordingActive = true; nextRecordingSample = 0; message = "Traversal capture armed.";
        }

        private void SaveTraversal()
        {
            if (recording.Count == 0) throw new InvalidOperationException("No traversal samples are buffered.");
            string path = EditorUtility.SaveFilePanelInProject("Save audio traversal", "AudioTraversal", "asset", "Choose an authored traversal destination.", "Assets/NfsMw/Modules/Driving/Data/AudioZones");
            if (string.IsNullOrEmpty(path)) return;
            EnsureFolder("Assets/NfsMw/Modules/Driving/Data/AudioZones");
            var asset = ScriptableObject.CreateInstance<AudioZoneTraversalAsset>();
            asset.Configure("audio.traversal." + Guid.NewGuid().ToString("N"), SceneManager.GetActiveScene().path,
                AudioZoneEditorModel.ComputeRevision(zones, portals), AudioZoneListenerPolicy.RenderedAudioListener, 0.05f);
            asset.SetSamples(recording); asset.SetCaptureNotes("Editor preview capture; synthetic authoring evidence, not a gameplay save or measured device capture.");
            AssetDatabase.CreateAsset(asset, path); AssetDatabase.SaveAssets(); Selection.activeObject = asset;
            message = "Traversal asset saved: " + path;
        }

        private void OnEditorUpdate()
        {
            if (preview != null && preview.IsValid)
            {
                preview.Tick();
                if (recordingActive && preview.Playing && EditorApplication.timeSinceStartup >= nextRecordingSample)
                {
                    nextRecordingSample = EditorApplication.timeSinceStartup + 0.05;
                    recording.Add(CaptureSample(preview.ListenerPosition, preview.Elapsed));
                }
                Repaint();
            }
        }

        private AudioZoneTraversalSample CaptureSample(Vector3 position, float time)
        {
            var ids = new List<string>(); var weights = new List<float>(); string primary = string.Empty; float highest = 0;
            foreach (var zone in zones)
            {
                if (zone.Profile == null || !zone.TryEvaluate(position, false, out float weight, out _, out _)) continue;
                ids.Add(zone.StableId); weights.Add(weight);
                if (weight > highest) { highest = weight; primary = zone.StableId; }
            }
            return new AudioZoneTraversalSample { time = time, position = position, primaryZoneId = primary, zoneIds = ids.ToArray(), weights = weights.ToArray() };
        }

        private void BuildSelectedPath()
        {
            if (selectedZone == null) { message = "Select a zone first."; return; }
            var bounds = selectedZone.WorldBounds;
            startPosition = bounds.center - selectedZone.transform.forward * Mathf.Max(10, bounds.extents.z + 12);
            listenerPath.Clear(); listenerPath.Add(startPosition); listenerPath.Add(bounds.center); listenerPath.Add(bounds.center + selectedZone.transform.forward * Mathf.Max(10, bounds.extents.z + 12));
        }

        private void CreateZone()
        {
            Vector3 position = selectedZone != null ? selectedZone.WorldBounds.center : SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera.transform.position + SceneView.lastActiveSceneView.camera.transform.forward * 12 : Vector3.zero;
            selectedZone = AudioZoneEditorModel.CreateZone(createShape, selectedProfile, position);
            RefreshCatalog(); tab = 0; message = "Created a reversible " + createShape + " zone.";
        }

        private void CreateProfile()
        {
            selectedProfile = AudioZoneEditorModel.CreateProfile("Assets/NfsMw/Modules/Driving/Data/AudioZones", (AudioZoneCategory)categoryIndex);
            RefreshCatalog(); tab = 1; message = "Created profile with a stable ID and category defaults.";
        }

        private void CreatePortal()
        {
            if (selectedZone == null) throw new InvalidOperationException("Select the source zone before creating a portal.");
            AudioZone target = null;
            foreach (var zone in zones) if (zone != selectedZone) { target = zone; break; }
            if (target == null) throw new InvalidOperationException("Create a second zone before creating a portal.");
            selectedPortal = AudioZoneEditorModel.CreatePortal(selectedZone, target, (selectedZone.WorldBounds.center + target.WorldBounds.center) * 0.5f);
            RefreshCatalog(); message = "Created an authored acoustic portal between the first two zones.";
        }

        private void SelectZone(AudioZone zone)
        {
            selectedZone = zone; selectedPortal = null; if (zone != null) { selectedProfile = zone.Profile; startPosition = zone.WorldBounds.center; }
            if (zone != null) { Selection.activeGameObject = zone.gameObject; tab = 0; }
            Repaint(); SceneView.RepaintAll();
        }

        private void SelectPortal(AudioZonePortal portal)
        {
            selectedPortal = portal; selectedZone = null; if (portal != null) Selection.activeGameObject = portal.gameObject;
            Repaint(); SceneView.RepaintAll();
        }

        private void SelectProfile(AudioZoneProfile profile)
        {
            selectedProfile = profile; selectedZone = null; selectedPortal = null; if (profile != null) Selection.activeObject = profile;
            Repaint();
        }

        private void SyncSelection()
        {
            if (Selection.activeObject is AudioZone zone) SelectZone(zone);
            else if (Selection.activeGameObject != null && Selection.activeGameObject.TryGetComponent(out AudioZone selected)) SelectZone(selected);
            else if (Selection.activeObject is AudioZonePortal portal) SelectPortal(portal);
            else if (Selection.activeGameObject != null && Selection.activeGameObject.TryGetComponent(out AudioZonePortal selectedPortalComponent)) SelectPortal(selectedPortalComponent);
            else if (Selection.activeObject is AudioZoneProfile profile) SelectProfile(profile);
        }

        private void RefreshCatalog()
        {
            zones.Clear(); zones.AddRange(AudioZoneEditorModel.FindZones());
            portals.Clear(); portals.AddRange(AudioZoneEditorModel.FindPortals());
            profiles.Clear(); profiles.AddRange(AudioZoneEditorModel.FindProfiles());
            if (selectedZone != null && !zones.Contains(selectedZone)) selectedZone = null;
            if (selectedPortal != null && !portals.Contains(selectedPortal)) selectedPortal = null;
            if (selectedProfile != null && !profiles.Contains(selectedProfile)) selectedProfile = null;
            Repaint(); SceneView.RepaintAll();
        }

        private void OnUndoRedo() { RefreshCatalog(); if (preview != null) StopPreview(); }
        private void OnPlayModeStateChanged(PlayModeStateChange state) { if (state != PlayModeStateChange.EnteredEditMode) StopPreview(); }
        private void FocusSelected() { if (selectedZone != null) Focus(selectedZone.gameObject); else if (selectedPortal != null) Focus(selectedPortal.gameObject); }
        private static void Focus(GameObject go) { Selection.activeGameObject = go; SceneView.lastActiveSceneView?.FrameSelected(); }

        private void SelectDiagnosticTarget(UnityEngine.Object target)
        {
            if (target is AudioZone zone) SelectZone(zone);
            else if (target is AudioZonePortal portal) SelectPortal(portal);
            else if (target is AudioZoneProfile profile) SelectProfile(profile);
            EditorGUIUtility.PingObject(target);
        }

        private bool Matches(string a, string b, string c) => Matches(search, a, b, c);
        private static bool Matches(string query, params string[] values)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            for (int i = 0; i < values.Length; i++) if (!string.IsNullOrEmpty(values[i]) && values[i].IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static MessageType ToMessageType(AudioZoneDiagnosticSeverity severity)
            => severity == AudioZoneDiagnosticSeverity.Error ? MessageType.Error : severity == AudioZoneDiagnosticSeverity.Warning ? MessageType.Warning : MessageType.Info;

        private static AudioClip DefaultPreset() => AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/NfsMw/Modules/Driving/Audio/Diagnostic/wind.wav");

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string[] parts = path.Split('/'); string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void Safe(Action action)
        {
            try { action(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        private static void OpenGuide()
        {
            string path = Path.GetFullPath("Assets/NfsMw/Modules/Driving/AUDIO_ZONE_EDITOR.md");
            if (File.Exists(path)) Application.OpenURL("file://" + path);
            else Debug.LogWarning("Audio Zone Editor guide has not been created yet: " + path);
        }
    }
}
#endif
