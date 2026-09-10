using System;
using System.IO;
using System.Text;
using System.Threading;
using NfsMwRemaster.Driving.AudioAnalysis;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    [Serializable]
    public sealed class BlackBoxWaveManifest
    {
        public int schema = 1, channels, sampleRate, startFrame, endFrameExclusive;
        public string sourceHash, sourcePath, recordingId, decoderVersion, outputHash;
        public string processing = "IEEE float32 PCM; original rate/channels; no normalization, trimming, resampling or denoising";
        public string interval = "Half-open source sample frames; channel scalars are interleaved within each frame";
    }

    public static class BlackBoxWaveExport
    {
        // Preserve float bits rather than quantizing source WAVs or silently clipping evidence above unity.
        public static void Write(Stream stream, float[] pcm, int rate, int channels, int startFrame, int endFrame, CancellationToken cancel = default)
        {
            if (pcm == null || rate < 1 || channels < 1 || channels > 8 || pcm.Length % channels != 0 ||
                startFrame < 0 || endFrame <= startFrame || endFrame > pcm.Length / channels)
                throw new InvalidDataException("Missing PCM or invalid half-open frame interval.");
            int scalars = checked((endFrame - startFrame) * channels), dataBytes = checked(scalars * 4);
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(checked(48 + dataBytes));
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((ushort)3); writer.Write((ushort)channels); writer.Write(rate);
                writer.Write(checked(rate * channels * 4)); writer.Write((ushort)(channels * 4)); writer.Write((ushort)32);
                writer.Write(Encoding.ASCII.GetBytes("fact")); writer.Write(4); writer.Write(endFrame - startFrame);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(dataBytes);
                for (int i = checked(startFrame * channels); i < checked(endFrame * channels); i++)
                {
                    if ((i & 8191) == 0) cancel.ThrowIfCancellationRequested();
                    if (float.IsNaN(pcm[i]) || float.IsInfinity(pcm[i])) throw new InvalidDataException("Non-finite PCM is not exportable as audio.");
                    writer.Write(pcm[i]);
                }
            }
        }

        public static BlackBoxWaveManifest ExportNew(string path, BlackBoxDocument document, Recording recording,
            int startFrame, int endFrame, CancellationToken cancellation = default)
        {
            if (recording == null || !recording.Decoded || recording.Pcm == null || document.IsStale)
                throw new InvalidDataException("Decoded current source evidence is required.");
            if (File.Exists(path) || File.Exists(path + ".json")) throw new IOException("Export target exists. Choose a new path to preserve prior evidence.");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            bool moved = false;
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                    Write(stream, recording.Pcm, recording.SampleRate, recording.Channels, startFrame, endFrame, cancellation);
                var manifest = new BlackBoxWaveManifest { sourceHash = document.Report.Source.Sha256, sourcePath = document.Report.Source.SourcePath,
                    recordingId = recording.Id, sampleRate = recording.SampleRate, channels = recording.Channels,
                    startFrame = startFrame, endFrameExclusive = endFrame, decoderVersion = document.Report.ParserVersion,
                    outputHash = BlackBoxSourceAccess.Hash(File.ReadAllBytes(temporary)) };
                File.WriteAllText(temporary + ".json", JsonUtility.ToJson(manifest, true));
                cancellation.ThrowIfCancellationRequested(); File.Move(temporary, path); moved = true;
                File.Move(temporary + ".json", path + ".json");
                return manifest;
            }
            catch { if (moved && File.Exists(path)) File.Delete(path); throw; }
            finally { if (File.Exists(temporary)) File.Delete(temporary); if (File.Exists(temporary + ".json")) File.Delete(temporary + ".json"); }
        }
    }
}
