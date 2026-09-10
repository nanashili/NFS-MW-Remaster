using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Audio-thread bridge for a pooled SensoryAudioWorld voice. It owns no Unity state in the callback.</summary>
    public sealed class NativeEngineVoiceFilter : MonoBehaviour
    {
        private IProceduralVehicleAudio renderer;
        private int sampleRate = 48000;
        public void Configure(IProceduralVehicleAudio value, int outputRate = 48000) { System.Threading.Volatile.Write(ref sampleRate, outputRate > 0 ? outputRate : 48000); System.Threading.Volatile.Write(ref renderer, value); }
        public void SetSampleRate(int outputRate) { if (outputRate > 0) System.Threading.Volatile.Write(ref sampleRate, outputRate); }
        public void Clear() { System.Threading.Volatile.Write(ref renderer, null); }
        private void OnAudioFilterRead(float[] data, int channelCount)
        {
            var r = System.Threading.Volatile.Read(ref renderer); if (r == null || data == null || data.Length == 0) return;
            int channels = channelCount > 0 ? channelCount : 1;
            r.RenderInto(data, channels, System.Threading.Volatile.Read(ref sampleRate));
        }
    }
}
