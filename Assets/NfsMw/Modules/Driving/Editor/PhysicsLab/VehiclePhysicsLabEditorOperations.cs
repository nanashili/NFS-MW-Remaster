using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor
{
    public static class VehiclePhysicsLabEditorOperations
    {
        public const string ExampleFolder = "Assets/NfsMw/Modules/Driving/Examples/VehiclePhysicsLab";
        public const string ExampleDefinitionPath = ExampleFolder + "/StandingLaunch.asset";
        public const string ExampleTrackPath = ExampleFolder + "/FlatStraight.asset";
        public const string ExampleScenePath = ExampleFolder + "/VehiclePhysicsLab.unity";

        public static string Fingerprint(VehiclePhysicsLabDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            string vehicle = definition.vehicle == null ? "<null>" : RacingLineSnapshot.VehicleFingerprintOf(definition.vehicle);
            using (var digest = new RacingDigest())
            {
                digest.Add("vehicle-physics-lab.1");
                digest.Add(definition.id);
                digest.Add(JsonUtility.ToJson(definition));
                digest.Add(definition.vehicle == null ? "<null>" : definition.vehicle.id);
                digest.Add(definition.track == null ? "<null>" : definition.track.id);
                digest.Add(vehicle);
                digest.Add(VehicleController.SimulationRevision);
                digest.Add(Application.unityVersion);
                return digest.Finish();
            }
        }

        public static VehicleTuning CreateEffectiveTuning(VehiclePhysicsLabDefinition definition, out string[] diagnostics)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!definition.IsValid(out string failure)) throw new ArgumentException(failure);
            VehicleTuning tuning = definition.vehicle.CreateEffectiveTuning();
            if (!VehiclePhysicsLabTuningOverrides.Apply(tuning, definition.temporaryOverrides, out diagnostics))
            {
                UnityEngine.Object.DestroyImmediate(tuning);
                throw new ArgumentException(string.Join("; ", diagnostics));
            }

            try
            {
                RacingLineSnapshot.ValidateTuning(tuning);
                return tuning;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(tuning);
                throw;
            }
        }

        public static string ReportCsv(VehiclePhysicsLabRunReport report)
        {
            ValidateReport(report);
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("tick,time,measurement_time,pos_x,pos_y,pos_z,vel_x,vel_y,vel_z,accel_x,accel_y,accel_z,speed_kph,forward_speed_kph,lateral_speed_mps,yaw_rate_rad_s,lateral_accel_mps2,slip_angle_deg,slip_angle_valid,airborne,grounded_wheels,engine_rpm,engine_torque,drivetrain_torque,gear,shifting,nitrous_active,nitrous_seconds,raw_steering,raw_throttle,raw_brake,raw_handbrake,raw_nitrous,final_steering,final_throttle,final_brake,final_handbrake,final_nitrous,handling,assist_yaw_torque,average_longitudinal_slip");
            for (int i = 0; i < report.samples.Length; i++)
            {
                VehiclePhysicsLabSample s = report.samples[i];
                builder.Append(Csv(s.tick)).Append(',').Append(Csv(s.time)).Append(',').Append(Csv(s.measurementTime)).Append(',')
                    .Append(Csv(s.position.x)).Append(',').Append(Csv(s.position.y)).Append(',').Append(Csv(s.position.z)).Append(',')
                    .Append(Csv(s.velocity.x)).Append(',').Append(Csv(s.velocity.y)).Append(',').Append(Csv(s.velocity.z)).Append(',')
                    .Append(Csv(s.acceleration.x)).Append(',').Append(Csv(s.acceleration.y)).Append(',').Append(Csv(s.acceleration.z)).Append(',')
                    .Append(Csv(s.speedKph)).Append(',').Append(Csv(s.forwardSpeedKph)).Append(',').Append(Csv(s.lateralSpeedMps)).Append(',')
                    .Append(Csv(s.yawRateRadPerSec)).Append(',').Append(Csv(s.lateralAccelerationMps2)).Append(',').Append(Csv(s.slipAngleDegrees)).Append(',')
                    .Append(s.slipAngleValid ? "1" : "0").Append(',').Append(s.airborne ? "1" : "0").Append(',').Append(Csv(s.groundedWheels)).Append(',')
                    .Append(Csv(s.engineRpm)).Append(',').Append(Csv(s.engineTorque)).Append(',').Append(Csv(s.drivetrainTorque)).Append(',').Append(Csv(s.gear)).Append(',')
                    .Append(s.shifting ? "1" : "0").Append(',').Append(s.nitrousActive ? "1" : "0").Append(',').Append(Csv(s.nitrousSeconds)).Append(',')
                    .Append(Csv(s.rawInput.Steering)).Append(',').Append(Csv(s.rawInput.Throttle)).Append(',').Append(Csv(s.rawInput.Brake)).Append(',')
                    .Append(s.rawInput.Handbrake ? "1" : "0").Append(',').Append(s.rawInput.Nitrous ? "1" : "0").Append(',')
                    .Append(Csv(s.finalInput.Steering)).Append(',').Append(Csv(s.finalInput.Throttle)).Append(',').Append(Csv(s.finalInput.Brake)).Append(',')
                    .Append(s.finalInput.Handbrake ? "1" : "0").Append(',').Append(s.finalInput.Nitrous ? "1" : "0").Append(',')
                    .Append(s.handling).Append(',').Append(Csv(s.assistYawTorque)).Append(',').Append(Csv(s.averageLongitudinalSlip)).AppendLine();
            }
            return builder.ToString();
        }

        public static void ValidateReport(VehiclePhysicsLabRunReport report)
        {
            if (report == null || report.schema != VehiclePhysicsLabRunReport.CurrentSchema
                || !Guid.TryParseExact(report.runId, "N", out _)
                || report.samples == null || report.metrics == null || report.diagnostics == null || report.collisions == null)
            {
                throw new ArgumentException("PHYSICS_LAB_REPORT: schema, identity and bounded collections are required.");
            }
            if (report.samples.Length > 100000 || report.collisions.Length > 10000)
            {
                throw new ArgumentException("PHYSICS_LAB_REPORT_BUDGET: imported report exceeds the bounded sample/event budget.");
            }
            if (report.appliedOverrides == null || report.appliedOverrides.Length > 128)
            {
                throw new ArgumentException("PHYSICS_LAB_REPORT_OVERRIDES: report override provenance exceeds the bounded budget.");
            }
            for (int i = 0; i < report.appliedOverrides.Length; i++)
            {
                VehiclePhysicsLabTuningOverride item = report.appliedOverrides[i];
                if (item.enabled && (float.IsNaN(item.value) || float.IsInfinity(item.value)))
                    throw new ArgumentException("PHYSICS_LAB_REPORT_OVERRIDES: override " + i + " is non-finite.");
            }
            float previousTime = -1f;
            for (int i = 0; i < report.samples.Length; i++)
            {
                VehiclePhysicsLabSample sample = report.samples[i];
                if (!Finite(sample.time) || !Finite(sample.measurementTime) || sample.time < previousTime || !Finite(sample.position)
                    || !Finite(sample.rotation) || !Finite(sample.velocity) || !Finite(sample.acceleration) || !Finite(sample.speedKph)
                    || !Finite(sample.lateralAccelerationMps2) || sample.wheels == null)
                {
                    throw new ArgumentException("PHYSICS_LAB_REPORT_SAMPLE: sample " + i + " contains invalid or non-finite data.");
                }
                previousTime = sample.time;
            }
        }

        public static bool TryBakeCapability(
            RacingCapabilityProfile profile,
            VehiclePhysicsLabRunReport report,
            out string failure)
        {
            failure = string.Empty;
            try { ValidateReport(report); }
            catch (ArgumentException exception) { failure = exception.Message; return false; }
            if (profile == null)
            {
                failure = "PHYSICS_LAB_CAPABILITY: assign a Racing Capability Profile first.";
                return false;
            }
            if (report.samples.Length < 8)
            {
                failure = "PHYSICS_LAB_CAPABILITY: capture at least eight samples before baking a profile.";
                return false;
            }

            float maximumSpeed = 0f;
            bool hasLateral = false;
            for (int i = 0; i < report.samples.Length; i++)
            {
                maximumSpeed = Mathf.Max(maximumSpeed, report.samples[i].speedKph / 3.6f);
                hasLateral |= Mathf.Abs(report.samples[i].lateralAccelerationMps2) > 0.1f;
            }
            maximumSpeed = Mathf.Min(150f, Mathf.Max(5f, maximumSpeed));
            int pointCount = profile.points == null || profile.points.Length < 2 ? 6 : Mathf.Clamp(profile.points.Length, 2, 32);
            var points = new RacingCapabilityPoint[pointCount];
            int longitudinalBins = 0;
            int lateralBins = 0;
            for (int point = 0; point < pointCount; point++)
            {
                float speed = maximumSpeed * point / Mathf.Max(1f, pointCount - 1f);
                float radius = Mathf.Max(2f, maximumSpeed / Mathf.Max(2f, pointCount - 1f));
                var acceleration = new List<float>();
                var braking = new List<float>();
                var lateral = new List<float>();
                for (int i = 0; i < report.samples.Length; i++)
                {
                    VehiclePhysicsLabSample sample = report.samples[i];
                    float sampleSpeed = sample.speedKph / 3.6f;
                    if (Mathf.Abs(sampleSpeed - speed) > radius) continue;
                    float longitudinalAcceleration = Vector3.Dot(sample.acceleration, sample.rotation * Vector3.forward);
                    if (!sample.shifting && longitudinalAcceleration > 0.05f) acceleration.Add(longitudinalAcceleration);
                    if (!sample.shifting && longitudinalAcceleration < -0.05f) braking.Add(-longitudinalAcceleration);
                    if (Mathf.Abs(sample.lateralAccelerationMps2) > 0.1f) lateral.Add(Mathf.Abs(sample.lateralAccelerationMps2));
                }
                if (acceleration.Count > 0) longitudinalBins++;
                if (lateral.Count > 0) lateralBins++;
                float fallbackAcceleration = Existing(profile.points, point, true, 0.1f);
                float fallbackBraking = Existing(profile.points, point, false, 0.1f);
                points[point] = new RacingCapabilityPoint
                {
                    speed = speed,
                    acceleration = QuantileOr(acceleration, fallbackAcceleration, 0.25f),
                    braking = QuantileOr(braking, fallbackBraking, 0.25f),
                    lateralAcceleration = QuantileOr(lateral, ExistingLateral(profile.points, point), 0.5f)
                };
            }

            if (longitudinalBins < Mathf.Min(2, pointCount))
            {
                failure = "PHYSICS_LAB_CAPABILITY_COVERAGE: the report does not contain enough positive longitudinal acceleration bins; no asset was changed.";
                return false;
            }

            Undo.RecordObject(profile, "Bake physics lab capability profile");
            profile.points = points;
            profile.vehicleFingerprint = report.vehicleFingerprint;
            profile.longitudinalMeasured = longitudinalBins >= 2;
            profile.lateralMeasured = lateralBins >= 2 && hasLateral;
            profile.physicsLabRunId = report.runId;
            profile.physicsLabFingerprint = report.definitionFingerprint;
            profile.physicsLabEvidence = "Baked from Vehicle Physics Lab run " + report.runId + "; vehicle=" + report.vehicleFingerprint
                + "; longitudinal bins=" + longitudinalBins + "; lateral bins=" + lateralBins
                + "; raw samples=" + report.samples.Length + "; unmeasured channels remain explicit assumptions.";
            EditorUtility.SetDirty(profile);
            return true;
        }

        public static void CreateDemo()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before creating the Vehicle Physics Lab example.");
            EnsureFolder(ExampleFolder);
            VehiclePhysicsLabTrack track = AssetDatabase.LoadAssetAtPath<VehiclePhysicsLabTrack>(ExampleTrackPath);
            VehiclePhysicsLabDefinition definition = AssetDatabase.LoadAssetAtPath<VehiclePhysicsLabDefinition>(ExampleDefinitionPath);
            if (track != null && definition != null)
            {
                Selection.activeObject = definition;
                return;
            }

            RacingVehicleSetup setup = FindFirstSetup();
            if (setup == null)
            {
                VehicleTuning tuning = VehicleTuning.CreateStreetRacer();
                tuning.name = "Physics Lab Tuning";
                AssetDatabase.CreateAsset(tuning, AssetDatabase.GenerateUniqueAssetPath(ExampleFolder + "/PhysicsLabTuning.asset"));
                setup = ScriptableObject.CreateInstance<RacingVehicleSetup>();
                setup.name = "Physics Lab Vehicle Setup";
                setup.tuning = tuning;
                AssetDatabase.CreateAsset(setup, AssetDatabase.GenerateUniqueAssetPath(ExampleFolder + "/PhysicsLabVehicle.asset"));
            }

            if (track == null)
            {
                track = ScriptableObject.CreateInstance<VehiclePhysicsLabTrack>();
                track.name = "FlatStraight";
                track.displayName = "Production vehicle flat straight fixture";
                track.kind = VehiclePhysicsLabTrackKind.FlatStraight;
                track.length = 240f;
                track.width = 18f;
                AssetDatabase.CreateAsset(track, AssetDatabase.GenerateUniqueAssetPath(ExampleTrackPath));
            }

            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<VehiclePhysicsLabDefinition>();
                definition.name = "StandingLaunch";
                definition.displayName = "Standing launch - shared physics baseline";
                definition.vehicle = setup;
                definition.track = track;
                definition.experiment = VehiclePhysicsLabExperimentKind.StandingLaunch;
                definition.warmupSeconds = 1f;
                definition.safety.maximumSeconds = 20f;
                definition.evaluation.requireTargetSpeed = true;
                definition.evaluation.targetSpeedKph = 100f;
                AssetDatabase.CreateAsset(definition, AssetDatabase.GenerateUniqueAssetPath(ExampleDefinitionPath));
            }

            CreateDemoScene(track);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = definition;
            Debug.Log("PHYSICS_LAB_EXAMPLE_CREATED: " + AssetDatabase.GetAssetPath(definition) + "; open the Vehicle Physics Lab window and run the baseline.");
        }

        private static void CreateDemoScene(VehiclePhysicsLabTrack track)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ExampleScenePath) != null) return;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var anchor = new GameObject("Vehicle Physics Lab Track Anchor");
                SceneManager.MoveGameObjectToScene(anchor, scene);
                anchor.AddComponent<VehiclePhysicsLabTrackAnchor>().track = track;
                var note = new GameObject("Vehicle Physics Lab Usage");
                SceneManager.MoveGameObjectToScene(note, scene);
                note.AddComponent<VehiclePhysicsLabDemoMarker>();
                EditorSceneManager.SaveScene(scene, ExampleScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static RacingVehicleSetup FindFirstSetup()
        {
            string[] guids = AssetDatabase.FindAssets("t:RacingVehicleSetup");
            for (int i = 0; i < guids.Length; i++)
            {
                RacingVehicleSetup setup = AssetDatabase.LoadAssetAtPath<RacingVehicleSetup>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (setup != null && setup.tuning != null) return setup;
            }
            return null;
        }

        private static float QuantileOr(List<float> values, float fallback, float quantile)
        {
            if (values == null || values.Count == 0) return Mathf.Max(0.1f, fallback);
            values.Sort();
            return Mathf.Max(0.1f, values[Mathf.Clamp(Mathf.FloorToInt((values.Count - 1) * quantile), 0, values.Count - 1)]);
        }

        private static float Existing(RacingCapabilityPoint[] points, int index, bool acceleration, float fallback)
        {
            if (points == null || points.Length == 0) return fallback;
            RacingCapabilityPoint point = points[Mathf.Clamp(index, 0, points.Length - 1)];
            return acceleration ? point.acceleration : point.braking;
        }

        private static float ExistingLateral(RacingCapabilityPoint[] points, int index)
        {
            if (points == null || points.Length == 0) return 0.1f;
            return Mathf.Max(0.1f, points[Mathf.Clamp(index, 0, points.Length - 1)].lateralAcceleration);
        }

        private static string Csv(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string Csv(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }

    [DisallowMultipleComponent]
    internal sealed class VehiclePhysicsLabDemoMarker : MonoBehaviour
    {
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.65f, 0.1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 0.5f);
        }
    }
}
