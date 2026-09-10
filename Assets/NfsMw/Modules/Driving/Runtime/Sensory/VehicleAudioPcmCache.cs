using System;
using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>Main-thread leases share immutable decoded PCM across cars. Last owner releases the managed copy.</summary>
    internal sealed class VehicleAudioPcmCache : IDisposable
    {
        private sealed class ClipEntry { public float[] pcm; public int users; }
        private sealed class GinEntry { public EngineAudioCompiledSnapshot snapshot; public int users; }
        private static readonly Dictionary<AudioClip, ClipEntry> clips = new Dictionary<AudioClip, ClipEntry>();
        private static readonly Dictionary<EngineAudioRegion, GinEntry> grains = new Dictionary<EngineAudioRegion, GinEntry>();
        private readonly HashSet<AudioClip> heldClips = new HashSet<AudioClip>();
        private readonly HashSet<EngineAudioRegion> heldGrains = new HashSet<EngineAudioRegion>();
        public AemsPcmRenderer.Recording Recording(AudioClip clip, int id, int start = 0, int end = 0)
        {
            if (clip == null || clip.loadState != AudioDataLoadState.Loaded) throw new InvalidOperationException("Vehicle audio needs loaded, decompressed PCM clips.");
            if (!clips.TryGetValue(clip, out var entry))
            {
                var pcm = new float[checked(clip.samples * clip.channels)];
                if (!clip.GetData(pcm, 0)) throw new InvalidOperationException("Could not read vehicle audio PCM: " + clip.name);
                entry = new ClipEntry { pcm = pcm }; clips.Add(clip, entry);
            }
            if (heldClips.Add(clip)) entry.users++;
            return new AemsPcmRenderer.Recording { SampleId = id, SampleRate = clip.frequency, Channels = clip.channels,
                LoopStart = start, LoopEnd = end, Pcm = entry.pcm };
        }
        public EngineAudioCompiledSnapshot Gin(EngineAudioRegion region)
        {
            if (region == null) throw new InvalidOperationException("Missing GIN region.");
            if (!grains.TryGetValue(region, out var entry))
            {
                if (!EngineAudioCompiler.TryBuildSnapshot(new[] { region }, 1, out var snapshot)) throw new InvalidOperationException("Invalid recovered GIN mapping: " + region.id);
                entry = new GinEntry { snapshot = snapshot }; grains.Add(region, entry);
            }
            if (heldGrains.Add(region)) entry.users++;
            return entry.snapshot;
        }
        public void Dispose()
        {
            foreach (var clip in heldClips) if (--clips[clip].users == 0) clips.Remove(clip);
            foreach (var region in heldGrains) if (--grains[region].users == 0) grains.Remove(region);
            heldClips.Clear(); heldGrains.Clear();
        }
    }
}
