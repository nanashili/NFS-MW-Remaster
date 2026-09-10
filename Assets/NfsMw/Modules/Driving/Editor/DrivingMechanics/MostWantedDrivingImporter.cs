using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.DrivingMechanics
{
    [Serializable]
    public sealed class MostWantedDrivingMapping
    {
        public string recordId, field, destination, interpretation;
        public MostWantedHandlingLocation source;
    }

    [Serializable]
    public sealed class MostWantedDrivingImportReport
    {
        public string version = "mw-driving-import/1";
        public string referenceCommit = "13189413c4c6e447c2225052c66b55d76863b985";
        public string vehicle, vehicleRecordId, baseline;
        public string fidelity = "Parameters from the captured PC GLOBAL packs, evaluated with selected source-informed equations. Executable checks in the research document apply only to the recorded research binary, not automatically to this import. Shared Unity contacts, clutch dynamics and tyre-force amplitudes remain adaptations.";
        public string researchExecutableSha256 = "80774c2e5d619b4f120b48d4462896fd504c263399d203a238769cffde1d253c";
        public bool executableVerifiedForThisImport;
        public List<MostWantedHandlingSource> sources = new List<MostWantedHandlingSource>();
        public List<MostWantedHandlingLink> selectedLinks = new List<MostWantedHandlingLink>();
        public List<MostWantedDrivingMapping> mapped = new List<MostWantedDrivingMapping>();
        public List<string> retainedAdaptations = new List<string>();
        public List<string> unmapped = new List<string>();
    }

    /// <summary>Owns a transient tuning until the caller explicitly saves it as a new asset.</summary>
    public sealed class MostWantedDrivingImport : IDisposable
    {
        public VehicleTuning Tuning { get; internal set; }
        public MostWantedDrivingImportReport Report { get; internal set; }
        public void Dispose()
        {
            if (Tuning != null && !UnityEditor.EditorUtility.IsPersistent(Tuning)) UnityEngine.Object.DestroyImmediate(Tuning);
            Tuning = null;
        }
    }

    public static class MostWantedDrivingImporter
    {
        public static readonly string[] Roles = { "engine", "transmission", "tires", "chassis", "brakes", "nos", "induction" };

        /// <summary>
        /// A preview choice, NOT a claim that slot zero is the active/stock game configuration.
        /// Preserve the actual array index and record key in the resulting evidence.
        /// </summary>
        public static Dictionary<string, string> FirstReferences(MostWantedHandlingVehicle vehicle)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (vehicle.links == null || vehicle.links.Any(link => link == null)) throw new InvalidDataException("Vehicle reference list is malformed.");
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string role in Roles)
            {
                var first = vehicle.links.Where(link => link.fieldName == role && link.status == "resolved")
                    .OrderBy(link => link.index).FirstOrDefault();
                if (first != null) result.Add(role, first.targetRecordId);
            }
            return result;
        }

        public static MostWantedDrivingImport Create(MostWantedHandlingReport source, MostWantedHandlingVehicle vehicle,
            IReadOnlyDictionary<string, string> choices, VehicleTuning baseline = null)
        {
            if (source == null || vehicle == null || choices == null) throw new ArgumentNullException("An decoding, vehicle and explicit record choices are required.");
            ValidateCaptureShape(source);
            if (!source.vehicles.Any(item => item.recordId == vehicle.recordId)) throw new InvalidDataException("Vehicle does not belong to this evidence report.");
            if (source.sources.Count == 0 || source.sources.Any(item => !item.unchanged || item.sha256 != item.afterSha256))
                throw new InvalidDataException("Source snapshot hashes must be verified unchanged before mapping.");
            VehicleTuning tuning = baseline != null ? baseline.CreateRuntimeCopy() : VehicleTuning.CreateStreetRacer();
            tuning.hideFlags = HideFlags.DontSave;
            tuning.name = vehicle.name + " Most Wanted reference";
            var report = new MostWantedDrivingImportReport { vehicle = vehicle.name, vehicleRecordId = vehicle.recordId,
                baseline = baseline != null ? baseline.name : "Explicit generic Unity contact/geometry baseline", sources = source.sources.ToList() };
            var imported = new MostWantedDrivingImport { Tuning = tuning, Report = report };
            try
            {
                var selected = new Dictionary<string, MostWantedHandlingRecord>(StringComparer.Ordinal);
                foreach (string role in Roles)
                {
                    if (!choices.TryGetValue(role, out string id)) throw new InvalidDataException("Select a resolved " + role + " reference; no fallback is invented.");
                    var link = vehicle.links.FirstOrDefault(item => item.fieldName == role && item.targetRecordId == id && item.status == "resolved");
                    if (link == null) throw new InvalidDataException("Selected " + role + " record is not linked by this vehicle.");
                    var record = source.records.SingleOrDefault(item => item.id == id);
                    if (record == null || record.className != role || !record.inheritanceResolved)
                        throw new InvalidDataException("Selected " + role + " record or its inheritance is unresolved.");
                    selected.Add(role, record); report.selectedLinks.Add(link);
                }
                var vehicleRecord = source.records.Single(item => item.id == vehicle.recordId);
                if (!vehicleRecord.inheritanceResolved) throw new InvalidDataException("Vehicle inheritance is unresolved.");
                var context = new MappingContext(report);
                var engine = selected["engine"]; var transmission = selected["transmission"];
                var tires = selected["tires"]; var chassis = selected["chassis"]; var brakes = selected["brakes"];
                var nos = selected["nos"]; var induction = selected["induction"];
                tuning.simulationModel = VehicleSimulationModel.MostWantedReference;
                tuning.mostWanted = new MostWantedDrivingSettings();
                var e = tuning.engine; var m = tuning.mostWanted;

                tuning.chassis.mass = context.Scalar(vehicleRecord, "MASS", "chassis.mass", "Source mass, kg");
                e.idleRpm = context.Scalar(engine, "IDLE", "engine.idleRpm", "RPM");
                e.redlineRpm = context.Scalar(engine, "RED_LINE", "engine.redlineRpm", "Limiter RPM, not table endpoint");
                m.torqueTableMaximumRpm = context.Scalar(engine, "MAX_RPM", "mostWanted.torqueTableMaximumRpm", "Torque-table endpoint RPM");
                float[] rawTorque = context.Floats(engine, "TORQUE", "mostWanted.normalizedTorque / engine.maxTorqueNewtonMeters", "Linear IDLE..MAX_RPM samples, ft·lbf × 1.3558 = N·m; evaluation clamps to RED_LINE");
                if (rawTorque.Length < 2 || rawTorque.Any(value => value < 0f) || rawTorque.Max() <= 0f)
                    throw new InvalidDataException("TORQUE must contain at least two nonnegative samples with a positive peak.");
                float maximum = rawTorque.Max();
                e.maxTorqueNewtonMeters = maximum * MostWantedVehicleMath.FootPoundsToNewtonMeters;
                m.normalizedTorque = rawTorque.Select(value => value / maximum).ToArray();
                e.peakTorqueRpm = Mathf.Min(e.redlineRpm, Mathf.Lerp(e.idleRpm, m.torqueTableMaximumRpm,
                    Array.IndexOf(rawTorque, maximum) / (float)(rawTorque.Length - 1)));
                m.engineBraking = context.Floats(engine, "ENGINE_BRAKING", "mostWanted.engineBraking", "Fractions of full engine torque, on IDLE..MAX_RPM domain");
                e.engineInertia = MostWantedVehicleMath.LoadedEngineInertia(context.Scalar(engine, "FLYWHEEL_MASS", "engine.engineInertia", "Loaded inertia = source × 0.025 + 0.25; Unity RPM transient remains adapted"));

                float[] gears = context.Floats(transmission, "GEAR_RATIO", "engine.reverseRatio / engine.gearRatios", "Index 0 reverse, 1 neutral, 2+ forward; negate reverse for Unity");
                float[] efficiencies = context.Floats(transmission, "GEAR_EFFICIENCY", "mostWanted.gearEfficiency / reverseGearEfficiency", "Use corresponding source gear indexes; unused trailing entries retained in raw evidence");
                if (gears.Length < 3 || gears[0] <= 0f || gears[1] != 0f || efficiencies.Length < gears.Length)
                    throw new InvalidDataException("GEAR_RATIO must begin with positive reverse and zero neutral, and efficiencies must cover all ratios.");
                e.reverseRatio = -gears[0]; e.gearRatios = gears.Skip(2).ToArray();
                m.reverseGearEfficiency = efficiencies[0]; m.gearEfficiency = efficiencies.Skip(2).Take(e.gearRatios.Length).ToArray();
                e.finalDrive = context.Scalar(transmission, "FINAL_GEAR", "engine.finalDrive", "Dimensionless final-drive ratio");
                e.shiftDuration = context.Scalar(transmission, "SHIFT_SPEED", "engine.shiftDuration", "Shift time scale: multiply by the newly selected ratio; downshifts take one quarter; clutch shape remains adapted");
                e.drivelineEfficiency = 1f; e.clutchEngagement = 1f;
                e.shiftUpRpm = Mathf.Max(e.idleRpm, e.redlineRpm - 100f);
                e.shiftDownRpm = Mathf.Lerp(e.idleRpm, e.redlineRpm, .25f);
                float frontShare = context.Scalar(transmission, "TORQUE_SPLIT", "driveLayout / awdFrontTorqueBias", "Front torque fraction: 0=RWD, 1=FWD");
                if (frontShare < 0f || frontShare > 1f) throw new InvalidDataException("TORQUE_SPLIT is outside [0,1].");
                tuning.awdFrontTorqueBias = frontShare;
                tuning.driveLayout = frontShare == 0f ? VehicleDriveLayout.Rwd : frontShare == 1f ? VehicleDriveLayout.Fwd : VehicleDriveLayout.Awd;
                tuning.differential = VehicleDifferentialMode.Open; // Do not misinterpret the three source DIFFERENTIAL floats as an enum.
                tuning.differentialLockStrength = 0f; tuning.differentialPreload = 0f;

                Vector2 rims = context.Pair(tires, "RIM_SIZE", "tires.wheelRadius / mostWanted.rearWheelRadiusScale", "Rim diameter in inches");
                Vector2 widths = context.Pair(tires, "SECTION_WIDTH", "tires.wheelRadius / wheelWidth", "Tyre section width in millimetres");
                Vector2 aspects = context.Pair(tires, "ASPECT_RATIO", "tires.wheelRadius / mostWanted.rearWheelRadiusScale", "Sidewall aspect percentage");
                float frontRadius = MostWantedVehicleMath.WheelRadius(rims.x, widths.x, aspects.x);
                float rearRadius = MostWantedVehicleMath.WheelRadius(rims.y, widths.y, aspects.y);
                if (frontRadius < .05f || rearRadius < .05f) throw new InvalidDataException("Tyre geometry produces an invalid radius.");
                tuning.tires.wheelRadius = frontRadius; tuning.tires.wheelWidth = widths.x * .001f; m.rearWheelRadiusScale = rearRadius / frontRadius;
                m.steeringCoefficient = context.Scalar(tires, "STEERING", "mostWanted.steeringCoefficient", "Multiplier on recovered degree range and steering rate");
                tuning.controls.maxSteerAngle = MostWantedSteeringModel.AbsoluteMaximumDegrees;

                Vector2 springs = context.Pair(chassis, "SPRING_STIFFNESS", "tires.springRate / mostWanted.rearSpringScale", "Source lb/in × 175.1268; linear spring retained in the Unity contact adapter");
                Vector2 dampers = context.Pair(chassis, "SHOCK_STIFFNESS", "tires.damperRate / mostWanted.rearDamperScale", "Source conversion × 175.1268; compression damping");
                Vector2 rebounds = context.Pair(chassis, "SHOCK_EXT_STIFFNESS", "mostWanted.frontReboundDamperScale / rearReboundDamperScale", "Source conversion × 175.1268; rebound damping");
                if (springs.x <= 0f || springs.y <= 0f || dampers.x <= 0f || dampers.y <= 0f)
                    throw new InvalidDataException("Positive front/rear springs and dampers are required by this contact adapter.");
                tuning.tires.springRate = springs.x * MostWantedVehicleMath.PoundsPerInchToNewtonsPerMeter;
                m.rearSpringScale = springs.y / springs.x;
                tuning.tires.damperRate = dampers.x * MostWantedVehicleMath.PoundsPerInchToNewtonsPerMeter;
                m.rearDamperScale = dampers.y / dampers.x;
                m.frontReboundDamperScale = rebounds.x / dampers.x; m.rearReboundDamperScale = rebounds.y / dampers.x;

                float brakeConversion = MostWantedVehicleMath.FootPoundsToNewtonMeters * 4f;
                Vector2 service = context.Pair(brakes, "BRAKES", "controls.serviceBrakeTorque / frontBrakeBias", "Per-wheel ft·lbf × 1.3558 × recovered BrakingTorque 4; Unity field is front+rear per-wheel capacities, not four-wheel total");
                if (service.x < 0f || service.y < 0f || service.x + service.y <= 0f) throw new InvalidDataException("BRAKES must have a positive total capacity.");
                tuning.controls.serviceBrakeTorque = (service.x + service.y) * brakeConversion;
                tuning.controls.frontBrakeBias = service.x / (service.x + service.y);
                tuning.controls.handbrakeTorque = context.Scalar(brakes, "EBRAKE", "controls.handbrakeTorque", "ft·lbf × 1.3558 × recovered EBrakingTorque 10") * MostWantedVehicleMath.FootPoundsToNewtonMeters * 10f;

                // Map the original drag constant into the common SI drag product; do not label it a directly recovered Cd.
                float drag = context.Scalar(chassis, "DRAG_COEFFICIENT", "aero.dragCoefficient", "Set Cd so 0.5 × density × Cd × area equals source coefficient; reference mode adds off-throttle drag");
                float divisor = .5f * tuning.aero.airDensity * tuning.aero.frontalArea;
                if (divisor <= 0f) throw new InvalidDataException("Baseline air density and frontal area must be positive.");
                tuning.aero.dragCoefficient = drag / divisor;
                m.linearDownforceCoefficient = context.Scalar(chassis, "AERO_COEFFICIENT", "mostWanted.linearDownforceCoefficient", "Multiply source by 2000; force scales with speed, orientation and ground contact") * 2000f;
                tuning.aero.downforceBalance = context.Scalar(chassis, "AERO_CG", "aero.downforceBalance", "Percentage of distance from rear to front axle") * .01f;
                tuning.aero.downforceCoefficient = 0f; tuning.chassis.rollingResistance = 0f;
                tuning.speedGovernor = false;

                m.inductionSpoolRpmFraction = context.Scalar(induction, "SPOOL", "mostWanted.inductionSpoolRpmFraction", "Normalized IDLE..RED_LINE spool threshold");
                m.inductionLowBoost = context.Scalar(induction, "LOW_BOOST", "mostWanted.inductionLowBoost", "Dimensionless torque boost fraction");
                m.inductionHighBoost = context.Scalar(induction, "HIGH_BOOST", "mostWanted.inductionHighBoost", "Dimensionless torque boost fraction");
                m.inductionVacuum = context.Scalar(induction, "VACUUM", "mostWanted.inductionVacuum", "Below-threshold boost fraction");
                e.boostSpoolSeconds = context.Scalar(induction, "SPOOL_TIME_UP", "engine.boostSpoolSeconds", "Seconds to traverse normalized spool range");
                m.inductionSpoolDownSeconds = context.Scalar(induction, "SPOOL_TIME_DOWN", "mostWanted.inductionSpoolDownSeconds", "Seconds to traverse normalized spool range");
                e.forcedInduction = m.inductionLowBoost > 0f || m.inductionHighBoost > 0f;
                e.boostTorqueMultiplier = 1f + Mathf.Max(m.inductionLowBoost, m.inductionHighBoost);

                e.nitrousFuelSeconds = context.Scalar(nos, "NOS_CAPACITY", "engine.nitrousFuelSeconds", "Full-tank discharge duration in seconds");
                e.nitrousTorque = 0f;
                m.nitrousTorqueBoost = context.Scalar(nos, "TORQUE_BOOST", "mostWanted.nitrousTorqueBoost", "Multiply engine torque by 1+boost; not additive N·m");
                m.nitrousDisengageSeconds = context.Scalar(nos, "NOS_DISENGAGE", "mostWanted.nitrousDisengageSeconds", "Delay before recharge");
                m.nitrousRechargeMinimumSeconds = context.Scalar(nos, "RECHARGE_MIN", "mostWanted.nitrousRechargeMinimumSeconds", "Full recharge duration at minimum speed, not a fuel rate");
                m.nitrousRechargeMaximumSeconds = context.Scalar(nos, "RECHARGE_MAX", "mostWanted.nitrousRechargeMaximumSeconds", "Full recharge duration at maximum speed");
                m.nitrousRechargeMinimumKph = context.Scalar(nos, "RECHARGE_MIN_SPEED", "mostWanted.nitrousRechargeMinimumKph", "Reference mph × 0.44703001 × 3.6") * MostWantedVehicleMath.ReferenceMphToKph;
                m.nitrousRechargeMaximumKph = context.Scalar(nos, "RECHARGE_MAX_SPEED", "mostWanted.nitrousRechargeMaximumKph", "Reference mph × 0.44703001 × 3.6") * MostWantedVehicleMath.ReferenceMphToKph;

                tuning.assists.stabilityControl = tuning.assists.countersteering = tuning.assists.tractionControl = tuning.assists.abs = false;
                tuning.handling.brakeToDrift = false; tuning.handling.driftBias = 0f;
                report.retainedAdaptations.AddRange(new[] {
                    "Baseline rig, center of mass, inertia tensor, suspension rest length/travel and wheel mass are retained; source body/axle coordinates need model-space alignment.",
                    "Longitudinal and lateral tyre forces use the shared Unity combined-slip solver. Source STATIC_GRIP, DYNAMIC_GRIP, GRIP_SCALE, nitrous-to-traction coupling, yaw/drift/burnout logic and protected PC lateral amplitude are not reproduced or silently treated as Unity friction coefficients.",
                    "Unity clutch/RPM transient, rev limiter, throttle/brake smoothing, collision solver, differential and shift control remain adaptations. Recovered torque-crossover shift selection corrects the public reconstruction's ambiguous induction arguments.",
                    "Suspension has mapped linear stiffness and compression/rebound damping, but not original progressive springs, digressive valving, sway bars, bottom-out impulses or dynamic center of gravity.",
                    "Source steering tables/history and simplified Ackermann are active. The second remap follows source constants; active PC selection remains unverified. Countersteer uses mean Unity rear slip, not the original selected-wheel feedback. Speedbreaker, steering-wheel-device variants, collision/wall steering and AI catch-up cheats are not implemented.",
                    "Legacy brake-to-drift, yaw assist, ABS and traction-control overlays are disabled to avoid presenting those authored systems as recovered mechanics.",
                    "Chosen reference slots are explicit data selections, not proof of active garage upgrades. Upgrade interpolation/Junkman behavior is not applied." });
                foreach (var record in selected.Values)
                    foreach (var field in record.fields)
                        if (!report.mapped.Any(map => map.recordId == record.id && map.field == field.name))
                            report.unmapped.Add(record.className + "/" + record.rowKey + "/" + field.name + " (" + field.status + ")");
                RacingLineSnapshot.ValidateTuning(tuning);
                return imported;
            }
            catch { imported.Dispose(); throw; }
        }

        public static void ValidateCaptureShape(MostWantedHandlingReport source)
        {
            if (source == null || source.schema != 1 || source.vehicles == null || source.records == null || source.sources == null || source.issues == null)
                throw new InvalidDataException("Not a supported Most Wanted handling capture.");
            if (source.sources.Count == 0 || source.sources.Count > 256 || source.vehicles.Count > 20000 || source.records.Count > 20000)
                throw new InvalidDataException("Source hashes or record count are outside the supported evidence budget.");
            if (source.sources.Any(item => item == null || string.IsNullOrWhiteSpace(item.relativePath) || item.byteLength < 0
                || !Hash(item.sha256) || !Hash(item.afterSha256)) || source.issues.Any(issue => issue == null))
                throw new InvalidDataException("Source identity or issue list is malformed.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in source.records)
            {
                if (record == null || string.IsNullOrEmpty(record.id) || !ids.Add(record.id) || record.fields == null || record.links == null
                    || record.links.Any(link => link == null) || record.fields.Any(field => field == null || field.values == null
                        || field.values.Any(value => value == null || value.components == null || value.components.Any(component => component == null))))
                    throw new InvalidDataException("Handling record IDs must be unique and field/value collections non-null.");
            }
            var vehicles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var vehicle in source.vehicles)
                if (vehicle == null || string.IsNullOrEmpty(vehicle.recordId) || !vehicles.Add(vehicle.recordId) || !ids.Contains(vehicle.recordId)
                    || vehicle.links == null || vehicle.links.Any(link => link == null))
                    throw new InvalidDataException("Vehicle identity or reference list is malformed.");
        }

        private static bool Hash(string value) => value != null && value.Length == 64
            && value.All(character => character >= '0' && character <= '9' || character >= 'a' && character <= 'f' || character >= 'A' && character <= 'F');

        private sealed class MappingContext
        {
            private readonly MostWantedDrivingImportReport report;
            public MappingContext(MostWantedDrivingImportReport report) { this.report = report; }
            private MostWantedHandlingField Field(MostWantedHandlingRecord record, string name, string target, string treatment)
            {
                var field = record.fields.SingleOrDefault(item => item.name == name);
                if (field == null || field.status != "decoded" || field.values.Any(value => value.status != "decoded"))
                    throw new InvalidDataException(record.className + "." + name + " is absent or unsupported; no guessed default was applied.");
                report.mapped.Add(new MostWantedDrivingMapping { recordId = record.id, field = name, destination = target, interpretation = treatment, source = field.source });
                return field;
            }
            public float[] Floats(MostWantedHandlingRecord record, string name, string target, string treatment)
            {
                var field = Field(record, name, target, treatment);
                if (field.values.Any(value => value.kind != "float32" || !double.IsFinite(value.numericValue) || Math.Abs(value.numericValue) > 1e7))
                    throw new InvalidDataException(record.className + "." + name + " must contain finite float32 values.");
                return field.values.Select(value => (float)value.numericValue).ToArray();
            }
            public float Scalar(MostWantedHandlingRecord record, string name, string target, string treatment)
            {
                var values = Floats(record, name, target, treatment);
                if (values.Length != 1) throw new InvalidDataException(record.className + "." + name + " must contain one value.");
                return values[0];
            }
            public Vector2 Pair(MostWantedHandlingRecord record, string name, string target, string treatment)
            {
                var field = Field(record, name, target, treatment);
                if (field.values.Count != 1 || field.values[0].kind != "axle-pair") throw new InvalidDataException(name + " must be an AxlePair.");
                var components = field.values[0].components;
                var front = components.SingleOrDefault(item => item.name == "Front"); var rear = components.SingleOrDefault(item => item.name == "Rear");
                if (front == null || rear == null || !front.finite || !rear.finite) throw new InvalidDataException(name + " has invalid axle values.");
                return new Vector2((float)front.numericValue, (float)rear.numericValue);
            }
        }
    }
}
