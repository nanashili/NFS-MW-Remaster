using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public sealed partial class VehicleAudioRuntime
    {
        public void Update(VehicleFeedbackFrame frame, float dt)
        {
            if (disposed || !SensoryMath.IsFinite(dt) || dt <= 0) return;
            if (dt > .25f) DroppedControlSeconds += dt - .25f;
            accumulator += Math.Min(dt, .25f);
            while (accumulator + 1e-8 >= .025)
            {
                accumulator -= .025; ControlTicks++;
                UpdateScrape(frame);
                UpdateWheelAudio(frame);
                for (int i = voices.Count - 1; i >= 0; i--)
                {
                    var voice = voices[i]; voice.Age += .025f;
                    var channel = mix.Find(voice.Channel);
                    float pitch = VehicleAudioMix.Safe(mix.pitch, 1, .5f, 2) * VehicleAudioMix.Safe(channel?.pitch ?? 1, 1, .5f, 2);
                    float tempo = VehicleAudioMix.Safe(mix.tempo, 1, .5f, 2) * VehicleAudioMix.Safe(channel?.tempo ?? 1, 1, .5f, 2);
                    if (voice.Sound != null) { pitch *= VehicleAudioMix.Safe(voice.Sound.pitch, 1, .5f, 2); tempo *= VehicleAudioMix.Safe(voice.Sound.tempo, 1, .5f, 2); }
                    if (voice.WheelIndex >= 0 && voice.WheelIndex < frame.WheelCount)
                    {
                        var wheel = frame.GetWheel(voice.WheelIndex);
                        pitch *= voice.WheelSlip ? Mathf.Lerp(.85f, 1.15f, Mathf.Clamp01(wheel.Slip))
                            : Mathf.Lerp(.7f, 1.4f, Mathf.Clamp01(frame.SpeedIntensity));
                    }
                    pitch = Mathf.Clamp(pitch, .25f, 4); tempo = Mathf.Clamp(tempo, .25f, 4);
                    float gain = VoiceGain(voice, frame);
                    if (voice.Graph != null)
                    {
                        if (!voice.Event && voice.Sound == null) Parameters(voice, frame);
                        bool active = gain > .0001f;
                        if (voice.Sound != null && !voice.Event)
                        {
                            if (!active)
                            {
                                voice.WasActive = false;
                                voice.Pcm.SetPlayback(pitch, tempo);
                                voice.Pcm.Publish(voice.Players);
                            }
                            else
                            {
                                if (!voice.WasActive) RestartContinuous(voice);
                                voice.WasActive = true;
                                Bind(voice, frame);
                                voice.Pcm.SetPlayback(pitch, tempo);
                                voice.Pcm.Publish(voice.Players); voice.Graph.Step(25 * tempo); voice.Pcm.Publish(voice.Players);
                            }
                        }
                        else
                        {
                            if (voice.Sound != null) Bind(voice, frame);
                            voice.Pcm.SetPlayback(pitch, tempo);
                            voice.Pcm.Publish(voice.Players); voice.Graph.Step(25 * tempo); voice.Pcm.Publish(voice.Players);
                        }
                    }
                    else if (voice.Pcm != null)
                    {
                        bool active = gain > .0001f;
                        if (!voice.Event && voice.Sound != null && active && !voice.WasActive)
                        { voice.Players[0].Generation++; voice.Players[0].Completed = false; }
                        voice.WasActive = active; voice.Pcm.SetPlayback(pitch, tempo); voice.Pcm.Publish(voice.Players);
                    }
                    else if (voice.Gin != null)
                    {
                        float low = float.MaxValue, high = 0;
                        foreach (var anchor in voice.Region.rpmAnchors) { low = Mathf.Min(low, anchor.rpm); high = Mathf.Max(high, anchor.rpm); }
                        voice.Gin.SetFrame(Mathf.Clamp(frame.EngineRpm, low, high), Mathf.Clamp01(frame.EngineLoad), frame.EngineRunning, frame.Sequence, frame.Time, 0, frame.Gear);
                        voice.Gin.SetPlayback(pitch, tempo);
                    }
                    if (voice.Event && (voice.Age >= voice.MaximumDuration / tempo || voice.Graph != null && voice.Graph.Finished
                        || voice.Age > .1f && Done(voice))) { RemoveAt(i); continue; }
                    gain *= VehicleAudioMix.Safe(mix.gain, 1, 0, 2) * VehicleAudioMix.Safe(channel?.gain ?? 1, 1, 0, 2);
                    if (mix.mute || channel != null && channel.mute) gain = 0;
                    voice.Layer.Set(gain, VehicleAudioMix.Safe(channel?.pan ?? 0, 0, -1, 1), VehicleAudioMix.Safe(channel?.lowPassHz ?? 22000, 22000, 20, 22000), VehicleAudioMix.Safe(channel?.highPassHz ?? 0, 0, 0, 2000));
                }
            }
            Publish();
            if (world != null && bus.IsReady)
            {
                if (!world.Owns(lease)) lease = world.PlayNative(bus, category, owner, position, 1, category == SensoryCategory.Player ? 16 : 140);
                world.UpdateVoice(lease, 1, 1, position);
            }
            else if (world != null && lease.IsValid) { world.Release(lease); lease = default; }
        }
        private static bool Done(Voice voice)
        {
            if (voice.Players == null) return false;
            foreach (var p in voice.Players) if (p.Active && !p.Completed) return false;
            return true;
        }
        private float VoiceGain(Voice voice, VehicleFeedbackFrame f)
        {
            float gain = VehicleAudioMix.Safe(voice.Gain, 1, 0, 2);
            if (voice.Sound != null) return gain * (voice.Event ? 1 : TriggerGain(voice.Sound.trigger, f));
            if (voice.WheelIndex >= 0 && voice.WheelIndex < f.WheelCount)
            {
                var wheel = f.GetWheel(voice.WheelIndex);
                if (!wheel.Grounded) return 0;
                return voice.WheelSlip ? gain * Mathf.Clamp01(wheel.Slip) * .35f
                    : gain * (profile != null ? profile.roadGain.Evaluate(f.Speed) : 1) * .25f;
            }
            if (voice.LegacyLayer != null)
            {
                gain *= voice.LegacyLayer.gain * profile.highSpeedEngineGain.Evaluate(f.SpeedIntensity);
                gain *= voice.LegacyLayer.hoodGain.Evaluate(hoodView ? 1 : 0);
                if (voice.LegacyLayer.kind == EngineLayerKind.Induction) gain *= SensoryMath.Unit(f.Boost);
                if (voice.Gin == null)
                {
                    var region = voice.LegacyLayer.regions[voice.LegacyRegion];
                    voice.Players[0].Pitch = Mathf.Clamp(f.EngineRpm / region.rpm, voice.LegacyLayer.minimumPitch, voice.LegacyLayer.maximumPitch);
                    gain *= EngineBlend.Weight(voice.LegacyLayer.regions, voice.LegacyRegion, f.EngineRpm, f.EngineLoad, voice.OnLoad == 0);
                }
                return f.EngineRunning ? gain : 0;
            }
            if (setup != null)
            {
                gain *= setup.masterGain;
                if (voice.Channel == VehicleAudioChannel.Acceleration || voice.Channel == VehicleAudioChannel.Deceleration)
                {
                    float low = float.MaxValue; foreach (var anchor in voice.Region.rpmAnchors) low = Mathf.Min(low, anchor.rpm);
                    gain *= Mathf.InverseLerp(setup.idleRpm, Mathf.Max(setup.idleRpm + 1, low), f.EngineRpm);
                    gain *= Mathf.Sqrt(voice.Channel == VehicleAudioChannel.Acceleration ? Mathf.Clamp01(f.EngineLoad) : 1 - Mathf.Clamp01(f.EngineLoad));
                }
                if (voice.Channel <= VehicleAudioChannel.Sweeteners) return f.EngineRunning ? gain : 0;
                return voice.Channel == VehicleAudioChannel.Scrape ? gain * f.Scrape : gain;
            }
            switch (voice.Channel)
            {
                case VehicleAudioChannel.Nitrous: return gain * f.NitroIntensity;
                case VehicleAudioChannel.Wind: return gain * (profile != null ? profile.windGain.Evaluate(f.Speed) : 1);
                case VehicleAudioChannel.Road: return gain * (profile != null ? profile.roadGain.Evaluate(f.Speed) : 1);
                case VehicleAudioChannel.Scrape: return gain * f.Scrape;
                default: return gain;
            }
        }
        private float TriggerGain(VehicleAudioTrigger trigger, VehicleFeedbackFrame f)
        {
            switch (trigger)
            {
                case VehicleAudioTrigger.Horn: return horn ? 1 : 0;
                case VehicleAudioTrigger.Siren: return siren ? 1 : 0;
                case VehicleAudioTrigger.Speed: return Mathf.Clamp01(f.Speed / 60);
                case VehicleAudioTrigger.EngineLoad: return f.EngineRunning ? Mathf.Clamp01(f.EngineLoad) : 0;
                case VehicleAudioTrigger.EngineRpm: return f.EngineRunning ? Mathf.Clamp01(f.NormalizedRpm) : 0;
                case VehicleAudioTrigger.WheelSlip: return Mathf.Clamp01(Mathf.Max(Mathf.Max(f.Wheelspin, f.BrakeLock), Mathf.Max(f.FrontSlip, f.RearSlip)));
                case VehicleAudioTrigger.Scrape: return Mathf.Clamp01(f.Scrape);
                default: return 1;
            }
        }
        private void Bind(Voice voice, VehicleFeedbackFrame f)
        {
            if (voice.Sound.bindings == null) return;
            foreach (var binding in voice.Sound.bindings)
            {
                float value = Signal(binding.signal, f) * binding.multiplier + binding.offset;
                voice.Graph.SetParameter(binding.parameter, (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, (double)SensoryMath.Finite(value))));
            }
        }
        private float Signal(VehicleAudioSignal signal, VehicleFeedbackFrame f)
        {
            switch (signal)
            {
                case VehicleAudioSignal.Rpm: return f.EngineRpm; case VehicleAudioSignal.NormalizedRpm: return f.NormalizedRpm;
                case VehicleAudioSignal.Load: return f.EngineLoad; case VehicleAudioSignal.Speed: return f.Speed;
                case VehicleAudioSignal.Throttle: return f.Throttle; case VehicleAudioSignal.Brake: return f.Brake;
                case VehicleAudioSignal.Gear: return f.Gear; case VehicleAudioSignal.Shifting: return f.Shifting ? 1 : 0;
                case VehicleAudioSignal.Slip: return Mathf.Max(Mathf.Max(f.Wheelspin, f.BrakeLock), Mathf.Max(f.FrontSlip, f.RearSlip));
                case VehicleAudioSignal.Nitrous: return f.NitroIntensity; case VehicleAudioSignal.Scrape: return f.Scrape;
                case VehicleAudioSignal.EngineRunning: return f.EngineRunning ? 1 : 0;
                case VehicleAudioSignal.Horn: return horn ? 1 : 0; case VehicleAudioSignal.Siren: return siren ? 1 : 0; default: return 1;
            }
        }
        private void UpdateScrape(VehicleFeedbackFrame f)
        {
            if (profile == null) return;
            var surface = profile.Surface(f.ScrapeMaterial);
            var pair = profile.impactMaterials != null ? profile.impactMaterials.Find(f.BodyMaterial, f.ScrapeMaterial) : null;
            var clip = pair != null ? pair.scrape : surface != null ? surface.scrape : null;
            if (clip == scrapeClip) return;
            if (scrapeVoice != null) { int index = voices.IndexOf(scrapeVoice); if (index >= 0) RemoveAt(index); }
            scrapeClip = clip; scrapeVoice = AddClip(clip, VehicleAudioChannel.Scrape, true);
        }

        private void UpdateWheelAudio(VehicleFeedbackFrame f)
        {
            if (setup != null || profile == null)
            {
                for (int i = voices.Count - 1; i >= 0; i--)
                    if (voices[i].WheelIndex >= 0) RemoveAt(i);
                return;
            }
            for (int i = voices.Count - 1; i >= 0; i--)
            {
                var voice = voices[i];
                if (voice.WheelIndex < 0) continue;
                if (voice.WheelIndex >= f.WheelCount)
                { RemoveAt(i); continue; }
                var surface = profile.Surface(f.GetWheel(voice.WheelIndex).Surface);
                var clip = voice.WheelSlip ? surface?.tireSlip : surface?.rolling;
                if (voice.WheelClip != clip) RemoveAt(i);
            }
            for (int wheelIndex = 0; wheelIndex < f.WheelCount; wheelIndex++)
            {
                var surface = profile.Surface(f.GetWheel(wheelIndex).Surface);
                if (surface == null) continue;
                EnsureWheelVoice(wheelIndex, false, surface.rolling);
                EnsureWheelVoice(wheelIndex, true, surface.tireSlip);
            }
        }

        private void EnsureWheelVoice(int wheelIndex, bool slip, AudioClip clip)
        {
            if (clip == null) return;
            foreach (var voice in voices)
                if (voice.WheelIndex == wheelIndex && voice.WheelSlip == slip) return;
            var added = AddClip(clip, slip ? VehicleAudioChannel.Tires : VehicleAudioChannel.Road, true);
            if (added != null) { added.WheelIndex = wheelIndex; added.WheelSlip = slip; added.WheelClip = clip; }
        }

        private static void RestartContinuous(Voice voice)
        {
            voice.Graph.Reset();
            Set(voice, voice.Sound.parameters);
        }
    }
}
