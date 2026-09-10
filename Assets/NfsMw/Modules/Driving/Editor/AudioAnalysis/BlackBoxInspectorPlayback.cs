using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NfsMwRemaster.Driving.AudioAnalysis.Analysis;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    public sealed partial class BlackBoxInspectorWindow
    {
        [SerializeField] private int latencyFrames;
        [SerializeField] private string traceSearch = "";
        private string comparison = "", referenceLabel = "No reference selected";
        private float[] difference;
        private bool traceCacheDirty = true;
        [SerializeField] private bool traceDetailsOpen, existingPreviewOpen;
        private IReadOnlyList<EngineAudioTrace> cachedTraceSource;
        private readonly List<int> cachedTraceIndices = new List<int>();
        private void RefreshTraceCache()
        {
            IReadOnlyList<EngineAudioTrace> traces = live != null && EditorApplication.isPlaying ? (IReadOnlyList<EngineAudioTrace>)liveTraces : render?.traces ?? Array.Empty<EngineAudioTrace>();
            if (!traceCacheDirty && ReferenceEquals(cachedTraceSource, traces)) return;
            cachedTraceSource = traces; traceCacheDirty = false; cachedTraceIndices.Clear();
            for (int i = 0; i < traces.Count; i++)
            { var trace = traces[i]; if (traceSearch.Length == 0 || (trace.reason + " region " + trace.regionIndex + " tick " + trace.telemetryTick).IndexOf(traceSearch, StringComparison.OrdinalIgnoreCase) >= 0) cachedTraceIndices.Add(i); }
        }
        private void DrawTrace()
        {
            GUILayout.Label("Preview your engine", EditorStyles.largeLabel);
            existingPreviewOpen = EditorGUILayout.Foldout(existingPreviewOpen, "Existing profile or live vehicle (optional)", true);
            if (existingPreviewOpen)
            {
                live = (VehicleAudio)EditorGUILayout.ObjectField("Live vehicle presenter", live, typeof(VehicleAudio), true);
                EditorGUI.BeginChangeCheck(); var existing = (VehicleSensoryProfile)EditorGUILayout.ObjectField("Existing audio profile", session.profile, typeof(VehicleSensoryProfile), false);
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(session, "Select existing audio profile"); session.profile = existing; presenter.Touch(); }
            }
            bool liveMode = live != null && EditorApplication.isPlaying;
            if (liveMode)
            {
                freezeLive = EditorGUILayout.Toggle("Freeze displayed snapshot", freezeLive);
                Text("Live renderer", "Manual test telemetry is separate. Freezing this view continues draining the ring and does not freeze gameplay. Profile: " + (live.Profile == null ? "missing" : live.Profile.name));
                Text("Trace loss", "Renderer dropped " + liveDropped + " · editor discarded/frozen " + editorDropped + ". Missing records do not establish that no sound played.");
            }
            else
            {
                EditorGUILayout.LabelField("Hear your mapping through the vehicle's engine renderer.", EditorStyles.wordWrappedMiniLabel);
                replaySettings.scenario = (BlackBoxScenario)EditorGUILayout.EnumPopup("Authored scenario", replaySettings.scenario);
                replaySettings.rpm = EditorGUILayout.FloatField("Requested / start RPM", replaySettings.rpm);
                replaySettings.endRpm = EditorGUILayout.FloatField("End / upper RPM", replaySettings.endRpm);
                replaySettings.load = EditorGUILayout.Slider("Engine load", replaySettings.load, 0, 1);
                replaySettings.seconds = EditorGUILayout.Slider("Duration · seconds", replaySettings.seconds, 0.05f, 20);
                replayDetailsOpen = EditorGUILayout.Foldout(replayDetailsOpen, "Audio format and timing", true);
                if (replayDetailsOpen)
                {
                    replaySettings.gear = EditorGUILayout.IntField("Starting gear", replaySettings.gear);
                    replaySettings.outputRate = EditorGUILayout.IntField("Output sample rate · Hz", replaySettings.outputRate);
                    replaySettings.channels = EditorGUILayout.IntSlider("Output channels", replaySettings.channels, 1, 2);
                    replaySettings.blockFrames = EditorGUILayout.IntField("Maximum DSP block · frames", replaySettings.blockFrames);
                    replaySettings.controlHz = EditorGUILayout.IntField("Control cadence · Hz", replaySettings.controlHz);
                    replaySettings.gain = EditorGUILayout.Slider("Renderer gain", replaySettings.gain, 0, 1);
                }
                using (new EditorGUI.DisabledScope(presenter.Busy))
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(session.regions.Count == 0 || session.regions.Any(r => !r.reviewed)))
                        if (GUILayout.Button("▶ Render and hear mapping", GUILayout.Height(30))) Queue(() => BeginRender(false, 0, true));
                    if (existingPreviewOpen && session.profile != null && GUILayout.Button("Hear existing profile", GUILayout.Height(30))) Queue(() => BeginRender(true, 0, true));
                }
                if (session.regions.Count == 0 || session.regions.Any(r => !r.reviewed))
                    if (GUILayout.Button("Review mappings to enable preview →")) ShowView(2);
                if (render != null)
                {
                    EditorGUILayout.LabelField("Render ready · " + (render.summary.FrameCount / (double)render.settings.outputRate).ToString("F2") + " seconds · peak " + render.summary.Peak.ToString("G5"), EditorStyles.boldLabel);
                    if (render.summary.Peak == 0) EditorGUILayout.HelpBox("This preview is silent. Check that RPM and load fall inside a reviewed mapping; renderer decisions below show the cause.", MessageType.Info);
                    if (render.summary.Peak >= 1) EditorGUILayout.HelpBox("The render reaches or exceeds unity. PCM remains unclipped in the evidence buffer; monitor volume is separate.", MessageType.Warning);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Hear render A")) Run(() => PlayRender(render, "Native renderer A"));
                        if (GUILayout.Button("Keep this render as B")) { referenceRender = render; referenceLabel = "Native reconstruction snapshot " + render.fingerprint; }
                        if (GUILayout.Button("Export render and telemetry trace")) Run(ExportReplay);
                    }
                    if (GUILayout.Button("Apply to vehicle →", GUILayout.Height(28))) ShowView(5);
                }
            }
            GUILayout.Space(12);
            traceDetailsOpen = EditorGUILayout.Foldout(traceDetailsOpen, "Renderer decisions and frame stepping", true);
            if (!traceDetailsOpen) return;
            if (!liveMode)
            {
                cursorFrame = EditorGUILayout.IntField("Output frame", cursorFrame);
                using (new EditorGUI.DisabledScope(presenter.Busy))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Seek")) Queue(() => BeginRender(false, Math.Max(1, cursorFrame)));
                    if (GUILayout.Button("Step control tick")) Queue(() => { cursorFrame += Math.Max(1, (int)Math.Round(replaySettings.outputRate / (double)Math.Max(1, replaySettings.controlHz))); BeginRender(false, cursorFrame); });
                    if (GUILayout.Button("Step DSP block")) Queue(() => { cursorFrame += Math.Max(1, replaySettings.blockFrames); BeginRender(false, cursorFrame); });
                }
                EditorGUILayout.LabelField("Seeking rebuilds renderer history from frame zero. Native grain and interpolation rules are authored; original EA control parity is unverified.", EditorStyles.wordWrappedMiniLabel);
            }
            IReadOnlyList<EngineAudioTrace> traces = liveMode ? (IReadOnlyList<EngineAudioTrace>)liveTraces : render?.traces ?? Array.Empty<EngineAudioTrace>();
            if (traces.Count == 0) { EditorGUILayout.HelpBox("No renderer decisions captured yet. Render a reviewed region or run a vehicle with a compiled native profile. Missing legacy audio traces are not reconstructed by this window.", MessageType.Info); return; }
            EditorGUI.BeginChangeCheck(); traceSearch = EditorGUILayout.TextField("Find reason / region / tick", traceSearch);
            if (EditorGUI.EndChangeCheck()) traceCacheDirty = true;
            // The trace is bounded, and only one page is drawn. Snapshot indices retain original event ordering.
            var indices = cachedTraceIndices;
            tracePage = Math.Min(tracePage, Math.Max(0, (indices.Count - 1) / 32)); Page(ref tracePage, indices.Count, 32);
            foreach (int index in indices.Skip(tracePage * 32).Take(32))
            {
                if (index >= traces.Count) continue;
                var trace = traces[index];
                if (GUILayout.Toggle(selectedTrace == index, "out " + trace.outputFrameStart + " · tick " + trace.telemetryTick + " · " + trace.requestedRpm.ToString("F0") + " RPM / load " + trace.requestedLoad.ToString("F2") + " · region " + trace.regionIndex + " · " + trace.reason, "Button")) selectedTrace = index;
            }
            selectedTrace = Mathf.Clamp(selectedTrace, 0, traces.Count - 1);
            DrawWhy(traces[selectedTrace], liveMode);
        }
        private void DrawWhy(EngineAudioTrace trace, bool liveMode)
        {
            Text("Why this decision? — " + trace.reason, "Vehicle " + trace.vehicleId + " · runtime revision " + trace.revision + "\nTick " + trace.telemetryTick + " at " + trace.timestamp.ToString("F6") + " s → " + trace.requestedRpm.ToString("F2") + " RPM / " + trace.requestedLoad.ToString("F3") + " load / gear " + trace.gear + " / running " + trace.engineRunning);
            Text("Renderer path", "Authored RPM lookup → region " + trace.regionIndex + " → source frame " + trace.sourceFrame + " at " + trace.sampleRate + " Hz / " + trace.channels + " channels → source/output ratio " + trace.sourceRate.ToString("G8") + "\nSource region/layer gain " + trace.sourceRegionGain.ToString("G5") + " × renderer gain " + trace.gain.ToString("G5") + " × supplied route attenuation " + trace.routeAttenuation.ToString("G5") + " = " + trace.effectiveGain.ToString("G5") + " before grain windows\nReset epoch " + trace.resetEpoch + " → output [" + trace.outputFrameStart + ", " + (trace.outputFrameStart + trace.outputFrames) + ")");
            if (trace.grainLength > 0) Text("Actual grain event", "Output start " + trace.grainStartOutputFrame + " · " + trace.grainLength + " output frames · source interval offset " + trace.sourceOffset + " · hop/length " + trace.hopRatio);
            var captured = liveMode ? live?.NativeRenderer?.Snapshot : render?.snapshot;
            if (captured != null && captured.Token == trace.snapshotToken && trace.regionIndex >= 0 && trace.regionIndex < captured.RegionCount)
            {
                var region = captured.GetRegionInfo(trace.regionIndex); Text("Compiled snapshot evidence", "Profile " + captured.ProfileId + " · " + region.SourceHash + " / " + region.RecordingId + " / " + region.Id + "\n" + region.Provenance + " · " + region.Evidence);
            }
            else Text("Snapshot / authoring distinction", "This trace describes the renderer revision above. Current authoring overlays may differ. Do not attribute them to this event without matching the compiled identity.");
            EditorGUILayout.LabelField(liveMode ? "Emitter and post-filter mix use the selected presenter's existing SensoryAudioWorld route. These pre-mixer samples do not measure listener/spatializer attenuation or device latency." : "Offline emitter: none. Route attenuation 1. Preview monitor volume and device output are downstream of this captured PCM.", EditorStyles.wordWrappedMiniLabel);
        }
        private void BeginRender(bool existing, int through, bool listen = false)
        {
            EngineAudioRegion[] regions; string fingerprint;
            if (existing)
            {
                if (session.profile == null) throw new InvalidOperationException("Select an existing sound profile first.");
                regions = session.profile.engineLayers.Where(l => l != null && l.nativeRegions != null).SelectMany(l => l.nativeRegions).ToArray();
                fingerprint = "Native profile " + session.profile.engineAudioId + " / " + session.profile.engineAudioRevision;
            }
            else
            {
                if (session.regions.Count < 1 || session.regions.Any(r => !r.reviewed || string.IsNullOrWhiteSpace(r.assumptions) || r.provenance != "Authored override")) throw new InvalidOperationException("Add and explicitly review authored regions with their assumptions in RPM & regions.");
                if (presenter.Documents.Any(d => d.IsStale)) throw new InvalidOperationException("Source revision changed; remapping review is required.");
                regions = BlackBoxNativeMapping.CreateRegions(session, presenter.Documents, false); fingerprint = BlackBoxNativeMapping.Fingerprint(session);
            }
            EngineAudioCompiledSnapshot snapshot;
            bool compiled = existing ? EngineAudioCompiler.TryBuildProfile(session.profile, out snapshot) : EngineAudioCompiler.TryBuildSnapshot(regions, session.revision, out snapshot, session.id);
            if (!compiled) throw new InvalidOperationException("Native compilation failed: no valid reviewed PCM/mapping snapshot is available.");
            var settings = replaySettings.Copy(); settings.Validate();
            presenter.Start(c => BlackBoxReplay.Render(snapshot, settings, fingerprint, through, c), value => { render = value; traceCacheDirty = true; difference = null; comparison = ""; tracePage = selectedTrace = 0; cursorFrame = (int)value.summary.FrameCount; presenter.SetStatus("Preview ready. Apply the mapping to your vehicle when it sounds right."); if (listen) PlayRender(value, "Engine mapping preview"); });
        }
        private void PlayRender(BlackBoxReplayResult value, string label)
        { audition.Play(value.pcm, value.settings.outputRate, value.settings.channels, 0, (int)value.summary.FrameCount, monitorGain, label); }
        private void DrawAuditionButton(float[] pcm, int rate, int channels, int start, int end, string label, string subject, params GUILayoutOption[] options)
        {
            bool playing = audition.Matches(pcm, start, end) && audition.Active;
            if (GUILayout.Button((playing ? "Ⅱ Pause " : "▶ Play ") + subject, options))
                Run(() => { audition.Toggle(pcm, rate, channels, start, end, monitorGain, label); Repaint(); });
        }
        private void DrawAuditionProgress(float[] pcm, int rate, int start, int end)
        {
            if (rate <= 0 || end <= start) return;
            bool selected = audition.Matches(pcm, start, end);
            int frame = selected ? Mathf.Clamp(audition.Frame, 0, end - start) : 0;
            string elapsed = PlaybackTime(frame / (double)rate), duration = PlaybackTime((end - start) / (double)rate);
            string state = selected && audition.Paused ? " · Paused" : selected && audition.Active ? " · Playing" : "";
            EditorGUI.ProgressBar(GUILayoutUtility.GetRect(100, 20, GUILayout.ExpandWidth(true)), frame / (float)(end - start), elapsed + " / " + duration + state);
        }
        private static string PlaybackTime(double seconds) => ((int)(seconds / 60)).ToString() + ":" + (seconds % 60).ToString("00.0", System.Globalization.CultureInfo.InvariantCulture);
        private void Queue(Action action)
        {
            var owner = presenter; var selectedSession = session;
            presenter.SetStatus("Preparing the requested operation…");
            EditorApplication.delayCall += () =>
            {
                if (this != null && presenter == owner && session == selectedSession) { Run(action); Repaint(); }
            };
        }
        [Serializable] private sealed class ReplayExport
        { public int schema = 1; public string fingerprint, interpretation = "Authored native reconstruction; interactive parity unverified"; public BlackBoxReplaySettings settings; public BlackBoxTelemetry[] telemetry; public EngineAudioTrace[] traces; public long dropped; }
        private void ExportReplay()
        {
            string path = EditorUtility.SaveFilePanel("Export native output and trace", "", "native-render", "wav"); if (path.Length == 0) return;
            if (File.Exists(path) || File.Exists(path + ".json")) throw new IOException("Choose a new export path."); var value = render;
            presenter.Start(c =>
            {
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp"; bool moved = false;
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) BlackBoxWaveExport.Write(stream, value.pcm, value.settings.outputRate, value.settings.channels, 0, (int)value.summary.FrameCount, c);
                    File.WriteAllText(temporary + ".json", JsonUtility.ToJson(new ReplayExport { fingerprint = value.fingerprint, settings = value.settings, telemetry = value.telemetry, traces = value.traces, dropped = value.dropped }, true));
                    c.ThrowIfCancellationRequested(); File.Move(temporary, path); moved = true; File.Move(temporary + ".json", path + ".json"); return path;
                }
                catch { if (moved && File.Exists(path)) File.Delete(path); throw; }
                finally { if (File.Exists(temporary)) File.Delete(temporary); if (File.Exists(temporary + ".json")) File.Delete(temporary + ".json"); }
            }, valuePath => presenter.SetStatus("Exported native output and sample-coordinate trace: " + valuePath));
        }
        [SerializeField] private bool exportDetailsOpen, attachVehicleOpen, comparisonOpen, replayDetailsOpen, fileExportOpen;
        private string lastPackagePath = "";
        private void DrawNative()
        {
            GUILayout.Label("Apply audio to your vehicle", EditorStyles.largeLabel);
            DrawVehicleAttachment();
            DrawCompleteAttachment();
            var completeSaved = AssetDatabase.LoadAssetAtPath<VehicleSensoryProfile>(session.generatedProfilePath);
            if (completeSaved != null && completeSaved.HasCompleteAudio) DrawAttachedVehicle(completeSaved);
            GUILayout.Space(12);
            manualEngineAttachment = EditorGUILayout.Foldout(manualEngineAttachment, "Manual engine mappings and file export", true);
            if (!manualEngineAttachment) return;
            EditorGUILayout.LabelField("Attach your reviewed RPM/load mappings as engine layers.", EditorStyles.wordWrappedMiniLabel);
            int pending = session.regions.Count(r => !r.reviewed);
            if (session.regions.Count == 0 || pending > 0)
            {
                EditorGUILayout.HelpBox(session.regions.Count == 0 ? "Create one mapping to attach engine audio." : "Review " + pending + " mapping(s) before attaching the profile.", MessageType.Info);
                if (GUILayout.Button("Review mappings →", GUILayout.Height(28))) ShowView(2);
            }
            else EditorGUILayout.LabelField("✓ " + session.regions.Count + " mappings reviewed", EditorStyles.boldLabel);
            GUILayout.Space(8);
            EditorGUI.BeginChangeCheck();
            var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(session.outputFolder);
            var nextFolder = (DefaultAsset)EditorGUILayout.ObjectField("Audio assets folder", folder, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(session, "Set output folder"); session.outputFolder = AssetDatabase.GetAssetPath(nextFolder); presenter.Touch(); }
            if (string.IsNullOrEmpty(session.outputFolder)) EditorGUILayout.LabelField("The profile and mapped audio will be saved under Assets/NfsMw/Content/Audio.", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUI.DisabledScope(presenter.Busy || attachmentTarget == null || session.regions.Count == 0 || pending > 0 || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Prepare attachment", GUILayout.Height(30))) Queue(PrepareAttachment);
            DrawAttachmentPlan();
            var saved = AssetDatabase.LoadAssetAtPath<VehicleSensoryProfile>(session.generatedProfilePath);
            DrawAttachedVehicle(saved);
            GUILayout.Space(12);
            fileExportOpen = EditorGUILayout.Foldout(fileExportOpen, "Export files (optional)", true);
            if (fileExportOpen)
            {
                using (new EditorGUI.DisabledScope(presenter.Busy || session.regions.Count == 0 || pending > 0))
                    if (GUILayout.Button("1. Prepare export", GUILayout.Height(30))) Queue(PrepareExport);
                var plan = presenter.Plan;
                if (plan != null && plan.SceneTarget == null)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        GUILayout.Label("Included in the export", EditorStyles.boldLabel);
                        EditorGUILayout.LabelField("✓ Vehicle audio profile (.asset)");
                        EditorGUILayout.LabelField("✓ Mapped recordings (.wav + source manifests)");
                        EditorGUILayout.LabelField("✓ RPM/load mapping and source evidence (.json)");
                        EditorGUILayout.LabelField(plan.ProfilePath, EditorStyles.wordWrappedMiniLabel);
                        using (new EditorGUI.DisabledScope(presenter.Busy))
                            if (GUILayout.Button("2. Save engine profile", GUILayout.Height(30))) Queue(() => ApplyPlan(false));
                    }
                    exportDetailsOpen = EditorGUILayout.Foldout(exportDetailsOpen, "Export details and fidelity notes", true);
                    if (exportDetailsOpen) { Text("Files", string.Join("\n", plan.Changes)); Text("Interpretation", string.Join("\n", plan.Losses)); }
                }
                if (!string.IsNullOrEmpty(session.generatedProfilePath))
                {
                    GUILayout.Space(8);
                    EditorGUILayout.ObjectField("Saved profile", saved, typeof(VehicleSensoryProfile), false);
                    using (new EditorGUI.DisabledScope(presenter.Busy))
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Select saved profile")) { Selection.activeObject = saved; EditorGUIUtility.PingObject(saved); }
                        if (GUILayout.Button("Export Unity package…")) Queue(() =>
                        {
                            string path = EditorUtility.SaveFilePanel("Export profile, audio and mapping", "", "Vehicle-audio", "unitypackage");
                            if (path.Length == 0) return;
                            BlackBoxAudioExport.ExportPackage(path, session, presenter.Documents); lastPackagePath = path;
                            presenter.SetStatus("Unity package saved: " + path);
                        });
                    }
                    if (lastPackagePath.Length > 0) Text("Package saved", lastPackagePath);
                }
                using (new EditorGUI.DisabledScope(presenter.Busy)) if (GUILayout.Button("Export all decoded recordings…")) Run(() =>
                {
                    string path = EditorUtility.OpenFolderPanel("Choose a parent folder for all recordings and evidence", "", ""); if (path.Length == 0) return;
                    var documents = presenter.Documents;
                    presenter.Start(c => BlackBoxAudioExport.ExportRecordings(path, documents, c), result => { presenter.SetStatus("All decoded recordings exported: " + result); EditorUtility.RevealInFinder(result); });
                });
                EditorGUILayout.LabelField("Raw recording export does not need an RPM mapping.", EditorStyles.wordWrappedMiniLabel);
            }
            GUILayout.Space(12);
            attachVehicleOpen = EditorGUILayout.Foldout(attachVehicleOpen, "Attach to a saved vehicle prefab", true);
            if (attachVehicleOpen)
            {
                EditorGUI.BeginChangeCheck();
                var vehicle = (VehicleProfileDraft)EditorGUILayout.ObjectField("Vehicle draft", session.vehicle, typeof(VehicleProfileDraft), false);
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(session, "Select target vehicle"); session.vehicle = vehicle; presenter.Touch(); }
                EditorGUILayout.LabelField("Requires a saved prefab with one existing VehicleAudio.", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(presenter.Busy || vehicle == null || session.regions.Count == 0 || pending > 0))
                    if (GUILayout.Button("Save and attach profile")) Queue(() => { PrepareExport(); ApplyPlan(true); });
            }
            GUILayout.Space(12);
            comparisonOpen = EditorGUILayout.Foldout(comparisonOpen, "Advanced comparison and recovery", true);
            if (!comparisonOpen) return;
            if (GUILayout.Button("Recover interrupted generation")) Queue(() => presenter.SetStatus("Restored " + BlackBoxNativeMapping.RecoverInterrupted() + " interrupted generations."));
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Comparison · A = latest native render", EditorStyles.boldLabel);
            Text("Reference B", referenceLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use selected decoded source as B")) Queue(() =>
                {
                    var r = Recording; if (r == null || !r.Decoded) throw new InvalidOperationException("Choose decoded PCM in Sources / RPM first.");
                    var doc = Document;
                    presenter.Start(c => new BlackBoxReplayResult { pcm = r.Pcm, settings = new BlackBoxReplaySettings { outputRate = r.SampleRate, channels = r.Channels }, summary = SignalAnalysis.Summarize(r.Pcm, r.SampleRate, r.Channels, c), fingerprint = doc.Report.Source.Sha256 }, result =>
                    { referenceRender = result; referenceLabel = "Raw decode " + r.Id + "; source signal comparison only. Interactive capture context must be supplied separately."; });
                });
                using (new EditorGUI.DisabledScope(referenceRender == null)) if (GUILayout.Button("Hear B")) Run(() => PlayRender(referenceRender, "Reference B · " + referenceLabel));
                using (new EditorGUI.DisabledScope(render == null)) if (GUILayout.Button("Hear A")) Run(() => PlayRender(render, "Native reconstruction A"));
            }
            EditorGUI.BeginChangeCheck(); string captureContext = EditorGUILayout.TextField("B capture · game/build/platform, telemetry, mixer, limitations", session.referenceCaptureContext);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(session, "Record reference capture context"); session.referenceCaptureContext = captureContext; presenter.Touch(); }
            latencyFrames = EditorGUILayout.IntField("Declared latency · + skips A frames", latencyFrames);
            using (new EditorGUI.DisabledScope(presenter.Busy || render == null || referenceRender == null)) if (GUILayout.Button("Compare PCM, spectra and renderer decisions")) Run(CompareRenders);
            if (comparison.Length > 0) Text("Comparison result", comparison);
            if (difference != null && difference.Length > 0 && GUILayout.Button("Hear A − B difference")) Run(() => audition.Play(difference, render.settings.outputRate, render.settings.channels, 0, difference.Length / render.settings.channels, monitorGain, "Declared-alignment difference; original buffers retained"));
            EditorGUILayout.HelpBox("Decode checks, mapping/control checks and interactive output parity are distinct. No game capture with matched telemetry/listener/mixer context is attached here. Unaligned buffers are retained; no normalization or time warping is applied.", MessageType.None);
        }
        private void PrepareExport()
        {
            EnsureOutputFolder();
            presenter.Plan = BlackBoxNativeMapping.Prepare(session, presenter.Documents, true);
            presenter.SetStatus("Export prepared. Save the engine profile to create the listed files.");
        }
        private void EnsureOutputFolder()
        {
            if (string.IsNullOrEmpty(session.outputFolder))
            {
                session.ValidateSchema();
                if (!AssetDatabase.IsValidFolder("Assets/NfsMw/Content/Audio")) AssetDatabase.CreateFolder("Assets", "Audio");
                string folder = "Assets/NfsMw/Content/Audio/Engine-" + session.id;
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/NfsMw/Content/Audio", "Engine-" + session.id);
                Undo.RecordObject(session, "Choose generated output folder"); session.outputFolder = folder; presenter.Touch();
            }
        }
        private void ApplyPlan(bool vehicle)
        {
            var profile = BlackBoxNativeMapping.Apply(presenter.Plan, vehicle); EditorUtility.SetDirty(session);
            presenter.SetStatus(vehicle ? "Reviewed native profile generated and bound to the existing vehicle presenter." : "Separate authored native profile saved; select it for production-renderer audition.");
            Selection.activeObject = profile;
        }
        private void CompareRenders()
        {
            var a = render; var b = referenceRender; int latency = latencyFrames;
            if (a.settings.outputRate != b.settings.outputRate || a.settings.channels != b.settings.channels) throw new InvalidOperationException("Comparison requires matching rate and channels. Select the matching native output format; no implicit resampling/downmix is allowed.");
            presenter.Start(c =>
            {
                var result = SignalAnalysis.Compare(a.pcm, b.pcm, a.settings.channels, latency); int channels = a.settings.channels;
                int count = checked((int)result.ComparedFrames * channels); var delta = new float[count];
                int offsetA = checked(Math.Max(0, latency) * channels), offsetB = checked(Math.Max(0, -latency) * channels);
                for (int i = 0; i < count; i++) { if ((i & 8191) == 0) c.ThrowIfCancellationRequested(); delta[i] = a.pcm[offsetA + i] - b.pcm[offsetB + i]; }
                string text = result.ComparedFrames + " overlapping frames · original lengths match: " + result.LengthsMatch + " · peak difference " + result.PeakDifference.ToString("G8") + " · RMS difference " + result.RmsDifference.ToString("G8") + "\nUnaligned source buffers preserved; declared offset " + latency + " frames.";
                text += "\n" + BlackBoxReplayComparison.CompareDecisions(a, b, latency, c);
                if (count >= 1024 * channels)
                {
                    var xa = new float[count]; var xb = new float[count]; Array.Copy(a.pcm, offsetA, xa, 0, count); Array.Copy(b.pcm, offsetB, xb, 0, count);
                    var parameters = new SpectrogramParameters { WindowSize = 1024, HopSize = Math.Max(256, count / channels / 256), MaxColumns = 256 };
                    var sa = SignalAnalysis.Spectrogram(xa, a.settings.outputRate, channels, parameters, c); var sb = SignalAnalysis.Spectrogram(xb, b.settings.outputRate, channels, parameters, c);
                    double sum = 0; for (int i = 0; i < sa.Magnitudes.Length; i++) sum += Math.Abs(sa.Magnitudes[i] - sb.Magnitudes[i]);
                    text += "\nMean absolute STFT magnitude difference (channel 0): " + (sum / Math.Max(1, sa.Magnitudes.Length)).ToString("G8") + " · Hann1024, hop " + parameters.HopSize;
                }
                return Tuple.Create(delta, text);
            }, result => { difference = result.Item1; comparison = result.Item2; });
        }
    }
}
