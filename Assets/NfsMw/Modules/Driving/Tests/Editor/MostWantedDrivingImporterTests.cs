using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using NfsMwRemaster.Driving.Editor.DrivingMechanics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    /// <summary>
    /// Offline regression tests against the preserved PC capture, never the installation.
    /// Slot zero is an explicit test selection, not a claim about active garage upgrades.
    /// Each test receives a deep-cloned managed evidence graph; asset writes use unique test folders.
    /// </summary>
    [Category("DrivingMechanicsOffline")]
    public sealed class MostWantedDrivingImporterTests
    {
        private const string CaptureRelativePath = "Tools/DrivingMechanics/Evidence/handling-local-20260908.json";
        private const string BaselineRelativePath = "Tools/VehicleFramework/Evidence/Baseline/reference-data.json";
        private const string CaptureSha256 = "47bdd24fb8ef208c0ec41f920944d388588603b404a6855b5313be8791984614";
        private const string BaselineSha256 = "17a9d3a9b68387e2fd6bb3a7c9bcaa76ee5fe4595c4b2b557126471c2bbc3ccc";
        private const float TorqueUnit = 1.3558f;
        private const float SpringUnit = 175.1268f;
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
            { TypeNameHandling = TypeNameHandling.None, MaxDepth = 64 };

        private MostWantedHandlingReport original, source;
        private string originalJson, capturePath, baselinePath, scratchFolder, assetFolder;

        [OneTimeSetUp]
        public void LoadPreservedCapture()
        {
            // Isolated test projects may point at the original project's read-only fixture files.
            string root = Environment.GetEnvironmentVariable("MOST_WANTED_DRIVING_TEST_ROOT");
            if (string.IsNullOrWhiteSpace(root)) root = Directory.GetParent(Application.dataPath).FullName;
            capturePath = Path.Combine(root, CaptureRelativePath);
            baselinePath = Path.Combine(root, BaselineRelativePath);
            Assert.That(File.Exists(capturePath), Is.True, "Copy the Tools evidence fixtures or set MOST_WANTED_DRIVING_TEST_ROOT.");
            Assert.That(File.Exists(baselinePath), Is.True, "The historical baseline must remain available for the immutability check.");
            Assert.That(HashFile(capturePath), Is.EqualTo(CaptureSha256));
            Assert.That(HashFile(baselinePath), Is.EqualTo(BaselineSha256));
            original = MostWantedDrivingWindow.ReadReport(capturePath);
            originalJson = JsonConvert.SerializeObject(original, JsonSettings);
        }

        [SetUp]
        public void DeepCloneEvidenceForEveryTest()
        {
            source = JsonConvert.DeserializeObject<MostWantedHandlingReport>(originalJson, JsonSettings);
            Assert.That(source, Is.Not.SameAs(original));
            Assert.That(source.records[0].fields, Is.Not.SameAs(original.records[0].fields));
        }

        [TearDown]
        public void RemoveOnlyTestOwnedFilesAndCheckEvidenceImmutability()
        {
            try
            {
                if (assetFolder != null)
                {
                    Assert.That(assetFolder, Does.StartWith("Assets/__MostWantedImporterTests_"));
                    Assert.That(AssetDatabase.DeleteAsset(assetFolder), Is.True, "Test-owned asset folder cleanup failed.");
                }
            }
            finally
            {
                assetFolder = null;
                if (scratchFolder != null && Directory.Exists(scratchFolder)) Directory.Delete(scratchFolder, true);
                scratchFolder = null;
            }
            Assert.That(JsonConvert.SerializeObject(original, JsonSettings), Is.EqualTo(originalJson), "A test mutated the shared evidence graph.");
            Assert.That(HashFile(capturePath), Is.EqualTo(CaptureSha256), "The captured JSON was modified.");
            Assert.That(HashFile(baselinePath), Is.EqualTo(BaselineSha256), "The historical baseline was modified.");
        }

        [Test]
        public void CapturedSnapshotRetainsThreeVerifiedFilesAndExplicitUnsupportedFields()
        {
            Assert.That(source.vehicles.Select(vehicle => vehicle.name), Is.EquivalentTo(new[] { "bmwm3gtre46", "gti", "punto" }));
            Assert.That(source.records.Count, Is.EqualTo(39));
            Assert.That(source.issues.Count, Is.EqualTo(49));
            Assert.That(source.issues.All(issue => issue.code == "unsupported-field"), Is.True);
            Assert.That(source.complete, Is.False, "Unrelated unsupported fields must not disappear to make mapping succeed.");
            Assert.That(source.sources.Select(file => file.relativePath), Is.EquivalentTo(new[]
                { "GLOBAL/attributes.bin", "GLOBAL/FE_ATTRIB.bin", "GLOBAL/gameplay.bin" }));
            foreach (var file in source.sources)
            {
                Assert.That(file.unchanged, Is.True);
                Assert.That(file.sha256, Has.Length.EqualTo(64));
                Assert.That(file.afterSha256, Is.EqualTo(file.sha256));
                Assert.That(file.verification, Is.EqualTo("File SHA-256 before parsing and after decoding"));
            }
        }

        [TestCase("bmwm3gtre46", VehicleDriveLayout.Rwd, 1300f, 8500f, 9500f)]
        [TestCase("gti", VehicleDriveLayout.Fwd, 1328f, 7000f, 8000f)]
        [TestCase("punto", VehicleDriveLayout.Fwd, 1100f, 6500f, 7500f)]
        public void ExplicitSlotZeroChoicesMapEveryCapturedCar(string name, VehicleDriveLayout layout, float mass, float redline, float maximum)
        {
            var vehicle = Car(name); var choices = ChoicesZero(vehicle);
            Assert.That(MostWantedDrivingImporter.FirstReferences(vehicle), Is.EquivalentTo(choices));
            using var imported = MostWantedDrivingImporter.Create(source, vehicle, choices);
            var tuning = imported.Tuning;
            Assert.That(tuning.UsesMostWantedReference, Is.True);
            Assert.That(EditorUtility.IsPersistent(tuning), Is.False);
            Assert.That(tuning.driveLayout, Is.EqualTo(layout));
            Assert.That(tuning.awdFrontTorqueBias, Is.EqualTo(layout == VehicleDriveLayout.Fwd ? 1f : 0f));
            Assert.That(tuning.chassis.mass, Is.EqualTo(mass));
            Assert.That(tuning.engine.idleRpm, Is.EqualTo(800f));
            Assert.That(tuning.engine.redlineRpm, Is.EqualTo(redline));
            Assert.That(tuning.mostWanted.torqueTableMaximumRpm, Is.EqualTo(maximum));
            Assert.That(imported.Report.selectedLinks.Count, Is.EqualTo(7));
            Assert.That(imported.Report.selectedLinks.All(link => link.index == 0), Is.True);
            Assert.That(imported.Report.selectedLinks.Select(link => link.fieldName), Is.EquivalentTo(MostWantedDrivingImporter.Roles));
            Assert.That(imported.Report.retainedAdaptations, Is.Not.Empty);
            Assert.That(imported.Report.unmapped, Is.Not.Empty);
            Assert.DoesNotThrow(() => RacingLineSnapshot.ValidateTuning(tuning));
        }

        [Test]
        public void BmwTorqueUsesIdleToMaximumDomainAndClampsAtRedline()
        {
            using var imported = CreateImport("bmwm3gtre46"); var tuning = imported.Tuning;
            var samples = RawFloats(Selected("bmwm3gtre46", "engine"), "TORQUE");
            Assert.That(samples, Is.EqualTo(new[] { 170f, 251f, 340f, 428f, 467f, 452f, 411f, 375f, 350f }));
            Assert.That(tuning.engine.peakTorqueRpm, Is.EqualTo(5150f));
            Assert.That(tuning.engine.maxTorqueNewtonMeters, Is.EqualTo(633.1586f).Within(.001f));
            Assert.That(MostWantedVehicleMath.Torque(tuning, 5150f), Is.EqualTo(633.1586f).Within(.001f));
            for (int index = 0; index < samples.Length - 1; index++)
            {
                float rpm = 800f + index * ((9500f - 800f) / 8f);
                Assert.That(MostWantedVehicleMath.Torque(tuning, rpm), Is.EqualTo(samples[index] * TorqueUnit).Within(.001f), "Source table index " + index);
            }
            Assert.That(MostWantedVehicleMath.Torque(tuning, 4606.25f), Is.EqualTo((428f + 467f) * .5f * TorqueUnit).Within(.001f), "Linear interpolation, not a shaped AnimationCurve.");
            Assert.That(MostWantedVehicleMath.Torque(tuning, -100f), Is.EqualTo(170f * TorqueUnit).Within(.001f));
            float atLimiter = (375f + (350f - 375f) * ((8500f - 8412.5f) / 1087.5f)) * TorqueUnit;
            Assert.That(MostWantedVehicleMath.Torque(tuning, 8500f), Is.EqualTo(atLimiter).Within(.001f));
            Assert.That(MostWantedVehicleMath.Torque(tuning, 9500f), Is.EqualTo(atLimiter).Within(.001f));
            Assert.That(MostWantedVehicleMath.SampleUniform(tuning.mostWanted.normalizedTorque, 9500f, 800f, 9500f)
                * tuning.engine.maxTorqueNewtonMeters, Is.EqualTo(350f * TorqueUnit).Within(.001f), "MAX_RPM is the stored endpoint, not the limiter.");
            Assert.That(MostWantedVehicleMath.EngineBrakingFraction(tuning, 5150f), Is.EqualTo(.8f).Within(.00001f));
            Assert.That(tuning.engine.engineInertia, Is.EqualTo(.5f).Within(.00001f));
        }

        [TestCase("bmwm3gtre46")]
        [TestCase("gti")]
        [TestCase("punto")]
        public void GearMappingStripsReverseNeutralAndRetainsUnusedEvidence(string name)
        {
            var transmission = Selected(name, "transmission");
            var ratios = RawFloats(transmission, "GEAR_RATIO"); var efficiencies = RawFloats(transmission, "GEAR_EFFICIENCY");
            using var imported = CreateImport(name); var tuning = imported.Tuning;
            Assert.That(ratios.Length, Is.EqualTo(8)); Assert.That(efficiencies.Length, Is.EqualTo(9));
            Assert.That(tuning.engine.reverseRatio, Is.EqualTo(-ratios[0]));
            Assert.That(tuning.engine.gearRatios, Is.EqualTo(ratios.Skip(2).ToArray()));
            Assert.That(tuning.engine.gearRatios.Length, Is.EqualTo(6));
            Assert.That(tuning.mostWanted.gearEfficiency, Is.EqualTo(efficiencies.Skip(2).Take(6).ToArray()));
            Assert.That(tuning.mostWanted.reverseGearEfficiency, Is.EqualTo(efficiencies[0]));
            Assert.That(MostWantedVehicleMath.GearEfficiency(tuning, 0), Is.Zero);
            Assert.That(MostWantedVehicleMath.GearEfficiency(tuning, 7), Is.Zero);
            Assert.That(Field(transmission, "GEAR_EFFICIENCY").values.Count, Is.EqualTo(9), "Do not delete the unused ninth source entry.");
        }

        [Test]
        public void DistinctEfficiencySentinelsDetectOffByTwoIndexing()
        {
            var field = Field(Selected("bmwm3gtre46", "transmission"), "GEAR_EFFICIENCY");
            float[] sentinel = { .11f, .22f, .33f, .44f, .55f, .66f, .77f, .88f, .99f };
            for (int i = 0; i < sentinel.Length; i++) SetFloat(field.values[i], sentinel[i]);
            using var imported = CreateImport("bmwm3gtre46");
            Assert.That(MostWantedVehicleMath.GearEfficiency(imported.Tuning, -1), Is.EqualTo(.11f));
            for (int gear = 1; gear <= 6; gear++)
                Assert.That(MostWantedVehicleMath.GearEfficiency(imported.Tuning, gear), Is.EqualTo(sentinel[gear + 1]));
            Assert.That(imported.Tuning.mostWanted.gearEfficiency.Contains(.99f), Is.False);
            Assert.That(field.values.Last().numericValue, Is.EqualTo((double).99f));
        }

        [TestCase("bmwm3gtre46", .3433f)]
        [TestCase("gti", .32165f)]
        [TestCase("punto", .29225f)]
        public void TiresAndSuspensionKeepPerAxleUnitConversions(string name, float expectedRadius)
        {
            using var imported = CreateImport(name); var tuning = imported.Tuning; var settings = tuning.mostWanted;
            var chassis = Selected(name, "chassis"); var tires = Selected(name, "tires");
            Vector2 springs = RawPair(chassis, "SPRING_STIFFNESS"), dampers = RawPair(chassis, "SHOCK_STIFFNESS"), rebounds = RawPair(chassis, "SHOCK_EXT_STIFFNESS");
            Assert.That(tuning.tires.wheelRadius, Is.EqualTo(expectedRadius).Within(.000001f));
            Assert.That(tuning.tires.wheelWidth, Is.EqualTo(RawPair(tires, "SECTION_WIDTH").x * .001f).Within(.000001f));
            Assert.That(settings.rearWheelRadiusScale, Is.EqualTo(1f).Within(.000001f));
            Assert.That(tuning.tires.springRate, Is.EqualTo(springs.x * SpringUnit).Within(.02f));
            Assert.That(tuning.tires.springRate * settings.rearSpringScale, Is.EqualTo(springs.y * SpringUnit).Within(.02f));
            Assert.That(tuning.tires.damperRate, Is.EqualTo(dampers.x * SpringUnit).Within(.002f));
            Assert.That(tuning.tires.damperRate * settings.rearDamperScale, Is.EqualTo(dampers.y * SpringUnit).Within(.002f));
            Assert.That(tuning.tires.damperRate * settings.frontReboundDamperScale, Is.EqualTo(rebounds.x * SpringUnit).Within(.002f));
            Assert.That(tuning.tires.damperRate * settings.rearReboundDamperScale, Is.EqualTo(rebounds.y * SpringUnit).Within(.002f));
        }

        [Test]
        public void AsymmetricTireFixtureDoesNotDiscardTheRearAxle()
        {
            var tires = Selected("bmwm3gtre46", "tires");
            SetComponent(tires, "RIM_SIZE", "Rear", 20f);
            SetComponent(tires, "SECTION_WIDTH", "Rear", 275f);
            SetComponent(tires, "ASPECT_RATIO", "Rear", 35f);
            using var imported = CreateImport("bmwm3gtre46");
            Assert.That(imported.Tuning.tires.wheelRadius, Is.EqualTo(.3433f).Within(.000001f));
            Assert.That(imported.Tuning.tires.wheelRadius * imported.Tuning.mostWanted.rearWheelRadiusScale,
                Is.EqualTo(.35025f).Within(.000001f));
        }

        [TestCase("bmwm3gtre46", 475f, 600f, 925f)]
        [TestCase("gti", 300f, 400f, 375f)]
        [TestCase("punto", 325f, 450f, 350f)]
        public void BrakeMappingUsesPerWheelAxleCapacitiesNotFourWheelTotals(string name, float front, float rear, float handbrake)
        {
            using var imported = CreateImport(name); var controls = imported.Tuning.controls;
            Assert.That(controls.serviceBrakeTorque, Is.EqualTo((front + rear) * TorqueUnit * 4f).Within(.002f));
            Assert.That(controls.frontBrakeBias, Is.EqualTo(front / (front + rear)).Within(.000001f));
            Assert.That(controls.serviceBrakeTorque * controls.frontBrakeBias, Is.EqualTo(front * TorqueUnit * 4f).Within(.002f));
            Assert.That(controls.serviceBrakeTorque * (1f - controls.frontBrakeBias), Is.EqualTo(rear * TorqueUnit * 4f).Within(.002f));
            Assert.That(controls.handbrakeTorque, Is.EqualTo(handbrake * TorqueUnit * 10f).Within(.002f));
        }

        [TestCase("bmwm3gtre46", 2.5f, .75f)]
        [TestCase("gti", 0f, 0f)]
        [TestCase("punto", 0f, 0f)]
        public void NitrousPreservesDurationAndMultiplicativeBoostSemantics(string name, float capacity, float boost)
        {
            using var imported = CreateImport(name); var tuning = imported.Tuning; var settings = tuning.mostWanted;
            var nos = Selected(name, "nos");
            Assert.That(tuning.engine.nitrousFuelSeconds, Is.EqualTo(capacity));
            Assert.That(tuning.engine.nitrousTorque, Is.Zero, "Source boost is not additive N.m.");
            Assert.That(settings.nitrousTorqueBoost, Is.EqualTo(boost));
            Assert.That(settings.nitrousDisengageSeconds, Is.EqualTo(RawFloat(nos, "NOS_DISENGAGE")));
            Assert.That(settings.nitrousRechargeMinimumSeconds, Is.EqualTo(RawFloat(nos, "RECHARGE_MIN")));
            Assert.That(settings.nitrousRechargeMaximumSeconds, Is.EqualTo(RawFloat(nos, "RECHARGE_MAX")));
            Assert.That(settings.nitrousRechargeMinimumKph, Is.EqualTo(RawFloat(nos, "RECHARGE_MIN_SPEED") * .44703001f * 3.6f).Within(.00002f));
            Assert.That(settings.nitrousRechargeMaximumKph, Is.EqualTo(RawFloat(nos, "RECHARGE_MAX_SPEED") * .44703001f * 3.6f).Within(.00002f));
            float plain = MostWantedVehicleMath.NetEngineTorque(tuning, 4000f, 1f, 1f, false);
            Assert.That(MostWantedVehicleMath.NetEngineTorque(tuning, 4000f, 1f, 1f, true), Is.EqualTo(plain * (1f + boost)).Within(.002f));
            Assert.That(imported.Report.unmapped.Any(item => item.EndsWith("/FLOW_RATE (decoded)", StringComparison.Ordinal)), Is.True);
        }

        [TestCase("bmwm3gtre46", false)]
        [TestCase("gti", true)]
        [TestCase("punto", false)]
        public void InductionComesFromTheExplicitLinkedRecord(string name, bool forcedInduction)
        {
            using var imported = CreateImport(name); var tuning = imported.Tuning; var settings = tuning.mostWanted;
            var induction = Selected(name, "induction");
            Assert.That(tuning.engine.forcedInduction, Is.EqualTo(forcedInduction));
            Assert.That(settings.inductionLowBoost, Is.EqualTo(RawFloat(induction, "LOW_BOOST")));
            Assert.That(settings.inductionHighBoost, Is.EqualTo(RawFloat(induction, "HIGH_BOOST")));
            Assert.That(settings.inductionVacuum, Is.EqualTo(RawFloat(induction, "VACUUM")));
            Assert.That(settings.inductionSpoolRpmFraction, Is.EqualTo(RawFloat(induction, "SPOOL")));
            Assert.That(tuning.engine.boostSpoolSeconds, Is.EqualTo(RawFloat(induction, "SPOOL_TIME_UP")));
            Assert.That(settings.inductionSpoolDownSeconds, Is.EqualTo(RawFloat(induction, "SPOOL_TIME_DOWN")));
            Assert.That(imported.Report.unmapped.Any(item => item.EndsWith("/PSI (decoded)", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void MappedFieldsRetainSourceHashesOffsetsAndInheritedOwnership()
        {
            using var imported = CreateImport("bmwm3gtre46");
            Assert.That(source.records.SelectMany(record => record.fields).Any(field => field.inherited), Is.True);
            foreach (var mapping in imported.Report.mapped)
            {
                var record = source.records.Single(item => item.id == mapping.recordId);
                var field = Field(record, mapping.field);
                Assert.That(mapping.source, Is.Not.Null);
                Assert.That(mapping.source.sourceFile, Is.EqualTo(field.source.sourceFile));
                Assert.That(mapping.source.sha256, Is.EqualTo(source.sources.Single(file => file.relativePath == mapping.source.sourceFile).sha256));
                Assert.That(mapping.source.packOffset, Is.EqualTo(field.source.packOffset));
                Assert.That(mapping.source.packOffset, Is.EqualTo(mapping.source.segmentStart + mapping.source.segmentOffset));
                Assert.That(field.inheritance.Last().rowKey, Is.EqualTo(field.ownerRowKey));
            }
        }

        [TestCase("unverified")]
        [TestCase("mismatch")]
        [TestCase("missing-sources")]
        public void RejectsUnverifiedOrChangedSourceHashes(string defect)
        {
            if (defect == "unverified") source.sources[0].unchanged = false;
            else if (defect == "mismatch") source.sources[0].afterSha256 = new string('0', 64);
            else source.sources.Clear();
            AssertImportRejected("hashes");
        }

        [TestCase("pvehicle")]
        [TestCase("engine")]
        [TestCase("transmission")]
        [TestCase("tires")]
        [TestCase("chassis")]
        [TestCase("brakes")]
        [TestCase("nos")]
        [TestCase("induction")]
        public void RejectsUnresolvedVehicleOrSelectedRecordInheritance(string role)
        {
            var record = role == "pvehicle" ? source.records.Single(item => item.id == Car("bmwm3gtre46").recordId) : Selected("bmwm3gtre46", role);
            record.inheritanceResolved = false;
            AssertImportRejected("inheritance");
        }

        [TestCase("engine", "TORQUE")]
        [TestCase("engine", "IDLE")]
        [TestCase("engine", "MAX_RPM")]
        [TestCase("transmission", "GEAR_EFFICIENCY")]
        [TestCase("tires", "SECTION_WIDTH")]
        [TestCase("chassis", "SPRING_STIFFNESS")]
        [TestCase("brakes", "BRAKES")]
        [TestCase("nos", "NOS_CAPACITY")]
        [TestCase("induction", "SPOOL")]
        public void MissingRequiredFieldsNeverReceiveGuessedDefaults(string role, string field)
        {
            Assert.That(Selected("bmwm3gtre46", role).fields.RemoveAll(item => item.name == field), Is.EqualTo(1));
            AssertImportRejected(field);
        }

        [TestCase("field-unsupported")]
        [TestCase("value-unsupported")]
        [TestCase("wrong-type")]
        [TestCase("non-finite")]
        [TestCase("empty")]
        public void RequiredScalarPayloadsMustBeDecodedFiniteSingleFloats(string defect)
        {
            var field = Field(Selected("bmwm3gtre46", "engine"), "IDLE");
            switch (defect)
            {
                case "field-unsupported": field.status = "unsupported"; break;
                case "value-unsupported": field.values[0].status = "unsupported"; break;
                case "wrong-type": field.values[0].kind = "uint32"; break;
                case "non-finite": field.values[0].numericValue = double.NaN; break;
                case "empty": field.values.Clear(); break;
            }
            AssertImportRejected("IDLE");
        }

        [Test]
        public void MissingRearAxleIsRejectedRatherThanCopiedFromFront()
        {
            Field(Selected("bmwm3gtre46", "brakes"), "BRAKES").values[0].components.RemoveAll(component => component.name == "Rear");
            AssertImportRejected("BRAKES");
        }

        [Test]
        public void ChoicesMustBePresentAndActuallyLinkedByTheSelectedVehicle()
        {
            var vehicle = Car("bmwm3gtre46"); var choices = ChoicesZero(vehicle);
            choices.Remove("engine");
            Assert.Throws<InvalidDataException>(() => { using var ignored = MostWantedDrivingImporter.Create(source, vehicle, choices); });
            choices["engine"] = ChoicesZero(Car("gti"))["engine"];
            Assert.Throws<InvalidDataException>(() => { using var ignored = MostWantedDrivingImporter.Create(source, vehicle, choices); });
        }

        [Test]
        public void PreviewCopiesBaselineWithoutChangingHistoricalEvidenceOrAuthoredTuning()
        {
            var baseline = VehicleTuning.CreateStreetRacer();
            try
            {
                baseline.name = "Retained authored baseline"; baseline.tires.wheelMass = 23f;
                baseline.chassis.centerOfMass = new Vector3(.12f, -.41f, .07f);
                string before = JsonUtility.ToJson(baseline); string evidenceBefore = JsonConvert.SerializeObject(source, JsonSettings);
                using (var imported = CreateImport("bmwm3gtre46", baseline))
                {
                    Assert.That(imported.Tuning, Is.Not.SameAs(baseline));
                    Assert.That(imported.Tuning.engine.gearRatios, Is.Not.SameAs(baseline.engine.gearRatios));
                    Assert.That(imported.Tuning.chassis.centerOfMass, Is.EqualTo(baseline.chassis.centerOfMass));
                    Assert.That(imported.Tuning.tires.wheelMass, Is.EqualTo(23f));
                    Assert.That(imported.Tuning.assists.abs || imported.Tuning.assists.tractionControl || imported.Tuning.assists.stabilityControl || imported.Tuning.assists.countersteering, Is.False);
                    Assert.That(imported.Tuning.handling.brakeToDrift, Is.False);
                    Assert.That(imported.Tuning.handling.driftBias, Is.Zero);
                    imported.Tuning.engine.gearRatios[0] = 9f;
                }
                Assert.That(JsonUtility.ToJson(baseline), Is.EqualTo(before));
                Assert.That(JsonConvert.SerializeObject(source, JsonSettings), Is.EqualTo(evidenceBefore));
                Assert.That(HashFile(baselinePath), Is.EqualTo(BaselineSha256));
            }
            finally { UnityEngine.Object.DestroyImmediate(baseline); }
        }

        [TestCase("")]
        [TestCase("null")]
        [TestCase("{\"schema\":2}")]
        [TestCase("{\"vehicles\":null}")]
        [TestCase("{\"records\":null}")]
        [TestCase("{\"sources\":null}")]
        [TestCase("{\"issues\":null}")]
        public void ReadReportRejectsEmptyNullUnsupportedSchemaAndNullCollections(string json)
        {
            string path = WriteScratchJson(json);
            Assert.Throws<InvalidDataException>(() => MostWantedDrivingWindow.ReadReport(path));
        }

        [TestCase("{not-json}")]
        [TestCase("{\"schema\":[}")]
        [TestCase("[]")]
        public void ReadReportRejectsMalformedOrWrongRootJson(string json)
        {
            string path = WriteScratchJson(json);
            Assert.That(() => MostWantedDrivingWindow.ReadReport(path), Throws.InstanceOf<JsonException>());
        }

        [Test]
        public void ReadReportEnforcesByteAndDepthBudgetsBeforeImport()
        {
            string large = WriteScratchJson(" ");
            using (var stream = new FileStream(large, FileMode.Open, FileAccess.Write, FileShare.None)) stream.SetLength(64L * 1024 * 1024 + 1);
            Assert.Throws<InvalidDataException>(() => MostWantedDrivingWindow.ReadReport(large));
            string deep = WriteScratchJson("{\"unknown\":" + new string('[', 70) + "0" + new string(']', 70) + "}");
            Assert.That(() => MostWantedDrivingWindow.ReadReport(deep), Throws.InstanceOf<JsonException>());
        }

        [Test]
        public void SaveCreatesSeparateAssetsAndRoundTripsProvenanceWithoutOverwriting()
        {
            string path = NewAssetFolder() + "/BmwReference.asset";
            using var imported = CreateImport("bmwm3gtre46");
            var saved = MostWantedDrivingWindow.SaveNew(imported, path);
            Assert.That(saved, Is.Not.SameAs(imported.Tuning)); Assert.That(EditorUtility.IsPersistent(saved), Is.True);
            Assert.That(EditorUtility.IsPersistent(imported.Tuning), Is.False);
            Assert.That(saved.UsesMostWantedReference, Is.True);
            Assert.That(MostWantedVehicleMath.Torque(saved, 5150f), Is.EqualTo(633.1586f).Within(.001f));
            string evidencePath = Path.ChangeExtension(AssetDatabase.GetAssetPath(saved), ".provenance.json");
            string savedHash = HashFile(path), provenanceHash = HashFile(evidencePath);
            var report = JsonConvert.DeserializeObject<MostWantedDrivingImportReport>(File.ReadAllText(evidencePath), JsonSettings);
            Assert.That(report.vehicle, Is.EqualTo("bmwm3gtre46"));
            Assert.That(report.selectedLinks.Count, Is.EqualTo(7));
            Assert.That(report.sources.Select(file => file.sha256), Is.EqualTo(source.sources.Select(file => file.sha256)));
            Assert.That(report.mapped.Select(mapping => mapping.source.packOffset), Is.EqualTo(imported.Report.mapped.Select(mapping => mapping.source.packOffset)));
            var second = MostWantedDrivingWindow.SaveNew(imported, path);
            Assert.That(AssetDatabase.GetAssetPath(second), Is.Not.EqualTo(path), "A second save must choose a new asset path.");
            Assert.That(HashFile(path), Is.EqualTo(savedHash)); Assert.That(HashFile(evidencePath), Is.EqualTo(provenanceHash));
        }

        [Test]
        public void SaveRejectsSidecarCollisionWithoutReplacingExistingBytes()
        {
            string path = NewAssetFolder() + "/Collision.asset";
            string evidencePath = Path.ChangeExtension(path, ".provenance.json");
            File.WriteAllText(evidencePath, "retained user evidence", new UTF8Encoding(false));
            using var imported = CreateImport("bmwm3gtre46");
            Assert.Throws<IOException>(() => MostWantedDrivingWindow.SaveNew(imported, path));
            Assert.That(File.ReadAllText(evidencePath), Is.EqualTo("retained user evidence"));
            Assert.That(File.Exists(path), Is.False);
        }

        [TestCase("Tools/DrivingMechanics/Evidence/not-an-asset.asset")]
        [TestCase("Assets/not-an-asset.json")]
        [TestCase("Assets/../not-an-asset.asset")]
        public void SaveRejectsPathsOutsideAssetsOrWrongExtension(string path)
        {
            using var imported = CreateImport("bmwm3gtre46");
            Assert.Throws<ArgumentException>(() => MostWantedDrivingWindow.SaveNew(imported, path));
        }

        private MostWantedHandlingVehicle Car(string name) => source.vehicles.Single(vehicle => vehicle.name == name);
        private static Dictionary<string, string> ChoicesZero(MostWantedHandlingVehicle vehicle) => MostWantedDrivingImporter.Roles
            .ToDictionary(role => role, role => vehicle.links.Single(link => link.fieldName == role && link.index == 0 && link.status == "resolved").targetRecordId, StringComparer.Ordinal);
        private MostWantedHandlingRecord Selected(string name, string role) => source.records.Single(record => record.id == ChoicesZero(Car(name))[role]);
        private MostWantedDrivingImport CreateImport(string name, VehicleTuning baseline = null) => MostWantedDrivingImporter.Create(source, Car(name), ChoicesZero(Car(name)), baseline);
        private static MostWantedHandlingField Field(MostWantedHandlingRecord record, string name) => record.fields.Single(field => field.name == name);
        private static float[] RawFloats(MostWantedHandlingRecord record, string name) => Field(record, name).values.Select(value => (float)value.numericValue).ToArray();
        private static float RawFloat(MostWantedHandlingRecord record, string name) => RawFloats(record, name).Single();
        private static Vector2 RawPair(MostWantedHandlingRecord record, string name)
        {
            var values = Field(record, name).values.Single().components;
            return new Vector2((float)values.Single(component => component.name == "Front").numericValue,
                (float)values.Single(component => component.name == "Rear").numericValue);
        }
        private void AssertImportRejected(string expectedMessage)
        {
            var error = Assert.Throws<InvalidDataException>(() => { using var ignored = CreateImport("bmwm3gtre46"); });
            Assert.That(error.Message, Does.Contain(expectedMessage));
        }
        private static void SetFloat(MostWantedHandlingValue value, float number)
        {
            value.numericValue = number; value.numberText = number.ToString("R", CultureInfo.InvariantCulture);
            var bytes = BitConverter.GetBytes(number); value.floatBits = "0x" + BitConverter.ToUInt32(bytes, 0).ToString("X8", CultureInfo.InvariantCulture);
            value.rawHex = value.relocatedHex = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
        private static void SetComponent(MostWantedHandlingRecord record, string field, string axle, float number)
        {
            // Deliberately synthetic mutation of this test's deep clone; never published as source evidence.
            var component = Field(record, field).values.Single().components.Single(value => value.name == axle);
            component.numericValue = number; component.numberText = number.ToString("R", CultureInfo.InvariantCulture);
            component.floatBits = "0x" + BitConverter.ToUInt32(BitConverter.GetBytes(number), 0).ToString("X8", CultureInfo.InvariantCulture);
        }
        private string WriteScratchJson(string text)
        {
            if (scratchFolder == null)
            {
                scratchFolder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "DrivingMechanics", "ImporterTests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(scratchFolder);
            }
            string path = Path.Combine(scratchFolder, Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, text, new UTF8Encoding(false)); return path;
        }
        private string NewAssetFolder()
        {
            Assert.That(assetFolder, Is.Null, "Only one test-owned asset root per test.");
            string leaf = "__MostWantedImporterTests_" + Guid.NewGuid().ToString("N");
            string guid = AssetDatabase.CreateFolder("Assets", leaf);
            assetFolder = AssetDatabase.GUIDToAssetPath(guid);
            Assert.That(assetFolder, Is.EqualTo("Assets/" + leaf)); return assetFolder;
        }
        private static string HashFile(string path)
        {
            using var sha = SHA256.Create(); using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
