using System;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting.APIUpdating;

namespace NfsMwRemaster.Driving
{
    /// <summary>The only vehicle-audio component authors attach. All playback is owned and released here.</summary>
    [MovedFrom(true, "NfsMwRemaster.Driving", null, "VehicleSensoryPresenter")]
    [AddComponentMenu("NFS MW Remaster/Vehicle Audio"), DisallowMultipleComponent]
    public sealed class VehicleAudio : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour sourceComponent;
        [SerializeField] private SensoryAudioWorld world;
        [SerializeField] private VehicleSensoryProfile profile;
        [SerializeField] private VehicleCameraRig cameraRig;
        [SerializeField] private SensoryEffectsWorld effects;
        [SerializeField] private bool player;
        [SerializeField] private bool useBankOverride;
        [SerializeField] private MostWantedVehicleAudio bankOverride;
        [SerializeField] private VehicleAudioMix mix = new VehicleAudioMix();
        [SerializeField] private VehicleAudioSound[] sounds = Array.Empty<VehicleAudioSound>();
        [SerializeField] private bool manualInput;
        private IVehicleFeedbackSource source;
        private VehicleFeedbackSampler sampler;
        private VehicleController controller;
        private VehicleAudioRuntime runtime;
        private VehicleFeedbackFrame frame;
        private bool seenFrame, failed, horn, siren;
        private string status = "Waiting for telemetry";
        private readonly EngineAudioRenderer nativeRenderer = new EngineAudioRenderer(128);
        private static readonly ProfilerMarker Marker = new ProfilerMarker("Sensory.VehicleAudio");
        public VehicleSensoryProfile Profile => profile;
        public IVehicleFeedbackSource Source => source;
        public VehicleFeedbackFrame Frame => frame;
        public VehicleAudioMix Mix => mix;
        public VehicleAudioSound[] Sounds => sounds;
        public MostWantedVehicleAudio Banks => useBankOverride ? bankOverride : profile != null && profile.HasCompleteAudio ? profile.mostWantedAudio : null;
        public string RuntimeStatus => status;
        public int ActiveLayers => runtime?.ActiveLayers ?? 0;
        public long ControlTicks => runtime?.ControlTicks ?? 0;
        public bool ManualInput
        {
            get => manualInput;
            set { if (manualInput == value) return; DetachSource(); manualInput = value; ReleasePlayback(); seenFrame = false; if (isActiveAndEnabled) AttachSource(); }
        }
        public EngineAudioRenderer NativeRenderer => runtime?.AccelerationRenderer ?? nativeRenderer;
        public void SetNativeTelemetry(EngineAudioCompiledSnapshot snapshot) => NativeRenderer.SetTelemetry(snapshot);
        public int RenderNative(float rpm, float load, int sampleRate, int frames, float gain, float routeAttenuation,
            float[] output, int maxVoices = 4, int channels = 1)
            => NativeRenderer.Render(rpm, load, sampleRate, frames, gain, routeAttenuation, output, maxVoices, channels);
        public void SetEffects(SensoryEffectsWorld particles) { effects = particles; }
        public void Configure(MonoBehaviour telemetry, SensoryAudioWorld audioWorld, VehicleSensoryProfile content, bool isPlayer, VehicleCameraRig rig = null)
        {
            DetachSource(); ReleasePlayback(); sourceComponent = telemetry; world = audioWorld; profile = content; player = isPlayer; cameraRig = rig;
            failed = false; seenFrame = false; frame = default;
            if (isActiveAndEnabled) AttachSource();
            BuildNativeSnapshot();
        }
        private void OnEnable() { failed = false; seenFrame = false; AttachSource(); }
        private void Start() => BuildNativeSnapshot();
        private void BuildNativeSnapshot()
        { if (profile != null && !profile.HasCompleteAudio && EngineAudioCompiler.TryBuildProfile(profile, out var snapshot)) nativeRenderer.SetTelemetry(snapshot); }
        private void AttachSource()
        {
            if (manualInput || source != null) return;
            source = sourceComponent as IVehicleFeedbackSource;
            if (source is UnityEngine.Object sourceObject && sourceObject == null) source = null;
            if (source == null && !(sourceComponent is VehicleController))
            {
                foreach (var candidate in GetComponents<MonoBehaviour>())
                    if (candidate is IVehicleFeedbackSource feedback && (!(feedback is UnityEngine.Object unityObject) || unityObject != null)) { source = feedback; break; }
            }
            if (source == null)
            {
                controller = sourceComponent as VehicleController;
                if (controller == null) controller = GetComponentInParent<VehicleController>();
                if (controller != null)
                {
                    sampler = new VehicleFeedbackSampler(controller); source = sampler;
                    controller.PhysicsSampled += sampler.Capture; controller.PoseReset += sampler.ResetHistory;
                }
            }
            if (source != null) { source.Sampled += SubmitFrame; source.Impact += SubmitImpact; }
            else status = "Assign telemetry, attach to a VehicleController, or enable manual input for rehearsal.";
        }
        private void DetachSource()
        {
            if (source != null) { source.Sampled -= SubmitFrame; source.Impact -= SubmitImpact; }
            if (controller != null && sampler != null) { controller.PhysicsSampled -= sampler.Capture; controller.PoseReset -= sampler.ResetHistory; }
            source = null; controller = null; sampler = null;
        }
        public void Rebuild()
        {
            DetachSource(); ReleasePlayback(); failed = false; seenFrame = false;
            if (mix == null) mix = new VehicleAudioMix();
            if (sounds == null) sounds = Array.Empty<VehicleAudioSound>();
            if (isActiveAndEnabled) AttachSource();
            BuildNativeSnapshot(); status = "Ready; waiting for telemetry";
        }
        public void SubmitFrame(VehicleFeedbackFrame value)
        {
            if (!isActiveAndEnabled || failed) return;
            if (!ValidFrame(value))
            { Fail(new InvalidOperationException("Vehicle audio received invalid telemetry.")); return; }
            if (seenFrame && value.Epoch == frame.Epoch && value.Sequence <= frame.Sequence) return;
            if (seenFrame && value.Epoch != frame.Epoch) { ReleasePlayback(); value.Edges = FeedbackEdges.None; }
            frame = value; seenFrame = true;
            try { if (EnsureRuntime()) runtime.Sample(frame); }
            catch (Exception error) { Fail(error); }
        }
        public void SubmitImpact(FeedbackImpact impact)
        {
            if (!isActiveAndEnabled || failed) return;
            if (!ValidImpact(impact)) { Fail(new InvalidOperationException("Vehicle audio received invalid impact telemetry.")); return; }
            try { if (EnsureRuntime()) runtime.Impact(impact); }
            catch (Exception error) { Fail(error); return; }
            if (profile == null || effects == null) return;
            var surface = profile.Surface(impact.Material);
            var pair = profile.impactMaterials != null ? profile.impactMaterials.Find(impact.BodyMaterial, impact.Material) : null;
            effects.Emit(pair != null ? pair.particles : surface != null ? surface.impactEffect : null,
                impact.Point, impact.Normal, Vector3.zero, Mathf.CeilToInt(impact.Severity * 20), impact.Severity);
        }
        public void Advance(float dt)
        {
            if (!isActiveAndEnabled || failed || runtime == null && !seenFrame || dt <= 0) return;
            using (Marker.Auto())
            {
                if (!player && world != null && world.Listener != null && (frame.Position - world.Listener.position).sqrMagnitude > 120 * 120)
                { ReleasePlayback(); status = "Outside audible range"; return; }
                try
                {
                    if (!EnsureRuntime()) return;
                    runtime.SetHoodView(cameraRig != null && cameraRig.IsHoodView);
                    runtime.Update(frame, dt); status = "Playing " + runtime.ActiveLayers + " layers through one spatial voice";
                }
                catch (Exception error) { Fail(error); }
            }
        }
        public bool Play(string soundId)
        {
            if (!isActiveAndEnabled || failed) return false;
            try { return EnsureRuntime() && runtime.Play(soundId); }
            catch (Exception error) { Fail(error); return false; }
        }
        public void SetHorn(bool active) { horn = active; EnsureControlRuntime(); runtime?.SetHorn(active); }
        public void SetSiren(bool active) { siren = active; EnsureControlRuntime(); runtime?.SetSiren(active); }
        private void EnsureControlRuntime()
        {
            if (!isActiveAndEnabled || runtime != null || failed) return;
            try { EnsureRuntime(); }
            catch (Exception error) { Fail(error); }
        }

        private static bool ValidFrame(VehicleFeedbackFrame value)
        {
            if (value.WheelCount < 0 || value.WheelCount > 8 || double.IsNaN(value.Time) || double.IsInfinity(value.Time)
                || !Finite(value.Clutch) || !Finite(value.Boost) || !Finite(value.BodyDamage) || !Finite(value.WaterDepth)
                || !Finite(value.Position) || !Finite(value.Velocity) || !Finite(value.LocalVelocity)
                || !Finite(value.AccelerationG) || !Finite(value.Rotation) || !Finite(value.EngineRpm) || !Finite(value.NormalizedRpm)
                || !Finite(value.EngineLoad) || !Finite(value.EngineTorque) || !Finite(value.DrivetrainTorque) || !Finite(value.Throttle)
                || !Finite(value.Brake) || !Finite(value.NitrousFlow) || !Finite(value.NitrousRemaining) || !Finite(value.Speed)
                || !Finite(value.SlipAngle) || !Finite(value.YawRate) || !Finite(value.FrontSlip) || !Finite(value.RearSlip)
                || !Finite(value.Wheelspin) || !Finite(value.BrakeLock) || !Finite(value.Drift) || !Finite(value.SpeedIntensity)
                || !Finite(value.NitroIntensity) || !Finite(value.EngineStress) || !Finite(value.Landing) || !Finite(value.Scrape)) return false;
            for (int i = 0; i < value.WheelCount; i++)
            {
                var wheel = value.GetWheel(i);
                if (!Finite(wheel.Point) || !Finite(wheel.Normal) || !Finite(wheel.AngularSpeed) || !Finite(wheel.LinearSpeed)
                    || !Finite(wheel.RoadSpeed) || !Finite(wheel.LongitudinalSlip) || !Finite(wheel.LateralSlip)
                    || !Finite(wheel.Load) || !Finite(wheel.Compression) || !Finite(wheel.Slip) || !Finite(wheel.Spin) || !Finite(wheel.Lock)) return false;
            }
            return ValidImpact(value.LastImpact);
        }

        private static bool ValidImpact(FeedbackImpact value)
        {
            return !double.IsNaN(value.Time) && !double.IsInfinity(value.Time) && Finite(value.Point) && Finite(value.Normal)
                && Finite(value.LocalDirection) && Finite(value.Severity) && Finite(value.NormalSpeed)
                && Finite(value.TangentSpeed) && Finite(value.Impulse);
        }

        private static bool Finite(Vector3 value) => SensoryMath.IsFinite(value.x) && SensoryMath.IsFinite(value.y) && SensoryMath.IsFinite(value.z);
        private static bool Finite(Quaternion value) => SensoryMath.IsFinite(value.x) && SensoryMath.IsFinite(value.y)
            && SensoryMath.IsFinite(value.z) && SensoryMath.IsFinite(value.w);
        private static bool Finite(float value) => SensoryMath.IsFinite(value);
        private bool EnsureRuntime()
        {
            if (runtime != null) return true;
            if (failed) return false;
            if (useBankOverride && bankOverride == null) throw new InvalidOperationException("Assign the Black Box bank override.");
            if (profile != null && !profile.Validate(out string failure)) throw new InvalidOperationException(failure);
            if (profile == null && Banks == null && (sounds == null || sounds.Length == 0)) { status = "Attach an decoded vehicle audio profile or additional sounds."; return false; }
            if (world == null)
            {
                world = FindFirstObjectByType<SensoryAudioWorld>();
                if (world == null)
                {
                    var root = new GameObject("Vehicle Audio World");
                    if (gameObject.scene.IsValid()) SceneManager.MoveGameObjectToScene(root, gameObject.scene);
                    world = root.AddComponent<SensoryAudioWorld>();
                    var listener = FindFirstObjectByType<AudioListener>();
                    world.Configure(listener != null ? listener.transform : null, player ? transform : null, null);
                }
            }
            Vector3 exhaust = profile != null && profile.exhaustPorts.Length > 0 ? profile.exhaustPorts[0] : Vector3.zero;
            runtime = new VehicleAudioRuntime(Banks, world, transform, player ? SensoryCategory.Player : SensoryCategory.OtherVehicle, exhaust, mix, profile, sounds);
            runtime.SetHorn(horn); runtime.SetSiren(siren);
            return true;
        }
        private void Update() { if (!manualInput) Advance(Time.deltaTime); }
        private void OnCollisionEnter(Collision collision) => sampler?.RecordImpact(collision);
        private void OnCollisionStay(Collision collision) => sampler?.RecordScrape(collision);
        private void ReleasePlayback() { runtime?.Dispose(); runtime = null; }
        private void Fail(Exception error)
        { ReleasePlayback(); failed = true; status = error.Message; Debug.LogError("Vehicle audio stopped: " + error.Message, this); }
        private void OnDisable() { DetachSource(); ReleasePlayback(); seenFrame = false; horn = siren = false; }
        private void OnDestroy() { DetachSource(); ReleasePlayback(); }
    }
}
