using System;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>One recorded interval. EndFrame is exclusive; no implicit full-recording mapping is made.</summary>
    [Serializable]
    public sealed class EngineAudioRegion
    {
        public string id = string.Empty;
        public int sourceStartFrame;
        public int sourceEndFrame;
        public int sampleRate;
        public int channels = 1;
        public EngineRpmAnchor[] rpmAnchors = Array.Empty<EngineRpmAnchor>();
        [NonSerialized] public float[] compiledPcm = Array.Empty<float>();
        public AudioClip sourceClip;
        public string sourceHash = string.Empty;
        public string recordingId = string.Empty;
        public string provenance = string.Empty;
        public string evidence = string.Empty;
        public float minimumLoad;
        public float maximumLoad = 1;
        [Range(0, 1)] public float gain = 1;
        public int schemaVersion = 1;
        public bool IsValid => schemaVersion == 1 && sourceStartFrame >= 0 && sourceEndFrame > sourceStartFrame
            && sampleRate > 0 && channels > 0
            && (compiledPcm != null && compiledPcm.LongLength == ((long)sourceEndFrame - sourceStartFrame) * channels || sourceClip != null);
        internal EngineAudioRegion WithLayerGain(float layerGain) { var copy = (EngineAudioRegion)MemberwiseClone(); copy.gain *= layerGain; return copy; }
    }

    [Serializable]
    public struct EngineRpmAnchor
    {
        public float rpm;
        public int sourceFrame;
        public EngineRpmAnchor(float rpm, int sourceFrame) { this.rpm = rpm; this.sourceFrame = sourceFrame; }
    }

    /// <summary>Editor/main-thread compilation boundary. AudioClip.GetData is deliberately confined here.</summary>
    public static class EngineAudioCompiler
    {
        public static bool TryBuildProfile(VehicleSensoryProfile profile, out EngineAudioCompiledSnapshot snapshot)
        {
            snapshot = null; if (profile == null || profile.engineAudioSchema != 1 || profile.engineLayers == null) return false;
            var regions = new System.Collections.Generic.List<EngineAudioRegion>(16);
            foreach (var layer in profile.engineLayers)
            {
                if (layer == null || layer.nativeRegions == null) continue;
                if (!SensoryMath.IsFinite(layer.gain) || layer.gain < 0) return false;
                foreach (var region in layer.nativeRegions)
                { if (region == null || regions.Count >= 16) return false; regions.Add(region.WithLayerGain(layer.gain)); }
            }
            return TryBuildSnapshot(regions.ToArray(), profile.engineAudioRevision, out snapshot, profile.engineAudioId);
        }
        public static bool TryBuildSnapshot(EngineAudioRegion[] authored, int revision, out EngineAudioCompiledSnapshot snapshot, string profileId = "")
        {
            snapshot = null; if (authored == null || authored.Length < 1 || authored.Length > 16) return false;
            var compiled = new EngineAudioRegion[authored.Length];
            for (int i = 0; i < authored.Length; i++)
            {
                var item = authored[i]; if (item == null || item.schemaVersion != 1) return false;
                if (item.compiledPcm != null && item.compiledPcm.Length > 0) { compiled[i] = item; continue; }
                if (item.sourceClip == null || !TryCompile(item.sourceClip, item.sourceStartFrame, item.sourceEndFrame, item.rpmAnchors, out compiled[i], item.id)) return false;
                compiled[i].sourceHash = item.sourceHash; compiled[i].recordingId = item.recordingId; compiled[i].provenance = item.provenance; compiled[i].evidence = item.evidence;
                compiled[i].minimumLoad = item.minimumLoad; compiled[i].maximumLoad = item.maximumLoad; compiled[i].gain = item.gain;
            }
            var candidate = new EngineAudioCompiledSnapshot(compiled, revision, profileId);
            for (int i = 0; i < candidate.RegionCount; i++) if (candidate.GetRegion(i) == null || !candidate.GetRegion(i).IsValid) return false;
            snapshot = candidate; return true;
        }
        public static bool TryCompile(AudioClip clip, int sourceStartFrame, int sourceEndFrame,
            EngineRpmAnchor[] anchors, out EngineAudioRegion region, string id = "")
        {
            region = null;
            if (clip == null || clip.loadState != AudioDataLoadState.Loaded || clip.channels <= 0 || clip.frequency <= 0) return false;
            if (sourceStartFrame < 0 || sourceEndFrame <= sourceStartFrame || sourceEndFrame > clip.samples) return false;
            if (((long)sourceEndFrame - sourceStartFrame) * clip.channels > 32 * 1024 * 1024) return false;
            var data = new float[checked((sourceEndFrame - sourceStartFrame) * clip.channels)];
            if (!clip.GetData(data, sourceStartFrame)) return false;
            if (!TryCompile(data, sourceStartFrame, sourceEndFrame, clip.frequency, clip.channels, anchors, out region, id)) return false;
            region.sourceClip = clip;
            return true;
        }

        public static bool TryCompile(float[] pcm, int sourceStartFrame, int sourceEndFrame, int sampleRate,
            int channels, EngineRpmAnchor[] anchors, out EngineAudioRegion region, string id = "")
        {
            region = null;
            if (pcm == null || sampleRate <= 0 || channels <= 0 || sourceStartFrame < 0 || sourceEndFrame <= sourceStartFrame
                || pcm.LongLength != ((long)sourceEndFrame - sourceStartFrame) * channels || !ValidAnchors(anchors, sourceStartFrame, sourceEndFrame)) return false;
            region = new EngineAudioRegion { id = id ?? string.Empty, sourceStartFrame = sourceStartFrame,
                sourceEndFrame = sourceEndFrame, sampleRate = sampleRate, channels = channels,
                rpmAnchors = (EngineRpmAnchor[])anchors.Clone(), compiledPcm = (float[])pcm.Clone() };
            return true;
        }

        private static bool ValidAnchors(EngineRpmAnchor[] anchors, int start, int end)
        {
            if (anchors == null || anchors.Length < 2) return false;
            for (int i = 0; i < anchors.Length; i++)
                if (!SensoryMath.IsFinite(anchors[i].rpm) || anchors[i].rpm <= 0 || anchors[i].sourceFrame < start || anchors[i].sourceFrame >= end) return false;
            return true;
        }
    }

    public enum EngineAudioTraceReason : byte { Rendered, MissingRegion, InvalidRegion, UnknownControl, AmbiguousMapping, VoiceLimit, ZeroGain, EndOfSource, RpmOutOfRange, LoadExcluded, NotRunning, ChannelMismatch, GrainStarted }
    [Serializable]
    public struct EngineAudioTrace
    {
        public int revision, outputFrames, sourceFrame, sourceFramesRead, sampleRate, channels, voices, telemetryTick, gear, regionIndex;
        public long vehicleId;
        public long outputFrameStart;
        public long grainStartOutputFrame;
        public int grainLength, sourceOffset;
        public float hopRatio, sourceFramesSpan;
        public double timestamp;
        public bool engineRunning;
        public float requestedRpm, requestedLoad, sourceRate, gain, routeAttenuation;
        public float sourceRegionGain, effectiveGain;
        public long resetEpoch, snapshotToken;
        public EngineAudioTraceReason reason;
    }
    public struct EngineAudioTraceContext
    { public long outputFrameStart; public double timestamp; public int telemetryTick, gear, regionIndex; public long vehicleId, resetEpoch, snapshotToken; public bool engineRunning; }

    /// <summary>Bounded drop-new trace storage. The render callback only writes already allocated slots.</summary>
    public sealed class EngineAudioTraceRing
    {
        private readonly EngineAudioTrace[] entries;
        private long readIndex, writeIndex, dropped;
        public int Count { get { long count = System.Threading.Volatile.Read(ref writeIndex) - System.Threading.Volatile.Read(ref readIndex); return count > int.MaxValue ? int.MaxValue : count < 0 ? 0 : (int)count; } } public int Capacity => entries.Length; public long Dropped => System.Threading.Volatile.Read(ref dropped);
        public EngineAudioTraceRing(int capacity) { entries = new EngineAudioTrace[Mathf.Max(0, capacity)]; }
        public bool TryWrite(EngineAudioTrace value)
        { if (entries.Length == 0) return false; long write = System.Threading.Volatile.Read(ref writeIndex), read = System.Threading.Volatile.Read(ref readIndex); if (write - read >= entries.Length) { long prior; do { prior = System.Threading.Volatile.Read(ref dropped); if (prior == long.MaxValue) break; } while (System.Threading.Interlocked.CompareExchange(ref dropped, prior + 1, prior) != prior); return false; } entries[(int)(write % entries.Length)] = value; System.Threading.Volatile.Write(ref writeIndex, write + 1); return true; }
        public bool TryRead(out EngineAudioTrace value)
        { if (entries.Length == 0) { value = default; return false; } long read = System.Threading.Volatile.Read(ref readIndex), write = System.Threading.Volatile.Read(ref writeIndex); if (read >= write) { value = default; return false; } value = entries[(int)(read % entries.Length)]; System.Threading.Volatile.Write(ref readIndex, read + 1); return true; }
        public EngineAudioTrace Read(int index) { if (entries.Length == 0) return default; long read = System.Threading.Volatile.Read(ref readIndex), write = System.Threading.Volatile.Read(ref writeIndex), target = read + index; return index >= 0 && target < write ? entries[(int)(target % entries.Length)] : default; }
        public void Clear() { long write = System.Threading.Volatile.Read(ref writeIndex); System.Threading.Volatile.Write(ref readIndex, write); System.Threading.Volatile.Write(ref dropped, 0); }
    }

    public readonly struct EngineAudioRegionInfo
    {
        public readonly string Id, SourceHash, RecordingId, Provenance, Evidence;
        public readonly int SourceStartFrame, SourceEndFrame, SampleRate, Channels;
        public EngineAudioRegionInfo(EngineAudioRegion source)
        { Id = source.id ?? string.Empty; SourceHash = source.sourceHash ?? string.Empty; RecordingId = source.recordingId ?? string.Empty;
            Provenance = source.provenance ?? string.Empty; Evidence = source.evidence ?? string.Empty; SourceStartFrame = source.sourceStartFrame;
            SourceEndFrame = source.sourceEndFrame; SampleRate = source.sampleRate; Channels = source.channels; }
    }

    public sealed class EngineAudioRuntimeRegion
    {
        public readonly int SourceStartFrame, SourceEndFrame, SampleRate, Channels;
        internal readonly EngineRpmAnchor[] RpmAnchors;
        internal readonly float[] Pcm;
        public readonly float Gain, MinimumLoad, MaximumLoad;
        private readonly bool finitePcm, validAnchors;
        internal EngineAudioRuntimeRegion(EngineAudioRegion source)
        { SourceStartFrame = source.sourceStartFrame; SourceEndFrame = source.sourceEndFrame; SampleRate = source.sampleRate; Channels = source.channels;
            RpmAnchors = source.rpmAnchors == null ? Array.Empty<EngineRpmAnchor>() : (EngineRpmAnchor[])source.rpmAnchors.Clone();
            Pcm = source.compiledPcm == null ? Array.Empty<float>() : (float[])source.compiledPcm.Clone(); Gain = source.gain;
            finitePcm = true; for (int i = 0; i < Pcm.Length; i++) if (!SensoryMath.IsFinite(Pcm[i])) { finitePcm = false; break; }
            validAnchors = RpmAnchors.Length >= 2; for (int i = 0; i < RpmAnchors.Length; i++) if (!SensoryMath.IsFinite(RpmAnchors[i].rpm) || RpmAnchors[i].rpm <= 0 || RpmAnchors[i].sourceFrame < SourceStartFrame || RpmAnchors[i].sourceFrame >= SourceEndFrame) { validAnchors = false; break; }
            MinimumLoad = source.minimumLoad; MaximumLoad = source.maximumLoad; }
        public bool IsValid => SourceStartFrame >= 0 && SourceEndFrame > SourceStartFrame && SampleRate > 0 && Channels >= 1 && Channels <= 8 && MinimumLoad <= MaximumLoad
            && SensoryMath.IsFinite(Gain) && finitePcm && validAnchors && Pcm.LongLength == ((long)SourceEndFrame - SourceStartFrame) * Channels;
    }

    public sealed class EngineAudioCompiledSnapshot
    {
        private static long nextToken;
        public long Token { get; } = System.Threading.Interlocked.Increment(ref nextToken);
        private readonly EngineAudioRuntimeRegion[] regions;
        private readonly EngineAudioRegionInfo[] regionInfos;
        public int RegionCount => regions.Length;
        internal EngineAudioRuntimeRegion GetRegion(int index) => regions[index];
        public EngineAudioRegionInfo GetRegionInfo(int index) => regionInfos[index];
        public string ProfileId { get; }
        public readonly int Revision;
        public EngineAudioCompiledSnapshot(EngineAudioRegion[] regions, int revision)
        { Revision = revision; ProfileId = string.Empty; if (regions == null) { this.regions = Array.Empty<EngineAudioRuntimeRegion>(); regionInfos = Array.Empty<EngineAudioRegionInfo>(); return; }
            this.regions = new EngineAudioRuntimeRegion[regions.Length]; regionInfos = new EngineAudioRegionInfo[regions.Length]; for (int i = 0; i < regions.Length; i++)
                if (regions[i] != null) { regionInfos[i] = new EngineAudioRegionInfo(regions[i]); if (regions[i].compiledPcm != null) this.regions[i] = new EngineAudioRuntimeRegion(regions[i]); } }
        public EngineAudioCompiledSnapshot(EngineAudioRegion[] regions, int revision, string profileId)
            : this(regions, revision) { ProfileId = profileId ?? string.Empty; }
        internal EngineAudioCompiledSnapshot(EngineAudioRuntimeRegion[] regions, int revision, bool trusted)
        { this.regions = regions ?? Array.Empty<EngineAudioRuntimeRegion>(); regionInfos = new EngineAudioRegionInfo[this.regions.Length]; Revision = revision; ProfileId = string.Empty; }
    }

    /// <summary>Audio-owned grain scheduler state. Allocate and publish it before the callback starts.</summary>
    public sealed class EngineAudioRenderState
    {
        internal readonly double[] phase, phaseB;
        internal readonly double[] age, ageB, grainStart;
        internal EngineAudioRenderState(int count)
        {
            VehicleAudioWindow.Warmup();
            phase = new double[count]; phaseB = new double[count]; age = new double[count]; ageB = new double[count]; grainStart = new double[count];
            Reset();
        }
        internal void Reset()
        { for (int i = 0; i < phase.Length; i++) { phase[i] = double.NaN; phaseB[i] = double.NaN; age[i] = ageB[i] = grainStart[i] = 0; } }
    }

    /// <summary>Requested controls are main-thread writes; the renderer owns smoothed values.</summary>
    public sealed class EngineAudioPlaybackState
    {
        internal float Pitch = 1, Tempo = 1;
        internal volatile float RequestedPitch = 1, RequestedTempo = 1;
    }

    /// <summary>Pure bounded mixer. Callers own destination storage and may invoke it from an audio callback.</summary>
    public static class EngineAudioKernel
    {
        public static int Render(EngineAudioCompiledSnapshot snapshot, float rpm, float load, int outputSampleRate,
            int outputFrames, float gain, float routeAttenuation, float[] output, EngineAudioTraceRing trace,
            int maxVoices = 4, int outputChannels = 1, double[] cursors = null, EngineAudioTraceContext context = default, EngineAudioRenderState state = null,
            EngineAudioPlaybackState playback = null)
        {
            if (output == null || outputFrames <= 0 || outputChannels <= 0 || outputFrames > int.MaxValue / outputChannels || output.Length < outputFrames * outputChannels) return 0;
            Array.Clear(output, 0, outputFrames * outputChannels);
            context.snapshotToken = snapshot?.Token ?? 0;
            if (snapshot == null || snapshot.RegionCount == 0 || outputSampleRate <= 0 || outputSampleRate > 192000 || !SensoryMath.IsFinite(rpm) || rpm <= 0 || !SensoryMath.IsFinite(load) || load < 0 || load > 1 || !SensoryMath.IsFinite(gain) || !SensoryMath.IsFinite(routeAttenuation))
            { Write(trace, new EngineAudioTrace { revision = snapshot?.Revision ?? 0, outputFrames = outputFrames, requestedRpm = rpm, requestedLoad = load, reason = EngineAudioTraceReason.UnknownControl }, context); return 0; }
            int rendered = 0;
            float initialPitch = playback?.Pitch ?? 1, initialTempo = playback?.Tempo ?? 1;
            float targetPitch = playback?.RequestedPitch ?? 1, targetTempo = playback?.RequestedTempo ?? 1;
            float smoothing = 1f - (float)Math.Exp(-1.0 / (outputSampleRate * .0125));
            if (maxVoices <= 0)
            { Write(trace, new EngineAudioTrace { revision = snapshot.Revision, outputFrames = outputFrames, requestedRpm = rpm, requestedLoad = load, reason = EngineAudioTraceReason.VoiceLimit }, context); return 0; }
            for (int i = 0; i < snapshot.RegionCount && rendered < maxVoices; i++)
            {
                var region = snapshot.GetRegion(i);
                context.regionIndex = i;
                if (region == null || !region.IsValid) { Write(trace, new EngineAudioTrace { revision = snapshot.Revision, outputFrames = outputFrames, requestedRpm = rpm, requestedLoad = load, reason = EngineAudioTraceReason.InvalidRegion }, context); continue; }
                if (region.Channels > outputChannels) { Write(trace, new EngineAudioTrace { revision = snapshot.Revision, outputFrames = outputFrames, requestedRpm = rpm, requestedLoad = load, reason = EngineAudioTraceReason.ChannelMismatch }, context); continue; }
                bool ambiguous = false;
                // Validate the requested RPM at every callback boundary, even while grains are active.
                int mapped = ResolveFrame(region, rpm, out ambiguous);
                if (ambiguous) { Write(trace, new EngineAudioTrace { revision = snapshot.Revision, outputFrames = outputFrames, requestedRpm = rpm, requestedLoad = load, reason = EngineAudioTraceReason.AmbiguousMapping }, context); continue; }
                if (mapped < 0) { Write(trace, new EngineAudioTrace { revision = snapshot.Revision, outputFrames = outputFrames, requestedRpm = rpm, requestedLoad = load, reason = EngineAudioTraceReason.RpmOutOfRange }, context); continue; }
                if (load < region.MinimumLoad || load > region.MaximumLoad) { Write(trace, new EngineAudioTrace { revision = snapshot.Revision, outputFrames = outputFrames, requestedRpm = rpm, requestedLoad = load, reason = EngineAudioTraceReason.LoadExcluded }, context); continue; }
                float voiceGain = Mathf.Clamp01(SensoryMath.Finite(gain) * Mathf.Clamp01(region.Gain) * Mathf.Max(0, SensoryMath.Finite(routeAttenuation)));
                if (voiceGain <= 0) { Write(trace, new EngineAudioTrace { revision = snapshot.Revision, outputFrames = outputFrames, requestedRpm = rpm, requestedLoad = load, reason = EngineAudioTraceReason.ZeroGain }, context); continue; }
                int read = 0, grainLength = Mathf.Max(2, Mathf.RoundToInt(outputSampleRate * 0.04f)), hop = Mathf.Max(1, grainLength / 2);
                float samplePitch = initialPitch, sampleTempo = initialTempo;
                double step = region.SampleRate / (double)outputSampleRate;
                // GIN is RPM-addressed rather than a finite clip. Tempo advances the grain-envelope
                // clock; pitch advances PCM within each grain. RPM continues to select every origin.
                double source = state != null && i < state.phase.Length ? state.phase[i] : double.NaN;
                double sourceB = state != null && i < state.phaseB.Length ? state.phaseB[i] : double.NaN;
                double age = state != null && i < state.age.Length ? state.age[i] : grainLength;
                double ageB = state != null && i < state.ageB.Length ? state.ageB[i] : grainLength;
                double untilNext = state != null && i < state.grainStart.Length ? state.grainStart[i] : 0;
                for (int o = 0; o < outputFrames; o++)
                {
                    samplePitch += (targetPitch - samplePitch) * smoothing;
                    sampleTempo += (targetTempo - sampleTempo) * smoothing;
                    double samplePitchStep = region.SampleRate / (double)outputSampleRate * samplePitch;
                    if (untilNext <= 0)
                    {
                        if (double.IsNaN(source)) { source = mapped; age = 0; }
                        else if (double.IsNaN(sourceB)) { sourceB = mapped; ageB = 0; }
                        else if (ageB > age) { sourceB = mapped; ageB = 0; }
                        else { source = mapped; age = 0; }
                        untilNext += hop;
                        Write(trace, new EngineAudioTrace { revision = snapshot.Revision, outputFrames = outputFrames,
                            sourceFrame = mapped, sourceFramesRead = 0, sampleRate = region.SampleRate, channels = region.Channels,
                            requestedRpm = rpm, requestedLoad = load, sourceRate = region.SampleRate / (float)outputSampleRate,
                            gain = gain, sourceRegionGain = region.Gain, effectiveGain = voiceGain, routeAttenuation = routeAttenuation, voices = rendered, reason = EngineAudioTraceReason.GrainStarted,
                            grainStartOutputFrame = context.outputFrameStart + o, grainLength = grainLength,
                            sourceOffset = mapped - region.SourceStartFrame, hopRatio = 0.5f }, context);
                    }
                    double currentA = source, currentB = sourceB;
                    bool activeA = !double.IsNaN(currentA) && age < grainLength;
                    bool activeB = !double.IsNaN(currentB) && ageB < grainLength;
                    for (int c = 0; c < outputChannels; c++)
                    {
                        float channelSample = 0;
                        int authoredChannel = c % region.Channels;
                        if (activeA) channelSample += SampleLinear(region, WrapSource(region, currentA), authoredChannel) * Hann(age, grainLength);
                        if (activeB) channelSample += SampleLinear(region, WrapSource(region, currentB), authoredChannel) * Hann(ageB, grainLength);
                        output[o * outputChannels + c] += channelSample * voiceGain;
                    }
                    if (activeA) { source = WrapSource(region, currentA) + samplePitchStep; age += sampleTempo; read++; if (age >= grainLength) source = double.NaN; }
                    if (activeB) { sourceB = WrapSource(region, currentB) + samplePitchStep; ageB += sampleTempo; if (ageB >= grainLength) sourceB = double.NaN; }
                    untilNext -= sampleTempo;
                }
                if (playback != null) { playback.Pitch = samplePitch; playback.Tempo = sampleTempo; }
                if (state != null && i < state.phase.Length) { state.phase[i] = source; state.phaseB[i] = sourceB; state.age[i] = age; state.ageB[i] = ageB; state.grainStart[i] = untilNext; }
                rendered++; Write(trace, new EngineAudioTrace { revision = snapshot.Revision, outputFrames = outputFrames, sourceFrame = mapped, sourceFramesRead = read, sourceFramesSpan = (float)(read * step), sampleRate = region.SampleRate, channels = region.Channels, requestedRpm = rpm, requestedLoad = load, sourceRate = region.SampleRate / (float)outputSampleRate, gain = gain, sourceRegionGain = region.Gain, effectiveGain = voiceGain, routeAttenuation = routeAttenuation, voices = rendered, reason = read == 0 ? EngineAudioTraceReason.EndOfSource : EngineAudioTraceReason.Rendered }, context);
            }
            if (rendered >= maxVoices && snapshot.RegionCount > maxVoices)
                Write(trace, new EngineAudioTrace { revision = snapshot.Revision, outputFrames = outputFrames, requestedRpm = rpm, requestedLoad = load, voices = rendered, reason = EngineAudioTraceReason.VoiceLimit }, context);
            return rendered;
        }

        private static float Hann(double age, int length) => VehicleAudioWindow.Hann(age / length);
        private static double WrapSource(EngineAudioRuntimeRegion region, double source)
        {
            double length = region.SourceEndFrame - region.SourceStartFrame;
            double offset = (source - region.SourceStartFrame) % length;
            if (offset < 0) offset += length;
            return region.SourceStartFrame + offset;
        }
        private static float SampleLinear(EngineAudioRuntimeRegion region, double source, int channel)
        {
            double local = source - region.SourceStartFrame;
            int length = region.SourceEndFrame - region.SourceStartFrame;
            int a = Mathf.Clamp((int)Math.Floor(local), 0, length - 1);
            int b = (a + 1) % length;
            float t = (float)(local - Math.Floor(local));
            return Mathf.Lerp(region.Pcm[a * region.Channels + channel], region.Pcm[b * region.Channels + channel], t);
        }

        private static int ResolveFrame(EngineAudioRuntimeRegion region, float rpm, out bool ambiguous)
        {
            ambiguous = false; var a = region.RpmAnchors; if (a == null || a.Length < 2) return -1;
            int exact = -1;
            for (int e = 0; e < a.Length; e++) if (rpm == a[e].rpm) { if (exact >= 0) { ambiguous = true; return -1; } exact = e; }
            if (exact >= 0) return exact == 0 ? a[0].sourceFrame : a[exact].sourceFrame;
            int match = -1;
            for (int i = 0; i < a.Length - 1; i++)
            {
                float lo = a[i].rpm, hi = a[i + 1].rpm;
                if ((rpm >= lo && rpm <= hi) || (rpm >= hi && rpm <= lo)) { if (match >= 0) { ambiguous = true; return -1; } match = i; }
            }
            if (match < 0) return -1;
            float t = Mathf.InverseLerp(a[match].rpm, a[match + 1].rpm, rpm);
            return Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(a[match].sourceFrame, a[match + 1].sourceFrame, t)), region.SourceStartFrame, region.SourceEndFrame - 1);
        }
        private static float ClampPlayback(float value)
        { return !SensoryMath.IsFinite(value) ? 1f : Mathf.Clamp(value, 0.25f, 4f); }
        private static void Write(EngineAudioTraceRing trace, EngineAudioTrace item, EngineAudioTraceContext context) { if (trace == null) return; item.outputFrameStart = context.outputFrameStart; item.timestamp = context.timestamp; item.telemetryTick = context.telemetryTick; item.vehicleId = context.vehicleId; item.gear = context.gear; item.regionIndex = context.regionIndex; item.resetEpoch = context.resetEpoch; item.snapshotToken = context.snapshotToken; item.engineRunning = context.engineRunning; trace.TryWrite(item); }
    }

    public sealed class EngineAudioRenderer : IProceduralVehicleAudio
    {
        /// <summary>Playback multipliers are finite and clamped to 0.25..4.</summary>
        public const float SupportedPlaybackMinimum = 0.25f, SupportedPlaybackMaximum = 4f;
        public void RenderInto(float[] output, int channels, int sampleRate)
            => RenderInto(output, channels, sampleRate, output != null && channels > 0 ? output.Length / channels : 0);
        public void RenderInto(float[] output, int channels, int sampleRate, int frameCount)
        { if (output == null || channels < 1 || frameCount < 0 || frameCount > output.Length / Math.Max(1, channels)) return; Render(0, 0, sampleRate, frameCount, 1, 1, output, 4, channels); }
        private sealed class RendererFrame
        {
            internal readonly float rpm, load; internal readonly bool hasInputs, running; internal readonly int tick, gear; internal readonly long vehicle; internal readonly double time;
            internal RendererFrame(float rpm, float load, bool hasInputs, bool running, int tick, double time, long vehicle, int gear)
            { this.rpm = rpm; this.load = load; this.hasInputs = hasInputs; this.running = running; this.tick = tick; this.time = time; this.vehicle = vehicle; this.gear = gear; }
        }

        private sealed class RenderBundle
        {
            internal readonly EngineAudioCompiledSnapshot snapshot;
            internal readonly EngineAudioRenderState state;
            internal RenderBundle(EngineAudioCompiledSnapshot snapshot)
            { this.snapshot = snapshot; state = new EngineAudioRenderState(snapshot == null ? 0 : snapshot.RegionCount); }
        }
        private RenderBundle bundle;
        public EngineAudioCompiledSnapshot Snapshot => System.Threading.Volatile.Read(ref bundle)?.snapshot;
        public bool IsReady => Snapshot != null;
        public readonly EngineAudioTraceRing Trace;
        private RendererFrame frame = new RendererFrame(1000, 0, false, true, 0, 0, 0, 0);
        private int cadence;
        private long outputFrame;
        private long resetEpoch;
        private int resetToken, appliedResetToken;
        private readonly EngineAudioPlaybackState playback = new EngineAudioPlaybackState();

        public EngineAudioRenderer(int traceCapacity = 128) { VehicleAudioWindow.Warmup(); Trace = new EngineAudioTraceRing(traceCapacity); }

        /// <summary>Sets the authored-rate layer. Values outside 0.25..4 are clamped; non-finite values become 1.</summary>
        public void SetPlayback(float pitch, float tempo)
        { playback.RequestedPitch = ClampPlayback(pitch); playback.RequestedTempo = ClampPlayback(tempo); }
        private static float ClampPlayback(float value)
        { return !SensoryMath.IsFinite(value) ? 1f : Mathf.Clamp(value, SupportedPlaybackMinimum, SupportedPlaybackMaximum); }

        public void SetTelemetry(EngineAudioCompiledSnapshot value)
        {
            // Publish the immutable snapshot and its matching audio-owned state as one bundle.
            System.Threading.Volatile.Write(ref bundle, new RenderBundle(value));
        }

        public void SetInputs(float rpm, float load)
        { var prior = System.Threading.Volatile.Read(ref frame); SetFrame(rpm, load, prior.running, prior.tick, prior.time, prior.vehicle, prior.gear); }
        public void SetInputs(float rpm, float load, bool running)
        { var prior = System.Threading.Volatile.Read(ref frame); SetFrame(rpm, load, running, prior.tick, prior.time, prior.vehicle, prior.gear); }
        public void SetContext(int tick, double time, long vehicle, int currentGear)
        { var prior = System.Threading.Volatile.Read(ref frame); SetFrame(prior.rpm, prior.load, prior.running, tick, time, vehicle, currentGear); }
        public void SetFrame(float rpm, float load, bool running, int tick, double time, long vehicle, int currentGear)
        { System.Threading.Volatile.Write(ref frame, new RendererFrame(rpm, load, true, running, tick, time, vehicle, currentGear)); }

        public void Reset()
        {
            // Reset is consumed by the audio callback; it never mutates callback-owned cursors from the main thread.
            System.Threading.Interlocked.Increment(ref resetToken);
        }

        public int Render(float rpm, float load, int sampleRate, int frames, float gain, float routeAttenuation,
            float[] output, int maxVoices = 4, int channels = 1)
        {
            int requestedReset = System.Threading.Volatile.Read(ref resetToken);
            if (requestedReset != appliedResetToken)
            {
                appliedResetToken = requestedReset; cadence = 0; outputFrame = 0; resetEpoch++;
                var resetBundle = System.Threading.Volatile.Read(ref bundle);
                resetBundle?.state.Reset();
                // The callback owns the write cursor; only the consumer may drain old records.
                // Keep records across reset so resetting cannot race or overwrite a reader.
            }
            cadence++;
            var currentFrame = System.Threading.Volatile.Read(ref frame);
            var published = System.Threading.Volatile.Read(ref bundle);
            var current = published?.snapshot;
            var state = published?.state;
            EngineAudioTraceContext traceContext = new EngineAudioTraceContext { outputFrameStart = outputFrame,
                timestamp = currentFrame.time, telemetryTick = currentFrame.tick, vehicleId = currentFrame.vehicle, gear = currentFrame.gear,
                resetEpoch = resetEpoch, snapshotToken = current?.Token ?? 0, engineRunning = currentFrame.running };
            if (!currentFrame.running)
            {
                if (output != null && channels > 0 && frames > 0) Array.Clear(output, 0, (int)Math.Min((long)output.Length, (long)frames * channels));
                WriteStopped(current, frames, traceContext); outputFrame += frames; return 0;
            }
            int result = EngineAudioKernel.Render(current, currentFrame.hasInputs ? currentFrame.rpm : rpm,
                currentFrame.hasInputs ? currentFrame.load : load, sampleRate, frames, gain, routeAttenuation, output, Trace,
                maxVoices, channels, null, traceContext, state, playback);
            outputFrame += frames; return result;
        }

        private void WriteStopped(EngineAudioCompiledSnapshot current, int frames, EngineAudioTraceContext context)
        { Trace.TryWrite(new EngineAudioTrace { revision = current?.Revision ?? 0, outputFrames = frames,
            outputFrameStart = context.outputFrameStart, timestamp = context.timestamp,
            telemetryTick = context.telemetryTick, vehicleId = context.vehicleId, gear = context.gear,
            resetEpoch = context.resetEpoch, snapshotToken = context.snapshotToken, engineRunning = false, reason = EngineAudioTraceReason.NotRunning }); }

        private static void VolatileWrite<T>(ref T location, T value) where T : class
        { System.Threading.Volatile.Write(ref location, value); }
        private static T VolatileRead<T>(ref T location) where T : class
        { return System.Threading.Volatile.Read(ref location); }
        public int Cadence => cadence;
    }
}
