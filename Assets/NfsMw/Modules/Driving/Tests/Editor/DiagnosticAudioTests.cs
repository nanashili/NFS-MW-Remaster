using System;
using System.IO;
using System.Text;
using NfsMwRemaster.Driving.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class DiagnosticAudioTests
    {
        private static void AssertSignal(float[] pcm, float maximum)
        {
            double energy = 0; float peak = 0; bool invalid = false;
            foreach (float value in pcm)
            {
                invalid |= float.IsNaN(value) || float.IsInfinity(value);
                peak = Math.Max(peak, Math.Abs(value)); energy += value * value;
            }
            Assert.False(invalid); Assert.LessOrEqual(peak, maximum + .00001f);
            Assert.Greater(Math.Sqrt(energy / pcm.Length), .002, "Signal must not be silent.");
        }

        [TestCase(EngineLayerKind.Exhaust, 900)]
        [TestCase(EngineLayerKind.Intake, 3200)]
        [TestCase(EngineLayerKind.Mechanical, 7200)]
        public void EngineLayersAreDeterministicNonSilentAndLoadSensitive(EngineLayerKind kind, float rpm)
        {
            var on = DiagnosticAudioSynthesis.Engine(kind, rpm, true);
            var off = DiagnosticAudioSynthesis.Engine(kind, rpm, false);
            CollectionAssert.AreEqual(on, DiagnosticAudioSynthesis.Engine(kind, rpm, true));
            CollectionAssert.AreNotEqual(on, off);
            Assert.AreEqual(DiagnosticAudioSynthesis.SampleRate * 2, on.Length);
            AssertSignal(on, .22f); AssertSignal(off, .22f);
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void SirensHaveBoundedEnergyAndNoDiscontinuousLoopSplice(int variant)
        {
            var pcm = DiagnosticAudioSynthesis.Siren(variant);
            AssertSignal(pcm, .21f);
            float nearby = 0;
            for (int i = 1; i < 200; i++)
                nearby = Math.Max(nearby, Math.Max(Math.Abs(pcm[i] - pcm[i - 1]), Math.Abs(pcm[pcm.Length - i] - pcm[pcm.Length - i - 1])));
            Assert.LessOrEqual(Math.Abs(pcm[0] - pcm[pcm.Length - 1]), nearby + .001f);
        }

        [Test]
        public void MusicStemsHaveExactSharedBarLength()
        {
            for (int stem = 0; stem < 3; stem++)
            {
                var pcm = DiagnosticAudioSynthesis.Music(stem, 120, 4, 8);
                Assert.AreEqual(16 * DiagnosticAudioSynthesis.SampleRate, pcm.Length);
                AssertSignal(pcm, .16f);
            }
        }

        [Test]
        public void TransientsFadeToSilenceAndMaterialsAreDifferent()
        {
            var metal = DiagnosticAudioSynthesis.Surface(SensorySurface.Metal, "heavy");
            var wood = DiagnosticAudioSynthesis.Surface(SensorySurface.Wood, "heavy");
            Assert.AreEqual(0, metal[0]); Assert.AreEqual(0, metal[metal.Length - 1]);
            CollectionAssert.AreNotEqual(metal, wood); AssertSignal(metal, .34f);
        }

        [Test]
        public void WaveWriterEmitsLittleEndianMonoPcmAndLeavesStreamOpen()
        {
            using (var stream = new MemoryStream())
            {
                DiagnosticAudioSynthesis.WriteWave(stream, new[] { 0f, .5f, -.5f });
                var bytes = stream.ToArray();
                Assert.AreEqual(50, bytes.Length); Assert.AreEqual("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
                Assert.AreEqual(42, BitConverter.ToInt32(bytes, 4)); Assert.AreEqual("WAVEfmt ", Encoding.ASCII.GetString(bytes, 8, 8));
                Assert.AreEqual(1, BitConverter.ToInt16(bytes, 20)); Assert.AreEqual(1, BitConverter.ToInt16(bytes, 22));
                Assert.AreEqual(24000, BitConverter.ToInt32(bytes, 24)); Assert.AreEqual(16, BitConverter.ToInt16(bytes, 34));
                Assert.AreEqual("data", Encoding.ASCII.GetString(bytes, 36, 4)); Assert.AreEqual(6, BitConverter.ToInt32(bytes, 40));
                Assert.AreEqual(16384, BitConverter.ToInt16(bytes, 46)); Assert.AreEqual(-16384, BitConverter.ToInt16(bytes, 48));
                Assert.True(stream.CanWrite);
            }
        }

        [Test]
        public void InvalidSamplesAreRejectedBeforeAnyWaveBytesAreWritten()
        {
            using (var stream = new MemoryStream())
            {
                Assert.Throws<InvalidDataException>(() => DiagnosticAudioSynthesis.WriteWave(stream, new[] { float.NaN }));
                Assert.Throws<InvalidDataException>(() => DiagnosticAudioSynthesis.WriteWave(stream, new[] { 1.1f }));
                Assert.AreEqual(0, stream.Length);
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => DiagnosticAudioSynthesis.Engine(EngineLayerKind.Exhaust, float.NaN, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => DiagnosticAudioSynthesis.Music(0, 120, 4, 100));
        }

        [Test]
        public void InstalledPackIsMonoPreloadedPcmWithOriginalProvenance()
        {
            var ids = AssetDatabase.FindAssets("t:AudioClip", new[] { SensoryDiagnosticAudioBuilder.AudioFolder });
            Assert.GreaterOrEqual(ids.Length, 100, "Install the diagnostic sound pack before running content acceptance.");
            foreach (string id in ids)
            {
                string path = AssetDatabase.GUIDToAssetPath(id);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                Assert.AreEqual(1, clip.channels, path); Assert.AreEqual(24000, clip.frequency, path);
                Assert.True(importer.defaultSampleSettings.preloadAudioData, path);
                Assert.AreEqual(AudioClipLoadType.DecompressOnLoad, importer.defaultSampleSettings.loadType, path);
                Assert.AreEqual(AudioCompressionFormat.PCM, importer.defaultSampleSettings.compressionFormat, path);
                StringAssert.Contains("Original deterministic diagnostic", importer.userData, path);
                var pcm = new float[clip.samples]; Assert.True(clip.GetData(pcm, 0), path);
                double energy = 0; float peak = 0;
                foreach (float value in pcm) { energy += value * value; peak = Math.Max(peak, Math.Abs(value)); }
                Assert.Greater(Math.Sqrt(energy / pcm.Length), .002, path); Assert.LessOrEqual(peak, .341, path);
            }
        }

        [TestCase("SensoryTest")] [TestCase("DrivingDemo")] [TestCase("FreeRoam")] [TestCase("PursuitTest")]
        public void InstalledScenesReferenceTheSharedPopulatedProfiles(string scene)
        {
            string serialized = File.ReadAllText("Assets/NfsMw/Scenes/Tests/" + scene + ".unity");
            foreach (string name in new[] { "ReferenceVehicle", "PoliceFeedback", "AdaptiveMusic", "SensoryMix" })
            {
                string path = "Assets/NfsMw/Modules/Driving/Data/Sensory/" + name + ".asset";
                string guid = AssetDatabase.AssetPathToGUID(path);
                Assert.IsNotEmpty(guid); StringAssert.Contains(guid, serialized, scene + ": " + name);
            }
            var vehicle = AssetDatabase.LoadAssetAtPath<VehicleSensoryProfile>("Assets/NfsMw/Modules/Driving/Data/Sensory/ReferenceVehicle.asset");
            foreach (var layer in vehicle.engineLayers)
                foreach (var region in layer.regions) { Assert.NotNull(region.onLoad); Assert.NotNull(region.offLoad); }
            var music = AssetDatabase.LoadAssetAtPath<SensoryMusicProfile>("Assets/NfsMw/Modules/Driving/Data/Sensory/AdaptiveMusic.asset");
            Assert.True(music.Validate(out string failure), failure);
            foreach (var stem in music.stems) Assert.NotNull(stem.clip);
        }
    }
}
