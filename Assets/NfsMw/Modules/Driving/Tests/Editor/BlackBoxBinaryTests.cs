using System;
using NUnit.Framework;
using NfsMwRemaster.Driving.AudioAnalysis;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class BlackBoxBinaryTests
    {
        [Test]
        public void GinKeepsExtraTableEntryAndDecodesXasV0()
        {
            // 0x20 + (1+1)*4 + (1+1)*4 = 0x30; two 0x13-byte XAS frames follow.
            var data = new byte[0x30 + 0x26]; WriteAscii(data, 0, "Gnsu"); Write32(data, 0x10, 1); Write32(data, 0x14, 1); Write32(data, 0x18, 32); Write32(data, 0x1c, 44100);
            Write32(data, 0x20, 0x3f800000); Write32(data, 0x24, 0x40000000); Write32(data, 0x28, 0x40400000); Write32(data, 0x2c, 0x40800000);
            var report = new BinaryAnalysisParser().Parse(data, "synthetic.gin");
            Assert.AreEqual("Gnsu GIN / EA-XAS v0", report.Variant); Assert.AreEqual(2, report.Tables[0].Entries.Count); Assert.AreEqual(2, report.Tables[1].Entries.Count);
            Assert.AreEqual(1, report.Recordings.Count); Assert.IsTrue(report.Recordings[0].Decoded); Assert.AreEqual(32, report.Recordings[0].Pcm.Length); Assert.AreEqual(0x30, report.Recordings[0].Payload.Offset);
        }

        [Test]
        public void GinRejectsTruncatedPayloadWithoutAllocatingUnboundedData()
        {
            var data = new byte[0x30 + 0x13]; WriteAscii(data, 0, "Octn"); Write32(data, 0x18, 64); Write32(data, 0x1c, 22050);
            var report = BinaryAnalysisParser.ParseBytes(data, "short.gin");
            Assert.IsTrue(report.IsPartial); Assert.IsTrue(report.Gaps.Exists(g => g.Code == "payload-truncated" || g.Code == "decode-truncated")); Assert.AreEqual(1, report.Recordings.Count); Assert.AreEqual(32, report.Recordings[0].ValidFrames);
        }

        [Test]
        public void UnknownInputRetainsHashAndGap()
        {
            var report = BinaryAnalysisParser.ParseBytes(new byte[] { 1, 2, 3, 4 }, "unknown.bin");
            Assert.AreEqual(64, report.Source.Sha256.Length); Assert.IsTrue(report.IsPartial); Assert.AreEqual("unknown-signature", report.Gaps[0].Code);
        }

        [Test]
        public void AbkcOutOfRangeModulePointerIsPartialAndBounded()
        {
            var data = new byte[0x24]; WriteAscii(data, 0, "ABKC"); Write16(data, 0x0a, 1); Write32(data, 0x1c, 0x10000000); Write32(data, 0x20, 0x20000000);
            var report = BinaryAnalysisParser.ParseBytes(data, "malformed.abk");
            Assert.IsTrue(report.IsPartial); Assert.IsTrue(report.Gaps.Exists(g => g.Code == "module-table-invalid")); Assert.AreEqual(0, report.Recordings.Count);
        }

        private static void WriteAscii(byte[] b, int p, string s) { for (int i = 0; i < s.Length; i++) b[p + i] = (byte)s[i]; }
        private static void Write32(byte[] b, int p, uint n) { b[p] = (byte)n; b[p + 1] = (byte)(n >> 8); b[p + 2] = (byte)(n >> 16); b[p + 3] = (byte)(n >> 24); }
        private static void Write16(byte[] b, int p, ushort n) { b[p] = (byte)n; b[p + 1] = (byte)(n >> 8); }
    }
}
