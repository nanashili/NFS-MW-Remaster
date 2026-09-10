using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NfsMwRemaster.Driving.AudioAnalysis;
using NfsMwRemaster.Driving.Editor;
using NUnit.Framework;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    /// <summary>Opt-in measured evidence through the production Physics Lab. Never loads or modifies a user's scene.</summary>
    public sealed class VehicleFrameworkBaselineTests
    {
        [Serializable] private sealed class Measurement
        {
            public string revision, unity, cpu, scenario, configuration;
            public long memoryBytes;
            public double wallMilliseconds;
            public string scope = "Editor Physics Lab run including setup, capture and analysis; not simulation-only or GPU timing";
            public VehiclePhysicsLabRunReport report;
        }
        [Serializable] private sealed class ReferenceField
        {
            public string owner, field, type, value, confidence, units, conversion, unityParameter;
            public int byteOffset;
        }
        [Serializable] private sealed class ReferenceCapture
        {
            public string gameVersion = "Not identified; binary hashes identify this local installation exactly.";
            public string[] files, sha256;
            public ReferenceField[] fields;
        }

        [Test]
        public void CaptureProductionPhysicsBaseline()
        {
            string output = Environment.GetEnvironmentVariable("VEHICLE_FRAMEWORK_EVIDENCE");
            if (string.IsNullOrEmpty(output)) Assert.Ignore("Set VEHICLE_FRAMEWORK_EVIDENCE to capture the requested baseline.");
            Directory.CreateDirectory(output);
            foreach (var kind in new[] { VehiclePhysicsLabExperimentKind.StandingLaunch, VehiclePhysicsLabExperimentKind.Braking,
                VehiclePhysicsLabExperimentKind.CoastDown, VehiclePhysicsLabExperimentKind.StepSteer })
            {
                var tuning = VehicleTuning.CreateStreetRacer();
                var setup = ScriptableObject.CreateInstance<RacingVehicleSetup>();
                var track = ScriptableObject.CreateInstance<VehiclePhysicsLabTrack>();
                var definition = ScriptableObject.CreateInstance<VehiclePhysicsLabDefinition>();
                try
                {
                    setup.tuning = tuning; track.length = 3000; track.width = 200;
                    definition.vehicle = setup; definition.track = track; definition.experiment = kind;
                    definition.warmupSeconds = 1; definition.safety.maximumSeconds = 21;
                    definition.safety.fixtureHalfExtent = 2000; definition.capture.maximumSamples = 1600;
                    definition.startingSpeedKph = kind == VehiclePhysicsLabExperimentKind.StandingLaunch ? 0 : 80;
                    definition.brakingStartSpeedKph = 80; definition.input.steering = kind == VehiclePhysicsLabExperimentKind.StepSteer ? 0.12f : 0;
                    definition.evaluation.maximumLateralErrorMetres = 200;
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    using var runner = new VehiclePhysicsLabRunner(definition, () => VehicleInputState.Neutral);
                    while (!runner.IsDone && timer.Elapsed.TotalSeconds < 60) runner.Advance(25);
                    if (!runner.IsDone) runner.MarkIncomplete("WALL_TIMEOUT", "Baseline exceeded 60 seconds.");
                    var measurement = new Measurement { revision = VehicleController.SimulationRevision,
                        unity = Application.unityVersion, cpu = SystemInfo.processorType, memoryBytes = (long)SystemInfo.systemMemorySize * 1024 * 1024,
                        scenario = kind.ToString(), configuration = JsonUtility.ToJson(tuning), wallMilliseconds = timer.Elapsed.TotalMilliseconds, report = runner.Report };
                    File.WriteAllText(Path.Combine(output, kind + ".json"), JsonUtility.ToJson(measurement, true));
                    File.WriteAllText(Path.Combine(output, kind + ".csv"), VehiclePhysicsLabEditorOperations.ReportCsv(runner.Report));
                    Assert.That(runner.Report.samples.Length, Is.GreaterThan(5));
                    Assert.That(runner.Report.samples.All(s => float.IsFinite(s.speedKph)), Is.True);
                    // The measured outcome is evidence even when the baseline misses its handling target.
                    Assert.That(runner.Report.complete, Is.True, runner.Report.failureMessage);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(definition); UnityEngine.Object.DestroyImmediate(track);
                    UnityEngine.Object.DestroyImmediate(setup); UnityEngine.Object.DestroyImmediate(tuning);
                }
            }
        }

        [Test]
        public void CaptureAuthoredTopSpeedEvidence()
        {
            string output = Environment.GetEnvironmentVariable("VEHICLE_FRAMEWORK_EVIDENCE");
            if (string.IsNullOrEmpty(output)) Assert.Ignore("Set VEHICLE_FRAMEWORK_EVIDENCE to capture bounded top-speed evidence.");
            Directory.CreateDirectory(output);
            foreach (string id in new[] { "hatch-fwd", "coupe-rwd" })
            {
                var vehicle = UnityEditor.AssetDatabase.LoadAssetAtPath<VehicleDefinition>("Assets/NfsMw/Modules/Driving/Examples/VehicleFramework/" + id + "/Definition.asset");
                Assert.That(vehicle, Is.Not.Null);
                var setup = ScriptableObject.CreateInstance<RacingVehicleSetup>();
                var track = ScriptableObject.CreateInstance<VehiclePhysicsLabTrack>();
                var experiment = ScriptableObject.CreateInstance<VehiclePhysicsLabDefinition>();
                VehicleTuning resolved = null;
                try
                {
                    setup.definition = vehicle; setup.tuning = vehicle.factoryTuning;
                    track.length = 12000; track.width = 200;
                    experiment.vehicle = setup; experiment.track = track; experiment.warmupSeconds = 0;
                    experiment.safety.maximumSeconds = 120; experiment.safety.fixtureHalfExtent = 12000;
                    experiment.capture.maximumSamples = 2000; experiment.capture.sampleIntervalSeconds = .1f; experiment.capture.captureWheelChannels = false;
                    experiment.evaluation.requireTargetSpeed = false; experiment.evaluation.maximumLateralErrorMetres = 200;
                    using var runner = new VehiclePhysicsLabRunner(experiment);
                    while (!runner.IsDone) runner.Advance(50);
                    Assert.That(runner.Report.Passed, Is.True, runner.Report.failureMessage);
                    var samples = runner.Report.samples; var tail = samples.Where(s => s.time >= samples.Last().time - 10f).ToArray();
                    float range = tail.Max(s => s.speedKph) - tail.Min(s => s.speedKph);
                    resolved = setup.CreateEffectiveTuning();
                    var estimate = VehicleTopSpeedEstimator.Estimate(resolved);
                    var result = new TopSpeedEvidence { vehicleId = id, configuredTargetKph = resolved.chassis.maxSpeedKph,
                        governor = resolved.speedGovernor, engineeringEstimateKph = estimate.estimatedKph, gearingCeilingKph = estimate.gearingCeilingKph,
                        observedPeakKph = samples.Max(s => s.speedKph), finalTenSecondsMeanKph = tail.Average(s => s.speedKph), finalTenSecondsRangeKph = range,
                        steadyFullThrottle = tail.Last().time - tail.First().time >= 9.8f && range <= 1f
                            && tail.All(s => s.finalInput.Throttle >= .99f && s.finalInput.Brake == 0 && !s.nitrousActive && s.groundedWheels == 4),
                        scope = "120 s production Physics Lab launch, 0.02 s fixed step, 0.1 s telemetry. Terminal-speed evidence requires the last 10 s to remain within 1 km/h at full throttle, no brake/nitrous, four contacts; otherwise only the observed peak is a measured result." };
                    File.WriteAllText(Path.Combine(output, id + "-top-speed.json"), JsonUtility.ToJson(result, true));
                    File.WriteAllText(Path.Combine(output, id + "-top-speed-run.json"), JsonUtility.ToJson(runner.Report, true));
                }
                finally { if (resolved) UnityEngine.Object.DestroyImmediate(resolved); UnityEngine.Object.DestroyImmediate(experiment); UnityEngine.Object.DestroyImmediate(track); UnityEngine.Object.DestroyImmediate(setup); }
            }
        }

        [Serializable] private sealed class TopSpeedEvidence
        {
            public string vehicleId, scope;
            public bool governor, steadyFullThrottle;
            public float configuredTargetKph, engineeringEstimateKph, gearingCeilingKph, observedPeakKph, finalTenSecondsMeanKph, finalTenSecondsRangeKph;
        }

        [Test]
        public void CaptureInstalledReferenceDataWithoutChangingSources()
        {
            string output = Environment.GetEnvironmentVariable("VEHICLE_FRAMEWORK_EVIDENCE");
            string game = Environment.GetEnvironmentVariable("BLACKBOX_GAME");
            if (string.IsNullOrEmpty(output) || string.IsNullOrEmpty(game)) Assert.Ignore("Set evidence output and BLACKBOX_GAME for read-only reference capture.");
            string[] relative = { "GLOBAL/attributes.bin", "GLOBAL/FE_ATTRIB.bin", "GLOBAL/gameplay.bin", "speed.exe" };
            var paths = relative.Select(p => MostWantedAudioSetup.ResolveFile(game, p)).ToArray();
            var before = paths.Select(HashFile).ToArray();
            var db = new MostWantedAudioDatabase();
            string[] classes = { "engine", "transmission", "tires", "brakes", "chassis", "rigidbody", "suspension", "induction", "nos", "axlepair" };
            for (int i = 0; i < 3; i++) db.Load(File.ReadAllBytes(paths[i]), classes);
            var values = new List<ReferenceField>();
            string[] names = { "MASS", "Mass", "mass", "IDLE", "IDLE_RPM", "IdleRPM", "RED_LINE", "Redline", "REDLINE", "REV_LIMIT", "TORQUE", "TORQUE_CURVE", "RPM", "GEAR_RATIO", "GEAR_RATIOS", "FINAL_GEAR", "FINAL_DRIVE", "BRAKE_BIAS", "RIDE_HEIGHT", "SPRING_STIFFNESS", "DAMPER_STIFFNESS", "MinRPM", "MaxRPM", "CarID" };
            var labels = names.Distinct().GroupBy(MostWantedAudioDatabase.Hash).ToDictionary(g => g.Key, g => string.Join("|", g));
            foreach (string vehicle in new[] { "bmwm3gtre46", "m3gtre46", "gti", "golfgti", "punto" })
            {
                if (!db.TryFind("pvehicle", vehicle, out var car)) continue;
                foreach (string role in classes.Concat(new[] { "engineaudio" }))
                {
                    if (!db.Resolve(car).TryGetValue(MostWantedAudioDatabase.Hash(role), out var reference)) continue;
                    foreach (var item in reference.Items())
                    {
                        if (!db.TryFind(MostWantedAudioDatabase.Hash(role), item.CollectionKey(), out var row)) continue;
                        foreach (var entry in db.Resolve(row))
                        {
                            if (!labels.TryGetValue(entry.Key, out string name)) continue;
                            var numbers = new List<string>();
                            foreach (var value in entry.Value.Items())
                                try { numbers.Add(value.Number().ToString("R", System.Globalization.CultureInfo.InvariantCulture)); }
                                catch (InvalidDataException) { numbers.Add("Unsupported type; not interpreted."); }
                            values.Add(new ReferenceField { owner = vehicle + "/" + role + "/0x" + row.Key.ToString("X8"), field = name,
                                type = "0x" + entry.Value.Definition.Type.ToString("X8"), value = string.Join(",", numbers), byteOffset = entry.Value.Position,
                                confidence = "Verified field-hash match, inheritance and typed bytes; physical interpretation unverified",
                                units = "Source units unverified", conversion = "None; inspection only", unityParameter = "Not automatically applied" });
                        }
                    }
                }
            }
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "reference-data.json"), JsonUtility.ToJson(new ReferenceCapture
                { files = relative, sha256 = before, fields = values.ToArray() }, true));
            Assert.That(paths.Select(HashFile).ToArray(), Is.EqualTo(before), "Source installation must remain byte-identical.");
            Assert.That(values.Count, Is.GreaterThan(0), "No supported named reference fields were recovered.");
        }

        private static string HashFile(string path)
        {
            using var sha = SHA256.Create(); using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
