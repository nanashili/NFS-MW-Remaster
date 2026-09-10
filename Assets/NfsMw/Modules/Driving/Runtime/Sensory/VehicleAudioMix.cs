using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum VehicleAudioChannel
    {
        Acceleration, Deceleration, Engine, Transmission, Sputter, Whine, Shifts, Sweeteners,
        Tires, Road, SurfaceTransitions, Wind, Nitrous, Startup, Limiter, Overrun, Impacts, Scrape, Horn, Siren, Custom
    }
    public enum VehicleAudioTrigger
    {
        Manual, Startup, Upshift, Downshift, Limiter, Overrun, NitroOn, NitroOff, Impact,
        Horn, Siren, Constant, Speed, EngineLoad, EngineRpm, WheelSlip, Scrape
    }
    public enum VehicleAudioSignal { Constant, Rpm, NormalizedRpm, Load, Speed, Throttle, Brake, Gear, Shifting, Slip, Nitrous, Scrape, EngineRunning, Horn, Siren }

    [Serializable]
    public sealed class VehicleAudioChannelMix
    {
        public VehicleAudioChannel channel;
        public bool mute;
        [Range(0, 2)] public float gain = 1;
        [Range(.5f, 2)] public float pitch = 1;
        [Range(.5f, 2)] public float tempo = 1;
        [Range(-1, 1)] public float pan;
        [Range(20, 22000)] public float lowPassHz = 22000;
        [Range(0, 2000)] public float highPassHz;
    }

    /// <summary>Author controls are separate from the recovered bank and its original parameter domain.</summary>
    [Serializable]
    public sealed class VehicleAudioMix
    {
        public bool mute;
        [Range(0, 2)] public float gain = 1;
        [Range(.5f, 2)] public float pitch = 1;
        [Range(.5f, 2)] public float tempo = 1;
        public VehicleAudioChannelMix[] channels = Defaults();
        public VehicleAudioChannelMix Find(VehicleAudioChannel channel)
        {
            if (channels != null) foreach (var item in channels) if (item != null && item.channel == channel) return item;
            return null;
        }
        public static VehicleAudioChannelMix[] Defaults()
        {
            var values = (VehicleAudioChannel[])Enum.GetValues(typeof(VehicleAudioChannel));
            var result = new VehicleAudioChannelMix[values.Length];
            for (int i = 0; i < result.Length; i++) result[i] = new VehicleAudioChannelMix { channel = values[i] };
            return result;
        }
        internal static float Safe(float value, float fallback, float low, float high)
            => SensoryMath.IsFinite(value) ? Mathf.Clamp(value, low, high) : fallback;
    }

    [Serializable]
    public sealed class VehicleAudioParameterBinding
    {
        [Min(0)] public int parameter;
        public VehicleAudioSignal signal;
        public float multiplier = 1;
        public float offset;
    }

    /// <summary>Attach an additional decoded recording or recovered interface without another car component.</summary>
    [Serializable]
    public sealed class VehicleAudioSound
    {
        public string id = "sound";
        public VehicleAudioChannel channel = VehicleAudioChannel.Custom;
        public VehicleAudioTrigger trigger;
        public AudioClip clip;
        public bool loop;
        [Min(0)] public int loopStart, loopEnd;
        public AemsAudioBank bank;
        public string interfaceName = "";
        public int[] parameters = Array.Empty<int>();
        public VehicleAudioParameterBinding[] bindings = Array.Empty<VehicleAudioParameterBinding>();
        [Range(0, 2)] public float gain = 1;
        [Range(.5f, 2)] public float pitch = 1;
        [Range(.5f, 2)] public float tempo = 1;
        [Min(.1f)] public float maximumDuration = 30;
    }
}
