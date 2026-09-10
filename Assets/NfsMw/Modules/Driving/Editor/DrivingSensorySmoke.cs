#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Unity.Profiling;

namespace NfsMwRemaster.Driving.Editor
{
    [InitializeOnLoad]
    public static class DrivingSensorySmoke
    {
        private const string Key = "NfsSensorySmoke";
        private static int stage, samples, previousFrame, pausedSequence, maxVoices, maxParticles, maxDebris;
        private static double deadline, began, phaseAt;
        private static long maxGc;
        private static VehicleAudio source;
        private static SensoryAudioWorld world;
        private static SensoryEffectsWorld effects;
        private static DestructionWorld debris;
        private static VehiclePoliceUnit[] units;
        private static FeedbackVoiceLease[] leases;
        private static AudioClip diagnostic;
        private static ParticleSystem effect;
        private static ProfilerRecorder gc;
        private static float[] telemetryNanoseconds = new float[1024];
        private static ProfilerRecorder telemetry;
        private static Vector3 startPosition;
        private static bool failure;
        private static bool enginePlayed, musicPlayed, sirenPlayed;
        private static int contentVoicesPeak;
        private static AudioSource[] emitters;
        private static readonly float[] mixedSamples = new float[1024];
        private static float mixedPeak;
        static DrivingSensorySmoke() { if (SessionState.GetBool(Key, false)) EditorApplication.delayCall += Resume; }
        public static void Run()
        {
            if (!Application.isBatchMode || !Application.dataPath.StartsWith("/private/tmp/", StringComparison.Ordinal))
                throw new InvalidOperationException("Use an isolated /private/tmp Unity project for sensory smoke.");
            EditorSceneManager.OpenScene("Assets/NfsMw/Scenes/Tests/SensoryTest.unity");
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0, "Missing serialized script: " + transform.name);
            foreach (var profile in UnityEngine.Object.FindObjectsByType<CareerProfileSystem>(FindObjectsSortMode.None)) profile.ConfigureAutomaticPersistence(false, false, false);
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true; Resume();
        }
        public static void BuildMacPlayer()
        {
            if (!Application.isBatchMode || !Application.dataPath.StartsWith("/private/tmp/", StringComparison.Ordinal))
                throw new InvalidOperationException("Use an isolated /private/tmp Unity project for verification builds.");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/NfsMw/Scenes/Tests/SensoryTest.unity" }, target = BuildTarget.StandaloneOSX,
                locationPathName = "Builds/SensoryVerification.app", options = BuildOptions.Development
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Sensory macOS player build failed: " + report.summary.result);
            Debug.Log("SENSORY_MAC_PLAYER_BUILD_PASS bytes=" + report.summary.totalSize);
        }
        private static void Resume()
        {
            deadline = EditorApplication.timeSinceStartup + 120; stage = samples = previousFrame = 0; failure = false;
            maxVoices = maxParticles = maxDebris = 0; maxGc = 0;
            enginePlayed = musicPlayed = sirenPlayed = false; contentVoicesPeak = 0;
            mixedPeak = 0;
            Application.logMessageReceived -= Log; Application.logMessageReceived += Log;
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
        }
        private static void Log(string message, string trace, LogType type)
        {
            // Existing Unity 6000.6 editor search-index defect; never exempt runtime stack traces.
            if (type == LogType.Exception && trace.Contains("UnityEditor.Search.SearchDatabase")) return;
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) failure = true;
        }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Next() { stage++; phaseAt = EditorApplication.timeSinceStartup; }
        private static void Poll()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Sensory smoke timed out at " + stage);
                if (!EditorApplication.isPlaying || Time.time < 0.1f) return;
                if (failure) throw new Exception("Unity reported a runtime error. See preceding log.");
                switch (stage)
                {
                    case 0:
                        var rig = UnityEngine.Object.FindAnyObjectByType<VehicleCameraRig>(); source = rig.Target.GetComponent<VehicleAudio>();
                        world = UnityEngine.Object.FindAnyObjectByType<SensoryAudioWorld>(); effects = UnityEngine.Object.FindAnyObjectByType<SensoryEffectsWorld>();
                        debris = UnityEngine.Object.FindAnyObjectByType<DestructionWorld>();
                        units = UnityEngine.Object.FindObjectsByType<VehiclePoliceUnit>(FindObjectsSortMode.None);
                        Require(units.Length >= 24, "Stress fleet missing.");
                        Require(UnityEngine.Object.FindObjectsByType<RoadVehicleMotor>(FindObjectsSortMode.None).Length >= 16, "Stress traffic missing.");
                        Require(source != null && source.Frame.Sequence > 0 && source.Frame.EngineRpm > 0 && source.Frame.WheelCount == 4, "Live telemetry is not connected.");
                        Require(source.Profile.Validate(out string contentFailure), contentFailure);
                        var engineClip = source.Profile.engineLayers[0].regions[0].offLoad;
                        Require(engineClip != null && engineClip.loadState == AudioDataLoadState.Loaded, "Installed engine test clip is missing or unloaded.");
                        var pcm = new float[engineClip.samples * engineClip.channels];
                        Require(engineClip.GetData(pcm, 0) && Array.Exists(pcm, sample => Mathf.Abs(sample) > 0.01f), "Engine content is silent.");
                        var mix = AssetDatabase.LoadAssetAtPath<SensoryMixProfile>(DrivingDemoBuilder.SensoryFolder + "/SensoryMix.asset");
                        Require(mix != null && mix.routes.Length == 9 && mix.snapshots.Length == 6, "Mix graph incomplete.");
                        foreach (var route in mix.routes) Require(route.group != null && route.group.audioMixer == mix.mixer, "Unrouted category.");
                        foreach (var snapshot in mix.snapshots) Require(snapshot.snapshot != null && mix.mixer.FindSnapshot(snapshot.state.ToString()) == snapshot.snapshot, "Snapshot not serialized.");
                        foreach (string parameter in new[] { "MasterVolume", "VehicleVolume", "EffectsVolume", "MusicVolume", "PoliceVolume" })
                            Require(mix.mixer.GetFloat(parameter, out _), "Exposed category missing: " + parameter);
                        diagnostic = AudioClip.Create("TEST ONLY silent admission fixture", 48000, 1, 48000, false);
                        // Isolate allocator assertions from already-playing content for this synchronous block only.
                        world.enabled = false; world.enabled = true;
                        leases = new FeedbackVoiceLease[world.VoiceLimit];
                        for (int i = 0; i < leases.Length; i++) leases[i] = world.Play(diagnostic, SensoryCategory.UI, null, Vector3.zero, 0, 1, 200, true);
                        Require(world.ActiveVoices == world.VoiceLimit, "Voice fixture did not reach budget.");
                        var important = world.Play(diagnostic, SensoryCategory.Radio, null, Vector3.zero, 0, 1, 4, true);
                        Require(world.Owns(important) && !world.Owns(leases[0]), "Stealing failed.");
                        world.Release(leases[0]); Require(world.Owns(important), "Stale return released new owner.");
                        leases[0] = important;
                        maxVoices = world.ActiveVoices;
                        foreach (var lease in leases) world.Release(lease);
                        Require(UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length == world.VoiceLimit, "An audio emitter bypassed the world pool.");
                        emitters = world.GetComponentsInChildren<AudioSource>();
                        effect = AssetDatabase.LoadAssetAtPath<GameObject>(DrivingDemoBuilder.SensoryFolder + "/DiagnosticSmoke.prefab").GetComponent<ParticleSystem>();
                        foreach (var prop in UnityEngine.Object.FindObjectsByType<DestructibleProp>(FindObjectsSortMode.None))
                        {
                            var impact = new FeedbackImpact { Severity = 0.8f, Impulse = 6000, Point = prop.transform.position, Normal = Vector3.up };
                            Require(prop.ApplyImpact(impact, source.GetComponent<VehicleController>()), "First break rejected."); Require(!prop.ApplyImpact(impact, source.GetComponent<VehicleController>()), "Break duplicated.");
                        }
                        Require(debris.BreaksPublished == 12, "Destruction facts not exactly once.");
                        foreach (var unit in units)
                        {
                            unit.SetDirector(null);
                            unit.SetPursuitCommand(new VehiclePoliceUnitCommand(VehiclePursuitPhase.Engaged, VehiclePoliceTactic.Chase,
                                unit.Position + Vector3.forward * 120, 1, 0.6f, 0, 0, 0), null, new VehiclePoliceResponseDefaultProfile().GetTier(3));
                        }
                        startPosition = units[0].Position;
                        gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                        telemetry = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Sensory.Telemetry");
                        began = EditorApplication.timeSinceStartup; Next(); break;
                    case 1:
                        foreach (var emitter in emitters)
                        {
                            if (emitter.clip == null || emitter.clip == diagnostic || !emitter.isPlaying || emitter.volume <= 0) continue;
                            string name = emitter.clip.name;
                            enginePlayed |= name.StartsWith("engine-", StringComparison.Ordinal);
                            musicPlayed |= name.StartsWith("music-", StringComparison.Ordinal);
                            sirenPlayed |= name.StartsWith("siren-", StringComparison.Ordinal);
                        }
                        contentVoicesPeak = Mathf.Max(contentVoicesPeak, world.ActiveVoices);
                        AudioListener.GetOutputData(mixedSamples, 0);
                        foreach (float value in mixedSamples) mixedPeak = Mathf.Max(mixedPeak, Mathf.Abs(value));
                        for (int i = 0; i < 32; i++) effects.Emit(effect, source.transform.position, Vector3.up, Vector3.zero, 24, 1);
                        maxVoices = Mathf.Max(maxVoices, world.ActiveVoices); maxParticles = Mathf.Max(maxParticles, effects.LiveParticles);
                        maxDebris = Mathf.Max(maxDebris, debris.ActiveDebris);
                        Require(world.ActiveVoices <= world.VoiceLimit && effects.LiveParticles <= effects.ParticleLimit && debris.ActiveDebris <= debris.Capacity, "Budget overflow.");
                        if (Time.frameCount != previousFrame && samples < telemetryNanoseconds.Length)
                        { previousFrame = Time.frameCount; telemetryNanoseconds[samples++] = telemetry.Valid ? telemetry.LastValue : -1; if (gc.Valid) maxGc = Math.Max(maxGc, gc.LastValue); }
                        if (EditorApplication.timeSinceStartup - began < 6) return;
                        Require(Vector3.Distance(units[0].Position, startPosition) > 1, "Police stress rig did not move physically.");
                        Require(enginePlayed && musicPlayed && sirenPlayed, "Installed engine, music and siren clips did not all play through the pool.");
                        Require(mixedPeak > 0.0001f, "AudioListener rendered silence. Run the sound smoke with audio enabled (not -nographics), and check mute/output settings.");
                        pausedSequence = source.Frame.Sequence; Time.timeScale = 0; AudioListener.pause = true; Next(); break;
                    case 2:
                        if (EditorApplication.timeSinceStartup - phaseAt < 0.25) return;
                        Require(source.Frame.Sequence == pausedSequence, "Telemetry advanced during pause.");
                        Time.timeScale = 1; AudioListener.pause = false;
                        world.enabled = false; Require(world.ActiveVoices == 0, "Disable leaked voices.");
                        world.enabled = true;
                        source.GetComponent<VehicleController>().NotifyPoseReset(); Require(source.Source.Frame.Sequence == 0, "Respawn retained old telemetry.");
                        SceneManager.LoadScene("SensoryTest"); Next(); break;
                    case 3:
                        if (EditorApplication.timeSinceStartup - phaseAt < 0.3) return;
                        var fresh = UnityEngine.Object.FindAnyObjectByType<SensoryAudioWorld>();
                        Require(fresh != null && fresh != world, "Scene did not replace sensory world.");
                        Require(UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length == fresh.VoiceLimit, "Scene reload accumulated voices.");
                        Array.Sort(telemetryNanoseconds, 0, samples);
                        Debug.Log("SENSORY_SMOKE_PASS units=24 traffic=16 voicesPeak=" + maxVoices + " particlesPeak=" + maxParticles
                            + " debrisPeak=" + maxDebris + " sampledFrames=" + samples + " telemetryLastValueP95Ns="
                            + (samples > 0 ? telemetryNanoseconds[Mathf.Min(samples - 1, (int)(samples * 0.95f))] : -1)
                            + " editorWholeFrameGcPeakBytes=" + maxGc + " contentVoicesPeak=" + contentVoicesPeak
                            + " enginePlayed=" + enginePlayed + " musicPlayed=" + musicPlayed + " sirenPlayed=" + sirenPlayed
                            + " listenerOutputPeak=" + mixedPeak
                            + " (batch editor, diagnostic content; NOT a shipping CPU/audio/GPU measurement)");
                        Finish(0); break;
                }
            }
            catch (Exception exception) { Debug.LogException(exception); Finish(1); }
        }
        private static void Finish(int code)
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Poll; Application.logMessageReceived -= Log;
            Time.timeScale = 1; AudioListener.pause = false; gc.Dispose(); telemetry.Dispose();
            if (diagnostic != null) UnityEngine.Object.Destroy(diagnostic);
            EditorApplication.Exit(code);
        }
    }
}
#endif
