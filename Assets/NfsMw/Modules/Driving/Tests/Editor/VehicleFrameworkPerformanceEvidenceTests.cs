using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using NfsMwRemaster.Driving.Editor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Profiling;
using Unity.Profiling;

namespace NfsMwRemaster.Driving.Tests
{
    [Serializable] public sealed class VehicleFrameworkPerformanceEvidence
    {
        public string unityVersion, platform, processor, device; public int systemMemoryMb, carCount, steps; public float fixedStep;
        public double averageControllerUs, p95ControllerUs, p99ControllerUs, averagePhysicsUs, p95PhysicsUs, p99PhysicsUs;
        public long managedAllocationBytes, controllerAllocationBytes, physicsAllocationBytes, profilerAllocatedBytes, profilerReservedBytes, positiveControlAllocationBytes;
        public long controllerAllocationCount, physicsAllocationCount, positiveControlAllocationCount;
        public string allocationMeasurement;
        public string configuration, measurementScope;
    }

    public sealed class VehicleFrameworkPerformanceEvidenceTests
    {
        [Test]
        public void OptInControllerAndPhysicsEvidenceIsWrittenSeparately()
        {
            string path = Environment.GetEnvironmentVariable("VEHICLE_FRAMEWORK_EVIDENCE");
            if (string.IsNullOrWhiteSpace(path)) Assert.Ignore("Set VEHICLE_FRAMEWORK_EVIDENCE to write performance evidence.");
            const int steps = 500; const float fixedStep = 1f / 50f;
            var reports = new List<VehicleFrameworkPerformanceEvidence>();
            for (int carCount = 1; carCount <= 8; carCount *= 8) reports.Add(Measure(carCount, steps, fixedStep));
            using var allocationRecorder = CreateAllocationRecorder();
            allocationRecorder.Start(); byte[] positive = new byte[1024]; GC.KeepAlive(positive); allocationRecorder.Stop();
            long positiveCount = AllocationCount(allocationRecorder);
            foreach (var report in reports) { report.positiveControlAllocationBytes = 1024; report.positiveControlAllocationCount = positiveCount; }
            string folder = Path.GetDirectoryName(Path.GetFullPath(path)); if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(path, JsonUtility.ToJson(new EvidenceEnvelope { schema = 1, reports = reports.ToArray() }, true));
            foreach (var report in reports) { Assert.That(report.averageControllerUs, Is.Not.NaN.And.GreaterThan(0)); Assert.That(report.averagePhysicsUs, Is.Not.NaN.And.GreaterThan(0)); Assert.That(report.positiveControlAllocationCount, Is.GreaterThan(0), "Allocation probe must observe a deliberate allocation."); Assert.That(report.controllerAllocationCount, Is.EqualTo(0), "Steady controller simulation must allocate no managed objects."); }
        }

        [Serializable] private sealed class EvidenceEnvelope { public int schema; public VehicleFrameworkPerformanceEvidence[] reports; }
        private static VehicleFrameworkPerformanceEvidence Measure(int carCount, int steps, float fixedStep)
        {
            var definition = ScriptableObject.CreateInstance<VehiclePhysicsLabDefinition>(); var setup = ScriptableObject.CreateInstance<RacingVehicleSetup>(); var tuning = VehicleTuning.CreateStreetRacer(); var track = ScriptableObject.CreateInstance<VehiclePhysicsLabTrack>();
            definition.id = Guid.NewGuid().ToString("N"); setup.id = Guid.NewGuid().ToString("N"); setup.tuning = tuning; setup.upgrades = Array.Empty<VehiclePerformanceUpgradeDefinition>(); definition.vehicle = setup; definition.track = track; definition.warmupSeconds = 0; definition.safety.maximumSeconds = 30; definition.capture.maximumSamples = 64; definition.evaluation.requireTargetSpeed = false; definition.fixedStep = fixedStep;
            var controllerTimes = new double[steps]; var physicsTimes = new double[steps];
            try
            {
                var runners = new VehiclePhysicsLabRunner[carCount];
                try
                {
                    var controllers = new VehicleController[carCount]; var inputs = new RacingSimulationInput[carCount]; var scenes = new PhysicsScene[carCount];
                    for (int c = 0; c < carCount; c++) { runners[c] = new VehiclePhysicsLabRunner(definition, () => new VehicleInputState { Throttle = .65f, Steering = .12f }); controllers[c] = runners[c].PreviewVehicle.GetComponent<VehicleController>(); inputs[c] = runners[c].PreviewVehicle.GetComponent<RacingSimulationInput>(); inputs[c].Current = new VehicleInputState { Throttle = .65f, Steering = .12f }; if (!controllers[c].ManualSimulation) controllers[c].SetManualSimulation(true); scenes[c] = runners[c].PreviewScene.GetPhysicsScene(); }
                    for (int c = 1; c < carCount; c++)
                    {
                        SceneManager.MoveGameObjectToScene(runners[c].PreviewVehicle, runners[0].PreviewScene);
                        controllers[c].transform.position += Vector3.right * (4f * c);
                    }
                    Physics.SyncTransforms();
                    for (int i = 0; i < 50; i++) { for (int c = 0; c < carCount; c++) controllers[c].StepSimulation(fixedStep); scenes[0].Simulate(fixedStep); }
                    using var allocations = CreateAllocationRecorder();
                    long controllerAlloc = 0, physicsAlloc = 0;
                    for (int i = 0; i < steps; i++)
                    {
                        allocations.Reset(); allocations.Start(); long start = Stopwatch.GetTimestamp();
                        for (int c = 0; c < carCount; c++) controllers[c].StepSimulation(fixedStep);
                        controllerTimes[i] = (Stopwatch.GetTimestamp() - start) * 1000000.0 / Stopwatch.Frequency; allocations.Stop(); controllerAlloc += AllocationCount(allocations);
                        allocations.Reset(); allocations.Start(); start = Stopwatch.GetTimestamp(); scenes[0].Simulate(fixedStep); physicsTimes[i] = (Stopwatch.GetTimestamp() - start) * 1000000.0 / Stopwatch.Frequency; allocations.Stop(); physicsAlloc += AllocationCount(allocations);
                    }
                    var report = Build(carCount, steps, fixedStep, controllerTimes, physicsTimes, controllerAlloc + physicsAlloc == 0 ? 0 : -1, controllerAlloc == 0 ? 0 : -1, physicsAlloc == 0 ? 0 : -1, Profiler.GetTotalAllocatedMemoryLong(), Profiler.GetTotalReservedMemoryLong());
                    report.controllerAllocationCount = controllerAlloc; report.physicsAllocationCount = physicsAlloc; report.allocationMeasurement = "Unity ProfilerRecorder GC.Alloc current-thread sample counts; bytes are zero for no events, otherwise unavailable (-1). Positive-control bytes are requested payload size."; return report;
                }
                finally { for (int c = runners.Length - 1; c >= 0; c--) runners[c]?.Dispose(); }
            }
            finally { UnityEngine.Object.DestroyImmediate(definition); UnityEngine.Object.DestroyImmediate(setup); UnityEngine.Object.DestroyImmediate(tuning); UnityEngine.Object.DestroyImmediate(track); }
        }
        private static VehicleFrameworkPerformanceEvidence Build(int cars, int steps, float fixedStep, double[] a, double[] b, long alloc, long controllerAlloc, long physicsAlloc, long memory, long reserved)
        { Array.Sort(a); Array.Sort(b); return new VehicleFrameworkPerformanceEvidence { unityVersion = Application.unityVersion, platform = Application.platform.ToString(), processor = SystemInfo.processorType, device = SystemInfo.graphicsDeviceName, systemMemoryMb = SystemInfo.systemMemorySize, carCount = cars, steps = steps, fixedStep = fixedStep, averageControllerUs = Mean(a), p95ControllerUs = Percentile(a, .95), p99ControllerUs = Percentile(a, .99), averagePhysicsUs = Mean(b), p95PhysicsUs = Percentile(b, .95), p99PhysicsUs = Percentile(b, .99), managedAllocationBytes = alloc, controllerAllocationBytes = controllerAlloc, physicsAllocationBytes = physicsAlloc, profilerAllocatedBytes = memory, profilerReservedBytes = reserved, configuration = VehicleController.SimulationRevision, measurementScope = "aggregate of " + cars + " simultaneously driven vehicles in one isolated local physics scene; editor total memory, excluding rendering and audio" }; }
        private static double Mean(double[] values) { double total = 0; foreach (double value in values) total += value; return total / Math.Max(1, values.Length); }
        private static ProfilerRecorder CreateAllocationRecorder() => new ProfilerRecorder(ProfilerCategory.Internal, "GC.Alloc", 1, ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
        private static long AllocationCount(ProfilerRecorder recorder) => recorder.Count > 0 ? recorder.GetSample(0).Count : 0;
        private static double Percentile(double[] values, double fraction) { return values[Math.Min(values.Length - 1, Math.Max(0, (int)Math.Ceiling(values.Length * fraction) - 1))]; }
    }
}
