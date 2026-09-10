using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>One owner for recovered AEMS graphs, GIN mappings, attached sounds and spatial output.</summary>
    public sealed partial class VehicleAudioRuntime : IDisposable
    {
        private sealed class Voice
        {
            public VehicleAudioChannel Channel;
            public AemsProgram Program;
            public AemsEvaluator Graph;
            public AemsEvaluator.Player[] Players;
            public AemsPcmRenderer Pcm;
            public EngineAudioRenderer Gin;
            public EngineAudioRegion Region;
            public VehicleAudioBus.Layer Layer;
            public VehicleAudioSound Sound;
            public EngineSoundLayer LegacyLayer;
            public int LegacyRegion, OnLoad;
            public int WheelIndex = -1;
            public bool WheelSlip;
            public AudioClip WheelClip;
            public bool Event, WasActive;
            public float Age, Gain = 1, MaximumDuration = 30;
            public int[] Parameters;
        }
        private const int MaximumLayers = 128;
        private readonly MostWantedVehicleAudio setup;
        private readonly VehicleSensoryProfile profile;
        private readonly VehicleAudioMix mix;
        private readonly VehicleAudioSound[] sounds;
        private readonly SensoryAudioWorld world;
        private readonly Transform owner;
        private readonly SensoryCategory category;
        private readonly Vector3 position;
        private readonly List<Voice> voices = new List<Voice>(MaximumLayers);
        private readonly Dictionary<AemsAudioBank, AemsPcmRenderer.Recording[]> banks = new Dictionary<AemsAudioBank, AemsPcmRenderer.Recording[]>();
        private readonly VehicleAudioPcmCache cache = new VehicleAudioPcmCache();
        private readonly VehicleAudioBus bus = new VehicleAudioBus();
        private FeedbackVoiceLease lease;
        private VehicleFeedbackFrame previous;
        private double accumulator;
        private bool sampled, dirty, disposed, horn, siren;
        private MostWantedSurfaceAudio roadSurface;
        private AudioClip scrapeClip;
        private Voice scrapeVoice;
        private bool hoodView;
        public int ActiveLayers => voices.Count;
        public long ControlTicks { get; private set; }
        public double DroppedControlSeconds { get; private set; }
        public EngineAudioRenderer AccelerationRenderer { get; private set; }
        public IProceduralVehicleAudio Renderer => bus;
        public void SetHoodView(bool value) { hoodView = value; }

        public VehicleAudioRuntime(MostWantedVehicleAudio setup, SensoryAudioWorld world, Transform owner, SensoryCategory category,
            Vector3 exhaust, VehicleAudioMix mix = null, VehicleSensoryProfile profile = null, VehicleAudioSound[] sounds = null)
        {
            this.setup = setup; this.world = world; this.owner = owner; this.category = category; position = exhaust;
            this.mix = mix ?? new VehicleAudioMix(); this.profile = profile; this.sounds = sounds ?? Array.Empty<VehicleAudioSound>();
            try
            {
                if (profile != null && !profile.Validate(out string profileFailure)) throw new InvalidOperationException(profileFailure);
                if (setup != null)
                {
                    if (!setup.Validate(out string failure)) throw new InvalidOperationException(failure);
                    AddGin(setup.acceleration, VehicleAudioChannel.Acceleration);
                    AddGin(setup.deceleration, VehicleAudioChannel.Deceleration);
                    AddBank(setup.engine, "CAR", VehicleAudioChannel.Engine);
                    AddBank(setup.transmission, "CAR_TRANNY", VehicleAudioChannel.Transmission);
                    AddBank(setup.sweeteners, "CAR_Sputter", VehicleAudioChannel.Sputter);
                    AddBank(setup.whine, "CAR_WHINE", VehicleAudioChannel.Whine);
                    AddBank(setup.skids, "FX_SKID", VehicleAudioChannel.Tires);
                    AddBank(setup.road, "FX_ROADNOISE", VehicleAudioChannel.Road);
                    AddBank(setup.wind, "FX_WIND", VehicleAudioChannel.Wind);
                    AddBank(setup.nitrous, "FX_NITROUS", VehicleAudioChannel.Nitrous);
                    roadSurface = Array.Find(setup.surfaces, s => s.surface == SensorySurface.Unknown);
                }
                else BuildAuthoredEngine();
                ValidateSounds();
                foreach (var sound in this.sounds) if (IsContinuous(sound.trigger)) AddSound(sound, false);
                Publish();
            }
            catch { Dispose(); throw; }
        }
        private Voice Add(Voice voice, IProceduralVehicleAudio renderer)
        {
            if (voices.Count >= MaximumLayers) return null;
            voice.Layer = new VehicleAudioBus.Layer(renderer); voices.Add(voice); dirty = true; return voice;
        }
        private Voice AddGin(EngineAudioRegion region, VehicleAudioChannel channel)
        {
            var renderer = new EngineAudioRenderer(); renderer.SetTelemetry(cache.Gin(region));
            if (AccelerationRenderer == null) AccelerationRenderer = renderer;
            return Add(new Voice { Gin = renderer, Region = region, Channel = channel }, renderer);
        }
        private Voice AddBank(AemsAudioBank bank, string name, VehicleAudioChannel channel, bool oneShot = false)
        {
            if (bank == null || voices.Count >= MaximumLayers) return null;
            var program = Array.Find(bank.programs ?? Array.Empty<AemsProgram>(), p => p != null && p.interfaceName == name);
            if (program == null) throw new InvalidOperationException("Missing recovered interface: " + bank.name + "/" + name);
            if (!banks.TryGetValue(bank, out var recordings))
            {
                if (bank.recordings == null) throw new InvalidOperationException("Bank has no decoded recordings: " + bank.name);
                recordings = new AemsPcmRenderer.Recording[bank.recordings.Length];
                var ids = new HashSet<int>();
                for (int i = 0; i < recordings.Length; i++)
                {
                    var rec = bank.recordings[i];
                    if (rec == null || rec.sampleId < 1 || !ids.Add(rec.sampleId)) throw new InvalidOperationException("Invalid or duplicate sample ID in " + bank.name);
                    recordings[i] = cache.Recording(rec.clip, rec.sampleId, rec.loopStart, rec.loopEnd);
                }
                banks.Add(bank, recordings);
            }
            var graph = new AemsEvaluator(program); var pcm = new AemsPcmRenderer(recordings, graph.Players.Length);
            return Add(new Voice { Program = program, Graph = graph, Players = graph.Players, Pcm = pcm,
                Parameters = new int[program.parameterCount], Channel = channel, Event = oneShot }, pcm);
        }
        private Voice AddClip(AudioClip clip, VehicleAudioChannel channel, bool loop, float gain = 1, int start = 0, int end = 0)
        {
            if (clip == null || voices.Count >= MaximumLayers) return null;
            if (loop && end == 0) end = clip.samples;
            var recording = cache.Recording(clip, 1, loop ? start : 0, loop ? end : 0);
            var pcm = new AemsPcmRenderer(new[] { recording }, 1);
            var players = new[] { new AemsEvaluator.Player { Active = true, Control = 1, Generation = 1, Sample = 1 } };
            pcm.Publish(players);
            return Add(new Voice { Pcm = pcm, Players = players, Channel = channel, Event = !loop, Gain = gain }, pcm);
        }
        private void BuildAuthoredEngine()
        {
            if (profile == null) return;
            foreach (var layer in profile.engineLayers)
            {
                if (layer.nativeRegions != null && layer.nativeRegions.Length > 0)
                {
                    foreach (var region in layer.nativeRegions) { var voice = AddGin(region, VehicleAudioChannel.Engine); if (voice != null) voice.LegacyLayer = layer; }
                    continue;
                }
                for (int r = 0; r < layer.regions.Length; r++) for (int on = 0; on < 2; on++)
                {
                    var voice = AddClip(on == 0 ? layer.regions[r].onLoad : layer.regions[r].offLoad, VehicleAudioChannel.Engine, true);
                    if (voice != null) { voice.LegacyLayer = layer; voice.LegacyRegion = r; voice.OnLoad = on; }
                }
            }
            AddClip(profile.nitrous, VehicleAudioChannel.Nitrous, true);
            AddClip(profile.wind, VehicleAudioChannel.Wind, true);
            AddClip(profile.trafficRoadNoise, VehicleAudioChannel.Road, true);
        }
        private void ValidateSounds()
        {
            if (sounds.Length > 32) throw new InvalidOperationException("Attach at most 32 additional vehicle sounds.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sound in sounds)
            {
                if (sound == null || string.IsNullOrWhiteSpace(sound.id) || !ids.Add(sound.id)) throw new InvalidOperationException("Attached sounds need unique, nonempty IDs.");
                if ((sound.clip == null) == (sound.bank == null)) throw new InvalidOperationException(sound.id + ": assign either a decoded bank or one PCM clip.");
                if (sound.bank != null)
                {
                    var p = Array.Find(sound.bank.programs ?? Array.Empty<AemsProgram>(), value => value != null && value.interfaceName == sound.interfaceName);
                    if (p == null || sound.parameters == null || sound.parameters.Length != p.parameterCount) throw new InvalidOperationException(sound.id + ": bank interface parameters must match the decoded program.");
                    if (sound.bindings != null) foreach (var binding in sound.bindings)
                        if (binding == null || binding.parameter < 0 || binding.parameter >= p.parameterCount || !SensoryMath.IsFinite(binding.multiplier) || !SensoryMath.IsFinite(binding.offset))
                            throw new InvalidOperationException(sound.id + ": invalid telemetry parameter binding.");
                }
            }
        }
        private Voice AddSound(VehicleAudioSound sound, bool oneShot)
        {
            var voice = sound.bank != null ? AddBank(sound.bank, sound.interfaceName, sound.channel, oneShot)
                : AddClip(sound.clip, sound.channel, !oneShot && sound.loop, sound.gain, sound.loopStart, sound.loopEnd);
            if (voice == null) return null;
            voice.Event = oneShot; voice.Sound = sound; voice.Gain = sound.gain;
            voice.MaximumDuration = VehicleAudioMix.Safe(sound.maximumDuration, 30, .1f, 600);
            if (voice.Graph != null) Set(voice, sound.parameters);
            return voice;
        }
        private static bool IsContinuous(VehicleAudioTrigger trigger) => trigger >= VehicleAudioTrigger.Horn;
        public bool Play(string id)
        {
            if (disposed) return false;
            var sound = Array.Find(sounds, value => value.id == id);
            return sound != null && AddSound(sound, true) != null;
        }
        public void SetHorn(bool active) { horn = active; }
        public void SetSiren(bool active) { siren = active; }
        private void Trigger(VehicleAudioTrigger trigger)
        { foreach (var sound in sounds) if (sound.trigger == trigger) AddSound(sound, true); }
        private static void Set(Voice voice, ReadOnlySpan<int> values)
        {
            if (voice == null) return;
            if (values.Length != voice.Parameters.Length) throw new InvalidOperationException("Recovered interface parameter count changed.");
            values.CopyTo(voice.Parameters);
            for (int i = 0; i < values.Length; i++) voice.Graph.SetParameter(i, values[i]);
        }
        private void RemoveAt(int index) { voices.RemoveAt(index); dirty = true; }
        private void Publish()
        {
            if (!dirty) return;
            var layers = new VehicleAudioBus.Layer[voices.Count];
            for (int i = 0; i < layers.Length; i++) layers[i] = voices[i].Layer;
            bus.Publish(layers); dirty = false;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (world != null) world.Release(lease); lease = default;
            bus.Publish(Array.Empty<VehicleAudioBus.Layer>()); voices.Clear(); cache.Dispose(); banks.Clear();
        }
    }
}
