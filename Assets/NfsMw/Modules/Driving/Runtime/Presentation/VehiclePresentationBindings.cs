using System;
using UnityEngine;
using System.Collections.Generic;

namespace NfsMwRemaster.Driving
{
    public enum VehicleLampMode { Off, Automatic, Low, High }
    public enum VehiclePresentationRole { Player, Opponent, Traffic, Parked, Garage }
    public enum VehicleDashboardSignal { None, LeftIndicator, RightIndicator, HighBeam, Abs, TractionControl, Nitrous }
    [Serializable]
    public sealed class VehicleLampBinding
    {
        public string id = "";
        public Light[] lights = Array.Empty<Light>();
        public Renderer[] emissive = Array.Empty<Renderer>();
        [Min(0)] public float intensity = 1f;
        [Min(0)] public float emissionIntensity = 2f;
        public VehicleDashboardSignal dashboardSignal;
        [Min(0)] public float range = 20f;
        public LightShadows shadows = LightShadows.None;
        public VehicleLampMode mode = VehicleLampMode.Automatic;
        public Color color = Color.white;
    }

    [Serializable]
    public sealed class VehicleWheelPresentationBinding
    {
        public string id = "";
        public Transform caliper;
        public Transform wheel;
        public float caliperSteerSign = 1f;
    }

    [Serializable]
    public sealed class VehicleGaugeBinding
    {
        public string id = "";
        public Transform needle;
        public Vector3 localAxis = Vector3.forward;
        public float minimumAngle = -130f;
        public float maximumAngle = 130f;
        public float neutralAngle;
        public float minimumValue;
        public float maximumValue = 1f;
    }

    /// <summary>Stable, explicit model bindings. Missing optional capabilities are safe.</summary>
    [DisallowMultipleComponent]
    public sealed class VehiclePresentationBindings : MonoBehaviour
    {
        public VehiclePresentationRole role = VehiclePresentationRole.Player;
        public VehicleLampBinding headlights = new VehicleLampBinding { id = "headlights" };
        public VehicleLampBinding highBeams = new VehicleLampBinding { id = "high-beams" };
        public VehicleLampBinding daytimeRunning = new VehicleLampBinding { id = "drl" };
        public VehicleLampBinding tailLights = new VehicleLampBinding { id = "tail" };
        public VehicleLampBinding brakeLights = new VehicleLampBinding { id = "brake" };
        public VehicleLampBinding reverseLights = new VehicleLampBinding { id = "reverse" };
        public VehicleLampBinding leftIndicator = new VehicleLampBinding { id = "indicator-left" };
        public VehicleLampBinding rightIndicator = new VehicleLampBinding { id = "indicator-right" };
        public VehicleLampBinding fogLights = new VehicleLampBinding { id = "fog" };
        public Renderer[] windscreen = Array.Empty<Renderer>();
        public Renderer[] rearWindow = Array.Empty<Renderer>();
        public Renderer[] sideWindows = Array.Empty<Renderer>();
        public Transform[] wiperPivots = Array.Empty<Transform>();
        public Transform steeringWheel;
        public Vector3 steeringAxis = Vector3.forward;
        public Quaternion steeringNeutral = Quaternion.identity;
        public Transform cockpitCameraAnchor;
        public VehicleGaugeBinding speedometer = new VehicleGaugeBinding { id = "speedometer" };
        public VehicleGaugeBinding tachometer = new VehicleGaugeBinding { id = "tachometer" };
        public Renderer[] gearDisplay = Array.Empty<Renderer>();
        public TextMesh gearText;
        public Transform[] pedals = Array.Empty<Transform>();
        public Transform gearLever;
        public Quaternion gearNeutral = Quaternion.identity;
        public Quaternion[] gearPoses = Array.Empty<Quaternion>();
        public Quaternion reverseGearPose = Quaternion.identity;
        public Transform[] driverHandTargets = Array.Empty<Transform>();
        public VehicleLampBinding[] dashboardIndicators = Array.Empty<VehicleLampBinding>();
        public Transform[] sideWindowPivots = Array.Empty<Transform>();
        public Vector3 sideWindowOpenAxis = Vector3.right;
        [Min(0)] public float sideWindowOpenDistance = .3f;
        [Range(0, 1)] public float sideWindowOpen;
        public VehicleWheelPresentationBinding[] wheels = new VehicleWheelPresentationBinding[4];
        public float steeringWheelDegrees = 450f;
        public float steeringRatio = 1f;
        [Min(0)] public float steeringSharpness = 14f;
        public float steeringInputDegrees = 450f;
        public float wiperDegrees = 65f;
        [Range(0, 1)] public float glassTint = .15f;
        public Color glassTintColor = new Color(.55f, .7f, .85f);
        public string glassTintProperty = "_TransmittanceColor";
        public string glassWetnessProperty = "_Wetness";
        public float glassWetness = .35f;
        public bool useAutomaticLights = true;
        public bool headlightsEnabled;
        public bool highBeamEnabled;
        public bool fogLightsEnabled;
        public bool hazards;
        public int indicator = 0;

        public VehicleLampBinding[] Lamps => new[] { headlights, highBeams, daytimeRunning, tailLights, brakeLights,
            reverseLights, leftIndicator, rightIndicator, fogLights };

        public void Rebind(GameObject replacementRoot)
        {
            var module = GetComponentInParent<VehiclePresentationModule>();
            if (module) module.RebindReplacement(replacementRoot);
        }

        public void ValidateBindings(List<string> issues)
        {
            if (issues == null) return;
            for (int i = 0; i < Lamps.Length; i++) if (Lamps[i] == null || string.IsNullOrEmpty(Lamps[i].id)) issues.Add("lamp[" + i + "]: assign a stable semantic ID and binding");
            if (!steeringWheel) issues.Add("cockpit.steeringWheel: assign a steering wheel or disable cockpit capability");
            if (windscreen == null || windscreen.Length == 0) issues.Add("glass.windscreen: assign renderers or disable windows capability");
            if (wiperPivots == null || wiperPivots.Length == 0) issues.Add("glass.wipers: optional rig unavailable; wipers will remain parked");
            if (wheels == null || wheels.Length != 4) issues.Add("wheels: assign four wheel presentation bindings");
        }
    }
}
