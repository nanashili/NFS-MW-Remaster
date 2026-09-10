using System;
using System.Threading;

namespace NfsMwRemaster.Driving
{
    /// <summary>One spatial source per car; bounded layers render through preallocated scratch storage.</summary>
    internal sealed class VehicleAudioBus : IProceduralVehicleAudio
    {
        internal sealed class Layer
        {
            private sealed class Control { public float gain, pan, low = 22000, high; }
            public readonly IProceduralVehicleAudio Renderer;
            private Control control = new Control();
            private readonly float[] lowState = new float[8], highState = new float[8], prior = new float[8];
            private float gain;
            public Layer(IProceduralVehicleAudio renderer) { Renderer = renderer; }
            public void Set(float volume, float pan, float low, float high)
            {
                var old = Volatile.Read(ref control);
                if (old.gain == volume && old.pan == pan && old.low == low && old.high == high) return;
                Volatile.Write(ref control, new Control { gain = volume, pan = pan, low = low, high = high });
            }
            public void Mix(float[] scratch, float[] output, int offset, int frames, int channels, int sampleRate)
            {
                Renderer.RenderInto(scratch, channels, sampleRate, frames);
                var c = Volatile.Read(ref control);
                float smooth = (float)(1 - Math.Exp(-1.0 / (.005 * sampleRate)));
                bool lowEnabled = c.low < Math.Min(22000, sampleRate * .49f), highEnabled = c.high > 0;
                float lowAlpha = (float)(1 - Math.Exp(-2 * Math.PI * c.low / sampleRate));
                float highAlpha = (float)Math.Exp(-2 * Math.PI * c.high / sampleRate);
                float left = (float)Math.Sqrt(1 - Math.Max(0, c.pan)), right = (float)Math.Sqrt(1 + Math.Min(0, c.pan));
                for (int f = 0; f < frames; f++)
                {
                    gain += (c.gain - gain) * smooth;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        float value = scratch[f * channels + ch];
                        lowState[ch] += lowAlpha * (value - lowState[ch]);
                        if (lowEnabled) value = lowState[ch];
                        highState[ch] = highAlpha * (highState[ch] + value - prior[ch]); prior[ch] = value;
                        if (highEnabled) value = highState[ch];
                        output[offset + f * channels + ch] += value * gain * (channels == 1 ? 1 : ch == 0 ? left : ch == 1 ? right : 1);
                    }
                }
            }
        }
        private Layer[] layers = Array.Empty<Layer>();
        private readonly float[] scratch = new float[512 * 8];
        public bool IsReady => Volatile.Read(ref layers).Length > 0;
        public void Publish(Layer[] next) => Volatile.Write(ref layers, next ?? Array.Empty<Layer>());
        public void RenderInto(float[] output, int channels, int sampleRate)
            => RenderInto(output, channels, sampleRate, output != null && channels > 0 ? output.Length / channels : 0);
        public void RenderInto(float[] output, int channels, int sampleRate, int frameCount)
        {
            if (output == null || channels < 1 || channels > 8 || sampleRate < 1 || frameCount < 0 || frameCount > output.Length / channels) return;
            Array.Clear(output, 0, frameCount * channels);
            var current = Volatile.Read(ref layers);
            for (int start = 0; start < frameCount; start += 512)
            {
                int count = Math.Min(512, frameCount - start);
                foreach (var layer in current) layer.Mix(scratch, output, start * channels, count, channels, sampleRate);
            }
            // The sum can exceed full scale when several original banks fire together. Bound the actual output.
            for (int i = 0; i < frameCount * channels; i++) output[i] = Math.Max(-1, Math.Min(1, output[i]));
        }
    }
}
