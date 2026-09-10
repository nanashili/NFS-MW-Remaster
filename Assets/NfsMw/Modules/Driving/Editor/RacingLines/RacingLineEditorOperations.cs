using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    public static class RacingLineEditorOperations
    {
        public static RacingLineSnapshot Capture(RacingLineSource source)
        {
            var snapshot = RacingLineSnapshot.Capture(source);
            foreach (var authoring in UnityEngine.Object.FindObjectsByType<RoadNetworkAuthoring>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (authoring.Baked == source.route.network && !RoadNetworkBake.IsCurrent(authoring))
                    throw new ArgumentException("LINE_ROAD_STALE: the loaded road authoring differs from its publication. Publish it in Road Network Editor first.");
            return snapshot;
        }
        public static void ValidateUniqueDocument(RacingLineSource source)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:RacingLineSource"))
            {
                var other = AssetDatabase.LoadAssetAtPath<RacingLineSource>(AssetDatabase.GUIDToAssetPath(guid));
                if (other != source && other.id == source.id)
                    throw new ArgumentException("LINE_DUPLICATE_ID: use Studio > Duplicate with new IDs; a raw duplicated document shares content identity.");
            }
        }
        public static RacingLineArtifact Publish(RacingLineSource source, RacingLineCandidate candidate, RacingVerificationReport report, string requestedPath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new ArgumentException("Exit Play mode before publishing.");
            var snapshot = Capture(source); ValidateUniqueDocument(source);
            if (string.IsNullOrEmpty(requestedPath) || !requestedPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !requestedPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Publish inside Assets using an .asset path.");
            var artifact = ScriptableObject.CreateInstance<RacingLineArtifact>(); string created = null;
            try
            {
                artifact.Initialize(snapshot, candidate, report);
                string path = AssetDatabase.GenerateUniqueAssetPath(requestedPath);
                AssetDatabase.CreateAsset(artifact, path); created = path; AssetDatabase.SaveAssetIfDirty(artifact);
                if (!artifact.TryOpen(snapshot.Fingerprint, out _, out string failure)) throw new ArgumentException(failure);
                Undo.RecordObject(source, "Publish verified racing line"); source.published = artifact; EditorUtility.SetDirty(source);
                // Source reference participates in Undo. A previous immutable revision is never deleted.
                return artifact;
            }
            catch
            {
                if (created != null) AssetDatabase.DeleteAsset(created); else UnityEngine.Object.DestroyImmediate(artifact);
                throw;
            }
        }
        public static RacingLineSource Duplicate(RacingLineSource source)
        {
            var copy = UnityEngine.Object.Instantiate(source); copy.name = source.name + " Copy";
            copy.id = Guid.NewGuid().ToString("N"); copy.published = null;
            foreach (var hint in copy.hints) hint.id = Guid.NewGuid().ToString("N");
            foreach (var exclusion in copy.exclusions) exclusion.id = Guid.NewGuid().ToString("N");
            return copy;
        }
        public static void MigrateV1(RacingLineSource source)
        {
            if (source == null || source.schema != 1) throw new ArgumentException("Only document schema 1 can be migrated to schema 2.");
            Undo.RecordObject(source, "Migrate racing line schema 1 to 2");
            source.verification ??= new RacingVerificationSettings(); source.hints ??= Array.Empty<RacingLineHint>();
            source.exclusions ??= Array.Empty<RacingLineExclusion>(); source.schema = RacingLineSource.CurrentSchema;
            EditorUtility.SetDirty(source);
        }
        public static void AddHint(RacingLineSource source, RacingHintKind kind, float station, float lateral = 0)
        {
            var snapshot = Capture(source); Undo.RecordObject(source, "Add racing line " + kind);
            var hints = new List<RacingLineHint>(source.hints) { new RacingLineHint { kind = kind,
                station = Mathf.Clamp(station, 0, snapshot.Length), lateral = lateral, radius = Mathf.Min(10, snapshot.Length) } };
            source.hints = hints.ToArray(); EditorUtility.SetDirty(source);
        }
        public static void MoveHint(RacingLineSource source, string id, float lateral)
        {
            var hint = Array.Find(source.hints, h => h.id == id);
            if (hint == null || !RacingLineSnapshot.Finite(lateral)) throw new ArgumentException("The selected hint no longer exists or has an invalid offset.");
            Undo.RecordObject(source, "Move racing line hint"); hint.lateral = lateral; EditorUtility.SetDirty(source);
        }
        public static string TelemetryCsv(RacingVerificationReport report)
        {
            if (report == null) throw new ArgumentException("Run a verification first.");
            var csv = new StringBuilder("fingerprint,candidate,trial,vehicle,time_s,station_m,target_mps,actual_mps,lateral_m,heading_deg,steering,throttle,brake,slip_deg,yaw_radps,assist_Nm,contacts,clearance_m,handling,x,y,z\n");
            foreach (var trial in report.trials)
                foreach (var s in trial.telemetry)
                {
                    csv.Append(report.fingerprint).Append(',').Append(report.candidateId).Append(',').Append(trial.trial).Append(',').Append(s.vehicleIndex);
                    foreach (float value in new[] { s.time, s.station, s.targetSpeed, s.actualSpeed, s.lateralError, s.headingError,
                        s.steering, s.throttle, s.brake, s.slipDegrees, s.yawRate, s.assistTorque, s.contacts, s.clearance })
                        csv.Append(',').Append(value.ToString("R", CultureInfo.InvariantCulture));
                    csv.Append(',').Append(s.handling);
                    foreach (float value in new[] { s.position.x, s.position.y, s.position.z }) csv.Append(',').Append(value.ToString("R", CultureInfo.InvariantCulture));
                    csv.Append('\n');
                }
            return csv.ToString();
        }
        public static void Export(string path, string content)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, content, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static void ValidateImportedReport(RacingVerificationReport report, RacingLineCandidate candidate)
        {
            if (report == null || candidate == null || report.trials == null || report.trials.Length < 1 || report.trials.Length > 25
                || report.verificationRevision != RacingVerificationReport.Revision || report.engineVersion != Application.unityVersion
                || report.controllerRevision != RacingLineTracker.Revision || report.fingerprint != candidate.fingerprint
                || report.trajectoryFingerprint != candidate.GeometryFingerprint() || report.candidateId != candidate.id
                || !double.IsFinite(report.computeMilliseconds) || !float.IsFinite(report.telemetryIntervalSeconds))
                throw new ArgumentException("LINE_REPORT_IMPORT: this report does not match the selected trajectory and verifier revision.");
            int total = 0;
            foreach (var trial in report.trials)
            {
                if (trial == null || trial.telemetry == null || trial.telemetry.Length > 50000 - total)
                    throw new ArgumentException("LINE_REPORT_IMPORT: require non-null trials/traces and at most 50,000 raw samples.");
                total += trial.telemetry.Length;
                foreach (var field in typeof(RacingTrialReport).GetFields())
                    if (field.FieldType == typeof(float) && !float.IsFinite((float)field.GetValue(trial)))
                        throw new ArgumentException("LINE_REPORT_IMPORT: non-finite trial metric " + field.Name + ".");
                foreach (var sample in trial.telemetry)
                {
                    if (sample.vehicleIndex < 0 || sample.vehicleIndex > (report.companionRun ? 1 : 0)
                        || sample.contacts < 0 || sample.contacts > 4 || !RacingLineSnapshot.Finite(sample.position)
                        || !Enum.IsDefined(typeof(VehicleHandlingMode), sample.handling))
                        throw new ArgumentException("LINE_REPORT_IMPORT: invalid telemetry identity, contact count, pose or handling phase.");
                    foreach (float value in new[] { sample.time, sample.station, sample.targetSpeed, sample.actualSpeed, sample.lateralError,
                        sample.headingError, sample.steering, sample.throttle, sample.brake, sample.slipDegrees, sample.yawRate, sample.assistTorque, sample.clearance })
                        if (!float.IsFinite(value)) throw new ArgumentException("LINE_REPORT_IMPORT: non-finite telemetry value.");
                }
            }
        }
    }
}
