using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>One presentation pass. Geometry bindings never own simulation or installed state.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(VehiclePresentationBindings))]
    public sealed class VehiclePresentationModule : MonoBehaviour, IVehicleModule, IVehiclePresentationModule
    {
        [SerializeField] private VehiclePresentationBindings bindings;
        [SerializeField] private bool enableCockpit = true;
        [SerializeField] private bool enableWipers = true;
        [SerializeField] private VehicleCameraRig cameraRig;
        private readonly List<Rig> rigs = new List<Rig>();
        private readonly Dictionary<Renderer, Emission> emissions = new Dictionary<Renderer, Emission>();
        private readonly Dictionary<Light, Illumination> lights = new Dictionary<Light, Illumination>();
        private readonly VehicleLampBinding[] lamps = new VehicleLampBinding[9];
        private MaterialPropertyBlock properties;
        private VehicleWheel[] vehicleWheels;
        private VehicleCapabilities capabilities = VehicleCapabilities.All;
        private Camera observer;
        private float steering, elapsed;
        public string ModuleId => "presentation";
        public int ExecutionOrder => 100;
        public VehiclePresentationBindings Bindings => bindings;

        private sealed class Emission { public Color color; }
        private sealed class Illumination { public float intensity; public VehicleLampBinding source; }
        private sealed class Rig
        {
            public readonly VehiclePresentationBindings data;
            public readonly Dictionary<Transform, Quaternion> rotations = new Dictionary<Transform, Quaternion>();
            public readonly Dictionary<Transform, Vector3> positions = new Dictionary<Transform, Vector3>();
            public readonly Vector3[] hands;
            public readonly Quaternion[] handRotations;
            public readonly int tintProperty, wetProperty;
            public Rig(VehiclePresentationBindings value)
            {
                data = value; tintProperty = Shader.PropertyToID(value.glassTintProperty ?? "_TransmittanceColor"); wetProperty = Shader.PropertyToID(value.glassWetnessProperty ?? "_Wetness");
                Capture(value.speedometer?.needle); Capture(value.tachometer?.needle);
                Capture(value.wiperPivots); Capture(value.pedals); Capture(value.sideWindowPivots);
                if (value.wheels != null) foreach (var wheel in value.wheels) if (wheel != null && wheel.caliper) { Capture(wheel.caliper); if (wheel.wheel) rotations[wheel.caliper] = Quaternion.Inverse(wheel.wheel.rotation) * wheel.caliper.rotation; }
                int count = value.driverHandTargets?.Length ?? 0; hands = new Vector3[count]; handRotations = new Quaternion[count];
                for (int i = 0; i < count; i++) if (value.driverHandTargets[i] && value.steeringWheel) { hands[i] = value.steeringWheel.InverseTransformPoint(value.driverHandTargets[i].position); handRotations[i] = Quaternion.Inverse(value.steeringWheel.rotation) * value.driverHandTargets[i].rotation; }
            }
            private void Capture(Transform[] values) { if (values != null) foreach (Transform value in values) Capture(value); }
            private void Capture(Transform value) { if (value && !rotations.ContainsKey(value)) { rotations.Add(value, value.localRotation); positions.Add(value, value.localPosition); } }
            public Quaternion Rotation(Transform value) => value && rotations.TryGetValue(value, out var result) ? result : Quaternion.identity;
            public void Park() { Restore(data.wiperPivots, true); Restore(data.sideWindowPivots, false); }
            private void Restore(Transform[] values, bool rotation) { if (values == null) return; foreach (var value in values) if (value) { if (rotation) value.localRotation = Rotation(value); else if (positions.TryGetValue(value, out var p)) value.localPosition = p; } }
        }

        public void Initialize(VehicleModuleContext context)
        {
            if (!bindings) bindings = GetComponent<VehiclePresentationBindings>();
            if (properties == null) properties = new MaterialPropertyBlock();
            vehicleWheels = context.Wheels;
            var configuration = GetComponent<VehicleConfiguration>();
            capabilities = configuration && configuration.Definition ? configuration.Definition.capabilities : VehicleCapabilities.All;
            if (rigs.Count == 0 && bindings) { rigs.Add(new Rig(bindings)); RebuildLamps(); }
            if (!cameraRig && context.Vehicle != null) cameraRig = context.Vehicle.CameraRig;
            observer = cameraRig ? cameraRig.GetComponent<Camera>() : Camera.main;
            BindCamera();
        }

        /// <summary>Use after an atomic visual install/rollback. A supplied semantic lamp replaces that complete lamp assembly.</summary>
        public void SetReplacementBindings(IReadOnlyList<VehiclePresentationBindings> replacements)
        {
            if (!bindings) bindings = GetComponent<VehiclePresentationBindings>();
            if (rigs.Count == 0 && bindings) rigs.Add(new Rig(bindings));
            DisableOutputs();
            for (int i = rigs.Count - 1; i > 0; i--) { rigs[i].Park(); rigs.RemoveAt(i); }
            if (replacements != null) for (int i = 0; i < replacements.Count; i++) if (replacements[i] && replacements[i] != bindings) rigs.Add(new Rig(replacements[i]));
            RebuildLamps(); BindCamera();
        }
        public void RebindReplacement(GameObject root) => SetReplacementBindings(root ? root.GetComponentsInChildren<VehiclePresentationBindings>(true) : Array.Empty<VehiclePresentationBindings>());
        private void BindCamera()
        {
            if (!cameraRig || !Supports(VehicleCapabilities.Cockpit)) return;
            Transform anchor = bindings ? bindings.cockpitCameraAnchor : null;
            foreach (var rig in rigs) if (rig.data && rig.data.cockpitCameraAnchor) anchor = rig.data.cockpitCameraAnchor;
            cameraRig.SetCockpitAnchor(anchor);
        }
        private void RebuildLamps()
        {
            emissions.Clear(); lights.Clear(); Array.Clear(lamps, 0, lamps.Length);
            foreach (var rig in rigs)
            {
                var candidates = rig.data.Lamps;
                for (int i = 0; i < lamps.Length; i++) if (lamps[i] == null || HasOutput(candidates[i])) lamps[i] = candidates[i];
                if (rig.data.dashboardIndicators != null) foreach (var lamp in rig.data.dashboardIndicators) CacheLamp(lamp);
            }
            foreach (var lamp in lamps) CacheLamp(lamp);
        }
        private static bool HasOutput(VehicleLampBinding lamp) => lamp != null && ((lamp.lights?.Length ?? 0) > 0 || (lamp.emissive?.Length ?? 0) > 0);
        private void CacheLamp(VehicleLampBinding lamp)
        {
            if (lamp == null) return;
            if (lamp.emissive != null) foreach (var renderer in lamp.emissive) if (renderer && !emissions.ContainsKey(renderer)) emissions.Add(renderer, new Emission());
            if (lamp.lights != null) foreach (var light in lamp.lights) if (light && !lights.ContainsKey(light)) lights.Add(light, new Illumination());
        }
        private bool Supports(VehicleCapabilities capability) => (capabilities & capability) == capability;
        private bool Detailed => bindings.role == VehiclePresentationRole.Player || bindings.role == VehiclePresentationRole.Garage;
        public void Present(VehicleModuleContext context, float deltaTime)
        {
            if (!bindings || rigs.Count == 0) return;
            elapsed += Mathf.Max(0, deltaTime);
            var telemetry = context.Telemetry;
            var weather = DynamicWeatherWorld.Active;
            float wetness = weather ? weather.Snapshot.surfaceWetness : 0;
            bool night = weather && weather.Snapshot.daylight < .28f;
            var mode = bindings.headlights?.mode ?? VehicleLampMode.Automatic;
            bool high = mode == VehicleLampMode.High || bindings.highBeamEnabled;
            bool lit = mode != VehicleLampMode.Off && (mode == VehicleLampMode.Low || high || bindings.headlightsEnabled || bindings.useAutomaticLights && night);
            bool running = context.Vehicle ? context.Vehicle.EngineRunning : telemetry.EngineRpm > 0;
            bool flash = Mathf.Repeat(elapsed, .8f) < .4f;
            bool left = flash && (bindings.hazards || bindings.indicator < 0), right = flash && (bindings.hazards || bindings.indicator > 0);
            foreach (var pair in emissions) pair.Value.color = Color.black;
            foreach (var pair in lights) { pair.Value.intensity = 0; pair.Value.source = null; }
            if (Supports(VehicleCapabilities.Lighting))
            {
                Accumulate(lamps[0], lit && !high); Accumulate(lamps[1], lit && high); Accumulate(lamps[2], running && !lit);
                Accumulate(lamps[3], lit); Accumulate(lamps[4], telemetry.BrakeLightsRequired || telemetry.Brake > .02f);
                Accumulate(lamps[5], telemetry.Gear < 0); Accumulate(lamps[6], left); Accumulate(lamps[7], right); Accumulate(lamps[8], lit && bindings.fogLightsEnabled);
            }
            float roadAngle = 0; int steeringWheels = 0;
            if (vehicleWheels != null) foreach (var wheel in vehicleWheels) if (wheel && wheel.IsSteeringWheel) { roadAngle += wheel.SteerAngle; steeringWheels++; }
            roadAngle /= Mathf.Max(1, steeringWheels);
            steering = Mathf.Lerp(steering, roadAngle, 1f - Mathf.Exp(-deltaTime * Mathf.Max(0, bindings.steeringSharpness)));
            foreach (var rig in rigs)
            {
                if (!rig.data) continue;
                if (Supports(VehicleCapabilities.Glass)) ApplyGlass(rig, wetness);
                if (Supports(VehicleCapabilities.Windows)) ApplyWindows(rig);
                ApplyWipers(rig, Supports(VehicleCapabilities.Wipers) && enableWipers && weather && (Detailed || bindings.role == VehiclePresentationRole.Opponent) ? weather.Snapshot.precipitationIntensity : 0);
                if (Supports(VehicleCapabilities.Cockpit) && enableCockpit && Detailed) ApplyCockpit(rig, telemetry, high && lit, left, right);
                ApplyCalipers(rig);
            }
            FlushOutputs();
        }
        private void Accumulate(VehicleLampBinding lamp, bool active)
        {
            if (!active || lamp == null || lamp.mode == VehicleLampMode.Off) return;
            if (lamp.lights != null) foreach (var light in lamp.lights) if (light && lights.TryGetValue(light, out var value) && lamp.intensity >= value.intensity) { value.source = lamp; value.intensity = lamp.intensity; }
            if (lamp.emissive != null) foreach (var renderer in lamp.emissive) if (renderer && emissions.TryGetValue(renderer, out var value)) { Color color = lamp.color * lamp.emissionIntensity; value.color = new Color(Mathf.Max(value.color.r, color.r), Mathf.Max(value.color.g, color.g), Mathf.Max(value.color.b, color.b), 1); }
        }
        private void FlushOutputs()
        {
            bool illuminate = Detailed || bindings.role == VehiclePresentationRole.Opponent && observer && (observer.transform.position - transform.position).sqrMagnitude < 1600;
            foreach (var pair in lights) if (pair.Key)
            {
                var lamp = pair.Value.source; pair.Key.enabled = illuminate && lamp != null && pair.Value.intensity > 0;
                if (lamp == null) continue;
                pair.Key.intensity = pair.Value.intensity; pair.Key.range = lamp.range; pair.Key.color = lamp.color; pair.Key.shadows = Detailed ? lamp.shadows : LightShadows.None;
            }
            foreach (var pair in emissions) if (pair.Key) { pair.Key.GetPropertyBlock(properties); properties.SetColor("_EmissiveColor", pair.Value.color); pair.Key.SetPropertyBlock(properties); }
        }
        private void ApplyCockpit(Rig rig, VehicleTelemetry t, bool high, bool left, bool right)
        {
            var b = rig.data;
            if (b.steeringWheel) b.steeringWheel.localRotation = b.steeringNeutral * Quaternion.AngleAxis(-Mathf.Clamp(steering * b.steeringRatio, -b.steeringWheelDegrees, b.steeringWheelDegrees), b.steeringAxis.normalized);
            ApplyGauge(b.speedometer, t.SpeedKph, rig); ApplyGauge(b.tachometer, t.EngineRpm, rig);
            if (b.gearText && b.gearText.text != t.GearLabel) b.gearText.text = t.GearLabel;
            if (b.gearDisplay != null) foreach (var renderer in b.gearDisplay) if (renderer && renderer.sharedMaterial && renderer.sharedMaterial.HasProperty("_Gear")) { renderer.GetPropertyBlock(properties); properties.SetFloat("_Gear", t.Gear); renderer.SetPropertyBlock(properties); }
            if (b.gearLever) { if (t.Gear < 0) b.gearLever.localRotation = b.gearNeutral * b.reverseGearPose; else if (b.gearPoses != null && t.Gear < b.gearPoses.Length) b.gearLever.localRotation = b.gearNeutral * b.gearPoses[t.Gear]; }
            if (b.pedals != null) for (int i = 0; i < b.pedals.Length; i++) if (b.pedals[i]) b.pedals[i].localRotation = rig.Rotation(b.pedals[i]) * Quaternion.Euler(i == 0 ? t.Brake * 25f : i == 1 ? t.Throttle * -20f : 0, 0, 0);
            if (b.steeringWheel) for (int i = 0; i < rig.hands.Length; i++) if (b.driverHandTargets[i]) b.driverHandTargets[i].SetPositionAndRotation(b.steeringWheel.TransformPoint(rig.hands[i]), b.steeringWheel.rotation * rig.handRotations[i]);
            if (b.dashboardIndicators != null) for (int i = 0; i < b.dashboardIndicators.Length; i++)
            {
                var lamp = b.dashboardIndicators[i]; if (lamp == null) continue;
                var signal = lamp.dashboardSignal == VehicleDashboardSignal.None ? (i == 0 ? VehicleDashboardSignal.LeftIndicator : i == 1 ? VehicleDashboardSignal.RightIndicator : VehicleDashboardSignal.None) : lamp.dashboardSignal;
                bool active = signal == VehicleDashboardSignal.LeftIndicator ? left : signal == VehicleDashboardSignal.RightIndicator ? right : signal == VehicleDashboardSignal.HighBeam ? high : signal == VehicleDashboardSignal.Abs ? t.AbsReduction > .01f : signal == VehicleDashboardSignal.TractionControl ? t.TractionControlReduction > .01f : signal == VehicleDashboardSignal.Nitrous && t.NitrousActive;
                Accumulate(lamp, active);
            }
        }
        private static void ApplyGauge(VehicleGaugeBinding gauge, float value, Rig rig)
        { if (gauge != null && gauge.needle) gauge.needle.localRotation = rig.Rotation(gauge.needle) * Quaternion.AngleAxis(gauge.neutralAngle + Mathf.Lerp(gauge.minimumAngle, gauge.maximumAngle, Mathf.InverseLerp(gauge.minimumValue, Mathf.Max(gauge.minimumValue + .001f, gauge.maximumValue), value)), gauge.localAxis.normalized); }
        private static void ApplyWindows(Rig rig)
        { var b = rig.data; if (b.sideWindowPivots != null) foreach (var pivot in b.sideWindowPivots) if (pivot && rig.positions.TryGetValue(pivot, out var position)) pivot.localPosition = position + b.sideWindowOpenAxis.normalized * b.sideWindowOpen * b.sideWindowOpenDistance; }
        private void ApplyWipers(Rig rig, float wetness)
        { var b = rig.data; float sweep = wetness > .08f ? (.5f - .5f * Mathf.Cos(elapsed * Mathf.Lerp(3, 9, wetness))) : 0; if (b.wiperPivots != null) foreach (var pivot in b.wiperPivots) if (pivot) pivot.localRotation = rig.Rotation(pivot) * Quaternion.AngleAxis(b.wiperDegrees * sweep, Vector3.forward); }
        private void ApplyGlass(Rig rig, float wetness)
        { ApplyGlassSet(rig.data.windscreen, rig, wetness); ApplyGlassSet(rig.data.rearWindow, rig, wetness); ApplyGlassSet(rig.data.sideWindows, rig, wetness); }
        private void ApplyGlassSet(Renderer[] values, Rig rig, float wetness)
        {
            if (values == null) return;
            foreach (var renderer in values) if (renderer && renderer.sharedMaterial)
            {
                var material = renderer.sharedMaterial; bool tint = material.HasProperty(rig.tintProperty), wet = material.HasProperty(rig.wetProperty);
                if (!tint && !wet) continue;
                renderer.GetPropertyBlock(properties);
                if (tint) properties.SetColor(rig.tintProperty, Color.Lerp(Color.white, rig.data.glassTintColor, rig.data.glassTint));
                if (wet) properties.SetFloat(rig.wetProperty, wetness * rig.data.glassWetness);
                renderer.SetPropertyBlock(properties);
            }
        }
        private void ApplyCalipers(Rig rig)
        {
            if (vehicleWheels == null || rig.data.wheels == null) return;
            for (int i = 0; i < vehicleWheels.Length && i < rig.data.wheels.Length; i++)
            {
                var wheel = vehicleWheels[i]; var binding = rig.data.wheels[i]; if (!wheel || binding == null || !binding.caliper) continue;
                binding.caliper.SetPositionAndRotation(wheel.transform.position - wheel.transform.up * wheel.SuspensionLength, wheel.transform.rotation * Quaternion.AngleAxis(wheel.SteerAngle * binding.caliperSteerSign, Vector3.up) * rig.Rotation(binding.caliper));
            }
        }
        private void DisableOutputs()
        {
            foreach (var pair in lights) if (pair.Key) pair.Key.enabled = false;
            if (properties == null) return;
            foreach (var pair in emissions) if (pair.Key) { pair.Key.GetPropertyBlock(properties); properties.SetColor("_EmissiveColor", Color.black); pair.Key.SetPropertyBlock(properties); }
        }
        public void ResetPresentation() { steering = 0; elapsed = 0; foreach (var rig in rigs) if (rig.data) rig.Park(); DisableOutputs(); }
        private void OnDisable() => ResetPresentation();
    }
}
