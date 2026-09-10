using System;
using System.Linq;
using NfsMwRemaster.Driving.AudioAnalysis;
using NUnit.Framework;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class BlackBoxBnkTailTests
    {
        [TestCase(false, 1)]
        [TestCase(false, 15)]
        [TestCase(false, 16)]
        [TestCase(true, 1)]
        [TestCase(true, 15)]
        public void FinalFrameNeedsOnlyItsDeclaredSamples(bool pcm, int frames)
        {
            byte[] bank = Bank(pcm, frames);
            var report = new BinaryAnalysisParser().Parse(bank, "tail.bnk");
            Assert.IsEmpty(report.Gaps.Where(g => g.Code.StartsWith("bnk-", StringComparison.Ordinal)));
            var recording = report.Recordings.Single(); Assert.AreEqual(frames, recording.ValidFrames);
            Assert.True(recording.Pcm.All(sample => Math.Abs(sample - .125f) < .00001f));
            var truncated = new BinaryAnalysisParser().Parse(bank.Take(bank.Length - 1).ToArray(), "truncated.bnk");
            Assert.True(truncated.Gaps.Any(g => g.Code == "bnk-payload-truncated"));
            Assert.AreEqual(0, truncated.Recordings.Single().ValidFrames, "Missing samples must not be padded or read from the next recording.");
        }
        private static byte[] Bank(bool pcm, int frames)
        {
            var bytes = new byte[0x40 + (pcm ? 5 + frames * 2 : 1 + (frames + 1) / 2)];
            bytes[0] = (byte)'B'; bytes[1] = (byte)'N'; bytes[2] = (byte)'K'; bytes[3] = (byte)'l';
            bytes[4] = 5; bytes[6] = 2; bytes[0x18] = 4;
            byte[] header = { (byte)'P', (byte)'T', 0, 0, 0x85, 1, (byte)frames, 0x88, 1, 0x40, 0xff };
            Array.Copy(header, 0, bytes, 0x1c, header.Length);
            if (pcm) { bytes[0x40] = 0xee; for (int i = 0; i < frames; i++) bytes[0x45 + i * 2] = 0x10; }
            else for (int i = 0x41; i < bytes.Length; i++) bytes[i] = 0x11;
            return bytes;
        }
    }
}
