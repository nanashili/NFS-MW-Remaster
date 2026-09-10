using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using NfsMwRemaster.Driving.AudioAnalysis.Analysis;
using Unity.Profiling;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class BlackBoxPerformanceTests
    {
        [Serializable] sealed class Measurement
        { public int vehicles, callbacks, blockFrames; public bool tracing; public double elapsedMilliseconds, maximumCallbackMilliseconds; public long allocationEvents, dropped; }
        [Serializable] sealed class Report
        { public string unity, cpu, operatingSystem, utc, scope, allocationMethod; public int processorCount; public Measurement[] callbacks; public double analysisMilliseconds; public long allocationPositiveControlEvents, gcCounterPositiveControlBytes, analysisAllocationEvents, analysisRetainedScalarBytes; }
        const ProfilerRecorderOptions AllocationOptions = ProfilerRecorderOptions.CollectOnlyOnCurrentThread;

        [Test] public void MeasureAnalysisAndOneOrSixteenVehiclesOnAvailableHardware()
        {
            var pcm = new float[44100 * 3]; for (int i = 0; i < pcm.Length; i++) pcm[i] = (float)Math.Sin(i * 0.042) * 0.2f;
            Assert.True(EngineAudioCompiler.TryCompile(pcm, 0, pcm.Length, 44100, 1, new[] { new EngineRpmAnchor(1000, 0), new EngineRpmAnchor(7000, pcm.Length - 1) }, out var region));
            var snapshot = new EngineAudioCompiledSnapshot(new[] { region }, 1);
            using var positive = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 64, AllocationOptions);
            long counterBefore = GC.GetAllocatedBytesForCurrentThread(); var control = new byte[4096]; GC.KeepAlive(control);
            positive.Stop(); long counterControl = GC.GetAllocatedBytesForCurrentThread() - counterBefore, profilerControl = Allocations(positive);
            Assert.Greater(profilerControl, 0, "Allocation probe must detect a known managed allocation before zero counts can prove anything.");
            var report = new Report { unity = Application.unityVersion, cpu = SystemInfo.processorType, operatingSystem = SystemInfo.operatingSystem, processorCount = SystemInfo.processorCount, utc = DateTime.UtcNow.ToString("O"),
                allocationMethod = "Unity ProfilerRecorder GC.Alloc event counts, current thread, no frame summation; positive control retains a 4096-byte array. Marker duration is not allocation size. GC.GetAllocatedBytesForCurrentThread is a diagnostic control only.",
                allocationPositiveControlEvents = profilerControl, gcCounterPositiveControlBytes = counterControl,
                scope = "Unity Edit Mode, synchronous production callback kernel. Callback timing excludes main-thread snapshot compilation and trace draining. Does not measure device latency, audible quality, GUI responsiveness or game capture parity.",
                callbacks = new[] { Measure(snapshot, 1, 257, false), Measure(snapshot, 1, 257, true), Measure(snapshot, 16, 257, true), Measure(snapshot, 16, 1024, true) } };
            using var allocation = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 16384, AllocationOptions);
            var watch = Stopwatch.StartNew();
            var spectrum = SignalAnalysis.Spectrogram(pcm, 44100, 1, new SpectrogramParameters { WindowSize = 1024, HopSize = 256, MaxColumns = 1024 });
            SignalAnalysis.FrequencyCandidates(pcm, 44100, EngineOrderModel.Explicit, 60, 12);
            watch.Stop(); allocation.Stop(); report.analysisMilliseconds = watch.Elapsed.TotalMilliseconds; report.analysisAllocationEvents = Allocations(allocation);
            report.analysisRetainedScalarBytes = (long)spectrum.Magnitudes.Length * sizeof(float) + pcm.LongLength * sizeof(float);
            string folder = "Library/BlackBoxAudio/Reports"; Directory.CreateDirectory(folder); File.WriteAllText(folder + "/performance.json", JsonUtility.ToJson(report, true));
            foreach (var measurement in report.callbacks) Assert.AreEqual(0, measurement.allocationEvents, "Steady callback allocations");
            TestContext.WriteLine(JsonUtility.ToJson(report, true));
        }

        static long Allocations(ProfilerRecorder recorder)
        {
            Assert.True(recorder.Valid, "Unity GC.Alloc recorder unavailable");
            Assert.Less(recorder.Count, recorder.Capacity, "Allocation sample buffer exhausted");
            return recorder.Count;
        }

        static Measurement Measure(EngineAudioCompiledSnapshot snapshot, int count, int block, bool trace)
        {
            const int iterations = 120;
            var renderers = new EngineAudioRenderer[count]; var outputs = new float[count][];
            for (int i = 0; i < count; i++)
            {
                renderers[i] = new EngineAudioRenderer(trace ? 128 : 0); renderers[i].SetTelemetry(snapshot); renderers[i].SetFrame(2500 + i * 100, 0.7f, true, 0, 0, i, 3);
                outputs[i] = new float[block]; for (int w = 0; w < 8; w++) renderers[i].Render(0, 0, 48000, block, 1, 1, outputs[i]);
                while (renderers[i].Trace.TryRead(out _)) { }
            }
            long ticks = 0, maximum = 0;
            using var allocation = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 128, AllocationOptions);
            for (int n = 0; n < iterations; n++) for (int i = 0; i < count; i++)
            {
                long before = Stopwatch.GetTimestamp();
                renderers[i].Render(0, 0, 48000, block, 1, 1, outputs[i]);
                long duration = Stopwatch.GetTimestamp() - before; ticks += duration; maximum = Math.Max(maximum, duration);
                while (renderers[i].Trace.TryRead(out _)) { }
            }
            allocation.Stop(); long bytes = Allocations(allocation);
            long dropped = 0; foreach (var renderer in renderers) dropped += renderer.Trace.Dropped;
            return new Measurement { vehicles = count, callbacks = iterations * count, blockFrames = block, tracing = trace, allocationEvents = bytes, dropped = dropped,
                elapsedMilliseconds = ticks * 1000.0 / Stopwatch.Frequency, maximumCallbackMilliseconds = maximum * 1000.0 / Stopwatch.Frequency };
        }
    }
}
