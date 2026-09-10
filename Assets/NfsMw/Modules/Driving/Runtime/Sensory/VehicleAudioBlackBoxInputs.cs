using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    // Recovered MW interface order and integer domains. Author mix controls never overwrite these values.
    public sealed partial class VehicleAudioRuntime
    {
        public void Sample(VehicleFeedbackFrame frame)
        {
            if (disposed || sampled && frame.Epoch == previous.Epoch && frame.Sequence <= previous.Sequence) return;
            bool edgesAllowed = !sampled || frame.Epoch == previous.Epoch;
            if (setup != null)
            {
                SensorySurface surfaceId = SensorySurface.Air; float greatest = -1;
                for (int i = 0; i < frame.WheelCount; i++)
                { var w = frame.GetWheel(i); if (w.Grounded && w.Load > greatest) { greatest = w.Load; surfaceId = w.Surface; } }
                var surface = Array.Find(setup.surfaces, s => s.surface == surfaceId);
                if (surface != roadSurface)
                {
                    if (sampled && surface != null && roadSurface != null && surface.roadLoop != roadSurface.roadLoop && frame.Speed > 2 && surface.enter != -2 && roadSurface.exit != -2)
                    {
                        int id = surface.enter >= 0 ? surface.enter : roadSurface.exit;
                        if (id >= 0) Set(AddBank(setup.road, "FX_ROADNOISE_TRANS", VehicleAudioChannel.SurfaceTransitions, true), stackalloc int[] { id, 24000, 4096, 0, 1, 0, 0, 25000, 0, 32767, 0 });
                    }
                    for (int i = voices.Count - 1; i >= 0; i--) if (voices[i].Channel == VehicleAudioChannel.Road && voices[i].Sound == null) RemoveAt(i);
                    roadSurface = surface; AddBank(setup.road, "FX_ROADNOISE", VehicleAudioChannel.Road);
                }
                if (edgesAllowed && frame.EngineRunning)
                {
                    if ((frame.Edges & FeedbackEdges.Upshift) != 0) Shift(0, frame);
                    if ((frame.Edges & FeedbackEdges.Downshift) != 0) Shift(1, frame);
                    if (sampled && previous.Brake < .75f && frame.Brake >= .75f && frame.Speed > 2) Shift(2, frame);
                    if (sampled && previous.Throttle < .2f && frame.Throttle >= .6f) Sweet(1, frame);
                    if (sampled && previous.Throttle > .5f && frame.Throttle <= .15f) Sweet(0, frame);
                }
            }
            if (edgesAllowed)
            {
                Edge(frame, FeedbackEdges.Startup, VehicleAudioTrigger.Startup, profile != null ? profile.startup : null, VehicleAudioChannel.Startup);
                Edge(frame, FeedbackEdges.Upshift, VehicleAudioTrigger.Upshift, setup == null && profile != null ? profile.shiftUp : null, VehicleAudioChannel.Shifts);
                Edge(frame, FeedbackEdges.Downshift, VehicleAudioTrigger.Downshift, setup == null && profile != null ? profile.shiftDown : null, VehicleAudioChannel.Shifts);
                Edge(frame, FeedbackEdges.Limiter, VehicleAudioTrigger.Limiter, setup == null && profile != null ? profile.limiter : null, VehicleAudioChannel.Limiter);
                Edge(frame, FeedbackEdges.Overrun, VehicleAudioTrigger.Overrun, setup == null && profile != null ? profile.overrun : null, VehicleAudioChannel.Overrun);
                Edge(frame, FeedbackEdges.NitroOn, VehicleAudioTrigger.NitroOn, null, VehicleAudioChannel.Nitrous);
                Edge(frame, FeedbackEdges.NitroOff, VehicleAudioTrigger.NitroOff, null, VehicleAudioChannel.Nitrous);
            }
            previous = frame; sampled = true;
        }
        private void Edge(VehicleFeedbackFrame frame, FeedbackEdges edge, VehicleAudioTrigger trigger, AudioClip clip, VehicleAudioChannel channel)
        { if ((frame.Edges & edge) != 0) { Trigger(trigger); AddClip(clip, channel, false, .7f); } }
        private void Shift(int id, VehicleFeedbackFrame frame)
        {
            Set(AddBank(setup.shifts, "FX_SHIFTING_01", VehicleAudioChannel.Shifts, true), stackalloc int[] { id, 32767, 4096, 0, 0, 0 });
            if (id < 2 && frame.EngineRpm >= 7000) { Sweet(0, frame); Sweet(1, frame); }
        }
        private void Sweet(int id, VehicleFeedbackFrame frame)
            => Set(AddBank(setup.sweeteners, "CAR_SWTN", VehicleAudioChannel.Sweeteners, true), stackalloc int[] { id, setup.carId, (int)frame.EngineRpm, 32767, 0, 0, 0 });
        public void Impact(FeedbackImpact impact)
        {
            if (disposed) return;
            Trigger(VehicleAudioTrigger.Impact);
            if (profile == null) return;
            var surface = profile.Surface(impact.Material);
            var pair = profile.impactMaterials != null ? profile.impactMaterials.Find(impact.BodyMaterial, impact.Material) : null;
            AddClip(pair != null ? impact.Severity >= pair.heavyThreshold ? pair.heavy : pair.light : surface != null ? surface.impact : null,
                VehicleAudioChannel.Impacts, false, Mathf.Clamp01(impact.Severity));
        }
        private void Parameters(Voice voice, VehicleFeedbackFrame f)
        {
            int speed = (int)Mathf.Abs(f.Speed), pitch = 4096;
            float rpm = Mathf.Max(1, f.EngineRpm), load = Mathf.Clamp01(f.EngineLoad);
            switch (voice.Program.interfaceName)
            {
                case "CAR": Set(voice, stackalloc int[] { setup.carId, (int)rpm, 0, (int)(load * 1024), 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, rpm >= setup.maximumRpm * .98f ? 32767 : 0 }); break;
                case "CAR_TRANNY": Set(voice, stackalloc int[] { setup.carId, speed * 15, 32767, 0, 0, 25000, 0, 32767, f.Shifting ? 1 : 0 }); break;
                case "CAR_Sputter": Set(voice, stackalloc int[] { setup.carId, 1, (int)rpm, 32767, 0, 0, 0, (int)Mathf.Clamp(f.EngineTorque * 10.24f, 0, 1024), 0, f.Throttle > .15f ? 1 : 0, f.Shifting ? 1 : 0 }); break;
                case "CAR_WHINE": Set(voice, stackalloc int[] { setup.carId, (int)rpm, f.Shifting ? 32767 : 0, 0, 0, 0, 25000, 0, 32767 }); break;
                case "FX_SKID":
                    int forward = (int)(Mathf.Clamp01(Mathf.Max(f.Wheelspin, f.BrakeLock)) * 32767), side = (int)(Mathf.Clamp01(Mathf.Max(f.FrontSlip, f.RearSlip)) * 32767);
                    int grip = f.Airborne ? 0 : 1023, fb = f.Airborne ? 0 : forward * 1023 / 32767;
                    Set(voice, stackalloc int[] { f.Airborne ? 0 : forward, f.Airborne ? 0 : side, 0, 0, 0, 0, 0, (int)(speed * 2.23694f), 0, 0, 0, Mathf.Clamp(roadSurface?.skid ?? 0, 0, 5),
                        fb, (int)(Mathf.Clamp01(f.FrontSlip) * grip), grip, fb, (int)(Mathf.Clamp01(f.RearSlip) * grip), grip, 25000, 0, 32767, 0 }); break;
                case "FX_ROADNOISE": Set(voice, stackalloc int[] { Math.Max(0, roadSurface?.roadLoop ?? -1), f.Airborne || roadSurface == null || roadSurface.roadLoop < 0 ? 0 : (int)(Mathf.Clamp01(speed / 60f) * 20000), (int)Mathf.Lerp(1500, 4500, speed / 100f), 0, 0, 0, speed, 25000, 0, 32767, 0 }); break;
                case "FX_WIND": int wind = (int)(Mathf.Clamp01(speed / 90f) * 10000); Set(voice, stackalloc int[] { pitch, wind, wind, 0, 0, (int)(Mathf.Clamp01(speed / 90f) * 32767), 0, 0, 0, 25000, 0, 32767, 0, 15 }); break;
                case "FX_NITROUS": Set(voice, stackalloc int[] { 0, (int)(f.NitroIntensity * 32767), 0, f.NitroIntensity > 0 ? 0 : 1, pitch, 25000, 0, 32767, 0 }); break;
            }
        }
    }
}
