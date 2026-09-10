using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NfsMwRemaster.Driving.AudioAnalysis;
using NfsMwRemaster.Driving.Editor.DrivingMechanics;
using NUnit.Framework;

namespace NfsMwRemaster.Driving.Tests
{
    /// <summary>Synthetic format fixtures contain no game bytes and require neither Unity nor an installation.</summary>
    public sealed class MostWantedHandlingReaderTests
    {
        [Test]
        public void HashesMatchPinnedGeneratedClassKeysRatherThanOnlySelfGeneratedFixtures()
        {
            // Independent constants in the pinned generated class headers; see DECODING.md.
            Assert.That(Hash("engine"), Is.EqualTo(0xf1f5fbc7u));
            Assert.That(Hash("transmission"), Is.EqualTo(0x07a7a3e5u));
            Assert.That(Hash("tires"), Is.EqualTo(0xbd38d1cau));
            Assert.That(Hash("chassis"), Is.EqualTo(0xafa210f0u));
            Assert.That(Hash("brakes"), Is.EqualTo(0x36350867u));
            Assert.That(Hash("nos"), Is.EqualTo(0xb1669f64u));
            Assert.That(Hash("induction"), Is.EqualTo(0xc92a0142u));
            Assert.That(Hash("RIDE_HEIGHT"), Is.EqualTo(0x46c189b0u));
        }

        [Test]
        public void DecodesEveryRequestedRoleAndKeepsDuplicateUpgradeSlots()
        {
            var report = Decode(Fixture()); var car = report.vehicles.Single();
            var engines = car.links.Where(link => link.fieldName == "engine").ToArray();
            Assert.That(engines.Select(link => link.index), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(engines.All(link => link.status == "resolved"), Is.True);
            Assert.That(engines[1].targetRecordId, Is.EqualTo(engines[2].targetRecordId));
            Assert.That(car.links.Select(link => link.fieldName).Distinct(), Is.EquivalentTo(new[]
                { "engine", "transmission", "tires", "chassis", "brakes", "nos", "induction" }));
            Assert.That(report.records.Count(record => record.className == "engine"), Is.EqualTo(2));
            Assert.That(report.sources.Single().unchanged, Is.True);
        }

        [Test]
        public void FieldInheritanceIdentifiesTheDeclaringRowAndExactSourceBytes()
        {
            var pack = Fixture(); byte[] before = (byte[])pack.Clone(); var report = Decode(pack);
            var engine = report.records.Single(record => record.className == "engine" && record.rowKey == Key("upgraded"));
            var torque = engine.fields.Single(field => field.name == "TORQUE");
            Assert.That(torque.inherited, Is.True); Assert.That(torque.ownerRowKey, Is.EqualTo(Key("stock")));
            Assert.That(torque.inheritance.Select(row => row.rowKey), Is.EqualTo(new[] { Key("upgraded"), Key("stock") }));
            Assert.That(torque.values.Select(value => value.numericValue), Is.EqualTo(new[] { 100d, 200d, 300d }));
            var scalar = torque.values[1];
            Assert.That(scalar.source.sourceFile, Is.EqualTo("GLOBAL/synthetic.bin"));
            Assert.That(scalar.source.sha256, Is.EqualTo(report.sources[0].sha256));
            Assert.That(scalar.source.packOffset, Is.EqualTo(scalar.source.segmentStart + scalar.source.segmentOffset));
            Assert.That(Hex(pack.Skip(scalar.source.packOffset).Take(4).ToArray()), Is.EqualTo(scalar.rawHex));
            Assert.That(pack, Is.EqualTo(before), "Source input must remain byte-identical.");
            var limit = engine.fields.Single(field => field.name == "RED_LINE");
            Assert.That(limit.inherited, Is.False); Assert.That(limit.source.segment, Is.EqualTo("vlt"));
        }

        [Test]
        public void BaseLayoutAxlePairsDecodeFrontRearWithoutUnitConversions()
        {
            var report = Decode(Fixture()); var chassis = report.records.Single(row => row.className == "chassis");
            var ride = chassis.fields.Single(field => field.name == "RIDE_HEIGHT");
            Assert.That(ride.source.segment, Is.EqualTo("bin")); Assert.That(ride.layoutOffset, Is.Zero);
            Assert.That(ride.values.Single().kind, Is.EqualTo("axle-pair"));
            Assert.That(ride.values[0].components.Select(component => component.name), Is.EqualTo(new[] { "Front", "Rear" }));
            Assert.That(ride.values[0].components.Select(component => component.numericValue), Is.EqualTo(new[] { 0.125d, 0.25d }));
            Assert.That(report.physicalInterpretation, Does.Contain("no physical conversions"));
        }

        [Test]
        public void OriginalAndRelocatedReferenceWordsRemainDistinct()
        {
            var pack = Fixture(); var report = Decode(pack);
            var field = report.records.Single(record => record.className == "pvehicle").fields.Single(value => value.name == "engine");
            var reference = field.values[0];
            Assert.That(reference.rawHex.Substring(16, 8), Is.EqualTo("efbeadde"));
            Assert.That(reference.relocatedHex.Substring(16, 8), Is.EqualTo("34120000"));
            Assert.That(reference.source.segment, Is.EqualTo("bin"));
            Assert.That(reference.referenceClassKey, Is.EqualTo(Key("engine")));
            Assert.That(reference.referenceRowKey, Is.EqualTo(Key("stock")));
        }

        [Test]
        public void ExactIntegersNeverRoundThroughFloat32()
        {
            var unsigned = MostWantedHandlingReader.Decode(Value("EA::Reflection::UInt32", UInt(uint.MaxValue)));
            var signed = MostWantedHandlingReader.Decode(Value("EA::Reflection::Int32", UInt(0x80000001)));
            Assert.That(unsigned.unsignedValue, Is.EqualTo(uint.MaxValue));
            Assert.That(signed.signedValue, Is.EqualTo(int.MinValue + 1));
            Assert.That(unsigned.kind, Is.EqualTo("uint32")); Assert.That(signed.kind, Is.EqualTo("int32"));
        }

        [Test]
        public void UnknownTypesAndWrongWidthsDoNotInventFloats()
        {
            var unknown = MostWantedHandlingReader.Decode(Value("MysteryPhysicsType", UInt(0x3f800000)));
            Assert.That(unknown.status, Is.EqualTo("unsupported")); Assert.That(unknown.rawHex, Is.EqualTo("0000803f"));
            var wrong = MostWantedHandlingReader.Decode(Value("AxlePair", new byte[12]));
            Assert.That(wrong.status, Is.EqualTo("unsupported")); Assert.That(wrong.unsupportedReason, Does.Contain("width mismatch"));
        }

        [Test]
        public void NonFiniteFloatsKeepBitsWithoutProducingNonFiniteJsonNumbers()
        {
            var value = MostWantedHandlingReader.Decode(Value("EA::Reflection::Float", UInt(0x7fc01234)));
            Assert.That(value.status, Is.EqualTo("unsupported")); Assert.That(value.floatBits, Is.EqualTo("0x7FC01234"));
            Assert.That(double.IsNaN(value.numericValue) || double.IsInfinity(value.numericValue), Is.False);
            var negativeZero = MostWantedHandlingReader.Decode(Value("EA::Reflection::Float", UInt(0x80000000)));
            Assert.That(negativeZero.floatBits, Is.EqualTo("0x80000000"));
        }

        [Test]
        public void InvalidArraysAreExplicitInsteadOfSilentlyReturningNoValues()
        {
            var fixture = Specs(); fixture.Rows.Single(row => row.Name == "stock").Values["TORQUE"] = ArrayBytes(4, 2, FloatBytes(100, 200, 300));
            var report = Decode(Build(fixture)); var field = report.records.Single(row => row.rowKey == Key("stock")).fields.Single(f => f.name == "TORQUE");
            Assert.That(field.status, Is.EqualTo("unsupported")); Assert.That(field.arrayCapacity, Is.EqualTo(2));
            Assert.That(field.arrayCount, Is.EqualTo(4)); Assert.That(report.complete, Is.False);
            Assert.That(report.issues.Any(issue => issue.code == "unsupported-field"), Is.True);
        }

        [Test]
        public void MissingReferenceTargetsAreNotReplacedByRoleDefaults()
        {
            var fixture = Specs(); fixture.Rows.Single(row => row.Name == "car").Values["brakes"] = Reference("brakes", "missing");
            var report = Decode(Build(fixture)); var link = report.vehicles.Single().links.Single(edge => edge.fieldName == "brakes");
            Assert.That(link.status, Is.EqualTo("missing-row")); Assert.That(link.targetRowKey, Is.EqualTo(Key("missing")));
            Assert.That(link.targetRecordId, Is.Null); Assert.That(report.complete, Is.False);
        }

        [Test]
        public void ReferenceClassComesFromPayloadRatherThanFieldName()
        {
            var fixture = Specs(); fixture.Rows.Single(row => row.Name == "car").Values["brakes"] = Reference("tires", "rubber");
            var report = Decode(Build(fixture)); var link = report.vehicles.Single().links.Single(edge => edge.fieldName == "brakes");
            Assert.That(link.status, Is.EqualTo("resolved")); Assert.That(link.targetClassKey, Is.EqualTo(Key("tires")));
            Assert.That(report.records.Single(row => row.id == link.targetRecordId).className, Is.EqualTo("tires"));
        }

        [Test]
        public void InheritanceCyclesPreserveOnlyOwnEvidenceAndFlagUnresolvedChain()
        {
            var fixture = Specs(); fixture.Rows.Single(row => row.Name == "stock").Parent = "upgraded";
            var report = Decode(Build(fixture));
            Assert.That(report.records.Single(row => row.rowKey == Key("upgraded")).inheritanceResolved, Is.False);
            Assert.That(report.issues.Any(issue => issue.code == "unresolved-inheritance"), Is.True);
            Assert.That(report.records.Single(row => row.rowKey == Key("upgraded")).fields.Any(field => field.name == "TORQUE"), Is.False);
        }

        [Test]
        public void CrossFileInheritanceKeepsParentSourceIdentity()
        {
            var fixture = Specs(); var upgraded = fixture.Rows.Single(row => row.Name == "upgraded"); fixture.Rows.Remove(upgraded);
            byte[] first = Build(fixture); fixture.Rows.Clear(); fixture.Rows.Add(upgraded);
            var report = MostWantedHandlingReader.DecodeSources(new[] { new MostWantedHandlingSourceInput("GLOBAL/first.bin", first),
                new MostWantedHandlingSourceInput("GLOBAL/second.bin", Build(fixture, includeClasses: false)) });
            var engine = report.records.Single(row => row.rowKey == Key("upgraded"));
            Assert.That(engine.source.sourceFile, Is.EqualTo("GLOBAL/second.bin"));
            Assert.That(engine.fields.Single(field => field.name == "TORQUE").source.sourceFile, Is.EqualTo("GLOBAL/first.bin"));
            Assert.That(engine.fields.Single(field => field.name == "RED_LINE").source.sourceFile, Is.EqualTo("GLOBAL/second.bin"));
        }

        [Test]
        public void AudioReaderApisStillResolveTheSameValuesAndItemsAreNotNestedArrays()
        {
            var pack = Fixture(); var legacy = new MostWantedAudioDatabase(); legacy.Load(pack, "engine", "chassis", "transmission", "tires", "brakes", "nos", "induction");
            var analysis = new MostWantedAudioDatabase(); analysis.LoadSource(pack, "synthetic.bin", "engine", "chassis", "transmission", "tires", "brakes", "nos", "induction");
            var old = legacy.Resolve(legacy.Find("engine", "upgraded")); var current = analysis.Resolve(analysis.Find("engine", "upgraded"));
            Assert.That(current[Hash("MAX_RPM")].Number(), Is.EqualTo(old[Hash("MAX_RPM")].Number()));
            var items = current[Hash("TORQUE")].Items(); Assert.That(items.Select(value => value.Number()), Is.EqualTo(new[] { 100f, 200f, 300f }));
            Assert.That(items[0].Items().Single(), Is.SameAs(items[0]));
        }

        [Test]
        public void RejectsInvalidPacksDuplicateIdentitiesAndEmptySelections()
        {
            Assert.Throws<InvalidDataException>(() => Decode(new byte[] { 1, 2, 3 }));
            var input = new MostWantedHandlingSourceInput("GLOBAL/synthetic.bin", Fixture());
            Assert.Throws<ArgumentException>(() => MostWantedHandlingReader.DecodeSources(new[] { input, input }));
            Assert.Throws<ArgumentException>(() => MostWantedHandlingReader.DecodeSources(new[] { input }, Array.Empty<string>()));
            Assert.Throws<ArgumentException>(() => MostWantedHandlingReader.DecodeSources(new[] { new MostWantedHandlingSourceInput("../source.bin", Fixture()) }));
        }

        [Test]
        public void RequestedMissingVehiclesAreExplicitAndAllRowsRemainAvailableWithoutSelection()
        {
            var input = new MostWantedHandlingSourceInput("synthetic.bin", Fixture());
            var missing = MostWantedHandlingReader.DecodeSources(new[] { input }, new[] { "missing" });
            Assert.That(missing.vehicles, Is.Empty); Assert.That(missing.issues.Any(issue => issue.code == "missing-vehicle"), Is.True);
            Assert.That(MostWantedHandlingReader.DecodeSources(new[] { input }).vehicles.Count, Is.EqualTo(1));
        }

        [Test]
        public void UpgradeSpecsUnknownLayoutIsExplicitButCollectionIdentityIsRetained()
        {
            var value = MostWantedHandlingReader.Decode(Value("UpgradeSpecs", Reference("engine", "stock")));
            Assert.That(value.kind, Is.EqualTo("upgrade-specs")); Assert.That(value.referenceRowKey, Is.EqualTo(Key("stock")));
            Assert.That(value.status, Is.EqualTo("unsupported")); Assert.That(value.referenceClassKey, Is.Null);
        }

        [Test]
        public void AllVehicleSelectionHasNoThreeCarWhitelist()
        {
            var fixture = Specs();
            foreach (string name in new[] { "synthetic-alpha", "synthetic-beta", "synthetic-gamma", "synthetic-delta" })
                fixture.Rows.Add(new RowSpec("pvehicle", name, "car"));
            var report = Decode(Build(fixture)); Assert.That(report.vehicles.Count, Is.EqualTo(5));
            Assert.That(report.vehicles.All(car => car.links.Count(link => link.fieldName == "engine") == 3), Is.True);
        }

        [Test]
        public void MultiVaultAbsoluteOffsetsIdentifyTheOriginalPackBytes()
        {
            var fixture = Specs(); var upgraded = fixture.Rows.Single(row => row.Name == "upgraded"); fixture.Rows.Remove(upgraded);
            byte[] first = Build(fixture); fixture.Rows.Clear(); fixture.Rows.Add(upgraded); byte[] second = Build(fixture, includeClasses: false);
            int Read(byte[] bytes, int at) => checked((int)BitConverter.ToUInt32(bytes, at));
            var pack = new byte[first.Length + second.Length]; Write32(pack, 0, 0x4b415056); Write32(pack, 4, 2); int position = 64;
            for (int i = 0; i < 2; i++)
            {
                var source = i == 0 ? first : second; int binSize = Read(source, 20), vltSize = Read(source, 24), entry = 16 + i * 20;
                Write32(pack, entry + 4, (uint)binSize); Write32(pack, entry + 8, (uint)vltSize);
                Write32(pack, entry + 12, (uint)position); Array.Copy(source, Read(source, 28), pack, position, binSize); position += binSize;
                Write32(pack, entry + 16, (uint)position); Array.Copy(source, Read(source, 32), pack, position, vltSize); position += vltSize;
            }
            Array.Resize(ref pack, position); var report = Decode(pack);
            var upgradedRow = report.records.Single(row => row.rowKey == Key("upgraded"));
            Assert.That(upgradedRow.source.vaultIndex, Is.EqualTo(1));
            var limit = upgradedRow.fields.Single(field => field.name == "RED_LINE").values[0];
            Assert.That(limit.source.vaultIndex, Is.EqualTo(1));
            Assert.That(Hex(pack.Skip(limit.source.packOffset).Take(4).ToArray()), Is.EqualTo(limit.rawHex));
            Assert.That(upgradedRow.fields.Single(field => field.name == "TORQUE").source.vaultIndex, Is.Zero);
        }

        [Test]
        public void JunkmanModKeepsDefinitionKeyDistinctFromCollectionReferences()
        {
            var bytes = UInt(Hash("engine")).Concat(UInt(Hash("TORQUE"))).Concat(FloatBytes(1.25f)).ToArray();
            var value = MostWantedHandlingReader.Decode(Value("JunkmanMod", bytes));
            Assert.That(value.status, Is.EqualTo("decoded")); Assert.That(value.kind, Is.EqualTo("junkman-mod"));
            Assert.That(value.definitionKey, Is.EqualTo(Key("TORQUE"))); Assert.That(value.referenceRowKey, Is.Null);
            Assert.That(value.components.Single().numericValue, Is.EqualTo(1.25d));
        }

        [Test]
        public void DecoderRejectsArrayHeadersAndReaderRejectsTruncatedCapacity()
        {
            var value = Value("EA::Reflection::Float", ArrayBytes(1, 4096, FloatBytes(1)));
            value.Definition.Size = 4; value.Definition.Flags = 1;
            Assert.That(MostWantedHandlingReader.Decode(value).status, Is.EqualTo("unsupported"));
            Assert.Throws<InvalidDataException>(() => value.Items());
        }

        [Test]
        public void FileApiVerifiesHashesAndCreatesNothingInsideSyntheticSourceDirectory()
        {
            string root = Path.Combine(Environment.CurrentDirectory, "Library", "DrivingMechanics", "test-source-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "GLOBAL"));
            try
            {
                File.WriteAllBytes(Path.Combine(root, "GLOBAL", "attributes.bin"), Fixture());
                foreach (string name in new[] { "FE_ATTRIB.bin", "gameplay.bin" }) File.WriteAllBytes(Path.Combine(root, "GLOBAL", name), Build(new PackSpec()));
                var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path).ToArray();
                var report = MostWantedHandlingReader.Decode(root);
                Assert.That(report.sources.Count, Is.EqualTo(3)); Assert.That(report.sources.All(source => source.unchanged && source.sha256 == source.afterSha256), Is.True);
                Assert.That(report.sources.All(source => source.verification.StartsWith("File SHA-256", StringComparison.Ordinal)), Is.True);
                Assert.That(Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path).ToArray(), Is.EqualTo(before));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test, Explicit("Read-only integration; requires an explicitly permitted BLACKBOX_GAME installation path.")]
        public void InstalledHandlingIntegrationKeepsEverySourceHashUnchanged()
        {
            string game = Environment.GetEnvironmentVariable("BLACKBOX_GAME");
            if (string.IsNullOrWhiteSpace(game)) Assert.Ignore("Set BLACKBOX_GAME to an explicitly permitted source installation.");
            var report = MostWantedHandlingReader.Decode(game);
            Assert.That(report.vehicles, Is.Not.Empty);
            Assert.That(report.sources.All(source => source.unchanged && source.sha256 == source.afterSha256), Is.True);
            foreach (string role in new[] { "engine", "transmission", "tires", "chassis", "brakes", "nos", "induction" })
                Assert.That(report.vehicles.Any(vehicle => vehicle.links.Any(link => link.fieldName == role && link.status == "resolved")), Is.True, role);
        }

        private static MostWantedHandlingReport Decode(byte[] bytes) => MostWantedHandlingReader.DecodeSources(new[] { new MostWantedHandlingSourceInput("GLOBAL/synthetic.bin", bytes) });
        private static uint Hash(string text) => MostWantedAudioDatabase.Hash(text);
        private static string Key(string text) => "0x" + Hash(text).ToString("X8");
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        private static MostWantedAudioDatabase.Value Value(string type, byte[] bytes) => new MostWantedAudioDatabase.Value
            { Definition = new MostWantedAudioDatabase.Field { Type = Hash(type), Size = bytes.Length, Alignment = 4 }, Data = bytes, Strings = bytes };
        private static byte[] UInt(uint value) { var bytes = new byte[4]; Write32(bytes, 0, value); return bytes; }
        private static byte[] FloatBytes(params float[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();
        private static byte[] Reference(string type, string name) => UInt(Hash(type)).Concat(UInt(Hash(name))).Concat(UInt(0xdeadbeef)).ToArray();
        private static byte[] ArrayBytes(int count, int capacity, byte[] payload, int size = 4)
        {
            var bytes = new byte[8 + payload.Length]; Write16(bytes, 0, capacity); Write16(bytes, 2, count); Write16(bytes, 4, size);
            Array.Copy(payload, 0, bytes, 8, payload.Length); return bytes;
        }
        private static void Write32(byte[] bytes, int at, uint value)
        { bytes[at] = (byte)value; bytes[at + 1] = (byte)(value >> 8); bytes[at + 2] = (byte)(value >> 16); bytes[at + 3] = (byte)(value >> 24); }
        private static void Write16(byte[] bytes, int at, int value) { bytes[at] = (byte)value; bytes[at + 1] = (byte)(value >> 8); }

        private sealed class FieldSpec
        {
            public string Name, Type; public int Size, Flags, Offset;
            public FieldSpec(string name, string type, int size, int flags = 0, int offset = 0) { Name = name; Type = type; Size = size; Flags = flags; Offset = offset; }
        }
        private sealed class ClassSpec
        {
            public string Name; public FieldSpec[] Fields;
            public ClassSpec(string name, params FieldSpec[] fields) { Name = name; Fields = fields; }
        }
        private sealed class RowSpec
        {
            public string Class, Name, Parent; public byte[] Layout;
            public readonly Dictionary<string, byte[]> Values = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            public RowSpec(string type, string name, string parent = null) { Class = type; Name = name; Parent = parent; }
        }
        private sealed class PackSpec
        {
            public readonly List<ClassSpec> Classes = new List<ClassSpec>();
            public readonly List<RowSpec> Rows = new List<RowSpec>();
        }
        private static byte[] Fixture() => Build(Specs());
        private static PackSpec Specs()
        {
            var result = new PackSpec();
            FieldSpec Number(string name) => new FieldSpec(name, "EA::Reflection::Float", 4);
            FieldSpec Pair(string name, int flags = 0, int offset = 0) => new FieldSpec(name, "AxlePair", 8, flags, offset);
            result.Classes.Add(new ClassSpec("pvehicle", new FieldSpec("engine", "Attrib::RefSpec", 12, 1),
                new FieldSpec("transmission", "Attrib::RefSpec", 12), new FieldSpec("tires", "Attrib::RefSpec", 12),
                new FieldSpec("chassis", "Attrib::RefSpec", 12), new FieldSpec("brakes", "Attrib::RefSpec", 12),
                new FieldSpec("nos", "Attrib::RefSpec", 12), new FieldSpec("induction", "Attrib::RefSpec", 12), Number("MASS")));
            result.Classes.Add(new ClassSpec("engine", new FieldSpec("TORQUE", "EA::Reflection::Float", 4, 1), Number("MAX_RPM"), Number("RED_LINE"), Number("IDLE")));
            result.Classes.Add(new ClassSpec("chassis", Pair("RIDE_HEIGHT", 2), Pair("SPRING_STIFFNESS", 2, 8)));
            result.Classes.Add(new ClassSpec("transmission", new FieldSpec("GEAR_RATIO", "EA::Reflection::Float", 4, 1), Number("FINAL_GEAR"),
                new FieldSpec("GEAR_EFFICIENCY", "EA::Reflection::Float", 4, 1)));
            result.Classes.Add(new ClassSpec("tires", Pair("SECTION_WIDTH"), Pair("RIM_SIZE"), Pair("ASPECT_RATIO")));
            result.Classes.Add(new ClassSpec("brakes", Pair("BRAKES")));
            result.Classes.Add(new ClassSpec("nos", Number("NOS_CAPACITY")));
            result.Classes.Add(new ClassSpec("induction", Number("PSI")));
            var car = new RowSpec("pvehicle", "car");
            car.Values.Add("engine", ArrayBytes(3, 3, Reference("engine", "stock").Concat(Reference("engine", "upgraded")).Concat(Reference("engine", "upgraded")).ToArray(), 12));
            foreach (var target in new[] { ("transmission", "gearbox"), ("tires", "rubber"), ("chassis", "springs"), ("brakes", "stoppers"), ("nos", "bottle"), ("induction", "turbo") })
                car.Values.Add(target.Item1, Reference(target.Item1, target.Item2));
            car.Values.Add("MASS", FloatBytes(1500)); result.Rows.Add(car);
            var stock = new RowSpec("engine", "stock"); stock.Values.Add("TORQUE", ArrayBytes(3, 3, FloatBytes(100, 200, 300)));
            stock.Values.Add("MAX_RPM", FloatBytes(9000)); stock.Values.Add("RED_LINE", FloatBytes(7500)); stock.Values.Add("IDLE", FloatBytes(800)); result.Rows.Add(stock);
            var upgrade = new RowSpec("engine", "upgraded", "stock"); upgrade.Values.Add("RED_LINE", FloatBytes(8500)); result.Rows.Add(upgrade);
            result.Rows.Add(new RowSpec("chassis", "springs") { Layout = FloatBytes(.125f, .25f, 25, 30) });
            var transmission = new RowSpec("transmission", "gearbox"); transmission.Values.Add("GEAR_RATIO", ArrayBytes(4, 4, FloatBytes(-3, 0, 3.5f, 2)));
            transmission.Values.Add("GEAR_EFFICIENCY", ArrayBytes(4, 4, FloatBytes(1, 1, .9f, .95f))); transmission.Values.Add("FINAL_GEAR", FloatBytes(3.4f)); result.Rows.Add(transmission);
            var tires = new RowSpec("tires", "rubber"); tires.Values.Add("SECTION_WIDTH", FloatBytes(225, 245)); tires.Values.Add("RIM_SIZE", FloatBytes(18, 19)); tires.Values.Add("ASPECT_RATIO", FloatBytes(40, 35)); result.Rows.Add(tires);
            var brakes = new RowSpec("brakes", "stoppers"); brakes.Values.Add("BRAKES", FloatBytes(1.25f, .75f)); result.Rows.Add(brakes);
            var nos = new RowSpec("nos", "bottle"); nos.Values.Add("NOS_CAPACITY", FloatBytes(10)); result.Rows.Add(nos);
            var induction = new RowSpec("induction", "turbo"); induction.Values.Add("PSI", FloatBytes(15)); result.Rows.Add(induction);
            return result;
        }

        private static byte[] Build(PackSpec fixture, bool includeClasses = true)
        {
            using (var bin = new MemoryStream())
            using (var records = new MemoryStream())
            {
                bin.Write(new byte[16], 0, 16); var exports = new List<Tuple<uint, int, int>>(); int fixup = -1;
                int Store(byte[] bytes) { while (bin.Length % 4 != 0) bin.WriteByte(0); int position = (int)bin.Position; bin.Write(bytes, 0, bytes.Length); return position; }
                void Export(uint type, byte[] bytes) { exports.Add(Tuple.Create(type, (int)records.Position, bytes.Length)); records.Write(bytes, 0, bytes.Length); }
                if (includeClasses)
                    foreach (var type in fixture.Classes)
                    {
                        var definitions = new byte[type.Fields.Length * 16];
                        for (int i = 0; i < type.Fields.Length; i++)
                        {
                            var field = type.Fields[i]; int at = i * 16; Write32(definitions, at, Hash(field.Name)); Write32(definitions, at + 4, Hash(field.Type));
                            Write16(definitions, at + 8, field.Offset); Write16(definitions, at + 10, field.Size); Write16(definitions, at + 12, 32);
                            definitions[at + 14] = (byte)field.Flags; definitions[at + 15] = 2;
                        }
                        var data = new byte[16]; Write32(data, 0, Hash(type.Name)); Write32(data, 8, (uint)type.Fields.Length); Write32(data, 12, (uint)Store(definitions)); Export(0x5e970cbc, data);
                    }
                foreach (var row in fixture.Rows)
                {
                    var type = fixture.Classes.Single(candidate => candidate.Name == row.Class); var data = new byte[32 + row.Values.Count * 12];
                    Write32(data, 0, Hash(row.Name)); Write32(data, 4, Hash(row.Class)); Write32(data, 8, Hash(row.Parent));
                    Write32(data, 20, (uint)row.Values.Count); if (row.Layout != null) Write32(data, 28, (uint)Store(row.Layout)); int index = 0;
                    foreach (var entry in row.Values)
                    {
                        var field = type.Fields.Single(candidate => candidate.Name == entry.Key); int at = 32 + index++ * 12; Write32(data, at, Hash(entry.Key));
                        if (field.Size <= 4 && (field.Flags & 1) == 0) Array.Copy(entry.Value, 0, data, at + 4, entry.Value.Length);
                        else { int position = Store(entry.Value); Write32(data, at + 4, (uint)position); if (row.Class == "pvehicle" && entry.Key == "engine") fixup = position + 16; }
                    }
                    Export(0x8e112eb7, data);
                }
                int fixupLength = fixup < 0 ? 0 : 44; int exportLength = 12 + exports.Count * 20; int dataStart = fixupLength + exportLength + 8;
                var vlt = new byte[dataStart + records.Length];
                if (fixup >= 0)
                {
                    Write32(vlt, 0, 0x5074724e); Write32(vlt, 4, 44); Write16(vlt, 12, 2); Write16(vlt, 14, 1);
                    Write32(vlt, 20, (uint)fixup); Write16(vlt, 24, 1); Write32(vlt, 28, 0x1234);
                }
                Write32(vlt, fixupLength, 0x4578704e); Write32(vlt, fixupLength + 4, (uint)exportLength); Write32(vlt, fixupLength + 8, (uint)exports.Count);
                for (int i = 0; i < exports.Count; i++)
                {
                    int at = fixupLength + 12 + i * 20; var export = exports[i]; Write32(vlt, at + 4, export.Item1);
                    Write32(vlt, at + 12, (uint)export.Item3); Write32(vlt, at + 16, (uint)(dataStart + export.Item2));
                }
                Write32(vlt, dataStart - 8, 0x44415441); Write32(vlt, dataStart - 4, (uint)(records.Length + 8));
                Array.Copy(records.ToArray(), 0, vlt, dataStart, records.Length);
                int binStart = 64, vltStart = binStart + (int)bin.Length; var pack = new byte[vltStart + vlt.Length];
                Write32(pack, 0, 0x4b415056); Write32(pack, 4, 1); Write32(pack, 20, (uint)bin.Length); Write32(pack, 24, (uint)vlt.Length);
                Write32(pack, 28, (uint)binStart); Write32(pack, 32, (uint)vltStart); Array.Copy(bin.ToArray(), 0, pack, binStart, bin.Length); Array.Copy(vlt, 0, pack, vltStart, vlt.Length);
                return pack;
            }
        }
    }
}
